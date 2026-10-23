namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool HasAnniversaryCaiYang=>_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ArmAnniversaryCaiYangXunji);
 private static CardConversionSource CaiYangSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
 private static ProgramTriggerCandidate CaiYangCandidate(ProgramSkillFrame f)=>new(f.OwnerSeat,f.SkillId,f.TriggerId!,f.SkillInstanceId,f.GameplayHash,0,f.WindowContext?.OccurrenceIndex??0);
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,45600)] private void StartCaiYangNextTurn(TurnStartedEvent t)
 {
  if(!HasAnniversaryCaiYang||ProgramEventHistory<CaiYangXunjiArmedEvent>().Count==0)return;
  foreach(var g in ProgramEventHistory<CaiYangXunjiArmedEvent>().Select(e=>e.Grant).Where(g=>g.Target==t.ActorSeat&&g.IssuedTurn<t.TurnNumber&&!ProgramEventHistory<CaiYangXunjiDueEvent>().Any(e=>e.GrantFrameId==g.FrameId)).ToArray())
   AdvanceEventRulesAndQueueFact(new CaiYangXunjiDueEvent(g.FrameId,t.TurnNumber,t.ActorSeat));
 }
 [ProgramRuleObserver(ProgramRulePhase.AfterEvent,45600)] private void TrackCaiYangBlackUse(CardUseAppearanceCapturedEvent e)
 {
  if(!HasAnniversaryCaiYang||e.Action is not{Type:CardActionType.Use,EffectiveSuit:Suit.Spade or Suit.Club} a||
   !ProgramEventHistory<CaiYangXunjiDueEvent>().Any(d=>d.ActualTurn==_turnNumber&&d.Target==a.ActorSeat)||ProgramEventHistory<CaiYangBlackUseEvent>().Any(f=>f.ActionId==a.ActionId))return;
  AdvanceEventRulesAndQueueFact(new CaiYangBlackUseEvent(_turnNumber,a.ActorSeat,a.ActionId));
 }
 [ProgramRuleObserver(ProgramRulePhase.AfterEvent,45601)] private void ExpireCaiYangXunji(TurnEndedEvent t)
 {
  if(!HasAnniversaryCaiYang||ProgramEventHistory<CaiYangXunjiDueEvent>().Count==0)return;
  foreach(var d in ProgramEventHistory<CaiYangXunjiDueEvent>().Where(d=>d.ActualTurn==t.TurnNumber&&d.Target==t.ActorSeat&&!ProgramEventHistory<CaiYangXunjiConsumedEvent>().Any(e=>e.GrantFrameId==d.GrantFrameId)).ToArray())
   AdvanceEventRulesAndQueueFact(new CaiYangXunjiConsumedEvent(d.GrantFrameId,t.TurnNumber,false));
 }
 private bool ValidCaiYangGrant(CaiYangXunjiGrant g)=>g.Source.SkillId==AnniversaryCaiYangComposition.Xunji&&g.Source.BindingId==AnniversaryCaiYangComposition.Arm&&
  IsValidPlayerSeat(g.Source.OwnerSeat)&&IsValidPlayerSeat(g.Target)&&g.Target!=g.Source.OwnerSeat&&g.FrameId>0&&
  _contentRegistry.GetSkill(g.Source.SkillId).Program?.GameplayHash==g.Hash&&ProgramEventHistory<CaiYangXunjiArmedEvent>().Count(e=>e.Grant==g)==1;
 [ProgramPipelineHook(ProgramPipelinePhase.AppendTurnEndingItems,45600)] private void AppendCaiYangEnding(List<TurnEndingBoundaryItem> items)
 {
  if(!HasAnniversaryCaiYang||ProgramEventHistory<CaiYangXunjiDueEvent>().Count==0)return;
  // Remove the ordinary skill enumeration; only actual issued next-turn obligations can enter.
  items.RemoveAll(i=>i.Candidate is{SkillId:AnniversaryCaiYangComposition.Xunji,BindingId:AnniversaryCaiYangComposition.Ending});
  var trigger=_contentRegistry.GetSkill(AnniversaryCaiYangComposition.Xunji).Program!.Triggers.Single(t=>t.Id==AnniversaryCaiYangComposition.Ending);
  foreach(var due in ProgramEventHistory<CaiYangXunjiDueEvent>().Where(d=>d.ActualTurn==_turnNumber&&d.Target==_currentSeat&&!ProgramEventHistory<CaiYangXunjiConsumedEvent>().Any(e=>e.GrantFrameId==d.GrantFrameId)).ToArray())
  {
   var g=ProgramEventHistory<CaiYangXunjiArmedEvent>().Single(e=>e.Grant.FrameId==due.GrantFrameId).Grant;
   if(!ValidCaiYangGrant(g)||!_players[g.Source.OwnerSeat].IsAlive||!CanIssueSelectedActorDuel(g.Target,g.Source.OwnerSeat)||
    !ProgramEventHistory<CaiYangBlackUseEvent>().Any(e=>e.ActualTurn==_turnNumber&&e.ActorSeat==g.Target))continue;
   var c=new ProgramTriggerCandidate(g.Source.OwnerSeat,g.Source.SkillId,trigger.Id,g.Source.SkillInstanceId,g.Hash,trigger.Priority,items.Count);
   items.Add(new(TurnEndingBoundaryItemKind.Program,c.Priority,"cai-yang-xunji:"+g.FrameId,c,CaptureProgramTriggerFacts(_players[c.OwnerSeat])));
  }
 }
 private CaiYangXunjiGrant? CaiYangEndingWitness(ProgramTriggerCandidate c,ProgramSkillWindowContext w,bool begun=false)
 {
  if(c.SkillId!=AnniversaryCaiYangComposition.Xunji||c.BindingId!=AnniversaryCaiYangComposition.Ending||w.Window!=SkillProgramTriggerWindow.TurnEnding||w.SourceSeat!=_currentSeat||w.TargetSeat!=_currentSeat||
   _resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(x=>x.Id==w.ParentFrameId)is not{} p||p.OwnerSeat!=_currentSeat||p.TurnNumber!=_turnNumber||p.ItemIndex<0||p.ItemIndex>=p.Items.Count||p.Items[p.ItemIndex].Candidate!=c)return null;
  return ProgramEventHistory<CaiYangXunjiArmedEvent>().Select(e=>e.Grant).SingleOrDefault(g=>p.Items[p.ItemIndex].StableIdentity=="cai-yang-xunji:"+g.FrameId&&g.Source.OwnerSeat==c.OwnerSeat&&
   g.Source.SkillInstanceId==c.SkillInstanceId&&g.Hash==c.GameplayHash&&g.Target==_currentSeat&&ValidCaiYangGrant(g)&&
   ProgramEventHistory<CaiYangXunjiDueEvent>().Count(d=>d.GrantFrameId==g.FrameId&&d.ActualTurn==_turnNumber&&d.Target==g.Target)==1&&
   ProgramEventHistory<CaiYangBlackUseEvent>().Any(e=>e.ActualTurn==_turnNumber&&e.ActorSeat==g.Target)&&
   ProgramEventHistory<CaiYangXunjiConsumedEvent>().Count(e=>e.GrantFrameId==g.FrameId)==(begun?1:0));
 }
 [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate,45600)] private bool AdmitCaiYangXunji(ProgramTriggerCandidate c,ProgramSkillWindowContext w)=>
  HasAnniversaryCaiYang&&c.SkillId==AnniversaryCaiYangComposition.Xunji&&(c.BindingId is AnniversaryCaiYangComposition.Ending or AnniversaryCaiYangComposition.Retaliate)&&IsValidPlayerSeat(c.OwnerSeat)&&_players[c.OwnerSeat].IsAlive&&
   (CaiYangEndingWitness(c,w)is{}g&&CanIssueSelectedActorDuel(g.Target,c.OwnerSeat)||CaiYangDuelDamage(c.OwnerSeat,w)is not null);
 private int CaiYangChainCursor(IDamageAttempt a)=>a.IsChainPropagation?a is ProgramAttackHandle?GetProgramAttackState(a.ResolutionId).ChainedTargetIndex:GetCardAttackState(a.ResolutionId).ChainedTargetIndex:0;
 private bool CaiYangFirstDamage(int owner)=>!EventsSinceLastBoundary(e=>e is TurnStartedEvent).OfType<DamageAppliedEvent>().Any(e=>e.SourceSeat==owner&&!e.SourceLess&&e.Amount>0);
 private bool TryBeginAnniversaryCaiYangJiaofengSourceWindow(IDamageAttempt a)
 {
  if(!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversaryCaiYangJiaofeng)||a.IsSourceLess||a.DamageAmountFinalized||a.DamageWasApplied||a.DamageAmount<=0||
   !IsValidPlayerSeat(a.SourceSeat)||!_players[a.SourceSeat].IsAlive||!_players[a.TargetSeat].IsAlive||!HasRuntimeSkill(_players[a.SourceSeat],AnniversaryCaiYangComposition.Jiaofeng)||!CaiYangFirstDamage(a.SourceSeat))return false;
  var owner=_players[a.SourceSeat];var lost=UnambiguousHp(owner)is{}hp?Math.Max(0,owner.MaxHp-hp):0;
  if(lost==0||ProgramEventHistory<CaiYangJiaofengWindowIssuedEvent>().Any(e=>e.Origin.ActualTurn==_turnNumber&&e.Origin.AttackId==a.ResolutionId&&e.Origin.Target==a.TargetSeat&&e.Origin.ChainCursor==CaiYangChainCursor(a)&&e.Origin.Chain==a.IsChainPropagation))return false;
  var c=CollectProgramTriggerCandidates(owner,SkillProgramTriggerWindow.BeforeDamageApplied).FirstOrDefault(c=>c.SkillId==AnniversaryCaiYangComposition.Jiaofeng&&c.BindingId==AnniversaryCaiYangComposition.First);
  if(c is null)return false;
  var parent=_resolutionStack.Last().Id;var id=++_resolutionSequence;
  var r=new CaiYangJiaofengOrigin(id,parent,a.ResolutionId,new(c.SkillId,c.BindingId,c.OwnerSeat,c.SkillInstanceId),c.GameplayHash,_turnNumber,a.TargetSeat,CaiYangChainCursor(a),a.IsChainPropagation,a.DamageAmount,lost);
  var window=new BeforeDamageProgramWindowFrame(id,parent,a.SourceSeat,a.TargetSeat,a.DamageAmount,GetDamageNature(a),BeforeDamageProgramContinuation.Attack,[new(c,CaptureProgramTriggerFacts(owner))]){CaiYangJiaofeng=r,ContinuationAttackResolutionId=a.ResolutionId};
  PushRuntimeFrame(window);AdvanceEventRulesAndQueueFact(new CaiYangJiaofengWindowIssuedEvent(r));AdvanceRuntimeTop<BeforeDamageProgramWindowFrame>();return true;
 }
 private bool ValidCaiYangFirstOrigin(CaiYangJiaofengOrigin r)=>r.Source.SkillId==AnniversaryCaiYangComposition.Jiaofeng&&r.Source.BindingId==AnniversaryCaiYangComposition.First&&r.LostHp>0&&r.OriginalAmount>0&&
  r.ActualTurn==_turnNumber&&_contentRegistry.GetSkill(r.Source.SkillId).Program?.GameplayHash==r.Hash&&ProgramEventHistory<CaiYangJiaofengWindowIssuedEvent>().Count(e=>e.Origin==r)==1;
 private CaiYangJiaofengOrigin? CaiYangFirstWitness(ProgramTriggerCandidate c,ProgramSkillWindowContext w)=>
  w.Window==SkillProgramTriggerWindow.BeforeDamageApplied&&_resolutionStack.OfType<BeforeDamageProgramWindowFrame>().SingleOrDefault(x=>x.Id==w.ParentFrameId)is{CaiYangJiaofeng:{}r} p&&ValidCaiYangFirstOrigin(r)&&
  c.OwnerSeat==r.Source.OwnerSeat&&c.SkillId==r.Source.SkillId&&c.BindingId==r.Source.BindingId&&c.SkillInstanceId==r.Source.SkillInstanceId&&c.GameplayHash==r.Hash&&w.SourceSeat==c.OwnerSeat&&w.TargetSeat==r.Target&&w.Amount==r.OriginalAmount&&
  p.CandidateIndex>=0&&p.CandidateIndex<p.Candidates.Count&&p.Candidates[p.CandidateIndex].Candidate==c&&CurrentDamageAttempt is{IsSourceLess:false,DamageAmountFinalized:false}a&&
  a.ResolutionId==r.AttackId&&a.SourceSeat==c.OwnerSeat&&a.TargetSeat==r.Target&&CaiYangChainCursor(a)==r.ChainCursor?r:null;
 private bool ResumeAnniversaryCaiYangJiaofengSourceWindow(BeforeDamageProgramWindowFrame p,IDamageAttempt a)
 {
  if(p.CaiYangJiaofeng is not{}r)return false;
  if(!ValidCaiYangFirstOrigin(r)||p.Id!=r.WindowId||p.ParentFrameId!=r.ParentId||p.CandidateIndex!=p.Candidates.Count||p.Prevented||p.RedirectedTargetSeat is not null||
   a.ResolutionId!=r.AttackId||a.IsSourceLess||a.SourceSeat!=r.Source.OwnerSeat||a.TargetSeat!=r.Target||CaiYangChainCursor(a)!=r.ChainCursor||a.IsChainPropagation!=r.Chain||a.DamageAmountFinalized||a.DamageAmount!=r.OriginalAmount||_resolutionStack.LastOrDefault()?.Id!=r.ParentId||
   ProgramEventHistory<CaiYangJiaofengWindowReturnedEvent>().Any(e=>e.WindowId==p.Id))throw new InvalidOperationException("Jiaofeng lost its original unfinalized damage return.");
  AdvanceEventRulesAndQueueFact(new CaiYangJiaofengWindowReturnedEvent(p.Id,r.AttackId,r.Target,r.ChainCursor));if(!ApplyAttackDamage(a))CompleteDamageAttack(a);return true;
 }
 [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount,45600)] private int CaiYangJiaofengDamageBonus(IDamageAttempt a)=>a.IsSourceLess?0:
  ProgramEventHistory<CaiYangJiaofengWindowIssuedEvent>().Count(e=>e.Origin.LostHp>1&&e.Origin.AttackId==a.ResolutionId&&e.Origin.Source.OwnerSeat==a.SourceSeat&&e.Origin.Target==a.TargetSeat&&e.Origin.ChainCursor==CaiYangChainCursor(a)&&e.Origin.Chain==a.IsChainPropagation&&ValidCaiYangFirstOrigin(e.Origin));
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,45600,SkillProgramEffectOp.ResolveAnniversaryCaiYangXunji,SkillProgramEffectOp.ResolveAnniversaryCaiYangJiaofeng)]
 private bool CanOfferCaiYang(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext w)=>t.Id switch{
  AnniversaryCaiYangComposition.First=>CaiYangFirstWitness(c,w)is not null,
  AnniversaryCaiYangComposition.Ending=>CaiYangEndingWitness(c,w)is{}g&&_players[c.OwnerSeat].IsAlive&&CanIssueSelectedActorDuel(g.Target,c.OwnerSeat),
  AnniversaryCaiYangComposition.Retaliate=>CaiYangDuelDamage(c.OwnerSeat,w)is not null,_=>false};
}
