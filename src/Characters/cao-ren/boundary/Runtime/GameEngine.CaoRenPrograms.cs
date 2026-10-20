namespace CardGame.Core;

public sealed partial class GameEngine
{

    [ProgramCapability]
    private sealed class CaoRenProgramCapability(GameEngine engine) : ICaoRenProgramHost
    {
        public SkillProgramStepOutcome DiscardHandOrUseEquipment(ProgramSkillFrame frame) =>
            engine.BeginDiscardHandOrUseEquipment(frame);

        public SkillProgramStepOutcome MoveFieldEquipment(ProgramSkillFrame frame) =>
            engine.BeginMoveFieldEquipment(frame);
    }
}
