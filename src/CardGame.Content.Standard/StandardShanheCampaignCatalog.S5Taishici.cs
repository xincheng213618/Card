using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static ShanheNodeDefinition WithS5TaishiciEncounter(ShanheNodeDefinition node) => node with
    {
        StoryText = node.StoryText + "\n太史慈率军守关。斗缠在准备阶段逐张取得牌堆中的决斗；缺少决斗时，攻击范围与每个出牌阶段的出杀次数便会增长。" +
            "笃烈可使指向他的唯一目标基本牌或普通锦囊额外结算一次，再按结算后的攻击范围摸牌。" +
            "此候选采用S5组2632/22632的完整4/4基础双技；增长按本场累积并在失技后保留，属于本地期限裁定。",
        Description = node.Description + "另有笃烈之士太史慈：斗缠取得决斗或积累范围与出杀次数，笃烈以当前范围补牌。",
        Encounters = node.Encounters!.Append(new ShanheEncounterDefinition(
            "dagu:taishici-s5", "大谷关 · 太史慈（笃烈之士）",
            [new(StandardShanheS5TaishiciPackage.TaishiciId)], [],
            VictoryGold: 80, HealAfterVictory: 2)).ToArray()
    };

    private static bool RequiresTaishiciBossRegistry(ShanheBattleSetup setup) =>
        new[] { setup.PlayerGeneralId }.Concat(setup.AllyRoster.Concat(setup.EnemyRoster).Select(member => member.GeneralId))
            .Contains(StandardShanheS5TaishiciPackage.TaishiciId, StringComparer.Ordinal) ||
        setup.TacticSkillIds.Concat(setup.ExtraSkillIds)
            .Concat(setup.AllyRoster.Concat(setup.EnemyRoster).SelectMany(member => member.ExtraSkillIds ?? []))
            .Any(id => id is StandardShanheS5TaishiciPackage.DulieId or StandardShanheS5TaishiciPackage.DouchanId);
}
