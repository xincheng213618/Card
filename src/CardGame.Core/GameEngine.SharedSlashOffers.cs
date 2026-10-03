namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string SharedSlashGiftReason = "program.shared-slash.gift";
    private const string SharedSlashOwnerDrawReason = "program.shared-slash.owner-draw";
    private const string SharedSlashRecipientDrawReason = "program.shared-slash.recipient-draw";
    private IEnumerable<IGameEvent> SharedSlashHistory() => _events.Select(e => e.Payload).Concat(_pendingEvents);

    private SkillProgramEffect SharedSlashPaused(ProgramSkillFrame f) =>
        ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;

    // Only the new terminal gift contract extends printed selection by the
    // already-issued intrinsic Alcohol-as-Slash identity. Optional ViewAs is not identity.
    private bool IsSharedSlashGiftSelection(ProgramSkillFrame f, string bind)
    {
        if (f.TriggerId is not null) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!);
        return f.InstructionIndex == 1 && plan.Instructions.Count == 2 &&
            plan.Instructions[0].Op == SkillProgramEffectOp.SelectOwnedCards && plan.Instructions[0].ResultBind == bind &&
            plan.Instructions[1].Op == SkillProgramEffectOp.GiveBoundCardThenOfferVirtualSlashOrSharedDraw &&
            plan.Instructions[1].SourceBind == bind;
    }

    private bool MatchesProgramOwnedSelectionKind(ProgramSkillFrame f, string bind, CharacterState owner,
        Card card, CardLocation from, IReadOnlyList<CardKind> kinds) =>
        (!IsSharedSlashGiftSelection(f, bind) || !card.IsGeneralWeapon) && (kinds.Count == 0 || kinds.Contains(card.Kind) ||
        IsSharedSlashGiftSelection(f, bind) && from == CardLocation.Hand(owner.Seat) &&
        card.Kind == CardKind.Alcohol && kinds.Contains(CardKind.Slash) && HasAlcoholKingIdentityPolicy(owner));

    private bool SharedSlashGiftKindStillValid(ProgramSkillFrame f, Card card, CardLocation from)
    {
        var plan = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!);
        var selection = plan.Instructions[0];
        return selection.CardKinds.Count == 0 || selection.CardKinds.Contains(card.Kind) ||
            from == CardLocation.Hand(f.OwnerSeat) && card.Kind == CardKind.Alcohol &&
            selection.CardKinds.Contains(CardKind.Slash) && HasAlcoholKingIdentityPolicy(_players[f.OwnerSeat]);
    }

    private CardConversionSource SharedSlashSource(ProgramSkillFrame f) =>
        new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    private SkillProgramStepOutcome BeginSharedSlashGift(long frameId, int actorSeat, string bind)
    {
        var f = GetActiveProgramFrame(frameId); var cards = GetProgramCardSet(f, bind);
        if (f.SharedSlashOffer is not null) throw new InvalidOperationException("A shared Slash gift cannot repay its cost.");
        if (_winner != Winner.None || !IsValidPlayerSeat(actorSeat) || actorSeat == f.OwnerSeat ||
            f.SelectedTargetSeats is not [var selected] || selected != actorSeat ||
            !_players[actorSeat].IsAlive || !_players[f.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            cards.CardIds.Count != 1 || cards.SourceLocations.Count != 1 ||
            cards.SourceLocations[0].OwnerSeat != f.OwnerSeat ||
            cards.SourceLocations[0].Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
            _cardZones.GetLocation(cards.CardIds[0]) != cards.SourceLocations[0])
        {
            CancelProgramBindingAndCleanup(f, "赠牌来源、受赠者或技能实例已失效，未支付。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var card = _cardZones.CardsAt(cards.SourceLocations[0]).Single(c => c.Id == cards.CardIds[0]);
        if (card.IsGeneralWeapon || !SharedSlashGiftKindStillValid(f, card, cards.SourceLocations[0]))
        {
            CancelProgramBindingAndCleanup(f, "赠牌的实际身份已失效，未支付。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var receipt = new SharedSlashOfferReceipt(f.InstructionIndex, bind, SharedSlashSource(f), actorSeat,
            new(card.Id, card.Kind, cards.SourceLocations[0], CapturePhysicalCardColor(f.OwnerSeat, card)));
        ReplaceRuntimeTop(f with { SharedSlashOffer = receipt });
        MoveProgramCardsFromMultipleSources([card.Id], CardLocation.Hand(actorSeat), new(SharedSlashGiftReason), (_, movements) =>
        {
            var moved = movements.Single(); var current = GetActiveProgramFrame(frameId);
            ReplaceRuntimeFrame(frameId, current with { SharedSlashOffer = current.SharedSlashOffer! with { GiftMovementSequence = moved.Sequence } });
            AdvanceEventRulesAndQueueFact(new SharedSlashGiftCommittedEvent(frameId, receipt.Source, actorSeat, moved.Sequence));
        });
        AdvanceRuntimeProgram(frameId);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private bool SharedSlashUnchosenSourceValid(ProgramSkillFrame f) =>
        _winner == Winner.None && _players[f.OwnerSeat].IsAlive && _players[f.SharedSlashOffer!.ActorSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId);

    private int[] SharedSlashLegalTargets(int actorSeat) =>
        GetProgramVirtualSlashOfferTargets(actorSeat).Where(seat =>
            !IsCardTargetProhibited(_players[seat], CardKind.Slash, Suit.None)).ToArray();

    private bool ResumeSharedSlashOffer(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != frameId || f.SharedSlashOffer is not { } r) return false;
        AssertSharedSlashOffer(f);
        if (TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.Program) ||
            TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.Program) ||
            TryBeginCardsMovedProgramWindow(f.Id)) return true;
        if (r.Stage == SharedSlashOfferStage.GiftChildren)
        {
            if (!SharedSlashUnchosenSourceValid(f)) { FinishProgramSkill(f, false); return true; }
            ReplaceRuntimeTop(f = f with { SharedSlashOffer = r with { Stage = SharedSlashOfferStage.ChoosingOption } });
            PublishSharedSlashOffer(f); return true;
        }
        if (r.Stage is SharedSlashOfferStage.ChoosingOption or SharedSlashOfferStage.ChoosingTarget)
        {
            if (!SharedSlashUnchosenSourceValid(f)) { ClearPendingDecision(); FinishProgramSkill(f, false); return true; }
            if (_pendingDecision is null) PublishSharedSlashOffer(f);
            return true;
        }
        if (r.Stage == SharedSlashOfferStage.SlashIssued)
            throw new InvalidOperationException("A shared Slash producer returned without its exact typed completion.");
        if (r.Stage == SharedSlashOfferStage.OwnerDrawIssued)
        {
            IssueSharedSlashDraw(f, recipient: true); return true;
        }
        if (r.Stage == SharedSlashOfferStage.RecipientDrawIssued)
        {
            ReplaceRuntimeTop(f = f with { SharedSlashOffer = r with { Stage = SharedSlashOfferStage.Complete } });
            FinishProgramSkill(f, true); return true;
        }
        FinishProgramSkill(f, true); return true;
    }

    private IReadOnlyList<PromptChoice> SharedSlashChoices(ProgramSkillFrame f)
    {
        var r = f.SharedSlashOffer!;
        PromptChoice Choice(string option, string label, IReadOnlyList<int> targets) => new(
            new($"shared-slash.{f.Id}.{option}"), label, [], targets,
            new Dictionary<string, string> { ["program-action"] = "shared-slash-offer",
                ["frame-id"] = f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), ["offer-option"] = option });
        if (r.Stage == SharedSlashOfferStage.ChoosingTarget)
            return Array.AsReadOnly(SharedSlashLegalTargets(r.ActorSeat)
                .Select(seat => Choice($"target-{seat}", $"令 {_players[r.ActorSeat].Name} 对 {_players[seat].Name} 使用【杀】", [seat])).ToArray());
        var choices = new List<PromptChoice>();
        if (SharedSlashLegalTargets(r.ActorSeat).Length > 0) choices.Add(Choice("slash", "视为使用一张【杀】；若造成伤害，双方各摸一张牌", []));
        choices.Add(Choice("draw", "你与明策拥有者各摸一张牌", []));
        return Array.AsReadOnly(choices.ToArray());
    }

    private void PublishSharedSlashOffer(ProgramSkillFrame f)
    {
        var r = f.SharedSlashOffer!; var target = r.Stage == SharedSlashOfferStage.ChoosingTarget;
        var chooser = target ? f.OwnerSeat : r.ActorSeat; var choices = SharedSlashChoices(f);
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, target ? "请选择该角色使用【杀】的目标。" : "请选择使用【杀】或双方摸牌。",
            [], target ? SharedSlashLegalTargets(r.ActorSeat) : [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = chooser, Choices = choices,
          SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveSharedSlashOfferChoice(PromptChoice choice)
    {
        var f = GetActiveProgramFrame(_resolutionStack.Last().Id); AssertSharedSlashOffer(f);
        var r = f.SharedSlashOffer ?? throw new InvalidOperationException("A shared Slash answer lost its paid gift.");
        if (_pendingDecision?.PlayerSeat != (r.Stage == SharedSlashOfferStage.ChoosingTarget ? f.OwnerSeat : r.ActorSeat) ||
            !SharedSlashChoices(f).Any(c => c.Id == choice.Id && c.Targets.SequenceEqual(choice.Targets) && c.Cards.SequenceEqual(choice.Cards)))
            throw new InvalidOperationException("A shared Slash choice changed its frozen chooser or target.");
        ClearPendingDecision();
        if (!SharedSlashUnchosenSourceValid(f)) { FinishProgramSkill(f, false); return; }
        var option = choice.Parameters.GetValueOrDefault("offer-option");
        if (r.Stage == SharedSlashOfferStage.ChoosingOption && option == "draw") { IssueSharedSlashDraw(f, false); return; }
        if (r.Stage == SharedSlashOfferStage.ChoosingOption && option == "slash")
        {
            ReplaceRuntimeTop(f = f with { SharedSlashOffer = r with { Stage = SharedSlashOfferStage.ChoosingTarget } });
            PublishSharedSlashOffer(f); return;
        }
        if (r.Stage != SharedSlashOfferStage.ChoosingTarget || choice.Targets is not [var seat] ||
            !SharedSlashLegalTargets(r.ActorSeat).Contains(seat) || option != $"target-{seat}")
            throw new InvalidOperationException("The offered shared Slash target is no longer legal.");
        BeginSharedSlashUse(f, seat);
    }

    private void BeginSharedSlashUse(ProgramSkillFrame f, int targetSeat)
    {
        if (ActiveCardAttack is not null || ActiveDuel is not null)
            throw new InvalidOperationException("A shared Slash cannot overwrite an attack.");
        var r = f.SharedSlashOffer!; var actor = _players[r.ActorSeat]; var id = ++_resolutionSequence;
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence,
            _resolutionStack.OfType<CardUseFrame>().LastOrDefault()?.Action?.ActionId, CardActionType.Use,
            actor.Seat, actor.Seat, null, null, null, CardKind.Slash, [targetSeat], [], [], effectiveSuit: Suit.None, effectiveRank: 0));
        var returned = new SharedSlashBenefitReturn(f.Id, f.InstructionIndex, r.Source, actor.Seat, targetSeat, id, action.ActionId);
        ReplaceRuntimeTop(f with { SharedSlashOffer = r with { Stage = SharedSlashOfferStage.SlashIssued, SlashReturn = returned } });
        PushRuntimeFrame(new CardUseFrame(id, actor.Seat, 0, CardKind.Slash, [targetSeat], PhysicalCardIds: [])
        { Action = action, SharedSlashBenefit = returned });
        AdvanceEventRulesAndQueueFact(new SharedSlashUseIssuedEvent(returned));
        if (TracksPlayCardHistory) AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(action));
        AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(id, 0, CardKind.Slash, actor.Seat));
        AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(id, [targetSeat]));
        RecordYingboCardUse(id, actor.Seat, CardKind.Slash);
        RecordProgramUsedBasicCard(actor.Seat, CardKind.Slash);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, CardKind.Slash);
        RecordActualPlayPhaseUse(action);
        var attack = new CardAttackHandle(this, id, actor.Seat, targetSeat, card: null,
            damageAmount: actor.HasAlcoholEffect ? 2 : 1, playedCardKind: CardKind.Slash,
            ignoresArmor: HasCardArmorBypass(actor, _players[targetSeat], CardKind.Slash), programSkillCardUseFrameId: f.Id);
        CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false; ActiveCardAttack = attack;
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, CardKind.Slash, actor.Seat, targetSeat));
        TryMarkProgramUseCommitted(id);
        if (!TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardUseCommitted, action.TargetSeats, ProgramCardContinuation.CommittedSlash))
            BeginSlashTargetResolution(attack);
    }

    private void CompleteSharedSlashBenefit(AttackCompletionReceipt completion)
    {
        var returned = completion.SharedSlashBenefit ?? throw new InvalidOperationException("A shared Slash lost its typed return.");
        var f = GetActiveProgramFrame(returned.ProgramFrameId); var r = f.SharedSlashOffer!;
        if (completion.ProgramFrameId != f.Id || completion.ResolutionId != returned.CardUseFrameId ||
            r.Stage != SharedSlashOfferStage.SlashIssued || r.SlashReturn != returned ||
            returned.Source != SharedSlashSource(f) || returned.InstructionIndex != f.InstructionIndex ||
            !SharedSlashHistory().OfType<CardUseFinishedEvent>().Any(e => e.ResolutionId == returned.CardUseFrameId) ||
            _resolutionStack.OfType<CardUseFrame>().Any(u => u.Id == returned.CardUseFrameId))
            throw new InvalidOperationException("The shared Slash completed outside its exact owning use.");
        ReplaceRuntimeTop(f = f with { SharedSlashOffer = r with { CausedDamage = completion.SharedSlashCausedDamage } });
        AdvanceEventRulesAndQueueFact(new SharedSlashUseReturnedEvent(returned, completion.SharedSlashCausedDamage));
        if (completion.SharedSlashCausedDamage) IssueSharedSlashDraw(f, false);
        else { ReplaceRuntimeTop(f = f with { SharedSlashOffer = f.SharedSlashOffer! with { Stage = SharedSlashOfferStage.Complete } }); FinishProgramSkill(f, true); }
    }

    private void IssueSharedSlashDraw(ProgramSkillFrame f, bool recipient)
    {
        var r = f.SharedSlashOffer!; var seat = recipient ? r.ActorSeat : f.OwnerSeat;
        ReplaceRuntimeTop(f = f with { SharedSlashOffer = r with
        { Stage = recipient ? SharedSlashOfferStage.RecipientDrawIssued : SharedSlashOfferStage.OwnerDrawIssued } });
        var drawn = _winner == Winner.None && _players[seat].IsAlive
            ? DrawCards(_players[seat], 1, true, new(recipient ? SharedSlashRecipientDrawReason : SharedSlashOwnerDrawReason))
            : Array.Empty<int>();
        AdvanceEventRulesAndQueueFact(new SharedSlashDrawIssuedEvent(f.Id, seat, drawn.Count));
        AdvanceRuntimeProgram(f.Id);
    }

    private void AssertSharedSlashOffer(ProgramSkillFrame f)
    {
        if (f.SharedSlashOffer is not { } r) return;
        if (f.TriggerId is not null || f.InstructionIndex != 2 || r.InstructionIndex != f.InstructionIndex ||
            SharedSlashPaused(f) is not { Op: SkillProgramEffectOp.GiveBoundCardThenOfferVirtualSlashOrSharedDraw } op ||
            op.SourceBind != r.SourceBind || r.Source != SharedSlashSource(f) ||
            f.SelectedTargetSeats is not [var actor] || actor != r.ActorSeat || actor == f.OwnerSeat || !IsValidPlayerSeat(actor) ||
            !Enum.IsDefined(r.Stage) || r.GiftCost.From.OwnerSeat != f.OwnerSeat ||
            r.GiftCost.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || r.GiftMovementSequence <= 0 ||
            !_cardMovements.Any(m => m.Sequence == r.GiftMovementSequence && m.CardId == r.GiftCost.CardId &&
                m.CardKind == r.GiftCost.CardKind && m.From == r.GiftCost.From &&
                m.To == CardLocation.Hand(actor) &&
                m.Reason.Value == SharedSlashGiftReason) ||
            !SharedSlashHistory().OfType<SharedSlashGiftCommittedEvent>().Any(e => e.ProgramFrameId == f.Id &&
                e.Source == r.Source && e.RecipientSeat == actor && e.MovementSequence == r.GiftMovementSequence))
            throw new InvalidOperationException("A shared Slash offer lost its exact paid gift and owning instruction.");
        if (r.SlashReturn is { } returned)
        {
            if (returned.ProgramFrameId != f.Id || returned.InstructionIndex != f.InstructionIndex || returned.Source != r.Source ||
                returned.ActorSeat != actor || returned.OriginalTargetSeat == actor || !IsValidPlayerSeat(returned.OriginalTargetSeat) ||
                returned.CardUseFrameId <= f.Id || returned.CardActionId <= 0 ||
                !SharedSlashHistory().OfType<SharedSlashUseIssuedEvent>().Any(e => e.Return == returned))
                throw new InvalidOperationException("A shared Slash lost its original typed use issuance.");
            var use = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Id == returned.CardUseFrameId);
            if (use is not null && (use.SharedSlashBenefit != returned ||
                use.CardId != 0 || use.CardKind != CardKind.Slash || use.PhysicalCardIds?.Count != 0 ||
                use.Action is not { Type: CardActionType.Use, EffectiveKind: CardKind.Slash } a ||
                a.ActionId != returned.CardActionId || a.ActorSeat != actor || a.ProviderSeat != actor ||
                a.PhysicalCards.Count != 0 || a.ConversionChain.Count != 0 ||
                !a.TargetSeats.SequenceEqual(use.TargetSeats) ||
                use.TargetSeats.Count > 1 && !HasIssuedOriginalTargetAdditionTail(use) ||
                !SharedSlashHistory().OfType<TargetsConfirmedEvent>().Any(e => e.ResolutionId == use.Id && e.TargetSeats.SequenceEqual([returned.OriginalTargetSeat]))))
                throw new InvalidOperationException("A shared Slash changed its exact zero-entity accepted action.");
            if (r.Stage == SharedSlashOfferStage.SlashIssued && (use is null || r.CausedDamage is not null) ||
                r.Stage != SharedSlashOfferStage.SlashIssued && r.CausedDamage is null)
                throw new InvalidOperationException("A shared Slash lost its aggregate typed completion.");
        }
        else if (r.Stage == SharedSlashOfferStage.SlashIssued || r.CausedDamage is not null)
            throw new InvalidOperationException("A shared Slash damage result has no issued use.");
        if (ReferenceEquals(f, _resolutionStack.LastOrDefault()) &&
            r.Stage is SharedSlashOfferStage.ChoosingOption or SharedSlashOfferStage.ChoosingTarget &&
            _pendingDecision is { } decision &&
            (decision.PlayerSeat != (r.Stage == SharedSlashOfferStage.ChoosingTarget ? f.OwnerSeat : actor) ||
                !decision.IsPrivate || !AssistedChoicesEqual(decision.Choices, SharedSlashChoices(f))))
            throw new InvalidOperationException("A shared Slash prompt changed its actual chooser or frozen public choices.");
    }

    private PromptChoice SelectAiSharedSlashOffer(PendingDecision decision, ProgramSkillFrame f)
    {
        var actor = f.SharedSlashOffer!.ActorSeat; var snapshot = CreateSnapshot(decision.PlayerSeat);
        var damage = new SkillProgramAiHint(0, 0, 0, 0, 0, 1, false, false);
        if (f.SharedSlashOffer.Stage == SharedSlashOfferStage.ChoosingTarget)
            return decision.Choices.OrderByDescending(c => _aiBrains[decision.PlayerSeat].ScoreProgramTarget(snapshot, c.Targets.Single(), damage))
                .ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
        var canDamage = SharedSlashLegalTargets(actor).Any(seat => _aiBrains[actor].ScoreProgramTarget(snapshot, seat, damage) > 0);
        return decision.Choices.First(c => c.Parameters.GetValueOrDefault("offer-option") == (canDamage ? "slash" : "draw"));
    }

    private void AssertSharedSlashUseReturns()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(u => u.SharedSlashBenefit is not null))
        {
            var returned = use.SharedSlashBenefit!;
            var index = _resolutionStack.FindIndex(frame => frame.Id == use.Id);
            if (index <= 0 || _resolutionStack[index - 1] is not ProgramSkillFrame f || f.Id != returned.ProgramFrameId ||
                f.SharedSlashOffer?.SlashReturn != returned)
                throw new InvalidOperationException("A shared Slash return lost its exact live program producer.");
            AssertSharedSlashOffer(f);
        }
    }

    private sealed partial class ProgramSkillHost : ISharedSlashOfferProgramHost
    {
        public SkillProgramStepOutcome GiveBoundCardThenOffer(ProgramSkillFrame frame, int actorSeat, string sourceBind) =>
            engine.BeginSharedSlashGift(frame.Id, actorSeat, sourceBind);
    }
}
