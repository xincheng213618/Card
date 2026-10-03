namespace CardGame.Core;

public sealed partial class GameEngine
{
    // CardTargetCount contributions add to the card's existing maximum. Iron
    // Chain already permits two targets before any program contribution.
    private int GetProgramIronChainTargetLimit(CharacterState actor) =>
        checked(1 + ((FiniteRuleQueryValue)EvaluateCardTargetCount(actor, CardKind.IronChain).Value).Value);

    private IEnumerable<IReadOnlyList<int>> EnumerateProgramIronChainTargets(
        CharacterState actor, IReadOnlyList<CharacterState> candidates)
    {
        var ordered = candidates.OrderBy(player => player.Seat).ToArray();
        var maximum = Math.Min(GetProgramIronChainTargetLimit(actor), ordered.Length);
        for (var count = 1; count <= maximum; count++)
            foreach (var targets in Select(0, count, []))
                yield return targets;

        IEnumerable<IReadOnlyList<int>> Select(int start, int remaining, IReadOnlyList<int> selected)
        {
            if (remaining == 0)
            {
                yield return Array.AsReadOnly(selected.ToArray());
                yield break;
            }
            for (var index = start; index <= ordered.Length - remaining; index++)
                foreach (var targets in Select(index + 1, remaining - 1, [.. selected, ordered[index].Seat]))
                    yield return targets;
        }
    }

    private void AddProgramIronChainUseActions(
        ICollection<LegalAction> actions, CharacterState actor, Card physicalCard,
        IReadOnlyList<CharacterState> candidates, CardKind? playedCardKind = null,
        CardConversionSource? conversionSource = null)
    {
        foreach (var targets in EnumerateProgramIronChainTargets(actor, candidates))
        {
            var targetNames = string.Join("、", targets.Select(seat => _players[seat].Name));
            var description = playedCardKind is null
                ? $"对 {targetNames} 使用【铁索连环】"
                : $"将【{physicalCard.DisplayName}】当【铁索连环】对 {targetNames} 使用";
            actions.Add(new LegalAction(LegalActionKind.IronChain, physicalCard.Id,
                targets.Count == 1 ? targets[0] : null,
                description,
                PlayedCardKind: playedCardKind, TargetSeats: targets)
            { ConversionSource = conversionSource });
        }
    }
}
