using System.Text.Json.Serialization;

namespace CardGame.Core;
internal sealed class YanshuangDescriptor() : ZhouQunDescriptor(SkillProgramEffectOp.ResolveYanshuangEndingLoss){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.ZhouQun is { Paid: true };
}
