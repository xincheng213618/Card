namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments,34700)]
    private bool MouDongZhuoOlMovement(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation p)=>
        f.MouDongZhuoOl is{Paid:true}r&&effect?.Op==r.Operation&&MouDongZhuoOl.Valid(f)&&p==f.PendingMovementContinuation&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null;
    private bool MouDongZhuoOlFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(!MouDongZhuoOl.Valid(f)||f.MouDongZhuoOl is not{}r)return false;
        if(child is CardUseFrame u)return r.Stage==MouDongZhuoOlStage.Wine&&r.WineUseId==u.Id&&IsMouDongZhuoOlWine(u.Id);
        if(child is CardsMovedTriggerWindowFrame moved&&f.PendingMovementContinuation is{}p&&MouDongZhuoOlMovement(f,GetPausedPrivateOfferEffect(f),p))
            return ProgramMovementReturnMatches(f,moved)&&moved.Batch.ParentFrameId==f.Id&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillId==f.SkillId&&
                moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After);
        if(r.Stage is MouDongZhuoOlStage.CostChildren or MouDongZhuoOlStage.PeerChildren)
        {
            var lion=_cardMovements.Where(m=>r.Materials.Any(c=>c.Id==m.CardId&&c.From==m.From)&&m.CardKind==CardKind.SilverLion&&m.From==CardLocation.Equipment(r.Payer)&&
                m.Sequence>r.Before&&m.Sequence<=r.After&&m.Reason.Value==MouDongZhuoOlRuntime.CostReason).ToArray();
            if(lion.Length>0&&child is RecoveryReplacementFrame recovery)return RecoveryReplacementFrameRidesOn(recovery,f)&&recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement&&
                recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&recovery.Attempt.Completion.MoveReason?.Value==MouDongZhuoOlRuntime.CostReason&&recovery.Attempt.SourceSeat==r.Payer&&recovery.Attempt.TargetSeat==r.Payer;
            if(lion.Length>0&&child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement&&
                hp.Change.Kind==HpChangeKind.Recovery&&hp.Change.SourceSeat==r.Payer&&hp.Change.TargetSeat==r.Payer&&ProgramEventHistory<SilverLionRemovedRecoveryEvent>().Any(e=>e.PlayerSeat==r.Payer&&e.Reason.Value==MouDongZhuoOlRuntime.CostReason&&e.RemainingHp==hp.Change.HpAfter);
        }
        if(r.Stage==MouDongZhuoOlStage.Damage&&f.AttackAttempt is{}a&&f.AttackReturn is not null&&a.SourceSeat==f.OwnerSeat&&a.TargetSeat==r.Payer&&!a.SourceLess&&
            ProgramEventHistory<MouDongZhuoOlDamageIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.Target==r.Payer&&e.Cursor==r.Cursor)==1)return DyingSuitsStructuralEdge(f,child);
        if(r.Stage is MouDongZhuoOlStage.ZhibingRecovery or MouDongZhuoOlStage.ZhibingGrants or MouDongZhuoOlStage.Finishing)
        {
            if(child is RecoveryReplacementFrame rr)return RecoveryReplacementFrameRidesOn(rr,f)&&rr.Attempt.Completion.Producer==RecoveryAttemptProducer.Program&&rr.Attempt.SourceSeat==f.OwnerSeat&&rr.Attempt.TargetSeat==f.OwnerSeat;
            if(child is HpChangedTriggerWindowFrame hp)return hp.ResumeFrameId==f.Id&&hp.Change.ParentFrameId==f.Id&&hp.Change.TargetSeat==f.OwnerSeat;
        }
        return child is ProgramLifecycleTriggerWindowFrame state&&state.ResumeProgramFrameId==f.Id&&
            (state.Window==SkillProgramTriggerWindow.SkillsChanged&&state.Continuation==ProgramLifecycleContinuation.ResumeParentProgram||state.Continuation==ProgramLifecycleContinuation.ResumeCharacterStateChange&&
                ProgramEventHistory<CharacterStateChangedEvent>().Any(e=>e.Change.Id==state.Id&&e.Change.ParentFrameId==f.Id));
    }
    private bool MouDongZhuoOlEdge(int i)=>IsMovementObserverEdge(i)||IsSameNameHandObserverEdge(i)||IsYanPuDyingExitEdge(i)||
        TypedDamagePostEventEdge(_resolutionStack[i-1],_resolutionStack[i])||_resolutionStack[i-1]is ProgramSkillFrame f&&MouDongZhuoOlFirstChild(f,_resolutionStack[i])||
        NativeDrawAttemptRidesOn(_resolutionStack[i],_resolutionStack[i-1])||RecoveryReplacementFrameRidesOn(_resolutionStack[i],_resolutionStack[i-1]);
    private bool MouDongZhuoOlSuffix(int n)=>_resolutionStack[n+1]is DyingFrame d&&n+2<_resolutionStack.Count&&IsSuspendedOrPaidRescueObserverSuffix(n+1,d)||
        HasObserverSuffix(n,static(g,i)=>g.MouDongZhuoOlEdge(i),static(g,i,d)=>g.IsSuspendedOrPaidRescueObserverSuffix(i,d));
    private ProgramSkillFrame? MouDongZhuoOlRoot()=>FindProgramObserverRoot(static(g,f,c)=>g.MouDongZhuoOlFirstChild(f,c),static(g,n)=>g.MouDongZhuoOlSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,34700)][ProgramObserverProof(ProgramObserverProofPhase.NestedDamage,34700)]
    private bool MouDongZhuoOlDamageWindow(long id)=>MouDongZhuoOlRoot()is{}f&&_resolutionStack.FindIndex(x=>x.Id==id&&x is DamageTriggerWindowFrame)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage,34700)]
    private bool MouDongZhuoOlBeforeDamage(long id)=>MouDongZhuoOlRoot()is{}f&&_resolutionStack.FindIndex(x=>x.Id==id&&x is BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant,34700)][ProgramDyingProof(ProgramDyingProofPhase.Runtime,34700)]
    private bool MouDongZhuoOlDying()=>ActiveDying is{}d&&MouDongZhuoOlRoot()is{}f&&_resolutionStack.FindIndex(x=>x.Id==d.Id)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts,34700)][ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts,34700)]
    private bool MouDongZhuoOlNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||sourceLess)return false;
        if(MouDongZhuoOl.Valid(f)&&f.MouDongZhuoOl is{Stage:MouDongZhuoOlStage.Damage}r)return target==r.Payer&&amount==1&&source is null&&nature is null;
        if(MouDongZhuoOlRoot()is not{}root||root.Id==f.Id)return false;var e=GetPausedPrivateOfferEffect(f);
        return e is{Op:SkillProgramEffectOp.Damage}&&amount==e.Amount&&source==e.ActorReference&&nature==e.DamageNature&&target==(e.TargetReference is{}p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor,34700)][ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor,34700)]
    private bool MouDongZhuoOlRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)
    {if(MouDongZhuoOlFirstChild(f,recovery))return true;var n=_resolutionStack.FindIndex(x=>x.Id==f.Id);var c=_resolutionStack.FindIndex(x=>x.Id==recovery.Id);
        return n>=0&&c>n+1&&MouDongZhuoOlFirstChild(f,_resolutionStack[n+1])&&MouDongZhuoOlSuffix(n)&&RecoveryReplacementFrameRidesOn(recovery,_resolutionStack[c-1]);}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,34700)]private bool MouDongZhuoOlAdvanceChildren()=>_pendingDecision is null&&MouDongZhuoOlRoot()is not null&&TryAdvanceObserverSubtree(true);
}
