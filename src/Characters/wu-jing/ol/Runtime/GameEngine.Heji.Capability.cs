namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasWuJingHeji => _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.UseHejiAtCompletedSingleTarget) == true;
}
