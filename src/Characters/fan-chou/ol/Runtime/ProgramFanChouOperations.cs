using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum FanChouStage { Branch, Field, Deck, Other, OtherPayment, MovementChildren, DrawChildren }
public sealed record FanChouUseProof(long WindowId,long NativeParentId,int ActualTurn,int PhaseInstance,CardActionContext Action,ProgramCompletedResponseKind? ResponseKind);
public sealed record FanChouReceipt(CardConversionSource Source,string GameplayHash,int InstructionIndex,FanChouUseProof Use,FanChouStage Stage,string? Branch=null,int? Recipient=null,int? SelectedId=null,CardLocation? From=null,long Before=0,long After=0,int ActualCount=0,int? SelectedRank=null)
{
 private readonly IReadOnlyList<int> _deck=Array.AsReadOnly(Array.Empty<int>());
 public IReadOnlyList<int> DeckCandidates {get=>_deck;init=>_deck=Array.AsReadOnly(value.ToArray());}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public long? PaidBatchId {get;init;}
}
public sealed record XingluanBranchChosenEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,long WindowId,long ActionId,string Branch):IGameEvent;
public sealed record XingluanSegmentPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,long WindowId,long ActionId,string Branch,int SubjectSeat,long Before,long After,int ActualCount):IGameEvent
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public long? PaidBatchId {get;init;}
}
public sealed partial record ProgramSkillFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public FanChouReceipt? FanChou {get;init;}
}
internal interface IFanChouProgramHost{SkillProgramStepOutcome BeginFanChou(ProgramSkillFrame frame,SkillProgramEffect effect);}
internal sealed class ChooseXingluanRankSixBenefitDescriptor:ProgramOperationDescriptorBase
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ChooseXingluanRankSixBenefit;
 public override ISkillProgramEffectHandler Handler{get;}=new FanChouHandler();
 public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,_)=>{});
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)];
}
internal sealed class FanChouHandler:ISkillProgramEffectHandler
{
 public SkillProgramEffectOp Op=>SkillProgramEffectOp.ChooseXingluanRankSixBenefit;
 public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<IFanChouProgramHost>().BeginFanChou(f,e);
}
internal static class FanChouComposition
{
 internal static void Activation(string path,SkillProgramActivation a){if(ProgramInstructionResolver.Default.Features(a).HasOperation(SkillProgramEffectOp.ChooseXingluanRankSixBenefit))throw new InvalidOperationException(path+": Xingluan requires a completed native use.");}
 internal static void Trigger(string path,SkillProgramTrigger t)
 {
  if(!ProgramInstructionResolver.Default.Features(t).HasOperation(SkillProgramEffectOp.ChooseXingluanRankSixBenefit))return;
  if(t.Window!=SkillProgramTriggerWindow.CardUseCompleted||t.OwnerRelation!=SkillProgramCardActionOwnerRelation.Actor||!t.IncludeResponseUses||!t.Optional||t.UsageScope!=SkillUsageScope.Phase||t.UsageLimit!=1||t.CardKinds.Count!=0||t.CardCategories.Count!=0||t.Effects is not[{Op:SkillProgramEffectOp.ChooseXingluanRankSixBenefit,Condition.Kind:SkillProgramConditionKind.Always}])throw new InvalidOperationException(path+": Xingluan requires its exact optional completed-use actor window and one actual Play usage.");
 }
}
