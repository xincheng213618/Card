namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TryBeginProvidedCardSupplyCompletion(CardAttackHandle attack, FactionCardRequestHandle? request)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerWindow(SkillProgramTriggerWindow.CardSupplyCompleted)) return false;
        if (request is null || _resolutionStack.LastOrDefault() is not CardUseFrame use || use.Id != attack.ResolutionId ||
            use.CompletedCardSupplyFrameId is not null || use.Action is not { Type: CardActionType.Use } action || action.ActorSeat == action.ProviderSeat) return false;
        if (_winner != Winner.None || action.RequesterSeat != action.ActorSeat || action.PhysicalCards.Count == 0 || !IsSlashCard(action.EffectiveKind) ||
            request.ActiveAttack?.ResolutionId != use.Id || request.OwnerSeat != action.ActorSeat || request.CurrentCandidateSeat != action.ProviderSeat ||
            request.AwaitingProviders || request.Purpose is not (FactionCardRequestPurpose.ProgramSkillUse or FactionCardRequestPurpose.AssistedProgramUse or
                FactionCardRequestPurpose.BorrowedSwordUse or FactionCardRequestPurpose.QinglongCrescentBladeUse) ||
            action.PhysicalCards.Any(c => c.From.OwnerSeat != action.ProviderSeat || c.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)))
            throw new InvalidOperationException("An active supplied Slash lost its exact native provider/requester payment.");
        var costs = action.PhysicalCards.Select(c =>
        {
            var entry = _cardMovements.LastOrDefault(m => m.CardId == c.CardId);
            return entry is not null && IsCardSupplyCompletionEntry(action, use.Id, request.OwnerFrameId, c, entry)
                ? entry : throw new InvalidOperationException("An active supplied Slash lost its exact native entry cost.");
        }).OrderBy(m => m.Sequence).ToArray();
        var sequences = costs.Select(m => m.Sequence).ToHashSet();
        var r = new ProgramCardSupplyCompletionReturn { ActionId = action.ActionId, ParentFrameId = use.Id, RequestOwnerFrameId = request.OwnerFrameId,
            Purpose = request.Purpose, RequesterSeat = action.ActorSeat, ProviderSeat = action.ProviderSeat, EffectiveKind = action.EffectiveKind,
            ActualTurnNumber = _turnNumber, ActualTurnOwnerSeat = _currentSeat, NativeCosts = costs,
            CostBatches = _pendingCardsMovedBatches.Where(b => b.ParentFrameId == use.Id && b.AwaitingProgramFrameId is null && b.Movements.Any(m => sequences.Contains(m.Sequence))).OrderBy(b => b.Id).ToArray(),
            CostRecoveries = (use.PendingRecoveryAttempts ?? []).Where(a => IsResponseCompletionSilverLionAttempt(costs, a)).ToArray(),
            CostHealthChanges = _pendingHpChanges.Where(h => h.ParentFrameId == use.Id && IsResponseCompletionSilverLionHealth(costs, h)).ToArray() };
        if (CompleteProgramEventHistory().OfType<CardSupplyCompletionStartedEvent>().Any(e => e.ActionId == r.ActionId))
            throw new InvalidOperationException("An actual supplied play cannot complete twice.");
        var window = new ProgramCardTriggerWindowFrame(++_resolutionSequence, use.Id, action, ProgramCardContinuation.CommittedSlash, [],
            AttackOwnerFrameId: use.Id) { CardSupplyCompletion = r };
        AdvanceEventRulesAndQueueFact(new CardSupplyCompletionStartedEvent(window.Id, r.ActionId, r.ParentFrameId, r.RequestOwnerFrameId,
            r.Purpose, r.RequesterSeat, r.ProviderSeat, r.EffectiveKind, r.ActualTurnNumber, r.ActualTurnOwnerSeat));
        PushRuntimeFrame(window); AdvanceRuntimeTop<ProgramCardTriggerWindowFrame>(); return true;
    }

    private static CardActionContext CardSupplyCompletionActorAction(ProgramCardTriggerWindowFrame window)
    {
        var a = window.Action; var r = window.CardSupplyCompletion!;
        // This trusted observer projection remains a Use. It is not a new accepted
        // response and does not change who owns the underlying Slash's Use.
        return new(a.ActionId, a.ParentActionId, a.Type, r.ProviderSeat, r.ProviderSeat, a.RequesterSeat, a.ResponderSeat,
            a.OpponentSeat, a.EffectiveKind, a.TargetSeats, a.PhysicalCards, a.ConversionChain, a.DesignatedTargetSeats,
            a.EffectiveSuit, a.EffectiveRank, a.EffectiveIsRed, a.FactionOrigin);
    }
    private static CardSupplyCompletedEvent CardSupplyCompletionFact(ProgramCardTriggerWindowFrame window)
    {
        var r = window.CardSupplyCompletion!;
        return new(window.Id, r.ActionId, r.ParentFrameId, r.RequesterSeat, r.ProviderSeat, r.EffectiveKind, r.ActualTurnNumber, r.ActualTurnOwnerSeat);
    }
    private bool IsCardSupplyCompletionCostPaid(ProgramCardTriggerWindowFrame window, CardActionCost cost) =>
        window.CardSupplyCompletion is { } r && window.Action.PhysicalCards.Contains(cost) && r.NativeCosts.Where(m => m.CardId == cost.CardId).ToArray() is [var moved] &&
        IsCardSupplyCompletionEntry(window.Action, r.ParentFrameId, r.RequestOwnerFrameId, cost, moved);

    private bool IsCardSupplyCompletionEntry(CardActionContext action, long useId, long requestOwnerId,
        CardActionCost cost, CardMovementRecord moved)
    {
        if (!_cardMovements.Contains(moved) || moved.CardId != cost.CardId || moved.CardKind != cost.CardKind ||
            moved.From != cost.From || moved.To != CardLocation.Processing) return false;
        if (moved.Reason == CardMoveReasons.Use) return true;
        // A successful declaration has already drained its own payment children.
        // The native Use claims that same entity instead of moving it a second
        // time. Its original request frame remains the typed payment owner.
        if (moved.Reason.Value != "conversion.declaration.pay" || moved.TurnNumber != _turnNumber ||
            action.PhysicalCards is not [var only] || only != cost || cost.From != CardLocation.Hand(action.ProviderSeat) ||
            _resolutionStack.SingleOrDefault(f => f.Id == requestOwnerId)?.AcceptedDeclarationPayment is not { Claimed: true } paid ||
            paid.OwnerFrameId != requestOwnerId || paid.DeclarationId <= 0 || paid.ProviderSeat != action.ProviderSeat ||
            paid.ActorSeat != action.ActorSeat || paid.Cost != cost || paid.Source.OwnerSeat != action.ProviderSeat ||
            !action.ConversionChain.Contains(paid.Source) || ViewAsRule(paid.Source) is not { DeclarationValidation: not null } rule ||
            rule.OutputKind != paid.DeclaredKind) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<CardDeclarationCommittedEvent>().Where(e => e.DeclarationId == paid.DeclarationId).ToArray() is not [var declared] ||
            declared.OwnerSeat != action.ProviderSeat || declared.ActorSeat != action.ActorSeat || declared.DeclaredKind != paid.DeclaredKind ||
            !declared.TargetSeats.SequenceEqual(action.EffectiveDesignatedTargetSeats) ||
            history.OfType<CardDeclarationRevealedEvent>().Any(e => e.DeclarationId == paid.DeclarationId && !e.Succeeded)) return false;
        return paid.DeclaredKind == action.EffectiveKind || paid.DeclaredKind == CardKind.Slash && action.EffectiveKind == CardKind.FireSlash &&
            history.OfType<ZhuqueFanConvertedEvent>().Any(e => e.ResolutionId == useId && e.SourceSeat == action.ActorSeat &&
                e.PhysicalCardIds.SequenceEqual([cost.CardId]) && e.TargetSeats.SequenceEqual(action.EffectiveDesignatedTargetSeats));
    }

    private bool ValidCardSupplyCompletionWindow(ProgramCardTriggerWindowFrame window)
    {
        if (window.CardSupplyCompletion is not { } r || window.ResponseCompletion is not null || window.CompletedResponseReturn is not null ||
            window.Continuation != ProgramCardContinuation.CommittedSlash || window.ParentFrameId != r.ParentFrameId || window.AttackOwnerFrameId != r.ParentFrameId ||
            r.ActualTurnNumber != _turnNumber || r.ActualTurnOwnerSeat != _currentSeat || r.RequesterSeat == r.ProviderSeat || !IsValidPlayerSeat(r.ProviderSeat) ||
            r.Purpose is not (FactionCardRequestPurpose.ProgramSkillUse or FactionCardRequestPurpose.AssistedProgramUse or FactionCardRequestPurpose.BorrowedSwordUse or FactionCardRequestPurpose.QinglongCrescentBladeUse) ||
            LifecycleCardUse(r.ParentFrameId) is not { Action: { } native, CardAttack: not null } use || use.CompletedCardSupplyFrameId is not null ||
            window.Action is not { Type: CardActionType.Use } a || a.ActionId != r.ActionId || a.ActorSeat != r.RequesterSeat || a.ProviderSeat != r.ProviderSeat ||
            a.RequesterSeat != r.RequesterSeat || a.EffectiveKind != r.EffectiveKind || !IsSlashCard(r.EffectiveKind) || native.ActionId != a.ActionId ||
            native.ActorSeat != a.ActorSeat || native.ProviderSeat != a.ProviderSeat || native.RequesterSeat != a.RequesterSeat || native.EffectiveKind != a.EffectiveKind ||
            !native.PhysicalCards.SequenceEqual(a.PhysicalCards) || !native.ConversionChain.SequenceEqual(a.ConversionChain) || !native.TargetSeats.SequenceEqual(a.TargetSeats) ||
            a.PhysicalCards.Count == 0 || a.PhysicalCards.Any(c => c.From.OwnerSeat != r.ProviderSeat || c.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) ||
            r.NativeCosts.Count != a.PhysicalCards.Count || r.NativeCosts.Select(m => m.Sequence).Distinct().Count() != r.NativeCosts.Count ||
            !a.PhysicalCards.All(c => IsCardSupplyCompletionCostPaid(window, c))) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<CardUseDeclaredEvent>().Where(e => e.ResolutionId == use.Id).ToArray() is not [var declared] ||
            declared.SourceSeat != r.RequesterSeat || declared.CardKind != r.EffectiveKind || declared.CardId != use.CardId ||
            history.OfType<CardSupplyCompletionStartedEvent>().Where(e => e.ActionId == r.ActionId).ToArray() is not [var started] ||
            started != new CardSupplyCompletionStartedEvent(window.Id, r.ActionId, r.ParentFrameId, r.RequestOwnerFrameId, r.Purpose,
                r.RequesterSeat, r.ProviderSeat, r.EffectiveKind, r.ActualTurnNumber, r.ActualTurnOwnerSeat) ||
            r.CostBatchCursor < 0 || r.CostBatchCursor > r.CostBatches.Count || r.CostRecoveryCursor < 0 || r.CostRecoveryCursor > r.CostRecoveries.Count ||
            r.CostHealthCursor < 0 || r.CostHealthCursor > r.CostHealthChanges.Count || r.CostBatches.Select(b => b.Id).Distinct().Count() != r.CostBatches.Count ||
            r.CostBatches.Any(b => b.ParentFrameId != r.ParentFrameId || b.AwaitingProgramFrameId is not null || b.Movements.Count == 0 ||
                !b.Movements.Any(m => r.NativeCosts.Contains(m)) || b.Movements.Any(m => !_cardMovements.Contains(m))) ||
            r.CostRecoveries.Any(a2 => !IsResponseCompletionSilverLionAttempt(r.NativeCosts, a2)) ||
            r.CostHealthChanges.Any(h => h.ParentFrameId != r.ParentFrameId || !IsResponseCompletionSilverLionHealth(r.NativeCosts, h))) return false;
        var completed = history.OfType<CardSupplyCompletedEvent>().Where(e => e.ActionId == r.ActionId).ToArray();
        if (r.CostsDrained) return r.CostBatchCursor == r.CostBatches.Count && r.CostRecoveryCursor == r.CostRecoveries.Count &&
            r.CostHealthCursor == r.CostHealthChanges.Count && r.ActiveHealthChildFrameId is null && r.ActiveCostChildFrameId is null && completed is [var fact] && fact == CardSupplyCompletionFact(window);
        if (window.Candidates.Count != 0 || window.CandidateIndex != 0 || completed.Length != 0) return false;
        if (r.ActiveHealthChildFrameId is { } health)
            return r.ActiveCostChildFrameId is null && _resolutionStack.SingleOrDefault(f => f.Id == health) is { } child && IsCardSupplyCompletionHealthChild(window, child);
        return r.ActiveCostChildFrameId is not { } movement || _resolutionStack.SingleOrDefault(f => f.Id == movement) is { } moved && CardSupplyCompletionFirstChild(window, moved);
    }

    private bool TryDrainCardSupplyCompletionCosts(ProgramCardTriggerWindowFrame supplied)
    {
        if (supplied.CardSupplyCompletion is not { CostsDrained: false }) return false;
        var window = supplied;
        if (_resolutionStack.LastOrDefault()?.Id != window.Id || !ValidCardSupplyCompletionWindow(window)) throw new InvalidOperationException("A supplied play lost its exact paid cost prelude.");
        var r = window.CardSupplyCompletion!;
        if (r.ActiveCostChildFrameId is not null || r.ActiveHealthChildFrameId is not null) throw new InvalidOperationException("A supplied play cannot skip its actual native cost child.");
        if (TryDrainCardSupplyCompletionHealth(window)) return true;
        window = (ProgramCardTriggerWindowFrame)_resolutionStack[^1]; r = window.CardSupplyCompletion!;
        while (r.CostBatchCursor < r.CostBatches.Count)
        {
            var batch = r.CostBatches[r.CostBatchCursor]; var queued = _pendingCardsMovedBatches.SingleOrDefault(b => b.Id == batch.Id);
            if (queued is null || !queued.Movements.SequenceEqual(batch.Movements)) throw new InvalidOperationException("A supplied play lost its real queued Use movement batch.");
            _pendingCardsMovedBatches.Remove(queued); var child = CreateCardsMovedProgramWindow(queued, null);
            if (child is null) { r = r with { CostBatchCursor = r.CostBatchCursor + 1 }; ReplaceRuntimeTop(window = window with { CardSupplyCompletion = r }); continue; }
            child = child with { ResumeCardSupplyCompletionFrameId = window.Id };
            ReplaceRuntimeTop(window with { CardSupplyCompletion = r with { ActiveCostChildFrameId = child.Id } });
            PushRuntimeFrame(child); AdvanceRuntimeTop<CardsMovedTriggerWindowFrame>(); return true;
        }
        var candidates = CollectSharedCardActionCandidates(CardSupplyCompletionActorAction(window), SkillProgramTriggerWindow.CardSupplyCompleted, [], null)
            .OrderByDescending(c => c.Priority).ThenBy(c => c.OwnerSeat).ThenBy(c => c.SkillId, StringComparer.Ordinal).ThenBy(c => c.SkillInstanceId, StringComparer.Ordinal)
            .ThenBy(c => c.TriggerId, StringComparer.Ordinal).Select(c => c.FrozenContext is { CardUse: { } use } context ? c with {
                FrozenContext = context with { ParentFrameId = window.Id, CardUse = use with { ParentCardUseFrameId = window.ParentFrameId } } } :
                throw new InvalidOperationException("A supplied play candidate lost its frozen provider context.")).ToArray();
        ReplaceRuntimeTop(window = window with { CardSupplyCompletion = r with { CostsDrained = true }, Candidates = Array.AsReadOnly(candidates) });
        AdvanceEventRulesAndQueueFact(CardSupplyCompletionFact(window)); return false;
    }

    private bool TryDrainCardSupplyCompletionHealth(ProgramCardTriggerWindowFrame supplied)
    {
        var window = supplied; var r = window.CardSupplyCompletion!;
        while (r.CostRecoveryCursor < r.CostRecoveries.Count)
        {
            var request = r.CostRecoveries[r.CostRecoveryCursor]; var native = _resolutionStack.Single(f => f.Id == r.ParentFrameId);
            var actual = native.PendingRecoveryAttempts?.SingleOrDefault(a => a.Id == request.Id);
            if (actual is null || !SameResponseCompletionRecovery(actual, request)) throw new InvalidOperationException("A supplied play lost its queued Silver Lion recovery.");
            var remaining = native.PendingRecoveryAttempts!.Where(a => a.Id != request.Id).ToArray();
            ReplaceRuntimeFrame(native.Id, native with { PendingRecoveryAttempts = remaining.Length == 0 ? null : Array.AsReadOnly(remaining) });
            ReplaceRuntimeTop(window with { CardSupplyCompletion = r with { ActiveHealthChildFrameId = request.Id } });
            PushRuntimeFrame(new RecoveryReplacementFrame(request.Id, window.Id, request, new(PostEventContinuation.CardSupplyCompletion, window.Id)));
            AdvanceRuntimeFrame(request.Id); return true;
        }
        while (r.CostHealthCursor < r.CostHealthChanges.Count)
        {
            var change = r.CostHealthChanges[r.CostHealthCursor];
            if (!_pendingHpChanges.Remove(change)) throw new InvalidOperationException("A supplied play lost its actual cost HP invoice.");
            var owner = _players[change.TargetSeat]; var facts = CaptureProgramTriggerFacts(owner) with {
                HpChangeAmount = change.Amount, HpBeforeChange = change.HpBefore, HpAfterChange = change.HpAfter };
            var candidates = owner.IsAlive ? new[] { SkillProgramTriggerWindow.AfterHpRecovered, SkillProgramTriggerWindow.AfterHealthChanged }
                .SelectMany(w => CollectProgramTriggerCandidates(owner, w)).Where(c => GetProgramTrigger(c).Condition.Evaluate(facts, c.SkillId, c.SkillInstanceId))
                .SelectMany(c => Enumerable.Range(0, GetProgramTrigger(c).HpChangeOccurrence == SkillProgramHpChangeOccurrence.PerPoint ? change.Amount : 1).Select(i => c with { OccurrenceIndex = i })).ToArray() : [];
            if (candidates.Length == 0) { r = r with { CostHealthCursor = r.CostHealthCursor + 1 }; ReplaceRuntimeTop(window = window with { CardSupplyCompletion = r }); continue; }
            var contexts = candidates.Select(c => new ProgramSkillWindowContext(GetProgramTrigger(c).Window, change.Id, owner.Seat,
                SourceSeat: change.SourceSeat, TargetSeat: owner.Seat, Amount: change.Amount, OccurrenceIndex: c.OccurrenceIndex, Facts: facts, HpChange: change)).ToArray();
            ReplaceRuntimeTop(window with { CardSupplyCompletion = r with { ActiveHealthChildFrameId = change.Id } });
            PushRuntimeFrame(new HpChangedTriggerWindowFrame(change.Id, change, Array.AsReadOnly(candidates), Array.AsReadOnly(contexts), PostEventContinuation.CardSupplyCompletion, window.Id));
            AdvanceRuntimeTop<HpChangedTriggerWindowFrame>(); return true;
        }
        return false;
    }
    private bool IsCardSupplyCompletionHealthChild(ProgramCardTriggerWindowFrame window, ResolutionFrame child)
    {
        if (window.CardSupplyCompletion is not { CostsDrained: false, ActiveHealthChildFrameId: { } id } r || child.Id != id || r.ActiveCostChildFrameId is not null) return false;
        if (child is RecoveryReplacementFrame recovery)
            return r.CostRecoveryCursor >= 0 && r.CostRecoveryCursor < r.CostRecoveries.Count && recovery.ParentFrameId == window.Id &&
                recovery.Return == new RecoveryReplacementReturn(PostEventContinuation.CardSupplyCompletion, window.Id) &&
                SameResponseCompletionRecovery(recovery.Attempt, r.CostRecoveries[r.CostRecoveryCursor]) && IsResponseCompletionSilverLionAttempt(r.NativeCosts, recovery.Attempt);
        return child is HpChangedTriggerWindowFrame hp && r.CostRecoveryCursor == r.CostRecoveries.Count && r.CostHealthCursor >= 0 && r.CostHealthCursor < r.CostHealthChanges.Count &&
            hp.Continuation == PostEventContinuation.CardSupplyCompletion && hp.ResumeFrameId == window.Id && hp.CardId is null && hp.CardKind is null &&
            hp.Change == r.CostHealthChanges[r.CostHealthCursor] && hp.Change.ParentFrameId == r.ParentFrameId && IsResponseCompletionSilverLionHealth(r.NativeCosts, hp.Change);
    }
    private bool CardSupplyCompletionFirstChild(ProgramCardTriggerWindowFrame window, ResolutionFrame child) =>
        IsCardSupplyCompletionHealthChild(window, child) || window.CardSupplyCompletion is { CostsDrained: false, ActiveCostChildFrameId: { } id } r &&
        r.CostBatchCursor >= 0 && r.CostBatchCursor < r.CostBatches.Count && child is CardsMovedTriggerWindowFrame moved && moved.Id == id &&
        moved.ResumeCardSupplyCompletionFrameId == window.Id && moved.ResumeResponseCompletionFrameId is null && moved.ResumeProgramFrameId is null &&
        moved.Batch.Id == r.CostBatches[r.CostBatchCursor].Id && moved.Batch.ParentFrameId == r.ParentFrameId && moved.Batch.AwaitingProgramFrameId is null &&
        moved.Batch.Movements.SequenceEqual(r.CostBatches[r.CostBatchCursor].Movements);
    private bool ReturnCardSupplyCompletionMovement(CardsMovedTriggerWindowFrame child)
    {
        if (child.ResumeCardSupplyCompletionFrameId is not { } id) return false;
        if (_resolutionStack.LastOrDefault() is not ProgramCardTriggerWindowFrame window || window.Id != id || !CardSupplyCompletionFirstChild(window, child))
            throw new InvalidOperationException("A supplied play movement lost its exact typed parent.");
        var r = window.CardSupplyCompletion!;
        ReplaceRuntimeTop(window with { CardSupplyCompletion = r with { CostBatchCursor = r.CostBatchCursor + 1, ActiveCostChildFrameId = null } });
        AdvanceRuntimeTop<ProgramCardTriggerWindowFrame>(); return true;
    }
    private bool ReturnCardSupplyCompletionRecovery(RecoveryReplacementFrame child)
    {
        if (child.Return.Continuation != PostEventContinuation.CardSupplyCompletion) return false;
        if (_resolutionStack.LastOrDefault() is not ProgramCardTriggerWindowFrame window || !IsCardSupplyCompletionHealthChild(window, child))
            throw new InvalidOperationException("A supplied play recovery lost its exact typed parent.");
        var r = window.CardSupplyCompletion!;
        ReplaceRuntimeTop(window with { CardSupplyCompletion = r with { CostRecoveryCursor = r.CostRecoveryCursor + 1, ActiveHealthChildFrameId = null } });
        AdvanceRuntimeTop<ProgramCardTriggerWindowFrame>(); return true;
    }
    private bool ReturnCardSupplyCompletionHpChange(HpChangedTriggerWindowFrame child)
    {
        if (child.Continuation != PostEventContinuation.CardSupplyCompletion) return false;
        if (_resolutionStack.LastOrDefault() is not ProgramCardTriggerWindowFrame window || !IsCardSupplyCompletionHealthChild(window, child))
            throw new InvalidOperationException("A supplied play HP observer lost its exact typed parent.");
        var r = window.CardSupplyCompletion!;
        ReplaceRuntimeTop(window with { CardSupplyCompletion = r with { CostHealthCursor = r.CostHealthCursor + 1, ActiveHealthChildFrameId = null } });
        AdvanceRuntimeTop<ProgramCardTriggerWindowFrame>(); return true;
    }
    private void ContinueCardSupplyAfterCompletion(CardAttackHandle attack, ProgramCardTriggerWindowFrame window)
    {
        if (window.CardSupplyCompletion is not { CostsDrained: true } || !ValidCardSupplyCompletionWindow(window) ||
            _resolutionStack.LastOrDefault() is not CardUseFrame use || use.Id != window.ParentFrameId || attack.ResolutionId != use.Id)
            throw new InvalidOperationException("A supplied play lost its once-paid native Slash return.");
        ReplaceRuntimeTop(use with { CompletedCardSupplyFrameId = window.Id });
        if (_winner != Winner.None) { CompleteAttack(attack); return; }
        var action = use.Action;
        if (TryPauseRecoveryPaidCardUse(use.Id, new(RecoveryPaidCardUseKind.CommittedSlash, attack.SourceSeat))) return;
        if (action is not null && TryMarkProgramUseCommitted(use.Id) && TryBeginProgramCardWindow(attack, action,
                SkillProgramTriggerWindow.CardUseCommitted, action.TargetSeats, ProgramCardContinuation.CommittedSlash)) return;
        BeginSlashTargetResolution(attack);
    }
    private bool CardSupplyCompletionStructuralEdge(ResolutionFrame parent, ResolutionFrame child) =>
        parent is ProgramCardTriggerWindowFrame { CardSupplyCompletion: not null } w && CardSupplyCompletionFirstChild(w, child) ||
        child is ProgramCardTriggerWindowFrame { CardSupplyCompletion: not null } window && window.ParentFrameId == parent.Id && ValidCardSupplyCompletionWindow(window);
    private ProgramCardTriggerWindowFrame? CardSupplyCompletionObserverRoot()
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
            if (_resolutionStack[index] is ProgramCardTriggerWindowFrame { CardSupplyCompletion: not null } root &&
                ValidCardSupplyCompletionWindow(root) && CardSupplyCompletionFirstChild(root, _resolutionStack[index + 1]) && SameNameHandObserverSuffix(index)) return root;
        return null;
    }
}
