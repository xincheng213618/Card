using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string HappyShehuReason="skill-program.happy-shehu.discard",HappyDingliDraw="skill-program.happy-dingli.draw";
    private static CardConversionSource HappySunQuanSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    [ProgramCapability] private sealed class HappySunQuanCapability(GameEngine e):IHappySunQuanHost
    {public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginHappySunQuan(f,op);}
    private ProgramLifecycleTriggerWindowFrame? HappyLearningParent(long id)=>_resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(p=>p.Id==id&&p.HappyLearningStart is not null);
    private ProgramCardTriggerWindowFrame? HappyShehuParent(long id)=>_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(p=>p.Id==id);
    private bool HappyDingliQualified(int owner,int holder)=>owner!=holder&&_players[owner].IsAlive&&_players[holder].IsAlive&&_players[owner].HasHpValue&&_players[holder].HasHpValue&&
        (_players[holder].Hp<_players[owner].Hp||_players[owner].Hp<_players[owner].MaxHp);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,40940,SkillProgramEffectOp.GrantHappySunQuanLearning,SkillProgramEffectOp.ResolveHappySunQuanLearning,SkillProgramEffectOp.ResolveHappySunQuanShehu,SkillProgramEffectOp.ResolveHappySunQuanDingli)]
    private bool HappySunQuanEligible(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext w)
    {
        if(c.SkillId==HappySunQuanComposition.Quanxue)return _players.Any(p=>p.IsAlive&&p.Seat!=c.OwnerSeat);
        if(c.SkillId==HappySunQuanComposition.Learning)return IsIssuedHappyLearning(c,w);
        if(c.SkillId==HappySunQuanComposition.Shehu)return HappyShehuParent(w.ParentFrameId) is{}p&&p.Action.Type==CardActionType.Use&&p.Action.ActorSeat==c.OwnerSeat&&IsSlashCard(p.Action.EffectiveKind)&&
            p.Action.EffectiveDesignatedTargetSeats.Any(s=>_players[s].IsAlive&&HappyXue(s)>0&&GetHand(_players[s]).Count>0)&&
            !ProgramEventHistory<HappySunQuanStartedEvent>().Any(e=>e.Source.OwnerSeat==c.OwnerSeat&&e.Source.SkillInstanceId==c.SkillInstanceId&&e.Operation==SkillProgramEffectOp.ResolveHappySunQuanShehu&&e.ActionId==p.Action.ActionId);
        return HappyLearningParent(w.ParentFrameId) is{}start&&IsHappyLearningStart(start)&&start.CandidateIndex>0&&start.CandidateIndex<start.Candidates.Count&&start.Candidates[start.CandidateIndex]==c&&
            ProgramEventHistory<HappyLearningChoiceEvent>().Count(e=>e.WindowId==start.Id)==1&&HappyDingliQualified(c.OwnerSeat,start.OwnerSeat);
    }
    private SkillProgramStepOutcome BeginHappySunQuan(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.InstructionIndex!=1||f.HappySunQuan is not null||f.WindowContext is not{}w)throw new InvalidOperationException("Happy Sun Quan requires his original unpaid trigger.");
        var card=op==SkillProgramEffectOp.ResolveHappySunQuanShehu?HappyShehuParent(w.ParentFrameId):null;
        var start=op is SkillProgramEffectOp.ResolveHappySunQuanLearning or SkillProgramEffectOp.ResolveHappySunQuanDingli?HappyLearningParent(w.ParentFrameId):null;
        var target=start?.OwnerSeat??f.OwnerSeat;
        var amount=op==SkillProgramEffectOp.ResolveHappySunQuanLearning?start!.HappyLearningStart!.Grants.Sum(g=>g.After):0;
        var branch=op==SkillProgramEffectOp.ResolveHappySunQuanDingli?(_players[target].Hp>=_players[f.OwnerSeat].Hp?1:2):0;
        if(op==SkillProgramEffectOp.ResolveHappySunQuanDingli)amount=branch==1?1:Math.Min(2,_players[f.OwnerSeat].Hp-_players[target].Hp);
        int[] targets=card is null?[]:card.Action.EffectiveDesignatedTargetSeats.Distinct().Where(s=>_players[s].IsAlive&&HappyXue(s)>0).OrderBy(s=>RelativeTableOrder(f.OwnerSeat,s)).ToArray();
        var r=new HappySunQuanReceipt(HappySunQuanSource(f),f.GameplayHash,op,w.ParentFrameId,_turnNumber)
        {Stage=op switch {SkillProgramEffectOp.GrantHappySunQuanLearning=>HappySunQuanStage.Targets,SkillProgramEffectOp.ResolveHappySunQuanLearning=>HappySunQuanStage.LearningChoice,
            SkillProgramEffectOp.ResolveHappySunQuanShehu=>HappySunQuanStage.Discard,_=>branch==1?HappySunQuanStage.RecoveryChildren:HappySunQuanStage.Drawing},
            Target=target,Amount=amount,Branch=branch,Targets=targets,ActionId=card?.Action.ActionId??0,Before=_movementSequence,After=_movementSequence};
        ReplaceRuntimeTop(f=f with{HappySunQuan=r});
        AdvanceEventRulesAndQueueFact(new HappySunQuanStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.ParentId,r.ActualTurn,r.ActionId,target,amount));
        if(op==SkillProgramEffectOp.ResolveHappySunQuanLearning)
        {
            if(start is null||!IsHappyLearningStart(start)||start.CandidateIndex!=0||f.OwnerSeat!=target||HappyXue(target)!=amount)throw new InvalidOperationException("Learning lost the exact holder's frozen source obligations.");
            foreach(var grant in start.HappyLearningStart!.Grants)SetStrategicMarker(target,PlayerMarkerKind.HappyXue,grant.Source.OwnerSeat,0,f);
            AdvanceEventRulesAndQueueFact(new HappyLearningRemovedEvent(f.Id,start.Id,_turnNumber,target,amount));
        }
        if(op==SkillProgramEffectOp.ResolveHappySunQuanShehu)
            for(var i=0;i<targets.Length;i++)AdvanceEventRulesAndQueueFact(new HappyShehuTargetEvent(f.Id,i,r.ActionId,targets[i],HappyXue(targets[i])));
        if(op==SkillProgramEffectOp.ResolveHappySunQuanDingli)
        {
            AdvanceEventRulesAndQueueFact(new HappyDingliBenefitEvent(f.Id,r.ParentId,target,_players[f.OwnerSeat].Hp,_players[target].Hp,branch,amount));
            if(branch==2){BeginOperationProgramDraw(f,f.OwnerSeat,amount,new(HappyDingliDraw));return SkillProgramStepOutcome.AwaitChild;}
            ReplaceRuntimeTop(f=f with{PendingMovementContinuation=new(f.OwnerSeat,0,null)});
            new ProgramSkillHost(this).Recover(f.Id,f.OwnerSeat,f.OwnerSeat,1,null,null);
            RecordHappySunQuanMovement(f.Id,r.Before);
        }
        ContinueHappySunQuan(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    private IReadOnlyList<PromptChoice> HappySunQuanChoices(ProgramSkillFrame f)
    {
        var r=f.HappySunQuan!;
        PromptChoice C(string option,string label,int target=-1,IReadOnlyList<int>?cards=null,IReadOnlyDictionary<string,string>? parameters=null)=>new(new($"happy-sun-quan.{f.Id}.{r.Index}.{option}"),label,cards??[],target<0?[]:[target],
            new Dictionary<string,string>(parameters??new Dictionary<string,string>()){["program-action"]="happy-sun-quan",["option"]=option,["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture)});
        if(r.Stage==HappySunQuanStage.Targets)
        {
            var result=_players.Where(p=>p.IsAlive&&p.Seat!=f.OwnerSeat&&!r.Targets.Contains(p.Seat)).OrderBy(p=>RelativeTableOrder(f.OwnerSeat,p.Seat))
                .Select(p=>C("target:"+p.Seat,"令“"+p.Name+"”获得一个学",p.Seat)).ToList();
            result.Add(C("finish","完成选择"));return result;
        }
        if(r.Stage==HappySunQuanStage.LearningChoice)return[C("restricted","本回合出牌阶段只能对自己使用牌",f.OwnerSeat),C("lose-hp","失去1点体力",f.OwnerSeat)];
        if(r.Stage!=HappySunQuanStage.Discard||r.Index>=r.Targets.Count)return[];
        return BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,r.Targets[r.Index],[CardZoneKind.Hand],OwnedCardMoveIntent.Discard)
            .Select((c,i)=>C("discard:"+i,c.Description,r.Targets[r.Index],c.Cards,c.Parameters)).ToArray();
    }
    [ProgramContinuation(ProgramContinuationKind.Resume,40940)] private bool ContinueHappySunQuan(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{HappySunQuan:not null}f||f.Id!=id)return false;
        RequireHappySunQuan(f);var r=f.HappySunQuan!;
        if(r.Stage==HappySunQuanStage.Drawing)return true;
        if(r.Stage is HappySunQuanStage.MovementChildren or HappySunQuanStage.RecoveryChildren or HappySunQuanStage.HpLoss)
        {
            if(DrainHappySunQuan(f))return true;
            var next=r.Operation==SkillProgramEffectOp.ResolveHappySunQuanShehu?HappySunQuanStage.Discard:HappySunQuanStage.Finished;
            ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null,HappySunQuan=r=r with{Stage=next,Index=r.Operation==SkillProgramEffectOp.ResolveHappySunQuanShehu?r.Index+1:r.Index,
                Before=_movementSequence,After=_movementSequence,Paid=[],ChosenCard=0}});
        }
        if(r.Stage==HappySunQuanStage.Finished||_winner!=Winner.None||!_players[f.OwnerSeat].IsAlive){FinishHappySunQuan(f);return true;}
        if(r.Stage==HappySunQuanStage.Targets&&r.Targets.Count==2){FinishHappySunQuan(f);return true;}
        if(r.Stage==HappySunQuanStage.Discard)
        {
            while(r.Index<r.Targets.Count&&(!_players[r.Targets[r.Index]].IsAlive||HappySunQuanChoices(f).Count==0))
            {
                AdvanceEventRulesAndQueueFact(new HappyShehuSkippedEvent(f.Id,r.Index,r.Targets[r.Index]));
                ReplaceRuntimeTop(f=f with{HappySunQuan=r=r with{Index=r.Index+1}});
            }
            if(r.Index>=r.Targets.Count){FinishHappySunQuan(f);return true;}
        }
        var choices=HappySunQuanChoices(f);var skill=_contentRegistry.GetSkill(f.SkillId);
        if(choices.Count==0){FinishHappySunQuan(f);return true;}
        ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,skill.Name+"：请选择。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=true,TargetSeat=r.Stage==HappySunQuanStage.Discard?r.Targets[r.Index]:f.OwnerSeat,Choices=Array.AsReadOnly(choices.ToArray()),SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();return true;
    }
    [ProgramAnswer("happy-sun-quan")] private void AnswerHappySunQuan(PromptChoice chosen)
    {
        var f=_resolutionStack.LastOrDefault() as ProgramSkillFrame??throw new InvalidOperationException("Happy Sun Quan lost his choice parent.");RequireHappySunQuan(f);
        if(!HappySunQuanChoices(f).Any(c=>AssistedChoicesEqual([c],[chosen])))throw new InvalidOperationException("Happy Sun Quan requires his exact current choice.");
        var r=f.HappySunQuan!;var option=chosen.Parameters["option"];ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
        if(r.Stage==HappySunQuanStage.Targets)
        {
            if(option=="finish"){FinishHappySunQuan(f);return;}
            var target=chosen.Targets.Single();var before=_players[target].MarkerSourceCounts.GetValueOrDefault((PlayerMarkerKind.HappyXue,f.OwnerSeat));
            SetStrategicMarker(target,PlayerMarkerKind.HappyXue,f.OwnerSeat,before+1,f);
            AdvanceEventRulesAndQueueFact(new HappyXueGrantEvent(f.Id,r.Source,r.GameplayHash,target,before,before+1));
            ReplaceRuntimeTop(f with{HappySunQuan=r with{Targets=r.Targets.Append(target).ToArray()}});ContinueHappySunQuan(f.Id);return;
        }
        if(r.Stage==HappySunQuanStage.LearningChoice)
        {
            var branch=option=="restricted"?1:2;
            ReplaceRuntimeTop(f=f with{HappySunQuan=r with{Branch=branch,Stage=branch==1?HappySunQuanStage.Finished:HappySunQuanStage.HpLoss}});
            AdvanceEventRulesAndQueueFact(new HappyLearningChoiceEvent(f.Id,r.ParentId,r.ActualTurn,f.OwnerSeat,branch));
            if(branch==2&&new ProgramSkillHost(this).LoseHp(f.Id,f.SkillId,f.OwnerSeat,1)==SkillProgramStepOutcome.AwaitChild)return;
            ContinueHappySunQuan(f.Id);return;
        }
        var holder=r.Targets[r.Index];var slot=int.Parse(chosen.Parameters["slot-index"],CultureInfo.InvariantCulture);var card=GetHand(_players[holder])[slot];var sequence=_movementSequence;
        ReplaceRuntimeTop(f=f with{HappySunQuan=r with{Stage=HappySunQuanStage.MovementChildren,Target=holder,ChosenCard=card.Id,Before=sequence,After=sequence,Paid=[]},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        MoveProgramLogicalDiscard(f,f.OwnerSeat,[card.Id],new(HappyShehuReason),(_,records)=>
        {
            var parent=GetActiveProgramFrame(f.Id);var paid=parent.HappySunQuan!;
            ReplaceRuntimeTop(parent with{HappySunQuan=paid with{After=_movementSequence,Paid=records,Payments=paid.Payments.Append(new(r.Index,holder,card.Id,sequence,_movementSequence){Movements=records}).ToArray()}});
            AdvanceEventRulesAndQueueFact(new HappySunQuanMovementEvent(f.Id,r.Index,sequence,_movementSequence,records.Count));
        });
        ContinueHappySunQuan(f.Id);
    }
    private void RecordHappySunQuanMovement(long id,long before)
    {
        var f=GetActiveProgramFrame(id);var paid=_cardMovements.Where(m=>m.Sequence>before&&m.Sequence<=_movementSequence).ToArray();
        ReplaceRuntimeTop(f with{HappySunQuan=f.HappySunQuan! with{Before=before,After=_movementSequence,Paid=paid}});
        AdvanceEventRulesAndQueueFact(new HappySunQuanMovementEvent(id,f.HappySunQuan.Index,before,_movementSequence,paid.Length));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveHappySunQuanDingli)] private void ReturnHappyDingliDraw(ProgramSkillFrame f,NativeDrawOutcome outcome)
    {
        if(f.HappySunQuan is not{Stage:HappySunQuanStage.Drawing,Branch:2,Draw:null}r||outcome.RecipientSeat!=f.OwnerSeat||outcome.RequestedCount!=r.Amount||outcome.SequenceAfter!=_movementSequence)
            throw new InvalidOperationException("Dingli lost its once-issued native draw return.");
        ReplaceRuntimeTop(f=f with{HappySunQuan=r with{Stage=HappySunQuanStage.MovementChildren,Draw=outcome,Before=outcome.SequenceBefore,After=outcome.SequenceAfter,
            Paid=_cardMovements.Where(m=>m.Sequence>outcome.SequenceBefore&&m.Sequence<=outcome.SequenceAfter&&m.Reason.Value==HappyDingliDraw).ToArray()},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        AdvanceEventRulesAndQueueFact(new HappySunQuanMovementEvent(f.Id,0,outcome.SequenceBefore,outcome.SequenceAfter,f.HappySunQuan!.Paid.Count));ContinueHappySunQuan(f.Id);
    }
    private bool DrainHappySunQuan(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||
        TryBeginAdvancedSkillsChanged(f.Id)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id);
    private void FinishHappySunQuan(ProgramSkillFrame f)
    {
        if(f.PendingMovementContinuation is not null)throw new InvalidOperationException("Happy Sun Quan cannot finish with an unreturned paid child.");
        ReplaceRuntimeTop(f=f with{HappySunQuan=f.HappySunQuan! with{Stage=HappySunQuanStage.Finished}});AdvanceEventRulesAndQueueFact(new HappySunQuanCompletedEvent(f.Id));FinishProgramSkill(f,true);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement,40940)] private bool ReturnHappySunQuanMovement(ProgramSkillFrame f)=>f.HappySunQuan is not null&&ContinueHappySunQuan(f.Id);
    [ProgramAiChoice(SkillProgramEffectOp.GrantHappySunQuanLearning,SkillProgramEffectOp.ResolveHappySunQuanLearning,SkillProgramEffectOp.ResolveHappySunQuanShehu,SkillProgramEffectOp.ResolveHappySunQuanDingli)]
    private PromptChoice AiHappySunQuan(PendingDecision p,ProgramSkillFrame f)=>p.Choices.First();
}
