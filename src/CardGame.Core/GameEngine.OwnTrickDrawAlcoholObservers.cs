namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Completion normalization gives this already resolved, zero-entity Alcohol
    // an accepted action. Its direct HP return is distinct from queued recovery.
    private bool OwnTrickDrawLegacyAlcoholRide(ProgramSkillFrame root, ActualUseTargetWindowFrame parent,
        int dyingIndex, DyingFrame dying)
    {
        var rootIndex = _resolutionStack.FindIndex(frame => frame.Id == root.Id);
        if (rootIndex < 1 || rootIndex + 1 >= dyingIndex || dyingIndex + 3 >= _resolutionStack.Count ||
            _resolutionStack[rootIndex - 1] is not ActualUseTargetWindowFrame owningWindow || owningWindow.Id != parent.Id ||
            !OwnTrickDrawMatches(root, parent) || !OwnTrickDrawFirstChild(root, _resolutionStack[rootIndex + 1]) ||
            _resolutionStack[dyingIndex].Id != dying.Id || ActiveDying?.FrameId != dying.Id ||
            dying.Continuation != DyingContinuationKind.ProgramSkill || dying.KillerSeat is not null ||
            dying.ResponderIndex < 0 || dying.ResponderIndex >= dying.ResponderSeats.Count ||
            _resolutionStack[dyingIndex - 1] is not ProgramSkillFrame losing || losing.Id != dying.ParentFrameId ||
            _resolutionStack[dyingIndex + 1] is not ProgramSkillFrame program ||
            program.WindowContext is not { Window: SkillProgramTriggerWindow.SelfDyingResponse } context ||
            context.ParentFrameId != dying.Id || context.TargetSeat != dying.VictimSeat || context.SourceSeat is not null ||
            context.DamageFrameId is not null || context.OwnerSeat != program.OwnerSeat || context.OccurrenceIndex != 0 ||
            program.OwnerSeat != dying.VictimSeat || program.OwnerSeat != dying.ResponderSeat ||
            string.IsNullOrWhiteSpace(program.TriggerId) || string.IsNullOrWhiteSpace(program.SkillInstanceId) ||
            program.ActivationId != program.TriggerId || program.InstructionIndex < 1 ||
            _contentRegistry.Skills.GetValueOrDefault(program.SkillId)?.Program is not { } definition ||
            definition.GameplayHash != program.GameplayHash ||
            definition.Triggers.SingleOrDefault(trigger => trigger.Id == program.TriggerId)?.Window != context.Window)
            return false;
        for (var index = rootIndex + 1; index <= dyingIndex; index++)
            if (!HalfHandPaidDamageObserverEdge(index)) return false;

        var plan = ProgramInstructionResolver.Default.Resolve(program, definition);
        if (program.InstructionIndex > plan.Instructions.Count || plan.GetPausedInstruction(program.InstructionIndex).Effect is not
                { Op: SkillProgramEffectOp.UseVirtualDyingAlcohol, Target: SkillProgramEffectTarget.Owner } ||
            _resolutionStack[dyingIndex + 2] is not CardUseFrame
                { CardId: 0, CardKind: CardKind.Alcohol, PhysicalCardIds.Count: 0, Action: not null,
                    LegacyDyingAlcoholReturn: { } returned, Step: ResolutionFrameStep.ResolvingEffect } use ||
            use.SourceSeat != dying.VictimSeat || !use.TargetSeats.SequenceEqual([dying.VictimSeat]) ||
            use.DyingResponse is not null || use.VirtualBasicReturn is not null ||
            returned.ProgramFrameId != program.Id || returned.InstructionIndex != program.InstructionIndex ||
            returned.ProducerSource != new CardConversionSource(program.SkillId, GetProgramBindingId(program), program.OwnerSeat, program.SkillInstanceId) ||
            !IsExactLegacyActualUseCompletion(use) ||
            _resolutionStack[dyingIndex + 3] is not HpChangedTriggerWindowFrame hp || hp.Id != hp.Change.Id ||
            hp.ResumeFrameId != use.Id || hp.Change.ParentFrameId != use.Id || hp.Continuation != PostEventContinuation.CardUse ||
            hp.CardId != 0 || hp.CardKind != CardKind.Alcohol || hp.Change.Kind != HpChangeKind.Recovery ||
            hp.Change.SourceSeat != dying.VictimSeat || hp.Change.TargetSeat != dying.VictimSeat ||
            hp.Change.Amount != 1 || hp.Change.HpBefore > 0 || hp.Change.HpAfter != hp.Change.HpBefore + 1 ||
            hp.Candidates.Count == 0 || hp.Candidates.Count != hp.Contexts.Count ||
            hp.CandidateIndex < 0 || hp.CandidateIndex > hp.Candidates.Count) return false;

        var history = CompleteProgramEventHistory().ToArray();
        // A paid rescue keeps its accepted source identity even if a child later
        // disables that skill. Re-enumerating live eligibility would revoke it.
        if (history.OfType<ProgramBindingStartedEvent>().Count(fact => fact.FrameId == program.Id &&
                fact.SkillId == program.SkillId && fact.BindingId == program.TriggerId && fact.SkillInstanceId == program.SkillInstanceId &&
                fact.OwnerSeat == program.OwnerSeat && fact.Window == context.Window) != 1 ||
            history.OfType<PlayerDyingEvent>().Count(fact => fact.ResolutionId == dying.Id &&
                fact.VictimSeat == dying.VictimSeat && fact.KillerSeat is null) != 1 ||
            history.OfType<CardUseDeclaredEvent>().Count(fact => fact.ResolutionId == use.Id && fact.CardId == 0 &&
                fact.CardKind == CardKind.Alcohol && fact.SourceSeat == dying.VictimSeat) != 1 ||
            history.OfType<TargetsConfirmedEvent>().Count(fact => fact.ResolutionId == use.Id &&
                fact.TargetSeats.SequenceEqual([dying.VictimSeat])) != 1 ||
            history.OfType<ProgramDyingRescueEvent>().Count(fact => fact.DyingFrameId == dying.Id && fact.SkillId == program.SkillId &&
                fact.OwnerSeat == program.OwnerSeat && fact.VictimSeat == dying.VictimSeat && fact.CardId == 0 &&
                fact.RecoveredHp == 1 && fact.VictimHp == hp.Change.HpAfter) != 1) return false;

        for (var index = 0; index < hp.Candidates.Count; index++)
        {
            var candidate = hp.Candidates[index]; var hpContext = hp.Contexts[index];
            if (candidate.OwnerSeat != dying.VictimSeat || hpContext.OwnerSeat != candidate.OwnerSeat ||
                hpContext.ParentFrameId != hp.Id || hpContext.HpChange != hp.Change ||
                hpContext.SourceSeat != hp.Change.SourceSeat || hpContext.TargetSeat != hp.Change.TargetSeat ||
                hpContext.Amount != hp.Change.Amount || hpContext.OccurrenceIndex != candidate.OccurrenceIndex ||
                hpContext.Window is not (SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged) ||
                GetProgramTrigger(candidate).Window != hpContext.Window) return false;
        }
        if (dyingIndex + 4 == _resolutionStack.Count) return true;
        return dyingIndex + 5 == _resolutionStack.Count && hp.CandidateIndex < hp.Candidates.Count &&
            _resolutionStack[dyingIndex + 4] is ProgramSkillFrame observer &&
            MountObserverCandidateMatches(observer, hp.Candidates[hp.CandidateIndex]) &&
            observer.WindowContext == hp.Contexts[hp.CandidateIndex];
    }
}
