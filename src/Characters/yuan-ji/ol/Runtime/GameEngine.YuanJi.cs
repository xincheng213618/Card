using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
 private const string YuanJiPairReason="skill-program.yuan-ji.jieyan.draw",YuanJiJinghuaReason="skill-program.yuan-ji.jinghua.draw",YuanJiShuiyueReason="skill-program.yuan-ji.shuiyue.draw",YuanJiBottomReason="skill-program.yuan-ji.jinghua.return",YuanJiTopReason="skill-program.yuan-ji.shuiyue.return";
 [ProgramCapability]private sealed class YuanJiCapability(GameEngine e):IYuanJiHost{public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.ResolveYuanJi(f,op);}
 private SkillProgramStepOutcome ResolveYuanJi(ProgramSkillFrame supplied,SkillProgramEffectOp op)
 {
  var f=GetActiveProgramFrame(supplied.Id);if(f.YuanJi is not null||f.InstructionIndex!=1||GetPausedPrivateOfferEffect(f)?.Op!=op||f.WindowContext is not{}c)throw new InvalidOperationException("YuanJi requires the exact first committed native instruction and lifecycle window.");
  var candidate=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,0,c.OccurrenceIndex);if(!YuanJiWindowCandidate(candidate,c,out var parent)||_resolutionStack.Count<2||_resolutionStack[^2].Id!=parent.Id)throw new InvalidOperationException("YuanJi lost its immediate native owning boundary.");
  var loss=op==SkillProgramEffectOp.ResolveJieyanOppositeEndDrawPair?JieyanOpportunity(candidate):null;if(op==SkillProgramEffectOp.ResolveJieyanOppositeEndDrawPair&&!IsIssuedJieyanCandidate(candidate,c)||IsYuanJiReturnOp(op)&&!IsIssuedYuanJiReturn(candidate,c))throw new InvalidOperationException("YuanJi cannot invent an atomic Hand pair or a previous exact mark issuance.");
  var r=new YuanJiReceipt(YuanJiSource(f),f.GameplayHash,op,parent.Id,c.Window,_turnNumber,_turnProgression.OwnerSeat,_roundNumber,IsYuanJiReturnOp(op)?YuanJiStage.ReturnOrder:YuanJiStage.Drawing,loss?.LoserSeat??-1,loss?.Index??-1);
  if(IsYuanJiReturnOp(op)){var claims=YuanJiActiveClaims(f.OwnerSeat,f.SkillId).ToArray();r=r with{Claims=claims,Eligible=claims.Where(a=>_cardZones.GetLocation(a.CardId)==CardLocation.Hand(f.OwnerSeat)).Select(a=>a.CardId).Distinct().ToArray()};}
  ReplaceRuntimeTop(f=f with{YuanJi=r});AdvanceEventRulesAndQueueFact(new YuanJiStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.ParentId,r.Window,r.ActualTurn,r.TurnOwner,r.Round,r.LoserSeat,r.OpportunityIndex));foreach(var p in _players)_observedSkillGrantRevisions.TryAdd(p.Seat,p.SkillGrants.Revision);ContinueYuanJi(f.Id);return SkillProgramStepOutcome.AwaitChild;
 }
 private IReadOnlyList<int> DrawYuanJiEnd(int seat,bool top,CardConversionSource source)
 {
  var reason=new CardMoveReason(YuanJiPairReason);var draft=CaptureNativeDrawInvocation(seat,1,reason,source);var ids=new List<int>();
  if(_players[seat].IsAlive&&EnsureDrawPile()){var pile=_cardZones.CardsAt(CardLocation.DrawPile);var card=top?pile[^1]:pile[0];MoveCard(card,CardLocation.DrawPile,CardLocation.Hand(seat),reason);ids.Add(card.Id);}
  CompleteNativeDrawInvocation(draft,ids);return ids;
 }
 private void BeginYuanJiDraw(ProgramSkillFrame f)
 {
  var r=f.YuanJi!;var pair=r.Op==SkillProgramEffectOp.ResolveJieyanOppositeEndDrawPair;var seat=pair&&r.Cursor==1?r.LoserSeat:f.OwnerSeat;if(!_players[f.OwnerSeat].IsAlive||!_players[seat].IsAlive||_winner!=Winner.None){FinishYuanJi(f);return;}
  var before=_movementSequence;var top=pair?r.Cursor==0:!HasCardPolicy(_players[seat],SkillProgramCardPolicyKind.DrawFromBottom);var requested=pair?1:2;ReplaceRuntimeTop(f=f with{YuanJi=r with{Stage=YuanJiStage.DrawChildren,Before=before,After=before},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  var ids=pair?DrawYuanJiEnd(seat,top,r.Source):DrawCards(_players[seat],2,true,new(r.Op==SkillProgramEffectOp.ResolveJinghuaRoundDraw?YuanJiJinghuaReason:YuanJiShuiyueReason),r.Source);var after=_movementSequence;var card=pair&&ids.Count==1?GetAdvancedCard(ids[0]):null;var step=new YuanJiDrawStep(r.Cursor,seat,top,requested,ids.Count,before,after,card?.Id,card?.Suit);
  f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{YuanJi=f.YuanJi! with{After=after,Draws=f.YuanJi!.Draws.Append(step).ToArray()}});AdvanceEventRulesAndQueueFact(new YuanJiDrawCompletedEvent(f.Id,r.Cursor,seat,top,requested,ids.Count,before,after));
  if(pair&&card is not null){var move=_cardMovements.Single(m=>m.Sequence>before&&m.Sequence<=after&&m.CardId==card.Id&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(seat)&&m.Reason.Value==YuanJiPairReason);AdvanceEventRulesAndQueueFact(new YuanJiShownEvent(f.Id,r.Cursor,seat,card.Id,card.Suit,card.Rank,move.Sequence));AdvanceEventRulesAndQueueFact(new CardsRevealedEvent(f.Id,[ToSnapshot(card)]));}
  else if(!pair)for(var i=0;i<ids.Count;i++){var move=_cardMovements.Single(m=>m.Sequence>before&&m.Sequence<=after&&m.CardId==ids[i]&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(seat)&&m.Reason.Value==(r.Op==SkillProgramEffectOp.ResolveJinghuaRoundDraw?YuanJiJinghuaReason:YuanJiShuiyueReason));AdvanceEventRulesAndQueueFact(new YuanJiAcquiredEvent(f.Id,i,ids[i],move.Sequence));}
 }
 private void ContinueYuanJi(long id)
 {
  while(_resolutionStack.LastOrDefault()is ProgramSkillFrame{YuanJi:{}r}f&&f.Id==id)
  {
   AssertYuanJi(f);
   if(r.Stage is YuanJiStage.DrawChildren or YuanJiStage.ReturnChildren){if(DrainYuanJi(f))return;f=GetActiveProgramFrame(id);r=f.YuanJi!;if(r.Stage==YuanJiStage.ReturnChildren){FinishYuanJi(f);return;}ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null,YuanJi=r with{Cursor=r.Cursor+1,Stage=YuanJiStage.Drawing,Before=0,After=0}});r=f.YuanJi!;}
   if(r.Stage==YuanJiStage.ReturnOrder){if(_pendingDecision is null){if(r.Ordered.Count==r.Eligible.Count)PayYuanJiReturn(f);else PublishYuanJiOrder(f);}return;}
   var count=r.Op==SkillProgramEffectOp.ResolveJieyanOppositeEndDrawPair?2:1;if(r.Cursor<count){BeginYuanJiDraw(f);continue;}
   if(r.Op==SkillProgramEffectOp.ResolveJieyanOppositeEndDrawPair&&r.Draws is[{Suit:{}first},{Suit:{}second}]&&first!=second)AdvanceEventRulesAndQueueFact(new YuanJiJieyanDisabledEvent(f.Id,f.OwnerSeat,r.ActualTurn,r.Round,first,second));FinishYuanJi(f);return;
  }
 }
 private IReadOnlyList<PromptChoice> YuanJiOrderChoices(ProgramSkillFrame f)=>Array.AsReadOnly(f.YuanJi!.Eligible.Where(id=>!f.YuanJi.Ordered.Contains(id)).Select(id=>new PromptChoice(new($"yuan-ji.{f.Id}.{f.YuanJi.Ordered.Count}.{id}"),$"先置{(f.YuanJi.Op==SkillProgramEffectOp.ResolveJinghuaTurnReturn?"底":"顶")}：{GetAdvancedCard(id).DisplayName}",[id],[],new Dictionary<string,string>{{"program-action","yuan-ji-order"},{"card-id",id.ToString(CultureInfo.InvariantCulture)}})).ToArray());
 private void PublishYuanJiOrder(ProgramSkillFrame f)
 {var skill=_contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});_pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"选择原标记牌回置次序，第一张最先从该端摸到。",f.YuanJi!.Eligible,[],f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=true,Choices=YuanJiOrderChoices(f),SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};_status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();}
 [ProgramAnswer("yuan-ji-order")]private void AnswerYuanJiOrder(PromptChoice choice)
 {if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{YuanJi:{Stage:YuanJiStage.ReturnOrder}r}f||!AssistedChoicesEqual(YuanJiOrderChoices(f).Where(c=>c.Id==choice.Id).ToArray(),[choice]))throw new InvalidOperationException("YuanJi return order requires the exact current private original entities.");AssertYuanJi(f);ClearPendingDecision();var id=choice.Cards.Single();AdvanceEventRulesAndQueueFact(new YuanJiReturnSelectedEvent(f.Id,r.Ordered.Count,id));ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect,YuanJi=r with{Ordered=r.Ordered.Append(id).ToArray()}});if(f.YuanJi!.Ordered.Count==f.YuanJi.Eligible.Count)PayYuanJiReturn(f);else PublishYuanJiOrder(f);}
 private void PayYuanJiReturn(ProgramSkillFrame f)
 {
  var r=f.YuanJi!;if(r.Ordered.Count!=r.Eligible.Count||r.Ordered.Distinct().Count()!=r.Ordered.Count||!r.Ordered.Order().SequenceEqual(r.Eligible.Order())||r.Ordered.Any(id=>_cardZones.GetLocation(id)!=CardLocation.Hand(f.OwnerSeat)))throw new InvalidOperationException("YuanJi cannot replace or reclaim a foreign-held mark entity.");var before=_movementSequence;var top=r.Op==SkillProgramEffectOp.ResolveShuiyueRoundReturn;
  foreach(var c in r.Claims)AdvanceEventRulesAndQueueFact(new YuanJiMarkClosedEvent(f.Id,c.DrawFrameId,c.Index,c.CardId,r.Eligible.Contains(c.CardId)));
  ReplaceRuntimeTop(f=f with{YuanJi=r with{Stage=YuanJiStage.ReturnChildren,Before=before,After=before},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  if(r.Ordered.Count>0){MoveCards(r.Ordered.Select(GetAdvancedCard).ToArray(),CardLocation.Hand(f.OwnerSeat),CardLocation.DrawPile,new(top?YuanJiTopReason:YuanJiBottomReason));if(top)_cardZones.PlaceDrawPileCardsAtTop(r.Ordered);else _cardZones.PlaceDrawPileCardsAtBottom(r.Ordered);}
  f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{YuanJi=f.YuanJi! with{After=_movementSequence}});AdvanceEventRulesAndQueueFact(new YuanJiReturnPaidEvent(f.Id,r.Ordered.Count,top,before,_movementSequence));ContinueYuanJi(f.Id);
 }
 private bool DrainYuanJi(ProgramSkillFrame f)=>TryBeginAdvancedSkillsChanged(f.Id)||TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id);
 private void FinishYuanJi(ProgramSkillFrame f){AdvanceEventRulesAndQueueFact(new YuanJiCompletedEvent(f.Id,f.YuanJi!.Op));ReplaceRuntimeTop(f=f with{YuanJi=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
 [ProgramContinuation(ProgramContinuationKind.Resume,27540)]private bool ResumeYuanJi(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{YuanJi:not null}f||f.Id!=id)return false;ContinueYuanJi(id);return true;}
 [ProgramContinuation(ProgramContinuationKind.Movement,27540)]private bool ReturnYuanJiMovement(ProgramSkillFrame f){if(f.YuanJi is null)return false;ContinueYuanJi(f.Id);return true;}
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27540)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27540)]private bool ContinueIssuedYuanJi(ProgramSkillFrame f)
 {if(f.YuanJi is not null){AssertYuanJi(f);return true;}if(f.InstructionIndex is not(0 or 1)||f.TriggerId is null||f.WindowContext is not{}c)return false;var candidate=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,0,c.OccurrenceIndex);return IsIssuedJieyanCandidate(candidate,c)||IsIssuedYuanJiReturn(candidate,c);}
 [ProgramAiChoice(SkillProgramEffectOp.ResolveJinghuaTurnReturn,SkillProgramEffectOp.ResolveShuiyueRoundReturn)]private PromptChoice AiYuanJi(PendingDecision p,ProgramSkillFrame f)=>p.Choices.First();
}
