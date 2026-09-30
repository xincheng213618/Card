namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool CanPayProgramMarkerCost(CharacterState owner, SkillProgramMarkerCost? cost) =>
        cost is null || owner.Markers.GetValueOrDefault(cost.Marker) >= cost.Amount;

    // Marker costs are paid only after a legal action/trigger has been accepted.
    // Consume attributed sources in stable order so aggregate and source counts stay equal.
    private void PayProgramMarkerCost(CharacterState owner, SkillProgramMarkerCost? cost,
        string skillId, string bindingId)
    {
        if (cost is null) return;
        if (!CanPayProgramMarkerCost(owner, cost))
            throw new InvalidOperationException("The configured marker cost is no longer affordable.");
        var sources = owner.MarkerSourceCounts.Where(pair => pair.Key.Marker == cost.Marker)
            .OrderBy(pair => pair.Key.SkillOwnerSeat).ToArray();
        if (sources.Sum(pair => pair.Value) != owner.Markers.GetValueOrDefault(cost.Marker))
            throw new InvalidOperationException("Attributed marker totals are inconsistent.");
        var remaining = cost.Amount;
        foreach (var source in sources)
        {
            var paid = Math.Min(remaining, source.Value);
            if (paid == 0) continue;
            if (paid == source.Value) owner.MarkerSourceCounts.Remove(source.Key);
            else owner.MarkerSourceCounts[source.Key] = source.Value - paid;
            var count = owner.Markers[cost.Marker] - paid;
            if (count == 0) owner.Markers.Remove(cost.Marker);
            else owner.Markers[cost.Marker] = count;
            QueueGameEvent(new PlayerMarkerChangedEvent(
                ++_resolutionSequence, owner.Seat, cost.Marker, -paid, count,
                source.Key.SkillOwnerSeat, $"skill-program.{skillId}.{bindingId}.cost"));
            remaining -= paid;
            if (remaining == 0) break;
        }
    }
}
