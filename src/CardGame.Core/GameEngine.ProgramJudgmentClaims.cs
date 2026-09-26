namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome ClaimProgramJudgmentCard(ProgramSkillFrame frame)
    {
        var context = frame.WindowContext;
        var judgment = context?.Judgment ??
            throw new InvalidOperationException("A judgment claim requires the finalized judgment context.");
        var pending = _pendingJudgment ??
            throw new InvalidOperationException("A judgment claim lost its parent judgment.");
        var active = GetActiveProgramFrame(frame.Id);
        if (context!.Window != SkillProgramTriggerWindow.JudgmentFinalized ||
            context.ParentFrameId != _resolutionStack.OfType<ProgramJudgmentTriggerWindowFrame>()
                .LastOrDefault()?.Id ||
            pending.FrameId != judgment.JudgmentFrameId ||
            pending.TargetSeat != frame.OwnerSeat ||
            judgment.SubjectSeat != frame.OwnerSeat ||
            pending.CurrentCard?.Id != judgment.CardId ||
            active.PendingMovementContinuation is not null ||
            !_players[frame.OwnerSeat].IsAlive)
            throw new InvalidOperationException("The judgment claim is no longer legal.");
        var card = pending.CurrentCard;
        var from = CardLocation.Judgment(frame.OwnerSeat);
        if (_cardZones.GetLocation(card.Id) != from)
            throw new InvalidOperationException("The final judgment card has already left its judgment zone.");

        _resolutionStack[^1] = active with
        {
            PendingMovementContinuation = new ProgramMovementContinuation(frame.OwnerSeat, 0, null)
        };
        MoveCard(card, from, CardLocation.Hand(frame.OwnerSeat),
            new CardMoveReason($"skill-program.{frame.SkillId}.claimJudgmentCard"));
        QueueGameEvent(new ProgramJudgmentCardClaimedEvent(
            pending.FrameId, frame.SkillId, frame.OwnerSeat, card.Id, card.Kind));
        if (!TryBeginCardsMovedProgramWindow())
            CompleteAwaitedProgramMovement(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
}
