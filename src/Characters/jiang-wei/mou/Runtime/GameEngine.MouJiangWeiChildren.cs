namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 35200)]
    private bool MouJiangWeiAnniversaryMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) => f.MouJiangWeiAnniversary is { Paid: true } r &&
        effect?.Op == r.Operation && MouJiangWeiAnniversary.Valid(f) && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool MouJiangWeiAnniversaryFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!MouJiangWeiAnniversary.Valid(f) || f.MouJiangWeiAnniversary is not { } r) return false;
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == MouJiangWeiAnniversaryStage.Drawing && f.NativeDraw is not null;
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } p && MouJiangWeiAnniversaryMovement(f, GetPausedPrivateOfferEffect(f), p))
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
        if (r.Stage == MouJiangWeiAnniversaryStage.DiscardChildren && r.Card is { From.Zone: CardZoneKind.Equipment } card)
        {
            var lion = _cardMovements.Any(m => m.CardId == card.Id && m.From == card.From && m.CardKind == CardKind.SilverLion && m.Sequence > r.Before && m.Sequence <= r.After && m.Reason.Value == MouJiangWeiAnniversaryRuntime.CostReason);
            if (lion && child is RecoveryReplacementFrame rr) return RecoveryReplacementFrameRidesOn(rr, f) && rr.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && rr.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && rr.Attempt.SourceSeat == r.DamageSource && rr.Attempt.TargetSeat == r.DamageSource;
            if (lion && child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.TargetSeat == r.DamageSource;
        }
        if (r.Stage == MouJiangWeiAnniversaryStage.Damage && f.AttackAttempt is { } a && f.AttackReturn is not null && a.SourceSeat == r.DamageSource && a.TargetSeat == r.Victim && !a.SourceLess &&
            ProgramEventHistory<MouJiangWeiAnniversaryDamageIssuedEvent>().Count(e => e.FrameId == f.Id && e.Source == a.SourceSeat && e.Victim == a.TargetSeat) == 1) return DyingSuitsStructuralEdge(f, child);
        if (r.Stage == MouJiangWeiAnniversaryStage.RecoveryChildren)
        {
            if (child is RecoveryReplacementFrame rr) return RecoveryReplacementFrameRidesOn(rr, f) && rr.Attempt.Completion.Producer == RecoveryAttemptProducer.Program && rr.Attempt.SourceSeat == f.OwnerSeat && rr.Attempt.TargetSeat == f.OwnerSeat;
            if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Change.TargetSeat == f.OwnerSeat;
        }
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
                ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouJiangWeiAnniversaryEdge(int i) => IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) ||
        TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) || _resolutionStack[i - 1] is ProgramSkillFrame f && MouJiangWeiAnniversaryFirstChild(f, _resolutionStack[i]) ||
        NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]);
    private bool MouJiangWeiAnniversarySuffix(int n) => _resolutionStack[n + 1] is DyingFrame d && n + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(n + 1, d) ||
        HasObserverSuffix(n, static (g, i) => g.MouJiangWeiAnniversaryEdge(i), static (g, i, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(i, dying));
    private ProgramSkillFrame? MouJiangWeiAnniversaryRoot() => FindProgramObserverRoot(static (g, f, c) => g.MouJiangWeiAnniversaryFirstChild(f, c), static (g, n) => g.MouJiangWeiAnniversarySuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 35200)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 35200)]
    private bool MouJiangWeiAnniversaryDamageWindow(long id) => MouJiangWeiAnniversaryRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 35200)]
    private bool MouJiangWeiAnniversaryBeforeDamage(long id) => MouJiangWeiAnniversaryRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 35200)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 35200)]
    private bool MouJiangWeiAnniversaryDying() => ActiveDying is { } d && MouJiangWeiAnniversaryRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 35200)] [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 35200)]
    private bool MouJiangWeiAnniversaryNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess) return false;
        if (MouJiangWeiAnniversary.Valid(f) && f.MouJiangWeiAnniversary is { Stage: MouJiangWeiAnniversaryStage.Damage } r) return target == r.Victim && amount == 1 && source is null && nature is null;
        if (MouJiangWeiAnniversaryRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature && target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 35200)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 35200)]
    private bool MouJiangWeiAnniversaryRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (MouJiangWeiAnniversaryFirstChild(f, recovery)) return true;
        var n = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return n >= 0 && c > n + 1 && MouJiangWeiAnniversaryFirstChild(f, _resolutionStack[n + 1]) && MouJiangWeiAnniversarySuffix(n) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[c - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 35200)]
    private bool MouJiangWeiAnniversaryAdvanceChildren() => _pendingDecision is null && MouJiangWeiAnniversaryRoot() is not null && TryAdvanceObserverSubtree(true);
}
