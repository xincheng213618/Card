using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum XianfuEchoKind { Damage, Recovery }
public sealed record XianfuRelationGrant(long FrameId,CardConversionSource Source,string GameplayHash,int TargetSeat,long GameStartingWindowId);
public sealed record XianfuEchoQualification(XianfuRelationGrant Grant,XianfuEchoKind Kind,long NativeChangeId,long NativeParentFrameId,int OriginalTargetSeat,int ActualAmount,int? OriginalSourceSeat,int? HpBefore,int HpAfter);
public sealed record XianfuRelationArmedEvent(XianfuRelationGrant Grant):IGameEvent;
public sealed record XianfuEchoQualifiedEvent(XianfuEchoQualification Receipt):IGameEvent;
public sealed record XianfuEchoIssuedEvent(long FrameId,XianfuEchoQualification Receipt,int InstructionIndex,int NativeAmount):IGameEvent;
public sealed record XianfuEchoCompletedEvent(long FrameId,long NativeChangeId,XianfuEchoKind Kind):IGameEvent;
public sealed record XianfuEchoPending(XianfuEchoQualification Receipt,int InstructionIndex,int HpBefore,bool NativeIssued,int NativeAmount);
public sealed record ChoucePointReceipt(CardConversionSource Source,string GameplayHash,long DamageWindowId,long DamageFrameId,long OriginalAttackId,int PointIndex,int OriginalAmount,int? DamageSourceSeat,int DamageTargetSeat,int StartInstructionIndex,long JudgmentFrameId,string Reason,string ResultBind);
public enum ChouceStage { Judgment, ChooseTarget, ChooseCard, NativeChildren }
public sealed record ChoucePending(ChoucePointReceipt Receipt,ChouceStage Stage,Suit? FrozenSuit=null,int? TargetSeat=null,int? CardId=null,CardLocation? CardLocation=null,long Before=0,long After=0,int Count=0);
public sealed record ChouceJudgmentIssuedEvent(long FrameId,ChoucePointReceipt Receipt):IGameEvent;
public sealed record ChouceJudgmentFrozenEvent(long FrameId,long JudgmentFrameId,Suit EffectiveSuit,int? CardId):IGameEvent;
public sealed record ChouceOutcomeIssuedEvent(long FrameId,long JudgmentFrameId,int TargetSeat,bool Red,long Before,long After,int Count,int? CardId):IGameEvent;
public sealed record ChouceCompletedEvent(long FrameId,long JudgmentFrameId,int PointIndex):IGameEvent;
public sealed partial class CharacterState
{
 private IReadOnlyList<XianfuRelationGrant> _xianfuRelations=Array.AsReadOnly(Array.Empty<XianfuRelationGrant>());
 internal IReadOnlyList<XianfuRelationGrant> XianfuRelations{get=>_xianfuRelations;set=>_xianfuRelations=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillWindowContext
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public XianfuEchoQualification? XianfuEcho{get;init;}}
public sealed partial record ProgramSkillFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public XianfuEchoPending? XianfuPending{get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ChoucePending? Chouce{get;init;}
}
public sealed partial class GameEngine
{
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 590)]
    private bool AllowsRegisteredIsXianfuEchoTrigger(ProgramTriggerLegalityContext gate)
    {
        var candidate = gate.Candidate;
        var trigger = gate.Trigger;
        var context = gate.Context;
        return !(IsXianfuEchoTrigger(trigger) && !CanRunXianfuEcho(candidate, context));
    }

 private CardConversionSource XiZhiCaiSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
 private bool HasXianfuRelation {get{foreach(var p in _players)if(p.XianfuRelations.Count>0)return true;return false;}}
 private IEnumerable<XianfuRelationGrant> LiveXianfuRelations()=>_players.Where(p=>p.IsAlive&&p.XianfuRelations.Count>0).SelectMany(p=>p.XianfuRelations).Where(g=>_players[g.TargetSeat].IsAlive&&HasRuntimeSkillInstance(_players[g.Source.OwnerSeat],g.Source.SkillId,g.Source.SkillInstanceId));
 private SkillProgramStepOutcome ArmXianfuRelation(ProgramSkillFrame supplied,int target)
 {
  var f=GetActiveProgramFrame(supplied.Id);
  if(f.WindowContext is not {Window:SkillProgramTriggerWindow.GameStarting} w||f.SelectedTargetSeats is not [var selected]||selected!=target||target==f.OwnerSeat||!_players[target].IsAlive||!_players[f.OwnerSeat].IsAlive||_players[f.OwnerSeat].XianfuRelations.Any(g=>g.Source.SkillInstanceId==f.SkillInstanceId))throw new InvalidOperationException("Xianfu requires its original actual game-start other-character selection.");
  var g=new XianfuRelationGrant(f.Id,XiZhiCaiSource(f),f.GameplayHash,target,w.ParentFrameId);_players[f.OwnerSeat].XianfuRelations=_players[f.OwnerSeat].XianfuRelations.Append(g).ToArray();AdvanceEventRulesAndQueueFact(new XianfuRelationArmedEvent(g));return SkillProgramStepOutcome.Continue;
 }
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 130)]
 private void ObserveXianfuDamageQualification(IGameEvent payload)
 {
  if(payload is not DamageAppliedEvent{Amount:>0} e||!HasXianfuRelation||CurrentDamageAttempt is not {} attack||!attack.DamageWasApplied||attack.TargetSeat!=e.TargetSeat||attack.DamageAmount!=e.Amount)return;
  var native=_resolutionStack.OfType<DamageFrame>().LastOrDefault(d=>d.ParentFrameId==attack.ResolutionId&&d.TargetSeat==e.TargetSeat&&d.Amount==e.Amount)??throw new InvalidOperationException("Xianfu damage lacks its native damage owner.");
  foreach(var g in LiveXianfuRelations().Where(g=>g.TargetSeat==e.TargetSeat).ToArray())
  {
   var receipt=new XianfuEchoQualification(g,XianfuEchoKind.Damage,native.Id,native.ParentFrameId,e.TargetSeat,e.Amount,attack.IsSourceLess||attack.IsDelayedJudgmentDamage?null:e.SourceSeat,null,e.RemainingHp);
   if(!CompleteProgramEventHistory().OfType<XianfuEchoQualifiedEvent>().Any(x=>x.Receipt.Grant==g&&x.Receipt.NativeChangeId==native.Id))AdvanceEventRulesAndQueueFact(new XianfuEchoQualifiedEvent(receipt));
  }
 }
 private void CaptureXianfuRecoveryQualification(HpChangeContext change)
 {
  if(!HasXianfuRelation||change.Kind!=HpChangeKind.Recovery||change.HpAfter<=change.HpBefore)return;
  foreach(var g in LiveXianfuRelations().Where(g=>g.TargetSeat==change.TargetSeat).ToArray())
   AdvanceEventRulesAndQueueFact(new XianfuEchoQualifiedEvent(new(g,XianfuEchoKind.Recovery,change.Id,change.ParentFrameId??0,change.TargetSeat,change.HpAfter-change.HpBefore,change.SourceSeat,change.HpBefore,change.HpAfter)));
 }
 private bool IsXianfuEchoTrigger(SkillProgramTrigger trigger)=>trigger.Effects is [{Op:SkillProgramEffectOp.SettleXianfuNativeEcho}];
 private XianfuEchoQualification? XianfuQualification(ProgramTriggerCandidate c,long nativeId)=>CompleteProgramEventHistory().OfType<XianfuEchoQualifiedEvent>().Select(e=>e.Receipt).SingleOrDefault(r=>r.NativeChangeId==nativeId&&r.Grant.Source.OwnerSeat==c.OwnerSeat&&r.Grant.Source.SkillId==c.SkillId&&r.Grant.Source.SkillInstanceId==c.SkillInstanceId);
 private bool XianfuQualifiedSourceEnabled(XianfuEchoQualification r)=>_players[r.Grant.Source.OwnerSeat].IsAlive&&_players[r.Grant.Source.OwnerSeat].XianfuRelations.Contains(r.Grant)&&HasRuntimeSkillInstance(_players[r.Grant.Source.OwnerSeat],r.Grant.Source.SkillId,r.Grant.Source.SkillInstanceId);
 private bool XianfuEchoConsumed(XianfuEchoQualification r)=>CompleteProgramEventHistory().OfType<XianfuEchoIssuedEvent>().Any(e=>e.Receipt.Grant==r.Grant&&e.Receipt.NativeChangeId==r.NativeChangeId);
 private IReadOnlyList<ProgramTriggerCandidate> CollectXianfuHpCandidates(HpChangeContext change)
 {
  if(!HasXianfuRelation||change.Kind!=HpChangeKind.Recovery||!_players.Any(p=>p.IsAlive&&p.XianfuRelations.Any(g=>g.TargetSeat==change.TargetSeat)))return [];
  return _players.Where(p=>p.IsAlive&&p.XianfuRelations.Count>0).SelectMany(p=>p.XianfuRelations).Where(g=>g.TargetSeat==change.TargetSeat).SelectMany(g=>CollectProgramTriggerCandidates(_players[g.Source.OwnerSeat],SkillProgramTriggerWindow.AfterHpRecovered).Where(c=>c.SkillId==g.Source.SkillId&&c.SkillInstanceId==g.Source.SkillInstanceId&&IsXianfuEchoTrigger(GetProgramTrigger(c))&&XianfuQualification(c,change.Id) is {} q&&XianfuQualifiedSourceEnabled(q)&&!XianfuEchoConsumed(q))).Distinct().ToArray();
 }
 private ProgramSkillWindowContext CreateXianfuHpContext(HpChangeContext change,ProgramTriggerCandidate c)
 {var r=XianfuQualification(c,change.Id)??throw new InvalidOperationException("Xianfu lacks its actual recovered-HP qualification.");return new(SkillProgramTriggerWindow.AfterHpRecovered,change.Id,c.OwnerSeat,SourceSeat:change.SourceSeat,TargetSeat:change.TargetSeat,Amount:r.ActualAmount,HpChange:change){XianfuEcho=r};}
 private ProgramSkillWindowContext CreateXianfuDamageContext(DamageTriggerWindowFrame window,ProgramTriggerCandidate c)
 {var r=XianfuQualification(c,window.ParentFrameId)??throw new InvalidOperationException("Xianfu lacks its actual applied-damage qualification.");return new(SkillProgramTriggerWindow.AfterDamageApplied,window.Id,c.OwnerSeat,SourceSeat:r.OriginalSourceSeat,TargetSeat:r.OriginalTargetSeat,DamageFrameId:window.ParentFrameId,Amount:r.ActualAmount){XianfuEcho=r};}
 private bool CanOfferXianfuDamage(int ownerSeat,SkillProgramTrigger trigger,IDamageAttempt attack)
 {if(!IsXianfuEchoTrigger(trigger))return true;return _players[ownerSeat].XianfuRelations.Any(g=>g.Source.OwnerSeat==ownerSeat&&g.TargetSeat==attack.TargetSeat&&CompleteProgramEventHistory().OfType<XianfuEchoQualifiedEvent>().Any(e=>e.Receipt.Grant==g&&e.Receipt.Kind==XianfuEchoKind.Damage&&e.Receipt.NativeParentFrameId==attack.ResolutionId&&XianfuQualifiedSourceEnabled(e.Receipt)&&!XianfuEchoConsumed(e.Receipt)));}
 private bool CanRunXianfuEcho(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
 {
  if(context.XianfuEcho is not {} r||r.Grant.Source.OwnerSeat!=c.OwnerSeat||r.Grant.Source.SkillId!=c.SkillId||r.Grant.Source.SkillInstanceId!=c.SkillInstanceId||r.ActualAmount<=0||XianfuEchoConsumed(r)||!XianfuQualifiedSourceEnabled(r))return false;
  return r==XianfuQualification(c,r.NativeChangeId)&&(r.Kind==XianfuEchoKind.Damage?context.DamageFrameId==r.NativeChangeId&&context.Window==SkillProgramTriggerWindow.AfterDamageApplied:context.HpChange is {} hp&&context.Window==SkillProgramTriggerWindow.AfterHpRecovered&&hp.Id==r.NativeChangeId&&hp.Kind==HpChangeKind.Recovery&&hp.HpAfter-hp.HpBefore==r.ActualAmount);
 }
 private SkillProgramStepOutcome SettleXianfuNativeEcho(ProgramSkillFrame supplied)
 {
  var f=GetActiveProgramFrame(supplied.Id);var r=f.WindowContext?.XianfuEcho??throw new InvalidOperationException("Xianfu lost its issued native cause.");
  if(XianfuEchoConsumed(r))throw new InvalidOperationException("Xianfu cannot pay the same actual native mutation twice.");
  var nativeAmount=r.Kind==XianfuEchoKind.Damage?r.ActualAmount:Math.Max(0,Math.Min(r.ActualAmount,_players[f.OwnerSeat].MaxHp-_players[f.OwnerSeat].Hp));
  ReplaceRuntimeTop(f=f with{XianfuPending=new(r,f.InstructionIndex,_players[f.OwnerSeat].Hp,true,nativeAmount),PendingMovementContinuation=r.Kind==XianfuEchoKind.Recovery?new(f.OwnerSeat,0,null):null});AdvanceEventRulesAndQueueFact(new XianfuEchoIssuedEvent(f.Id,r,f.InstructionIndex,nativeAmount));
  if(!_players[f.OwnerSeat].IsAlive){FinishXianfuEcho(f);return SkillProgramStepOutcome.AwaitChild;}
  if(r.Kind==XianfuEchoKind.Damage)return BeginProgramSkillDamage(f,f.OwnerSeat,r.ActualAmount,sourceLess:true);
  new ProgramSkillHost(this).Recover(f.Id,f.OwnerSeat,f.OwnerSeat,nativeAmount,null,null);AdvanceRuntimeProgram(f.Id);return SkillProgramStepOutcome.AwaitChild;
 }
 [ProgramContinuation(ProgramContinuationKind.Resume,23101)]private bool ResumeXianfuEcho(long id)
 {
  if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{XianfuPending:{}} f||f.Id!=id)return false;RequireIssuedXiZhiCai(f);
  if(f.AttackAttempt is not null)return false;
  if(TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id))return true;
  FinishXianfuEcho(f);return true;
 }
 [ProgramContinuation(ProgramContinuationKind.Movement,23101)]private bool ReturnXianfuEchoMovement(ProgramSkillFrame f)=>f.XianfuPending is not null&&ResumeXianfuEcho(f.Id);
 private void FinishXianfuEcho(ProgramSkillFrame f)
 {var r=f.XianfuPending!.Receipt;AdvanceEventRulesAndQueueFact(new XianfuEchoCompletedEvent(f.Id,r.NativeChangeId,r.Kind));ReplaceRuntimeTop(f=f with{XianfuPending=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}

 private SkillProgramStepOutcome StartChouceDamagePointJudgment(ProgramSkillFrame supplied,SkillProgramEffect effect)
 {
  var f=GetActiveProgramFrame(supplied.Id);var index=_resolutionStack.FindIndex(p=>p.Id==f.Id);
  if(f.Chouce is not null||index<1||_resolutionStack[index-1] is not DamageTriggerWindowFrame window||window.TriggerWindow!=SkillProgramTriggerWindow.AfterDamageApplied||f.WindowContext is not {Window:SkillProgramTriggerWindow.AfterDamageApplied} c||c.ParentFrameId!=window.Id||window.CandidateIndex<0||window.CandidateIndex>=window.Candidates.Count||!MountObserverCandidateMatches(f,window.Candidates[window.CandidateIndex].ToProgramCandidate()))throw new InvalidOperationException("Chouce needs its exact actual after-damage point candidate.");
  var attack=GetDamageTriggerAttack(window);if(!attack.DamageWasApplied||attack.TargetSeat!=f.OwnerSeat||c.OccurrenceIndex<0||c.OccurrenceIndex>=attack.DamageAmount)throw new InvalidOperationException("Chouce lost its actual positive damage point.");
  var receipt=new ChoucePointReceipt(XiZhiCaiSource(f),f.GameplayHash,window.Id,window.ParentFrameId,attack.ResolutionId,c.OccurrenceIndex,attack.DamageAmount,attack.IsSourceLess||attack.IsDelayedJudgmentDamage?null:attack.SourceSeat,attack.TargetSeat,f.InstructionIndex,_resolutionSequence+1,effect.JudgmentReason!,effect.ResultBind!);
  ReplaceRuntimeTop(f=f with{Chouce=new(receipt,ChouceStage.Judgment)});AdvanceEventRulesAndQueueFact(new ChouceJudgmentIssuedEvent(f.Id,receipt));return StartProgramJudgment(f,f.OwnerSeat,receipt.Reason,receipt.ResultBind,SkillProgramCardSetVisibility.Public,f.OwnerSeat);
 }
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 150)]
 private void CaptureChouceFrozenJudgment(IGameEvent payload)
 {
  if(payload is not JudgmentResolvedEvent{Suit:{} suit} e)return;
  ProgramSkillFrame? root=null;
  for(var index=_resolutionStack.Count-1;index>=0;index--)
   if(_resolutionStack[index] is ProgramSkillFrame{Chouce:{Stage:ChouceStage.Judgment} pending} candidate&&pending.Receipt.JudgmentFrameId==e.ResolutionId&&candidate.Id==e.ParentResolutionId){root=candidate;break;}
  if(root is null)return;var receipt=root.Chouce!.Receipt;
  if(e.TargetSeat!=root.OwnerSeat||e.Reason!=receipt.Reason||root.Chouce.FrozenSuit is not null)throw new InvalidOperationException("Chouce judgment lost its owning point or was frozen twice.");
  ReplaceRuntimeFrame(root.Id,root with{Chouce=root.Chouce with{FrozenSuit=suit}});AdvanceEventRulesAndQueueFact(new ChouceJudgmentFrozenEvent(root.Id,e.ResolutionId,suit,e.CardId));
 }
 private SkillProgramStepOutcome ResolveChouceFrozenJudgment(ProgramSkillFrame supplied,SkillProgramEffect effect)
 {
  var f=GetActiveProgramFrame(supplied.Id);RequireIssuedXiZhiCai(f);
  if(f.Chouce is not {Stage:ChouceStage.Judgment,FrozenSuit:{} suit} p||p.Receipt.ResultBind!=effect.SourceBind)throw new InvalidOperationException("Chouce requires its frozen real final judgment.");
  if(!_players[f.OwnerSeat].IsAlive){FinishChouce(f);return SkillProgramStepOutcome.AwaitChild;}
  ReplaceRuntimeTop(f=f with{Chouce=p with{Stage=ChouceStage.ChooseTarget}});
  if(ChouceChoices(f).Count==0){FinishChouce(f);return SkillProgramStepOutcome.AwaitChild;}PublishChouceDecision(f);return SkillProgramStepOutcome.AwaitChild;
 }
 private bool ChouceIsRed(ProgramSkillFrame f)=>f.Chouce!.FrozenSuit is Suit.Heart or Suit.Diamond;
 private IReadOnlyList<PromptChoice> ChouceCardOptions(ProgramSkillFrame f,int target)=>BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,target,[CardZoneKind.Hand,CardZoneKind.Equipment,CardZoneKind.Judgment],OwnedCardMoveIntent.Discard,canSelect:(zone,card)=>target!=f.OwnerSeat||!IsSelfHandCategoryDiscardForbidden(target,card,new(zone,target),OwnedCardMoveIntent.Discard));
 private IReadOnlyList<PromptChoice> ChouceChoices(ProgramSkillFrame f)
 {
  if(f.Chouce!.Stage==ChouceStage.ChooseCard)
   return Array.AsReadOnly(ChouceCardOptions(f,f.Chouce.TargetSeat!.Value).Select(c=>c with{Parameters=new Dictionary<string,string>(c.Parameters){["program-action"]="chouce",["chouce-action"]="discard"}}).ToArray());
  return Array.AsReadOnly(_players.Where(p=>p.IsAlive&&(ChouceIsRed(f)||ChouceCardOptions(f,p.Seat).Count>0)).Select(p=>new PromptChoice(new($"chouce.{f.Id}.target-{p.Seat}"),p.Name,[],[p.Seat],new Dictionary<string,string>{{"program-action","chouce"},{"chouce-action","target"}})).ToArray());
 }
 private void PublishChouceDecision(ProgramSkillFrame f)
 {
  var choices=ChouceChoices(f);var skill=_contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});
  _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,ChouceIsRed(f)?"请选择筹策摸牌角色。":"请选择筹策弃牌。",choices.SelectMany(c=>c.Cards).ToArray(),choices.SelectMany(c=>c.Targets).ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=true,TargetSeat=f.Chouce!.TargetSeat,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
  _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 [ProgramAiChoice(SkillProgramEffectOp.ResolveChouceFrozenJudgment)]
 private PromptChoice ChouceAiChoice(ProgramSkillFrame f)=>ChouceChoices(f).First();
 [ProgramAnswer("chouce")]
 private void ResolveChouceChoice(PromptChoice choice)
 {
  var f=(ProgramSkillFrame)_resolutionStack.Last();RequireIssuedXiZhiCai(f);var p=f.Chouce!;
  if(_pendingDecision?.PlayerSeat!=f.OwnerSeat||!AssistedChoicesEqual([choice],ChouceChoices(f).Where(c=>c.Id==choice.Id).ToArray()))throw new InvalidOperationException("Chouce lost its actual chooser authority.");ClearPendingDecision();
  if(!_players[f.OwnerSeat].IsAlive){FinishChouce(f);return;}
  if(p.Stage==ChouceStage.ChooseTarget)
  {
   var target=choice.Targets.Single();ReplaceRuntimeTop(f=f with{Chouce=p=p with{TargetSeat=target}});
   if(!ChouceIsRed(f)){ReplaceRuntimeTop(f=f with{Chouce=p with{Stage=ChouceStage.ChooseCard}});PublishChouceDecision(f);return;}
   var count=_players[f.OwnerSeat].XianfuRelations.Any(g=>g.TargetSeat==target)?2:1;var before=_movementSequence;
   ReplaceRuntimeTop(f=f with{Chouce=p with{Stage=ChouceStage.NativeChildren,Before=before,After=before,Count=0},PendingMovementContinuation=new(target,0,null),Step=ResolutionFrameStep.ResolvingEffect});
   var actual=_players[target].IsAlive?DrawCards(_players[target],count,true,new("program.chouce.red")).Count:0;
   var fresh=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=fresh with{Chouce=fresh.Chouce!with{After=_movementSequence,Count=actual}});AdvanceEventRulesAndQueueFact(new ChouceOutcomeIssuedEvent(f.Id,p.Receipt.JudgmentFrameId,target,true,before,_movementSequence,actual,null));AdvanceRuntimeProgram(f.Id);return;
  }
  var zone=Enum.Parse<CardZoneKind>(choice.Parameters["source-zone"]);var owner=p.TargetSeat!.Value;var location=new CardLocation(zone,owner);var slot=int.Parse(choice.Parameters["slot-index"],System.Globalization.CultureInfo.InvariantCulture);var card=_cardZones.CardsAt(location)[slot];
  if(IsForeignEquipmentDiscardPrevented(f.OwnerSeat,card,location,OwnedCardMoveIntent.Discard)||owner==f.OwnerSeat&&IsSelfHandCategoryDiscardForbidden(owner,card,location,OwnedCardMoveIntent.Discard))throw new InvalidOperationException("Chouce actual discard ceased to be legal.");
  var first=_movementSequence;ReplaceRuntimeTop(f=f with{Chouce=p with{Stage=ChouceStage.NativeChildren,CardId=card.Id,CardLocation=location,Before=first,After=first,Count=1},PendingMovementContinuation=new(owner,0,null),Step=ResolutionFrameStep.ResolvingEffect});
  MoveProgramCardsFromMultipleSources([card.Id],CardLocation.DiscardPile,new("program.chouce.black"));var freshBlack=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=freshBlack with{Chouce=freshBlack.Chouce!with{After=_movementSequence}});AdvanceEventRulesAndQueueFact(new ChouceOutcomeIssuedEvent(f.Id,p.Receipt.JudgmentFrameId,owner,false,first,_movementSequence,1,card.Id));AdvanceRuntimeProgram(f.Id);
 }
 [ProgramContinuation(ProgramContinuationKind.Resume,23103)]private bool ResumeChouce(long id)
 {
  if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{Chouce:{} pending} f||f.Id!=id)return false;
  if(pending.Stage==ChouceStage.Judgment&&!_players[f.OwnerSeat].IsAlive&&pending.FrozenSuit is not null&&f.InstructionIndex==pending.Receipt.StartInstructionIndex&&
   f.CardSetBindings.Any(binding=>binding.Name==pending.Receipt.ResultBind)&&!_resolutionStack.Any(child=>child.Id==pending.Receipt.JudgmentFrameId))
  {RequireIssuedXiZhiCai(f);FinishChouce(f);return true;}
  if(pending.Stage!=ChouceStage.NativeChildren)return false;RequireIssuedXiZhiCai(f);
  if(TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id))return true;
  FinishChouce(f);return true;
 }
 [ProgramContinuation(ProgramContinuationKind.Movement,23103)]private bool ReturnChouceMovement(ProgramSkillFrame f)=>f.Chouce is not null&&ResumeChouce(f.Id);
 private void FinishChouce(ProgramSkillFrame f)
 {var r=f.Chouce!.Receipt;AdvanceEventRulesAndQueueFact(new ChouceCompletedEvent(f.Id,r.JudgmentFrameId,r.PointIndex));ReplaceRuntimeTop(f=f with{Chouce=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
 [ProgramCapability]
 private sealed class XiZhiCaiProgramCapability(GameEngine engine) : IXiZhiCaiProgramHost
 {
  public SkillProgramStepOutcome ArmXianfuRelation(ProgramSkillFrame f,int t)=>engine.ArmXianfuRelation(f,t);
  public SkillProgramStepOutcome SettleXianfuNativeEcho(ProgramSkillFrame f)=>engine.SettleXianfuNativeEcho(f);
  public SkillProgramStepOutcome StartChouceDamagePointJudgment(ProgramSkillFrame f,SkillProgramEffect e)=>engine.StartChouceDamagePointJudgment(f,e);
  public SkillProgramStepOutcome ResolveChouceFrozenJudgment(ProgramSkillFrame f,SkillProgramEffect e)=>engine.ResolveChouceFrozenJudgment(f,e);
 }
}
