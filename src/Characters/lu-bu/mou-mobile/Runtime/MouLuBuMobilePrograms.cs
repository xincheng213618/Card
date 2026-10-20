using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLuBuLiyuReceipt? MouLuBuLiyu { get; init; } }

internal interface IMouLuBuMobileHost
{ SkillProgramStepOutcome Bonus(ProgramSkillFrame f); SkillProgramStepOutcome Liyu(ProgramSkillFrame f); }
internal abstract class MouLuBuMobileDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
