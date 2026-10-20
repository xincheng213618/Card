using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static ShanheNodeDefinition WithS5HuaxiongEncounter(ShanheNodeDefinition node) => node with
    {
        StoryText = node.StoryText + "\n华雄横刀拦关，耀武扬威。红色牌伤会增加他的体力上限，其他颜色的牌伤则令他先回复体力；" +
            "他还能主动邀你决斗，每个出牌阶段可发动的次数等于当前轮数。越久攻不下，他的攻势便越密。" +
            "此候选采用当前S5新手组22647的完整基础双技与3/3体力，不附加主组的高难技能或固定装备。",
        Description = node.Description + "另有虎牢双技华雄：耀武按牌色增体力上限或回复，势斩按当前轮数主动使用决斗。",
        Encounters = node.Encounters!.Append(new ShanheEncounterDefinition(
            "hulao:huaxiong-newplayer-s5", "虎牢关 · 华雄（22647基础双技）",
            [new(StandardShanheS5HuaxiongPackage.HuaxiongId)], [],
            VictoryGold: 100, HealAfterVictory: 2)).ToArray()
    };

    private static bool RequiresHuaxiongBossRegistry(ShanheBattleSetup setup) =>
        new[] { setup.PlayerGeneralId }.Concat(setup.AllyRoster.Concat(setup.EnemyRoster).Select(member => member.GeneralId))
            .Contains(StandardShanheS5HuaxiongPackage.HuaxiongId, StringComparer.Ordinal) ||
        setup.TacticSkillIds.Concat(setup.ExtraSkillIds)
            .Concat(setup.AllyRoster.Concat(setup.EnemyRoster).SelectMany(member => member.ExtraSkillIds ?? []))
            .Any(id => id is StandardShanheS5HuaxiongPackage.YaowuId or StandardShanheS5HuaxiongPackage.ShizhanId);
}
