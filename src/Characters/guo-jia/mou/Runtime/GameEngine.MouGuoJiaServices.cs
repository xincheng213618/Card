namespace CardGame.Core;
public sealed partial class GameEngine
{
 private MouGuoJiaRuntime MouGuoJia=>GetRuntimeModule<MouGuoJiaRuntime>();
 [ProgramCapability]private sealed class MouGuoJiaFacts(GameEngine e):IMouGuoJiaFacts
 {
  public int Turn=>e._turnNumber;public long Sequence=>e._movementSequence;public bool Finished=>e._winner!=Winner.None;public bool Alive(int s)=>e.IsValidPlayerSeat(s)&&e._players[s].IsAlive;public bool Human(int s)=>e._players[s].IsHuman;public string Name(int s)=>e._players[s].Name;
  public SkillProgram? Program(string id)=>e._contentRegistry.Skills.GetValueOrDefault(id)?.Program;public IEnumerable<T> History<T>()where T:IGameEvent=>e.ProgramEventHistory<T>();public bool Yin(int s,string instance)=>e.GetProgramBooleanState(s,MouGuoJiaRuntime.Xianmou,instance,"yin");
  public IReadOnlyList<int> Living=>Array.AsReadOnly(e._players.Where(p=>p.IsAlive).Select(p=>p.Seat).ToArray());
  public IReadOnlyList<MouGjEntity> Hand(int s)=>Array.AsReadOnly(e.GetHand(e._players[s]).Select(c=>new MouGjEntity(c.Id,c.Kind,CardLocation.Hand(s),c.DisplayName)).ToArray());
  public bool At(MouGjEntity c)=>e._cardZones.GetLocation(c.Id)==c.From;
  public bool Discardable(int s,int id)=>e._cardZones.GetLocation(id)==CardLocation.Hand(s)&&!e.IsSelfHandCategoryDiscardForbidden(s,e.GetAdvancedCard(id),CardLocation.Hand(s),OwnedCardMoveIntent.Discard);
  public int LossCount(int s)=>e._cardMovements.Count(m=>m.TurnNumber==e._turnNumber&&m.From.OwnerSeat==s&&m.From!=m.To&&m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment);
  public IReadOnlyList<(int Seat,string Instance)> XianmouOwners=>Array.AsReadOnly(e._players.Where(p=>p.IsAlive&&e.EnabledSkillPrograms(p).Any(s=>s.Id==MouGuoJiaRuntime.Xianmou)).Select(p=>(p.Seat,e.GetRuntimeSkillInstanceId(p,MouGuoJiaRuntime.Xianmou))).ToArray());
  public bool Parent(ProgramSkillFrame f)=>e.MouGjParent(f);
 }
 private bool MouGjParent(ProgramSkillFrame f)
 {
  if(f.TriggerId is null||f.TriggerId!=f.ActivationId||f.WindowContext is not{}w||w.OwnerSeat!=f.OwnerSeat)return false;var at=_resolutionStack.FindIndex(x=>x.Id==f.Id);if(at<1||_resolutionStack[at-1].Id!=w.ParentFrameId)return false;
  if(f.TriggerId=="opening-state")return f.SkillId==MouGuoJiaRuntime.Xianmou&&w.Window==SkillProgramTriggerWindow.GameStarting&&_resolutionStack[at-1]is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.GameStarting}life&&life.CandidateIndex>=0&&life.CandidateIndex<life.Candidates.Count&&MountObserverCandidateMatches(f,life.Candidates[life.CandidateIndex]);
  if(f.SkillId==MouGuoJiaRuntime.Xianmou&&f.TriggerId is"own-ended-loss" or"other-ended-loss")return w.Window==SkillProgramTriggerWindow.AfterTurnEnded&&_resolutionStack[at-1]is DeferredTurnEndFrame ended&&IsActualAfterTurnEndedParent(ended)&&MountObserverCandidateMatches(f,AfterTurnEndedCandidate(ended));
  return f.SkillId==MouGuoJiaRuntime.Yiji&&f.TriggerId=="per-damage-point"&&w.Window==SkillProgramTriggerWindow.AfterDamageApplied&&w.TargetSeat==f.OwnerSeat&&w.Amount>0&&_resolutionStack[at-1]is DamageTriggerWindowFrame damage&&damage.ParentFrameId==w.DamageFrameId&&damage.TriggerWindow==w.Window&&damage.CandidateIndex>=0&&damage.CandidateIndex<damage.Candidates.Count&&MountObserverCandidateMatches(f,damage.Candidates[damage.CandidateIndex].ToProgramCandidate());
 }
 [ProgramCapability]private sealed class MouGuoJiaNative(GameEngine e):IMouGuoJiaNative
 {
  public void Finish(ProgramSkillFrame f)=>e.FinishProgramSkill(f,true);public void Publish()=>e.AdvanceRulesAndPublishState();
  public bool Drain(ProgramSkillFrame f)=>e.TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||e.TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||e.TryBeginAdvancedSkillsChanged(f.Id)||e.TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||e.TryBeginCardsMovedProgramWindow(f.Id);
  public void State(ProgramSkillFrame f,bool yin){if(!e.MouGuoJia.Valid(f)||f.SkillId!=MouGuoJiaRuntime.Xianmou)throw new InvalidOperationException("Xianmou state requires its issued opening or paid original branch.");e.SetProgramBooleanState(f.OwnerSeat,f.SkillId,f.SkillInstanceId,"yin",yin);}
  public IReadOnlyList<MouGjEntity> Peek(ProgramSkillFrame f,int count)
  {
   if(!e.MouGuoJia.Valid(f)||f.MouGuoJia is not{}r||!(r.Stage==MouGuoJiaStage.RemoveGrantChildren&&count==5||r.Operation==SkillProgramEffectOp.ResolveMouGuoJiaYiji&&r.Stage==MouGuoJiaStage.YijiCard&&count==2))throw new InvalidOperationException("Mou Guo Jia can only privately view the exact current top five or inherited Yiji top two.");
   _=e.EnsureDrawPile();return Array.AsReadOnly(e._cardZones.CardsAt(CardLocation.DrawPile).TakeLast(count).Reverse().Select(c=>new MouGjEntity(c.Id,c.Kind,CardLocation.DrawPile,c.DisplayName)).ToArray());
  }
  public void Move(ProgramSkillFrame f)
  {
   if(!e.MouGuoJia.Valid(f)||f.MouGuoJia is not{Stage:MouGuoJiaStage.MovementChildren or MouGuoJiaStage.YijiChildren}r||r.Selected.Any(id=>!r.Viewed.Any(c=>c.Id==id&&e._cardZones.GetLocation(id)==c.From))||r.Yin&&r.Selected.Any(id=>!new MouGuoJiaFacts(e).Discardable(r.Target,id)))throw new InvalidOperationException("Mou Guo Jia lost his original privately viewed payable entity.");
   var yiji=r.Stage==MouGuoJiaStage.YijiChildren;var to=r.Yin?CardLocation.DiscardPile:CardLocation.Hand(yiji?r.Target:f.OwnerSeat);
   void Paid(long batch,IReadOnlyList<CardMovementRecord> moves)
   {
    var parent=e.GetActiveProgramFrame(f.Id);var paid=parent.MouGuoJia!;if(!e.MouGuoJia.Valid(parent)||moves.Count!=r.Selected.Count||moves.Any(m=>!r.Viewed.Any(c=>r.Selected.Contains(c.Id)&&c.Id==m.CardId&&c.From==m.From)||m.Sequence<=r.Before||m.To!=to&&m.To!=CardLocation.OutsideGame))throw new InvalidOperationException("Mou Guo Jia lost his actual selected gain, native logical discard or top-card Give receipt.");
    e.ReplaceRuntimeFrame(f.Id,parent with{MouGuoJia=paid with{Actual=moves.Count(m=>m.To==to),After=moves.Max(m=>m.Sequence)}});e.AdvanceEventRulesAndQueueFact(new MouGjMovementPaidEvent(f.Id,r.Cursor,moves.Count(m=>m.To==to),r.Before,moves.Max(m=>m.Sequence)));if(yiji)e.RecordNativeGivenCards(f.Id,f.OwnerSeat,r.Target,r.Selected,r.Before);
   }
   if(r.Yin)e.MoveProgramLogicalDiscard(f,r.Target,r.Selected,new("skill-program.mou:xianmou.discard"),Paid);else e.MoveProgramCardsFromMultipleSources(r.Selected,to,new(yiji?"skill-program.mou-guo-jia:yiji.give":"skill-program.mou:xianmou.obtain"),Paid);
  }
  public void YijiGrant(ProgramSkillFrame f,bool acquire)
  {
   if(!e.MouGuoJia.Valid(f)||f.MouGuoJia is not{Yin:false}r||!(acquire?r.Stage==MouGuoJiaStage.GrantChildren&&r.Actual<r.X:r.Stage==MouGuoJiaStage.RemoveGrantChildren))throw new InvalidOperationException("Only another actual Yang activation may replace its own earlier Yiji source.");
   var key=MouGuoJiaRuntime.GrantSource(f);var count=0;if(acquire)count=e.AcquireRuntimeSkills(e._players[f.OwnerSeat],key,[MouGuoJiaRuntime.Yiji]).Count;else foreach(var grant in e._players[f.OwnerSeat].SkillGrants.Grants.Where(g=>g.SkillId==MouGuoJiaRuntime.Yiji&&g.SourceId=="acquired:"+key).ToArray())if(e._players[f.OwnerSeat].SkillGrants.RemoveGrant(grant.GrantId))count++;
   e.AdvanceEventRulesAndQueueFact(new MouGjYijiGrantEvent(f.Id,r.Source,key,acquire,count));
  }
  public void Judge(ProgramSkillFrame f)=>e.BeginMouGjJudgment(f);
  public void Damage(ProgramSkillFrame f){if(!e.MouGuoJia.Valid(f)||f.MouGuoJia is not{Stage:MouGuoJiaStage.DamageChildren,LightningHit:true}r||e.ProgramEventHistory<MouGjLightningIssuedEvent>().Count(p=>p.FrameId==f.Id&&p.JudgmentId==r.JudgmentId&&p.Target==f.OwnerSeat&&p.Amount==3)!=1)throw new InvalidOperationException("Xianmou requires his exact finalized Lightning hit once.");e.BeginProgramSkillDamage(f,f.OwnerSeat,3,nature:DamageNature.Thunder,sourceLess:true);}
 }
 [ProgramViewContributor(ProgramViewContribution.PrivateCards,37100)]private CardSnapshot[] MouGjPrivateCards(int viewer)=>_resolutionStack.OfType<ProgramSkillFrame>().Where(f=>f.OwnerSeat==viewer&&f.MouGuoJia is not null).SelectMany(f=>f.MouGuoJia!.Viewed.Select(c=>c.Id)).Distinct().Select(id=>ToSnapshot(GetAdvancedCard(id))).ToArray();
}
