using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
 private const string GuolunExchangeReason="skill-program.guolun.exchange",SpPangTongDrawReason="skill-program.sp-pang-tong.draw";
 private static CardConversionSource SpPangTongSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
 private bool SpPangTongCurrent(ProgramSkillFrame f)=>_winner==Winner.None&&_players[f.OwnerSeat].IsAlive&&HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)&&_contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash==f.GameplayHash;
 private bool SpPangTongActivationTargetAllowed(CharacterState target,SkillProgramActivation activation)=>!ProgramInstructionResolver.Default.Features(activation).HasOperation(SkillProgramEffectOp.ResolveGuolunRevealOptionalAtomicExchange)||target.IsAlive&&GetHand(target).Count>0;
 private int[] MatchingZhanjiDrawIndexes(CardMovementBatchContext batch,ProgramTriggerCandidate candidate)
 {
  if(batch.NativeDrawInvocation is not{}p||p.LastBatchId!=batch.Id||p.ParentFrameId!=batch.ParentFrameId||p.OwnerSeat!=candidate.OwnerSeat||p.ActualCount<1||p.ActualTurnNumber!=batch.TurnNumber||p.ActualTurnOwnerSeat!=candidate.OwnerSeat||p.PhaseActorSeat!=candidate.OwnerSeat||p.Phase?.Kind!=ActualDiscardRecoveryPhaseKind.Play||!ValidNativeDrawInvocation(p)||!HasZhanjiDrawEntitlement(p,candidate.OwnerSeat,candidate.SkillId,candidate.BindingId,candidate.SkillInstanceId,candidate.GameplayHash)||p.DirectProducer is{}producer&&producer.OwnerSeat==candidate.OwnerSeat&&producer.SkillId==candidate.SkillId)return [];
  if(CompleteProgramEventHistory().OfType<SpPangTongSegmentPaidEvent>().Any(e=>e.Source.OwnerSeat==candidate.OwnerSeat&&e.Source.SkillId==candidate.SkillId&&e.DrawInvocation==p.InvocationId))return [];
  var last=p.Materials[^1];return batch.Movements.Select((m,i)=>(m,i)).Where(x=>x.m.Sequence==last.MovementSequence&&x.m.CardId==last.CardId&&x.m.CardKind==last.PrintedKind&&x.m.From==CardLocation.DrawPile&&x.m.To==CardLocation.Hand(candidate.OwnerSeat)&&x.m.Reason==p.Reason&&_cardMovements.Contains(x.m)).Select(x=>x.i).ToArray();
 }
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 510)]
 private bool CanOfferSpPangTong(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext w)
 {
  if(!t.Effects.Any(e=>SpPangTongComposition.Own(e.Op)))return true;
  if(!_players[c.OwnerSeat].IsAlive)return false;
  return t.Effects[0].Op switch{
   SkillProgramEffectOp.ResolveSongsangLimitedOtherDeath=>w.Window==SkillProgramTriggerWindow.CharacterDied&&w.TargetSeat is{}victim&&victim!=c.OwnerSeat&&IsValidPlayerSeat(victim)&&!_players[victim].IsAlive&&_players[c.OwnerSeat].HasHpValue&&_resolutionStack.OfType<ProgramKillTriggerWindowFrame>().Any(p=>p.Id==w.ParentFrameId&&p.VictimSeat==victim),
   SkillProgramEffectOp.DrawAfterActualPlayDrawInvocation=>w.Window==SkillProgramTriggerWindow.CardsGained&&w.MovementBatch is{}b&&MatchingZhanjiDrawIndexes(b,c).Length==1,
   _=>false};
 }
 private SkillProgramStepOutcome ExecuteSpPangTong(ProgramSkillFrame supplied,SkillProgramEffectOp op)
 {
  var f=GetActiveProgramFrame(supplied.Id);if(f.SpPangTong is not null||f.InstructionIndex!=1||!SpPangTongCurrent(f))throw new InvalidOperationException("SP Pang Tong requires its exact original live skill instruction.");
  var r=new SpPangTongReceipt(SpPangTongSource(f),f.GameplayHash,f.InstructionIndex,_turnNumber,"reveal",ParentId:f.WindowContext?.ParentFrameId);
  AdvanceEventRulesAndQueueFact(new SpPangTongStartedEvent(f.Id,r.Source,r.GameplayHash,r.Instruction,r.Turn,r.ParentId,op));
  if(op==SkillProgramEffectOp.ResolveGuolunRevealOptionalAtomicExchange)
  {
   if(f.TriggerId is not null||f.WindowContext is not null||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats is not[var target]||target==f.OwnerSeat||!IsValidPlayerSeat(target)||!_players[target].IsAlive||GetHand(_players[target]).Count==0)throw new InvalidOperationException("Guolun lost its real selected other hand owner.");
   ReplaceRuntimeTop(f=f with{SpPangTong=r with{Target=target,OriginalHand=GetHand(_players[target]).Select(c=>c.Id).ToArray()}});PublishSpPangTong(f);return SkillProgramStepOutcome.AwaitChoice;
  }
  if(f.WindowContext is not{}w)throw new InvalidOperationException("SP Pang Tong needs its original native window.");
  var trigger=_contentRegistry.GetSkill(f.SkillId).Program!.Triggers.Single(t=>t.Id==f.TriggerId);
  var candidate=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,f.TriggerId!,f.SkillInstanceId,f.GameplayHash,trigger.Priority);
  if(!CanOfferSpPangTong(candidate,trigger,w))throw new InvalidOperationException("SP Pang Tong lost its exact real death or draw invocation.");
  if(op==SkillProgramEffectOp.DrawAfterActualPlayDrawInvocation)
  {
   ReplaceRuntimeTop(f=f with{SpPangTong=r with{DrawInvocation=w.MovementBatch!.NativeDrawInvocation!.InvocationId,RewardSeat=f.OwnerSeat}});PaySpPangTongDraw(f);return SkillProgramStepOutcome.AwaitChild;
  }
  var owner=_players[f.OwnerSeat];var wounded=IsRuleWounded(owner);r=r with{Stage="health-children",Target=w.TargetSeat!.Value,Wounded=wounded,HpBefore=owner.Hp,MaximumBefore=owner.MaxHp,RuleWounded=UsesTreatedWounded?wounded:null};ReplaceRuntimeTop(f=f with{SpPangTong=r});AdvanceEventRulesAndQueueFact(new SongsangHealthIssuedEvent(f.Id,r.Source,r.GameplayHash,r.Target,r.Wounded,r.HpBefore,r.MaximumBefore,r.RuleWounded));
  if(r.Wounded)(new ProgramSkillHost(this)).Recover(f.Id,f.OwnerSeat,f.OwnerSeat,1,null,null);else ChangeProgramMaximumHp(f,1);
  AdvanceRuntimeProgram(f.Id);return SkillProgramStepOutcome.AwaitChild;
 }
 private IReadOnlyList<PromptChoice> SpPangTongChoices(ProgramSkillFrame f)
 {
  var r=f.SpPangTong!;var choices=new List<PromptChoice>();
  Dictionary<string,string> Args(string branch)=>new(){["program-action"]="sp-pang-tong",["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture),["branch"]=branch};
  if(r.Stage=="reveal")
  {
   foreach(var choice in BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,r.Target,[CardZoneKind.Hand],OwnedCardMoveIntent.Obtain))
   {var args=choice.Parameters.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);args["program-action"]="sp-pang-tong";args["branch"]="reveal";choices.Add(choice with{Parameters=args});}
  }
  else if(r.Stage=="exchange"&&r.RevealedCard is{}id&&_cardZones.GetLocation(id)==CardLocation.Hand(r.Target))
  {
   var grain=UsesFormalWoodenOx&&GetEquipment(_players[f.OwnerSeat]).Any(c=>c.Kind==CardKind.WoodenOx)?GetWoodenOxGrain(_players[f.OwnerSeat]):[];
   foreach(var card in GetHand(_players[f.OwnerSeat]).Concat(GetEquipment(_players[f.OwnerSeat])).Concat(grain))
   {
    var at=_cardZones.GetLocation(card.Id);if(card.IsGeneralWeapon||IsForeignEquipmentDiscardPrevented(f.OwnerSeat,card,at,OwnedCardMoveIntent.Transfer)||at.Zone==CardZoneKind.Equipment&&IsActiveProgramSourceEquipmentCard(f.OwnerSeat,f.SkillId,f.SkillInstanceId,card))continue;
    var args=Args("exchange");args["card-id"]=card.Id.ToString(CultureInfo.InvariantCulture);choices.Add(new(new($"guolun.{f.Id}.material.{card.Id}"),$"以{card.DisplayName}交换",[card.Id],[],args));
   }
   choices.Add(new(new($"guolun.{f.Id}.decline"),"不交换",[],[],Args("decline")));
  }
  return Array.AsReadOnly(choices.ToArray());
 }
 private void PublishSpPangTong(ProgramSkillFrame f)
 {
  if(!SpPangTongCurrent(f)||!_players[f.SpPangTong!.Target].IsAlive){FinishSpPangTong(f);return;}var choices=SpPangTongChoices(f);if(choices.Count==0){FinishSpPangTong(f);return;}
  var skill=_contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});_pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,f.SpPangTong.Stage=="reveal"?"过论：选择展示其一张手牌。":"过论：选择你的牌交换，或不交换。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),[],f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=true,TargetSeat=f.OwnerSeat,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};_status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 [ProgramAnswer("sp-pang-tong")] private void AnswerSpPangTong(PromptChoice selected)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{SpPangTong:{}r}f||!SpPangTongCurrent(f)||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!AssistedChoicesEqual([selected],SpPangTongChoices(f).Where(c=>c.Id==selected.Id).ToArray()))throw new InvalidOperationException("Guolun lost its actual private chooser and current material.");AssertSpPangTong(f);ClearPendingDecision();var branch=selected.Parameters["branch"];
  if(branch=="decline"){FinishSpPangTong(f);return;}
  if(branch=="reveal")
  {
   var slot=int.Parse(selected.Parameters["slot-index"],CultureInfo.InvariantCulture);var hand=GetHand(_players[r.Target]);if(slot<0||slot>=hand.Count||!hand.Select(c=>c.Id).SequenceEqual(r.OriginalHand))throw new InvalidOperationException("Guolun opaque hand slot changed before its actual reveal.");
   var card=hand[slot];ReplaceRuntimeTop(f=f with{SpPangTong=r with{Stage="exchange",RevealedCard=card.Id}});AdvanceEventRulesAndQueueFact(new CardsRevealedEvent(f.Id,[ToSnapshot(card)]));AdvanceEventRulesAndQueueFact(new GuolunRevealedEvent(f.Id,r.Source,r.Target,card.Id));PublishSpPangTong(f);return;
  }
  var own=GetAdvancedCard(int.Parse(selected.Parameters["card-id"],CultureInfo.InvariantCulture));var foreign=GetAdvancedCard(r.RevealedCard!.Value);var from=_cardZones.GetLocation(own.Id);var before=_movementSequence;
  r=r with{Stage="exchange-children",OwnCard=own.Id,OwnFrom=from,OwnRank=EffectiveOwnedCardRank(_players[f.OwnerSeat],own),TargetRank=EffectiveOwnedCardRank(_players[r.Target],foreign),Before=before,After=before};r=r with{RewardSeat=r.OwnRank<r.TargetRank?f.OwnerSeat:r.TargetRank<r.OwnRank?r.Target:null};ReplaceRuntimeTop(f=f with{SpPangTong=r,PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  var moves=new[]{(Card:own,From:from,To:CardLocation.Hand(r.Target)),(Card:foreign,From:CardLocation.Hand(r.Target),To:CardLocation.Hand(f.OwnerSeat))};var batch=BeginCardMovementBatch(moves.Select(m=>m.From),moves.Select(m=>m.To));var records=new List<CardMovementRecord>();var committed=false;
  try{
   foreach(var m in moves)_cardZones.Move(m.Card.Id,m.From,m.To);
   foreach(var m in moves){records.Add(RecordMovement(m.Card,m.From,m.To,new(GuolunExchangeReason)));ResolveEquipmentSkillGrant(m.Card,m.From,m.To);ClearJudgmentEffectiveKindAfterMove(m.Card,m.From,m.To);ResolveSilverLionRemoval(m.Card,m.From,new(GuolunExchangeReason));ResolveWoodenOxMove(m.Card,m.From,m.To);}committed=true;
  }finally{CompleteCardMovementBatch(batch,records,committed);}
  f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{SpPangTong=f.SpPangTong!with{After=_movementSequence,ActualCount=2,BatchId=batch.Id}});IssueSpPangTongSegment(f);AdvanceRuntimeProgram(f.Id);
 }
 private void IssueSpPangTongSegment(ProgramSkillFrame f){var r=f.SpPangTong!;AdvanceEventRulesAndQueueFact(new SpPangTongSegmentPaidEvent(f.Id,r.Source,r.GameplayHash,r.Instruction,r.Stage,r.Stage=="draw-children"?r.RewardSeat!.Value:r.Target,r.Before,r.After,r.ActualCount,r.DrawInvocation));}
 private void PaySpPangTongDraw(ProgramSkillFrame f)
 {
  var r=f.SpPangTong!;if(r.RewardSeat is not{}seat||!_players[seat].IsAlive||_winner!=Winner.None){FinishSpPangTong(f);return;}var before=_movementSequence;ReplaceRuntimeTop(f=f with{SpPangTong=r with{Stage="draw-children",Before=before,After=before,ActualCount=0},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  var drawn=DrawCards(_players[seat],1,true,new(SpPangTongDrawReason),r.Source);f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{SpPangTong=f.SpPangTong!with{After=_movementSequence,ActualCount=drawn.Count}});IssueSpPangTongSegment(f);AdvanceRuntimeProgram(f.Id);
 }
 private void FinishSpPangTong(ProgramSkillFrame f){ReplaceRuntimeTop(f=f with{SpPangTong=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
 private bool IsSpPangTongMovement(ProgramSkillFrame f,SkillProgramEffect?e,ProgramMovementContinuation p)=>e is not null&&SpPangTongComposition.Own(e.Op)&&f.SpPangTong is{Stage:"exchange-children" or "draw-children"}&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null;
 [ProgramContinuation(ProgramContinuationKind.Movement,24700)] private bool ReturnSpPangTongMovement(ProgramSkillFrame f)=>f.SpPangTong is not null&&ResumeSpPangTong(f.Id);
 [ProgramContinuation(ProgramContinuationKind.Resume,24700)] private bool ResumeSpPangTong(long id)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{SpPangTong:{}r}f||f.Id!=id)return false;AssertSpPangTong(f);
  if(r.Stage is "reveal" or "exchange"){if(!SpPangTongCurrent(f)){FinishSpPangTong(f);return true;}if(_pendingDecision is null)PublishSpPangTong(f);return true;}
  if(TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id))return true;
  f=GetActiveProgramFrame(id);r=f.SpPangTong!;if(r.Stage=="exchange-children"){ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null});PaySpPangTongDraw(f);return true;}
  if(r.Stage=="health-children"&&_players[f.OwnerSeat].IsAlive&&_winner==Winner.None){ReplaceRuntimeTop(f=f with{SpPangTong=r with{Stage="grant-children"}});AcquireRuntimeSkills(_players[f.OwnerSeat],f.SkillId,["ol:zhanji"]);AdvanceRuntimeProgram(id);return true;}
  FinishSpPangTong(f);return true;
 }
 [ProgramAiChoice(SkillProgramEffectOp.ResolveGuolunRevealOptionalAtomicExchange)] private PromptChoice SelectAiSpPangTong(PendingDecision p,ProgramSkillFrame f)=>p.Choices[0];
 [ProgramCapability]
 private sealed class SpPangTongProgramCapability(GameEngine engine) : ISpPangTongProgramHost
 {public SkillProgramStepOutcome ExecuteSpPangTong(ProgramSkillFrame f,SkillProgramEffectOp op)=>engine.ExecuteSpPangTong(f,op);}
}
