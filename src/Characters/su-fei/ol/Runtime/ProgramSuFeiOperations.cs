using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum SuFeiStage { DrawChildren, ChoosingGift, GiftChildren }
public sealed record SuFeiActualUse(int ActualTurn,int PhaseInstance,long NativeUseId,CardActionContext Action,IReadOnlyList<int> PrimaryTargets)
{
 private readonly IReadOnlyList<int> _targets=Array.AsReadOnly(PrimaryTargets.ToArray());
 public IReadOnlyList<int> PrimaryTargets {get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
}
public sealed record SuFeiConsecutiveQualification(SuFeiActualUse Previous,SuFeiActualUse Current,IReadOnlyList<int> CommonTargets)
{
 private readonly IReadOnlyList<int> _common=Array.AsReadOnly(CommonTargets.ToArray());
 public IReadOnlyList<int> CommonTargets {get=>_common;init=>_common=Array.AsReadOnly(value.ToArray());}
}
public sealed record SuFeiReceipt(CardConversionSource Source,string GameplayHash,int InstructionIndex,long WindowId,SuFeiConsecutiveQualification Qualification,SuFeiStage Stage,long DrawBefore=0,long DrawAfter=0,int? DrawnId=null,int ActualDraw=0,int? GiftTarget=null,long GiftBefore=0,long GiftAfter=0);
public sealed record SuFeiActualUseRecordedEvent(SuFeiActualUse Use):IGameEvent;
public sealed record LianpianDrawPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,long NativeUseId,long ActionId,long Before,long After,int ActualCount):IGameEvent;
public sealed record LianpianGiftPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,long NativeUseId,long ActionId,int TargetSeat,long Before,long After):IGameEvent;
public sealed partial class CharacterState
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public SuFeiActualUse? SuFeiLastUse {get;set;}
}
public sealed partial record CardUseFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public SuFeiConsecutiveQualification? SuFeiQualification {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public SuFeiActualUse? SuFeiFinalizedUse {get;init;}
}
public sealed partial record ProgramSkillFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public SuFeiReceipt? SuFei {get;init;}
}
internal interface ISuFeiProgramHost {SkillProgramStepOutcome BeginSuFei(ProgramSkillFrame frame,SkillProgramEffect effect);}
internal sealed class DrawThenOfferActualConsecutiveTargetGiftDescriptor:ProgramOperationDescriptorBase
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.DrawThenOfferActualConsecutiveTargetGift;
 public override ISkillProgramEffectHandler Handler{get;}=new SuFeiHandler();
 public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,_)=>{});
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];
}
internal sealed class SuFeiHandler:ISkillProgramEffectHandler
{
 public SkillProgramEffectOp Op=>SkillProgramEffectOp.DrawThenOfferActualConsecutiveTargetGift;
 public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<ISuFeiProgramHost>().BeginSuFei(f,e);
}
internal static class SuFeiComposition
{
 internal static void Activation(string path,SkillProgramActivation a){if(ProgramInstructionResolver.Default.Features(a).HasOperation(SkillProgramEffectOp.DrawThenOfferActualConsecutiveTargetGift))throw new InvalidOperationException(path+": Lianpian requires a real finalized native Use.");}
 internal static void Trigger(string path,SkillProgramTrigger t)
 {
  if(!ProgramInstructionResolver.Default.Features(t).HasOperation(SkillProgramEffectOp.DrawThenOfferActualConsecutiveTargetGift))return;
  if(t.Window!=SkillProgramTriggerWindow.CardUseTargetsFinalized||t.Effects is not[{Op:SkillProgramEffectOp.DrawThenOfferActualConsecutiveTargetGift,Condition.Kind:SkillProgramConditionKind.Always}]||!t.Optional||t.OwnerRelation!=SkillProgramCardActionOwnerRelation.Actor||t.UsageScope!=SkillUsageScope.Turn||t.UsageLimit!=3||t.CardKinds.Count!=0||t.CardCategories.Count!=0)throw new InvalidOperationException(path+": Lianpian requires one optional actor native finalized Use with an actual-turn quota of three.");
 }
}
