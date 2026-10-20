namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool OldHandDispositionFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.OldHandDisposition is not { Stage: "movement" } r || root.PendingMovementContinuation is not { } pending ||
            !IsOldHandDispositionMovement(root, OldHandPausedEffect(root), pending) || child is not CardsMovedTriggerWindowFrame moved ||
            moved.Batch.ParentFrameId != root.Id || moved.ResumeProgramFrameId is not null || moved.Batch.AwaitingProgramFrameId != root.Id ||
            moved.Batch.OriginOwnerSeat != root.OwnerSeat || moved.Batch.OriginSkillId != root.SkillId || moved.Batch.OriginSkillInstanceId != root.SkillInstanceId ||
            moved.Batch.Movements.Count == 0 || moved.Batch.Movements.Any(m => !_cardMovements.Contains(m) || m.Sequence <= r.Before || m.Sequence > r.After)) return false;
        AssertOldHandDisposition(root); return true;
    }
    private ProgramSkillFrame? OldHandDispositionObserverRoot() => FindProgramObserverRoot(
        static (engine, root, child) => engine.OldHandDispositionFirstChild(root, child),
        static (engine,index) => engine.HasHandGainDebtObserverSuffix(index));
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 890)]
    private bool AllowsOldHandDispositionNestedDamage(ProgramSkillFrame observer,int target,int amount,
        ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex<1 || _resolutionStack.LastOrDefault()?.Id!=observer.Id ||
            ActiveDying is not null || observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or
            SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.SkillsChanged) ||
            OldHandDispositionObserverRoot() is not { } root || root.Id==observer.Id)return false;
        var e=ProgramInstructionResolver.Default.Resolve(observer,_contentRegistry.GetSkill(observer.SkillId).Program!).GetPausedInstruction(observer.InstructionIndex).Effect;
        return e.Op==SkillProgramEffectOp.Damage && amount==e.Amount && source==e.ActorReference && nature==e.DamageNature &&
            target==(e.TargetReference is { } reference ? ResolveProgramParticipant(observer,reference) : ResolveProgramEffectTarget(observer,e.Target));
    }
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 70, group: 10)]
    private bool IsOldHandDispositionProgramDying()=>ActiveDying is { } dying && OldHandDispositionObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f=>f.Id==dying.FrameId)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1730)]
    private bool HasOldHandDispositionDamageObserver(long id)=>_resolutionStack.Any(f=>f.Id==id && f is DamageTriggerWindowFrame) && OldHandDispositionObserverRoot() is not null;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 510)]
    private bool TryAdvanceOldHandDispositionSubtree()=>_pendingDecision is null && OldHandDispositionObserverRoot() is not null && TryAdvanceObserverSubtree(false);
}
