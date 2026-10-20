using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record BeiMiHuTargetMutation(long ProgramFrameId,long CardUseId,long ActionId,CardConversionSource Source,string GameplayHash,int Turn,string Kind)
{
    public CardActionContext OriginalAction {get;init;}=null!;
    private IReadOnlyList<int> _before=Array.Empty<int>(),_after=Array.Empty<int>(),_paid=Array.Empty<int>();
    public IReadOnlyList<int> BeforeTargets {get=>_before;init=>_before=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<int> AfterTargets {get=>_after;init=>_after=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<int> PaidMarkerSeats {get=>_paid;init=>_paid=Array.AsReadOnly(value.ToArray());}
}
public sealed record BeiMiHuTargetsPaidEvent(BeiMiHuTargetMutation Receipt):IGameEvent;
internal sealed class ConsumeKuiCancelSoleIncomingTargetDescriptor():BeiMiHuDescriptor(SkillProgramEffectOp.ConsumeKuiCancelSoleIncomingTarget)
{
 public override ProgramContextCapability RequiredCapabilities=>ProgramContextCapability.CardAction;
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];
}
internal sealed class ConsumeKuiAppendLegalTargetsDescriptor():BeiMiHuDescriptor(SkillProgramEffectOp.ConsumeKuiAppendLegalTargets)
{
 public override ProgramContextCapability RequiredCapabilities=>ProgramContextCapability.CardAction;
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized),new RequireCardActionActor()];
}
