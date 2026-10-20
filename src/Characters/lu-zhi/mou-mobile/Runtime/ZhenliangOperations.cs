using System.Text.Json.Serialization;
namespace CardGame.Core;
internal sealed class ResolveMouLuZhiZhenliangYangDescriptor : MouLuZhiDescriptor {
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Activation;
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);
 public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouLuZhiZhenliangYang; }
internal sealed class ResolveMouLuZhiZhenliangYinDescriptor : MouLuZhiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouLuZhiZhenliangYin; }
