namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HappyPtOriginalCandidate(ProgramSkillFrame f)
    {
        if(f.WindowContext is not{}context||context.OwnerSeat!=f.OwnerSeat||f.ActivationId!=f.TriggerId||
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(e=>e.FrameId==f.Id&&e.OwnerSeat==f.OwnerSeat&&e.SkillId==f.SkillId&&e.SkillInstanceId==f.SkillInstanceId&&e.BindingId==f.TriggerId&&e.Window==context.Window)!=1)return false;
        return _resolutionStack.SingleOrDefault(p=>p.Id==context.ParentFrameId) switch
        {
            ProgramLifecycleTriggerWindowFrame w=>w.Window==context.Window&&w.CandidateIndex>=0&&w.CandidateIndex<w.Candidates.Count&&MountObserverCandidateMatches(f,w.Candidates[w.CandidateIndex]),
            ProgramCardTriggerWindowFrame w=>context.Window==SkillProgramTriggerWindow.CardUseTargetsFinalized&&context.CardUse?.CardActionId==w.Action.ActionId&&
                w.CandidateIndex>=0&&w.CandidateIndex<w.Candidates.Count&&w.Candidates[w.CandidateIndex] is{}c&&c.OwnerSeat==f.OwnerSeat&&c.SkillId==f.SkillId&&c.TriggerId==f.TriggerId&&c.SkillInstanceId==f.SkillInstanceId&&c.GameplayHash==f.GameplayHash,
            DeferredTurnEndFrame p=>MatchesAfterTurnEndedChild(p,f,context),
            _=>false
        };
    }
    private bool HappyPtIssuedPlanProof(HappyPangTongPlanIssuedEvent plan)=>plan.PlanId>0&&IsValidPlayerSeat(plan.TargetSeat)&&Enum.IsDefined(plan.Kind)&&
        plan.Source.SkillId==HappyPangTongComposition.Lunce&&plan.Source.BindingId is("round-plan" or "turn-plan" or "renew-plan")&&
        ProgramEventHistory<HappyPangTongStartedEvent>().Count(e=>e.FrameId==plan.PlanId&&e.Source==plan.Source&&e.GameplayHash==plan.GameplayHash&&e.Operation==SkillProgramEffectOp.IssueHappyPangTongPlan)==1;
    private bool ValidHappyPt(ProgramSkillFrame f)
    {
        if(f.HappyPangTong is not{}r||r.Source!=HappyPtSource(f)||r.GameplayHash!=f.GameplayHash||r.ParentId!=f.WindowContext?.ParentFrameId||r.ActualTurn!=_turnNumber||
            !Enum.IsDefined(r.Stage)||!HappyPtOriginalCandidate(f)||GetPausedPrivateOfferEffect(f)?.Op!=r.Operation||
            ProgramEventHistory<HappyPangTongStartedEvent>().Count(e=>e==new HappyPangTongStartedEvent(f.Id,r.Source,r.GameplayHash,r.ParentId,r.Operation,r.ActualTurn))!=1||
            r.Cursor<0||r.Cursor>r.Plans.Count||r.Plans.Distinct().Count()!=r.Plans.Count)return false;
        if(r.Operation==SkillProgramEffectOp.IssueHappyPangTongPlan)return f.SkillId==HappyPangTongComposition.Lunce&&r.Stage==HappyPangTongStage.Grant&&
            r.Plans.Count==0&&r.CurrentPlanId==0&&f.SelectedTargetSeats.Count==0&&f.InstructionIndex==1;
        if(r.Operation==SkillProgramEffectOp.ResolveHappyPangTongLanhai)
        {
            if(f.SkillId!=HappyPangTongComposition.Lanhai||f.WindowContext?.Window!=SkillProgramTriggerWindow.HappyPlanSettled||
                _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w=>w.Id==r.ParentId) is not{HappyPlanReturn:{}ret}window||
                ret.PlanId!=r.CurrentPlanId||!HappyPtSettlementPaid(window))return false;
            if(ret.Success)return r.Stage is HappyPangTongStage.RewardDrawing or HappyPangTongStage.RewardChildren&&r.DrawCount>=1&&
                (r.Stage!=HappyPangTongStage.RewardChildren||HappyPtDrawOutcomeProof(f));
            return r.Stage is HappyPangTongStage.GrowthMaximumChildren or HappyPangTongStage.GrowthHpChildren&&
                r.MaximumAfter>=r.MaximumBefore&&ProgramEventHistory<HappyPangTongBenefitIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.PlanId==r.CurrentPlanId&&e.Branch=="grow"&&e.MaximumBefore==r.MaximumBefore&&e.MaximumAfter==r.MaximumAfter)==1&&
                (r.Stage!=HappyPangTongStage.GrowthHpChildren||ProgramEventHistory<HappyPangTongRecoveryIssuedEvent>().Count(e=>e==new HappyPangTongRecoveryIssuedEvent(f.Id,r.CurrentPlanId,f.OwnerSeat,1))==1);
        }
        if(f.SkillId!=HappyPangTongComposition.Lunce||r.Operation is not(SkillProgramEffectOp.ResolveHappyPangTongMiddle or SkillProgramEffectOp.ResolveHappyPangTongEnding)||
            r.Plans.Count==0||r.Plans.Any(id=>!HappyPtIssuedPlanProof(HappyPtPlan(id))||HappyPtPlan(id).Source.OwnerSeat!=f.OwnerSeat)||
            ProgramEventHistory<HappyPangTongDueIssuedEvent>().Where(e=>e.FrameId==f.Id).OrderBy(e=>e.Ordinal).ToArray() is not{}dues||dues.Length!=r.Plans.Count||
            dues.Where((e,i)=>e!=new HappyPangTongDueIssuedEvent(f.Id,i,r.Plans[i],r.ParentId)).Any()||
            r.Plans.Take(r.Cursor).Any(id=>ProgramEventHistory<HappyPangTongPlanSettledEvent>().Count(e=>e.PlanId==id&&e.RootFrameId==f.Id)!=1))return false;
        if(r.Stage==HappyPangTongStage.NextPlan)return true;
        if(r.Cursor>=r.Plans.Count||r.CurrentPlanId!=r.Plans[r.Cursor])return false;
        var plan=HappyPtPlan(r.CurrentPlanId);
        if(r.Stage==HappyPangTongStage.Settling)return ProgramEventHistory<HappyPangTongPlanSettledEvent>().Count(e=>e.PlanId==r.CurrentPlanId&&e.RootFrameId==f.Id&&e.OwnerSeat==f.OwnerSeat&&e.ActualTurn==r.ActualTurn)==1;
        if(ProgramEventHistory<HappyPangTongPlanSettledEvent>().Any(e=>e.PlanId==r.CurrentPlanId))return false;
        if(r.Stage==HappyPangTongStage.MiddleCard)return r.Operation==SkillProgramEffectOp.ResolveHappyPangTongMiddle&&plan.Kind==HappyPangTongPlanKind.Middle&&f.WindowContext?.CardUse is{}card&&plan.TargetSeat==card.ActorSeat&&plan.PlanId<card.ParentCardUseFrameId;
        if(r.Stage==HappyPangTongStage.GainChildren)return plan.Kind==HappyPangTongPlanKind.Middle&&r.GainCardId>0&&r.GainFrom is{}from&&r.After>r.Before&&
            _cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.CardId==r.GainCardId&&m.From==from&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value=="skill-program.happy:lunce.middle-gain")==1;
        if(r.Stage is HappyPangTongStage.UpperTarget or HappyPangTongStage.UpperCard or HappyPangTongStage.UpperUse or HappyPangTongStage.UpperReturned)
            return plan.Kind==HappyPangTongPlanKind.Upper&&r.Operation==SkillProgramEffectOp.ResolveHappyPangTongEnding&&
                (r.Stage==HappyPangTongStage.UpperTarget||IsValidPlayerSeat(r.SlashTarget)&&r.SlashTarget!=f.OwnerSeat)&&
                (r.Stage!=HappyPangTongStage.UpperReturned||r.NativeUseId>0&&ProgramEventHistory<HappyPangTongUpperReturnedEvent>().Count(e=>e==new HappyPangTongUpperReturnedEvent(f.Id,r.CurrentPlanId,r.NativeUseId,r.NativeActionId))==1);
        return plan.Kind==HappyPangTongPlanKind.Lower&&r.Operation==SkillProgramEffectOp.ResolveHappyPangTongEnding&&
            r.Stage is HappyPangTongStage.LowerChoice or HappyPangTongStage.LowerDrawing or HappyPangTongStage.LowerDrawChildren or HappyPangTongStage.LowerRecoveryChildren&&
            (r.Stage==HappyPangTongStage.LowerChoice||r.LowerAccepted&&r.DrawCount==2)&&
            (r.Stage is not(HappyPangTongStage.LowerDrawChildren or HappyPangTongStage.LowerRecoveryChildren)||HappyPtDrawOutcomeProof(f))&&
            (r.Stage!=HappyPangTongStage.LowerRecoveryChildren||ProgramEventHistory<HappyPangTongRecoveryIssuedEvent>().Count(e=>e==new HappyPangTongRecoveryIssuedEvent(f.Id,r.CurrentPlanId,plan.TargetSeat,1))==1);
    }
    private bool HappyPtDrawOutcomeProof(ProgramSkillFrame f)=>f.HappyPangTong is{DrawOutcome:{}outcome}r&&outcome.RecipientSeat==f.OwnerSeat&&outcome.RequestedCount==r.DrawCount&&
        outcome.SequenceBefore>=0&&outcome.SequenceAfter>=outcome.SequenceBefore&&outcome.SequenceAfter<=_movementSequence&&
        ProgramEventHistory<HappyPangTongDrawReturnedEvent>().Count(e=>e==new HappyPangTongDrawReturnedEvent(f.Id,r.CurrentPlanId,outcome.RequestedCount,outcome.ActualDrawIds.Count,outcome.SequenceBefore,outcome.SequenceAfter))==1&&
        outcome.ActualDrawIds.Distinct().Count()==outcome.ActualDrawIds.Count&&outcome.ActualDrawIds.All(id=>_cardMovements.Count(m=>m.CardId==id&&m.Sequence>outcome.SequenceBefore&&m.Sequence<=outcome.SequenceAfter&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat))==1);
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost,42180)]
    private bool HappyPtMovement(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation pending)=>
        f.HappyPangTong is{}r&&effect?.Op==r.Operation&&pending==f.PendingMovementContinuation&&pending.SubjectSeat==f.OwnerSeat&&pending.BeforeCount==0&&pending.CoverageResultBind is null&&
        r.Stage is HappyPangTongStage.GainChildren or HappyPangTongStage.LowerDrawChildren or HappyPangTongStage.LowerRecoveryChildren or HappyPangTongStage.RewardChildren or HappyPangTongStage.GrowthMaximumChildren or HappyPangTongStage.GrowthHpChildren&&ValidHappyPt(f);
    private bool HappyPangTongStructuralEdge(ResolutionFrame child,ResolutionFrame parent)=>
        child is ProgramLifecycleTriggerWindowFrame{HappyPlanReturn:{}ret}window&&ret.RootId==parent.Id&&parent is ProgramSkillFrame{HappyPangTong:{Stage:HappyPangTongStage.Settling}}&&HappyPtSettlementPaid(window)||
        child is ProgramSkillFrame{HappyPangTong:{}r}program&&r.ParentId==parent.Id&&parent is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.HappyPlanSettled}&&ValidHappyPt(program)||
        child is CardUseFrame use&&parent is ProgramSkillFrame root&&HappyPtUpperRidesOn(use,root);
    private bool HasHappyPangTongPrompt()=>_resolutionStack.LastOrDefault() is ProgramSkillFrame{HappyPangTong:{}r}f&&
        _pendingDecision is{Kind:DecisionKind.ProgramTrigger}p&&p.PlayerSeat==f.OwnerSeat&&ValidHappyPt(f)&&
        r.Stage is HappyPangTongStage.Grant or HappyPangTongStage.MiddleCard or HappyPangTongStage.UpperTarget or HappyPangTongStage.UpperCard or HappyPangTongStage.LowerChoice&&
        AssistedChoicesEqual(p.Choices,HappyPtChoices(f));
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,42180)]private void AssertHappyPt(ProgramSkillFrame f)
    {
        if(f.HappyPangTong is not null&&!ValidHappyPt(f))throw new InvalidOperationException("Happy Pang Tong lost its original independent plan, exact real boundary, paid movement or native return.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,42180)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,42180)]private bool KeepHappyPtPaid(ProgramSkillFrame f)=>f.HappyPangTong is{}r&&
        r.Stage is HappyPangTongStage.GainChildren or HappyPangTongStage.UpperUse or HappyPangTongStage.UpperReturned or HappyPangTongStage.LowerDrawing or HappyPangTongStage.LowerDrawChildren or HappyPangTongStage.LowerRecoveryChildren or HappyPangTongStage.RewardDrawing or HappyPangTongStage.RewardChildren or HappyPangTongStage.GrowthMaximumChildren or HappyPangTongStage.GrowthHpChildren or HappyPangTongStage.Settling&&ValidHappyPt(f);
}
