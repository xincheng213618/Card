namespace CardGame.Core;
public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost
    {
        public SkillProgramStepOutcome ExecuteAdvancedLifecycle(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat) =>
            engine.ExecuteAdvancedLifecycle(effect, frame, targetSeat);
    }
}
