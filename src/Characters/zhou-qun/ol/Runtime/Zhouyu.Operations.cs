using System.Text.Json.Serialization;

namespace CardGame.Core;
internal sealed class ZhouyuNeighborsDescriptor() : ZhouQunDescriptor(SkillProgramEffectOp.ResolveZhouyuThunderNeighbors){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.ZhouQun is { Paid: true };
}
