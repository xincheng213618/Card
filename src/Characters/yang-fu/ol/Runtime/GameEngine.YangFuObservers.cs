namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 27310)]
    private void AssertYangFu(ProgramSkillFrame f)
    {
        if (f.YangFu is not { } r) return;
        if (r.Source != YangFuSource(f) || r.GameplayHash != f.GameplayHash || r.ActualTurn != _turnNumber || r.ParentId != (f.WindowContext?.ParentFrameId ?? 0) ||
            f.InstructionIndex != 1 || !YangFuComposition.Handles(r.Op) || GetPausedPrivateOfferEffect(f)?.Op != r.Op || f.SelectedCardIds.Count != 0 || !Enum.IsDefined(r.Stage) ||
            ProgramEventHistory<YangFuStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var start] ||
            start != new YangFuStartedEvent(f.Id, r.Source, r.GameplayHash, r.Op, r.ActualTurn, r.ParentId, r.TargetSeat, r.X, r.Ordinal, r.UseId, r.ActionId, r.OriginalTargets.Count) ||
            r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            (f.TriggerId is null ? ProgramEventHistory<ProgramSkillStartedEvent>().Count(e => e.FrameId == f.Id && e.SkillId == f.SkillId && e.OwnerSeat == f.OwnerSeat && e.ActivationId == f.ActivationId) != 1 :
                ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == f.Id && e.SkillId == f.SkillId && e.OwnerSeat == f.OwnerSeat && e.BindingId == GetProgramBindingId(f) && e.SkillInstanceId == f.SkillInstanceId) != 1))
            throw new InvalidOperationException($"Yang Fu lost his exact issuing receipt: frame={f.Id}, op={r.Op}, stage={r.Stage}, source={r.Source == YangFuSource(f)}, hash={r.GameplayHash == f.GameplayHash}, turn={r.ActualTurn}/{_turnNumber}, parent={r.ParentId}/{f.WindowContext?.ParentFrameId ?? 0}, instruction={f.InstructionIndex}, starts={ProgramEventHistory<YangFuStartedEvent>().Count(e => e.FrameId == f.Id)}, activationStarts={ProgramEventHistory<ProgramSkillStartedEvent>().Count(e => e.FrameId == f.Id)}, triggerStarts={ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == f.Id)}.");
        if (r.Op == SkillProgramEffectOp.ResolveBingzhongExtraTargets)
        {
            var use = RequireBingzhongUse(f, out _);
            if (r.Stage != YangFuStage.Targets || r.X < 1 || r.Ordinal is not > 0 || r.UseId != use.Id || r.ActionId != use.Action!.ActionId ||
                !r.OriginalTargets.SequenceEqual(use.TargetSeats) ||
                !ProgramEventHistory<BingzhongOriginalTargetEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).Select(e => e.Seat).SequenceEqual(r.OriginalTargets) ||
                !ProgramEventHistory<BingzhongTargetSelectedEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).Select(e => e.Seat).SequenceEqual(r.AddedTargets) ||
                r.AddedTargets.Count / (use.CardKind == CardKind.BorrowedSword ? 2 : 1) > r.X || f.PendingMovementContinuation is not null || r.NativeOrigin is not null)
                throw new InvalidOperationException("Bingzhong changed its frozen X, original targets or scalar target selection invoices.");
            if (_resolutionStack.LastOrDefault()?.Id == f.Id && _pendingDecision is { } prompt &&
                (prompt.PlayerSeat != f.OwnerSeat || prompt.IsPrivate || prompt.SkillPrompt?.SkillId != f.SkillId || !AssistedChoicesEqual(prompt.Choices, BingzhongChoices(f))))
                throw new InvalidOperationException("Bingzhong's public prompt changed its actual normal-range choices.");
        }
        else if (r.Op == SkillProgramEffectOp.ResolveBingzhongAfterDamageDraw)
        {
            if (r.Stage != YangFuStage.DrawChildren || r.X < 1 || r.TargetSeat != f.OwnerSeat || f.SelectedTargetSeats.Count != 0 ||
                f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0, DamageFrameId: { } damageId } c || c.TargetSeat != f.OwnerSeat ||
                _resolutionStack.OfType<DamageFrame>().SingleOrDefault(d => d.Id == damageId) is not { } damage || damage.Amount != c.Amount || damage.TargetSeat != f.OwnerSeat ||
                _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w => w.Id == r.ParentId) is not { } window || window.ParentFrameId != damageId || window.TriggerWindow != c.Window ||
                window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count || !MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex].ToProgramCandidate()) ||
                ProgramEventHistory<BingzhongDrawPaidEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var paid] ||
                paid != new BingzhongDrawPaidEvent(f.Id, r.X, r.DrawCount, r.Before, r.After) || r.DrawCount < 0 || r.DrawCount > r.X ||
                _cardMovements.Count(m => m.Sequence > r.Before && m.Sequence <= r.After && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == BingzhongDrawReason) != r.DrawCount)
                throw new InvalidOperationException("Bingzhong lost its real single native draw and frozen dynamic X.");
        }
        else
        {
            if (f.TriggerId is not null || f.WindowContext is not null || f.SelectedTargetSeats is not [var target] || target != r.TargetSeat || target == f.OwnerSeat ||
                r.Stage is not (YangFuStage.EquipmentUse or YangFuStage.EquipmentChildren or YangFuStage.SlashUse or YangFuStage.SlashChildren) || r.X != 0 || r.Ordinal is not null || r.UseId is not null || r.ActionId is not null)
                throw new InvalidOperationException("Suyi changed its original foreign equipment recipient or native stage.");
            if (r.NativeOrigin is { } origin && (origin.ProgramFrameId != f.Id || origin.Source != r.Source || origin.GameplayHash != r.GameplayHash || origin.ActualTurn != r.ActualTurn ||
                ProgramEventHistory<SuyiNativeIssuedEvent>().Count(e => e.Origin == origin) != 1 ||
                (r.NativeCompleted ? ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == origin.UseId) != 1 : LifecycleCardUse(origin.UseId) is not { } use || !IsIssuedSuyiUse(use))))
                throw new InvalidOperationException("Suyi lost its exact native equipment/Slash issuance and completion.");
        }
        var pending = r.Stage is YangFuStage.DrawChildren or YangFuStage.EquipmentChildren or YangFuStage.SlashChildren || r.Stage == YangFuStage.EquipmentUse && r.NativeOrigin is not null;
        if (pending ? f.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } p || p.SubjectSeat != (r.Stage is YangFuStage.EquipmentChildren or YangFuStage.EquipmentUse ? r.TargetSeat : f.OwnerSeat) : f.PendingMovementContinuation is not null)
            throw new InvalidOperationException("Yang Fu lost the native movement continuation on its owning frame.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 27310)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 27310)]
    private bool ContinuePaidYangFu(ProgramSkillFrame f)
    { if (f.YangFu is not { } r || r.Stage == YangFuStage.Targets) return false; AssertYangFu(f); return true; }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 300)]
    private bool IsYangFuMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p)
    {
        if (f.YangFu is not { } r || e?.Op != r.Op || p != f.PendingMovementContinuation ||
            r.Stage is not (YangFuStage.DrawChildren or YangFuStage.EquipmentChildren or YangFuStage.SlashChildren) &&
                !(r.Stage == YangFuStage.EquipmentUse && r.NativeOrigin is { } origin && LifecycleCardUse(origin.UseId) is { } use && IsIssuedSuyiUse(use))) return false;
        AssertYangFu(f); return true;
    }
    private bool YangFuFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.YangFu is not { } r) return false; AssertYangFu(f);
        if (child is CardUseFrame use) return r.Stage is YangFuStage.EquipmentUse or YangFuStage.SlashUse && use.SuyiOrigin == r.NativeOrigin && IsIssuedSuyiUse(use);
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills) return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        return child is CardsMovedTriggerWindowFrame moved && r.Stage is YangFuStage.DrawChildren or YangFuStage.EquipmentChildren or YangFuStage.SlashChildren &&
            ProgramMovementReturnMatches(f, moved) && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
    }
    private ProgramSkillFrame? YangFuObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.YangFuFirstChild(f, c), static (e, i) => e.HasObserverSuffix(i,
        static (x, n) => x.IsMovementObserverEdge(n) || x.IsYanPuDyingExitEdge(n) || x.RandomEquipmentFrameRidesOn(x._resolutionStack[n], x._resolutionStack[n - 1]) || x.RecoveryReplacementFrameRidesOn(x._resolutionStack[n], x._resolutionStack[n - 1]),
        static (x, n, d) => x.IsSuspendedOrPaidRescueObserverSuffix(n, d)));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 770)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 880)]
    private bool HasYangFuDamageObserver(long id) => YangFuObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 910)]
    private bool HasYangFuBeforeDamageObserver(long id) => YangFuObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 740)]
    private bool IsYangFuDying() => ActiveDying is { } dying && YangFuObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1310)]
    private bool AllowsYangFuNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || YangFuObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(f, reference) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 27310)]
    private bool AdvanceYangFuObserver() => _pendingDecision is null && YangFuObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
