using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string QinLangDrawReason="skill-program.qin-lang.xianying.draw",QinLangDiscardReason="skill-program.qin-lang.xianying.discard";
    private bool HasQinLangCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveXianyingDrawDiscard);
    private static CardConversionSource QinLangSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private static bool SameQinLangSource(CardConversionSource a,CardConversionSource b)=>a.SkillId==b.SkillId&&a.OwnerSeat==b.OwnerSeat&&a.SkillInstanceId==b.SkillInstanceId;
    private bool QinLangCountUsed(int owner,int round,int count)=>ProgramEventHistory<QinLangXianyingDiscardPaidEvent>().Any(e=>e.Source.OwnerSeat==owner&&e.RoundNumber==round&&e.Count==count);
    private bool QinLangGrantConsumed(long id)=>ProgramEventHistory<QinLangXianyingGrantConsumedEvent>().Any(e=>e.GrantFrameId==id);
    private IEnumerable<QinLangXianyingGrantedEvent> OutstandingQinLangGrants(int turn)=>ProgramEventHistory<QinLangXianyingGrantedEvent>().Where(e=>e.ActualTurn==turn&&!QinLangGrantConsumed(e.FrameId));
    private IReadOnlyList<QinLangXianyingCard> QinLangDiscardable(ProgramSkillFrame f)=>Array.AsReadOnly(GetHand(_players[f.OwnerSeat]).Concat(GetEquipment(f.OwnerSeat))
        .Where(c=>!IsForeignEquipmentDiscardPrevented(f.OwnerSeat,c,_cardZones.GetLocation(c.Id),OwnedCardMoveIntent.Discard)&&
            !IsActiveProgramSourceEquipmentCard(f.OwnerSeat,f.SkillId,f.SkillInstanceId,c)).Select(c=>new QinLangXianyingCard(c.Id,c.Kind,_cardZones.GetLocation(c.Id),QinLangName(_cardZones.GetLocation(c.Id).Zone==CardZoneKind.Hand?AdvancedEffectiveHandKind(_players[f.OwnerSeat],c):c.Kind))).ToArray());
    [ProgramCapability] private sealed class QinLangCapability(GameEngine e):IQinLangHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffect effect)=>e.BeginQinLang(f,effect.Op==SkillProgramEffectOp.ResolveXianyingTurnEndUses); }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.Collection,27170)]
    private bool CollectQinLang(SkillProgramTrigger t)=>t.Effects is not [{Op:SkillProgramEffectOp.ResolveXianyingTurnEndUses}];
    private IReadOnlyList<TurnEndingBoundaryItem> IncludeQinLangXianyingAfterTurnEndedItems(DeferredTurnEndFrame parent,IReadOnlyList<TurnEndingBoundaryItem> ordinary)
    {
        if(!HasQinLangCapability)return ordinary;
        var items=ordinary.Where(i=>i.Candidate is not{SkillId:"ol:xianying",BindingId:"turn-end-own" or "turn-end-foreign"}).ToList();
        foreach(var group in OutstandingQinLangGrants(parent.TurnNumber).Where(e=>_players[e.Source.OwnerSeat].IsAlive).GroupBy(e=>(e.Source.OwnerSeat,e.Source.SkillInstanceId,e.GameplayHash)))
        {
            var c=new ProgramTriggerCandidate(group.Key.OwnerSeat,"ol:xianying",group.Key.OwnerSeat==parent.OwnerSeat?"turn-end-own":"turn-end-foreign",group.Key.SkillInstanceId,group.Key.GameplayHash,0);
            items.Add(new(TurnEndingBoundaryItemKind.Program,c.Priority,$"program:{c.SkillId}:{c.BindingId}:{c.SkillInstanceId}",c,CaptureProgramTriggerFacts(_players[c.OwnerSeat])));
        }
        return Array.AsReadOnly(items.OrderBy(i=>(i.Candidate!.OwnerSeat-parent.OwnerSeat+_players.Count)%_players.Count).ThenByDescending(i=>i.Priority).ThenBy(i=>i.StableIdentity,StringComparer.Ordinal).ToArray());
    }
    private bool IsIssuedQinLangXianyingCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(!HasQinLangCapability||c.SkillId!="ol:xianying"||c.BindingId is not("turn-end-own"or"turn-end-foreign")||!IsValidPlayerSeat(c.OwnerSeat)||!_players[c.OwnerSeat].IsAlive||_winner!=Winner.None||
           context.Window!=SkillProgramTriggerWindow.AfterTurnEnded||context.OwnerSeat!=c.OwnerSeat||_resolutionStack.OfType<DeferredTurnEndFrame>().SingleOrDefault(p=>p.Id==context.ParentFrameId)is not{}parent||
           !IsActualAfterTurnEndedParent(parent)||AfterTurnEndedCandidate(parent)!=c||context.SourceSeat!=parent.OwnerSeat||context.TargetSeat!=parent.OwnerSeat||context.OccurrenceIndex!=c.OccurrenceIndex||
           context.Facts!=parent.AfterTurnEnded!.Items[parent.AfterTurnEnded.ItemIndex].Facts||c.BindingId!=(c.OwnerSeat==parent.OwnerSeat?"turn-end-own":"turn-end-foreign")||
           _contentRegistry.GetSkill(c.SkillId).Program is not{}program||program.GameplayHash!=c.GameplayHash)return false;
        return OutstandingQinLangGrants(parent.TurnNumber).Any(e=>e.Source.OwnerSeat==c.OwnerSeat&&e.Source.SkillInstanceId==c.SkillInstanceId&&e.GameplayHash==c.GameplayHash)||
            _resolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.WindowContext==context&&f.QinLangXianying is{}r&&r.Grants.Count>0&&r.Source==QinLangSource(f)&&r.GameplayHash==c.GameplayHash&&r.Source.SkillInstanceId==c.SkillInstanceId);
    }
    private SkillProgramStepOutcome BeginQinLang(ProgramSkillFrame input,bool ending)
    {
        var f=GetActiveProgramFrame(input.Id);
        if(f.QinLangXianying is not null||f.SkillId!="ol:xianying"||f.InstructionIndex!=1||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||f.WindowContext is not{}context||!_players[f.OwnerSeat].IsAlive)
            throw new InvalidOperationException("Xianying requires its actual preparation/damage or issued actual-turn-end instruction.");
        if(ending)
        {
            var c=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,context.OccurrenceIndex);
            if(!IsIssuedQinLangXianyingCandidate(c,context))throw new InvalidOperationException("Xianying lost its exact frozen ending candidate.");
        }
        else if(context.Window is not(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or SkillProgramTriggerWindow.AfterDamageApplied)||!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId))
            throw new InvalidOperationException("Unpaid Xianying requires its living original source and actual trigger.");
        var r=new QinLangXianyingReceipt(QinLangSource(f),f.GameplayHash,1,_turnNumber,_turnProgression.OwnerSeat,_roundNumber,ending?QinLangXianyingStage.EndChoice:QinLangXianyingStage.DrawChildren)
        {Before=_movementSequence,After=_movementSequence,Grants=ending?OutstandingQinLangGrants(_turnNumber).Where(e=>SameQinLangSource(e.Source,QinLangSource(f))&&e.GameplayHash==f.GameplayHash).ToArray():[]};
        ReplaceRuntimeTop(f=f with{QinLangXianying=r});AdvanceEventRulesAndQueueFact(new QinLangXianyingStartedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.TurnOwner,r.RoundNumber,ending));
        if(!ending)
        {
            var count=DrawCards(_players[f.OwnerSeat],2,true,new(QinLangDrawReason)).Count;
            f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{QinLangXianying=f.QinLangXianying! with{Paid=true,ActualDrawCount=count,After=_movementSequence}});
            AdvanceEventRulesAndQueueFact(new QinLangXianyingDrawnEvent(f.Id,f.OwnerSeat,count,r.Before,_movementSequence));
        }
        ContinueQinLang(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    private static CardKind QinLangName(CardKind kind)=>IsSlashCard(kind)?CardKind.Slash:kind;
    private static bool QinLangImitable(CardKind kind)=>IsSlashCard(kind)||kind is CardKind.Peach or CardKind.Alcohol||IsOrdinaryTrick(kind)&&kind!=CardKind.Nullification;
    private IReadOnlyList<PromptChoice> QinLangChoices(ProgramSkillFrame f)
    {
        var r=f.QinLangXianying!;Dictionary<string,string> Args(string branch)=>new(){["program-action"]="qin-lang-xianying",["branch"]=branch,["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture)};
        if(r.Stage==QinLangXianyingStage.EndChoice)
        {
            var choices=new List<PromptChoice>();
            foreach(var grant in r.Grants.Where(g=>!QinLangGrantConsumed(g.FrameId)))
            {
                var options=QinLangUseOptions(f,grant);
                for(var i=0;i<options.Count;i++)
                {var option=options[i];var args=Args("use");args["grant-id"]=grant.FrameId.ToString(CultureInfo.InvariantCulture);args["option-index"]=i.ToString(CultureInfo.InvariantCulture);
                 choices.Add(new(new($"xianying.{f.Id}.use.{grant.FrameId}.{i}"),$"贤膺：{option.Description}",[],option.TargetSeats,args));}
                var skip=Args("skip");skip["grant-id"]=grant.FrameId.ToString(CultureInfo.InvariantCulture);choices.Add(new(new($"xianying.{f.Id}.skip.{grant.FrameId}"),$"放弃本次【{CardCatalog.Get(grant.Name).DisplayName}】",[],[],skip));
            }
            choices.Add(new(new($"xianying.{f.Id}.finish"),"结束贤膺结算，放弃剩余用牌",[],[],Args("finish")));return Array.AsReadOnly(choices.ToArray());
        }
        if(r.Stage!=QinLangXianyingStage.DiscardChoice)return [];
        var legal=QinLangDiscardable(f);var result=new List<PromptChoice>();
        foreach(var c in r.Cards.Where(legal.Contains))
        {var selected=r.Selected.Contains(c);result.Add(new(new($"xianying.{f.Id}.card.{c.CardId}"),(selected?"取消":"选择")+$"【{GetAdvancedCard(c.CardId).DisplayName}】",[c.CardId],[],Args("card")));}
        if(!QinLangCountUsed(f.OwnerSeat,r.RoundNumber,r.Selected.Count))result.Add(new(new($"xianying.{f.Id}.pay.{r.Selected.Count}"),$"确认弃置{r.Selected.Count}张",[],[],Args("pay")));
        return Array.AsReadOnly(result.ToArray());
    }
    private void PublishQinLang(ProgramSkillFrame f)
    {
        var choices=QinLangChoices(f);if(choices.Count==0)throw new InvalidOperationException("Xianying cannot expose an empty frozen choice.");var skill=_contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,f.QinLangXianying!.Stage==QinLangXianyingStage.DiscardChoice?"贤膺：弃置任意张手牌或装备牌，本轮已使用过的张数不能重复。":"贤膺：选择独立承诺及其当前合法用法。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=true,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("qin-lang-xianying")]
    private void AnswerQinLang(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{QinLangXianying:{}r}f||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!QinLangChoices(f).Any(c=>AssistedChoicesEqual([c],[choice])))throw new InvalidOperationException("Xianying changed its private chooser, frozen payment or actual legal use.");
        AssertQinLang(f);ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});var branch=choice.Parameters["branch"];
        if(branch=="card")
        {var card=r.Cards.Single(c=>c.CardId==choice.Cards.Single());ReplaceRuntimeTop(f=f with{QinLangXianying=r with{Selected=r.Selected.Contains(card)?r.Selected.Where(c=>c!=card).ToArray():r.Selected.Append(card).ToArray()}});PublishQinLang(f);return;}
        if(branch=="pay"){PayQinLang(f);ContinueQinLang(f.Id);return;}
        if(branch=="finish")
        {foreach(var g in r.Grants.Where(g=>!QinLangGrantConsumed(g.FrameId)))AdvanceEventRulesAndQueueFact(new QinLangXianyingGrantConsumedEvent(g.FrameId,f.Id,f.OwnerSeat,r.ActualTurn,false));FinishQinLang(f);return;}
        var grant=r.Grants.Single(g=>g.FrameId==long.Parse(choice.Parameters["grant-id"],CultureInfo.InvariantCulture)&&!QinLangGrantConsumed(g.FrameId));
        if(branch=="skip"){AdvanceEventRulesAndQueueFact(new QinLangXianyingGrantConsumedEvent(grant.FrameId,f.Id,f.OwnerSeat,r.ActualTurn,false));ContinueQinLang(f.Id);return;}
        var option=QinLangUseOptions(f,grant)[int.Parse(choice.Parameters["option-index"],CultureInfo.InvariantCulture)];
        var origin=new QinLangXianyingUseOrigin(f.Id,_resolutionSequence+1,grant.FrameId,r.Source,r.GameplayHash,r.ActualTurn,grant.Name,f.OwnerSeat,option.Kind,option.TargetCardId,option.RequiredKind,string.Join(',',option.TargetSeats));
        ReplaceRuntimeTop(f=f with{QinLangXianying=r with{Stage=QinLangXianyingStage.NativeUse,Use=origin,Before=_movementSequence,After=_movementSequence}});
        AdvanceEventRulesAndQueueFact(new QinLangXianyingGrantConsumedEvent(grant.FrameId,f.Id,f.OwnerSeat,r.ActualTurn,true));AdvanceEventRulesAndQueueFact(new QinLangXianyingUseIssuedEvent(origin));BeginQinLangXianyingUse(f,origin,option.TargetSeats);
    }
    private void PayQinLang(ProgramSkillFrame f)
    {
        var r=f.QinLangXianying!;var selected=r.Selected;
        if(r.Stage!=QinLangXianyingStage.DiscardChoice||QinLangCountUsed(f.OwnerSeat,r.RoundNumber,selected.Count)||selected.Any(c=>!QinLangDiscardable(f).Contains(c)))throw new InvalidOperationException("Xianying cannot reuse a round count or substitute a paid HE entity.");
        var grain=selected.Where(c=>c.Kind==CardKind.WoodenOx&&c.From.Zone==CardZoneKind.Equipment).SelectMany(c=>GetWoodenOxGrain(_players[f.OwnerSeat]).Select(x=>new QinLangXianyingCard(x.Id,x.Kind,CardLocation.WoodenOxGrain(f.OwnerSeat)))).ToArray();
        ReplaceRuntimeTop(f=f with{QinLangXianying=r with{Stage=QinLangXianyingStage.DiscardChildren,Paid=true,Before=_movementSequence,After=_movementSequence,Auxiliaries=grain}});
        if(selected.Count>0)MoveProgramLogicalDiscard(f,f.OwnerSeat,selected.Select(c=>c.CardId).ToArray(),new(QinLangDiscardReason),(_,moves)=>
        {var current=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(current with{QinLangXianying=current.QinLangXianying! with{After=_movementSequence,PaidBatchId=_activeCardMovementBatchIds.Peek()}});
         foreach(var m in moves)AdvanceEventRulesAndQueueFact(new QinLangXianyingEntityDiscardedEvent(f.Id,m.CardId,m.CardKind,m.From,m.Sequence,selected.Single(c=>c.CardId==m.CardId).EffectiveName!.Value));});
        f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{QinLangXianying=f.QinLangXianying! with{After=_movementSequence}});
        AdvanceEventRulesAndQueueFact(new QinLangXianyingDiscardPaidEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.RoundNumber,selected.Count,f.QinLangXianying!.Before,_movementSequence));
        var names=selected.Select(c=>c.EffectiveName!.Value).Distinct().ToArray();
        if(names is[var name]&&QinLangImitable(name))AdvanceEventRulesAndQueueFact(new QinLangXianyingGrantedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.TurnOwner,selected.Count,name));
    }
    private bool DrainQinLang(long id)=>TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.Program)||TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.Program)||TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id);
    private void ContinueQinLang(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{QinLangXianying:{}r}f||f.Id!=id)return;AssertQinLang(f);
        if(r.Stage is QinLangXianyingStage.DrawChildren or QinLangXianyingStage.DiscardChildren or QinLangXianyingStage.NativeChildren)
        {
            if(DrainQinLang(id))return;f=GetActiveProgramFrame(id);r=f.QinLangXianying!;
            if(r.Stage==QinLangXianyingStage.DiscardChildren){FinishQinLang(f);return;}
            if(r.Stage==QinLangXianyingStage.DrawChildren)
            {var cards=QinLangDiscardable(f);ReplaceRuntimeTop(f=f with{QinLangXianying=r with{Stage=QinLangXianyingStage.DiscardChoice,Cards=cards}});r=f.QinLangXianying!;
             if(Enumerable.Range(0,cards.Count+1).All(n=>QinLangCountUsed(f.OwnerSeat,r.RoundNumber,n))){FinishQinLang(f);return;}}
            else {ReplaceRuntimeTop(f=f with{QinLangXianying=r with{Stage=QinLangXianyingStage.EndChoice,Use=null}});r=f.QinLangXianying!;}
        }
        if(_winner!=Winner.None||!_players[f.OwnerSeat].IsAlive){FinishQinLang(f);return;}
        if(r.Stage==QinLangXianyingStage.EndChoice&&!r.Grants.Any(g=>!QinLangGrantConsumed(g.FrameId))){FinishQinLang(f);return;}
        if(r.Stage is QinLangXianyingStage.DiscardChoice or QinLangXianyingStage.EndChoice)PublishQinLang(f);
    }
    private void FinishQinLang(ProgramSkillFrame f){ReplaceRuntimeTop(f=f with{QinLangXianying=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,27170)]private bool ResumeQinLang(long id)
    {if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{QinLangXianying:not null}f||f.Id!=id)return false;ContinueQinLang(id);return true;}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27170)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27170)]
    private bool ContinuePaidQinLang(ProgramSkillFrame f)=>f.QinLangXianying is{Paid:true}&&ValidQinLang(f)||f.QinLangXianying is{Grants.Count:>0}&&ValidQinLang(f)||f.QinLangXianying is null&&f.WindowContext is{Window:SkillProgramTriggerWindow.AfterTurnEnded}context&&
        IsIssuedQinLangXianyingCandidate(new(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,context.OccurrenceIndex),context);
}
