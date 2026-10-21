namespace CardGame.Core;
// Native adjacency only: private commitments, payment cursors and ordered
// outcomes belong to the independent Runtime and its owning Program frame.
public sealed partial class GameEngine
{
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments,34400)]
    private bool MouZhuRanMovementReturn(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation p)=>
        effect?.Op is SkillProgramEffectOp.ResolveMouZhuRanZhenwei or SkillProgramEffectOp.ResolveMouZhuRanHeyuan&&MouZhuRan.Valid(f)&&
        p==f.PendingMovementContinuation&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null;
    private bool MouZhuRanFirstNativeChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(!MouZhuRan.Valid(f)||f.MouZhuRan is not{}r)return false;
        if(NativeDrawAttemptRidesOn(child,f))return r.Stage==MouZhuRanStage.Drawing&&f.NativeDraw is not null;
        if(child is CardsMovedTriggerWindowFrame moved&&f.PendingMovementContinuation is{}p&&MouZhuRanMovementReturn(f,GetPausedPrivateOfferEffect(f),p))
            return ProgramMovementReturnMatches(f,moved)&&moved.Batch.ParentFrameId==f.Id&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&
                moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&moved.Batch.Movements.Count>0&&
                moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After);
        if(r.Stage==MouZhuRanStage.CostChildren)
        {
            var reason=r.Operation==SkillProgramEffectOp.ResolveMouZhuRanZhenwei?MouZhuRanRuntime.CostReason:MouZhuRanRuntime.HeyuanCostReason;
            var lion=_cardMovements.Where(m=>r.Paying.Any(c=>c.CardId==m.CardId&&c.From==m.From)&&m.CardKind==CardKind.SilverLion&&
                m.From==CardLocation.Equipment(r.Payer)&&m.Sequence>r.Before&&m.Sequence<=r.After&&m.Reason.Value==reason).ToArray();
            if(lion.Length>0&&child is RecoveryReplacementFrame recovery)return RecoveryReplacementFrameRidesOn(recovery,f)&&
                recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement&&recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&
                recovery.Attempt.Completion.MoveReason?.Value==reason&&recovery.Attempt.SourceSeat==r.Payer&&recovery.Attempt.TargetSeat==r.Payer&&recovery.Attempt.Amount==1;
            if(lion.Length>0&&child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&
                hp.Continuation==PostEventContinuation.AwaitedProgramMovement&&hp.Change.Kind==HpChangeKind.Recovery&&hp.Change.SourceSeat==r.Payer&&hp.Change.TargetSeat==r.Payer&&
                ProgramEventHistory<SilverLionRemovedRecoveryEvent>().Any(e=>e.ResolutionId>f.Id&&e.ResolutionId<hp.Change.Id&&e.PlayerSeat==r.Payer&&e.Reason.Value==reason&&
                    e.RecoveredAmount==hp.Change.Amount&&e.RemainingHp==hp.Change.HpAfter);
        }
        if(r.Stage==MouZhuRanStage.Damage&&f.AttackAttempt is{}a&&f.AttackReturn is not null&&a.SourceSeat==f.OwnerSeat&&a.TargetSeat==r.Recipient&&!a.SourceLess&&
            ProgramEventHistory<MouZhuRanEffectIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.Index==r.EffectIndex&&e.Effect==MouZhuRanEffect.Damage&&e.SourceSeat==f.OwnerSeat&&e.RecipientSeat==r.Recipient)==1)
            return DyingSuitsStructuralEdge(f,child);
        return child is ProgramLifecycleTriggerWindowFrame state&&state.ResumeProgramFrameId==f.Id&&
            (state.Window==SkillProgramTriggerWindow.SkillsChanged&&state.Continuation==ProgramLifecycleContinuation.ResumeParentProgram||
             state.Continuation==ProgramLifecycleContinuation.ResumeCharacterStateChange&&ProgramEventHistory<CharacterStateChangedEvent>().Any(e=>e.Change.Id==state.Id&&e.Change.ParentFrameId==f.Id));
    }
    private bool MouZhuRanNativeEdge(int i)=>IsMovementObserverEdge(i)||IsSameNameHandObserverEdge(i)||IsYanPuDyingExitEdge(i)||
        TypedDamagePostEventEdge(_resolutionStack[i-1],_resolutionStack[i])||_resolutionStack[i-1]is ProgramSkillFrame f&&MouZhuRanFirstNativeChild(f,_resolutionStack[i])||
        NativeDrawAttemptRidesOn(_resolutionStack[i],_resolutionStack[i-1])||RecoveryReplacementFrameRidesOn(_resolutionStack[i],_resolutionStack[i-1]);
    private bool MouZhuRanNativeSuffix(int n)=>_resolutionStack[n+1]is DyingFrame d&&n+2<_resolutionStack.Count&&IsSuspendedOrPaidRescueObserverSuffix(n+1,d)||
        HasObserverSuffix(n,static(g,i)=>g.MouZhuRanNativeEdge(i),static(g,i,d)=>g.IsSuspendedOrPaidRescueObserverSuffix(i,d));
    private ProgramSkillFrame? MouZhuRanNativeRoot()=>FindProgramObserverRoot(static(g,f,c)=>g.MouZhuRanFirstNativeChild(f,c),static(g,n)=>g.MouZhuRanNativeSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,34400)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage,34400)]
    private bool MouZhuRanDamageWindow(long id)=>MouZhuRanNativeRoot()is{}f&&_resolutionStack.FindIndex(x=>x.Id==id&&x is DamageTriggerWindowFrame)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage,34400)]
    private bool MouZhuRanBeforeDamage(long id)=>MouZhuRanNativeRoot()is{}f&&_resolutionStack.FindIndex(x=>x.Id==id&&x is BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant,34400)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime,34400)]
    private bool MouZhuRanDying()=>ActiveDying is{}d&&MouZhuRanNativeRoot()is{}f&&_resolutionStack.FindIndex(x=>x.Id==d.Id)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts,34400)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts,34400)]
    private bool MouZhuRanNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||sourceLess)return false;
        if(MouZhuRan.Valid(f)&&f.MouZhuRan is{Stage:MouZhuRanStage.Damage}r)return target==r.Recipient&&amount==1&&source is null&&nature is null;
        if(MouZhuRanNativeRoot()is not{}root||root.Id==f.Id)return false;
        var e=GetPausedPrivateOfferEffect(f);
        return e is{Op:SkillProgramEffectOp.Damage}&&amount==e.Amount&&source==e.ActorReference&&nature==e.DamageNature&&
            target==(e.TargetReference is{}p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor,34400)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor,34400)]
    private bool MouZhuRanRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)
    {
        if(MouZhuRanFirstNativeChild(f,recovery))return true;
        var n=_resolutionStack.FindIndex(x=>x.Id==f.Id);var c=_resolutionStack.FindIndex(x=>x.Id==recovery.Id);
        return n>=0&&c>n+1&&MouZhuRanFirstNativeChild(f,_resolutionStack[n+1])&&MouZhuRanNativeSuffix(n)&&RecoveryReplacementFrameRidesOn(recovery,_resolutionStack[c-1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,34400)]
    private bool MouZhuRanAdvanceChildren()=>_pendingDecision is null&&MouZhuRanNativeRoot()is not null&&TryAdvanceObserverSubtree(true);
}
