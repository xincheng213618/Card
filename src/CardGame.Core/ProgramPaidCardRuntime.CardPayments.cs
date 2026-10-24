namespace CardGame.Core;

internal sealed partial class ProgramPaidCardRuntime
{
    internal int ResolveProgramParticipant(ProgramSkillFrame frame, ProgramParticipantReference reference)
    {
        return reference.Kind switch
        {
            ProgramParticipantRef.Owner => frame.OwnerSeat,
            ProgramParticipantRef.Actor when frame.WindowContext?.CardUse is { } context => context.ActorSeat,
            ProgramParticipantRef.CardProvider => _facts.ProviderSeat(frame),
            ProgramParticipantRef.EventTarget when frame.WindowContext?.TargetSeat is { } seat => seat,
            ProgramParticipantRef.EventSource when frame.WindowContext?.SourceSeat is { } seat => seat,
            ProgramParticipantRef.SelectedTarget when frame.SelectedTargetSeats is [var seat] => seat,
            ProgramParticipantRef.SelectedFirst when frame.SelectedTargetSeats.Count >= 1 => frame.SelectedTargetSeats[0],
            ProgramParticipantRef.SelectedSecond when frame.SelectedTargetSeats.Count >= 2 => frame.SelectedTargetSeats[1],
            ProgramParticipantRef.ResultSource => frame.PindianResultBindings
                .Single(item => item.Name == reference.ResultBind).SourceSeat,
            ProgramParticipantRef.ResultOpponent => frame.PindianResultBindings
                .Single(item => item.Name == reference.ResultBind).OpponentSeat,
            _ => throw new InvalidOperationException("The requested program participant is unavailable.")
        };
    }

    internal SkillProgramStepOutcome SelectAndMoveProgramOwnedCard(
        ProgramSkillFrame frame,
        ProgramParticipantReference chooser,
        ProgramParticipantReference cardOwner,
        IReadOnlyList<CardZoneKind> zones,
        SkillProgramCardDestination destination,
        ProgramParticipantReference? destinationRef,
        string? resultBind,
        CardMoveReason reason,
        IReadOnlyList<SkillProgramCardCategory>? cardCategories = null,
        bool skipIfNoCards = false,
        bool allowSameOwnerHandReturn = false,
        string? coverageResultBind = null,
        bool awaitMovementTriggers = false,
        bool revealBeforeMove = false,
        IReadOnlyList<CardKind>? cardKinds = null,
        bool prohibitReplacingEquipment = false,
        IReadOnlyList<Suit>? suits = null)
    {
        var active = _frames.ActiveProgram(frame.Id);
        var chooserSeat = ResolveProgramParticipant(active, chooser);
        var cardOwnerSeat = ResolveProgramParticipant(active, cardOwner);
        var destinationSeat = destinationRef is null ? (int?)null : ResolveProgramParticipant(active, destinationRef);
        if (destination == SkillProgramCardDestination.SelectedTargetHand &&
            (destinationSeat is not { } seat || !_facts.IsAlive(seat) ||
             seat == cardOwnerSeat && !allowSameOwnerHandReturn))
            throw new InvalidOperationException("A selected-target transfer requires a distinct living recipient.");
        if (destination == SkillProgramCardDestination.SelectedTargetEquipment &&
            (destinationSeat is not { } recipientSeat || !_facts.IsAlive(recipientSeat) ||
             recipientSeat == cardOwnerSeat))
            throw new InvalidOperationException("An equipment gift requires a distinct living recipient.");
        if (prohibitReplacingEquipment &&
            (destination != SkillProgramCardDestination.SelectedTargetCorrespondingZone ||
             destinationSeat is not { } recipient || !_facts.IsAlive(recipient)))
            throw new InvalidOperationException("A no-replace corresponding-zone transfer requires a living selected target.");
        var choices = BuildOwnedCardPaymentChoices(active.Id, chooserSeat, cardOwnerSeat, zones, destination == SkillProgramCardDestination.DiscardPile ? OwnedCardMoveIntent.Discard : OwnedCardMoveIntent.Transfer,
            cardCategories, cardKinds, suits,
            destination == SkillProgramCardDestination.SelectedTargetCorrespondingZone
                ? (zone, card) => destinationSeat is { } seat &&
                    CanMoveProgramCardToCorrespondingZone(card, zone, seat)
                : null,
            destination, destinationSeat, prohibitReplacingEquipment,
            correspondingZoneSeat: prohibitReplacingEquipment && zones.Contains(CardZoneKind.Equipment)
                ? destinationSeat : null);
        if (choices.Count == 0)
        {
            if (skipIfNoCards)
            {
                if (_facts.OwnedSelectionEffect(active).FreezeMovedCardSuit && resultBind is not null)
                    _operations.SetCardSet(frame.Id, resultBind, [], SkillProgramCardSetVisibility.Public, []);
                return SkillProgramStepOutcome.Continue;
            }
            _operations.Cancel(active, "没有可支付的区域牌，技能结算已取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        if (_facts.OwnedSelectionEffect(active).AllowDecline)
            choices = Array.AsReadOnly(choices.Append(new PromptChoice(
                new ChoiceId($"program-field-decline.frame-{active.Id}"), "不移动场上的牌。", [], [],
                new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>
                { ["program-action"] = "decline-owned-field-move", ["frame-id"] = active.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) }))).ToArray());
        var visibleCardIds = choices.SelectMany(choice => choice.Cards).Distinct().Order().ToArray();
        var skill = _facts.Skill(frame.SkillId);
        _prompts.Set(new PendingDecision(
            DecisionKind.ProgramTrigger, chooserSeat,
            destination == SkillProgramCardDestination.SelectedTargetHand
                ? "请选择一张区域牌交给目标。" : "请选择一张区域牌支付。", visibleCardIds, [],
            chooserSeat)
        {
            PromptId = _prompts.NextId(),
            IsPrivate = true,
            TargetSeat = frame.WindowContext?.TargetSeat ?? cardOwnerSeat,
            SkillPrompt = new SkillPromptPresentation(frame.SkillId, skill.Name,
                destination == SkillProgramCardDestination.SelectedTargetHand
                    ? $"{skill.Name} · 选择转交牌" : $"{skill.Name} · 选择支付牌", skill.Description),
            Choices = choices
        }, _facts.IsHuman(chooserSeat));
        return SkillProgramStepOutcome.AwaitChoice;
    }

