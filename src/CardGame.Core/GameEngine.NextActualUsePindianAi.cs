namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasNextActualUsePindianStructure(SkillProgramActivation activation) =>
        NextActualUsePindianSelectionAi.HasStructure(activation, GetProgramCardCategory);

}
