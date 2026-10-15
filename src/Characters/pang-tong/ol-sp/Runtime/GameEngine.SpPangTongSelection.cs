namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class SpPangTongSelectionQueries(GameEngine engine) : ISpPangTongSelectionQueries
    {
        public bool Allows(int targetSeat, SkillProgramActivation activation) =>
            engine.SpPangTongActivationTargetAllowed(engine._players[targetSeat], activation);
    }

}
