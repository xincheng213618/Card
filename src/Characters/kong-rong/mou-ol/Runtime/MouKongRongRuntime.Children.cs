namespace CardGame.Core;

internal sealed partial class MouKongRongRuntime
{
    [ProgramContinuation(ProgramContinuationKind.Movement,34600)]
    internal bool Movement(ProgramSkillFrame f)=>f.MouKrLiwen is not null&&f.PendingMovementContinuation is not null&&Resume(f.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments,34600)]
    internal bool MovementProof(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation pending)=>f.MouKrLiwen is{Stage:MouKrLiwenStage.Children or MouKrLiwenStage.DrawChildren}&&
        Valid(f)&&effect?.Op==SkillProgramEffectOp.ResolveMouKongRongLiwen&&f.PendingMovementContinuation==pending&&pending.BeforeCount==0&&pending.CoverageResultBind is null;
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor,34600)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor,34600)]
    internal bool RecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame child)=>(f.MouKrLiwen is not null||f.MouKrZhengyi is not null)&&Valid(f)&&health.ProgramRecoveryChild(f,child);
    private bool FirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(!Valid(f))return false;
        if(child is NativeDrawAttemptFrame draw)return f.MouKrLiwen is{Stage:MouKrLiwenStage.DrawChildren,DrawReturned:false}&&RuntimeSkills.RuntimeRules_Core_NativeDrawAttempts.NativeDrawAttemptRidesOn(draw,f);
        if(child is CardsMovedTriggerWindowFrame moved)return f.PendingMovementContinuation is not null&&RuntimeSkills.RuntimeRules_Core_CommonRules.ProgramMovementReturnMatches(f,moved)&&
            f.MouKrLiwen is{}r&&moved.Batch.Movements.All(m=>m.Sequence>r.Before&&m.Sequence<=r.After);
        if(health.ProgramRecoveryChild(f,child))return true;
        if(f.MouKrZhengyi is not{Stage:MouKrZhengyiStage.HealthChildren,Cursor:>0}z)return false;
        var target=z.Payers[z.Cursor-1].Seat;
        if(child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Continuation==PostEventContinuation.Program&&hp.Change.Kind==HpChangeKind.Loss&&hp.Change.SourceSeat is null&&hp.Change.TargetSeat==target&&hp.Change.Amount==z.Amount&&
            facts.History<ProgramSkillHpLostEvent>().Any(e=>e.FrameId==f.Id&&e.TargetSeat==target&&e.Amount==z.Amount);
        return child is DyingFrame dying&&dying.ParentFrameId==f.Id&&dying.VictimSeat==target&&dying.Continuation==DyingContinuationKind.ProgramSkill&&dying.KillerSeat is null&&
            health.LastPendingLoss(f.Id,target)is{Kind:HpChangeKind.Loss}loss&&loss.Amount==z.Amount;
    }
    private ProgramSkillFrame? ObserverRoot()=>frames.OfType<ProgramSkillFrame>().LastOrDefault(f=>frames.ChildOf(f.Id)is{}child&&FirstChild(f,child)&&(child is DyingFrame||health.PaidObserverSuffix(f.Id)));
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime,34600)]
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant,34600)]
    internal bool DyingProof()
    {
        if(health.ActiveDying is not{}dying||ObserverRoot()is not{}root)return false;
        for(ResolutionFrame? p=dying;p is not null;p=frames.ParentOf(p.Id))if(p.Id==root.Id)return true;return false;
    }
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,34600)]
    internal bool ObserverDamage(long id)
    {
        if(ObserverRoot()is not{}root)return false;
        for(ResolutionFrame? p=root;p is not null;p=frames.ParentOf(p.Id))if(p.Id==id)return true;return false;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,34600)]
    internal bool AdvanceChildren()=>ObserverRoot()is not null&&healthOperations.AdvanceObserverSubtree();
}
