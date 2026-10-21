namespace CardGame.Core;

internal sealed partial class MouWangLingRuntime
{
    [ProgramContinuation(ProgramContinuationKind.Movement,34500)]
    internal bool MovementReturned(ProgramSkillFrame f)=>f.MouWangLing is not null&&f.PendingMovementContinuation is not null&&Resume(f.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments,34500)]
    internal bool MovementProof(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation pending)=>
        f.MouWangLing is{Paid:true,Stage:MouWangLingStage.GiftChildren or MouWangLingStage.DrawChildren or MouWangLingStage.DiscardChildren}r&&Valid(f)&&
        effect?.Op==r.Operation&&f.PendingMovementContinuation==pending&&pending.BeforeCount==0&&pending.CoverageResultBind is null;
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor,34500)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor,34500)]
    internal bool RecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)=>f.MouWangLing is not null&&Valid(f)&&health.ProgramRecoveryChild(f,recovery);
    private bool FirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.MouWangLing is not{}r||!Valid(f))return false;
        if(child is NativeDrawAttemptFrame draw)return r.Stage==MouWangLingStage.Drawing&&RuntimeSkills.RuntimeRules_Core_NativeDrawAttempts.NativeDrawAttemptRidesOn(draw,f);
        if(child is CardsMovedTriggerWindowFrame moved)return f.PendingMovementContinuation is not null&&
            RuntimeSkills.RuntimeRules_Core_CommonRules.ProgramMovementReturnMatches(f,moved)&&moved.Batch.Movements.All(m=>m.Sequence>r.Before&&m.Sequence<=r.After);
        return health.ProgramRecoveryChild(f,child);
    }
    private ProgramSkillFrame? ObserverRoot()=>frames.OfType<ProgramSkillFrame>().LastOrDefault(f=>frames.ChildOf(f.Id)is{}child&&FirstChild(f,child)&&health.PaidObserverSuffix(f.Id));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,34500)]
    internal bool DamageObserver(long windowId)
    {
        if(ObserverRoot()is not{}root)return false;
        for(ResolutionFrame? p=root;p is not null;p=frames.ParentOf(p.Id))if(p.Id==windowId)return true;
        return false;
    }
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime,34500)]
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant,34500)]
    internal bool DyingObserver()
    {
        if(health.ActiveDying is not{}dying||ObserverRoot()is not{}root)return false;
        for(ResolutionFrame? p=dying;p is not null;p=frames.ParentOf(p.Id))if(p.Id==root.Id)return true;
        return false;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,34500)]
    internal bool AdvanceChildren()=>ObserverRoot()is not null&&healthOperations.AdvanceObserverSubtree();
}
