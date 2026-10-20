using System.Text.Json.Serialization;
namespace CardGame.Core;
internal sealed class InitializeWuZhugeLiangJincuiDescriptor : WuZhugeLiangDescriptor {
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override SkillProgramEffectOp Op => SkillProgramEffectOp.InitializeWuZhugeLiangJincui;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class ResolveWuZhugeLiangJincuiDescriptor : WuZhugeLiangDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuZhugeLiangJincui;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
