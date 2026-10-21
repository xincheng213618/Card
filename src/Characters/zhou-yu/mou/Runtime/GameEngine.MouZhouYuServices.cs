namespace CardGame.Core;
public sealed partial class GameEngine
{
 private MouZhouYuRuntime MouZyAnniversary=>GetRuntimeModule<MouZhouYuRuntime>();
 [ProgramCapability]private sealed class MouZhouYuFacts(GameEngine e):IMouZhouYuFacts
 {
  public int Turn=>e._turnNumber;public long Sequence=>e._movementSequence;public bool Finished=>e._winner!=Winner.None;public int FactionCount=>e.GetLivingFactionCount();
  public bool Alive(int s)=>e.IsValidPlayerSeat(s)&&e._players[s].IsAlive;public bool Human(int s)=>e._players[s].IsHuman;public string Name(int s)=>e._players[s].Name;public int HandCount(int s)=>e.GetHand(e._players[s]).Count;
  public IReadOnlyList<int> Living()=>Array.AsReadOnly(e._players.Where(p=>p.IsAlive).Select(p=>p.Seat).ToArray());
  public SkillProgram? Program(string id)=>e._contentRegistry.Skills.GetValueOrDefault(id)?.Program;
  public IEnumerable<T> History<T>()where T:IGameEvent=>e.ProgramEventHistory<T>();
  public string Instance(int s,string skill)=>e.GetRuntimeSkillInstanceId(e._players[s],skill);
  public bool Yin(int s,string instance)=>e.GetProgramBooleanState(s,MouZhouYuRuntime.Yingmou,instance,"yin");
  public IReadOnlyList<CardConversionSource> FireSources()=>Array.AsReadOnly(e._players.Where(p=>p.IsAlive&&e.EnabledSkillPrograms(p).Any(x=>x.Id==MouZhouYuRuntime.Ronghuo)).Select(p=>new CardConversionSource(MouZhouYuRuntime.Ronghuo,"next-fire-use",p.Seat,Instance(p.Seat,MouZhouYuRuntime.Ronghuo))).ToArray());
  public long? FireDamage(ProgramTriggerCandidate c,ProgramSkillWindowContext w)
  {
   if(c.SkillId!=MouZhouYuRuntime.Ronghuo||c.BindingId!="fire-after-damage"||w.Window!=SkillProgramTriggerWindow.AfterDamageApplied||w.OwnerSeat!=c.OwnerSeat||w.Amount<=0||w.OccurrenceIndex!=c.OccurrenceIndex||e._resolutionStack.SingleOrDefault(x=>x.Id==w.ParentFrameId)is not DamageTriggerWindowFrame window||window.ParentFrameId!=w.DamageFrameId||window.TriggerWindow!=w.Window||window.CandidateIndex<0||window.CandidateIndex>=window.Candidates.Count||window.Candidates[window.CandidateIndex].ToProgramCandidate()!=c||e._resolutionStack.SingleOrDefault(x=>x.Id==window.ParentFrameId)is not DamageFrame{Nature:DamageNature.Fire,Amount:>0}d||d.TargetSeat!=w.TargetSeat||d.Amount!=w.Amount)return null;
   return d.Id;
  }
  public MouZhouYuCompletedUse? Use(ProgramTriggerCandidate c,ProgramSkillWindowContext w)
  {
   if(w.Window!=SkillProgramTriggerWindow.CardUseCompleted||w.OwnerSeat!=c.OwnerSeat||w.CardUse is not{}card||e._resolutionStack.SingleOrDefault(x=>x.Id==w.ParentFrameId)is not ProgramCardTriggerWindowFrame window||window.Action.ActionId!=card.CardActionId||window.Action is not{Type:CardActionType.Use}a||a.ActorSeat!=c.OwnerSeat||window.CandidateIndex<0||window.CandidateIndex>=window.Candidates.Count||window.Candidates[window.CandidateIndex]is not{}now||now.OwnerSeat!=c.OwnerSeat||now.SkillId!=c.SkillId||now.TriggerId!=c.BindingId||now.SkillInstanceId!=c.SkillInstanceId||now.GameplayHash!=c.GameplayHash)return null;
   var use=e._resolutionStack.OfType<CardUseFrame>().SingleOrDefault(x=>x.Action?.ActionId==a.ActionId);return new(window.Id,use?.Id??0,a);
  }
  public bool Parent(ProgramSkillFrame f)
  {
   if(f.TriggerId!=f.ActivationId||f.WindowContext is not{}w||w.OwnerSeat!=f.OwnerSeat)return false;var n=e._resolutionStack.FindIndex(x=>x.Id==f.Id);if(n<1||e._resolutionStack[n-1].Id!=w.ParentFrameId)return false;
   if(f.TriggerId=="opening-state")return w.Window==SkillProgramTriggerWindow.GameStarting&&e._resolutionStack[n-1]is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.GameStarting};
   return f.TriggerId=="used-other-targets"&&Use(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,0),w)is not null;
  }
  public IReadOnlyList<MouZhouYuCard> Hand(int s)=>Array.AsReadOnly(e.GetHand(e._players[s]).Select(c=>{var kind=e.AdvancedEffectiveHandKind(e._players[s],c);return new MouZhouYuCard(c.Id,CardLocation.Hand(s),c.DisplayName,kind,IsSlashCard(kind)||GetProgramCardCategory(kind)==SkillProgramCardCategory.Trick&&(IsDamageCardKind(kind)||IsCompletedUndamagedDamageUse(kind)||kind==CardKind.Lightning));}).ToArray());
  public IReadOnlyList<MouZhouYuCard> DiscardableHand(int s)=>Hand(s).Where(c=>!e.IsSelfHandCategoryDiscardForbidden(s,e.GetAdvancedCard(c.Id),c.From,OwnedCardMoveIntent.Discard)).ToArray();
  public IReadOnlyList<LegalAction> UseOptions(ProgramSkillFrame f)=>e.MouZyUseOptions(f);
  public IReadOnlyList<IReadOnlyList<int>> FireOptions(ProgramSkillFrame f)=>e.MouZyFireOptions(f);
 }
 [ProgramCapability]private sealed class MouZhouYuNative(GameEngine e):IMouZhouYuNative
 {
  public void Finish(ProgramSkillFrame f)=>e.FinishProgramSkill(f,true);public void Publish()=>e.AdvanceRulesAndPublishState();
  public bool Drain(ProgramSkillFrame f)=>e.TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||e.TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||e.TryBeginAdvancedSkillsChanged(f.Id)||e.TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||e.TryBeginCardsMovedProgramWindow(f.Id);
  public void State(ProgramSkillFrame f,bool yin){if(!e.MouZyAnniversary.Valid(f))throw new InvalidOperationException("Yingmou state lost its original accepted branch.");e.SetProgramBooleanState(f.OwnerSeat,MouZhouYuRuntime.Yingmou,f.SkillInstanceId,"yin",yin);}
  public void Use(ProgramSkillFrame f)=>e.IssueMouZyUse(f);
  public void Discard(ProgramSkillFrame f)
  {
   if(!e.MouZyAnniversary.Valid(f)||f.MouZhouYuAnniversary is not{Stage:MouZhouYuStage.DiscardChildren,Frozen.Count:0}r||r.Selected.Count==0||r.Selected.Any(id=>!new MouZhouYuFacts(e).DiscardableHand(r.Actor).Any(c=>c.Id==id)))throw new InvalidOperationException("Yingmou discard requires no related Hand cards and the actor's real discardable Hand.");
   e.MoveProgramLogicalDiscard(f,r.Actor,r.Selected,new(MouZhouYuRuntime.DiscardReason),(_,moves)=>{var parent=e.GetActiveProgramFrame(f.Id);var paid=parent.MouZhouYuAnniversary!;if(!e.MouZyAnniversary.Valid(parent)||moves.Count!=r.Selected.Count||moves.Any(m=>!r.Selected.Contains(m.CardId)||m.From!=CardLocation.Hand(r.Actor)||m.Sequence<=r.Before||m.To!=CardLocation.DiscardPile&&m.To!=CardLocation.OutsideGame))throw new InvalidOperationException("Yingmou lost its primary native Hand discard receipt.");e.ReplaceRuntimeFrame(f.Id,parent with{MouZhouYuAnniversary=paid with{After=moves.Max(m=>m.Sequence)}});foreach(var m in moves)e.AdvanceEventRulesAndQueueFact(new MouZhouYuDiscardPaidEvent(f.Id,r.Actor,m.CardId,m.Sequence));});
  }
  public void Damage(ProgramSkillFrame f){if(!e.MouZyAnniversary.Valid(f)||f.MouZhouYuAnniversary is not{Stage:MouZhouYuStage.DamageChildren,Damaged:false}r)throw new InvalidOperationException("Yingmou final fire damage requires its genuine undamaged child chain.");e.BeginProgramSkillDamage(f,r.Target,1,nature:DamageNature.Fire,explicitSourceSeat:f.OwnerSeat);}
 }
 [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount,36000)]private int MouZyFireBonus(IDamageAttempt a)
 {var u=a is CardAttackHandle&&!a.IsChainPropagation?LifecycleCardUse(a.ResolutionId):null;return MouZyAnniversary.DamageBonus(u?.SourceSeat??-1,u?.CardKind??CardKind.Dodge,u?.Action is{Type:CardActionType.Use});}
 [ProgramDamageHook(ProgramDamagePhase.AppliedBeforeDying,36000)]private bool MouZyDamageApplied(IDamageAttempt a,long id)
 {
  if(!a.DamageWasApplied||_resolutionStack.SingleOrDefault(x=>x.Id==id)is not DamageFrame{Amount:>0}d)return false;
  foreach(var f in _resolutionStack.OfType<ProgramSkillFrame>().Where(f=>f.MouZhouYuAnniversary is{Target:var t}&&t==d.TargetSeat).ToArray())
  {var n=_resolutionStack.FindIndex(x=>x.Id==f.Id);if(_resolutionStack.FindIndex(x=>x.Id==id)>n&&MouZyAnniversary.Valid(f))MouZyAnniversary.Damaged(f,id,d.TargetSeat,d.Amount);}
  return false;
 }
}
