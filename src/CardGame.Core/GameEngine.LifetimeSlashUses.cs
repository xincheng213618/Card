namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Native declarations exist before a character acquires a counting skill.
    // Accepted actions and physical materials are not additional uses: a Duel
    // response merely plays Slash, while Borrowed Sword starts a declared Use.
    private int LifetimeSlashUseCount(int seat)
    {
        if (!IsValidPlayerSeat(seat))
            throw new ArgumentOutOfRangeException(nameof(seat));
        var changes = ProgramEventHistory<ProgramCardUseActorReplacedEvent>()
            .GroupBy(fact => fact.CardUseFrameId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var count = 0;
        foreach (var group in ProgramEventHistory<CardUseDeclaredEvent>()
                     .Where(fact => IsSlashCard(fact.CardKind)).GroupBy(fact => fact.ResolutionId))
        {
            var declaration = group.First();
            if (!IsValidPlayerSeat(declaration.SourceSeat) || group.Any(fact =>
                    fact.SourceSeat != declaration.SourceSeat || fact.CardId != declaration.CardId ||
                    fact.CardKind != declaration.CardKind))
                throw new InvalidOperationException("A lifetime Slash use requires one consistent native declaration.");
            var actor = declaration.SourceSeat;
            if (changes.TryGetValue(declaration.ResolutionId, out var replacements))
            {
                int? provider = null;
                foreach (var replacement in replacements)
                {
                    if (replacement.PreviousActorSeat != actor || replacement.OwnerSeat != actor ||
                        !IsValidPlayerSeat(replacement.ActorSeat) || !IsValidPlayerSeat(replacement.ProviderSeat) ||
                        provider is { } originalProvider && originalProvider != replacement.ProviderSeat)
                        throw new InvalidOperationException("A lifetime Slash use lost its native actor-replacement chain.");
                    actor = replacement.ActorSeat;
                    provider = replacement.ProviderSeat;
                }
            }
            if (actor == seat) count = checked(count + 1);
        }
        return count;
    }
}
