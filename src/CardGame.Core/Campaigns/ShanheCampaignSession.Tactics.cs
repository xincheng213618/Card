using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace CardGame.Core.Campaigns;

public sealed partial class ShanheCampaignSession
{
    private readonly IReadOnlyDictionary<string, CampaignTacticAcquisitionRule> _tacticAcquisitionRules;
    public string? TacticAcquisitionHash { get; }

    private IReadOnlyDictionary<string, CampaignTacticAcquisitionRule> CreateTacticAcquisitionRules(ContentRegistry registry) =>
        new ReadOnlyDictionary<string, CampaignTacticAcquisitionRule>(Definition.Rewards.Select(reward => reward.TacticSkillId)
            .OfType<string>().Distinct(StringComparer.Ordinal).ToDictionary(id => id,
                id => CampaignTacticAcquisitionRules.Resolve(registry.GetSkill(id)), StringComparer.Ordinal));

    private string? CreateTacticAcquisitionHash(ContentRegistry registry)
    {
        if (!_tacticAcquisitionRules.Keys.Any(id => registry.GetSkill(id).CampaignTacticAcquisition is not null)) return null;
        var rules = _tacticAcquisitionRules.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new { SkillId = pair.Key, pair.Value.IdentityId, pair.Value.Repeatable }).ToArray();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ShanheCampaignJson.SerializeValue(rules)))).ToLowerInvariant();
    }

    private bool CanAcquireTactic(RunState state, string skillId) =>
        _tacticAcquisitionRules[skillId].Repeatable || !HasTactic(state, skillId);

    private void ValidateBossAcquisitionGroups()
    {
        foreach (var node in Definition.Nodes.Where(node => node.BossRewards is not null))
        {
            var groups = new Dictionary<(string Kind, string Id), string>();
            foreach (var group in node.BossRewards!.Groups)
            foreach (var id in group.RewardIds)
            {
                var reward = _rewards[id];
                if (reward.TacticSkillId is { } tactic && _tacticAcquisitionRules[tactic] is { Repeatable: false } rule)
                    Check(("tactic", rule.IdentityId), group.Id);
                if (reward.SkillId is { } skill) Check(("skill", skill), group.Id);
            }

            void Check((string Kind, string Id) identity, string groupId)
            {
                Require(!groups.TryGetValue(identity, out var existing) || existing == groupId,
                    $"Boss '{node.Id}' exposes the same unique {identity.Kind} '{identity.Id}' in multiple reward groups.");
                groups[identity] = groupId;
            }
        }
    }

    private string RewardAcquisitionIdentity(ShanheRewardDefinition reward) => reward.TacticSkillId is { } tactic
        ? _tacticAcquisitionRules[tactic].IdentityId : reward.SkillId ?? reward.Id;
}
