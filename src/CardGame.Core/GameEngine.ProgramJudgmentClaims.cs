namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome ClaimProgramJudgmentCard(ProgramSkillFrame frame)
    {
        var context = frame.WindowContext;
        var judgment = context?.Judgment ??
            throw new InvalidOperationException("A judgment claim requires the finalized judgment context.");
        var pending = ActiveJudgment ??
            throw new InvalidOperationException("A judgment claim lost its parent judgment.");
        var active = GetActiveProgramFrame(frame.Id);
        if (context!.Window != SkillProgramTriggerWindow.JudgmentFinalized ||
            context.ParentFrameId != _resolutionStack.OfType<ProgramJudgmentTriggerWindowFrame>()
                .LastOrDefault()?.Id ||
            pending.Id != judgment.JudgmentFrameId ||
            pending.TargetSeat != frame.OwnerSeat ||
            judgment.SubjectSeat != frame.OwnerSeat ||
            GetJudgmentCard(pending) is not { } card || card.Id != judgment.CardId ||
            active.PendingMovementContinuation is not null ||
            !_players[frame.OwnerSeat].IsAlive)
            throw new InvalidOperationException("The judgment claim is no longer legal.");
        var from = CardLocation.Judgment(frame.OwnerSeat);
        if (_cardZones.GetLocation(card.Id) != from)
            throw new InvalidOperationException("The final judgment card has already left its judgment zone.");

        ReplaceRuntimeTop(active with
        {
            PendingMovementContinuation = new ProgramMovementContinuation(frame.OwnerSeat, 0, null)
        });
        MoveCard(card, from, CardLocation.Hand(frame.OwnerSeat),
            new CardMoveReason($"skill-program.{frame.SkillId}.claimJudgmentCard"));
        AdvanceEventRulesAndQueueFact(new ProgramJudgmentCardClaimedEvent(
            pending.Id, frame.SkillId, frame.OwnerSeat, card.Id, card.Kind));
        if (!TryBeginCardsMovedProgramWindow())
            ReturnRuntimeProgramMovement(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
}
