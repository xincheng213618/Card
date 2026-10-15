namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool FrozenHandRosterFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.FrozenHandRoster is not{}r || !ValidFrozenHandRoster(f))return false;
        if(r.Stage=="movement" && f.PendingMovementContinuation is not null && child is CardsMovedTriggerWindowFrame moved)
            return moved.ResumeProgramFrameId is null && moved.Batch.ParentFrameId==f.Id && moved.Batch.AwaitingProgramFrameId==f.Id &&
                moved.Batch.OriginOwnerSeat==f.OwnerSeat && moved.Batch.OriginSkillId==f.SkillId && moved.Batch.OriginSkillInstanceId==f.SkillInstanceId &&
                moved.Batch.Movements.Count>0 && moved.Batch.Movements.All(m=>_cardMovements.Contains(m) && m.Sequence>r.Before && m.Sequence<=r.After);
        return r.Stage=="damage-issued" && r.DamageIssued && f.AttackAttempt is{} attack && f.AttackReturn is not null &&
            attack.SourceSeat==f.OwnerSeat && attack.TargetSeat==r.DamageTarget && !attack.SourceLess && DyingSuitsStructuralEdge(f,child);
    }
    private ProgramSkillFrame? FrozenHandRosterObserverRoot()=>FindProgramObserverRoot(
        static(e,r,c)=>e.FrozenHandRosterFirstChild(r,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
    private bool AllowsFrozenHandRosterNestedDamage(ProgramSkillFrame observer,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex<1 || _resolutionStack.LastOrDefault()?.Id!=observer.Id)return false;
        if(observer.FrozenHandRoster is{Stage:"damage-issued",DamageIssued:true,DamageTarget:{}issued} && ValidFrozenHandRoster(observer))return target==issued && amount==1 && source is null && nature is null;
        if(observer.WindowContext?.Window is not(SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.AfterHpRecovered or
            SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterDamageApplied or SkillProgramTriggerWindow.SkillsChanged) || FrozenHandRosterObserverRoot() is null)return false;
        var e=ProgramInstructionResolver.Default.Resolve(observer,_contentRegistry.GetSkill(observer.SkillId).Program!).GetPausedInstruction(observer.InstructionIndex).Effect;
        return e.Op==SkillProgramEffectOp.Damage && amount==e.Amount && source==e.ActorReference && nature==e.DamageNature && target==(e.TargetReference is{}r?ResolveProgramParticipant(observer,r):ResolveProgramEffectTarget(observer,e.Target));
    }
    private bool IsFrozenHandRosterDying()=>ActiveDying is{}d && FrozenHandRosterObserverRoot() is{}r && _resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
    private bool HasFrozenHandRosterDamageObserver(long id)=>_resolutionStack.Any(f=>f.Id==id && f is DamageTriggerWindowFrame) && FrozenHandRosterObserverRoot() is not null;
    private bool HasFrozenHandRosterBeforeDamageObserver(long id)=>_resolutionStack.Any(f=>f.Id==id && f is BeforeDamageProgramWindowFrame) && FrozenHandRosterObserverRoot() is not null;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 520)]
    private bool TryAdvanceFrozenHandRosterSubtree()=>_pendingDecision is null && FrozenHandRosterObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 530)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 550)]
    private bool CanContinueFrozenHandRoster(ProgramSkillFrame f)=>f.FrozenHandRoster is{Stage:"movement" or "damage-issued"} && ValidFrozenHandRoster(f);
}