    internal bool HasOwnedProgramCardCategory(int ownerSeat,
        IReadOnlyList<CardZoneKind> zones, IReadOnlyList<SkillProgramCardCategory> categories,
        IReadOnlyList<CardKind> kinds) =>
        zones.Any(zone => _facts.CardsAt(new CardLocation(zone, ownerSeat))
            .Any(card => MatchesProgramCardFilter(card.Kind, categories, kinds)));

    internal IReadOnlyList<PromptChoice> BuildOwnedCardPaymentChoices(
        long frameId, int chooserSeat, int cardOwnerSeat, IReadOnlyList<CardZoneKind> zones, OwnedCardMoveIntent intent,
        IReadOnlyList<SkillProgramCardCategory>? cardCategories = null,
        IReadOnlyList<CardKind>? cardKinds = null,
        IReadOnlyList<Suit>? suits = null,
        Func<CardZoneKind, Card, bool>? canSelect = null,
        SkillProgramCardDestination destination = SkillProgramCardDestination.DiscardPile,
        int? destinationSeat = null,
        bool prohibitReplacingEquipment = false, int? correspondingZoneSeat = null)
    {
        var frame = _frames.ActiveProgram(frameId);
        var result = new List<PromptChoice>();
        foreach (var zone in zones)
        {
            var cards = zone switch
            {
                CardZoneKind.Hand => _facts.Hand(cardOwnerSeat),
                CardZoneKind.Equipment => _facts.Equipment(cardOwnerSeat),
                CardZoneKind.Judgment => _facts.Judgment(cardOwnerSeat),
                _ => throw new InvalidOperationException("Unsupported payment card zone.")
            };
            for (var slot = 0; slot < cards.Count; slot++)
            {
                if (_facts.ForeignDiscardPrevented(chooserSeat, cards[slot],
                        new CardLocation(zone, cardOwnerSeat), intent)) continue;
                if (cardCategories is { Count: > 0 } && !MatchesProgramCardCategory(cards[slot].Kind, cardCategories))
                    continue;
                if (cardKinds is { Count: > 0 } && !ProgramCardKindMatch.Contains(cardKinds, cards[slot].Kind))
                    continue;
                if (suits is { Count: > 0 } && !suits.Contains(cards[slot].Suit))
                    continue;
                if (canSelect is not null && !canSelect(zone, cards[slot]))
                    continue;
                if (destination == SkillProgramCardDestination.SelectedTargetEquipment &&
                    (destinationSeat is not { } recipient ||
                        !CanEnterEquipmentSlot(recipient, cards[slot])))
                    continue;
                if (zone == CardZoneKind.Equipment && cardOwnerSeat == frame.OwnerSeat &&
                    _facts.IsActiveSourceEquipment(cardOwnerSeat, frame.SkillId,
                        frame.SkillInstanceId, cards[slot]))
                    continue;
                if (prohibitReplacingEquipment && correspondingZoneSeat is { } recipientSeat &&
                    EquipmentCatalog.IsEquipment(cards[slot].Kind) &&
                    _facts.Equipment(recipientSeat).Any(item =>
                        _facts.FieldEquipmentSlot(item) == _facts.FieldEquipmentSlot(cards[slot])))
                    continue;
                var hidden = zone == CardZoneKind.Hand && chooserSeat != cardOwnerSeat;
                var parameters = new Dictionary<string, string>
                {
                    ["program-action"] = "select-and-move-owned-card",
                    ["frame-id"] = frameId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["card-owner-seat"] = cardOwnerSeat.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["source-zone"] = zone.ToString(),
                    ["slot-index"] = slot.ToString(System.Globalization.CultureInfo.InvariantCulture)
                };
                result.Add(new PromptChoice(
                    new ChoiceId($"program-owned-card.frame-{frameId}.zone-{zone}.slot-{slot}"),
                    hidden ? $"选择第 {slot + 1} 张手牌" : $"选择【{_facts.Snapshot(cards[slot]).DisplayName}】",
                    hidden ? [] : [cards[slot].Id], [], parameters));
            }
        }
        return Array.AsReadOnly(result.ToArray());
    }

