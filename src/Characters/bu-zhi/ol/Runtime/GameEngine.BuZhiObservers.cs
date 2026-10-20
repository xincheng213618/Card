namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool BuZhiFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.BuZhi is not{}r || !ValidBuZhi(f))return false;
        if(r.Stage=="movement" && f.PendingMovementContinuation is not null && child is CardsMovedTriggerWindowFrame moved)
            return moved.ResumeProgramFrameId is null && moved.Batch.ParentFrameId==f.Id && moved.Batch.AwaitingProgramFrameId==f.Id &&
                moved.Batch.OriginOwnerSeat==f.OwnerSeat && moved.Batch.OriginSkillId==f.SkillId && moved.Batch.OriginSkillInstanceId==f.SkillInstanceId &&
                moved.Batch.Movements.Count>0 && moved.Batch.Movements.All(m=>_cardMovements.Contains(m) && m.Sequence>r.Before && m.Sequence<=r.After);
        return r.Stage=="damage-issued" && r.DamageIssued && f.AttackAttempt is{}attack && f.AttackReturn is not null &&
            attack.SourceSeat==f.OwnerSeat && attack.TargetSeat==r.Target && !attack.SourceLess && DyingSuitsStructuralEdge(f,child);
    }
    private ProgramSkillFrame? BuZhiObserverRoot()=>FindProgramObserverRoot(static(e,r,c)=>e.BuZhiFirstChild(r,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 400)]
    private bool AllowsBuZhiNestedDamage(ProgramSkillFrame observer,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex<1 || _resolutionStack.LastOrDefault()?.Id!=observer.Id)return false;
        if(observer.BuZhi is{Stage:"damage-issued",DamageIssued:true,Target:{}issued} && ValidBuZhi(observer))return target==issued && amount==1 && source is null && nature is null;
        if(observer.WindowContext?.Window is not(SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.AfterHpRecovered or
            SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterDamageApplied or SkillProgramTriggerWindow.SkillsChanged) || BuZhiObserverRoot() is null)return false;
        var e=BuZhiPausedEffect(observer)!;
        return e.Op==SkillProgramEffectOp.Damage && amount==e.Amount && source==e.ActorReference && nature==e.DamageNature && target==(e.TargetReference is{}r?ResolveProgramParticipant(observer,r):ResolveProgramEffectTarget(observer,e.Target));
    }
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 10)]
    private bool IsBuZhiDying()=>ActiveDying is{}d && BuZhiObserverRoot() is{}r && _resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 50)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 160)]
    private bool HasBuZhiDamageObserver(long id)=>_resolutionStack.Any(f=>f.Id==id && f is DamageTriggerWindowFrame) && BuZhiObserverRoot() is not null;
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 270)]
    private bool HasBuZhiBeforeDamageObserver(long id)=>_resolutionStack.Any(f=>f.Id==id && f is BeforeDamageProgramWindowFrame) && BuZhiObserverRoot() is not null;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 130)]
    private bool TryAdvanceBuZhiSubtree()=>_pendingDecision is null && BuZhiObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 70)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 70)]
    private bool CanContinueBuZhi(ProgramSkillFrame f)=>ValidBuZhi(f) &&
        (f.BuZhi is{Stage:"movement" or "damage-issued"} ||
         f.BuZhi is{Stage:"branch" or "discard" or "damage"} && _players[f.OwnerSeat].IsAlive && BuZhiPaidEquipmentEntitlement(f));
}
