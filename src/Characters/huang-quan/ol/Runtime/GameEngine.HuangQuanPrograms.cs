using System.Globalization;
using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record DianhuRelation(long FrameId,CardConversionSource Source,string GameplayHash,int TargetSeat,long GameStartingWindowId);
public sealed record DianhuOccurrence(DianhuRelation Relation,bool Recovery,long NativeChangeId,long NativeParentId,int TargetSeat,int ActualAmount,int? SourceSeat);
public sealed record DianhuArmedEvent(DianhuRelation Relation):IGameEvent;
public sealed record DianhuQualifiedEvent(DianhuOccurrence Receipt):IGameEvent;
public sealed record HuangQuanDrawPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,string Kind,int RecipientSeat,int ActualTurn,int TurnOwner,long ActualPhase,long Before,long After,int CardCount,DianhuOccurrence? Occurrence):IGameEvent;
public sealed record HuangQuanCompletedEvent(long FrameId,CardConversionSource Source,string Kind,long? NativeActionId):IGameEvent;
public sealed record JianjiNativeReceipt(long ProgramFrameId,int InstructionIndex,CardConversionSource Source,string GameplayHash,int ActualTurn,int TurnOwner,long ActualPhase,int ActorSeat,int CardId,LegalAction Action,long UseId,long ActionId);
public sealed record JianjiUseIssuedEvent(JianjiNativeReceipt Receipt):IGameEvent;
public sealed record JianjiUseReturnedEvent(JianjiNativeReceipt Receipt):IGameEvent;
public sealed record HuangQuanPending(CardConversionSource Source,string GameplayHash,int InstructionIndex,string Kind,int RecipientSeat,int ActualTurn,int TurnOwner,long ActualPhase,long Before,long After,IReadOnlyList<int> CardIds,DianhuOccurrence? Occurrence,string Stage,JianjiNativeReceipt? NativeUse=null);
public sealed partial class CharacterState
{
 private IReadOnlyList<DianhuRelation> _dianhuRelations=Array.AsReadOnly(Array.Empty<DianhuRelation>());
 internal IReadOnlyList<DianhuRelation> DianhuRelations{get=>_dianhuRelations;set=>_dianhuRelations=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public HuangQuanPending? HuangQuan{get;init;}}
public sealed partial record ProgramSkillWindowContext
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public DianhuOccurrence? Dianhu{get;init;}}
public sealed partial record CardUseFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public JianjiNativeReceipt? JianjiUse{get;init;}}
public sealed partial class GameEngine
{

 private CardConversionSource HuangQuanSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
 private SkillProgramStepOutcome IssueHuangQuanDraw(ProgramSkillFrame f,int target,string kind,DianhuOccurrence? occurrence)
 {
  if(f.HuangQuan is not null)throw new InvalidOperationException("Huang Quan cannot repay an issued native draw.");var before=_movementSequence;var pending=new HuangQuanPending(HuangQuanSource(f),f.GameplayHash,f.InstructionIndex,kind,target,_turnNumber,_currentSeat,_cardUseDebitPhaseInstanceId,before,before,Array.AsReadOnly(Array.Empty<int>()),occurrence,"drawing");ReplaceRuntimeTop(f=f with{HuangQuan=pending,PendingMovementContinuation=new(target,0,null)});
  if(f.SkillId=="dragonboat:jianji"){ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null});return BeginOperationProgramDraw(f,target,1,new("program.jianji.draw"));}
  DrawProgramCards(f.Id,target,1,null,null,SkillProgramCardSetVisibility.Private,new("program."+kind+".draw"));
  var ids=_cardMovements.Where(m=>m.Sequence>before&&m.Reason.Value=="program."+kind+".draw"&&m.To==CardLocation.Hand(target)).Select(m=>m.CardId).ToArray();var after=_movementSequence;f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{HuangQuan=f.HuangQuan! with{After=after,CardIds=Array.AsReadOnly(ids),Stage="draw-paid"}});AdvanceEventRulesAndQueueFact(new HuangQuanDrawPaidEvent(f.Id,pending.Source,f.GameplayHash,kind,target,pending.ActualTurn,pending.TurnOwner,pending.ActualPhase,before,after,ids.Length,occurrence));AdvanceRuntimeProgram(f.Id);return SkillProgramStepOutcome.AwaitChild;
 }
 [ProgramContinuation(ProgramContinuationKind.Resume,24000)]
 private bool ResumeHuangQuan(long id)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{HuangQuan:{}p}f||f.Id!=id)return false;RequireIssuedHuangQuan(f);
  if(p.Stage=="draw-paid")
  {
   if(TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id))return true;
   f=GetActiveProgramFrame(id);p=f.HuangQuan!;ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null});
   if(p.Kind=="dianhu"||!_players[p.RecipientSeat].IsAlive||_winner!=Winner.None||p.CardIds.Count!=1){FinishHuangQuan(f);return true;}
   ReplaceRuntimeTop(f=f with{HuangQuan=p with{Stage="offer"}});PublishJianjiChoice(f);return true;
  }
  if(p.Stage=="offer"){if(_pendingDecision is null)PublishJianjiChoice(f);return true;}
  if(p.Stage=="returned"){FinishHuangQuan(f);return true;}
  return true;
 }
 [ProgramContinuation(ProgramContinuationKind.Movement,24000)]private bool ReturnHuangQuanMovement(ProgramSkillFrame f)=>f.HuangQuan is not null&&ResumeHuangQuan(f.Id);
 private void FinishHuangQuan(ProgramSkillFrame f)
 {if(FinishGuanduYuanlveUse(f))return;var p=f.HuangQuan!;AdvanceEventRulesAndQueueFact(new HuangQuanCompletedEvent(f.Id,p.Source,p.Kind,p.NativeUse?.ActionId));ReplaceRuntimeTop(f=f with{HuangQuan=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
}

public sealed record JianjiOrdinaryAlcoholPaidEvent(long NativeUseId,long ActionId,int ActualTurn,int ActorSeat,bool CountsQuota,long? JianjiProgramFrameId):IGameEvent;
public sealed partial class GameEngine
{
}

public sealed partial class GameEngine
{
 [ProgramCapability]
 private sealed class HuangQuanProgramCapability(GameEngine engine) : IHuangQuanProgramHost
 {
  public SkillProgramStepOutcome ArmDianhuRelation(ProgramSkillFrame f,int target)=>engine.ArmDianhuRelation(f,target);
  public SkillProgramStepOutcome DrawForDianhuActualOccurrence(ProgramSkillFrame f)=>engine.DrawForDianhuActualOccurrence(f);
  public SkillProgramStepOutcome DrawAndOfferJianjiExactCard(ProgramSkillFrame f,int target)=>engine.DrawAndOfferJianjiExactCard(f,target);
 }
}
