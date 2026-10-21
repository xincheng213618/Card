using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramCapability]private sealed class YaDanCapability(GameEngine e):IYaDanHost
    {public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginYaDan(f,op);}
    private SkillProgramStepOutcome BeginYaDan(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);var candidate=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,f.WindowContext?.OccurrenceIndex??0);
        if(f.YaDan is not null||f.InstructionIndex!=1||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||f.WindowContext is not{}context)throw new InvalidOperationException("Qingya requires its exact original native instruction.");
        YaDanReceipt r;
        if(op==SkillProgramEffectOp.ResolveQingyaBetweenHandDiscards)
        {
            var use=QingyaQualifiedUse(candidate,context)??throw new InvalidOperationException("Qingya requires one genuinely designated native Slash target.");
            var living=Array.AsReadOnly(CurrentTableOrder().Where(seat=>_players[seat].IsAlive).ToArray());var arcs=QingyaArcs(f.OwnerSeat,use.TargetSeats.Single(),living);
            r=new(YaDanSource(f),f.GameplayHash,op,_turnNumber,context.ParentFrameId,arcs.Count>1?QingyaStage.SideChoice:QingyaStage.HandChoice)
            {IssuedPhase=CurrentActualDiscardRecoveryPhase(),OriginalUseId=use.Id,OriginalActionId=use.Action!.ActionId,OriginalProvider=use.Action.ProviderSeat,
                OriginalTarget=use.TargetSeats.Single(),Arcs=arcs,LivingSeats=living,IssuedHistoryCutoff=CompleteProgramEventHistory().Count()};
            if(arcs.Count==1)r=r with{Direction=arcs[0].Direction,Targets=arcs[0].Seats};
        }
        else
        {
            if(op!=SkillProgramEffectOp.ResolveQingyaNextActualPhaseUse||!IsIssuedQingyaCandidate(candidate,context)||context.ActualPhaseEnd is not{}due)throw new InvalidOperationException("Qingya cannot borrow another actual phase's issued opportunity.");
            var grant=ProgramEventHistory<QingyaGrantIssuedEvent>().Single(g=>g.GrantId==due.EntitlementId);
            r=new(grant.Source,grant.GameplayHash,op,_turnNumber,context.ParentFrameId,QingyaStage.NativeChoice)
            {GrantId=grant.GrantId,IssuedPhase=grant.IssuedPhase,IssuedHistoryCutoff=grant.HistoryCutoff,DuePhase=due.Phase,Entities=QingyaGrantedEntities(grant)};
        }
        ReplaceRuntimeTop(f=f with{YaDan=r});
        AdvanceEventRulesAndQueueFact(new YaDanStartedEvent(f.Id,r.Source,r.GameplayHash,r.Op,r.ActualTurn,r.ParentId,r.OriginalUseId,r.OriginalActionId,r.OriginalTarget,r.IssuedPhase,r.IssuedHistoryCutoff,r.GrantId,r.DuePhase));
        if(r.Op==SkillProgramEffectOp.ResolveQingyaBetweenHandDiscards)
        {
            for(var i=0;i<r.LivingSeats.Count;i++)AdvanceEventRulesAndQueueFact(new QingyaLivingSeatEvent(f.Id,i,r.LivingSeats[i]));
            foreach(var arc in r.Arcs){AdvanceEventRulesAndQueueFact(new QingyaArcFrozenEvent(f.Id,arc.Direction,arc.Seats.Count));for(var i=0;i<arc.Seats.Count;i++)AdvanceEventRulesAndQueueFact(new QingyaArcSeatEvent(f.Id,arc.Direction,i,arc.Seats[i]));}
            if(r.Direction is{}direction)AdvanceEventRulesAndQueueFact(new QingyaSideChosenEvent(f.Id,direction));
            if(r.Stage==QingyaStage.SideChoice)PublishYaDan(f);else NextQingyaHand(f);
        }
        else
        {
            AdvanceEventRulesAndQueueFact(new QingyaGrantConsumedEvent(r.GrantId!.Value,f.Id,r.DuePhase!));
            if(QingyaNativeActions(f).Count==0)FinishYaDan(f);else PublishYaDan(f);
        }
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private static Dictionary<string,string> YaDanArgs(string branch) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_ya_dan_ol_YaDan.YaDanArgs(branch);
    private IReadOnlyList<PromptChoice> YaDanChoices(ProgramSkillFrame f)
    {
        var r=f.YaDan!;
        if(r.Stage==QingyaStage.SideChoice)return Array.AsReadOnly(r.Arcs.Select(a=>new PromptChoice(new($"qingya.{f.Id}.side.{a.Direction}"),a.Direction==1?"顺时针一侧":"逆时针一侧",[],a.Seats,
            new Dictionary<string,string>(YaDanArgs("side")){["direction"]=a.Direction.ToString(CultureInfo.InvariantCulture)})).ToArray());
        if(r.Stage==QingyaStage.HandChoice)return Array.AsReadOnly(r.Materials.Select(m=>new PromptChoice(new($"qingya.{f.Id}.{r.Cursor}.slot.{m.Slot}"),$"弃置 {_players[m.HolderSeat].Name} 的第{m.Slot+1}张手牌",[],[],
            new Dictionary<string,string>(YaDanArgs("hand")){["slot-index"]=m.Slot.ToString(CultureInfo.InvariantCulture)})).ToArray());
        if(r.Stage!=QingyaStage.NativeChoice)return [];
        var choices=QingyaNativeActions(f).Select((a,i)=>new PromptChoice(new($"qingya.{f.Id}.native.{i}"),a.Description,[a.CardId!.Value],a.TargetSeats.Count>0?a.TargetSeats:a.TargetSeat is{}seat?[seat]:[],
            new Dictionary<string,string>(YaDanArgs("use")){["action-index"]=i.ToString(CultureInfo.InvariantCulture)})).ToList();
        choices.Add(new(new($"qingya.{f.Id}.decline"),"不使用倾轧弃牌。",[],[],YaDanArgs("decline")));return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishYaDan(ProgramSkillFrame f)
    {
        AssertYaDan(f);var r=f.YaDan!;var choices=YaDanChoices(f);
        if(!_players[f.OwnerSeat].IsAlive){FinishYaDan(f);return;}
        if(choices.Count==0)throw new InvalidOperationException("Qingya cannot publish a missing frozen side, Hand slot or native action.");
        ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});var skill=_contentRegistry.GetSkill(f.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,r.Stage==QingyaStage.SideChoice?"倾轧：选择等长的一侧。":r.Stage==QingyaStage.HandChoice?"倾轧：弃置当前角色的一张暗手牌。":"倾轧：可使用一张仍留在弃牌堆的原牌。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=r.Stage==QingyaStage.HandChoice,TargetSeat=r.Stage==QingyaStage.HandChoice?r.Targets[r.Cursor]:f.OwnerSeat,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("ya-dan-qingya")]private void AnswerYaDan(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{YaDan:{}r}f||_pendingDecision?.PlayerSeat!=f.OwnerSeat||
            !YaDanChoices(f).Any(c=>AssistedChoicesEqual([c],[choice])))throw new InvalidOperationException("Qingya requires its exact original private slot, side or native card action.");
        AssertYaDan(f);ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
        if(!_players[f.OwnerSeat].IsAlive){FinishYaDan(f);return;}
        if(r.Stage==QingyaStage.SideChoice)
        {
            var direction=int.Parse(choice.Parameters["direction"],CultureInfo.InvariantCulture);var arc=r.Arcs.Single(a=>a.Direction==direction);
            AdvanceEventRulesAndQueueFact(new QingyaSideChosenEvent(f.Id,direction));ReplaceRuntimeTop(f=f with{YaDan=r with{Direction=direction,Targets=arc.Seats,Stage=QingyaStage.HandChoice}});
            NextQingyaHand(f);return;
        }
        if(r.Stage==QingyaStage.NativeChoice){AnswerQingyaNativeUse(f,choice);return;}
        if(r.Stage!=QingyaStage.HandChoice||r.Cursor>=r.Targets.Count)throw new InvalidOperationException("Qingya cannot discard outside its frozen path.");
        var material=r.Materials.Single(m=>m.Slot.ToString(CultureInfo.InvariantCulture)==choice.Parameters["slot-index"]);
        if(!QingyaHandMaterials(f.OwnerSeat,material.HolderSeat).Contains(material))throw new InvalidOperationException("Qingya cannot replace the original opaque Hand entity.");
        var before=_movementSequence;ReplaceRuntimeTop(f=f with{YaDan=r with{Stage=QingyaStage.DiscardChildren,Before=before},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        var logical=CaptureNativeLogicalDiscardInvocation(f.OwnerSeat,f.OwnerSeat,[material.CardId],new(QingyaDiscardReason),f.Id);
        MoveProgramCardsFromMultipleSources([material.CardId],CardLocation.DiscardPile,new(QingyaDiscardReason));CompleteNativeLogicalDiscardInvocation(logical);
        var paid=_cardMovements.Single(m=>m.Sequence>before&&m.CardId==material.CardId&&m.From==CardLocation.Hand(material.HolderSeat)&&m.To==CardLocation.DiscardPile&&m.Reason.Value==QingyaDiscardReason);
        var entity=new QingyaDiscardEntity(material.CardId,material.Kind,material.HolderSeat,paid.Sequence);
        ReplaceRuntimeTop(f with{YaDan=f.YaDan! with{After=_movementSequence,Entities=r.Entities.Append(entity).ToArray()}});
        AdvanceEventRulesAndQueueFact(new QingyaHandDiscardedEvent(f.Id,r.Cursor,material,paid.Sequence,before,_movementSequence));ContinueYaDan(f.Id);
    }
    private void ContinueYaDan(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{YaDan:{}r}f||f.Id!=id)return;AssertYaDan(f);
        if(r.Stage is QingyaStage.DiscardChildren or QingyaStage.NativeChildren)
        {
            if(TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id))return;
            if(r.Stage==QingyaStage.NativeChildren){FinishYaDan(GetActiveProgramFrame(id));return;}
            f=GetActiveProgramFrame(id);r=f.YaDan!;
            ReplaceRuntimeTop(f=f with{YaDan=r with{Cursor=r.Cursor+1,Materials=[],Before=0,After=0,Stage=QingyaStage.HandChoice},PendingMovementContinuation=null});NextQingyaHand(f);
        }
        else if(r.Stage is not QingyaStage.NativeUse&&_pendingDecision is null)PublishYaDan(f);
    }
    private void FinishYaDan(ProgramSkillFrame f)
    {
        var r=f.YaDan!;
        if(r.Op==SkillProgramEffectOp.ResolveQingyaBetweenHandDiscards&&r.Entities.Count>0)
        {
            if(ProgramEventHistory<QingyaGrantIssuedEvent>().Any(e=>e.GrantId==f.Id))throw new InvalidOperationException("Qingya cannot issue its next-phase obligation twice.");
            AdvanceEventRulesAndQueueFact(new QingyaGrantIssuedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.IssuedPhase,r.IssuedHistoryCutoff,r.Entities.Count));
        }
        AdvanceEventRulesAndQueueFact(new YaDanCompletedEvent(f.Id));ReplaceRuntimeTop(f=f with{YaDan=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume,27260)]private bool ResumeYaDan(long id)
    {if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{YaDan:not null}f||f.Id!=id)return false;ContinueYaDan(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,27260)]private bool ReturnYaDanMovement(ProgramSkillFrame f)
    {if(f.YaDan is null)return false;ContinueYaDan(f.Id);return true;}
    [ProgramAiChoice(SkillProgramEffectOp.ResolveQingyaBetweenHandDiscards,SkillProgramEffectOp.ResolveQingyaNextActualPhaseUse)]
    private PromptChoice ChooseYaDanAi(PendingDecision p,ProgramSkillFrame f)
    {
        if(f.YaDan!.Stage==QingyaStage.SideChoice)return p.Choices.OrderByDescending(c=>c.Targets.Sum(s=>_aiBrains[f.OwnerSeat].ScoreProgramTarget(CreateSnapshot(f.OwnerSeat),s,new(1,0,0,0,0,0,false,false)))).First();
        if(f.YaDan.Stage==QingyaStage.HandChoice)return p.Choices.First();
        return p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("branch")=="use")??p.Choices.Last();
    }
}

