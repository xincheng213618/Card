namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsNativeDrawGainMovement(CardMovementRecord movement)
    {
        if (movement.From != CardLocation.DrawPile || movement.To.Zone != CardZoneKind.Hand ||
            movement.To.OwnerSeat is not { } recipient || !_cardMovements.Contains(movement)) return false;
        var invoices = ProgramEventHistory<NativeDrawInvocationRecordedEvent>().Where(e =>
            e.OwnerSeat == recipient && e.TurnNumber == movement.TurnNumber &&
            movement.Sequence > e.SequenceBefore && movement.Sequence <= e.SequenceAfter).ToArray();
        if (invoices is not [var invoice] || invoice.InvocationId <= 0 || invoice.LastBatchId <= 0 ||
            invoice.ActualCount <= 0 || invoice.Requested < invoice.ActualCount ||
            !IsValidPlayerSeat(invoice.OwnerSeat) || !IsValidPlayerSeat(invoice.ActualTurnOwnerSeat) ||
            !IsValidPlayerSeat(invoice.PhaseActorSeat) || invoice.TurnNumber < 1 ||
            invoice.SequenceBefore < 0 || invoice.SequenceAfter <= invoice.SequenceBefore ||
            invoice.SequenceAfter > _movementSequence || invoice.Reason == CardMoveReasons.InitialDeal ||
            ProgramEventHistory<NativeDrawInvocationRecordedEvent>().Count(e => e.InvocationId == invoice.InvocationId) != 1)
            return false;
        // This native completion fact was issued only after DrawCards verified
        // every actual ID against its structured invocation. Rebuild that exact
        // material proof even after its final movement window has drained.
        var materials = _cardMovements.Where(m => m.TurnNumber == invoice.TurnNumber &&
            m.Sequence > invoice.SequenceBefore && m.Sequence <= invoice.SequenceAfter &&
            m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(recipient) &&
            m.Reason == invoice.Reason).OrderBy(m => m.Sequence).ToArray();
        if (materials.Length != invoice.ActualCount || materials.Select(m => m.CardId).Distinct().Count() != materials.Length ||
            materials.Select(m => m.Sequence).Distinct().Count() != materials.Length || !materials.Contains(movement)) return false;
        var currentProof = _pendingCardsMovedBatches.Select(b => b.NativeDrawInvocation)
            .Concat(_resolutionStack.OfType<CardsMovedTriggerWindowFrame>().Select(f => f.Batch.NativeDrawInvocation))
            .FirstOrDefault(p => p?.InvocationId == invoice.InvocationId);
        return currentProof is null || ValidNativeDrawInvocation(currentProof) &&
            currentProof.Materials.Select(m => new NativeDrawMaterial(m.CardId, m.PrintedKind, m.MovementSequence))
                .SequenceEqual(materials.Select(m => new NativeDrawMaterial(m.CardId, m.CardKind, m.Sequence)));
    }
}
