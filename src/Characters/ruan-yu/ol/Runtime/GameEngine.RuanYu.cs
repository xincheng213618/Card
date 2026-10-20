namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string RuanYuExchangeReason="skill-program.ruan-yu.xingzuo.exchange",MiaoxianDrawReason="skill-program.ruan-yu.miaoxian.draw";
    private bool HasRuanYuCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveXingzuoPrivateBottomExchange)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMiaoxianUniqueRedUseDraw);
    private static CardConversionSource RuanYuSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_ruan_yu_ol_RuanYu.RuanYuSource(f);
    [ProgramCapability]private sealed class RuanYuCapability(GameEngine e):IRuanYuHost {public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginRuanYu(f,op);}
    private SkillProgramStepOutcome BeginRuanYu(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);var context=f.WindowContext??throw new InvalidOperationException("Ruan Yu requires a genuine native trigger.");
        if(f.RuanYu is not null||f.InstructionIndex!=1||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||!_players[f.OwnerSeat].IsAlive)throw new InvalidOperationException("Ruan Yu lost his exact starting cursor.");
        var candidate=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,context.OccurrenceIndex);
        if(!CanOfferRuanYu(candidate,GetProgramTrigger(f),context))throw new InvalidOperationException("Ruan Yu lost his original source, actual phase or prepayment true-use qualification.");
        var stage=op==SkillProgramEffectOp.ResolveXingzuoPrivateBottomExchange?RuanYuStage.Hand:op==SkillProgramEffectOp.ResolveXingzuoIssuedEndingExchange?RuanYuStage.EndingTarget:RuanYuStage.RedChildren;
        var r=new RuanYuReceipt(RuanYuSource(f),f.GameplayHash,op,context.ParentFrameId,_turnNumber,_turnProgression.OwnerSeat,stage,f.OwnerSeat);
        if(stage==RuanYuStage.Hand)r=r with{ViewedBottom=_cardZones.CardsAt(CardLocation.DrawPile).Take(3).Select(c=>c.Id).ToArray()};
        if(stage==RuanYuStage.EndingTarget)r=r with{DebtId=CurrentRuanYuEndingDebt(candidate,context)!.DebtId,Paid=true};
        if(stage==RuanYuStage.RedChildren)r=r with{ActionId=context.CardUse!.CardActionId,Paid=true};
        ReplaceRuntimeTop(f=f with{RuanYu=r});AdvanceEventRulesAndQueueFact(new RuanYuStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.ParentId,r.ActualTurn,r.TurnOwner));
        if(r.DebtId is{}debt)AdvanceEventRulesAndQueueFact(new XingzuoEndingConsumedEvent(debt,f.Id,r.ParentId));
        if(stage==RuanYuStage.RedChildren)
        {
            AdvanceEventRulesAndQueueFact(new MiaoxianRedDrawIssuedEvent(f.Id,r.ActionId!.Value,r.Source,r.ActualTurn));PrepareRuanYuMovement(ref f,RuanYuStage.RedChildren,[]);
            var count=DrawCards(_players[f.OwnerSeat],1,true,new(MiaoxianDrawReason)).Count;CompleteRuanYuMovement(f.Id,count);
        }
        else PublishRuanYu(f);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private int[] RuanYuEndingTargets()=>_players.Where(p=>p.IsAlive&&GetHand(p).Count>0).Select(p=>p.Seat).ToArray();
    private PromptChoice RuanYuChoice(ProgramSkillFrame f,string branch,string label,int? card=null,int? seat=null)=>new(new($"ruan-yu.{f.Id}.{branch}.{card??seat??-1}"),label,card is{}id?[id]:[],seat is{}s?[s]:[],new Dictionary<string,string>{{"program-action","ruan-yu"},{"branch",branch}});
    private int[] RuanYuDraftBottom(RuanYuReceipt r)=>r.SelectedHand.Concat(r.ViewedBottom.Where(id=>!r.SelectedBottom.Contains(id))).ToArray();
    private IReadOnlyList<PromptChoice> RuanYuChoices(ProgramSkillFrame f)
    {
        var r=f.RuanYu!;var choices=new List<PromptChoice>();
        if(r.Stage==RuanYuStage.Hand)
        {
            if(r.SelectedHand.Count<r.ViewedBottom.Count)choices.AddRange(GetHand(_players[f.OwnerSeat]).Where(c=>!r.SelectedHand.Contains(c.Id)).Select(c=>RuanYuChoice(f,"hand","换入【"+c.DisplayName+"】",c.Id)));
            choices.Add(RuanYuChoice(f,"done","选齐手牌，确定等量底牌"));choices.Add(RuanYuChoice(f,"cancel","取消兴作"));
        }
        if(r.Stage==RuanYuStage.Bottom)choices.AddRange(r.ViewedBottom.Where(id=>!r.SelectedBottom.Contains(id)).Select(id=>RuanYuChoice(f,"bottom","取得【"+GetAdvancedCard(id).DisplayName+"】",id)));
        if(r.Stage==RuanYuStage.Order)choices.AddRange(RuanYuDraftBottom(r).Where(id=>!r.OrderedBottom.Contains(id)).Select(id=>RuanYuChoice(f,"order","依次置于底牌栏【"+GetAdvancedCard(id).DisplayName+"】",id)));
        if(r.Stage==RuanYuStage.EndingTarget)choices.AddRange(RuanYuEndingTargets().Select(s=>RuanYuChoice(f,"target",_players[s].Name,seat:s)));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishRuanYu(ProgramSkillFrame f)
    {
        if(!_players[f.OwnerSeat].IsAlive||_winner!=Winner.None){FinishRuanYu(f);return;}
        var choices=RuanYuChoices(f);if(choices.Count==0){if(f.RuanYu!.Stage==RuanYuStage.Order)PayRuanYuFront(f);else FinishRuanYu(f);return;}
        ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});var skill=_contentRegistry.GetSkill(f.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"兴作：选择真实原手牌、底牌及最终底牌次序。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=true,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("ruan-yu")]private void AnswerRuanYu(PromptChoice supplied)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{RuanYu:{}r}f||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!RuanYuChoices(f).Any(c=>AssistedChoicesEqual([c],[supplied])))throw new InvalidOperationException("Xingzuo requires the exact private current entity choice.");
        AssertRuanYu(f);ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
        if(!_players[f.OwnerSeat].IsAlive||_winner!=Winner.None){FinishRuanYu(f);return;}var branch=supplied.Parameters["branch"];
        if(branch=="cancel"){FinishRuanYu(f);return;}
        if(branch=="hand")r=r with{SelectedHand=r.SelectedHand.Append(supplied.Cards.Single()).ToArray()};
        else if(branch=="done")r=r with{Stage=r.SelectedHand.Count==0?RuanYuStage.Order:RuanYuStage.Bottom};
        else if(branch=="bottom"){var ids=r.SelectedBottom.Append(supplied.Cards.Single()).ToArray();r=r with{SelectedBottom=ids,Stage=ids.Length==r.SelectedHand.Count?RuanYuStage.Order:RuanYuStage.Bottom};}
        else if(branch=="order"){r=r with{OrderedBottom=r.OrderedBottom.Append(supplied.Cards.Single()).ToArray()};}
        else if(branch=="target"){PayRuanYuEnding(f,supplied.Targets.Single());return;}
        else throw new InvalidOperationException("Unknown Xingzuo choice.");
        ReplaceRuntimeTop(f=f with{RuanYu=r});if(r.Stage==RuanYuStage.Order&&r.OrderedBottom.Count==r.ViewedBottom.Count)PayRuanYuFront(f);else PublishRuanYu(f);
    }
    private void PrepareRuanYuMovement(ref ProgramSkillFrame f,RuanYuStage stage,IReadOnlyList<RuanYuEntity> entities)
    {
        foreach(var p in _players)_observedSkillGrantRevisions.TryAdd(p.Seat,p.SkillGrants.Revision);var r=f.RuanYu!;
        ReplaceRuntimeTop(f=f with{RuanYu=r with{Paid=true,Stage=stage,Step=r.Step+1,Before=_movementSequence,After=_movementSequence,ActualCount=0,Entities=entities},PendingMovementContinuation=new(r.Recipient,0,null)});
    }
    private void RecordRuanYuMovement(ProgramSkillFrame f,long batch,IReadOnlyList<CardMovementRecord> records)
    {foreach(var m in records)AdvanceEventRulesAndQueueFact(new RuanYuEntityMovedEvent(f.Id,f.RuanYu!.Step,batch,m.CardId,m.CardKind,m.From,m.To,m.Sequence));}
    private void CompleteRuanYuMovement(long id,int count)
    {var f=GetActiveProgramFrame(id);var r=f.RuanYu!;ReplaceRuntimeTop(f=f with{RuanYu=r with{After=_movementSequence,ActualCount=count}});AdvanceEventRulesAndQueueFact(new RuanYuMovementPaidEvent(id,r.Step,r.Stage,r.Before,_movementSequence,count));ContinueRuanYu(id);}
    private void MoveRuanYuExchange(ProgramSkillFrame f,IReadOnlyList<RuanYuEntity> entities,IReadOnlyList<int> desired)
    {
        var r=f.RuanYu!;var old=_cardZones.CardsAt(CardLocation.DrawPile).Select(c=>c.Id).ToArray();ReplaceRuntimeTop(f=f with{RuanYu=r with{DeckBefore=old,DesiredDeck=desired}});PrepareRuanYuMovement(ref f,r.Stage,entities);
        void Reorder(){var now=_cardZones.CardsAt(CardLocation.DrawPile).Reverse().Select(c=>c.Id).ToArray();_cardZones.ReorderDrawPileTop(now,desired.Reverse().ToArray(),[]);}
        if(entities.Count>0)MoveProgramCardsFromMultipleSources(entities.Select(c=>c.CardId).ToArray(),CardLocation.DrawPile,new(RuanYuExchangeReason),(batch,records)=>{RecordRuanYuMovement(f,batch,records);Reorder();},(_,from)=>from==CardLocation.DrawPile?CardLocation.Hand(r.Recipient):CardLocation.DrawPile);else Reorder();
        for(var i=0;i<(r.Stage==RuanYuStage.FrontChildren?r.OrderedBottom.Count:r.SelectedHand.Count);i++)AdvanceEventRulesAndQueueFact(new XingzuoBottomOrderedEvent(f.Id,i,desired[i],r.Stage==RuanYuStage.EndingChildren));
        CompleteRuanYuMovement(f.Id,entities.Count);
    }
    private void PayRuanYuFront(ProgramSkillFrame f)
    {
        var r=f.RuanYu!;var deck=_cardZones.CardsAt(CardLocation.DrawPile).Select(c=>c.Id).ToArray();
        if(!r.ViewedBottom.SequenceEqual(deck.Take(r.ViewedBottom.Count))||r.SelectedHand.Count!=r.SelectedBottom.Count||!r.SelectedHand.All(id=>_cardZones.GetLocation(id)==CardLocation.Hand(f.OwnerSeat))||!r.SelectedBottom.All(r.ViewedBottom.Contains)||r.OrderedBottom.Count!=r.ViewedBottom.Count||!r.OrderedBottom.Order().SequenceEqual(RuanYuDraftBottom(r).Order()))throw new InvalidOperationException("Xingzuo cannot replace or reorder different original physical sets.");
        var entities=r.SelectedHand.Select(id=>new RuanYuEntity(id,GetAdvancedCard(id).Kind,CardLocation.Hand(f.OwnerSeat))).Concat(r.SelectedBottom.Select(id=>new RuanYuEntity(id,GetAdvancedCard(id).Kind,CardLocation.DrawPile))).ToArray();
        ReplaceRuntimeTop(f=f with{RuanYu=r with{Stage=RuanYuStage.FrontChildren}});AdvanceEventRulesAndQueueFact(new XingzuoEndingIssuedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn));MoveRuanYuExchange(f,entities,r.OrderedBottom.Concat(deck.Skip(r.ViewedBottom.Count)).ToArray());
    }
    private void PayRuanYuEnding(ProgramSkillFrame f,int recipient)
    {
        var r=f.RuanYu!;if(!RuanYuEndingTargets().Contains(recipient))throw new InvalidOperationException("Xingzuo Ending requires a genuinely living nonempty-Hand participant.");
        var hand=GetHand(_players[recipient]).ToArray();var bottom=_cardZones.CardsAt(CardLocation.DrawPile).Take(3).ToArray();var deck=_cardZones.CardsAt(CardLocation.DrawPile).Select(c=>c.Id).ToArray();var randomized=hand.Select(c=>c.Id).ToArray();
        for(var i=randomized.Length-1;i>0;i--){var j=_random.Next(i+1);(randomized[i],randomized[j])=(randomized[j],randomized[i]);}
        var entities=hand.Select(c=>new RuanYuEntity(c.Id,c.Kind,CardLocation.Hand(recipient))).Concat(bottom.Select(c=>new RuanYuEntity(c.Id,c.Kind,CardLocation.DrawPile))).ToArray();
        ReplaceRuntimeTop(f=f with{RuanYu=r with{Recipient=recipient,Stage=RuanYuStage.EndingChildren,SelectedHand=hand.Select(c=>c.Id).ToArray(),ViewedBottom=bottom.Select(c=>c.Id).ToArray(),OrderedBottom=randomized}});
        MoveRuanYuExchange(f,entities,randomized.Concat(deck.Skip(bottom.Length)).ToArray());
    }
    private bool DrainRuanYu(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,f.RuanYu!.Stage==RuanYuStage.LoseHpChildren?PostEventContinuation.Program:PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueRuanYu(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{RuanYu:{}r}f||f.Id!=id)return;AssertRuanYu(f);
        if(r.Stage is RuanYuStage.Hand or RuanYuStage.Bottom or RuanYuStage.Order or RuanYuStage.EndingTarget){if(_pendingDecision is null)PublishRuanYu(f);return;}
        if(r.Stage==RuanYuStage.LoseHpIssuing)
        {
            _=ExecuteParticipantReserve(new(SkillProgramEffectOp.LoseHpUnclamped,SkillProgramEffectTarget.Owner,1,new(SkillProgramConditionKind.Always,0,[])),f);
            if(_resolutionStack.LastOrDefault()?.Id!=id)return;f=GetActiveProgramFrame(id);r=f.RuanYu!;if(r.Stage==RuanYuStage.LoseHpIssuing)return;
        }
        if(DrainRuanYu(f))return;f=GetActiveProgramFrame(id);r=f.RuanYu!;ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null});
        if(r.Stage==RuanYuStage.EndingChildren&&r.SelectedHand.Count>3&&_players[f.OwnerSeat].IsAlive&&_winner==Winner.None)
        {
            ReplaceRuntimeTop(f=f with{RuanYu=r with{Stage=RuanYuStage.LoseHpIssuing}});ContinueRuanYu(id);return;
        }
        FinishRuanYu(f);
    }
    private void FinishRuanYu(ProgramSkillFrame f){AdvanceEventRulesAndQueueFact(new RuanYuCompletedEvent(f.Id));ReplaceRuntimeTop(f=f with{RuanYu=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,27430)]private bool ResumeRuanYu(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{RuanYu:not null}f||f.Id!=id)return false;ContinueRuanYu(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,27430)]private bool ReturnRuanYuMovement(ProgramSkillFrame f){if(f.RuanYu is null)return false;ContinueRuanYu(f.Id);return true;}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27430)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27430)]private bool ContinuePaidRuanYu(ProgramSkillFrame f)
    {
        if(f.RuanYu is{Paid:true})return ValidRuanYu(f);
        if(f.RuanYu is not null||f.TriggerId is null||f.WindowContext is not{}context||f.InstructionIndex is not(0 or 1)||!ProgramEventHistory<ProgramBindingStartedEvent>().Any(e=>e.FrameId==f.Id&&e.SkillId==f.SkillId&&e.BindingId==f.TriggerId&&e.SkillInstanceId==f.SkillInstanceId))return false;
        var c=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,context.OccurrenceIndex);return IsIssuedRuanYuXingzuoEndingCandidate(c,context)||IsIssuedRuanYuMiaoxianRedCandidate(c,context);
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent,27430)]private void CaptureRuanYuHpLoss(ProgramSkillHpLostEvent paid)
    {if(_resolutionStack.LastOrDefault()is ProgramSkillFrame{RuanYu:{Stage:RuanYuStage.LoseHpIssuing}}f&&paid.FrameId==f.Id&&paid.SkillId==f.SkillId&&paid.TargetSeat==f.OwnerSeat&&paid.Amount==1){ReplaceRuntimeTop(f with{RuanYu=f.RuanYu! with{Stage=RuanYuStage.LoseHpChildren}});AdvanceEventRulesAndQueueFact(new XingzuoHpLossPaidEvent(f.Id,paid.RemainingHp));}}
    [ProgramAiChoice(SkillProgramEffectOp.ResolveXingzuoPrivateBottomExchange,SkillProgramEffectOp.ResolveXingzuoIssuedEndingExchange)]private PromptChoice ChooseRuanYu(PendingDecision p,ProgramSkillFrame f)
    {
        var r=f.RuanYu!;if(r.Stage==RuanYuStage.Hand){var c=p.Choices.Where(c=>c.Parameters["branch"]=="hand").OrderBy(c=>CardCatalog.Get(GetAdvancedCard(c.Cards.Single()).Kind).HandKeepValue).FirstOrDefault();var good=r.ViewedBottom.Select(id=>CardCatalog.Get(GetAdvancedCard(id).Kind).HandKeepValue).OrderDescending().Skip(r.SelectedHand.Count).FirstOrDefault();return c is not null&&CardCatalog.Get(GetAdvancedCard(c.Cards.Single()).Kind).HandKeepValue<good?c:p.Choices.First(c=>c.Parameters["branch"]=="done");}
        if(r.Stage==RuanYuStage.Bottom)return p.Choices.OrderByDescending(c=>CardCatalog.Get(GetAdvancedCard(c.Cards.Single()).Kind).HandKeepValue).First();
        if(r.Stage==RuanYuStage.Order)return p.Choices.OrderBy(c=>Array.IndexOf(RuanYuDraftBottom(r),c.Cards.Single())).First();
        return p.Choices.OrderByDescending(c=>GetHand(_players[c.Targets.Single()]).Count).ThenBy(c=>c.Targets.Single()).First();
    }
    [ProgramViewContributor(ProgramViewContribution.PrivateCards, 60)]
    private IReadOnlyList<CardSnapshot> RuanYuPrivatelyViewedBottomCards(int viewerSeat)=>_resolutionStack.LastOrDefault()is ProgramSkillFrame{RuanYu:{Stage:RuanYuStage.Hand or RuanYuStage.Bottom or RuanYuStage.Order}r}f&&f.OwnerSeat==viewerSeat&&_pendingDecision is{IsPrivate:true,PlayerSeat:var actor}&&actor==viewerSeat?Array.AsReadOnly(r.ViewedBottom.Concat(r.OrderedBottom).Distinct().Select(id=>ToSnapshot(GetAdvancedCard(id))).ToArray()):[];
}
