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
        _resolutionStack[^1] = active with
        {
            PendingMovementContinuation = new ProgramMovementContinuation(targetSeat, 0, null)
        };
        QueueGameEvent(new ProgramCardsRevealedEvent(frame.Id, frame.SkillId,
            GetProgramBindingId(frame), frame.OwnerSeat, resultBind,
            Array.AsReadOnly(new[] { snapshot })));
        MoveCard(card, CardLocation.Hand(frame.OwnerSeat), CardLocation.Processing, reason);
        MoveCard(card, CardLocation.Processing, CardLocation.Hand(targetSeat), reason);
        SetProgramCardSet(frame.Id, resultBind, [card.Id], SkillProgramCardSetVisibility.Public,
            [CardLocation.Hand(targetSeat)], card.Suit);
        if (!TryBeginCardsMovedProgramWindow())
            CompleteAwaitedProgramMovement(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    /// <summary>
    /// Takes up to the configured number of random cards from one bound participant's
    /// declared areas into the owner's hand. Only the source seat and the taken count reach
    /// the public event stream; hand cards stay opaque and equipment cards were visible.
    /// All taken cards cross in one atomic movement batch that keeps the participant as the
    /// movement source, so one-time-gain triggers observe a single multi-card gain.
    /// </summary>
    private SkillProgramStepOutcome TakeProgramRandomCardsFromParticipant(
        ProgramSkillFrame frame,
        ProgramParticipantReference participantReference,
        int amount,
        IReadOnlyList<CardZoneKind> zones,
        CardMoveReason reason)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var sourceSeat = ResolveProgramParticipant(active, participantReference);
        if (active.SkillId != frame.SkillId || active.SkillInstanceId != frame.SkillInstanceId ||
            !IsValidPlayerSeat(sourceSeat) || sourceSeat == frame.OwnerSeat ||
            !_players[frame.OwnerSeat].IsAlive || !_players[sourceSeat].IsAlive ||
            zones.Count == 0 ||
            zones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)))
            throw new InvalidOperationException(
                "A participant take requires a living distinct bound participant and declared areas.");

        var picked = new List<(Card Card, CardLocation Location)>();
        for (var takenCount = 0; takenCount < amount; takenCount++)
        {
            var candidates = zones
                .SelectMany(zone => zone switch
                {
                    CardZoneKind.Hand => GetHand(_players[sourceSeat]).Select(card =>
                        (Card: card, Location: CardLocation.Hand(sourceSeat))),
                    CardZoneKind.Equipment => GetEquipment(_players[sourceSeat]).Select(card =>
                        (Card: card, Location: CardLocation.Equipment(sourceSeat))),
                    _ => throw new InvalidOperationException($"Unsupported take area '{zone}'.")
                })
                .Where(entry => !picked.Any(previous => previous.Card.Id == entry.Card.Id))
                .OrderBy(entry => entry.Card.Id)
                .ToArray();
            if (candidates.Length == 0) break;
            var pick = candidates[_random.Next(candidates.Length)];
            picked.Add(pick);
        }

        if (picked.Count > 0)
        {
            var destination = CardLocation.Hand(frame.OwnerSeat);
            var batch = BeginCardMovementBatch(
                picked.Select(entry => entry.Location).Distinct(), [destination]);
            var movements = new List<CardMovementRecord>(picked.Count);
            var committed = false;
            try
            {
                foreach (var entry in picked)
                {
                    _cardZones.Move(entry.Card.Id, entry.Location, destination);
                    movements.Add(RecordMovement(entry.Card, entry.Location, destination, reason));
                    ResolveEquipmentSkillGrant(entry.Card, entry.Location, destination);
                    ClearJudgmentEffectiveKindAfterMove(entry.Card, entry.Location, destination);
                    ResolveSilverLionRemoval(entry.Card, entry.Location, reason);
                    ResolveWoodenOxMove(entry.Card, entry.Location, destination);
                    CollectDiscardPhaseHandDiscard(entry.Card, entry.Location, destination);
                }
                committed = true;
            }
            finally
            {
                CompleteCardMovementBatch(batch, movements, committed);
            }
        }

        QueueGameEvent(new ProgramRandomCardsTakenFromParticipantEvent(
            frame.Id,
            frame.SkillId,
            GetProgramBindingId(frame),
            frame.OwnerSeat,
            sourceSeat,
            Array.AsReadOnly(zones.ToArray()),
            picked.Count));
        AddLog("SkillEffect", picked.Count == 0
            ? $"{_players[frame.OwnerSeat].Name} 未从 {_players[sourceSeat].Name} 处获得牌。"
            : $"{_players[frame.OwnerSeat].Name} 从 {_players[sourceSeat].Name} 处随机获得 {picked.Count} 张牌。",
            frame.OwnerSeat, sourceSeat);
        return SkillProgramStepOutcome.Continue;
    }
}
