namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ResponseUseCompletion
{
    internal static bool IsNullificationResponseProgramWindow(ProgramCardTriggerWindowFrame frame) =>
        frame.Continuation == ProgramCardContinuation.NullificationResponse ||
        frame.CompletedResponseReturn?.Kind == ProgramCompletedResponseKind.Nullification;

    // The existing response action remains a Response for accepted-response observers.
    // Only explicit opt-in completion observers see responses that the game calls uses.
    internal static bool IsCompletedResponseUse(CardActionContext action, ProgramCardContinuation continuation, bool authorizedForeignHand = false) =>
        action.Type == CardActionType.Response && (action.ActorSeat == action.ProviderSeat || authorizedForeignHand) && action.RequesterSeat is null &&
        action.ResponderSeat == action.ActorSeat &&
        (continuation == ProgramCardContinuation.NullificationResponse && action.EffectiveKind == CardKind.Nullification ||
         continuation == ProgramCardContinuation.Dodge && action.EffectiveKind == CardKind.Dodge);

    internal static bool IsCompletedResponseUse(CardActionContext action, ProgramCompletedResponseKind kind, bool authorizedForeignHand = false) =>
        action.Type == CardActionType.Response && (action.ActorSeat == action.ProviderSeat || authorizedForeignHand) && action.RequesterSeat is null &&
        action.ResponderSeat == action.ActorSeat &&
        (kind == ProgramCompletedResponseKind.Nullification && action.EffectiveKind == CardKind.Nullification ||
         kind == ProgramCompletedResponseKind.Dodge && action.EffectiveKind == CardKind.Dodge);
}
