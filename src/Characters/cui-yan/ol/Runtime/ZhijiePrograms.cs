using System.Text.Json.Serialization;
namespace CardGame.Core;
internal sealed class ResolveZhijieActualTurnEndDescriptor():CuiYanDescriptor(SkillProgramEffectOp.ResolveZhijieActualTurnEnd,SkillProgramTriggerWindow.AfterTurnEnded) {
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger; }
