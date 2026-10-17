using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static IReadOnlyList<ShanheEncounterDefinition> WithS5ZhangliaoEncounter(
        IEnumerable<ShanheEncounterDefinition> encounters, int difficulty) => encounters.Append(
            new ShanheEncounterDefinition("hulao:zhangliao-s5-base", "文远之威 · 基础技能形态",
                [new(StandardShanheS5BossPackage.ZhangliaoBaseId, MaxHpBonus: difficulty - 1,
                    InitialHandBonus: difficulty - 1, DrawBonus: difficulty == 3 ? 1 : 0)], [],
                VictoryGold: 100, HealAfterVictory: 2)).ToArray();
}
