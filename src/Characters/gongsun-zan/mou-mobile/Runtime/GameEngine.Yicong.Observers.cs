namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidMouGszMobile(ProgramSkillFrame f)
    {
        if(f.MouGongsunZanMobile is not{}r||r.Source!=MouGszMobileSource(f)||r.GameplayHash!=f.GameplayHash||_contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash!=f.GameplayHash||f.InstructionIndex!=1||r.WindowId!=f.WindowContext?.ParentFrameId||r.Round<1||r.Before<0||r.After<r.Before||r.After>_movementSequence||!Enum.IsDefined(r.Stage)||
            ProgramEventHistory<MouGongsunZanMobileStartedEvent>().Count(e=>e==new MouGongsunZanMobileStartedEvent(f.Id,r.Source,r.GameplayHash,r.Op,r.WindowId,r.Round,r.DamageFrameId,r.TargetSeat))!=1)return false;
        if(r.Op==SkillProgramEffectOp.ResolveMouGongsunZanYicong)
        {
            if(f.SkillId!=MouGongsunZanMobileComposition.Yicong||f.TriggerId!="round-charge-and-hu"||f.WindowContext?.Window!=SkillProgramTriggerWindow.RoundStarting||r.TargetSeat!=-1||r.DamageFrameId!=0||r.Charge is <0 or >4||
                !_resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(w=>w.Id==r.WindowId&&w.CandidateIndex>=0&&w.CandidateIndex<w.Candidates.Count&&MountObserverCandidateMatches(f,w.Candidates[w.CandidateIndex])))return false;
            if(r.Accepted&&ProgramEventHistory<MouGongsunZanMobileDistanceEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.Round==r.Round&&e.Charge==r.Charge&&e.Offensive==r.Offensive)!=1)return false;
        }
        else if(r.Op!=SkillProgramEffectOp.ResolveMouGongsunZanQiaomeng||f.SkillId!=MouGongsunZanMobileComposition.Qiaomeng||f.TriggerId!="slash-damage-reward"||f.WindowContext is not{}c||MouGszMobileDamage(c,f.OwnerSeat) is not{}damage||damage.Id!=r.DamageFrameId||damage.TargetSeat!=r.TargetSeat||
            !_resolutionStack.OfType<DamageTriggerWindowFrame>().Any(w=>w.Id==r.WindowId&&w.CandidateIndex>=0&&w.CandidateIndex<w.Candidates.Count&&MountObserverCandidateMatches(f,w.Candidates[w.CandidateIndex].ToProgramCandidate())))return false;
        return ProgramEventHistory<MouGongsunZanMobileStoredEvent>().Where(e=>e.FrameId==f.Id).All(e=>_cardMovements.Any(m=>m.Sequence==e.Sequence&&m.CardId==e.CardId&&m.CardKind==e.Kind&&m.From==e.From&&m.To==e.To))&&
            ProgramEventHistory<MouGongsunZanMobileDiscardedEvent>().Where(e=>e.FrameId==f.Id).All(e=>_cardMovements.Any(m=>m.Sequence==e.Sequence&&m.CardId==e.CardId&&m.CardKind==e.Kind&&m.From==e.From&&m.To==e.To));
    }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments,33600)]
    private bool MouGszMobileMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>e is not null&&MouGongsunZanMobileComposition.Handles(e.Op)&&ValidMouGszMobile(f)&&f.MouGongsunZanMobile!.Stage is MouGongsunZanMobileStage.MovementChildren or MouGongsunZanMobileStage.DrawChildren&&p==f.PendingMovementContinuation&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null;
    private bool MouGszMobileLion(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(!ValidMouGszMobile(f)||f.MouGongsunZanMobile is not{Stage:MouGongsunZanMobileStage.MovementChildren})return false;
        var p=ProgramEventHistory<MouGongsunZanMobileDiscardedEvent>().SingleOrDefault(e=>e.FrameId==f.Id&&e.Kind==CardKind.SilverLion&&e.From.Zone==CardZoneKind.Equipment);if(p?.From.OwnerSeat is not{}seat)return false;
        if(child is RecoveryReplacementFrame r)return RecoveryReplacementFrameRidesOn(r,f)&&r.Return.Continuation==PostEventContinuation.AwaitedProgramMovement&&r.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&r.Attempt.Completion.MoveReason?.Value=="skill-program.mou-mobile:qiaomeng.discard"&&r.Attempt.SourceSeat==seat&&r.Attempt.TargetSeat==seat;
        return child is HpChangedTriggerWindowFrame h&&h.Change.ParentFrameId==f.Id&&h.ResumeFrameId==f.Id&&h.Continuation==PostEventContinuation.AwaitedProgramMovement&&h.Change.Kind==HpChangeKind.Recovery&&h.Change.SourceSeat==seat&&h.Change.TargetSeat==seat&&ProgramEventHistory<SilverLionRemovedRecoveryEvent>().Any(e=>e.PlayerSeat==seat&&e.Reason.Value=="skill-program.mou-mobile:qiaomeng.discard"&&e.ResolutionId>f.Id&&e.ResolutionId<h.Change.Id&&e.RecoveredAmount==h.Change.Amount);
    }
    private bool MouGszMobileChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(!ValidMouGszMobile(f))return false;var r=f.MouGongsunZanMobile!;
        if(NativeDrawAttemptRidesOn(child,f))return r.Stage==MouGongsunZanMobileStage.Drawing&&f.NativeDraw is not null;
        if(MouGszMobileLion(f,child))return true;
        if(child is RecoveryReplacementFrame recovery)return ProgramRecoveryReplacementReceiptMatches(f,recovery,CompleteProgramEventHistory().ToArray());
        if(child is HpChangedTriggerWindowFrame hp)return ProgramHpReturnMatches(f,hp);
        if(child is CardsMovedTriggerWindowFrame moved)return f.PendingMovementContinuation is not null&&ProgramMovementReturnMatches(f,moved)&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After);
        return child is ProgramLifecycleTriggerWindowFrame state&&state.ResumeProgramFrameId==f.Id&&(state.Window==SkillProgramTriggerWindow.SkillsChanged&&state.Continuation==ProgramLifecycleContinuation.ResumeParentProgram||state.Continuation==ProgramLifecycleContinuation.ResumeCharacterStateChange&&ProgramEventHistory<CharacterStateChangedEvent>().Any(e=>e.Change.Id==state.Id&&e.Change.ParentFrameId==f.Id));
    }
    private bool MouGszMobileSuffix(int n)=>HasObserverSuffix(n,static(g,i)=>g.IsMovementObserverEdge(i)||g.IsSameNameHandObserverEdge(i)||g.IsYanPuDyingExitEdge(i)||g.TypedDamagePostEventEdge(g._resolutionStack[i-1],g._resolutionStack[i])||g._resolutionStack[i-1] is ProgramSkillFrame f&&g.MouGszMobileChild(f,g._resolutionStack[i])||NativeDrawAttemptRidesOn(g._resolutionStack[i],g._resolutionStack[i-1])||g.RecoveryReplacementFrameRidesOn(g._resolutionStack[i],g._resolutionStack[i-1]),static(g,i,d)=>g.IsSuspendedOrPaidRescueObserverSuffix(i,d));
    private ProgramSkillFrame? MouGszMobileRoot()=>FindProgramObserverRoot(static(g,f,c)=>g.MouGszMobileChild(f,c),static(g,n)=>g.MouGszMobileSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,33600)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage,33600)]
    private bool MouGszMobileDamageProof(long id)=>MouGszMobileRoot() is{}f&&_resolutionStack.FindIndex(x=>x.Id==id&&x is DamageTriggerWindowFrame)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage,33600)]
    private bool MouGszMobileBeforeDamage(long id)=>MouGszMobileRoot() is{}f&&_resolutionStack.FindIndex(x=>x.Id==id&&x is BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime,33600)]
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant,33600)]
    private bool MouGszMobileDying()=>ActiveDying is{}d&&MouGszMobileRoot() is{}f&&_resolutionStack.FindIndex(x=>x.Id==d.Id)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor,33600)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor,33600)]
    private bool MouGszMobileRecovery(ProgramSkillFrame f,RecoveryReplacementFrame child)
    {if(!ValidMouGszMobile(f))return false;if(MouGszMobileLion(f,child)||ProgramRecoveryReplacementReceiptMatches(f,child,CompleteProgramEventHistory().ToArray()))return true;var a=_resolutionStack.FindIndex(x=>x.Id==f.Id);var b=_resolutionStack.FindIndex(x=>x.Id==child.Id);return a>=0&&b>a+1&&MouGszMobileChild(f,_resolutionStack[a+1])&&MouGszMobileSuffix(a)&&RecoveryReplacementFrameRidesOn(child,_resolutionStack[b-1]);}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,33600)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,33600)]
    private bool KeepMouGszMobile(ProgramSkillFrame f)=>f.MouGongsunZanMobile is{Accepted:true}&&ValidMouGszMobile(f);
    [ProgramContinuation(ProgramContinuationKind.Resume,33600)]
    private bool ResumeMouGszMobile(long id){if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{MouGongsunZanMobile:not null}f||f.Id!=id)return false;ContinueMouGszMobile(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,33600)]
    private bool ReturnMouGszMobileMovement(ProgramSkillFrame f)=>f.PendingMovementContinuation is not null&&ResumeMouGszMobile(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,33600)]
    private bool AdvanceMouGszMobileChild()=>_pendingDecision is null&&MouGszMobileRoot() is not null&&TryAdvanceObserverSubtree(true);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,33600)]
    private void AssertMouGszMobile(ProgramSkillFrame f){if(f.MouGongsunZanMobile is not null&&!ValidMouGszMobile(f))throw new InvalidOperationException("Mobile Gongsun Zan lost its own native trigger or paid child invoice.");}
}
