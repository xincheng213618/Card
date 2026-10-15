using System.Globalization;
using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record RoundOnceCardlessProgramRequestReceipt(long ProgramFrameId,CardConversionSource RequestSource,string RequestGameplayHash,int InstructionIndex,SkillProgramEffectOp RequestOp,int ActorSeat,int TargetSeat,string? ResultBind,PromptId OriginalPromptId,long OriginalRevision,long? OriginalWindowId,RoundOnceCardlessBasicPaymentReceipt Payment,long NativeUseId=0,long NativeActionId=0);
public sealed record RoundOnceCardlessProgramRequestPending(RoundOnceCardlessProgramRequestReceipt Receipt,bool NativeReturned=false);
public sealed record RoundOnceCardlessProgramRequestAcceptedEvent(RoundOnceCardlessProgramRequestReceipt Receipt):IGameEvent;
public sealed record RoundOnceCardlessProgramRequestBoundEvent(RoundOnceCardlessProgramRequestReceipt Receipt):IGameEvent;
public sealed record RoundOnceCardlessProgramRequestReturnedEvent(RoundOnceCardlessProgramRequestReceipt Receipt):IGameEvent;
public sealed partial record ProgramSkillFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public RoundOnceCardlessProgramRequestPending? RoundOnceCardlessProgramRequest{get;init;}
}
public sealed partial record CardUseFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public RoundOnceCardlessProgramRequestReceipt? RoundOnceCardlessProgramRequest{get;init;}
}
public sealed partial class GameEngine
{
 private const string RoundOnceCardlessProgramMarker="round-once-cardless-request";
 private bool RoundOnceCardlessProgramActor(ProgramSkillFrame f,SkillProgramEffectOp op,int actor)
 {
  if(f.InstructionIndex<1||_contentRegistry.Skills.GetValueOrDefault(f.SkillId)?.Program is not {} program||program.GameplayHash!=f.GameplayHash)return false;
  var plan=ProgramInstructionResolver.Default.Resolve(f,program);if(f.InstructionIndex>plan.Instructions.Count||plan.GetPausedInstruction(f.InstructionIndex).Effect.Op!=op)return false;
  if(op==SkillProgramEffectOp.UseOwnerSlashAgainstTurnOwner)return actor==f.OwnerSeat&&f.WindowContext?.Window==SkillProgramTriggerWindow.TurnEnding&&f.WindowContext.TargetSeat==_currentSeat&&f.FixedTargetSlash is {} fixedDraft&&fixedDraft.TargetSeat==_currentSeat&&fixedDraft.TargetSeat!=actor;
  if(op==SkillProgramEffectOp.RequestSlashByNearest)
  {var cursors=f.NumberBindings.Where(b=>b.Name=="participant-cursor-"+(f.InstructionIndex-1)).ToArray();return f.ReexecuteParticipantInstruction&&cursors is [var cursor]&&cursor.Value>=1&&cursor.Value<_playerCount&&actor==(f.OwnerSeat+cursor.Value)%_playerCount;}
  return (op is SkillProgramEffectOp.RequestSlashByTarget or SkillProgramEffectOp.RequestSlashAgainstChosenTarget or SkillProgramEffectOp.RequestLegalSlashByNearest)&&f.SelectedTargetSeats.SequenceEqual(new[]{actor});
 }
 private int[] RoundOnceCardlessProgramTargets(ProgramSkillFrame f,int actor,SkillProgramEffectOp op)
 {
  if(!RoundOnceCardlessProgramActor(f,op,actor))return [];
  return op switch
  {
   SkillProgramEffectOp.RequestSlashByTarget=>[f.OwnerSeat],
   SkillProgramEffectOp.UseOwnerSlashAgainstTurnOwner when f.FixedTargetSlash is {} fixedDraft=>[fixedDraft.TargetSeat],
   SkillProgramEffectOp.RequestSlashByNearest=>GetNearestLivingCharacterSeats(actor),
   SkillProgramEffectOp.RequestSlashAgainstChosenTarget when f.AssistedSlashRequest is {TargetSeat:{} target} draft&&draft.ActorSeat==actor=>[target],
   SkillProgramEffectOp.RequestLegalSlashByNearest when f.NearestLegalSlashRequest is {AwaitingFaction:false} nearest&&nearest.ActorSeat==actor&&nearest.NearestSeats.SequenceEqual(GetNearestLivingCharacterSeats(actor))=>nearest.NearestSeats.ToArray(),
   _=>[]
  };
 }
 private bool HasRoundOnceCardlessProgramSlash(CharacterState actor,IEnumerable<int> targets)=>targets.Any(target=>IsValidPlayerSeat(target)&&_players[target].IsAlive&&RoundOnceCardlessRules(actor,CardKind.Slash).Any(entry=>RoundOnceCardlessProgramTargetLegal(actor,target,entry.Source)));
 private bool RoundOnceCardlessProgramTargetLegal(CharacterState actor,int target,CardConversionSource source,bool ignoreDistance=false)=>IsValidPlayerSeat(target)&&actor.Seat!=target&&_players[target].IsAlive&&!HasBeneficiarySuitShield(actor.Seat,target,Suit.None)&&!IsCardTargetProhibited(_players[target],CardKind.Slash,Suit.None)&&CanUseSlashTarget(actor,_players[target],new(0,CardKind.Slash,Suit.None,0),source,CardKind.Slash,ignoreDistance:ignoreDistance,noEffectiveRank:true,physicalCardIds:[]);
 private IReadOnlyList<PromptChoice> RoundOnceCardlessProgramSlashChoices(ProgramSkillFrame frame,int actorSeat,IReadOnlyList<int> targets,string? resultBind=null)
 {
  if(frame.RoundOnceCardlessProgramRequest is not null||!_players[frame.OwnerSeat].IsAlive||!HasRuntimeSkillInstance(_players[frame.OwnerSeat],frame.SkillId,frame.SkillInstanceId)||!IsValidPlayerSeat(actorSeat))return [];
  var effect=GetPausedPrivateOfferEffect(frame);if(effect is null)return [];
  var allowed=RoundOnceCardlessProgramTargets(frame,actorSeat,effect.Op);if(allowed.Length==0||targets.Any(t=>!allowed.Contains(t))||resultBind is not null&&resultBind!=effect.ResultBind)return [];
  var result=new List<PromptChoice>();
  foreach(var(source,_)in RoundOnceCardlessRules(_players[actorSeat],CardKind.Slash))foreach(var target in targets.Distinct().Where(t=>RoundOnceCardlessProgramTargetLegal(_players[actorSeat],t,source,frame.FixedTargetSlash?.IgnoreDistance==true)))
  {
   var parameters=new Dictionary<string,string>{["program-action"]=RoundOnceCardlessProgramMarker,["frame-id"]=frame.Id.ToString(CultureInfo.InvariantCulture),["instruction-index"]=frame.InstructionIndex.ToString(CultureInfo.InvariantCulture),["actor-seat"]=actorSeat.ToString(CultureInfo.InvariantCulture),["target-seat"]=target.ToString(CultureInfo.InvariantCulture),["request-op"]=effect.Op.ToString()};AddConversionParameters(parameters,source);
   result.Add(new(new ChoiceId($"round-cardless.request.{frame.Id}.{frame.InstructionIndex}.{actorSeat}.{target}.{source.SkillInstanceId}.{source.BindingId}"),$"发动【{ProgramConversionName(source)}】，视为对 {_players[target].Name} 使用【杀】。",[],[target],parameters));
  }
  return Array.AsReadOnly(result.ToArray());
 }
 private PendingDecision RoundOnceCardlessProgramNativeDecision(PendingDecision p)=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")==RoundOnceCardlessProgramMarker)?p with{Choices=Array.AsReadOnly(p.Choices.Where(c=>c.Parameters.GetValueOrDefault("program-action")!=RoundOnceCardlessProgramMarker).ToArray())}:p;
 private bool TryResolveRoundOnceCardlessProgramSlash(PromptChoice choice,bool advance)
 {
  if(choice.Parameters.GetValueOrDefault("program-action")!=RoundOnceCardlessProgramMarker)return false;
  if(_pendingDecision is not {Kind:DecisionKind.ProgramTrigger} prompt||_resolutionStack.LastOrDefault()is not ProgramSkillFrame f||!prompt.Choices.Any(c=>AssistedChoicesEqual([c],[choice]))||choice.Cards.Count!=0||choice.Targets is not [var target]||choice.Parameters.GetValueOrDefault("frame-id")!=f.Id.ToString(CultureInfo.InvariantCulture)||choice.Parameters.GetValueOrDefault("instruction-index")!=f.InstructionIndex.ToString(CultureInfo.InvariantCulture)||choice.Parameters.GetValueOrDefault("actor-seat")!=prompt.PlayerSeat.ToString(CultureInfo.InvariantCulture))throw new InvalidOperationException("Cardless requested Slash changed its exact native prompt, actor, target or owning instruction.");
  var effect=GetPausedPrivateOfferEffect(f)??throw new InvalidOperationException("Cardless Slash lost its paused request effect.");var actor=_players[prompt.PlayerSeat];var targets=RoundOnceCardlessProgramTargets(f,actor.Seat,effect.Op);
  if(!RoundOnceCardlessProgramSlashChoices(f,actor.Seat,targets,effect.ResultBind).Any(c=>AssistedChoicesEqual([c],[choice])))throw new InvalidOperationException("Cardless Slash request lost its source qualification or current native allowed targets.");
  var source=RequireConversionSource(choice);ClearPendingDecision();var payment=ConsumeRoundOnceCardlessBasic(source,false);
  var receipt=new RoundOnceCardlessProgramRequestReceipt(f.Id,new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId),f.GameplayHash,f.InstructionIndex,effect.Op,actor.Seat,target,effect.ResultBind,prompt.PromptId,prompt.Revision,f.WindowContext?.ParentFrameId,payment);
  ReplaceRuntimeTop(f=f with{RoundOnceCardlessProgramRequest=new(receipt)});AdvanceEventRulesAndQueueFact(new RoundOnceCardlessProgramRequestAcceptedEvent(receipt));
  if(effect.Op==SkillProgramEffectOp.RequestSlashByTarget)CommitProgramChoiceResult(f.Id,effect.ResultBind!,RequestSlashByTargetProgramOperationDescriptor.UsedSlashOption,actor.Seat,"卫境：视为使用【杀】。");
  else if(effect.Op==SkillProgramEffectOp.RequestSlashAgainstChosenTarget){ReplaceRuntimeTop(f=GetActiveProgramFrame(f.Id)with{AssistedSlashRequest=null});CommitProgramChoiceResult(f.Id,effect.ResultBind!,"used-slash",actor.Seat,"卫境：视为使用【杀】。");}
  else if(effect.Op==SkillProgramEffectOp.UseOwnerSlashAgainstTurnOwner)ReplaceRuntimeTop(GetActiveProgramFrame(f.Id)with{FixedTargetSlash=f.FixedTargetSlash! with{Issued=true}});
  else if(effect.Op==SkillProgramEffectOp.RequestLegalSlashByNearest)ReplaceRuntimeTop(GetActiveProgramFrame(f.Id)with{NearestLegalSlashRequest=null});
  var card=new Card(0,CardKind.Slash,Suit.None,0);var id=BeginCardUse(card,actor.Seat,[target],CardKind.Slash,ignoresArmor:HasCardArmorBypass(actor,_players[target],CardKind.Slash),physicalCardIds:[],conversionSource:source);
  BindRoundOnceCardlessProgramRequestUse(LifecycleCardUse(id)!);IssueRoundOnceCardlessNativeUse(id,source);
  var attack=new CardAttackHandle(this,id,actor.Seat,LifecycleCardUse(id)!.TargetSeats[0],card:null,damageAmount:1+actor.AlcoholDamageBonus,playedCardKind:CardKind.Slash,ignoresArmor:HasCardArmorBypass(actor,_players[target],CardKind.Slash),programSkillCardUseFrameId:f.Id);
  ActiveCardAttack=attack;CaptureProgramAdjustedSlashBaseDamage(attack);CaptureProgramAlcoholConsumption(id,actor);actor.HasAlcoholEffect=false;MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat,CardKind.Slash);AdvanceEventRulesAndQueueFact(new CardUsedEvent(0,CardKind.Slash,actor.Seat,target,IgnoresArmor:attack.IgnoresArmor));NotifyAiOfSlash(actor,_players[target]);
  if(!TryMarkProgramUseCommitted(id)||!TryBeginProgramCardWindow(attack,LifecycleCardUse(id)!.Action!,SkillProgramTriggerWindow.CardUseCommitted,LifecycleCardUse(id)!.TargetSeats,ProgramCardContinuation.CommittedSlash))BeginSlashTargetResolution(attack);
  AdvanceRulesAndPublishState();if(advance)AdvanceToHumanBoundary();return true;
 }
 private void BindRoundOnceCardlessProgramRequestUse(CardUseFrame use)
 {
  var index=_resolutionStack.FindIndex(f=>f.Id==use.Id);if(index<1||_resolutionStack[index-1]is not ProgramSkillFrame{RoundOnceCardlessProgramRequest:{} p}parent)return;
  var r=p.Receipt;if(use.RoundOnceCardlessProgramRequest is {} bound){if(bound!=r||bound.NativeUseId!=use.Id)throw new InvalidOperationException("Cardless requested Slash cannot bind a second native child.");return;}
  if(r.NativeUseId!=0||r.ProgramFrameId!=parent.Id||!RoundOnceCardlessProgramActor(parent,r.RequestOp,r.ActorSeat)||use.CardId!=0||use.CardKind!=CardKind.Slash||use.PhysicalCardIds is not {Count:0}||use.Action is not {Type:CardActionType.Use} action||action.ActorSeat!=r.ActorSeat||action.ProviderSeat!=r.ActorSeat||action.RequesterSeat is not null||action.ConversionChain is not [var source]||source!=r.Payment.Source||action.PhysicalCards.Count!=0||!use.TargetSeats.SequenceEqual(new[]{r.TargetSeat})||!ValidRoundOnceCardlessProgramReceipt(parent,r,false))throw new InvalidOperationException("Cardless requested Slash changed its actual self-use, paid source, parent request or allowed target.");
  var final=r with{NativeUseId=use.Id,NativeActionId=action.ActionId};ReplaceRuntimeFrame(parent.Id,parent with{RoundOnceCardlessProgramRequest=p with{Receipt=final}});UpdateLifecycleCardUse(use.Id,f=>f with{RoundOnceCardlessProgramRequest=final});AdvanceEventRulesAndQueueFact(new RoundOnceCardlessProgramRequestBoundEvent(final));
 }
 private bool ValidRoundOnceCardlessProgramReceipt(ProgramSkillFrame parent,RoundOnceCardlessProgramRequestReceipt r,bool requireBound=true)
 {
  if(parent.Id!=r.ProgramFrameId||parent.InstructionIndex!=r.InstructionIndex||r.RequestSource!=new CardConversionSource(parent.SkillId,GetProgramBindingId(parent),parent.OwnerSeat,parent.SkillInstanceId)||parent.GameplayHash!=r.RequestGameplayHash||parent.WindowContext?.ParentFrameId!=r.OriginalWindowId||r.Payment.Source.OwnerSeat!=r.ActorSeat||!RoundOnceCardlessProgramActor(parent,r.RequestOp,r.ActorSeat)||r.OriginalPromptId.Value<=0||r.OriginalRevision<0||r.Payment.GameplayHash!=_contentRegistry.GetSkill(r.Payment.Source.SkillId).Program!.GameplayHash||ViewAsRule(r.Payment.Source)is not {OutputKind:CardKind.Slash,ForPlay:true,ForResponse:false,RoundOnceCardlessBasic:{}}||CompleteProgramEventHistory().OfType<RoundOnceCardlessBasicQuotaPaidEvent>().Count(e=>e.Receipt==r.Payment)!=1||CompleteProgramEventHistory().OfType<RoundOnceCardlessProgramRequestAcceptedEvent>().Count(e=>e.Receipt==(r with{NativeUseId=0,NativeActionId=0}))!=1)return false;
  var effect=GetPausedPrivateOfferEffect(parent);if(effect?.ResultBind!=r.ResultBind)return false;
  if(r.RequestOp is SkillProgramEffectOp.RequestSlashByTarget or SkillProgramEffectOp.RequestSlashAgainstChosenTarget)
  {if(parent.ChoiceBindings.Count(b=>b.Name==r.ResultBind&&b.ChooserSeat==r.ActorSeat&&b.OptionId=="used-slash")!=1)return false;}
  return !requireBound||r.NativeUseId>0&&r.NativeActionId>0&&CompleteProgramEventHistory().OfType<RoundOnceCardlessProgramRequestBoundEvent>().Count(e=>e.Receipt==r)==1;
 }
 private bool RoundOnceCardlessProgramRequestRidesOn(CardUseFrame use,ProgramSkillFrame parent)=>use.RoundOnceCardlessProgramRequest is {} r&&parent.RoundOnceCardlessProgramRequest is {NativeReturned:false} p&&p.Receipt==r&&ValidRoundOnceCardlessProgramReceipt(parent,r)&&IsRoundOnceCardlessBasicUse(use.Id)&&use.CardAttack?.ProgramSkillCardUseFrameId==parent.Id&&use.RoundOnceCardlessBasicUse?.CardActionId==r.NativeActionId;
 private bool CanContinueIssuedRoundOnceCardlessProgramRequest(ProgramSkillFrame parent)=>parent.RoundOnceCardlessProgramRequest is {} p&&ValidRoundOnceCardlessProgramReceipt(parent,p.Receipt)&&(!p.NativeReturned||CompleteProgramEventHistory().OfType<RoundOnceCardlessProgramRequestReturnedEvent>().Count(e=>e.Receipt==p.Receipt)==1);
 private void AssertRoundOnceCardlessProgramRequests()
 {
  foreach(var parent in _resolutionStack.OfType<ProgramSkillFrame>().Where(f=>f.RoundOnceCardlessProgramRequest is not null))if(!CanContinueIssuedRoundOnceCardlessProgramRequest(parent))throw new InvalidOperationException("Cardless request lost its exact original actor, instruction, source invoice or paid native return.");
  foreach(var use in _resolutionStack.OfType<CardUseFrame>().Where(f=>f.RoundOnceCardlessProgramRequest is not null))if(_resolutionStack.SingleOrDefault(f=>f.Id==use.RoundOnceCardlessProgramRequest!.ProgramFrameId)is not ProgramSkillFrame parent||!RoundOnceCardlessProgramRequestRidesOn(use,parent))throw new InvalidOperationException("Cardless request child lost its original paid program parent.");
 }
 private void AssertRoundOnceCardlessProgramRequestCompletion(CardUseFrame use)
 {
  if(use.RoundOnceCardlessProgramRequest is not {} r)return;
  if(use.Step!=ResolutionFrameStep.Completed||_resolutionStack.LastOrDefault()?.Id!=use.Id||use.PendingRecoveryAttempts is {Count:>0}||use.RecoveryPaidContinuation is not null||_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(w=>w.ParentFrameId==use.Id)||_resolutionStack.SingleOrDefault(f=>f.Id==r.ProgramFrameId)is not ProgramSkillFrame parent||!RoundOnceCardlessProgramRequestRidesOn(use,parent))throw new InvalidOperationException("Cardless request cannot return before its actual native Use and all paid children completed.");
 }
 private void ReturnRoundOnceCardlessProgramRequestUse(CardUseFrame completed)
 {
  if(completed.RoundOnceCardlessProgramRequest is not {} r)return;
  if(_resolutionStack.Any(f=>f.Id==completed.Id)||completed.Step!=ResolutionFrameStep.Completed||completed.RoundOnceCardlessBasicUse?.CardActionId!=r.NativeActionId||_resolutionStack.SingleOrDefault(f=>f.Id==r.ProgramFrameId)is not ProgramSkillFrame parent||parent.RoundOnceCardlessProgramRequest is not {NativeReturned:false} p||p.Receipt!=r||!ValidRoundOnceCardlessProgramReceipt(parent,r)||CompleteProgramEventHistory().OfType<RoundOnceCardlessProgramRequestReturnedEvent>().Any(e=>e.Receipt==r))throw new InvalidOperationException("Cardless request can return only once after its own actual native child completed.");
  ReplaceRuntimeFrame(parent.Id,parent with{RoundOnceCardlessProgramRequest=p with{NativeReturned=true}});AdvanceEventRulesAndQueueFact(new RoundOnceCardlessProgramRequestReturnedEvent(r));
 }
 [ProgramContinuation(ProgramContinuationKind.Resume,23402)]
 private bool ResumeRoundOnceCardlessProgramRequest(long id)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{RoundOnceCardlessProgramRequest:{NativeReturned:true}}parent||parent.Id!=id)return false;
  if(!CanContinueIssuedRoundOnceCardlessProgramRequest(parent))throw new InvalidOperationException("Cardless request lost its once completed paid native return.");ReplaceRuntimeTop(parent=parent with{RoundOnceCardlessProgramRequest=null});
  if(_winner!=Winner.None||_status==EngineStatus.Completed||!_players[parent.OwnerSeat].IsAlive||!HasRuntimeSkillInstance(_players[parent.OwnerSeat],parent.SkillId,parent.SkillInstanceId))FinishProgramSkill(parent,true);else if(parent.FixedTargetSlash is {Issued:true})TryResumeFixedSlashAndDeclaredDeck(id);else AdvanceRuntimeProgram(id);return true;
 }
}
