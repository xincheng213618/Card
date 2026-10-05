namespace CardGame.Core;

public sealed partial class GameEngine
{
    // 掳掠 branch one: the chosen counterpart hands their entire hand to the owner.
    private SkillProgramStepOutcome GiveProgramSelectedTargetHand(ProgramSkillFrame frame, int targetSeat)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.SelectedTargetSeats is not [var selected] || selected != targetSeat)
            throw new InvalidOperationException("LueLve lost its selected counterpart.");
        var location = CardLocation.Hand(targetSeat);
        var cards = _cardZones.CardsAt(location);
        if (cards.Count == 0) return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(active with { PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        MoveCards(cards.ToArray(), location, CardLocation.Hand(frame.OwnerSeat),
            new CardMoveReason($"skill-program.{frame.SkillId}.luelve-gift"));
        if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private sealed partial class ProgramSkillHost : ILiangXingProgramHost
    {
        public SkillProgramStepOutcome GiveSelectedTargetHand(ProgramSkillFrame frame, int targetSeat) =>
            engine.GiveProgramSelectedTargetHand(frame, targetSeat);
    }
}
