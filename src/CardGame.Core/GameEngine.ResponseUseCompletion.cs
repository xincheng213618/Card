namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool IsNullificationResponseProgramWindow(ProgramCardTriggerWindowFrame frame) =>
        frame.Continuation == ProgramCardContinuation.NullificationResponse ||
        frame.Continuation == ProgramCardContinuation.CompletedResponse &&
        frame.CompletedResponseContinuation == ProgramCardContinuation.NullificationResponse;

    // The existing response action remains a Response for accepted-response observers.
    // Only explicit opt-in completion observers see responses that the game calls uses.
    private static bool IsCompletedResponseUse(CardActionContext action, ProgramCardContinuation continuation) =>
        action.Type == CardActionType.Response && action.ActorSeat == action.ProviderSeat && action.RequesterSeat is null &&
        (continuation == ProgramCardContinuation.NullificationResponse && action.EffectiveKind == CardKind.Nullification ||
         continuation == ProgramCardContinuation.Dodge && action.EffectiveKind == CardKind.Dodge);

    private bool HasResponseUseCompletionObserver(CardActionContext action, ProgramCardContinuation continuation) =>
        IsCompletedResponseUse(action, continuation) && _players.Any(owner => owner.IsAlive &&
            (GetSkillBindingShard(owner)?.GetInstanceTriggers(SkillProgramTriggerWindow.CardUseCompleted) ?? [])
                .Any(binding => binding.Trigger.IncludeResponseUses));

    private bool TryBeginCompletedResponseUsePrograms(AttackResolution? attack, CardActionContext action,
        ProgramCardContinuation continuation)
    {
        if (_winner != Winner.None || !HasResponseUseCompletionObserver(action, continuation)) return false;
        // Arrow Barrage Dodge and supplied faction cards are response-only. A Dodge use
        // must be the responder's own defense against the actual Slash parent.
        if (continuation == ProgramCardContinuation.Dodge &&
            (attack is null || attack.EffectiveCardKind is not { } incomingKind || !IsSlashCard(incomingKind) || attack.TargetSeat != action.ActorSeat)) return false;
        return TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardUseCompleted, [],
            ProgramCardContinuation.CompletedResponse, completedResponseContinuation: continuation);
    }

    private void ContinueNullificationAfterResponseUse(CardActionContext action)
    {
        if (!TryBeginCompletedResponseUsePrograms(null, action, ProgramCardContinuation.NullificationResponse))
            ContinueNullificationWindow(_pendingNullification ??
                throw new InvalidOperationException("The completed counterspell lost its original chain."));
    }

    private void ContinueCompletedResponseUse(AttackResolution? attack, ProgramCardTriggerWindowFrame frame)
    {
        var continuation = frame.CompletedResponseContinuation ??
            throw new InvalidOperationException("The completed response use lost its typed continuation.");
        if (!IsCompletedResponseUse(frame.Action, continuation))
            throw new InvalidOperationException("A completed response use changed its actor or card kind.");
        if (continuation == ProgramCardContinuation.NullificationResponse)
            ContinueNullificationWindow(_pendingNullification ??
                throw new InvalidOperationException("The completed counterspell lost its original chain."));
        else ContinueFinishedCardResponse(attack ??
            throw new InvalidOperationException("The completed Dodge lost its original Slash."), frame.Action, continuation);
    }
}
