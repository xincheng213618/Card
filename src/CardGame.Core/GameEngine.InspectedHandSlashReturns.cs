namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidInspectedHandSlash(ProgramSkillFrame f)
    {
        if (f.InspectedHandSlash is not { } d || f.TriggerId is not null || f.WindowContext is not null ||
            f.InstructionIndex != 1 || d.InstructionIndex != f.InstructionIndex || f.SelectedCardIds.Count != 0 ||
            f.SelectedTargetSeats is not [var target] || target != d.TargetSeat || !IsValidPlayerSeat(target) || target == f.OwnerSeat ||
            d.Source != InspectedHandSource(f) || d.GameplayHash != f.GameplayHash || d.TurnNumber != _turnNumber ||
            d.TurnOwnerSeat != _currentSeat || d.TurnOwnerSeat != f.OwnerSeat || _phase != TurnPhase.Play ||
            d.HpBefore < 1 || d.HpAfter != d.HpBefore - 1 || !Enum.IsDefined(d.Stage) ||
            d.ViewedCardIds.Any(id => id <= 0) || d.ViewedCardIds.Distinct().Count() != d.ViewedCardIds.Count ||
            _contentRegistry.GetSkill(f.SkillId).Program is not { } program || program.GameplayHash != f.GameplayHash) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(f, program);
        if (plan.Activation is not { UsesPerPhase: 1, MinCards: 0, MaxCards: 0, MinTargets: 1, MaxTargets: 1,
                TargetKind: SkillProgramTargetKind.OtherLivingWithHand } ||
            plan.Instructions is not [{ Op: SkillProgramEffectOp.PayHpInspectHandThenDiscardOrSlash }]) return false;
        var facts = CompleteProgramEventHistory().ToArray();
        if (facts.OfType<InspectedHandHpPaidEvent>().Count(e => e.ProgramFrameId == f.Id && e.Source == d.Source &&
                e.GameplayHash == d.GameplayHash && e.TurnNumber == d.TurnNumber && e.TurnOwnerSeat == d.TurnOwnerSeat &&
                e.TargetSeat == d.TargetSeat && e.HpBefore == d.HpBefore && e.HpAfter == d.HpAfter) != 1 ||
            facts.OfType<ProgramSkillHpLostEvent>().Count(e => e.FrameId == f.Id && e.SkillId == f.SkillId &&
                e.TargetSeat == f.OwnerSeat && e.Amount == 1 && e.RemainingHp == d.HpAfter) != 1) return false;
        if (d.Stage == InspectedHandSlashStage.HpChildren) return d.ViewedCardIds.Count == 0 && !d.ContainsPrintedDodge &&
            d.DiscardedCardId is null && d.SequenceBefore == 0 && d.SequenceAfter == 0 && d.SlashReturn is null;
        if (d.ViewedCardIds.Count == 0 || d.ContainsPrintedDodge != d.ViewedCardIds.Any(id => GetAttackCard(id).Kind == CardKind.Dodge) ||
            facts.OfType<InspectedHandViewedEvent>().Count(e => e.ProgramFrameId == f.Id && e.ViewerSeat == f.OwnerSeat &&
                e.TargetSeat == d.TargetSeat && e.Count == d.ViewedCardIds.Count) != 1) return false;
        if (d.Stage == InspectedHandSlashStage.Viewing) return d.DiscardedCardId is null && d.SlashReturn is null &&
            d.SequenceBefore == 0 && d.SequenceAfter == 0 && GetHand(_players[target]).Select(c => c.Id).SequenceEqual(d.ViewedCardIds);
        if (d.Stage == InspectedHandSlashStage.SlashIssued) return d.ContainsPrintedDodge && d.DiscardedCardId is null &&
            d.SequenceBefore == 0 && d.SequenceAfter == 0 && d.SlashReturn is { } issued &&
            issued.ProgramFrameId == f.Id && issued.InstructionIndex == d.InstructionIndex && issued.Source == d.Source &&
            issued.GameplayHash == d.GameplayHash && issued.TurnNumber == d.TurnNumber && issued.TurnOwnerSeat == d.TurnOwnerSeat &&
            issued.OriginalTargetSeat == target && issued.CardUseFrameId > f.Id && issued.ActionId > 0 &&
            facts.OfType<InspectedHandSlashIssuedEvent>().Count(e => e.Return == issued) == 1;
        return !d.ContainsPrintedDodge && d.DiscardedCardId is { } id && d.ViewedCardIds.Contains(id) && d.SlashReturn is null &&
            d.SequenceBefore >= 0 && d.SequenceAfter == d.SequenceBefore + 1 &&
            _cardMovements.Count(m => m.Sequence == d.SequenceAfter && m.CardId == id && m.From == CardLocation.Hand(target) &&
                m.To == CardLocation.DiscardPile && m.Reason.Value == InspectedHandDiscardReason && m.TurnNumber == d.TurnNumber) == 1 &&
            facts.OfType<InspectedHandDiscardPaidEvent>().Count(e => e.ProgramFrameId == f.Id && e.TargetSeat == target &&
                e.SequenceBefore == d.SequenceBefore && e.SequenceAfter == d.SequenceAfter) == 1;
    }
    private bool CanIssueInspectedHandSlash(ProgramSkillFrame f)
    {
        var actor = f.OwnerSeat; var target = f.InspectedHandSlash!.TargetSeat;
        // The proved Dodge branch gives distance 1 before range validation. Like the mature
        // forced skill-Slash producer, this real Use neither requires nor debits finite Play quota.
        return _players[actor].IsAlive && _players[target].IsAlive && actor != target &&
            !HasTurnCardTargetRestriction(actor, SkillProgramCardTargetRestriction.SelfOnly) &&
            !IsCardUseForbidden(actor, CardKind.Slash, CardActionType.Use) &&
            !IsDirectedCardTargetProhibited(actor, target, CardKind.Slash) &&
            !IsCardTargetProhibited(_players[target], CardKind.Slash, Suit.None, null) &&
            !HasBeneficiarySuitShield(actor, target, Suit.None) && !IsSlashProhibited(_players[target]) &&
            CanSpendSlashUse(_players[actor], _players[target], ignoresCount: true);
    }
    private void IssueInspectedHandSlash(ProgramSkillFrame f)
    {
        var d = f.InspectedHandSlash!;
        if (!d.ContainsPrintedDodge || !ValidInspectedHandSlash(f)) throw new InvalidOperationException("The forced Slash lost its inspected Dodge branch.");
        if (!CanIssueInspectedHandSlash(f)) { FinishInspectedHandSlash(f, false); return; }
        if (ActiveCardAttack is not null || ActiveDuel is not null) throw new InvalidOperationException("An inspected Slash cannot overwrite a child attack.");
        var actor = _players[f.OwnerSeat]; var target = _players[d.TargetSeat]; var useId = ++_resolutionSequence;
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence,
            _resolutionStack.OfType<CardUseFrame>().LastOrDefault()?.Action?.ActionId, CardActionType.Use,
            actor.Seat, actor.Seat, null, null, null, CardKind.Slash, [target.Seat], [], [], effectiveSuit: Suit.None, effectiveRank: 0));
        var returned = new InspectedHandSlashReturn(f.Id, f.InstructionIndex, d.Source, d.GameplayHash, d.TurnNumber,
            d.TurnOwnerSeat, d.TargetSeat, useId, action.ActionId);
        ReplaceRuntimeTop(f with { InspectedHandSlash = d with { Stage = InspectedHandSlashStage.SlashIssued, SlashReturn = returned } });
        PushRuntimeFrame(new CardUseFrame(useId, actor.Seat, 0, CardKind.Slash, [target.Seat], PhysicalCardIds: [])
        { Action = action, InspectedHandSlashReturn = returned, FirstOwnPlayUseDistanceUnlimited = HasFirstActualPlayUseDistance(actor) });
        AdvanceEventRulesAndQueueFact(new InspectedHandSlashIssuedEvent(returned));
        var grant = _turnCardUseEffects.GrantInspectedHandDistance(f.Id, f.InstructionIndex, d.Source, d.GameplayHash,
            d.TurnNumber, d.TurnOwnerSeat, d.TargetSeat);
        AdvanceEventRulesAndQueueFact(new InspectedHandDistanceGrantedEvent(grant));
        if (TracksPlayCardHistory) AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(action));
        AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(useId, 0, CardKind.Slash, actor.Seat));
        AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(useId, [target.Seat]));
        RecordYingboCardUse(useId, actor.Seat, CardKind.Slash); RecordProgramUsedBasicCard(actor.Seat, CardKind.Slash);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, CardKind.Slash); RecordActualPlayPhaseUse(action);
        var attack = new CardAttackHandle(this, useId, actor.Seat, target.Seat, card: null,
            damageAmount: actor.HasAlcoholEffect ? 2 : 1, playedCardKind: CardKind.Slash,
            ignoresArmor: HasCardArmorBypass(actor, target, CardKind.Slash), programSkillCardUseFrameId: f.Id);
        CaptureProgramAlcoholConsumption(useId, actor); actor.HasAlcoholEffect = false; ActiveCardAttack = attack;
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, CardKind.Slash, actor.Seat, target.Seat));
        TryMarkProgramUseCommitted(useId);
        if (!TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardUseCommitted, action.TargetSeats,
                ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
    }
    private void CompleteInspectedHandSlash(AttackCompletionReceipt completion)
    {
        var returned = completion.InspectedHandSlashReturn ?? throw new InvalidOperationException("Missing inspected Slash return.");
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != returned.ProgramFrameId ||
            completion.ProgramFrameId != f.Id || completion.ResolutionId != returned.CardUseFrameId || !ValidInspectedHandSlash(f) ||
            f.InspectedHandSlash?.SlashReturn != returned || _resolutionStack.OfType<CardUseFrame>().Any(u => u.Id == returned.CardUseFrameId) ||
            !CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Any(e => e.ResolutionId == returned.CardUseFrameId))
            throw new InvalidOperationException("The inspected Slash lost its exact retired Use and once-paid program parent.");
        FinishInspectedHandSlash(f, true);
    }
    private RuleQueryEvaluation ApplyInspectedHandDistance(CharacterState actor, CharacterState target, RuleQueryEvaluation prior)
    {
        var grants = _turnCardUseEffects.InspectedHandDistances.Where(g => g.TurnNumber == _turnNumber &&
            g.TurnOwnerSeat == _currentSeat && g.Source.OwnerSeat == actor.Seat && g.TargetSeat == target.Seat).ToArray();
        return grants.Length == 0 ? prior : RuleQueryService.Evaluate(SkillRuleQuery.OutgoingDistance, new RuleQueryBounds(1, int.MaxValue),
            [new RuleQueryBaseTerm("distance:before-paid-hand-inspection", ConvertRuleValue(prior))],
            grants.Select(g => (RuleQueryContribution)new FiniteRuleQueryContribution($"paid-inspection:{g.ProgramFrameId}:{g.GrantSequence}", SkillRuleOperation.Set, 1)).ToArray());
    }
    private void AssertInspectedHandSlashes()
    {
        foreach (var f in _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.InspectedHandSlash is not null))
        {
            if (!ValidInspectedHandSlash(f)) throw new InvalidOperationException("Invalid inspected hand payment/branch receipt.");
            if (f.InspectedHandSlash!.Stage == InspectedHandSlashStage.Viewing && _resolutionStack.LastOrDefault()?.Id == f.Id &&
                (_pendingDecision is not { IsPrivate: true, Kind: DecisionKind.ProgramTrigger } p || p.PlayerSeat != f.OwnerSeat ||
                 !AssistedChoicesEqual(p.Choices, InspectedHandChoices(f)))) throw new InvalidOperationException("The whole-hand view lost its only authorized chooser.");
        }
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(u => u.InspectedHandSlashReturn is not null))
        {
            var r = use.InspectedHandSlashReturn!; var index = _resolutionStack.FindIndex(f => f.Id == use.Id);
            if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame root || !ValidInspectedHandSlash(root) ||
                root.InspectedHandSlash!.SlashReturn != r || use.Id != r.CardUseFrameId || use.CardId != 0 || use.PhysicalCardIds?.Count != 0 ||
                use.Action is not { Type: CardActionType.Use, EffectiveSuit: Suit.None, PhysicalCards.Count: 0 } action ||
                action.ActionId != r.ActionId || action.ProviderSeat != root.OwnerSeat ||
                !IsSlashCard(action.EffectiveKind) || action.EffectiveKind != use.CardKind ||
                use.CardKind != CardKind.Slash && !IsCurrentSlashFireChangedUse(use) ||
                !CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().Any(e => e.ResolutionId == use.Id && e.CardId == 0 &&
                    e.SourceSeat == root.OwnerSeat && e.CardKind == CardKind.Slash))
                throw new InvalidOperationException("An inspected Slash requires its own zero-entity issued origin.");
        }
        foreach (var g in _turnCardUseEffects.InspectedHandDistances)
            if (g.GrantSequence < 1 || g.ProgramFrameId < 1 || g.InstructionIndex != 1 || g.TurnNumber != _turnNumber ||
                g.TurnOwnerSeat != _currentSeat || g.Source.OwnerSeat != g.TurnOwnerSeat || !IsValidPlayerSeat(g.TargetSeat) ||
                g.TargetSeat == g.Source.OwnerSeat || _contentRegistry.GetSkill(g.Source.SkillId).Program is not { } p || p.GameplayHash != g.GameplayHash ||
                !CompleteProgramEventHistory().OfType<InspectedHandDistanceGrantedEvent>().Any(e => e.Grant == g) ||
                !CompleteProgramEventHistory().OfType<InspectedHandSlashIssuedEvent>().Any(e => e.Return.ProgramFrameId == g.ProgramFrameId &&
                    e.Return.Source == g.Source && e.Return.GameplayHash == g.GameplayHash && e.Return.TurnNumber == g.TurnNumber &&
                    e.Return.TurnOwnerSeat == g.TurnOwnerSeat && e.Return.OriginalTargetSeat == g.TargetSeat))
                throw new InvalidOperationException("An inspected-hand distance right lost its true actual-turn issuance.");
    }
}
