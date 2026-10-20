using System.Text.Json.Serialization;
namespace CardGame.Core;
internal sealed class ResolveMouXuHuangDuanliangDescriptor : MouXuHuangDescriptor
{
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouXuHuangDuanliang;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
