namespace CardGame.Core;
public sealed partial class GameEngine
{
 private static CardConversionSource WangCanSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_wang_can_ol_WangCanPrograms.WangCanSource(f);
 private static CardLocation WangCanLease(int owner) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_wang_can_ol_WangCanPrograms.WangCanLease(owner);
 private static string WangCanReason(WangCanStage stage) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_wang_can_ol_WangCanPrograms.WangCanReason(stage);
 private SkillProgramStepOutcome ExecuteWangCan(SkillProgramEffectOp op,ProgramSkillFrame supplied)
 {
  var f=GetActiveProgramFrame(supplied.Id);var source=WangCanSource(f);
  if(f.WangCan is not null||f.InstructionIndex!=1||f.WindowContext is not{}w||!_players[f.OwnerSeat].IsAlive||!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId))throw new InvalidOperationException("Wang Can requires its actual unpaid native source and window.");
  var initial=op switch{SkillProgramEffectOp.ResolveSanwenActualGainDuplicates=>WangCanStage.SanwenDiscard,SkillProgramEffectOp.CollectQiaiLivingContributions=>WangCanStage.QiaiChoose,_=>WangCanStage.DenglouViewChildren};
  var r=new WangCanReceipt(source,f.GameplayHash,1,w.ParentFrameId,_turnNumber,_cardUseDebitPhaseInstanceId,initial,Sanwen:w.Sanwen);
  if(op==SkillProgramEffectOp.ResolveSanwenActualGainDuplicates&&(w.Window!=SkillProgramTriggerWindow.CardsGained||w.Sanwen is not{}q||q.OwnerSeat!=f.OwnerSeat||w.MovementBatch is not{}batch||!ValidSanwenProof(q,batch)||SanwenPayable(q).Count==0))throw new InvalidOperationException("Sanwen lost the frozen real gain batch or its original hand witnesses.");
  if(op==SkillProgramEffectOp.CollectQiaiLivingContributions&&(w.Window!=SkillProgramTriggerWindow.DyingEntering||ActiveDying is not{}dying||dying.VictimSeat!=f.OwnerSeat||_resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(entry=>entry.Id==w.ParentFrameId)?.ResumeDyingFrameId!=dying.Id))throw new InvalidOperationException("Qiai requires its actual own entering-dying frame.");
  if(op==SkillProgramEffectOp.ViewDenglouTopAndUseBasics&&(w.Window!=SkillProgramTriggerWindow.TurnEnding||f.OwnerSeat!=_currentSeat||GetHand(_players[f.OwnerSeat]).Count!=0))throw new InvalidOperationException("Denglou requires an actual own Ending with no hand.");
  ReplaceRuntimeTop(f=f with{WangCan=r});AdvanceEventRulesAndQueueFact(new WangCanStartedEvent(f.Id,source,f.GameplayHash,w.ParentFrameId,_turnNumber,op));
  if(op==SkillProgramEffectOp.ResolveSanwenActualGainDuplicates)
  {
   var ids=SanwenPayable(r.Sanwen!);var names=r.Sanwen!.Incoming.Where(i=>ids.Contains(i.CardId)).Select(i=>i.Name).ToHashSet();var witnesses=r.Sanwen.Witnesses.Where(i=>names.Contains(i.Name)&&_cardZones.GetLocation(i.CardId)==CardLocation.Hand(f.OwnerSeat)).Select(i=>i.CardId).ToArray();
   SetProgramCardSet(f.Id,"sanwen-witness",witnesses,SkillProgramCardSetVisibility.Public);AdvanceEventRulesAndQueueFact(new ProgramCardsRevealedEvent(f.Id,f.SkillId,GetProgramBindingId(f),f.OwnerSeat,"sanwen-witness",Array.AsReadOnly(witnesses.Select(id=>ToSnapshot(GetAdvancedCard(id))).ToArray())));f=GetActiveProgramFrame(f.Id);PayWangCanMove(f,ids,CardLocation.DiscardPile,WangCanStage.SanwenDiscard);return SkillProgramStepOutcome.AwaitChild;
  }
  if(op==SkillProgramEffectOp.CollectQiaiLivingContributions)
  {
   var seats=Enumerable.Range(1,_players.Count-1).Select(i=>(f.OwnerSeat+i)%_players.Count).Where(s=>_players[s].IsAlive).ToArray();ReplaceRuntimeTop(f=f with{WangCan=r with{Seats=seats,DyingFrameId=ActiveDying!.Id}});ContinueQiai(f);return SkillProgramStepOutcome.AwaitChoice;
  }
  BeginDenglouLease(f);return SkillProgramStepOutcome.AwaitChild;
 }
 private void PayWangCanMove(ProgramSkillFrame f,IReadOnlyList<int> ids,CardLocation destination,WangCanStage stage)
 {
  var r=f.WangCan!;var before=_movementSequence;r=r with{Stage=stage,Before=before,After=before,ActualCount=0,Entities=r.Lease is null?ids:r.Entities};ReplaceRuntimeTop(f=f with{WangCan=r,PendingMovementContinuation=new(f.OwnerSeat,0,null),Step=ResolutionFrameStep.ResolvingEffect});
   if(ids.Count>0){if(stage==WangCanStage.SanwenDiscard)MoveProgramLogicalDiscard(f,f.OwnerSeat,ids,new(WangCanReason(stage)));else MoveProgramCardsFromMultipleSources(ids,destination,new(WangCanReason(stage)));}f=GetActiveProgramFrame(f.Id);r=f.WangCan!with{After=_movementSequence,ActualCount=_cardMovements.Count(m=>m.Sequence>before&&m.Sequence<=_movementSequence&&m.Reason.Value==WangCanReason(stage))};ReplaceRuntimeTop(f=f with{WangCan=r});IssueWangCanSegment(f);AdvanceRuntimeProgram(f.Id);
 }
 private void IssueWangCanSegment(ProgramSkillFrame f){var r=f.WangCan!;AdvanceEventRulesAndQueueFact(new WangCanSegmentPaidEvent(f.Id,r.Source,r.GameplayHash,r.Stage,r.ActualTurn,r.Stage==WangCanStage.QiaiGift?r.Seats[r.Cursor]:f.OwnerSeat,r.Before,r.After,r.ActualCount));}
 private void PublishWangCan(ProgramSkillFrame f,int seat,IReadOnlyList<PromptChoice> choices,string text)
 {
  var def=_contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});_pendingDecision=new(DecisionKind.ProgramTrigger,seat,text,choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=true,Choices=choices,SkillPrompt=new(f.SkillId,def.Name,def.Name,def.Description)};_status=_players[seat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 private IReadOnlyList<PromptChoice> WangCanChoices(ProgramSkillFrame f)
 {
  var r=f.WangCan!;if(r.Stage==WangCanStage.QiaiChoose)return QiaiChoices(f);
  if(r.Stage==WangCanStage.DenglouView)return [new(new($"denglou.{f.Id}.view"),"确认观看并获得非基本牌",r.Remaining,[],new Dictionary<string,string>{{"program-action","wangcan"},{"branch","view"}})];
  if(r.Stage!=WangCanStage.DenglouChoose)return [];
  return Array.AsReadOnly(BuildDenglouNativeBasicUseOptions(f).Select((a,i)=>new PromptChoice(new($"denglou.{f.Id}.use-{i}"),a.Description,[a.CardId!.Value],a.TargetSeats,new Dictionary<string,string>{{"program-action","wangcan"},{"branch","use"},{"option",i.ToString()}})).ToArray());
 }
 [ProgramAiChoice(SkillProgramEffectOp.CollectQiaiLivingContributions,SkillProgramEffectOp.ViewDenglouTopAndUseBasics)]
 private PromptChoice WangCanAiChoice(ProgramSkillFrame f)=>WangCanChoices(f).First();
 [ProgramAnswer("wangcan")]
 private void ResolveWangCanChoice(PromptChoice choice)
 {
  var f=(ProgramSkillFrame)_resolutionStack.Last();RequireIssuedWangCan(f);var r=f.WangCan!;var seat=r.Stage==WangCanStage.QiaiChoose?r.Seats[r.Cursor]:f.OwnerSeat;
  if(_pendingDecision?.PlayerSeat!=seat||!WangCanChoices(f).Any(c=>AssistedChoicesEqual([choice],[c])))throw new InvalidOperationException("Wang Can choice changed the private actual payer, lease entity or legal native use.");ClearPendingDecision();
  if(r.Stage==WangCanStage.QiaiChoose)
  {var id=choice.Cards.Single();if(!_players[seat].IsAlive||!_players[f.OwnerSeat].IsAlive||_cardZones.GetLocation(id).OwnerSeat!=seat||_cardZones.GetLocation(id).Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment))throw new InvalidOperationException("Qiai payer no longer holds its actual chosen transferable entity.");PayWangCanMove(f,[id],CardLocation.Hand(f.OwnerSeat),WangCanStage.QiaiGift);return;}
  if(r.Stage==WangCanStage.DenglouView)
  {var nonbasic=r.Remaining.Where(id=>_cardZones.GetLocation(id)==r.Lease&&GetProgramCardCategory(GetAdvancedCard(id).Kind)!=SkillProgramCardCategory.Basic).ToArray();ReplaceRuntimeTop(f=f with{WangCan=r with{Remaining=r.Remaining.Except(nonbasic).ToArray()}});PayWangCanMove(f,nonbasic,CardLocation.Hand(f.OwnerSeat),WangCanStage.DenglouGain);return;}
  IssueDenglouNativeUse(f,BuildDenglouNativeBasicUseOptions(f)[int.Parse(choice.Parameters["option"])]);
 }
 private bool DrainWangCan(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id);
 [ProgramContinuation(ProgramContinuationKind.Resume,24600)] private bool ResumeWangCan(long id)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{WangCan:{}r}f||f.Id!=id||r.Stage is WangCanStage.QiaiChoose or WangCanStage.DenglouView or WangCanStage.DenglouChoose or WangCanStage.DenglouNative)return false;RequireIssuedWangCan(f);if(DrainWangCan(f))return true;
  if(r.Stage==WangCanStage.SanwenDiscard){PaySanwenDraw(f);return true;}
  if(r.Stage==WangCanStage.QiaiGift){ReplaceRuntimeTop(f=f with{WangCan=r with{Cursor=r.Cursor+1,Stage=WangCanStage.QiaiChoose},PendingMovementContinuation=null});ContinueQiai(f);return true;}
  if(r.Stage==WangCanStage.DenglouViewChildren&&_players[f.OwnerSeat].IsAlive&&_winner==Winner.None){ReplaceRuntimeTop(f=f with{WangCan=r with{Stage=WangCanStage.DenglouView},PendingMovementContinuation=null});PublishWangCan(f,f.OwnerSeat,WangCanChoices(f),"登楼：私下观看实际牌堆顶的牌。");return true;}
  if(r.Stage is WangCanStage.DenglouGain or WangCanStage.Completed){ContinueDenglou(f);return true;}
  if(r.Stage==WangCanStage.DenglouViewChildren){ContinueDenglou(f);return true;}
  FinishWangCan(f);return true;
 }
 [ProgramContinuation(ProgramContinuationKind.Movement,24600)] private bool ReturnWangCanMovement(ProgramSkillFrame f)=>f.WangCan is not null&&ResumeWangCan(f.Id);
 private void FinishWangCan(ProgramSkillFrame f){var r=f.WangCan!;if(CompleteProgramEventHistory().OfType<WangCanCompletedEvent>().Any(e=>e.FrameId==f.Id))throw new InvalidOperationException("Wang Can completed twice.");AdvanceEventRulesAndQueueFact(new WangCanCompletedEvent(f.Id,r.Source,r.GameplayHash));ReplaceRuntimeTop(f=f with{WangCan=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
 [ProgramCapability]
 private sealed class WangCanProgramCapability(GameEngine engine) : IWangCanProgramHost
 {public SkillProgramStepOutcome ExecuteWangCan(SkillProgramEffectOp op,ProgramSkillFrame f)=>engine.ExecuteWangCan(op,f);}
}
