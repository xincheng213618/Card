namespace CardGame.Core;

public sealed partial class GameEngine
{
    // This producer already owns payment children on its exact Program return
    // after the whole Use retires. A generic payment tail must not take them.
    private static bool OwnsGenericSlashPaymentTail(CardUseFrame use) =>
        use.ForeignSelectedCardSlashReturn is null;

    private void PaySlashUseMaterials(IReadOnlyList<Card> cards, int providerSeat, CardLocation? sourceLocation)
    {
        if (TryPayXieweiMaterials(cards, providerSeat, sourceLocation, CardMoveReasons.Use)) return;
        if (cards.Count == 1)
        {
            MoveCard(cards[0], sourceLocation ?? FindOwnedCardLocation(_players[providerSeat], cards[0]),
                CardLocation.Processing, CardMoveReasons.Use, disclosure: CardMovementDisclosure.FaceUp);
            return;
        }
        // One accepted action pays its physical materials together, including HE
        // conversions. Each material still uses the ordinary alternative-cost rule.
        foreach (var card in cards)
            if (_cardZones.GetLocation(card.Id) !=
                (sourceLocation ?? FindOwnedCardLocation(_players[providerSeat], card)))
                throw new InvalidOperationException("A card-use payment lost its original physical source.");
        MoveProgramCardsFromMultipleSources(cards.Select(card => card.Id).ToArray(), CardLocation.Processing,
            CardMoveReasons.Use, destinationForCard: (card, from) =>
                NormalizeProgramViewAsCostDestination(card, from, CardLocation.Processing, CardMoveReasons.Use),
            disclosure: CardMovementDisclosure.FaceUp);
    }

    private void CaptureSlashUsePaymentBatch(CardMovementBatchContext batch)
    {
        CaptureCaoChunUsePaymentBatch(batch);
        CaptureXieweiPaymentBatch(batch);
        CaptureNeifaPaymentBatch(batch);
        CaptureXinPiUseMovementBatch(batch);
        if (_resolutionStack.LastOrDefault() is not CardUseFrame use || batch.ParentFrameId != use.Id ||
            use.Action is not { Type: CardActionType.Use } action || !IsSlashCard(action.EffectiveKind) ||
            !CardUsePaymentMaterialsMatch(use, batch)) return;
        if (use.PaymentMovementReceipt is not null)
            throw new InvalidOperationException("One card-use payment cannot issue its physical materials twice.");
        ReplaceRuntimeFrame(use.Id, use with
        {
            PaymentMovementReceipt = new(use.Id, action.ActionId, batch.Movements[0].Sequence - 1,
                batch.Movements[^1].Sequence) { Batch = batch }
        });
    }

    private bool IsCardUsePaymentBatch(CardUseFrame use, CardMovementBatchContext batch) =>
        IsNativePublicPileEquipmentBatch(use, batch) || IsWenhePoisonUsePaymentBatch(use, batch) && CardUsePaymentBatchMatchesReceipt(use, batch) || IsXieweiPaymentBatch(use, batch) || IsFeiYiTrickPaymentBatch(use, batch) || IsWuJingHejiTrickPaymentBatch(use, batch) || IsHuiyunTrickPaymentBatch(use, batch) || IsQiejianUsePaymentBatch(use, batch) || IsJuewuUsePaymentBatch(use, batch) || CardUsePaymentBatchMatchesReceipt(use, batch) && CardUsePaymentMaterialsMatch(use, batch);

    private static bool CardUsePaymentBatchMatchesReceipt(CardUseFrame use, CardMovementBatchContext batch,
        CardUsePaymentMovementReceipt? issuedReceipt = null)
    {
        if (use.Action is not { } action || (issuedReceipt ?? use.PaymentMovementReceipt) is not { } receipt ||
            receipt.OwnerFrameId != use.Id || receipt.ActionId != action.ActionId || receipt.SequenceBefore < 0 ||
            receipt.SequenceAfter <= receipt.SequenceBefore || batch.Movements.Count == 0 ||
            receipt.SequenceBefore != batch.Movements[0].Sequence - 1 ||
            receipt.SequenceAfter != batch.Movements[^1].Sequence || receipt.Batch is not { } issued) return false;
        return batch.Id == issued.Id && batch.ParentFrameId == issued.ParentFrameId && batch.ParentBatchId == issued.ParentBatchId &&
            batch.TurnNumber == issued.TurnNumber && batch.AwaitingProgramFrameId == issued.AwaitingProgramFrameId &&
            batch.OriginSkillId == issued.OriginSkillId && batch.OriginSkillInstanceId == issued.OriginSkillInstanceId &&
            batch.OriginOwnerSeat == issued.OriginOwnerSeat && batch.MovementTiming == issued.MovementTiming &&
            batch.DiscardRecoveryPhase == issued.DiscardRecoveryPhase && batch.NativeDrawInvocation == issued.NativeDrawInvocation &&
            batch.NativeBuZhiDiscardInvocation == issued.NativeBuZhiDiscardInvocation &&
            batch.Movements.SequenceEqual(issued.Movements) && batch.SourceCounts.SequenceEqual(issued.SourceCounts) &&
            ((batch.DestinationCounts is null && issued.DestinationCounts is null) ||
             batch.DestinationCounts is { } destinations && issued.DestinationCounts is { } issuedDestinations &&
             destinations.SequenceEqual(issuedDestinations));
    }

