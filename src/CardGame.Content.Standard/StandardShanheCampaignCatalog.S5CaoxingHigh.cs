using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static ShanheNodeDefinition WithS5CaoxingHighEncounters(ShanheNodeDefinition node, int difficulty)
    {
        if (difficulty < 3) return node;
        return node with
        {
            StoryText = node.StoryText + "\n曹性留牌愈多，骄恣愈盛；手牌独占最多时，他伤人更重，受伤也更重。" +
                "噬魂形态还能在杀命中直接目标后，随机获得对方一项自己尚未拥有的有效非锁定技能。",
            Description = node.Description + "另有本地骄恣、噬魂组合候选；夺牌后的手牌领先会改变伤害，噬魂所得技能保留至本场结束。",
            Encounters = node.Encounters!.Concat(new ShanheEncounterDefinition[]
            {
                new("guangcheng:caoxing-jiaozi-s5", "夺目一箭 · 曹性（骄恣·本地组合）",
                    [new(StandardShanheS5CaoxingHighPackage.CaoxingJiaoziId)], [], VictoryGold: 80, HealAfterVictory: 2),
                new("guangcheng:caoxing-shihun-s5", "夺目一箭 · 曹性（噬魂·本地组合）",
                    [new(StandardShanheS5CaoxingHighPackage.CaoxingShihunId)], [], VictoryGold: 80, HealAfterVictory: 2)
            }).ToArray()
        };
    }

    private static bool RequiresCaoxingHighRegistry(ShanheBattleSetup setup) =>
        new[] { setup.PlayerGeneralId }.Concat(setup.AllyRoster.Concat(setup.EnemyRoster).Select(m => m.GeneralId))
            .Any(id => id is StandardShanheS5CaoxingHighPackage.CaoxingJiaoziId or StandardShanheS5CaoxingHighPackage.CaoxingShihunId) ||
        setup.TacticSkillIds.Concat(setup.ExtraSkillIds)
            .Concat(setup.AllyRoster.Concat(setup.EnemyRoster).SelectMany(m => m.ExtraSkillIds ?? []))
            .Contains(StandardShanheS5CaoxingHighPackage.ShihunId, StringComparer.Ordinal);
}
