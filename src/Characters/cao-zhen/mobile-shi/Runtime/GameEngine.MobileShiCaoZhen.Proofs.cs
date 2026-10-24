namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidShiCaoZ(ProgramSkillFrame f) => f.ShiCaoZhen is { } r && f.SkillId == ShiCaoZSifeng && f.InstructionIndex == 1 &&
        r.Source == ShiCaoZSource(f) && r.Hash == f.GameplayHash && _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash == r.Hash &&
        r.Turn == _turnNumber && r.TurnOwner == _currentSeat && r.ParentId == f.WindowContext?.ParentFrameId && Enum.IsDefined(r.Stage) &&
        r.Before >= 0 && r.After >= r.Before && r.After <= _movementSequence && GetPausedPrivateOfferEffect(f)?.Op == r.Operation &&
        ProgramEventHistory<ShiCaoZStartedEvent>().Count(e => e == new ShiCaoZStartedEvent(f.Id, r.Source, r.Hash, r.Turn, r.TurnOwner, r.ParentId, r.Operation)) == 1;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 49900)]
    private void RequireShiCaoZ(ProgramSkillFrame f)
    {
        if (f.ShiCaoZhen is not { } r) return;
        if (!ValidShiCaoZ(f) || r.Pool.Count > 3 || r.Pool.Count != r.Pool.Distinct().Count() || r.Assigned is < 0 or > 3 ||
            r.Materials.Select(m => m.PlacementId).Distinct().Count() != r.Materials.Count || r.Materials.Any(m => !ValidShiCaoZMaterial(m) || m.Source.OwnerSeat != f.OwnerSeat) ||
            r.Operation == SkillProgramEffectOp.ResolveMobileShiCaoZhenSifengPlace && (f.TriggerId != "place-forecast" || f.WindowContext?.Window != SkillProgramTriggerWindow.TurnEnding || r.Pool.Any(id => !ProgramEventHistory<ShiCaoZTopTakenEvent>().Any(e => e.FrameId == f.Id && e.CardId == id))) ||
            r.Operation == SkillProgramEffectOp.ResolveMobileShiCaoZhenSifengUse && (f.TriggerId != "issued-use" || r.Materials.Count != 1 || r.Holder != r.Materials[0].Holder ||
                ProgramEventHistory<ShiCaoZUseIssuedEvent>().Count(e => e.FrameId == f.Id && e.UseId == r.UseId && e.ActionId == r.ActionId && e.PlacementId == r.Materials[0].PlacementId) != 1) ||
            r.Operation == SkillProgramEffectOp.ResolveMobileShiCaoZhenSifengEnd && (f.TriggerId != "issued-end" || r.Materials.Any(m => m.Holder != r.Holder) ||
                ProgramEventHistory<ShiCaoZEndIssuedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.Turn == r.Turn && e.Holder == r.Holder && e.Count == r.Materials.Count) != 1))
            throw new InvalidOperationException("Sifeng lost its original source/hash, actual native trigger, ordered physical forecast or one issued obligation.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && f.Step == ResolutionFrameStep.AwaitingResponse && r.Stage is ShiCaoZStage.Assign or ShiCaoZStage.Discard or ShiCaoZStage.EndChoice &&
            (_pendingDecision is not { } prompt || prompt.PlayerSeat != ShiCaoZChooser(f) || !prompt.IsPrivate || !AssistedChoicesEqual(prompt.Choices, ShiCaoZChoices(f))))
            throw new InvalidOperationException("Sifeng lost its exact issuer-only ordering or target-owned hand-only payment prompt.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 49900)]
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 49900)]
    [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments, 49900)]
    private bool ShiCaoZMovementProof(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => f.ShiCaoZhen is { Paid: true } r && ValidShiCaoZ(f) &&
        e?.Op == r.Operation && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null &&
        r.Stage is ShiCaoZStage.TopChildren or ShiCaoZStage.AssignmentChildren or ShiCaoZStage.UseRemovalChildren or ShiCaoZStage.DiscardChildren or ShiCaoZStage.EndChildren or ShiCaoZStage.CleanupChildren;
    private bool ShiCaoZOwnMovement(ProgramSkillFrame f, CardMovementRecord m)
    {
        var r = f.ShiCaoZhen!;
        if (m.Sequence <= r.Before || m.Sequence > r.After || !_cardMovements.Contains(m)) return false;
        if (r.Stage == ShiCaoZStage.TopChildren) return m.From == CardLocation.DrawPile && m.To == CardLocation.Processing && m.Reason.Value == ShiCaoZTopReason || m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle;
        if (r.Stage == ShiCaoZStage.AssignmentChildren) return m.From == CardLocation.Processing && m.To.Zone == CardZoneKind.PrivatePersistentPile && m.Reason.Value == ShiCaoZPlaceReason && ProgramEventHistory<ShiCaoZPlacedEvent>().Any(e => e.Material.SetupId == f.Id && e.Material.CardId == m.CardId && e.Material.Sequence == m.Sequence);
        if (r.Stage == ShiCaoZStage.DiscardChildren) return m.From == CardLocation.Hand(r.Holder) && m.To.Zone is CardZoneKind.DiscardPile or CardZoneKind.OutsideGame && m.Reason.Value == ShiCaoZDiscardReason;
        if (r.Stage == ShiCaoZStage.CleanupChildren) return r.Pool.Contains(m.CardId) && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason.Value == ShiCaoZRemoveReason;
        return r.Materials.Any(c => c.CardId == m.CardId && c.Location == m.From) &&
            (m.To == CardLocation.DiscardPile && m.Reason.Value == ShiCaoZRemoveReason || m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == ShiCaoZObtainReason);
    }
    private bool ShiCaoZFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidShiCaoZ(f) || f.ShiCaoZhen is not { Paid: true } r) return false;
        if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is { } p && ShiCaoZMovementProof(f, GetPausedPrivateOfferEffect(f), p) &&
            ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
            moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => ShiCaoZOwnMovement(f, m));
        if (child is DamageFrame damage) return r.Stage == ShiCaoZStage.DamageChildren && damage.ParentFrameId == f.Id && damage.SourceSeat == f.OwnerSeat && f.AttackAttempt is { } a && damage.TargetSeat == a.TargetSeat && damage.Amount == a.DamageAmount;
        if (child is BeforeDamageProgramWindowFrame before) return r.Stage == ShiCaoZStage.DamageChildren && (before.ContinuationAttackResolutionId ?? before.ParentFrameId) == f.Id && f.AttackAttempt is not null;
        if (child is RecoveryReplacementFrame recovery) return recovery.ParentFrameId == f.Id && recovery.Return.ResumeFrameId == f.Id && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement;
        if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && state.CharacterStateContinuation == CharacterStateContinuation.Program);
    }
    private bool ShiCaoZChildEdge(int i) => SkillInvocationStructuralEdge(_resolutionStack[i - 1], _resolutionStack[i]) || IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) ||
        TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) || _resolutionStack[i - 1] is ProgramSkillFrame f && ShiCaoZFirstChild(f, _resolutionStack[i]) ||
        NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]);
    private bool ShiCaoZChildSuffix(int i) => HasObserverSuffix(i, static (g, n) => g.ShiCaoZChildEdge(n),
        static (g, n, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(n, dying) || g.TieredRoundZeroDyingRescueRide(n, dying) || g.DrawFundedDistinctBasicDyingRescueRide(n, dying) || g.HuanpeiDyingRescueRide(n, dying));
    private ProgramSkillFrame? ShiCaoZObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.ShiCaoZFirstChild(f, child), static (g, i) => g.ShiCaoZChildSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 49900)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 49900)]
    private bool ShiCaoZDamageObserver(long id) => ShiCaoZObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 49900)] private bool ShiCaoZBeforeDamageObserver(long id) => ShiCaoZObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 49900)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 49900)]
    private bool ShiCaoZDyingObserver() => ActiveDying is { } dying && ShiCaoZObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 49900)] [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 49900)]
    private bool ShiCaoZNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || ShiCaoZObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature && target == (e.TargetReference is { } reference ? ResolveProgramParticipant(f, reference) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 49900)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 49900)]
    private bool ShiCaoZRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (ShiCaoZFirstChild(f, recovery)) return true;
        var i = _resolutionStack.FindIndex(p => p.Id == f.Id); var child = _resolutionStack.FindIndex(p => p.Id == recovery.Id);
        return i >= 0 && child > i + 1 && ShiCaoZFirstChild(f, _resolutionStack[i + 1]) && ShiCaoZChildSuffix(i) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 49900)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 49900)]
    private bool KeepShiCaoZIssued(ProgramSkillFrame f) => f.ShiCaoZhen is { Paid: true } && ValidShiCaoZ(f) ||
        f.SkillId == ShiCaoZSifeng && f.ShiCaoZhen is null && f.InstructionIndex is 0 or 1 && f.WindowContext is { } context &&
        IsIssuedShiCaoZCandidate(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex), context);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 49900)]
    private bool AdvanceShiCaoZChildren() => _pendingDecision is null && ShiCaoZObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
