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
        MoveCards([bottom], CardLocation.DrawPile, CardLocation.Hand(recipient),
            new CardMoveReason($"skill-program.{frame.SkillId}.jijie-give"));
        if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private sealed partial class ProgramSkillHost : IYiJiProgramHost
    {
        public SkillProgramStepOutcome GiveDrawPileBottomCard(ProgramSkillFrame frame) =>
            engine.BeginGiveDrawPileBottomCard(frame);
    }
}
