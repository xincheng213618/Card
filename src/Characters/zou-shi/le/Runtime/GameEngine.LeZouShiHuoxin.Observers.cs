namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 270)]
    private bool IsHuoxinMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) =>
        f.Huoxin is { Paid: true, Stage: HuoxinStage.ObtainChildren, PaidSequence: > 0 } &&
        effect is { Op: SkillProgramEffectOp.ResolveHuoxinNonEquipmentUse, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } &&
        p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool HuoxinFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.Huoxin is not { Stage: HuoxinStage.ObtainChildren } r || !ValidHuoxin(f)) return false;
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state)
            return state.ResumeProgramFrameId == f.Id && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
                ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id && e.Change.Window == state.Window);
        return child is CardsMovedTriggerWindowFrame moved && ProgramMovementReturnMatches(f, moved) &&
            moved.Batch.Id == r.BatchId && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat &&
            moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
            moved.Batch.Movements is [var m] && m.CardId == r.CardId && m.CardKind == r.PrintedKind &&
            m.From == CardLocation.Hand(r.TargetSeat) && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == HuoxinObtainReason && m.Sequence == r.PaidSequence;
    }
    private bool HuoxinObserverSuffix(int index) => HasObserverSuffix(index, static (g, n) =>
        g.IsMovementObserverEdge(n) || g.IsSameNameHandObserverEdge(n) || g.IsYanPuDyingExitEdge(n) ||
        g.TypedDamagePostEventEdge(g._resolutionStack[n - 1], g._resolutionStack[n]) ||
        g._resolutionStack[n - 1] is ProgramSkillFrame p && (g.HuoxinFirstChild(p, g._resolutionStack[n]) || p.Zenrun is not null && g.ZenrunFirstChild(p, g._resolutionStack[n])) ||
        NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) ||
        g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]),
        static (g, n, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(n, dying));
    private ProgramSkillFrame? HuoxinObserverRoot() => FindProgramObserverRoot(
        static (g, f, child) => g.HuoxinFirstChild(f, child), static (g, i) => g.HuoxinObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1220)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1330)]
    private bool HasHuoxinDamageObserver(long id) => HuoxinObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1360)]
    private bool HasHuoxinBeforeDamageObserver(long id) => HuoxinObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 1190)]
    private bool IsHuoxinDying() => ActiveDying is { } d && HuoxinObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1760)]
    private bool AllowsHuoxinNestedDamage(ProgramSkillFrame f, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || HuoxinObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 450)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 440)]
    private bool IsHuoxinRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.Huoxin is null || recovery.ParentFrameId == f.Id) return false;
        var parent = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return parent >= 0 && child > parent + 1 && HuoxinFirstChild(f, _resolutionStack[parent + 1]) &&
            HuoxinObserverSuffix(parent) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 28951)]
    private bool AdvanceHuoxinChildren() => _pendingDecision is null && HuoxinObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
