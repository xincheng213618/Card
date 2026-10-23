namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HappyZcOriginalFrame(ProgramSkillFrame f)
    {
        if(f.TriggerId is null)return f.SkillId is HappyZuoCiComposition.Huanhua or HappyZuoCiComposition.Huanjing&&
            ProgramEventHistory<ProgramSkillStartedEvent>().Count(e=>e.FrameId==f.Id&&e.OwnerSeat==f.OwnerSeat&&e.SkillId==f.SkillId&&e.ActivationId==f.ActivationId)==1;
        if(f.SkillId!=HappyZuoCiComposition.Huanshu||f.ActivationId!=f.TriggerId||f.WindowContext is not{}context||context.OwnerSeat!=f.OwnerSeat||
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(e=>e.FrameId==f.Id&&e.OwnerSeat==f.OwnerSeat&&e.SkillId==f.SkillId&&e.SkillInstanceId==f.SkillInstanceId&&e.BindingId==f.TriggerId&&e.Window==context.Window)!=1)return false;
        return _resolutionStack.SingleOrDefault(p=>p.Id==context.ParentFrameId) switch
        {
            ProgramLifecycleTriggerWindowFrame w=>w.Window==context.Window&&w.CandidateIndex>=0&&w.CandidateIndex<w.Candidates.Count&&MountObserverCandidateMatches(f,w.Candidates[w.CandidateIndex]),
            PlayPhaseStartingBoundaryFrame w=>context.Window==SkillProgramTriggerWindow.PlayPhaseStarting&&f.TriggerId=="refresh-phantoms"&&w.OwnerSeat==f.OwnerSeat&&
                w.ItemIndex>=0&&w.ItemIndex<w.Items.Count&&w.Items[w.ItemIndex].Candidate is{}candidate&&MountObserverCandidateMatches(f,candidate)&&
                context==CreatePlayPhaseStartingProgramContext(w,candidate,w.Items[w.ItemIndex].Facts),
            DamageTriggerWindowFrame w=>context.Window==SkillProgramTriggerWindow.AfterDamageApplied&&w.TriggerWindow==context.Window&&w.TargetSeat==f.OwnerSeat&&
                w.CandidateIndex>=0&&w.CandidateIndex<w.Candidates.Count&&MountObserverCandidateMatches(f,w.Candidates[w.CandidateIndex].ToProgramCandidate()),
            CardsMovedTriggerWindowFrame w=>context.Window==SkillProgramTriggerWindow.CardsMoved&&f.TriggerId=="foreign-phantom"&&context.MovementBatch==w.Batch&&
                w.CandidateIndex>=0&&w.CandidateIndex<w.Candidates.Count&&MountObserverCandidateMatches(f,w.Candidates[w.CandidateIndex])&&
                context.MovementIndex==w.Candidates[w.CandidateIndex].OccurrenceIndex&&context.MovementIndex is{}index&&index>=0&&index<w.Batch.Movements.Count&&HappyZcForeignGain(w.Batch.Movements[index])==f.OwnerSeat,
            _=>false
        };
    }
    private bool ValidHappyZc(ProgramSkillFrame f)
    {
        if(f.HappyZuoCi is not{}r||r.Source!=HappyZcSource(f)||r.GameplayHash!=f.GameplayHash||r.ParentId!=(f.WindowContext?.ParentFrameId??0)||
            r.ActualTurn!=_turnNumber||!Enum.IsDefined(r.Stage)||GetPausedPrivateOfferEffect(f)?.Op!=r.Operation||f.InstructionIndex!=1||
            f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||!HappyZcOriginalFrame(f)||r.Before<0||r.After<r.Before||r.After>_movementSequence||
            r.GainedIds is not System.Collections.IList{IsReadOnly:true}||r.GainedIds.Distinct().Count()!=r.GainedIds.Count||
            ProgramEventHistory<HappyZuoCiStartedEvent>().Count(e=>e==new HappyZuoCiStartedEvent(f.Id,r.Source,r.GameplayHash,r.Operation,r.ParentId,r.ActualTurn))!=1)return false;
        var state=_players[f.OwnerSeat].HappyZuoCi;
        if(state is null||!HappyZcSourceProof(f.OwnerSeat,state)||state.Entities is not System.Collections.IList{IsReadOnly:true}||state.UsedTemplates is not System.Collections.IList{IsReadOnly:true})return false;
        if(r.Stage==HappyZuoCiStage.InitialChildren)return r.Operation==SkillProgramEffectOp.InitializeHappyZuoCiPhantoms&&state.InitialFrameId==f.Id;
        if(r.Stage==HappyZuoCiStage.RefreshChildren)return r.Operation==SkillProgramEffectOp.RefreshHappyZuoCiPhantoms&&
            ProgramEventHistory<HappyZuoCiRefreshedEvent>().Count(e=>e.FrameId==f.Id&&e.OwnerSeat==f.OwnerSeat)==1;
        if(r.Stage is HappyZuoCiStage.PhantomChoice or HappyZuoCiStage.TemplateChoice)return r.Operation==SkillProgramEffectOp.TransformHappyZuoCiPhantom&&f.SkillId==HappyZuoCiComposition.Huanhua&&
            r.TemplateId==0&&(r.Stage==HappyZuoCiStage.PhantomChoice?r.PhantomId==0:r.PhantomId>0&&state.Entities.Any(e=>e.CardId==r.PhantomId&&!e.ManuallyTransformed));
        if(r.Operation==SkillProgramEffectOp.TransformHappyZuoCiPhantom&&(r.PhantomId<=0||r.TemplateId<=0||r.PhantomId==r.TemplateId||!state.UsedTemplates.Contains(r.TemplateId)||
            !state.Entities.Any(e=>e.CardId==r.PhantomId&&e.ManuallyTransformed)||ProgramEventHistory<HappyZuoCiTransformedEvent>().Count(e=>e.FrameId==f.Id&&e.OwnerSeat==f.OwnerSeat&&e.SameSuit)!=1))return false;
        if(r.Operation==SkillProgramEffectOp.ResolveHappyZuoCiHuanjing&&ProgramEventHistory<HappyZuoCiHuanjingIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.OwnerSeat==f.OwnerSeat&&e.ActualTurn==r.ActualTurn&&e.Amount==r.Requested)!=1)return false;
        if(r.Stage==HappyZuoCiStage.GainChildren)return r.Requested>0&&r.Overflow>=0&&r.Overflow<=r.Requested&&r.GainedIds.Count<=r.Requested-r.Overflow&&
            r.GainedIds.All(id=>state.Entities.Any(e=>e.CardId==id)&&_cardMovements.Count(m=>m.CardId==id&&m.From==state.Deck&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value=="skill-program.happy:huanshu.gain"&&m.Sequence>r.Before&&m.Sequence<=r.After)==1)&&
            ProgramEventHistory<HappyZuoCiGainIssuedEvent>().Count(e=>e==new HappyZuoCiGainIssuedEvent(f.Id,r.Requested,r.GainedIds.Count,r.Overflow,r.Before,r.After))==1;
        if(r.Stage==HappyZuoCiStage.Drawing)return r.Overflow>0;
        return r.Stage==HappyZuoCiStage.DrawChildren&&r.DrawOutcome is{}draw&&draw.RecipientSeat==f.OwnerSeat&&draw.RequestedCount==r.Overflow&&draw.SequenceBefore==r.Before&&draw.SequenceAfter==r.After&&
            ProgramEventHistory<HappyZuoCiDrawReturnedEvent>().Count(e=>e==new HappyZuoCiDrawReturnedEvent(f.Id,draw.RequestedCount,draw.ActualDrawIds.Count,r.Before,r.After))==1&&
            draw.ActualDrawIds.Distinct().Count()==draw.ActualDrawIds.Count&&draw.ActualDrawIds.All(id=>_cardMovements.Count(m=>m.CardId==id&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Sequence>r.Before&&m.Sequence<=r.After)==1);
    }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost,42600)]
    private bool HappyZcMovement(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation pending)=>f.HappyZuoCi is{}r&&effect?.Op==r.Operation&&
        pending==f.PendingMovementContinuation&&pending.SubjectSeat==f.OwnerSeat&&pending.BeforeCount==0&&pending.CoverageResultBind is null&&
        r.Stage is HappyZuoCiStage.InitialChildren or HappyZuoCiStage.GainChildren or HappyZuoCiStage.DrawChildren or HappyZuoCiStage.RefreshChildren&&ValidHappyZc(f);
    private bool HasHappyZuoCiPrompt()=>_resolutionStack.LastOrDefault() is ProgramSkillFrame{HappyZuoCi:{Stage:HappyZuoCiStage.PhantomChoice or HappyZuoCiStage.TemplateChoice}}f&&
        _pendingDecision is{Kind:DecisionKind.ProgramTrigger}p&&p.PlayerSeat==f.OwnerSeat&&ValidHappyZc(f)&&AssistedChoicesEqual(p.Choices,HappyZcChoices(f));
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,42600)]private void AssertHappyZc(ProgramSkillFrame f)
    {if(f.HappyZuoCi is not null&&!ValidHappyZc(f))throw new InvalidOperationException("Happy Zuo Ci lost its exact original deck/frame, persistent transform or paid native movement/draw.");}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,42600)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,42600)]private bool KeepPaidHappyZc(ProgramSkillFrame f)=>f.HappyZuoCi is{Stage:not(HappyZuoCiStage.PhantomChoice or HappyZuoCiStage.TemplateChoice)}&&ValidHappyZc(f);
}
