namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool HasZhangQiYingCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.InitializeFaluSuitTokens)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GainFaluTokensAfterActualDiscard)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ReorderTopByCurrentFaluTokens)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ChooseFinalJudgmentFiveByZiwei)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.IncreaseActualSourceDamageByYuqing)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GainThreeDeckCategoriesAfterElementalDamage);
 private bool IsZhenyiHoutu(CardConversionSource source)=>HasZhangQiYingCapability&&_contentRegistry.ProgramDependencies.GetTriggerOperationSkillIds(SkillProgramEffectOp.ChooseFinalJudgmentFiveByZiwei).Contains(source.SkillId,StringComparer.Ordinal)&&ViewAsRule(source)is{OutputKind:CardKind.Peach,InputCount:1};
 private bool CanPayZhenyiHoutu(CharacterState owner,CardConversionSource source)=>!IsZhenyiHoutu(source)||source.OwnerSeat==owner.Seat&&owner.IsAlive&&owner.Seat!=_currentSeat&&_turnNumber>0&&owner.Markers.GetValueOrDefault(PlayerMarkerKind.Houtu)==1&&HasRuntimeSkillInstance(owner,source.SkillId,source.SkillInstanceId)&&EnabledSkillPrograms(owner).Any(p=>p.Id==source.SkillId);
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 20)]
 private void ObserveAcceptedZhenyiHoutu(CardActionAcceptedEvent accepted)
 {
  if (accepted.Action.ConversionChain.Count != 0) PayZhenyiHoutu(accepted.Action);
 }

 private void PayZhenyiHoutu(CardActionContext action)
 {
  if(!HasZhangQiYingCapability||action.ConversionChain.Count==0)return;var sources=action.ConversionChain.Where(IsZhenyiHoutu).ToArray();if(sources.Length==0)return;
  var prior=CompleteProgramEventHistory().OfType<ZhenyiHoutuPaidEvent>().Where(e=>e.ActionId==action.ActionId).ToArray();
  if(prior is[var paid]&&sources is[var repeated]&&paid.Source==repeated&&paid.Turn==_turnNumber&&paid.Actor==action.ActorSeat&&paid.Provider==action.ProviderSeat&&action.PhysicalCards.SequenceEqual([paid.Material])&&action.EffectiveKind==CardKind.Peach&&paid.GameplayHash==_contentRegistry.GetSkill(repeated.SkillId).Program?.GameplayHash)return;
  if(prior.Length!=0||sources is not[var source]||source.OwnerSeat!=action.ProviderSeat||action.ActorSeat!=action.ProviderSeat||action.Type!=CardActionType.Use||action.EffectiveKind!=CardKind.Peach||action.PhysicalCards is not[var material]||material.From.OwnerSeat!=action.ProviderSeat||material.From.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain)||!CanPayZhenyiHoutu(_players[action.ProviderSeat],source)||ActiveDying is not{}dying||!action.TargetSeats.SequenceEqual([dying.VictimSeat]))throw new InvalidOperationException("Houtu requires one exact out-of-turn native rescue Peach and its real HE/approved grain material.");
  var owner=_players[action.ProviderSeat];owner.Markers.Remove(PlayerMarkerKind.Houtu);AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(action.ActionId,owner.Seat,PlayerMarkerKind.Houtu,-1,0,owner.Seat,"zhenyi.houtu"));AdvanceEventRulesAndQueueFact(new ZhenyiHoutuPaidEvent(action.ActionId,source,_contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash,_turnNumber,action.ActorSeat,action.ProviderSeat,material));
 }
 private Suit EffectiveZhenyiJudgmentSuit(JudgmentFrame frame,Suit fallback){AssertZhenyiJudgment(frame);return frame.ZhenyiOverride?.Suit??fallback;}
 private int EffectiveZhenyiJudgmentRank(JudgmentFrame frame,int fallback){AssertZhenyiJudgment(frame);return frame.ZhenyiOverride?.Rank??fallback;}
 private void AssertZhenyiJudgment(JudgmentFrame frame)
 {
  if(frame.ZhenyiOverride is not{}r)return;
  if(r.JudgmentId!=frame.Id||r.CardId!=frame.CardId||r.Rank!=5||r.Suit is not(Suit.Spade or Suit.Heart)||r.GameplayHash!=_contentRegistry.GetSkill(r.Source.SkillId).Program?.GameplayHash||CompleteProgramEventHistory().OfType<ZhenyiJudgmentOverriddenEvent>().Count(e=>e.Receipt==r)!=1)throw new InvalidOperationException("Zhenyi effective judgment lost its exact unchanged physical card and paid result.");
 }
 private void AssertZhangQiYing(ProgramSkillFrame f)
 {
  if(f.ZhangQiYing is not{}r||r.Source!=ZhangQiYingSource(f)||r.GameplayHash!=f.GameplayHash||r.Instruction!=f.InstructionIndex||r.ParentId!=f.WindowContext?.ParentFrameId||r.Before>r.After||r.After>_movementSequence)throw new InvalidOperationException("Zhang Qi Ying lost its exact owning source and instruction.");
  if(r.Stage=="gain-children"&&(CompleteProgramEventHistory().OfType<ZhenyiDeckGainPaidEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.Before==r.Before&&e.After==r.After&&e.ActualCount==r.Cards.Count)!=1||r.Cards.Count>3||r.Cards.Distinct().Count()!=r.Cards.Count||f.PendingMovementContinuation is not{BeforeCount:0,CoverageResultBind:null,SubjectSeat:var seat}||seat!=f.OwnerSeat||_cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==ZhenyiGainReason&&r.Cards.Contains(m.CardId))!=r.Cards.Count))throw new InvalidOperationException("Gouchen needs one exact already-paid three-category gain invoice.");
 }

 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 300)]
 private void AssertRegisteredZhangQiYing(ProgramSkillFrame frame)
 {
     if (frame.ZhangQiYing is not null) AssertZhangQiYing(frame);
 }
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 260)]
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 260)]
 private bool CanContinueZhangQiYing(ProgramSkillFrame f){if(f.ZhangQiYing is not{Stage:"gain-children"})return false;AssertZhangQiYing(f);return true;}
 [ProgramContinuation(ProgramContinuationKind.Resume,24405)] private bool ResumeZhangQiYing(long id)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{ZhangQiYing:{Stage:"gain-children"}}f||f.Id!=id)return false;AssertZhangQiYing(f);
  if(TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id))return true;
  ReplaceRuntimeTop(f=f with{ZhangQiYing=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);return true;
 }
 [ProgramContinuation(ProgramContinuationKind.Movement,24405)] private bool ReturnZhangQiYingMovement(ProgramSkillFrame f)=>CanContinueZhangQiYing(f)&&ResumeZhangQiYing(f.Id);
 private bool IsZhangQiYingMovement(ProgramSkillFrame f,SkillProgramEffect?e,ProgramMovementContinuation p)=>e?.Op==SkillProgramEffectOp.GainThreeDeckCategoriesAfterElementalDamage&&CanContinueZhangQiYing(f)&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null;
 private bool ZhangQiYingFirstChild(ProgramSkillFrame f,ResolutionFrame child)=>f.ZhangQiYing is{Stage:"gain-children"}r&&CanContinueZhangQiYing(f)&&child is CardsMovedTriggerWindowFrame moved&&moved.Batch.ParentFrameId==f.Id&&moved.Batch.AwaitingProgramFrameId==f.Id&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After&&r.Cards.Contains(m.CardId)&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==ZhenyiGainReason);
 private ProgramSkillFrame? ZhangQiYingObserverRoot()=>FindProgramObserverRoot(static(e,r,c)=>e.ZhangQiYingFirstChild(r,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
 private bool HasZhangQiYingDamageObserver(long id)=>ZhangQiYingObserverRoot()is not null&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame);
 private bool HasZhangQiYingBeforeDamageObserver(long id)=>ZhangQiYingObserverRoot()is not null&&_resolutionStack.Any(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame);
 private bool IsZhangQiYingDying()=>ActiveDying is not null&&ZhangQiYingObserverRoot()is not null;
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 330)]
 private bool TryAdvanceZhangQiYingSubtree()=>_pendingDecision is null&&ZhangQiYingObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
 private bool AllowsZhangQiYingNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference?source,DamageNature?nature,bool sourceLess)
 {
  if(sourceLess||f.AttackAttempt is not null||f.InstructionIndex<1||_resolutionStack.LastOrDefault()?.Id!=f.Id||ZhangQiYingObserverRoot()is null||f.WindowContext?.Window is not(SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged))return false;
  var e=GetPausedPrivateOfferEffect(f);return e is{Op:SkillProgramEffectOp.Damage}&&amount==e.Amount&&source==e.ActorReference&&nature==e.DamageNature&&target==(e.TargetReference is{}r?ResolveProgramParticipant(f,r):ResolveProgramEffectTarget(f,e.Target));
 }
}
