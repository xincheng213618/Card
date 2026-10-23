namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string HappyQinyin="happy:qinyin";
    private static CardConversionSource HappyZySource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private (NativeLogicalDiscardInvocationRecordedEvent Invocation,NativeLogicalDiscardMaterialCompletedEvent Material)? HappyZyDiscard(int owner)
    {
        foreach(var invocation in ProgramEventHistory<NativeLogicalDiscardInvocationRecordedEvent>().Where(e=>e.TurnNumber==_turnNumber&&e.ActualTurnOwnerSeat==owner&&e.ExecutorSeat==owner&&e.HandCount>0))
        foreach(var material in ProgramEventHistory<NativeLogicalDiscardMaterialCompletedEvent>().Where(e=>e.InvocationId==invocation.InvocationId&&e.From==CardLocation.Hand(owner)&&e.To==CardLocation.DiscardPile))
            if(_cardMovements.Any(m=>m.Sequence==material.CompletionSequence&&m.CardId==material.CardId&&m.To==material.To&&(m.From==material.From||GetProgramDiscardSource(m)==material.From)))return(invocation,material);
        return null;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,41440,SkillProgramEffectOp.ResolveHappyZhouYuQinyin)]
    private bool CanOfferHappyZy(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext w)=>c.SkillId==HappyQinyin&&w.Window==SkillProgramTriggerWindow.DiscardPhaseEnded&&c.OwnerSeat==_currentSeat&&_phase==TurnPhase.Discard&&HappyZyDiscard(c.OwnerSeat) is not null;
    [ProgramCapability] private sealed class HappyZhouYuHost(GameEngine e):IHappyZhouYuHost
    {public SkillProgramStepOutcome Begin(ProgramSkillFrame f)=>e.BeginHappyZy(f);}
    private SkillProgramStepOutcome BeginHappyZy(ProgramSkillFrame supplied)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.HappyZhouYu is not null||f.InstructionIndex!=1||f.SkillId!=HappyQinyin||f.TriggerId!="whole-turn-discard"||f.WindowContext?.Window!=SkillProgramTriggerWindow.DiscardPhaseEnded||
            f.OwnerSeat!=_currentSeat||_phase!=TurnPhase.Discard||HappyZyDiscard(f.OwnerSeat) is not{}discard)throw new InvalidOperationException("Happy Qinyin requires its real own-discard ending and an actually completed own-hand discard in this turn.");
        var r=new HappyZyReceipt(HappyZySource(f),f.GameplayHash,f.WindowContext.ParentFrameId,_turnNumber,discard.Invocation.InvocationId,discard.Material.CardId)
        {Stage=HappyZyStage.Choosing,Seats=_players.Where(p=>p.IsAlive).OrderBy(p=>RelativeTableOrder(f.OwnerSeat,p.Seat)).Select(p=>p.Seat).ToArray()};
        ReplaceRuntimeTop(f=f with{HappyZhouYu=r});AdvanceEventRulesAndQueueFact(new HappyZyStartedEvent(f.Id,r.Source,r.Hash,r.Parent,r.Turn,r.DiscardInvocation,r.Seats.Count));
        for(var i=0;i<r.Seats.Count;i++)AdvanceEventRulesAndQueueFact(new HappyZyParticipantEvent(f.Id,i,r.Seats[i]));
        PublishHappyZy(f);return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> HappyZyChoices(ProgramSkillFrame f)=>new[]{("heal","令所有角色各回复1点体力"),("lose","令所有角色各失去1点体力"),("draw","令所有角色各摸一张牌")}.Select(x=>new PromptChoice(new("happy-qinyin."+f.Id+"."+x.Item1),x.Item2,[],[],new Dictionary<string,string>{{"program-action","happy-qinyin"},{"mode",x.Item1}})).ToArray();
    private void PublishHappyZy(ProgramSkillFrame f)
    {
        var choices=HappyZyChoices(f);var skill=_contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"琴音：选择全场结算方式。",[],[],f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=true,Choices=Array.AsReadOnly(choices.ToArray()),SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("happy-qinyin")] private void AnswerHappyZy(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{HappyZhouYu:{Stage:HappyZyStage.Choosing}r}f||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!HappyZyChoices(f).Any(c=>AssistedChoicesEqual([c],[choice])))throw new InvalidOperationException("Happy Qinyin requires its exact original three-way choice.");
        RequireHappyZy(f);ClearPendingDecision();var mode=choice.Parameters["mode"];
        ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.ResolvingEffect,HappyZhouYu=r with{Mode=mode,Stage=HappyZyStage.Ready}});AdvanceEventRulesAndQueueFact(new HappyZyModeEvent(f.Id,mode));ResumeHappyZy(f.Id);
    }
    private void IssueHappyZy(ProgramSkillFrame f,bool skipped,HappyZyStage next)
    {
        var r=f.HappyZhouYu!;ReplaceRuntimeTop(f with{HappyZhouYu=r with{Cursor=r.Cursor+1,Stage=next,Draw=null}});
        AdvanceEventRulesAndQueueFact(new HappyZyIssuedEvent(f.Id,r.Cursor+1,r.Target,r.Mode!,skipped));
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,41440)] private void CaptureHappyZyHp(ProgramSkillHpLostEvent fact)
    {
        if(_resolutionStack.LastOrDefault() is ProgramSkillFrame{HappyZhouYu:{Stage:HappyZyStage.HpLoss,Mode:"lose"}r}f&&fact.FrameId==f.Id&&fact.SkillId==f.SkillId&&fact.TargetSeat==r.Target&&fact.Amount==1)IssueHappyZy(f,false,HappyZyStage.Children);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,41441)] private void CaptureHappyZyPrevented(IdentityHpDeductionPreventedEvent fact)
    {
        if(_resolutionStack.LastOrDefault() is IdentityAppearanceFrame appearance&&appearance.Id==fact.AppearanceFrameId&&fact.Entry==IdentityAppearanceEntry.ProgramHpLoss&&fact.RequestedDeduction==1&&
            _resolutionStack.SingleOrDefault(p=>p.Id==appearance.Receipt.ParentFrameId) is ProgramSkillFrame{HappyZhouYu:{Stage:HappyZyStage.HpLoss,Mode:"lose"}r}f&&r.Target==fact.OwnerSeat&&appearance.Receipt.OriginalInstructionIndex==f.InstructionIndex)
        {var updated=f with{HappyZhouYu=r with{Cursor=r.Cursor+1,Stage=HappyZyStage.Children}};ReplaceRuntimeFrame(f.Id,updated);AdvanceEventRulesAndQueueFact(new HappyZyIssuedEvent(f.Id,r.Cursor+1,r.Target,"lose",true));}
    }
    [ProgramContinuation(ProgramContinuationKind.Resume,41440)] private bool ResumeHappyZy(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{HappyZhouYu:not null}root||root.Id!=id)return false;
        while(_pendingDecision is null&&_resolutionStack.LastOrDefault() is ProgramSkillFrame{HappyZhouYu:{}r}f&&f.Id==id)
        {
            RequireHappyZy(f);
            if(r.Stage==HappyZyStage.Choosing){PublishHappyZy(f);return true;}
            if(r.Stage==HappyZyStage.Drawing)return true;
            if(r.Stage==HappyZyStage.Children)
            {
                if(DrainHappyZy(f))return true;f=GetActiveProgramFrame(id);r=f.HappyZhouYu!;
                ReplaceRuntimeTop(f with{PendingMovementContinuation=null,HappyZhouYu=r with{Stage=HappyZyStage.Ready}});continue;
            }
            if(r.Stage==HappyZyStage.HpLoss)
            {
                new ProgramSkillHost(this).LoseHp(f.Id,f.SkillId,r.Target,1);
                if(_resolutionStack.LastOrDefault()?.Id!=id||GetActiveProgramFrame(id).HappyZhouYu!.Stage==HappyZyStage.HpLoss)return true;
                continue;
            }
            if(r.Cursor>=r.Seats.Count||_winner!=Winner.None)
            {ReplaceRuntimeTop(f=f with{HappyZhouYu=r with{Stage=HappyZyStage.Finished}});AdvanceEventRulesAndQueueFact(new HappyZyCompletedEvent(f.Id));FinishProgramSkill(f,true);return true;}
            var target=r.Seats[r.Cursor];ReplaceRuntimeTop(f=f with{HappyZhouYu=r=r with{Target=target}});
            if(!_players[target].IsAlive||r.Mode!="draw"&&!_players[target].HasHpValue){IssueHappyZy(f,true,HappyZyStage.Children);continue;}
            if(r.Mode=="lose"){ReplaceRuntimeTop(f with{HappyZhouYu=r with{Stage=HappyZyStage.HpLoss}});continue;}
            IssueHappyZy(f,false,r.Mode=="draw"?HappyZyStage.Drawing:HappyZyStage.Children);f=GetActiveProgramFrame(id);
            if(r.Mode=="draw"){BeginOperationProgramDraw(f,target,1,new("skill.happy-qinyin.draw"));return true;}
            new ProgramSkillHost(this).Recover(id,f.OwnerSeat,target,1,null,null);
        }
        return true;
    }
    private bool DrainHappyZy(ProgramSkillFrame f)
    {
        var continuation=f.HappyZhouYu!.Mode=="draw"?PostEventContinuation.AwaitedProgramMovement:PostEventContinuation.Program;
        return TryBeginQueuedRecoveryReplacement(f.Id,continuation)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||
            TryBeginHpChangedProgramWindow(f.Id,continuation)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id);
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveHappyZhouYuQinyin)] private void ReturnHappyZyDraw(ProgramSkillFrame f,NativeDrawOutcome outcome)
    {
        if(f.HappyZhouYu is not{Stage:HappyZyStage.Drawing,Mode:"draw",Draw:null}r||outcome.RecipientSeat!=r.Target||outcome.RequestedCount!=1||outcome.SequenceAfter!=_movementSequence||
            ProgramEventHistory<HappyZyDrawReturnedEvent>().Any(e=>e.FrameId==f.Id&&e.Ordinal==r.Cursor))throw new InvalidOperationException("Happy Qinyin lost its exact once-issued one-card native draw return.");
        ReplaceRuntimeTop(f with{HappyZhouYu=r with{Stage=HappyZyStage.Children,Draw=outcome},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        AdvanceEventRulesAndQueueFact(new HappyZyDrawReturnedEvent(f.Id,r.Cursor,r.Target,outcome.ActualDrawIds.Count,outcome.SequenceBefore,outcome.SequenceAfter,outcome.AttemptFrameId));ResumeHappyZy(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement,41440)] private bool ReturnHappyZyMovement(ProgramSkillFrame f)=>f.HappyZhouYu is not null&&ResumeHappyZy(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,41440)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,41440)] private bool RetainIssuedHappyZy(ProgramSkillFrame f)=>f.HappyZhouYu is{Mode:not null};
    [ProgramAiChoice(SkillProgramEffectOp.ResolveHappyZhouYuQinyin)] private PromptChoice ChooseHappyZy(PendingDecision p,ProgramSkillFrame f)=>p.Choices.Last();
}
