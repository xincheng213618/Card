namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class ZhangJiSelectionQueries(GameEngine engine) : IZhangJiSelectionQueries
    {
        public bool Allows(int ownerSeat, int targetSeat, SkillProgramActivation activation) =>
            engine.ZhangJiActivationTargetAllowed(engine._players[ownerSeat], engine._players[targetSeat], activation);
    }

}
