using System.Text.Json.Serialization;
namespace CardGame.Core;
internal sealed class ResolveShuiyueTurnDrawDescriptor:YuanJiDescriptor{
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger;public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveShuiyueTurnDraw;}
internal sealed class ResolveShuiyueRoundReturnDescriptor:YuanJiDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveShuiyueRoundReturn;}
