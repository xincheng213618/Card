namespace CardGame.Core;
internal sealed partial class MouHuangQuanRuntime
{
    private bool FirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.MouHuangQuan is not{}r||!Valid(f))return false;
        if(child is NativeDrawAttemptFrame draw)return r.Stage==MouHqStage.Drawing&&RuntimeSkills.RuntimeRules_Core_NativeDrawAttempts.NativeDrawAttemptRidesOn(draw,f);
        if(child is CardsMovedTriggerWindowFrame moved)return f.PendingMovementContinuation is not null&&RuntimeSkills.RuntimeRules_Core_CommonRules.ProgramMovementReturnMatches(f,moved)&&moved.Batch.Movements.All(m=>m.Sequence>r.Before&&m.Sequence<=r.After);
        return facts.MovementRecoveryChild(f,child);
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor,35700)][ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor,35700)]
    internal bool RecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame child)=>f.MouHuangQuan is not null&&Valid(f)&&facts.MovementRecoveryChild(f,child);
    private ProgramSkillFrame? ObserverRoot()=>frames.OfType<ProgramSkillFrame>().LastOrDefault(f=>frames.ChildOf(f.Id)is{}child&&FirstChild(f,child)&&health.PaidObserverSuffix(f.Id));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,35700)]internal bool DamageObserver(long id)
    {
        if(ObserverRoot()is not{}root)return false;
        for(ResolutionFrame? p=root;p is not null;p=frames.ParentOf(p.Id))if(p.Id==id)return true;return false;
    }
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime,35700)][ProgramDyingProof(ProgramDyingProofPhase.Invariant,35700)]internal bool DyingObserver()
    {
        if(health.ActiveDying is not{}dying||ObserverRoot()is not{}root)return false;
        for(ResolutionFrame? p=dying;p is not null;p=frames.ParentOf(p.Id))if(p.Id==root.Id)return true;return false;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,35700)]internal bool AdvanceChildren()=>prompts.Current is null&&ObserverRoot()is not null&&healthOperations.AdvanceObserverSubtree();
}
