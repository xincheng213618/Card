using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
 private const string ZhangJiGainReason="program.zhang-ji.lueming.gain";
 private static bool IsZhangJiOp(SkillProgramEffectOp op)=>(int)op is >=23800 and <=23801;
 private CardConversionSource ZhangJiSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
 private SkillProgramEffect? ZhangJiPausedEffect(ProgramSkillFrame f)=>f.InstructionIndex<1?null:ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
 private int ZhangJiLueMingCount(int owner)=>CompleteProgramEventHistory().OfType<ZhangJiStartedEvent>().Count(e=>e.Source.OwnerSeat==owner && !e.Equipment);
 [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCondition, 70)]
 private bool CanActivateZhangJi(int owner,SkillProgramActivation a)=>true;
 private bool ZhangJiActivationTargetAllowed(CharacterState owner,CharacterState target,SkillProgramActivation a)=>
  !ProgramInstructionResolver.Default.Features(a).HasOperation(SkillProgramEffectOp.ResolveTargetChosenRankJudgment) || target.Seat!=owner.Seat && GetEquipment(target).Count<GetEquipment(owner).Count;
 private SkillProgramStepOutcome BeginZhangJiOperation(ProgramSkillFrame supplied,SkillProgramEffectOp op)
 {
  var f=GetActiveProgramFrame(supplied.Id);var equip=op==SkillProgramEffectOp.UseDistinctDeckEquipmentByLueMingHistory;
  if(!IsZhangJiOp(op) || f.ZhangJi is not null || f.TriggerId is not null || f.WindowContext is not null || f.InstructionIndex!=1 || f.SelectedCardIds.Count!=0 ||
   f.SelectedTargetSeats is not[var target] || !IsValidPlayerSeat(target) || !_players[target].IsAlive ||
   ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).Activation is not{}a || !ZhangJiActivationTargetAllowed(_players[f.OwnerSeat],_players[target],a))
   throw new InvalidOperationException("Zhang Ji lost its real selected participant or equipment count eligibility.");
  var count=ZhangJiLueMingCount(f.OwnerSeat);var r=new ZhangJiReceipt{Source=ZhangJiSource(f),GameplayHash=f.GameplayHash,Turn=_turnNumber,PhaseInstance=_cardUseDebitPhaseInstanceId,InstructionIndex=f.InstructionIndex,
   Target=target,Equipment=equip,HistoricalCount=count,Stage=equip?"equipment":"rank"};ReplaceRuntimeTop(f=f with{ZhangJi=r});
  AdvanceEventRulesAndQueueFact(new ZhangJiStartedEvent(f.Id,r.Source,r.GameplayHash,r.Turn,r.PhaseInstance,target,equip,count));
  if(equip)IssueZhangJiEquipment(f);else PublishZhangJi(f);return equip?SkillProgramStepOutcome.AwaitChild:SkillProgramStepOutcome.AwaitChoice;
 }
 private IReadOnlyList<PromptChoice> ZhangJiChoices(ProgramSkillFrame f)=>Array.AsReadOnly(Enumerable.Range(1,13).Select(rank=>new PromptChoice(new($"zhang-ji.{f.Id}.rank.{rank}"),"选择点数 "+rank,[],[],new Dictionary<string,string>{["program-action"]="zhang-ji",["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture),["rank"]=rank.ToString(CultureInfo.InvariantCulture)})).ToArray());
 private void PublishZhangJi(ProgramSkillFrame f)
 {
  var r=f.ZhangJi!;if(!_players[r.Target].IsAlive || _winner!=Winner.None){FinishZhangJi(f);return;}
  var choices=ZhangJiChoices(f);var skill=_contentRegistry.GetSkill(f.SkillId);
  _pendingDecision=new(DecisionKind.ProgramTrigger,r.Target,"掠命：请选择一个点数。",[],[],f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=true,TargetSeat=r.Target,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
  _status=_players[r.Target].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 [ProgramAnswer("zhang-ji")]
 private void ResolveZhangJiChoice(PromptChoice supplied)
 {
  if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{ZhangJi:{Stage:"rank"}r}f || _pendingDecision?.PlayerSeat!=r.Target)throw new InvalidOperationException("Lueming requires its actual recipient's rank decision.");
  AssertZhangJi(f);var choice=ZhangJiChoices(f).SingleOrDefault(c=>c.Id==supplied.Id && PromptChoiceEquals(c,supplied))??throw new InvalidOperationException("Invalid Lueming rank choice.");
  var rank=int.Parse(choice.Parameters["rank"],CultureInfo.InvariantCulture);ClearPendingDecision();var id=_resolutionSequence+1;
  ReplaceRuntimeTop(f=f with{ZhangJi=r with{ChosenRank=rank,JudgmentId=id,Stage="judgment"}});AdvanceEventRulesAndQueueFact(new ZhangJiRankChosenEvent(f.Id,r.Target,rank,id));
  _=BeginJudgment(null,f.OwnerSeat,"lueming",f.Id,null,JudgmentContinuationKind.ProgramSkill,sourceSeat:f.OwnerSeat,programResultBind:"zhang-ji-rank",programResultVisibility:SkillProgramCardSetVisibility.Public);
 }
 private static bool PromptChoiceEquals(PromptChoice a,PromptChoice b)=>a.Description==b.Description && a.Cards.SequenceEqual(b.Cards) && a.Targets.SequenceEqual(b.Targets) && a.Parameters.Count==b.Parameters.Count && a.Parameters.All(p=>b.Parameters.TryGetValue(p.Key,out var v)&&v==p.Value);
 private bool CanBeginZhangJiJudgment(ProgramSkillFrame f,int targetSeat,string reason,string bind,SkillProgramCardSetVisibility visibility)=>f.ZhangJi is{Stage:"judgment",Equipment:false,JudgmentReturned:false,ChosenRank:>=1 and <=13,JudgmentId:not null} && ValidZhangJi(f) && targetSeat==f.OwnerSeat && reason=="lueming" && bind=="zhang-ji-rank" && visibility==SkillProgramCardSetVisibility.Public;
 private bool IsZhangJiJudgment(JudgmentFrame j,ProgramSkillFrame f)=>f.ZhangJi is{Equipment:false,Stage:"judgment",JudgmentReturned:false,JudgmentId:{}id}r && ValidZhangJi(f) &&
  j.Id==id && j.ParentFrameId==f.Id && j.TargetSeat==f.OwnerSeat && j.SourceSeat==f.OwnerSeat && j.Continuation==JudgmentContinuationKind.ProgramSkill && j.Reason=="lueming" && j.ProgramResultBind=="zhang-ji-rank" && j.ProgramResultVisibility==SkillProgramCardSetVisibility.Public;
 private bool TryReturnZhangJiJudgment(JudgmentFrame j)
 {
  if(_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==j.ParentFrameId) is not{ZhangJi:not null}f)return false;
  if(!IsZhangJiJudgment(j,f))throw new InvalidOperationException("Lueming received a foreign judgment return.");
  var result=CompleteProgramEventHistory().OfType<JudgmentResolvedEvent>().Single(e=>e.ResolutionId==j.Id);
  var finalCard=GetJudgmentCard(j);
  SetProgramCardSet(f.Id,"zhang-ji-rank",finalCard is null?[]:[finalCard.Id],SkillProgramCardSetVisibility.Public,finalCard is null?[]:[_cardZones.GetLocation(finalCard.Id)],finalCard is null?null:EffectiveZhenyiJudgmentSuit(j,EffectiveSuit(_players[j.TargetSeat],finalCard)));
  f=GetActiveProgramFrame(f.Id);var r=f.ZhangJi!;
  ReplaceRuntimeTop(f=f with{ZhangJi=r with{JudgmentReturned=true,JudgmentCard=result.CardId,FinalRank=result.Rank,Stage="outcome"}});
  AdvanceEventRulesAndQueueFact(new ZhangJiJudgmentReturnedEvent(f.Id,j.Id,result.CardId,result.Rank));ResumeZhangJi(f.Id);return true;
 }
 private void IssueZhangJiOutcome(ProgramSkillFrame f)
 {
  var r=f.ZhangJi!;if(!_players[r.Target].IsAlive || _winner!=Winner.None){FinishZhangJi(f);return;}
  if(r.FinalRank==r.ChosenRank)
  {
   ReplaceRuntimeTop(f=f with{ZhangJi=r with{Stage="damage"}});AdvanceEventRulesAndQueueFact(new ZhangJiDamageIssuedEvent(f.Id,f.OwnerSeat,r.Target,2));BeginProgramSkillDamage(f,r.Target,2);return;
  }
  if(!_players[f.OwnerSeat].IsAlive){FinishZhangJi(f);return;}
  var cards=new[]{CardLocation.Hand(r.Target),CardLocation.Equipment(r.Target),CardLocation.Judgment(r.Target)}.SelectMany(loc=>_cardZones.CardsAt(loc)).Where(c=>!IsForeignEquipmentDiscardPrevented(f.OwnerSeat,c,_cardZones.GetLocation(c.Id),OwnedCardMoveIntent.Obtain)).OrderBy(c=>c.Id).ToArray();
  if(cards.Length==0){FinishZhangJi(f);return;}
  var card=cards[_random.Next(cards.Length)];var from=_cardZones.GetLocation(card.Id);var before=_movementSequence;
  ReplaceRuntimeTop(f=f with{ZhangJi=r with{Stage="movement",Before=before,After=before,GainCard=card.Id,GainFrom=from},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  MoveProgramCardsFromMultipleSources([card.Id],CardLocation.Hand(f.OwnerSeat),new(ZhangJiGainReason),(_,records)=>{var current=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(current with{ZhangJi=current.ZhangJi! with{After=_movementSequence}});AdvanceEventRulesAndQueueFact(new ZhangJiCardGainedEvent(f.Id,r.Target,f.OwnerSeat,before,_movementSequence));});
  if(!TryBeginCardsMovedProgramWindow(f.Id))ReturnRuntimeProgramMovement(f.Id);
 }
 private void IssueZhangJiEquipment(ProgramSkillFrame f)
 {
  var r=f.ZhangJi!;var target=_players[r.Target];
  if(r.Slots.Count>=r.HistoricalCount || !target.IsAlive || _winner!=Winner.None){FinishZhangJi(f);return;}
  var candidates=_cardZones.CardsAt(CardLocation.DrawPile).Where(c=>EquipmentCatalog.IsEquipment(c.Kind) && !r.Slots.Contains(EquipmentCatalog.Get(c.Kind).Slot) && target.EquipmentSlotCapacity(EquipmentCatalog.Get(c.Kind).Slot)>0 &&
   !IsCardUseForbidden(target.Seat,c.Kind,CardActionType.Use) && !IsSelfTargetForbiddenAction(target,c.Kind,[target.Seat]) && !IsResponseEntityRestricted(target.Seat,c.Id) && !IsPlayPhasePhysicalCardRestricted(target,c)).OrderBy(c=>c.Id).ToArray();
  if(candidates.Length==0){FinishZhangJi(f);return;}
  var card=candidates[_random.Next(candidates.Length)];var slot=EquipmentCatalog.Get(card.Kind).Slot;
  ReplaceRuntimeTop(f=f with{ZhangJi=r with{Stage="issuing-equipment",NativeUse=null,NativeReturned=false,Slots=r.Slots.Append(slot).ToArray()}});
  var id=BeginCardUse(card,target.Seat,[],conversionSource:r.Source);MoveCard(card,CardLocation.DrawPile,CardLocation.Processing,CardMoveReasons.EquipmentUse);
  if(!TryBeginEquipmentTargetPrograms(target,card,id))CompleteEquipmentUse(target,card,id);
 }
 private void BindZhangJiNativeUse(long useId)
 {
  if(LifecycleCardUse(useId) is not{Action:{}a}use || _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.ZhangJi is{Stage:"issuing-equipment"}) is not{}f)return;
  var r=f.ZhangJi!;if(a.ActorSeat!=r.Target || a.ProviderSeat!=r.Target || a.RequesterSeat is not null || a.PhysicalCards is not[var cost] || cost.From!=CardLocation.DrawPile || !EquipmentCatalog.IsEquipment(a.EffectiveKind) || r.Slots[^1]!=EquipmentCatalog.Get(a.EffectiveKind).Slot || a.ConversionChain is not[var source] || source!=r.Source)throw new InvalidOperationException("Tunjun native use lost its exact deck entity or actual beneficiary.");
  var origin=new ZhangJiNativeUseOrigin(f.Id,r.Source,r.GameplayHash,r.Turn,r.PhaseInstance,f.InstructionIndex,use.Id,a.ActionId,r.Target,cost.CardId,a.EffectiveKind,r.Slots[^1],_movementSequence);
  ReplaceRuntimeFrame(f.Id,f with{ZhangJi=r with{NativeUse=origin,Stage="equipment-issued"}});UpdateLifecycleCardUse(use.Id,u=>u with{ZhangJiNativeOrigin=origin});AdvanceEventRulesAndQueueFact(new ZhangJiNativeUseIssuedEvent(origin));
 }
 private bool ValidZhangJiNativeUse(CardUseFrame u)
 {
  if(u.ZhangJiNativeOrigin is not{}o || _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==o.ProgramFrameId) is not{ZhangJi:{}r}f || !ValidZhangJi(f) || r.NativeUse!=o ||
   u.Id!=o.UseId || u.CardId!=o.CardId || u.CardKind!=o.Kind || u.PhysicalCardIds is not[var id] || id!=o.CardId || u.Action is not{}a || a.ActionId!=o.ActionId || a.ActorSeat!=o.Actor || a.ProviderSeat!=o.Actor || a.RequesterSeat is not null || a.EffectiveKind!=o.Kind || a.PhysicalCards is not[var c] || c.CardId!=o.CardId || c.From!=CardLocation.DrawPile || a.ConversionChain is not[var source] || source!=o.Source)return false;
  return CompleteProgramEventHistory().OfType<ZhangJiNativeUseIssuedEvent>().Count(e=>e.Origin==o)==1 && _cardMovements.Count(m=>m.Sequence>o.Before && m.CardId==o.CardId && m.From==CardLocation.DrawPile && m.To==CardLocation.Processing)<=1;
 }
 private bool ZhangJiCardUseRidesOn(CardUseFrame u,ProgramSkillFrame f)=>u.ZhangJiNativeOrigin?.ProgramFrameId==f.Id && ValidZhangJiNativeUse(u);
 private bool ReturnZhangJiNativeUse(CardUseFrame u)
 {
  if(u.ZhangJiNativeOrigin is not{}o)return false;
  if(!ValidZhangJiNativeUse(u) || _cardMovements.Count(m=>m.Sequence>o.Before && m.CardId==o.CardId && m.From==CardLocation.DrawPile && m.To==CardLocation.Processing)!=1 || CompleteProgramEventHistory().OfType<ZhangJiNativeUseReturnedEvent>().Any(e=>e.Origin.UseId==u.Id))throw new InvalidOperationException("Tunjun native equipment returned a foreign or unpaid entity.");
  var f=_resolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.Id==o.ProgramFrameId);ReplaceRuntimeFrame(f.Id,f with{ZhangJi=f.ZhangJi! with{NativeReturned=true}});AdvanceEventRulesAndQueueFact(new ZhangJiNativeUseReturnedEvent(o,_movementSequence));return true;
 }
 private bool IsZhangJiMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>e is not null && IsZhangJiOp(e.Op) && f.ZhangJi is{Stage:"movement"} && p.SubjectSeat==f.OwnerSeat && p.BeforeCount==0 && p.CoverageResultBind is null;
 [ProgramContinuation(ProgramContinuationKind.Movement,23800)]
 private bool ReturnZhangJiMovement(ProgramSkillFrame f)
 {
  if(f.ZhangJi is null)return false;AssertZhangJi(f);if(f.PendingMovementContinuation is not{}p || !IsZhangJiMovement(f,ZhangJiPausedEffect(f),p))throw new InvalidOperationException("Foreign Lueming movement return.");
  if(GainGiftHasFinalGameEnd() || TryDrainFireTargetMovement(f))return true;FinishZhangJi(f);return true;
 }
 [ProgramContinuation(ProgramContinuationKind.Resume,23800)]
 private bool ResumeZhangJi(long id)
 {
  if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{ZhangJi:{}r}f || f.Id!=id)return false;AssertZhangJi(f);
  if(f.PendingMovementContinuation is not null){if(!TryDrainFireTargetMovement(f))ReturnZhangJiMovement(f);return true;}
  if(r.Stage=="outcome"){IssueZhangJiOutcome(f);return true;}
  if(r.Stage=="damage"){if(f.AttackAttempt is not null)return false;FinishZhangJi(f);return true;}
  if(r.Stage=="equipment-issued" && r.NativeReturned){ReplaceRuntimeTop(f=f with{ZhangJi=r with{NativeUse=null,NativeReturned=false,Stage="equipment"}});IssueZhangJiEquipment(f);return true;}
  if(r.Stage=="rank" && _pendingDecision is null){PublishZhangJi(f);return true;}return false;
 }
 private void FinishZhangJi(ProgramSkillFrame f){ClearPendingDecision();ReplaceRuntimeTop(f=f with{ZhangJi=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
 [ProgramCapability]
 private sealed class ZhangJiProgramCapability(GameEngine engine) : IZhangJiProgramHost
 {public SkillProgramStepOutcome BeginZhangJiOperation(ProgramSkillFrame f,SkillProgramEffectOp op)=>engine.BeginZhangJiOperation(f,op);}
}
