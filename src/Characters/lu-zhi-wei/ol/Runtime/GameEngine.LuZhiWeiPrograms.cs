using System.Globalization;
using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record QingzhongPlayPhaseKey(int ActualTurnNumber,int OwnerSeat,int PhaseInstanceId);
public sealed record QingzhongPlayDebt(long ProgramFrameId,CardConversionSource Source,string GameplayHash,long StartingWindowId,QingzhongPlayPhaseKey Phase,string EndingBindingId,int InstructionIndex);
public sealed record QingzhongDebtArmedEvent(QingzhongPlayDebt Debt):IGameEvent;
public sealed record QingzhongDebtConsumedEvent(long FrameId,QingzhongPlayDebt Debt,bool Settled):IGameEvent;
public enum QingzhongStage { DrawChildren,ChooseMinimum,SwapChildren }
public sealed record QingzhongPending(QingzhongPlayDebt Debt,QingzhongStage Stage,int InstructionIndex,long Before,long After,int ActualCount,int? TargetSeat=null,int OwnerHandCount=0,int TargetHandCount=0);
public sealed record QingzhongMovementIssuedEvent(long FrameId,QingzhongStage Stage,long Before,long After,int ActualCount,int? TargetSeat,int OwnerHandCount,int TargetHandCount):IGameEvent;
public sealed record QingzhongCompletedEvent(long FrameId,QingzhongPlayDebt Debt,QingzhongStage Stage,int? TargetSeat,bool OwnerAlive):IGameEvent;
public sealed partial class CharacterState
{
 private readonly List<QingzhongPlayDebt> _qingzhongDebts=[];
 internal IReadOnlyList<QingzhongPlayDebt> QingzhongDebts=>_qingzhongDebts.AsReadOnly();
 internal void ArmQingzhongDebt(QingzhongPlayDebt debt)=>_qingzhongDebts.Add(debt);
 internal bool ConsumeQingzhongDebt(QingzhongPlayDebt debt)=>_qingzhongDebts.Remove(debt);
}
public sealed partial record ProgramSkillFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public QingzhongPending? Qingzhong{get;init;}
}
public sealed partial record ProgramSkillWindowContext
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public QingzhongPlayPhaseKey? QingzhongPhase{get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public QingzhongPlayDebt? QingzhongDebt{get;init;}
}
public sealed partial class GameEngine
{
 private const string QingzhongDrawReason="program.qingzhong.draw",QingzhongSwapReason="program.qingzhong.swap";
 private QingzhongPlayPhaseKey? CaptureQingzhongStartingPhase(CharacterState owner)=>GetSkillBindingShard(owner).ProgramInstances.Any(i=>i.Program.Triggers.Any(t=>t.Effects.Any(e=>e.Op==SkillProgramEffectOp.DrawAndArmQingzhongActualPlayDebt)))?new(_turnNumber,owner.Seat,_cardUseDebitPhaseInstanceId):null;
 private QingzhongPlayPhaseKey? CaptureQingzhongEndingPhase(CharacterState owner)=>owner.QingzhongDebts.Any(g=>g.Phase.ActualTurnNumber==_turnNumber&&g.Phase.PhaseInstanceId==_cardUseDebitPhaseInstanceId)?new(_turnNumber,owner.Seat,_cardUseDebitPhaseInstanceId):null;
 private ProgramSkillWindowContext CreateQingzhongStartingContext(PlayPhaseStartingBoundaryFrame boundary,ProgramSkillWindowContext context)=>context with{QingzhongPhase=boundary.QingzhongStartingPhase};
 private IEnumerable<ProgramTriggerCandidate> IssuedQingzhongEndingCandidates(CharacterState owner)=>owner.QingzhongDebts.Where(g=>g.Phase.ActualTurnNumber==_turnNumber&&g.Phase.PhaseInstanceId==_cardUseDebitPhaseInstanceId).Select(g=>new ProgramTriggerCandidate(owner.Seat,g.Source.SkillId,g.EndingBindingId,g.Source.SkillInstanceId,g.GameplayHash,0));
 private ProgramSkillWindowContext CreateQingzhongEndingContext(ProgramLifecycleTriggerWindowFrame f,ProgramTriggerCandidate c,ProgramSkillWindowContext context)=>context with{QingzhongDebt=f.QingzhongEndingPhase is {} phase?_players[c.OwnerSeat].QingzhongDebts.SingleOrDefault(g=>g.Phase==phase&&g.Source.SkillId==c.SkillId&&g.Source.SkillInstanceId==c.SkillInstanceId&&g.EndingBindingId==c.BindingId&&g.GameplayHash==c.GameplayHash):null};
 private bool HasIssuedQingzhongDebtCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext w)=>w.Window==SkillProgramTriggerWindow.PlayEnding&&w.SourceSeat==c.OwnerSeat&&w.QingzhongDebt is {} g&&g.Source.SkillId==c.SkillId&&g.Source.SkillInstanceId==c.SkillInstanceId&&g.EndingBindingId==c.BindingId&&g.GameplayHash==c.GameplayHash&&_players[c.OwnerSeat].QingzhongDebts.Contains(g);
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 570)]
 private void ObserveQingzhongDebtTermination(IGameEvent payload)
 {
  if(payload is not (TurnEndedEvent or PlayerDiedEvent))return;
  foreach(var owner in _players)foreach(var g in owner.QingzhongDebts.Where(g=>payload is PlayerDiedEvent d&&d.VictimSeat==owner.Seat||payload is TurnEndedEvent t&&t.ActorSeat==owner.Seat&&t.TurnNumber==g.Phase.ActualTurnNumber).ToArray())
  {owner.ConsumeQingzhongDebt(g);AdvanceEventRulesAndQueueFact(new QingzhongDebtConsumedEvent(g.ProgramFrameId,g,false));}
 }
 private SkillProgramStepOutcome DrawAndArmQingzhongActualPlayDebt(ProgramSkillFrame input)
 {
  var f=GetActiveProgramFrame(input.Id);
  if(f.Qingzhong is not null||f.WindowContext is not {Window:SkillProgramTriggerWindow.PlayPhaseStarting,QingzhongPhase:{} phase} w||phase.OwnerSeat!=f.OwnerSeat||w.SourceSeat!=f.OwnerSeat||phase.ActualTurnNumber!=_turnNumber||f.InstructionIndex!=1)throw new InvalidOperationException("Qingzhong requires its original unpaid own Play-start boundary.");
  var ending=_contentRegistry.GetSkill(f.SkillId).Program!.Triggers.Single(t=>t.Effects is [{Op:SkillProgramEffectOp.SettleQingzhongActualPlayDebt}]);
  var debt=new QingzhongPlayDebt(f.Id,new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId),f.GameplayHash,w.ParentFrameId,phase,ending.Id,f.InstructionIndex);
  _players[f.OwnerSeat].ArmQingzhongDebt(debt);ReplaceRuntimeTop(f=f with{Qingzhong=new(debt,QingzhongStage.DrawChildren,f.InstructionIndex,_movementSequence,_movementSequence,0),PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  AdvanceEventRulesAndQueueFact(new QingzhongDebtArmedEvent(debt));var before=_movementSequence;
  DrawProgramCards(f.Id,f.OwnerSeat,2,null,null,SkillProgramCardSetVisibility.Private,new(QingzhongDrawReason));
  f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{Qingzhong=f.Qingzhong!with{After=_movementSequence,ActualCount=_cardMovements.Count(m=>m.Sequence>before&&m.Sequence<=_movementSequence&&m.Reason.Value==QingzhongDrawReason&&m.To==CardLocation.Hand(f.OwnerSeat))}});
  IssueQingzhongMovement(f);AdvanceRuntimeProgram(f.Id);return SkillProgramStepOutcome.AwaitChild;
 }
 private SkillProgramStepOutcome SettleQingzhongActualPlayDebt(ProgramSkillFrame input)
 {
  var f=GetActiveProgramFrame(input.Id);var debt=f.WindowContext?.QingzhongDebt??throw new InvalidOperationException("Qingzhong ending needs its issued actual phase debt.");
  if(!_players[f.OwnerSeat].IsAlive)return SkillProgramStepOutcome.Continue;
  ReplaceRuntimeTop(f=f with{Qingzhong=new(debt,QingzhongStage.ChooseMinimum,f.InstructionIndex,0,0,0)});PublishQingzhongMinimum(f);return SkillProgramStepOutcome.AwaitChoice;
 }
 private int[] QingzhongMinimumSeats()
 {var living=_players.Where(p=>p.IsAlive).ToArray();if(living.Length==0)return [];var count=living.Min(p=>GetHand(p).Count);return living.Where(p=>GetHand(p).Count==count).Select(p=>p.Seat).ToArray();}
 private void PublishQingzhongMinimum(ProgramSkillFrame f)
 {
  var seats=QingzhongMinimumSeats();var choices=seats.Select(s=>new PromptChoice(new ChoiceId($"qingzhong.{f.Id}.{s}"),$"与{_players[s].Name}交换手牌",[],[s],new Dictionary<string,string>{["program-action"]="qingzhong-minimum",["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture)})).ToArray();
  var skill=_contentRegistry.GetSkill(f.SkillId);_pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"清忠：选择当前手牌最少的角色。",[],seats,f.OwnerSeat){PromptId=CreatePromptId(),Choices=Array.AsReadOnly(choices),SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};_status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
 }
 [ProgramAnswer("qingzhong-minimum")]
 private void ResolveQingzhongMinimumChoice(PromptChoice choice)
 {
  var f=_resolutionStack.LastOrDefault()as ProgramSkillFrame??throw new InvalidOperationException("Qingzhong lost its owning chooser.");RequireIssuedLuZhiWei(f);
  if(f.Qingzhong is not {Stage:QingzhongStage.ChooseMinimum} p||choice.Parameters.GetValueOrDefault("frame-id")!=f.Id.ToString(CultureInfo.InvariantCulture)||choice.Targets is not [var target]||!QingzhongMinimumSeats().Contains(target))throw new InvalidOperationException("Qingzhong choice is not a current minimum living hand seat.");
  ClearPendingDecision();var ownerIds=GetHand(_players[f.OwnerSeat]).Select(c=>c.Id).ToArray();var targetIds=target==f.OwnerSeat?[]:GetHand(_players[target]).Select(c=>c.Id).ToArray();var before=_movementSequence;
  _players[f.OwnerSeat].ConsumeQingzhongDebt(p.Debt);AdvanceEventRulesAndQueueFact(new QingzhongDebtConsumedEvent(f.Id,p.Debt,true));
  ReplaceRuntimeTop(f=f with{Qingzhong=p with{Stage=QingzhongStage.SwapChildren,TargetSeat=target,Before=before,After=before,ActualCount=target==f.OwnerSeat?0:ownerIds.Length+targetIds.Length,OwnerHandCount=ownerIds.Length,TargetHandCount=targetIds.Length},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  if(target!=f.OwnerSeat)MoveProgramCardsFromMultipleSources(ownerIds.Concat(targetIds).ToArray(),CardLocation.Hand(target),new(QingzhongSwapReason),destinationForCard:(_,location)=>CardLocation.Hand(location.OwnerSeat==f.OwnerSeat?target:f.OwnerSeat));
  f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{Qingzhong=f.Qingzhong!with{After=_movementSequence}});IssueQingzhongMovement(f);AdvanceRuntimeProgram(f.Id);
 }
 private void IssueQingzhongMovement(ProgramSkillFrame f)
 {var p=f.Qingzhong!;AdvanceEventRulesAndQueueFact(new QingzhongMovementIssuedEvent(f.Id,p.Stage,p.Before,p.After,p.ActualCount,p.TargetSeat,p.OwnerHandCount,p.TargetHandCount));}
 [ProgramContinuation(ProgramContinuationKind.Resume,23400)]
 private bool ResumeQingzhong(long id)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Qingzhong:{} p}f||f.Id!=id)return false;RequireIssuedLuZhiWei(f);
  if(p.Stage==QingzhongStage.ChooseMinimum){if(!_players[f.OwnerSeat].IsAlive){FinishQingzhong(f);return true;}if(_pendingDecision is null)PublishQingzhongMinimum(f);return true;}
  if(TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(id))return true;
  FinishQingzhong(f);return true;
 }
 [ProgramContinuation(ProgramContinuationKind.Movement,23400)]
 private bool ReturnQingzhongMovement(ProgramSkillFrame f)=>f.Qingzhong is not null&&ResumeQingzhong(f.Id);
 private void FinishQingzhong(ProgramSkillFrame f)
 {var p=f.Qingzhong!;AdvanceEventRulesAndQueueFact(new QingzhongCompletedEvent(f.Id,p.Debt,p.Stage,p.TargetSeat,_players[f.OwnerSeat].IsAlive));ReplaceRuntimeTop(f=f with{Qingzhong=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
 [ProgramCapability]
 private sealed class LuZhiWeiProgramCapability(GameEngine engine) : ILuZhiWeiProgramHost
 {
  public SkillProgramStepOutcome DrawAndArmQingzhongActualPlayDebt(ProgramSkillFrame f)=>engine.DrawAndArmQingzhongActualPlayDebt(f);
  public SkillProgramStepOutcome SettleQingzhongActualPlayDebt(ProgramSkillFrame f)=>engine.SettleQingzhongActualPlayDebt(f);
 }
}
