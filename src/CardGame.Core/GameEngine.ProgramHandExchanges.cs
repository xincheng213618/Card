namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome ExchangeProgramSelectedTargetHands(ProgramSkillFrame frame) =>
        ExchangeProgramSelectedTargetHands(frame, GetActiveProgramFrame(frame.Id).SelectedTargetSeats);

    private SkillProgramStepOutcome ExchangeProgramSelectedTargetHands(ProgramSkillFrame frame, IReadOnlyList<int> seats)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.SkillId != frame.SkillId || active.SkillInstanceId != frame.SkillInstanceId ||
            active.OwnerSeat != frame.OwnerSeat || active.GameplayHash != frame.GameplayHash ||
            seats.Count != 2 || seats[0] == seats[1] || !IsValidPlayerSeat(seats[0]) || !IsValidPlayerSeat(seats[1]))
            throw new InvalidOperationException("A hand exchange requires two distinct current program targets.");
        var first = _players[seats[0]];
        var second = _players[seats[1]];
        if (!IsValidPlayerSeat(first.Seat) || !IsValidPlayerSeat(second.Seat) ||
            !first.IsAlive || !second.IsAlive)
            throw new InvalidOperationException("A hand exchange requires two living program targets.");

        var firstHand = GetHand(first).OrderBy(card => card.Id).ToArray();
        var secondHand = GetHand(second).OrderBy(card => card.Id).ToArray();
        if (firstHand.Length == 0 && secondHand.Length == 0)
        { FinalizeDeferredHandExchange(frame.Id); return SkillProgramStepOutcome.Continue; }
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.exchange");
        ReplaceRuntimeTop(active with
        {
            PendingMovementContinuation = new ProgramMovementContinuation(frame.OwnerSeat, 0, null)
        });
        foreach (var card in firstHand)
        {
            MoveCard(card, CardLocation.Hand(first.Seat), CardLocation.Processing, reason);
            MoveCard(card, CardLocation.Processing, CardLocation.Hand(second.Seat), reason);
        }
        foreach (var card in secondHand)
        {
            MoveCard(card, CardLocation.Hand(second.Seat), CardLocation.Processing, reason);
            MoveCard(card, CardLocation.Processing, CardLocation.Hand(first.Seat), reason);
        }
        FinalizeDeferredHandExchange(frame.Id);
        if (!TryBeginCardsMovedProgramWindow())
            ReturnRuntimeProgramMovement(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
}
