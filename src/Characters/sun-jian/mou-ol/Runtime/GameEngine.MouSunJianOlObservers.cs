namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidMouSunJianOlYipo(ProgramSkillFrame f)
    {
        if (f.MouSunJianOlYipo is not { } r || r.Source != MouSunJianOlSource(f) || r.GameplayHash != f.GameplayHash || f.SkillId != MouSunJianOlComposition.Yipo ||
            f.ActivationId != f.TriggerId || f.InstructionIndex != 1 || f.WindowContext is not { } w || w.ParentFrameId != r.ChangeId || !Enum.IsDefined(r.Stage) ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash || !QualifiedMouSunJianOlYipo(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, 0), w) ||
            ProgramEventHistory<MouSunJianOlYipoStartedEvent>().Count(e => e == new MouSunJianOlYipoStartedEvent(f.Id, r.Source, r.GameplayHash, r.ChangeId)) != 1) return false;
        var n = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (n < 1 || _resolutionStack[n - 1] is not HpChangedTriggerWindowFrame parent || parent.Id != r.ChangeId || parent.Change != w.HpChange || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || !MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex])) return false;
        if (r.Branch is null) return r.Stage is MouSunJianOlYipoStage.Target or MouSunJianOlYipoStage.Option && r.X == 0 && r.Draw is null && r.Selected.Count == 0;
        if (r.TargetSeat is not { } target || !IsValidPlayerSeat(target) || r.Branch is not ("draw-many" or "discard-many") || r.X < 1 ||
            r.DrawCount != (r.Branch == "draw-many" ? r.X : 1) || r.DiscardCount != (r.Branch == "draw-many" ? 1 : r.X) ||
            ProgramEventHistory<MouSunJianOlYipoChosenEvent>().Count(e => e == new MouSunJianOlYipoChosenEvent(f.Id, target, r.Branch, r.X, r.DrawCount, r.DiscardCount)) != 1 ||
            r.Selected.Count > r.DiscardCount || r.Selected.Distinct().Count() != r.Selected.Count) return false;
        if (r.Stage == MouSunJianOlYipoStage.Drawing) return r.Draw is null;
        if (r.Draw is not { } d || d.RequestedCount != r.DrawCount || d.RecipientSeat != target ||
            ProgramEventHistory<MouSunJianOlYipoDrawEvent>().Count(e => e == new MouSunJianOlYipoDrawEvent(f.Id, target, d.RequestedCount, d.ActualDrawIds.Count, d.ReplacementGainedIds.Count)) != 1) return false;
        if (r.Stage != MouSunJianOlYipoStage.DiscardChildren) return r.Stage is MouSunJianOlYipoStage.DrawChildren or MouSunJianOlYipoStage.Discard;
        return r.Before >= 0 && r.After >= r.Before && r.After <= _movementSequence && ProgramEventHistory<MouSunJianOlYipoDiscardEvent>().Count(e => e.FrameId == f.Id && e.TargetSeat == target && e.Before == r.Before && e.After == r.After && e.Count == r.Selected.Count) == 1 &&
            r.Selected.All(id => _cardMovements.Any(m => m.CardId == id && m.From.OwnerSeat == target && m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment && m.Sequence > r.Before && m.Sequence <= r.After && m.Reason.Value == MouSunJianOlYipoDiscardReason));
    }
    private bool ValidMouSunJianOlHulie(ProgramSkillFrame f)
    {
        if (f.MouSunJianOlHulie is not { } r || f.SkillId != MouSunJianOlComposition.Hulie || f.ActivationId != f.TriggerId || f.InstructionIndex != 1 || f.WindowContext is not { } w ||
            r.Entry.Source.OwnerSeat != f.OwnerSeat || r.Entry.Source.SkillId != f.SkillId || r.Entry.Source.SkillInstanceId != f.SkillInstanceId || r.Entry.GameplayHash != f.GameplayHash ||
            !Enum.IsDefined(r.Stage) || w.CardUse?.ParentCardUseFrameId != r.Entry.UseId || w.CardUse.CardActionId != r.Entry.ActionId ||
            LifecycleCardUse(r.Entry.UseId) is not { } use || use.MouSunJianOlHulieEntries?.Any(e => e with { Boosted = false } == r.Entry with { Boosted = false } && ValidMouSunJianOlHulieEntry(use, e)) != true) return false;
        var n = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (n < 1 || _resolutionStack[n - 1] is not ProgramCardTriggerWindowFrame parent || parent.Id != w.ParentFrameId || parent.ParentFrameId != use.Id || parent.Action.ActionId != r.Entry.ActionId ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || !MountObserverCandidateMatches(f, ToSharedCandidate(parent.Candidates[parent.CandidateIndex]))) return false;
        if (r.Stage == MouSunJianOlHulieStage.DamageChoice) return f.TriggerId == "single-target-damage" && w.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized && !r.Accepted && r.UseId == 0;
        if (f.TriggerId != "undamaged-counter" || w.Window != SkillProgramTriggerWindow.CardUseCompleted || use.Step != ResolutionFrameStep.Completed ||
            ProgramEventHistory<MouSunJianOlCounterConsideredEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.OriginalUseId == use.Id) != 1 ||
            ProgramEventHistory<MouSunJianOlUseDamageEvent>().Any(e => e.UseId == use.Id && e.Amount > 0)) return false;
        if (r.Stage == MouSunJianOlHulieStage.CounterChoice) return r.UseId == 0 && !r.Accepted || r.UseId == 0 && r.Accepted;
        return r.Accepted && ProgramEventHistory<MouSunJianOlHulieUsedEvent>().Count(e => e.FrameId == f.Id && e.Counter && e.OwnerSeat == f.OwnerSeat && e.Turn == r.Entry.Turn && e.TurnOwner == r.Entry.TurnOwner && e.OriginalUseId == use.Id) == 1 &&
            (r.Stage == MouSunJianOlHulieStage.Issuing ? r.UseId == 0 && !r.NativeReturned : r.UseId > 0 && r.ActionId > 0);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 32500)]
    private void AssertMouSunJianOl(ProgramSkillFrame f)
    {
        if (f.MouSunJianOlYipo is not null && !ValidMouSunJianOlYipo(f) || f.MouSunJianOlHulie is not null && !ValidMouSunJianOlHulie(f)) throw new InvalidOperationException("Mou Sun Jian lost its original source, native parent or accepted typed receipt.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 32500)]
    private bool IsMouSunJianOlMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation movement) => movement == f.PendingMovementContinuation && movement.SubjectSeat == f.OwnerSeat && movement.BeforeCount == 0 && movement.CoverageResultBind is null &&
        (f.MouSunJianOlYipo is { Stage: MouSunJianOlYipoStage.DrawChildren or MouSunJianOlYipoStage.DiscardChildren } && effect?.Op == SkillProgramEffectOp.ResolveMouSunJianYipo && ValidMouSunJianOlYipo(f) ||
         f.MouSunJianOlHulie is { Stage: MouSunJianOlHulieStage.Children } && effect?.Op == SkillProgramEffectOp.ResolveMouSunJianHulieCounter && ValidMouSunJianOlHulie(f));
    private bool MouSunJianOlLionPayment(ProgramSkillFrame f, int seat, CardMoveReason? reason) => f.MouSunJianOlYipo is { Stage: MouSunJianOlYipoStage.DiscardChildren } r && seat == r.TargetSeat && reason?.Value == MouSunJianOlYipoDiscardReason &&
        _cardMovements.Any(m => r.Selected.Contains(m.CardId) && m.CardKind == CardKind.SilverLion && m.From == CardLocation.Equipment(seat) && m.Sequence > r.Before && m.Sequence <= r.After && m.Reason == reason);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 32500)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 32500)]
    private bool IsMouSunJianOlRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery) => ValidMouSunJianOlYipo(f) && f.PendingMovementContinuation is not null && RecoveryReplacementFrameRidesOn(recovery, f) &&
        recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.SourceSeat == recovery.Attempt.TargetSeat &&
        MouSunJianOlLionPayment(f, recovery.Attempt.TargetSeat, recovery.Attempt.Completion.MoveReason);
    private bool MouSunJianOlFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MouSunJianOlYipo is { } r && ValidMouSunJianOlYipo(f))
        {
            if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == MouSunJianOlYipoStage.Drawing && f.NativeDraw is not null;
            if (child is RecoveryReplacementFrame recovery) return IsMouSunJianOlRecoveryParent(f, recovery);
            if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind == HpChangeKind.Recovery &&
                hp.Change.SourceSeat == hp.Change.TargetSeat && MouSunJianOlLionPayment(f, hp.Change.TargetSeat, new(MouSunJianOlYipoDiscardReason));
            if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } pending && IsMouSunJianOlMovement(f, GetPausedPrivateOfferEffect(f), pending))
                return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                    moved.Batch.Movements.All(m => _cardMovements.Contains(m) && (r.Draw is { } d && r.Stage == MouSunJianOlYipoStage.DrawChildren ? m.Sequence > d.SequenceBefore && m.Sequence <= d.SequenceAfter : m.Sequence > r.Before && m.Sequence <= r.After));
        }
        else if (f.MouSunJianOlHulie is { Stage: MouSunJianOlHulieStage.NativeUse } && ValidMouSunJianOlHulie(f) && child is CardUseFrame native) return IsMouSunJianOlCounterUse(native);
        else if (f.MouSunJianOlHulie is null || !ValidMouSunJianOlHulie(f)) return false;
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
                ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouSunJianOlNativeEdge(int n)
    {
        if (_resolutionStack[n - 1] is CardUseFrame { MouSunJianOlCounter: not null } use && IsMouSunJianOlCounterUse(use) && _resolutionStack[n] is ProgramCardTriggerWindowFrame window) return window.ParentFrameId == use.Id && window.Action.ActionId == use.Action!.ActionId;
        if (_resolutionStack[n - 1] is ProgramCardTriggerWindowFrame parent && LifecycleCardUse(parent.ParentFrameId) is { MouSunJianOlCounter: not null } u && IsMouSunJianOlCounterUse(u) && _resolutionStack[n] is ProgramSkillFrame f && parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count)
        { var c = parent.Candidates[parent.CandidateIndex]; return MountObserverCandidateMatches(f, ToSharedCandidate(c)) && f.WindowContext == CreateCardActionProgramContext(parent, c); }
        return false;
    }
    private bool MouSunJianOlObserverEdge(int n) => IsMovementObserverEdge(n) || IsSameNameHandObserverEdge(n) || IsYanPuDyingExitEdge(n) || TypedDamagePostEventEdge(_resolutionStack[n - 1], _resolutionStack[n]) || MouSunJianOlNativeEdge(n) ||
        _resolutionStack[n - 1] is ProgramSkillFrame f && MouSunJianOlFirstChild(f, _resolutionStack[n]) || NativeDrawAttemptRidesOn(_resolutionStack[n], _resolutionStack[n - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[n], _resolutionStack[n - 1]);
    private bool MouSunJianOlObserverSuffix(int n) => HasObserverSuffix(n, static (g, i) => g.MouSunJianOlObserverEdge(i), static (g, i, d) => g.IsSuspendedOrPaidRescueObserverSuffix(i, d));
    private ProgramSkillFrame? MouSunJianOlObserverRoot() => FindProgramObserverRoot(static (g, f, c) => g.MouSunJianOlFirstChild(f, c), static (g, n) => g.MouSunJianOlObserverSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 32500)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 32500)]
    private bool HasMouSunJianOlDamage(long id) => MouSunJianOlObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 32500)]
    private bool HasMouSunJianOlBeforeDamage(long id) => MouSunJianOlObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 32500)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 32500)]
    private bool IsMouSunJianOlDying() => ActiveDying is { } d && MouSunJianOlObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 32500)] [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 32500)]
    private bool AllowsMouSunJianOlNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || MouSunJianOlObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature && target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 32500)]
    private bool AdvanceMouSunJianOlChildren() => _pendingDecision is null && MouSunJianOlObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 32500)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 32500)]
    private bool ContinuePaidMouSunJianOl(ProgramSkillFrame f) => f.MouSunJianOlYipo is { Branch: not null } && ValidMouSunJianOlYipo(f) || f.MouSunJianOlHulie is { Accepted: true } && ValidMouSunJianOlHulie(f) ||
        f.MouSunJianOlHulie is null && f.TriggerId == "undamaged-counter" && f.InstructionIndex is 0 or 1 && f.WindowContext is { } w && CanRunIssuedMouSunJianOlHulie(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, 0), w);
}
