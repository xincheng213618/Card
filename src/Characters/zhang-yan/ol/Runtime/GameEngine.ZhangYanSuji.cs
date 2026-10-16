namespace CardGame.Core;
public sealed partial class GameEngine
{
 private void BindZhangYanSujiUse(long id)
 {
  var use=LifecycleCardUse(id);var at=_resolutionStack.FindIndex(f=>f.Id==id);
  if(use is null||at<1||_resolutionStack[at-1]is not ProgramSkillFrame{ZhangYan:{Stage:ZhangYanStage.IssuingUse}r}parent)return;
  if(r.Operation!=SkillProgramEffectOp.UseBlackCardAtWoundedPlayStart||r.NativeReturn is not null||use.Action is not{Type:CardActionType.Use,EffectiveKind:CardKind.Slash}a||a.ActorSeat!=parent.OwnerSeat||a.ProviderSeat!=parent.OwnerSeat||a.PhysicalCards is not[var cost]||cost.CardId!=r.CardId||cost.From!=r.From||use.CardId!=r.CardId||!a.TargetSeats.SequenceEqual([r.Target!.Value])||!a.ConversionChain.SequenceEqual([r.Source]))throw new InvalidOperationException("Suji must bind its exact chosen original black entity to one native Use.");
  var ret=new ZhangYanSujiReturn(parent.Id,id,a.ActionId,r.Source,r.GameplayHash,r.Turn,r.PhaseOwner,r.Target.Value,cost.CardId,cost.CardKind,cost.From);
  ReplaceRuntimeFrame(parent.Id,parent with{ZhangYan=r with{Stage=ZhangYanStage.NativeUse,NativeReturn=ret}});UpdateLifecycleCardUse(id,u=>u with{ZhangYanSuji=ret});AdvanceEventRulesAndQueueFact(new ZhangYanSujiIssuedEvent(ret));
 }
 private void CompleteZhangYanSujiPayment(long id)
 {
  if(LifecycleCardUse(id)is not{ZhangYanSuji:{}ret}use)return;var parent=_resolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.Id==ret.ProgramFrameId);var r=parent.ZhangYan!;
  if(r.Stage!=ZhangYanStage.NativeUse||r.NativeReturn!=ret||r.BatchId is not null)throw new InvalidOperationException("Suji cannot pay the same native entity twice.");
  var paid=_cardMovements.Where(m=>m.Sequence>r.Before&&m.CardId==ret.CardId&&m.From==ret.From&&m.Reason==CardMoveReasons.Use).ToArray();
  if(paid is not[var move]||move.To!=(ret.PrintedKind==CardKind.GeneralWeapon&&ret.From.Zone==CardZoneKind.Equipment?CardLocation.OutsideGame:IsProgramAlternativeCost(use.Action!,ret.CardId)?CardLocation.DrawPile:CardLocation.Processing))throw new InvalidOperationException("Suji lost its actual native payment destination.");
  var batch=use.PaymentMovementReceipt?.Batch??_pendingCardsMovedBatches.Single(b=>b.ParentFrameId==id&&b.Movements.Contains(move));
  ReplaceRuntimeFrame(parent.Id,parent with{ZhangYan=r with{After=_movementSequence,BatchId=batch.Id}});AdvanceEventRulesAndQueueFact(new ZhangYanSujiPaidEvent(parent.Id,id,ret.CardId,ret.From,move.To,r.Before,_movementSequence,batch.Id));
 }
 private bool IsZhangYanSujiUse(CardUseFrame use)
 {
  if(use.ZhangYanSuji is not{}r||use.Action is not{Type:CardActionType.Use}a||r.UseId!=use.Id||r.ActionId!=a.ActionId||r.CardId!=use.CardId||use.PhysicalCardIds is not[var material]||material!=r.CardId||!IsSlashCard(use.CardKind)||a.PhysicalCards is not[var cost]||cost.CardId!=r.CardId||cost.CardKind!=r.PrintedKind||cost.From!=r.From||!a.ConversionChain.Contains(r.Source)||!ShownEntityUseActorMatches(use,r.Source.OwnerSeat,r.Source.OwnerSeat)||ProgramEventHistory<ZhangYanSujiIssuedEvent>().Count(e=>e.Return==r)!=1)return false;
  var at=_resolutionStack.FindIndex(f=>f.Id==use.Id);return at>0&&_resolutionStack[at-1]is ProgramSkillFrame{ZhangYan:{Stage:ZhangYanStage.NativeUse,NativeCompleted:false}p}parent&&parent.Id==r.ProgramFrameId&&p.NativeReturn==r&&p.Source==r.Source&&p.GameplayHash==r.GameplayHash;
 }
 private bool IsZhangYanSujiRemovedMaterial(long useId,int cardId)=>LifecycleCardUse(useId)is{ZhangYanSuji:{PrintedKind:CardKind.GeneralWeapon}r}u&&r.CardId==cardId&&r.From.Zone==CardZoneKind.Equipment&&_cardZones.Contains(CardLocation.OutsideGame,cardId)&&IsZhangYanSujiUse(u);
 [NativeUseValidation(27000)]private void ValidateZhangYanSujiUse(CardUseFrame use)
 {if(use.ZhangYanSuji is not null&&!IsZhangYanSujiUse(use))throw new InvalidOperationException("Suji's retiring native Use lost its original actor/material parent or issued conversion.");}
 [NativeUseReturn(27000)]private void ReturnZhangYanSujiUse(CardUseFrame use)
 {
  if(use.ZhangYanSuji is not{}ret)return;var f=GetActiveProgramFrame(ret.ProgramFrameId);if(f.ZhangYan is not{Stage:ZhangYanStage.NativeUse,NativeCompleted:false}r||r.NativeReturn!=ret||_resolutionStack.LastOrDefault()?.Id!=f.Id)throw new InvalidOperationException("Suji lost its exact whole-use typed return.");
  ReplaceRuntimeTop(f with{ZhangYan=r with{NativeCompleted=true}});
 }
 [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword,27000)]private bool ReturnZhangYanSujiAttack(AttackCompletionReceipt completion)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{ZhangYan:{Stage:ZhangYanStage.NativeUse,NativeCompleted:true,NativeReturn:{}ret}r}f||ret.UseId!=completion.ResolutionId)return false;
  if(completion.ProgramFrameId!=f.Id||LifecycleCardUse(ret.UseId)is not null||ProgramEventHistory<CardUseFinishedEvent>().Count(e=>e.ResolutionId==ret.UseId)!=1||ProgramEventHistory<ZhangYanSujiReturnedEvent>().Any(e=>e.Return==ret))throw new InvalidOperationException("Suji cannot resume before or twice after its genuine complete native attack.");
  AdvanceEventRulesAndQueueFact(new ZhangYanSujiReturnedEvent(ret));ReplaceRuntimeTop(f with{ZhangYan=r with{Stage=ZhangYanStage.NativeChildren},PendingMovementContinuation=new(f.OwnerSeat,0,null)});return true;
 }
 private CardUseFrame? ZhangYanRewardUse(ProgramSkillWindowContext c)
 {
  if(c.Window!=SkillProgramTriggerWindow.AfterDamageApplied||c.DamageFrameId is not{}id||_resolutionStack.SingleOrDefault(f=>f.Id==c.ParentFrameId)is not DamageTriggerWindowFrame w||w.ParentFrameId!=id||w.TriggerWindow!=c.Window||_resolutionStack.SingleOrDefault(f=>f.Id==id)is not DamageFrame damage||damage.TargetSeat!=c.TargetSeat||damage.Amount!=c.Amount||LifecycleCardUse(damage.ParentFrameId)is not{ZhangYanSuji:{}ret}use||ret.PhaseOwner!=damage.TargetSeat||!IsZhangYanSujiUse(use))return null;
  return use;
 }
 private void AppendZhangYanSujiDamageCandidates(IDamageAttempt attack,SkillProgramTriggerWindow window,List<DamageTriggerCandidate> candidates)
 {
  if(window!=SkillProgramTriggerWindow.AfterDamageApplied||LifecycleCardUse(attack.ResolutionId)is not{ZhangYanSuji:{}r}use||r.PhaseOwner!=attack.TargetSeat||!IsZhangYanSujiUse(use)||!ZhangYanRewardChoicesAvailable(r.Source.OwnerSeat,r.PhaseOwner))return;
  var program=_contentRegistry.GetSkill(r.Source.SkillId).Program!;var t=program.Triggers.Single(t=>t.Effects is[{Op:SkillProgramEffectOp.ObtainSujiDamagedPhaseOwnerCard}]);
  candidates.RemoveAll(c=>c.ProgramId==r.Source.SkillId&&c.OwnerSeat==r.Source.OwnerSeat&&c.ProgramTriggerId==t.Id);
  candidates.Add(new(r.Source.OwnerSeat,$"program:{r.Source.SkillId}:{t.Id}:{r.Source.SkillInstanceId}:occurrence-0",t.Priority,false,r.Source.SkillId,t.Id,r.Source.SkillInstanceId,r.GameplayHash,0));
 }
 private bool HasIssuedZhangYanSujiRewardCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
 {
  if(ZhangYanRewardUse(context)is not{ZhangYanSuji:{}r}||r.Source.OwnerSeat!=c.OwnerSeat||r.Source.SkillId!=c.SkillId||r.Source.SkillInstanceId!=c.SkillInstanceId||r.GameplayHash!=c.GameplayHash||GetProgramTrigger(c).Effects is not[{Op:SkillProgramEffectOp.ObtainSujiDamagedPhaseOwnerCard}]||_resolutionStack.SingleOrDefault(f=>f.Id==context.ParentFrameId)is not DamageTriggerWindowFrame w||w.CandidateIndex<0||w.CandidateIndex>=w.Candidates.Count||w.Candidates[w.CandidateIndex].ToProgramCandidate()!=c)return false;
  return !ProgramEventHistory<ZhangYanStartedEvent>().Any(e=>e.Operation==SkillProgramEffectOp.ObtainSujiDamagedPhaseOwnerCard&&e.Source.OwnerSeat==c.OwnerSeat&&e.DamageId==w.ParentFrameId);
 }
}
