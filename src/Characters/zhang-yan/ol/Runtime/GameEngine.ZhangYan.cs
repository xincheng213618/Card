using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
 private const string ZhangYanObtainReason="skill-program.zhang-yan.suji.obtain";
 private CardConversionSource ZhangYanSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
 [ProgramCapability]private sealed class ZhangYanCapability(GameEngine e):IZhangYanHost {public SkillProgramStepOutcome Execute(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginZhangYan(f,op);}
 private bool ZhangYanPlayParent(ProgramTriggerCandidate candidate,ProgramSkillWindowContext c)=>c.Window==SkillProgramTriggerWindow.PlayPhaseStarting&&c.OwnerSeat==candidate.OwnerSeat&&c.SourceSeat==c.TargetSeat&&c.SourceSeat==_currentSeat&&_phase==TurnPhase.Play&&
  _resolutionStack.SingleOrDefault(x=>x.Id==c.ParentFrameId)is PlayPhaseStartingBoundaryFrame w&&w.OwnerSeat==c.SourceSeat&&w.ItemIndex>=0&&w.ItemIndex<w.Items.Count&&w.Items[w.ItemIndex].Candidate==candidate;
 private bool CanZhangYanSujiMaterial(int actor,Card card,CardLocation from,int target)=>_winner==Winner.None&&_players[actor].IsAlive&&_players[target].IsAlive&&actor!=target&&from.OwnerSeat==actor&&from.Zone is CardZoneKind.Hand or CardZoneKind.Equipment&&_cardZones.GetLocation(card.Id)==from&&
  GetProgramEffectiveSuit(_players[actor],card)is Suit.Spade or Suit.Club&&!IsTurnPhysicalUseForbidden(actor,[card.Id])&&(from.Zone!=CardZoneKind.Hand||!IsTurnHandCardRestricted(_players[actor],card))&&
  !HasTurnCardTargetRestriction(actor,SkillProgramCardTargetRestriction.SelfOnly)&&CanUseSlashTarget(_players[actor],_players[target],card,effectiveKind:CardKind.Slash,physicalCardIds:[card.Id],excludedEquipmentId:from.Zone==CardZoneKind.Equipment?card.Id:null)&&
  CanSpendSlashUse(_players[actor],_players[target],true,CardKind.Slash,card,excludedEquipmentId:from.Zone==CardZoneKind.Equipment?card.Id:null,physicalCardIds:[card.Id]);
 private int[] ZhangYanSujiTargets(int actor,Card card,CardLocation from)=>_players.Where(p=>CanZhangYanSujiMaterial(actor,card,from,p.Seat)).Select(p=>p.Seat).ToArray();
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27000)]
 private bool CanOfferZhangYan(ProgramTriggerCandidate candidate,SkillProgramTrigger trigger,ProgramSkillWindowContext c)
 {
  if(trigger.Effects is not[var e]||!ZhangYanComposition.IsOperation(e.Op))return true;
  if(e.Op==SkillProgramEffectOp.ObtainSujiDamagedPhaseOwnerCard)return HasIssuedZhangYanSujiRewardCandidate(candidate,c)&&ZhangYanRewardChoicesAvailable(candidate.OwnerSeat,c.TargetSeat!.Value);
  if(e.Op==SkillProgramEffectOp.ResolveLangdaoSimultaneousSlash)return ZhangYanLangdaoParent(candidate,c)is{ZhangYanLangdao:null}use&&use.TargetSeats.Count==1&&ZhangYanAvailableOptions(new(candidate.SkillId,candidate.BindingId,candidate.OwnerSeat,candidate.SkillInstanceId)).Count>0;
  return ZhangYanPlayParent(candidate,c)&&c.Facts is{EventTargetHpUndefined:false,EventTargetMaxHp:>0}facts&&(facts.EventTargetRuleWounded??facts.EventTargetHp<facts.EventTargetMaxHp)&&
   new[]{CardZoneKind.Hand,CardZoneKind.Equipment}.Any(z=>_cardZones.CardsAt(new(z,candidate.OwnerSeat)).Any(card=>ZhangYanSujiTargets(candidate.OwnerSeat,card,new(z,candidate.OwnerSeat)).Length>0));
 }
 private SkillProgramStepOutcome BeginZhangYan(ProgramSkillFrame supplied,SkillProgramEffectOp op)
 {
  var f=GetActiveProgramFrame(supplied.Id);if(f.ZhangYan is not null||f.InstructionIndex!=1||f.TriggerId is null||f.WindowContext is not{}c)throw new InvalidOperationException("Zhang Yan requires its exact new native trigger instruction.");
  var candidate=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,c.OccurrenceIndex);
  if(!CanOfferZhangYan(candidate,GetProgramTrigger(f),c))throw new InvalidOperationException("Zhang Yan lost its actual phase, issued damage reward or unique native Slash.");
  var use=op==SkillProgramEffectOp.ResolveLangdaoSimultaneousSlash?ZhangYanLangdaoParent(candidate,c):op==SkillProgramEffectOp.ObtainSujiDamagedPhaseOwnerCard?ZhangYanRewardUse(c):null;
  var ret=op==SkillProgramEffectOp.ObtainSujiDamagedPhaseOwnerCard?use!.ZhangYanSuji:null;
  var phaseOwner=ret?.PhaseOwner??(op==SkillProgramEffectOp.UseBlackCardAtWoundedPlayStart?c.SourceSeat!.Value:use!.TargetSeats.Single());
  var damage=op==SkillProgramEffectOp.ObtainSujiDamagedPhaseOwnerCard?_resolutionStack.OfType<DamageTriggerWindowFrame>().Single(w=>w.Id==c.ParentFrameId).ParentFrameId:(long?)null;
  var r=new ZhangYanReceipt(ZhangYanSource(f),f.GameplayHash,c.ParentFrameId,op,_turnNumber,phaseOwner,op==SkillProgramEffectOp.UseBlackCardAtWoundedPlayStart?ZhangYanStage.SujiCard:op==SkillProgramEffectOp.ResolveLangdaoSimultaneousSlash?ZhangYanStage.LangdaoVotes:ZhangYanStage.RewardChoice,NativeReturn:ret,DamageId:damage)
   {Options=op==SkillProgramEffectOp.ResolveLangdaoSimultaneousSlash?ZhangYanAvailableOptions(ZhangYanSource(f)):[]};
  ReplaceRuntimeTop(f=f with{ZhangYan=r});AdvanceEventRulesAndQueueFact(new ZhangYanStartedEvent(f.Id,r.Source,r.GameplayHash,r.WindowId,op,r.Turn,r.PhaseOwner,damage));
  for(var i=0;i<r.Options.Count;i++)AdvanceEventRulesAndQueueFact(new ZhangYanLangdaoOptionFrozenEvent(f.Id,i,r.Options[i]));PublishZhangYan(f);return SkillProgramStepOutcome.AwaitChoice;
 }
 private int ZhangYanChooser(ProgramSkillFrame f)=>f.ZhangYan is{Stage:ZhangYanStage.LangdaoVotes,Votes.Count:1}r?r.PhaseOwner:f.OwnerSeat;
 private IReadOnlyList<PromptChoice> ZhangYanChoices(ProgramSkillFrame f)
 {
  var r=f.ZhangYan!;PromptChoice C(string option,string label,int? card=null,int? target=null)=>new(new($"zhang-yan.{f.Id}.{r.Stage}.{option}.{card}.{target}.{r.Votes.Count}.{r.AddedTargets.Count}"),label,card is{}id?[id]:[],target is{}seat?[seat]:[],new Dictionary<string,string>{{"program-action","zhang-yan"},{"option",option}});
  if(r.Stage==ZhangYanStage.SujiCard)return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,f.OwnerSeat,[CardZoneKind.Hand,CardZoneKind.Equipment],OwnedCardMoveIntent.Transfer,
    canSelect:(z,card)=>ZhangYanSujiTargets(f.OwnerSeat,card,new(z,f.OwnerSeat)).Length>0).Select(c=>c with{Parameters=new Dictionary<string,string>(c.Parameters){["program-action"]="zhang-yan",["option"]="material"}}).ToArray());
  if(r.Stage==ZhangYanStage.SujiTarget)return Array.AsReadOnly(ZhangYanSujiTargets(f.OwnerSeat,GetAdvancedCard(r.CardId!.Value),r.From!.Value).Select(t=>C("target",$"对{_players[t].Name}使用此杀",target:t)).ToArray());
  if(r.Stage==ZhangYanStage.LangdaoVotes)return Array.AsReadOnly(r.Options.Select(option=>C(option.ToString(),option==LangdaoOption.Damage?"令此杀伤害+1":option==LangdaoOption.ExtraTarget?"令此杀目标数+1":"令此杀不能被响应")).ToArray());
  if(r.Stage==ZhangYanStage.LangdaoTargets)
  {var use=LifecycleCardUse(f.WindowContext!.CardUse!.ParentCardUseFrameId)!;var choices=ZhangYanExtraTargets(f,use).Select(t=>C("append",$"额外指定{_players[t].Name}",target:t)).ToList();choices.Add(C("finish","完成额外目标选择"));return Array.AsReadOnly(choices.ToArray());}
  if(r.Stage!=ZhangYanStage.RewardChoice||!_players[f.OwnerSeat].IsAlive||!_players[r.PhaseOwner].IsAlive)return [];
  return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,r.PhaseOwner,[CardZoneKind.Hand,CardZoneKind.Equipment],OwnedCardMoveIntent.Transfer,canSelect:(_,card)=>!card.IsGeneralWeapon)
   .Select(c=>c with{Parameters=new Dictionary<string,string>(c.Parameters){["program-action"]="zhang-yan",["option"]="obtain"}}).ToArray());
 }
 private bool ZhangYanRewardChoicesAvailable(int owner,int target)=>_players[owner].IsAlive&&_players[target].IsAlive&&new[]{CardZoneKind.Hand,CardZoneKind.Equipment}.Any(z=>_cardZones.CardsAt(new(z,target)).Any(card=>!card.IsGeneralWeapon));
 private void PublishZhangYan(ProgramSkillFrame f)
 {
  var choices=ZhangYanChoices(f);if(choices.Count==0){FinishZhangYan(f,false);return;}var chooser=ZhangYanChooser(f);var skill=_contentRegistry.GetSkill(f.SkillId);
  ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.AwaitingResponse});_pendingDecision=new(DecisionKind.ProgramTrigger,chooser,f.ZhangYan!.Stage==ZhangYanStage.LangdaoVotes?"狼蹈：独立密选，双方提交后同时公开。":"张燕：结算当前原生牌/阶段窗口。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat)
   {PromptId=CreatePromptId(),IsPrivate=f.ZhangYan.Stage is not ZhangYanStage.LangdaoTargets,TargetSeat=f.ZhangYan.PhaseOwner,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
  _status=_players[chooser].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 [ProgramAnswer("zhang-yan")]
 private void AnswerZhangYan(PromptChoice choice)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{ZhangYan:{}r}f||_pendingDecision?.PlayerSeat!=ZhangYanChooser(f)||!AssistedChoicesEqual([choice],ZhangYanChoices(f).Where(c=>c.Id==choice.Id).ToArray()))throw new InvalidOperationException("Zhang Yan lost its exact native chooser or sealed legal choices.");
  AssertZhangYan(f);ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});var option=choice.Parameters["option"];
  if(r.Stage==ZhangYanStage.LangdaoVotes)
  {var vote=new ZhangYanVote(ZhangYanChooser(f),Enum.Parse<LangdaoOption>(option));ReplaceRuntimeTop(f=f with{ZhangYan=r with{Votes=r.Votes.Append(vote).ToArray()}});if(f.ZhangYan!.Votes.Count==1){PublishZhangYan(f);return;}ResolveZhangYanLangdao(f);return;}
  if(r.Stage==ZhangYanStage.LangdaoTargets)
  {if(option=="append"){ReplaceRuntimeTop(f=f with{ZhangYan=r with{AddedTargets=r.AddedTargets.Append(choice.Targets.Single()).ToArray()}});if(f.ZhangYan!.AddedTargets.Count<f.ZhangYan.Votes.Count(v=>v.Option==LangdaoOption.ExtraTarget)){PublishZhangYan(f);return;}}CompleteZhangYanLangdaoTargets(f);return;}
  if(r.Stage==ZhangYanStage.SujiTarget)
  {
   var target=choice.Targets.Single();var card=GetAdvancedCard(r.CardId!.Value);if(!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)||!CanZhangYanSujiMaterial(f.OwnerSeat,card,r.From!.Value,target)){FinishZhangYan(f,false);return;}
   var grain=r.From==CardLocation.Equipment(f.OwnerSeat)&&card.Kind==CardKind.WoodenOx?GetWoodenOxGrain(_players[f.OwnerSeat]).Select(c=>c.Id).ToArray():[];
   ReplaceRuntimeTop(f with{ZhangYan=r with{Stage=ZhangYanStage.IssuingUse,Target=target,Before=_movementSequence,After=_movementSequence,Grain=grain}});
   ResolveSlashCore(_players[f.OwnerSeat],_players[target],card,CardKind.Slash,f.OwnerSeat,physicalCards:[card],countsTowardSlashLimit:false,conversionSource:r.Source,programSkillCardUseFrameId:f.Id);return;
  }
  if(!Enum.TryParse<CardZoneKind>(choice.Parameters.GetValueOrDefault("source-zone"),out var zone)||!int.TryParse(choice.Parameters.GetValueOrDefault("slot-index"),CultureInfo.InvariantCulture,out var slot))throw new InvalidOperationException("Zhang Yan lost its exact HE slot.");
  var payer=r.Stage==ZhangYanStage.SujiCard?f.OwnerSeat:r.PhaseOwner;var from=new CardLocation(zone,payer);var cards=_cardZones.CardsAt(from);if(slot<0||slot>=cards.Count)throw new InvalidOperationException("Zhang Yan's original HE slot is absent.");var selected=cards[slot];
  if(r.Stage==ZhangYanStage.SujiCard){ReplaceRuntimeTop(f=f with{ZhangYan=r with{Stage=ZhangYanStage.SujiTarget,CardId=selected.Id,From=from}});PublishZhangYan(f);return;}
  var grainIds=from==CardLocation.Equipment(payer)&&selected.Kind==CardKind.WoodenOx?GetWoodenOxGrain(_players[payer]).Select(c=>c.Id).ToArray():[];
  ReplaceRuntimeTop(f=f with{ZhangYan=r with{Stage=ZhangYanStage.RewardChildren,CardId=selected.Id,From=from,Before=_movementSequence,After=_movementSequence,Grain=grainIds},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  if(from!=CardLocation.Hand(f.OwnerSeat))MoveProgramCardsFromMultipleSources([selected.Id],CardLocation.Hand(f.OwnerSeat),new(ZhangYanObtainReason));var current=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=current with{ZhangYan=current.ZhangYan! with{After=_movementSequence}});
  AdvanceEventRulesAndQueueFact(new ZhangYanRewardIssuedEvent(f.Id,r.NativeReturn!.UseId,r.DamageId!.Value,payer,selected.Id,from,current.ZhangYan!.Before,_movementSequence));ContinueZhangYan(f.Id);
 }
 private bool DrainZhangYan(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id);
 private void ContinueZhangYan(long id)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{ZhangYan:{}r}f||f.Id!=id)return;AssertZhangYan(f);
  if(r.Stage is ZhangYanStage.NativeUse or ZhangYanStage.IssuingUse)return;
  if(r.Stage is ZhangYanStage.NativeChildren or ZhangYanStage.RewardChildren){if(DrainZhangYan(f))return;FinishZhangYan(GetActiveProgramFrame(id),true);return;}
  if(_pendingDecision is null)PublishZhangYan(f);
 }
 private void FinishZhangYan(ProgramSkillFrame f,bool applied){AdvanceEventRulesAndQueueFact(new ZhangYanCompletedEvent(f.Id,applied));ReplaceRuntimeTop(f=f with{ZhangYan=null,PendingMovementContinuation=null});FinishProgramSkill(f,applied);}
 [ProgramContinuation(ProgramContinuationKind.Resume,27000)]private bool ResumeZhangYan(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{ZhangYan:not null}f||f.Id!=id)return false;ContinueZhangYan(id);return true;}
 [ProgramContinuation(ProgramContinuationKind.Movement,27000)]private bool ReturnZhangYanMovement(ProgramSkillFrame f){if(f.ZhangYan is null)return false;ContinueZhangYan(f.Id);return true;}
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27000)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27000)]
 private bool ContinuePaidZhangYan(ProgramSkillFrame f)=>f.ZhangYan is{}r&&r.Stage is ZhangYanStage.NativeUse or ZhangYanStage.NativeChildren or ZhangYanStage.RewardChildren&&ProgramEventHistory<ZhangYanStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash)==1;
 [ProgramAiChoice(SkillProgramEffectOp.UseBlackCardAtWoundedPlayStart,SkillProgramEffectOp.ResolveLangdaoSimultaneousSlash,SkillProgramEffectOp.ObtainSujiDamagedPhaseOwnerCard)]
 private PromptChoice AiZhangYan(PendingDecision p,ProgramSkillFrame f)
 {
  if(f.ZhangYan!.Stage==ZhangYanStage.LangdaoVotes){var allied=AreProgramDistributionAllies(_players[p.PlayerSeat],_players[f.OwnerSeat]);var option=allied?LangdaoOption.Damage:LangdaoOption.ExtraTarget;return p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("option")==option.ToString())??p.Choices.First();}
  return p.Choices.FirstOrDefault(c=>c.Targets.Count==1&&!AreProgramDistributionAllies(_players[f.OwnerSeat],_players[c.Targets.Single()]))??p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("option")=="finish")??p.Choices.First();
 }
}
