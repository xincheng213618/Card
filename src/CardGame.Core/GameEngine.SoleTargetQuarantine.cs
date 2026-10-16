namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsValidSoleTargetProtectionPaidFact(SoleTargetProtectionReceipt r)
    {
        if (r.Cost is not { } cost || r.InstructionIndex != 1 || r.ProgramFrameId <= r.WindowFrameId || r.WindowFrameId <= r.CardUseFrameId ||
            r.ActionId <= 0 || !IsValidPlayerSeat(r.Source.OwnerSeat) || !IsValidPlayerSeat(r.ActorSeat) || !IsValidPlayerSeat(r.ProviderSeat) ||
            !IsValidPlayerSeat(r.ProtectedSeat) || r.Source.OwnerSeat == r.ActorSeat || r.Source.OwnerSeat == r.ProtectedSeat ||
            r.OwnerHp <= r.ProtectedHp || r.ActualTurnNumber <= 0 || !IsValidPlayerSeat(r.ActualTurnOwnerSeat) ||
            cost.From.OwnerSeat != r.Source.OwnerSeat || cost.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
            r.CostSequenceAfter <= r.CostSequenceBefore || _contentRegistry.GetSkill(r.Source.SkillId).Program?.GameplayHash != r.GameplayHash ||
            !ActualHandGainMutationBinding(r.ProgramFrameId, r.Source.SkillId, r.Source.OwnerSeat,
                SkillProgramTriggerWindow.CardUseTargetsFinalized, SkillProgramEffectOp.ProtectOtherSoleCardTarget) ||
            !CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Any(e => e.FrameId == r.ProgramFrameId &&
                e.SkillId == r.Source.SkillId && e.BindingId == r.Source.BindingId && e.SkillInstanceId == r.Source.SkillInstanceId && e.OwnerSeat == r.Source.OwnerSeat)) return false;
        var destination = cost.Kind == CardKind.GeneralWeapon && cost.From.Zone == CardZoneKind.Equipment ? CardLocation.OutsideGame : CardLocation.DiscardPile;
        return _cardMovements.Count(m => m.TurnNumber == r.ActualTurnNumber && m.CardId == cost.CardId && m.CardKind == cost.Kind && m.From == cost.From && m.To == destination &&
            m.Reason.Value == SoleProtectionCostReason && m.Sequence > r.CostSequenceBefore && m.Sequence <= r.CostSequenceAfter) == 1;
    }
    private bool MatchesSoleTargetProtectionOriginalTargets(long id, IReadOnlyList<int> originalTargets) =>
        LifecycleCardUse(id) is { Action: { } action, SoleTargetProtectionOrigin: { } r } use &&
        r.CardUseFrameId == id && r.ActionId == action.ActionId && r.ActorSeat == use.SourceSeat &&
        r.ProviderSeat == action.ProviderSeat && r.EffectiveKind == use.CardKind && r.OriginalTargets.SequenceEqual(originalTargets) &&
        r.Materials.All(m => action.PhysicalCards.Any(c => c.CardId == m.CardId && c.CardKind == m.Kind && c.From == m.From)) &&
        CompleteProgramEventHistory().OfType<SoleTargetProtectionPaidEvent>().Any(e => e.Receipt.CardUseFrameId == id &&
            e.Receipt.ActionId == r.ActionId && e.Receipt.OriginalTargets.SequenceEqual(originalTargets) &&
            e.Receipt.Materials.SequenceEqual(r.Materials) && IsValidSoleTargetProtectionPaidFact(e.Receipt) &&
            ActualHandGainMutationBinding(e.Receipt.ProgramFrameId, e.Receipt.Source.SkillId, e.Receipt.Source.OwnerSeat,
                SkillProgramTriggerWindow.CardUseTargetsFinalized, SkillProgramEffectOp.ProtectOtherSoleCardTarget)) &&
        CompleteProgramEventHistory().OfType<SoleTargetReplacedEvent>().Any(e => IsSoleTargetReplacementFact(use, e, originalTargets)) &&
        HasExactAcceptedActualHandGainUse(use, action);

    private bool HasExactSoleTargetQuarantine(CardUseFrame use)
    {
        if (use.SoleTargetQuarantine is not { } r || use.Action is not { } action || r.CardUseFrameId != use.Id ||
            r.ActionId != action.ActionId || r.ActorSeat != use.SourceSeat || r.ActorSeat != action.ActorSeat ||
            r.ProviderSeat != action.ProviderSeat || r.EffectiveKind != use.CardKind ||
            r.ActualTurnNumber != _turnNumber || r.ActualTurnOwnerSeat != _currentSeat ||
            !r.TargetSeats.SequenceEqual(use.TargetSeats) || !action.TargetSeats.SequenceEqual(use.TargetSeats) ||
            use.SoleTargetProtectionSources?.Contains(r.Source) != true ||
            !CompleteProgramEventHistory().OfType<SoleTargetProtectionPaidEvent>().Any(e =>
                e.Receipt.CardUseFrameId == use.Id && e.Receipt.ActionId == action.ActionId && e.Receipt.Source == r.Source &&
                e.Receipt.GameplayHash == r.GameplayHash && IsValidSoleTargetProtectionPaidFact(e.Receipt))) return false;
        if (r.Materials.Select(m => m.CardId).Distinct().Count() != r.Materials.Count ||
            r.Materials.Any(m => !action.PhysicalCards.Any(c => c.CardId == m.CardId && c.From == m.From && c.CardKind == m.Kind) ||
                !_cardMovements.Any(move => move.CardId == m.CardId && move.From == CardLocation.Processing &&
                    move.To == CardLocation.OutsideGame && move.Reason.Value == SoleProtectionQuarantineReason &&
                    move.Sequence > r.QuarantineSequenceBefore && move.Sequence <= r.QuarantineSequenceAfter))) return false;
        return CompleteProgramEventHistory().OfType<SoleTargetQuarantineIssuedEvent>().Any(e =>
            e.Receipt.CardUseFrameId == r.CardUseFrameId && e.Receipt.ActionId == r.ActionId &&
            e.Receipt.Source == r.Source && e.Receipt.Materials.SequenceEqual(r.Materials) &&
            e.Receipt.QuarantineSequenceBefore == r.QuarantineSequenceBefore && e.Receipt.QuarantineSequenceAfter == r.QuarantineSequenceAfter);
    }
    private bool IsSoleTargetQuarantinedMaterial(long id, int cardId) => LifecycleCardUse(id) is { } use &&
        HasExactSoleTargetQuarantine(use) && use.SoleTargetQuarantine!.Materials.Any(m => m.CardId == cardId) &&
        (_cardZones.GetLocation(cardId) == CardLocation.OutsideGame || IsGameEndedSoleTargetMaterial(use, cardId));

    private bool IsGameEndedSoleTargetMaterial(CardUseFrame use, int cardId) => _winner != Winner.None &&
        _cardZones.GetLocation(cardId) == CardLocation.DiscardPile && use.SoleTargetQuarantine is { } r &&
        CompleteProgramEventHistory().OfType<SoleTargetQuarantineSettledEvent>().Any(e => e.GameEnded &&
            e.CardUseFrameId == use.Id && e.ActionId == r.ActionId && e.ActorSeat == r.ActorSeat && e.Destination == CardLocation.DiscardPile &&
            e.CardIds.Contains(cardId) && _cardMovements.Count(m => m.CardId == cardId && m.Sequence > e.SequenceBefore && m.Sequence <= e.SequenceAfter &&
                m.From == CardLocation.OutsideGame && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.sole-target-protection.game-end") == 1);

    private bool TryCompleteSoleTargetQuarantinedUse(long id)
    {
        var use = LifecycleCardUse(id);
        if (use?.SoleTargetQuarantine is null || use.Step == ResolutionFrameStep.Completed) return false;
        if (_resolutionStack.LastOrDefault()?.Id != id || !HasExactSoleTargetQuarantine(use) ||
            use.SoleTargetQuarantine.Materials.Any(m => !IsSoleTargetQuarantinedMaterial(use.Id, m.CardId)))
            throw new InvalidOperationException("A quarantined use lost its exact paid material issuance or owning completion.");
        var attack = use.CardAttack is null ? null : new CardAttackHandle(this, id);
        var completion = attack is null ? null : CaptureAttackCompletion(attack);
        var card = use.CardId > 0 ? GetAttackCard(use.CardId) : new Card(0, use.CardKind, Suit.None, 0);
        QueueCardUseFinishedWithoutPop(id, card, use.CardKind);
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!, SkillProgramTriggerWindow.CardUseCompleted,
            use.TargetSeats, attack is null ? ProgramCardContinuation.CompletedCard : ProgramCardContinuation.CompletedSlash)) return true;
        PopFinishedCardUse(id);
        if (attack is not null) CompleteFinishedAttackCardUse(attack);
        if (completion is not null) CompleteAttackAfterCardResolution(completion);
        return true;
    }

    private SoleTargetQuarantineReceipt[] OutstandingSoleTargetQuarantines(int? turn = null, int? owner = null)
    {
        if (!HasSoleTargetProtectionCapability) return [];
        var history = CompleteProgramEventHistory().ToArray();
        var settled = history.OfType<SoleTargetQuarantineSettledEvent>().Select(e => (e.CardUseFrameId, e.ActionId)).ToHashSet();
        return history.OfType<SoleTargetQuarantineIssuedEvent>().Select(e => e.Receipt)
            .Where(r => !settled.Contains((r.CardUseFrameId, r.ActionId)) && (turn is null || r.ActualTurnNumber == turn) &&
                (owner is null || r.ActualTurnOwnerSeat == owner)).OrderBy(r => r.CardUseFrameId).ToArray();
    }
    private void SettleSoleTargetQuarantine(SoleTargetQuarantineReceipt r, bool gameEnded)
    {
        if (r.Materials.Any(m => _cardZones.GetLocation(m.CardId) != CardLocation.OutsideGame))
            throw new InvalidOperationException("A due quarantine lost one of its actual isolated materials.");
        var destination = !gameEnded && _players[r.ActorSeat].IsAlive ? CardLocation.Hand(r.ActorSeat) : CardLocation.DiscardPile;
        var before = _movementSequence;
        if (r.Materials.Count > 0) MoveCards(r.Materials.Select(m => GetAttackCard(m.CardId)).ToArray(), CardLocation.OutsideGame,
            destination, new(gameEnded ? "skill-program.sole-target-protection.game-end" : "skill-program.sole-target-protection.return"));
        AdvanceEventRulesAndQueueFact(new SoleTargetQuarantineSettledEvent(r.CardUseFrameId, r.ActionId, r.ActorSeat,
            r.ActualTurnNumber, r.ActualTurnOwnerSeat, destination, Array.AsReadOnly(r.Materials.Select(m => m.CardId).ToArray()),
            before, _movementSequence, gameEnded));
    }
    private void SettleGameEndedSoleTargetQuarantines()
    { foreach (var r in OutstandingSoleTargetQuarantines()) SettleSoleTargetQuarantine(r, true); }

    private bool ContinueSoleTargetQuarantineReturns(DeferredTurnEndFrame parent)
    {
        var cursor = parent.SoleTargetQuarantineReturns;
        var afterEnded = parent.AfterTurnEnded is not null;
        var resumedAfterEnded = afterEnded &&
            _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveDianjunActualTurnEnd) &&
            IsActualAfterTurnEndedParent(parent) && parent.AfterTurnEnded!.CurrentChild is null &&
            parent.AfterTurnEnded.ItemIndex >= 0 && parent.AfterTurnEnded.ItemIndex <= parent.AfterTurnEnded.Items.Count;
        if (resumedAfterEnded && (cursor is null || cursor.Index == cursor.Receipts.Count))
        {
            if (_resolutionStack.LastOrDefault() != parent)
                throw new InvalidOperationException("Late quarantine return requires its exact resumed actual-end parent.");
            // The original cursor was frozen before Dianjun's end-owned Play.
            // Add only still-outstanding receipts from this same actual turn;
            // retain the consumed prefix and the original after-end window.
            var late = OutstandingSoleTargetQuarantines(parent.TurnNumber, parent.OwnerSeat);
            if (late.Length > 0)
            {
                var history = CompleteProgramEventHistory().ToArray();
                if (!history.OfType<ZhangYiResolutionStartedEvent>().Any(start =>
                    start.ParentWindowId == parent.Id && start.ActualTurnNumber == parent.TurnNumber &&
                    start.Source.OwnerSeat == parent.OwnerSeat &&
                    history.OfType<ZhangYiDianjunCleanupIssuedEvent>().Any(cleaned => cleaned.FrameId == start.FrameId &&
                        cleaned.OwnerSeat == parent.OwnerSeat && cleaned.ActualTurnNumber == parent.TurnNumber)))
                    throw new InvalidOperationException("A late quarantine lacks its completed end-owned Play return.");
                if (cursor is not null && late.Any(r => cursor.Receipts.Any(old => old.CardUseFrameId == r.CardUseFrameId && old.ActionId == r.ActionId)))
                    throw new InvalidOperationException("A consumed quarantine cursor retained an unsettled receipt.");
                cursor = cursor is null ? new(Array.AsReadOnly(late)) : cursor with
                    { Receipts = Array.AsReadOnly(cursor.Receipts.Concat(late).ToArray()) };
                ReplaceRuntimeTop(parent = parent with { SoleTargetQuarantineReturns = cursor });
            }
        }
        if (cursor is null) return false;
        if (cursor.MovementChildFrameId is not null || parent.OwnerSeat != _currentSeat || parent.TurnNumber != _turnNumber ||
            parent.Current is not null || afterEnded && !resumedAfterEnded || parent.Prelude is { Completed: false } ||
            !CompleteProgramEventHistory().OfType<TurnEndedEvent>().Any(e => e.ActorSeat == parent.OwnerSeat && e.TurnNumber == parent.TurnNumber))
            throw new InvalidOperationException("Quarantine return requires its actual completed turn and drained owned movement child.");
        while (true)
        {
            while (_pendingCardsMovedBatches.Any(b => b.AwaitingProgramFrameId is null && b.ParentFrameId == parent.Id))
            {
                var batch = _pendingCardsMovedBatches.Where(b => b.AwaitingProgramFrameId is null && b.ParentFrameId == parent.Id).OrderBy(b => b.Id).First();
                _pendingCardsMovedBatches.Remove(batch);
                var child = CreateCardsMovedProgramWindow(batch);
                if (child is null) continue;
                cursor = cursor with { MovementChildFrameId = child.Id, MovementBatchId = batch.Id, CurrentBatch = batch };
                ReplaceRuntimeTop(parent with { SoleTargetQuarantineReturns = cursor });
                PushRuntimeFrame(child with { SoleTargetQuarantineEndReturn = parent.Id });
                AdvanceRuntimeTop<CardsMovedTriggerWindowFrame>(); return true;
            }
            if (cursor.Index == cursor.Receipts.Count) return false;
            var r = cursor.Receipts[cursor.Index];
            if (r.ActualTurnNumber != parent.TurnNumber || r.ActualTurnOwnerSeat != parent.OwnerSeat ||
                !OutstandingSoleTargetQuarantines(parent.TurnNumber, parent.OwnerSeat).Any(x => x.CardUseFrameId == r.CardUseFrameId && x.ActionId == r.ActionId))
                throw new InvalidOperationException("An actual-end cursor retained a premature or already settled use.");
            cursor = cursor with { Index = cursor.Index + 1 };
            ReplaceRuntimeTop(parent = parent with { SoleTargetQuarantineReturns = cursor });
            SettleSoleTargetQuarantine(r, false);
        }
    }
    private bool MatchesSoleTargetQuarantineReturn(DeferredTurnEndFrame parent, CardsMovedTriggerWindowFrame child) =>
        parent.SoleTargetQuarantineReturns is { CurrentBatch: { } batch } cursor && parent.OwnerSeat == _currentSeat &&
        parent.TurnNumber == _turnNumber && _phase == TurnPhase.Finished && cursor.Index > 0 && cursor.Index <= cursor.Receipts.Count &&
        cursor.MovementChildFrameId == child.Id && cursor.MovementBatchId == child.Batch.Id &&
        child.SoleTargetQuarantineEndReturn == parent.Id && child.ResumeProgramFrameId is null &&
        child.Batch.ParentFrameId == parent.Id && child.Batch.AwaitingProgramFrameId is null &&
        SameDeferredTurnEndBatch(batch, child.Batch);
    private void ReturnSoleTargetQuarantineMovement(CardsMovedTriggerWindowFrame child)
    {
        if (_resolutionStack.LastOrDefault() is not DeferredTurnEndFrame parent || !MatchesSoleTargetQuarantineReturn(parent, child) ||
            child.CandidateIndex != child.Candidates.Count) throw new InvalidOperationException("A quarantine gain lost its exact actual-end return.");
        ReplaceRuntimeTop(parent with { SoleTargetQuarantineReturns = parent.SoleTargetQuarantineReturns! with
            { MovementChildFrameId = null, MovementBatchId = null, CurrentBatch = null } });
        ContinueDeferredTurnEnd();
    }
    private bool HasSoleTargetQuarantineEndBoundary(DeferredTurnEndFrame parent)
    {
        if (_resolutionStack.Count < 2 || _resolutionStack[1] is not CardsMovedTriggerWindowFrame child ||
            !MatchesSoleTargetQuarantineReturn(parent, child)) throw new InvalidOperationException("The quarantine return boundary lost its exact movement child.");
        return true;
    }

    private void RefreshSoleProtectionObservers(CardUseFrame use)
    {
        var window = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last(w => w.ParentFrameId == use.Id && GetCardActionWindow(w) == SkillProgramTriggerWindow.CardUseTargetsFinalized);
        var extra = CollectSharedCardActionCandidates(use.Action!, SkillProgramTriggerWindow.CardUseTargetsFinalized, SoleProtectionMainTargets(use), null, window.Continuation)
            .Where(c => _contentRegistry.GetSkill(c.SkillId).Program!.Triggers.Single(t => t.Id == c.TriggerId).Effects.Any(e => e.Op == SkillProgramEffectOp.ProtectOtherSoleCardTarget) &&
                use.SoleTargetProtectionSources?.Contains(new(c.SkillId, c.TriggerId, c.OwnerSeat, c.SkillInstanceId)) != true &&
                !window.Candidates.Skip(window.CandidateIndex + 1).Any(old => old.OwnerSeat == c.OwnerSeat && old.SkillId == c.SkillId && old.TriggerId == c.TriggerId && old.SkillInstanceId == c.SkillInstanceId))
            .Select(c => c with { FrozenContext = c.FrozenContext! with { ParentFrameId = window.Id, CardUse = c.FrozenContext.CardUse! with { ParentCardUseFrameId = use.Id } } }).ToArray();
        if (extra.Length > 0) ReplaceRuntimeFrame(window.Id, window with { Candidates = Array.AsReadOnly(window.Candidates.Concat(extra).ToArray()) });
    }

    private bool IsSoleTargetReplacementFact(CardUseFrame use, SoleTargetReplacedEvent fact, IReadOnlyList<int> before)
    {
        if (fact.CardUseFrameId != use.Id || fact.ActionId != use.Action?.ActionId || !fact.BeforeTargets.SequenceEqual(before) ||
            fact.PreviousTargetSeat == fact.NewTargetSeat || fact.NewTargetSeat != fact.Source.OwnerSeat ||
            use.SoleTargetProtectionSources?.Contains(fact.Source) != true) return false;
        var expected = before.Select((s, i) => s == fact.PreviousTargetSeat && (use.CardKind != CardKind.BorrowedSword || i % 2 == 0) ? fact.NewTargetSeat : s);
        if (!expected.SequenceEqual(fact.AfterTargets)) return false;
        return CompleteProgramEventHistory().OfType<SoleTargetProtectionPaidEvent>().Count(e =>
            e.Receipt.CardUseFrameId == use.Id && e.Receipt.ActionId == fact.ActionId && e.Receipt.Source == fact.Source &&
            e.Receipt.ProtectedSeat == fact.PreviousTargetSeat && e.Receipt.OriginalTargets.SequenceEqual(before) &&
            IsValidSoleTargetProtectionPaidFact(e.Receipt) && ActualHandGainMutationBinding(e.Receipt.ProgramFrameId, fact.Source.SkillId, fact.Source.OwnerSeat,
                SkillProgramTriggerWindow.CardUseTargetsFinalized, SkillProgramEffectOp.ProtectOtherSoleCardTarget)) == 1;
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 70)]
    private void AssertSoleTargetProtection(ProgramSkillFrame f)
    {
        if (f.SoleTargetProtection is not { } r) return;
        if (!HasExactSoleProtectionRoot(f) || f.InstructionIndex != 1 || GetProgramTrigger(f).Effects is not [{ Op: SkillProgramEffectOp.ProtectOtherSoleCardTarget }] ||
            r.Materials.Select(m => m.CardId).Distinct().Count() != r.Materials.Count || r.ActorSeat == f.OwnerSeat ||
            r.ProtectedSeat == f.OwnerSeat || r.OwnerHp <= r.ProtectedHp ||
            (r.Stage is SoleTargetProtectionStage.ChoosingCost or SoleTargetProtectionStage.ChoosingBranch) == (f.PendingMovementContinuation is not null))
            throw new InvalidOperationException("Sole-target protection lost its exact frozen source, effect, cost or continuation stage.");
        if (ReferenceEquals(f, _resolutionStack.LastOrDefault()) && r.Stage is SoleTargetProtectionStage.ChoosingCost or SoleTargetProtectionStage.ChoosingBranch &&
            (_pendingDecision is not { IsPrivate: true } p || p.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual(p.Choices, SoleProtectionChoices(f))))
            throw new InvalidOperationException("Sole-target protection lost its private owning prompt.");
    }
}
