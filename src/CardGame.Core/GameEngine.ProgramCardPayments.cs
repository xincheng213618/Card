namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int ResolveProgramParticipant(ProgramSkillFrame frame, ProgramParticipantReference reference)
    {
        return reference.Kind switch
        {
            ProgramParticipantRef.Owner => frame.OwnerSeat,
            ProgramParticipantRef.Actor when frame.WindowContext?.CardUse is { } context => context.ActorSeat,
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

    private SkillProgramStepOutcome SelectAndMoveProgramOwnedCard(
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
        bool prohibitReplacingEquipment = false)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var chooserSeat = ResolveProgramParticipant(active, chooser);
        var cardOwnerSeat = ResolveProgramParticipant(active, cardOwner);
        var destinationSeat = destinationRef is null ? (int?)null : ResolveProgramParticipant(active, destinationRef);
        if (destination == SkillProgramCardDestination.SelectedTargetHand &&
            (destinationSeat is not { } seat || !_players[seat].IsAlive ||
             seat == cardOwnerSeat && !allowSameOwnerHandReturn))
            throw new InvalidOperationException("A selected-target transfer requires a distinct living recipient.");
        if (destination == SkillProgramCardDestination.SelectedTargetEquipment &&
            (destinationSeat is not { } recipientSeat || !_players[recipientSeat].IsAlive ||
             recipientSeat == cardOwnerSeat))
            throw new InvalidOperationException("An equipment gift requires a distinct living recipient.");
        if (prohibitReplacingEquipment &&
            (destination != SkillProgramCardDestination.SelectedTargetCorrespondingZone ||
             destinationSeat is not { } recipient || !_players[recipient].IsAlive))
            throw new InvalidOperationException("A no-replace corresponding-zone transfer requires a living selected target.");
        var choices = BuildOwnedCardPaymentChoices(active.Id, chooserSeat, cardOwnerSeat, zones,
            cardCategories, cardKinds,
            destination == SkillProgramCardDestination.SelectedTargetCorrespondingZone
                ? (zone, card) => destinationSeat is { } seat &&
                    CanMoveProgramCardToCorrespondingZone(card, zone, seat)
                : null,
            destination, destinationSeat, prohibitReplacingEquipment);
        if (choices.Count == 0)
        {
            if (skipIfNoCards)
                return SkillProgramStepOutcome.Continue;
            CancelProgramBindingAndCleanup(active, "没有可支付的区域牌，技能结算已取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var visibleCardIds = choices.SelectMany(choice => choice.Cards).Distinct().Order().ToArray();
        var skill = _contentRegistry!.GetSkill(frame.SkillId);
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, chooserSeat,
            destination == SkillProgramCardDestination.SelectedTargetHand
                ? "请选择一张区域牌交给目标。" : "请选择一张区域牌支付。", visibleCardIds, [],
            chooserSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = frame.WindowContext?.TargetSeat ?? cardOwnerSeat,
            SkillPrompt = new SkillPromptPresentation(frame.SkillId, skill.Name,
                destination == SkillProgramCardDestination.SelectedTargetHand
                    ? $"{skill.Name} · 选择转交牌" : $"{skill.Name} · 选择支付牌", skill.Description),
            Choices = choices
        };
        _status = _players[chooserSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private bool HasOwnedProgramCardCategory(int ownerSeat,
        IReadOnlyList<CardZoneKind> zones, IReadOnlyList<SkillProgramCardCategory> categories,
        IReadOnlyList<CardKind> kinds) =>
        zones.Any(zone => _cardZones.CardsAt(new CardLocation(zone, ownerSeat))
            .Any(card => MatchesProgramCardFilter(card.Kind, categories, kinds)));

    private IReadOnlyList<PromptChoice> BuildOwnedCardPaymentChoices(
        long frameId, int chooserSeat, int cardOwnerSeat, IReadOnlyList<CardZoneKind> zones,
        IReadOnlyList<SkillProgramCardCategory>? cardCategories = null,
        IReadOnlyList<CardKind>? cardKinds = null,
        Func<CardZoneKind, Card, bool>? canSelect = null,
        SkillProgramCardDestination destination = SkillProgramCardDestination.DiscardPile,
        int? destinationSeat = null,
        bool prohibitReplacingEquipment = false, int? correspondingZoneSeat = null)
    {
        var frame = GetActiveProgramFrame(frameId);
        var result = new List<PromptChoice>();
        foreach (var zone in zones)
        {
            var cards = zone switch
            {
                CardZoneKind.Hand => GetHand(_players[cardOwnerSeat]),
                CardZoneKind.Equipment => GetEquipment(_players[cardOwnerSeat]),
                CardZoneKind.Judgment => GetJudgment(_players[cardOwnerSeat]),
                _ => throw new InvalidOperationException("Unsupported payment card zone.")
            };
            for (var slot = 0; slot < cards.Count; slot++)
            {
                if (cardCategories is { Count: > 0 } && !MatchesProgramCardCategory(cards[slot].Kind, cardCategories))
                    continue;
                if (cardKinds is { Count: > 0 } && !cardKinds.Contains(cards[slot].Kind))
                    continue;
                if (canSelect is not null && !canSelect(zone, cards[slot]))
                    continue;
                if (destination == SkillProgramCardDestination.SelectedTargetEquipment &&
                    (destinationSeat is not { } recipient ||
                        !CanEnterEquipmentSlot(recipient, cards[slot])))
                    continue;
                if (zone == CardZoneKind.Equipment && cardOwnerSeat == frame.OwnerSeat &&
                    IsActiveProgramSourceEquipmentCard(cardOwnerSeat, frame.SkillId,
                        frame.SkillInstanceId, cards[slot]))
                    continue;
                if (prohibitReplacingEquipment && correspondingZoneSeat is { } recipientSeat &&
                    EquipmentCatalog.IsEquipment(cards[slot].Kind) &&
                    GetEquipment(_players[recipientSeat]).Any(item =>
                        EquipmentCatalog.Get(item.Kind).Slot == EquipmentCatalog.Get(cards[slot].Kind).Slot))
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
                    hidden ? $"选择第 {slot + 1} 张手牌" : $"选择【{cards[slot].DisplayName}】",
                    hidden ? [] : [cards[slot].Id], [], parameters));
            }
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private void ResolveSelectAndMoveOwnedCardChoice(
        ProgramSkillFrame frame, SkillProgramEffect effect, PromptChoice selected)
    {
        var chooserSeat = ResolveProgramParticipant(frame, effect.ChooserRef!);
        var ownerSeat = ResolveProgramParticipant(frame, effect.CardOwnerRef!);
        if (!_players[frame.OwnerSeat].IsAlive || !_players[chooserSeat].IsAlive || !_players[ownerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(frame, "支付参与者或技能实例已失效，技能剩余步骤取消。");
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
            CardZoneKind.Hand => GetHand(_players[ownerSeat]),
            CardZoneKind.Equipment => GetEquipment(_players[ownerSeat]),
            CardZoneKind.Judgment => GetJudgment(_players[ownerSeat]),
            _ => throw new InvalidOperationException("Unsupported payment card zone.")
        };
        if (slot < 0 || slot >= cards.Count)
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(frame, "公布的支付牌位已失效，技能结算已取消。");
            return;
        }
        var card = cards[slot];
        if (zone == CardZoneKind.Equipment && ownerSeat == frame.OwnerSeat &&
            IsActiveProgramSourceEquipmentCard(ownerSeat, frame.SkillId, frame.SkillInstanceId, card))
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(frame, "提供当前技能的装备牌不能用于支付。");
            return;
        }
        if (effect.CardCategories.Count > 0 &&
            !MatchesProgramCardCategory(card.Kind, effect.CardCategories))
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(frame, "公布的支付牌类别已失效，技能结算已取消。");
            return;
        }
        if (effect.CardKinds.Count > 0 && !effect.CardKinds.Contains(card.Kind))
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(frame, "公布的支付牌种类已失效，技能结算已取消。");
            return;
        }
        var destinationSeat = effect.Destination == SkillProgramCardDestination.SelectedTargetCorrespondingZone
            ? ResolveProgramParticipant(frame, effect.TargetReference!) : (int?)null;
        if (destinationSeat is { } correspondingSeat &&
            !CanMoveProgramCardToCorrespondingZone(card, zone, correspondingSeat))
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(frame, "对应区域已无法接收所选牌，技能剩余结算已取消。");
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
            _ => throw new InvalidOperationException("Unsupported selected-card destination.")
        };
        if (destination.OwnerSeat is { } recipientSeat && !_players[recipientSeat].IsAlive)
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(frame, "受牌角色已失效，技能结算已取消。");
            return;
        }
        if (effect.Destination == SkillProgramCardDestination.SelectedTargetEquipment)
        {
            var recipient = _players[destination.OwnerSeat!.Value];
            var slotAvailable = !recipient.EquipmentAreaAbolished && CanEnterEquipmentSlot(recipient.Seat, card);
            if (!slotAvailable)
            {
                // The target's corresponding slot was filled (or the area was
                // abolished) between selection and resolution: the gift cancels.
                ClearPendingDecision();
                CancelProgramBindingAndCleanup(frame, "目标装备槽不可用，技能结算已取消。");
                return;
            }
        }
        if (destination == CardLocation.Hand(ownerSeat) && source == CardLocation.Hand(ownerSeat))
            throw new InvalidOperationException("A same-hand card movement is not a payment.");
        if (destination == CardLocation.Hand(ownerSeat) &&
            effect.Destination == SkillProgramCardDestination.SelectedTargetHand &&
            (!effect.AllowSameOwnerHandReturn || zone is not (CardZoneKind.Equipment or CardZoneKind.Judgment)))
            throw new InvalidOperationException("A same-owner hand return requires an authorized public source zone.");
        if (effect.CoverageResultBind is not null && zone != CardZoneKind.Equipment)
            throw new InvalidOperationException("Attack-range coverage requires a public equipment movement.");
        if (effect.Destination == SkillProgramCardDestination.SelectedTargetCorrespondingZone &&
            destination.Zone == CardZoneKind.Equipment)
        {
            var recipient = destination.OwnerSeat!.Value;
            var replacedSlot = EquipmentCatalog.Get(card.Kind).Slot;
            var replaced = GetEquipment(_players[recipient])
                .SingleOrDefault(item => EquipmentCatalog.Get(item.Kind).Slot == replacedSlot);
            if (replaced is not null && replaced.Id != card.Id)
            {
                if (effect.ProhibitReplacingEquipment)
                {
                    ClearPendingDecision();
                    CancelProgramBindingAndCleanup(frame, "目标装备栏已有牌，不能替换原有装备，技能结算已取消。");
                    return;
                }
                MoveCard(replaced, CardLocation.Equipment(recipient), CardLocation.DiscardPile,
                    CardMoveReasons.EquipmentReplace);
            }
        }
        ClearPendingDecision();
        var frozenMovedSuit = effect.FreezeMovedCardSuit ? EffectiveSuit(_players[ownerSeat], card) : (Suit?)null;
        var beforeCoverage = effect.CoverageResultBind is null ? 0 : CountLivingInAttackRange(ownerSeat);
        if (effect.AwaitMovementTriggers)
        {
            _resolutionStack[^1] = frame with
            {
                PendingMovementContinuation = new ProgramMovementContinuation(
                    ownerSeat, beforeCoverage, effect.CoverageResultBind)
            };
        }
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}");
        if (effect.RevealBeforeMove)
            QueueGameEvent(new ProgramCardsRevealedEvent(frame.Id, frame.SkillId,
                GetProgramBindingId(frame), frame.OwnerSeat, effect.ResultBind!,
                Array.AsReadOnly(new[] { ToSnapshot(card) })));
        if (effect.Destination == SkillProgramCardDestination.SelectedTargetHand)
        {
            MoveCard(card, source, CardLocation.Processing, reason);
            MoveCard(card, CardLocation.Processing, destination, reason);
        }
        else MoveCard(card, source, destination, reason);
        if (effect.Destination == SkillProgramCardDestination.SelectedTargetEquipment)
        {
            // Mirror CompleteEquipmentUse's public entry trio (move + event + log);
            // a gifted card never replaces anything, so there is no replaced card.
            QueueGameEvent(new EquipmentChangedEvent(frame.Id, destination.OwnerSeat!.Value,
                EquipmentCatalog.Get(card.Kind).Slot, card.Id, card.Kind, ReplacedCardId: null));
            AddLog("EquipmentChanged",
                $"{_players[frame.OwnerSeat].Name} 将【{EquipmentCatalog.Get(card.Kind).DisplayName}】置于 " +
                $"{_players[destination.OwnerSeat.Value].Name} 的装备区。",
                destination.OwnerSeat.Value);
        }
        if (effect.ResultBind is { } bind)
            SetProgramCardSet(frame.Id, bind, [card.Id],
                effect.RevealBeforeMove ? SkillProgramCardSetVisibility.Public : SkillProgramCardSetVisibility.Private,
                [destination], frozenMovedSuit ?? (effect.RevealBeforeMove ? card.Suit : null));
        if (effect.AwaitMovementTriggers)
        {
            if (!TryBeginCardsMovedProgramWindow())
                CompleteAwaitedProgramMovement(frame.Id);
            return;
        }
        if (effect.CoverageResultBind is { } coverageBind)
            SetProgramAttackRangeCoverage(frame.Id, coverageBind, ownerSeat, beforeCoverage,
                CountLivingInAttackRange(ownerSeat));
        ContinueProgramSkill(frame.Id);
    }

    private bool CanMoveProgramCardToCorrespondingZone(Card card, CardZoneKind sourceZone,
        int destinationSeat)
    {
        if (!IsValidPlayerSeat(destinationSeat) || !_players[destinationSeat].IsAlive)
            return false;
        var recipient = _players[destinationSeat];
        if (sourceZone == CardZoneKind.Judgment)
            return !recipient.JudgmentAreaAbolished &&
                IsDelayedCard(GetJudgmentEffectiveCardKind(card)) &&
                !HasJudgmentEffectiveCard(recipient, GetJudgmentEffectiveCardKind(card));
        return sourceZone is CardZoneKind.Hand or CardZoneKind.Equipment &&
            EquipmentCatalog.IsEquipment(card.Kind) && !recipient.EquipmentAreaAbolished;
    }

    private CardLocation ProgramCorrespondingZoneLocation(Card card, CardZoneKind sourceZone,
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

    private int CountLivingInAttackRange(int subjectSeat)
    {
        if (!_players[subjectSeat].IsAlive) return 0;
        var range = GetAttackRange(subjectSeat);
        return _players.Count(player => player.IsAlive && player.Seat != subjectSeat &&
            IsWithinAttackRange(subjectSeat, player.Seat));
    }

    private bool WouldEquipmentRemovalReduceCoverage(int subjectSeat, int equipmentCardId)
    {
        if (!_players[subjectSeat].IsAlive || !GetEquipment(_players[subjectSeat])
                .Any(card => card.Id == equipmentCardId)) return false;
        var subject = _players[subjectSeat];
        var projectedRange = ConvertRuleValue(EvaluateAttackRange(subject, equipmentCardId));
        var projectedCount = _players.Count(player => player.IsAlive && player.Seat != subjectSeat &&
            (IsGameFactionAttackRangeTarget(subjectSeat, player.Seat) ||
             ConvertRuleValue(EvaluateDistance(subject, player, equipmentCardId)) <= projectedRange));
        return projectedCount < CountLivingInAttackRange(subjectSeat);
    }

    private void SetProgramAttackRangeCoverage(long frameId, string name, int subjectSeat,
        int beforeCount, int afterCount)
    {
        var frame = GetActiveProgramFrame(frameId);
        if (frame.AttackRangeCoverageBindings.Any(item => item.Name == name))
            throw new InvalidOperationException("The attack-range coverage result was already produced.");
        _resolutionStack[^1] = frame with
        {
            AttackRangeCoverageBindings = Array.AsReadOnly(frame.AttackRangeCoverageBindings
                .Append(new ProgramAttackRangeCoverageBinding(name, subjectSeat, beforeCount, afterCount)).ToArray())
        };
    }

    private static bool IsProgramAttackRangeCoverageDecreased(ProgramSkillFrame frame, string bind)
    {
        var result = frame.AttackRangeCoverageBindings.Single(item => item.Name == bind);
        return result.AfterCount < result.BeforeCount;
    }

    private void CompleteAwaitedProgramMovement(long frameId)
    {
        var frame = GetActiveProgramFrame(frameId);
        var pending = frame.PendingMovementContinuation ??
            throw new InvalidOperationException("The movement continuation is missing.");
        _resolutionStack[^1] = frame with { PendingMovementContinuation = null };
        if (!_players[frame.OwnerSeat].IsAlive || !_players[pending.SubjectSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        {
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(frameId),
                "移动响应后技能持有人、装备持有人或技能实例已失效。");
            return;
        }
        if (pending.CoverageResultBind is { } bind)
            SetProgramAttackRangeCoverage(frameId, bind, pending.SubjectSeat, pending.BeforeCount,
                CountLivingInAttackRange(pending.SubjectSeat));
        if (frame.RepeatedJudgment is { LastMatched: not null })
            ContinueProgramRepeatedJudgmentAfterMovement(frameId);
        else
            ContinueProgramSkill(frameId);
    }

    private static bool MatchesProgramCardCategory(
        CardKind kind,
        IReadOnlyList<SkillProgramCardCategory> categories)
    {
        return categories.Contains(GetProgramCardCategory(kind));
    }

    private static bool MatchesProgramCardFilter(
        CardKind kind,
        IReadOnlyList<SkillProgramCardCategory> categories,
        IReadOnlyList<CardKind> kinds) =>
        (categories.Count == 0 || MatchesProgramCardCategory(kind, categories)) &&
        (kinds.Count == 0 || kinds.Contains(kind));

    /// <summary>Equipment gifts cannot replace an equipped card: the matching slot must be free.</summary>
    private bool CanEnterEquipmentSlot(int recipientSeat, Card card) =>
        EquipmentCatalog.IsEquipment(card.Kind) &&
        GetEquipment(_players[recipientSeat]).Count(equipped =>
            EquipmentCatalog.Get(equipped.Kind).Slot == EquipmentCatalog.Get(card.Kind).Slot) <
        _players[recipientSeat].EquipmentSlotCapacity(EquipmentCatalog.Get(card.Kind).Slot);

    private static SkillProgramCardCategory GetProgramCardCategory(CardKind kind)
    {
        return EquipmentCatalog.IsEquipment(kind)
            ? SkillProgramCardCategory.Equipment
            : CardCatalog.Get(kind).CategoryName switch
            {
                "基本牌" => SkillProgramCardCategory.Basic,
                "锦囊牌" => SkillProgramCardCategory.Trick,
                _ => throw new InvalidOperationException($"Card kind '{kind}' has no supported skill-program category.")
            };
    }
}
