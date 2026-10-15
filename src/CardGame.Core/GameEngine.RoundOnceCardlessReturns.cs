namespace CardGame.Core;

public sealed record RoundOnceCardlessRequestedOwnerDamagedEvent(
    long ProgramFrameId, long NativeUseId, long NativeActionId, int ActualSourceSeat, int TargetSeat, int Amount) : IGameEvent;

public sealed partial class GameEngine
{
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 120)]
    private void ObserveRoundOnceCardlessRequestedOwnerDamage(DamageAppliedEvent damage)
    {
        if (damage.Amount <= 0 || damage.SourceLess || CurrentDamageAttempt is not { } attack ||
            LifecycleCardUse(attack.ResolutionId) is not { RoundOnceCardlessProgramRequest: { } receipt } use ||
            _resolutionStack.SingleOrDefault(frame => frame.Id == receipt.ProgramFrameId) is not ProgramSkillFrame parent ||
            receipt.RequestOp != SkillProgramEffectOp.RequestSlashByTarget || damage.TargetSeat != parent.OwnerSeat ||
            damage.SourceSeat != attack.SourceSeat || damage.TargetSeat != attack.TargetSeat ||
            !RoundOnceCardlessProgramRequestRidesOn(use, parent)) return;
        AdvanceEventRulesAndQueueFact(new RoundOnceCardlessRequestedOwnerDamagedEvent(parent.Id, use.Id,
            receipt.NativeActionId, damage.SourceSeat, damage.TargetSeat, damage.Amount));
    }

    private bool RoundOnceCardlessRequestedOwnerWasDamaged(ProgramSkillFrame parent, AttackCompletionReceipt completion)
    {
        if (parent.RoundOnceCardlessProgramRequest is not { NativeReturned: true, Receipt: { } receipt } ||
            receipt.NativeUseId != completion.ResolutionId || receipt.RequestOp != SkillProgramEffectOp.RequestSlashByTarget ||
            !CanContinueIssuedRoundOnceCardlessProgramRequest(parent)) return false;
        return CompleteProgramEventHistory().OfType<RoundOnceCardlessRequestedOwnerDamagedEvent>().Any(fact =>
            fact.ProgramFrameId == parent.Id && fact.NativeUseId == receipt.NativeUseId &&
            fact.NativeActionId == receipt.NativeActionId && fact.TargetSeat == parent.OwnerSeat && fact.Amount > 0);
    }

    private bool IsRoundOnceCardlessProgramRequestAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing)
    {
        if (attack.Card is not null || attack.PhysicalCards.Count != 0 ||
            LifecycleCardUse(attack.ResolutionId) is not { RoundOnceCardlessProgramRequest: { } receipt } use ||
            _resolutionStack.SingleOrDefault(frame => frame.Id == receipt.ProgramFrameId) is not ProgramSkillFrame parent ||
            !RoundOnceCardlessProgramRequestRidesOn(use, parent) || attack.EffectiveCardKind != use.CardKind ||
            attack.SourceSeat != use.SourceSeat || attack.ProgramSkillCardUseFrameId != parent.Id) return false;
        var parentIndex = _resolutionStack.FindIndex(frame => frame.Id == parent.Id);
        var outerCards = _resolutionStack.Take(parentIndex).OfType<CardUseFrame>().SelectMany(frame => frame.PhysicalCardIds ?? [])
            .Where(id => _cardZones.GetLocation(id) == CardLocation.Processing).Distinct().Order().ToArray();
        return processing.Select(card => card.Id).Order().SequenceEqual(outerCards);
    }

    private bool FinishRoundOnceCardlessAttack(CardAttackHandle attack, out bool paused)
    {
        paused = false;
        if (attack.Card is not null || !IsRoundOnceCardlessBasicUse(attack.ResolutionId) ||
            attack.EffectiveCardKind is not { } kind || !IsSlashCard(kind)) return false;
        var use = LifecycleCardUse(attack.ResolutionId)!;
        SetCardUseStep(use.Id, ResolutionFrameStep.Completed);
        AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, 0, use.CardKind));
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!, SkillProgramTriggerWindow.CardUseCompleted,
                use.TargetSeats, ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(use.Id); CompleteFinishedAttackCardUse(attack); }
        return true;
    }

    private bool TryResolveRoundOnceCardlessAiDodge()
    {
        if (_pendingDecision is not { Kind: DecisionKind.RespondDodge } prompt || _players[prompt.PlayerSeat].IsHuman) return false;
        var choice = RoundOnceCardlessResponseChoices(_players[prompt.PlayerSeat], CardKind.Dodge).FirstOrDefault();
        if (choice is null || !prompt.Choices.Any(candidate => candidate.Id == choice.Id) || ActiveCardAttack is not { } attack) return false;
        var (use, _, thought) = _aiBrains[prompt.PlayerSeat].ChooseDodgeResponse(CreateSnapshot(prompt.PlayerSeat), attack.SourceSeat,
            ++_thoughtSequence, attack.IgnoresArmor, true);
        AddThought(thought);
        if (!use) return false;
        return TryResolveRoundOnceCardlessResponse(choice, false);
    }
}
