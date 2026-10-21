namespace CardGame.Core;
internal sealed partial class MouDongChengRuntime
{
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,37401)]private void AttachmentDying(PlayerDyingEvent fact)
 {
  if(lightning.DyingCause(fact)is not{}cause||facts.History<MouDcLightningRewardEvent>().Any(e=>e.Cause.OriginalUseId==cause.OriginalUseId))return;
  delayed.MarkReward(cause,fact.ResolutionId);events.AdvanceFact(new MouDcLightningRewardEvent(cause,fact.ResolutionId,facts.Turn));
 }
 internal bool BeginDelayedSettlement(long judgment)
 {
  if(lightning.NextSettlement(judgment)is not{}settlement)return false;var source=settlement.OriginalSource;var program=facts.Program(source.SkillId)??throw new InvalidOperationException("A paid delayed return lost its content definition.");if(program.GameplayHash!=settlement.OriginalHash)throw new InvalidOperationException("A delayed payment cannot cross its original content hash.");
  var f=new ProgramSkillFrame(frames.NextId(),source.OwnerSeat,source.SkillId,"native-delayed-settlement",program.GameplayHash,1,[],[]){SkillInstanceId=source.SkillInstanceId,MouDongChengSettlement=settlement};
  var r=new MouDongChengReceipt(Source(f),f.GameplayHash,SkillProgramEffectOp.ResolveMouDongChengDelayedSettlement,facts.Turn,judgment){Stage=settlement.Reward?MouDongChengStage.RecoveryChildren:MouDongChengStage.ClaimChildren,Before=facts.Sequence,After=facts.Sequence,UseId=settlement.OriginalUseId,Returned=settlement.Reward,Dying=settlement.Reward?[settlement.DyingId]:[]};
  f=delayed.BindSettlement(f with{MouDongCheng=r,PendingMovementContinuation=new(f.OwnerSeat,0,null)},judgment);frames.Push(f);events.AdvanceFact(new MouDcSettlementIssuedEvent(f.Id,settlement));events.AdvanceFact(new MouDcStartedEvent(f.Id,r.Source,r.GameplayHash,r.Operation,r.Turn,r.ParentId));delayed.MarkSettlement(settlement);if(settlement.Reward)native.Recover(f);else native.Claim(f);Continue(f.Id);return true;
 }
 internal void DelayedSettlementReturned(MouDcSettlement settlement)=>delayed.ResumeCleanup(settlement.JudgmentId);
}
