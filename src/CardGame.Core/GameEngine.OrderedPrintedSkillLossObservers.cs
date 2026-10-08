namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ExactOrderedPrintedSkillLossParent(ProgramSkillFrame f, out DamageTriggerWindowFrame window,
        out DamageFrame damage, out IDamageAttempt attack)
    {
        window = null!; damage = null!; attack = null!;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (index < 2 || f.TriggerId is null || f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied,
                DamageFrameId: { } damageId, TargetSeat: { } target, Amount: > 0 } context || context.OwnerSeat != f.OwnerSeat ||
            _resolutionStack[index - 1] is not DamageTriggerWindowFrame w || w.Id != context.ParentFrameId || w.ParentFrameId != damageId ||
            w.TriggerWindow != context.Window || w.TargetSeat != target || w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count ||
            !MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex].ToProgramCandidate()) ||
            _resolutionStack[index - 2] is not DamageFrame d || d.Id != damageId || w.SourceSeat != d.SourceSeat || d.TargetSeat != target || d.Amount != context.Amount)
            return false;
        // Read the exact suspended producer, never the innermost active damage.
        // This is also used by pure original-Dying ancestry validation.
        var a = LastDamageSourceOriginalAttack(d.ParentFrameId);
        if (a is null || a.ResolutionId != d.ParentFrameId || w.SourceCardId != a.Card?.Id || w.SourceCard != a.EffectiveCardKind ||
            !a.DamageWasApplied || a.DamageAmount <= 0 || a.DamageAmount != d.Amount ||
            a.SourceSeat != d.SourceSeat || a.TargetSeat != d.TargetSeat || GetDamageNature(a) != d.Nature ||
            context.SourceSeat != (a.IsSourceLess ? null : a.SourceSeat) ||
            f.OwnerSeat != target && (a.IsSourceLess || f.OwnerSeat != a.SourceSeat)) return false;
        var history = CompleteProgramEventHistory().ToArray();
        var requested = history.OfType<DamageRequestedEvent>().Where(e => e.ResolutionId == d.Id).ToArray();
        if (requested is not [var request] || request.SourceSeat != d.SourceSeat || request.TargetSeat != d.TargetSeat ||
            request.Amount != d.Amount || request.Nature != d.Nature || request.SourceLess != a.IsSourceLess || request.SourceCard != a.EffectiveCardKind) return false;
        var start = Array.FindIndex(history, e => ReferenceEquals(e, request));
        var applied = history.Skip(start + 1).TakeWhile(e => e is not DamageRequestedEvent).OfType<DamageAppliedEvent>().FirstOrDefault();
        if (applied is null || applied.SourceSeat != d.SourceSeat || applied.TargetSeat != d.TargetSeat || applied.Amount != d.Amount ||
            applied.Nature != d.Nature || applied.SourceLess != a.IsSourceLess) return false;
        window = w; damage = d; attack = a; return true;
    }

    private bool ValidOrderedPrintedSkillLossLease(OrderedPrintedSkillLossLease lease)
    {
        if (lease.ProgramFrameId <= 0 || !IsValidPlayerSeat(lease.Source.OwnerSeat) || lease.ActualTurnNumber <= 0 ||
            !IsValidPlayerSeat(lease.ActualTurnOwnerSeat) || lease.TemplateSourceId is not (CharacterState.PrimarySkillSource or CharacterState.SecondarySkillSource) ||
            lease.FrozenAttackRange < 0 || lease.LostSkillIds.Count > lease.FrozenAttackRange ||
            lease.LostSkillIds.Distinct(StringComparer.Ordinal).Count() != lease.LostSkillIds.Count ||
            lease.RemovedGrants.Select(g => g.GrantId).Distinct(StringComparer.Ordinal).Count() != lease.RemovedGrants.Count ||
            lease.RemovedGrants.Any(g => !lease.LostSkillIds.Contains(g.SkillId, StringComparer.Ordinal)) ||
            lease.LostSkillIds.Any(id => !lease.RemovedGrants.Any(g => g.SkillId == id)) ||
            _contentRegistry.GetSkill(lease.Source.SkillId).Program is not { } program || program.GameplayHash != lease.GameplayHash ||
            program.Triggers.SingleOrDefault(t => t.Id == lease.Source.BindingId) is not { } trigger || !IsOrderedPrintedSkillLossTrigger(trigger) ||
            !_contentRegistry.Generals.TryGetValue(lease.GeneralId, out var general)) return false;
        var printedOrder = general.SkillIds.Distinct(StringComparer.Ordinal).ToArray(); var previous = -1;
        foreach (var id in lease.LostSkillIds)
        {
            var position = Array.IndexOf(printedOrder, id);
            if (position <= previous) return false;
            previous = position;
        }
        var history = CompleteProgramEventHistory().ToArray();
        return history.OfType<OrderedPrintedSkillLossIssuedEvent>().Where(e => e.FrameId == lease.ProgramFrameId).ToArray() is [var issued] &&
            issued == new OrderedPrintedSkillLossIssuedEvent(lease.ProgramFrameId, lease.Source, lease.GameplayHash, lease.ActualTurnNumber,
                lease.ActualTurnOwnerSeat, lease.TemplateSourceId, lease.GeneralId, lease.FrozenAttackRange, lease.LostSkillIds.Count, lease.RemovedGrants.Count) &&
            history.OfType<OrderedPrintedSkillLostEvent>().Where(e => e.FrameId == lease.ProgramFrameId)
                .SequenceEqual(lease.LostSkillIds.Select((id, ordinal) => new OrderedPrintedSkillLostEvent(lease.ProgramFrameId, ordinal, id))) &&
            history.OfType<OrderedPrintedSkillGrantRemovedEvent>().Where(e => e.FrameId == lease.ProgramFrameId)
                .Select(e => e.Grant).SequenceEqual(lease.RemovedGrants) &&
            history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == lease.ProgramFrameId && e.SkillId == lease.Source.SkillId &&
                e.BindingId == lease.Source.BindingId && e.SkillInstanceId == lease.Source.SkillInstanceId && e.OwnerSeat == lease.Source.OwnerSeat &&
                e.Window == SkillProgramTriggerWindow.AfterDamageApplied) == 1 &&
            history.OfType<TurnStartedEvent>().Any(e => e.TurnNumber == lease.ActualTurnNumber && e.ActorSeat == lease.ActualTurnOwnerSeat);
    }

    private bool ValidOrderedPrintedSkillLossReceipt(ProgramSkillFrame f)
    {
        if (f.OrderedPrintedSkillLoss is not { } r || f.InstructionIndex != 1 || r.InstructionIndex != 1 ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || !Enum.IsDefined(r.Stage) || !Enum.IsDefined(r.DamageNature) ||
            r.Lease.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) ||
            r.Lease.GameplayHash != f.GameplayHash || r.Lease.ProgramFrameId != f.Id || !ValidOrderedPrintedSkillLossLease(r.Lease) ||
            !_orderedPrintedSkillLossLeases.Contains(r.Lease) || !ExactOrderedPrintedSkillLossParent(f, out var window, out var damage, out var attack) ||
            window.Id != r.DamageWindowId || damage.Id != r.DamageFrameId || damage.ParentFrameId != r.AttackFrameId ||
            r.DamageSourceSeat != (attack.IsSourceLess ? null : attack.SourceSeat) || r.DamageTargetSeat != attack.TargetSeat ||
            r.DamageAmount != attack.DamageAmount || r.DamageNature != GetDamageNature(attack) || r.SourceLess != attack.IsSourceLess) return false;
        var history = CompleteProgramEventHistory().ToArray();
        var draws = history.OfType<OrderedPrintedSkillLossDrawIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var completed = history.OfType<OrderedPrintedSkillLossCompletedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Stage == OrderedPrintedSkillLossStage.LossChildren)
            return !r.DrawIssued && r.DrawActual == 0 && r.DrawBefore == 0 && r.DrawAfter == 0 && draws.Length == 0 && completed.Length == 0 &&
                f.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } lossPending && lossPending.SubjectSeat == f.OwnerSeat;
        if (!r.DrawIssued || r.DrawActual < 0 || r.DrawActual > r.Lease.LostSkillIds.Count || r.DrawBefore < 0 ||
            r.DrawAfter < r.DrawBefore || r.DrawAfter > OrderedPrintedLossMovementSequence || draws is not [var draw] ||
            draw != new OrderedPrintedSkillLossDrawIssuedEvent(f.Id, r.Lease.LostSkillIds.Count, r.DrawActual, r.DrawBefore, r.DrawAfter)) return false;
        var drawn = _cardMovements.Where(m => m.Sequence > r.DrawBefore && m.Sequence <= r.DrawAfter && m.Reason.Value == OrderedPrintedLossDrawReason(f)).ToArray();
        if (drawn.Length != r.DrawActual || drawn.Any(m => m.From != CardLocation.DrawPile || m.To != CardLocation.Hand(f.OwnerSeat))) return false;
        return r.Stage == OrderedPrintedSkillLossStage.Complete
            ? f.PendingMovementContinuation is null && completed is [var done] && done == new OrderedPrintedSkillLossCompletedEvent(f.Id, r.Lease.LostSkillIds.Count, r.DrawActual)
            : completed.Length == 0 && f.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } drawPending && drawPending.SubjectSeat == f.OwnerSeat;
    }

    private bool OrderedPrintedSkillLossSkillsChangedEdge(ResolutionFrame parent, ResolutionFrame child)
    {
        if (parent is ProgramSkillFrame program && child is ProgramLifecycleTriggerWindowFrame changed)
            return changed.Window == SkillProgramTriggerWindow.SkillsChanged && changed.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
                changed.ResumeProgramFrameId == program.Id && changed.Candidates.Count > 0 && changed.CandidateIndex >= 0 && changed.CandidateIndex <= changed.Candidates.Count &&
                changed.Candidates.All(c => c.OwnerSeat == changed.OwnerSeat && GetProgramTrigger(c).Window == changed.Window);
        return parent is ProgramLifecycleTriggerWindowFrame skills && skills.Window == SkillProgramTriggerWindow.SkillsChanged &&
            skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram && skills.CandidateIndex >= 0 && skills.CandidateIndex < skills.Candidates.Count &&
            child is ProgramSkillFrame observer && observer.WindowContext is { Window: SkillProgramTriggerWindow.SkillsChanged } context &&
            context.ParentFrameId == skills.Id && context.OwnerSeat == observer.OwnerSeat && context.SourceSeat == skills.OwnerSeat && context.TargetSeat == skills.OwnerSeat &&
            MountObserverCandidateMatches(observer, skills.Candidates[skills.CandidateIndex]) &&
            CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == observer.Id && e.OwnerSeat == observer.OwnerSeat &&
                e.SkillId == observer.SkillId && e.BindingId == observer.TriggerId && e.SkillInstanceId == observer.SkillInstanceId && e.Window == context.Window) == 1;
    }

    private bool OrderedPrintedSkillLossFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.OrderedPrintedSkillLoss is not { } r || !ValidOrderedPrintedSkillLossReceipt(root)) return false;
        if (OrderedPrintedSkillLossSkillsChangedEdge(root, child)) return true;
        return r.Stage == OrderedPrintedSkillLossStage.DrawChildren && child is CardsMovedTriggerWindowFrame moved &&
            moved.Batch.ParentFrameId == root.Id && moved.Batch.AwaitingProgramFrameId == root.Id && moved.ResumeProgramFrameId is null &&
            moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.OriginSkillId == root.SkillId && moved.Batch.OriginSkillInstanceId == root.SkillInstanceId &&
            moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.DrawBefore && m.Sequence <= r.DrawAfter &&
                (m.Reason.Value == OrderedPrintedLossDrawReason(root) && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(root.OwnerSeat) ||
                 m.Reason == CardMoveReasons.Reshuffle && m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile));
    }

    private bool OrderedPrintedSkillLossStructuralEdge(ResolutionFrame parent, ResolutionFrame child) =>
        parent is ProgramSkillFrame root && root.OrderedPrintedSkillLoss is not null && OrderedPrintedSkillLossFirstChild(root, child) ||
        child is ProgramSkillFrame { OrderedPrintedSkillLoss: not null } observer && observer.WindowContext?.ParentFrameId == parent.Id && ValidOrderedPrintedSkillLossReceipt(observer);

    private ProgramSkillFrame? OrderedPrintedSkillLossObserverRoot(long? ownerRootId = null)
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not ProgramSkillFrame root || ownerRootId is { } requestedRoot && root.Id != requestedRoot ||
                !OrderedPrintedSkillLossFirstChild(root, _resolutionStack[index + 1])) continue;
            var valid = true;
            for (var child = index + 2; child < _resolutionStack.Count; child++)
            {
                if (OrderedPrintedSkillLossSkillsChangedEdge(_resolutionStack[child - 1], _resolutionStack[child])) continue;
                if (!DyingSuitsStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !MatchingRecastStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !OverflowTargetCancellationStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !HalfHandPaidDamageObserverEdge(child) && !PaidTargetObserverEdge(child))
                { valid = false; break; }
                if (_resolutionStack[child] is DyingFrame dying && child + 1 < _resolutionStack.Count &&
                    (IsOriginalDyingSuspendedByDyingSuits(dying) || IsOriginalDyingSuspendedByRecipientCategoryMark(dying) || IsOriginalDyingSuspendedByOwnedDeathBenefit(dying) ||
                     IsPaidHandRepaymentProgramAlcoholRide(child, dying) || IsPaidHandRepaymentRescueRide(child, dying) ||
                     PolicyCounterspellVirtualAlcoholRide(child, dying) || PaidObserverDamageVirtualAlcoholRide(child, dying))) break;
            }
            if (valid) return root;
        }
        return null;
    }

    private bool IsOrderedPrintedSkillLossProgramDying() => ActiveDying is { } dying && OrderedPrintedSkillLossObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasOrderedPrintedSkillLossDamageObserver(long id) => HasOrderedPrintedSkillLossObserver(id, false);
    private bool HasOrderedPrintedSkillLossBeforeDamageObserver(long id) => HasOrderedPrintedSkillLossObserver(id, true);
    private bool HasOrderedPrintedSkillLossObserver(long id, bool before)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == id && (before ? f is BeforeDamageProgramWindowFrame : f is DamageTriggerWindowFrame));
        if (index < 0 || OrderedPrintedSkillLossObserverRoot() is not { } root) return false;
        var rootIndex = _resolutionStack.FindIndex(f => f.Id == root.Id);
        if (index > rootIndex) return true; // Already proved by this exact root's suffix.
        for (var child = index + 1; child <= rootIndex; child++)
            if (!OrderedPrintedSkillLossStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                !OrderedPrintedSkillLossSkillsChangedEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                !DyingSuitsStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                !MatchingRecastStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                !OverflowTargetCancellationStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child])) return false;
        return index < rootIndex;
    }

    private bool AllowsOrderedPrintedSkillLossNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.DiscardPileReceived or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) ||
            OrderedPrintedSkillLossObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!).GetPausedInstruction(observer.InstructionIndex).Effect;
        return effect.Op == SkillProgramEffectOp.Damage && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target));
    }

    private bool TryAdvanceOrderedPrintedSkillLossSubtree()
    {
        if (_pendingDecision is not null || OrderedPrintedSkillLossObserverRoot() is null) return false;
        var top = _resolutionStack.LastOrDefault();
        if (top is ProgramSkillFrame { AttackAttempt: not null } attack)
        {
            if (CurrentDamageAttempt?.ResolutionId != attack.Id || ActiveDying is not null || attack.AttackReturn is null ||
                _resolutionStack.Any(f => f is DamageFrame d && d.ParentFrameId == attack.Id || f is BeforeDamageProgramWindowFrame b && (b.ContinuationAttackResolutionId ?? b.ParentFrameId) == attack.Id)) return false;
            CompleteDamageAttack(new ProgramAttackHandle(this, attack.Id)); AdvanceRulesAndPublishState(); return true;
        }
        if (top is ProgramSkillFrame or CardsMovedTriggerWindowFrame or HpChangedTriggerWindowFrame or ProgramLifecycleTriggerWindowFrame or ProgramCardTriggerWindowFrame or
            BeforeDamageProgramWindowFrame or ProgramDeathTriggerWindowFrame or ProgramKillTriggerWindowFrame or RecoveryReplacementFrame)
        { AdvanceRuntimeFrame(top.Id); AdvanceRulesAndPublishState(); return true; }
        if (top is DeathFrame death) { ContinueDeathResolution(death.Id); AdvanceRulesAndPublishState(); return true; }
        return false;
    }

    private void AssertOrderedPrintedSkillLoss(ProgramSkillFrame f)
    {
        if (f.OrderedPrintedSkillLoss is null) return;
        if (!ValidOrderedPrintedSkillLossReceipt(f)) throw new InvalidOperationException("Ordered printed-skill loss lost its exact damage, removed grants or once-issued Draw receipt.");
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (index + 1 < _resolutionStack.Count && !OrderedPrintedSkillLossFirstChild(f, _resolutionStack[index + 1]))
            throw new InvalidOperationException("Ordered printed-skill loss retained an unrelated native child.");
    }
    private void AssertOrderedPrintedSkillLossObservers()
    {
        foreach (var root in _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.OrderedPrintedSkillLoss is not null))
        {
            var index = _resolutionStack.FindIndex(f => f.Id == root.Id);
            if (index + 1 < _resolutionStack.Count && OrderedPrintedSkillLossObserverRoot(root.Id) is null)
                throw new InvalidOperationException("An ordered printed-skill loss descendant lost its exact native return ancestry.");
        }
    }

    private void AssertOrderedPrintedSkillLossState()
    {
        if (!_orderedPrintedSkillLossStateStarted && _orderedPrintedSkillLossLeases.Count == 0) return;
        var history = CompleteProgramEventHistory().ToArray();
        if (_orderedPrintedSkillLossLeases.Select(l => l.ProgramFrameId).Distinct().Count() != _orderedPrintedSkillLossLeases.Count ||
            _orderedPrintedSkillLossLeases.Any(l => !ValidOrderedPrintedSkillLossLease(l) || history.OfType<OrderedPrintedSkillLossExpiredEvent>().Any(e => e.FrameId == l.ProgramFrameId)))
            throw new InvalidOperationException("An ordered printed-skill loss lease lost its immutable removed-grant issuance.");
        foreach (var issued in history.OfType<OrderedPrintedSkillLossIssuedEvent>())
        {
            var expires = history.OfType<OrderedPrintedSkillLossExpiredEvent>().Where(e => e.FrameId == issued.FrameId).ToArray();
            if (expires.Length == 0)
            {
                if (_orderedPrintedSkillLossLeases.Count(l => l.ProgramFrameId == issued.FrameId) != 1)
                    throw new InvalidOperationException("An unexpired ordered printed-skill loss disappeared before actual turn end.");
                continue;
            }
            var returned = history.OfType<OrderedPrintedSkillGrantRestoredEvent>().Where(e => e.FrameId == issued.FrameId).ToArray();
            var removed = history.OfType<OrderedPrintedSkillGrantRemovedEvent>().Where(e => e.FrameId == issued.FrameId).ToArray();
            if (expires is not [var end] || end.OwnerSeat != issued.Source.OwnerSeat || end.ActualTurnNumber != issued.ActualTurnNumber ||
                end.ActualTurnOwnerSeat != issued.ActualTurnOwnerSeat || returned.Length != issued.RemovedGrantCount ||
                !returned.Select(e => e.Grant).SequenceEqual(removed.Select(e => e.Grant)) || returned.Any(e => e.OwnerSeat != end.OwnerSeat) ||
                end.RestoredCount != returned.Count(e => e.Restored) || end.SkippedCount != returned.Count(e => !e.Restored) ||
                !history.OfType<TurnEndedEvent>().Any(e => e.TurnNumber == end.ActualTurnNumber && e.ActorSeat == end.ActualTurnOwnerSeat))
                throw new InvalidOperationException("An ordered printed-skill restoration lost its original grants or matching actual TurnEnded fact.");
        }
    }
}
