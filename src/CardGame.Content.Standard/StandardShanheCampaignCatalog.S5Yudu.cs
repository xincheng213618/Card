using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static readonly string[] S5YuduSkillIds =
        [StandardShanheS5YuduPackage.CuiduId, StandardShanheS5YuduPackage.ZhongduId, StandardShanheS5YuduPackage.NiepanId];

    private static ShanheNodeDefinition WithS5YuduEncounter(ShanheNodeDefinition node) => node with
    {
        StoryText = node.StoryText + "\n于毒率黑山匪众拦住关道。他造成伤害后会令敌方中毒，并按当前轮数摸牌。" +
            "中毒者准备阶段须判定：方块解毒，否则失去当前轮数点体力。于毒自己也初始持有中毒，濒死时可发动一次涅槃。" +
            "此候选采用当前S5组2633/22633的完整3/3基础形态，不附加高难固定牌或成长。",
        Description = node.Description + "另有黑山匪害于毒：伤害施毒，准备阶段判定解毒或失去体力，濒死可涅槃一次。",
        Encounters = node.Encounters!.Append(new ShanheEncounterDefinition(
            "dagu:yudu-s5", "大谷关 · 黑山匪害（基础于毒）",
            [new(StandardShanheS5YuduPackage.YuduId)], [],
            VictoryGold: 80, HealAfterVictory: 2)).ToArray()
    };

    private static bool RequiresYuduBossRegistry(ShanheBattleSetup setup) =>
        new[] { setup.PlayerGeneralId }.Concat(setup.AllyRoster.Concat(setup.EnemyRoster).Select(member => member.GeneralId))
            .Contains(StandardShanheS5YuduPackage.YuduId, StringComparer.Ordinal) ||
        setup.TacticSkillIds.Concat(setup.ExtraSkillIds)
            .Concat(setup.AllyRoster.Concat(setup.EnemyRoster).SelectMany(member => member.ExtraSkillIds ?? []))
            .Any(id => S5YuduSkillIds.Contains(id, StringComparer.Ordinal));
}
