namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_CardAttackState
{
    internal static SuspendedDecisionState CaptureSuspendedDecision(PendingDecision prompt) =>
        new(prompt.Kind, prompt.PlayerSeat, prompt.Prompt, Array.AsReadOnly(prompt.ValidCardIds.ToArray()), Array.AsReadOnly(prompt.ValidTargetSeats.ToArray()),
            prompt.SourceSeat, prompt.IncomingCard, prompt.SkillPrompt, prompt.PromptId, prompt.Revision,
            prompt.RequiredCardCount, Array.AsReadOnly(prompt.Choices.ToArray()), prompt.IsPrivate, Array.AsReadOnly(prompt.ValidContentIds.ToArray()),
            prompt.TargetSeat, prompt.RequiredCardKind);

    internal static PendingDecision RestoreSuspendedDecision(SuspendedDecisionState state) =>
        new(state.Kind, state.PlayerSeat, state.Prompt, state.ValidCardIds, state.ValidTargetSeats,
            state.SourceSeat, state.IncomingCard)
        { SkillPrompt = state.SkillPrompt, PromptId = state.PromptId, Revision = state.Revision,
            RequiredCardCount = state.RequiredCardCount, Choices = state.Choices, IsPrivate = state.IsPrivate,
            ValidContentIds = state.ValidContentIds, TargetSeat = state.TargetSeat, RequiredCardKind = state.RequiredCardKind };
}
