using System.Globalization;
using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record HappyMedicineHandLoss(int OwnerSeat,Suit EffectiveSuit,int ActualTurnOwnerSeat,long Sequence);
public sealed partial record CardMovementRecord
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public HappyMedicineHandLoss? HappyMedicineHandLoss{get;init;}
}
public sealed partial class GameEngine
{
    private HappyMedicineHandLoss? CaptureHappyHuaTuoHandLoss(Card card,CardLocation from,CardLocation to,long sequence)
    {
        if(!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GainHappyHuaTuoMedicine)||
            from is not{Zone:CardZoneKind.Hand,OwnerSeat:{}owner}||from==to||CaptureMovementTiming() is not{}timing||timing.ActualTurnOwnerSeat==owner)return null;
        var suit=EffectiveSuit(_players[owner],card);
        return suit is Suit.Heart or Suit.Diamond?new(owner,suit,timing.ActualTurnOwnerSeat,sequence):null;
    }
    private int HappyMedicine(int owner)=>_players[owner].Markers.GetValueOrDefault(PlayerMarkerKind.HappyMedicine);
    private void ChangeHappyMedicine(ProgramSkillFrame f,int delta)
    {
        var owner=_players[f.OwnerSeat];var before=HappyMedicine(owner.Seat);var after=Math.Clamp(before+delta,0,3);
        var own=owner.MarkerSourceCounts.GetValueOrDefault((PlayerMarkerKind.HappyMedicine,owner.Seat));
        SetAttributedNatureMarker(owner.Seat,owner.Seat,PlayerMarkerKind.HappyMedicine,Math.Max(0,own+after-before),f.Id);
    }
    private int HappyMedicineRedLossCount(int owner,ProgramSkillWindowContext? context)=>context?.MovementBatch is{}batch?
        batch.Movements.Where(m=>m.From==CardLocation.Hand(owner)&&m.From!=m.To&&m.HappyMedicineHandLoss is{}loss&&
            loss.OwnerSeat==owner&&loss.Sequence==m.Sequence&&loss.ActualTurnOwnerSeat!=owner&&loss.EffectiveSuit is Suit.Heart or Suit.Diamond).Select(m=>m.CardId).Distinct().Count():0;
    private bool HappyHuaDyingTarget(ProgramSkillWindowContext? context,out DyingFrame dying)
    {
        dying=null!;
        if(context is not{Window:SkillProgramTriggerWindow.DyingEntering,TargetSeat:{}victim}||!IsValidPlayerSeat(victim)||
            !_players[victim].IsAlive||!_players[victim].HasHpValue||_players[victim].Hp>0||ActiveDying is not{}active||active.VictimSeat!=victim||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId) is not{Window:SkillProgramTriggerWindow.DyingEntering,Continuation:ProgramLifecycleContinuation.ResumeDyingEntry}entry||
            entry.ResumeDyingFrameId!=active.FrameId||_resolutionStack.OfType<DyingFrame>().SingleOrDefault(d=>d.Id==active.FrameId) is not{}exact)return false;
        dying=exact;return true;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,41780)]
    private bool CanOfferHappyHuaTuo(ProgramTriggerLegalityContext gate)=>gate.SkillId switch
    {
        "happy:jishi" when gate.Trigger.Id=="medicine-rescue"=>HappyMedicine(gate.OwnerSeat)>0&&HappyHuaDyingTarget(gate.WindowContext,out _),
        "happy:jishi" when gate.Trigger.Id=="red-hand-loss"=>HappyMedicine(gate.OwnerSeat)<3&&HappyMedicineRedLossCount(gate.OwnerSeat,gate.WindowContext)>0,
        "happy:taoxian"=>gate.WindowContext?.CardUse is{EffectiveKind:CardKind.Peach}use&&use.ActorSeat!=gate.OwnerSeat,
        _=>true
    };
    [ProgramCapability]private sealed class HappyHuaTuoCapability(GameEngine engine):IHappyHuaTuoHost
    {public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>engine.ResolveHappyHuaTuo(f,op);}
    private SkillProgramStepOutcome ResolveHappyHuaTuo(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(op==SkillProgramEffectOp.InitializeHappyHuaTuoMedicine)
        {
            if(f.WindowContext is not{Window:SkillProgramTriggerWindow.GameStarting}context||!HappyHuaOriginalCandidate(f,context.Window,context.ParentFrameId))throw new InvalidOperationException("Jishi initial medicine requires the exact actual game-start candidate.");
            ChangeHappyMedicine(f,3-HappyMedicine(f.OwnerSeat));return SkillProgramStepOutcome.Continue;
        }
        if(op==SkillProgramEffectOp.GainHappyHuaTuoMedicine)
        {
            if(f.WindowContext is not{Window:SkillProgramTriggerWindow.CardsMoved,MovementBatch:{}batch}context||
                _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId) is not{}window||window.Batch.Id!=batch.Id||
                window.CandidateIndex>=window.Candidates.Count||!MountObserverCandidateMatches(f,window.Candidates[window.CandidateIndex]))throw new InvalidOperationException("Jishi medicine requires its original native red hand-loss movement window.");
            ChangeHappyMedicine(f,HappyMedicineRedLossCount(f.OwnerSeat,f.WindowContext));return SkillProgramStepOutcome.Continue;
        }
        if(op==SkillProgramEffectOp.ResolveHappyHuaTuoMedicine)
        {
            if(!HappyHuaDyingTarget(f.WindowContext,out var dying)||HappyMedicine(f.OwnerSeat)<=0||f.HappyHuaRescue is not null)
                throw new InvalidOperationException("Happy Jishi requires its actual current dying entry and an unpaid medicine.");
            var entry=_resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(w=>w.Id==f.WindowContext!.ParentFrameId);
            if(entry.CandidateIndex>=entry.Candidates.Count||!MountObserverCandidateMatches(f,entry.Candidates[entry.CandidateIndex]))
                throw new InvalidOperationException("Happy Jishi lost its unique actual dying candidate.");
            var before=HappyMedicine(f.OwnerSeat);var source=new CardConversionSource(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
            var receipt=new HappyHuaRescueReceipt(source,f.GameplayHash,entry.Id,dying.Id,dying.VictimSeat,_players[dying.VictimSeat].Hp,before,before-1);
            ReplaceRuntimeTop(f=f with{HappyHuaRescue=receipt,SelectedTargetSeats=Array.AsReadOnly(new[]{dying.VictimSeat})});
            ChangeHappyMedicine(f,-1);
            AdvanceEventRulesAndQueueFact(new HappyHuaMedicinePaidEvent(f.Id,source,f.GameplayHash,before,before-1,"rescue"));
            AdvanceEventRulesAndQueueFact(new HappyHuaRescueIssuedEvent(f.Id,entry.Id,dying.Id,dying.VictimSeat,receipt.HpBefore));
            return SkillProgramStepOutcome.Continue;
        }
        if(op!=SkillProgramEffectOp.ResolveHappyHuaTuoShenzhen||f.HappyHuaNeedle is not null||f.WindowContext is not{Window:SkillProgramTriggerWindow.TurnStartBeforeNormalFlow}needleContext)
            throw new InvalidOperationException("Happy Shenzhen requires its original actual turn-start instruction.");
        ReplaceRuntimeTop(f=f with{HappyHuaNeedle=new(new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId),f.GameplayHash,needleContext.ParentFrameId,_turnNumber,
            HappyHuaNeedleStage.AmountChoice,0,HappyMedicine(f.OwnerSeat),HappyMedicine(f.OwnerSeat))});
        PublishHappyHuaNeedle(f);return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> HappyHuaNeedleChoices(ProgramSkillFrame f)
    {
        var r=f.HappyHuaNeedle!;var choices=new List<PromptChoice>();
        PromptChoice Make(string branch,string value,string text,IReadOnlyList<int>? targets=null)=>new(new ChoiceId($"happy-needle.{f.Id}.{branch}.{value}"),text,[],targets??[],
            new Dictionary<string,string>{{"program-action","happy-hua-needle"},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"branch",branch},{"value",value}});
        if(r.Stage==HappyHuaNeedleStage.AmountChoice)
        {
            for(var amount=0;amount<=Math.Min(HappyMedicine(f.OwnerSeat),_players.Count(p=>p.IsAlive&&p.HasHpValue));amount++)
                choices.Add(Make("amount",amount.ToString(CultureInfo.InvariantCulture),amount==0?"移去零枚药，不产生效果。":$"移去{amount}枚药。"));
        }
        else if(r.Stage==HappyHuaNeedleStage.TargetChoice)
        {
            var living=CurrentTableOrder().Where(s=>_players[s].IsAlive&&_players[s].HasHpValue).ToArray();
            foreach(var targets in HappyHuaSubsets(living,r.Amount))foreach(var mode in new[]{"recover","lose"})
                choices.Add(Make("targets",mode+"-"+string.Join('-',targets),$"令{string.Join('、',targets.Select(s=>_players[s].Name))}各{(mode=="recover"?"回复":"失去")}1点体力。",targets));
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private static IEnumerable<int[]> HappyHuaSubsets(int[] seats,int count,int start=0)
    {
        if(count==0){yield return [];yield break;}
        for(var i=start;i<=seats.Length-count;i++)foreach(var tail in HappyHuaSubsets(seats,count-1,i+1))yield return new[]{seats[i]}.Concat(tail).ToArray();
    }
    private void PublishHappyHuaNeedle(ProgramSkillFrame f)
    {
        var choices=HappyHuaNeedleChoices(f);
        if(choices.Count==0){CommitHappyHuaNeedle(f,[],"zero");return;}
        ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.AwaitingResponse});
        var skill=_contentRegistry.GetSkill(f.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"【神针】选择药数及等量角色。",[],Array.AsReadOnly(choices.SelectMany(c=>c.Targets).Distinct().ToArray()),f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=false,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("happy-hua-needle")]private void AnswerHappyHuaNeedle(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{HappyHuaNeedle:{}r}f||_pendingDecision?.PlayerSeat!=f.OwnerSeat||
            !ValidHappyHuaNeedle(f)||!AssistedChoicesEqual([choice],HappyHuaNeedleChoices(f).Where(c=>c.Id==choice.Id).ToArray()))
            throw new InvalidOperationException("Happy Shenzhen requires its exact original live owner menu and medicine payment.");
        ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
        if(r.Stage==HappyHuaNeedleStage.AmountChoice)
        {
            var amount=int.Parse(choice.Parameters["value"],CultureInfo.InvariantCulture);
            if(amount==0){CommitHappyHuaNeedle(f,[],"zero");return;}
            var before=HappyMedicine(f.OwnerSeat);
            ReplaceRuntimeTop(f=f with{HappyHuaNeedle=r with{Stage=HappyHuaNeedleStage.TargetChoice,Amount=amount,Before=before,After=before-amount}});
            ChangeHappyMedicine(f,-amount);
            AdvanceEventRulesAndQueueFact(new HappyHuaMedicinePaidEvent(f.Id,r.Source,f.GameplayHash,before,before-amount,"needle"));
            PublishHappyHuaNeedle(f);return;
        }
        CommitHappyHuaNeedle(f,choice.Targets,choice.Parameters["value"].StartsWith("recover-",StringComparison.Ordinal)?"recover":"lose");
    }
    private void CommitHappyHuaNeedle(ProgramSkillFrame f,IReadOnlyList<int> targets,string mode)
    {
        var r=f.HappyHuaNeedle!;
        if(mode!="zero"&&targets.Count!=r.Amount)throw new InvalidOperationException("Paid Shenzhen chooses exactly its paid number of distinct characters.");
        ReplaceRuntimeTop(f=f with{HappyHuaNeedle=r with{Stage=HappyHuaNeedleStage.Committed},SelectedTargetSeats=Array.AsReadOnly(targets.ToArray()),
            ChoiceBindings=Array.AsReadOnly(f.ChoiceBindings.Append(new ProgramChoiceResultBinding("needle-mode",mode,f.OwnerSeat)).ToArray())});
        for(var i=0;i<targets.Count;i++)AdvanceEventRulesAndQueueFact(new HappyHuaNeedleTargetEvent(f.Id,i,targets[i],mode));
        AdvanceRuntimeProgram(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveHappyHuaTuoShenzhen)]private PromptChoice HappyHuaNeedleAi(PendingDecision p,ProgramSkillFrame f)=>
        p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("branch")=="targets"&&c.Parameters.GetValueOrDefault("value")?.StartsWith("recover-",StringComparison.Ordinal)==true&&c.Targets.Any(s=>_players[s].Hp<_players[s].MaxHp))??p.Choices[0];
}
