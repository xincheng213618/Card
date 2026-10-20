using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static ShanheNodeDefinition WithS5CaocaoEncounter(ShanheNodeDefinition node) => node with
    {
        StoryText = node.StoryText + "\n俊彦曹操守在轘辕关前。每受到1点伤害，他都可以发动专版归心：" +
            "逐人随机取得其他角色一个非空手牌区、装备区或判定区中的全部牌，随后翻面。" +
            "此候选采用当前S5组2649/22649的完整3/3基础形态，不附加飞影、固定装备或成长；非空区域的筛选按本地裁定。",
        Description = node.Description + "另有俊彦曹操：专版归心随机取得其他角色一个非空区域的所有牌，再翻面。",
        Encounters = node.Encounters!.Append(new ShanheEncounterDefinition(
            "huanyuan:caocao-s5", "轘辕关 · 俊彦曹操（基础归心）",
            [new(StandardShanheS5CaocaoPackage.CaocaoId)], [],
            VictoryGold: 80, HealAfterVictory: 2)).ToArray()
    };

    private static bool RequiresCaocaoBossRegistry(ShanheBattleSetup setup) =>
        new[] { setup.PlayerGeneralId }.Concat(setup.AllyRoster.Concat(setup.EnemyRoster).Select(member => member.GeneralId))
            .Contains(StandardShanheS5CaocaoPackage.CaocaoId, StringComparer.Ordinal) ||
        setup.TacticSkillIds.Concat(setup.ExtraSkillIds)
            .Concat(setup.AllyRoster.Concat(setup.EnemyRoster).SelectMany(member => member.ExtraSkillIds ?? []))
            .Contains(StandardShanheS5CaocaoPackage.GuixinId, StringComparer.Ordinal);
}
