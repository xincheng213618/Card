using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string YanRouDiscardReason="skill-program.yan-rou.choutao.discard",YanRouDrawReason="skill-program.yan-rou.xiangshu.draw";
    private CardConversionSource YanRouSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    [ProgramCapability]private sealed class YanRouCapability(GameEngine e):IYanRouHost
    {public SkillProgramStepOutcome Execute(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginYanRou(f,op);}
    private bool YanRouSpent(int owner,string skill,string hash)=>ProgramEventHistory<YanRouXiangshuSpentEvent>().Any(e=>e.Source.OwnerSeat==owner&&e.Source.SkillId==skill&&e.GameplayHash==hash);
    private int YanRouTurnDamage(int owner)=>Math.Min(5,EventsSinceLastBoundary(e=>e is TurnStartedEvent).OfType<DamageAppliedEvent>().Where(e=>!e.SourceLess&&e.SourceSeat==owner&&e.Amount>0).Sum(e=>e.Amount));
    private bool YanRouParent(ProgramTriggerCandidate candidate,ProgramSkillWindowContext c)
    {
        if(c.OwnerSeat!=candidate.OwnerSeat)return false;
        var parent=_resolutionStack.SingleOrDefault(f=>f.Id==c.ParentFrameId);
        if(parent is ProgramCardTriggerWindowFrame w)return c.Window==SkillProgramTriggerWindow.CardUseTargetsFinalized&&GetCardActionWindow(w)==c.Window&&w.Action.Type==CardActionType.Use&&IsSlashCard(w.Action.EffectiveKind)&&
            c.CardUse is{}card&&card.CardActionId==w.Action.ActionId&&w.CandidateIndex>=0&&w.CandidateIndex<w.Candidates.Count&&ToSharedCandidate(w.Candidates[w.CandidateIndex])==candidate&&
            _resolutionStack.OfType<CardUseFrame>().Any(use=>use.Id==card.ParentCardUseFrameId&&use.Action?.ActionId==card.CardActionId&&IsSlashCard(use.CardKind));
        return parent is TurnEndingBoundaryFrame end&&c.Window==SkillProgramTriggerWindow.TurnEnding&&end.OwnerSeat==candidate.OwnerSeat&&end.OwnerSeat==_currentSeat&&end.TurnNumber==_turnNumber&&
            c.SourceSeat==end.OwnerSeat&&c.TargetSeat==end.OwnerSeat&&end.ItemIndex>=0&&end.ItemIndex<end.Items.Count&&end.Items[end.ItemIndex].Candidate==candidate&&c.OccurrenceIndex==candidate.OccurrenceIndex;
    }
    private bool YanRouCanDiscard(int chooser,int payer,Card card,CardLocation from)=>from.OwnerSeat==payer&&from.Zone is(CardZoneKind.Hand or CardZoneKind.Equipment)&&
        !IsForeignEquipmentDiscardPrevented(chooser,card,from,OwnedCardMoveIntent.Discard)&&!IsSelfHandCategoryDiscardForbidden(chooser,card,from,OwnedCardMoveIntent.Discard);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,26800)]
    private bool CanOfferYanRou(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)
    {
        if(t.Effects is not [var effect]||!YanRouComposition.IsOperation(effect.Op))return true;
        if(!YanRouParent(c,context))return false;
        if(effect.Op==SkillProgramEffectOp.RecoverAndDrawByTurnDamage)return !YanRouSpent(c.OwnerSeat,c.SkillId,c.GameplayHash)&&_players.Any(p=>p.IsAlive&&IsRuleWounded(p));
        var card=context.CardUse!;var use=_resolutionStack.OfType<CardUseFrame>().Single(u=>u.Id==card.ParentCardUseFrameId);
        var payer=use.SourceSeat;
        return _players[payer].IsAlive&&(t.OwnerRelation==SkillProgramCardActionOwnerRelation.Actor?c.OwnerSeat==payer:context.TargetSeat==c.OwnerSeat&&use.Action!.EffectiveDesignatedTargetSeats.Contains(c.OwnerSeat))&&
            GetHand(_players[payer]).Concat(GetEquipment(payer)).Any(ca=>YanRouCanDiscard(c.OwnerSeat,payer,ca,_cardZones.GetLocation(ca.Id)));
    }
    private SkillProgramStepOutcome BeginYanRou(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);var c=f.WindowContext;
        if(f.YanRou is not null||f.InstructionIndex!=1||f.TriggerId is null||c is null||!_players[f.OwnerSeat].IsAlive||_winner!=Winner.None||!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)||
            !CanOfferYanRou(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,c.OccurrenceIndex),GetProgramTrigger(f),c))
            throw new InvalidOperationException("Yan Rou requires its exact current unpaid native candidate.");
        var use=op==SkillProgramEffectOp.DiscardSlashUserForUncancelable?_resolutionStack.OfType<CardUseFrame>().Single(u=>u.Id==c.CardUse!.ParentCardUseFrameId):null;
        var r=new YanRouReceipt(YanRouSource(f),f.GameplayHash,c.ParentFrameId,c.Window,op,_turnNumber,_turnProgression.OwnerSeat,use is null?YanRouStage.TargetChoice:YanRouStage.DiscardChoice,
            use?.SourceSeat??f.OwnerSeat,use is null?YanRouTurnDamage(f.OwnerSeat):0,use?.Id,use?.Action!.ActionId,use?.Action is{}a?GetCardUseDebit(a.ActionId):null);
        ReplaceRuntimeTop(f=f with{YanRou=r});
        AdvanceEventRulesAndQueueFact(new YanRouStartedEvent(f.Id,r.Source,r.GameplayHash,r.WindowId,r.Window,r.Operation,r.Turn,r.TurnOwner,r.Payer,r.Amount,r.UseId,r.ActionId));
        PublishYanRou(f);return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> YanRouChoices(ProgramSkillFrame f)
    {
        var r=f.YanRou!;if(!_players[f.OwnerSeat].IsAlive||_winner!=Winner.None)return [];
        if(r.Stage==YanRouStage.TargetChoice)return Array.AsReadOnly(_players.Where(p=>p.IsAlive&&IsRuleWounded(p)).Select(p=>new PromptChoice(new($"yan-rou.{f.Id}.target.{p.Seat}"),$"令{p.Name}回复{r.Amount}点体力并摸{r.Amount}张牌",[],[p.Seat],new Dictionary<string,string>{{"program-action","yan-rou"},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)}})).ToArray());
        if(r.Stage!=YanRouStage.DiscardChoice||!_players[r.Payer].IsAlive||!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId))return [];
        return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,r.Payer,[CardZoneKind.Hand,CardZoneKind.Equipment],OwnedCardMoveIntent.Discard,
            canSelect:(zone,card)=>YanRouCanDiscard(f.OwnerSeat,r.Payer,card,new(zone,r.Payer)))
            .Select(c=>c with{Parameters=new Dictionary<string,string>(c.Parameters){["program-action"]="yan-rou"}}).ToArray());
    }
    private void PublishYanRou(ProgramSkillFrame f)
    {
        var choices=YanRouChoices(f);if(choices.Count==0){FinishYanRou(f,false);return;}var skill=_contentRegistry.GetSkill(f.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,f.YanRou!.Stage==YanRouStage.TargetChoice?"襄戍：选择已受伤角色。":"仇讨：弃置此杀使用者的一张牌。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=true,TargetSeat=f.YanRou.Payer,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
    }
    [ProgramAnswer("yan-rou")]
    private void AnswerYanRou(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{YanRou:{}r}f||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!AssistedChoicesEqual([choice],YanRouChoices(f).Where(c=>c.Id==choice.Id).ToArray()))
            throw new InvalidOperationException("Yan Rou lost its actual private chooser or published opaque slot.");
        AssertYanRou(f);ClearPendingDecision();
        if(r.Stage==YanRouStage.TargetChoice)
        {
            var target=choice.Targets.Single();if(YanRouSpent(f.OwnerSeat,f.SkillId,f.GameplayHash))throw new InvalidOperationException("Xiangshu is permanently spent.");
            AdvanceEventRulesAndQueueFact(new YanRouXiangshuSpentEvent(f.Id,r.Source,r.GameplayHash,r.WindowId,r.Turn,target,r.Amount));
            ReplaceRuntimeTop(f=f with{YanRou=r with{Target=target,Payer=target,Stage=YanRouStage.RecoveryChildren,Before=_movementSequence,After=_movementSequence},PendingMovementContinuation=new(target,0,null)});
            if(r.Amount>0)new ProgramSkillHost(this).Recover(f.Id,f.OwnerSeat,target,r.Amount,null,null);
            RecordYanRouMovement(f.Id);ContinueYanRou(f.Id);return;
        }
        if(!Enum.TryParse<CardZoneKind>(choice.Parameters.GetValueOrDefault("source-zone"),out var zone)||zone is not(CardZoneKind.Hand or CardZoneKind.Equipment)||!int.TryParse(choice.Parameters.GetValueOrDefault("slot-index"),out var slot))throw new InvalidOperationException("Choutao lost its actual HE source slot.");
        var from=new CardLocation(zone,r.Payer);var cards=_cardZones.CardsAt(from);if(slot<0||slot>=cards.Count)throw new InvalidOperationException("Choutao source slot is absent.");var card=cards[slot];
        var grain=zone==CardZoneKind.Equipment&&card.Kind==CardKind.WoodenOx?GetWoodenOxGrain(_players[r.Payer]).Select(c=>c.Id).ToArray():[];
        ReplaceRuntimeTop(f=f with{YanRou=r with{Stage=YanRouStage.DiscardChildren,CardId=card.Id,From=from,Before=_movementSequence,After=_movementSequence,WoodenOxGrainIds=grain},PendingMovementContinuation=new(r.Payer,0,null)});
        MoveProgramLogicalDiscard(f,r.Payer,[card.Id],new(YanRouDiscardReason));RecordYanRouMovement(f.Id);ContinueYanRou(f.Id);
    }
    private void RecordYanRouMovement(long id)
    {
        var f=GetActiveProgramFrame(id);var r=f.YanRou!;var after=_movementSequence;
        var shuffles=r.Stage==YanRouStage.DrawChildren?_pendingCardsMovedBatches.Where(b=>b.ParentFrameId==id&&b.ParentBatchId is null&&b.AwaitingProgramFrameId==id&&b.TurnNumber==r.Turn&&b.OriginOwnerSeat==f.OwnerSeat&&b.OriginSkillId==f.SkillId&&b.OriginSkillInstanceId==f.SkillInstanceId&&b.Movements.Count>0&&
            b.Movements.All(m=>m.Sequence>r.Before&&m.Sequence<=after&&m.From==CardLocation.DiscardPile&&m.To==CardLocation.DrawPile&&m.Reason==CardMoveReasons.Reshuffle&&_cardMovements.Contains(m))&&
            b.SourceCounts is [{Location:var source,CountBefore:var discarded,CountAfter:0}]&&source==CardLocation.DiscardPile&&discarded==b.Movements.Count&&b.DestinationCounts is [{Location:var destination,CountBefore:0,CountAfter:var replenished}]&&destination==CardLocation.DrawPile&&replenished==b.Movements.Count)
            .Select(b=>new YanRouReshuffle(b.Id,id,b.Movements[0].Sequence-1,b.Movements[^1].Sequence){Movements=b.Movements}).ToArray():[];
        var count=_cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=after&&(r.Stage!=YanRouStage.DrawChildren||m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(r.Payer)&&m.Reason.Value==YanRouDrawReason));
        ReplaceRuntimeTop(f with{YanRou=r with{After=after,ActualCount=count,Reshuffles=shuffles}});
        foreach(var s in shuffles)AdvanceEventRulesAndQueueFact(new YanRouReshuffleCapturedEvent(id,s.BatchId,s.Before,s.After,s.Movements.Count));
        AdvanceEventRulesAndQueueFact(new YanRouMovementIssuedEvent(id,r.Stage,r.Payer,r.CardId,r.From,r.Before,after,count));
    }
    private bool DrainYanRou(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueYanRou(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{YanRou:{}r}f||f.Id!=id)return;AssertYanRou(f);
        if(r.Stage is YanRouStage.TargetChoice or YanRouStage.DiscardChoice){if(_pendingDecision is null)PublishYanRou(f);return;}
        if(DrainYanRou(f))return;ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null});
        if(r.Stage==YanRouStage.DiscardChildren)
        {
            var use=_resolutionStack.OfType<CardUseFrame>().Single(u=>u.Id==r.UseId&&u.Action?.ActionId==r.ActionId);
            UpdateLifecycleCardUse(use.Id,u=>u with{Enhancements=u.Enhancements|CurrentCardEnhancement.Uncancelable});
            var refunded=r.Payer==f.OwnerSeat&&r.Debit is{}debit&&debit.ActorSeat==r.Payer&&TryRefundCardUseDebit(debit);
            AdvanceEventRulesAndQueueFact(new YanRouChoutaoAppliedEvent(f.Id,use.Id,r.ActionId!.Value,r.Payer,r.CardId!.Value,refunded));FinishYanRou(f,true);return;
        }
        if(r.Stage==YanRouStage.RecoveryChildren&&_winner==Winner.None&&_players[r.Target!.Value].IsAlive&&r.Amount>0)
        {
            ReplaceRuntimeTop(f=f with{YanRou=r with{Stage=YanRouStage.DrawChildren,Before=_movementSequence,After=_movementSequence,ActualCount=0,Reshuffles=[]},PendingMovementContinuation=new(r.Payer,0,null)});
            DrawProgramCards(f.Id,r.Payer,r.Amount,null,null,SkillProgramCardSetVisibility.Private,new(YanRouDrawReason));RecordYanRouMovement(f.Id);ContinueYanRou(f.Id);return;
        }
        FinishYanRou(f,true);
    }
    private void FinishYanRou(ProgramSkillFrame f,bool applied)
    {AdvanceEventRulesAndQueueFact(new YanRouCompletedEvent(f.Id,applied));ReplaceRuntimeTop(f=f with{YanRou=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,26800)]private bool ResumeYanRou(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{YanRou:not null}f||f.Id!=id)return false;ContinueYanRou(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,26800)]private bool ReturnYanRou(ProgramSkillFrame f){if(f.YanRou is null)return false;ContinueYanRou(f.Id);return true;}
    private bool IsYanRouAwaitedMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>f.YanRou is{}r&&p.SubjectSeat==r.Payer&&p.BeforeCount==0&&p.CoverageResultBind is null&&(e is null||YanRouComposition.IsOperation(e.Op));
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,26800)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,26800)]private bool ContinuePaidYanRou(ProgramSkillFrame f){if(f.YanRou is null)return false;AssertYanRou(f);return true;}
    [ProgramAiChoice(SkillProgramEffectOp.DiscardSlashUserForUncancelable,SkillProgramEffectOp.RecoverAndDrawByTurnDamage)]private PromptChoice AiYanRou(PendingDecision p,ProgramSkillFrame f)=>p.Choices.First();
}
