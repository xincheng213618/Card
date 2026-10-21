namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 36600)] private bool MouZhangXiuAnniversaryMovement(ProgramSkillFrame f, SkillProgramEffect? op, ProgramMovementContinuation p) =>
        f.MouZhangXiuAnniversary is not null && (op?.Op is SkillProgramEffectOp.ResolveMouZhangXiuFuxi or SkillProgramEffectOp.ResolveMouZhangXiuHaoyi) && ValidMouZhangXiuAnniversary(f) && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool MouZhangXiuAnniversaryFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouZhangXiuAnniversary(f) || f.MouZhangXiuAnniversary is not { } r) return false;
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == MouZhangXiuAnniversaryStage.Drawing && f.NativeDraw is not null;
        if (child is CardUseFrame use) return r.Stage == MouZhangXiuAnniversaryStage.NativeSlash && use.MouZhangXiuAnniversarySlash is { } o && o.FrameId == f.Id && use.Id == r.UseId && IsIssuedMouZhangXiuAnniversaryUse(use);
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } p && MouZhangXiuAnniversaryMovement(f, GetPausedPrivateOfferEffect(f), p))
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements.All(m => _cardMovements.Contains(m) && (r.Stage == MouZhangXiuAnniversaryStage.SlashChildren || m.Sequence > r.Before && m.Sequence <= r.After));
        var lion = r.Cards.FirstOrDefault(c => c.From.Zone == CardZoneKind.Equipment && c.Kind == CardKind.SilverLion && _cardMovements.Any(m => m.CardId == c.Id && m.From == c.From && m.Sequence > r.Before && m.Sequence <= r.After));
        if (lion?.From.OwnerSeat is { } holder)
        {
            if (child is RecoveryReplacementFrame rr) return RecoveryReplacementFrameRidesOn(rr, f) && rr.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && rr.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && rr.Attempt.SourceSeat == holder && rr.Attempt.TargetSeat == holder;
            if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.TargetSeat == holder;
        }
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouZhangXiuAnniversaryEdge(int i) => IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) || TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) ||
        _resolutionStack[i - 1] is ProgramSkillFrame f && MouZhangXiuAnniversaryFirstChild(f, _resolutionStack[i]) || NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) ||
        DyingSuitsStructuralEdge(_resolutionStack[i - 1], _resolutionStack[i]);
    private bool MouZhangXiuAnniversarySuffix(int n) => _resolutionStack[n + 1] is DyingFrame d && n + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(n + 1, d) || HasObserverSuffix(n, static (g,i) => g.MouZhangXiuAnniversaryEdge(i), static (g,i,dying) => g.IsSuspendedOrPaidRescueObserverSuffix(i,dying));
    private ProgramSkillFrame? MouZhangXiuAnniversaryRoot() => FindProgramObserverRoot(static (g,f,c) => g.MouZhangXiuAnniversaryFirstChild(f,c), static (g,n) => g.MouZhangXiuAnniversarySuffix(n));
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 36600)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 36600)] private bool MouZhangXiuAnniversaryDying() => ActiveDying is { } d && MouZhangXiuAnniversaryRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 36600)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 36600)] private bool MouZhangXiuAnniversaryDamageWindow(long id) => MouZhangXiuAnniversaryRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 36600)] private bool MouZhangXiuAnniversaryBeforeDamage(long id) => MouZhangXiuAnniversaryRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 36600)] [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 36600)]
    private bool MouZhangXiuAnniversaryNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess || MouZhangXiuAnniversaryRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature && target == (e.TargetReference is { } p ? ResolveProgramParticipant(f,p) : ResolveProgramEffectTarget(f,e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 36600)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 36600)] private bool MouZhangXiuAnniversaryRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (MouZhangXiuAnniversaryFirstChild(f, recovery)) return true;
        var n = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return n >= 0 && c > n + 1 && MouZhangXiuAnniversaryFirstChild(f, _resolutionStack[n + 1]) && MouZhangXiuAnniversarySuffix(n) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[c - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 36600)] private bool MouZhangXiuAnniversaryAdvanceChildren() => _pendingDecision is null && MouZhangXiuAnniversaryRoot() is not null && TryAdvanceObserverSubtree(true);
}
