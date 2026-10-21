namespace CardGame.Core;

internal sealed partial class MouSunShangxiangRuntime
{
    [ProgramContinuation(ProgramContinuationKind.Movement,35100)]
    internal bool MovementReturned(ProgramSkillFrame f)=>f.MouSsx is not null&&f.PendingMovementContinuation is not null&&Resume(f.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments,35100)]
    internal bool MovementProof(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation pending)=>
        f.MouSsx is{Paid:true,Stage:MouSsxStage.GiftChildren or MouSsxStage.DepositChildren or MouSsxStage.DiscardChildren or MouSsxStage.FailureObtainChildren or MouSsxStage.DrawChildren}r&&Valid(f)&&
        effect?.Op==r.Operation&&f.PendingMovementContinuation==pending&&pending.BeforeCount==0&&pending.CoverageResultBind is null;
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor,35100)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor,35100)]
    internal bool RecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame child)=>f.MouSsx is not null&&Valid(f)&&(health.ProgramRecoveryChild(f,child)||facts.MovementRecoveryChild(f,child));
    private bool FirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.MouSsx is not{}r||!Valid(f))return false;
        if(child is NativeDrawAttemptFrame draw)return r.Stage==MouSsxStage.Drawing&&RuntimeSkills.RuntimeRules_Core_NativeDrawAttempts.NativeDrawAttemptRidesOn(draw,f);
        if(child is CardsMovedTriggerWindowFrame moved)return f.PendingMovementContinuation is not null&&
            RuntimeSkills.RuntimeRules_Core_CommonRules.ProgramMovementReturnMatches(f,moved)&&moved.Batch.Movements.All(m=>m.Sequence>r.Before&&m.Sequence<=r.After);
        if(health.ProgramRecoveryChild(f,child)||facts.MovementRecoveryChild(f,child))return true;
        return r.Stage==MouSsxStage.FailureMaximumHpChildren&&child is HpChangedTriggerWindowFrame hp&&hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&
            hp.Continuation==PostEventContinuation.Program&&hp.Change.Kind==HpChangeKind.MaximumHp&&hp.Change.SourceSeat is null&&hp.Change.TargetSeat==f.OwnerSeat&&hp.Change.Amount==1&&
            facts.History<MouSsxFailureStepEvent>().Any(e=>e.FrameId==f.Id&&e.Step==4)&&facts.History<MaximumHpChangedEvent>().Any(e=>e.PlayerSeat==f.OwnerSeat&&e.Delta==-1&&e.SkillId==Jieyin);
    }
    private ProgramSkillFrame? ObserverRoot()=>frames.OfType<ProgramSkillFrame>().LastOrDefault(f=>frames.ChildOf(f.Id)is{}child&&FirstChild(f,child)&&health.PaidObserverSuffix(f.Id));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,35100)]
    internal bool DamageObserver(long id)
    {
        if(ObserverRoot()is not{}root)return false;
        for(ResolutionFrame? p=root;p is not null;p=frames.ParentOf(p.Id))if(p.Id==id)return true;
        return false;
    }
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime,35100)]
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant,35100)]
    internal bool DyingObserver()
    {
        if(health.ActiveDying is not{}dying||ObserverRoot()is not{}root)return false;
        for(ResolutionFrame? p=dying;p is not null;p=frames.ParentOf(p.Id))if(p.Id==root.Id)return true;
        return false;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,35100)]
    internal bool AdvanceChildren()=>ObserverRoot()is not null&&healthOperations.AdvanceObserverSubtree();
}
