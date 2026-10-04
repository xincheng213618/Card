namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsProgramEquipmentExchangePair(int ownerSeat, int firstSeat, int secondSeat)
    {
        if (!IsValidPlayerSeat(firstSeat) || !IsValidPlayerSeat(secondSeat) || firstSeat == secondSeat ||
            !_players[firstSeat].IsAlive || !_players[secondSeat].IsAlive) return false;
        var firstCount = GetEquipment(_players[firstSeat]).Count;
        var secondCount = GetEquipment(_players[secondSeat]).Count;
        return firstCount + secondCount > 0 && Math.Abs(firstCount - secondCount) <=
            Math.Max(0, _players[ownerSeat].MaxHp - _players[ownerSeat].Hp);
    }

    private SkillProgramStepOutcome ExchangeProgramSelectedTargetEquipment(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.EquipmentPairPayment is not null)
        {
            if (!CanResumePaidEquipmentPair(active))
            {
                CancelProgramBindingAndCleanup(active, "足额付款已完成，但原来源或交换对象已失效；保留真实成本。");
                return SkillProgramStepOutcome.AwaitChild;
            }
            active = FreezePaidEquipmentExchangeStart(active);
            if (active.SelectedTargetSeats.All(seat => GetEquipment(_players[seat]).Count == 0))
                return SkillProgramStepOutcome.Continue;
        }
        else if (active.SelectedTargetSeats.Count != 2 ||
            !IsProgramEquipmentExchangePair(active.OwnerSeat, active.SelectedTargetSeats[0], active.SelectedTargetSeats[1]))
            throw new InvalidOperationException("Equipment exchange requires a current legal equipment pair.");
        var first = active.SelectedTargetSeats[0];
        var second = active.SelectedTargetSeats[1];
        var incoming = GetEquipment(_players[first]).OrderBy(card => card.Id)
            .Select(card => (Card: card, Source: first, Recipient: second))
            .Concat(GetEquipment(_players[second]).OrderBy(card => card.Id)
                .Select(card => (Card: card, Source: second, Recipient: first))).ToArray();
        var occupied = new Dictionary<(int Seat, EquipmentSlot Slot), int>();
        var equipmentMoves = incoming.Select(item =>
        {
            var slot = EquipmentCatalog.Get(item.Card.Kind).Slot;
            var key = (item.Recipient, slot);
            var count = occupied.GetValueOrDefault(key);
            var destination = item.Card.IsGeneralWeapon ? CardLocation.OutsideGame :
                count < _players[item.Recipient].EquipmentSlotCapacity(slot)
                    ? CardLocation.Equipment(item.Recipient) : CardLocation.DiscardPile;
            if (destination.Zone == CardZoneKind.Equipment) occupied[key] = count + 1;
            return (item.Card, From: CardLocation.Equipment(item.Source), To: destination);
        }).ToArray();
        // Freeze both Ox stores before either transfers, so swapping two treasures
        // never merges one owner's grains into the other owner's outgoing store.
        var moves = equipmentMoves.Concat(equipmentMoves.Where(move =>
                UsesFormalWoodenOx && move.Card.Kind == CardKind.WoodenOx)
            .SelectMany(move => _cardZones.CardsAt(CardLocation.WoodenOxGrain(move.From.OwnerSeat!.Value))
                .Select(card => (Card: card, From: CardLocation.WoodenOxGrain(move.From.OwnerSeat.Value),
                    To: move.To.Zone == CardZoneKind.Equipment
                        ? CardLocation.WoodenOxGrain(move.To.OwnerSeat!.Value) : CardLocation.DiscardPile)))).ToArray();
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.equipment-exchange");
        ReplaceRuntimeTop(active with
        {
            PendingMovementContinuation = new ProgramMovementContinuation(frame.OwnerSeat, 0, null)
        });
        var batch = BeginCardMovementBatch(moves.Select(move => move.From), moves.Select(move => move.To));
        var records = new List<CardMovementRecord>(moves.Length);
        var committed = false;
        try
        {
            // Both equipment areas vacate before entering either recipient area. Processing
            // is an internal staging zone, never a separate observable movement or trigger.
            foreach (var move in moves) _cardZones.Move(move.Card.Id, move.From, CardLocation.Processing);
            foreach (var move in moves) _cardZones.Move(move.Card.Id, CardLocation.Processing, move.To);
            foreach (var move in moves)
            {
                records.Add(RecordMovement(move.Card, move.From, move.To, reason));
                ResolveEquipmentSkillGrant(move.Card, move.From, move.To);
                ResolveSilverLionRemoval(move.Card, move.From, reason);
            }
            committed = true;
        }
        finally
        {
            CompleteCardMovementBatch(batch, records, committed);
        }
        if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
}
