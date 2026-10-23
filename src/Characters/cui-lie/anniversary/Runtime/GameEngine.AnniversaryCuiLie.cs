using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
 private const string CuiLieAction="anniversary-cui-lie",CuiLieGive="skill-program.anniversary:zijue.give",CuiLieDraw="skill-program.anniversary:zijue.draw",CuiLieClaim="skill-program.anniversary:chibi.claim",CuiLieContest="anniversary-cui-lie-contest";
 private static CardConversionSource CuiLieSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
 private bool CuiLieDisabled(int owner)=>ProgramEventHistory<AnniversaryCuiLieDisabledEvent>().Any(d=>d.Source.OwnerSeat==owner&&d.Turn==_turnNumber&&!ProgramEventHistory<TurnEndedEvent>().Any(e=>e.TurnNumber==d.Turn));
 private bool IsAnniversaryCuiLieGrantQualified(CharacterState owner,SkillGrant grant)=>grant.SkillId!=AnniversaryCuiLieComposition.Chibi||!CuiLieDisabled(owner.Seat);
 private (long Inner,int Issued,int Ended) _cuiLieQualificationInputs;
 private long _cuiLieQualificationStamp;
 private long CaptureAnniversaryCuiLieQualificationStamp(long underlying)
 {
  var issued=ProgramEventHistory<AnniversaryCuiLieDisabledEvent>().Count;if(issued==0)return underlying;
  var input=(underlying,issued,ProgramEventHistory<TurnEndedEvent>().Count);
  if(_cuiLieQualificationStamp==0||input!=_cuiLieQualificationInputs){_cuiLieQualificationInputs=input;_cuiLieQualificationStamp++;}
  return long.MinValue/128+_cuiLieQualificationStamp;
 }
 [ProgramCapability]private sealed class AnniversaryCuiLieHost(GameEngine e):IAnniversaryCuiLieHost{public SkillProgramStepOutcome Begin(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginCuiLie(f,op);}
 private AnniversaryCuiLieLeaseEvent[] CuiLieLeases(int owner)=>ProgramEventHistory<AnniversaryCuiLieLeaseEvent>().Where(l=>l.Source.OwnerSeat==owner&&l.Amount is>=2 and<=4&&l.Source.SkillId==AnniversaryCuiLieComposition.Zijue&&l.Source.BindingId=="declare-number"&&
  _contentRegistry.GetSkill(l.Source.SkillId).Program?.GameplayHash==l.Hash&&!ProgramEventHistory<TurnStartedEvent>().Any(t=>t.ActorSeat==owner&&t.TurnNumber>l.Turn)&&
  ProgramEventHistory<AnniversaryCuiLieGiftEvent>().Count(g=>g.FrameId==l.FrameId&&g.Count==l.Amount)==1).ToArray();
 private int AnniversaryCuiLiePindianBonus(int owner)=>CuiLieLeases(owner).Sum(l=>l.Amount);
 [ProgramDistanceRule(ProgramDistanceStage.IncomingContributions,46900)]private IEnumerable<RuleQueryContribution> CuiLieIncoming(int target)
 {foreach(var lease in CuiLieLeases(target))yield return new FiniteRuleQueryContribution($"issued:{lease.FrameId}:anniversary:zijue:incoming",SkillRuleOperation.Add,lease.Amount);}
 private IEnumerable<ProgramTriggerCandidate> AnniversaryCuiLieIssuedPindianCandidates(IReadOnlyList<PindianRevealedCard> cards)=>cards.Select(c=>c.Seat).Distinct().Where(s=>_players[s].IsAlive).SelectMany(s=>CuiLieLeases(s).DistinctBy(l=>l.Source.SkillInstanceId)
  .Select(l=>new ProgramTriggerCandidate(s,AnniversaryCuiLieComposition.Zijue,"paid-pindian-draw",l.Source.SkillInstanceId,l.Hash,0)));
 [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate,46900)]private bool IsIssuedCuiLieDraw(ProgramTriggerCandidate c,ProgramSkillWindowContext w)=>c.SkillId==AnniversaryCuiLieComposition.Zijue&&c.BindingId=="paid-pindian-draw"&&w.Window==SkillProgramTriggerWindow.PindianCardsRevealed&&
  _resolutionStack.OfType<PindianRevealWindowFrame>().SingleOrDefault(p=>p.Id==w.ParentFrameId) is{}p&&p.CandidateIndex>=0&&p.CandidateIndex<p.Candidates.Count&&p.Candidates[p.CandidateIndex]==c&&p.Cards.Any(card=>card.Seat==c.OwnerSeat)&&CuiLieLeases(c.OwnerSeat).Any(l=>l.Source.SkillInstanceId==c.SkillInstanceId&&l.Hash==c.GameplayHash);
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,46900,SkillProgramEffectOp.ResolveAnniversaryCuiLieChibi,SkillProgramEffectOp.ResolveAnniversaryCuiLieZijuePindianDraw)]private bool CanOfferCuiLie(ProgramTriggerLegalityContext c)=>c.SkillId==AnniversaryCuiLieComposition.Chibi?
  CuiLieUse(c.WindowContext) is{}u&&CuiLieContestEligible(c.OwnerSeat,u):CuiLieLeases(c.OwnerSeat).Any(l=>l.Source.SkillInstanceId==c.Candidate.SkillInstanceId);
 private CardUseFrame? CuiLieUse(ProgramSkillWindowContext? w)=>w is{Window:SkillProgramTriggerWindow.CardUseTargetsFinalized,CardUse:{}id}&&LifecycleCardUse(id.ParentCardUseFrameId) is{Action:{}a}u&&a.ActionId==id.CardActionId?u:null;
 private bool CuiLieContestEligible(int owner,CardUseFrame use)=>use.Action is{Type:CardActionType.Use}a&&a.ActorSeat!=owner&&_players[a.ActorSeat].IsAlive&&_players[owner].IsAlive&&
  !CuiLieDisabled(owner)&&HasWuLuKangPindianInput(_players[owner])&&HasWuLuKangPindianInput(_players[a.ActorSeat])&&CanBePindianTarget(owner,a.ActorSeat)&&
  OverflowPrimaryTargets(use.CardKind,a.EffectiveDesignatedTargetSeats).Any(t=>IsValidPlayerSeat(t)&&_players[t].IsAlive&&GetCombatDistance(a.ActorSeat,t)>1);
 private SkillProgramStepOutcome BeginCuiLie(ProgramSkillFrame supplied,SkillProgramEffectOp op)
 {
  var f=GetActiveProgramFrame(supplied.Id);if(f.AnniversaryCuiLie is not null||f.InstructionIndex!=1||!CuiLieOriginal(f,op))throw new InvalidOperationException("Cui Lie requires his original phase, paid-lease reveal or far-target native candidate.");
  var actor=op==SkillProgramEffectOp.ResolveAnniversaryCuiLieZijue?f.SelectedTargetSeats.Single():op==SkillProgramEffectOp.ResolveAnniversaryCuiLieChibi?CuiLieUse(f.WindowContext)!.SourceSeat:f.OwnerSeat;
  var r=new AnniversaryCuiLieReceipt(CuiLieSource(f),f.GameplayHash,op,f.WindowContext?.ParentFrameId??0,_turnNumber,_cardUseDebitPhaseInstanceId,actor,op==SkillProgramEffectOp.ResolveAnniversaryCuiLieZijue?AnniversaryCuiLieStage.Number:op==SkillProgramEffectOp.ResolveAnniversaryCuiLieChibi?AnniversaryCuiLieStage.Pindian:AnniversaryCuiLieStage.Drawing);
  if(op==SkillProgramEffectOp.ResolveAnniversaryCuiLieChibi){var use=CuiLieUse(f.WindowContext)!;r=r with{UseId=use.Id,ActionId=use.Action!.ActionId,PindianId=_resolutionSequence+1,Before=_movementSequence,After=_movementSequence};}
  if(op==SkillProgramEffectOp.ResolveAnniversaryCuiLieZijuePindianDraw)r=r with{Number=1,RewardLeases=CuiLieLeases(f.OwnerSeat).Where(l=>l.Source.SkillInstanceId==f.SkillInstanceId).Select(l=>l.FrameId).ToArray()};
  ReplaceRuntimeTop(f=f with{AnniversaryCuiLie=r});AdvanceEventRulesAndQueueFact(new AnniversaryCuiLieStartedEvent(f.Id,r.Source,r.Hash,op,r.ParentId,r.Turn,r.Phase,actor));
  if(op==SkillProgramEffectOp.ResolveAnniversaryCuiLieChibi)
  {AdvanceEventRulesAndQueueFact(new AnniversaryCuiLiePindianIssuedEvent(f.Id,r.PindianId,actor));var skill=_contentRegistry.GetSkill(f.SkillId);BeginSharedPindian(f.Id,new(f.SkillId,skill.Name,skill.Name,skill.Description),f.OwnerSeat,actor,programResultBind:CuiLieContest);return SkillProgramStepOutcome.AwaitChild;}
  if(op==SkillProgramEffectOp.ResolveAnniversaryCuiLieZijuePindianDraw){IssueCuiLieDraw(f);return SkillProgramStepOutcome.AwaitChild;}
  PublishCuiLie(f);return SkillProgramStepOutcome.AwaitChoice;
 }
 private int CuiLieChooser(ProgramSkillFrame f)=>f.AnniversaryCuiLie!.Stage==AnniversaryCuiLieStage.Number?f.AnniversaryCuiLie.Actor:f.OwnerSeat;
 private PromptChoice CuiLieChoice(ProgramSkillFrame f,string verb,string value,string text,IReadOnlyList<int>? cards=null,IReadOnlyList<int>? targets=null)=>new(new($"cui-lie.{f.Id}.{verb}.{value}"),text,cards??[],targets??[],new Dictionary<string,string>{{"program-action",CuiLieAction},{"verb",verb},{"value",value},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)}});
 private Card[] CuiLieTransferable(ProgramSkillFrame f)=>BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,f.OwnerSeat,[CardZoneKind.Hand,CardZoneKind.Equipment],OwnedCardMoveIntent.Transfer).SelectMany(c=>c.Cards).Distinct().Select(GetAdvancedCard).OrderBy(c=>c.Id).ToArray();
 private IReadOnlyList<PromptChoice> CuiLieChoices(ProgramSkillFrame f)
 {
  var r=f.AnniversaryCuiLie!;
  if(r.Stage==AnniversaryCuiLieStage.Number)return Array.AsReadOnly(Enumerable.Range(2,3).Select(n=>CuiLieChoice(f,"number",n.ToString(CultureInfo.InvariantCulture),"声明 "+n)).ToArray());
  if(r.Stage==AnniversaryCuiLieStage.Payment)
  {
   var choices=new List<PromptChoice>();if(CuiLieTransferable(f).Length>=r.Number)choices.AddRange(CuiLieTransferable(f).Where(c=>!r.Cards.Contains(c.Id)).Select(c=>CuiLieChoice(f,"card",c.Id.ToString(CultureInfo.InvariantCulture),"交出【"+c.DisplayName+"】",[c.Id])));
   choices.Add(CuiLieChoice(f,"draw","done","不交牌，摸声明数量的牌"));return Array.AsReadOnly(choices.ToArray());
  }
  if(r.Stage==AnniversaryCuiLieStage.BorrowedVictim)return Array.AsReadOnly(_players.Where(p=>IsLegalBorrowedSwordSlashTarget(_players[f.OwnerSeat],p)).Select(p=>CuiLieChoice(f,"victim",p.Seat.ToString(CultureInfo.InvariantCulture),"追加借刀目标",targets:[f.OwnerSeat,p.Seat])).ToArray());
  return [];
 }
 private void PublishCuiLie(ProgramSkillFrame f)
 {
  var choices=CuiLieChoices(f);if(choices.Count==0){FinishCuiLie(f);return;}
  ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.AwaitingResponse});var seat=CuiLieChooser(f);var skill=_contentRegistry.GetSkill(f.SkillId);
  _pendingDecision=new(DecisionKind.ProgramTrigger,seat,rPrompt(f),Array.AsReadOnly(choices.SelectMany(c=>c.Cards).Distinct().ToArray()),Array.AsReadOnly(choices.SelectMany(c=>c.Targets).Distinct().ToArray()),f.OwnerSeat)
   {PromptId=CreatePromptId(),IsPrivate=true,TargetSeat=seat,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
  _status=_players[seat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 private static string rPrompt(ProgramSkillFrame f)=>f.AnniversaryCuiLie!.Stage==AnniversaryCuiLieStage.Number?"赀爵：由所选角色声明2、3或4。":f.AnniversaryCuiLie.Stage==AnniversaryCuiLieStage.Payment?$"赀爵：交出{f.AnniversaryCuiLie.Number}张牌，已选{f.AnniversaryCuiLie.Cards.Count}张；或不交牌摸牌。":"斥避：选择完整借刀追加目标。";
 [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryCuiLieZijue,SkillProgramEffectOp.ResolveAnniversaryCuiLieChibi)]private PromptChoice CuiLieAi(PendingDecision p)=>p.Choices.First();
 [ProgramAnswer(CuiLieAction)]private void AnswerCuiLie(PromptChoice answer)
 {
  if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{AnniversaryCuiLie:{}r}f||!ValidCuiLie(f)||_pendingDecision?.PlayerSeat!=CuiLieChooser(f)||!AssistedChoicesEqual([answer],CuiLieChoices(f).Where(c=>c.Id==answer.Id).ToArray()))throw new InvalidOperationException("Cui Lie rejects another participant, private cost or native target.");
  ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
  switch(answer.Parameters["verb"])
  {
   case "number":var n=int.Parse(answer.Parameters["value"],CultureInfo.InvariantCulture);ReplaceRuntimeTop(f=f with{AnniversaryCuiLie=r with{Number=n,Stage=AnniversaryCuiLieStage.Payment}});AdvanceEventRulesAndQueueFact(new AnniversaryCuiLieNumberEvent(f.Id,r.Actor,n));PublishCuiLie(f);return;
   case "card":var ids=r.Cards.Append(answer.Cards.Single()).ToArray();ReplaceRuntimeTop(f=f with{AnniversaryCuiLie=r=r with{Cards=ids}});if(ids.Length<r.Number){PublishCuiLie(f);return;}GiveCuiLie(f);return;
   case "draw":ReplaceRuntimeTop(f=f with{AnniversaryCuiLie=r with{Cards=[],Stage=AnniversaryCuiLieStage.Drawing}});IssueCuiLieDraw(f);return;
   case "victim":ApplyCuiLieMutation(f,answer.Targets);return;
  }
  throw new InvalidOperationException("Unknown Cui Lie native selection.");
 }
 private ProgramSkillFrame CuiLieChildren(ProgramSkillFrame f,AnniversaryCuiLieStage stage)
 {ReplaceRuntimeTop(f=f with{AnniversaryCuiLie=f.AnniversaryCuiLie! with{Stage=stage,Before=_movementSequence,After=_movementSequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});return f;}
 private void GiveCuiLie(ProgramSkillFrame f)
 {
  var r=f.AnniversaryCuiLie!;if(r.Cards.Count!=r.Number||!r.Cards.All(id=>CuiLieTransferable(f).Any(c=>c.Id==id)))throw new InvalidOperationException("Zijue lost its exact dynamically transferable HE payment.");
  f=CuiLieChildren(f,AnniversaryCuiLieStage.GiveChildren);r=f.AnniversaryCuiLie!;var native=NativeGiveCompletionRecords(f.Id,f.OwnerSeat,r.Actor,r.Cards,r.Before,null);
  MoveProgramCardsFromMultipleSources(r.Cards,CardLocation.Hand(r.Actor),new(CuiLieGive),(batch,moves)=>{native?.Invoke(batch,moves);var current=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(current with{AnniversaryCuiLie=current.AnniversaryCuiLie! with{Moves=moves,After=_movementSequence}});AdvanceEventRulesAndQueueFact(new AnniversaryCuiLieGiftEvent(f.Id,moves.Count,r.Before,_movementSequence));});ContinueCuiLie(f.Id);
 }
 private void IssueCuiLieDraw(ProgramSkillFrame f)
 {ReplaceRuntimeTop(f=f with{AnniversaryCuiLie=f.AnniversaryCuiLie! with{Stage=AnniversaryCuiLieStage.Drawing,Draw=null,Before=_movementSequence,After=_movementSequence}});BeginOperationProgramDraw(f,f.OwnerSeat,f.AnniversaryCuiLie!.Number,new(CuiLieDraw));}
 [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveAnniversaryCuiLieZijue,SkillProgramEffectOp.ResolveAnniversaryCuiLieZijuePindianDraw)]private void ReturnCuiLieDraw(ProgramSkillFrame supplied,NativeDrawOutcome draw)
 {
  var f=GetActiveProgramFrame(supplied.Id);var r=f.AnniversaryCuiLie!;
  if(!ValidCuiLie(f)||r.Stage!=AnniversaryCuiLieStage.Drawing||draw.RecipientSeat!=f.OwnerSeat||draw.RequestedCount!=r.Number||draw.SequenceBefore!=r.Before)throw new InvalidOperationException("Zijue lost its once-issued original native draw.");
  ReplaceRuntimeTop(f=f with{AnniversaryCuiLie=r with{Stage=AnniversaryCuiLieStage.DrawChildren,Draw=draw,After=draw.SequenceAfter},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  AdvanceEventRulesAndQueueFact(new AnniversaryCuiLieDrawReturnedEvent(f.Id,f.OwnerSeat,r.Number,draw.ActualDrawIds.Count,draw.SequenceBefore,draw.SequenceAfter));ContinueCuiLie(f.Id);
 }
 private void ContinueCuiLie(long id)
 {
  while(_pendingDecision is null&&_resolutionStack.LastOrDefault() is ProgramSkillFrame{AnniversaryCuiLie:{}r}f&&f.Id==id)
  {
   if(!ValidCuiLie(f))throw new InvalidOperationException("Cui Lie lost his original paid frame or native return.");
   if(r.Stage==AnniversaryCuiLieStage.Pindian)
   {
    var result=ProgramEventHistory<PindianResultDeterminedEvent>().Single(e=>e.FrameId==r.PindianId&&e.SkillId==f.SkillId).Result;
    ReplaceRuntimeTop(f=f with{AnniversaryCuiLie=r with{Stage=AnniversaryCuiLieStage.PindianChildren,After=_movementSequence,Result=result},PendingMovementContinuation=new(f.OwnerSeat,0,null)});AdvanceEventRulesAndQueueFact(new AnniversaryCuiLiePindianReturnedEvent(f.Id,r.PindianId,result.SourceRank,result.OpponentRank,result.SourceWon));continue;
   }
   if(r.Stage is AnniversaryCuiLieStage.Number or AnniversaryCuiLieStage.Payment or AnniversaryCuiLieStage.BorrowedVictim){PublishCuiLie(f);return;}
   if(r.Stage is AnniversaryCuiLieStage.Drawing or AnniversaryCuiLieStage.Completed)return;
   if(TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginAdvancedSkillsChanged(id)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(id))return;
   f=GetActiveProgramFrame(id);r=f.AnniversaryCuiLie!;ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null});
   if(r.Stage==AnniversaryCuiLieStage.GiveChildren)
   {
    f=CuiLieChildren(f,AnniversaryCuiLieStage.RecoveryChildren);RecoverProgramTargetTo(f.Id,f.OwnerSeat,f.OwnerSeat,SkillProgramNumberExpression.IntegerConstant,_players[f.OwnerSeat].Hp+1,true);continue;
   }
   if(r.Stage==AnniversaryCuiLieStage.RecoveryChildren)
   {AdvanceEventRulesAndQueueFact(new AnniversaryCuiLieLeaseEvent(f.Id,r.Source,r.Hash,r.Turn,r.Number));FinishCuiLie(f);return;}
   if(r.Stage==AnniversaryCuiLieStage.PindianChildren){SettleCuiLieContest(f);return;}
   if(r.Stage==AnniversaryCuiLieStage.DrawChildren&&r.Operation==SkillProgramEffectOp.ResolveAnniversaryCuiLieZijuePindianDraw&&r.Cursor+1<r.RewardLeases.Count)
   {ReplaceRuntimeTop(f=f with{AnniversaryCuiLie=r with{Cursor=r.Cursor+1}});IssueCuiLieDraw(f);return;}
   FinishCuiLie(f);return;
  }
 }
 private void FinishCuiLie(ProgramSkillFrame f){ClearPendingDecision();ReplaceRuntimeTop(f=f with{AnniversaryCuiLie=f.AnniversaryCuiLie! with{Stage=AnniversaryCuiLieStage.Completed},PendingMovementContinuation=null});AdvanceEventRulesAndQueueFact(new AnniversaryCuiLieCompletedEvent(f.Id));AdvanceRuntimeProgram(f.Id);}
 [ProgramContinuation(ProgramContinuationKind.Resume,46900)]private bool ResumeCuiLie(long id){if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{AnniversaryCuiLie:{Stage:not AnniversaryCuiLieStage.Completed}}f||f.Id!=id)return false;ContinueCuiLie(id);return true;}
 [ProgramContinuation(ProgramContinuationKind.Movement,46900)]private bool ReturnCuiLieMovement(ProgramSkillFrame f)=>f.AnniversaryCuiLie is not null&&ResumeCuiLie(f.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,46900)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,46900)]private bool KeepCuiLie(ProgramSkillFrame f)=>f.AnniversaryCuiLie is{}r?ValidCuiLie(f)&&r.Stage is not(AnniversaryCuiLieStage.Number or AnniversaryCuiLieStage.Payment):f.SkillId==AnniversaryCuiLieComposition.Zijue&&f.TriggerId=="paid-pindian-draw"&&CuiLieLeases(f.OwnerSeat).Any(l=>l.Source.SkillInstanceId==f.SkillInstanceId);
}
