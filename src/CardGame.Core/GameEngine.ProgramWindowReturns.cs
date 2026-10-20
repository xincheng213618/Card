namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void ReturnSurvivedDyingProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnSurvivedDyingProgramWindow(frame, completed);
    private void ReturnPindianCardsRevealedProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnPindianCardsRevealedProgramWindow(frame, completed);
    private void ReturnActualSlashTargetPenaltyProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnActualSlashTargetPenaltyProgramWindow(frame, completed);
    private void ReturnActualSlashTargetBenefitProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnActualSlashTargetBenefitProgramWindow(frame, completed);
    private void ReturnOtherActualUseTargetedProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnOtherActualUseTargetedProgramWindow(frame, completed);
    private void ReturnProgramTargetCommittedProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnProgramTargetCommittedProgramWindow(frame, completed);
    private void ReturnOtherActualTurnStartedProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnOtherActualTurnStartedProgramWindow(frame, completed);
    private void ReturnTurnBeforeStartProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnTurnBeforeStartProgramWindow(frame, completed);
    private void ReturnRoundStartingProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnRoundStartingProgramWindow(frame, completed);
    private void ReturnDrawPhaseEndedProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnDrawPhaseEndedProgramWindow(frame, completed);
    private void ReturnAfterNormalDrawProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnAfterNormalDrawProgramWindow(frame, completed);
    private void ReturnDrawPhaseStartingProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnDrawPhaseStartingProgramWindow(frame, completed);
    private void ReturnPlayEndingProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnPlayEndingProgramWindow(frame, completed);
    private void ReturnDiscardPhaseStartingProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnDiscardPhaseStartingProgramWindow(frame, completed);
    private void ReturnDiscardPhaseEndedProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnDiscardPhaseEndedProgramWindow(frame, completed);
    private void ReturnTurnEndingProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnTurnEndingProgramWindow(frame, completed);
    private void ReturnPlayPhaseStartingProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnPlayPhaseStartingProgramWindow(frame, completed);
    private void ReturnGameStartingProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnGameStartingProgramWindow(frame, completed);
    private void ReturnSelfDyingResponseProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnSelfDyingResponseProgramWindow(frame, completed);
    private void ReturnBeforeDamageAppliedProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnBeforeDamageAppliedProgramWindow(frame, completed);
    private void ReturnDamageAppliedBeforeDyingProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnDamageAppliedBeforeDyingProgramWindow(frame, completed);
    private void ReturnAfterHpLostProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnAfterHpLostProgramWindow(frame, completed);
    private void ReturnCardsGainedProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnCardsGainedProgramWindow(frame, completed);
    private void ReturnOwnerDiedProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnOwnerDiedProgramWindow(frame, completed);
    private void ReturnCharacterDiedProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnCharacterDiedProgramWindow(frame, completed);
    private void ReturnCardEffectBeforeApplyProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnCardEffectBeforeApplyProgramWindow(frame, completed);
    private void ReturnCardUseCommittedProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnCardUseCommittedProgramWindow(frame, completed);
    private void ReturnSlashTargetRedirectingProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnSlashTargetRedirectingProgramWindow(frame, completed);
    private void ReturnJudgmentFinalizedProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnJudgmentFinalizedProgramWindow(frame, completed);
    private void ReturnJudgmentReplacingProgramWindow(ProgramSkillFrame frame, bool completed) =>
        GetRuntimeModule<ProgramWindowReturnRuntime>().ReturnJudgmentReplacingProgramWindow(frame, completed);
}
