namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidShiZy(ProgramSkillFrame f) => f.ShiZhangYan is { } r && f.InstructionIndex == 1 && r.Source == ShiZySource(f) && r.Hash == f.GameplayHash &&
        r.Turn == _turnNumber && r.WindowId == f.WindowContext?.ParentFrameId && Enum.IsDefined(r.Stage) && r.Before >= 0 && r.After >= r.Before && r.After <= _movementSequence &&
        ProgramEventHistory<ShiZyStartedEvent>().Count(e => e == new ShiZyStartedEvent(f.Id, r.Source, r.Hash, r.Op, r.Turn, r.WindowId, r.UseId, r.ActionId, r.Target, r.OriginalAttackActive, r.OriginalTargetIndex)) == 1;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 50500)]
    private void AssertShiZy(ProgramSkillFrame f)
    {
        if (f.ShiZhangYan is not { } r) return;
        if (!ValidShiZy(f) || !IsValidPlayerSeat(r.Target) || r.Plans.Select(p => p.Seat).Distinct().Count() != r.Plans.Count || r.Cursor < 0 || r.Cursor > r.Plans.Count ||
            r.Plans.Any(p => !IsValidPlayerSeat(p.Seat) || p.Seat == f.OwnerSeat || p.Seat == r.Target || p.Selected > 0 && !p.Hand.Contains(p.Selected) || p.Hand.Count != p.Hand.Distinct().Count()) ||
            (r.Op == SkillProgramEffectOp.ResolveMobileShiZhangYanXiaogeDuel
                ? r.OriginalAttackActive is null || r.OriginalTargetIndex is not >= 0
                : r.OriginalAttackActive is not null || r.OriginalTargetIndex is not null) ||
            r.Op == SkillProgramEffectOp.ResolveMobileShiZhangYanFeijing && (f.SkillId != ShiZyFeijing || f.TriggerId != "sole-slash-path" || r.Stage is ShiZyStage.RecoverChildren or ShiZyStage.ObtainChildren or ShiZyStage.DuelIssued or ShiZyStage.DuelChildren) ||
            r.Op == SkillProgramEffectOp.ResolveMobileShiZhangYanXiaogePrevent && (f.SkillId != ShiZyXiaoge || f.TriggerId != "extra-target-prevent" || r.CardId <= 0 || r.PaymentSequence <= 0 ||
                !ProgramEventHistory<ShiZyHandDiscardedEvent>().Any(e => e.Seat == r.Target && e.CardId == r.CardId && e.Sequence == r.PaymentSequence)))
            throw new InvalidOperationException("Shi Zhang Yan lost the original native window, frozen route, independent Hand commitments or exact discard entity.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && r.Stage is ShiZyStage.Path or ShiZyStage.Commit or ShiZyStage.Color &&
            (_pendingDecision is not { } p || p.PlayerSeat != ShiZyChooser(f) || p.IsPrivate != (r.Stage == ShiZyStage.Commit) || !AssistedChoicesEqual(p.Choices, ShiZyChoices(f))))
            throw new InvalidOperationException("Feijing lost its original public route or chooser-private Hand decision.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 50500)]
    [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments, 50500)]
    private bool ShiZyMovementProof(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => f.ShiZhangYan is { } r && ValidShiZy(f) &&
        r.Stage is ShiZyStage.DiscardChildren or ShiZyStage.ObtainChildren && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null &&
        e?.Op is SkillProgramEffectOp.ResolveMobileShiZhangYanFeijing or SkillProgramEffectOp.ResolveMobileShiZhangYanXiaogePrevent;
    private bool ShiZyFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidShiZy(f) || f.ShiZhangYan is not { } r) return false;
        if (child is CardUseFrame use) return r.Stage == ShiZyStage.DuelIssued && use.Id == r.NativeUseId && ValidShiZyVirtualDuelUse(use);
        if (child is PublicHandRevealWindowFrame reveal) return r.Stage == ShiZyStage.RevealChildren && reveal.ParentFrameId == f.Id && reveal.ReturnKind == PublicHandRevealReturnKind.Registered && reveal.ReturnKey == "mobile-shi-zhang-yan-path";
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } p && ShiZyMovementProof(f, GetPausedPrivateOfferEffect(f), p))
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId &&
                moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
        if (child is RecoveryReplacementFrame recovery) return recovery.ParentFrameId == f.Id && RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.ResumeFrameId == f.Id;
        if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && state.CharacterStateContinuation == CharacterStateContinuation.Program);
    }
    private bool ShiZyEdge(int i) => SkillInvocationStructuralEdge(_resolutionStack[i - 1], _resolutionStack[i]) || IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) ||
        TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) || _resolutionStack[i - 1] is ProgramSkillFrame f && ShiZyFirstChild(f, _resolutionStack[i]) ||
        RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]);
    private bool ShiZySuffix(int n) => _resolutionStack[n + 1] is DyingFrame d && n + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(n + 1, d) ||
        HasObserverSuffix(n, static (e, i) => e.ShiZyEdge(i), static (e, i, dying) => e.IsSuspendedOrPaidRescueObserverSuffix(i, dying));
    private ProgramSkillFrame? ShiZyObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.ShiZyFirstChild(f, c), static (e, n) => e.ShiZySuffix(n));
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 50500)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 50500)]
    private bool ShiZyDyingChild() => ActiveDying is { } d && ShiZyObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 50500)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 50500)]
    private bool ShiZyDamageChild(long id) => ShiZyObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 50500)]
    private bool ShiZyBeforeDamageChild(long id) => ShiZyObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 50500)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 50500)]
    private bool ShiZyRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (ShiZyFirstChild(f, recovery)) return true;
        var n = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return n >= 0 && c > n + 1 && ShiZyFirstChild(f, _resolutionStack[n + 1]) && ShiZySuffix(n) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[c - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 50500)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 50500)]
    private bool KeepShiZyPaid(ProgramSkillFrame f) => f.ShiZhangYan is { Stage: not ShiZyStage.Path } && ValidShiZy(f);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 50500)]
    private bool AdvanceShiZyChildren() => _pendingDecision is null && ShiZyObserverRoot() is not null && TryAdvanceObserverSubtree(true);

    private bool ShiZyIssuedExtra(long id, int actor, int target) => LifecycleCardUse(id) is { ShiZhangYanPath: { } path, Action: { } a } &&
        path.UseId == id && path.ActionId == a.ActionId && a.ActorSeat == actor && path.Extra.Contains(target) &&
        ProgramEventHistory<ShiZyTargetAddedEvent>().Any(e => e.UseId == id && e.ActionId == a.ActionId && e.Target == target && e.FrameId == path.ProgramFrameId);
    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 50500)]
    private bool ShiZyAddedTargetDistance(ProgramCardDistanceQuery q) => q.CardUseFrameId is { } id && ShiZyIssuedExtra(id, q.Actor.Seat, q.Target.Seat);
    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 50500)] [ProgramDistanceRule(ProgramDistanceStage.VirtualSlash, 50500)]
    [ProgramDistanceRule(ProgramDistanceStage.ProvidedSlash, 50500)] [ProgramDistanceRule(ProgramDistanceStage.BorrowedSlash, 50500)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPhysicalSlash, 50500)] [ProgramDistanceRule(ProgramDistanceStage.BorrowedPaidWeaponSlash, 50500)]
    private bool ShiZyAddedSlashDistance(ProgramSlashDistanceQuery q) => q.ExistingUseFrameId is { } id && ShiZyIssuedExtra(id, q.Actor.Seat, q.Target.Seat);
}
