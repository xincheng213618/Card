namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<RuleQueryContribution> CollectOtherPublicTeamNumericRuleContributions(
        CharacterState recipient, SkillRuleQuery query)
    {
        // Only bindings are cached. Public relationships and life state belong
        // to this query, before the native producer freezes its requested count.
        if (query != SkillRuleQuery.DrawCount || !recipient.IsAlive ||
            !recipient.TeamRevealed || recipient.TeamId is null)
            return Array.Empty<RuleQueryContribution>();

        var contributions = new List<RuleQueryContribution>();
        foreach (var owner in _players)
        {
            if (!OtherRevealedPublicTeamAlly(owner, recipient)) continue;
            var bindings = GetSkillBindingShard(owner).GetNumericModifiers(query)
                .Where(binding => binding.Modifier.RecipientScope == SkillRuleRecipientScope.OtherPublicTeamAllies)
                .ToArray();
            if (bindings.Length == 0) continue;

            // Owner stays the actual skill holder, so identities from separate
            // owners and separate acquired instances cannot collapse together.
            var context = new SkillProgramRuleContext(CreateSkillContext(owner), GetLivingFactionCount());
            contributions.AddRange(SkillProgramRules.CollectIndexedContributions(
                query, context, bindings, SkillRuleRecipientScope.OtherPublicTeamAllies));
        }
        return Array.AsReadOnly(contributions.ToArray());
    }
}
