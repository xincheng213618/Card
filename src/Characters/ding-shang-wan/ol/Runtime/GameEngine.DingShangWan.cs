using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string DingShangWanDrawReason="skill-program.ding-shang-wan.draw",FudaoGiftReason="skill-program.ding-shang-wan.fudao.give",
        FudaoDiscardReason="skill-program.ding-shang-wan.fudao.discard",FengyanGiftReason="skill-program.ding-shang-wan.fengyan.give",FengyanDiscardReason="skill-program.ding-shang-wan.fengyan.discard";
    private CardConversionSource DingShangWanSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private FudaoHandRecordedEvent? DingShangWanRecord(int owner,string skill,string hash)=>ProgramEventHistory<FudaoHandRecordedEvent>().SingleOrDefault(e=>e.Source.OwnerSeat==owner&&e.Source.SkillId==skill&&e.GameplayHash==hash);
    [ProgramCapability] private sealed class DingShangWanCapability(GameEngine e):IDingShangWanHost
    { public SkillProgramStepOutcome Execute(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginDingShangWan(f,op); }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,26700)]
    private bool CanOfferDingShangWan(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)
    {
        if(t.Effects is not [var effect]||!DingShangWanComposition.IsOperation(effect.Op))return true;
        if(!DingShangWanParent(c,context))return false;
        if(effect.Op==SkillProgramEffectOp.InitializeFudaoHandRecord)return context.Window==SkillProgramTriggerWindow.GameStarting&&_phase==TurnPhase.NotStarted&&
            !ProgramEventHistory<DingShangWanStartedEvent>().Any(e=>e.Source.OwnerSeat==c.OwnerSeat&&e.Source.SkillId==c.SkillId&&e.Operation==effect.Op);
        if(effect.Op==SkillProgramEffectOp.DrawFudaoRecordedPair)return context.Window==SkillProgramTriggerWindow.AfterTurnEnded&&context.TargetSeat is{}ended&&_players[ended].IsAlive&&
            DingShangWanRecord(c.OwnerSeat,c.SkillId,c.GameplayHash)is{}record&&context.Facts?.EventTargetHandCount==record.HandCount;
        return DingShangWanCounterpart(c.OwnerSeat,context)is not null;
    }
    private int? DingShangWanCounterpart(int owner,ProgramSkillWindowContext c)=>c.Window switch
    {
        SkillProgramTriggerWindow.AfterDamageApplied when c.TargetSeat==owner&&c.Amount>0&&c.DamageFrameId is not null&&c.SourceSeat is{}s&&s!=owner&&IsValidPlayerSeat(s)&&CurrentDamageAttempt is{IsSourceLess:false}=>s,
        SkillProgramTriggerWindow.CardResponseCompleted when c.CardUse is{ActorSeat:var actor,CardActionId:var action}&&actor==owner&&c.TargetSeat is{}s&&s!=owner&&IsValidPlayerSeat(s)&&
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w=>w.Id==c.ParentFrameId)is{ResponseCompletion:{CostsDrained:true}}w&&w.Action.ActionId==action&&w.Action.OpponentSeat==s&&ValidResponseCompletionWindow(w)=>s,
        _=>null
    };
    private bool DingShangWanParent(ProgramTriggerCandidate candidate,ProgramSkillWindowContext c)
    {
        if(c.OwnerSeat!=candidate.OwnerSeat)return false;
        var parent=_resolutionStack.SingleOrDefault(f=>f.Id==c.ParentFrameId);
        return parent switch
        {
            ProgramLifecycleTriggerWindowFrame w=>c.Window==SkillProgramTriggerWindow.GameStarting&&w.Window==c.Window&&w.Continuation==ProgramLifecycleContinuation.CompleteGameStarting&&c.SourceSeat==w.OwnerSeat&&c.TargetSeat==w.OwnerSeat&&w.CandidateIndex>=0&&w.CandidateIndex<w.Candidates.Count&&w.Candidates[w.CandidateIndex]==candidate,
            DeferredTurnEndFrame w=>c.Window==SkillProgramTriggerWindow.AfterTurnEnded&&IsActualAfterTurnEndedParent(w)&&AfterTurnEndedCandidate(w)==candidate&&c.SourceSeat==w.OwnerSeat&&c.TargetSeat==w.OwnerSeat&&c.OccurrenceIndex==candidate.OccurrenceIndex&&c.Facts==w.AfterTurnEnded!.Items[w.AfterTurnEnded.ItemIndex].Facts,
            DamageTriggerWindowFrame w=>c.Window==SkillProgramTriggerWindow.AfterDamageApplied&&w.ParentFrameId==c.DamageFrameId&&w.CandidateIndex>=0&&w.CandidateIndex<w.Candidates.Count&&w.Candidates[w.CandidateIndex].ToProgramCandidate()==candidate,
            ProgramCardTriggerWindowFrame w=>c.Window==SkillProgramTriggerWindow.CardResponseCompleted&&w.ResponseCompletion is{CostsDrained:true}&&w.CandidateIndex>=0&&w.CandidateIndex<w.Candidates.Count&&ToSharedCandidate(w.Candidates[w.CandidateIndex])==candidate&&ValidResponseCompletionWindow(w),
            _=>false
        };
    }
    private SkillProgramStepOutcome BeginDingShangWan(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);if(f.DingShangWan is not null||f.InstructionIndex!=1||f.TriggerId is null||f.WindowContext is not{}c||
            !_players[f.OwnerSeat].IsAlive||_winner!=Winner.None||!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)||
            !CanOfferDingShangWan(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,c.OccurrenceIndex),GetProgramTrigger(f),c))
            throw new InvalidOperationException("Ding Shang Wan requires its exact current unpaid native candidate.");
        var counterpart=op==SkillProgramEffectOp.ResolveFengyanCounterpart?DingShangWanCounterpart(f.OwnerSeat,c):op==SkillProgramEffectOp.DrawFudaoRecordedPair?c.TargetSeat:null;
        var stage=op==SkillProgramEffectOp.InitializeFudaoHandRecord?DingShangWanStage.OpeningDraw:op==SkillProgramEffectOp.DrawFudaoRecordedPair?DingShangWanStage.PairOwnerDraw:DingShangWanStage.FengChoice;
        var r=new DingShangWanReceipt(DingShangWanSource(f),f.GameplayHash,1,c.ParentFrameId,c.Window,op,_turnNumber,_turnProgression.OwnerSeat,stage,counterpart,c.DamageFrameId,c.CardUse?.CardActionId);
        ReplaceRuntimeTop(f=f with{DingShangWan=r});
        AdvanceEventRulesAndQueueFact(new DingShangWanStartedEvent(f.Id,r.Source,r.GameplayHash,r.WindowId,r.Window,r.Operation,r.Turn,r.TurnOwner,r.Counterpart,r.DamageFrameId,r.ActionId));
        if(op==SkillProgramEffectOp.ResolveFengyanCounterpart)PublishDingShangWan(f);
        else BeginDingShangWanDraw(f,f.OwnerSeat,op==SkillProgramEffectOp.InitializeFudaoHandRecord?3:counterpart==f.OwnerSeat?2:1,stage);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private bool DingShangWanCanPay(int seat,Card card,CardLocation from,bool gift)=>!card.IsGeneralWeapon&&from.OwnerSeat==seat&&from.Zone is(CardZoneKind.Hand or CardZoneKind.Equipment)&&
        !IsForeignEquipmentDiscardPrevented(seat,card,from,gift?OwnedCardMoveIntent.Transfer:OwnedCardMoveIntent.Discard)&&
        (gift||!IsSelfHandCategoryDiscardForbidden(seat,card,from,OwnedCardMoveIntent.Discard));
    private int DingShangWanChooser(ProgramSkillFrame f)=>f.DingShangWan!.Stage==DingShangWanStage.FengDiscardChoice?f.DingShangWan.Counterpart!.Value:f.OwnerSeat;
    private IReadOnlyList<PromptChoice> DingShangWanChoices(ProgramSkillFrame f)
    {
        var r=f.DingShangWan!;var result=new List<PromptChoice>();
        PromptChoice C(string branch,string label,int? card=null,int? target=null)=>new(new($"ding-shang-wan.{f.Id}.{r.Stage}.{branch}.{card}.{target}.{r.Selected.Count}"),label,card is{}id?[id]:[],target is{}seat?[seat]:[],new Dictionary<string,string>{{"program-action","ding-shang-wan"},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"branch",branch}});
        if(r.Stage==DingShangWanStage.FengChoice)
        {result.Add(C("owner-draw","摸一张牌并交给其一张牌"));result.Add(C("other-draw","其摸一张牌并弃置两张牌"));return Array.AsReadOnly(result.ToArray());}
        var payer=DingShangWanChooser(f);if(!_players[payer].IsAlive||_winner!=Winner.None)return [];
        if(r.Stage==DingShangWanStage.OpeningGiftChoice)
            foreach(var p in _players.Where(p=>p.IsAlive&&p.Seat!=f.OwnerSeat))result.Add(C("recipient",$"交给{p.Name}",target:p.Seat));
        if(r.Stage is not(DingShangWanStage.OpeningGiftChoice or DingShangWanStage.OpeningDiscardChoice or DingShangWanStage.FengGiftChoice or DingShangWanStage.FengDiscardChoice))return [];
        var gift=r.Stage is DingShangWanStage.OpeningGiftChoice or DingShangWanStage.FengGiftChoice;
        foreach(var id in r.Candidates.Where(id=>!r.Selected.Contains(id)&&r.Selected.Count<r.Maximum&&DingShangWanCanPay(payer,GetAdvancedCard(id),_cardZones.GetLocation(id),gift)&&
            (r.Stage is not(DingShangWanStage.OpeningGiftChoice or DingShangWanStage.OpeningDiscardChoice)||_cardZones.GetLocation(id)==CardLocation.Hand(payer))))
            result.Add(C("pick",$"选择【{GetAdvancedCard(id).DisplayName}】",id));
        foreach(var id in r.Selected)result.Add(C("unpick",$"撤回【{GetAdvancedCard(id).DisplayName}】",id));
        if(r.Selected.Count>=r.Minimum&&(r.Stage!=DingShangWanStage.OpeningGiftChoice||r.Selected.Count==0||r.Recipient is{}recipient&&_players[recipient].IsAlive))
            result.Add(C("finish",r.Stage==DingShangWanStage.OpeningGiftChoice&&r.Selected.Count==0?"不交牌，继续弃牌":"完成选择"));
        return Array.AsReadOnly(result.ToArray());
    }
    private void PublishDingShangWan(ProgramSkillFrame f)
    {
        var choices=DingShangWanChoices(f);if(choices.Count==0){FinishDingShangWan(f);return;}
        PublishParticipantHandChoice(f,DingShangWanChooser(f),choices,"请选择实际结算。",f.DingShangWan!.Counterpart??f.DingShangWan.Recipient??f.OwnerSeat);
    }
    private void BeginDingShangWanSelection(ProgramSkillFrame f,DingShangWanStage stage)
    {
        var r=f.DingShangWan!;var payer=stage==DingShangWanStage.FengDiscardChoice?r.Counterpart!.Value:f.OwnerSeat;
        if(!_players[payer].IsAlive||_winner!=Winner.None||stage==DingShangWanStage.FengGiftChoice&&(r.Counterpart is not{}target||!_players[target].IsAlive)){FinishDingShangWan(f);return;}
        var gift=stage is DingShangWanStage.OpeningGiftChoice or DingShangWanStage.FengGiftChoice;
        var handOnly=stage is DingShangWanStage.OpeningGiftChoice or DingShangWanStage.OpeningDiscardChoice;
        var cards=(handOnly?GetHand(_players[payer]):GetHand(_players[payer]).Concat(GetEquipment(payer))).Where(card=>DingShangWanCanPay(payer,card,_cardZones.GetLocation(card.Id),gift)).Select(c=>c.Id).ToArray();
        var min=stage==DingShangWanStage.FengGiftChoice?Math.Min(1,cards.Length):stage==DingShangWanStage.FengDiscardChoice?Math.Min(2,cards.Length):0;
        var max=stage==DingShangWanStage.OpeningGiftChoice?Math.Min(3,cards.Length):stage==DingShangWanStage.FengGiftChoice?Math.Min(1,cards.Length):stage==DingShangWanStage.FengDiscardChoice?Math.Min(2,cards.Length):cards.Length;
        ReplaceRuntimeTop(f=f with{DingShangWan=r with{Stage=stage,Payer=payer,Minimum=min,Maximum=max,Candidates=cards,Selected=[]}});PublishDingShangWan(f);
    }
    [ProgramAnswer("ding-shang-wan")]
    private void AnswerDingShangWan(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{DingShangWan:{}r}f||_pendingDecision?.PlayerSeat!=DingShangWanChooser(f)||
            !AssistedChoicesEqual([choice],DingShangWanChoices(f).Where(c=>c.Id==choice.Id).ToArray()))throw new InvalidOperationException("Ding Shang Wan lost its actual private chooser or selected physical entities.");
        AssertDingShangWan(f);ClearPendingDecision();var branch=choice.Parameters["branch"];
        if(r.Stage==DingShangWanStage.FengChoice)
        {
            AdvanceEventRulesAndQueueFact(new DingShangWanBranchEvent(f.Id,branch,r.Counterpart));ReplaceRuntimeTop(f=f with{DingShangWan=r with{Branch=branch}});
            BeginDingShangWanDraw(f,branch=="owner-draw"?f.OwnerSeat:r.Counterpart!.Value,1,branch=="owner-draw"?DingShangWanStage.FengOwnerDraw:DingShangWanStage.FengTargetDraw);return;
        }
        if(branch=="recipient"){ReplaceRuntimeTop(f=f with{DingShangWan=r with{Recipient=choice.Targets.Single()}});PublishDingShangWan(f);return;}
        if(branch!="finish")
        {var id=choice.Cards.Single();ReplaceRuntimeTop(f=f with{DingShangWan=r with{Selected=branch=="pick"?r.Selected.Append(id).ToArray():r.Selected.Where(x=>x!=id).ToArray()}});PublishDingShangWan(f);return;}
        var stage=r.Stage switch{DingShangWanStage.OpeningGiftChoice=>DingShangWanStage.OpeningGiftChildren,DingShangWanStage.OpeningDiscardChoice=>DingShangWanStage.OpeningDiscardChildren,DingShangWanStage.FengGiftChoice=>DingShangWanStage.FengGiftChildren,_=>DingShangWanStage.FengDiscardChildren};
        var gift=stage is DingShangWanStage.OpeningGiftChildren or DingShangWanStage.FengGiftChildren;var recipient=stage==DingShangWanStage.OpeningGiftChildren?r.Recipient:r.Counterpart;
        var grain=r.Selected.Any(id=>_cardZones.GetLocation(id)==CardLocation.Equipment(r.Payer)&&GetAdvancedCard(id).Kind==CardKind.WoodenOx)
            ?GetWoodenOxGrain(_players[r.Payer]).Select(c=>c.Id).ToArray():[];
        var before=_movementSequence;ReplaceRuntimeTop(f=f with{DingShangWan=r with{Stage=stage,Before=before,After=before,Recipient=recipient,WoodenOxGrainIds=grain,Reshuffles=[]},PendingMovementContinuation=new(r.Payer,0,null)});
        if(r.Selected.Count>0)MoveProgramCardsFromMultipleSources(r.Selected,gift?CardLocation.Hand(recipient!.Value):CardLocation.DiscardPile,new(stage switch{DingShangWanStage.OpeningGiftChildren=>FudaoGiftReason,DingShangWanStage.OpeningDiscardChildren=>FudaoDiscardReason,DingShangWanStage.FengGiftChildren=>FengyanGiftReason,_=>FengyanDiscardReason}));
        RecordDingShangWanMovement(f.Id);ContinueDingShangWan(f.Id);
    }
    private void BeginDingShangWanDraw(ProgramSkillFrame f,int seat,int count,DingShangWanStage stage)
    {
        if(!_players[seat].IsAlive||_winner!=Winner.None){FinishDingShangWan(f);return;}
        var before=_movementSequence;ReplaceRuntimeTop(f=f with{DingShangWan=f.DingShangWan! with{Stage=stage,Payer=seat,Before=before,After=before,Candidates=[],Selected=[],WoodenOxGrainIds=[],Reshuffles=[]},PendingMovementContinuation=new(seat,0,null)});
        DrawProgramCards(f.Id,seat,count,null,null,SkillProgramCardSetVisibility.Private,new(DingShangWanDrawReason));RecordDingShangWanMovement(f.Id);ContinueDingShangWan(f.Id);
    }
    private void RecordDingShangWanMovement(long id)
    {
        var f=GetActiveProgramFrame(id);var r=f.DingShangWan!;var after=_movementSequence;
        var count=_cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=after&&(!IsDingShangWanDrawStage(r.Stage)||m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(r.Payer)&&m.Reason.Value==DingShangWanDrawReason));
        var shuffles=IsDingShangWanDrawStage(r.Stage)?_pendingCardsMovedBatches.Where(b=>b.ParentFrameId==id&&b.ParentBatchId is null&&b.AwaitingProgramFrameId==id&&b.TurnNumber==r.Turn&&
            b.OriginOwnerSeat==f.OwnerSeat&&b.OriginSkillId==f.SkillId&&b.OriginSkillInstanceId==f.SkillInstanceId&&b.Movements.Count>0&&
            b.Movements.All(m=>m.Sequence>r.Before&&m.Sequence<=after&&m.From==CardLocation.DiscardPile&&m.To==CardLocation.DrawPile&&m.Reason==CardMoveReasons.Reshuffle&&_cardMovements.Contains(m))&&
            b.SourceCounts is [{Location:var source,CountBefore:var discarded,CountAfter:0}]&&source==CardLocation.DiscardPile&&discarded==b.Movements.Count&&
            b.DestinationCounts is [{Location:var destination,CountBefore:0,CountAfter:var replenished}]&&destination==CardLocation.DrawPile&&replenished==b.Movements.Count)
            .Select(b=>new DingShangWanReshuffle(b.Id,id,b.Movements[0].Sequence-1,b.Movements[^1].Sequence){Movements=b.Movements}).ToArray():[];
        ReplaceRuntimeTop(f with{DingShangWan=r with{After=after,ActualCount=count,Reshuffles=shuffles}});
        foreach(var shuffle in shuffles)AdvanceEventRulesAndQueueFact(new DingShangWanReshuffleCapturedEvent(id,shuffle.BatchId,shuffle.Before,shuffle.After,shuffle.Movements.Count));
        AdvanceEventRulesAndQueueFact(new DingShangWanMovementIssuedEvent(id,r.Stage,r.Payer,r.Recipient,r.Before,after,count));
    }
    private static bool IsDingShangWanDrawStage(DingShangWanStage stage)=>stage is DingShangWanStage.OpeningDraw or DingShangWanStage.PairOwnerDraw or DingShangWanStage.PairTargetDraw or DingShangWanStage.FengOwnerDraw or DingShangWanStage.FengTargetDraw;
    private bool DrainDingShangWan(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueDingShangWan(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{DingShangWan:{}r}f||f.Id!=id)return;
        AssertDingShangWan(f);
        if(r.Stage is DingShangWanStage.OpeningGiftChoice or DingShangWanStage.OpeningDiscardChoice or DingShangWanStage.FengChoice or DingShangWanStage.FengGiftChoice or DingShangWanStage.FengDiscardChoice){if(_pendingDecision is null)PublishDingShangWan(f);return;}
        if(DrainDingShangWan(f))return;ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null});
        if(_winner!=Winner.None||!_players[f.OwnerSeat].IsAlive){FinishDingShangWan(f);return;}
        switch(r.Stage)
        {
            case DingShangWanStage.OpeningDraw:BeginDingShangWanSelection(f,DingShangWanStage.OpeningGiftChoice);return;
            case DingShangWanStage.OpeningGiftChildren:BeginDingShangWanSelection(f,DingShangWanStage.OpeningDiscardChoice);return;
            case DingShangWanStage.OpeningDiscardChildren:
                AdvanceEventRulesAndQueueFact(new FudaoHandRecordedEvent(f.Id,r.Source,r.GameplayHash,GetHand(_players[f.OwnerSeat]).Count));FinishDingShangWan(f);return;
            case DingShangWanStage.PairOwnerDraw:
                if(r.Counterpart!=f.OwnerSeat&&r.Counterpart is{}recipient&&_players[recipient].IsAlive){BeginDingShangWanDraw(f,recipient,1,DingShangWanStage.PairTargetDraw);return;}break;
            case DingShangWanStage.FengOwnerDraw:BeginDingShangWanSelection(f,DingShangWanStage.FengGiftChoice);return;
            case DingShangWanStage.FengTargetDraw:BeginDingShangWanSelection(f,DingShangWanStage.FengDiscardChoice);return;
        }
        FinishDingShangWan(f);
    }
    private void FinishDingShangWan(ProgramSkillFrame f)
    {AdvanceEventRulesAndQueueFact(new DingShangWanCompletedEvent(f.Id,true));ReplaceRuntimeTop(f=f with{DingShangWan=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,26700)]private bool ResumeDingShangWan(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{DingShangWan:not null}f||f.Id!=id)return false;ContinueDingShangWan(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,26700)]private bool ReturnDingShangWan(ProgramSkillFrame f){if(f.DingShangWan is null)return false;ContinueDingShangWan(f.Id);return true;}
    private bool IsDingShangWanAwaitedMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>f.DingShangWan is{}r&&p.SubjectSeat==r.Payer&&p.BeforeCount==0&&p.CoverageResultBind is null&&(e is null||DingShangWanComposition.IsOperation(e.Op));
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,26700)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,26700)]
    private bool ContinuePaidDingShangWan(ProgramSkillFrame f){if(f.DingShangWan is null)return false;AssertDingShangWan(f);return true;}
    [ProgramAiChoice(SkillProgramEffectOp.InitializeFudaoHandRecord,SkillProgramEffectOp.DrawFudaoRecordedPair,SkillProgramEffectOp.ResolveFengyanCounterpart)]
    private PromptChoice AiDingShangWan(PendingDecision p,ProgramSkillFrame f)=>p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("branch")=="finish")??p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("branch")=="other-draw")??p.Choices.First(c=>c.Parameters.GetValueOrDefault("branch")!="unpick");
}
