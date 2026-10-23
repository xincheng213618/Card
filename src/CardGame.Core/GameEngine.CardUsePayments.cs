namespace CardGame.Core;

public sealed partial class GameEngine
{
    // This producer already owns payment children on its exact Program return
    // after the whole Use retires. A generic payment tail must not take them.
    private static bool OwnsGenericSlashPaymentTail(CardUseFrame use) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_CardUsePayments.OwnsGenericSlashPaymentTail(use);

    private void PaySlashUseMaterials(IReadOnlyList<Card> cards, int providerSeat, CardLocation? sourceLocation)
    {
        if (CardUsePayments.TryPayMaterials(cards, providerSeat, sourceLocation, CardMoveReasons.Use)) return;
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
        CardUsePayments.CaptureMovement(batch);
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
        CardUsePayments.MatchesBatch(use, batch) || CardUsePaymentBatchMatchesReceipt(use, batch) && CardUsePaymentMaterialsMatch(use, batch);

    private static bool CardUsePaymentBatchMatchesReceipt(CardUseFrame use, CardMovementBatchContext batch,
        CardUsePaymentMovementReceipt? issuedReceipt = null) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_CardUsePayments.CardUsePaymentBatchMatchesReceipt(use,batch,issuedReceipt);

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
                ? CardLocation.OutsideGame : IsWushuangJinzhiPaidMaterial(action, move.CardId) ? CardLocation.Processing : IsProgramAlternativeCost(action, move.CardId)
                    ? CardLocation.DrawPile : CardLocation.Processing;
            destination = NormalizeEphemeralCardDestination(card, cost.From, destination);
            if (move.To != destination || IsDiouCarrier(card.Id) && destination == CardLocation.OutsideGame && !IsEphemeralCardDestroyedMaterial(move)) return false;
        }
        return true;
    }

    private bool IsPaidCardUseMovementReturn(CardUseFrame use, CardsMovedTriggerWindowFrame movement) =>
        use.RecoveryPaidContinuation is { } continuation &&
        HasPaidCardUseMovementTail(use, continuation) &&
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
        movement.ResumeRoundPileAlcoholUseFrameId is null && movement.ResumeDrawPhaseObligationFrameId is null && movement.ResumeManualDiscardFrameId is null &&
        movement.ResumeFactionRequestCostFrameId is null && movement.DeferredTurnEndReturn is null &&
        movement.ResumeCardSupplyCompletionFrameId is null && movement.ResumeResponseCompletionFrameId is null &&
        IsCardUsePaymentBatch(use, movement.Batch);

    [ProgramCardUsePayment(ProgramCardUsePaymentStage.ProcessingAllowance, 10)]
    private bool HasSharedCardUsePaymentProcessing(IReadOnlyList<Card> processing) =>
        _resolutionStack.Count == 2 && _resolutionStack[0] is CardUseFrame { RecoveryPaidContinuation: { } continuation } use &&
        _resolutionStack[1] is CardsMovedTriggerWindowFrame movement && CardUsePayments.AllowsTail(ProgramCardUsePaymentStage.SharedProcessingTail, use, continuation) &&
        IsPaidCardUseMovementReturn(use, movement) && processing.All(card =>
            use.PaymentMovementReceipt!.Batch.Movements.Any(cost => cost.CardId == card.Id && cost.To == CardLocation.Processing));
}
