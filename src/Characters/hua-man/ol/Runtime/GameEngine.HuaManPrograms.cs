namespace CardGame.Core;
public sealed partial class GameEngine
{
 private const string HuaManDiscardReason="skill-program.huaman.discard",HuaManDrawReason="skill-program.huaman.draw",HuaManClaimReason="skill-program.huaman.claim";
 private SkillProgramStepOutcome ExecuteHuaMan(ProgramSkillFrame supplied,SkillProgramEffectOp op)
 {
  var f=GetActiveProgramFrame(supplied.Id);var w=f.WindowContext;var owner=_players[f.OwnerSeat];if(f.HuaMan is not null||f.InstructionIndex!=1||w is null||!HuaManCurrent(f))throw new InvalidOperationException("Hua Man requires the current unpaid native source and original instruction.");
  if(op==SkillProgramEffectOp.DrawAndCountMansiActualBarbarianDamage)
  {
   if(w.DamageFrameId is not{}damage||_resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(d=>d.Id==w.ParentFrameId)is not{}window||window.ParentFrameId!=damage||GetDamageTriggerAttack(window)is not{EffectiveCardKind:CardKind.BarbarianAssault,DamageWasApplied:true}attack||_resolutionStack.OfType<DamageFrame>().SingleOrDefault(d=>d.Id==damage)is not{}paid||paid.Amount!=w.Amount||paid.Amount<1||attack.ResolutionId!=paid.ParentFrameId)throw new InvalidOperationException("Mansi cannot claim another card kind, event or damage amount.");
   BeginHuaMan(f,HuaManStage.MansiDraw,op);PayHuaManDraw(GetActiveProgramFrame(f.Id),f.OwnerSeat,1,HuaManStage.MansiDraw);return SkillProgramStepOutcome.AwaitChild;
  }
  if(op==SkillProgramEffectOp.AwakenZhanyuanAndOfferXili)
  {
   if(owner.HuaManProgress is not{MansiCards:>7,Awakened:false}progress||CurrentActualDiscardRecoveryPhase()is not{Kind:ActualDiscardRecoveryPhaseKind.Preparation}phase||phase.ActorSeat!=f.OwnerSeat||f.OwnerSeat!=_currentSeat)throw new InvalidOperationException("Zhanyuan requires actual preparation and more than seven actually obtained Mansi cards.");owner.HuaManProgress=progress with{Awakened=true};BeginHuaMan(f,HuaManStage.MaximumChildren,op,true);ChangeProgramMaximumHp(GetActiveProgramFrame(f.Id),1);AdvanceRuntimeProgram(f.Id);return SkillProgramStepOutcome.AwaitChild;
  }
  if(op==SkillProgramEffectOp.PaySouyingDirectionalSoleTarget)
  {
   if(!ExactSouyingUse(f,out var use)||use.HuaManSouyingMutation is not null)throw new InvalidOperationException("Souying requires its actual directional sole designation.");BeginHuaMan(f,HuaManStage.DiscardChoice,op);f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{HuaMan=f.HuaMan!with{Souying=w.HuaManSouying}});PublishHuaManChoice(f);return SkillProgramStepOutcome.AwaitChoice;
  }
  if(op!=SkillProgramEffectOp.PayXiliActualOtherOwnerDamage||w.SourceSeat is not{}source||source==f.OwnerSeat||source!=_currentSeat||w.TargetSeat is not{}target||!HasRuntimeXili(source)||HasRuntimeXili(target)||_resolutionStack[^2]is not BeforeDamageProgramWindowFrame beforeDamage||beforeDamage.Id!=w.ParentFrameId||CurrentDamageAttempt is not{IsSourceLess:false,IsDelayedJudgmentDamage:false}a||a.SourceSeat!=source||a.TargetSeat!=target)throw new InvalidOperationException("Xili requires its other actual-turn owner's native source damage against a non-Xili target.");
  BeginHuaMan(f,HuaManStage.DiscardChoice,op);f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{HuaMan=f.HuaMan!with{OtherSeat=source,DamageTargetSeat=target,DamageAttemptResolutionId=a.ResolutionId}});PublishHuaManChoice(f);return SkillProgramStepOutcome.AwaitChoice;
 }
 private void BeginHuaMan(ProgramSkillFrame f,HuaManStage stage,SkillProgramEffectOp op,bool paid=false)
 {
  var r=new HuaManReceipt(HuaManSource(f),f.GameplayHash,f.InstructionIndex,f.WindowContext!.ParentFrameId,_turnNumber,stage,Paid:paid);ReplaceRuntimeTop(f with{HuaMan=r,PendingMovementContinuation=paid?new(f.OwnerSeat,0,null):null});AdvanceEventRulesAndQueueFact(new HuaManStartedEvent(f.Id,r.Source,r.GameplayHash,r.WindowId,r.ActualTurn,op));
 }
 private IReadOnlyList<PromptChoice> HuaManChoices(ProgramSkillFrame f)
 {
  var r=f.HuaMan!;
  if(r.Stage==HuaManStage.MaleChoice)return Array.AsReadOnly(_players.Where(p=>p.IsAlive&&p.Seat!=f.OwnerSeat&&p.Gender==GeneralGender.Male).Select(p=>new PromptChoice(new($"huaman.{f.Id}.male.{p.Seat}"),p.Name,[],[p.Seat],new Dictionary<string,string>{{"program-action","huaman"},{"branch","male"}})).Append(new(new($"huaman.{f.Id}.cancel"),"不选择男性角色",[],[],new Dictionary<string,string>{{"program-action","huaman"},{"branch","cancel"}})).ToArray());
  if(r.Stage!=HuaManStage.DiscardChoice)return [];
  var payments=BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,f.OwnerSeat,[CardZoneKind.Hand,CardZoneKind.Equipment],OwnedCardMoveIntent.Discard,canSelect:(_,c)=>!c.IsGeneralWeapon).ToList();
  foreach(var card in HuaManDiscardCards(f.OwnerSeat).Where(c=>_cardZones.GetLocation(c.Id)==CardLocation.WoodenOxGrain(f.OwnerSeat)))payments.Add(new(new($"huaman.{f.Id}.grain.{card.Id}"),$"弃置【{card.DisplayName}】",[card.Id],[],new Dictionary<string,string>{{"source-zone",CardZoneKind.WoodenOxGrain.ToString()},{"frame-id",f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)}}));
  return Array.AsReadOnly(payments.Select(c=>c with{Parameters=new System.Collections.ObjectModel.ReadOnlyDictionary<string,string>(new Dictionary<string,string>(c.Parameters){["program-action"]="huaman",["branch"]="discard"})}).ToArray());
 }
 private void PublishHuaManChoice(ProgramSkillFrame f)
 {
  var choices=HuaManChoices(f);if(choices.Count==0){FinishHuaMan(f);return;}var s=_contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});_pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,s.Name,choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=f.HuaMan!.Stage==HuaManStage.DiscardChoice,Choices=choices,SkillPrompt=new(f.SkillId,s.Name,s.Name,s.Description)};_status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 [ProgramAiChoice(SkillProgramEffectOp.PaySouyingDirectionalSoleTarget,SkillProgramEffectOp.PayXiliActualOtherOwnerDamage,SkillProgramEffectOp.AwakenZhanyuanAndOfferXili)]private PromptChoice SelectHuaManAi(ProgramSkillFrame f)=>HuaManChoices(f).First();
 [ProgramAnswer("huaman")]private void ResolveHuaManChoice(PromptChoice choice)
 {
  var f=(ProgramSkillFrame)_resolutionStack.Last();AssertHuaManFrame(f);var r=f.HuaMan!;if(_pendingDecision?.PlayerSeat!=f.OwnerSeat||!HuaManChoices(f).Any(c=>AssistedChoicesEqual([c],[choice])))throw new InvalidOperationException("Hua Man choice lost the actual owner and current physical payment.");ClearPendingDecision();
  if(r.Stage==HuaManStage.MaleChoice){CompleteZhanyuan(f,choice.Targets.Count==1?choice.Targets.Single():null);return;}
  if(!HuaManCurrent(f)||r.Stage!=HuaManStage.DiscardChoice||choice.Cards is not[var id]||!HuaManDiscardLocation(f.OwnerSeat,_cardZones.GetLocation(id))||IsSelfHandCategoryDiscardForbidden(f.OwnerSeat,GetAdvancedCard(id),_cardZones.GetLocation(id),OwnedCardMoveIntent.Discard))throw new InvalidOperationException("Hua Man cannot pay a moved, forbidden or foreign entity.");
   var before=_movementSequence;ReplaceRuntimeTop(f=f with{HuaMan=r with{Stage=HuaManStage.DiscardChildren,Before=before,After=before,DiscardedId=id,Paid=true,NativeOwnerHpBefore=_players[f.OwnerSeat].Hp,NativeOwnerHpAfter=_players[f.OwnerSeat].Hp},PendingMovementContinuation=new(f.OwnerSeat,0,null)});MoveLogicalDiscard(f.OwnerSeat,f.OwnerSeat,GetAdvancedCard(id),_cardZones.GetLocation(id),new(HuaManDiscardReason));f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{HuaMan=f.HuaMan!with{After=_movementSequence,ActualCount=1,NativeOwnerHpAfter=_players[f.OwnerSeat].Hp}});RecordHuaManMovement(f,f.OwnerSeat);AdvanceRuntimeProgram(f.Id);
 }
 private void RecordHuaManMovement(ProgramSkillFrame f,int subject){var r=f.HuaMan!;AdvanceEventRulesAndQueueFact(new HuaManMovementPaidEvent(f.Id,r.Source,r.GameplayHash,r.Stage,r.Before,r.After,r.ActualCount,subject));}
 private void PayHuaManDraw(ProgramSkillFrame f,int subject,int amount,HuaManStage stage)
 {
  var r=f.HuaMan!;var before=_movementSequence;ReplaceRuntimeTop(f=f with{HuaMan=r with{Stage=stage,Before=before,After=before,ActualCount=0,Paid=true},PendingMovementContinuation=new(f.OwnerSeat,0,null)});var ids=_players[subject].IsAlive&&_winner==Winner.None?DrawCards(_players[subject],amount,true,new(HuaManDrawReason)):[];f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{HuaMan=f.HuaMan!with{After=_movementSequence,ActualCount=ids.Count}});RecordHuaManMovement(f,subject);
  if(stage==HuaManStage.MansiDraw){var progress=_players[f.OwnerSeat].HuaManProgress??new(0);_players[f.OwnerSeat].HuaManProgress=progress with{MansiCards=checked(progress.MansiCards+ids.Count)};AdvanceEventRulesAndQueueFact(new MansiActualCardsObtainedEvent(f.Id,r.Source,progress.MansiCards,ids.Count,progress.MansiCards+ids.Count));}
  AdvanceRuntimeProgram(f.Id);
 }
 private bool DrainHuaMan(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id);
 [ProgramContinuation(ProgramContinuationKind.Resume,25200)]private bool ResumeHuaMan(long id)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{HuaMan:{}r}f||f.Id!=id||r.Stage is HuaManStage.DiscardChoice or HuaManStage.MaleChoice)return false;AssertHuaManFrame(f);if(DrainHuaMan(f))return true;
  if(r.Stage==HuaManStage.DiscardChildren){if(r.Souying is not null)PaySouyingMutation(f);else PayXiliIncrease(f);return true;}
  if(r.Stage==HuaManStage.XiliPayerDraw){PayHuaManDraw(f,r.OtherSeat!.Value,2,HuaManStage.XiliSourceDraw);return true;}
  if(r.Stage==HuaManStage.MaximumChildren){var owner=_players[f.OwnerSeat];var amount=owner.IsAlive&&owner.HasHpValue?Math.Min(1,owner.MaxHp-owner.Hp):0;ReplaceRuntimeTop(f=f with{HuaMan=r with{Stage=HuaManStage.RecoveryChildren,RecoveryRequested=amount}});if(amount>0)new ProgramSkillHost(this).Recover(f.Id,f.OwnerSeat,f.OwnerSeat,amount,null,null);AdvanceRuntimeProgram(f.Id);return true;}
  if(r.Stage==HuaManStage.RecoveryChildren){if(_players[f.OwnerSeat].IsAlive){ReplaceRuntimeTop(f=f with{HuaMan=r with{Stage=HuaManStage.MaleChoice},PendingMovementContinuation=null});PublishHuaManChoice(f);}else FinishHuaMan(f);return true;}
  FinishHuaMan(f);return true;
 }
 [ProgramContinuation(ProgramContinuationKind.Movement,25200)]private bool ReturnHuaManMovement(ProgramSkillFrame f)=>f.HuaMan is not null&&ResumeHuaMan(f.Id);
 private void FinishHuaMan(ProgramSkillFrame f){var r=f.HuaMan!;if(CompleteProgramEventHistory().OfType<HuaManCompletedEvent>().Any(e=>e.FrameId==f.Id))throw new InvalidOperationException("Hua Man cannot finish twice.");AdvanceEventRulesAndQueueFact(new HuaManCompletedEvent(f.Id,r.Source,r.GameplayHash));ReplaceRuntimeTop(f=f with{HuaMan=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
 [ProgramCapability]
 private sealed class HuaManProgramCapability(GameEngine engine) : IHuaManProgramHost
 {public SkillProgramStepOutcome ExecuteHuaMan(ProgramSkillFrame f,SkillProgramEffectOp op)=>engine.ExecuteHuaMan(f,op);}
}
