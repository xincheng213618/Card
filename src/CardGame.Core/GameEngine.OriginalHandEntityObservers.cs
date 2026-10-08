namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool OriginalHandBindingStarted(ProgramSkillFrame f, SkillProgramTriggerWindow window) =>
        CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat &&
            e.SkillId == f.SkillId && e.BindingId == f.TriggerId && e.SkillInstanceId == f.SkillInstanceId && e.Window == window) == 1;

    private bool ExactOriginalHandLifecycleParent(ProgramSkillFrame f, SkillProgramTriggerWindow window)
    {
        if (f.TriggerId is null || f.WindowContext is not { } context || context.Window != window || context.OwnerSeat != f.OwnerSeat ||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } parent ||
            parent.Window != window || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            !MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex]) || context.SourceSeat != parent.OwnerSeat ||
            context.TargetSeat != parent.OwnerSeat) return false;
        if (window == SkillProgramTriggerWindow.GameStarting &&
            (parent.Continuation != ProgramLifecycleContinuation.CompleteGameStarting || _phase != TurnPhase.NotStarted)) return false;
        if (window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow &&
            (parent.Continuation != ProgramLifecycleContinuation.NormalTurnStart || parent.OwnerSeat != f.OwnerSeat || _currentSeat != f.OwnerSeat)) return false;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        return index > 0 && _resolutionStack[index - 1].Id == parent.Id;
    }
    private bool ExactOriginalHandBenefitParent(ProgramSkillFrame f)
    {
        if (f.TriggerId is null || f.WindowContext is not { Window: SkillProgramTriggerWindow.CardsMoved, MovementBatch: { } batch,
                MovementIndex: { } occurrence } context || context.OwnerSeat != f.OwnerSeat || context.SourceSeat != f.OwnerSeat ||
            context.TargetSeat != f.OwnerSeat || occurrence < 0 || occurrence >= batch.Movements.Count || context.OccurrenceIndex != occurrence ||
            _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } parent ||
            parent.Batch.Id != batch.Id || !parent.Batch.Movements.SequenceEqual(batch.Movements) ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            !MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex]) ||
            parent.Candidates[parent.CandidateIndex].OccurrenceIndex != occurrence) return false;
        var movement = batch.Movements[occurrence]; var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        return index > 0 && _resolutionStack[index - 1].Id == parent.Id && _cardMovements.Contains(movement) &&
            movement.From == CardLocation.Hand(f.OwnerSeat) && movement.To != movement.From;
    }
    private bool ExactOriginalHandInheritanceParent(ProgramSkillFrame f)
    {
        if (f.TriggerId is null || f.WindowContext is not { Window: SkillProgramTriggerWindow.OwnerDied } context ||
            context.OwnerSeat != f.OwnerSeat || context.TargetSeat != f.OwnerSeat || _players[f.OwnerSeat].IsAlive ||
            _resolutionStack.OfType<ProgramDeathTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } parent ||
            parent.OwnerSeat != f.OwnerSeat || parent.KillerSeat != context.SourceSeat || parent.CandidateIndex < 0 ||
            parent.CandidateIndex >= parent.Candidates.Count || !MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex])) return false;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        return index > 1 && _resolutionStack[index - 1].Id == parent.Id && _resolutionStack[index - 2] is DeathFrame death &&
            death.Id == parent.DeathFrameId && death.VictimSeat == f.OwnerSeat;
    }

    private void AssertOriginalHandEntityState()
    {
        if (!TracksOriginalHandEntities && _originalHandEntityStates.Count == 0 && _originalHandPermanentBonuses.Count == 0)
            return;
        var history = CompleteProgramEventHistory().Where(fact => fact is
            OriginalHandEntitiesInitializedEvent or OriginalHandEntityConsumedEvent or
            OriginalHandPermanentBonusGrantedEvent or OriginalHandBonusTransferredEvent).ToArray();
        var initialized = history.OfType<OriginalHandEntitiesInitializedEvent>().ToArray();
        if (initialized.Length != _originalHandEntityStates.Count)
            throw new InvalidOperationException("Original-hand durable states lost their unique native initialization facts.");
        foreach (var pair in _originalHandEntityStates)
        {
            var key = pair.Key; var state = pair.Value;
            var dealt = _cardMovements.Where(m => m.Reason == CardMoveReasons.InitialDeal && m.From == CardLocation.DrawPile &&
                m.To == CardLocation.Hand(key.OwnerSeat)).OrderBy(m => m.Sequence).ToArray();
            var facts = initialized.Where(e => e.Source.OwnerSeat == key.OwnerSeat && e.Source.SkillId == key.SkillId && e.StateId == key.StateId).ToArray();
            if (!IsValidPlayerSeat(key.OwnerSeat) || state.Source.OwnerSeat != key.OwnerSeat || state.Source.SkillId != key.SkillId ||
                state.InitializationFrameId <= 0 || facts is not [var init] || init.FrameId != state.InitializationFrameId ||
                init.Source != state.Source || init.GameplayHash != state.GameplayHash || init.OriginalCount != dealt.Length ||
                init.RemainingCount < 0 || init.RemainingCount > init.OriginalCount || state.Entities.Count != dealt.Length ||
                state.Entities.Select(e => e.CardId).Distinct().Count() != state.Entities.Count)
                throw new InvalidOperationException("Original-hand durable state changed its actual initial-deal invoice.");
            for (var index = 0; index < dealt.Length; index++)
            {
                var entity = state.Entities[index]; var deal = dealt[index];
                var first = _cardMovements.FirstOrDefault(m => m.Sequence > deal.Sequence && m.CardId == deal.CardId &&
                    m.From == CardLocation.Hand(key.OwnerSeat) && m.To != m.From)?.Sequence;
                if (entity.CardId != deal.CardId || entity.DealSequence != deal.Sequence || entity.FirstLossSequence != first)
                    throw new InvalidOperationException("An original physical entity was restored or consumed without its first actual hand loss.");
            }
            var losses = history.OfType<OriginalHandEntityConsumedEvent>().Where(e => e.OwnerSeat == key.OwnerSeat &&
                e.SkillId == key.SkillId && e.StateId == key.StateId).ToArray();
            if (losses.Select(e => e.MovementSequence).Distinct().Count() != losses.Length ||
                losses.Any(e => !state.Entities.Any(entity => entity.FirstLossSequence == e.MovementSequence)) ||
                !losses.Select(e => e.RemainingCount).SequenceEqual(Enumerable.Range(0, losses.Length).Select(i => init.RemainingCount - i - 1)) ||
                init.RemainingCount - losses.Length != state.Entities.Count(e => e.FirstLossSequence is null))
                throw new InvalidOperationException("Original-hand depletion lost its once-only sequence or remaining count.");
        }
        var expected = new Dictionary<long, OriginalHandPermanentBonus>();
        foreach (var fact in history)
        {
            if (fact is OriginalHandPermanentBonusGrantedEvent grant)
            {
                if (grant.Kind is not (OriginalHandBenefitKind.HandLimit or OriginalHandBenefitKind.AttackRange) ||
                    !IsValidPlayerSeat(grant.RecipientSeat) || !_originalHandEntityStates.ContainsKey(new(grant.SourceOwnerSeat, grant.SourceSkillId, grant.StateId)) ||
                    !expected.TryAdd(grant.FrameId, new(grant.FrameId, grant.SourceOwnerSeat, grant.SourceSkillId, grant.StateId, grant.RecipientSeat, grant.Kind)))
                    throw new InvalidOperationException("Original-hand permanent bonuses lost their unique source attribution.");
            }
            if (fact is OriginalHandBonusTransferredEvent transfer)
            {
                if (!expected.TryGetValue(transfer.BonusId, out var previous) || previous.SourceOwnerSeat != transfer.SourceOwnerSeat ||
                    previous.SourceSkillId != transfer.SourceSkillId || previous.StateId != transfer.StateId || previous.Kind != transfer.Kind ||
                    previous.RecipientSeat != transfer.PreviousRecipientSeat || transfer.RecipientSeat == previous.RecipientSeat || !IsValidPlayerSeat(transfer.RecipientSeat))
                    throw new InvalidOperationException("Original-hand inheritance changed the identity or previous recipient of a received bonus.");
                expected[transfer.BonusId] = previous with { RecipientSeat = transfer.RecipientSeat };
            }
        }
        if (expected.Count != _originalHandPermanentBonuses.Count || expected.Any(p => _originalHandPermanentBonuses.GetValueOrDefault(p.Key) != p.Value))
            throw new InvalidOperationException("Original-hand permanent bonuses diverged from their actual grants and transfers.");
    }

    private bool ValidOriginalHandBenefitReceipt(ProgramSkillFrame f)
    {
        if (f.OriginalHandBenefit is not { } r || f.InstructionIndex != 1 || r.InstructionIndex != 1 || !ExactOriginalHandBenefitParent(f) ||
            r.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) || r.GameplayHash != f.GameplayHash ||
            !Enum.IsDefined(r.Stage) || GetProgramTrigger(f).Effects is not [{ Op: SkillProgramEffectOp.OfferOriginalHandLossBenefit } effect] ||
            effect.StateId != r.StateId || r.MovementWindowId != f.WindowContext!.ParentFrameId || r.BatchId != f.WindowContext.MovementBatch!.Id ||
            r.MovementSequence != f.WindowContext.MovementBatch.Movements[f.WindowContext.MovementIndex!.Value].Sequence ||
            !_originalHandEntityStates.TryGetValue(new(f.OwnerSeat, f.SkillId, r.StateId), out var state) ||
            !state.Entities.Any(e => e.CardId == f.WindowContext.MovementBatch.Movements[f.WindowContext.MovementIndex.Value].CardId && e.FirstLossSequence == r.MovementSequence) ||
            OriginalHandLossEligibility(f.OwnerSeat, f.SkillId, r.StateId, f.TriggerId!, r.MovementSequence) is not { } eligibility ||
            eligibility.SkillInstanceId != f.SkillInstanceId || eligibility.GameplayHash != f.GameplayHash ||
            r.CandidateSeats.Count == 0 || r.CandidateSeats.Distinct().Count() != r.CandidateSeats.Count || r.CandidateSeats.Any(s => !IsValidPlayerSeat(s)) ||
            r.TargetSeat is { } selected && !r.CandidateSeats.Contains(selected)) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (!OriginalHandBindingStarted(f, SkillProgramTriggerWindow.CardsMoved) ||
            history.OfType<OriginalHandBenefitStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var start] ||
            start != new OriginalHandBenefitStartedEvent(f.Id, r.Source, r.GameplayHash, r.StateId, r.MovementWindowId, r.BatchId, r.MovementSequence) ||
            history.OfType<OriginalHandBenefitStartedEvent>().Count(e => e.Source.OwnerSeat == f.OwnerSeat && e.Source.SkillId == f.SkillId &&
                e.StateId == r.StateId && e.MovementSequence == r.MovementSequence) != 1) return false;
        var chosen = history.OfType<OriginalHandBenefitChosenEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var bonuses = history.OfType<OriginalHandPermanentBonusGrantedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var draws = history.OfType<OriginalHandBenefitDrawIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var completed = history.OfType<OriginalHandBenefitCompletedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Option is { } kind ? !Enum.IsDefined(kind) || chosen is not [var choice] || choice.TargetSeat != r.TargetSeat || choice.Kind != kind : chosen.Length != 0) return false;
        if (r.BonusApplied ? r.Option is not (OriginalHandBenefitKind.HandLimit or OriginalHandBenefitKind.AttackRange) || bonuses is not [var granted] ||
                granted.SourceOwnerSeat != f.OwnerSeat || granted.SourceSkillId != f.SkillId || granted.StateId != r.StateId ||
                granted.RecipientSeat != r.TargetSeat || granted.Kind != r.Option : bonuses.Length != 0) return false;
        if (r.DrawIssued)
        {
            if (r.Option != OriginalHandBenefitKind.Draw || r.TargetSeat is not { } recipient || r.BonusApplied || r.ActualDrawCount is < 0 or > 1 ||
                r.DrawBefore < 0 || r.DrawAfter < r.DrawBefore || r.DrawAfter > OriginalHandMovementSequence || draws is not [var draw] ||
                draw != new OriginalHandBenefitDrawIssuedEvent(f.Id, recipient, r.ActualDrawCount, r.DrawBefore, r.DrawAfter)) return false;
            var movements = _cardMovements.Where(m => m.Sequence > r.DrawBefore && m.Sequence <= r.DrawAfter).ToArray();
            var reason = new CardMoveReason($"skill-program.{f.SkillId}.original-hand-benefit.draw");
            if (movements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(recipient) && m.Reason == reason) != r.ActualDrawCount ||
                movements.Any(m => !(m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(recipient) && m.Reason == reason ||
                    m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle))) return false;
        }
        else if (r.ActualDrawCount != 0 || r.DrawBefore != 0 || r.DrawAfter != 0 || draws.Length != 0) return false;
        if (r.Stage == OriginalHandBenefitStage.Complete)
            return f.PendingMovementContinuation is null && completed is [var completion] && completion ==
                new OriginalHandBenefitCompletedEvent(f.Id, r.TargetSeat, r.Option, r.BonusApplied, r.DrawIssued, r.ActualDrawCount);
        if (completed.Length != 0) return false;
        return r.Stage switch {
            OriginalHandBenefitStage.ChoosingTarget => r.TargetSeat is null && r.Option is null && !r.BonusApplied && !r.DrawIssued && f.PendingMovementContinuation is null,
            OriginalHandBenefitStage.ChoosingOption => r.TargetSeat is not null && r.Option is null && !r.BonusApplied && !r.DrawIssued && f.PendingMovementContinuation is null,
            OriginalHandBenefitStage.DrawChildren => r.DrawIssued && f.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } pending && pending.SubjectSeat == r.TargetSeat,
            _ => false };
    }

    private bool ValidOriginalHandInheritanceReceipt(ProgramSkillFrame f)
    {
        if (f.OriginalHandInheritance is not { } r || f.InstructionIndex != 1 || r.InstructionIndex != 1 || !ExactOriginalHandInheritanceParent(f) ||
            r.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) || r.GameplayHash != f.GameplayHash ||
            !Enum.IsDefined(r.Stage) || r.DeathWindowId != f.WindowContext!.ParentFrameId ||
            GetProgramTrigger(f).Effects is not [{ Op: SkillProgramEffectOp.InheritOriginalHandBonuses } effect] || effect.StateId != r.StateId ||
            _resolutionStack.OfType<ProgramDeathTriggerWindowFrame>().Single(w => w.Id == r.DeathWindowId).DeathFrameId != r.DeathFrameId ||
            f.PendingMovementContinuation is not null || r.CandidateSeats.Count == 0 || r.CandidateSeats.Distinct().Count() != r.CandidateSeats.Count ||
            r.CandidateSeats.Any(s => !IsValidPlayerSeat(s) || s == f.OwnerSeat) || r.Bonuses.Count == 0 ||
            r.Bonuses.Select(b => b.BonusId).Distinct().Count() != r.Bonuses.Count || r.Bonuses.Any(b => b.RecipientSeat != f.OwnerSeat ||
                b.SourceSkillId != f.SkillId || b.StateId != r.StateId || b.Kind is not (OriginalHandBenefitKind.HandLimit or OriginalHandBenefitKind.AttackRange))) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (!OriginalHandBindingStarted(f, SkillProgramTriggerWindow.OwnerDied) ||
            history.OfType<OriginalHandInheritanceStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var start] ||
            start != new OriginalHandInheritanceStartedEvent(f.Id, r.Source, r.GameplayHash, r.StateId, r.DeathFrameId, r.DeathWindowId, r.Bonuses.Count)) return false;
        var transfers = history.OfType<OriginalHandBonusTransferredEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var completed = history.OfType<OriginalHandInheritanceCompletedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Stage == OriginalHandInheritanceStage.ChoosingTarget)
            return !r.Applied && r.TargetSeat is null && transfers.Length == 0 && completed.Length == 0 &&
                r.Bonuses.SequenceEqual(OriginalHandInheritableBonuses(f.OwnerSeat, f.SkillId, r.StateId));
        return r.Applied && r.TargetSeat is { } recipient && r.CandidateSeats.Contains(recipient) &&
            transfers.SequenceEqual(r.Bonuses.Select(b => new OriginalHandBonusTransferredEvent(f.Id, b.BonusId, b.SourceOwnerSeat,
                b.SourceSkillId, b.StateId, f.OwnerSeat, recipient, b.Kind))) &&
            r.Bonuses.All(b => _originalHandPermanentBonuses.GetValueOrDefault(b.BonusId) == (b with { RecipientSeat = recipient })) &&
            completed is [var completion] && completion == new OriginalHandInheritanceCompletedEvent(f.Id, recipient, r.Bonuses.Count);
    }

    private bool OriginalHandBenefitFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.OriginalHandBenefit is not { Stage: OriginalHandBenefitStage.DrawChildren, DrawIssued: true, TargetSeat: { } recipient } r ||
            !ValidOriginalHandBenefitReceipt(f) || child is not CardsMovedTriggerWindowFrame moved) return false;
        var reason = new CardMoveReason($"skill-program.{f.SkillId}.original-hand-benefit.draw");
        return moved.ResumeProgramFrameId is null && moved.Batch.Id == moved.Id && moved.Batch.ParentFrameId == f.Id &&
            moved.Batch.AwaitingProgramFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId &&
            moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m =>
                _cardMovements.Contains(m) && m.Sequence > r.DrawBefore && m.Sequence <= r.DrawAfter &&
                (m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(recipient) && m.Reason == reason ||
                    m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle));
    }
    private bool OriginalHandEntityFirstChild(ProgramSkillFrame f, ResolutionFrame child) =>
        OriginalHandBenefitFirstChild(f, child) || f.OriginalHandAwakening is not null && OriginalHandAwakeningFirstChild(f, child);
    private bool OriginalHandEntityStructuralEdge(ResolutionFrame parent, ResolutionFrame child)
    {
        if (parent is ProgramSkillFrame f && OriginalHandEntityFirstChild(f, child)) return true;
        return child is ProgramSkillFrame observer && observer.WindowContext?.ParentFrameId == parent.Id &&
            (observer.OriginalHandBenefit is not null && ValidOriginalHandBenefitReceipt(observer) ||
             observer.OriginalHandInheritance is not null && ValidOriginalHandInheritanceReceipt(observer) ||
             observer.OriginalHandAwakening is not null && ValidOriginalHandAwakeningReceipt(observer));
    }
    private void AssertOriginalHandEntityProgram(ProgramSkillFrame f)
    {
        var receipts = (f.OriginalHandBenefit is null ? 0 : 1) + (f.OriginalHandInheritance is null ? 0 : 1) + (f.OriginalHandAwakening is null ? 0 : 1);
        if (receipts == 0)
        {
            if (!TracksOriginalHandEntities || f.TriggerId is null || GetProgramTrigger(f).Effects.All(effect => effect.Op is not
                    (SkillProgramEffectOp.OfferOriginalHandLossBenefit or SkillProgramEffectOp.InheritOriginalHandBonuses or
                     SkillProgramEffectOp.AwakenWhenOriginalHandEmpty))) return;
            var history = CompleteProgramEventHistory().ToArray();
            if (history.OfType<OriginalHandBenefitStartedEvent>().Any(e => e.FrameId == f.Id) ||
                history.OfType<OriginalHandInheritanceStartedEvent>().Any(e => e.FrameId == f.Id) ||
                history.OfType<OriginalHandAwakeningMaximumPaidEvent>().Any(e => e.FrameId == f.Id))
                throw new InvalidOperationException("An issued original-hand operation lost its owning receipt.");
            return;
        }
        if (receipts != 1 || f.OriginalHandBenefit is not null && !ValidOriginalHandBenefitReceipt(f) ||
            f.OriginalHandInheritance is not null && !ValidOriginalHandInheritanceReceipt(f) ||
            f.OriginalHandAwakening is not null && !ValidOriginalHandAwakeningReceipt(f))
            throw new InvalidOperationException("Original-hand operation lost its exact native parent, physical first loss, choice or issued payment.");
        var index = _resolutionStack.FindIndex(frame => frame.Id == f.Id);
        if (index + 1 < _resolutionStack.Count && !OriginalHandEntityFirstChild(f, _resolutionStack[index + 1]))
            throw new InvalidOperationException("Original-hand operation retained an unrelated first native child.");
        if (index == _resolutionStack.Count - 1 && _pendingDecision is { } prompt &&
            (f.OriginalHandInheritance is { Stage: OriginalHandInheritanceStage.ChoosingTarget } ||
                f.OriginalHandBenefit is { Stage: OriginalHandBenefitStage.ChoosingTarget or OriginalHandBenefitStage.ChoosingOption }) &&
            !IsOriginalHandEntityChoice(f, prompt))
            throw new InvalidOperationException("Original-hand operation changed its published target or option choices.");
    }
    private bool IsOriginalHandEntityChoice(ProgramSkillFrame f, PendingDecision decision)
    {
        if (decision.Kind != DecisionKind.ProgramTrigger || !decision.IsPrivate || decision.PlayerSeat != f.OwnerSeat || decision.SourceSeat != f.OwnerSeat ||
            decision.ValidCardIds.Count != 0 || decision.ValidContentIds.Count != 0 || decision.RequiredCardCount != 0 || decision.SkillPrompt?.SkillId != f.SkillId) return false;
        var expected = OriginalHandEntityChoices(f);
        return decision.ValidTargetSeats.SequenceEqual(expected.SelectMany(c => c.Targets).Distinct()) && decision.Choices.Count == expected.Count &&
            decision.Choices.Zip(expected).All(pair => pair.First.Id == pair.Second.Id && pair.First.Cards.Count == 0 &&
                pair.First.Targets.SequenceEqual(pair.Second.Targets) && pair.First.Parameters.OrderBy(p => p.Key).SequenceEqual(pair.Second.Parameters.OrderBy(p => p.Key)));
    }

    private ProgramSkillFrame? OriginalHandEntityObserverRoot()
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not ProgramSkillFrame root || !OriginalHandEntityFirstChild(root, _resolutionStack[index + 1])) continue;
            var aligned = true;
            for (var child = index + 2; child < _resolutionStack.Count; child++)
            {
                if (_resolutionStack[child] is ProgramLifecycleTriggerWindowFrame changed && _resolutionStack[child - 1] is ProgramSkillFrame owner &&
                    changed.Window == SkillProgramTriggerWindow.SkillsChanged && changed.ResumeProgramFrameId == owner.Id &&
                    changed.Continuation == ProgramLifecycleContinuation.ResumeParentProgram && changed.CandidateIndex >= 0 && changed.CandidateIndex <= changed.Candidates.Count) continue;
                if (!SameNameHandStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !CompletedUndamagedTargetRevealStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) && !RecipientCategoryMarkStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) && !OffTurnUsedCardGiftStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !CardSupplyCompletionStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !ResponseCompletionStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !OriginalHandEntityStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !OutsidePhaseDrawDiscardStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !DyingSuitsStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !HalfHandPaidDamageObserverEdge(child) && !PaidTargetObserverEdge(child)) { aligned = false; break; }
                if (_resolutionStack[child] is DyingFrame dying && child + 1 < _resolutionStack.Count &&
                    ((IsOriginalDyingSuspendedByDyingSuits(dying) || IsOriginalDyingSuspendedByRecipientCategoryMark(dying)) || IsOriginalDyingSuspendedByOwnedDeathBenefit(dying) ||
                     IsPaidHandRepaymentProgramAlcoholRide(child, dying) || IsPaidHandRepaymentRescueRide(child, dying) ||
                     PolicyCounterspellVirtualAlcoholRide(child, dying) || PaidObserverDamageVirtualAlcoholRide(child, dying))) break;
            }
            if (aligned) return root;
        }
        return null;
    }
    private bool IsOriginalHandEntityProgramDying() => ActiveDying is { } dying && OriginalHandEntityObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasOriginalHandEntityDamageObserver(long windowId) => _resolutionStack.Any(f => f.Id == windowId && f is DamageTriggerWindowFrame) &&
        OriginalHandEntityObserverRoot() is not null;
    private bool AllowsOriginalHandEntityNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or
                SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or
                SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) ||
            OriginalHandEntityObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!)
            .GetPausedInstruction(observer.InstructionIndex).Effect;
        return effect.Op == SkillProgramEffectOp.Damage && amount == effect.Amount && source == effect.ActorReference && nature == effect.DamageNature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target));
    }
    private bool TryAdvanceOriginalHandEntitySubtree()
    {
        if (_pendingDecision is not null || OriginalHandEntityObserverRoot() is null) return false;
        var top = _resolutionStack.LastOrDefault();
        if (top is ProgramSkillFrame { AttackAttempt: not null } attack)
        {
            if (CurrentDamageAttempt?.ResolutionId != attack.Id || ActiveDying is not null || attack.AttackReturn is null ||
                _resolutionStack.Any(frame => frame is DamageFrame damage && damage.ParentFrameId == attack.Id ||
                    frame is BeforeDamageProgramWindowFrame before && (before.ContinuationAttackResolutionId ?? before.ParentFrameId) == attack.Id)) return false;
            CompleteDamageAttack(new ProgramAttackHandle(this, attack.Id)); AdvanceRulesAndPublishState(); return true;
        }
        if (top is ProgramSkillFrame or CardsMovedTriggerWindowFrame or HpChangedTriggerWindowFrame or ProgramLifecycleTriggerWindowFrame or
            ProgramCardTriggerWindowFrame or BeforeDamageProgramWindowFrame or ProgramDeathTriggerWindowFrame or ProgramKillTriggerWindowFrame or RecoveryReplacementFrame)
        { AdvanceRuntimeFrame(top.Id); AdvanceRulesAndPublishState(); return true; }
        if (top is DeathFrame death) { ContinueDeathResolution(death.Id); AdvanceRulesAndPublishState(); return true; }
        return false;
    }
}
