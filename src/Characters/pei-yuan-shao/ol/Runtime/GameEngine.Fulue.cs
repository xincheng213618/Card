namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string FulueObtainReason="skill-program.pei-yuan-shao.fulue.obtain";
    private const string FulueCounterBind="fulue-counter";
    private static FulueOption OtherFulueOption(FulueOption option) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_pei_yuan_shao_ol_Fulue.OtherFulueOption(option);
    private bool FulueOptionAvailable(CardConversionSource source,int turn,FulueOption option)=>!ProgramEventHistory<FulueOptionExecutedEvent>().Any(e=>e.Entry.Source.OwnerSeat==source.OwnerSeat&&e.Entry.Source.SkillId==source.SkillId&&e.Entry.ActualTurn==turn&&e.Entry.Option==option);
    private static bool FulueCardKind(CardKind kind) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_pei_yuan_shao_ol_Fulue.FulueCardKind(kind);
    private CardUseFrame? FulueContextUse(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(context.CardUse is not{}card||context.OwnerSeat!=c.OwnerSeat||LifecycleCardUse(card.ParentCardUseFrameId)is not{Action:{Type:CardActionType.Use}action}use||
            action.ActionId!=card.CardActionId||!FulueCardKind(use.CardKind)||!HasExactAcceptedActualHandGainUse(use,action)||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId)is not{}window||window.ParentFrameId!=use.Id||window.Action.ActionId!=action.ActionId||window.CandidateIndex<0||window.CandidateIndex>=window.Candidates.Count)return null;
        var current=window.Candidates[window.CandidateIndex];
        return current.OwnerSeat==c.OwnerSeat&&current.SkillId==c.SkillId&&current.TriggerId==c.BindingId&&current.SkillInstanceId==c.SkillInstanceId&&current.GameplayHash==c.GameplayHash&&CreateCardActionProgramContext(window,current)==context?use:null;
    }
    private bool FulueOriginalCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(context.Window!=SkillProgramTriggerWindow.CardUseTargetsFinalized||FulueContextUse(c,context)is not{}use||use.SourceSeat!=c.OwnerSeat||use.TargetSeats is not[var target]||target==c.OwnerSeat||!_players[target].IsAlive||!_players[c.OwnerSeat].IsAlive||
            use.Action!.EffectiveDesignatedTargetSeats.Distinct().Count()!=1||!use.Action.EffectiveDesignatedTargetSeats.Contains(target)||!HasRuntimeSkillInstance(_players[c.OwnerSeat],c.SkillId,c.SkillInstanceId)||
            use.FulueEntries?.Any(e=>!e.Counter&&e.Source.OwnerSeat==c.OwnerSeat&&e.Source.SkillId==c.SkillId)==true)return false;
        var source=new CardConversionSource(c.SkillId,c.BindingId,c.OwnerSeat,c.SkillInstanceId);
        return FulueOptionAvailable(source,_turnNumber,FulueOption.Damage)||FulueOptionAvailable(source,_turnNumber,FulueOption.Obtain)&&HasFulueObtainableCard(c.OwnerSeat,target);
    }
    private bool FulueUseProof(CardUseFrame use)
    {
        if(use.Action is not{Type:CardActionType.Use}action||use.FulueEntries is not{Count:>0}entries||!FulueCardKind(use.CardKind)||!HasExactAcceptedActualHandGainUse(use,action)||entries.Distinct().Count()!=entries.Count)return false;
        var facts=ProgramEventHistory<FulueOptionExecutedEvent>().Where(e=>e.Entry.UseId==use.Id).Select(e=>e.Entry).ToArray();
        return entries.SequenceEqual(facts)&&entries.All(e=>e.UseId==use.Id&&e.ActionId==action.ActionId&&e.ActualTurn==_turnNumber&&e.TurnOwner==_currentSeat&&e.UserSeat!=e.TargetSeat&&e.ProgramFrameId>use.Id&&Enum.IsDefined(e.Option)&&
            _contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash==e.GameplayHash&&ShownEntityUseActorMatches(use,e.UserSeat,action.ProviderSeat)&&
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(b=>b.FrameId==e.ProgramFrameId&&b.SkillId==e.Source.SkillId&&b.SkillInstanceId==e.Source.SkillInstanceId&&b.OwnerSeat==e.Source.OwnerSeat&&b.Window==SkillProgramTriggerWindow.CardUseTargetsFinalized)==1&&
            (e.Counter?use.FulueCounter is{}counter&&counter.Source==e.Source&&counter.OtherOption==e.Option&&counter.ActorSeat==e.UserSeat&&counter.TargetSeat==e.TargetSeat:use.Action.EffectiveDesignatedTargetSeats.SequenceEqual([e.TargetSeat])&&e.Source.OwnerSeat==e.UserSeat));
    }
    private bool FulueZeroDamage(CardUseFrame use)=>!ProgramEventHistory<CompletedUndamagedUseDamageRecordedEvent>().Any(e=>e.CardUseFrameId==use.Id&&e.Amount>0);
    private FulueUseEntry? FulueOriginalEntry(CardUseFrame use,int owner,string skill,string instance)=>use.FulueEntries?.SingleOrDefault(e=>!e.Counter&&e.Source.OwnerSeat==owner&&e.Source.SkillId==skill&&e.Source.SkillInstanceId==instance);
    private bool IsIssuedFulueCandidate(ProgramTriggerCandidate c)=>_contentRegistry.Skills.GetValueOrDefault(c.SkillId)?.Program?.Triggers.Any(t=>t.Id==c.BindingId&&t.Effects.FirstOrDefault()?.Op is SkillProgramEffectOp.RequestFulueZeroDamageCounterSlash or SkillProgramEffectOp.ResolveFulueCounterOption)==true;
    private bool CanRunIssuedFulueCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(!IsIssuedFulueCandidate(c)||FulueContextUse(c,context)is not{}use)return false;
        var op=GetProgramTrigger(c).Effects[0].Op;
        if(op==SkillProgramEffectOp.ResolveFulueCounterOption)return context.Window==SkillProgramTriggerWindow.CardUseTargetsFinalized&&use.FulueCounter is{Applied:false}r&&ValidFulueCounterInvoice(use,r)&&r.Source.OwnerSeat==c.OwnerSeat&&r.Source.SkillId==c.SkillId&&r.Source.SkillInstanceId==c.SkillInstanceId&&r.GameplayHash==c.GameplayHash&&FulueOptionAvailable(r.Source,r.ActualTurn,r.OtherOption);
        return context.Window==SkillProgramTriggerWindow.CardUseCompleted&&use.Step==ResolutionFrameStep.Completed&&FulueUseProof(use)&&FulueZeroDamage(use)&&FulueOriginalEntry(use,c.OwnerSeat,c.SkillId,c.SkillInstanceId)is{}entry&&
            _players[entry.UserSeat].IsAlive&&_players[entry.TargetSeat].IsAlive&&!ProgramEventHistory<FulueCounterRequestedEvent>().Any(e=>e.Invoice.OriginalUseId==use.Id&&e.Invoice.Source==entry.Source)&&
            ProgramEventHistory<CardUseFinishedEvent>().Count(e=>e.ResolutionId==use.Id)==1;
    }
    [ProgramPipelineHook(ProgramPipelinePhase.CollectIssuedCardTrigger, 13)]
    private void CollectIssuedFulueCandidates(CardActionContext action,SkillProgramTriggerWindow window,List<ProgramCardTriggerCandidate> result)
    {
        if(_resolutionStack.LastOrDefault()is not CardUseFrame use||use.Action?.ActionId!=action.ActionId)return;
        var issued=new List<(CardConversionSource Source,string Hash,SkillProgramEffectOp Op)>();
        if(window==SkillProgramTriggerWindow.CardUseCompleted&&use.FulueEntries is{}entries&&FulueUseProof(use)&&FulueZeroDamage(use))
            issued.AddRange(entries.Where(e=>!e.Counter&&_players[e.UserSeat].IsAlive&&_players[e.TargetSeat].IsAlive&&!ProgramEventHistory<FulueCounterRequestedEvent>().Any(f=>f.Invoice.OriginalUseId==use.Id&&f.Invoice.Source==e.Source)).Select(e=>(e.Source,e.GameplayHash,SkillProgramEffectOp.RequestFulueZeroDamageCounterSlash)));
        if(window==SkillProgramTriggerWindow.CardUseTargetsFinalized&&use.FulueCounter is{Applied:false}counter&&ValidFulueCounterInvoice(use,counter)&&FulueOptionAvailable(counter.Source,counter.ActualTurn,counter.OtherOption))issued.Add((counter.Source,counter.GameplayHash,SkillProgramEffectOp.ResolveFulueCounterOption));
        foreach(var(source,hash,op)in issued)
        {
            var p=_contentRegistry.GetSkill(source.SkillId).Program!;var trigger=p.Triggers.Single(t=>t.Effects[0].Op==op);
            if(result.Any(c=>c.OwnerSeat==source.OwnerSeat&&c.SkillId==source.SkillId&&c.SkillInstanceId==source.SkillInstanceId&&c.TriggerId==trigger.Id))continue;
            result.Add(new(source.OwnerSeat,source.OwnerSeat,source.SkillId,trigger.Id,hash,source.SkillInstanceId,trigger.Priority,CreateCardActionProgramContext(action,window,0,source.OwnerSeat,source.OwnerSeat,CaptureProgramTriggerFacts(_players[source.OwnerSeat],action))));
        }
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27120)]
    private bool QualifyFulue(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)=>!t.Effects.Any(e=>PeiYuanShaoComposition.Handles(e.Op))||
        (t.Effects[0].Op==SkillProgramEffectOp.OfferFulueSingleTargetOption?FulueOriginalCandidate(c,context):CanRunIssuedFulueCandidate(c,context));
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 150)]
    private int FulueDamageBonus(IDamageAttempt attack)=>!attack.IsChainPropagation&&attack is CardAttackHandle&&LifecycleCardUse(attack.ResolutionId)is{FulueEntries:not null}use&&FulueUseProof(use)?use.FulueEntries!.Count(e=>e.Option==FulueOption.Damage):0;
    [ProgramCapability]private sealed class PeiYuanShaoCapability(GameEngine e):IPeiYuanShaoHost
    {public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginFulue(f,op);}
    private SkillProgramStepOutcome BeginFulue(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);var context=f.WindowContext??throw new InvalidOperationException("Fulue requires its genuine native card window.");var c=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,0);
        if(f.Fulue is not null||f.InstructionIndex!=1||!(op==SkillProgramEffectOp.OfferFulueSingleTargetOption?FulueOriginalCandidate(c,context):CanRunIssuedFulueCandidate(c,context)))throw new InvalidOperationException("Fulue lost its exact unconsumed native candidate.");
        var use=FulueContextUse(c,context)!;var action=use.Action!;
        if(op==SkillProgramEffectOp.RequestFulueZeroDamageCounterSlash)
        {
            var entry=FulueOriginalEntry(use,f.OwnerSeat,f.SkillId,f.SkillInstanceId)!;var invoice=new FulueCounterInvoice(f.Id,use.Id,action.ActionId,entry.Source,entry.GameplayHash,entry.ActualTurn,entry.TurnOwner,entry.TargetSeat,entry.UserSeat,OtherFulueOption(entry.Option));
            ReplaceRuntimeTop(f with{Fulue=new(use.Id,action.ActionId,entry.Source,f.GameplayHash,entry.ActualTurn,entry.TurnOwner,entry.UserSeat,entry.TargetSeat,false,FulueStage.CounterRequest){Option=entry.Option},SelectedTargetSeats=Array.AsReadOnly(new[]{entry.TargetSeat})});
            AdvanceEventRulesAndQueueFact(new FulueCounterRequestedEvent(f.Id,invoice));return SkillProgramStepOutcome.Continue;
        }
        var counter=op==SkillProgramEffectOp.ResolveFulueCounterOption;var source=counter?use.FulueCounter!.Source:new CardConversionSource(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);var user=counter?use.FulueCounter!.ActorSeat:action.ActorSeat;var target=counter?use.FulueCounter!.TargetSeat:use.TargetSeats.Single();
        ReplaceRuntimeTop(f=f with{Fulue=new(use.Id,action.ActionId,source,f.GameplayHash,_turnNumber,_currentSeat,user,target,counter,FulueStage.Option)});
        if(counter){UpdateLifecycleCardUse(use.Id,u=>u with{FulueCounter=u.FulueCounter! with{Applied=true}});ExecuteFulueOption(f,use.FulueCounter!.OtherOption);}else PublishFulue(f);
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> FulueObtainChoices(long frame,int user,int target)=>_players[user].IsAlive&&_players[target].IsAlive
        ?BuildOwnedCardPaymentChoices(frame,user,target,[CardZoneKind.Hand,CardZoneKind.Equipment,CardZoneKind.Judgment],OwnedCardMoveIntent.Obtain):[];
    private bool HasFulueObtainableCard(int user,int target)=>_players[user].IsAlive&&_players[target].IsAlive&&GetHand(_players[target]).Concat(GetEquipment(target)).Concat(GetJudgment(target)).Any(c=>!IsForeignEquipmentDiscardPrevented(user,c,_cardZones.GetLocation(c.Id),OwnedCardMoveIntent.Obtain));
    private IReadOnlyList<PromptChoice> FulueChoices(ProgramSkillFrame f)
    {
        var r=f.Fulue!;var list=new List<PromptChoice>();
        if(r.Stage==FulueStage.Obtain)return FulueObtainChoices(f.Id,r.UserSeat,r.TargetSeat).Select(c=>c with{Id=new("fulue."+c.Id.Value),Parameters=new Dictionary<string,string>(c.Parameters){["program-action"]="fulue",["branch"]="obtain"}}).ToArray();
        if(r.Stage!=FulueStage.Option)return [];
        foreach(var option in Enum.GetValues<FulueOption>().Where(o=>FulueOptionAvailable(r.Source,r.ActualTurn,o)&&(o!=FulueOption.Obtain||HasFulueObtainableCard(r.UserSeat,r.TargetSeat))))list.Add(new(new($"fulue.{f.Id}.{option}"),option==FulueOption.Damage?"此牌造成伤害+1":"获得目标角色的一张牌",[],[],new Dictionary<string,string>{{"program-action","fulue"},{"branch",option.ToString()}}));
        list.Add(new(new($"fulue.{f.Id}.skip"),"不发动复掠",[],[],new Dictionary<string,string>{{"program-action","fulue"},{"branch","skip"}}));return Array.AsReadOnly(list.ToArray());
    }
    private void PublishFulue(ProgramSkillFrame f)
    {
        var r=f.Fulue!;var choices=FulueChoices(f);if(choices.Count==0){FinishFulue(f);return;}ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});var skill=_contentRegistry.GetSkill(f.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,r.UserSeat,r.Stage==FulueStage.Obtain?"复掠：获得目标角色的一张牌。":"复掠：选择本回合未执行的一项。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),[],f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=r.Stage==FulueStage.Obtain,TargetSeat=r.UserSeat,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[r.UserSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    private void ExecuteFulueOption(ProgramSkillFrame f,FulueOption option)
    {
        var r=f.Fulue!;if(!FulueOptionAvailable(r.Source,r.ActualTurn,option))throw new InvalidOperationException("Fulue cannot execute either original owner's option twice in one actual turn.");var use=LifecycleCardUse(r.UseId)!;
        var entry=new FulueUseEntry(f.Id,use.Id,r.ActionId,r.Source,r.GameplayHash,r.ActualTurn,r.TurnOwner,r.UserSeat,r.TargetSeat,option,r.Counter);
        UpdateLifecycleCardUse(use.Id,u=>u with{FulueEntries=(u.FulueEntries??[]).Append(entry).ToArray()});AdvanceEventRulesAndQueueFact(new FulueOptionExecutedEvent(entry));
        ReplaceRuntimeTop(f=f with{Fulue=r with{Option=option,Stage=option==FulueOption.Obtain?FulueStage.Obtain:FulueStage.Option}});
        if(option==FulueOption.Obtain&&HasFulueObtainableCard(r.UserSeat,r.TargetSeat))PublishFulue(f);else FinishFulue(f);
    }
    [ProgramAnswer("fulue")]private void AnswerFulue(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Fulue:{}r}f||_pendingDecision?.PlayerSeat!=r.UserSeat||!FulueChoices(f).Any(c=>AssistedChoicesEqual([c],[choice])))throw new InvalidOperationException("Fulue needs its exact original user's current published choice.");ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
        if(r.Stage==FulueStage.Option){if(choice.Parameters["branch"]=="skip")FinishFulue(f);else ExecuteFulueOption(f,Enum.Parse<FulueOption>(choice.Parameters["branch"]));return;}
        var zone=Enum.Parse<CardZoneKind>(choice.Parameters["source-zone"]);var slot=int.Parse(choice.Parameters["slot-index"]);var from=new CardLocation(zone,r.TargetSeat);var card=_cardZones.CardsAt(from)[slot];var material=new CardActionCost(card.Id,card.Kind,from);var before=_movementSequence;
        ReplaceRuntimeTop(f=f with{Fulue=r with{Stage=FulueStage.MovementChildren,Obtained=material,Before=before},PendingMovementContinuation=new(f.OwnerSeat,0,null)});MoveProgramCardsFromMultipleSources([card.Id],CardLocation.Hand(r.UserSeat),new(FulueObtainReason));
        var move=_cardMovements.Single(m=>m.Sequence>before&&m.CardId==card.Id&&m.Reason.Value==FulueObtainReason);f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{Fulue=f.Fulue! with{After=_movementSequence,MoveSequence=move.Sequence}});AdvanceEventRulesAndQueueFact(new FulueObtainPaidEvent(f.Id,r.UseId,r.ActionId,r.UserSeat,material,before,_movementSequence,move.Sequence));ContinueFulue(f.Id);
    }
    private void FinishFulue(ProgramSkillFrame f){ReplaceRuntimeTop(f=f with{Fulue=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    private void ContinueFulue(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Fulue:{Stage:FulueStage.MovementChildren}}f||f.Id!=id)return;
        if(TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id))return;FinishFulue(GetActiveProgramFrame(id));
    }
    [ProgramContinuation(ProgramContinuationKind.Resume,27120)]private bool ResumeFulue(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Fulue:{Stage:FulueStage.MovementChildren}}f||f.Id!=id)return false;ContinueFulue(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,27120)]private bool ReturnFulueMovement(ProgramSkillFrame f){if(f.Fulue?.Stage!=FulueStage.MovementChildren)return false;ContinueFulue(f.Id);return true;}
    [ProgramAiChoice(SkillProgramEffectOp.OfferFulueSingleTargetOption,SkillProgramEffectOp.ResolveFulueCounterOption)]private PromptChoice ChooseFulueAi(PendingDecision p,ProgramSkillFrame f)=>f.Fulue?.Stage==FulueStage.Obtain?p.Choices.OrderByDescending(c=>c.Cards.Count>0?GetKeepValue(GetAdvancedCard(c.Cards[0]),_players[f.Fulue.TargetSeat]):2).First():p.Choices.First(c=>c.Parameters["branch"]!="skip");
}
