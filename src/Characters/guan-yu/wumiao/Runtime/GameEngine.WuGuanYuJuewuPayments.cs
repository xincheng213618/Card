namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void IssueJuewuUse(CardUseFrame supplied)
    {
        if (!HasWuGuanYuJuewuCapability || supplied.Action is not { Type: CardActionType.Use } action) return;
        var sources = action.ConversionChain.Where(IsJuewuSource).ToArray();
        if (sources.Length == 0) return;
        var use = LifecycleCardUse(supplied.Id)!;
        if (sources is not [var source] || action.ActorSeat != source.OwnerSeat || action.ProviderSeat != source.OwnerSeat ||
            action.PhysicalCards is not [var cost] || use.PhysicalCardIds is not [var id] || id != cost.CardId ||
            use.Juewu is not null || action.EffectiveRank != 2 ||
            _contentRegistry.GetSkill(source.SkillId).Program is not { } program ||
            !HasRuntimeSkillInstance(_players[source.OwnerSeat], source.SkillId, source.SkillInstanceId) ||
            ViewAsRule(source) is not { RankTwoDamageUse: true } rule ||
            action.EffectiveKind != rule.OutputKind && !(rule.OutputKind == CardKind.Slash && action.EffectiveKind == CardKind.FireSlash &&
                HasZhuqueFan(_players[source.OwnerSeat])))
            throw new InvalidOperationException("Juewu lost its original true-use actor, rank, output, source or single physical entity.");
        var card = _cardZones.CardsAt(cost.From).SingleOrDefault(c => c.Id == cost.CardId);
        if (card is null || cost.From.OwnerSeat != source.OwnerSeat ||
            cost.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain) ||
            _cardZones.GetLocation(card.Id) != cost.From || EffectiveOwnedCardRank(_players[source.OwnerSeat], card) != 2 ||
            _skillRuntimeState.GetUsage(source.OwnerSeat, source.SkillId, ActualTurnViewAsUsageId(rule), SkillUsageScope.Turn) != 1)
            throw new InvalidOperationException("Juewu must retain the exact native rank-two payment and once-paid output allowance.");
        var receipt = new JuewuUseReceipt(use.Id, action.ActionId, source, program.GameplayHash, _turnNumber,
            _turnProgression.OwnerSeat, rule.OutputKind, action.EffectiveKind, card.Id, card.Kind, card.Rank,
            cost.From, _movementSequence, cost.From == CardLocation.Hand(source.OwnerSeat)
                ? JuewuForeignHandEntry(source.OwnerSeat, card.Id)?.Sequence : null)
        {
            Grains = UsesFormalWoodenOx && card.Kind == CardKind.WoodenOx && cost.From == CardLocation.Equipment(source.OwnerSeat)
                ? _cardZones.CardsAt(CardLocation.WoodenOxGrain(source.OwnerSeat)).Select(grain =>
                    new JuewuPaymentGrain(grain.Id, grain.Kind, CardLocation.WoodenOxGrain(source.OwnerSeat))).ToArray() : []
        };
        ReplaceRuntimeFrame(use.Id, use with { Juewu = receipt });
        AdvanceEventRulesAndQueueFact(new JuewuUseIssuedEvent(receipt.UseId, receipt.ActionId, receipt.Source,
            receipt.GameplayHash, receipt.ActualTurn, receipt.TurnOwner, receipt.OutputKind, receipt.NativeKind,
            receipt.CardId, receipt.PrintedKind, receipt.PrintedRank, receipt.From, receipt.SequenceBefore,
            receipt.ForeignHandEntrySequence));
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 28510)]
    private void CaptureJuewuMaterialPayment(IGameEvent payload)
    {
        if (payload is not CardMovedEvent moved || !HasWuGuanYuJuewuCapability) return;
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(f => f.Juewu is { PaymentSequence: 0 }).ToArray())
        {
            var receipt = use.Juewu!;
            if (moved.CardId != receipt.CardId || moved.From != receipt.From) continue;
            var movement = _cardMovements.LastOrDefault();
            if (moved.Reason != CardMoveReasons.Use || moved.To != CardLocation.Processing || movement is null ||
                movement.Sequence <= receipt.SequenceBefore || movement.CardId != receipt.CardId ||
                movement.CardKind != receipt.PrintedKind || movement.From != receipt.From || movement.To != moved.To ||
                movement.Reason != moved.Reason || !ValidJuewuUse(use, requirePayment: false))
                throw new InvalidOperationException("Juewu cannot substitute an unrelated movement for its original native payment.");
            if (!_activeCardMovementBatchIds.TryPeek(out var primaryBatch))
                throw new InvalidOperationException("Juewu's native primary payment must own its real movement batch.");
            ReplaceRuntimeFrame(use.Id, use with { Juewu = receipt with { PaymentSequence = movement.Sequence, PaymentBatchId = primaryBatch } });
            AdvanceEventRulesAndQueueFact(new JuewuMaterialPaidEvent(use.Id, receipt.ActionId, receipt.CardId,
                receipt.From, moved.To, moved.Reason, movement.Sequence));
        }
    }

    // Capture only native payment producer boundaries. An Ox spill can finish its
    // child batch before the outer primary batch, so its exact parent ID was
    // frozen at the primary CardMoved fact, before equipment children start.
    private void CaptureJuewuUsePaymentBatch(CardMovementBatchContext batch)
    {
        if (!HasWuGuanYuJuewuCapability || _resolutionStack.LastOrDefault() is not CardUseFrame { Juewu: { PaymentSequence: > 0 } r } use ||
            !JuewuPaymentMaterialsMatch(use, batch)) return;
        if (r.PaymentBatches.Any(receipt => receipt.Batch.Id == batch.Id))
            throw new InvalidOperationException("Juewu cannot issue the same native payment batch twice.");
        var invoice = new CardUsePaymentMovementReceipt(use.Id, r.ActionId,
            batch.Movements[0].Sequence - 1, batch.Movements[^1].Sequence) { Batch = batch };
        var primary = batch.Id == r.PaymentBatchId;
        if (primary && use.PaymentMovementReceipt is not null && !CardUsePaymentBatchMatchesReceipt(use, batch))
            throw new InvalidOperationException("Juewu cannot overwrite another original native payment invoice.");
        ReplaceRuntimeFrame(use.Id, use with
        {
            PaymentMovementReceipt = primary ? use.PaymentMovementReceipt ?? invoice : use.PaymentMovementReceipt,
            Juewu = r with { PaymentBatches = r.PaymentBatches.Append(invoice).OrderBy(p => p.SequenceBefore).ToArray() }
        });
        AdvanceEventRulesAndQueueFact(new JuewuPaymentBatchIssuedEvent(use.Id, r.ActionId, batch.Id,
            batch.ParentBatchId, batch.Movements.Count, invoice.SequenceBefore, invoice.SequenceAfter, primary));
    }

    private bool JuewuPaymentMaterialsMatch(CardUseFrame use, CardMovementBatchContext batch)
    {
        if (use.Juewu is not { PaymentSequence: > 0, PaymentBatchId: > 0 } r || !ValidJuewuUse(use, requirePayment: true) ||
            batch.ParentFrameId != use.Id || batch.TurnNumber != r.ActualTurn || batch.NativeDrawInvocation is not null ||
            batch.NativeBuZhiDiscardInvocation is not null || batch.Movements.Count == 0 ||
            batch.Movements.Select(m => m.CardId).Distinct().Count() != batch.Movements.Count ||
            batch.Movements.Any(m => m.Sequence <= 0 || m.Sequence > _cardMovements.Count || _cardMovements[m.Sequence - 1] != m)) return false;
        if (batch.Id == r.PaymentBatchId)
            return batch.ParentBatchId is null && batch.Movements is [var primary] && primary.Sequence == r.PaymentSequence &&
                primary.CardId == r.CardId && primary.CardKind == r.PrintedKind && primary.From == r.From &&
                primary.To == CardLocation.Processing && primary.Reason == CardMoveReasons.Use;
        return r.From == CardLocation.Equipment(r.Source.OwnerSeat) && r.PrintedKind == CardKind.WoodenOx &&
            batch.ParentBatchId == r.PaymentBatchId && batch.Movements.All(m => m.Sequence > r.PaymentSequence &&
                m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard &&
                r.Grains.Any(g => g.CardId == m.CardId && g.PrintedKind == m.CardKind && g.From == m.From));
    }

    [ProgramCardUsePayment(ProgramCardUsePaymentStage.MatchBatch, 80)]
    private bool IsJuewuUsePaymentBatch(CardUseFrame use, CardMovementBatchContext batch)
    {
        var issued = use.Juewu?.PaymentBatches.SingleOrDefault(receipt => receipt.Batch.Id == batch.Id);
        return issued is not null && CardUsePaymentBatchMatchesReceipt(use, batch, issued) && JuewuPaymentMaterialsMatch(use, batch) &&
            ProgramEventHistory<JuewuPaymentBatchIssuedEvent>().Count(e => e.UseId == use.Id && e.ActionId == issued.ActionId &&
                e.BatchId == batch.Id && e.ParentBatchId == batch.ParentBatchId && e.MaterialCount == batch.Movements.Count &&
                e.SequenceBefore == issued.SequenceBefore && e.SequenceAfter == issued.SequenceAfter && e.Primary == (batch.Id == use.Juewu!.PaymentBatchId)) == 1;
    }

    [ProgramCardUsePayment(ProgramCardUsePaymentStage.TrailingTail, 70)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.HpObserverTail, 70)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.SharedProcessingTail, 70)]
    private bool HasJuewuUsePaymentTail(CardUseFrame use, RecoveryPaidCardUseContinuation continuation)
    {
        if (use.Juewu is not { PaymentSequence: > 0 } r || continuation.SourceSeat != use.SourceSeat ||
            use.PaymentMovementReceipt is not { } primary || primary.Batch.Id != r.PaymentBatchId ||
            !IsJuewuUsePaymentBatch(use, primary.Batch) || r.PaymentBatches.Any(p => !IsJuewuUsePaymentBatch(use, p.Batch)) ||
            r.Grains.Any(g => r.PaymentBatches.SelectMany(p => p.Batch.Movements).Count(m => m.CardId == g.CardId &&
                m.CardKind == g.PrintedKind && m.From == g.From && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard) != 1)) return false;
        return continuation.Kind switch
        {
            RecoveryPaidCardUseKind.CommittedSlash => IsSlashCard(use.CardKind),
            RecoveryPaidCardUseKind.Trick => continuation.CardId == use.CardId && continuation.ActionKind is not null &&
                continuation.TargetSeats is { } targets && targets.SequenceEqual(use.TargetSeats) &&
                (continuation.PlayedCardKind ?? use.CardKind) == use.CardKind,
            _ => false
        };
    }

    private bool ValidJuewuUse(CardUseFrame use, bool requirePayment)
    {
        if (use.Juewu is not { } r || use.Id != r.UseId || !IsJuewuSource(r.Source) ||
            _contentRegistry.GetSkill(r.Source.SkillId).Program?.GameplayHash != r.GameplayHash ||
            use.Action is not { Type: CardActionType.Use } a || a.ActionId != r.ActionId || !JuewuIssuedActorMatches(use, r) ||
            a.ProviderSeat != r.Source.OwnerSeat || !a.ConversionChain.Contains(r.Source) || a.EffectiveRank != 2 ||
            a.PhysicalCards is not [var cost] || cost.CardId != r.CardId || cost.CardKind != r.PrintedKind || cost.From != r.From ||
            use.PhysicalCardIds is not [var id] || id != r.CardId ||
            ProgramEventHistory<JuewuUseIssuedEvent>().Count(e => e.UseId == use.Id && e.ActionId == a.ActionId &&
                e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.CardId == r.CardId &&
                e.From == r.From && e.OutputKind == r.OutputKind && e.ActualTurn == r.ActualTurn && e.TurnOwner == r.TurnOwner) != 1)
            return false;
        if (r.PaymentSequence == 0) return !requirePayment;
        return ProgramEventHistory<JuewuMaterialPaidEvent>().Where(e => e.UseId == use.Id).ToArray() is [var paid] &&
            paid.ActionId == a.ActionId && paid.CardId == r.CardId && paid.From == r.From &&
            paid.To == CardLocation.Processing && paid.Reason == CardMoveReasons.Use && paid.MovementSequence == r.PaymentSequence &&
            _cardMovements.Count(m => m.Sequence == r.PaymentSequence && m.CardId == r.CardId &&
                m.From == r.From && m.To == paid.To && m.Reason == paid.Reason) == 1;
    }

    private bool JuewuIssuedActorMatches(CardUseFrame use, JuewuUseReceipt receipt)
    {
        var actor = receipt.Source.OwnerSeat;
        foreach (var replacement in ProgramEventHistory<ProgramCardUseActorReplacedEvent>()
            .Where(e => e.CardUseFrameId == use.Id))
        {
            if (replacement.PreviousActorSeat != actor || replacement.ProviderSeat != receipt.Source.OwnerSeat) return false;
            actor = replacement.ActorSeat;
        }
        return use.SourceSeat == actor && use.Action?.ActorSeat == actor;
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 28511)]
    private void ObserveJuewuUseFinished(IGameEvent payload)
    {
        if (payload is not CardUseFinishedEvent finished || !HasWuGuanYuJuewuCapability ||
            LifecycleCardUse(finished.ResolutionId) is not { Juewu: { } receipt } use) return;
        if (!ValidJuewuUse(use, requirePayment: true) ||
            ProgramEventHistory<JuewuUseFinishedEvent>().Any(e => e.UseId == use.Id))
            throw new InvalidOperationException("Juewu's original native use must complete once after its one actual payment.");
        AdvanceEventRulesAndQueueFact(new JuewuUseFinishedEvent(use.Id, receipt.ActionId, receipt.Source,
            receipt.OutputKind, receipt.CardId, receipt.PaymentSequence));
    }
}
