namespace CardGame.Core;
public sealed partial class GameEngine
{
 private IEnumerable<IGameEvent> HandComparisonHistory()=>_events.Select(e=>e.Payload).Concat(_pendingEvents);
 private int? HandComparisonPhaseLimit(CharacterState owner,SkillProgram program,SkillProgramActivation activation,string? instanceId = null)
 {
  var op=activation.Effects.SingleOrDefault(e=>e.Op==SkillProgramEffectOp.CompareSelectedHandWithHpHand);
  if(op is null)return activation.UsesPerPhase;
  var instance=instanceId??GetRuntimeSkillInstanceId(owner,program.Id);
  return HandComparisonHistory().OfType<ProgramHandComparisonResolvedEvent>().Any(e=>e.OwnerSeat==owner.Seat&&e.SkillId==program.Id&&e.SkillInstanceId==instance&&e.StateId==op.StateId&&e.TurnNumber==_turnNumber&&e.RankMatched)?2:1;
 }
 private IReadOnlyList<ProgramPublicRuleStateSnapshot>? GetHandComparisonPublicRuleStates(CharacterState owner,string skillId)
 {
  List<ProgramPublicRuleStateSnapshot>? result=null;
  var instances=GetSkillBindingShard(owner).ProgramInstances;
  for(var index=0;index<instances.Count;index++)
  {
   var instance=instances[index];
   if(instance.SkillId!=skillId)continue;
   var hasPublicState=false;
   for(var i=0;i<instance.Program.Activations.Count;i++)
   {
    var activation=instance.Program.Activations[i];
    var features=ProgramInstructionResolver.Default.Features(activation);
    if(features.HasOperation(SkillProgramEffectOp.CompareSelectedHandWithHpHand))
     (result??=[]).Add(new(instance.SkillInstanceId,activation.Id,ProgramPublicRuleStateKind.ActivationLimit,HandComparisonPhaseLimit(owner,instance.Program,activation,instance.SkillInstanceId)!.Value,_programPhaseUses.GetValueOrDefault((owner.Seat,skillId,activation.UsageGroup)),SkillUsageScope.Phase));
    hasPublicState|=features.HasOperation(SkillProgramEffectOp.CompareSelectedHandWithHpHand)||features.HasOperation(SkillProgramEffectOp.AdjustPersistentHandLimit)||features.HasOperation(SkillProgramEffectOp.ProhibitSelfCardTargetsForTurn);
   }
   for(var i=0;!hasPublicState&&i<instance.Program.Triggers.Count;i++)
   {
    var features=ProgramInstructionResolver.Default.Features(instance.Program.Triggers[i]);
    hasPublicState=features.HasOperation(SkillProgramEffectOp.CompareSelectedHandWithHpHand)||features.HasOperation(SkillProgramEffectOp.AdjustPersistentHandLimit)||features.HasOperation(SkillProgramEffectOp.ProhibitSelfCardTargetsForTurn);
   }
   if(!hasPublicState)continue;
   AddHandComparisonEffectPublicRuleStates(owner,skillId,instance,result??=[]);
  }
  return result is{Count:>0}?result.ToArray():null;
 }
 private void AddHandComparisonEffectPublicRuleStates(CharacterState owner,string skillId,IndexedSkillProgramInstance instance,List<ProgramPublicRuleStateSnapshot> result)
 {
   var effects=instance.Program.Activations.SelectMany(a=>a.Effects).Concat(instance.Program.Triggers.SelectMany(t=>t.Effects)).ToArray();
   foreach(var stateId in effects.Where(e=>e.Op is SkillProgramEffectOp.CompareSelectedHandWithHpHand or SkillProgramEffectOp.AdjustPersistentHandLimit).Select(e=>e.StateId!).Distinct())
    result.Add(new(instance.SkillInstanceId,stateId,ProgramPublicRuleStateKind.PersistentHandLimit,HandComparisonHistory().OfType<ProgramPersistentHandLimitChangedEvent>().Where(e=>e.OwnerSeat==owner.Seat&&e.SkillId==skillId&&e.SkillInstanceId==instance.SkillInstanceId&&e.StateId==stateId).Sum(e=>e.Amount)));
   if(effects.Any(e=>e.Op==SkillProgramEffectOp.ProhibitSelfCardTargetsForTurn))
    result.Add(new(instance.SkillInstanceId,"self-target",ProgramPublicRuleStateKind.SelfTargetProhibition,HandComparisonHistory().OfType<ProgramSelfCardTargetsProhibitedEvent>().Any(e=>e.OwnerSeat==owner.Seat&&e.SkillId==skillId&&e.SkillInstanceId==instance.SkillInstanceId&&e.TurnNumber==_turnNumber&&e.TurnSeat==_currentSeat)?1:0,null,SkillUsageScope.Turn));
 }
 private IReadOnlyList<int> GetSelfProhibitionPolicyTargets(CharacterState actor,LegalAction action)
 {
  if(!HasSelfCardTargetProhibition(actor.Seat))return GetDeclaredCardTargets(actor,action.Kind,action.TargetSeats).ToArray();
  if(action.Kind==LegalActionKind.BorrowedSword)return action.TargetSeats.Where((_,index)=>index%2==0).ToArray();
  return action.ConversionSource is{} source&&ViewAsRule(source)?.ExcludeOwnerEffects==true?action.TargetSeats:GetDeclaredCardTargets(actor,action.Kind,action.TargetSeats).ToArray();
 }
 private bool CanActivateHandComparison(CharacterState owner,SkillProgramActivation activation)=>!activation.Effects.Any(e=>e.Op==SkillProgramEffectOp.CompareSelectedHandWithHpHand)||GetHand(owner).Count>0&&_players.Any(p=>p.IsAlive&&p.Seat!=owner.Seat&&p.Hp>0&&GetHand(p).Count>0);
 private bool IsHandComparisonTarget(SkillProgramActivation activation,CharacterState p)=>!activation.Effects.Any(e=>e.Op==SkillProgramEffectOp.CompareSelectedHandWithHpHand)||p.Hp>0&&GetHand(p).Count>0;
 private IEnumerable<RuleQueryContribution> PersistentHandLimitContributions(CharacterState owner)
 {
  foreach(var g in HandComparisonHistory().OfType<ProgramPersistentHandLimitChangedEvent>().Where(e=>(e.TargetSeat<0?e.OwnerSeat:e.TargetSeat)==owner.Seat&&HasRuntimeSkillInstance(_players[e.OwnerSeat],e.SkillId,e.SkillInstanceId)).GroupBy(e=>(e.SkillId,e.SkillInstanceId,e.StateId)))
   yield return new FiniteRuleQueryContribution("persistent:"+g.Key,SkillRuleOperation.Add,g.Sum(e=>e.Amount),0);
 }
 private bool HasSelfCardTargetProhibition(int seat)=>ProgramEventHistory<ProgramSelfCardTargetsProhibitedEvent>().Any(e=>e.OwnerSeat==seat&&e.TurnNumber==_turnNumber&&e.TurnSeat==_currentSeat&&HasRuntimeSkillInstance(_players[seat],e.SkillId,e.SkillInstanceId));
 private bool IsImplicitSelfCardUse(CardKind kind)=>kind is CardKind.Peach or CardKind.Alcohol or CardKind.DrawTwo or CardKind.Lightning||EquipmentCatalog.IsEquipment(kind);
 private bool IsSelfTargetForbiddenAction(CharacterState actor,CardKind kind,IReadOnlyList<int> targets)
 {
  if(!HasSelfCardTargetProhibition(actor.Seat))return false;
  var designated=kind==CardKind.BorrowedSword?targets.Where((_,index)=>index%2==0):targets;
  return designated.Contains(actor.Seat)||targets.Count==0&&IsImplicitSelfCardUse(kind);
 }
 private IEnumerable<int> HandComparisonPublicIds()=>_resolutionStack.OfType<ProgramSkillFrame>().Where(f=>f.HandComparisonDraft is not null).SelectMany(f=>f.HandComparisonDraft!.RevealedIds.Prepend(f.HandComparisonDraft.SourceCardId)).Distinct();
 private sealed partial class ProgramSkillHost : IHandComparisonProgramHost
 {
  public SkillProgramStepOutcome CompareSelectedHandWithHpHand(ProgramSkillFrame f,SkillProgramEffect e)=>engine.CompareSelectedHandWithHpHand(f,e);
  public void AdjustPersistentHandLimit(ProgramSkillFrame f,int targetSeat,int amount,string stateId)=>engine.AdjustPersistentHandLimit(f,targetSeat,amount,stateId);
  public void ProhibitSelfCardTargetsForTurn(ProgramSkillFrame f){engine.ValidateProgramTurnEffectGrant(f);engine.AdvanceEventRulesAndQueueFact(new ProgramSelfCardTargetsProhibitedEvent(f.OwnerSeat,f.SkillId,f.SkillInstanceId,engine._turnNumber,engine._currentSeat));}
 }
 private void AdjustPersistentHandLimit(ProgramSkillFrame f,int targetSeat,int amount,string stateId)=>AdvanceEventRulesAndQueueFact(new ProgramPersistentHandLimitChangedEvent(f.OwnerSeat,f.SkillId,f.SkillInstanceId,stateId,amount,targetSeat));
 private SkillProgramStepOutcome CompareSelectedHandWithHpHand(ProgramSkillFrame f,SkillProgramEffect effect)
 {
  if(f.TriggerId is not null||f.SelectedCardIds is not [var source]||f.SelectedTargetSeats is not [var target]||target==f.OwnerSeat||_cardZones.GetLocation(source)!=CardLocation.Hand(f.OwnerSeat)||!_players[target].IsAlive||_players[target].Hp<=0||GetHand(_players[target]).Count==0)throw new InvalidOperationException("A hand comparison requires one own hand card and another living positive-HP nonempty hand.");
  var cards=GetHand(_players[target]).Select(c=>c.Id).ToArray();
  ReplaceRuntimeTop(f=f with{HandComparisonDraft=new(target,source,cards,Math.Min(_players[target].Hp,cards.Length),[],"reveal")});
  AdvanceEventRulesAndQueueFact(new ProgramHandComparisonSourceRevealedEvent(f.Id,f.OwnerSeat,source));PublishHandComparison(f);return SkillProgramStepOutcome.AwaitChoice;
 }
 private void PublishHandComparison(ProgramSkillFrame f)
 {
  var d=f.HandComparisonDraft!;var choices=new List<PromptChoice>();
  void Add(string branch,string token,string label,IReadOnlyList<int> cards,IReadOnlyList<int> seats)=>choices.Add(new(new ChoiceId($"hand-comparison.{f.Id}.{branch}.{token}"),label,cards,seats,new Dictionary<string,string>{["program-action"]="hand-comparison",["frame-id"]=f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),["branch"]=branch,["token"]=token}));
  if(d.Stage=="reveal")foreach(var id in d.CandidateIds.Except(d.RevealedIds))Add("reveal",Array.IndexOf(d.CandidateIds.ToArray(),id).ToString(System.Globalization.CultureInfo.InvariantCulture),"展示对方第"+(Array.IndexOf(d.CandidateIds.ToArray(),id)+1)+"张手牌",[],[d.TargetSeat]);
  else if(d.Stage=="complete") Add("complete","complete","展示完成，继续",[],[]);
  else if(d.Stage=="benefit")
  {
   Add("draw","draw","摸一张牌",[],[]);
   foreach(var p in _players.Where(p=>p.IsAlive&&p.Seat!=f.OwnerSeat&&HasDiscardableHeBy(f.OwnerSeat,p.Seat)))Add("target",p.Seat.ToString(),"弃置"+p.Name+"的一张牌",[],[p.Seat]);
  }
  else
  {
   var target=_players[int.Parse(d.Stage.Split(':')[1],System.Globalization.CultureInfo.InvariantCulture)];
   foreach(var card in GetHand(target))Add("discard",("hand-"+GetHand(target).ToList().IndexOf(card)),"弃置第"+(GetHand(target).ToList().IndexOf(card)+1)+"张手牌",[],[target.Seat]);
   foreach(var card in GetEquipment(target).Where(card=>!IsForeignEquipmentDiscardPrevented(f.OwnerSeat,card,CardLocation.Equipment(target.Seat),OwnedCardMoveIntent.Discard)))Add("discard",("equipment-"+card.Id),"弃置【"+card.DisplayName+" "+GetSuitDisplayName(card.Suit)+card.RankText+"】",[card.Id],[target.Seat]);
  }
  var skill=_contentRegistry.GetSkill(f.SkillId);
  _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,d.Stage=="reveal"?"选择展示与其体力值等量的手牌。":"选择同色奖励。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=false,TargetSeat=d.TargetSeat,Choices=choices.ToArray(),SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
  _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanCardSelection:EngineStatus.Running;
 }
 private void ResolveHandComparison(PromptChoice selected)
 {
  var f=(ProgramSkillFrame)_resolutionStack[^1];var d=f.HandComparisonDraft!;var effect=ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
  AssertHandComparison(f,effect);if(selected.Parameters.GetValueOrDefault("frame-id")!=f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))throw new InvalidOperationException("Hand comparison lost its frame.");
  var branch=selected.Parameters["branch"];var token=selected.Parameters["token"];
  if(branch=="reveal")
  {
   var slot=int.Parse(token,System.Globalization.CultureInfo.InvariantCulture);if(slot<0||slot>=d.CandidateIds.Count)throw new InvalidOperationException("Invalid hand slot.");var id=d.CandidateIds[slot];if(d.Stage!="reveal"||!d.CandidateIds.Contains(id)||d.RevealedIds.Contains(id)||_cardZones.GetLocation(id)!=CardLocation.Hand(d.TargetSeat))throw new InvalidOperationException("Invalid concealed hand reveal.");
   ClearPendingDecision();d=d with{RevealedIds=d.RevealedIds.Append(id).ToArray()};ReplaceRuntimeTop(f=f with{HandComparisonDraft=d});
   AdvanceEventRulesAndQueueFact(new ProgramHandComparisonCardRevealedEvent(f.Id,d.TargetSeat,id));
   if(d.RevealedIds.Count<d.Required){PublishHandComparison(f);return;}
   var own=GetHand(_players[f.OwnerSeat]).Single(c=>c.Id==d.SourceCardId);var revealed=GetHand(_players[d.TargetSeat]).Where(c=>d.RevealedIds.Contains(c.Id)).ToArray();
   var color=revealed.Any(c=>IsRedSuit(EffectiveSuit(_players[d.TargetSeat],c))==IsRedSuit(EffectiveSuit(_players[f.OwnerSeat],own)));var rank=revealed.Any(c=>c.Rank==own.Rank);
   AdvanceEventRulesAndQueueFact(new ProgramHandComparisonResolvedEvent(f.OwnerSeat,f.SkillId,f.SkillInstanceId,effect.StateId!,_turnNumber,d.SourceCardId,d.TargetSeat,d.RevealedIds,color,rank));
   if(!color&&!rank)AdjustPersistentHandLimit(f,f.OwnerSeat,-1,effect.StateId!);
   if(color){ReplaceRuntimeTop(f=f with{HandComparisonDraft=d with{Stage="benefit"}});PublishHandComparison(f);return;}
   ReplaceRuntimeTop(f=f with{HandComparisonDraft=d with{Stage="complete"}});PublishHandComparison(f);return;
  }
  if(branch=="complete"){if(d.Stage!="complete")throw new InvalidOperationException("Invalid completion choice.");FinishHandComparison(f);return;}
  if(branch=="target")
  {
   var target=int.Parse(token,System.Globalization.CultureInfo.InvariantCulture);if(d.Stage!="benefit"||target==f.OwnerSeat||!_players[target].IsAlive||!HasDiscardableHeBy(f.OwnerSeat,target))throw new InvalidOperationException("Invalid reward discard target.");ClearPendingDecision();ReplaceRuntimeTop(f=f with{HandComparisonDraft=d with{Stage="discard:"+target}});PublishHandComparison(f);return;
  }
  if(branch=="draw")
  {
   if(d.Stage!="benefit")throw new InvalidOperationException("Invalid reward draw.");ClearPendingDecision();ReplaceRuntimeTop(f with{HandComparisonDraft=null});DrawCards(_players[f.OwnerSeat],1,true,new("skill-program.hand-comparison.reward-draw"));AwaitProgramBoundCardMovements(f.Id,f.OwnerSeat);return;
  }
  if(branch=="discard")
  {
   if(!d.Stage.StartsWith("discard:",StringComparison.Ordinal))throw new InvalidOperationException("Invalid reward discard stage.");var target=int.Parse(d.Stage.Split(':')[1],System.Globalization.CultureInfo.InvariantCulture);var id=token.StartsWith("hand-",StringComparison.Ordinal)?GetHand(_players[target])[int.Parse(token[5..],System.Globalization.CultureInfo.InvariantCulture)].Id:int.Parse(token[10..],System.Globalization.CultureInfo.InvariantCulture);var location=_cardZones.GetLocation(id);if(!_players[target].IsAlive||location.OwnerSeat!=target||location.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment))throw new InvalidOperationException("Discard reward lost its real cost.");
   var card=_cardZones.CardsAt(location).Single(c=>c.Id==id);if(IsForeignEquipmentDiscardPrevented(f.OwnerSeat,card,location,OwnedCardMoveIntent.Discard)){FinishHandComparison(f);return;}ClearPendingDecision();ReplaceRuntimeTop(f with{HandComparisonDraft=null});MoveCard(card,location,CardLocation.DiscardPile,new("skill-program.hand-comparison.reward-discard"));AwaitProgramBoundCardMovements(f.Id,f.OwnerSeat);return;
  }
  throw new InvalidOperationException("Unknown comparison branch.");
 }
 private void FinishHandComparison(ProgramSkillFrame f){ClearPendingDecision();ReplaceRuntimeTop(f with{HandComparisonDraft=null});AdvanceRuntimeProgram(f.Id);}
 private void AssertHandComparison(ProgramSkillFrame f,SkillProgramEffect? op)
 {
  if(f.HandComparisonDraft is not{}d)return;
  if(op?.Op!=SkillProgramEffectOp.CompareSelectedHandWithHpHand||f.TriggerId is not null||f.SelectedCardIds is not[var source]||source!=d.SourceCardId||f.SelectedTargetSeats is not[var target]||target!=d.TargetSeat||target==f.OwnerSeat||d.Required<=0||d.Required>d.CandidateIds.Count||d.CandidateIds.Distinct().Count()!=d.CandidateIds.Count||d.RevealedIds.Distinct().Count()!=d.RevealedIds.Count||d.RevealedIds.Count>d.Required||d.RevealedIds.Any(id=>!d.CandidateIds.Contains(id))||d.CandidateIds.Any(id=>_cardZones.GetLocation(id)!=CardLocation.Hand(target))||_cardZones.GetLocation(source)!=CardLocation.Hand(f.OwnerSeat)||d.Stage!="reveal"&&d.Stage!="complete"&&d.Stage!="benefit"&&!d.Stage.StartsWith("discard:",StringComparison.Ordinal))throw new InvalidOperationException("Invalid serialized hand comparison.");
  if(ReferenceEquals(f,_resolutionStack.LastOrDefault())&&(_pendingDecision is not{Kind:DecisionKind.ProgramTrigger,IsPrivate:false}p||p.PlayerSeat!=f.OwnerSeat))throw new InvalidOperationException("Comparison lost its public chooser.");
 }
}
public sealed record ProgramHandComparisonSourceRevealedEvent(long FrameId,int OwnerSeat,int CardId):IGameEvent;
public sealed record ProgramHandComparisonCardRevealedEvent(long FrameId,int TargetSeat,int CardId):IGameEvent;
