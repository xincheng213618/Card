namespace CardGame.Core;
public sealed partial class GameEngine
{
 private const string WenjiGiftReason="skill-program.wenji.gift",TunjiangDrawReason="skill-program.tunjiang.draw";
 private static CardConversionSource LiuQiSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 380)]
 private bool CanOfferLiuQi(ProgramTriggerCandidate candidate,SkillProgramTrigger trigger,ProgramSkillWindowContext context)
 {
  if(ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.DrawForUnskippedUntargetedPlay))return HasUntargetedUnskippedLiuQiPlay(candidate.OwnerSeat);
  if(ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.ReceiveWenjiGiftAndArmTurnName))return WenjiEligibleGiftRecipients(candidate.OwnerSeat,candidate.SkillId).Any();
  return true;
 }
 private SkillProgramStepOutcome BeginLiuQi(ProgramSkillFrame supplied,SkillProgramEffect e)
 {
  var f=GetActiveProgramFrame(supplied.Id);
  if(f.LiuQi is not null||f.InstructionIndex!=1||f.OwnerSeat!=_currentSeat||f.TriggerId is null||f.WindowContext is not{}w||w.Window!=(e.Op==SkillProgramEffectOp.ReceiveWenjiGiftAndArmTurnName?SkillProgramTriggerWindow.PlayPhaseStarting:SkillProgramTriggerWindow.TurnEnding)||!_players[f.OwnerSeat].IsAlive||!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId))throw new InvalidOperationException("LiuQi requires its current unpaid own native phase trigger.");
  var r=new LiuQiReceipt(LiuQiSource(f),f.GameplayHash,f.InstructionIndex,_turnNumber,w.ParentFrameId,e.Op==SkillProgramEffectOp.ReceiveWenjiGiftAndArmTurnName?LiuQiStage.ChooseRecipient:LiuQiStage.DrawChildren);
  if(e.Op==SkillProgramEffectOp.ReceiveWenjiGiftAndArmTurnName){ReplaceRuntimeTop(f=f with{LiuQi=r});PublishWenjiChoice(f);return SkillProgramStepOutcome.AwaitChoice;}
  if(!HasUntargetedUnskippedLiuQiPlay(f.OwnerSeat))throw new InvalidOperationException("Tunjiang lost its actual unskipped untargeted Play qualification.");
  r=r with{Before=_movementSequence,After=_movementSequence,RequestedDraw=GetLivingFactionCount()};ReplaceRuntimeTop(f with{LiuQi=r,PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  var actual=DrawCards(_players[f.OwnerSeat],r.RequestedDraw,true,new(TunjiangDrawReason)).Count;r=r with{After=_movementSequence,ActualDraw=actual};ReplaceRuntimeTop(GetActiveProgramFrame(f.Id)with{LiuQi=r});AdvanceEventRulesAndQueueFact(new TunjiangDrawPaidEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.WindowId,r.RequestedDraw,actual,r.Before,r.After));AdvanceRuntimeProgram(f.Id);return SkillProgramStepOutcome.AwaitChild;
 }
 private IReadOnlyList<PromptChoice> LiuQiChoices(ProgramSkillFrame f)
 {
  var r=f.LiuQi!;
  if(r.Stage==LiuQiStage.ChooseRecipient)return Array.AsReadOnly(WenjiEligibleGiftRecipients(f.OwnerSeat,f.SkillId).Select(p=>new PromptChoice(new($"wenji.{f.Id}.recipient.{p.Seat}"),p.Name,[],[p.Seat],new Dictionary<string,string>{{"program-action","wenji"},{"step","recipient"}})).ToArray());
  if(r.Stage!=LiuQiStage.ChooseGift||r.RecipientSeat is not{}seat)return [];
  return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id,seat,seat,[CardZoneKind.Hand,CardZoneKind.Equipment],OwnedCardMoveIntent.Transfer,canSelect:(_,c)=>!c.IsGeneralWeapon).Select(c=>new PromptChoice(new($"wenji.{f.Id}.gift.{c.Cards.Single()}"),c.Label,c.Cards,[],new Dictionary<string,string>{{"program-action","wenji"},{"step","gift"}})).ToArray());
 }
 [ProgramAiChoice(SkillProgramEffectOp.ReceiveWenjiGiftAndArmTurnName)]
 private PromptChoice LiuQiAiChoice(ProgramSkillFrame f)=>LiuQiChoices(f).First();
 private void FinishLiuQi(ProgramSkillFrame f){ReplaceRuntimeTop(f=f with{LiuQi=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
 [ProgramContinuation(ProgramContinuationKind.Resume,23300)] private bool ResumeLiuQi(long id)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{LiuQi.Stage:LiuQiStage.GiftChildren or LiuQiStage.DrawChildren}f||f.Id!=id)return false;RequireLiuQi(f);
  if(TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id))return true;FinishLiuQi(f);return true;
 }
 [ProgramContinuation(ProgramContinuationKind.Movement,23300)] private bool ReturnLiuQiMovement(ProgramSkillFrame f)=>f.LiuQi is not null&&ResumeLiuQi(f.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 160)]
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 160)]
 private bool CanContinueLiuQi(ProgramSkillFrame f){if(f.LiuQi is not{Stage:LiuQiStage.GiftChildren or LiuQiStage.DrawChildren})return false;RequireLiuQi(f);return true;}
 [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 160)]
 private bool IsLiuQiMovement(ProgramSkillFrame f,SkillProgramEffect?effect,ProgramMovementContinuation p)=>f.LiuQi is{Stage:LiuQiStage.GiftChildren or LiuQiStage.DrawChildren}&&effect is not null&&LiuQiComposition.IsOp(effect.Op)&&CanContinueLiuQi(f)&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null;
 private void RequireLiuQi(ProgramSkillFrame f)
 {
  if(f.LiuQi is not{}r||r.Source!=LiuQiSource(f)||r.GameplayHash!=f.GameplayHash||r.InstructionIndex!=f.InstructionIndex||r.ActualTurn!=_turnNumber||r.WindowId!=f.WindowContext?.ParentFrameId||r.Before>r.After||r.After>_movementSequence)throw new InvalidOperationException("LiuQi lost its exact source/hash/actual window invoice.");
  if(r.Stage is LiuQiStage.ChooseRecipient or LiuQiStage.ChooseGift){if(f.PendingMovementContinuation is not null||r.After!=0||r.Stage==LiuQiStage.ChooseGift&&(r.RecipientSeat is not{}seat||seat==f.OwnerSeat||!IsValidPlayerSeat(seat)))throw new InvalidOperationException("Wenji choice cannot masquerade as paid movement.");return;}
  if(f.PendingMovementContinuation is not{SubjectSeat:var actor,BeforeCount:0,CoverageResultBind:null}||actor!=f.OwnerSeat)throw new InvalidOperationException("LiuQi paid child lost its owning continuation.");
  if(r.Stage==LiuQiStage.GiftChildren)
  {
   if(r.CardId is not{}id||r.Name is not{}name||r.From is not{}from||r.RecipientSeat is not{}recipient||from.OwnerSeat!=recipient||from.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment)||r.After<=r.Before||CompleteProgramEventHistory().OfType<WenjiGiftPaidEvent>().Count(e=>e==new WenjiGiftPaidEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,recipient,id,from,name,r.Before,r.After))!=1||_cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.CardId==id&&m.From==from&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==WenjiGiftReason)!=1)throw new InvalidOperationException("Wenji lacks its exact once-paid recipient entity invoice.");
  }
  else if(r.Stage==LiuQiStage.DrawChildren&&(r.RequestedDraw<1||r.ActualDraw<0||r.ActualDraw>r.RequestedDraw||CompleteProgramEventHistory().OfType<TunjiangDrawPaidEvent>().Count(e=>e==new TunjiangDrawPaidEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.WindowId,r.RequestedDraw,r.ActualDraw,r.Before,r.After))!=1||_cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==TunjiangDrawReason)!=r.ActualDraw))throw new InvalidOperationException("Tunjiang draw lacks its exact native payment interval.");
 }

 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 200)]
 private void AssertRegisteredLiuQi(ProgramSkillFrame frame)
 {
     if (frame.LiuQi is not null) RequireLiuQi(frame);
 }
 [ProgramCapability]
 private sealed class LiuQiProgramCapability(GameEngine engine) : ILiuQiProgramHost
 {public SkillProgramStepOutcome BeginLiuQi(ProgramSkillFrame f,SkillProgramEffect e)=>engine.BeginLiuQi(f,e);}
}
