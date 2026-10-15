using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string SimaHuiViewReason="program.sima-hui.chenghao.view",SimaHuiGiftReason="program.sima-hui.chenghao.give";
    private static readonly PlayerMarkerKind[] SimaHuiSeals=[PlayerMarkerKind.DragonSeal,PlayerMarkerKind.PhoenixSeal];
    private static bool IsSimaHuiOp(SkillProgramEffectOp op)=>(int)op is >=23500 and <=23505;
    private CardConversionSource SimaHuiSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private CardConversionSource SimaHuiIssuer(ProgramSkillFrame f)=>new("ol:jianjie","seals",f.OwnerSeat,f.SkillInstanceId);
    private static string SimaHuiSealSource(CardConversionSource issuer,PlayerMarkerKind marker)=>$"sima-hui:seal:{issuer.OwnerSeat}:{issuer.SkillInstanceId}:{marker}";
    private int? SimaHuiHolder(CardConversionSource issuer,PlayerMarkerKind marker)=>CompleteProgramEventHistory().OfType<SimaHuiSealMovedEvent>().LastOrDefault(e=>e.Issuer==issuer && e.Marker==marker)?.Holder;
    private int SimaHuiOwnTurns(int owner)=>CompleteProgramEventHistory().OfType<TurnStartedEvent>().Count(e=>e.ActorSeat==owner);
    private SimaHuiReceipt NewSimaHuiReceipt(ProgramSkillFrame f,string stage)=>new(){Source=SimaHuiSource(f),GameplayHash=f.GameplayHash,ParentFrameId=f.WindowContext?.ParentFrameId??f.ParentFrameId??0,
        ActualTurn=f.WindowContext?.BeforeTurnPreludeProof?.UpcomingTurnNumber??_turnNumber,InstructionIndex=f.InstructionIndex,Stage=stage};
    private bool CanOfferSimaHuiLifecycle(ProgramTriggerCandidate candidate,SkillProgramTrigger trigger,ProgramSkillWindowContext context)
    {
        if(trigger.Effects is not[{Op:var op}] || !IsSimaHuiOp(op))return true;
        var owner=candidate.OwnerSeat;
        if(op==SkillProgramEffectOp.AssignOrderedDragonPhoenixOnFirstActualTurn)return context.BeforeTurnPreludeProof is{}p && p.UpcomingOwnerSeat==owner && ValidBeiMiHuBeforeTurnPrelude(p) && SimaHuiOwnTurns(owner)==0 &&
            !CompleteProgramEventHistory().OfType<SimaHuiSealMovedEvent>().Any(e=>e.Issuer.OwnerSeat==owner) && _players.Count(p=>p.IsAlive && p.Seat!=owner)>=2;
        if(op==SkillProgramEffectOp.TransferDeadDragonPhoenixSeal)return context.Window==SkillProgramTriggerWindow.CharacterDied && context.TargetSeat is{}victim && IsValidPlayerSeat(victim) && !_players[victim].IsAlive &&
            _resolutionStack.OfType<ProgramKillTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId) is{}window && window.VictimSeat==victim &&
            SimaHuiSeals.Any(m=>SimaHuiHolder(new("ol:jianjie","seals",owner,candidate.SkillInstanceId),m)==victim) && _players.Any(p=>p.IsAlive && p.Seat!=victim);
        return true;
    }
    private bool CanActivateSimaHui(int owner,SkillProgramActivation activation)
    {
        var features=ProgramInstructionResolver.Default.Features(activation);
        if(features.HasOperation(SkillProgramEffectOp.TransferOwnedDragonOrPhoenixSeal))return SimaHuiOwnTurns(owner)>1 && SimaHuiSeals.Any(m=>_players.Any(p=>p.MarkerSourceCounts.GetValueOrDefault((m,owner))>0));
        if(features.HasOperation(SkillProgramEffectOp.ConsumeDragonPhoenixForNativeYeyan))return SimaHuiSeals.All(m=>_players[owner].Markers.GetValueOrDefault(m)>0);
        return true;
    }
    private SkillProgramStepOutcome BeginSimaHuiOperation(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);if(f.SimaHui is not null || f.InstructionIndex!=1 || !IsSimaHuiOp(op))throw new InvalidOperationException("Sima Hui requires its exact unpaid leading instruction.");
        if(op==SkillProgramEffectOp.PreventDamageWithoutSealsAndArmor)
        {if(!CanPreventSimaHuiDamage(f.OwnerSeat,GetProgramTrigger(f),f.WindowContext!))throw new InvalidOperationException("Yinshi lost its actual damage or equipment qualification.");PreventProgramCurrentDamage(f);return SkillProgramStepOutcome.Continue;}
        if(op==SkillProgramEffectOp.ConsumeDragonPhoenixForNativeYeyan)return PaySimaHuiYeyanSeals(f);
        if(op==SkillProgramEffectOp.DistributeTopCardsAfterElementalChainOrigin)return BeginSimaHuiDistribution(f);
        var initial=op==SkillProgramEffectOp.AssignOrderedDragonPhoenixOnFirstActualTurn;
        if(f.TriggerId is not null && !CanOfferSimaHuiLifecycle(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,0),GetProgramTrigger(f),f.WindowContext!))throw new InvalidOperationException("Jianjie lost its native turn/death eligibility.");
        if(!initial && f.TriggerId is null && !CanActivateSimaHui(f.OwnerSeat,ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).Activation!))throw new InvalidOperationException("Jianjie cannot transfer in its first actual turn.");
        var stage=initial?"initial-dragon":"transfer-kind";var r=NewSimaHuiReceipt(f,stage) with{Candidates=_players.Where(p=>p.IsAlive && (!initial || p.Seat!=f.OwnerSeat)).Select(p=>p.Seat).ToArray()};
        ReplaceRuntimeTop(f=f with{SimaHui=r});AdvanceEventRulesAndQueueFact(new SimaHuiChoiceStartedEvent(f.Id,r.Source,r.GameplayHash,r.ParentFrameId,r.ActualTurn,r.Stage,r.Candidates));PublishSimaHuiChoice(f);return SkillProgramStepOutcome.AwaitChoice;
    }
    private void SetSimaHuiSeal(ProgramSkillFrame f,CardConversionSource issuer,PlayerMarkerKind marker,int? from,int? to)
    {
        if(SimaHuiHolder(issuer,marker)!=from || to==from || to is{}recipient && !_players[recipient].IsAlive)throw new InvalidOperationException("Jianjie seal transfer lost its exact issuer or recipient.");
        void Change(int seat,int delta)
        {
            var p=_players[seat];var key=(marker,issuer.OwnerSeat);var attributed=p.MarkerSourceCounts.GetValueOrDefault(key);var total=p.Markers.GetValueOrDefault(marker);
            if(attributed+delta<0 || total+delta<0)throw new InvalidOperationException("Jianjie cannot remove another source's seal.");
            if(attributed+delta==0)p.MarkerSourceCounts.Remove(key);else p.MarkerSourceCounts[key]=attributed+delta;
            if(total+delta==0)p.Markers.Remove(marker);else p.Markers[marker]=total+delta;
            AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(f.Id,seat,marker,delta,total+delta,issuer.OwnerSeat,"program.sima-hui.seal"));
        }
        if(from is{}old)Change(old,-1);if(to is{}next)Change(next,1);
        var sourceId=SimaHuiSealSource(issuer,marker);foreach(var p in _players)foreach(var grant in p.SkillGrants.Grants.Where(g=>g.SourceId==sourceId).ToArray())p.SkillGrants.RemoveGrant(grant.GrantId);
        if(to is{}holder){var id=marker==PlayerMarkerKind.DragonSeal?"ol:huoji-jianjie":"ol:lianhuan-jianjie";var grantId=sourceId+":"+id;_players[holder].SkillGrants.Grant(new(grantId,id,grantId,sourceId));}
        AdvanceEventRulesAndQueueFact(new SimaHuiSealMovedEvent(f.Id,issuer,f.GameplayHash,f.SimaHui!.ActualTurn,marker,from,to,sourceId));RefreshSimaHuiCombinedGrants();
    }
    private void RefreshSimaHuiCombinedGrants()
    {
        foreach(var p in _players)
        {
            var source=$"sima-hui:combined:{p.Seat}";var id=source+":ol:yeyan-jianjie";var both=SimaHuiSeals.All(m=>p.Markers.GetValueOrDefault(m)>0);
            if(both)p.SkillGrants.Grant(new(id,"ol:yeyan-jianjie",id,source));else p.SkillGrants.RemoveGrant(id);
        }
    }
    private IReadOnlyList<PromptChoice> SimaHuiChoices(ProgramSkillFrame f)
    {
        var r=f.SimaHui!;var result=new List<PromptChoice>();
        void Add(string value,string label,IReadOnlyList<int>?cards=null,IReadOnlyList<int>?targets=null)=>result.Add(new(new($"sima-hui.{f.Id}.{r.Stage}.{r.Cursor}.{value}"),label,cards??[],targets??[],new Dictionary<string,string>{["program-action"]="sima-hui",["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture),["value"]=value}));
        if(r.Stage=="transfer-kind")
        {
            foreach(var marker in SimaHuiSeals)if(SimaHuiHolder(SimaHuiIssuer(f),marker) is{}holder && (f.WindowContext?.Window!=SkillProgramTriggerWindow.CharacterDied || holder==f.WindowContext.TargetSeat))Add(marker.ToString(),"转移"+PlayerMarkerCatalog.GetDisplayName(marker));
            Add("finish","结束转移");
        }
        else if(r.Stage=="distribute")
        {
            var card=r.OriginalCards[r.Cursor];foreach(var p in _players.Where(p=>p.IsAlive))Add(p.Seat.ToString(CultureInfo.InvariantCulture),"将此牌交给"+p.Name,[card],[p.Seat]);
        }
        else foreach(var seat in r.Candidates.Where(s=>_players[s].IsAlive && !r.SelectedSeats.Contains(s) && s!=r.PreviousHolder))Add(seat.ToString(CultureInfo.InvariantCulture),"选择"+_players[seat].Name,targets:[seat]);
        return Array.AsReadOnly(result.ToArray());
    }
    private void PublishSimaHuiChoice(ProgramSkillFrame f)
    {
        var choices=SimaHuiChoices(f);if(choices.Count==0){FinishSimaHui(f);return;}var skill=_contentRegistry.GetSkill(f.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"请选择。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=f.SimaHui!.Stage=="distribute",Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("sima-hui")]
    private void ResolveSimaHuiChoice(PromptChoice selected)
    {
        var f=_resolutionStack.LastOrDefault() as ProgramSkillFrame??throw new InvalidOperationException("Missing Sima Hui choice frame.");AssertSimaHui(f);
        if(_pendingDecision?.PlayerSeat!=f.OwnerSeat || !AssistedChoicesEqual([selected],SimaHuiChoices(f).Where(c=>c.Id==selected.Id).ToArray()))throw new InvalidOperationException("Sima Hui choice lost its exact current entity/recipient.");
        ClearPendingDecision();var r=f.SimaHui!;var value=selected.Parameters["value"];
        if(value=="finish"){FinishSimaHui(f);return;}
        if(r.Stage=="transfer-kind")
        {var marker=Enum.Parse<PlayerMarkerKind>(value);ReplaceRuntimeTop(f=f with{SimaHui=r with{Stage="transfer-target",Kind=value,PreviousHolder=SimaHuiHolder(SimaHuiIssuer(f),marker),Candidates=_players.Where(p=>p.IsAlive).Select(p=>p.Seat).ToArray()}});PublishSimaHuiChoice(f);return;}
        if(r.Stage=="distribute"){IssueSimaHuiGift(f,int.Parse(value,CultureInfo.InvariantCulture));return;}
        var target=selected.Targets.Single();var kind=r.Stage=="initial-dragon"?PlayerMarkerKind.DragonSeal:r.Stage=="initial-phoenix"?PlayerMarkerKind.PhoenixSeal:Enum.Parse<PlayerMarkerKind>(r.Kind);
        var nextStage=r.Stage=="initial-dragon"?"initial-phoenix":r.Stage=="initial-phoenix"?"complete":f.TriggerId is null?"complete":"transfer-kind";
        ReplaceRuntimeTop(f=f with{SimaHui=r with{Stage="seal-children",Kind=nextStage,ChosenHolder=target,SelectedSeats=r.Stage=="initial-dragon"?[target]:r.SelectedSeats},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        SetSimaHuiSeal(f,SimaHuiIssuer(f),kind,r.PreviousHolder,target);AdvanceRuntimeProgram(f.Id);
    }
    private SkillProgramStepOutcome BeginSimaHuiDistribution(ProgramSkillFrame f)
    {
        if(!TrySimaHuiChainOrigin(f.OwnerSeat,GetProgramTrigger(f),f.WindowContext!,out var proof))throw new InvalidOperationException("Chenghao requires actual applied elemental chain-origin damage.");
        var r=NewSimaHuiReceipt(f,"view-children") with{ChainOrigin=proof,MovementBefore=_movementSequence,MovementAfter=_movementSequence};ReplaceRuntimeTop(f=f with{SimaHui=r,PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        var cards=new List<int>();for(var i=0;i<=proof.LivingChainedCount;i++)if(DrawOneToProcessing(new(SimaHuiViewReason)) is{}card)cards.Add(card.Id);else break;
        ReplaceRuntimeTop(f=f with{SimaHui=r with{OriginalCards=cards,MovementAfter=_movementSequence}});AdvanceEventRulesAndQueueFact(new SimaHuiTopViewedEvent(f.Id,r.Source,r.GameplayHash,proof,f.SimaHui!.OriginalCards,r.MovementBefore,_movementSequence));AdvanceRuntimeProgram(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    private void IssueSimaHuiGift(ProgramSkillFrame f,int recipient)
    {
        var r=f.SimaHui!;var id=r.OriginalCards[r.Cursor];if(!_players[recipient].IsAlive || _cardZones.GetLocation(id)!=CardLocation.Processing)throw new InvalidOperationException("Chenghao lost its original privately viewed card.");
        ReplaceRuntimeTop(f=f with{SimaHui=r with{Stage="gift-children",ChosenHolder=recipient,MovementBefore=_movementSequence,MovementAfter=_movementSequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        MoveCard(_cardZones.CardsAt(CardLocation.Processing).Single(c=>c.Id==id),CardLocation.Processing,CardLocation.Hand(recipient),new(SimaHuiGiftReason));
        ReplaceRuntimeTop(f=f with{SimaHui=f.SimaHui! with{MovementAfter=_movementSequence}});AdvanceEventRulesAndQueueFact(new SimaHuiCardDistributedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.Cursor,id,recipient,f.SimaHui!.MovementBefore,_movementSequence));AdvanceRuntimeProgram(f.Id);
    }
    private SkillProgramStepOutcome PaySimaHuiYeyanSeals(ProgramSkillFrame f)
    {
        if(!SimaHuiSeals.All(m=>_players[f.OwnerSeat].Markers.GetValueOrDefault(m)>0))throw new InvalidOperationException("Jianjie Yeyan needs both actual seals.");
        if(f.SelectedCardIds.Count is not(0 or 4) || f.SelectedCardIds.Any(id=>_cardZones.GetLocation(id)!=CardLocation.Hand(f.OwnerSeat)))throw new InvalidOperationException("Jianjie Yeyan requires its original actual hand materials.");
        var r=NewSimaHuiReceipt(f,"yeyan-paid") with{OriginalCards=f.SelectedCardIds,MovementBefore=_movementSequence,MovementAfter=_movementSequence};ReplaceRuntimeTop(f=f with{SimaHui=r});var ids=new List<string>();
        foreach(var marker in SimaHuiSeals)
        foreach(var latest in CompleteProgramEventHistory().OfType<SimaHuiSealMovedEvent>().Where(e=>e.Marker==marker).GroupBy(e=>e.Issuer).Select(g=>g.Last()).Where(e=>e.Holder==f.OwnerSeat).ToArray())
        {ids.Add(latest.GrantSourceId);SetSimaHuiSeal(f,latest.Issuer,marker,f.OwnerSeat,null);}
        if(SimaHuiSeals.Any(m=>_players[f.OwnerSeat].Markers.GetValueOrDefault(m)>0))throw new InvalidOperationException("Jianjie Yeyan refuses an unissued seal.");
        AdvanceEventRulesAndQueueFact(new SimaHuiYeyanSealsPaidEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,Array.AsReadOnly(ids.ToArray()),r.OriginalCards));return SkillProgramStepOutcome.Continue;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume,23500)]
    private bool ResumeSimaHui(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{SimaHui:{}r}f || f.Id!=id || r.Stage=="yeyan-paid")return false;AssertSimaHui(f);
        if(r.Stage is "initial-dragon" or "initial-phoenix" or "transfer-kind" or "transfer-target" or "distribute"){if(_pendingDecision is null)PublishSimaHuiChoice(f);return true;}
        if(TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id))return true;
        f=GetActiveProgramFrame(id);r=f.SimaHui!;
        if(r.Stage=="return-children"){FinishSimaHui(f);return true;}
        if(_winner!=Winner.None || !_players[f.OwnerSeat].IsAlive){if(ReturnSimaHuiUnassignedCards(f))return true;FinishSimaHui(f);return true;}
        if(r.Stage=="seal-children")
        {ReplaceRuntimeTop(f=f with{SimaHui=r with{Stage=r.Kind,Kind="",PreviousHolder=null},PendingMovementContinuation=null});if(r.Kind=="complete")FinishSimaHui(f);else PublishSimaHuiChoice(f);return true;}
        var cursor=r.Cursor+(r.Stage=="gift-children"?1:0);while(cursor<r.OriginalCards.Count && _cardZones.GetLocation(r.OriginalCards[cursor])!=CardLocation.Processing)cursor++;
        ReplaceRuntimeTop(f=f with{SimaHui=r with{Stage="distribute",Cursor=cursor,ChosenHolder=null},PendingMovementContinuation=null});if(cursor==r.OriginalCards.Count)FinishSimaHui(f);else PublishSimaHuiChoice(f);return true;
    }
    private bool ReturnSimaHuiUnassignedCards(ProgramSkillFrame f)
    {
        var cards=f.SimaHui!.OriginalCards.Where(id=>_cardZones.GetLocation(id)==CardLocation.Processing).Select(id=>_cardZones.CardsAt(CardLocation.Processing).Single(c=>c.Id==id)).ToArray();
        if(cards.Length==0)return false;var r=f.SimaHui!;var before=_movementSequence;ReplaceRuntimeTop(f=f with{SimaHui=r with{Stage="return-children",MovementBefore=before,MovementAfter=before},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        MoveCards(cards,CardLocation.Processing,CardLocation.DrawPile,new("program.sima-hui.chenghao.return"));_cardZones.PlaceDrawPileCardsAtTop(cards.Select(c=>c.Id).ToArray());
        ReplaceRuntimeTop(f=f with{SimaHui=f.SimaHui! with{MovementAfter=_movementSequence}});AdvanceEventRulesAndQueueFact(new SimaHuiUnassignedReturnedEvent(f.Id,r.Source,r.GameplayHash,Array.AsReadOnly(cards.Select(c=>c.Id).ToArray()),before,_movementSequence));AdvanceRuntimeProgram(f.Id);return true;
    }
    private void FinishSimaHui(ProgramSkillFrame f){ReplaceRuntimeTop(f=f with{SimaHui=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    private sealed partial class ProgramSkillHost:ISimaHuiProgramHost
    {public SkillProgramStepOutcome BeginSimaHuiOperation(ProgramSkillFrame frame,SkillProgramEffectOp op)=>engine.BeginSimaHuiOperation(frame,op);}
}
