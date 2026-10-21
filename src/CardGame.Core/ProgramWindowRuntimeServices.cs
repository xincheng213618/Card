namespace CardGame.Core;

internal interface IProgramWindowCursors : IProgramRuntimeOperations
{
    void AdvanceProgramLifecycleCursor(ProgramLifecycleTriggerWindowFrame frame);
    void AdvancePlayPhaseStartingCursor(PlayPhaseStartingBoundaryFrame frame);
    void AdvanceTurnEndingBoundaryCursor(TurnEndingBoundaryFrame frame);
    void AdvanceCardsMovedProgramCursor(CardsMovedTriggerWindowFrame frame);
    void AdvanceHpChangedProgramCursor(HpChangedTriggerWindowFrame frame);
    void AdvanceOwnerDiedProgramCursor(ProgramDeathTriggerWindowFrame frame);
    void AdvanceBeforeDamageProgramCandidate(BeforeDamageProgramWindowFrame frame, bool activated, bool completed);
    void AdvanceProgramCardCandidate(ProgramCardTriggerWindowFrame frame);
    void AdvanceProgramJudgmentCandidate(ProgramJudgmentTriggerWindowFrame frame);
}

internal interface IProgramWindowReturnQueries : IProgramSelectionQuery
{
    TurnPhase Phase { get; }
    DamageTriggerWindowFrame? ActiveDamageTrigger { get; }
    bool IsCurrentYanPuDyingExitEdge();
    bool IsWushuangDyingExitReturn(ProgramSkillFrame frame);
    bool MountObserverCandidateMatches(ProgramSkillFrame frame, ProgramTriggerCandidate candidate);
    SkillProgramTrigger GetProgramTrigger(ProgramSkillFrame frame);
    bool ValidBeiMiHuPreludeLifecycleWindow(ProgramLifecycleTriggerWindowFrame frame);
    bool IsMingchaNormalDrawReplacement(ProgramSkillFrame frame);
    bool IsPhaseHandDebtResolver(ProgramTriggerCandidate candidate);
}

// These are returns into other native mechanisms. Parent selection and cursor
// ownership stay in ProgramWindowReturnRuntime, rather than in these adapters.
internal interface IProgramWindowNativeReturns : IProgramRuntimeOperations
{
    void CompletePindianRevealBinding(ProgramSkillFrame frame);
    void CompleteSlashTargetPenaltyBinding(ProgramSkillFrame frame);
    void CompleteSlashTargetBenefitBinding(ProgramSkillFrame frame);
    void CompleteActualUseTargetBinding(ProgramSkillFrame frame, bool completed);
    void CompleteForeignActualTurnStartBinding(ProgramSkillFrame frame);
    void CompleteDyingProgramBinding(ProgramSkillFrame frame, bool completed);
    void AdvanceDamageTriggerCandidate(DamageTriggerWindowFrame frame);
    void CompleteCardEffectCandidate(ProgramSkillWindowContext context);
    void RestoreCompletedUseAttackAfterIssuedChild(ProgramSkillFrame frame, ProgramCardTriggerWindowFrame parent);
    void CompleteProgramJudgmentReplacementBinding(ProgramSkillFrame frame, bool completed);
}

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class ProgramWindowReturnQueries(GameEngine engine) : IProgramWindowReturnQueries
    {
        public TurnPhase Phase => engine._phase;
        public DamageTriggerWindowFrame? ActiveDamageTrigger => engine.ActiveDamageTrigger;
        public bool IsCurrentYanPuDyingExitEdge() => engine.IsYanPuDyingExitEdge(engine._resolutionStack.Count - 1);
        public bool IsWushuangDyingExitReturn(ProgramSkillFrame frame) => engine.IsWushuangDyingExitReturn(frame);
        public bool MountObserverCandidateMatches(ProgramSkillFrame frame, ProgramTriggerCandidate candidate) =>
            RuntimeSkills.RuntimeRules_Core_DamageTargetMountObserver.MountObserverCandidateMatches(frame, candidate);
        public SkillProgramTrigger GetProgramTrigger(ProgramSkillFrame frame) => engine.GetProgramTrigger(frame);
        public bool ValidBeiMiHuPreludeLifecycleWindow(ProgramLifecycleTriggerWindowFrame frame) =>
            engine.ValidBeiMiHuPreludeLifecycleWindow(frame);
        public bool IsMingchaNormalDrawReplacement(ProgramSkillFrame frame) => engine.IsMingchaNormalDrawReplacement(frame);
        public bool IsPhaseHandDebtResolver(ProgramTriggerCandidate candidate) => engine.IsPhaseHandDebtResolver(candidate);
    }

    [ProgramCapability]
    private sealed class ProgramWindowNativeReturns(GameEngine engine) : IProgramWindowNativeReturns
    {
        public void CompletePindianRevealBinding(ProgramSkillFrame frame) => engine.CompletePindianRevealBinding(frame);
        public void CompleteSlashTargetPenaltyBinding(ProgramSkillFrame frame) => engine.CompleteSlashTargetPenaltyBinding(frame);
        public void CompleteSlashTargetBenefitBinding(ProgramSkillFrame frame) => engine.CompleteSlashTargetBenefitBinding(frame);
        public void CompleteActualUseTargetBinding(ProgramSkillFrame frame, bool completed) => engine.CompleteActualUseTargetBinding(frame, completed);
        public void CompleteForeignActualTurnStartBinding(ProgramSkillFrame frame) => engine.CompleteForeignActualTurnStartBinding(frame);
        public void CompleteDyingProgramBinding(ProgramSkillFrame frame, bool completed) => engine.CompleteDyingProgramBinding(frame, completed);
        public void AdvanceDamageTriggerCandidate(DamageTriggerWindowFrame frame) => engine.AdvanceDamageTriggerCandidate(frame);
        public void CompleteCardEffectCandidate(ProgramSkillWindowContext context) => engine.CompleteCardEffectCandidate(context);
        public void RestoreCompletedUseAttackAfterIssuedChild(ProgramSkillFrame frame, ProgramCardTriggerWindowFrame parent) =>
            engine.RestoreCompletedUseAttackAfterIssuedChild(frame, parent);
        public void CompleteProgramJudgmentReplacementBinding(ProgramSkillFrame frame, bool completed) =>
            engine.CompleteProgramJudgmentReplacementBinding(frame, completed);
    }
}
