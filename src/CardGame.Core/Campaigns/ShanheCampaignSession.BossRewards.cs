namespace CardGame.Core.Campaigns;

public sealed partial class ShanheCampaignSession
{
    private sealed record BossRewardGroupState(string Id, IReadOnlyList<string> OfferIds, string? SelectedRewardId = null);

    private RunState BeginBossRewards(RunState state, ShanheNodeDefinition node)
    {
        var groups = node.BossRewards!.Groups.Select(group =>
        {
            var offers = group.RewardIds.Where(id => CanApplyBossReward(state, _rewards[id]))
                .DistinctBy(id => RewardAcquisitionIdentity(_rewards[id])).ToList();
            var random = new DeterministicRandom(ShanheBattleSetup.DeriveSeed(_options.Seed,
                node.Id + ":boss:" + group.Id, state.EncounterOrdinal));
            random.Shuffle(offers);
            return new BossRewardGroupState(group.Id, ShanheCampaignContent.Freeze(offers.Take(group.ChoiceCount)));
        });
        return state with { Phase = ShanheCampaignPhase.Reward, RewardOfferIds = [], BossRewardGroups = ShanheCampaignContent.Freeze(groups) };
    }

    private bool CanApplyBossReward(RunState state, ShanheRewardDefinition reward)
    {
        if (!CanReceiveReward(state, reward)) return false;
        var replacements = reward.SkillId is not null && state.ExtraSkillIds.Count >= state.ExtraSkillSlots + reward.AddSkillSlots
            ? state.ExtraSkillIds.Cast<string?>() : new string?[] { null };
        foreach (var replacement in replacements)
        {
            try { _ = ApplyReward(state, reward, replacement); return true; }
            catch (Exception error) when (error is ArgumentException or OverflowException) { }
        }
        return false;
    }

    private RunState ChooseBossReward(ChooseShanheBossRewardCommand command)
    {
        Require(_state.Phase == ShanheCampaignPhase.Reward && _state.BossRewardGroups.Count > 0,
            "There is no chapter boss reward bundle.");
        var group = _state.BossRewardGroups.SingleOrDefault(group => group.Id == command.GroupId);
        Require(group is not null && group.SelectedRewardId is null && group.OfferIds.Contains(command.RewardId, StringComparer.Ordinal),
            "Choose one of this unclaimed boss group's frozen rewards.");
        var next = ApplyReward(_state, _rewards[command.RewardId], command.ReplaceSkillId);
        return next with
        {
            BossRewardGroups = ShanheCampaignContent.Freeze(_state.BossRewardGroups.Select(item =>
                item.Id == command.GroupId ? item with { SelectedRewardId = command.RewardId } : item))
        };
    }

    private RunState ConfirmBossReward()
    {
        Require(_state.Phase == ShanheCampaignPhase.Reward && _state.BossRewardGroups.Count > 0,
            "There is no chapter boss completion to confirm.");
        // Fully exhausted pools are explicitly reported by the snapshot, never silently rerolled.
        Require(_state.BossRewardGroups.All(group => group.SelectedRewardId is not null || group.OfferIds.Count == 0),
            "Every available boss reward group must be claimed before continuing.");
        return FinishNode(_state);
    }

    private ShanheBossRewardSummary? CreateBossRewardSummary(RunState state, long revision)
    {
        if (state.Phase != ShanheCampaignPhase.Reward || state.BossRewardGroups.Count == 0 ||
            state.CurrentNodeId is not { } id || _nodes[id].BossRewards is not { } bundle) return null;
        var node = _nodes[id];
        return new(node.ChapterNumber, node.ChapterTitle ?? node.Name, bundle.FixedGold, bundle.SkillSlots,
            ShanheCampaignContent.Freeze(state.BossRewardGroups.Select(group => new ShanheBossRewardGroupSnapshot(group.Id,
                bundle.Groups.Single(definition => definition.Id == group.Id).Name,
                ShanheCampaignContent.Freeze(group.OfferIds.Select(rewardId => _rewards[rewardId])),
                group.SelectedRewardId is { } selected ? _rewards[selected] : null))), revision);
    }
}
