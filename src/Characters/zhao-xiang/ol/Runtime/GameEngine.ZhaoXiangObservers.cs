namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ZhaoXiangFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.ZhaoXiang is not{}r || !ValidZhaoXiang(f))return false;
        if(r.Stage=="movement" && child is CardsMovedTriggerWindowFrame moved)
            return f.PendingMovementContinuation is not null && moved.ResumeProgramFrameId is null && moved.Batch.ParentFrameId==f.Id && moved.Batch.AwaitingProgramFrameId==f.Id &&
                moved.Batch.OriginOwnerSeat==f.OwnerSeat && moved.Batch.OriginSkillId==f.SkillId && moved.Batch.OriginSkillInstanceId==f.SkillInstanceId &&
                moved.Batch.Movements.Count>0 && moved.Batch.Movements.All(m=>_cardMovements.Contains(m) && m.Sequence>r.Before && m.Sequence<=r.After);
        if(r.Stage is not("replacement-children" or "recovery-children"))return false;
        if(child is HpChangedTriggerWindowFrame hp)return hp.ResumeFrameId==f.Id && hp.Continuation==PostEventContinuation.Program && hp.Change.ParentFrameId==f.Id &&
            hp.Change.TargetSeat==f.OwnerSeat && (r.Stage=="replacement-children"?hp.Change.Kind==HpChangeKind.MaximumHp:hp.Change.Kind==HpChangeKind.Recovery && hp.Change.SourceSeat==f.OwnerSeat && hp.Change.Amount<=r.RecoveryRequested);
        if(child is ProgramLifecycleTriggerWindowFrame skills)return skills.Window==SkillProgramTriggerWindow.SkillsChanged && skills.ResumeProgramFrameId==f.Id &&
            skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram && skills.CandidateIndex>=0 && skills.CandidateIndex<=skills.Candidates.Count;
        if(child is RecoveryReplacementFrame recovery)return r.Stage=="recovery-children" && recovery.ParentFrameId==f.Id && recovery.Attempt.SourceSeat==f.OwnerSeat &&
            recovery.Attempt.TargetSeat==f.OwnerSeat && recovery.Attempt.Amount==r.RecoveryRequested && recovery.Attempt.ActualTurnNumber==r.Turn &&
            recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.Program && recovery.Attempt.Completion.InstructionIndex==r.InstructionIndex;
        return false;
    }
    private ProgramSkillFrame? ZhaoXiangObserverRoot()=>FindProgramObserverRoot(static(e,r,c)=>e.ZhaoXiangFirstChild(r,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 120)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 120)]
    private bool CanContinueZhaoXiang(ProgramSkillFrame f)=>f.ZhaoXiang is not null && ValidZhaoXiang(f);
    private bool AllowsZhaoXiangNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference?source,DamageNature?nature,bool sourceLess)
    {
        if(sourceLess || f.AttackAttempt is not null || f.InstructionIndex<1 || _resolutionStack.LastOrDefault()?.Id!=f.Id || ZhaoXiangObserverRoot() is null ||
            f.WindowContext?.Window is not(SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.AfterHpRecovered or
                SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterDamageApplied or SkillProgramTriggerWindow.SkillsChanged))return false;
        var e=ZhaoXiangPausedEffect(f)!;
        return e.Op==SkillProgramEffectOp.Damage && amount==e.Amount && source==e.ActorReference && nature==e.DamageNature &&
            target==(e.TargetReference is{}r?ResolveProgramParticipant(f,r):ResolveProgramEffectTarget(f,e.Target));
    }
    private bool IsZhaoXiangDying()=>ActiveDying is{}d && ZhaoXiangObserverRoot() is{}r && _resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
    private bool HasZhaoXiangDamageObserver(long id)=>_resolutionStack.Any(f=>f.Id==id && f is DamageTriggerWindowFrame) && ZhaoXiangObserverRoot() is not null;
    private bool HasZhaoXiangBeforeDamageObserver(long id)=>_resolutionStack.Any(f=>f.Id==id && f is BeforeDamageProgramWindowFrame) && ZhaoXiangObserverRoot() is not null;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 180)]
    private bool TryAdvanceZhaoXiangSubtree()=>_pendingDecision is null && ZhaoXiangObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
