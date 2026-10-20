using System.Text.Json.Serialization;
namespace CardGame.Core;
internal sealed class ResolveJinghuaRoundDrawDescriptor:YuanJiDescriptor{
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger;public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveJinghuaRoundDraw;}
internal sealed class ResolveJinghuaTurnReturnDescriptor:YuanJiDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveJinghuaTurnReturn;}
