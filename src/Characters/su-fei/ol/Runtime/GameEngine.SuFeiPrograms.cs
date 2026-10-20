namespace CardGame.Core;
public sealed partial class GameEngine
{
 private const string SuFeiDrawReason="skill-program.lianpian.draw",SuFeiGiftReason="skill-program.lianpian.gift";
 private static CardConversionSource SuFeiSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_su_fei_ol_SuFeiPrograms.SuFeiSource(f);
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 450)]
 private bool CanOfferSuFei(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext w)
 {
  if(!ProgramInstructionResolver.Default.Features(t).HasOperation(SkillProgramEffectOp.DrawThenOfferActualConsecutiveTargetGift))return true;
  return w.CardUse is{}identity&&LifecycleCardUse(identity.ParentCardUseFrameId)is{SuFeiQualification:{}q,Action:{}a}&&a.ActionId==identity.CardActionId&&ValidSuFeiQualification(q,c.OwnerSeat);
 }
 private SkillProgramStepOutcome BeginSuFei(ProgramSkillFrame supplied,SkillProgramEffect effect)
 {
  var f=GetActiveProgramFrame(supplied.Id);
  if(f.SuFei is not null||f.InstructionIndex!=1||f.WindowContext is not{Window:SkillProgramTriggerWindow.CardUseTargetsFinalized,CardUse:{}identity}w||LifecycleCardUse(identity.ParentCardUseFrameId)is not{SuFeiQualification:{}q,Action:{}a}use||a.ActionId!=identity.CardActionId||!ValidSuFeiQualification(q,f.OwnerSeat)||!_players[f.OwnerSeat].IsAlive||!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId))throw new InvalidOperationException("Lianpian lost its unpaid exact consecutive native use.");
  var r=new SuFeiReceipt(SuFeiSource(f),f.GameplayHash,f.InstructionIndex,w.ParentFrameId,q,SuFeiStage.DrawChildren,DrawBefore:_movementSequence,DrawAfter:_movementSequence);
  ReplaceRuntimeTop(f with{SuFei=r,PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  var cards=DrawCards(_players[f.OwnerSeat],1,true,new(SuFeiDrawReason));r=r with{DrawAfter=_movementSequence,DrawnId=cards.Count==1?cards[0]:null,ActualDraw=cards.Count};ReplaceRuntimeTop(GetActiveProgramFrame(f.Id)with{SuFei=r});AdvanceEventRulesAndQueueFact(new LianpianDrawPaidEvent(f.Id,r.Source,r.GameplayHash,q.Current.NativeUseId,q.Current.Action.ActionId,r.DrawBefore,r.DrawAfter,r.ActualDraw));AdvanceRuntimeProgram(f.Id);return SkillProgramStepOutcome.AwaitChild;
 }
 private bool CanGiveSuFei(ProgramSkillFrame f)=>_players[f.OwnerSeat].IsAlive&&f.SuFei is{ActualDraw:1,DrawnId:{}id}r&&_cardZones.GetLocation(id)==CardLocation.Hand(f.OwnerSeat)&&r.Qualification.CommonTargets.Any(s=>s!=f.OwnerSeat&&_players[s].IsAlive);
 private IReadOnlyList<PromptChoice> SuFeiChoices(ProgramSkillFrame f)
 {
  if(f.SuFei is not{Stage:SuFeiStage.ChoosingGift}r||!CanGiveSuFei(f))return [];
  var choices=r.Qualification.CommonTargets.Where(s=>s!=f.OwnerSeat&&_players[s].IsAlive).Select(s=>new PromptChoice(new($"lianpian.{f.Id}.give-{s}"),$"将刚摸到的牌交给{_players[s].Name}",[r.DrawnId!.Value],[s],new Dictionary<string,string>{{"program-action","lianpian"},{"branch","give"}})).ToList();
  choices.Add(new(new($"lianpian.{f.Id}.keep"),"保留刚摸到的牌",[],[],new Dictionary<string,string>{{"program-action","lianpian"},{"branch","keep"}}));return Array.AsReadOnly(choices.ToArray());
 }
 private void PublishSuFeiChoice(ProgramSkillFrame f)
 {
  var choices=SuFeiChoices(f);if(choices.Count==0){FinishSuFei(f);return;}var skill=_contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});_pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"可将本次实际摸到的牌交给共同目标。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=true,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};_status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 [ProgramAiChoice(SkillProgramEffectOp.DrawThenOfferActualConsecutiveTargetGift)]
 private PromptChoice SuFeiAiChoice(ProgramSkillFrame f)=>SuFeiChoices(f).Last();
 [ProgramAnswer("lianpian")]
 private void ResolveSuFeiChoice(PromptChoice choice)
 {
  var f=(ProgramSkillFrame)_resolutionStack.Last();RequireSuFei(f);var r=f.SuFei!;
  if(r.Stage!=SuFeiStage.ChoosingGift||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!AssistedChoicesEqual([choice],SuFeiChoices(f).Where(c=>c.Id==choice.Id).ToArray()))throw new InvalidOperationException("Lianpian cannot replace its drawn entity or gift authority.");ClearPendingDecision();
  if(choice.Parameters.GetValueOrDefault("branch")=="keep"){FinishSuFei(f);return;}
  var target=choice.Targets.Single();if(!CanGiveSuFei(f)||!r.Qualification.CommonTargets.Contains(target)||!choice.Cards.SequenceEqual([r.DrawnId!.Value]))throw new InvalidOperationException("Lianpian lost its exact drawn entity or common target.");
  r=r with{Stage=SuFeiStage.GiftChildren,GiftTarget=target,GiftBefore=_movementSequence,GiftAfter=_movementSequence};ReplaceRuntimeTop(f with{SuFei=r,PendingMovementContinuation=new(f.OwnerSeat,0,null),Step=ResolutionFrameStep.ResolvingEffect});
  MoveProgramCardsFromMultipleSources([r.DrawnId.Value],CardLocation.Hand(target),new(SuFeiGiftReason),(_,moves)=>{var paid=r with{GiftAfter=moves.Single().Sequence};ReplaceRuntimeTop(GetActiveProgramFrame(f.Id)with{SuFei=paid});AdvanceEventRulesAndQueueFact(new LianpianGiftPaidEvent(f.Id,paid.Source,paid.GameplayHash,paid.Qualification.Current.NativeUseId,paid.Qualification.Current.Action.ActionId,target,paid.GiftBefore,paid.GiftAfter));});AdvanceRuntimeProgram(f.Id);
 }
 private bool DrainSuFei(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id);
 private void FinishSuFei(ProgramSkillFrame f){ReplaceRuntimeTop(f=f with{SuFei=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
 [ProgramContinuation(ProgramContinuationKind.Resume,23900)] private bool ResumeSuFei(long id)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{SuFei:{}r}f||f.Id!=id||r.Stage==SuFeiStage.ChoosingGift)return false;RequireSuFei(f);if(DrainSuFei(f))return true;
  if(r.Stage==SuFeiStage.GiftChildren||!CanGiveSuFei(f)){FinishSuFei(f);return true;}
  ReplaceRuntimeTop(f=f with{SuFei=r with{Stage=SuFeiStage.ChoosingGift},PendingMovementContinuation=null});PublishSuFeiChoice(f);return true;
 }
 [ProgramContinuation(ProgramContinuationKind.Movement,23900)] private bool ReturnSuFeiMovement(ProgramSkillFrame f)=>f.SuFei is not null&&ResumeSuFei(f.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 220)]
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 220)]
 private bool CanContinueSuFei(ProgramSkillFrame f){if(f.SuFei is null)return false;RequireSuFei(f);return true;}
 [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 220)]
 private bool IsSuFeiMovement(ProgramSkillFrame f,SkillProgramEffect?e,ProgramMovementContinuation p)=>f.SuFei is{Stage:SuFeiStage.DrawChildren or SuFeiStage.GiftChildren}&&e?.Op==SkillProgramEffectOp.DrawThenOfferActualConsecutiveTargetGift&&CanContinueSuFei(f)&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null;
 private void RequireSuFei(ProgramSkillFrame f)
 {
  if(f.SuFei is not{}r||r.Source!=SuFeiSource(f)||r.GameplayHash!=f.GameplayHash||r.InstructionIndex!=f.InstructionIndex||r.WindowId!=f.WindowContext?.ParentFrameId||!ValidSuFeiQualification(r.Qualification,f.OwnerSeat)||r.DrawBefore>r.DrawAfter||r.DrawAfter>_movementSequence||r.ActualDraw is not(0 or 1)||r.DrawnId.HasValue!=(r.ActualDraw==1)||CompleteProgramEventHistory().OfType<LianpianDrawPaidEvent>().Count(e=>e==new LianpianDrawPaidEvent(f.Id,r.Source,r.GameplayHash,r.Qualification.Current.NativeUseId,r.Qualification.Current.Action.ActionId,r.DrawBefore,r.DrawAfter,r.ActualDraw))!=1||_cardMovements.Count(m=>m.Sequence>r.DrawBefore&&m.Sequence<=r.DrawAfter&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==SuFeiDrawReason&&(r.DrawnId is null||m.CardId==r.DrawnId))!=r.ActualDraw)throw new InvalidOperationException("Lianpian lacks its exact native use, source, window or once-paid draw invoice.");
  if(r.Stage==SuFeiStage.ChoosingGift){if(f.PendingMovementContinuation is not null||r.GiftTarget is not null)throw new InvalidOperationException("Lianpian choice cannot borrow a movement payment.");}
  else if(f.PendingMovementContinuation is not{SubjectSeat:var actor,BeforeCount:0,CoverageResultBind:null}||actor!=f.OwnerSeat)throw new InvalidOperationException("Lianpian paid child lost its owning movement return.");
  if(r.Stage==SuFeiStage.GiftChildren&&(r.GiftTarget is not{}target||!r.Qualification.CommonTargets.Contains(target)||r.DrawnId is not{}id||r.GiftAfter<=r.GiftBefore||CompleteProgramEventHistory().OfType<LianpianGiftPaidEvent>().Count(e=>e==new LianpianGiftPaidEvent(f.Id,r.Source,r.GameplayHash,r.Qualification.Current.NativeUseId,r.Qualification.Current.Action.ActionId,target,r.GiftBefore,r.GiftAfter))!=1||_cardMovements.Count(m=>m.Sequence>r.GiftBefore&&m.Sequence<=r.GiftAfter&&m.CardId==id&&m.From==CardLocation.Hand(f.OwnerSeat)&&m.To==CardLocation.Hand(target)&&m.Reason.Value==SuFeiGiftReason)!=1))throw new InvalidOperationException("Lianpian cannot repeat or substitute its actual paid gift.");
 }

 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 260)]
 private void AssertRegisteredSuFei(ProgramSkillFrame frame)
 {
     if (frame.SuFei is not null) RequireSuFei(frame);
 }
 [ProgramCapability]
 private sealed class SuFeiProgramCapability(GameEngine engine) : ISuFeiProgramHost
 {public SkillProgramStepOutcome BeginSuFei(ProgramSkillFrame f,SkillProgramEffect e)=>engine.BeginSuFei(f,e);}
}
