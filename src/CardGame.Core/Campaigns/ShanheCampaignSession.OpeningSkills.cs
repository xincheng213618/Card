namespace CardGame.Core.Campaigns;

public sealed partial class ShanheCampaignSession
{
    private RunState BeginOpeningSkillDraft(RunState state)
    {
        var drafts = state.TacticIds.Where(id => _rewards[id].OpeningSkillRewardIds is not null).ToArray();
        if (drafts.Length == 0) return state;
        Require(state.ExtraSkillIds.Count + drafts.Length <= state.ExtraSkillSlots,
            "Initial skill choices require an available extra skill slot for each grant.");
        return PublishOpeningSkillOffer(state with
        {
            OpeningDraftTacticIds = ShanheCampaignContent.Freeze(drafts),
            OpeningResumeNodeIds = state.AvailableNodeIds, AvailableNodeIds = [],
            Phase = ShanheCampaignPhase.OpeningSkillDraft
        });
    }

    private RunState PublishOpeningSkillOffer(RunState state)
    {
        var tactic = _rewards[state.OpeningDraftTacticIds[0]];
        // An initial inheritance skill is granted by the tactic itself. The ordinary
        // codex unlock filter still applies to every later battle/shop reward.
        var candidates = tactic.OpeningSkillRewardIds!.Where(id => !HasSkill(state, _rewards[id].SkillId!))
            .DistinctBy(id => _rewards[id].SkillId).ToList();
        Require(candidates.Count > 0, "No new skill remains in this initial tactic's pool.");
        var random = new DeterministicRandom(ShanheBattleSetup.DeriveSeed(_options.Seed,
            Definition.Id + ":opening:" + tactic.Id, 0));
        random.Shuffle(candidates);
        return state with { OpeningSkillOfferIds = ShanheCampaignContent.Freeze(candidates.Take(3)) };
    }

    private RunState ChooseOpeningSkill(ChooseShanheOpeningSkillCommand command)
    {
        Require(_state.Phase == ShanheCampaignPhase.OpeningSkillDraft &&
            _state.OpeningSkillOfferIds.Contains(command.RewardId, StringComparer.Ordinal),
            "Choose one of the frozen initial skill offers.");
        var next = ApplyReward(_state, _rewards[command.RewardId], null, allowLockedOpeningSkill: true) with
        { OpeningDraftTacticIds = ShanheCampaignContent.Freeze(_state.OpeningDraftTacticIds.Skip(1)), OpeningSkillOfferIds = [] };
        return next.OpeningDraftTacticIds.Count > 0 ? PublishOpeningSkillOffer(next) : next with
        {
            Phase = ShanheCampaignPhase.Map, AvailableNodeIds = next.OpeningResumeNodeIds, OpeningResumeNodeIds = []
        };
    }
}
