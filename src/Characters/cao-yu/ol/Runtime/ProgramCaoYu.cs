using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum CaoYuStage { GongjieTargets,GongjieTake,GongjieGiftChildren,GongjieDrawChildren,XiangxuChoice,XiangxuDiscardChoice,XiangxuDrawChildren,XiangxuDiscardChildren,XiangxuRecoveryChildren,XiangzuRecipient,XiangzuCards,XiangzuGiftChildren,XiangzuRecoveryChildren }
public sealed record CaoYuMaterial(int CardId,CardKind Kind,CardLocation From,Suit Suit);
public sealed record CaoYuStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,long WindowId,int ActualTurn,int TurnOwner,int Round,long OpportunityId,long DyingId):IGameEvent;
public sealed record CaoYuTargetChoiceEvent(long FrameId,int Sequence,int Seat,bool Selected):IGameEvent;
public sealed record CaoYuTargetFrozenEvent(long FrameId,int Index,int Seat):IGameEvent;
public sealed record CaoYuMaterialFrozenEvent(long FrameId,CaoYuStage Stage,int PaymentIndex,int Index,int CardId,CardKind Kind,CardLocation From,Suit Suit):IGameEvent;
public sealed record CaoYuCardPaidEvent(long FrameId,CaoYuStage Stage,int Index,int CardId,CardKind Kind,CardLocation From,CardLocation To,Suit Suit,long Sequence,long BatchId):IGameEvent;
public sealed record CaoYuOxGrainFrozenEvent(long FrameId,CaoYuStage Stage,int PaymentIndex,int Index,int CardId):IGameEvent;
public sealed record CaoYuMovementPaidEvent(long FrameId,CaoYuStage Stage,int Index,int SubjectSeat,long Before,long After,int ActualCount):IGameEvent;
public sealed record CaoYuRecoveryIssuedEvent(long FrameId,CaoYuStage Stage,int Amount):IGameEvent;
public sealed record CaoYuCompletedEvent(long FrameId):IGameEvent;
public sealed record CaoYuReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,long WindowId,int ActualTurn,int TurnOwner,int Round,long OpportunityId,long DyingId,CaoYuStage Stage,
 IReadOnlyList<int> Targets,IReadOnlyList<int> Selected,IReadOnlyList<CaoYuMaterial> Materials,IReadOnlyList<int> GrainIds,int Index=0,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] int? Target=null,int Requested=0,int Recovery=0,bool HistoryQualified=false,bool Paid=false,long Before=0,long After=0,int Count=0)
{
 private IReadOnlyList<int> _targets=Array.AsReadOnly(Targets.ToArray()),_selected=Array.AsReadOnly(Selected.ToArray()),_grain=Array.AsReadOnly(GrainIds.ToArray());
 private IReadOnlyList<CaoYuMaterial> _materials=Array.AsReadOnly(Materials.ToArray());
 public IReadOnlyList<int> Targets{get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<int> Selected{get=>_selected;init=>_selected=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<int> GrainIds{get=>_grain;init=>_grain=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<CaoYuMaterial> Materials{get=>_materials;init=>_materials=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame {[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public CaoYuReceipt? CaoYu{get;init;}}
public sealed partial record ProgramSkillWindowContext {[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public long? CaoYuOpportunityId{get;init;}}
internal interface ICaoYuHost {SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal abstract class CaoYuDescriptor:ProgramOperationDescriptorBase
{
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
 public override ISkillProgramEffectHandler Handler=>new CaoYuHandler(Op);
 public override ProgramOperationInteraction Interaction=>Op==SkillProgramEffectOp.ResolveXiangxuMinimumTransition?ProgramOperationInteraction.Automatic:ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(Op switch{SkillProgramEffectOp.ResolveGongjieRoundFirstGifts=>SkillProgramTriggerWindow.RoundStarting,SkillProgramEffectOp.ResolveXiangxuMinimumTransition=>SkillProgramTriggerWindow.CardsMoved,SkillProgramEffectOp.ResolveXiangxuIssuedTurnEndAdjustment=>SkillProgramTriggerWindow.AfterTurnEnded,_=>SkillProgramTriggerWindow.DyingEntering})];
}
internal sealed class CaoYuHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler {public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost host)=>host.GetCapability<ICaoYuHost>().Resolve(f,op);}
internal static class CaoYuComposition
{
 internal static bool Handles(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ResolveGongjieRoundFirstGifts or SkillProgramEffectOp.ResolveXiangxuMinimumTransition or SkillProgramEffectOp.ResolveXiangxuIssuedTurnEndAdjustment or SkillProgramEffectOp.ResolveXiangzuDyingGiftAndRecovery;
 [ProgramCatalogValidation(610)]
 internal static void ValidateProgram(string path,SkillProgram p)
 {
  if(!p.Triggers.Any(t=>t.Effects.Any(e=>Handles(e.Op)))&&!p.Activations.Any(a=>a.Effects.Any(e=>Handles(e.Op))))return;
  if(p.Id is not("ol:gongjie"or"ol:xiangxu"or"ol:xiangzu")||p.Activations.Count!=0||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.HpSkillGrants.Count!=0||p.Triggers.Count!=(p.Id=="ol:xiangxu"?3:1))throw new InvalidOperationException(path+": CaoYu requires each complete ordinary-OL skill and exact trigger-only composition.");
  foreach(var t in p.Triggers)
  {
   var (id,window,op,optional)=p.Id switch{"ol:gongjie"=>("round-first",SkillProgramTriggerWindow.RoundStarting,SkillProgramEffectOp.ResolveGongjieRoundFirstGifts,true),"ol:xiangzu"=>("dying-gift",SkillProgramTriggerWindow.DyingEntering,SkillProgramEffectOp.ResolveXiangzuDyingGiftAndRecovery,true),_=>t.Id=="minimum-edge"?("minimum-edge",SkillProgramTriggerWindow.CardsMoved,SkillProgramEffectOp.ResolveXiangxuMinimumTransition,false):(t.Id=="end-own"?"end-own":"end-foreign",SkillProgramTriggerWindow.AfterTurnEnded,SkillProgramEffectOp.ResolveXiangxuIssuedTurnEndAdjustment,false)};
   if(t.Id!=id||t.Window!=window||t.Subject!=SkillProgramTriggerSubject.Owner||t.Optional!=optional||t.Priority!=0||t.UsageLimit is not null||t.MarkerCost is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.Effects is not[{ }e]||e.Op!=op||e.Target!=SkillProgramEffectTarget.Owner||e.Condition.Kind!=SkillProgramConditionKind.Always||window==SkillProgramTriggerWindow.AfterTurnEnded&&t.TurnOwnerScope!=(id=="end-own"?SkillProgramTurnOwnerScope.Own:SkillProgramTurnOwnerScope.OtherLiving)||window==SkillProgramTriggerWindow.CardsMoved&&(!t.SourceZones.SequenceEqual([CardZoneKind.Hand])||t.MovementOccurrence!=SkillProgramMovementOccurrence.PerBatch))throw new InvalidOperationException(path+": CaoYu lost an exact round, causal Hand transition, issued actual-turn-end or genuine dying producer.");
  }
 }
}
