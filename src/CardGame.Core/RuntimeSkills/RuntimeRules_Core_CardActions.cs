namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_CardActions
{
    internal static ProgramTriggerCandidate ToSharedCandidate(ProgramCardTriggerCandidate candidate) =>
        new(candidate.OwnerSeat, candidate.SkillId, candidate.TriggerId, candidate.SkillInstanceId,
            candidate.GameplayHash, candidate.Priority);

    internal static SkillProgramTriggerWindow GetCardActionWindow(ProgramCardTriggerWindowFrame frame) =>
        frame.CaoChunDebtBeforeEffects == true ? SkillProgramTriggerWindow.CardUseBeforeTargetEffects :
        frame.CardSupplyCompletion is not null ? SkillProgramTriggerWindow.CardSupplyCompleted :
        frame.ResponseCompletion is not null ? SkillProgramTriggerWindow.CardResponseCompleted :
        frame.CompletedResponseReturn is { IsCommitted: true } ? SkillProgramTriggerWindow.CardUseCommitted :
        frame.CompletedResponseReturn is not null ||
        frame.Continuation is ProgramCardContinuation.CompletedSlash or ProgramCardContinuation.CompletedCard
            ? SkillProgramTriggerWindow.CardUseCompleted
            : frame.Continuation == ProgramCardContinuation.SlashTargetRedirecting
            ? SkillProgramTriggerWindow.SlashTargetRedirecting
            : frame.Continuation == ProgramCardContinuation.SlashBeforeResponse
            ? SkillProgramTriggerWindow.SlashBeforeResponse
            : frame.Continuation == ProgramCardContinuation.SlashFullyDodged
            ? SkillProgramTriggerWindow.SlashFullyDodged
            : frame.Continuation is ProgramCardContinuation.CommittedSlash or
                ProgramCardContinuation.CommittedTrick or ProgramCardContinuation.CommittedSimpleCard
            ? SkillProgramTriggerWindow.CardUseCommitted
            : frame.Continuation is ProgramCardContinuation.BeforeTargetEffects or
                ProgramCardContinuation.BeforeTrickTargetEffects or ProgramCardContinuation.BeforeSimpleTargetEffects
                ? SkillProgramTriggerWindow.CardUseBeforeTargetEffects
                : frame.Action.Type == CardActionType.Use
                    ? SkillProgramTriggerWindow.CardUseTargetsFinalized
                    : SkillProgramTriggerWindow.CardResponseAccepted;
}
