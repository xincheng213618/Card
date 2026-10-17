namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsMoTianenMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) =>
        f.MoTianen is { PaymentSealed: true, Stage: MoTianenStage.DiscardChildren or MoTianenStage.ObtainChildren } &&
        effect is { Op: SkillProgramEffectOp.ResolveMoSunQuanTianenUniqueUse, Target: SkillProgramEffectTarget.Owner,
            Condition.Kind: SkillProgramConditionKind.Always } && p == f.PendingMovementContinuation &&
        p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool IsMoQiangangMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) =>
        f.MoQiangang is { Stage: MoQiangangStage.TianenLossChildren or MoQiangangStage.EntryChildren } &&
        effect is { Op: SkillProgramEffectOp.ResolveMoSunQuanQiangangEntry, Target: SkillProgramEffectTarget.Owner,
            Condition.Kind: SkillProgramConditionKind.Always } && p == f.PendingMovementContinuation &&
        p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;

    private bool MoTianenLionRemoval(ProgramSkillFrame f, int seat) => f.MoTianen is
        { PaymentSealed: true, Branch: MoTianenBranch.Different, Material: { PrintedKind: CardKind.SilverLion } material } &&
        material.From == CardLocation.Equipment(seat);
    private bool MoTianenFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MoTianen is not { } r || !ValidMoTianen(f)) return false;
        if (r.Stage == MoTianenStage.QuanyuChild) return MoQuanyuChildRidesOn(child, f);
        if (child is CardsMovedTriggerWindowFrame moved)
            return f.PendingMovementContinuation is not null && ProgramMovementReturnMatches(f, moved) &&
                moved.Batch.Movements.All(m => r.Movements.Any(p => p.CardId == m.CardId && p.PrintedKind == m.CardKind &&
                    p.From == m.From && p.To == m.To && p.Reason == m.Reason.Value && p.Sequence == m.Sequence));
        if (child is RecoveryReplacementFrame recovery) return IsMoSunQuanPaidRecoveryParent(f, recovery);
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == hp.Change.TargetSeat && hp.Change.Amount == 1 &&
                MoTianenLionRemoval(f, hp.Change.TargetSeat);
        return MoSunQuanPaidStateChild(f, child);
    }
    private bool MoQiangangFirstChild(ProgramSkillFrame f, ResolutionFrame child) =>
        f.MoQiangang is { Stage: MoQiangangStage.TianenLossChildren or MoQiangangStage.EntryChildren } &&
        ValidMoQiangang(f) && MoSunQuanPaidStateChild(f, child);
    private bool MoSunQuanPaidStateChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
            ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id && e.Change.Window == state.Window);
    }
    private bool MoSunQuanPaidObserverSuffix(int index) => HasObserverSuffix(index, static (g, n) =>
        g.IsMovementObserverEdge(n) || g.IsSameNameHandObserverEdge(n) || g.IsYanPuDyingExitEdge(n) ||
        g.TypedDamagePostEventEdge(g._resolutionStack[n - 1], g._resolutionStack[n]) ||
        g._resolutionStack[n - 1] is ProgramSkillFrame p &&
            (p.Zenrun is not null && g.ZenrunFirstChild(p, g._resolutionStack[n]) ||
             g.MoQuanyuFirstChild(p, g._resolutionStack[n]) || g.MoTianenFirstChild(p, g._resolutionStack[n]) ||
             g.MoQiangangFirstChild(p, g._resolutionStack[n])) ||
        NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) ||
        g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]),
        static (g, n, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(n, dying));
    private ProgramSkillFrame? MoSunQuanPaidObserverRoot() => FindProgramObserverRoot(
        static (g, f, child) => g.MoTianenFirstChild(f, child) || g.MoQiangangFirstChild(f, child),
        static (g, index) => g.MoSunQuanPaidObserverSuffix(index));
    private bool HasMoSunQuanPaidDamageObserver(long id) => MoSunQuanPaidObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasMoSunQuanPaidBeforeDamageObserver(long id) => MoSunQuanPaidObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsMoSunQuanPaidDying() => ActiveDying is { } dying && MoSunQuanPaidObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsMoSunQuanPaidNestedDamage(ProgramSkillFrame f, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id ||
            MoSunQuanPaidObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    private bool IsMoSunQuanPaidRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.MoTianen is null && f.MoQiangang is null) return false;
        if (recovery.ParentFrameId == f.Id)
            return ValidMoTianen(f) && RecoveryReplacementFrameRidesOn(recovery, f) &&
                recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
                recovery.Attempt.SourceSeat == recovery.Attempt.TargetSeat && recovery.Attempt.Amount == 1 &&
                recovery.Attempt.Completion.MoveReason?.Value == MoTianenDiscardReason && MoTianenLionRemoval(f, recovery.Attempt.TargetSeat);
        var parent = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return parent >= 0 && child > parent + 1 && (MoTianenFirstChild(f, _resolutionStack[parent + 1]) || MoQiangangFirstChild(f, _resolutionStack[parent + 1])) &&
            MoSunQuanPaidObserverSuffix(parent) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 28920)]
    private bool AdvanceMoSunQuanPaidChildren() => _pendingDecision is null && MoSunQuanPaidObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
