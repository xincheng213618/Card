namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasClassicGeneralPackage =>
        IsClassicIdentityMode &&
        _contentRegistry?.Packages.Any(package => package.Id == "standard-classic-generals") == true;

    private IReadOnlyList<string> AcquireRuntimeSkills(
        CharacterState player,
        string sourceSkillId,
        IReadOnlyList<string> skillIds)
    {
        if (_contentRegistry is null)
            throw new InvalidOperationException("Runtime skills require an active content registry.");

        var acquired = new List<string>();
        foreach (var skillId in skillIds.Distinct(StringComparer.Ordinal))
        {
            _ = _contentRegistry.GetSkill(skillId);
            if (!EnabledContentSkillIds(player).Contains(skillId, StringComparer.Ordinal))
                acquired.Add(skillId);
        }
        // A grant owns its source even if another source already supplies the skill.
        var sourceId = $"acquired:{sourceSkillId}";
        foreach (var skillId in skillIds.Distinct(StringComparer.Ordinal))
        {
            var grantId = $"{sourceId}:{skillId}";
            if (player.SkillGrants.Grants.Any(grant => grant.GrantId == grantId)) continue;
            player.SkillGrants.Grant(new SkillGrant(grantId, skillId, grantId, sourceId));
        }
        if (acquired.Count == 0) return [];
        foreach (var skillId in acquired)
            RegisterTaggedConversionSkill(player, skillId);
        QueueGameEvent(new SkillsAcquiredEvent(
            player.Seat,
            sourceSkillId,
            Array.AsReadOnly(acquired.ToArray())));
        return Array.AsReadOnly(acquired.ToArray());
    }

}
