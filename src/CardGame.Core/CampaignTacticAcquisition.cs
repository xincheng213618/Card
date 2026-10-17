namespace CardGame.Core;

/// <summary>Explicit campaign acquisition identity. Presentation names never determine stacking.</summary>
public sealed record CampaignTacticAcquisitionRule(string IdentityId, bool Repeatable = false);

internal static class CampaignTacticAcquisitionRules
{
    internal static CampaignTacticAcquisitionRule Resolve(ContentSkillDefinition skill) =>
        skill.CampaignTacticAcquisition ?? new(skill.Id);

    internal static void Validate(IReadOnlyDictionary<string, ContentSkillDefinition> skills)
    {
        foreach (var skill in skills.Values)
        {
            if (skill.CampaignTacticAcquisition is not { } rule) continue;
            _ = new ContentId(rule.IdentityId);
            if (skill.ImplementationStatus != SkillImplementationStatus.Complete || skill.Program is null ||
                !skills.TryGetValue(rule.IdentityId, out var canonical) || canonical.CampaignTacticAcquisition is not { } identity ||
                identity.IdentityId != canonical.Id || identity.Repeatable != rule.Repeatable ||
                canonical.ImplementationStatus != SkillImplementationStatus.Complete || canonical.Program is null)
                throw new InvalidOperationException($"Campaign tactic '{skill.Id}' requires a complete canonical program and consistent acquisition rule.");
        }
    }
}
