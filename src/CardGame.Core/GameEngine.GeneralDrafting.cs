namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool UsesFamilyGeneralDraft => _modeDefinition.AiGeneralSwapExcludedIds is not null ||
        _modeDefinition.GeneralSelectionWeights is not null;

    private List<GeneralDefinition[]> GetGeneralFamilies(IEnumerable<GeneralDefinition> generals)
    {
        var keys = _modeDefinition.GeneralVariantGroups?
            .SelectMany(group => group.Value.Select(id => (Id: id, Family: group.Key)))
            .ToDictionary(entry => entry.Id, entry => entry.Family, StringComparer.Ordinal);
        return generals.GroupBy(general => keys?.GetValueOrDefault(general.Id, general.Id) ?? general.Id,
                StringComparer.Ordinal).Select(group => group.ToArray()).ToList();
    }

    private IReadOnlyList<GeneralDefinition> DraftGeneralFamilies(
        IEnumerable<GeneralDefinition> generals, int count)
    {
        var families = GetGeneralFamilies(generals);
        var result = new List<GeneralDefinition>();
        while (result.Count < count && families.Count > 0)
        {
            // One ticket per person, then one equally likely exact edition.
            // Strength and extra editions never buy extra family tickets.
            var index = _random.Next(families.Count);
            var family = families[index];
            result.Add(family[_random.Next(family.Length)]);
            families.RemoveAt(index);
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private IReadOnlyList<(GeneralDefinition General, string CandidateId)> GetDraftGeneralOffers(
        CharacterState player, bool fullRoster)
    {
        var groups = GetGeneralFamilies(_availableGenerals);
        IReadOnlyList<GeneralDefinition> candidates;
        if (fullRoster)
            candidates = groups.Select(family => family[0]).ToArray();
        else
        {
            var count = _modeDefinition.RoleGeneralCandidateCounts?.GetValueOrDefault(
                player.Role.ToString(), _modeDefinition.GeneralCandidateCount) ?? _modeDefinition.GeneralCandidateCount;
            // The owning seat retains its exact draw until it commits this slot.
            candidates = player.GeneralDraftCandidates ??= DraftGeneralFamilies(_availableGenerals, count);
        }
        var families = groups
            .SelectMany(family => family.Select(general => (general.Id, Family: family)))
            .ToDictionary(entry => entry.Id, entry => entry.Family, StringComparer.Ordinal);
        var excluded = _modeDefinition.AiGeneralSwapExcludedIds;
        return candidates.SelectMany(candidate => new[] { candidate }.Concat(
                families[candidate.Id].Where(general => general.Id != candidate.Id &&
                    (player.IsHuman || excluded?.Contains(general.Id, StringComparer.Ordinal) != true))),
            (candidate, general) => (general, candidate.Id)).ToArray();
    }
}
