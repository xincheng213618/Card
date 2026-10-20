namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 26720)]
    private void AssertLiWan(ProgramSkillFrame f)
    {
        if (f.LiWan is not { } r) return;
        if (r.Source != LiWanSource(f) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != f.InstructionIndex || r.InstructionIndex != 1 ||
            r.ActualTurn != _turnNumber || !Enum.IsDefined(r.Stage) || !LiWanComposition.Handles(r.Op) ||
            GetPausedPrivateOfferEffect(f)?.Op != r.Op || f.WindowContext is not { } context || context.ParentFrameId != r.ParentId ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            ProgramEventHistory<LiWanStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var start] ||
            start.Source != r.Source || start.GameplayHash != r.GameplayHash || start.Op != r.Op || start.ParentId != r.ParentId ||
            start.ActualTurn != r.ActualTurn || start.EndingSerial != r.EndingSerial || start.PromiseFrameId != r.PromiseFrameId || start.RefundSequence != r.RefundSequence)
            throw new InvalidOperationException("Li Wan lost her exact source, instruction, native parent or single issued receipt.");
        var at = _resolutionStack.FindIndex(p => p.Id == f.Id);
        if (at < 1) throw new InvalidOperationException("Li Wan requires her retained native parent.");
        if (r.Op is SkillProgramEffectOp.OfferLianjuSameColorGift or SkillProgramEffectOp.ResolveLianjuOppositeColorReturn)
        {
            if (_resolutionStack[at - 1] is not TurnEndingBoundaryFrame parent || parent.Id != r.ParentId || parent.OwnerSeat != _currentSeat ||
                parent.TurnNumber != r.ActualTurn || context.Window != SkillProgramTriggerWindow.TurnEnding || parent.ItemIndex < 0 || parent.ItemIndex >= parent.Items.Count ||
                parent.Items[parent.ItemIndex].Candidate is not { } candidate || !MountObserverCandidateMatches(f, candidate) ||
                r.EndingSerial != LianjuEndingSerial || r.Damage is not null || r.RefundSequence is not null ||
                (r.Op == SkillProgramEffectOp.OfferLianjuSameColorGift ? parent.OwnerSeat != f.OwnerSeat || r.PromiseFrameId is not null :
                    r.PromiseFrameId is not { } promise || LianjuDuePromises().SingleOrDefault(p => p.FrameId == promise) is not { } issued ||
                    issued.Source.OwnerSeat != f.OwnerSeat || issued.Source.SkillInstanceId != f.SkillInstanceId || issued.GameplayHash != f.GameplayHash ||
                    r.IsRed != !issued.IsRed || r.RecipientSeat != f.OwnerSeat || ProgramEventHistory<LianjuPromiseConsumedEvent>().Count(e =>
                        e.PromiseFrameId == promise && e.ParentFrameId == parent.Id && e.EndingSerial == r.EndingSerial) != 1))
                throw new InvalidOperationException("Lianju lost its actual Ending cursor, original promise or frozen opposite color.");
            if (r.Stage == LiWanStage.Color ? r.IsRed is not null || r.RecipientSeat is not null || r.ChosenCards is not null :
                r.IsRed is null || (r.Stage == LiWanStage.Recipient ? r.RecipientSeat is not null || r.ChosenCards is not null :
                    r.RecipientSeat is null || r.ChosenCards is null || r.ChosenCards.Count > 2 || r.ChosenCards.Distinct().Count() != r.ChosenCards.Count))
                throw new InvalidOperationException("Lianju lost its public color and bounded unique entity selection.");
            if (r.Stage == LiWanStage.Cards && r.ChosenCards!.Except(LianjuEligibleCards(r.Op == SkillProgramEffectOp.OfferLianjuSameColorGift ? f.OwnerSeat : _currentSeat, r.IsRed!.Value)).Any())
                throw new InvalidOperationException("Lianju cannot select a played-out response, stale turn, different color or unavailable entity.");
        }
        else if (r.Op == SkillProgramEffectOp.DrawSilvMarkedCardAfterDamage)
        {
            if (r.Damage is not { } d || r.RecipientSeat != f.OwnerSeat || r.PromiseFrameId is not null || r.RefundSequence is not null ||
                context.Window != SkillProgramTriggerWindow.AfterDamageApplied || context.DamageFrameId != d.DamageFrameId || context.Amount != d.ActualAmount ||
                d.ActualAmount < 1 || d.TargetSeat != f.OwnerSeat || _resolutionStack[at - 1] is not DamageTriggerWindowFrame window || window.Id != r.ParentId ||
                window.ParentFrameId != d.DamageFrameId || window.TargetSeat != f.OwnerSeat || window.TriggerWindow != context.Window ||
                !_resolutionStack.OfType<DamageFrame>().Any(damage => damage.Id == d.DamageFrameId && damage.ParentFrameId == d.DamageParentId && damage.TargetSeat == f.OwnerSeat && damage.Amount == d.ActualAmount))
                throw new InvalidOperationException("Silv requires its exact actual damage instance rather than one draw per point.");
        }
        else
        {
            if (r.RefundSequence is not { } sequence || context.MovementBatch is not { } batch || context.MovementIndex is not { } index ||
                _resolutionStack[at - 1] is not CardsMovedTriggerWindowFrame window || window.Id != r.ParentId || batch.Id != window.Id ||
                index < 0 || index >= batch.Movements.Count || !batch.Movements.SequenceEqual(window.Batch.Movements) ||
                window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count || !MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex]) ||
                batch.Movements[index] is not { } move || move.Sequence != sequence || GetProgramDiscardSource(move)?.OwnerSeat != r.RecipientSeat ||
                r.ChosenCards is not [var id] || id != move.CardId || SilvMark(id) is not { } mark || mark.Source.OwnerSeat != f.OwnerSeat ||
                mark.Source.SkillInstanceId != f.SkillInstanceId || mark.GameplayHash != f.GameplayHash || r.Damage is not null || r.PromiseFrameId is not null)
                throw new InvalidOperationException("Silv requires its exact marked entity, native discard provenance and actual losing owner.");
        }
        var paid = ProgramEventHistory<LiWanPaidEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Stage != LiWanStage.Children)
        {
            if (paid.Length != 0 || f.PendingMovementContinuation is not null || r.Before != 0 || r.After != 0 || r.Count != 0)
                throw new InvalidOperationException("Lianju cannot move any card before the public draft is finished.");
            return;
        }
        if (r.RecipientSeat is not { } recipient || f.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != recipient ||
            paid is not [var invoice] || invoice.RecipientSeat != recipient || invoice.Before != r.Before || invoice.After != r.After || invoice.Count != r.Count ||
            r.Before < 0 || r.After < r.Before || r.After > _movementSequence || r.Count < 0 || r.Count > (r.Op == SkillProgramEffectOp.DrawSilvMarkedCardAfterDamage ? 1 : r.ChosenCards!.Count))
            throw new InvalidOperationException("Li Wan lost her single native payment invoice or movement return.");
        var reason = r.Op == SkillProgramEffectOp.DrawSilvMarkedCardAfterDamage ? SilvDrawReason : r.Op == SkillProgramEffectOp.ReturnDiscardedSilvEntity ? SilvRefundReason : LianjuGainReason;
        var actual = _cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= r.After && m.Reason.Value == reason && m.To == CardLocation.Hand(recipient)).ToArray();
        if (actual.Length != r.Count || actual.Select(m => m.CardId).Distinct().Count() != r.Count || actual.Any(m =>
                m.From != (r.Op == SkillProgramEffectOp.DrawSilvMarkedCardAfterDamage ? CardLocation.DrawPile : CardLocation.DiscardPile)) ||
            r.Op != SkillProgramEffectOp.DrawSilvMarkedCardAfterDamage && actual.Any(m => !r.ChosenCards!.Contains(m.CardId)))
            throw new InvalidOperationException("Li Wan cannot replay a paid gain, change its entities, or count replacement cards.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 26720)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 26720)]
    private bool ContinueIssuedLiWan(ProgramSkillFrame f)
    {
        if (f.LiWan is not null) { AssertLiWan(f); return true; }
        return f.InstructionIndex is 0 or 1 && f.TriggerId is { } id && f.ActivationId == id && f.WindowContext is { } context &&
            f.SelectedCardIds.Count == 0 && f.SelectedTargetSeats.Count == 0 && IsIssuedLiWanCandidate(new(f.OwnerSeat, f.SkillId, id, f.SkillInstanceId, f.GameplayHash, 0, context.OccurrenceIndex)) &&
            CanRunIssuedLiWanCandidate(new(f.OwnerSeat, f.SkillId, id, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex), context);
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 170)]
    private bool IsLiWanMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending)
    {
        if (f.LiWan is not { Stage: LiWanStage.Children } r || effect?.Op != r.Op || pending != f.PendingMovementContinuation) return false;
        AssertLiWan(f); return true;
    }
    private bool LiWanFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.LiWan is not { Stage: LiWanStage.Children } r || f.PendingMovementContinuation is null) return false;
        AssertLiWan(f);
        if (child is CardsMovedTriggerWindowFrame moved)
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId &&
                moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) &&
                    m.Sequence > r.Before && m.Sequence <= r.After && (m.Reason.Value is LianjuGainReason or SilvDrawReason or SilvRefundReason || m.Reason == CardMoveReasons.Reshuffle));
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange)
            return state.ResumeProgramFrameId == f.Id && state.CharacterStateContinuation == CharacterStateContinuation.Program &&
                ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id && e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
        return false;
    }
    // None of the four direct operations removes equipment or recovers HP. Recovery children belong to their exact nested gain observer.
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 700)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 640)]
    private bool IsLiWanRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery) => false;
    private ProgramSkillFrame? LiWanObserverRoot() => FindProgramObserverRoot(static (e, f, child) => e.LiWanFirstChild(f, child), static (e, i) => e.SameNameHandObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 500)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 610)]
    private bool HasLiWanDamageObserver(long id) => LiWanObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 640)]
    private bool HasLiWanBeforeDamageObserver(long id) => LiWanObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is BeforeDamageProgramWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 470)]
    private bool IsLiWanDying() => ActiveDying is { } dying && LiWanObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1110)]
    private bool AllowsLiWanNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || LiWanObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(f, reference) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 26720)]
    private bool AdvanceLiWanSubtree() => _pendingDecision is null && LiWanObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
