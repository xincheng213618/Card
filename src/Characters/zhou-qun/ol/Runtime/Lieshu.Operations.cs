using System.Text.Json.Serialization;

namespace CardGame.Core;
internal sealed class LieshuDescriptor() : ZhouQunDescriptor(SkillProgramEffectOp.ResolveLieshuEndingLoss){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.ZhouQun is { Paid: true };
}
