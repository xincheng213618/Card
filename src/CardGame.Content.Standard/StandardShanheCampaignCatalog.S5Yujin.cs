using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static ShanheNodeDefinition WithS5YujinEncounter(ShanheNodeDefinition node) => node with
    {
        StoryText = node.StoryText + "\n于禁以严整军阵据守轘辕关。准备阶段，他可发动专版镇军弃置一名角色的牌，" +
            "随后选择自己弃牌或令原目标立即摸牌，两项数量均按实际弃置的非装备牌计算。" +
            "此候选采用当前S5组2648/22648的完整4/4基础形态，不附加固营、羽刃、固定装备或成长。",
        Description = node.Description + "另有毅重镇军于禁：准备阶段弃牌，再按非装备牌数自行弃牌或让原目标立即摸牌。",
        Encounters = node.Encounters!.Append(new ShanheEncounterDefinition(
            "huanyuan:yujin-s5", "轘辕关 · 毅重镇军（基础于禁）",
            [new(StandardShanheS5YujinPackage.YujinId)], [],
            VictoryGold: 80, HealAfterVictory: 2)).ToArray()
    };

    private static bool RequiresYujinBossRegistry(ShanheBattleSetup setup) =>
        new[] { setup.PlayerGeneralId }.Concat(setup.AllyRoster.Concat(setup.EnemyRoster).Select(member => member.GeneralId))
            .Contains(StandardShanheS5YujinPackage.YujinId, StringComparer.Ordinal) ||
        setup.TacticSkillIds.Concat(setup.ExtraSkillIds)
            .Concat(setup.AllyRoster.Concat(setup.EnemyRoster).SelectMany(member => member.ExtraSkillIds ?? []))
            .Contains(StandardShanheS5YujinPackage.ZhenjunId, StringComparer.Ordinal);
}
