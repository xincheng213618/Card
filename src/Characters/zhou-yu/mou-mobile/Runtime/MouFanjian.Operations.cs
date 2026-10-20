using System.Text.Json.Serialization;
namespace CardGame.Core;
internal sealed class ResolveMouZhouYuFanjianDescriptor : MouZhouYuMobileDescriptor {
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Activation;
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouZhouYuFanjian;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