    internal void ResolveSelectAndMoveOwnedCardChoice(
        ProgramSkillFrame frame, SkillProgramEffect effect, PromptChoice selected)
    {
        var chooserSeat = ResolveProgramParticipant(frame, effect.ChooserRef!);
        var ownerSeat = ResolveProgramParticipant(frame, effect.CardOwnerRef!);
        if (!_facts.IsAlive(frame.OwnerSeat) || !_facts.IsAlive(chooserSeat) || !_facts.IsAlive(ownerSeat) ||
            !_facts.HasSkillInstance(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId))
        {
            _prompts.Clear();
            _operations.Cancel(frame, "支付参与者或技能实例已失效，技能剩余步骤取消。");
            return;
        }
        if (selected.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("card-owner-seat") != ownerSeat.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            !Enum.TryParse<CardZoneKind>(selected.Parameters.GetValueOrDefault("source-zone"), out var zone) ||
            !effect.Zones.Contains(zone) ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("slot-index"), out var slot))
            throw new InvalidOperationException("The selected payment no longer matches its suspended instruction.");
        var cards = zone switch
        {
            CardZoneKind.Hand => _facts.Hand(ownerSeat),
            CardZoneKind.Equipment => _facts.Equipment(ownerSeat),
            CardZoneKind.Judgment => _facts.Judgment(ownerSeat),
            _ => throw new InvalidOperationException("Unsupported payment card zone.")
        };
        if (slot < 0 || slot >= cards.Count)
        {
            _prompts.Clear();
            _operations.Cancel(frame, "公布的支付牌位已失效，技能结算已取消。");
            return;
        }
        var card = cards[slot];
        if (effect.Destination == SkillProgramCardDestination.DiscardPile &&
            _facts.ForeignDiscardPrevented(chooserSeat, card, new CardLocation(zone, ownerSeat), OwnedCardMoveIntent.Discard))
        {
            _prompts.Clear();
            _operations.Cancel(frame, "目标装备已不能被他人弃置，支付结算已取消。");
            return;
        }
        if (zone == CardZoneKind.Equipment && ownerSeat == frame.OwnerSeat &&
            _facts.IsActiveSourceEquipment(ownerSeat, frame.SkillId, frame.SkillInstanceId, card))
        {
            _prompts.Clear();
            _operations.Cancel(frame, "提供当前技能的装备牌不能用于支付。");
            return;
        }
        if (effect.CardCategories.Count > 0 &&
            !MatchesProgramCardCategory(card.Kind, effect.CardCategories))
        {
            _prompts.Clear();
            _operations.Cancel(frame, "公布的支付牌类别已失效，技能结算已取消。");
            return;
        }
        if (effect.CardKinds.Count > 0 && !ProgramCardKindMatch.Contains(effect.CardKinds, card.Kind))
        {
            _prompts.Clear();
            _operations.Cancel(frame, "公布的支付牌种类已失效，技能结算已取消。");
            return;
        }
        var destinationSeat = effect.Destination == SkillProgramCardDestination.SelectedTargetCorrespondingZone
            ? ResolveProgramParticipant(frame, effect.TargetReference!) : (int?)null;
        if (destinationSeat is { } correspondingSeat &&
            !CanMoveProgramCardToCorrespondingZone(card, zone, correspondingSeat))
        {
            _prompts.Clear();
            _operations.Cancel(frame, "对应区域已无法接收所选牌，技能剩余结算已取消。");
            return;
        }
        if (!(zone == CardZoneKind.Hand && chooserSeat != ownerSeat) &&
            (selected.Cards.Count != 1 || selected.Cards[0] != card.Id))
            throw new InvalidOperationException("The visible payment card identity changed.");
        var source = zone switch
        {
            CardZoneKind.Hand => CardLocation.Hand(ownerSeat),
            CardZoneKind.Equipment => CardLocation.Equipment(ownerSeat),
            CardZoneKind.Judgment => CardLocation.Judgment(ownerSeat),
            _ => throw new InvalidOperationException()
        };
        var destination = effect.Destination switch
        {
            SkillProgramCardDestination.OwnerHand => CardLocation.Hand(frame.OwnerSeat),
            SkillProgramCardDestination.SelectedTargetHand => CardLocation.Hand(
                ResolveProgramParticipant(frame, effect.TargetReference!)),
            SkillProgramCardDestination.SelectedTargetEquipment => CardLocation.Equipment(
                ResolveProgramParticipant(frame, effect.TargetReference!)),
            SkillProgramCardDestination.SelectedTargetCorrespondingZone => ProgramCorrespondingZoneLocation(
                card, zone, destinationSeat!.Value),
            SkillProgramCardDestination.DiscardPile => CardLocation.DiscardPile,
            SkillProgramCardDestination.DrawPileTop => CardLocation.DrawPile,
            _ => throw new InvalidOperationException("Unsupported selected-card destination.")
        };
        if (destination.OwnerSeat is { } recipientSeat && !_facts.IsAlive(recipientSeat))
        {
            _prompts.Clear();
            _operations.Cancel(frame, "受牌角色已失效，技能结算已取消。");
            return;
        }
        if (effect.Destination == SkillProgramCardDestination.SelectedTargetEquipment)
        {
            var recipient = destination.OwnerSeat!.Value;
            var slotAvailable = !_facts.EquipmentAreaAbolished(recipient) && CanEnterEquipmentSlot(recipient, card);
            if (!slotAvailable)
            {
                // The target's corresponding slot was filled (or the area was
                // abolished) between selection and resolution: the gift cancels.
                _prompts.Clear();
                _operations.Cancel(frame, "目标装备槽不可用，技能结算已取消。");
                return;
            }
        }
        var selfProviderReward = effect.AllowSameOwnerHandReturn &&
            effect.ChooserRef?.Kind == ProgramParticipantRef.CardProvider &&
            effect.CardOwnerRef?.Kind == ProgramParticipantRef.Owner &&
            effect.TargetReference?.Kind == ProgramParticipantRef.CardProvider && effect.AwaitMovementTriggers;
        if (destination == source && source == CardLocation.Hand(ownerSeat) &&
            (selfProviderReward || _facts.IsSatisfiedOwnHandReward(frame, effect)))
        {
            // Obtaining one's own hand card is already satisfied. Do not manufacture
            // a hand loss/gain pair or unrelated movement-trigger rewards.
            _prompts.Clear();
            if (effect.ResultBind is { } sameHandBind)
                _operations.SetCardSet(frame.Id, sameHandBind, [card.Id], SkillProgramCardSetVisibility.Private, [source]);
            _frames.AdvanceProgram(frame.Id);
            return;
        }
        if (destination == CardLocation.Hand(ownerSeat) && source == CardLocation.Hand(ownerSeat) && !selfProviderReward)
            throw new InvalidOperationException("A same-hand card movement is not a payment.");
        if (destination == CardLocation.Hand(ownerSeat) &&
            effect.Destination == SkillProgramCardDestination.SelectedTargetHand &&
            (!effect.AllowSameOwnerHandReturn || zone is not (CardZoneKind.Equipment or CardZoneKind.Judgment) && !selfProviderReward))
            throw new InvalidOperationException("A same-owner hand return requires an authorized public source zone.");
        if (effect.CoverageResultBind is not null && zone != CardZoneKind.Equipment)
            throw new InvalidOperationException("Attack-range coverage requires a public equipment movement.");
        if (effect.Destination == SkillProgramCardDestination.SelectedTargetCorrespondingZone &&
            destination.Zone == CardZoneKind.Equipment)
        {
            var recipient = destination.OwnerSeat!.Value;
            var replacedSlot = _facts.FieldEquipmentSlot(card);
            var replaced = _facts.Equipment(recipient)
                .SingleOrDefault(item => _facts.FieldEquipmentSlot(item) == replacedSlot);
            if (replaced is not null && replaced.Id != card.Id)
            {
                if (effect.ProhibitReplacingEquipment)
                {
                    _prompts.Clear();
                    _operations.Cancel(frame, "目标装备栏已有牌，不能替换原有装备，技能结算已取消。");
                    return;
                }
                _operations.Move(replaced, CardLocation.Equipment(recipient), CardLocation.DiscardPile,
                    CardMoveReasons.EquipmentReplace);
            }
        }
        _prompts.Clear();
        var frozenMovedSuit = effect.FreezeMovedCardSuit ? _facts.EffectiveSuit(ownerSeat, card) : (Suit?)null;
        var beforeCoverage = effect.CoverageResultBind is null ? 0 : _facts.CountLivingInAttackRange(ownerSeat);
        if (effect.AwaitMovementTriggers)
        {
            _frames.ReplaceTop(frame with
            {
                PendingMovementContinuation = new ProgramMovementContinuation(
                    ownerSeat, beforeCoverage, effect.CoverageResultBind)
            });
        }
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}");
        var discardInvocation = destination == CardLocation.DiscardPile
            ? _operations.CaptureLogicalDiscard(frame.OwnerSeat, chooserSeat, [card.Id], reason, frame.Id) : null;
        if (effect.RevealBeforeMove)
        {
            _frames.ReplaceTop(_frames.ActiveProgram(frame.Id) with { PendingMovementContinuation = null, PublicHandRevealSelection = new("selected-card-pre-move",frame.InstructionIndex,0,
                card.Id,card.Kind,source,destination,ownerSeat,chooserSeat,effect.ResultBind,frozenMovedSuit,beforeCoverage,discardInvocation) });
            _events.AdvanceFact(new ProgramCardsRevealedEvent(frame.Id, frame.SkillId,
                RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(frame), frame.OwnerSeat, effect.ResultBind!,
                Array.AsReadOnly(new[] { _facts.Snapshot(card) })));
            if(_operations.BeginPublicHandReveal(frame.Id,"selected-card-pre-move"))return;
            ContinueSelectedCardAfterPublicReveal(_frames.ActiveProgram(frame.Id));return;
        }
        if (TryCompleteRetainedSelectedCard(frame, effect, card, source, destination, reason,
            ownerSeat, beforeCoverage, frozenMovedSuit)) return;
        var disclosure = effect.RevealBeforeMove || destination.Zone is CardZoneKind.DiscardPile or CardZoneKind.Equipment or CardZoneKind.Judgment
            ? CardMovementDisclosure.FaceUp : CardMovementDisclosure.FaceDown;
        if (destination.Zone == CardZoneKind.Hand && NativeGiveSemantics.Selected(frame, effect))
            _operations.Give(frame, ownerSeat, card, source, destination, reason, effect.Destination == SkillProgramCardDestination.SelectedTargetHand, disclosure);
        else if (effect.Destination == SkillProgramCardDestination.SelectedTargetHand)
        {
            _operations.Move(card, source, CardLocation.Processing, reason, disclosure: disclosure);
            _operations.Move(card, CardLocation.Processing, destination, reason, disclosure: disclosure);
        }
        else _operations.Move(card, source, destination, reason, disclosure: disclosure);
        _operations.CompleteLogicalDiscard(discardInvocation);
        if (effect.Destination == SkillProgramCardDestination.SelectedTargetEquipment)
        {
            // Mirror CompleteEquipmentUse's public entry trio (move + event + log);
            // a gifted card never replaces anything, so there is no replaced card.
            _events.AdvanceFact(new EquipmentChangedEvent(frame.Id, destination.OwnerSeat!.Value,
                EquipmentCatalog.Get(card.Kind).Slot, card.Id, card.Kind, ReplacedCardId: null));
            _operations.Log("EquipmentChanged",
                $"{_facts.Name(frame.OwnerSeat)} 将【{EquipmentCatalog.Get(card.Kind).DisplayName}】置于 " +
                $"{_facts.Name(destination.OwnerSeat.Value)} 的装备区。",
                destination.OwnerSeat.Value);
        }
        if (effect.ResultBind is { } bind)
            _operations.SetCardSet(frame.Id, bind, [card.Id],
                effect.RevealBeforeMove || effect.SkipIfNoCards && effect.FreezeMovedCardSuit && effect.Destination == SkillProgramCardDestination.DiscardPile && effect.CardOwnerRef?.Kind == ProgramParticipantRef.SelectedTarget
                    ? SkillProgramCardSetVisibility.Public : SkillProgramCardSetVisibility.Private,
                [_facts.Location(card.Id)], frozenMovedSuit ?? (effect.RevealBeforeMove ? card.Suit : null));
        if (effect.AwaitMovementTriggers)
        {
            if (!_operations.BeginMovementWindow())
                _operations.ReturnMovement(frame.Id);
            return;
        }
        if (effect.CoverageResultBind is { } coverageBind)
            SetProgramAttackRangeCoverage(frame.Id, coverageBind, ownerSeat, beforeCoverage,
                _facts.CountLivingInAttackRange(ownerSeat));
        _frames.AdvanceProgram(frame.Id);
    }

    internal bool CanMoveProgramCardToCorrespondingZone(Card card, CardZoneKind sourceZone,
        int destinationSeat)
    {
        if (!_facts.IsValidSeat(destinationSeat) || !_facts.IsAlive(destinationSeat))
            return false;
        var recipient = destinationSeat;
        if (sourceZone == CardZoneKind.Judgment && _facts.IsZixiJudgment(card.Id))
            return _facts.CanTransferZixiJudgment(card, destinationSeat);
        if (sourceZone == CardZoneKind.Judgment)
            return !_facts.JudgmentAreaAbolished(recipient) && !_facts.HasCardPolicy(recipient, SkillProgramCardPolicyKind.ProhibitDelayedTrickTarget) &&
                _facts.IsDelayedCard(_facts.JudgmentKind(card)) &&
                !_facts.HasJudgmentEffectiveCard(recipient, _facts.JudgmentKind(card));
        return sourceZone is CardZoneKind.Hand or CardZoneKind.Equipment &&
            EquipmentCatalog.IsEquipment(card.Kind) && !_facts.EquipmentAreaAbolished(recipient);
    }

    internal CardLocation ProgramCorrespondingZoneLocation(Card card, CardZoneKind sourceZone,
        int destinationSeat)
    {
        if (!CanMoveProgramCardToCorrespondingZone(card, sourceZone, destinationSeat))
            throw new InvalidOperationException("The corresponding-zone recipient cannot receive this card.");
        if (sourceZone == CardZoneKind.Judgment) return CardLocation.Judgment(destinationSeat);
        if (sourceZone is CardZoneKind.Hand or CardZoneKind.Equipment)
            return CardLocation.Equipment(destinationSeat);
        throw new InvalidOperationException(
            $"Card '{card.Kind}' has no corresponding zone; only delayed tricks and equipment are transferable.");
    }

    internal void SetProgramAttackRangeCoverage(long frameId, string name, int subjectSeat,
        int beforeCount, int afterCount)
    {
        var frame = _frames.ActiveProgram(frameId);
        if (frame.AttackRangeCoverageBindings.Any(item => item.Name == name))
            throw new InvalidOperationException("The attack-range coverage result was already produced.");
        _frames.ReplaceTop(frame with
        {
            AttackRangeCoverageBindings = Array.AsReadOnly(frame.AttackRangeCoverageBindings
                .Append(new ProgramAttackRangeCoverageBinding(name, subjectSeat, beforeCount, afterCount)).ToArray())
        });
    }

    internal static bool IsProgramAttackRangeCoverageDecreased(ProgramSkillFrame frame, string bind) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramCardPayments.IsProgramAttackRangeCoverageDecreased(frame,bind);

    internal static bool MatchesProgramCardCategory(
        CardKind kind,
        IReadOnlyList<SkillProgramCardCategory> categories) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramCardPayments.MatchesProgramCardCategory(kind,categories);

    internal static bool MatchesSkillProgramCardCategory(CardKind kind, SkillProgramCardCategory category) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramCardPayments.MatchesSkillProgramCardCategory(kind,category);

    internal static bool MatchesProgramCardFilter(
        CardKind kind,
        IReadOnlyList<SkillProgramCardCategory> categories,
        IReadOnlyList<CardKind> kinds) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramCardPayments.MatchesProgramCardFilter(kind,categories,kinds);

    internal bool CanEnterEquipmentSlot(int recipientSeat, Card card) =>
        EquipmentCatalog.IsEquipment(card.Kind) &&
        _facts.Equipment(recipientSeat).Count(equipped =>
            _facts.FieldEquipmentSlot(equipped) == _facts.FieldEquipmentSlot(card)) <
        _facts.EquipmentSlotCapacity(recipientSeat, EquipmentCatalog.Get(card.Kind).Slot);

    internal static SkillProgramCardCategory GetProgramCardCategory(CardKind kind) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramCardPayments.GetProgramCardCategory(kind);

    private bool TryCompleteRetainedSelectedCard(ProgramSkillFrame frame, SkillProgramEffect effect,
        Card card, CardLocation source, CardLocation destination, CardMoveReason reason,
        int ownerSeat, int beforeCoverage, Suit? frozenMovedSuit)
    {
        if (destination.Zone != CardZoneKind.Hand ||
            !_operations.SettleRetainedGain(frame, card, source, destination, reason)) return false;
        var active = _frames.ActiveProgram(frame.Id);
        _frames.ReplaceTop(active with { PendingMovementContinuation = null });
        if (effect.ResultBind is { } bind)
            _operations.SetCardSet(frame.Id, bind, [card.Id], effect.RevealBeforeMove
                ? SkillProgramCardSetVisibility.Public : SkillProgramCardSetVisibility.Private,
                [source], frozenMovedSuit ?? (effect.RevealBeforeMove ? card.Suit : null));
        if (effect.CoverageResultBind is { } coverageBind)
            SetProgramAttackRangeCoverage(frame.Id, coverageBind, ownerSeat, beforeCoverage,
                _facts.CountLivingInAttackRange(ownerSeat));
        _frames.AdvanceProgram(frame.Id);
        return true;
    }

    [PublicHandRevealReturn("selected-card-pre-move")]
    private void ContinueSelectedCardAfterPublicReveal(ProgramSkillFrame frame)
    {
        var r=frame.PublicHandRevealSelection??throw new InvalidOperationException("Selected payment lost its original real entity.");
        if(r.ReturnKey!="selected-card-pre-move"||r.InstructionIndex!=frame.InstructionIndex)throw new InvalidOperationException("Selected payment changed its accepted instruction.");
        var effect=_facts.OwnedSelectionEffect(frame);var source=r.From;var destination=r.To;var ownerSeat=r.ActorSeat;var chooserSeat=r.ChooserSeat;var beforeCoverage=r.BeforeCoverage;var frozenMovedSuit=r.FrozenSuit;var discardInvocation=r.DiscardInvocation;
        _frames.ReplaceTop(frame=frame with{PublicHandRevealSelection=null});
        var card=_facts.CardsAt(source).SingleOrDefault(c=>c.Id==r.CardId&&c.Kind==r.PrintedKind);
        if(card is null||destination.OwnerSeat is{}recipient&&!_facts.IsAlive(recipient)){_operations.Cancel(frame,"展示后原支付实体或受牌人已失效。");return;}
        var reason=new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}");
        if (TryCompleteRetainedSelectedCard(frame, effect, card, source, destination, reason,
            ownerSeat, beforeCoverage, frozenMovedSuit)) return;
        if(effect.AwaitMovementTriggers)_frames.ReplaceTop(frame=frame with{PendingMovementContinuation=new(ownerSeat,beforeCoverage,effect.CoverageResultBind)});
        var disclosure = effect.RevealBeforeMove || destination.Zone is CardZoneKind.DiscardPile or CardZoneKind.Equipment or CardZoneKind.Judgment
            ? CardMovementDisclosure.FaceUp : CardMovementDisclosure.FaceDown;
        if (destination.Zone == CardZoneKind.Hand && NativeGiveSemantics.Selected(frame, effect))
            _operations.Give(frame, ownerSeat, card, source, destination, reason, effect.Destination == SkillProgramCardDestination.SelectedTargetHand, disclosure);
        else if (effect.Destination == SkillProgramCardDestination.SelectedTargetHand)
        {
            _operations.Move(card, source, CardLocation.Processing, reason, disclosure: disclosure);
            _operations.Move(card, CardLocation.Processing, destination, reason, disclosure: disclosure);
        }
        else _operations.Move(card, source, destination, reason, disclosure: disclosure);
        _operations.CompleteLogicalDiscard(discardInvocation);
        if (effect.Destination == SkillProgramCardDestination.SelectedTargetEquipment)
        {
            // Mirror CompleteEquipmentUse's public entry trio (move + event + log);
            // a gifted card never replaces anything, so there is no replaced card.
            _events.AdvanceFact(new EquipmentChangedEvent(frame.Id, destination.OwnerSeat!.Value,
                EquipmentCatalog.Get(card.Kind).Slot, card.Id, card.Kind, ReplacedCardId: null));
            _operations.Log("EquipmentChanged",
                $"{_facts.Name(frame.OwnerSeat)} 将【{EquipmentCatalog.Get(card.Kind).DisplayName}】置于 " +
                $"{_facts.Name(destination.OwnerSeat.Value)} 的装备区。",
                destination.OwnerSeat.Value);
        }
        if (effect.ResultBind is { } bind)
            _operations.SetCardSet(frame.Id, bind, [card.Id],
                effect.RevealBeforeMove || effect.SkipIfNoCards && effect.FreezeMovedCardSuit && effect.Destination == SkillProgramCardDestination.DiscardPile && effect.CardOwnerRef?.Kind == ProgramParticipantRef.SelectedTarget
                    ? SkillProgramCardSetVisibility.Public : SkillProgramCardSetVisibility.Private,
                [_facts.Location(card.Id)], frozenMovedSuit ?? (effect.RevealBeforeMove ? card.Suit : null));
        if (effect.AwaitMovementTriggers)
        {
            if (!_operations.BeginMovementWindow())
                _operations.ReturnMovement(frame.Id);
            return;
        }
        if (effect.CoverageResultBind is { } coverageBind)
            SetProgramAttackRangeCoverage(frame.Id, coverageBind, ownerSeat, beforeCoverage,
                _facts.CountLivingInAttackRange(ownerSeat));
        _frames.AdvanceProgram(frame.Id);
    }

}
