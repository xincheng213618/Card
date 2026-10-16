using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string LuYuShengDrawReason="skill-program.lu-yu-sheng.draw",LuYuShengBottomDiscardReason="skill-program.lu-yu-sheng.cangxin.discard",LuYuShengRunweiDiscardReason="skill-program.lu-yu-sheng.runwei.discard";
    private CardConversionSource LuYuShengSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    [ProgramCapability]private sealed class LuYuShengCapability(GameEngine e):ILuYuShengHost
    {public SkillProgramStepOutcome Execute(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginLuYuSheng(f,op);}
    private bool LuYuShengParent(ProgramTriggerCandidate candidate,ProgramSkillWindowContext c)
    {
        if(c.OwnerSeat!=candidate.OwnerSeat)return false;var parent=_resolutionStack.SingleOrDefault(p=>p.Id==c.ParentFrameId);
        if(parent is BeforeDamageProgramWindowFrame before)return c.Window==SkillProgramTriggerWindow.BeforeDamageApplied&&before.TargetSeat==candidate.OwnerSeat&&c.TargetSeat==before.TargetSeat&&before.CandidateIndex>=0&&before.CandidateIndex<before.Candidates.Count&&before.Candidates[before.CandidateIndex].Candidate==candidate;
        return parent is ProgramLifecycleTriggerWindowFrame w&&c.Window==w.Window&&w.CandidateIndex>=0&&w.CandidateIndex<w.Candidates.Count&&w.Candidates[w.CandidateIndex]==candidate&&
            (c.Window==SkillProgramTriggerWindow.DrawPhaseStarting?w.OwnerSeat==candidate.OwnerSeat&&w.Continuation==ProgramLifecycleContinuation.CompleteDrawPhase:
             c.Window==SkillProgramTriggerWindow.DiscardPhaseStarting&&w.OwnerSeat==_currentSeat&&w.Continuation==ProgramLifecycleContinuation.CompleteDiscardPhase)&&c.SourceSeat==w.OwnerSeat;
    }
    private bool LuYuShengFirstDamage(int seat)=>!EventsSinceLastBoundary(e=>e is TurnStartedEvent).OfType<DamageAppliedEvent>().Any(e=>e.TargetSeat==seat&&e.Amount>0)&&
        !ProgramEventHistory<LuYuShengStartedEvent>().Any(e=>e.Turn==_turnNumber&&e.Source.OwnerSeat==seat&&e.Operation==SkillProgramEffectOp.DiscardBottomHeartsForDamageReduction);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,26900)]
    private bool CanOfferLuYuSheng(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)
    {
        if(t.Effects is not [var e]||!LuYuShengComposition.IsOperation(e.Op))return true;if(!LuYuShengParent(c,context))return false;
        if(e.Op==SkillProgramEffectOp.RevealBottomHeartsAndDraw)return _phase==TurnPhase.Draw;
        if(e.Op==SkillProgramEffectOp.DiscardBottomHeartsForDamageReduction)return context.Amount>0&&LuYuShengFirstDamage(c.OwnerSeat)&&CurrentDamageAttempt is{DamageAmountFinalized:true}attack&&attack.TargetSeat==c.OwnerSeat;
        return _phase==TurnPhase.Discard&&CurrentActualDiscardRecoveryPhase()is{Kind:ActualDiscardRecoveryPhaseKind.Discard}p&&p.ActorSeat==_currentSeat&&IsRecordedActualDiscardRecoveryPhase(p)&&_players[p.ActorSeat].IsAlive&&_players[p.ActorSeat].Hp<_players[p.ActorSeat].MaxHp;
    }
    private SkillProgramStepOutcome BeginLuYuSheng(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);var c=f.WindowContext;
        if(f.LuYuSheng is not null||f.InstructionIndex!=1||f.TriggerId is null||c is null||!_players[f.OwnerSeat].IsAlive||_winner!=Winner.None||!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)||
            !CanOfferLuYuSheng(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,c.OccurrenceIndex),GetProgramTrigger(f),c))throw new InvalidOperationException("Lu Yu Sheng requires its exact unpaid actual native candidate.");
        var bottom=op==SkillProgramEffectOp.RunweiDrawOrDiscardForActualPhase?[]:_cardZones.CardsAt(CardLocation.DrawPile).Take(3).Select(card=>card.Id).ToArray();
        var attack=op==SkillProgramEffectOp.DiscardBottomHeartsForDamageReduction?CurrentDamageAttempt:null;
        var r=new LuYuShengReceipt(LuYuShengSource(f),f.GameplayHash,c.ParentFrameId,c.Window,op,_turnNumber,_turnProgression.OwnerSeat,
            op==SkillProgramEffectOp.RunweiDrawOrDiscardForActualPhase?LuYuShengStage.RunweiChoice:attack is null?LuYuShengStage.DrawChildren:LuYuShengStage.BottomChoice,
            op==SkillProgramEffectOp.RunweiDrawOrDiscardForActualPhase?_currentSeat:f.OwnerSeat,CurrentActualDiscardRecoveryPhase(),attack?.ResolutionId,c.Amount,attack is null?0:LuYuShengChainCursor(attack),attack?.IsChainPropagation??false,c.SourceSeat){BottomCards=bottom};
        ReplaceRuntimeTop(f=f with{LuYuSheng=r});AdvanceEventRulesAndQueueFact(new LuYuShengStartedEvent(f.Id,r.Source,r.GameplayHash,r.WindowId,op,r.Turn,r.TurnOwner,r.Payer,r.AttackId,r.OriginalDamage));
        for(var i=0;i<bottom.Length;i++)AdvanceEventRulesAndQueueFact(new LuYuShengBottomRevealedEvent(f.Id,i,bottom[i],GetAdvancedCard(bottom[i]).Suit));
        if(bottom.Length>0)AdvanceEventRulesAndQueueFact(new CardsRevealedEvent(f.Id,Array.AsReadOnly(bottom.Select(id=>ToSnapshot(GetAdvancedCard(id))).ToArray())));
        if(op==SkillProgramEffectOp.RevealBottomHeartsAndDraw){IssueLuYuShengDraw(f,bottom.Count(id=>GetAdvancedCard(id).Suit==Suit.Heart));return SkillProgramStepOutcome.AwaitChild;}
        PublishLuYuSheng(f);return SkillProgramStepOutcome.AwaitChoice;
    }
    private int LuYuShengChooser(ProgramSkillFrame f)=>f.LuYuSheng!.Stage==LuYuShengStage.RunweiDiscardChoice?f.LuYuSheng.Payer:f.OwnerSeat;
    private IReadOnlyList<PromptChoice> LuYuShengChoices(ProgramSkillFrame f)
    {
        var r=f.LuYuSheng!;PromptChoice C(string option,string label,int? card=null)=>new(new($"lu-yu-sheng.{f.Id}.{r.Stage}.{option}.{card}.{r.Selected.Count}"),label,card is{}id?[id]:[],[],new Dictionary<string,string>{{"program-action","lu-yu-sheng"},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"option",option}});
        if(r.Stage==LuYuShengStage.RunweiChoice)return [C("draw","令其摸一张牌，此阶段手牌上限-1"),C("discard","令其弃置一张牌，此阶段手牌上限+1")];
        if(r.Stage==LuYuShengStage.BottomChoice)
        {
            var choices=r.BottomCards.Where(id=>_cardZones.GetLocation(id)==CardLocation.DrawPile&&!r.Selected.Contains(id)).Select(id=>C("pick",$"弃置【{GetAdvancedCard(id).DisplayName}】",id)).ToList();
            choices.AddRange(r.Selected.Select(id=>C("unpick",$"撤回【{GetAdvancedCard(id).DisplayName}】",id)));choices.Add(C("finish",$"完成（弃置{r.Selected.Count}张）"));return Array.AsReadOnly(choices.ToArray());
        }
        if(r.Stage!=LuYuShengStage.RunweiDiscardChoice||!_players[r.Payer].IsAlive)return [];
        return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id,r.Payer,r.Payer,[CardZoneKind.Hand,CardZoneKind.Equipment],OwnedCardMoveIntent.Discard,
            canSelect:(zone,card)=>!IsSelfHandCategoryDiscardForbidden(r.Payer,card,new(zone,r.Payer),OwnedCardMoveIntent.Discard)).Select(c=>c with{Parameters=new Dictionary<string,string>(c.Parameters){["program-action"]="lu-yu-sheng",["option"]="pay"}}).ToArray());
    }
    private void PublishLuYuSheng(ProgramSkillFrame f)
    {
        var choices=LuYuShengChoices(f);if(choices.Count==0){IssueLuYuShengRunwei(f,1);FinishLuYuSheng(f);return;}var chooser=LuYuShengChooser(f);var skill=_contentRegistry.GetSkill(f.SkillId);
        PublishParticipantHandChoice(f,chooser,choices,f.LuYuSheng!.Stage==LuYuShengStage.BottomChoice?"藏心：选择弃置公开牌堆底中的任意张牌。":"润微：结算本次实际弃牌阶段。",f.LuYuSheng.Payer);
    }
    [ProgramAnswer("lu-yu-sheng")]
    private void AnswerLuYuSheng(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{LuYuSheng:{}r}f||_pendingDecision?.PlayerSeat!=LuYuShengChooser(f)||!AssistedChoicesEqual([choice],LuYuShengChoices(f).Where(c=>c.Id==choice.Id).ToArray()))throw new InvalidOperationException("Lu Yu Sheng lost its actual private chooser or original exposed entities.");
        AssertLuYuSheng(f);ClearPendingDecision();var option=choice.Parameters["option"];
        if(r.Stage==LuYuShengStage.RunweiChoice)
        {
            AdvanceEventRulesAndQueueFact(new LuYuShengRunweiChosenEvent(f.Id,r.Payer,option));ReplaceRuntimeTop(f=f with{LuYuSheng=r with{Branch=option}});
            if(option=="draw"){IssueLuYuShengDraw(f,1);return;}
            ReplaceRuntimeTop(f=f with{LuYuSheng=f.LuYuSheng! with{Stage=LuYuShengStage.RunweiDiscardChoice}});PublishLuYuSheng(f);return;
        }
        if(r.Stage==LuYuShengStage.BottomChoice)
        {
            if(option!="finish"){var id=choice.Cards.Single();ReplaceRuntimeTop(f=f with{LuYuSheng=r with{Selected=option=="pick"?r.Selected.Append(id).ToArray():r.Selected.Where(x=>x!=id).ToArray()}});PublishLuYuSheng(f);return;}
            if(r.Selected.Any(id=>_cardZones.GetLocation(id)!=CardLocation.DrawPile))throw new InvalidOperationException("Cangxin cannot discard a replaced or moved bottom entity.");
            ReplaceRuntimeTop(f=f with{LuYuSheng=r with{Stage=LuYuShengStage.BottomDiscardChildren,Count=r.Selected.Count(id=>GetAdvancedCard(id).Suit==Suit.Heart),Before=_movementSequence,After=_movementSequence},PendingMovementContinuation=new(r.Payer,0,null)});
            if(r.Selected.Count>0)MoveProgramCardsFromMultipleSources(r.Selected,CardLocation.DiscardPile,new(LuYuShengBottomDiscardReason));RecordLuYuShengMovement(f.Id);ContinueLuYuSheng(f.Id);return;
        }
        if(!Enum.TryParse<CardZoneKind>(choice.Parameters.GetValueOrDefault("source-zone"),out var zone)||zone is not(CardZoneKind.Hand or CardZoneKind.Equipment)||!int.TryParse(choice.Parameters.GetValueOrDefault("slot-index"),out var slot))throw new InvalidOperationException("Runwei lost its real self-discard slot.");
        var from=new CardLocation(zone,r.Payer);var cards=_cardZones.CardsAt(from);if(slot<0||slot>=cards.Count)throw new InvalidOperationException("Runwei payer slot is absent.");var card=cards[slot];
        var grain=from.Zone==CardZoneKind.Equipment&&card.Kind==CardKind.WoodenOx?GetWoodenOxGrain(_players[r.Payer]).Select(c=>c.Id).ToArray():[];
        ReplaceRuntimeTop(f=f with{LuYuSheng=r with{Stage=LuYuShengStage.RunweiDiscardChildren,CardId=card.Id,From=from,Before=_movementSequence,After=_movementSequence,WoodenOxGrainIds=grain},PendingMovementContinuation=new(r.Payer,0,null)});
        MoveProgramLogicalDiscard(f,r.Payer,[card.Id],new(LuYuShengRunweiDiscardReason));RecordLuYuShengMovement(f.Id);IssueLuYuShengRunwei(GetActiveProgramFrame(f.Id),1);ContinueLuYuSheng(f.Id);
    }
    private void IssueLuYuShengRunwei(ProgramSkillFrame f,int delta)
    {
        var r=f.LuYuSheng!;if(r.Phase is not{Kind:ActualDiscardRecoveryPhaseKind.Discard}p||p.ActorSeat!=r.Payer||CurrentActualDiscardRecoveryPhase()!=p||!IsRecordedActualDiscardRecoveryPhase(p)||
            r.Operation!=SkillProgramEffectOp.RunweiDrawOrDiscardForActualPhase||r.Branch!=(delta<0?"draw":"discard")||ProgramEventHistory<LuYuShengRunweiPhaseIssuedEvent>().Any(e=>e.FrameId==f.Id))throw new InvalidOperationException("Runwei requires its exact accepted real Discard phase and one committed direction.");
        AdvanceEventRulesAndQueueFact(new LuYuShengRunweiPhaseIssuedEvent(f.Id,r.Source,p,delta));
    }
    private int LuYuShengRunweiHandLimitBonus(int actorSeat)=>_phase==TurnPhase.Discard&&CurrentActualDiscardRecoveryPhase()is{Kind:ActualDiscardRecoveryPhaseKind.Discard}p&&p.ActorSeat==actorSeat?
        ProgramEventHistory<LuYuShengRunweiPhaseIssuedEvent>().Where(e=>e.Phase==p).Sum(e=>e.Delta):0;
    private IReadOnlyList<CardSnapshot> CurrentLuYuShengRevealedCards()=>Array.AsReadOnly(_resolutionStack.OfType<ProgramSkillFrame>().Where(f=>f.LuYuSheng is not null).SelectMany(f=>f.LuYuSheng!.BottomCards).Distinct().Select(id=>ToSnapshot(GetAdvancedCard(id))).ToArray());
    private void IssueLuYuShengDraw(ProgramSkillFrame f,int count)
    {
        var r=f.LuYuSheng!;ReplaceRuntimeTop(f=f with{LuYuSheng=r with{Stage=LuYuShengStage.DrawChildren,Count=count,Before=_movementSequence,After=_movementSequence},PendingMovementContinuation=new(r.Payer,0,null)});
        if(count>0&&_players[r.Payer].IsAlive&&_winner==Winner.None)DrawProgramCards(f.Id,r.Payer,count,null,null,SkillProgramCardSetVisibility.Private,new(LuYuShengDrawReason));
        RecordLuYuShengMovement(f.Id);if(r.Operation==SkillProgramEffectOp.RunweiDrawOrDiscardForActualPhase)IssueLuYuShengRunwei(GetActiveProgramFrame(f.Id),-1);ContinueLuYuSheng(f.Id);
    }
    private void RecordLuYuShengMovement(long id)
    {
        var f=GetActiveProgramFrame(id);var r=f.LuYuSheng!;var after=_movementSequence;
        var shuffles=r.Stage==LuYuShengStage.DrawChildren?_pendingCardsMovedBatches.Where(b=>b.ParentFrameId==id&&b.ParentBatchId is null&&b.AwaitingProgramFrameId==id&&b.TurnNumber==r.Turn&&b.OriginOwnerSeat==f.OwnerSeat&&b.OriginSkillId==f.SkillId&&b.OriginSkillInstanceId==f.SkillInstanceId&&b.Movements.Count>0&&
            b.Movements.All(m=>m.Sequence>r.Before&&m.Sequence<=after&&m.From==CardLocation.DiscardPile&&m.To==CardLocation.DrawPile&&m.Reason==CardMoveReasons.Reshuffle&&_cardMovements.Contains(m))&&b.SourceCounts is [{Location:var source,CountBefore:var discarded,CountAfter:0}]&&source==CardLocation.DiscardPile&&discarded==b.Movements.Count&&b.DestinationCounts is [{Location:var destination,CountBefore:0,CountAfter:var replenished}]&&destination==CardLocation.DrawPile&&replenished==b.Movements.Count)
            .Select(b=>new LuYuShengReshuffle(b.Id,id,b.Movements[0].Sequence-1,b.Movements[^1].Sequence){Movements=b.Movements}).ToArray():[];
        var count=_cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=after&&(r.Stage!=LuYuShengStage.DrawChildren||m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(r.Payer)&&m.Reason.Value==LuYuShengDrawReason));
        ReplaceRuntimeTop(f with{LuYuSheng=r with{After=after,ActualCount=count,Reshuffles=shuffles}});foreach(var s in shuffles)AdvanceEventRulesAndQueueFact(new LuYuShengReshuffleCapturedEvent(id,s.BatchId,s.Before,s.After,s.Movements.Count));
        AdvanceEventRulesAndQueueFact(new LuYuShengMovementIssuedEvent(id,r.Stage,r.Payer,r.Before,after,count));
    }
    private bool DrainLuYuSheng(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueLuYuSheng(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{LuYuSheng:{}r}f||f.Id!=id)return;AssertLuYuSheng(f);
        if(r.Stage is LuYuShengStage.BottomChoice or LuYuShengStage.RunweiChoice or LuYuShengStage.RunweiDiscardChoice){if(_pendingDecision is null)PublishLuYuSheng(f);return;}
        if(DrainLuYuSheng(f))return;ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null});if(r.Stage==LuYuShengStage.BottomDiscardChildren)ApplyLuYuShengReduction(f);FinishLuYuSheng(GetActiveProgramFrame(id));
    }
    private void FinishLuYuSheng(ProgramSkillFrame f){AdvanceEventRulesAndQueueFact(new LuYuShengCompletedEvent(f.Id,true));ReplaceRuntimeTop(f=f with{LuYuSheng=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,26900)]private bool ResumeLuYuSheng(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{LuYuSheng:not null}f||f.Id!=id)return false;ContinueLuYuSheng(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,26900)]private bool ReturnLuYuSheng(ProgramSkillFrame f){if(f.LuYuSheng is null)return false;ContinueLuYuSheng(f.Id);return true;}
    private bool IsLuYuShengAwaitedMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>f.LuYuSheng is{}r&&p.SubjectSeat==r.Payer&&p.BeforeCount==0&&p.CoverageResultBind is null&&(e is null||LuYuShengComposition.IsOperation(e.Op));
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,26900)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,26900)]private bool ContinuePaidLuYuSheng(ProgramSkillFrame f){if(f.LuYuSheng is null)return false;AssertLuYuSheng(f);return true;}
    [ProgramAiChoice(SkillProgramEffectOp.RevealBottomHeartsAndDraw,SkillProgramEffectOp.DiscardBottomHeartsForDamageReduction,SkillProgramEffectOp.RunweiDrawOrDiscardForActualPhase)]
    private PromptChoice AiLuYuSheng(PendingDecision p,ProgramSkillFrame f)=>p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("option")=="pick"&&GetAdvancedCard(c.Cards.Single()).Suit==Suit.Heart)??p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("option")=="finish")??p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("option")=="draw")??p.Choices.First();
}
