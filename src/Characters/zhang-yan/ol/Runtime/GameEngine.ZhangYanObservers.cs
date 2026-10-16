namespace CardGame.Core;
public sealed partial class GameEngine
{
 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27000)]
 private void AssertZhangYan(ProgramSkillFrame f)
 {
  if(f.ZhangYan is not{}r)return;
  if(f.InstructionIndex!=1||f.TriggerId is null||f.WindowContext is not{}ctx||r.Source!=ZhangYanSource(f)||r.GameplayHash!=f.GameplayHash||r.WindowId!=ctx.ParentFrameId||GetPausedPrivateOfferEffect(f)?.Op!=r.Operation||!ZhangYanComposition.IsOperation(r.Operation)||r.Turn!=_turnNumber||!IsValidPlayerSeat(r.PhaseOwner)||!Enum.IsDefined(r.Stage)||r.Before<0||r.After<r.Before||r.After>_movementSequence||r.Grain.Distinct().Count()!=r.Grain.Count||r.AddedTargets.Distinct().Count()!=r.AddedTargets.Count||r.Options.Distinct().Count()!=r.Options.Count||r.Options.Any(o=>!Enum.IsDefined(o))||
   ProgramEventHistory<ZhangYanStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.WindowId==r.WindowId&&e.Operation==r.Operation&&e.Turn==r.Turn&&e.PhaseOwner==r.PhaseOwner&&e.DamageId==r.DamageId)!=1)throw new InvalidOperationException("Zhang Yan lost its owning instruction, immutable collections or exact issued native start.");
  if(r.Operation==SkillProgramEffectOp.UseBlackCardAtWoundedPlayStart)
  {
   if(r.Stage is not(ZhangYanStage.SujiCard or ZhangYanStage.SujiTarget or ZhangYanStage.IssuingUse or ZhangYanStage.NativeUse or ZhangYanStage.NativeChildren)||r.DamageId is not null||r.Votes.Count!=0||r.Options.Count!=0||r.AddedTargets.Count!=0)throw new InvalidOperationException("Suji lost its physical conversion stage.");
   if(r.Stage is ZhangYanStage.NativeUse or ZhangYanStage.NativeChildren)
   {
    if(r.NativeReturn is not{}ret||ret.Source!=r.Source||ret.GameplayHash!=r.GameplayHash||ret.ProgramFrameId!=f.Id||ret.Turn!=r.Turn||ret.PhaseOwner!=r.PhaseOwner||ret.CardId!=r.CardId||ret.From!=r.From||ret.OriginalTarget!=r.Target||r.BatchId is null||ProgramEventHistory<ZhangYanSujiIssuedEvent>().Count(e=>e.Return==ret)!=1||
     ProgramEventHistory<ZhangYanSujiPaidEvent>().Count(e=>e.FrameId==f.Id&&e.UseId==ret.UseId&&e.CardId==ret.CardId&&e.From==ret.From&&e.Before==r.Before&&e.After==r.After&&e.BatchId==r.BatchId)!=1||
     _cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.CardId==ret.CardId&&m.From==ret.From&&m.Reason==CardMoveReasons.Use)!=1)throw new InvalidOperationException("Suji lost its once-paid original native entity and whole-use return.");
    if(r.Stage==ZhangYanStage.NativeChildren&&(!r.NativeCompleted||LifecycleCardUse(ret.UseId)is not null||ProgramEventHistory<ZhangYanSujiReturnedEvent>().Count(e=>e.Return==ret)!=1))throw new InvalidOperationException("Suji cannot settle an unretired native Use.");
   }
  }
  else if(r.Operation==SkillProgramEffectOp.ObtainSujiDamagedPhaseOwnerCard)
  {
   if(r.Stage is not(ZhangYanStage.RewardChoice or ZhangYanStage.RewardChildren)||r.NativeReturn is not{}ret||ret.Source.OwnerSeat!=f.OwnerSeat||ret.Source.SkillId!=f.SkillId||ret.Source.SkillInstanceId!=f.SkillInstanceId||ret.GameplayHash!=f.GameplayHash||ret.PhaseOwner!=r.PhaseOwner||r.DamageId!=ctx.DamageFrameId||r.DamageId is null||ProgramEventHistory<ZhangYanSujiIssuedEvent>().Count(e=>e.Return==ret)!=1||
    _resolutionStack.SingleOrDefault(x=>x.Id==r.DamageId)is not DamageFrame damage||damage.ParentFrameId!=ret.UseId||damage.TargetSeat!=r.PhaseOwner||damage.Amount<=0||ctx.TargetSeat!=r.PhaseOwner)throw new InvalidOperationException("Suji reward lost its original phase owner and actual same-use positive damage.");
   if(r.Stage==ZhangYanStage.RewardChildren&&(r.CardId is null||r.From?.OwnerSeat!=r.PhaseOwner||f.PendingMovementContinuation is null||ProgramEventHistory<ZhangYanRewardIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.UseId==ret.UseId&&e.DamageId==r.DamageId&&e.PhaseOwner==r.PhaseOwner&&e.CardId==r.CardId&&e.From==r.From&&e.Before==r.Before&&e.After==r.After)!=1||
     _cardMovements.Where(m=>m.Sequence>r.Before&&m.Sequence<=r.After).Any(m=>!IsZhangYanRewardMovement(f,m))))throw new InvalidOperationException("Suji reward lost its once-issued HE entity and native attached grain.");
  }
  else
  {
   if(r.Stage is not(ZhangYanStage.LangdaoVotes or ZhangYanStage.LangdaoTargets)||r.Options.Count is<1 or>3||r.Votes.Count>2||r.Votes.Where((v,i)=>v.Seat!=(i==0?f.OwnerSeat:r.PhaseOwner)||!r.Options.Contains(v.Option)).Any()||
    !ProgramEventHistory<ZhangYanLangdaoOptionFrozenEvent>().Where(e=>e.FrameId==f.Id).OrderBy(e=>e.Index).Select(e=>e.Option).SequenceEqual(r.Options)||
    (r.Stage==ZhangYanStage.LangdaoVotes?r.Votes.Count>1:r.Votes.Count!=2||r.AddedTargets.Count>r.Votes.Count(v=>v.Option==LangdaoOption.ExtraTarget)))throw new InvalidOperationException("Langdao lost its two independently sealed original option sets or counted additive directions.");
  }
  if(_resolutionStack.LastOrDefault()?.Id==f.Id&&r.Stage is ZhangYanStage.SujiCard or ZhangYanStage.SujiTarget or ZhangYanStage.RewardChoice or ZhangYanStage.LangdaoVotes or ZhangYanStage.LangdaoTargets)
   if(_pendingDecision is not{Kind:DecisionKind.ProgramTrigger}p||p.PlayerSeat!=ZhangYanChooser(f)||p.IsPrivate!=(r.Stage!=ZhangYanStage.LangdaoTargets)||!AssistedChoicesEqual(p.Choices,ZhangYanChoices(f)))throw new InvalidOperationException("Zhang Yan lost its actual source/target-private choice boundary.");
 }
 private bool IsZhangYanMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>f.ZhangYan is{}r&&r.Stage is ZhangYanStage.NativeChildren or ZhangYanStage.RewardChildren&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null&&(e is null||ZhangYanComposition.IsOperation(e.Op));
 private bool IsZhangYanRewardMovement(ProgramSkillFrame f,CardMovementRecord m)
 {
  var r=f.ZhangYan!;if(m.Sequence<=r.Before||m.Sequence>r.After||!_cardMovements.Contains(m))return false;
  return m.CardId==r.CardId&&m.From==r.From&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==ZhangYanObtainReason||
   r.Grain.Contains(m.CardId)&&m.From==CardLocation.WoodenOxGrain(r.PhaseOwner)&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.WoodenOxGrainDiscard&&_cardMovements.Any(ox=>ox.Sequence>r.Before&&ox.Sequence<=r.After&&ox.CardId==r.CardId&&ox.CardKind==CardKind.WoodenOx&&ox.From==CardLocation.Equipment(r.PhaseOwner)&&ox.To==CardLocation.Hand(f.OwnerSeat)&&ox.Reason.Value==ZhangYanObtainReason);
 }
 private bool IsZhangYanNativeSettlementBatch(ProgramSkillFrame f,CardMovementBatchContext batch)=>f.ZhangYan is{Stage:ZhangYanStage.NativeChildren,NativeCompleted:true,NativeReturn:{}ret}r&&batch.ParentFrameId==ret.UseId&&batch.AwaitingProgramFrameId is null&&batch.OriginOwnerSeat==f.OwnerSeat&&batch.OriginSkillId==f.SkillId&&batch.OriginSkillInstanceId==f.SkillInstanceId&&batch.Movements.Count>0&&
  ProgramEventHistory<ZhangYanSujiReturnedEvent>().Count(e=>e.Return==ret)==1&&batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.After&&m.CardId==ret.CardId&&m.CardKind==ret.PrintedKind&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.UseFinished);
 private bool IsZhangYanRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)=>f.ZhangYan is{Stage:ZhangYanStage.RewardChildren}r&&r.From==CardLocation.Equipment(r.PhaseOwner)&&r.CardId is{}id&&_cardMovements.Any(m=>IsZhangYanRewardMovement(f,m)&&m.CardId==id&&m.CardKind==CardKind.SilverLion)&&
  RecoveryReplacementFrameRidesOn(recovery,f)&&recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement&&recovery.Attempt.SourceSeat==r.PhaseOwner&&recovery.Attempt.TargetSeat==r.PhaseOwner&&recovery.Attempt.Amount==1&&recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&recovery.Attempt.Completion.MoveReason?.Value==ZhangYanObtainReason;
 private bool ZhangYanFirstChild(ProgramSkillFrame f,ResolutionFrame child)
 {
  if(f.ZhangYan is not{}r)return false;
  if(child is CardUseFrame use)return r.Stage==ZhangYanStage.NativeUse&&use.ZhangYanSuji==r.NativeReturn&&IsZhangYanSujiUse(use);
  if(r.Stage is not(ZhangYanStage.NativeChildren or ZhangYanStage.RewardChildren)||f.PendingMovementContinuation is null)return false;
  if(child is CardsMovedTriggerWindowFrame moved)return r.Stage==ZhangYanStage.NativeChildren?moved.ResumeProgramFrameId==f.Id&&IsZhangYanNativeSettlementBatch(f,moved.Batch):moved.Batch.ParentFrameId==f.Id&&(moved.ResumeProgramFrameId==f.Id||moved.Batch.AwaitingProgramFrameId==f.Id)&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>IsZhangYanRewardMovement(f,m));
  if(child is RecoveryReplacementFrame recovery)return IsZhangYanRecoveryParent(f,recovery);
  if(child is HpChangedTriggerWindowFrame hp)return r.Stage==ZhangYanStage.RewardChildren&&r.From==CardLocation.Equipment(r.PhaseOwner)&&r.CardId is{}id&&GetAdvancedCard(id).Kind==CardKind.SilverLion&&hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement&&hp.Change.SourceSeat==r.PhaseOwner&&hp.Change.TargetSeat==r.PhaseOwner&&hp.Change.Kind==HpChangeKind.Recovery&&hp.Change.Amount==1;
  if(child is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.SkillsChanged}skills)return skills.ResumeProgramFrameId==f.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
  return child is ProgramLifecycleTriggerWindowFrame state&&state.Continuation==ProgramLifecycleContinuation.ResumeCharacterStateChange&&state.ResumeProgramFrameId==f.Id&&ProgramEventHistory<CharacterStateChangedEvent>().Any(e=>e.Change.Id==state.Id&&e.Change.ParentFrameId==f.Id&&e.Change.TargetSeat==state.OwnerSeat&&e.Change.Window==state.Window);
 }
 private ProgramSkillFrame? ZhangYanObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.ZhangYanFirstChild(f,c),static(e,i)=>e.HasObserverSuffix(i,static(engine,c)=>engine.IsMovementObserverEdge(c)||engine.IsYanPuDyingExitEdge(c)||RecoveryReplacementFrameRidesOn(engine._resolutionStack[c],engine._resolutionStack[c-1]),static(engine,c,d)=>engine.IsSuspendedOrPaidRescueObserverSuffix(c,d)));
 private bool HasZhangYanDamageObserver(long id)
 {
  if(ZhangYanObserverRoot()is not{}root||!_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame))return false;
  var index=_resolutionStack.FindIndex(f=>f.Id==id);var own=_resolutionStack.FindIndex(f=>f.Id==root.Id);return index>own||index+1==own&&root.WindowContext?.ParentFrameId==id&&root.ZhangYan!.Operation==SkillProgramEffectOp.ObtainSujiDamagedPhaseOwnerCard;
 }
 private bool IsZhangYanDying()=>ActiveDying is{}d&&ZhangYanObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
 private bool AllowsZhangYanNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
 {if(sourceLess||f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||ZhangYanObserverRoot()is not{}root||root.Id==f.Id)return false;var e=GetPausedPrivateOfferEffect(f);return e is{Op:SkillProgramEffectOp.Damage}&&e.Amount==amount&&e.ActorReference==source&&e.DamageNature==nature&&target==(e.TargetReference is{}reference?ResolveProgramParticipant(f,reference):ResolveProgramEffectTarget(f,e.Target));}
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,27000)]private bool AdvanceZhangYanSubtree()=>_pendingDecision is null&&ZhangYanObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
}
