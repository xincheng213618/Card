namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TryBeginCommittedResponseUsePrograms(CardAttackHandle? attack, CardActionContext action, ProgramCardContinuation continuation)
    {
        if (_winner != Winner.None || !IsCompletedResponseUse(action, continuation) ||
            continuation == ProgramCardContinuation.Dodge &&
            (attack is null || attack.EffectiveCardKind is not { } incoming || !IsSlashCard(incoming) || attack.TargetSeat != action.ActorSeat)) return false;
        if (!_players.Any(owner => owner.IsAlive &&
            (GetSkillBindingShard(owner)?.GetInstanceTriggers(SkillProgramTriggerWindow.CardUseCommitted) ?? [])
                .Any(binding => binding.Trigger.IncludeResponseUses))) return false;
        return TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardUseCommitted, [],
            continuation: null, completedResponseReturn: new(continuation == ProgramCardContinuation.Dodge ? ProgramCompletedResponseKind.Dodge : ProgramCompletedResponseKind.Nullification, IsCommitted: true));
    }

    // Return through the original accepted-response and completion observers;
    // this new opt-in window never pays the physical response entity twice.
    private void ContinueCommittedResponseUse(CardAttackHandle? attack, ProgramCardTriggerWindowFrame frame)
    {
        var typed = frame.CompletedResponseReturn ??
            throw new InvalidOperationException("Committed response lost typed continuation.");
        var continuation = typed.Kind == ProgramCompletedResponseKind.Dodge ? ProgramCardContinuation.Dodge : ProgramCardContinuation.NullificationResponse;
        if (!typed.IsCommitted || !IsCompletedResponseUse(frame.Action, continuation))
            throw new InvalidOperationException("Committed response actor or kind changed.");
        if (continuation == ProgramCardContinuation.NullificationResponse)
        {
            if (TryBeginProgramCardWindow(null, frame.Action, SkillProgramTriggerWindow.CardResponseAccepted, [], continuation)) return;
            ContinueNullificationAfterResponseUse(frame.Action);
        }
        else
        {
            if (attack is null) throw new InvalidOperationException("Committed defense lost its Slash.");
            if (TryBeginProgramCardWindow(attack, frame.Action, SkillProgramTriggerWindow.CardResponseAccepted,
                [frame.Action.OpponentSeat!.Value], continuation)) return;
            ContinueAcceptedCardResponse(attack, frame.Action, continuation);
        }
    }
}