    private bool CardUsePaymentMaterialsMatch(CardUseFrame use, CardMovementBatchContext batch)
    {
        if (!OwnsGenericSlashPaymentTail(use) ||
            use.Action is not { Type: CardActionType.Use } action || !IsSlashCard(action.EffectiveKind) ||
            use.CardKind != action.EffectiveKind || batch.ParentFrameId != use.Id || batch.ParentBatchId is not null ||
            batch.AwaitingProgramFrameId is not null || batch.NativeDrawInvocation is not null ||
            batch.NativeBuZhiDiscardInvocation is not null || batch.Movements.Count == 0 ||
            batch.Movements.Count != action.PhysicalCards.Count ||
            batch.Movements.Select(move => move.CardId).Distinct().Count() != batch.Movements.Count)
            return false;
        foreach (var move in batch.Movements)
        {
            var cost = action.PhysicalCards.SingleOrDefault(cost => cost.CardId == move.CardId);
            if (cost is null || cost.From != move.From || cost.CardKind != move.CardKind || move.Reason != CardMoveReasons.Use ||
                move.TurnNumber != batch.TurnNumber || move.Sequence <= 0 || move.Sequence > _cardMovements.Count ||
                _cardMovements[move.Sequence - 1] != move)
                return false;
            var card = _cardZones.CardsAt(_cardZones.GetLocation(move.CardId)).Single(card => card.Id == move.CardId);
            var destination = card.IsGeneralWeapon && cost.From.Zone == CardZoneKind.Equipment ||
                IsGuanduThunderChariotDeparture(card, cost.From, CardLocation.Processing)
                ? CardLocation.OutsideGame : IsProgramAlternativeCost(action, move.CardId)
                    ? CardLocation.DrawPile : CardLocation.Processing;
            if (move.To != destination) return false;
        }
        return true;
    }

    private bool IsPaidCardUseMovementReturn(CardUseFrame use, CardsMovedTriggerWindowFrame movement) =>
        use.RecoveryPaidContinuation is { } continuation &&
        (HasNativePublicPileEquipmentTail(use, continuation) || HasWenhePoisonUsePaymentTail(use, continuation) || continuation.Kind == RecoveryPaidCardUseKind.CommittedSlash || (HasFeiYiTrickPaymentTail(use, continuation) || HasWuJingHejiTrickPaymentTail(use, continuation) || (HasHuiyunTrickPaymentTail(use, continuation) || (HasQiejianUsePaymentTail(use, continuation) || HasJuewuUsePaymentTail(use, continuation))))) &&
        continuation.SourceSeat == use.SourceSeat && use.Step == ResolutionFrameStep.ResolvingEffect &&
        movement.Id == movement.Batch.Id && movement.ResumePaidCardUseFrameId == use.Id &&
        movement.ResumeProgramFrameId is null && movement.ResumeDeclarationFrameId is null &&
        movement.ResumeRecoveryReplacementFrameId is null && movement.ResumeDrawFundedDistinctBasicFrameId is null &&
        movement.ResumeNativeCardEffectDrawFrameId is null &&
        movement.ResumeGuanduCardEffectFrameId is null &&
        movement.ResumeWumiaoFloodFrameId is null &&
        movement.ResumeDragonBoatCardEffectFrameId is null &&
        movement.ResumeEquipmentRecastFrameId is null && movement.ResumeColorFireAttackFrameId is null &&
        movement.ResumeCounterspellPaymentFrameId is null && movement.ResumeHistoricalEndingUseFrameId is null && movement.ResumeGuoSiUseFrameId is null && movement.ResumeDenglouUseFrameId is null &&
        movement.ResumeRoundPileAlcoholUseFrameId is null && movement.ResumeDrawPhaseObligationFrameId is null &&
        movement.ResumeFactionRequestCostFrameId is null && movement.DeferredTurnEndReturn is null &&
        movement.ResumeCardSupplyCompletionFrameId is null && movement.ResumeResponseCompletionFrameId is null &&
        IsCardUsePaymentBatch(use, movement.Batch);

    private bool HasFeiYiTrickPaymentProcessing(IReadOnlyList<Card> processing) =>
        _resolutionStack.Count == 2 && _resolutionStack[0] is CardUseFrame { RecoveryPaidContinuation: { } continuation } use &&
        _resolutionStack[1] is CardsMovedTriggerWindowFrame movement && (HasFeiYiTrickPaymentTail(use, continuation) || HasWuJingHejiTrickPaymentTail(use, continuation) || (HasQiejianUsePaymentTail(use, continuation) || HasJuewuUsePaymentTail(use, continuation))) &&
        IsPaidCardUseMovementReturn(use, movement) && processing.All(card =>
            use.PaymentMovementReceipt!.Batch.Movements.Any(cost => cost.CardId == card.Id && cost.To == CardLocation.Processing));
}
