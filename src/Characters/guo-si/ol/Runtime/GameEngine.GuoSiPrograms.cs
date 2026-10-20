namespace CardGame.Core;
public sealed partial class GameEngine
{
 private const string TanbeiGainReason="skill-program.tanbei.gain";
 private static CardConversionSource GuoSiSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
 private SkillProgramStepOutcome ExecuteGuoSi(SkillProgramEffectOp op,ProgramSkillFrame supplied)
 {
  var f=GetActiveProgramFrame(supplied.Id);if(f.GuoSi is not null||f.InstructionIndex!=1||_phase!=TurnPhase.Play||_currentSeat!=f.OwnerSeat||!_players[f.OwnerSeat].IsAlive||!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId))throw new InvalidOperationException("Guo Si requires the unpaid actual own Play source.");
  if(op==SkillProgramEffectOp.ChooseTanbeiTargetPact)
  {
   if(f.TriggerId is not null||f.SelectedTargetSeats is not[var target]||target==f.OwnerSeat||!_players[target].IsAlive)throw new InvalidOperationException("Tanbei requires exactly one other living target.");
   ReplaceRuntimeTop(f=f with{GuoSi=new(GuoSiSource(f),f.GameplayHash,f.InstructionIndex,_turnNumber,_cardUseDebitPhaseInstanceId,target,GuoSiStage.TargetChoice)});PublishGuoSiChoice(f);return SkillProgramStepOutcome.AwaitChoice;
  }
  if(f.WindowContext?.CardUse is not{}i||LifecycleCardUse(i.ParentCardUseFrameId)is not{GuoSiPair:{}pair,Action:{}a}||a.ActionId!=i.CardActionId||SidaoActions(f.OwnerSeat,pair).Count==0)throw new InvalidOperationException("Sidao requires its frozen completed native pair.");
  ReplaceRuntimeTop(f=f with{GuoSi=new(GuoSiSource(f),f.GameplayHash,f.InstructionIndex,_turnNumber,_cardUseDebitPhaseInstanceId,-1,GuoSiStage.SidaoChoice,pair)});PublishGuoSiChoice(f);return SkillProgramStepOutcome.AwaitChoice;
 }
 private IReadOnlyList<PromptChoice> GuoSiChoices(ProgramSkillFrame f)
 {
  var r=f.GuoSi!;
  if(r.Stage==GuoSiStage.TargetChoice)return Array.AsReadOnly(Enumerable.Range(0,2).Select(b=>new PromptChoice(new($"tanbei.{f.Id}.{b}"),b==0?"令其随机获得你区域里的一张牌；其本回合不能对你使用牌":"其本回合对你使用牌无距离和次数限制",[],[],new Dictionary<string,string>{{"program-action","guo-si"},{"option",b.ToString()}})).ToArray());
  if(r.Stage!=GuoSiStage.SidaoChoice||r.Pair is null)return [];
  var choices=SidaoActions(f.OwnerSeat,r.Pair).Select((a,index)=>new PromptChoice(new($"sidao.{f.Id}.{index}"),a.Description,[a.CardId!.Value],a.TargetSeats,new Dictionary<string,string>{{"program-action","guo-si"},{"option",index.ToString()}})).ToList();
  choices.Add(new(new($"sidao.{f.Id}.skip"),"不使用",[],[],new Dictionary<string,string>{{"program-action","guo-si"},{"option","skip"}}));return Array.AsReadOnly(choices.ToArray());
 }
 private void PublishGuoSiChoice(ProgramSkillFrame f)
 {
  var r=f.GuoSi!;var actor=r.Stage==GuoSiStage.TargetChoice?r.Target:f.OwnerSeat;var choices=GuoSiChoices(f);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});
  _pendingDecision=new(DecisionKind.ProgramTrigger,actor,r.Stage==GuoSiStage.TargetChoice?"请选择贪狈的效果":"可将一张手牌当顺手牵羊对连续用牌的共同目标使用",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),actor){PromptId=CreatePromptId(),IsPrivate=true,Choices=choices};_status=_players[actor].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 [ProgramAiChoice(SkillProgramEffectOp.ChooseTanbeiTargetPact, SkillProgramEffectOp.OfferSidaoCompletedPairSnatch)]
 private PromptChoice GuoSiAiChoice(ProgramSkillFrame f)=>GuoSiChoices(f).First();
 [ProgramAnswer("guo-si")]
 private void ResolveGuoSiChoice(PromptChoice choice)
 {
  var f=(ProgramSkillFrame)_resolutionStack.Last();RequireIssuedGuoSi(f);var r=f.GuoSi!;var actor=r.Stage==GuoSiStage.TargetChoice?r.Target:f.OwnerSeat;
  if(_pendingDecision?.PlayerSeat!=actor||!AssistedChoicesEqual([choice],GuoSiChoices(f).Where(c=>c.Id==choice.Id).ToArray()))throw new InvalidOperationException("Guo Si choice lost actual chooser, material or target authority.");ClearPendingDecision();
  if(r.Stage==GuoSiStage.SidaoChoice)
  {
   if(choice.Parameters["option"]=="skip"){FinishGuoSi(f);return;}var action=SidaoActions(f.OwnerSeat,r.Pair!)[int.Parse(choice.Parameters["option"])];IssueSidao(f,action);return;
  }
  var branch=int.Parse(choice.Parameters["option"]);var before=_movementSequence;var randomBefore=_random.State;var cards=GetHand(_players[r.Target]).Concat(GetEquipment(_players[r.Target])).Concat(GetJudgment(_players[r.Target])).OrderBy(c=>c.Id).ToArray();int? id=branch==0&&cards.Length>0?cards[_random.Next(cards.Length)].Id:null;var from=id is{}entity?_cardZones.GetLocation(entity):(CardLocation?)null;
  var pact=new GuoSiTurnPact(f.Id,r.Source,r.GameplayHash,r.Turn,_currentSeat,r.Phase,f.OwnerSeat,r.Target,branch);_players[f.OwnerSeat].GuoSiPacts=Array.AsReadOnly((_players[f.OwnerSeat].GuoSiPacts??[]).Append(pact).ToArray());
  r=r with{Branch=branch,Stage=GuoSiStage.MovementChildren,Before=before,After=before,SelectedId=id,SelectedFrom=from,RandomBefore=randomBefore,RandomAfter=_random.State};ReplaceRuntimeTop(f=f with{GuoSi=r,Step=ResolutionFrameStep.ResolvingEffect,PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  if(id is{}paid)MoveProgramCardsFromMultipleSources([paid],CardLocation.Hand(f.OwnerSeat),new(TanbeiGainReason));f=GetActiveProgramFrame(f.Id);r=f.GuoSi!with{After=_movementSequence};ReplaceRuntimeTop(f with{GuoSi=r});AdvanceEventRulesAndQueueFact(new GuoSiPactPaidEvent(pact,before,r.After,id.HasValue?1:0,randomBefore,r.RandomAfter));AdvanceRuntimeProgram(f.Id);
 }
 private void FinishGuoSi(ProgramSkillFrame f){ReplaceRuntimeTop(f=f with{GuoSi=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
 private bool DrainGuoSi(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id);
 [ProgramContinuation(ProgramContinuationKind.Resume,24300)] private bool ResumeGuoSi(long id)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{GuoSi:{}r}f||f.Id!=id||r.Stage is GuoSiStage.TargetChoice or GuoSiStage.SidaoChoice or GuoSiStage.NativeUse)return false;RequireIssuedGuoSi(f);if(DrainGuoSi(f))return true;FinishGuoSi(f);return true;
 }
 [ProgramContinuation(ProgramContinuationKind.Movement,24300)] private bool ReturnGuoSiMovement(ProgramSkillFrame f)=>f.GuoSi is not null&&ResumeGuoSi(f.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 250)]
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 250)]
 private bool CanContinueIssuedGuoSi(ProgramSkillFrame f){if(f.GuoSi?.Stage is not(GuoSiStage.MovementChildren or GuoSiStage.NativeUse or GuoSiStage.Completed))return false;RequireIssuedGuoSi(f);return true;}
 [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 250)]
 private bool IsGuoSiMovement(ProgramSkillFrame f,SkillProgramEffect?e,ProgramMovementContinuation p)=>f.GuoSi is{Stage:GuoSiStage.MovementChildren}&&e?.Op==SkillProgramEffectOp.ChooseTanbeiTargetPact&&CanContinueIssuedGuoSi(f)&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null;
 private void RequireIssuedGuoSi(ProgramSkillFrame f)
 {
  if(f.GuoSi is not{}r||r.Source!=GuoSiSource(f)||r.GameplayHash!=f.GameplayHash||r.Instruction!=f.InstructionIndex||r.Turn!=_turnNumber||r.Phase<=0||(r.Stage is GuoSiStage.TargetChoice or GuoSiStage.SidaoChoice)&&r.Phase!=_cardUseDebitPhaseInstanceId||r.Before>r.After||r.After>_movementSequence)throw new InvalidOperationException("Guo Si lacks its exact owning source and instruction receipt.");
  if(r.Stage==GuoSiStage.MovementChildren)
  {
   var paid=CompleteProgramEventHistory().OfType<GuoSiPactPaidEvent>().Where(e=>e.Pact.FrameId==f.Id&&e.Pact.Source==r.Source&&e.Pact.GameplayHash==r.GameplayHash&&e.Before==r.Before&&e.After==r.After&&e.Pact.Branch==r.Branch&&e.Pact.Target==r.Target&&e.Pact.Phase==r.Phase&&e.Pact.Turn==r.Turn&&e.Pact.Actor==f.OwnerSeat&&e.RandomBefore==r.RandomBefore&&e.RandomAfter==r.RandomAfter).ToArray();
   if(paid.Length!=1||paid[0].ActualCount!=(r.SelectedId.HasValue?1:0)||_cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.Reason.Value==TanbeiGainReason&&m.From==r.SelectedFrom&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.CardId==r.SelectedId)!=(r.SelectedId.HasValue?1:0)||f.PendingMovementContinuation is not{BeforeCount:0,CoverageResultBind:null,SubjectSeat:var s}||s!=f.OwnerSeat)throw new InvalidOperationException("Tanbei lacks its exact paid target choice and native movement bounds.");
  }
  if((r.Stage is GuoSiStage.SidaoChoice or GuoSiStage.NativeUse or GuoSiStage.Completed)&&(r.Pair is not{}pair||f.WindowContext?.CardUse is not{}identity||LifecycleCardUse(identity.ParentCardUseFrameId)?.GuoSiPair!=pair||!CompleteProgramEventHistory().OfType<GuoSiPairQualifiedEvent>().Any(e=>e.NativeUseId==identity.ParentCardUseFrameId&&e.OwnerSeat==f.OwnerSeat&&e.FirstActionId==pair.First.ActionId&&e.SecondActionId==pair.Second.ActionId)))throw new InvalidOperationException("Sidao lost its actual completed-window pair qualification.");
  if((r.Stage is GuoSiStage.NativeUse or GuoSiStage.Completed)&&(r.Native is not{}n||n.ParentActionId!=f.WindowContext?.CardUse?.CardActionId||n.Phase!=r.Phase||n.Turn!=r.Turn||n.Instruction!=r.Instruction||CompleteProgramEventHistory().OfType<GuoSiNativeIssuedEvent>().Count(e=>e.Receipt==n)!=1))throw new InvalidOperationException("Sidao has no unique issued native use.");
  if(r.Stage==GuoSiStage.Completed&&(r.Native is not{}returned||CompleteProgramEventHistory().OfType<GuoSiNativeReturnedEvent>().Count(e=>e.Receipt==returned)!=1||LifecycleCardUse(returned.NativeUseId)is not null))throw new InvalidOperationException("Sidao terminal completion requires its once-returned native use.");
 }

 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 290)]
 private void AssertRegisteredIssuedGuoSi(ProgramSkillFrame frame)
 {
     if (frame.GuoSi is not null) RequireIssuedGuoSi(frame);
 }
 [ProgramCapability]
 private sealed class GuoSiProgramCapability(GameEngine engine) : IGuoSiProgramHost
 {public SkillProgramStepOutcome ExecuteGuoSi(SkillProgramEffectOp op,ProgramSkillFrame f)=>engine.ExecuteGuoSi(op,f);}
}
