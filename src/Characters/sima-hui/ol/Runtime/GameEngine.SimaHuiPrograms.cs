using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private static readonly PlayerMarkerKind[] SimaHuiSeals=[PlayerMarkerKind.DragonSeal,PlayerMarkerKind.PhoenixSeal];
    private static bool IsSimaHuiOp(SkillProgramEffectOp op) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_sima_hui_ol_SimaHuiPrograms.IsSimaHuiOp(op);
    private CardConversionSource SimaHuiSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private SimaHuiReceipt NewSimaHuiReceipt(ProgramSkillFrame f,string stage)=>new(){Source=SimaHuiSource(f),GameplayHash=f.GameplayHash,ParentFrameId=f.WindowContext?.ParentFrameId??f.ParentFrameId??0,
        ActualTurn=f.WindowContext?.BeforeTurnPreludeProof?.UpcomingTurnNumber??_turnNumber,InstructionIndex=f.InstructionIndex,Stage=stage};
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 410)]
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
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCondition, 50)]
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
    private void FinishSimaHui(ProgramSkillFrame f){ReplaceRuntimeTop(f=f with{SimaHui=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    [ProgramCapability]
    private sealed class SimaHuiProgramCapability(GameEngine engine) : ISimaHuiProgramHost
    {public SkillProgramStepOutcome BeginSimaHuiOperation(ProgramSkillFrame frame,SkillProgramEffectOp op)=>engine.BeginSimaHuiOperation(frame,op);}
}
