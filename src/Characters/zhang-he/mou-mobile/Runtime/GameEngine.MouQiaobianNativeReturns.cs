namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments,34200)]
    private bool MouQiaobianMovementReturn(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation p)=>
        effect?.Op is SkillProgramEffectOp.PrepareMouZhangHeQiaobian or SkillProgramEffectOp.ResolveMouZhangHeSkippedPhase&&
        MouQiaobian.Valid(f)&&p==f.PendingMovementContinuation&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null;
    private bool MouQiaobianFirstNativeChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(!MouQiaobian.Valid(f)||f.MouZhangHe is not{}r)return false;
        if(child is CardsMovedTriggerWindowFrame moved)
            return ProgramMovementReturnMatches(f,moved)&&moved.Batch.ParentFrameId==f.Id&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&
                moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&moved.Batch.Movements.Count>0&&
                moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After);
        var removals=_cardMovements.Where(m=>m.CardKind==CardKind.SilverLion&&m.From.Zone==CardZoneKind.Equipment&&m.Sequence>r.Before&&m.Sequence<=r.After&&
            (m.Reason.Value==MouQiaobianRuntime.CostReason||m.Reason.Value==MouQiaobianRuntime.FieldReason||m.Reason==CardMoveReasons.EquipmentReplace)).ToArray();
        if(child is RecoveryReplacementFrame recovery)
            return RecoveryReplacementFrameRidesOn(recovery,f)&&recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement&&
                recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&recovery.Attempt.SourceSeat==recovery.Attempt.TargetSeat&&recovery.Attempt.Amount==1&&
                removals.Any(m=>m.From.OwnerSeat==recovery.Attempt.TargetSeat&&recovery.Attempt.Completion.MoveReason==m.Reason);
        if(child is HpChangedTriggerWindowFrame hp)
            return hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement&&hp.Change.Kind==HpChangeKind.Recovery&&
                hp.Change.SourceSeat==hp.Change.TargetSeat&&removals.Any(m=>m.From.OwnerSeat==hp.Change.TargetSeat&&ProgramEventHistory<SilverLionRemovedRecoveryEvent>().Any(e=>
                    e.ResolutionId>f.Id&&e.ResolutionId<hp.Change.Id&&e.PlayerSeat==m.From.OwnerSeat&&e.Reason==m.Reason&&e.RecoveredAmount==hp.Change.Amount&&e.RemainingHp==hp.Change.HpAfter));
        return NativeDrawAttemptRidesOn(child,f)||child is ProgramLifecycleTriggerWindowFrame state&&state.ResumeProgramFrameId==f.Id&&
            (state.Window==SkillProgramTriggerWindow.SkillsChanged&&state.Continuation==ProgramLifecycleContinuation.ResumeParentProgram||
             state.Continuation==ProgramLifecycleContinuation.ResumeCharacterStateChange&&ProgramEventHistory<CharacterStateChangedEvent>().Any(e=>e.Change.Id==state.Id&&e.Change.ParentFrameId==f.Id));
    }
    private bool MouQiaobianNativeEdge(int i)=>IsMovementObserverEdge(i)||IsSameNameHandObserverEdge(i)||IsYanPuDyingExitEdge(i)||
        TypedDamagePostEventEdge(_resolutionStack[i-1],_resolutionStack[i])||_resolutionStack[i-1]is ProgramSkillFrame f&&MouQiaobianFirstNativeChild(f,_resolutionStack[i])||
        NativeDrawAttemptRidesOn(_resolutionStack[i],_resolutionStack[i-1])||RecoveryReplacementFrameRidesOn(_resolutionStack[i],_resolutionStack[i-1]);
    private bool MouQiaobianNativeSuffix(int n)=>_resolutionStack[n+1]is DyingFrame d&&n+2<_resolutionStack.Count&&IsSuspendedOrPaidRescueObserverSuffix(n+1,d)||
        HasObserverSuffix(n,static(g,i)=>g.MouQiaobianNativeEdge(i),static(g,i,d)=>g.IsSuspendedOrPaidRescueObserverSuffix(i,d));
    private ProgramSkillFrame? MouQiaobianNativeRoot()=>FindProgramObserverRoot(static(g,f,c)=>g.MouQiaobianFirstNativeChild(f,c),static(g,n)=>g.MouQiaobianNativeSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,34200)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage,34200)]
    private bool MouQiaobianDamage(long id)=>MouQiaobianNativeRoot()is{}f&&_resolutionStack.FindIndex(x=>x.Id==id&&x is DamageTriggerWindowFrame)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage,34200)]
    private bool MouQiaobianBeforeDamage(long id)=>MouQiaobianNativeRoot()is{}f&&_resolutionStack.FindIndex(x=>x.Id==id&&x is BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant,34200)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime,34200)]
    private bool MouQiaobianDying()=>ActiveDying is{}d&&MouQiaobianNativeRoot()is{}f&&_resolutionStack.FindIndex(x=>x.Id==d.Id)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts,34200)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts,34200)]
    private bool MouQiaobianNested(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||sourceLess||MouQiaobianNativeRoot()is not{}root||root.Id==f.Id)return false;
        var e=GetPausedPrivateOfferEffect(f);
        return e is{Op:SkillProgramEffectOp.Damage}&&amount==e.Amount&&source==e.ActorReference&&nature==e.DamageNature&&
            target==(e.TargetReference is{}p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor,34200)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor,34200)]
    private bool MouQiaobianRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)
    {
        if(MouQiaobianFirstNativeChild(f,recovery))return true;
        var n=_resolutionStack.FindIndex(x=>x.Id==f.Id);var c=_resolutionStack.FindIndex(x=>x.Id==recovery.Id);
        return n>=0&&c>n+1&&MouQiaobianFirstNativeChild(f,_resolutionStack[n+1])&&MouQiaobianNativeSuffix(n)&&RecoveryReplacementFrameRidesOn(recovery,_resolutionStack[c-1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,34200)]
    private bool MouQiaobianAdvanceChildren()=>_pendingDecision is null&&MouQiaobianNativeRoot()is not null&&TryAdvanceObserverSubtree(true);
}
