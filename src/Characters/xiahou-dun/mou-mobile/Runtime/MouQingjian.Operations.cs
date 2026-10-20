using System.Text.Json.Serialization;
namespace CardGame.Core;
internal sealed class CollectMouXiahouDunQingjianDescriptor : MouXiahouDunMobileDescriptor {
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.NativeTrueUseProcessingEntry);
 public override SkillProgramEffectOp Op => SkillProgramEffectOp.CollectMouXiahouDunQingjian; }
internal sealed class DistributeMouXiahouDunQingjianDescriptor : MouXiahouDunMobileDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DistributeMouXiahouDunQingjian; }
