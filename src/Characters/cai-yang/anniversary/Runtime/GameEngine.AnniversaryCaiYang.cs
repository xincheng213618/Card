namespace CardGame.Core;
public sealed partial class GameEngine
{
 [ProgramCapability] private sealed class AnniversaryCaiYangHost(GameEngine e):IAnniversaryCaiYangHost
 {public SkillProgramStepOutcome Begin(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginCaiYang(f,op);}
 private SkillProgramStepOutcome BeginCaiYang(ProgramSkillFrame supplied,SkillProgramEffectOp operation)
 {
  var f=GetActiveProgramFrame(supplied.Id);
  if(f.InstructionIndex!=1||f.AnniversaryCaiYang is not null)throw new InvalidOperationException("Cai Yang requires his original unpaid instruction.");
  if(operation==SkillProgramEffectOp.ArmAnniversaryCaiYangXunji)
  {
   if(f.TriggerId is not null||f.ActivationId!=AnniversaryCaiYangComposition.Arm||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats is not[var target]||target==f.OwnerSeat||!_players[target].IsAlive||!_players[f.OwnerSeat].IsAlive||
    _currentSeat!=f.OwnerSeat||_phase!=TurnPhase.Play||!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)||ProgramEventHistory<CaiYangXunjiArmedEvent>().Any(e=>e.Grant.FrameId==f.Id))
    throw new InvalidOperationException("Xunji designation requires its real once-per-Play selected other living target.");
   AdvanceEventRulesAndQueueFact(new CaiYangXunjiArmedEvent(new(f.Id,CaiYangSource(f),f.GameplayHash,_turnNumber,target)));return SkillProgramStepOutcome.Continue;
  }
  if(f.WindowContext is not{}w)throw new InvalidOperationException("Cai Yang lost his native trigger context.");
  if(operation==SkillProgramEffectOp.ResolveAnniversaryCaiYangJiaofeng)
  {
   var first=CaiYangFirstWitness(CaiYangCandidate(f),w)??throw new InvalidOperationException("Jiaofeng requires its exact first unfinalized source damage.");
   ReplaceRuntimeTop(f=f with{AnniversaryCaiYang=new(CaiYangSource(f),f.GameplayHash,w.ParentFrameId,_turnNumber,AnniversaryCaiYangStage.Drawing,FirstDamage:first)});
   AdvanceEventRulesAndQueueFact(new CaiYangJiaofengStartedEvent(f.Id,first));BeginOperationProgramDraw(f,f.OwnerSeat,1,CaiYangJiaofengDrawReason);return SkillProgramStepOutcome.AwaitChild;
  }
  if(w.Window==SkillProgramTriggerWindow.TurnEnding)
  {
   var grant=CaiYangEndingWitness(CaiYangCandidate(f),w)??throw new InvalidOperationException("Xunji lost its issued next actual turn, true black use or real ending.");
   if(!CanIssueSelectedActorDuel(grant.Target,f.OwnerSeat))throw new InvalidOperationException("Xunji's native Duel must retain real current legality.");
   var origin=new CaiYangXunjiOrigin(f.Id,f.InstructionIndex,CaiYangSource(f),f.GameplayHash,w.ParentFrameId,_turnNumber,grant.Target,grant.FrameId,_resolutionSequence+1,0);
   ReplaceRuntimeTop(f=f with{AnniversaryCaiYang=new(CaiYangSource(f),f.GameplayHash,w.ParentFrameId,_turnNumber,AnniversaryCaiYangStage.Duel,Duel:origin)});
   AdvanceEventRulesAndQueueFact(new CaiYangXunjiConsumedEvent(grant.FrameId,_turnNumber,true));
   var native=new ProgramSelectedActorDuelOrigin(f.Id,f.InstructionIndex,f.OwnerSeat,f.OwnerSeat,f.SkillId,f.ActivationId!,f.SkillInstanceId,f.GameplayHash,_turnNumber){InitialTargetSeat=grant.Target,CaiYangXunji=origin};
   var card=new Card(0,CardKind.Duel,Suit.None,0);var id=BeginCardUse(card,f.OwnerSeat,[grant.Target],CardKind.Duel,physicalCardIds:[],selectedActorDuelOrigin:native);
   var use=LifecycleCardUse(id)!;if(id!=origin.UseId||use.Action is not{Type:CardActionType.Use}action)throw new InvalidOperationException("Xunji lost its one true declared Duel action.");
   origin=origin with{ActionId=action.ActionId};UpdateLifecycleCardUse(id,u=>u with{SelectedActorDuelOrigin=native with{CaiYangXunji=origin}});
   ReplaceRuntimeFrame(f.Id,f with{AnniversaryCaiYang=f.AnniversaryCaiYang! with{Duel=origin}});AdvanceEventRulesAndQueueFact(new CaiYangXunjiIssuedEvent(origin));
   BeginJizhiOrNullificationWindow(id,card,f.OwnerSeat,[grant.Target],LegalActionKind.Duel,playedCardKind:CardKind.Duel);return SkillProgramStepOutcome.AwaitChild;
  }
  var duel=CaiYangDuelDamage(f.OwnerSeat,w)??throw new InvalidOperationException("Xunji retaliation requires its own original Duel's applied damage.");
  ReplaceRuntimeTop(f=f with{AnniversaryCaiYang=new(CaiYangSource(f),f.GameplayHash,w.ParentFrameId,_turnNumber,AnniversaryCaiYangStage.Damage,Duel:duel,DamageId:w.DamageFrameId!.Value,DamageTarget:w.TargetSeat!.Value,DamageAmount:w.Amount)});
  AdvanceEventRulesAndQueueFact(new CaiYangXunjiDamageBoundEvent(f.Id,duel.UseId,w.DamageFrameId.Value,w.SourceSeat!.Value,w.TargetSeat.Value,w.Amount));
  if(!_players[w.TargetSeat.Value].IsAlive||!_players[f.OwnerSeat].IsAlive){FinishCaiYang(f);return SkillProgramStepOutcome.AwaitChild;}
  return BeginProgramSkillDamage(f,f.OwnerSeat,w.Amount,explicitSourceSeat:w.TargetSeat.Value);
 }
 private static readonly CardMoveReason CaiYangJiaofengDrawReason=new("skill-program.anniversary:jiaofeng.draw");
 private bool DrainCaiYang(long id)=>TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.Program)||TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||
  TryBeginHpChangedProgramWindow(id,PostEventContinuation.Program)||TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id);
 private void ContinueCaiYang(long id)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{AnniversaryCaiYang:{}r}f||f.Id!=id)return;
  RequireCaiYang(f);
  if(r.Stage==AnniversaryCaiYangStage.Drawing)return;
  if(r.Stage==AnniversaryCaiYangStage.Duel)
  {
   if(LifecycleCardUse(r.Duel!.UseId)is not null)return;
   if(ActiveCardAttack is not null||ActiveDuel is not null||ProgramEventHistory<CardUseFinishedEvent>().Count(e=>e.ResolutionId==r.Duel.UseId&&e.CardKind==CardKind.Duel&&e.CardId==0)!=1)
    throw new InvalidOperationException("Xunji must wait until its original native Duel is fully cleaned up.");
   AdvanceEventRulesAndQueueFact(new CaiYangXunjiReturnedEvent(f.Id,r.Duel.UseId,r.Duel.ActionId));FinishCaiYang(f);return;
  }
  if(r.Stage==AnniversaryCaiYangStage.Damage)
  {
   if(f.AttackAttempt is not null)return;
   ReplaceRuntimeTop(f=f with{AnniversaryCaiYang=r with{Stage=AnniversaryCaiYangStage.DamageReturned}});r=f.AnniversaryCaiYang!;
  }
  if(DrainCaiYang(id))return;
  f=GetActiveProgramFrame(id);r=f.AnniversaryCaiYang!;
  if(r.Stage==AnniversaryCaiYangStage.DrawChildren&&r.FirstDamage!.LostHp>2&&!r.RecoveryIssued)
  {
   ReplaceRuntimeTop(f=f with{AnniversaryCaiYang=r with{Stage=AnniversaryCaiYangStage.RecoveryChildren,RecoveryIssued=true}});
   AdvanceEventRulesAndQueueFact(new CaiYangJiaofengRecoveryIssuedEvent(f.Id,1));
   if(_players[f.OwnerSeat].IsAlive)new ProgramSkillHost(this).Recover(f.Id,f.OwnerSeat,f.OwnerSeat,1,null,null);
   if(DrainCaiYang(id))return;f=GetActiveProgramFrame(id);
  }
  FinishCaiYang(f);
 }
 private void FinishCaiYang(ProgramSkillFrame f)
 {AdvanceEventRulesAndQueueFact(new CaiYangCompletedEvent(f.Id));ReplaceRuntimeTop(f=f with{AnniversaryCaiYang=null});AdvanceRuntimeProgram(f.Id);}
 [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveAnniversaryCaiYangJiaofeng)] private void ReturnCaiYangDraw(ProgramSkillFrame supplied,NativeDrawOutcome draw)
 {
  var f=GetActiveProgramFrame(supplied.Id);RequireCaiYang(f);var r=f.AnniversaryCaiYang!;
  if(r.Stage!=AnniversaryCaiYangStage.Drawing||r.Draw is not null||f.NativeDraw is not null||draw.RecipientSeat!=f.OwnerSeat||draw.RequestedCount!=1||draw.SequenceAfter!=_movementSequence)
   throw new InvalidOperationException("Jiaofeng lost its original once-issued native one-card draw.");
  ReplaceRuntimeTop(f=f with{AnniversaryCaiYang=r with{Stage=AnniversaryCaiYangStage.DrawChildren,Draw=draw}});AdvanceEventRulesAndQueueFact(new CaiYangJiaofengDrawReturnedEvent(f.Id,draw.ActualDrawIds.Count,draw.AttemptFrameId,draw.SequenceBefore,draw.SequenceAfter));ContinueCaiYang(f.Id);
 }
 [ProgramContinuation(ProgramContinuationKind.Resume,45600)] private bool ResumeCaiYang(long id)
 {if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{AnniversaryCaiYang:not null}f||f.Id!=id)return false;ContinueCaiYang(id);return true;}
 [ProgramContinuation(ProgramContinuationKind.Movement,45600)] private bool ReturnCaiYangMovement(ProgramSkillFrame f)=>f.AnniversaryCaiYang is not null&&ResumeCaiYang(f.Id);
 [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup,45600)] private bool ReturnCaiYangDuel(AttackCompletionReceipt completion)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{AnniversaryCaiYang:{Stage:AnniversaryCaiYangStage.Duel,Duel:{}o}}f||completion.ResolutionId!=o.UseId)return false;
  if(completion.ProgramFrameId!=f.Id||LifecycleCardUse(o.UseId)is not null||ProgramEventHistory<CaiYangXunjiIssuedEvent>().Count(e=>e.Origin==o)!=1)throw new InvalidOperationException("Xunji lost its exact original Duel cleanup return.");
  ContinueCaiYang(f.Id);return true;
 }
}
