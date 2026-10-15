namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class HeQiSelectionQueries(GameEngine engine) : IHeQiSelectionQueries
    {
        public bool Allows(int ownerSeat, int targetSeat, SkillProgramActivation activation) =>
            engine.HeQiActivationTargetAllowed(engine._players[ownerSeat], engine._players[targetSeat], activation);
    }

}
