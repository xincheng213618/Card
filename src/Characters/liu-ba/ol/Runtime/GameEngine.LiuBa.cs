namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string TongduGiftReason="skill-program.liu-ba.tongdu.gift",TongduReturnReason="skill-program.liu-ba.tongdu.return",LiuBaExchangeReason="skill-program.liu-ba.zhubi.exchange";
    private bool HasLiuBaCapability=>_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.RecastZhubiAndMarkActualDraw)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.IssueTongduHandGift);
    private static CardConversionSource LiuBaSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    [ProgramCapability]private sealed class LiuBaCapability(GameEngine e):ILiuBaHost,ILiuBaSelectionQuery
    {public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginLiuBa(f,op);public int MaximumHp(int ownerSeat)=>e._players[ownerSeat].MaxHp;}
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCondition,27390,SkillProgramEffectOp.RecastZhubiAndMarkActualDraw)]
    private bool AllowsLiuBaRecast(ProgramActivationLegalityContext c)=>_programPhaseUses.GetValueOrDefault((c.OwnerSeat,c.SkillId,c.Activation.UsageGroup))<c.Owner.MaxHp&&_players.Any(p=>p.IsAlive&&LiuBaRecastEntities(p.Seat).Length>0);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27390)]
    private bool CanOfferLiuBa(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)
    {
        if(t.Effects is not[{ }e]||!LiuBaComposition.Handles(e.Op))return true;
        return e.Op switch {SkillProgramEffectOp.IssueTongduHandGift=>context.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow&&c.OwnerSeat==_currentSeat&&_players.Any(p=>p.IsAlive&&GetHand(p).Count>0),SkillProgramEffectOp.ReturnTongduOriginalCardAtPlayEnd=>IsIssuedTongduCandidate(c,context),SkillProgramEffectOp.ExchangeZhubiCoinsAtEnding=>IsIssuedLiuBaCoinEndingCandidate(c,context),_=>false};
    }
    private LiuBaEntity[] LiuBaRecastEntities(int seat)=>new[]{CardLocation.Hand(seat),CardLocation.Equipment(seat)}.SelectMany(l=>_cardZones.CardsAt(l).Select(c=>new LiuBaEntity(c.Id,c.Kind,l))).OrderBy(c=>c.CardId).ToArray();
    private LiuBaEntity[] LiuBaOwnedEntities(int seat)=>new[]{CardLocation.Hand(seat),CardLocation.Equipment(seat),CardLocation.Judgment(seat),CardLocation.WoodenOxGrain(seat)}.SelectMany(l=>_cardZones.CardsAt(l).Where(c=>!(l.Zone==CardZoneKind.Equipment&&c.IsGeneralWeapon)&&!IsForeignEquipmentDiscardPrevented(seat,c,l,OwnedCardMoveIntent.Transfer)).Select(c=>new LiuBaEntity(c.Id,c.Kind,l))).OrderBy(c=>c.CardId).ToArray();
    private LiuBaEntity[] LiuBaCoins(int seat)=>LiuBaOwnedEntities(seat).Where(e=>HasLiuBaCoinEntity(e.CardId)).ToArray();
    private SkillProgramStepOutcome BeginLiuBa(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);var context=f.WindowContext;
        if(f.LiuBa is not null||f.InstructionIndex!=1||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||!_players[f.OwnerSeat].IsAlive||_winner!=Winner.None)throw new InvalidOperationException("Liu Ba requires the original living native binding.");
        var issued=op is SkillProgramEffectOp.ReturnTongduOriginalCardAtPlayEnd or SkillProgramEffectOp.ExchangeZhubiCoinsAtEnding;
        if(op==SkillProgramEffectOp.RecastZhubiAndMarkActualDraw)
        {if(f.TriggerId is not null||f.ActivationId!="recast-coin"||context is not null||_phase!=TurnPhase.Play||f.OwnerSeat!=_currentSeat||_programPhaseUses.GetValueOrDefault((f.OwnerSeat,f.SkillId,ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).Activation!.UsageGroup))>_players[f.OwnerSeat].MaxHp)throw new InvalidOperationException("Zhubi lost its current maximum-HP quota and real Play.");}
        else if(context is null||!CanOfferLiuBa(new(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,context.OccurrenceIndex),GetProgramTrigger(f),context))throw new InvalidOperationException("Liu Ba lost the exact native preparation, PlayEnding debt or holder Ending entitlement.");
        if(!issued&&!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId))throw new InvalidOperationException("Liu Ba cannot start an unpaid operation after source loss.");
        var stage=op switch{SkillProgramEffectOp.IssueTongduHandGift=>LiuBaStage.GiftTarget,SkillProgramEffectOp.RecastZhubiAndMarkActualDraw=>LiuBaStage.RecastTarget,SkillProgramEffectOp.ReturnTongduOriginalCardAtPlayEnd=>LiuBaStage.ReturnChildren,_=>LiuBaStage.Coins};
        var r=new LiuBaReceipt(LiuBaSource(f),f.GameplayHash,op,context?.ParentFrameId??0,_turnNumber,_currentSeat,_cardUseDebitPhaseInstanceId,stage){Recipient=f.OwnerSeat};
        ReplaceRuntimeTop(f=f with{LiuBa=r});AdvanceEventRulesAndQueueFact(new LiuBaStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.ParentId,r.ActualTurn,r.TurnOwner,r.PhaseInstance));
        if(stage==LiuBaStage.ReturnChildren){PayTongduReturn(f);return SkillProgramStepOutcome.AwaitChild;}
        if(stage==LiuBaStage.Coins)
        {var viewed=_cardZones.CardsAt(CardLocation.DrawPile).Take(5).Select(c=>c.Id).ToArray();ReplaceRuntimeTop(f=f with{LiuBa=r with{ViewedBottom=viewed}});AdvanceEventRulesAndQueueFact(new LiuBaEndingViewedEvent(f.Id,r.ParentId,f.OwnerSeat,r.ActualTurn,viewed.Length));}
        PublishLiuBa(f);return SkillProgramStepOutcome.AwaitChild;
    }
    private int LiuBaChooser(ProgramSkillFrame f)=>f.LiuBa!.Stage is LiuBaStage.GiftCard or LiuBaStage.RecastCard?f.LiuBa.Recipient:f.OwnerSeat;
    private PromptChoice LiuBaChoice(ProgramSkillFrame f,string branch,string label,int? card=null,int? seat=null)=>new(new($"liu-ba.{f.Id}.{branch}.{card??seat??-1}"),label,card is{}id?[id]:[],seat is{}s?[s]:[],new Dictionary<string,string>{{"program-action","liu-ba"},{"branch",branch}});
    private IReadOnlyList<PromptChoice> LiuBaChoices(ProgramSkillFrame f)
    {
        var r=f.LiuBa!;var list=new List<PromptChoice>();
        if(r.Stage is LiuBaStage.GiftTarget or LiuBaStage.RecastTarget)
        {foreach(var p in _players.Where(p=>p.IsAlive&&(r.Stage==LiuBaStage.GiftTarget?GetHand(p).Count>0:LiuBaRecastEntities(p.Seat).Length>0)))list.Add(LiuBaChoice(f,"target",p.Name,seat:p.Seat));}
        else if(r.Stage==LiuBaStage.GiftCard)
        {foreach(var card in GetHand(_players[r.Recipient]).OrderBy(c=>c.Id))list.Add(LiuBaChoice(f,"card",card.DisplayName,card.Id));}
        else if(r.Stage==LiuBaStage.RecastCard)
        {foreach(var card in LiuBaRecastEntities(r.Recipient))list.Add(LiuBaChoice(f,"card",GetAdvancedCard(card.CardId).DisplayName,card.CardId));}
        else if(r.Stage==LiuBaStage.Coins)
        {
            if(r.Coins.Count<r.ViewedBottom.Count)foreach(var coin in LiuBaCoins(f.OwnerSeat).Where(c=>!r.Coins.Contains(c.CardId)))list.Add(LiuBaChoice(f,"coin",$"选择币【{GetAdvancedCard(coin.CardId).DisplayName}】",coin.CardId));
            list.Add(LiuBaChoice(f,"done",r.Coins.Count==0?"不交换":"确认这些币"));
        }
        else if(r.Stage==LiuBaStage.Bottom)
            foreach(var id in r.ViewedBottom.Where(id=>!r.SelectedBottom.Contains(id)))list.Add(LiuBaChoice(f,"bottom",$"换取底牌【{GetAdvancedCard(id).DisplayName}】",id));
        return Array.AsReadOnly(list.ToArray());
    }
    private void PublishLiuBa(ProgramSkillFrame f)
    {
        var choices=LiuBaChoices(f);if(choices.Count==0){FinishLiuBa(f);return;}var who=LiuBaChooser(f);var s=_contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});_pendingDecision=new(DecisionKind.ProgramTrigger,who,s.Name+"：选择本次原实体。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=true,TargetSeat=who,Choices=choices,SkillPrompt=new(f.SkillId,s.Name,s.Name,s.Description)};
        _status=_players[who].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("liu-ba")]private void AnswerLiuBa(PromptChoice supplied)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{LiuBa:{}r}f||_pendingDecision?.PlayerSeat!=LiuBaChooser(f)||!LiuBaChoices(f).Any(c=>AssistedChoicesEqual([c],[supplied])))throw new InvalidOperationException("Liu Ba requires the exact current participant's private answer.");
        AssertLiuBa(f);ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
        if(!_players[f.OwnerSeat].IsAlive||_winner!=Winner.None||(r.Op is SkillProgramEffectOp.IssueTongduHandGift or SkillProgramEffectOp.RecastZhubiAndMarkActualDraw)&&!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)){FinishLiuBa(f);return;}
        var branch=supplied.Parameters["branch"];
        if(branch=="target")
        {var seat=supplied.Targets.Single();ReplaceRuntimeTop(f=f with{LiuBa=r with{Recipient=seat,Stage=r.Stage==LiuBaStage.GiftTarget?LiuBaStage.GiftCard:LiuBaStage.RecastCard}});PublishLiuBa(f);return;}
        if(branch=="card")
        {var id=supplied.Cards.Single();var from=_cardZones.GetLocation(id);var entity=new LiuBaEntity(id,GetAdvancedCard(id).Kind,from);if(r.Stage==LiuBaStage.GiftCard)PayTongduGift(f,entity);else PayLiuBaRecast(f,entity);return;}
        if(!r.ViewedBottom.SequenceEqual(_cardZones.CardsAt(CardLocation.DrawPile).Take(r.ViewedBottom.Count).Select(c=>c.Id)))throw new InvalidOperationException("The frozen Liu Ba bottom view is no longer current.");
        if(branch=="coin")
        {var id=supplied.Cards.Single();var material=LiuBaCoins(f.OwnerSeat).Single(c=>c.CardId==id);ReplaceRuntimeTop(f=f with{LiuBa=r with{Coins=r.Coins.Append(id).ToArray(),Entities=r.Entities.Append(material).ToArray()}});PublishLiuBa(f);return;}
        if(branch=="done")
        {if(r.Coins.Count==0){FinishLiuBa(f);return;}ReplaceRuntimeTop(f=f with{LiuBa=r with{Stage=LiuBaStage.Bottom}});PublishLiuBa(f);return;}
        if(branch=="bottom")
        {var selected=r.SelectedBottom.Append(supplied.Cards.Single()).ToArray();ReplaceRuntimeTop(f=f with{LiuBa=r with{SelectedBottom=selected}});if(selected.Length==r.Coins.Count)PayLiuBaExchange(f);else PublishLiuBa(f);return;}
        throw new InvalidOperationException("Unsupported Liu Ba private selection.");
    }
    private void PrepareLiuBaMovement(ref ProgramSkillFrame f,LiuBaStage stage,IReadOnlyList<LiuBaEntity> entities)
    {
        foreach(var p in _players)_observedSkillGrantRevisions.TryAdd(p.Seat,p.SkillGrants.Revision);
        var r=f.LiuBa!;var grain=entities.Where(e=>e.Kind==CardKind.WoodenOx&&e.From.Zone==CardZoneKind.Equipment).SelectMany(e=>_cardZones.CardsAt(CardLocation.WoodenOxGrain(e.From.OwnerSeat!.Value))).Select(c=>c.Id).ToArray();
        ReplaceRuntimeTop(f=f with{LiuBa=r with{Paid=true,Stage=stage,Entities=entities,Grain=grain,Before=_movementSequence,After=_movementSequence,Step=r.Step+1,BatchId=null,ActualCount=0},PendingMovementContinuation=new(r.Recipient,0,null)});
    }
    private void RecordLiuBaMovement(ProgramSkillFrame f,long batch,IReadOnlyList<CardMovementRecord> records)
    {
        var current=GetActiveProgramFrame(f.Id);var r=current.LiuBa!;ReplaceRuntimeTop(current with{LiuBa=r with{BatchId=batch}});
        foreach(var m in records)AdvanceEventRulesAndQueueFact(new LiuBaEntityMovedEvent(f.Id,r.Step,batch,m.CardId,m.CardKind,m.From,m.To,m.Sequence));
    }
    private void CompleteLiuBaMovement(ProgramSkillFrame f,int count)
    {f=GetActiveProgramFrame(f.Id);var r=f.LiuBa!;ReplaceRuntimeTop(f with{LiuBa=r with{After=_movementSequence,ActualCount=count}});AdvanceEventRulesAndQueueFact(new LiuBaMovementIssuedEvent(f.Id,r.Step,r.Stage,r.Before,_movementSequence,count));ContinueLiuBa(f.Id);}
    private void PayTongduGift(ProgramSkillFrame f,LiuBaEntity card)
    {
        var r=f.LiuBa!;var entry=CurrentHandEntry(r.Recipient,card.CardId)??throw new InvalidOperationException("Tongdu requires the selected original Hand entry.");PrepareLiuBaMovement(ref f,LiuBaStage.GiftChildren,[card]);
        if(r.Recipient==f.OwnerSeat)AdvanceEventRulesAndQueueFact(new TongduGiftIssuedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.TurnOwner,r.Recipient,card.CardId,card.Kind,entry,null));
        else MoveProgramCardsFromMultipleSources([card.CardId],CardLocation.Hand(f.OwnerSeat),new(TongduGiftReason),(batch,records)=>
        {RecordLiuBaMovement(f,batch,records);AdvanceEventRulesAndQueueFact(new TongduGiftIssuedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.TurnOwner,r.Recipient,card.CardId,card.Kind,entry,records.Single().Sequence));});
        CompleteLiuBaMovement(f,r.Recipient==f.OwnerSeat?0:1);
    }
    private void PayTongduReturn(ProgramSkillFrame f)
    {
        var r=f.LiuBa!;var c=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,f.WindowContext!.OccurrenceIndex);
        var debt=CurrentTongduDebt(c,f.WindowContext!)??throw new InvalidOperationException("Tongdu lost its original issued entity debt.");var material=LiuBaOwnedEntities(f.OwnerSeat).Where(e=>e.CardId==debt.CardId).ToArray();
        ReplaceRuntimeTop(f=f with{LiuBa=r with{DebtId=debt.FrameId}});AdvanceEventRulesAndQueueFact(new TongduDebtConsumedEvent(debt.FrameId,f.Id,r.ParentId,r.ActualTurn,r.PhaseInstance));PrepareLiuBaMovement(ref f,LiuBaStage.ReturnChildren,material);
        if(material.Length!=0)MoveProgramCardsFromMultipleSources([debt.CardId],CardLocation.DrawPile,new(TongduReturnReason),(batch,records)=>RecordLiuBaMovement(f,batch,records));CompleteLiuBaMovement(f,material.Length);
    }
    private void PayLiuBaRecast(ProgramSkillFrame f,LiuBaEntity material)
    {
        f=BeginFrameNativeRecastInvocation(f,f.LiuBa!.Recipient,[material.CardId]);
        PrepareLiuBaMovement(ref f,LiuBaStage.RecastChildren,[material]);MoveProgramCardsFromMultipleSources([material.CardId],CardLocation.DiscardPile,CardMoveReasons.RecastDiscard,(batch,records)=>RecordLiuBaMovement(f,batch,records));CompleteLiuBaMovement(f,1);
    }
    private void PayLiuBaExchange(ProgramSkillFrame f)
    {
        var r=f.LiuBa!;var live=LiuBaCoins(f.OwnerSeat);if(r.Coins.Count==0||r.Coins.Count!=r.SelectedBottom.Count||r.Coins.Distinct().Count()!=r.Coins.Count||r.SelectedBottom.Distinct().Count()!=r.SelectedBottom.Count||r.Entities.Any(e=>!live.Contains(e))||!r.ViewedBottom.SequenceEqual(_cardZones.CardsAt(CardLocation.DrawPile).Take(r.ViewedBottom.Count).Select(c=>c.Id)))throw new InvalidOperationException("Liu Ba exchange requires both exact frozen original sets.");
        var deck=_cardZones.CardsAt(CardLocation.DrawPile).Select(c=>c.Id).ToArray();var mapping=r.SelectedBottom.Select((id,i)=>(id,coin:r.Coins[i])).ToDictionary(x=>x.id,x=>x.coin);var desired=deck.Select(id=>mapping.GetValueOrDefault(id,id)).ToArray();
        var entities=r.Entities.Concat(r.SelectedBottom.Select(id=>new LiuBaEntity(id,GetAdvancedCard(id).Kind,CardLocation.DrawPile))).ToArray();PrepareLiuBaMovement(ref f,LiuBaStage.ExchangeChildren,entities);
        MoveProgramCardsFromMultipleSources(entities.Select(e=>e.CardId).ToArray(),CardLocation.DrawPile,new(LiuBaExchangeReason),(batch,records)=>
        {RecordLiuBaMovement(f,batch,records);var now=_cardZones.CardsAt(CardLocation.DrawPile).Reverse().Select(c=>c.Id).ToArray();_cardZones.ReorderDrawPileTop(now,desired.Reverse().ToArray(),[]);},(card,from)=>from==CardLocation.DrawPile?CardLocation.Hand(f.OwnerSeat):CardLocation.DrawPile);
        CompleteLiuBaMovement(f,entities.Length);
    }
    private bool DrainLiuBa(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueLiuBa(long id)
    {
        while(_resolutionStack.LastOrDefault()is ProgramSkillFrame{LiuBa:{}r}f&&f.Id==id)
        {
            AssertLiuBa(f);if(!r.Paid){if(_pendingDecision is null)PublishLiuBa(f);return;}if(DrainLiuBa(f))return;f=GetActiveProgramFrame(id);r=f.LiuBa!;ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null});
            if(r.Stage==LiuBaStage.RecastChildren)
            {
                PrepareLiuBaMovement(ref f,LiuBaStage.DrawChildren,[]);var drawn=_players[r.Recipient].IsAlive&&_winner==Winner.None?DrawCards(_players[r.Recipient],1,true,CardMoveReasons.RecastDraw):[];
                f=GetActiveProgramFrame(id);var draw=f.LiuBa!;
                foreach(var card in drawn)
                {var m=_cardMovements.Single(m=>m.Sequence>draw.Before&&m.CardId==card&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(r.Recipient)&&m.Reason==CardMoveReasons.RecastDraw);if(!HasLiuBaCoinEntity(card))AdvanceEventRulesAndQueueFact(new LiuBaCoinMintedEvent(f.Id,draw.Source,draw.GameplayHash,r.Recipient,card,m.Sequence));}
                var paid=ProgramEventHistory<LiuBaEntityMovedEvent>().Single(e=>e.FrameId==id&&e.Step==1);AdvanceEventRulesAndQueueFact(new CardRecastEvent(r.Recipient,paid.CardId,paid.Kind,drawn.Count));f=CompleteFrameNativeRecastInvocation(GetActiveProgramFrame(id),drawn.Count);CompleteLiuBaMovement(f,drawn.Count);return;
            }
            FinishLiuBa(f);return;
        }
    }
    private void FinishLiuBa(ProgramSkillFrame f){AdvanceEventRulesAndQueueFact(new LiuBaCompletedEvent(f.Id,f.LiuBa!.Op,f.LiuBa.Step));ReplaceRuntimeTop(f=f with{LiuBa=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,27390)]private bool ResumeLiuBa(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{LiuBa:not null}f||f.Id!=id)return false;ContinueLiuBa(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,27390)]private bool ReturnLiuBaMovement(ProgramSkillFrame f){if(f.LiuBa is null)return false;ContinueLiuBa(f.Id);return true;}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27390)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27390)]
    private bool ContinuePaidLiuBa(ProgramSkillFrame f)
    {
        if(f.LiuBa is{Paid:true}){AssertLiuBa(f);return true;}
        if(f.TriggerId is null||f.WindowContext is not{}c||!ProgramEventHistory<ProgramBindingStartedEvent>().Any(e=>e.FrameId==f.Id&&e.SkillInstanceId==f.SkillInstanceId&&e.BindingId==f.TriggerId))return false;
        var candidate=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,c.OccurrenceIndex);return IsIssuedTongduCandidate(candidate,c)||IsIssuedLiuBaCoinEndingCandidate(candidate,c);
    }
    [ProgramAiChoice(SkillProgramEffectOp.IssueTongduHandGift,SkillProgramEffectOp.RecastZhubiAndMarkActualDraw,SkillProgramEffectOp.ExchangeZhubiCoinsAtEnding)]
    private PromptChoice ChooseLiuBa(PendingDecision p,ProgramSkillFrame f)
    {
        var r=f.LiuBa!;
        if(r.Stage==LiuBaStage.Coins)
        {var coin=p.Choices.Where(c=>c.Parameters["branch"]=="coin").OrderBy(c=>CardCatalog.Get(GetAdvancedCard(c.Cards.Single()).Kind).HandKeepValue).FirstOrDefault();var best=r.ViewedBottom.Select(id=>CardCatalog.Get(GetAdvancedCard(id).Kind).HandKeepValue).OrderDescending().Skip(r.Coins.Count).FirstOrDefault();return coin is not null&&best>CardCatalog.Get(GetAdvancedCard(coin.Cards.Single()).Kind).HandKeepValue?coin:p.Choices.First(c=>c.Parameters["branch"]=="done");}
        if(r.Stage==LiuBaStage.Bottom)return p.Choices.OrderByDescending(c=>CardCatalog.Get(GetAdvancedCard(c.Cards.Single()).Kind).HandKeepValue).ThenBy(c=>c.Cards.Single()).First();
        return p.Choices.OrderByDescending(c=>c.Targets is[var seat]?_aiBrains[f.OwnerSeat].ScoreProgramTarget(CreateSnapshot(f.OwnerSeat),seat,new SkillProgramAiHint(1,0,0,0,0,0,false,false)):c.Cards is[var id]?-CardCatalog.Get(GetAdvancedCard(id).Kind).HandKeepValue:0).ThenBy(c=>c.Id.Value,StringComparer.Ordinal).First();
    }
    [ProgramViewContributor(ProgramViewContribution.PrivateCards, 50)]
    private IReadOnlyList<CardSnapshot> LiuBaPrivatelyViewedBottomCards(int viewerSeat)=>_resolutionStack.LastOrDefault()is ProgramSkillFrame{LiuBa:{Stage:LiuBaStage.Coins or LiuBaStage.Bottom}r}f&&f.OwnerSeat==viewerSeat&&_pendingDecision is{IsPrivate:true,PlayerSeat:var actor}&&actor==viewerSeat?r.ViewedBottom.Select(id=>ToSnapshot(GetAdvancedCard(id))).ToArray():[];
}
