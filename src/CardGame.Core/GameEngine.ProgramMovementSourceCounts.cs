namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IEnumerable<(CardMovementSourceCount Count, bool DiscardOriginOnly)> ProgramMovementSourceCounts(
        CardMovementBatchContext batch, SkillProgramTriggerWindow window)
    {
        var actual = window == SkillProgramTriggerWindow.CardsGained ? batch.DestinationCounts ?? [] : batch.SourceCounts;
        foreach (var count in actual.Where(item => item.Location.OwnerSeat is not null)) yield return (count, false);
        if (window != SkillProgramTriggerWindow.CardsMoved) yield break;
        // Actual movement records stay unchanged. Only discard-specific entries
        // can read an original owner from a discard's two-stage processing path.
        var actualLocations = actual.Select(item => item.Location).ToHashSet();
        var origins = batch.Movements.Select(move => (Move: move, Source: GetProgramDiscardSource(move)))
            .Where(item => item.Source is { OwnerSeat: not null } && !actualLocations.Contains(item.Source.Value))
            .GroupBy(item => item.Source!.Value);
        foreach (var group in origins)
        {
            yield return (GetProgramDiscardOriginSourceCount(group.Key, group.Select(item => item.Move).ToArray()), true);
        }
    }
}
