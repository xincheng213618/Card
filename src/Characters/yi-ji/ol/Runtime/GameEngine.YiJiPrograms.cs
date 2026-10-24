namespace CardGame.Core;

public sealed partial class GameEngine
{
    // 机捷: hand the draw pile's bottom card to the selected participant.
    private SkillProgramStepOutcome BeginGiveDrawPileBottomCard(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.SelectedTargetSeats is not [var recipient] || !IsValidPlayerSeat(recipient) ||
            !_players[recipient].IsAlive)
            throw new InvalidOperationException("Jijie lost its recipient.");
        var drawPile = _cardZones.CardsAt(CardLocation.DrawPile);
        if (drawPile.Count == 0) return SkillProgramStepOutcome.Continue;
        var bottom = drawPile[0];
        var owner = _players[frame.OwnerSeat];
        AddLog("SkillEffect", $"{owner.Name} 观看牌堆底的一张牌并将其交给 {_players[recipient].Name}。",
            owner.Seat, recipient);
        ReplaceRuntimeTop(active with { PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        MoveCards([bottom], CardLocation.DrawPile, CardLocation.Hand(recipient), new CardMoveReason($"skill-program.{frame.SkillId}.jijie-give"), NativeGiveCompletion(frame.Id,frame.OwnerSeat,recipient,[bottom.Id],_movementSequence,null));
        if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    // A real Give can now open the recipient's Jiyuan before this paid instruction
    // returns. Its original cards-moved child already owns the complete invoice.
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 51000)]
    private bool IsYiJiBottomGiftMovement(ProgramSkillFrame frame, SkillProgramEffect? paidEffect,
        ProgramMovementContinuation pending) =>
        paidEffect?.Op == SkillProgramEffectOp.GiveDrawPileBottomCard &&
        frame.PendingMovementContinuation == pending && pending.SubjectSeat == frame.OwnerSeat &&
        pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        frame.SelectedTargetSeats is [var recipient] && IsValidPlayerSeat(recipient) &&
        _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(window =>
            window.ResumeProgramFrameId is null && window.Batch.ParentFrameId == frame.Id &&
            window.Batch.AwaitingProgramFrameId == frame.Id &&
            window.Batch.OriginSkillId == frame.SkillId && window.Batch.OriginSkillInstanceId == frame.SkillInstanceId &&
            window.Batch.OriginOwnerSeat == frame.OwnerSeat && window.Batch.Movements is [var movement] &&
            movement.From == CardLocation.DrawPile &&
            (movement.To == CardLocation.Hand(recipient) || movement.To == CardLocation.OutsideGame ||
                IsHappyWeiYanRedirectedGain(movement, CardLocation.Hand(recipient))) &&
            movement.Reason.Value == $"skill-program.{frame.SkillId}.jijie-give" &&
            _cardMovements.Contains(movement));

    [ProgramCapability]
    private sealed class YiJiProgramCapability(GameEngine engine) : IYiJiProgramHost
    {
        public SkillProgramStepOutcome GiveDrawPileBottomCard(ProgramSkillFrame frame) =>
            engine.BeginGiveDrawPileBottomCard(frame);
    }
}
