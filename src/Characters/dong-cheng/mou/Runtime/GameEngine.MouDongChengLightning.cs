namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool MouDcNativeLightning(JudgmentFrame j)=>j.Continuation==JudgmentContinuationKind.Lightning&&j.ParentAttackId is null&&j.ParentFrameId==0&&j.Reason==JudgmentReasons.Lightning&&j.SourceSeat==j.TargetSeat&&ProgramEventHistory<JudgmentRequestedEvent>().Count(e=>e.ResolutionId==j.Id&&e.TargetSeat==j.TargetSeat&&e.SourceCard==CardKind.Lightning&&e.Reason==JudgmentReasons.Lightning)==1&&(j.DelayedCardId is{}card&&j.OriginalUseLightning is null&&_cardZones.CardsAt(_cardZones.GetLocation(card)).Any(c=>c.Id==card)||j.DelayedCardId is null&&j.OriginalUseLightning is{Holder:var holder}&&holder==j.TargetSeat);
 private JudgmentFrame? MouDcDelayedDamage(MouDcOccurrence o)=>o.UseId==0&&_resolutionStack.OfType<JudgmentFrame>().SingleOrDefault(j=>j.Id==o.AttackId)is{}j&&MouDcNativeLightning(j)&&j.CardAttack is{IsDelayedJudgmentDamage:true}&&(j.DelayedCardId??0)==o.CardId?j:null;
 private bool MouDcDamageParent(MouDcOccurrence o)=>o.UseId>0?LifecycleCardUse(o.UseId)?.Action is not null:MouDcDelayedDamage(o)is not null;
 private bool MouDcHasPaidClaim(ProgramSkillFrame f,MouDcOccurrence o,long beforeId)
 {
  var claims=o.UseId>0?LifecycleCardUse(o.UseId)?.MouDongChengClaims:MouDcDelayedDamage(o)?.MouDongChengClaims;
  return claims?.Any(p=>p.ProtectionFrameId==f.Id&&p.Source==f.MouDongCheng!.Source&&p.BeforeWindowId==beforeId)==true;
 }
 [ProgramCapability]private sealed class MouDcLightningFacts(GameEngine e):IMouDcLightningFacts
 {
  public MouDcDelayedCause? DyingCause(PlayerDyingEvent fact)
  {
   if(e._resolutionStack.LastOrDefault()is not DyingFrame{Continuation:DyingContinuationKind.Damage}dying||dying.Id!=fact.ResolutionId||e._resolutionStack.OfType<DamageFrame>().SingleOrDefault(f=>f.Id==dying.ParentFrameId)is not{Amount:>0}damage||e.CurrentDamageAttempt is not CardAttackHandle{IsDelayedJudgmentDamage:true,IsSourceLess:true,DamageWasApplied:true}attack||attack.ResolutionId!=damage.ParentFrameId||attack.TargetSeat!=fact.VictimSeat||e._resolutionStack.OfType<JudgmentFrame>().SingleOrDefault(j=>j.Id==attack.ResolutionId)is not{OriginalUseLightning:{}a}j||!e.MouDcNativeLightning(j)||a.Origin.Source is not{SkillId:MouDongChengRuntime.Douwei,BindingId:"discard-damage-use"}source||e._contentRegistry.Skills.GetValueOrDefault(source.SkillId)?.Program?.GameplayHash!=a.Origin.GameplayHash||e.ProgramEventHistory<MouDcUseIssuedEvent>().Count(x=>x.UseId==a.Origin.UseId&&x.ActionId==a.Origin.ActionId&&x.Owner==source.OwnerSeat&&x.Kind==CardKind.Lightning)!=1||e.ProgramEventHistory<CardUseFinishedEvent>().Count(x=>x.ResolutionId==a.Origin.UseId)!=1)return null;
   return new(a.Origin.UseId,a.Origin.ActionId,source,a.Origin.GameplayHash,a.Id,j.Id,damage.Id,fact.VictimSeat);
  }
  public MouDcSettlement? NextSettlement(long id)
  {
   if(e._resolutionStack.LastOrDefault()is not JudgmentFrame{NativeDelayedAttackCompletion:{}ret}j||j.Id!=id||ret.ResolutionId!=id||!ret.DelayedTurn||ret.ProgramFrameId is not null||!e.MouDcNativeLightning(j))return null;
   var claim=j.MouDongChengClaims?.FirstOrDefault(p=>!p.Settled&&e._players[p.Source.OwnerSeat].IsAlive);if(claim is not null)return new(id,claim.Source,claim.GameplayHash,false,claim.ProtectionFrameId,0,0);
   return j.MouDongChengReward is{Settled:false}reward&&e._players[reward.Cause.Source.OwnerSeat].IsAlive?new(id,reward.Cause.Source,reward.Cause.GameplayHash,true,0,reward.DyingId,reward.Cause.OriginalUseId):null;
  }
  public bool SettlementParent(ProgramSkillFrame f)
  {
   var index=e._resolutionStack.FindIndex(x=>x.Id==f.Id);
   if(f.MouDongChengSettlement is not{}s||f.ActivationId!="native-delayed-settlement"||f.TriggerId is not null||f.WindowContext is not null||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||f.OwnerSeat!=s.OriginalSource.OwnerSeat||f.SkillId!=s.OriginalSource.SkillId||f.SkillInstanceId!=s.OriginalSource.SkillInstanceId||f.GameplayHash!=s.OriginalHash||index<1||e._resolutionStack[index-1]is not JudgmentFrame{NativeDelayedAttackCompletion:{}ret}j||j.Id!=s.JudgmentId||ret.ResolutionId!=j.Id||!ret.DelayedTurn||ret.ProgramFrameId is not null||!e.MouDcNativeLightning(j)||e.ProgramEventHistory<MouDcSettlementIssuedEvent>().Count(x=>x.FrameId==f.Id&&x.Return==s)!=1)return false;
   return s.Reward?j.MouDongChengReward is{Settled:true}reward&&reward.Cause.Source==s.OriginalSource&&reward.Cause.GameplayHash==s.OriginalHash&&reward.DyingId==s.DyingId&&reward.Cause.OriginalUseId==s.OriginalUseId&&e.ProgramEventHistory<MouDcLightningRewardEvent>().Count(x=>x.Cause==reward.Cause&&x.DyingId==reward.DyingId)==1:j.MouDongChengClaims?.Any(p=>p.ProtectionFrameId==s.ProtectionId&&p.Source==s.OriginalSource&&p.GameplayHash==s.OriginalHash)==true;
  }
 }
 [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart,37401,SkillProgramEffectOp.ResolveMouDongChengDelayedSettlement)]private bool MouDcInternalSettlementIsNotCommand(CharacterState owner)=>false;
 [ProgramCapability]private sealed class MouDcLightningNative(GameEngine e):IMouDcLightningNative
 {
  public ProgramSkillFrame BindSettlement(ProgramSkillFrame child,long judgment)=>e.BindNativeDelayedAttackChild(child,judgment);
  public void MarkReward(MouDcDelayedCause cause,long dying)
  {
   if(e._resolutionStack.OfType<JudgmentFrame>().SingleOrDefault(j=>j.Id==cause.JudgmentId)is not{OriginalUseLightning:{}a}j||a.Id!=cause.AttachmentId||a.Origin.UseId!=cause.OriginalUseId||a.Origin.Source!=cause.Source||a.Origin.GameplayHash!=cause.GameplayHash||j.MouDongChengReward is not null)throw new InvalidOperationException("Only the first causal native Lightning dying may earn its original whole-Use reward.");
   e.ReplaceRuntimeFrame(j.Id,j with{MouDongChengReward=new(cause,dying,e._turnNumber)});e._players[cause.Source.OwnerSeat].MouDongChengDisabledTurn=e._turnNumber;
  }
  public void ResumeCleanup(long judgment)=>e.ResumeNativeDelayedAttackCompletion(judgment);
  public void MarkSettlement(MouDcSettlement s)
  {
   if(e._resolutionStack.OfType<JudgmentFrame>().SingleOrDefault(j=>j.Id==s.JudgmentId)is not{NativeDelayedAttackCompletion:not null}j)throw new InvalidOperationException("Settlement must retain its complete native delayed return.");
   if(s.Reward){if(j.MouDongChengReward is not{Settled:false}reward||reward.DyingId!=s.DyingId)throw new InvalidOperationException("A delayed reward cannot settle twice.");e.ReplaceRuntimeFrame(j.Id,j with{MouDongChengReward=reward with{Settled=true}});}
  }
 }
 [NativeAttackReturn(NativeAttackReturnPhase.BeforeCleanup,37400)]private bool BeforeMouDcDelayedCleanup(AttackCompletionReceipt completion)
 {
  if(!completion.DelayedTurn||completion.ProgramFrameId is not null||_resolutionStack.LastOrDefault()is not JudgmentFrame j||j.Id!=completion.ResolutionId||j.MouDongChengReward is null&&j.MouDongChengClaims is null)return false;
  if(j.NativeDelayedAttackCompletion!=completion)throw new InvalidOperationException("Baojia and Douwei require the retained complete native delayed attack return.");return MouDongCheng.BeginDelayedSettlement(j.Id);
 }
 private void ClaimMouDcDelayedCard(ProgramSkillFrame f)
 {
  if(!MouDongCheng.Valid(f)||f.MouDongCheng is not{Stage:MouDongChengStage.ClaimChildren}r||f.MouDongChengSettlement is not{Reward:false}s||_resolutionStack.OfType<JudgmentFrame>().SingleOrDefault(j=>j.Id==s.JudgmentId)is not{NativeDelayedAttackCompletion:not null,MouDongChengClaims:{}claims}j||claims.SingleOrDefault(p=>p.ProtectionFrameId==s.ProtectionId)is not{Settled:false}claim)throw new InvalidOperationException("Baojia requires its one paid delayed card claim after native resolution.");
  var card=j.DelayedCardId;var ids=card is{}id&&_cardZones.GetLocation(id)==CardLocation.DiscardPile?new[]{id}:Array.Empty<int>();
  ReplaceRuntimeFrame(j.Id,j with{MouDongChengClaims=claims.Select(p=>p.ProtectionFrameId==claim.ProtectionFrameId?p with{Settled=true,Before=r.Before,After=r.Before,Cards=ids}:p).ToArray()});
  if(ids.Length>0)MoveProgramCardsFromMultipleSources(ids,CardLocation.Hand(f.OwnerSeat),new("skill-program.mou:baojia.delayed-claim"),(_,moves)=>
  {
   if(moves is not[var m]||m.CardId!=ids[0]||m.From!=CardLocation.DiscardPile||m.Sequence<=r.Before||m.To!=CardLocation.Hand(f.OwnerSeat)&&m.To!=CardLocation.OutsideGame)throw new InvalidOperationException("Baojia's genuine delayed entity claim invoice changed.");
   var own=GetActiveProgramFrame(f.Id);ReplaceRuntimeFrame(f.Id,own with{MouDongCheng=own.MouDongCheng! with{After=m.Sequence,Actual=m.To==CardLocation.Hand(f.OwnerSeat)?1:0}});var actual=(JudgmentFrame)_resolutionStack.Single(x=>x.Id==j.Id);ReplaceRuntimeFrame(j.Id,actual with{MouDongChengClaims=actual.MouDongChengClaims!.Select(p=>p.ProtectionFrameId==claim.ProtectionFrameId?p with{After=m.Sequence}:p).ToArray()});AdvanceEventRulesAndQueueFact(new MouDcDelayedClaimSettledEvent(f.Id,j.Id,claim.ProtectionFrameId,ids[0],m.To==CardLocation.Hand(f.OwnerSeat)?1:0,r.Before,m.Sequence));
  });
  else AdvanceEventRulesAndQueueFact(new MouDcDelayedClaimSettledEvent(f.Id,j.Id,claim.ProtectionFrameId,card??0,0,r.Before,r.Before));
 }
}
