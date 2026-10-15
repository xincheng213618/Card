namespace CardGame.Core;
public sealed partial class GameEngine
{
 private static readonly PlayerMarkerKind[] FaluTokens=[PlayerMarkerKind.Ziwei,PlayerMarkerKind.Houtu,PlayerMarkerKind.Yuqing,PlayerMarkerKind.Gouchen];
 private const string ZhenyiGainReason="skill-program.zhenyi.three-categories";
 private static CardConversionSource ZhangQiYingSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
 private int FaluTokenCount(int owner)=>FaluTokens.Count(m=>_players[owner].Markers.GetValueOrDefault(m)>0);
 private CardLocation? FaluDiscardOwnedSource(CardMovementRecord movement)
 {
  if(movement.To!=CardLocation.DiscardPile)return null;
  var origin=GetProgramDiscardSource(movement);
  if(origin is{OwnerSeat:not null,Zone:CardZoneKind.Hand or CardZoneKind.Equipment})return origin;
  return movement.From is{OwnerSeat:not null,Zone:CardZoneKind.WoodenOxGrain}&&IsDiscardMovementReason(movement.Reason)?movement.From:null;
 }
 private void SetFaluToken(ProgramSkillFrame f,PlayerMarkerKind marker,int value,string reason)
 {
  var owner=_players[f.OwnerSeat];var before=owner.Markers.GetValueOrDefault(marker);if(value is <0 or >1||before is <0 or >1)throw new InvalidOperationException("Falu tokens are binary.");
  if(before==value)return;if(value==0)owner.Markers.Remove(marker);else owner.Markers[marker]=value;
  AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(f.Id,f.OwnerSeat,marker,value-before,value,f.OwnerSeat,"falu."+reason));
  AdvanceEventRulesAndQueueFact(new FaluTokenChangedEvent(f.Id,f.OwnerSeat,marker,before,value,reason));
 }
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 480)]
 private bool CanOfferZhangQiYing(ProgramTriggerCandidate candidate,SkillProgramTrigger trigger,ProgramSkillWindowContext context)
 {
  SkillProgramEffectOp? op=null;for(var i=0;i<trigger.Effects.Count;i++)if(ZhangQiYingComposition.Own(trigger.Effects[i].Op)){op=trigger.Effects[i].Op;break;}if(op is null)return true;var seat=candidate.OwnerSeat;
  return op switch{
   SkillProgramEffectOp.InitializeFaluSuitTokens=>context.Window==SkillProgramTriggerWindow.GameStarting&&!CompleteProgramEventHistory().OfType<FaluTokenChangedEvent>().Any(e=>e.OwnerSeat==seat&&e.Reason=="gameStarting"),
   SkillProgramEffectOp.GainFaluTokensAfterActualDiscard=>context.MovementBatch is{}b&&b.Movements.Any(m=>FaluDiscardOwnedSource(m)?.OwnerSeat==seat),
   SkillProgramEffectOp.ReorderTopByCurrentFaluTokens=>seat==_currentSeat&&FaluTokenCount(seat)>0,
   SkillProgramEffectOp.ChooseFinalJudgmentFiveByZiwei=>context.Window==SkillProgramTriggerWindow.JudgmentBeforeResult&&_players[seat].Markers.GetValueOrDefault(PlayerMarkerKind.Ziwei)>0&&context.Judgment is{}j&&ActiveJudgment is{}pending&&pending.Id==j.JudgmentFrameId,
   SkillProgramEffectOp.IncreaseActualSourceDamageByYuqing=>context.SourceSeat==seat&&context.Amount>0&&_players[seat].Markers.GetValueOrDefault(PlayerMarkerKind.Yuqing)>0,
   SkillProgramEffectOp.GainThreeDeckCategoriesAfterElementalDamage=>context.TargetSeat==seat&&context.Amount>0&&context.DamageFrameId is{}id&&CurrentDamageAttempt is{}actual&&actual.TargetSeat==seat&&actual.DamageWasApplied&&GetDamageNature(actual)!=DamageNature.Normal&&_players[seat].Markers.GetValueOrDefault(PlayerMarkerKind.Gouchen)>0,
   _=>false};
 }
 private SkillProgramStepOutcome ExecuteZhangQiYing(ProgramSkillFrame supplied,SkillProgramEffectOp op)
 {
  var f=GetActiveProgramFrame(supplied.Id);var c=f.WindowContext??throw new InvalidOperationException("Zhang Qi Ying requires an actual native window.");
  if(f.InstructionIndex!=1||f.TriggerId is null||f.ZhangQiYing is not null||!_players[f.OwnerSeat].IsAlive||!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)||!CanOfferZhangQiYing(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,0),GetProgramTrigger(f),c))throw new InvalidOperationException("Zhang Qi Ying lost its exact unpaid source and native window.");
  if(op==SkillProgramEffectOp.InitializeFaluSuitTokens){foreach(var marker in FaluTokens)SetFaluToken(f,marker,1,"gameStarting");return SkillProgramStepOutcome.Continue;}
  if(op==SkillProgramEffectOp.GainFaluTokensAfterActualDiscard)
  {
   var batch=c.MovementBatch!;foreach(var m in batch.Movements.Where(m=>FaluDiscardOwnedSource(m)?.OwnerSeat==f.OwnerSeat))
   {if(!_cardMovements.Contains(m))throw new InvalidOperationException("Falu needs an actual committed movement.");var suit=GetAdvancedCard(m.CardId).Suit;var marker=suit switch{Suit.Spade=>PlayerMarkerKind.Ziwei,Suit.Club=>PlayerMarkerKind.Houtu,Suit.Heart=>PlayerMarkerKind.Yuqing,Suit.Diamond=>PlayerMarkerKind.Gouchen,_=>throw new InvalidOperationException("Falu discard has no printed suit.")};SetFaluToken(f,marker,1,"discard");}return SkillProgramStepOutcome.Continue;
  }
  if(op==SkillProgramEffectOp.ReorderTopByCurrentFaluTokens)return BeginProgramTopReorder(f,FaluTokenCount(f.OwnerSeat),null);
  if(op==SkillProgramEffectOp.ChooseFinalJudgmentFiveByZiwei)
  {ReplaceRuntimeTop(f=f with{ZhangQiYing=new(ZhangQiYingSource(f),f.GameplayHash,c.ParentFrameId,f.InstructionIndex,"judgment-choice")});PublishZhenyiJudgment(f);return SkillProgramStepOutcome.AwaitChoice;}
  if(op==SkillProgramEffectOp.IncreaseActualSourceDamageByYuqing)
  {
   if(_resolutionStack[^2]is not BeforeDamageProgramWindowFrame w||w.Id!=c.ParentFrameId||w.SourceSeat!=f.OwnerSeat||w.Prevented||CurrentDamageAttempt is not{}attack||attack.SourceSeat!=w.SourceSeat||attack.TargetSeat!=w.TargetSeat)throw new InvalidOperationException("Yuqing needs the exact source's actual unprevented damage.");
   SetFaluToken(f,PlayerMarkerKind.Yuqing,0,"yuqing");attack.IncreaseFinalizedDamageAmount(1);ReplaceRuntimeFrame(w.Id,w with{Amount=checked(w.Amount+1)});AdvanceEventRulesAndQueueFact(new ZhenyiDamageIncreasedEvent(f.Id,w.Id,w.SourceSeat,w.TargetSeat,w.Amount,w.Amount+1));return SkillProgramStepOutcome.Continue;
  }
  if(op==SkillProgramEffectOp.GainThreeDeckCategoriesAfterElementalDamage)
  {
   _=EnsureDrawPile();var cards=new[]{SkillProgramCardCategory.Basic,SkillProgramCardCategory.Trick,SkillProgramCardCategory.Equipment}.Select(k=>_cardZones.CardsAt(CardLocation.DrawPile).LastOrDefault(card=>MatchesSkillProgramCardCategory(card.Kind,k))).Where(card=>card is not null).Select(card=>card!.Id).ToArray();
   var r=new ZhangQiYingReceipt(ZhangQiYingSource(f),f.GameplayHash,c.ParentFrameId,f.InstructionIndex,"gain-children",_movementSequence,_movementSequence){Cards=cards};ReplaceRuntimeTop(f=f with{ZhangQiYing=r,PendingMovementContinuation=new(f.OwnerSeat,0,null)});SetFaluToken(f,PlayerMarkerKind.Gouchen,0,"gouchen");
   if(_players[f.OwnerSeat].IsAlive&&_winner==Winner.None&&cards.Length>0)MoveProgramCardsFromMultipleSources(cards,CardLocation.Hand(f.OwnerSeat),new(ZhenyiGainReason));
   f=GetActiveProgramFrame(f.Id);r=f.ZhangQiYing!with{After=_movementSequence};ReplaceRuntimeTop(f with{ZhangQiYing=r});AdvanceEventRulesAndQueueFact(new ZhenyiDeckGainPaidEvent(f.Id,r.Source,r.GameplayHash,r.Before,r.After,cards.Length));return SkillProgramStepOutcome.AwaitChoice;
  }
  throw new InvalidOperationException("Unknown Zhang Qi Ying operation.");
 }
 private IReadOnlyList<PromptChoice> ZhenyiJudgmentChoices(ProgramSkillFrame f)=>Array.AsReadOnly(new[]{Suit.Spade,Suit.Heart}.Select(s=>new PromptChoice(new($"zhenyi.{f.Id}.{s}"),s==Suit.Spade?"黑桃5":"红桃5",[],[],new Dictionary<string,string>{{"program-action","zhenyi-judgment"},{"suit",s.ToString()}})).ToArray());
 private void PublishZhenyiJudgment(ProgramSkillFrame f)
 {
  ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});_pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"将实际判定结果改为黑桃5或红桃5",[],[],f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=false,Choices=ZhenyiJudgmentChoices(f)};_status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 [ProgramAiChoice(SkillProgramEffectOp.ChooseFinalJudgmentFiveByZiwei)] private PromptChoice ZhenyiAiJudgment(ProgramSkillFrame f)=>ZhenyiJudgmentChoices(f).First();
 [ProgramAnswer("zhenyi-judgment")] private void ResolveZhenyiJudgment(PromptChoice choice)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{ZhangQiYing:{Stage:"judgment-choice"}}f||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!AssistedChoicesEqual([choice],ZhenyiJudgmentChoices(f).Where(c=>c.Id==choice.Id).ToArray()))throw new InvalidOperationException("Zhenyi judgment lost its exact actual chooser.");
  AssertZhangQiYing(f);var j=f.WindowContext!.Judgment!;var pending=ActiveJudgment!;if(pending.Id!=j.JudgmentFrameId||GetJudgmentCard(pending)?.Id!=j.CardId||_players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.Ziwei)!=1)throw new InvalidOperationException("Zhenyi judgment source/result changed before payment.");
  var receipt=new ZhenyiJudgmentOverride(f.Id,pending.Id,ZhangQiYingSource(f),f.GameplayHash,j.CardId,Enum.Parse<Suit>(choice.Parameters["suit"]),5);ClearPendingDecision();SetFaluToken(f,PlayerMarkerKind.Ziwei,0,"ziwei");ReplaceRuntimeFrame(pending.Id,pending with{ZhenyiOverride=receipt,Suit=receipt.Suit});AdvanceEventRulesAndQueueFact(new ZhenyiJudgmentOverriddenEvent(receipt));ReplaceRuntimeTop(f=f with{ZhangQiYing=null});FinishProgramSkill(f,true);
 }
 [ProgramCapability]
 private sealed class ZhangQiYingProgramCapability(GameEngine engine) : IZhangQiYingProgramHost
 {public SkillProgramStepOutcome ExecuteZhangQiYing(ProgramSkillFrame f,SkillProgramEffectOp op)=>engine.ExecuteZhangQiYing(f,op);}
}
