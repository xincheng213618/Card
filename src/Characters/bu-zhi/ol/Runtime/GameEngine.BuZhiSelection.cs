namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class BuZhiSelectionQueries(GameEngine engine) : IBuZhiSelectionQueries
    {
        public bool Allows(int targetSeat, SkillProgramActivation activation) =>
            engine.BuZhiActivationTargetAllowed(engine._players[targetSeat], activation);
    }

}
