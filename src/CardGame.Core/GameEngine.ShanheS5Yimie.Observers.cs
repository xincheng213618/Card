namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ShanheS5YimieCostFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.ShanheS5YimieCost is not { } r || !HasShanheS5YimieCostPaid(root)) return false;
        RequireShanheS5YimieOrigin(r);
        if (child is DyingFrame dying)
            return r.CostHpBefore == 1 && dying.ParentFrameId == root.Id && dying.VictimSeat == root.OwnerSeat &&
                dying.Continuation == DyingContinuationKind.ProgramSkill && dying.KillerSeat is null;
        return child is HpChangedTriggerWindowFrame hp && hp.ResumeFrameId == root.Id && hp.Change.ParentFrameId == root.Id &&
            hp.Continuation == PostEventContinuation.Program && hp.Change.Kind == HpChangeKind.Loss && hp.Change.SourceSeat is null &&
            hp.Change.TargetSeat == root.OwnerSeat && hp.Change.HpBefore == r.CostHpBefore && hp.Change.HpAfter == r.CostHpBefore - 1 && hp.Change.Amount == 1;
    }

    private ProgramSkillFrame? ShanheS5YimieCostObserverRoot() => FindProgramObserverRoot(
        static (e, root, child) => e.ShanheS5YimieCostFirstChild(root, child),
        static (e, i) => e.HasMovementObserverSuffix(i));

    private bool ShanheS5YimieRecoveryFirstChild(ShanheS5YimieRecoveryFrame root, ResolutionFrame child)
    {
        if (!root.Requested) return false;
        var o = root.Receipt.Origin;
        if (child is RecoveryReplacementFrame recovery) return recovery.ParentFrameId == root.Id && recovery.Return.ResumeFrameId == root.Id &&
            recovery.Return.Continuation == PostEventContinuation.ShanheS5YimieRecovery && recovery.Attempt.SourceSeat == o.Source.OwnerSeat &&
            recovery.Attempt.TargetSeat == o.TargetSeat && recovery.Attempt.Amount == o.X && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.Program;
        return child is HpChangedTriggerWindowFrame hp && hp.ResumeFrameId == root.Id && hp.Change.ParentFrameId == root.Id &&
            hp.Continuation == PostEventContinuation.ShanheS5YimieRecovery && hp.Change.Kind == HpChangeKind.Recovery &&
            hp.Change.SourceSeat == o.Source.OwnerSeat && hp.Change.TargetSeat == o.TargetSeat && hp.Change.Amount > 0;
    }

    private ShanheS5YimieRecoveryFrame? ShanheS5YimieRecoveryObserverRoot()
    {
        for (var i = 1; i + 1 < _resolutionStack.Count; i++)
            if (_resolutionStack[i] is ShanheS5YimieRecoveryFrame root && _resolutionStack[i - 1].Id == root.ParentFrameId &&
                _resolutionStack[i - 1].ShanheS5YimieDamage == root.Receipt && ShanheS5YimieRecoveryFirstChild(root, _resolutionStack[i + 1]) &&
                HasMovementObserverSuffix(i)) return root;
        return null;
    }

    private bool HasShanheS5YimieRecoveryContinuation(long attackId) =>
        ShanheS5YimieRecoveryObserverRoot() is { } root && root.ParentFrameId == attackId &&
        _resolutionStack.SingleOrDefault(f => f.Id == attackId)?.ShanheS5YimieDamage == root.Receipt;

    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 5795)]
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 5795)]
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamagePrelude, 5795)]
    private bool HasShanheS5YimieObserver(long id) =>
        ShanheS5YimieCostObserverRoot() is { } cost && (cost.ShanheS5YimieCost!.BeforeDamageWindowId == id ||
            _resolutionStack.SkipWhile(f => f.Id != cost.Id).Any(f => f.Id == id)) ||
        ShanheS5YimieRecoveryObserverRoot() is { } recovery && _resolutionStack.SkipWhile(f => f.Id != recovery.Id).Any(f => f.Id == id);

    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 5795, group: 10)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 5795)]
    private bool HasShanheS5YimieDyingObserver() => ActiveDying is { } dying &&
        (ShanheS5YimieCostObserverRoot() is { } cost && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == cost.Id) ||
         ShanheS5YimieRecoveryObserverRoot() is { } recovery && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == recovery.Id));

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 5795)]
    private bool AdvanceShanheS5YimieChildren()
    {
        if (_pendingDecision is not null || ShanheS5YimieCostObserverRoot() is null && ShanheS5YimieRecoveryObserverRoot() is null) return false;
        if (_resolutionStack.LastOrDefault() is NativeDrawAttemptFrame native)
        { AdvanceRuntimeFrame(native.Id); AdvanceRulesAndPublishState(); return true; }
        return TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
    }

    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 5795)]
    private bool AllowShanheS5YimieHealthObserverDamage(ProgramSkillFrame f, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || f.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != f.Id ||
            f.WindowContext?.Window is not (SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or
                SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained)) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.Damage || effect.Amount != amount || effect.ActorReference != source || effect.DamageNature != nature ||
            target != (effect.TargetReference is { } reference ? ResolveProgramParticipant(f, reference) : ResolveProgramEffectTarget(f, effect.Target))) return false;
        var original = ShanheS5YimieCostObserverRoot()?.ShanheS5YimieCost?.AttackId ?? ShanheS5YimieRecoveryObserverRoot()?.ParentFrameId;
        return original is not null && original == CurrentDamageAttempt?.ResolutionId;
    }
}
