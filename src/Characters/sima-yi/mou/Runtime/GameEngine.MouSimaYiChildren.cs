namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 36200)]
    private bool MouSmMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) => f.MouSimaYi is { } r && effect?.Op == r.Operation && MouSimaYi.Valid(f) &&
        p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool MouSmFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!MouSimaYi.Valid(f) || f.MouSimaYi is not { } r) return false;
        if(child is CardDeclarationFrame declaration)return MatchesMouSmPlayedDeclaration(f,declaration);
        if(NativeDrawAttemptRidesOn(child,f))return r.Stage==MouSmStage.Drawing&&f.NativeDraw is not null;
        if(child is ProgramCardTriggerWindowFrame card)return card.MouSimaYiPlayed is{}back&&back.FrameId==f.Id&&card.ParentFrameId==f.Id&&back.ActionId==r.ResponseActionId&&ValidMouSmPlayedWindow(card);
        if(r.Stage==MouSmStage.DamageChildren&&r.Cursor>0&&r.Cursor<=r.Targets.Count&&f.AttackAttempt is{ }attack&&attack.TargetSeat==r.Targets[r.Cursor-1]&&ProgramEventHistory<MouSmDamageIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.Cursor==r.Cursor-1&&e.Target==attack.TargetSeat)==1)
        {
            if(child is BeforeDamageProgramWindowFrame before)return before.ParentFrameId==f.Id&&before.SourceSeat==f.OwnerSeat&&before.TargetSeat==attack.TargetSeat&&before.Amount==1;
            if(TypedDamagePostEventEdge(f,child)||DyingSuitsStructuralEdge(f,child))return true;
        }
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } p && MouSmMovement(f, GetPausedPrivateOfferEffect(f), p))
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
        var lion = r.Materials.FirstOrDefault(c => c.From.Zone == CardZoneKind.Equipment && c.Kind == CardKind.SilverLion && _cardMovements.Any(m => m.CardId == c.Id && m.From == c.From && m.Sequence > r.Before && m.Sequence <= r.After));
        if (lion?.From.OwnerSeat is { } holder)
        {
            if (child is RecoveryReplacementFrame rr) return RecoveryReplacementFrameRidesOn(rr, f) && rr.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && rr.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && rr.Attempt.SourceSeat == holder && rr.Attempt.TargetSeat == holder;
            if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.TargetSeat == holder;
        }
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
                ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouSmEdge(int i) => MouSmDeclarationEdge(i) || IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) || TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) ||
        _resolutionStack[i - 1] is ProgramSkillFrame f && MouSmFirstChild(f, _resolutionStack[i]) || NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]);
    private bool MouSmSuffix(int n) => _resolutionStack[n + 1] is DyingFrame d && n + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(n + 1, d) ||
        HasObserverSuffix(n, static (g, i) => g.MouSmEdge(i), static (g, i, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(i, dying));
    private ProgramSkillFrame? MouSmRoot() => FindProgramObserverRoot(static (g, f, c) => g.MouSmFirstChild(f, c), static (g, n) => g.MouSmSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 36200)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 36200)]
    private bool MouSmDamageWindow(long id) => MouSmRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 36200)]
    private bool MouSmBeforeDamage(long id) => MouSmRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 36200)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 36200)]
    private bool MouSmDying() => ActiveDying is { } d && MouSmRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 36200)] [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 36200)]
    private bool MouSmNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess || MouSmRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature && target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 36200)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 36200)]
    private bool MouSmRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (MouSmFirstChild(f, recovery)) return true;
        var n = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return n >= 0 && c > n + 1 && MouSmFirstChild(f, _resolutionStack[n + 1]) && MouSmSuffix(n) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[c - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 36200)] private bool MouSmAdvanceChildren() => _pendingDecision is null && MouSmRoot() is not null && TryAdvanceObserverSubtree(true);
}
