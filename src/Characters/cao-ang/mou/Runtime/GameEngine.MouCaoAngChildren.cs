namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments,36700)]private bool MouCaMovement(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation p)=>f.MouCaoAng is{}r&&effect?.Op==r.Operation&&MouCaoAng.Valid(f)&&p==f.PendingMovementContinuation&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null;
    private bool MouCaFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(!MouCaoAng.Valid(f)||f.MouCaoAng is not{}r)return false;
        if(child is CardUseFrame use)return MouCaUseRidesOn(use,f);
        if(child is CardsMovedTriggerWindowFrame moved&&f.PendingMovementContinuation is{}p&&MouCaMovement(f,GetPausedPrivateOfferEffect(f),p))return ProgramMovementReturnMatches(f,moved)&&moved.Batch.ParentFrameId==f.Id&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After);
        if(NativeDrawAttemptRidesOn(child,f))return r.Stage==MouCaoAngStage.Drawing;
        if(r.Stage==MouCaoAngStage.DiscardChildren&&r.Equipment.Any(c=>c.Kind==CardKind.SilverLion&&_cardMovements.Any(m=>m.CardId==c.Id&&m.From==c.From&&m.Sequence>r.Before&&m.Sequence<=r.After)))
        {
            if(child is RecoveryReplacementFrame rr)return RecoveryReplacementFrameRidesOn(rr,f)&&rr.Return.Continuation==PostEventContinuation.AwaitedProgramMovement&&rr.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&rr.Attempt.SourceSeat==f.OwnerSeat&&rr.Attempt.TargetSeat==f.OwnerSeat;
            if(child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Change.Kind==HpChangeKind.Recovery&&hp.Change.TargetSeat==f.OwnerSeat;
        }
        return child is ProgramLifecycleTriggerWindowFrame state&&state.ResumeProgramFrameId==f.Id&&(state.Window==SkillProgramTriggerWindow.SkillsChanged&&state.Continuation==ProgramLifecycleContinuation.ResumeParentProgram||state.Continuation==ProgramLifecycleContinuation.ResumeCharacterStateChange&&ProgramEventHistory<CharacterStateChangedEvent>().Any(e=>e.Change.Id==state.Id&&e.Change.ParentFrameId==f.Id));
    }
    private bool MouCaEdge(int i)=>IsMovementObserverEdge(i)||IsSameNameHandObserverEdge(i)||IsYanPuDyingExitEdge(i)||TypedDamagePostEventEdge(_resolutionStack[i-1],_resolutionStack[i])||_resolutionStack[i-1]is ProgramSkillFrame f&&MouCaFirstChild(f,_resolutionStack[i])||NativeDrawAttemptRidesOn(_resolutionStack[i],_resolutionStack[i-1])||RecoveryReplacementFrameRidesOn(_resolutionStack[i],_resolutionStack[i-1]);
    private bool MouCaSuffix(int n)=>_resolutionStack[n+1]is DyingFrame d&&n+2<_resolutionStack.Count&&IsSuspendedOrPaidRescueObserverSuffix(n+1,d)||HasObserverSuffix(n,static(g,i)=>g.MouCaEdge(i),static(g,i,dying)=>g.IsSuspendedOrPaidRescueObserverSuffix(i,dying));
    private ProgramSkillFrame? MouCaObserverRoot()=>FindProgramObserverRoot(static(g,f,c)=>g.MouCaFirstChild(f,c),static(g,n)=>g.MouCaSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,36700)][ProgramObserverProof(ProgramObserverProofPhase.NestedDamage,36700)]private bool MouCaDamageWindow(long id)=>MouCaObserverRoot()is{}f&&_resolutionStack.FindIndex(x=>x.Id==id&&x is DamageTriggerWindowFrame)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage,36700)]private bool MouCaBeforeDamage(long id)=>MouCaObserverRoot()is{}f&&_resolutionStack.FindIndex(x=>x.Id==id&&x is BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant,36700)][ProgramDyingProof(ProgramDyingProofPhase.Runtime,36700)]private bool MouCaDying()=>ActiveDying is{}d&&MouCaObserverRoot()is{}f&&_resolutionStack.FindIndex(x=>x.Id==d.Id)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts,36700)][ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts,36700)]private bool MouCaNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {if(f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||sourceLess||MouCaObserverRoot()is not{}root||root.Id==f.Id)return false;var effect=GetPausedPrivateOfferEffect(f);return effect is{Op:SkillProgramEffectOp.Damage}&&amount==effect.Amount&&source==effect.ActorReference&&nature==effect.DamageNature&&target==(effect.TargetReference is{}p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,effect.Target));}
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor,36700)][ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor,36700)]private bool MouCaRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)
    {if(MouCaFirstChild(f,recovery))return true;var n=_resolutionStack.FindIndex(x=>x.Id==f.Id);var c=_resolutionStack.FindIndex(x=>x.Id==recovery.Id);return n>=0&&c>n+1&&MouCaFirstChild(f,_resolutionStack[n+1])&&MouCaSuffix(n)&&RecoveryReplacementFrameRidesOn(recovery,_resolutionStack[c-1]);}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,36700)]private bool MouCaAdvanceChildren()=>_pendingDecision is null&&MouCaObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
}
