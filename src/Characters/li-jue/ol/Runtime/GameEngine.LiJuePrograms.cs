namespace CardGame.Core;
public sealed partial class GameEngine
{
 private const string YisuanGainReason="skill-program.yisuan.actual-used-trick";
 private static CardConversionSource LiJueSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
 private bool LiJueCurrentSource(ProgramSkillFrame f)=>_players[f.OwnerSeat].IsAlive&&_players[f.OwnerSeat].HasHpValue&&HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)&&_contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash==f.GameplayHash;
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 440)]
 private bool CanOfferLiJue(ProgramTriggerCandidate candidate,SkillProgramTrigger trigger,ProgramSkillWindowContext context)
 {
  if(ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.ChooseHpBoundOtherAndDealRandomDamage))return LangxiTargets(candidate.OwnerSeat).Length>0;
  if(ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.PayMaxHpAndObtainActualUsedTrick))return context.MovementBatch is{LiJueUseDiscard:{}invoice}batch&&_players[candidate.OwnerSeat].HasHpValue&&_players[candidate.OwnerSeat].MaxHp>0&&ValidLiJueUseDiscard(invoice,batch,candidate.OwnerSeat);
  return true;
 }
 private SkillProgramStepOutcome BeginLiJue(ProgramSkillFrame supplied,SkillProgramEffect effect)
 {
  var f=GetActiveProgramFrame(supplied.Id);if(f.LiJue is not null||f.InstructionIndex!=1||f.TriggerId is null||f.WindowContext is not{}context||f.OwnerSeat!=_currentSeat||!LiJueCurrentSource(f))throw new InvalidOperationException("LiJue requires the current unpaid native own phase source.");
  var r=new LiJueReceipt(LiJueSource(f),f.GameplayHash,f.InstructionIndex,_turnNumber,context.ParentFrameId,effect.Op==SkillProgramEffectOp.ChooseHpBoundOtherAndDealRandomDamage?LiJueStage.ChoosingTarget:LiJueStage.MaximumHpChildren);
  if(effect.Op==SkillProgramEffectOp.ChooseHpBoundOtherAndDealRandomDamage){if(context.Window!=SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)throw new InvalidOperationException("Langxi requires actual Prepare.");ReplaceRuntimeTop(f=f with{LiJue=r});PublishLangxi(f);return SkillProgramStepOutcome.AwaitChoice;}
  if(context.Window!=SkillProgramTriggerWindow.CardsMoved||context.MovementBatch is not{LiJueUseDiscard:{}invoice}batch||!ValidLiJueUseDiscard(invoice,batch,f.OwnerSeat))throw new InvalidOperationException("Yisuan lost its actual native used-Trick movement.");
  var owner=_players[f.OwnerSeat];r=r with{OriginalUse=invoice,MaxBefore=owner.MaxHp,MaxAfter=owner.MaxHp-1,HpBefore=owner.Hp,HpAfter=Math.Min(owner.Hp,owner.MaxHp-1)};ReplaceRuntimeTop(f=f with{LiJue=r});
  if(r.MaxAfter<0)throw new InvalidOperationException("Yisuan cannot spend an absent maximum HP.");
  owner.MaxHp=r.MaxAfter;owner.Hp=r.HpAfter;
  AdvanceEventRulesAndQueueFact(new MaximumHpChangedEvent(owner.Seat,-1,owner.MaxHp,f.SkillId){HpBeforeClamp=r.HpBefore});
  AdvanceEventRulesAndQueueFact(new YisuanMaximumHpPaidEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.WindowId,invoice.NativeUseId,invoice.Action.ActionId,r.MaxBefore,r.MaxAfter,r.HpBefore,r.HpAfter));
  if(r.MaxAfter==0){BeginPlayerDeath(f.Id,owner,null,null,null,causingProgramSkillFrameId:f.Id);return SkillProgramStepOutcome.AwaitChild;}
  AdvanceRuntimeProgram(f.Id);return SkillProgramStepOutcome.AwaitChild;
 }
 private void FinishLiJue(ProgramSkillFrame f){ReplaceRuntimeTop(f=f with{LiJue=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
 [ProgramContinuation(ProgramContinuationKind.Resume,23700)] private bool ResumeLiJue(long id)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{LiJue:{}r}f||f.Id!=id||r.Stage==LiJueStage.ChoosingTarget)return false;RequireLiJue(f);
  if(r.Stage==LiJueStage.DamageChildren){FinishLiJue(f);return true;}
  // A maximum-HP payment owns a normal Program return. Only the later
  // physical gain has an actual issued movement continuation to return to.
  var continuation=f.PendingMovementContinuation is not null?PostEventContinuation.AwaitedProgramMovement:PostEventContinuation.Program;
  if(TryBeginQueuedRecoveryReplacement(f.Id,continuation)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,continuation)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id))return true;
  if(r.Stage==LiJueStage.GainChildren){FinishLiJue(f);return true;}
  var original=r.OriginalUse!;var cards=_players[f.OwnerSeat].IsAlive&&original.FinishedMovements.All(m=>_cardZones.GetLocation(m.CardId)==CardLocation.DiscardPile)?original.FinishedMovements.Select(m=>m.CardId).ToArray():[];
  r=r with{Stage=LiJueStage.GainChildren,Before=_movementSequence,After=_movementSequence,ActualGain=0};ReplaceRuntimeTop(f with{LiJue=r,PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  if(cards.Length==0){AdvanceEventRulesAndQueueFact(new YisuanGainPaidEvent(f.Id,r.Source,r.GameplayHash,original.NativeUseId,original.Action.ActionId,r.Before,r.After,0));FinishLiJue(GetActiveProgramFrame(f.Id));return true;}
  MoveProgramCardsFromMultipleSources(cards,CardLocation.Hand(f.OwnerSeat),new(YisuanGainReason),(_,moves)=>{var paid=r with{After=moves.Last().Sequence,ActualGain=moves.Count};ReplaceRuntimeTop(GetActiveProgramFrame(f.Id)with{LiJue=paid});AdvanceEventRulesAndQueueFact(new YisuanGainPaidEvent(f.Id,paid.Source,paid.GameplayHash,original.NativeUseId,original.Action.ActionId,paid.Before,paid.After,paid.ActualGain));});AdvanceRuntimeProgram(f.Id);return true;
 }
 [ProgramContinuation(ProgramContinuationKind.Movement,23700)] private bool ReturnLiJueMovement(ProgramSkillFrame f)=>f.LiJue is not null&&ResumeLiJue(f.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 200)]
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 200)]
 private bool CanContinueLiJue(ProgramSkillFrame f){if(f.LiJue is not{Stage:LiJueStage.DamageChildren or LiJueStage.MaximumHpChildren or LiJueStage.GainChildren})return false;RequireLiJue(f);return true;}
 [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 200)]
 private bool IsLiJueMovement(ProgramSkillFrame f,SkillProgramEffect?effect,ProgramMovementContinuation p)=>f.LiJue is{Stage:LiJueStage.GainChildren}&&effect?.Op==SkillProgramEffectOp.PayMaxHpAndObtainActualUsedTrick&&CanContinueLiJue(f)&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null;
 private void RequireLiJue(ProgramSkillFrame f)
 {
  if(f.LiJue is not{}r||r.Source!=LiJueSource(f)||r.GameplayHash!=f.GameplayHash||r.InstructionIndex!=f.InstructionIndex||r.ActualTurn!=_turnNumber||r.WindowId!=f.WindowContext?.ParentFrameId)throw new InvalidOperationException("LiJue lost its exact source, hash, instruction or actual native window.");
  if(r.Stage==LiJueStage.ChoosingTarget){if(r.Roll is not null||r.TargetSeat is not null||r.OriginalUse is not null)throw new InvalidOperationException("Langxi cannot borrow a previously issued random receipt.");return;}
  if(r.Stage==LiJueStage.DamageChildren){if(r.Roll is not (>=0 and <=2)||r.TargetSeat is not{}target||CompleteProgramEventHistory().OfType<LangxiRandomResolvedEvent>().Count(e=>e==new LangxiRandomResolvedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.WindowId,target,r.Roll.Value))!=1)throw new InvalidOperationException("Langxi cannot resume an unissued or changed true random result.");return;}
  if(r.OriginalUse is not{}invoice||f.WindowContext?.MovementBatch is not{}batch||!ValidLiJueUseDiscard(invoice,batch,f.OwnerSeat,false)||r.MaxBefore<1||r.MaxAfter!=r.MaxBefore-1||r.HpAfter!=Math.Min(r.HpBefore,r.MaxAfter)||CompleteProgramEventHistory().OfType<YisuanMaximumHpPaidEvent>().Count(e=>e==new YisuanMaximumHpPaidEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.WindowId,invoice.NativeUseId,invoice.Action.ActionId,r.MaxBefore,r.MaxAfter,r.HpBefore,r.HpAfter))!=1||!CompleteProgramEventHistory().OfType<MaximumHpChangedEvent>().Any(e=>e.PlayerSeat==f.OwnerSeat&&e.SkillId==f.SkillId&&e.Delta==-1&&e.MaximumHp==r.MaxAfter))throw new InvalidOperationException("Yisuan lost its exact used entity or actually paid maximum HP.");
  if(r.Stage==LiJueStage.GainChildren&&(r.Before>r.After||r.After>_movementSequence||r.ActualGain<0||r.ActualGain>invoice.FinishedMovements.Count||f.PendingMovementContinuation is not{SubjectSeat:var actor,BeforeCount:0,CoverageResultBind:null}||actor!=f.OwnerSeat||CompleteProgramEventHistory().OfType<YisuanGainPaidEvent>().Count(e=>e==new YisuanGainPaidEvent(f.Id,r.Source,r.GameplayHash,invoice.NativeUseId,invoice.Action.ActionId,r.Before,r.After,r.ActualGain))!=1||_cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.From==CardLocation.DiscardPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==YisuanGainReason&&invoice.FinishedMovements.Any(c=>c.CardId==m.CardId))!=r.ActualGain))throw new InvalidOperationException("Yisuan cannot repeat a completed actual gain interval.");
 }

 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 240)]
 private void AssertRegisteredLiJue(ProgramSkillFrame frame)
 {
     if (frame.LiJue is not null) RequireLiJue(frame);
 }
 [ProgramCapability]
 private sealed class LiJueProgramCapability(GameEngine engine) : ILiJueProgramHost
 {public SkillProgramStepOutcome BeginLiJue(ProgramSkillFrame f,SkillProgramEffect e)=>engine.BeginLiJue(f,e);}
}
