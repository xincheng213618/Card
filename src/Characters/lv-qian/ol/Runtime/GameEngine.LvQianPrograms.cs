namespace CardGame.Core;
public sealed partial class GameEngine
{
 private const string ZengdaoDepositReason="skill-program.zengdao.deposit",ZengdaoConsumeReason="skill-program.zengdao.consume";
 private SkillProgramStepOutcome ExecuteLvQian(ProgramSkillFrame supplied,SkillProgramEffectOp op)
 {
  var f=GetActiveProgramFrame(supplied.Id);var w=f.WindowContext;var owner=_players[f.OwnerSeat];
  if(f.LvQian is not null||f.InstructionIndex!=1)throw new InvalidOperationException("Lv Qian cannot reopen its exact instruction.");
  if(op==SkillProgramEffectOp.RecordWeiluActualDamageSource)
  {
   if(w is not{SourceSeat:{}attacker,DamageFrameId:{}damage,Amount:>0}||attacker==f.OwnerSeat||w.TargetSeat!=f.OwnerSeat||!LvQianSourceCurrent(LvQianSource(f),f.GameplayHash)||!ExactWeiluDamageParent(f,damage,attacker,w.Amount))throw new InvalidOperationException("Weilu requires its exact other-source actual applied damage.");
   var future=owner.WeiluFuture??[];var source=LvQianSource(f);if(!future.Any(g=>g.Source==source&&g.DamageSourceSeat==attacker&&g.DueOwnTurn is null))owner.WeiluFuture=[..future,new(source,f.GameplayHash,attacker,damage,_turnNumber)];
   AdvanceEventRulesAndQueueFact(new WeiluFutureArmedEvent(source,f.GameplayHash,damage,attacker,_turnNumber));return SkillProgramStepOutcome.Continue;
  }
  if(op==SkillProgramEffectOp.DepositZengdaoActualEquipment)
  {
   if(f.TriggerId is not null||!LvQianSourceCurrent(LvQianSource(f),f.GameplayHash)||f.SelectedTargetSeats is not[var recipient]||recipient==f.OwnerSeat||!_players[recipient].IsAlive||f.SelectedCardIds.Count<1||f.SelectedCardIds.Distinct().Count()!=f.SelectedCardIds.Count||f.SelectedCardIds.Any(id=>_cardZones.GetLocation(id)!=CardLocation.Equipment(f.OwnerSeat)||GetAdvancedCard(id).IsGeneralWeapon))throw new InvalidOperationException("Zengdao requires actual selected equipment and one other recipient.");
   var source=LvQianSource(f);var pileInstance=$"{source.OwnerSeat}:{source.SkillInstanceId}";var key=(recipient,source.SkillId,pileInstance);if(_publicPersistentPiles.ContainsKey(key))throw new InvalidOperationException("Zengdao cannot pay its limited deposit twice.");
   var pileId=PublicPileIdentity(source.SkillId,pileInstance);var pile=new PublicPersistentPileSource(recipient,source.SkillId,pileInstance,int.MaxValue,pileId);_cardZones.EnsurePublicPersistentPile(pile.Location);_publicPersistentPiles.Add(key,pile);
   var before=_movementSequence;var grant=new ZengdaoPublicGrant(source,f.GameplayHash,f.Id,recipient,pile.Location,before,before){Entities=f.SelectedCardIds};
   var r=new LvQianReceipt(source,f.GameplayHash,1,0,_turnNumber,LvQianStage.DepositChildren,Before:before,After:before,Grant:grant){Targets=[recipient],Entities=f.SelectedCardIds};ReplaceRuntimeTop(f=f with{LvQian=r,PendingMovementContinuation=new(f.OwnerSeat,0,null)});
   IssueLvQianStarted(f);MoveCards(f.SelectedCardIds.Select(GetAdvancedCard).ToArray(),CardLocation.Equipment(f.OwnerSeat),pile.Location,new(ZengdaoDepositReason));
   f=GetActiveProgramFrame(f.Id);grant=grant with{After=_movementSequence};r=f.LvQian!with{After=_movementSequence,ActualCount=f.SelectedCardIds.Count,Grant=grant};_players[recipient].ZengdaoGrants=[..(_players[recipient].ZengdaoGrants??[]),grant];ReplaceRuntimeTop(f=f with{LvQian=r});IssueLvQianSegment(f);return SkillProgramStepOutcome.AwaitChild;
  }
  if(op==SkillProgramEffectOp.ConsumeZengdaoForActualSourceDamage)
  {
   var candidate=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,f.TriggerId!,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,w?.OccurrenceIndex??0);
   if(w is null||!IsIssuedZengdaoCandidate(candidate,w)||w.ZengdaoGrant is not{}grant||_resolutionStack[^2]is not BeforeDamageProgramWindowFrame damageWindow||damageWindow.Id!=w.ParentFrameId||CurrentDamageAttempt is not{}attack||damageWindow.Prevented)throw new InvalidOperationException("Zengdao requires its granted recipient's exact native damage attempt.");
   var choices=(_players[f.OwnerSeat].ZengdaoGrants??[]).Where(IsDurableZengdaoGrant).OrderBy(g=>g.Source.OwnerSeat).ThenBy(g=>g.Source.SkillInstanceId,StringComparer.Ordinal).SelectMany(g=>g.Entities.Where(id=>_cardZones.GetLocation(id)==g.Location).Select(id=>new ZengdaoEntityChoice(id,g))).ToArray();
   if(choices.Length==0)throw new InvalidOperationException("Zengdao has no genuinely issued public entity to pay.");
   var r=new LvQianReceipt(grant.Source,grant.GameplayHash,1,damageWindow.Id,_turnNumber,LvQianStage.DamageChoice,Grant:grant,DamageAttemptResolutionId:attack.ResolutionId){Targets=[damageWindow.TargetSeat],Choices=choices};ReplaceRuntimeTop(f=f with{LvQian=r});IssueLvQianStarted(f);
   if(choices.Length==1)PayZengdaoDamage(f,choices[0]);else PublishZengdaoChoice(f);return SkillProgramStepOutcome.AwaitChild;
  }
  if(w?.WeiluPhase is not{}phase||phase.OwnerSeat!=f.OwnerSeat)throw new InvalidOperationException("Weilu requires its frozen original native phase key.");
  if(op==SkillProgramEffectOp.PayWeiluNextActualPlayHpLoss)
  {
   var selected=(owner.WeiluFuture??[]).Where(g=>g.FirstPhase==phase&&g.Source.SkillId==f.SkillId&&g.Source.SkillInstanceId==f.SkillInstanceId&&LvQianSourceCurrent(g.Source,g.GameplayHash)).ToArray();
   owner.WeiluFuture=(owner.WeiluFuture??[]).Except(selected).ToArray();var targets=selected.Select(g=>g.DamageSourceSeat).Distinct().ToArray();
   ReplaceRuntimeTop(f=f with{LvQian=new(LvQianSource(f),f.GameplayHash,1,w.ParentFrameId,_turnNumber,LvQianStage.LoseHpChildren,Phase:phase){Targets=targets},PendingMovementContinuation=new(f.OwnerSeat,0,null)});IssueLvQianStarted(f);PayNextWeiluTarget(f);return SkillProgramStepOutcome.AwaitChild;
  }
  if(op!=SkillProgramEffectOp.RestoreWeiluExactPaidPlayHp)throw new InvalidOperationException("Unknown Lv Qian operation.");
  var debts=(owner.WeiluDebts??[]).Where(g=>g.Phase==phase&&g.Source.SkillId==f.SkillId&&g.Source.SkillInstanceId==f.SkillInstanceId).ToArray();
  ReplaceRuntimeTop(f=f with{LvQian=new(LvQianSource(f),f.GameplayHash,1,w.ParentFrameId,_turnNumber,LvQianStage.RecoveryChildren,Phase:phase){Targets=debts.Select(g=>g.TargetSeat).ToArray()},PendingMovementContinuation=new(f.OwnerSeat,0,null)});IssueLvQianStarted(f);PayNextWeiluTarget(f);return SkillProgramStepOutcome.AwaitChild;
 }
 private IReadOnlyList<PromptChoice> ZengdaoDamageChoices(ProgramSkillFrame f)=>Array.AsReadOnly(f.LvQian!.Choices.Select((entry,index)=>new PromptChoice(new($"zengdao.{f.Id}.{entry.CardId}"),$"移去{GetAdvancedCard(entry.CardId).DisplayName}",[entry.CardId],[],new Dictionary<string,string>{{"program-action","zengdao-damage"},{"choice",index.ToString()}})).ToArray());
 private void PublishZengdaoChoice(ProgramSkillFrame f)
 {
  var s=_contentRegistry.GetSkill(f.SkillId);_pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"赠刀：选择移去一张实际赠刀牌。",f.LvQian!.Choices.Select(c=>c.CardId).ToArray(),[],f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=false,Choices=ZengdaoDamageChoices(f),SkillPrompt=new(f.SkillId,s.Name,s.Name,s.Description)};ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});_status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 [ProgramAiChoice(SkillProgramEffectOp.ConsumeZengdaoForActualSourceDamage)]private PromptChoice SelectZengdaoDamageChoice(ProgramSkillFrame f)=>ZengdaoDamageChoices(f).First();
 [ProgramAnswer("zengdao-damage")]private void ResolveZengdaoDamageChoice(PromptChoice choice)
 {
  var f=(ProgramSkillFrame)_resolutionStack.Last();AssertLvQian(f);if(f.LvQian is not{Stage:LvQianStage.DamageChoice}r||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!ZengdaoDamageChoices(f).Any(c=>AssistedChoicesEqual([c],[choice])))throw new InvalidOperationException("Zengdao payment lost its real recipient chooser.");ClearPendingDecision();PayZengdaoDamage(f,r.Choices[int.Parse(choice.Parameters["choice"])]);
 }
 private void PayZengdaoDamage(ProgramSkillFrame f,ZengdaoEntityChoice choice)
 {
  var r=f.LvQian!;var grant=choice.Grant;var cardId=choice.CardId;
  if(r.Stage!=LvQianStage.DamageChoice||!r.Choices.Contains(choice)||!IsDurableZengdaoGrant(grant)||_cardZones.GetLocation(cardId)!=grant.Location||_resolutionStack[^2]is not BeforeDamageProgramWindowFrame damageWindow||damageWindow.Id!=r.WindowId||damageWindow.Prevented||CurrentDamageAttempt is not{IsSourceLess:false,IsDelayedJudgmentDamage:false}attack||attack.ResolutionId!=r.DamageAttemptResolutionId||attack.SourceSeat!=f.OwnerSeat||attack.TargetSeat!=r.Targets.Single())throw new InvalidOperationException("Zengdao cannot substitute a grant, physical card or actual damage after the recipient chooses.");
  var before=_movementSequence;ReplaceRuntimeTop(f=f with{LvQian=r with{Stage=LvQianStage.DamagePaymentChildren,Before=before,After=before,Grant=grant,Entities=[cardId]},PendingMovementContinuation=new(f.OwnerSeat,0,null)});MoveCard(GetAdvancedCard(cardId),grant.Location,CardLocation.DiscardPile,new(ZengdaoConsumeReason));f=GetActiveProgramFrame(f.Id);r=f.LvQian!with{After=_movementSequence,ActualCount=1};ReplaceRuntimeTop(f=f with{LvQian=r});attack.IncreaseFinalizedDamageAmount(1);ReplaceRuntimeFrame(damageWindow.Id,damageWindow with{Amount=checked(damageWindow.Amount+1)});AdvanceEventRulesAndQueueFact(new ZengdaoDamagePaidEvent(f.Id,attack.ResolutionId,grant.Source,f.OwnerSeat,damageWindow.TargetSeat,cardId,before,_movementSequence));IssueLvQianSegment(f);
 }
 private void PayNextWeiluTarget(ProgramSkillFrame f)
 {
  var r=f.LvQian!;if(r.Cursor>=r.Targets.Count){FinishLvQian(f);return;}var target=_players[r.Targets[r.Cursor]];
  if(r.Stage==LvQianStage.LoseHpChildren)
  {
   var before=target.Hp;var requested=target.IsAlive&&target.HasHpValue?Math.Max(0,before-1):0;
   ReplaceRuntimeTop(f=f with{LvQian=r with{HpProof=new(target.Seat,requested,before,HpChangeKind.Loss)}});
   if(requested>0)new ProgramSkillHost(this).LoseHp(f.Id,f.SkillId,target.Seat,requested);
   f=GetActiveProgramFrame(f.Id);var lost=Math.Max(0,before-target.Hp);var debt=new WeiluPaidDebt(r.Source,r.GameplayHash,f.Id,r.Phase!,target.Seat,lost);_players[f.OwnerSeat].WeiluDebts=[..(_players[f.OwnerSeat].WeiluDebts??[]),debt];AdvanceEventRulesAndQueueFact(new WeiluHpPaidEvent(f.Id,r.Source,r.GameplayHash,r.Phase!,target.Seat,lost));
  }
  else
  {
   var debt=(_players[f.OwnerSeat].WeiluDebts??[]).SingleOrDefault(g=>g.Phase==r.Phase&&g.Source.SkillId==f.SkillId&&g.Source.SkillInstanceId==f.SkillInstanceId&&g.TargetSeat==target.Seat);if(debt is null){ReplaceRuntimeTop(f with{LvQian=r with{Cursor=r.Cursor+1}});return;}_players[f.OwnerSeat].WeiluDebts=(_players[f.OwnerSeat].WeiluDebts??[]).Where(g=>g!=debt).ToArray();AdvanceEventRulesAndQueueFact(new WeiluDebtConsumedEvent(f.Id,debt.ProducerFrameId,debt.Phase,target.Seat,debt.ActualHpLost,target.IsAlive));ReplaceRuntimeTop(f=f with{LvQian=r with{HpProof=new(target.Seat,target.IsAlive?Math.Min(debt.ActualHpLost,target.MaxHp-target.Hp):0,target.Hp,HpChangeKind.Recovery)}});if(target.IsAlive&&debt.ActualHpLost>0)new ProgramSkillHost(this).Recover(f.Id,f.OwnerSeat,target.Seat,debt.ActualHpLost,null,null);
  }
  f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f with{LvQian=f.LvQian!with{Cursor=r.Cursor+1}});
 }
 private void IssueLvQianStarted(ProgramSkillFrame f){var r=f.LvQian!;AdvanceEventRulesAndQueueFact(new LvQianStartedEvent(f.Id,r.Source,r.GameplayHash,r.WindowId,r.ActualTurn,r.Stage));}
 private void IssueLvQianSegment(ProgramSkillFrame f){var r=f.LvQian!;AdvanceEventRulesAndQueueFact(new LvQianSegmentPaidEvent(f.Id,r.Source,r.GameplayHash,r.Stage,r.Before,r.After,r.ActualCount,r.Targets.Single()));}
 private bool DrainLvQian(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id);
 [ProgramContinuation(ProgramContinuationKind.Resume,24900)]private bool ResumeLvQian(long id)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{LvQian:{}r}f||f.Id!=id||r.Stage==LvQianStage.DamageChoice)return false;AssertLvQian(f);if(DrainLvQian(f))return true;
  if(r.Stage is LvQianStage.LoseHpChildren or LvQianStage.RecoveryChildren){PayNextWeiluTarget(f);return true;}FinishLvQian(f);return true;
 }
 [ProgramContinuation(ProgramContinuationKind.Movement,24900)]private bool ReturnLvQianMovement(ProgramSkillFrame f)=>f.LvQian is not null&&ResumeLvQian(f.Id);
 private void FinishLvQian(ProgramSkillFrame f){ReplaceRuntimeTop(f=f with{LvQian=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
 [ProgramCapability]
 private sealed class LvQianProgramCapability(GameEngine engine) : ILvQianProgramHost
 {public SkillProgramStepOutcome ExecuteLvQian(ProgramSkillFrame f,SkillProgramEffectOp op)=>engine.ExecuteLvQian(f,op);}
}
