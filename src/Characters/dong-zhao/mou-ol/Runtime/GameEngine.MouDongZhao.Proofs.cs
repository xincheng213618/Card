namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 33500)]
    private bool IsMouDongZhaoMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect is not null && MouDongZhaoComposition.Handles(effect.Op) && ValidMouDongZhao(f) && pending == f.PendingMovementContinuation &&
        pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        f.MouDongZhao!.Stage is MouDongZhaoStage.DrawChildren or MouDongZhaoStage.GiftChildren or MouDongZhaoStage.DiscardChildren or MouDongZhaoStage.MoveChildren;
    private bool MouDongZhaoFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouDongZhao(f) || f.MouDongZhao is not { } r) return false;
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == MouDongZhaoStage.Drawing && f.NativeDraw is not null;
        if (RecoveryReplacementFrameRidesOn(child, f)) return r.Stage is MouDongZhaoStage.GiftChildren or MouDongZhaoStage.MoveChildren;
        if (child is HpChangedTriggerWindowFrame hp) return ProgramHpReturnMatches(f, hp);
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } pending &&
            IsMouDongZhaoMovement(f, GetPausedPrivateOfferEffect(f), pending))
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.Movements.All(m =>
                m.Sequence > r.Before && m.Sequence <= r.After && ProgramEventHistory<MouDongZhaoMovementSignedEvent>().Count(e =>
                    e.FrameId == f.Id && e.CardId == m.CardId && e.Kind == m.CardKind && e.From == m.From && e.To == m.To && e.Reason == m.Reason && e.Sequence == m.Sequence) == 1);
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouDongZhaoObserverEdge(int index) => IsMovementObserverEdge(index) || IsSameNameHandObserverEdge(index) || IsYanPuDyingExitEdge(index) ||
        TypedDamagePostEventEdge(_resolutionStack[index - 1], _resolutionStack[index]) ||
        NativeDrawAttemptRidesOn(_resolutionStack[index], _resolutionStack[index - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[index], _resolutionStack[index - 1]);
    private bool MouDongZhaoObserverSuffix(int index) => HasObserverSuffix(index, static (g, n) => g.MouDongZhaoObserverEdge(n),
        static (g, n, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(n, dying));
    private ProgramSkillFrame? MouDongZhaoObserverRoot() => FindProgramObserverRoot(static (g, f, c) => g.MouDongZhaoFirstChild(f, c), static (g, n) => g.MouDongZhaoObserverSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 33500)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 33500)]
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 33500)]
    private bool HasMouDongZhaoDamageObserver(long id) => MouDongZhaoObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 33500)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 33500)]
    private bool IsMouDongZhaoDying() => ActiveDying is { } dying && MouDongZhaoObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 33500)]
    private bool AllowsMouDongZhaoNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess || MouDongZhaoObserverRoot() is not { } root || root.Id == f.Id) return false;
        var effect = GetPausedPrivateOfferEffect(f);
        return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(f, reference) : ResolveProgramEffectTarget(f, effect.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 33500)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 33500)]
    private bool IsMouDongZhaoRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (ValidMouDongZhao(f) && RecoveryReplacementFrameRidesOn(recovery, f)) return true;
        var root = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return root >= 0 && child > root + 1 && MouDongZhaoFirstChild(f, _resolutionStack[root + 1]) && MouDongZhaoObserverSuffix(root) &&
            RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 33500)]
    private bool AdvanceMouDongZhaoChildren() => _pendingDecision is null && MouDongZhaoObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
