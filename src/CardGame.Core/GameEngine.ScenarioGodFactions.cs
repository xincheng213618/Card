namespace CardGame.Core;

public sealed partial class GameEngine
{
    // A recipe default is only a fallback. It never creates a player choice or a native declaration.
    private string? GetScenarioGodFaction(CharacterState player) =>
        string.Equals(player.General.FactionId, "god", StringComparison.Ordinal)
            ? _options.ShanheBattle?.DefaultGodFactionId
            : null;
}
