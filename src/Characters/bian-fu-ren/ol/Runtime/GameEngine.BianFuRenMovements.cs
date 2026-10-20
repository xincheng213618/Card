namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasBianFuRenCapability => _contentRegistry?.ProgramDependencies
        .HasTriggerOperation(SkillProgramEffectOp.WanweiObtainMatchingLostCard) == true ||
        _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.YuejianRevealAndClaimUsedCard) == true;

    private static bool IsBianFuRenOwnedZone(CardLocation from) => from.OwnerSeat is not null &&
        from.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain;

    // Called by the original typed transfer producer at its HE -> Processing
    // payment. Contributions own an action id rather than a live stack frame.
    private void RecordBianFuRenOwnedTransferProducer(CardMovementRecord movement, int recipientSeat, long producerId)
    {
        RecordDuFuRenOwnedTransferProducer(movement, recipientSeat, producerId);
        RecordTangTangOwnedTransferProducer(movement, recipientSeat, producerId);
        if (!HasBianFuRenCapability || !IsBianFuRenOwnedZone(movement.From) ||
            !HasBianFuRenWanwei(movement.From.OwnerSeat!.Value) || movement.To != CardLocation.Processing ||
            movement.From.OwnerSeat == recipientSeat || !IsValidPlayerSeat(recipientSeat) || producerId <= 0 ||
            !_cardMovements.Contains(movement)) return;
        AdvanceEventRulesAndQueueFact(new BianFuRenOwnedTransferProducerEvent(movement.Sequence, producerId,
            movement.From.OwnerSeat.Value, recipientSeat));
    }

    // Native producers can call this in RecordMovement's beforeFact callback.
    // It freezes the accepted actor at payment, never a later current player,
    // the producing skill's owner or a guessed interpretation of a reason name.
    private void RecordBianFuRenForeignDiscardProducer(CardMovementRecord movement, int actorSeat,
        long producerFrameId)
    {
        if (!HasBianFuRenCapability || !IsBianFuRenOwnedZone(movement.From) ||
            !HasBianFuRenWanwei(movement.From.OwnerSeat!.Value) ||
            movement.From.OwnerSeat == actorSeat || !IsValidPlayerSeat(actorSeat) || producerFrameId <= 0 ||
            !_resolutionStack.Any(frame => frame.Id == producerFrameId) ||
            !_cardMovements.Contains(movement) || movement.To != CardLocation.DiscardPile && movement.To != CardLocation.Processing)
            return;
        if (CompleteProgramEventHistory().OfType<BianFuRenForeignDiscardProducerEvent>()
            .Any(fact => fact.MovementSequence == movement.Sequence)) return;
        AdvanceEventRulesAndQueueFact(new BianFuRenForeignDiscardProducerEvent(movement.Sequence,
            movement.Sequence, producerFrameId, actorSeat, movement.From.OwnerSeat!.Value));
    }

    // Called immediately after a physical RecordMovement and before removal
    // observers can retire its typed chooser/payment frame.
    private void CaptureBianFuRenForeignDiscardProducer(CardMovementRecord movement)
    {
        CaptureBianFuRenNativeUsePayment(movement);
        CaptureBianFuRenDelayedPlacement(movement);
        if (!HasBianFuRenCapability || !IsBianFuRenOwnedZone(movement.From) ||
            !HasBianFuRenWanwei(movement.From.OwnerSeat!.Value) ||
            movement.To != CardLocation.DiscardPile && movement.To != CardLocation.Processing) return;
        var cardOwner = movement.From.OwnerSeat!.Value;
        if (_resolutionStack.LastOrDefault() is CardUseFrame use && use.Action is { } action &&
            action.ActorSeat == use.SourceSeat && action.ActorSeat != cardOwner &&
            action.EffectiveDesignatedTargetSeats.Contains(cardOwner))
        {
            // A native target-card selector has already validated this exact
            // Dismantlement payment; unlike a cost, its actor is the Use actor.
            if (action.EffectiveKind == CardKind.Dismantlement && movement.To == CardLocation.Processing &&
                movement.Reason == CardMoveReasons.Dismantlement)
            {
                RecordBianFuRenForeignDiscardProducer(movement, action.ActorSeat, use.Id);
                return;
            }
            if (action.EffectiveKind == CardKind.Snatch && movement.To == CardLocation.Processing &&
                movement.Reason == CardMoveReasons.Snatch)
            {
                RecordBianFuRenOwnedTransferProducer(movement, action.ActorSeat, use.Id);
                return;
            }
        }
        if (movement.To != CardLocation.DiscardPile) return;
        if (movement.Reason == CardMoveReasons.IceSwordDiscard && ActiveIceSword is { } ice &&
            ice.Attack.TargetSeat == cardOwner)
        {
            RecordBianFuRenForeignDiscardProducer(movement, ice.Attack.SourceSeat, ice.OwnerFrameId);
            return;
        }
        if (movement.Reason == CardMoveReasons.QilinBowDiscard && ActiveQilinBow is { } bow &&
            bow.Attack.TargetSeat == cardOwner)
        {
            RecordBianFuRenForeignDiscardProducer(movement, bow.Attack.SourceSeat, bow.OwnerFrameId);
            return;
        }
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame program || program.InstructionIndex < 1) return;
        var plan = ProgramInstructionResolver.Default.Resolve(program, _contentRegistry.GetSkill(program.SkillId).Program!);
        if (program.InstructionIndex > plan.Instructions.Count) return;
        var effect = plan.GetPausedInstruction(program.InstructionIndex).Effect;
        int? actor = null;
        switch (effect.Op)
        {
            case SkillProgramEffectOp.SelectAndMoveOwnedCard when effect.Destination == SkillProgramCardDestination.DiscardPile &&
                effect.CardOwnerRef is { } ownerRef && ResolveProgramParticipant(program, ownerRef) == cardOwner:
                actor = ResolveProgramParticipant(program, effect.ChooserRef!);
                break;
            case SkillProgramEffectOp.ChooseOtherOwnedCardDiscard:
                actor = ResolveProgramParticipant(program, effect.ChooserRef!);
                break;
            case SkillProgramEffectOp.ChooseOwnCardDiscard:
                // A requested self-discard stays the chooser's own action.
                actor = effect.ChooserRef is { } chooser ? ResolveProgramParticipant(program, chooser) : program.OwnerSeat;
                break;
            case SkillProgramEffectOp.MoveBoundCards when effect.Destination == SkillProgramCardDestination.DiscardPile &&
                effect.SourceBind is { } bind:
                var binding = GetProgramCardSet(program, bind);
                if (binding.CardIds.Contains(movement.CardId)) actor = binding.SelectionActorSeat;
                break;
        }
        if (actor is { } explicitActor)
            RecordBianFuRenForeignDiscardProducer(movement, explicitActor, program.Id);
    }

    private CardMovementRecord? BianFuRenOriginalEntry(CardMovementRecord movement)
    {
        if (IsBianFuRenOwnedZone(movement.From)) return movement;
        if (movement.From != CardLocation.Processing) return null;
        var previous = _cardMovements.LastOrDefault(entry => entry.CardId == movement.CardId && entry.Sequence < movement.Sequence);
        if (previous is null || previous.To != CardLocation.Processing || !IsBianFuRenOwnedZone(previous.From)) return null;
        if (movement.To.Zone == CardZoneKind.Hand && CompleteProgramEventHistory()
            .OfType<BianFuRenOwnedTransferProducerEvent>().Any(proof =>
                proof.EntryMovementSequence == previous.Sequence && proof.CardOwnerSeat == previous.From.OwnerSeat &&
                proof.RecipientSeat == movement.To.OwnerSeat)) return previous;
        // These are paired native hand/equipment target-card payments. A used,
        // responded, replaced or recast material never inherits this ownership.
        if (previous.Reason == CardMoveReasons.Dismantlement && movement.Reason == CardMoveReasons.DismantlementFinished ||
            previous.Reason == CardMoveReasons.Snatch && movement.Reason == CardMoveReasons.SnatchFinished) return previous;
        return null;
    }

    private long BianFuRenObtainProducer(CardMovementBatchContext batch, CardMovementRecord entry,
        CardMovementRecord completion) => CompleteProgramEventHistory().OfType<BianFuRenOwnedTransferProducerEvent>()
        .SingleOrDefault(proof => proof.EntryMovementSequence == entry.Sequence &&
            proof.CardOwnerSeat == entry.From.OwnerSeat && proof.RecipientSeat == completion.To.OwnerSeat)?.ProducerId
        ?? batch.ParentFrameId ?? 0;

    private void CaptureBianFuRenNativeUsePayment(CardMovementRecord movement)
    {
        if (!HasBianFuRenCapability || !BianFuRenNativeUsePayment(movement)) return;
        if (_resolutionStack.LastOrDefault() is CardDeclarationFrame { Stage: CardDeclarationStage.Paying } declaration &&
            declaration.Payment is { BianFuRenPaymentSequence: null } held &&
            held.DeclarationId == declaration.Id && held.OwnerFrameId == declaration.Id &&
            held.ProviderSeat == declaration.OwnerSeat && held.Cost.CardId == movement.CardId &&
            held.Cost.CardKind == movement.CardKind && held.Cost.From == movement.From &&
            movement.Reason.Value == "conversion.declaration.pay")
        {
            ReplaceRuntimeFrame(declaration.Id, declaration with
                { Payment = held with { BianFuRenPaymentSequence = movement.Sequence } });
            return;
        }
        // Only the current native producer can pay this action. A response or
        // program child cannot attach its move to a still-live outer use.
        // Different native producers intentionally have different reason names.
        if (_resolutionStack.LastOrDefault() is not CardUseFrame { Action: { Type: CardActionType.Use } producer } use ||
            producer.ActorSeat != use.SourceSeat || !producer.PhysicalCards.Any(cost =>
                cost.CardId == movement.CardId && cost.CardKind == movement.CardKind && cost.From == movement.From)) return;
        if (use.BianFuRenUsePayments?.Any(payment => payment.CardId == movement.CardId) == true)
            throw new InvalidOperationException("One native use cannot pay the same Bian Fu Ren material twice.");
        ReplaceRuntimeFrame(use.Id, use with { BianFuRenUsePayments = [.. (use.BianFuRenUsePayments ?? []),
            new(use.Id, producer.ActionId, movement.CardId, movement.CardKind, movement.From, movement.Sequence)] });
    }

    private void CaptureBianFuRenClaimedDeclarationPayment(long nativeUseId)
    {
        if (!HasBianFuRenCapability || LifecycleCardUse(nativeUseId) is not
            { Action: { Type: CardActionType.Use } action, AcceptedDeclarationPayment: { Claimed: true,
                BianFuRenPaymentSequence: { } sequence } declared } use) return;
        if (use.Id != nativeUseId || declared.OwnerFrameId != use.Id || declared.DeclarationId <= 0 ||
            declared.ProviderSeat != action.ProviderSeat || declared.Cost.From.OwnerSeat != declared.ProviderSeat ||
            !action.PhysicalCards.Any(cost => cost.CardId == declared.Cost.CardId &&
                cost.CardKind == declared.Cost.CardKind && cost.From == declared.Cost.From))
            throw new InvalidOperationException("A declared Yuejian material lost its typed payment adoption.");
        var payment = _cardMovements.SingleOrDefault(move => move.Sequence == sequence &&
            move.CardId == declared.Cost.CardId && move.CardKind == declared.Cost.CardKind &&
            move.From == declared.Cost.From && move.To == CardLocation.Processing &&
            move.Reason.Value == "conversion.declaration.pay");
        if (payment is null) throw new InvalidOperationException("A declared Yuejian material lost its original physical payment.");
        var invoice = new BianFuRenUsePayment(use.Id, action.ActionId, payment.CardId, payment.CardKind,
            payment.From, payment.Sequence);
        var prior = use.BianFuRenUsePayments?.SingleOrDefault(entry => entry.CardId == payment.CardId);
        if (prior is not null)
        {
            if (prior != invoice) throw new InvalidOperationException("A declaration cannot adopt another native use's payment.");
            return;
        }
        ReplaceRuntimeFrame(use.Id, use with { BianFuRenUsePayments = [.. (use.BianFuRenUsePayments ?? []), invoice] });
    }

    private bool BianFuRenOriginalPaymentMatches(BianFuRenUsedMaterial material, long nativeUseId, long actionId)
    {
        if (material.OriginalPayment is not { } proof || proof.NativeUseId != nativeUseId || proof.ActionId != actionId ||
            nativeUseId <= 0 || actionId <= 0 || proof.CardId != material.CardId || proof.PrintedKind != material.PrintedKind ||
            proof.MovementSequence != material.PaymentMovementSequence) return false;
        return _cardMovements.Any(payment => payment.Sequence == proof.MovementSequence &&
            payment.CardId == proof.CardId && payment.CardKind == proof.PrintedKind && payment.From == proof.OriginalFrom &&
            BianFuRenNativeUsePayment(payment));
    }

    private void CaptureBianFuRenDelayedPlacement(CardMovementRecord movement)
    {
        if (!HasBianFuRenCapability || movement.From != CardLocation.Processing ||
            movement.To.Zone != CardZoneKind.Judgment || movement.Reason != CardMoveReasons.DelayedCardPlace) return;
        var use = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(candidate => candidate.Action is { } action &&
            action.Type == CardActionType.Use && action.PhysicalCards.Any(cost => cost.CardId == movement.CardId) &&
            action.EffectiveKind is CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning &&
            movement.To.OwnerSeat is { } target && target != action.ActorSeat &&
            action.EffectiveDesignatedTargetSeats.Contains(target));
        if (use?.Action is not { } producer) return;
        var cost = producer.PhysicalCards.Single(material => material.CardId == movement.CardId);
        var ownPayment = use.BianFuRenUsePayments?.SingleOrDefault(payment => payment.CardId == cost.CardId &&
            payment.NativeUseId == use.Id && payment.ActionId == producer.ActionId);
        if (ownPayment is null) return;
        var payment = _cardMovements.SingleOrDefault(move => move.Sequence == ownPayment.MovementSequence);
        if (payment is null || payment.From != cost.From || payment.To != CardLocation.Processing ||
            payment.CardKind != cost.CardKind || !BianFuRenNativeUsePayment(payment) ||
            _cardMovements.Any(move => move.CardId == cost.CardId && move.Sequence > payment.Sequence && move.Sequence < movement.Sequence)) return;
        AdvanceEventRulesAndQueueFact(new BianFuRenDelayedUsePlacedEvent(use.Id, producer.ActionId, cost.CardId,
            payment.Sequence, movement.Sequence, producer.ActorSeat, movement.To.OwnerSeat!.Value,
            producer.EffectiveKind, producer.EffectiveSuit));
    }

    private bool BianFuRenDelayedFinish(BianFuRenUsedMaterial material, CardMovementRecord finish)
    {
        if (material.DelayedPlacementMovementSequence <= 0 || finish.To != CardLocation.DiscardPile ||
            finish.Sequence != material.CompletionMovementSequence || finish.CardId != material.CardId) return false;
        var trail = _cardMovements.Where(move => move.CardId == material.CardId &&
            move.Sequence >= material.DelayedPlacementMovementSequence && move.Sequence <= finish.Sequence).ToArray();
        if (trail.Length < 2 || trail[0].Sequence != material.DelayedPlacementMovementSequence ||
            trail[0].From != CardLocation.Processing || trail[0].To.Zone != CardZoneKind.Judgment ||
            trail[0].Reason != CardMoveReasons.DelayedCardPlace || trail[^1] != finish) return false;
        // Only its uninterrupted public judgment stay/transfer and its exact
        // terminal disposition belong to this original use. Obtaining and
        // re-discarding/reusing the same entity invalidates this lineage.
        for (var index = 1; index < trail.Length; index++)
        {
            var move = trail[index]; var previous = trail[index - 1];
            if (move.From != previous.To) return false;
            if (index == trail.Length - 1)
            {
                if (move.From.Zone == CardZoneKind.Judgment) return true;
                return move.From == CardLocation.Processing && previous.From.Zone == CardZoneKind.Judgment &&
                    previous.Reason == CardMoveReasons.DismantlementJudgment &&
                    move.Reason == CardMoveReasons.DismantlementJudgmentFinished;
            }
            if (move.From.Zone != CardZoneKind.Judgment ||
                !(move.To.Zone == CardZoneKind.Judgment && move.Reason == CardMoveReasons.DelayedCardTransfer ||
                    index == trail.Length - 2 && move.To == CardLocation.Processing &&
                    move.Reason == CardMoveReasons.DismantlementJudgment)) return false;
        }
        return false;
    }

    private CardMovementBatchContext CaptureBianFuRenMovementBatch(CardMovementBatchContext batch)
    {
        if (!HasBianFuRenCapability || !_players.Any(player => player.IsAlive &&
            (HasBianFuRenWanwei(player.Seat) || HasBianFuRenYuejian(player.Seat)))) return batch;
        var losses = new List<BianFuRenOwnedLossReceipt>();
        foreach (var movement in batch.Movements)
        {
            var entry = BianFuRenOriginalEntry(movement);
            if (entry?.From.OwnerSeat is not { } owner || !HasBianFuRenWanwei(owner)) continue;
            if (movement.To.Zone == CardZoneKind.Hand && movement.To.OwnerSeat is { } recipient && recipient != owner)
            {
                losses.Add(new(batch.Id, BianFuRenObtainProducer(batch, entry, movement), owner, recipient, movement.CardId,
                    entry.CardKind, entry.From, entry.Sequence, movement.Sequence, false));
            }
            else if (movement.To == CardLocation.DiscardPile)
            {
                var proof = CompleteProgramEventHistory().OfType<BianFuRenForeignDiscardProducerEvent>()
                    .SingleOrDefault(fact => fact.MovementSequence == entry.Sequence && fact.CardOwnerSeat == owner);
                if (proof is not null && proof.ActorSeat != owner)
                    losses.Add(new(batch.Id, proof.ProducerFrameId, owner, proof.ActorSeat, movement.CardId,
                        entry.CardKind, entry.From, entry.Sequence, movement.Sequence, true));
            }
        }
        var used = new List<BianFuRenUsedDiscardReceipt>();
        foreach (var use in _resolutionStack.OfType<CardUseFrame>())
        {
            if (use.Action is not { Type: CardActionType.Use } action || action.ActorSeat != use.SourceSeat ||
                action.PhysicalCards.Count == 0 || !action.EffectiveDesignatedTargetSeats.Any(target => target != action.ActorSeat &&
                    HasBianFuRenYuejian(target)) ||
                !batch.Movements.Any(move => action.PhysicalCards.Any(cost => cost.CardId == move.CardId) && BianFuRenNativeUseFinish(move)) ||
                action.PhysicalCards.Any(cost => _cardZones.GetLocation(cost.CardId) == CardLocation.Processing)) continue;
            var materials = new List<BianFuRenUsedMaterial>();
            foreach (var cost in action.PhysicalCards)
            {
                var finish = _cardMovements.LastOrDefault(move => move.CardId == cost.CardId);
                if (finish is null || !BianFuRenNativeUseFinish(finish)) continue;
                var ownPayment = use.BianFuRenUsePayments?.SingleOrDefault(payment => payment.CardId == cost.CardId &&
                    payment.NativeUseId == use.Id && payment.ActionId == action.ActionId);
                if (ownPayment is null) continue;
                var payment = _cardMovements.SingleOrDefault(move => move.Sequence == ownPayment.MovementSequence);
                if (payment is null || payment.From != cost.From || payment.CardKind != cost.CardKind ||
                    !BianFuRenNativeUsePayment(payment) || payment.TurnNumber != batch.TurnNumber ||
                    _cardMovements.Any(move => move.CardId == cost.CardId && move.Sequence > payment.Sequence && move.Sequence < finish.Sequence))
                    continue;
                // The exact accepted action is the frozen producer; a later
                // reuse of this entity cannot borrow the old target relation.
                if (CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Any(accepted =>
                    accepted.Action.ActionId > action.ActionId && accepted.Action.PhysicalCards.Any(card => card.CardId == cost.CardId))) continue;
                materials.Add(new(cost.CardId, cost.CardKind, payment.Sequence, finish.Sequence, OriginalPayment: ownPayment));
            }
            if (materials.Count == 0 || !materials.Any(material => batch.Movements.Any(move =>
                move.Sequence == material.CompletionMovementSequence))) continue;
            used.Add(new() { BatchId = batch.Id, NativeUseId = use.Id, Action = action, Materials = materials });
        }
        foreach (var finish in batch.Movements.Where(move => move.To == CardLocation.DiscardPile))
        {
            var placed = CompleteProgramEventHistory().OfType<BianFuRenDelayedUsePlacedEvent>()
                .LastOrDefault(invoice => invoice.CardId == finish.CardId && invoice.PlacementMovementSequence < finish.Sequence);
            if (placed is null) continue;
            var action = CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>()
                .Select(accepted => accepted.Action).LastOrDefault(action => action.ActionId == placed.ActionId &&
                    action.ActorSeat == placed.ActorSeat && action.EffectiveKind == placed.EffectiveKind &&
                    action.EffectiveSuit == placed.EffectiveSuit && action.EffectiveDesignatedTargetSeats.Contains(placed.OriginalTargetSeat));
            if (action is null || placed.ActorSeat == placed.OriginalTargetSeat || !HasBianFuRenYuejian(placed.OriginalTargetSeat)) continue;
            var cost = action.PhysicalCards.SingleOrDefault(card => card.CardId == finish.CardId);
            if (cost is null) continue;
            var material = new BianFuRenUsedMaterial(cost.CardId, cost.CardKind, placed.PaymentMovementSequence,
                finish.Sequence, placed.PlacementMovementSequence, new(placed.NativeUseId, placed.ActionId,
                    cost.CardId, cost.CardKind, cost.From, placed.PaymentMovementSequence));
            if (!BianFuRenDelayedFinish(material, finish)) continue;
            used.Add(new() { BatchId = batch.Id, NativeUseId = placed.NativeUseId, Action = action, Materials = [material] });
        }
        return batch with
        {
            BianFuRenOwnedLosses = losses.Count == 0 ? null : losses,
            BianFuRenUsedDiscards = used.Count == 0 ? null : used
        };
    }

    private static bool BianFuRenNativeUseFinish(CardMovementRecord movement) =>
        movement.From == CardLocation.Processing && movement.To == CardLocation.DiscardPile &&
        (movement.Reason == CardMoveReasons.UseFinished || movement.Reason == CardMoveReasons.IronChainFinished);

    private static bool BianFuRenNativeUsePayment(CardMovementRecord movement) =>
        // Eligibility comes from the typed producer captured on its owning use,
        // never from a later guess based on the reason string or current source.
        movement.To == CardLocation.Processing;

    private IEnumerable<ProgramTriggerCandidate> CollectBianFuRenLossCandidates(CardMovementBatchContext batch)
    {
        if (batch.BianFuRenOwnedLosses is not { Count: > 0 } losses) yield break;
        foreach (var owner in losses.Select(loss => loss.OwnerSeat).Distinct().Where(owner => _players[owner].IsAlive))
        foreach (var candidate in CollectProgramTriggerCandidates(_players[owner], SkillProgramTriggerWindow.CardsMoved)
                     .Where(candidate => IsBianFuRenWanweiTrigger(GetProgramTrigger(candidate))))
        foreach (var loss in losses.Where(loss => loss.OwnerSeat == owner))
        {
            var index = batch.Movements.ToList().FindIndex(move => move.Sequence == loss.CompletionMovementSequence);
            if (index >= 0) yield return candidate with { OccurrenceIndex = index };
        }
    }

    private bool BianFuRenMaterialAvailable(BianFuRenUsedMaterial material) =>
        material.OriginalPayment is { } proof && BianFuRenOriginalPaymentMatches(material, proof.NativeUseId, proof.ActionId) &&
        _cardZones.GetLocation(material.CardId) == CardLocation.DiscardPile &&
        _cardMovements.LastOrDefault(move => move.CardId == material.CardId) is { } last &&
        last.Sequence == material.CompletionMovementSequence &&
        (material.DelayedPlacementMovementSequence == 0 ? BianFuRenNativeUseFinish(last) : BianFuRenDelayedFinish(material, last));

    private ProgramSkillWindowContext CreateBianFuRenMovementContext(CardsMovedTriggerWindowFrame parent,
        ProgramTriggerCandidate candidate)
    {
        var trigger = GetProgramTrigger(candidate);
        var movement = parent.Batch.Movements[candidate.OccurrenceIndex];
        var loss = parent.Batch.BianFuRenOwnedLosses?.SingleOrDefault(receipt =>
            receipt.OwnerSeat == candidate.OwnerSeat && receipt.CompletionMovementSequence == movement.Sequence);
        var use = parent.Batch.BianFuRenUsedDiscards?.SingleOrDefault(receipt =>
            receipt.Action.ActorSeat != candidate.OwnerSeat && receipt.Action.EffectiveDesignatedTargetSeats.Contains(candidate.OwnerSeat) &&
            receipt.Materials.Any(material => material.CompletionMovementSequence == movement.Sequence));
        return new(trigger.Window, parent.Id, candidate.OwnerSeat,
            SourceSeat: IsBianFuRenWanweiTrigger(trigger) ? loss?.ActorSeat : use?.Action.ActorSeat,
            TargetSeat: candidate.OwnerSeat, OccurrenceIndex: candidate.OccurrenceIndex,
            Facts: CaptureProgramTriggerFacts(_players[candidate.OwnerSeat]), MovementBatch: parent.Batch,
            MovementIndex: candidate.OccurrenceIndex);
    }
}
