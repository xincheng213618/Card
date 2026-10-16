namespace CardGame.Core;

public sealed partial class GameEngine
{
    private List<GeneralDefinition[]> GetGeneralFamilies(IEnumerable<GeneralDefinition> generals)
    {
        var keys = _modeDefinition.GeneralVariantGroups?
            .SelectMany(group => group.Value.Select(id => (Id: id, Family: group.Key)))
            .ToDictionary(entry => entry.Id, entry => entry.Family, StringComparer.Ordinal);
        return generals.GroupBy(general => keys?.GetValueOrDefault(general.Id, general.Id) ?? general.Id,
                StringComparer.Ordinal).Select(group => group.ToArray()).ToList();
    }

    private int GeneralDraftWeight(GeneralDefinition general) =>
        _modeDefinition.GeneralSelectionWeights!.GetValueOrDefault(general.Id, 1);

    private IReadOnlyList<GeneralDefinition> DraftWeightedGeneralFamilies(
        IEnumerable<GeneralDefinition> generals, int count)
    {
        var families = GetGeneralFamilies(generals);
        var result = new List<GeneralDefinition>();
        while (result.Count < count && families.Count > 0)
        {
            // Max, rather than sum: extra editions must not buy extra family tickets.
            var weights = families.Select(family => family.Max(GeneralDraftWeight)).ToArray();
            var ticket = _random.NextDouble() * weights.Sum(weight => (double)weight);
            var index = 0;
            while (index < weights.Length - 1 && ticket >= weights[index]) ticket -= weights[index++];
            var family = families[index];
            var preferred = family.Where(general => GeneralDraftWeight(general) == weights[index]).ToArray();
            result.Add(preferred[_random.Next(preferred.Length)]);
            families.RemoveAt(index);
        }
        return result;
    }

    private IReadOnlyList<(GeneralDefinition General, string CandidateId)> GetWeightedGeneralOffers(
        CharacterState player, bool fullRoster)
    {
        // Full-roster inspection is stable and consumes no random numbers. Only
        // bounded offers draw afresh, so seats do not reuse one shuffled prefix.
        var count = _modeDefinition.RoleGeneralCandidateCounts?.GetValueOrDefault(
            player.Role.ToString(), _modeDefinition.GeneralCandidateCount) ?? _modeDefinition.GeneralCandidateCount;
        var candidates = fullRoster
            ? GetGeneralFamilies(_availableGenerals).Select(family => family.OrderByDescending(GeneralDraftWeight).First()).ToArray()
            : DraftWeightedGeneralFamilies(_availableGenerals, count);
        var families = GetGeneralFamilies(_availableGenerals)
            .SelectMany(family => family.Select(general => (general.Id, Family: family)))
            .ToDictionary(entry => entry.Id, entry => entry.Family, StringComparer.Ordinal);
        return candidates.SelectMany(candidate => new[] { candidate }.Concat(
                families[candidate.Id].Where(general => general.Id != candidate.Id)),
            (candidate, general) => (general, candidate.Id)).ToArray();
    }
}
