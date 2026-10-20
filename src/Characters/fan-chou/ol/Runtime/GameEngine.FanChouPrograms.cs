namespace CardGame.Core;
public sealed partial class GameEngine
{
 private const string FanChouDiscardReason="skill-program.xingluan.recipient.discard";
 private const string FanChouDrawReason="skill-program.xingluan.fallback-draw",FanChouFieldReason="skill-program.xingluan.field-gain",FanChouDeckReason="skill-program.xingluan.deck-gain",FanChouGiftReason="skill-program.xingluan.recipient-gift";
 private bool HasFanChouCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ChooseXingluanRankSixBenefit);
 private static CardConversionSource FanChouSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
 private bool FanChouSourceCurrent(ProgramSkillFrame f)=>_players[f.OwnerSeat].IsAlive&&HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)&&_contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash==f.GameplayHash;
 private bool TryFanChouUse(int owner,ProgramSkillWindowContext context,out FanChouUseProof proof)
 {
  proof=null!;if(_phase!=TurnPhase.Play||_turnNumber<1||owner!=_currentSeat||context.Window!=SkillProgramTriggerWindow.CardUseCompleted||context.CardUse is not{}identity||_resolutionStack.SingleOrDefault(f=>f.Id==context.ParentFrameId)is not ProgramCardTriggerWindowFrame window||GetCardActionWindow(window)!=SkillProgramTriggerWindow.CardUseCompleted||window.Action is not{}a||a.ActionId!=identity.CardActionId||a.ActorSeat!=owner)return false;
  if(a.Type==CardActionType.Use)
  {
   if(window.CompletedResponseReturn is not null||LifecycleCardUse(window.ParentFrameId)is not{Step:ResolutionFrameStep.Completed,Action:{}native}use||!SameOverflowAction(native,a)||!HasExactAcceptedActualHandGainUse(use,a)||!CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Any(e=>e.ResolutionId==use.Id&&e.CardId==use.CardId&&e.CardKind==use.CardKind))return false;
  }
  else if(window.CompletedResponseReturn is not{IsCommitted:false}ret||!IsCompletedResponseUse(a,ret.Kind)||!IsCompletedResponseParentConsistent(window)||!CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Any(e=>SameOverflowAction(e.Action,a)))return false;
  proof=new(window.Id,window.ParentFrameId,_turnNumber,_cardUseDebitPhaseInstanceId,a,window.CompletedResponseReturn?.Kind);return true;
 }
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 460)]
 private bool CanOfferFanChou(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext w)=>!ProgramInstructionResolver.Default.Features(t).HasOperation(SkillProgramEffectOp.ChooseXingluanRankSixBenefit)||TryFanChouUse(c.OwnerSeat,w,out _);
 private SkillProgramStepOutcome BeginFanChou(ProgramSkillFrame supplied,SkillProgramEffect effect)
 {
  var f=GetActiveProgramFrame(supplied.Id);if(f.FanChou is not null||f.InstructionIndex!=1||f.WindowContext is not{}w||!TryFanChouUse(f.OwnerSeat,w,out var use)||!FanChouSourceCurrent(f))throw new InvalidOperationException("Xingluan lost its unpaid actual completed own-Play use.");ReplaceRuntimeTop(f=f with{FanChou=new(FanChouSource(f),f.GameplayHash,f.InstructionIndex,use,FanChouStage.Branch)});PublishFanChou(f);return SkillProgramStepOutcome.AwaitChoice;
 }
 private IReadOnlyList<Card> FanChouFieldCards(int owner)=>_players.Where(p=>p.IsAlive).SelectMany(p=>new[]{CardLocation.Equipment(p.Seat),CardLocation.Judgment(p.Seat)}).SelectMany(_cardZones.CardsAt).Where(c=>!c.IsGeneralWeapon&&EffectiveOwnedCardRank(_players[_cardZones.GetLocation(c.Id).OwnerSeat!.Value],c)==6&&!IsForeignEquipmentDiscardPrevented(owner,c,_cardZones.GetLocation(c.Id),OwnedCardMoveIntent.Obtain)).ToArray();
 private PromptChoice FanChouChoice(ProgramSkillFrame f,string step,string branch,IReadOnlyList<int>?cards=null,IReadOnlyList<int>?targets=null,string?label=null)=>new(new($"xingluan.{f.Id}.{step}.{branch}."+string.Join('-',cards??[])+"."+string.Join('-',targets??[])),label??branch,cards??[],targets??[],new Dictionary<string,string>{{"program-action","xingluan"},{"step",step},{"branch",branch}});
 private IReadOnlyList<PromptChoice> FanChouChoices(ProgramSkillFrame f)
 {
  var r=f.FanChou!;var result=new List<PromptChoice>();
  if(r.Stage==FanChouStage.Branch){if(FanChouFieldCards(f.OwnerSeat).Count>0)result.Add(FanChouChoice(f,"branch","field",label:"获得场上一张点数6的牌"));result.Add(FanChouChoice(f,"branch","deck",label:"选择牌堆中的点数6牌，没有则摸一张"));if(_players.Any(p=>p.IsAlive&&p.Seat!=f.OwnerSeat))result.Add(FanChouChoice(f,"branch","other",label:"令其他角色弃点数6牌或交牌"));}
  else if(r.Stage==FanChouStage.Field)result.AddRange(FanChouFieldCards(f.OwnerSeat).Select(c=>FanChouChoice(f,"field","take",[c.Id],label:c.DisplayName)));
  else if(r.Stage==FanChouStage.Deck)result.AddRange(r.DeckCandidates.Where(id=>_cardZones.GetLocation(id)==CardLocation.DrawPile&&GetAdvancedCard(id).Rank==6).Select(id=>FanChouChoice(f,"deck","take",[id],label:GetAdvancedCard(id).DisplayName)));
  else if(r.Stage==FanChouStage.Other)result.AddRange(_players.Where(p=>p.IsAlive&&p.Seat!=f.OwnerSeat).Select(p=>FanChouChoice(f,"other","target",targets:[p.Seat],label:p.Name)));
  else if(r.Stage==FanChouStage.OtherPayment&&r.Recipient is{}seat&&_players[seat].IsAlive)
  {
   result.AddRange(BuildOwnedCardPaymentChoices(f.Id,seat,seat,[CardZoneKind.Hand,CardZoneKind.Equipment],OwnedCardMoveIntent.Discard,canSelect:(_,c)=>!c.IsGeneralWeapon&&EffectiveOwnedCardRank(_players[seat],c)==6).Select(c=>FanChouChoice(f,"payment","discard",c.Cards,label:"弃置 "+c.Label)));
   result.AddRange(BuildOwnedCardPaymentChoices(f.Id,seat,seat,[CardZoneKind.Hand,CardZoneKind.Equipment],OwnedCardMoveIntent.Transfer,canSelect:(_,c)=>!c.IsGeneralWeapon).Select(c=>FanChouChoice(f,"payment","give",c.Cards,label:"交给兴乱拥有者 "+c.Label)));
  }
  return Array.AsReadOnly(result.ToArray());
 }
 private void PublishFanChou(ProgramSkillFrame f)
 {
  var r=f.FanChou!;var choices=FanChouChoices(f);if(choices.Count==0){FinishFanChou(f);return;}var seat=r.Stage==FanChouStage.OtherPayment?r.Recipient!.Value:f.OwnerSeat;var skill=_contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});_pendingDecision=new(DecisionKind.ProgramTrigger,seat,"选择兴乱的实际结算。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=true,TargetSeat=r.Recipient,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};_status=_players[seat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 [ProgramAiChoice(SkillProgramEffectOp.ChooseXingluanRankSixBenefit)]
 private PromptChoice FanChouAiChoice(ProgramSkillFrame f)=>FanChouChoices(f).First();
 [ProgramAnswer("xingluan")]
 private void ResolveFanChouChoice(PromptChoice choice)
 {
  var f=(ProgramSkillFrame)_resolutionStack.Last();RequireFanChou(f);var r=f.FanChou!;var seat=r.Stage==FanChouStage.OtherPayment?r.Recipient!.Value:f.OwnerSeat;
  if(_pendingDecision?.PlayerSeat!=seat||!AssistedChoicesEqual([choice],FanChouChoices(f).Where(c=>c.Id==choice.Id).ToArray()))throw new InvalidOperationException("Xingluan choice changed its actual payer, private candidate or entity.");ClearPendingDecision();if(!FanChouSourceCurrent(f)){FinishFanChou(f);return;}
  var branch=choice.Parameters["branch"];
  if(r.Stage==FanChouStage.Branch)
  {
   AdvanceEventRulesAndQueueFact(new XingluanBranchChosenEvent(f.Id,r.Source,r.GameplayHash,r.Use.ActualTurn,r.Use.WindowId,r.Use.Action.ActionId,branch));
   if(branch=="deck")
   {
    var candidates=_cardZones.CardsAt(CardLocation.DrawPile).Reverse().Where(c=>c.Rank==6).Take(2).Select(c=>c.Id).ToArray();r=r with{Branch=branch,Stage=FanChouStage.Deck,DeckCandidates=candidates};ReplaceRuntimeTop(f=f with{FanChou=r});if(candidates.Length==0){PayFanChouDraw(f);return;}
   }
   else{r=r with{Branch=branch,Stage=branch=="field"?FanChouStage.Field:FanChouStage.Other};ReplaceRuntimeTop(f=f with{FanChou=r});}PublishFanChou(f);return;
  }
  if(r.Stage==FanChouStage.Other){ReplaceRuntimeTop(f=f with{FanChou=r with{Recipient=choice.Targets.Single(),Stage=FanChouStage.OtherPayment}});PublishFanChou(f);return;}
  var id=choice.Cards.Single();var from=_cardZones.GetLocation(id);var payment=r.Stage==FanChouStage.OtherPayment;var actualBranch=payment?"other-"+branch:r.Branch!;var subject=payment&&branch=="discard"?seat:f.OwnerSeat;
  var to=payment&&branch=="discard"?CardLocation.DiscardPile:CardLocation.Hand(f.OwnerSeat);var reason=actualBranch switch{"field"=>new CardMoveReason(FanChouFieldReason),"deck"=>new CardMoveReason(FanChouDeckReason),"other-give"=>new CardMoveReason(FanChouGiftReason),_=>new CardMoveReason(FanChouDiscardReason)};
  if((payment&&(from.OwnerSeat!=seat||from.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment)))||r.Stage==FanChouStage.Field&&!FanChouFieldCards(f.OwnerSeat).Any(c=>c.Id==id)||r.Stage==FanChouStage.Deck&&(!r.DeckCandidates.Contains(id)||from!=CardLocation.DrawPile))throw new InvalidOperationException("Xingluan cannot substitute an owned zone or private deck entity.");
  r=r with{Branch=actualBranch,Stage=FanChouStage.MovementChildren,SelectedId=id,SelectedRank=from.OwnerSeat is{}originalOwner?EffectiveOwnedCardRank(_players[originalOwner],GetAdvancedCard(id)):GetAdvancedCard(id).Rank,From=from,Before=_movementSequence,After=_movementSequence,ActualCount=1};ReplaceRuntimeTop(f with{FanChou=r,PendingMovementContinuation=new(subject,0,null),Step=ResolutionFrameStep.ResolvingEffect});
   var logicalDiscard=payment&&branch=="discard"?CaptureNativeLogicalDiscardInvocation(f.OwnerSeat,seat,[id],reason,f.Id):null;
   MoveProgramCardsFromMultipleSources([id],to,reason,(batchId,moves)=>{var paid=r with{After=moves.Single().Sequence,PaidBatchId=batchId};ReplaceRuntimeTop(GetActiveProgramFrame(f.Id)with{FanChou=paid});IssueFanChouPaid(f.Id,paid,subject);});CompleteNativeLogicalDiscardInvocation(logicalDiscard);AdvanceRuntimeProgram(f.Id);
 }
 private void PayFanChouDraw(ProgramSkillFrame f)
 {
  var r=f.FanChou! with{Stage=FanChouStage.DrawChildren,Before=_movementSequence,After=_movementSequence,ActualCount=0};ReplaceRuntimeTop(f with{FanChou=r,PendingMovementContinuation=new(f.OwnerSeat,0,null),Step=ResolutionFrameStep.ResolvingEffect});var cards=DrawCards(_players[f.OwnerSeat],1,true,new(FanChouDrawReason));r=r with{After=_movementSequence,ActualCount=cards.Count};ReplaceRuntimeTop(GetActiveProgramFrame(f.Id)with{FanChou=r});IssueFanChouPaid(f.Id,r,f.OwnerSeat);AdvanceRuntimeProgram(f.Id);
 }
 private void IssueFanChouPaid(long id,FanChouReceipt r,int subject)=>AdvanceEventRulesAndQueueFact(new XingluanSegmentPaidEvent(id,r.Source,r.GameplayHash,r.Use.WindowId,r.Use.Action.ActionId,r.Branch!,subject,r.Before,r.After,r.ActualCount){PaidBatchId=r.PaidBatchId});
 private void FinishFanChou(ProgramSkillFrame f){ReplaceRuntimeTop(f=f with{FanChou=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
 [ProgramContinuation(ProgramContinuationKind.Resume,24200)] private bool ResumeFanChou(long id)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{FanChou:{Stage:FanChouStage.MovementChildren or FanChouStage.DrawChildren}}f||f.Id!=id)return false;RequireFanChou(f);if(TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id))return true;FinishFanChou(f);return true;
 }
 [ProgramContinuation(ProgramContinuationKind.Movement,24200)] private bool ReturnFanChouMovement(ProgramSkillFrame f)=>f.FanChou is not null&&ResumeFanChou(f.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 240)]
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 240)]
 private bool CanContinueFanChou(ProgramSkillFrame f){if(f.FanChou is not{Stage:FanChouStage.MovementChildren or FanChouStage.DrawChildren})return false;RequireFanChou(f);return true;}
 [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 240)]
 private bool IsFanChouMovement(ProgramSkillFrame f,SkillProgramEffect?e,ProgramMovementContinuation p)=>f.FanChou is{}r&&e?.Op==SkillProgramEffectOp.ChooseXingluanRankSixBenefit&&CanContinueFanChou(f)&&p.SubjectSeat==(r.Branch=="other-discard"?r.Recipient!.Value:f.OwnerSeat)&&p.BeforeCount==0&&p.CoverageResultBind is null;
 private void RequireFanChou(ProgramSkillFrame f)
 {
  if(f.FanChou is not{}r||r.Source!=FanChouSource(f)||r.GameplayHash!=f.GameplayHash||r.InstructionIndex!=f.InstructionIndex||r.Use.WindowId!=f.WindowContext?.ParentFrameId||r.Use.ActualTurn!=_turnNumber||r.Use.PhaseInstance!=_cardUseDebitPhaseInstanceId||r.Use.Action.ActorSeat!=f.OwnerSeat||r.DeckCandidates.Count>2||r.DeckCandidates.Distinct().Count()!=r.DeckCandidates.Count||!Enum.IsDefined(r.Stage)||f.WindowContext is not{}originalContext||!TryFanChouUse(f.OwnerSeat,originalContext,out var originalUse)||originalUse.WindowId!=r.Use.WindowId||originalUse.NativeParentId!=r.Use.NativeParentId||originalUse.ActualTurn!=r.Use.ActualTurn||originalUse.PhaseInstance!=r.Use.PhaseInstance||originalUse.ResponseKind!=r.Use.ResponseKind||!SameOverflowAction(originalUse.Action,r.Use.Action))throw new InvalidOperationException("Xingluan lost its exact source, phase, native action or private candidates.");
  if(r.Stage is not(FanChouStage.MovementChildren or FanChouStage.DrawChildren)){if(f.PendingMovementContinuation is not null||r.Before!=0||r.After!=0||r.PaidBatchId is not null)throw new InvalidOperationException("Unpaid Xingluan cannot borrow a movement invoice.");return;}
  var subject=r.Branch=="other-discard"?r.Recipient!.Value:f.OwnerSeat;
  if(r.Before>r.After||r.After>_movementSequence||r.ActualCount is <0 or >1||f.PendingMovementContinuation is not{SubjectSeat:var actor,BeforeCount:0,CoverageResultBind:null}||actor!=subject||CompleteProgramEventHistory().OfType<XingluanSegmentPaidEvent>().Count(e=>e==new XingluanSegmentPaidEvent(f.Id,r.Source,r.GameplayHash,r.Use.WindowId,r.Use.Action.ActionId,r.Branch!,subject,r.Before,r.After,r.ActualCount){PaidBatchId=r.PaidBatchId})!=1)throw new InvalidOperationException("Xingluan cannot resume an unissued or changed payment.");
  if(r.Stage==FanChouStage.DrawChildren){if(r.Branch!="deck"||r.DeckCandidates.Count!=0||r.PaidBatchId is not null||_cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==FanChouDrawReason)!=r.ActualCount)throw new InvalidOperationException("Xingluan fallback draw was not actually paid once.");return;}
  var to=r.Branch=="other-discard"?CardLocation.DiscardPile:CardLocation.Hand(f.OwnerSeat);var reason=r.Branch switch{"field"=>new CardMoveReason(FanChouFieldReason),"deck"=>new CardMoveReason(FanChouDeckReason),"other-give"=>new CardMoveReason(FanChouGiftReason),_=>new CardMoveReason(FanChouDiscardReason)};
  if(r.Branch is not("field" or "deck" or "other-give" or "other-discard")||r.PaidBatchId is not >0||r.SelectedId is not{}id||r.From is not{}from||r.ActualCount!=1||(r.Branch is "field" or "deck" or "other-discard")&&r.SelectedRank!=6||r.After<=r.Before||_cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.CardId==id&&m.From==from&&m.To==to&&m.Reason==reason)!=1||r.Branch=="field"&&(from.Zone is not(CardZoneKind.Equipment or CardZoneKind.Judgment)||GetAdvancedCard(id).Rank!=6)||r.Branch=="deck"&&(from!=CardLocation.DrawPile||!r.DeckCandidates.Contains(id)||GetAdvancedCard(id).Rank!=6)||r.Branch is "other-discard" or "other-give"&&(from.OwnerSeat!=r.Recipient||from.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment)))throw new InvalidOperationException("Xingluan payment lost its exact real entity and original legal zone.");
 }

 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 280)]
 private void AssertRegisteredFanChou(ProgramSkillFrame frame)
 {
     if (frame.FanChou is not null) RequireFanChou(frame);
 }
 private IReadOnlyList<CardSnapshot> FanChouPrivateDeckCards(int viewer)=>_resolutionStack.LastOrDefault()is ProgramSkillFrame{OwnerSeat:var owner,FanChou:{Stage:FanChouStage.Deck}r}&&owner==viewer?Array.AsReadOnly(r.DeckCandidates.Where(id=>_cardZones.GetLocation(id)==CardLocation.DrawPile).Select(id=>ToSnapshot(GetAdvancedCard(id))).ToArray()):[];

 [ProgramCapability]
 private sealed class FanChouProgramCapability(GameEngine engine) : IFanChouProgramHost
 {public SkillProgramStepOutcome BeginFanChou(ProgramSkillFrame f,SkillProgramEffect e)=>engine.BeginFanChou(f,e);}
}
