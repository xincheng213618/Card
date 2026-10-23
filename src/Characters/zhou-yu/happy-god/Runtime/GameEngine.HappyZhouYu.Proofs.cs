namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState,41440)] private void RequireHappyZy(ProgramSkillFrame f)
    {
        if(f.HappyZhouYu is not{}r)return;
        var index=_resolutionStack.FindIndex(p=>p.Id==f.Id);
        if(index<1||f.InstructionIndex!=1||f.SkillId!=HappyQinyin||f.TriggerId!="whole-turn-discard"||f.WindowContext?.Window!=SkillProgramTriggerWindow.DiscardPhaseEnded||
            r.Source!=HappyZySource(f)||r.Hash!=f.GameplayHash||r.Parent!=f.WindowContext.ParentFrameId||_resolutionStack[index-1].Id!=r.Parent||r.Turn!=_turnNumber||f.OwnerSeat!=_currentSeat||_phase!=TurnPhase.Discard||
            !Enum.IsDefined(r.Stage)||r.Seats.Count==0||r.Seats[0]!=f.OwnerSeat||r.Seats.Distinct().Count()!=r.Seats.Count||r.Seats.Any(s=>!IsValidPlayerSeat(s))||r.Cursor<0||r.Cursor>r.Seats.Count||
            r.DiscardInvocation>=f.Id||ProgramEventHistory<NativeLogicalDiscardInvocationRecordedEvent>().SingleOrDefault(e=>e.InvocationId==r.DiscardInvocation) is not{}discard||
            discard.TurnNumber!=r.Turn||discard.ActualTurnOwnerSeat!=f.OwnerSeat||discard.ExecutorSeat!=f.OwnerSeat||discard.HandCount<1||
            !ProgramEventHistory<NativeLogicalDiscardMaterialCompletedEvent>().Any(e=>e.InvocationId==r.DiscardInvocation&&e.CardId==r.DiscardCard&&e.From==CardLocation.Hand(f.OwnerSeat)&&e.To==CardLocation.DiscardPile&&
                _cardMovements.Any(m=>m.Sequence==e.CompletionSequence&&m.CardId==e.CardId&&m.Sequence>discard.SequenceBefore&&m.Sequence<=discard.SequenceAfter&&m.To==e.To&&(m.From==e.From||GetProgramDiscardSource(m)==e.From)))||
            ProgramEventHistory<HappyZyStartedEvent>().Count(e=>e==new HappyZyStartedEvent(f.Id,r.Source,r.Hash,r.Parent,r.Turn,r.DiscardInvocation,r.Seats.Count))!=1||
            !ProgramEventHistory<HappyZyParticipantEvent>().Where(e=>e.FrameId==f.Id).SequenceEqual(r.Seats.Select((s,i)=>new HappyZyParticipantEvent(f.Id,i,s))))
            throw new InvalidOperationException("Happy Qinyin lost its accepted own-discard parent, real whole-turn hand discard, original skill instance or frozen participant order.");
        var modes=ProgramEventHistory<HappyZyModeEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        if(r.Stage==HappyZyStage.Choosing?r.Mode is not null||modes.Length!=0||r.Cursor!=0||r.Target!=-1:
            r.Mode is not("heal" or "lose" or "draw")||modes is not[var mode]||mode.Mode!=r.Mode)
            throw new InvalidOperationException("Happy Qinyin changed its already accepted global branch.");
        var issued=ProgramEventHistory<HappyZyIssuedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        if(issued.Length!=r.Cursor||issued.Where((e,i)=>e.Ordinal!=i+1||e.Target!=r.Seats[i]||e.Mode!=r.Mode).Any()||
            r.Stage is HappyZyStage.Drawing or HappyZyStage.Children&&(r.Cursor==0||r.Target!=r.Seats[r.Cursor-1])||
            r.Stage==HappyZyStage.HpLoss&&(r.Mode!="lose"||r.Cursor>=r.Seats.Count||r.Target!=r.Seats[r.Cursor])||
            r.Stage==HappyZyStage.Drawing&&(r.Mode!="draw"||r.Draw is not null)||
            ProgramEventHistory<HappyZyDrawReturnedEvent>().Where(e=>e.FrameId==f.Id).GroupBy(e=>e.Ordinal).Any(g=>g.Count()!=1)||
            r.Draw is{}draw&&(r.Mode!="draw"||draw.RecipientSeat!=r.Target||draw.RequestedCount!=1||draw.SequenceBefore<0||draw.SequenceAfter<draw.SequenceBefore||
                ProgramEventHistory<HappyZyDrawReturnedEvent>().Count(e=>e==new HappyZyDrawReturnedEvent(f.Id,r.Cursor,r.Target,draw.ActualDrawIds.Count,draw.SequenceBefore,draw.SequenceAfter,draw.AttemptFrameId))!=1))
            throw new InvalidOperationException("Happy Qinyin lost its once-issued participant cursor or original native draw return.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment,41440)] private bool HappyZyMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)
    {
        if(f.HappyZhouYu is not{Stage:HappyZyStage.Children,Mode:"draw",Draw:not null}||e?.Op!=SkillProgramEffectOp.ResolveHappyZhouYuQinyin||p!=f.PendingMovementContinuation||p.SubjectSeat!=f.OwnerSeat||p.BeforeCount!=0||p.CoverageResultBind is not null)return false;
        RequireHappyZy(f);return true;
    }
    private bool HappyZyFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.HappyZhouYu is not{}r)return false;RequireHappyZy(f);
        if(child is IdentityAppearanceFrame appearance)return r.Mode=="lose"&&r.Stage is HappyZyStage.HpLoss or HappyZyStage.Children&&appearance.Receipt.Entry==IdentityAppearanceEntry.ProgramHpLoss&&appearance.Receipt.ParentFrameId==f.Id&&appearance.Receipt.OwnerSeat==r.Target&&appearance.Receipt.RequestedDeduction==1&&appearance.Receipt.OriginalInstructionIndex==f.InstructionIndex;
        if(r.Stage!=HappyZyStage.Children)return false;
        if(child is CardsMovedTriggerWindowFrame moved)return r.Mode=="draw"&&r.Draw is{}draw&&f.PendingMovementContinuation is{}p&&HappyZyMovement(f,GetPausedPrivateOfferEffect(f),p)&&ProgramMovementReturnMatches(f,moved)&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>m.Sequence>draw.SequenceBefore&&m.Sequence<=draw.SequenceAfter&&_cardMovements.Contains(m));
        if(child is RecoveryReplacementFrame recovery)return r.Mode=="heal"&&recovery.Attempt.SourceSeat==f.OwnerSeat&&recovery.Attempt.TargetSeat==r.Target&&recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.Program&&RecoveryReplacementFrameRidesOn(recovery,f)&&recovery.Return.Continuation==PostEventContinuation.Program;
        if(child is HpChangedTriggerWindowFrame hp)return r.Mode is "heal" or "lose"&&hp.ResumeFrameId==f.Id&&hp.Change.ParentFrameId==f.Id&&hp.Change.TargetSeat==r.Target&&hp.Continuation is PostEventContinuation.Program or PostEventContinuation.AwaitedProgramMovement&&hp.Change.Kind==(r.Mode=="lose"?HpChangeKind.Loss:HpChangeKind.Recovery);
        if(child is DyingFrame dying)return r.Mode=="lose"&&dying.ParentFrameId==f.Id&&dying.VictimSeat==r.Target&&dying.Continuation==DyingContinuationKind.ProgramSkill&&dying.KillerSeat is null&&
            ProgramEventHistory<ProgramSkillHpLostEvent>().Any(e=>e.FrameId==f.Id&&e.SkillId==f.SkillId&&e.TargetSeat==r.Target&&e.Amount==1&&e.RemainingHp<=0);
        return child is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.SkillsChanged}skills&&skills.ResumeProgramFrameId==f.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? HappyZyObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.HappyZyFirstChild(f,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,41440)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage,41440)] private bool HappyZyDamageObserver(long id)=>HappyZyObserverRoot() is{}root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is DamageTriggerWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage,41440)] private bool HappyZyBeforeDamageObserver(long id)=>HappyZyObserverRoot() is{}root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant,41440)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime,41440)] private bool HappyZyDying()=>ActiveDying is{}dying&&HappyZyObserverRoot() is{}root&&_resolutionStack.FindIndex(f=>f.Id==dying.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor,41440)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor,41440)] private bool HappyZyRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)
    {
        if(f.HappyZhouYu is null)return false;var root=_resolutionStack.FindIndex(p=>p.Id==f.Id);var child=_resolutionStack.FindIndex(p=>p.Id==recovery.Id);
        return root>=0&&child>root&&HappyZyFirstChild(f,_resolutionStack[root+1])&&(child==root+1||Enumerable.Range(root+2,child-root-1).All(i=>SameNameHandObserverSuffix(i)))&&RecoveryReplacementFrameRidesOn(recovery,_resolutionStack[child-1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,41440)] private bool AdvanceHappyZyChildren()=>_pendingDecision is null&&HappyZyObserverRoot() is not null&&TryAdvanceObserverSubtree(true);
}
