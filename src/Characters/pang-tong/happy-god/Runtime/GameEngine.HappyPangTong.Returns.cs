namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool BeginHappyPtSettled(ProgramSkillFrame root,bool success)
    {
        if(!_players[root.OwnerSeat].IsAlive||_winner!=Winner.None)return false;
        var participant=_players[root.OwnerSeat];
        var candidates=CollectProgramTriggerCandidates(participant,SkillProgramTriggerWindow.HappyPlanSettled)
            .Where(c=>c.SkillId==HappyPangTongComposition.Lanhai&&c.BindingId=="settled-plan"||success&&c.SkillId==HappyPangTongComposition.Lunce&&c.BindingId=="renew-plan")
            .OrderByDescending(c=>c.Priority).ThenBy(c=>c.SkillId,StringComparer.Ordinal).ToArray();
        // Reissuing Lunce is the effect of an actual Lanhai invocation, not a
        // second free opportunity when the issuer has lost Lanhai.
        if(!candidates.Any(c=>c.SkillId==HappyPangTongComposition.Lanhai))return false;
        PushRuntimeFrame(new ProgramLifecycleTriggerWindowFrame(++_resolutionSequence,root.OwnerSeat,SkillProgramTriggerWindow.HappyPlanSettled,
            Array.AsReadOnly(candidates),ProgramLifecycleContinuation.HappyPlanSettled,CaptureProgramTriggerFacts(participant))
        {ResumeProgramFrameId=root.Id,HappyPlanReturn=new(root.Id,root.HappyPangTong!.CurrentPlanId,success,_turnNumber),
            ParticipantFacts=new Dictionary<int,SkillProgramTriggerFacts>{{root.OwnerSeat,CaptureProgramTriggerFacts(participant)}}});
        AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();return true;
    }
    private bool HappyPtSettlementPaid(ProgramLifecycleTriggerWindowFrame window)
    {
        if(window is not{Window:SkillProgramTriggerWindow.HappyPlanSettled,Continuation:ProgramLifecycleContinuation.HappyPlanSettled,HappyPlanReturn:{}ret}||
            window.ResumeProgramFrameId!=ret.RootId||ret.ActualTurn!=_turnNumber||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==ret.RootId) is not{HappyPangTong:{Stage:HappyPangTongStage.Settling}r}root||
            r.CurrentPlanId!=ret.PlanId||window.OwnerSeat!=root.OwnerSeat||!ValidHappyPt(root))return false;
        return ProgramEventHistory<HappyPangTongPlanSettledEvent>().Count(e=>e.RootFrameId==root.Id&&e.PlanId==ret.PlanId&&e.OwnerSeat==root.OwnerSeat&&e.Success==ret.Success&&e.ActualTurn==ret.ActualTurn)==1;
    }
    private bool CanRunHappyPangTongSettled(ProgramTriggerCandidate candidate,ProgramSkillWindowContext context)
    {
        if(context.Window!=SkillProgramTriggerWindow.HappyPlanSettled||context.OwnerSeat!=candidate.OwnerSeat||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId) is not{HappyPlanReturn:{}ret}window||
            window.OwnerSeat!=candidate.OwnerSeat||context.SourceSeat!=window.OwnerSeat||context.TargetSeat!=window.OwnerSeat||
            window.CandidateIndex<0||window.CandidateIndex>=window.Candidates.Count||window.Candidates[window.CandidateIndex]!=candidate||!HappyPtSettlementPaid(window))return false;
        if(candidate.SkillId==HappyPangTongComposition.Lanhai&&candidate.BindingId=="settled-plan")
            return ret.Success||ProgramEventHistory<HappyPangTongPlanSettledEvent>().Count(e=>e.OwnerSeat==candidate.OwnerSeat&&!e.Success)%2==0;
        return candidate.SkillId==HappyPangTongComposition.Lunce&&candidate.BindingId=="renew-plan"&&ret.Success&&
            ProgramEventHistory<ProgramBindingResolvedEvent>().Any(e=>e.FrameId==window.Id&&e.SkillId==HappyPangTongComposition.Lanhai&&e.BindingId=="settled-plan"&&e.OwnerSeat==candidate.OwnerSeat&&e.Completed);
    }
    [ProgramWindowReturn(SkillProgramTriggerWindow.HappyPlanSettled)]private void ReturnHappyPtWindow(ProgramSkillFrame child,bool completed)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame window||child.WindowContext?.ParentFrameId!=window.Id||
            window.CandidateIndex<0||window.CandidateIndex>=window.Candidates.Count||!MountObserverCandidateMatches(child,window.Candidates[window.CandidateIndex])||!HappyPtSettlementPaid(window))
            throw new InvalidOperationException("Lanhai/reissued Lunce lost its original settled-plan return.");
        AdvanceProgramLifecycleCandidate(window,true,completed);AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
    }
    [ProgramPipelineHook(ProgramPipelinePhase.LifecycleWindowCompleted,42180)]private bool CompleteHappyPtWindow(ProgramLifecycleTriggerWindowFrame window)
    {
        if(window.HappyPlanReturn is not{}ret)return false;
        if(window.CandidateIndex!=window.Candidates.Count||!HappyPtSettlementPaid(window)||_resolutionStack.LastOrDefault()?.Id!=ret.RootId)
            throw new InvalidOperationException("Settled-plan rewards cannot return to another plan or native root.");
        ReturnHappyPtSettled(ret.RootId,ret.PlanId);return true;
    }
    private void ReturnHappyPtSettled(long rootId,long planId)
    {
        var f=GetActiveProgramFrame(rootId);var r=f.HappyPangTong!;
        if(r.Stage!=HappyPangTongStage.Settling||r.CurrentPlanId!=planId||!ValidHappyPt(f))throw new InvalidOperationException("A plan result cannot advance another frozen due cursor.");
        ReplaceRuntimeTop(f with{HappyPangTong=r with{Stage=HappyPangTongStage.NextPlan,Cursor=r.Cursor+1},
            ChoiceBindings=Array.AsReadOnly(f.ChoiceBindings.Where(b=>b.Name!=HappyPtUpperBind(f)).ToArray())});ContinueHappyPt(rootId);
    }
    private string HappyPtUpperBind(ProgramSkillFrame f)=>"happy-upper-"+f.HappyPangTong!.CurrentPlanId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private bool IsHappyPangTongFactionOrigin(ProgramSkillFrame frame,int actor,int target,string? bind)=>
        frame.HappyPangTong is{Stage:HappyPangTongStage.UpperUse,FactionPending:true}r&&frame.OwnerSeat==actor&&r.SlashTarget==target&&
        bind==HappyPtUpperBind(frame)&&ValidHappyPt(frame)&&AssistedPhysicalSlashTargets(actor).Contains(target)&&
        CanRequestAssistedProgramFactionSlash(actor,target);
    private bool ValidateHappyPangTongFactionParent(FactionCardRequestHandle request,ProgramSkillFrame frame)
    {
        if(frame.HappyPangTong is not{FactionPending:true})return false;
        if(request.TargetSeat is not{}target||!IsHappyPangTongFactionOrigin(frame,request.OwnerSeat,target,request.AssistedResultBind)||request.PolicySource is null||
            (request.ActiveAttack is null?frame.ChoiceBindings.Any(b=>b.Name==request.AssistedResultBind):
                frame.ChoiceBindings.SingleOrDefault(b=>b.Name==request.AssistedResultBind) is not{OptionId:"used-slash"}))
            throw new InvalidOperationException("Upper-plan faction Slash lost its exact native request and once-only result.");
        return true;
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,42181)]private void BindHappyPtUpper(IGameEvent payload)
    {
        if(payload is not CardUseDeclaredEvent declared||LifecycleCardUse(declared.ResolutionId) is not{Action:{Type:CardActionType.Use}action}use)return;
        var index=_resolutionStack.FindIndex(p=>p.Id==use.Id);
        if(index<1||_resolutionStack[index-1] is not ProgramSkillFrame{HappyPangTong:{Stage:HappyPangTongStage.UpperUse,NativeUseId:0}r}root)return;
        if(root.OwnerSeat!=use.SourceSeat||action.ActorSeat!=root.OwnerSeat||!IsSlashCard(use.CardKind)||!use.TargetSeats.Contains(r.SlashTarget)||!ValidHappyPt(root))
            throw new InvalidOperationException("Upper plan must bind its actual owner's native Slash exactly once.");
        var ret=new HappyPangTongUpperReturn(root.Id,r.CurrentPlanId,use.Id,action.ActionId);
        ReplaceRuntimeFrame(root.Id,root with{HappyPangTong=r with{NativeUseId=use.Id,NativeActionId=action.ActionId}});
        UpdateLifecycleCardUse(use.Id,u=>u with{HappyPangTongUpper=ret});
        AdvanceEventRulesAndQueueFact(new HappyPangTongUpperBoundEvent(root.Id,r.CurrentPlanId,use.Id,action.ActionId));
    }
    [NativeUseValidation(42180)]private void AssertHappyPtUpperReturn(CardUseFrame use)
    {
        if(use.HappyPangTongUpper is not{}ret)return;
        if(_resolutionStack.SingleOrDefault(f=>f.Id==ret.RootId) is not ProgramSkillFrame root||!HappyPtUpperRidesOn(use,root)||use.Step!=ResolutionFrameStep.Completed)
            throw new InvalidOperationException("Upper-plan Slash must finish its own original native use before returning.");
    }
    [NativeUseReturn(42180)]private void ReturnHappyPtUpper(CardUseFrame completed)
    {
        if(completed.HappyPangTongUpper is not{}ret)return;
        if(_resolutionStack.Any(f=>f.Id==completed.Id)||completed.Step!=ResolutionFrameStep.Completed||
            _resolutionStack.LastOrDefault() is not ProgramSkillFrame{HappyPangTong:{Stage:HappyPangTongStage.UpperUse}r}root||
            !HappyPtUpperRidesOn(completed,root)||ProgramEventHistory<HappyPangTongUpperReturnedEvent>().Any(e=>e.UseId==completed.Id))
            throw new InvalidOperationException("Upper-plan Slash cannot return twice or to a different issued plan.");
        ReplaceRuntimeTop(root with{HappyPangTong=r with{Stage=HappyPangTongStage.UpperReturned}});
        AdvanceEventRulesAndQueueFact(new HappyPangTongUpperReturnedEvent(root.Id,r.CurrentPlanId,completed.Id,ret.ActionId));
    }
    private bool HappyPtUpperRidesOn(CardUseFrame use,ProgramSkillFrame root)=>use.HappyPangTongUpper is{}ret&&root.HappyPangTong is{}r&&
        ret.RootId==root.Id&&ret.PlanId==r.CurrentPlanId&&ret.UseId==use.Id&&ret.ActionId==use.Action?.ActionId&&r.NativeUseId==use.Id&&r.NativeActionId==ret.ActionId&&
        use.SourceSeat==root.OwnerSeat&&use.Action is{Type:CardActionType.Use}action&&action.ActorSeat==root.OwnerSeat&&IsSlashCard(use.CardKind)&&
        use.TargetSeats.Contains(r.SlashTarget)&&use.CardAttack?.ProgramSkillCardUseFrameId==root.Id&&ValidHappyPt(root)&&
        ProgramEventHistory<HappyPangTongUpperBoundEvent>().Count(e=>e==new HappyPangTongUpperBoundEvent(root.Id,r.CurrentPlanId,use.Id,ret.ActionId))==1;
}
