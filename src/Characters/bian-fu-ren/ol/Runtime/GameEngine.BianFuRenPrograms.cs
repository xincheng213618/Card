namespace CardGame.Core;

public sealed partial class GameEngine
{
    private BianFuRenOwnedLossReceipt? BianFuRenLoss(ProgramSkillWindowContext context, int owner)
    {
        if (context.Window != SkillProgramTriggerWindow.CardsMoved || context.MovementBatch is not { } batch ||
            context.MovementIndex is not { } index || index < 0 || index >= batch.Movements.Count) return null;
        var movement = batch.Movements[index];
        return batch.BianFuRenOwnedLosses?.SingleOrDefault(receipt => receipt.BatchId == batch.Id &&
            receipt.OwnerSeat == owner && receipt.ActorSeat != owner &&
            receipt.CompletionMovementSequence == movement.Sequence && receipt.CardId == movement.CardId &&
            receipt.OriginalFrom.OwnerSeat == owner && IsBianFuRenOwnedZone(receipt.OriginalFrom) &&
            _cardMovements.Contains(movement) && BianFuRenOriginalEntry(movement) is { } original &&
            original.Sequence == receipt.EntryMovementSequence && original.From == receipt.OriginalFrom &&
            original.CardKind == receipt.PrintedKind &&
            (receipt.Discarded ? movement.To == CardLocation.DiscardPile &&
                CompleteProgramEventHistory().OfType<BianFuRenForeignDiscardProducerEvent>().Any(proof =>
                    proof.MovementSequence == receipt.EntryMovementSequence && proof.ActorSeat == receipt.ActorSeat &&
                    proof.CardOwnerSeat == owner && proof.ProducerFrameId == receipt.ProducerFrameId)
                : movement.To == CardLocation.Hand(receipt.ActorSeat) &&
                  receipt.ProducerFrameId == BianFuRenObtainProducer(batch, original, movement)));
    }

    private BianFuRenUsedDiscardReceipt? BianFuRenUsedDiscard(ProgramSkillWindowContext context, int owner)
    {
        if (context.Window != SkillProgramTriggerWindow.DiscardPileReceived || context.MovementBatch is not { } batch ||
            context.MovementIndex is not { } index || index < 0 || index >= batch.Movements.Count) return null;
        var anchor = batch.Movements[index];
        return batch.BianFuRenUsedDiscards?.SingleOrDefault(receipt => receipt.BatchId == batch.Id &&
            receipt.NativeUseId > 0 && receipt.Action.Type == CardActionType.Use &&
            receipt.Action.ActorSeat != owner && receipt.Action.EffectiveDesignatedTargetSeats.Contains(owner) &&
            receipt.Materials.Any(material => material.CompletionMovementSequence == anchor.Sequence) &&
            receipt.Materials.All(material => receipt.Action.PhysicalCards.Any(cost =>
                cost.CardId == material.CardId && cost.CardKind == material.PrintedKind && cost.From == material.OriginalPayment?.OriginalFrom) &&
                BianFuRenOriginalPaymentMatches(material, receipt.NativeUseId, receipt.Action.ActionId) &&
                _cardMovements.Any(payment => payment.Sequence == material.PaymentMovementSequence &&
                    payment.CardId == material.CardId && BianFuRenNativeUsePayment(payment)) &&
                _cardMovements.Any(finish => finish.Sequence == material.CompletionMovementSequence &&
                    finish.CardId == material.CardId && (material.DelayedPlacementMovementSequence == 0
                        ? BianFuRenNativeUseFinish(finish) : BianFuRenDelayedFinish(material, finish)))));
    }

    private static bool IsBianFuRenOperation(SkillProgramEffectOp operation) =>
        operation is SkillProgramEffectOp.WanweiObtainMatchingLostCard or SkillProgramEffectOp.YuejianRevealAndClaimUsedCard;

