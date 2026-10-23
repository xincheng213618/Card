namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState,45500)]
    private void RequireAnniversaryXiahouJie(ProgramSkillFrame f)
    {
        if(f.AnniversaryXiahouJie is not {} r) return;
        var index=_resolutionStack.FindIndex(p=>p.Id==f.Id);
        if(f.WindowContext is not {} context || context.Window!=SkillProgramTriggerWindow.TurnStartBeforeNormalFlow ||
            r.Source!=AnniversaryXiahouJieSource(f) || r.Hash!=f.GameplayHash || r.ParentId!=context.ParentFrameId ||
            r.ActualTurn!=_turnNumber || r.TurnOwner!=_currentSeat || context.SourceSeat!=r.TurnOwner || index<1 ||
            _resolutionStack[index-1] is not ProgramLifecycleTriggerWindowFrame parent || parent.Id!=r.ParentId || parent.Window!=context.Window ||
            parent.OwnerSeat!=r.TurnOwner || parent.CandidateIndex<0 || parent.CandidateIndex>=parent.Candidates.Count ||
            !MountObserverCandidateMatches(f,parent.Candidates[parent.CandidateIndex]) || f.InstructionIndex!=1 ||
            GetPausedPrivateOfferEffect(f)?.Op!=SkillProgramEffectOp.ResolveAnniversaryXiahouJieLiedan || f.SelectedCardIds.Count!=0 || f.SelectedTargetSeats.Count!=0 ||
            !Enum.IsDefined(r.Stage) || r.Cursor is <0 or >3 || r.Comparisons.Count>3 ||
            r.Comparisons.Where((c,i)=>c.Index!=i).Any() || r.Draws.Select(d=>d.Index).Distinct().Count()!=r.Draws.Count ||
            ProgramEventHistory<AnniversaryXiahouJieStartedEvent>().Where(e=>e.FrameId==f.Id).ToArray() is not [var started] ||
            started!=new AnniversaryXiahouJieStartedEvent(f.Id,r.Source,r.Hash,r.ParentId,r.ActualTurn,r.TurnOwner,f.OwnerSeat==r.TurnOwner,r.DeathMarks))
            throw new InvalidOperationException("Liedan lost its exact owned preparation, original skill instance or sequential cursor.");
        if(r.Stage==AnniversaryXiahouJieStage.DirectDeath)
        {
            if(f.OwnerSeat!=r.TurnOwner || r.DeathMarks<5 || r.Cursor!=0 || r.Comparisons.Count!=0 || r.Draws.Count!=0)
                throw new InvalidOperationException("Liedan direct death lost its genuine own preparation and five previously paid marks.");
            return;
        }
        if(f.OwnerSeat==r.TurnOwner || r.DeathMarks!=0 || r.Comparisons.Count!=(r.Stage is AnniversaryXiahouJieStage.Drawing or AnniversaryXiahouJieStage.DrawChildren?r.Cursor+1:r.Cursor) ||
            r.Comparisons.Any(c=>ProgramEventHistory<AnniversaryXiahouJieComparedEvent>().Count(e=>e==new AnniversaryXiahouJieComparedEvent(f.Id,c.Index,c.OwnerValue,c.OtherValue))!=1) ||
            r.Draws.Any(d=>d.Index<0 || d.Index>=r.Comparisons.Count || r.Comparisons[d.Index].Greater!=true || d.Outcome.RecipientSeat!=f.OwnerSeat || d.Outcome.RequestedCount!=1 ||
                d.Outcome.ActualDrawIds.Count>1 || d.Outcome.SequenceBefore<0 || d.Outcome.SequenceAfter<d.Outcome.SequenceBefore || d.Outcome.SequenceAfter>_movementSequence ||
                ProgramEventHistory<AnniversaryXiahouJieDrawReturnedEvent>().Count(e=>e==new AnniversaryXiahouJieDrawReturnedEvent(f.Id,d.Index,d.Outcome.ActualDrawIds.Count,d.Outcome.AttemptFrameId,d.Outcome.SequenceBefore,d.Outcome.SequenceAfter))!=1 ||
                d.Outcome.ActualDrawIds.Any(id=>_cardMovements.Count(m=>m.CardId==id && m.Sequence>d.Outcome.SequenceBefore && m.Sequence<=d.Outcome.SequenceAfter && m.From==CardLocation.DrawPile && m.To==CardLocation.Hand(f.OwnerSeat) && m.Reason==AnniversaryXiahouJieDrawReason)!=1)) ||
            r.Comparisons.Any(c=>c.Greater==true && c.Index<r.Cursor && r.Draws.Count(d=>d.Index==c.Index)!=1) ||
            r.Stage==AnniversaryXiahouJieStage.Drawing && (r.Cursor>=3 || r.Comparisons[r.Cursor].Greater!=true || r.Draws.Any(d=>d.Index==r.Cursor)) ||
            r.Stage==AnniversaryXiahouJieStage.DrawChildren && r.Draws.Count(d=>d.Index==r.Cursor)!=1)
            throw new InvalidOperationException("Liedan lost a frozen comparison or its exact once-returned native draw materials.");
        if(r.Stage==AnniversaryXiahouJieStage.HpChildren && (r.Cursor!=3 || r.Comparisons.Any(c=>c.Greater!=false) || r.HpPaid &&
            (r.ActualLoss!=1 || (long)r.HpAfter!=(long)r.HpBefore-1 || ProgramEventHistory<AnniversaryXiahouJieHpPaidEvent>().Count(e=>e==new AnniversaryXiahouJieHpPaidEvent(f.Id,r.HpBefore,r.HpAfter,1))!=1 ||
             ProgramEventHistory<ProgramSkillHpLostEvent>().Count(e=>e.FrameId==f.Id && e.SkillId==f.SkillId && e.TargetSeat==f.OwnerSeat && e.Amount==1 && e.RemainingHp==r.HpAfter)!=1)) ||
           r.Stage==AnniversaryXiahouJieStage.MaximumChildren && (r.Cursor!=3 || r.Comparisons.Any(c=>c.Greater!=true) ||
            ProgramEventHistory<AnniversaryXiahouJieMaximumEvent>().Count(e=>e==new AnniversaryXiahouJieMaximumEvent(f.Id,r.MaximumBefore,r.MaximumAfter))!=1))
            throw new InvalidOperationException("Liedan lost its actual all-three comparison settlement.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,45500)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,45500)]
    private bool KeepPaidAnniversaryXiahouJie(ProgramSkillFrame f)
    { if(f.AnniversaryXiahouJie is null) return false; RequireAnniversaryXiahouJie(f); return true; }
    private bool AnniversaryXiahouJieFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.AnniversaryXiahouJie is not {} r) return false;
        RequireAnniversaryXiahouJie(f);
        if(r.Stage==AnniversaryXiahouJieStage.Drawing && NativeDrawAttemptRidesOn(child,f)) return true;
        if(child is DyingFrame dying) return r.Stage==AnniversaryXiahouJieStage.HpChildren && r.HpPaid && r.HpAfter<=0 &&
            dying.ParentFrameId==f.Id && dying.VictimSeat==f.OwnerSeat && dying.KillerSeat is null && dying.ResumesProgramSkill;
        if(child is DeathFrame death) return r.Stage==AnniversaryXiahouJieStage.DirectDeath && r.DeathMarks>=5 && death.ParentFrameId==f.Id && death.VictimSeat==f.OwnerSeat && death.ReturnKind==DeathReturnKind.ProgramSkill;
        if(child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId==f.Id && hp.Change.ParentFrameId==f.Id && hp.Continuation==PostEventContinuation.Program && hp.Change.TargetSeat==f.OwnerSeat && hp.Change.SourceSeat is null &&
            (r.Stage==AnniversaryXiahouJieStage.HpChildren && r.HpPaid && hp.Change.Kind==HpChangeKind.Loss && hp.Change.Amount==1 && hp.Change.HpAfter==r.HpAfter ||
             r.Stage==AnniversaryXiahouJieStage.MaximumChildren && hp.Change.Kind==HpChangeKind.MaximumHp && hp.Change.Amount==1);
        if(child is CardsMovedTriggerWindowFrame moved) return r.Stage==AnniversaryXiahouJieStage.DrawChildren && moved.Batch.ParentFrameId==f.Id &&
            (moved.ResumeProgramFrameId==f.Id || moved.Batch.AwaitingProgramFrameId==f.Id) && moved.Batch.OriginOwnerSeat==f.OwnerSeat && moved.Batch.OriginSkillId==f.SkillId && moved.Batch.OriginSkillInstanceId==f.SkillInstanceId &&
            moved.Batch.Movements.Count>0 && moved.Batch.Movements.All(m=>_cardMovements.Contains(m) && r.Draws.Any(d=>m.Sequence>d.Outcome.SequenceBefore && m.Sequence<=d.Outcome.SequenceAfter));
        return child is ProgramLifecycleTriggerWindowFrame skills && skills.Window==SkillProgramTriggerWindow.SkillsChanged && skills.ResumeProgramFrameId==f.Id && skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? AnniversaryXiahouJieObserverRoot()=>FindProgramObserverRoot(static(e,root,child)=>e.AnniversaryXiahouJieFirstChild(root,child),static(e,index)=>e.SameNameHandObserverSuffix(index));
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant,45500,group:10)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime,45500)]
    private bool HasAnniversaryXiahouJieDying()=>ActiveDying is {} dying && AnniversaryXiahouJieObserverRoot() is {} root && _resolutionStack.FindIndex(f=>f.Id==dying.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,45500)]
    private bool AdvanceAnniversaryXiahouJieObserver()=>_pendingDecision is null && AnniversaryXiahouJieObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramPipelineHook(ProgramPipelinePhase.LifecycleWindowCompleted,45500)]
    private bool CompleteAnniversaryXiahouJiePreparation(ProgramLifecycleTriggerWindowFrame parent)
    {
        if(parent.Window!=SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || parent.Continuation!=ProgramLifecycleContinuation.NormalTurnStart ||
            parent.OwnerSeat!=_currentSeat || parent.OwnerSeat!=_turnProgression.OwnerSeat || _players[parent.OwnerSeat].IsAlive && _winner==Winner.None ||
            parent.CandidateIndex!=parent.Candidates.Count || _resolutionStack.Count!=0 ||
            !ProgramEventHistory<AnniversaryXiahouJieStartedEvent>().Any(s=>s.ParentId==parent.Id && s.ActualTurn==_turnNumber && s.TurnOwner==parent.OwnerSeat &&
                parent.Candidates.Any(c=>c.OwnerSeat==s.Source.OwnerSeat && c.SkillId==s.Source.SkillId && c.BindingId==s.Source.BindingId && c.SkillInstanceId==s.Source.SkillInstanceId && c.GameplayHash==s.Hash) &&
                ProgramEventHistory<AnniversaryXiahouJieCompletedEvent>().Count(c=>c.FrameId==s.FrameId)==1)) return false;
        if(ProgramEventHistory<TurnEndedEvent>().Any(e=>e.TurnNumber==_turnNumber && e.ActorSeat==parent.OwnerSeat)) throw new InvalidOperationException("Liedan cannot end its actual preparation twice.");
        ClearActualDiscardRecoveryPhase(); if(_winner==Winner.None) FinalizeEndTurn(_players[parent.OwnerSeat]); return true;
    }
}
