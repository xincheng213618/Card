namespace CardGame.Core;

public sealed partial class GameEngine
{

    // 解围: offer every field equipment card that fits the selected destination's free slots.
    private SkillProgramStepOutcome BeginMoveFieldEquipment(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.SelectedTargetSeats is not [var destination])
            throw new InvalidOperationException("Jiewei lost its destination participant.");
        var candidates = _players.Where(player => player.IsAlive).SelectMany(player => GetEquipment(player))
            .Where(card =>
            {
                var location = _cardZones.GetLocation(card.Id);
                return !card.IsGeneralWeapon && location.OwnerSeat is { } source && source != destination &&
                    CanEnterEquipmentSlot(destination, card);
            })
            .Select(card => card.Id).OrderBy(id => id).ToArray();
        if (candidates.Length == 0) return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(frame = active with
        { StrategicDraft = new(SkillProgramEffectOp.MoveFieldEquipment, destination, null, 1) { Candidates = candidates } });
        PublishStrategicPrompt(frame);
        return _pendingDecision is null ? SkillProgramStepOutcome.Continue : SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveMoveFieldEquipment(ProgramSkillFrame frame, int cardId)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.SelectedTargetSeats is not [var destination])
            throw new InvalidOperationException("Jiewei lost its destination participant.");
        var location = _cardZones.GetLocation(cardId);
        if (location.Zone != CardZoneKind.Equipment || location.OwnerSeat is not { } source ||
            source == destination || !_cardZones.CardsAt(location).Any(item => item.Id == cardId))
            throw new InvalidOperationException("The Jiewei equipment left the field.");
        var card = _cardZones.CardsAt(location).Single(item => item.Id == cardId);
        if (card.IsGeneralWeapon || !CanEnterEquipmentSlot(destination, card))
            throw new InvalidOperationException("The Jiewei destination no longer accepts the equipment.");
        var target = NormalizeEphemeralCardDestination(card, location, CardLocation.Equipment(destination));
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.jiewei-move");
        ReplaceRuntimeTop(active with { PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        var batch = BeginCardMovementBatch([location], [target]);
        var records = new List<CardMovementRecord>(1);
        var committed = false;
        try
        {
            _cardZones.Move(cardId, location, CardLocation.Processing);
            _cardZones.Move(cardId, CardLocation.Processing, target);
            records.Add(RecordMovement(card, location, target, reason));
            ResolveEquipmentSkillGrant(card, location, target);
            ResolveSilverLionRemoval(card, location, reason);
            committed = true;
        }
        finally
        {
            CompleteCardMovementBatch(batch, records, committed);
        }
        if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
    }
}