    private void ValidateBianFuRenTrigger(SkillProgramTrigger trigger, SkillProgramEffectOp operation)
    {
        var wanwei = operation == SkillProgramEffectOp.WanweiObtainMatchingLostCard;
        if (trigger.Window != (wanwei ? SkillProgramTriggerWindow.CardsMoved : SkillProgramTriggerWindow.DiscardPileReceived) ||
            !trigger.Optional || trigger.Subject != SkillProgramTriggerSubject.Owner ||
            trigger.MovementOccurrence != SkillProgramMovementOccurrence.PerCard ||
            trigger.UsageScope != SkillUsageScope.Turn || trigger.UsageLimit != (wanwei ? 1 : 2) ||
            trigger.DynamicUsageLimit is not null || trigger.NamedUsageGroup is not null || trigger.MarkerCost is not null ||
            trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            wanwei && (trigger.SourceZones is not { Count: 2 } zones ||
                !zones.Contains(CardZoneKind.Hand) || !zones.Contains(CardZoneKind.Equipment)) ||
            trigger.Effects is not [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } first,
                { Op: SkillProgramEffectOp.AwaitOwnedCardMovement, Target: SkillProgramEffectTarget.Owner,
                    Condition.Kind: SkillProgramConditionKind.Always }] || first.Op != operation)
            throw new InvalidOperationException($"Bian Fu Ren lost its exact movement contract: {operation}, {trigger.Window}, subject={trigger.Subject}, optional={trigger.Optional}, occurrence={trigger.MovementOccurrence}, scope={trigger.UsageScope}, limit={trigger.UsageLimit}, condition={trigger.Condition.Kind}, zones={string.Join(',', trigger.SourceZones)}, effects={string.Join(',', trigger.Effects.Select(e => e.Op + "/" + e.Target + "/" + e.Condition.Kind))}.");
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 26040)]
    private bool CanOfferBianFuRen(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger,
        ProgramSkillWindowContext context)
    {
        var operation = trigger.Effects.FirstOrDefault(effect => IsBianFuRenOperation(effect.Op))?.Op;
        if (operation is null) return true;
        ValidateBianFuRenTrigger(trigger, operation.Value);
        return operation == SkillProgramEffectOp.WanweiObtainMatchingLostCard
            ? BianFuRenLoss(context, candidate.OwnerSeat) is not null
            : GetHand(_players[candidate.OwnerSeat]).Count > 0 &&
                BianFuRenUsedDiscard(context, candidate.OwnerSeat) is { } receipt && receipt.Materials.Any(BianFuRenMaterialAvailable);
    }

    private void ResolveBianFuRenMovement(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.InstructionIndex != 1 || frame.TriggerId is null || frame.WindowContext is not { } context ||
            _resolutionStack.Count < 2 || _resolutionStack[^2] is not CardsMovedTriggerWindowFrame parent ||
            parent.Id != context.ParentFrameId || parent.Batch.Id != context.MovementBatch?.Id ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            !MountObserverCandidateMatches(frame, parent.Candidates[parent.CandidateIndex]) ||
            !_players[frame.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) ||
            CompleteProgramEventHistory().Any(fact => fact is WanweiAcquisitionIssuedEvent wanwei && wanwei.ProgramFrameId == frame.Id ||
                fact is YuejianAcquisitionIssuedEvent yuejian && yuejian.ProgramFrameId == frame.Id))
            throw new InvalidOperationException("Bian Fu Ren cannot borrow or repeat a different accepted movement instruction.");
        ValidateBianFuRenTrigger(GetProgramTrigger(frame), operation);
        if (operation == SkillProgramEffectOp.WanweiObtainMatchingLostCard)
        {
            var loss = BianFuRenLoss(context, frame.OwnerSeat) ??
                throw new InvalidOperationException("Wanwei lost the actual other actor's frozen owned-loss payment.");
            var before = _movementSequence;
            var matching = _cardZones.CardsAt(CardLocation.DrawPile).Reverse().FirstOrDefault(card =>
                ProgramBasicCardName(card.Kind) == ProgramBasicCardName(loss.PrintedKind));
            if (matching is not null)
                MoveCard(matching, CardLocation.DrawPile, CardLocation.Hand(frame.OwnerSeat), new("skill-program.wanwei.named-acquisition"));
            else
                DrawProgramCards(frame.Id, frame.OwnerSeat, 1, null, null, SkillProgramCardSetVisibility.Private,
                    new("skill-program.wanwei.fallback-draw"));
            var after = _movementSequence;
            var actual = _cardMovements.Count(move => move.Sequence > before && move.Sequence <= after &&
                move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(frame.OwnerSeat) &&
                move.Reason.Value is "skill-program.wanwei.named-acquisition" or "skill-program.wanwei.fallback-draw");
            AdvanceEventRulesAndQueueFact(new WanweiAcquisitionIssuedEvent(frame.Id, loss.BatchId,
                loss.CompletionMovementSequence, frame.OwnerSeat, loss.ActorSeat, matching is not null, before, after, actual));
            return;
        }
        var receipt = BianFuRenUsedDiscard(context, frame.OwnerSeat) ??
            throw new InvalidOperationException("Yuejian lost the exact foreign used card's native finish invoice.");
        var hand = GetHand(_players[frame.OwnerSeat]).ToArray();
        if (hand.Length == 0) throw new InvalidOperationException("Yuejian requires at least one hand card to reveal.");
        const string revealBind = "yuejian-entire-hand";
        SetProgramCardSet(frame.Id, revealBind, hand.Select(card => card.Id).ToArray(),
            SkillProgramCardSetVisibility.Private, hand.Select(_ => CardLocation.Hand(frame.OwnerSeat)).ToArray());
        RevealProgramBoundCards(frame, revealBind);
        var incomingSuit = receipt.Action.EffectiveSuit ?? Suit.None;
        var different = hand.All(card => GetProgramEffectiveSuit(_players[frame.OwnerSeat], card) != incomingSuit);
        var sequenceBefore = _movementSequence;
        if (different)
        {
            var available = receipt.Materials.Where(BianFuRenMaterialAvailable).Select(material => material.CardId).ToArray();
            MoveProgramCardsFromMultipleSources(available, CardLocation.Hand(frame.OwnerSeat),
                new("skill-program.yuejian.exact-used-card"));
        }
        var sequenceAfter = _movementSequence;
        var claimed = _cardMovements.Count(move => move.Sequence > sequenceBefore && move.Sequence <= sequenceAfter &&
            move.From == CardLocation.DiscardPile && move.To == CardLocation.Hand(frame.OwnerSeat) &&
            move.Reason.Value == "skill-program.yuejian.exact-used-card" &&
            receipt.Materials.Any(material => material.CardId == move.CardId));
        AdvanceEventRulesAndQueueFact(new YuejianAcquisitionIssuedEvent(frame.Id, receipt.BatchId,
            receipt.NativeUseId, receipt.Action.ActionId, frame.OwnerSeat, hand.Length, different,
            sequenceBefore, sequenceAfter, claimed));
    }

    [ProgramCapability]
    private sealed class BianFuRenProgramCapability(GameEngine engine) : IBianFuRenProgramHost
    {
        public void ResolveBianFuRenMovement(ProgramSkillFrame frame, SkillProgramEffectOp operation) =>
            engine.ResolveBianFuRenMovement(frame, operation);
    }
}
