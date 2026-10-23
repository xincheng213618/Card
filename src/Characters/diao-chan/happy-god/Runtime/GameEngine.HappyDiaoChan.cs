using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string HappyDcAction="happy-diao-chan",HappyDcPindian="happy-huoxin-pindian",HappyDcGive="skill-program.happy:diao-chan.give",HappyDcTake="skill-program.happy:diao-chan.take",HappyDcDiscard="skill-program.happy:huoxin.cost";
    [ProgramCapability]private sealed class HappyDiaoChanHost(GameEngine e):IHappyDiaoChanHost
    {public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginHappyDc(f,op);}
    private CardConversionSource HappyDcSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private IEnumerable<(int Source,int Opponent)> HappyDcPairs(int? discarded=null)
    {
        var seats=_players.Where(p=>p.IsAlive&&GetHand(p).Any(c=>c.Id!=discarded)).Select(p=>p.Seat).ToArray();
        foreach(var source in seats)foreach(var opponent in seats)if(source!=opponent&&CanBePindianTarget(source,opponent))yield return(source,opponent);
    }
    private IReadOnlyList<PromptChoice> HappyDcCosts(ProgramSkillFrame f)=>BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,f.OwnerSeat,[CardZoneKind.Hand,CardZoneKind.Equipment],OwnedCardMoveIntent.Discard)
        .Where(c=>HappyDcPairs(HappyDcCard(c,f.OwnerSeat).Id).Any()).ToArray();
    private Card HappyDcCard(PromptChoice c,int seat)=>_cardZones.CardsAt(new(Enum.Parse<CardZoneKind>(c.Parameters["source-zone"]),seat))[int.Parse(c.Parameters["slot-index"],CultureInfo.InvariantCulture)];
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart,42220,SkillProgramEffectOp.ResolveHappyDiaoChanHuoxin)]
    private bool CanStartHappyHuoxin(CharacterState actor)=>GetHand(actor).Concat(GetEquipment(actor)).Any(card=>HappyDcPairs(card.Id).Any()&&
        !IsSelfHandCategoryDiscardForbidden(actor.Seat,card,FindOwnedCardLocation(actor,card),OwnedCardMoveIntent.Discard));
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,42220)]
    private bool CanOfferHappyMeihun(ProgramTriggerLegalityContext gate)=>gate.SkillId!=HappyDiaoChanComposition.Meihun||gate.Trigger.Id!="first-slash-target"||
        gate.WindowContext is{}context&&HappyDcFirstSlash(gate.OwnerSeat,context);
    private bool HappyDcFirstSlash(int owner,ProgramSkillWindowContext context)
    {
        if(context.CardUse is not{}use||!IsSlashCard(use.EffectiveKind))return false;
        var facts=CompleteProgramEventHistory().ToArray();var start=Array.FindLastIndex(facts,e=>e is TurnStartedEvent);
        var since=facts.Skip(start+1).ToArray();
        var first=since.OfType<TargetsConfirmedEvent>().FirstOrDefault(e=>e.TargetSeats.Contains(owner)&&since.OfType<CardUseDeclaredEvent>().Any(d=>d.ResolutionId==e.ResolutionId&&IsSlashCard(d.CardKind)));
        return first?.ResolutionId==use.ParentCardUseFrameId&&!ProgramEventHistory<HappyDiaoChanStartedEvent>().Any(e=>e.Operation==SkillProgramEffectOp.ResolveHappyDiaoChanMeihun&&e.Source.OwnerSeat==owner&&e.Source.BindingId=="first-slash-target"&&e.ParentId==context.ParentFrameId);
    }
    private SkillProgramStepOutcome BeginHappyDc(ProgramSkillFrame supplied,SkillProgramEffectOp operation)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.HappyDiaoChan is not null||f.InstructionIndex!=1||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||
            operation==SkillProgramEffectOp.ResolveHappyDiaoChanMeihun&&!HappyDcOriginalCandidate(f)||
            operation==SkillProgramEffectOp.ResolveHappyDiaoChanHuoxin&&(f.SkillId!=HappyDiaoChanComposition.Huoxin||f.ActivationId!="charm-contest"||f.TriggerId is not null))
            throw new InvalidOperationException("Happy Diao Chan requires her original native opportunity or zero-input paid activation.");
        var r=new HappyDiaoChanReceipt(HappyDcSource(f),f.GameplayHash,operation,f.WindowContext?.ParentFrameId??0,_turnNumber,_currentSeat,
            operation==SkillProgramEffectOp.ResolveHappyDiaoChanMeihun?HappyDiaoChanStage.Target:HappyDiaoChanStage.Cost);
        ReplaceRuntimeTop(f=f with{HappyDiaoChan=r});
        AdvanceEventRulesAndQueueFact(new HappyDiaoChanStartedEvent(f.Id,r.Source,r.GameplayHash,operation,r.ParentId,r.ActualTurn,r.ActualTurnOwner));
        PublishHappyDc(f);return SkillProgramStepOutcome.AwaitChild;
    }
    private PromptChoice HappyDcChoice(ProgramSkillFrame f,string mode,string text,IReadOnlyList<int>? targets=null,IReadOnlyList<int>? cards=null,string? value=null)=>
        new(new($"happy-diao.{f.Id}.{f.HappyDiaoChan!.Stage}.{f.HappyDiaoChan.NonwinnerIndex}.{mode}.{value}.{string.Join('-',targets??[])}.{string.Join('-',cards??[])}"),text,cards??[],targets??[],new Dictionary<string,string>
        {{"program-action",HappyDcAction},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"mode",mode},{"value",value??""}});
    private int HappyDcChooser(ProgramSkillFrame f)=>f.HappyDiaoChan is{Stage:HappyDiaoChanStage.Nonwinner}r?r.Nonwinners[r.NonwinnerIndex]:f.OwnerSeat;
    private IReadOnlyList<PromptChoice> HappyDcChoices(ProgramSkillFrame f)
    {
        var r=f.HappyDiaoChan!;var choices=new List<PromptChoice>();
        if(r.Stage==HappyDiaoChanStage.Target)foreach(var p in _players.Where(p=>p.IsAlive&&p.Seat!=f.OwnerSeat))choices.Add(HappyDcChoice(f,"target",$"选择{p.Name}",[p.Seat]));
        if(r.Stage is HappyDiaoChanStage.MeihunSuit or HappyDiaoChanStage.HuoxinSuit)
        {foreach(var suit in new[]{Suit.Spade,Suit.Heart,Suit.Club,Suit.Diamond})choices.Add(HappyDcChoice(f,"suit",$"声明{GetSuitDisplayName(suit)}",value:suit.ToString()));if(r.Stage==HappyDiaoChanStage.HuoxinSuit)choices.Add(HappyDcChoice(f,"decline","不声明花色"));}
        if(r.Stage==HappyDiaoChanStage.ViewHand)
        {
            var allowed=BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,r.Target,[CardZoneKind.Hand],OwnedCardMoveIntent.Obtain).Select(c=>HappyDcCard(c,r.Target).Id).ToHashSet();
            foreach(var id in r.Viewed.Where(allowed.Contains))choices.Add(HappyDcChoice(f,"view-take",$"获得【{GetAdvancedCard(id).DisplayName}】",[r.Target],[id]));
        }
        if(r.Stage==HappyDiaoChanStage.Cost)foreach(var c in HappyDcCosts(f))choices.Add(c with{Id=new($"happy-cost.{f.Id}.{c.Id.Value}"),Parameters=new Dictionary<string,string>(c.Parameters){["program-action"]=HappyDcAction,["mode"]="cost"}});
        if(r.Stage==HappyDiaoChanStage.Pair)foreach(var pair in HappyDcPairs())choices.Add(HappyDcChoice(f,"pair",$"令{_players[pair.Source].Name}与{_players[pair.Opponent].Name}拼点",[pair.Source,pair.Opponent]));
        if(r.Stage==HappyDiaoChanStage.Nonwinner)
        {choices.Add(HappyDcChoice(f,"give",$"交给貂蝉所有{GetSuitDisplayName(r.Suit)}牌"));choices.Add(HappyDcChoice(f,"charm",$"获得{GetSuitDisplayName(r.Suit)}魅惑，至自己回合结束不能使用或打出该花色牌"));}
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishHappyDc(ProgramSkillFrame f)
    {
        var choices=HappyDcChoices(f);if(choices.Count==0){FinishHappyDc(f);return;}
        ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.AwaitingResponse});
        PublishParticipantHandChoice(f,HappyDcChooser(f),choices,f.SkillId==HappyDiaoChanComposition.Meihun?"【魅魂】请选择。":"【惑心】请选择。",IsValidPlayerSeat(f.HappyDiaoChan!.Target)?f.HappyDiaoChan.Target:f.OwnerSeat);
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer(HappyDcAction)]private void AnswerHappyDc(PromptChoice answer)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{HappyDiaoChan:{}r}f||!ValidHappyDc(f)||_pendingDecision?.PlayerSeat!=HappyDcChooser(f)||
            !AssistedChoicesEqual([answer],HappyDcChoices(f).Where(c=>c.Id==answer.Id).ToArray()))throw new InvalidOperationException("Diao Chan requires the exact current choice of its real participant.");
        ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});var mode=answer.Parameters["mode"];
        if(mode=="decline"){FinishHappyDc(f);return;}
        if(r.Stage==HappyDiaoChanStage.Target){ReplaceRuntimeTop(f=f with{HappyDiaoChan=r with{Target=answer.Targets.Single(),Stage=HappyDiaoChanStage.MeihunSuit}});PublishHappyDc(f);return;}
        if(r.Stage is HappyDiaoChanStage.MeihunSuit or HappyDiaoChanStage.HuoxinSuit)
        {
            var suit=Enum.Parse<Suit>(answer.Parameters["value"]);ReplaceRuntimeTop(f=f with{HappyDiaoChan=r=r with{Suit=suit}});
            AdvanceEventRulesAndQueueFact(new HappyDiaoChanDeclaredEvent(f.Id,r.Target,suit));
            if(r.Stage==HappyDiaoChanStage.HuoxinSuit){ReplaceRuntimeTop(f=f with{HappyDiaoChan=r with{Stage=HappyDiaoChanStage.Nonwinner}});ContinueHappyDc(f.Id);return;}
            var matching=HappyDcSuitCards(r.Target,suit);
            if(matching.Length>0){GiveHappyDc(f,r.Target,matching,false);return;}
            var viewed=GetHand(_players[r.Target]).Select(c=>c.Id).ToArray();
            ReplaceRuntimeTop(f=f with{HappyDiaoChan=r with{Stage=HappyDiaoChanStage.ViewHand,Viewed=viewed}});
            AdvanceEventRulesAndQueueFact(new ProgramPrivateHandViewedEvent(f.Id,f.SkillId,f.OwnerSeat,r.Target,viewed.Length));PublishHappyDc(f);return;
        }
        if(r.Stage==HappyDiaoChanStage.ViewHand){GiveHappyDc(f,r.Target,answer.Cards.ToArray(),true);return;}
        if(r.Stage==HappyDiaoChanStage.Cost)
        {
            var card=HappyDcCard(answer,f.OwnerSeat);var from=FindOwnedCardLocation(_players[f.OwnerSeat],card);
            f=HappyDcChildren(f,HappyDiaoChanStage.CostChildren);
            ReplaceRuntimeTop(f=f with{HappyDiaoChan=f.HappyDiaoChan! with{Cost=new(card.Id,card.Kind,from),CostBefore=_movementSequence}});
            MoveProgramLogicalDiscard(f,f.OwnerSeat,[card.Id],new(HappyDcDiscard),(batch,moves)=>
            {var paid=moves.Single();var active=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(active with{HappyDiaoChan=active.HappyDiaoChan! with{CostAfter=paid.Sequence,CostBatch=batch}});AdvanceEventRulesAndQueueFact(new HappyDiaoChanCostPaidEvent(f.Id,batch,card.Id,paid.From,paid.To,paid.Sequence));});
            ContinueHappyDc(f.Id);return;
        }
        if(r.Stage==HappyDiaoChanStage.Pair)
        {
            var source=answer.Targets[0];var opponent=answer.Targets[1];var id=_resolutionSequence+1;
            ReplaceRuntimeTop(f=f with{HappyDiaoChan=r with{Stage=HappyDiaoChanStage.Pindian,PairSource=source,PairOpponent=opponent,PindianId=id,Before=_movementSequence,After=_movementSequence}});
            AdvanceEventRulesAndQueueFact(new HappyDiaoChanPindianIssuedEvent(f.Id,id,source,opponent));
            var skill=_contentRegistry.GetSkill(f.SkillId);BeginSharedPindian(f.Id,new(f.SkillId,skill.Name,skill.Name,skill.Description),source,opponent,programResultBind:HappyDcPindian);return;
        }
        if(r.Stage==HappyDiaoChanStage.Nonwinner)
        {
            var target=r.Nonwinners[r.NonwinnerIndex];AdvanceEventRulesAndQueueFact(new HappyDiaoChanNonwinnerChosenEvent(f.Id,r.NonwinnerIndex,target,r.Suit,mode=="give"));
            if(mode=="give"){GiveHappyDc(f,target,HappyDcSuitCards(target,r.Suit),false);return;}
            AdvanceEventRulesAndQueueFact(new HappyDiaoChanCharmIssuedEvent(f.Id,r.Source,r.GameplayHash,target,r.Suit,_turnNumber));SyncHappyDcCharmMarker(f.Id,target,r.Suit);
            ReplaceRuntimeTop(f=f with{HappyDiaoChan=r with{NonwinnerIndex=r.NonwinnerIndex+1}});ContinueHappyDc(f.Id);return;
        }
        throw new InvalidOperationException("Diao Chan cannot pay a completed branch twice.");
    }
    private int[] HappyDcSuitCards(int seat,Suit suit)=>GetHand(_players[seat]).Concat(GetEquipment(_players[seat])).Where(c=>EffectiveSuit(_players[seat],c)==suit).Select(c=>c.Id).ToArray();
    private void GiveHappyDc(ProgramSkillFrame f,int giver,IReadOnlyList<int> proposed,bool take)
    {
        var allowed=BuildOwnedCardPaymentChoices(f.Id,take?f.OwnerSeat:giver,giver,[CardZoneKind.Hand,CardZoneKind.Equipment],take?OwnedCardMoveIntent.Obtain:OwnedCardMoveIntent.Transfer);
        var ids=allowed.Select(c=>HappyDcCard(c,giver).Id).Where(proposed.Contains).Distinct().ToArray();
        f=HappyDcChildren(f,HappyDiaoChanStage.GiveChildren);var r=f.HappyDiaoChan!;
        ReplaceRuntimeTop(f=f with{HappyDiaoChan=r with{Target=giver,IsViewedTake=take,TransferOrdinal=r.TransferOrdinal+1,Moves=[]}});
        if(ids.Length==0||giver==f.OwnerSeat)
        {AdvanceEventRulesAndQueueFact(new HappyDiaoChanTransferIssuedEvent(f.Id,r.TransferOrdinal+1,giver,f.OwnerSeat,0,take,_movementSequence,_movementSequence));ContinueHappyDc(f.Id);return;}
        var nativeGive=take?null:NativeGiveCompletionRecords(f.Id,giver,f.OwnerSeat,ids,_movementSequence,null);
        MoveProgramCardsFromMultipleSources(ids,CardLocation.Hand(f.OwnerSeat),new(take?HappyDcTake:HappyDcGive),(batch,moves)=>
        {
            nativeGive?.Invoke(batch,moves);var active=GetActiveProgramFrame(f.Id);var receipt=active.HappyDiaoChan!;
            ReplaceRuntimeTop(active with{HappyDiaoChan=receipt with{After=_movementSequence,Moves=moves.Select(m=>new HappyDiaoChanMove(m.CardId,m.From,m.To,m.Sequence)).ToArray()}});
            AdvanceEventRulesAndQueueFact(new HappyDiaoChanTransferIssuedEvent(f.Id,receipt.TransferOrdinal,giver,f.OwnerSeat,moves.Count,take,receipt.Before,_movementSequence));
        });ContinueHappyDc(f.Id);
    }
    private ProgramSkillFrame HappyDcChildren(ProgramSkillFrame f,HappyDiaoChanStage stage)
    {f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{HappyDiaoChan=f.HappyDiaoChan! with{Stage=stage,Before=_movementSequence,After=_movementSequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});return f;}
    private bool DrainHappyDc(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginAdvancedSkillsChanged(f.Id)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id);
    private void ContinueHappyDc(long id)
    {
        while(_pendingDecision is null&&_resolutionStack.LastOrDefault() is ProgramSkillFrame{HappyDiaoChan:{}r}f&&f.Id==id)
        {
            if(r.Stage==HappyDiaoChanStage.Pindian)
            {
                var result=ProgramEventHistory<PindianResultDeterminedEvent>().Single(e=>e.FrameId==r.PindianId&&e.SkillId==f.SkillId).Result;
                var losers=new[]{result.SourceSeat,result.OpponentSeat}.Where(s=>!result.WonBy(s)).ToArray();
                ReplaceRuntimeTop(f=f with{HappyDiaoChan=r=r with{Stage=HappyDiaoChanStage.PindianChildren,Result=result,Nonwinners=losers,After=_movementSequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
                AdvanceEventRulesAndQueueFact(new HappyDiaoChanPindianReturnedEvent(f.Id,r.PindianId,result.SourceSeat,result.OpponentSeat,result.SourceRank,result.OpponentRank));
            }
            if(!ValidHappyDc(f))throw new InvalidOperationException("Diao Chan lost her original paid contest, exact movement or pending participant.");
            if(r.Stage is HappyDiaoChanStage.CostChildren or HappyDiaoChanStage.PindianChildren or HappyDiaoChanStage.GiveChildren)
            {
                if(DrainHappyDc(f))return;f=GetActiveProgramFrame(id);r=f.HappyDiaoChan!;
                ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null});
                if(r.Stage==HappyDiaoChanStage.GiveChildren)
                {if(r.Operation==SkillProgramEffectOp.ResolveHappyDiaoChanMeihun){FinishHappyDc(f);return;}ReplaceRuntimeTop(f=f with{HappyDiaoChan=r=r with{Stage=HappyDiaoChanStage.Nonwinner,NonwinnerIndex=r.NonwinnerIndex+1}});}
                else ReplaceRuntimeTop(f=f with{HappyDiaoChan=r=r with{Stage=r.Stage==HappyDiaoChanStage.CostChildren?HappyDiaoChanStage.Pair:HappyDiaoChanStage.HuoxinSuit}});
            }
            if(_winner!=Winner.None||!_players[f.OwnerSeat].IsAlive||r.Stage==HappyDiaoChanStage.Nonwinner&&r.NonwinnerIndex>=r.Nonwinners.Count){FinishHappyDc(f);return;}
            if(r.Stage==HappyDiaoChanStage.Nonwinner&&!_players[r.Nonwinners[r.NonwinnerIndex]].IsAlive){ReplaceRuntimeTop(f with{HappyDiaoChan=r with{NonwinnerIndex=r.NonwinnerIndex+1}});continue;}
            PublishHappyDc(f);return;
        }
    }
    private void FinishHappyDc(ProgramSkillFrame f)=>FinishProgramSkill(f,true);
    [ProgramContinuation(ProgramContinuationKind.Resume,42220)]private bool ResumeHappyDc(long id)
    {if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{HappyDiaoChan:not null}f||f.Id!=id)return false;ContinueHappyDc(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,42220)]private bool ReturnHappyDc(ProgramSkillFrame f)=>f.HappyDiaoChan is not null&&ResumeHappyDc(f.Id);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveHappyDiaoChanMeihun,SkillProgramEffectOp.ResolveHappyDiaoChanHuoxin)]private PromptChoice AiHappyDc(PendingDecision p,ProgramSkillFrame f)=>
        f.HappyDiaoChan!.Stage==HappyDiaoChanStage.Nonwinner?p.Choices.First(c=>c.Parameters["mode"]=="charm"):p.Choices.First();
}
