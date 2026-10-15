namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class LvKuangLvXiangSelectionQueries(GameEngine engine) : ILvKuangLvXiangSelectionQueries
    {
        public bool Allows(int ownerSeat, int targetSeat, SkillProgramActivation activation) =>
            engine.LvKuangLvXiangActivationTargetAllowed(engine._players[targetSeat], activation, ownerSeat);
    }

}
