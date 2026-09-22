namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int GetLivingFactionCount() =>
        _players
            .Where(player => player.IsAlive)
            .Select(GetEffectiveFactionId)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Count();
}
