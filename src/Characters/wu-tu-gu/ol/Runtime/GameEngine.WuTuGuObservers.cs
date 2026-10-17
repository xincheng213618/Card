namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidWuTuGuRanshang(ProgramSkillFrame f)
    {
        if(f.WuTuGuRanshang is not{}r||!HasWuTuGuCapability||f.SkillId!="ol:ranshang"||f.TriggerId!="ending-total-hp-loss"||f.ActivationId!=f.TriggerId||f.InstructionIndex!=1||GetPausedPrivateOfferEffect(f)?.Op!=SkillProgramEffectOp.ResolveRanshangEndingHpLoss||r.Source!=WuTuGuSource(f)||r.GameplayHash!=f.GameplayHash||r.ActualTurn!=_turnNumber||r.ParentId!=f.WindowContext?.ParentFrameId||r.Amount<1||!Enum.IsDefined(r.Stage)||f.PendingMovementContinuation is not null||!WuTuGuEndingParentMatches(f)||
            ProgramEventHistory<WuTuGuRanshangIssuedEvent>().Where(e=>e.ProgramFrameId==f.Id).ToArray()is not[var issued]||issued!=new WuTuGuRanshangIssuedEvent(f.Id,r.Source,r.GameplayHash,r.ParentId,r.ActualTurn,r.Amount))return false;
        var native=ProgramEventHistory<ProgramSkillHpLostEvent>().Where(e=>e.FrameId==f.Id).ToArray();var paid=ProgramEventHistory<WuTuGuRanshangPaidEvent>().Where(e=>e.ProgramFrameId==f.Id).ToArray();
        return r.Stage==WuTuGuRanshangStage.Issuing?native.Length==0&&paid.Length==0:native is[var hp]&&hp.SkillId==f.SkillId&&hp.TargetSeat==f.OwnerSeat&&hp.Amount==r.Amount&&paid is[var receipt]&&receipt.Amount==r.Amount&&receipt.RemainingHp==hp.RemainingHp;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27320)]private void AssertWuTuGuRanshang(ProgramSkillFrame f){if(f.WuTuGuRanshang is not null&&!ValidWuTuGuRanshang(f))throw new InvalidOperationException("Ranshang lost its once-issued actual-ending total, exact source or native equal-amount HP loss.");}
    private bool WuTuGuFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.WuTuGuRanshang is not{Stage:WuTuGuRanshangStage.HpChildren}r||!ValidWuTuGuRanshang(f))return false;
        if(child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Continuation==PostEventContinuation.Program&&hp.Change.SourceSeat is null&&hp.Change.TargetSeat==f.OwnerSeat&&hp.Change.Kind==HpChangeKind.Loss&&hp.Change.Amount==r.Amount;
        if(child is DyingFrame dying)return dying.ParentFrameId==f.Id&&dying.VictimSeat==f.OwnerSeat&&dying.Continuation==DyingContinuationKind.ProgramSkill&&dying.KillerSeat is null&&ProgramEventHistory<ProgramSkillHpLostEvent>().Count(e=>e.FrameId==f.Id&&e.Amount==r.Amount&&e.RemainingHp<=0)==1;
        return child is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.SkillsChanged}skills&&skills.ResumeProgramFrameId==f.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? WuTuGuObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.WuTuGuFirstChild(f,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
    private bool HasWuTuGuDamageObserver(long id)=>WuTuGuObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool HasWuTuGuBeforeDamageObserver(long id)=>HasWuTuGuDamageObserver(id);
    private bool IsWuTuGuDying()=>ActiveDying is{}dying&&WuTuGuObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==dying.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool AllowsWuTuGuNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(sourceLess||f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||WuTuGuObserverRoot()is not{}root||root.Id==f.Id)return false;var e=GetPausedPrivateOfferEffect(f);
        return e is{Op:SkillProgramEffectOp.Damage}&&e.Amount==amount&&e.ActorReference==source&&e.DamageNature==nature&&target==(e.TargetReference is{}p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,27320)]private bool AdvanceWuTuGuChildren()=>_pendingDecision is null&&WuTuGuObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
}
