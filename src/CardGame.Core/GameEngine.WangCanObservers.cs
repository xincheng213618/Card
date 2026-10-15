namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool HasSanwenCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveSanwenActualGainDuplicates);
 private bool HasDenglouCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ViewDenglouTopAndUseBasics);
 private IReadOnlyList<SanwenGainProof>? CaptureSanwenGainQualifications(CardMovementBatchContext batch)
 {
  if(!HasSanwenCapability||batch.TurnNumber<1)return null;var result=new List<SanwenGainProof>();
  foreach(var owner in _players)
  {
   if(!owner.IsAlive||!GetSkillBindingShard(owner).GetInstanceTriggers(SkillProgramTriggerWindow.CardsGained).Any(b=>ProgramInstructionResolver.Default.Features(b.Trigger).HasOperation(SkillProgramEffectOp.ResolveSanwenActualGainDuplicates)))continue;
   var incoming=(batch.NativeDrawInvocation is{}draw&&draw.OwnerSeat==owner.Seat?draw.Materials.Select(m=>m.CardId):batch.Movements.Where(m=>m.To==CardLocation.Hand(owner.Seat)&&m.From!=m.To).Select(m=>m.CardId)).Distinct().ToHashSet();if(incoming.Count==0)continue;
   var old=GetHand(owner).Where(c=>!incoming.Contains(c.Id)).Select(c=>new SanwenNamedEntity(c.Id,ProgramBasicCardName(AdvancedEffectiveHandKind(owner,c)))).ToArray();
   var gained=incoming.Select(id=>new SanwenNamedEntity(id,ProgramBasicCardName(AdvancedEffectiveHandKind(owner,GetAdvancedCard(id))))).Where(c=>old.Any(w=>w.Name==c.Name)).ToArray();if(gained.Length==0)continue;
   result.Add(new(batch.Id,batch.TurnNumber,owner.Seat,gained,old.Where(w=>gained.Any(g=>g.Name==w.Name)).ToArray()));
  }
  return result.Count==0?null:Array.AsReadOnly(result.ToArray());
 }
 private void CompleteSanwenNativeDrawQualification(NativeDrawInvocationProof proof)
 {
  if(!HasSanwenCapability)return;
  if(!ValidNativeDrawInvocation(proof))throw new InvalidOperationException("Sanwen requires the exact completed native draw invocation.");
  for(var index=0;index<_pendingCardsMovedBatches.Count;index++)
  {
   var batch=_pendingCardsMovedBatches[index];
   if(batch.ParentFrameId!=proof.ParentFrameId||!batch.Movements.Any(m=>proof.Materials.Any(p=>p.CardId==m.CardId&&p.MovementSequence==m.Sequence)))continue;
   batch=batch with{SanwenGains=null};
   if(batch.Id==proof.LastBatchId)batch=batch with{SanwenGains=CaptureSanwenGainQualifications(batch)};
   _pendingCardsMovedBatches[index]=batch;
  }
 }
 private bool SanwenIncomingBelongs(CardMovementBatchContext batch,int owner,int id)=>batch.NativeDrawInvocation is{}draw
  ?draw.OwnerSeat==owner&&draw.LastBatchId==batch.Id&&ValidNativeDrawInvocation(draw)&&draw.Materials.Any(m=>m.CardId==id)
  :batch.Movements.Any(m=>m.CardId==id&&m.From!=m.To&&m.To==CardLocation.Hand(owner));
 private SanwenGainProof? SanwenContextFor(CardMovementBatchContext batch,int owner)=>batch.SanwenGains?.SingleOrDefault(p=>p.OwnerSeat==owner);
 private bool ValidSanwenProof(SanwenGainProof q,CardMovementBatchContext batch)=>q.BatchId==batch.Id&&q.ActualTurn==batch.TurnNumber&&q.ActualTurn>0&&batch.SanwenGains?.Contains(q)==true&&q.Incoming.Count>0&&q.Witnesses.Count>0&&q.Incoming.Select(e=>e.CardId).Distinct().Count()==q.Incoming.Count&&q.Witnesses.All(w=>q.Incoming.All(i=>i.CardId!=w.CardId))&&q.Incoming.All(i=>SanwenIncomingBelongs(batch,q.OwnerSeat,i.CardId)&&q.Witnesses.Any(w=>w.Name==i.Name));
 private IReadOnlyList<int> SanwenPayable(SanwenGainProof q)=>Array.AsReadOnly(q.Incoming.Where(i=>_cardZones.GetLocation(i.CardId)==CardLocation.Hand(q.OwnerSeat)&&q.Witnesses.Any(w=>w.Name==i.Name&&_cardZones.GetLocation(w.CardId)==CardLocation.Hand(q.OwnerSeat))&&!IsSelfHandCategoryDiscardForbidden(q.OwnerSeat,GetAdvancedCard(i.CardId),CardLocation.Hand(q.OwnerSeat),OwnedCardMoveIntent.Discard)).Select(i=>i.CardId).ToArray());
 private bool CanOfferWangCan(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext w)
 {
  if(t.Effects.Any(e=>e.Op==SkillProgramEffectOp.ResolveSanwenActualGainDuplicates))return _players[c.OwnerSeat].IsAlive&&w.Sanwen is{}q&&q.OwnerSeat==c.OwnerSeat&&w.MovementBatch is{}batch&&ValidSanwenProof(q,batch)&&SanwenPayable(q).Count>0;
  if(t.Effects.Any(e=>e.Op==SkillProgramEffectOp.ViewDenglouTopAndUseBasics))return c.OwnerSeat==_currentSeat&&_players[c.OwnerSeat].IsAlive&&GetHand(_players[c.OwnerSeat]).Count==0;
  return true;
 }
 private bool WangCanSegmentMatches(ProgramSkillFrame f,WangCanReceipt r)=>CompleteProgramEventHistory().OfType<WangCanSegmentPaidEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.Stage==r.Stage&&e.ActualTurn==r.ActualTurn&&e.Before==r.Before&&e.After==r.After&&e.ActualCount==r.ActualCount)==1;
 private void RequireIssuedWangCan(ProgramSkillFrame f)
 {
  if(f.WangCan is not{}r)return;
  if(r.Source!=WangCanSource(f)||r.GameplayHash!=f.GameplayHash||r.Instruction!=f.InstructionIndex||r.WindowId!=f.WindowContext?.ParentFrameId||r.ActualTurn!=_turnNumber||r.Before>r.After||r.After>_movementSequence||CompleteProgramEventHistory().OfType<WangCanStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.WindowId==r.WindowId&&e.ActualTurn==r.ActualTurn&&e.Operation==GetPausedPrivateOfferEffect(f)?.Op)!=1||r.Entities.Distinct().Count()!=r.Entities.Count||r.Remaining.Distinct().Count()!=r.Remaining.Count)throw new InvalidOperationException("Wang Can lost its exact original source, window, instruction or physical receipt.");
  if(r.Sanwen is{}q&&(f.WindowContext?.Sanwen!=q||f.WindowContext.MovementBatch is not{}batch||!ValidSanwenProof(q,batch)))throw new InvalidOperationException("Sanwen lacks its original native gain batch and pre-gain witnesses.");
  if(r.Stage is WangCanStage.SanwenDiscard or WangCanStage.SanwenDraw or WangCanStage.QiaiGift or WangCanStage.DenglouViewChildren or WangCanStage.DenglouGain or WangCanStage.DenglouDiscard or WangCanStage.DenglouCancel)
  {
   if(!WangCanSegmentMatches(f,r)||f.PendingMovementContinuation is not{BeforeCount:0,CoverageResultBind:null})throw new InvalidOperationException("Wang Can cannot borrow another instruction's paid native movement.");
   var moves=_cardMovements.Where(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.Reason.Value==WangCanReason(r.Stage)).ToArray();
   if(moves.Length!=r.ActualCount||moves.Select(m=>m.CardId).Distinct().Count()!=moves.Length||r.Stage!=WangCanStage.SanwenDraw&&moves.Any(m=>!r.Entities.Contains(m.CardId)))throw new InvalidOperationException("Wang Can native payment differs from its exact entity and movement invoice.");
   if(r.Stage==WangCanStage.SanwenDiscard&&(r.Sanwen is not{}gain||moves.Any(m=>m.From!=CardLocation.Hand(f.OwnerSeat)||m.To!=CardLocation.DiscardPile||!gain.Incoming.Any(i=>i.CardId==m.CardId))))throw new InvalidOperationException("Sanwen may discard only its actual matching newly gained hand entities.");
   if(r.Stage==WangCanStage.SanwenDraw&&(moves.Any(m=>m.From!=CardLocation.DrawPile||m.To!=CardLocation.Hand(f.OwnerSeat))||r.ActualCount>r.Cursor||CompleteProgramEventHistory().OfType<WangCanSegmentPaidEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.Stage==WangCanStage.SanwenDiscard&&e.ActualCount*2==r.Cursor)!=1))throw new InvalidOperationException("Sanwen draw request must derive from exactly twice its own actual discard count.");
   if(r.Stage==WangCanStage.QiaiGift&&(r.Cursor<0||r.Cursor>=r.Seats.Count||moves.Any(m=>m.From.OwnerSeat!=r.Seats[r.Cursor]||m.From.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment)||m.To!=CardLocation.Hand(f.OwnerSeat))||r.ActualCount!=1))throw new InvalidOperationException("Qiai payment lost its exact living payer's chosen HE entity.");
   if(r.Stage==WangCanStage.DenglouGain&&moves.Any(m=>m.From!=r.Lease||m.To!=CardLocation.Hand(f.OwnerSeat)||GetProgramCardCategory(GetAdvancedCard(m.CardId).Kind)==SkillProgramCardCategory.Basic))throw new InvalidOperationException("Denglou first obtains only the actual nonbasic leased entities.");
   if(r.Stage is WangCanStage.DenglouDiscard or WangCanStage.DenglouCancel&&moves.Any(m=>m.From!=r.Lease||m.To!=CardLocation.DiscardPile))throw new InvalidOperationException("Denglou disposal cannot move another zone's cards.");

  }
  if(r.Lease is{}lease&&(lease!=WangCanLease(f.OwnerSeat)||r.Remaining.Any(id=>!r.Entities.Contains(id))))throw new InvalidOperationException("Denglou cannot claim another reserve entity.");
  if(r.Lease is{}originalLease)
  {
   var top=_cardMovements.Where(m=>m.Sequence>r.LeaseBefore&&m.Sequence<=r.LeaseAfter&&m.Reason.Value==WangCanReason(WangCanStage.DenglouViewChildren)).ToArray();
   if(r.Entities.Count>4||r.LeaseBefore>r.LeaseAfter||r.LeaseAfter>_movementSequence||!top.Select(m=>m.CardId).SequenceEqual(r.Entities)||top.Any(m=>m.From!=CardLocation.DrawPile||m.To!=originalLease)||CompleteProgramEventHistory().OfType<WangCanSegmentPaidEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.Stage==WangCanStage.DenglouViewChildren&&e.Before==r.LeaseBefore&&e.After==r.LeaseAfter&&e.ActualCount==r.Entities.Count)!=1)throw new InvalidOperationException("Denglou lost its once-paid original exact private top lease.");
  }
  if(r.DyingFrameId is{}dyingId&&_resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w=>w.Id==r.WindowId)?.ResumeDyingFrameId!=dyingId)throw new InvalidOperationException("Qiai lost its original accepted dying window.");
  if(r.Stage==WangCanStage.Completed&&(r.Native is not{}returned||CompleteProgramEventHistory().OfType<WangCanNativeReturnedEvent>().Count(e=>e.FrameId==f.Id&&e.UseId==returned.UseId&&e.ActionId==returned.ActionId)!=1||LifecycleCardUse(returned.UseId)is not null))throw new InvalidOperationException("Denglou terminal continuation requires its unique actual native return.");
  if(r.Stage==WangCanStage.DenglouNative&&(r.Native is not{}n||n.ProgramFrameId!=f.Id||CompleteProgramEventHistory().OfType<WangCanNativeIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.UseId==n.UseId&&e.ActionId==n.ActionId&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash)!=1))throw new InvalidOperationException("Denglou native use is not uniquely issued.");
 }
 private bool CanContinueIssuedWangCan(ProgramSkillFrame f)
 {
  if(f.WangCan is not{}r)return false;RequireIssuedWangCan(f);
  return true;
 }
 private bool IsWangCanMovement(ProgramSkillFrame f,SkillProgramEffect?effect,ProgramMovementContinuation p)=>f.WangCan is{}r&&effect is not null&&WangCanContract.Own(effect.Op)&&r.Stage is WangCanStage.SanwenDiscard or WangCanStage.SanwenDraw or WangCanStage.QiaiGift or WangCanStage.DenglouViewChildren or WangCanStage.DenglouGain or WangCanStage.DenglouDiscard or WangCanStage.DenglouCancel&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null&&CanContinueIssuedWangCan(f);
 private bool WangCanFirstChild(ProgramSkillFrame root,ResolutionFrame child)
 {
  if(root.WangCan is not{}r||!CanContinueIssuedWangCan(root))return false;
  if(child is CardUseFrame use)return DenglouNativeRidesOn(root,use);
  if(child is CardsMovedTriggerWindowFrame moved)return moved.Batch.ParentFrameId==root.Id&&moved.Batch.AwaitingProgramFrameId==root.Id&&moved.Batch.OriginOwnerSeat==root.OwnerSeat&&moved.Batch.OriginSkillId==root.SkillId&&moved.Batch.OriginSkillInstanceId==root.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After&&m.Reason.Value==WangCanReason(r.Stage));return false;
 }
 private ProgramSkillFrame? WangCanObserverRoot()=>FindProgramObserverRoot(static(e,r,c)=>e.WangCanFirstChild(r,c),static(e,i)=>e.WangCanObserverSuffix(i));
 private bool WangCanObserverSuffix(int index)=>HasObserverSuffix(index,static(e,i)=>e.IsSameNameHandObserverEdge(i)||e.DenglouNativePaymentEdge(i),static(e,i,d)=>e.IsSuspendedOrPaidRescueObserverSuffix(i,d)||e.TieredRoundZeroDyingRescueRide(i,d)||e.DrawFundedDistinctBasicDyingRescueRide(i,d));
 private bool HasWangCanDamageObserver(long id)=>WangCanObserverRoot()is{}r&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 private bool HasWangCanBeforeDamageObserver(long id)=>WangCanObserverRoot()is{}r&&_resolutionStack.Any(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 private bool IsWangCanDying()=>ActiveDying is{}d&&WangCanObserverRoot()is{}r&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 private bool TryAdvanceWangCanSubtree()=>_pendingDecision is null&&WangCanObserverRoot()is not null&&TryAdvanceObserverSubtree(advanceRecoveryReplacement:true);
 private bool AllowsWangCanNestedDamage(ProgramSkillFrame observer,int target,int amount,ProgramParticipantReference?source,DamageNature?nature,bool sourceLess)
 {
  if(sourceLess||observer.WindowContext?.Window is not(SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged)||observer.AttackAttempt is not null||observer.InstructionIndex<1||_resolutionStack.LastOrDefault()?.Id!=observer.Id||WangCanObserverRoot()is not{}r||r.Id==observer.Id)return false;var e=GetPausedPrivateOfferEffect(observer);return e is{Op:SkillProgramEffectOp.Damage}&&amount==e.Amount&&source==e.ActorReference&&nature==e.DamageNature&&target==(e.TargetReference is{}reference?ResolveProgramParticipant(observer,reference):ResolveProgramEffectTarget(observer,e.Target));
 }
}
