namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool DoesProgramFrozenSuitMatchChoice(
        ProgramSkillFrame frame, string cardBind, string choiceBind)
    {
        var card = GetProgramCardSet(frame, cardBind);
        var choice = frame.ChoiceBindings.Single(item => item.Name == choiceBind);
        if (card.Visibility != SkillProgramCardSetVisibility.Public ||
            card.CardIds.Count != 1 || card.FrozenRevealedSuit is not { } suit)
            throw new InvalidOperationException("A suit comparison requires a public frozen card suit.");
        return string.Equals(char.ToLowerInvariant(suit.ToString()[0]) + suit.ToString()[1..],
            choice.OptionId, StringComparison.Ordinal);
    }

    private SkillProgramStepOutcome TransferProgramRandomOwnedCard(
        ProgramSkillFrame frame, int targetSeat, string resultBind)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.TriggerId is not null || active.SkillId != frame.SkillId ||
            active.SkillInstanceId != frame.SkillInstanceId ||
            active.SelectedTargetSeats.Count != 1 ||
            active.SelectedTargetSeats[0] != targetSeat ||
            active.CardSetBindings.Any(binding => binding.Name == resultBind) ||
            !IsValidPlayerSeat(targetSeat) || targetSeat == frame.OwnerSeat ||
            !_players[frame.OwnerSeat].IsAlive || !_players[targetSeat].IsAlive)
            throw new InvalidOperationException("A random card transfer requires one current living program target.");

        var hand = GetHand(_players[frame.OwnerSeat]).OrderBy(card => card.Id).ToArray();
        if (hand.Length == 0)
            throw new InvalidOperationException("A random card transfer requires an owner hand card.");
        var card = hand[_random.Next(hand.Length)];
        var snapshot = ToSnapshot(card);
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.transferRandomOwnedCard");
        ReplaceRuntimeTop(active with
        {
            PendingMovementContinuation = new ProgramMovementContinuation(targetSeat, 0, null)
        });
        AdvanceEventRulesAndQueueFact(new ProgramCardsRevealedEvent(frame.Id, frame.SkillId,
            GetProgramBindingId(frame), frame.OwnerSeat, resultBind,
            Array.AsReadOnly(new[] { snapshot })));
        MoveCard(card, CardLocation.Hand(frame.OwnerSeat), CardLocation.Processing, reason);
        MoveCard(card, CardLocation.Processing, CardLocation.Hand(targetSeat), reason);
        SetProgramCardSet(frame.Id, resultBind, [card.Id], SkillProgramCardSetVisibility.Public,
            [CardLocation.Hand(targetSeat)], card.Suit);
        if (!TryBeginCardsMovedProgramWindow())
            ReturnRuntimeProgramMovement(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
}
