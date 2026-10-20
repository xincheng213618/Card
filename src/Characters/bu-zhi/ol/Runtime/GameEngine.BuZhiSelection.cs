namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class BuZhiSelectionQueries(GameEngine engine) : ISelectionRoleQueries
    {
        public Role Role(int seat) => engine._players[seat].Role;
    }

}
