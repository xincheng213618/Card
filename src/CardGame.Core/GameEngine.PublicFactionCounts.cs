namespace CardGame.Core;

public sealed partial class GameEngine
{
    // New public-count rules never read the hidden NationalFactionId into a
    // public event, game-usage key or native decision. Existing queries keep
    // their historical contracts.
    private string? GetPublicEffectiveFactionId(CharacterState player) =>
        IsNationalWarMode && !player.FactionRevealed || !IsNationalWarMode && !player.GeneralRevealed
            ? null : GetEffectiveFactionId(player);
    private int GetPublicLivingFactionCount() => _players.Where(p => p.IsAlive)
        .Select(GetPublicEffectiveFactionId).OfType<string>().Distinct(StringComparer.Ordinal).Count();
}
