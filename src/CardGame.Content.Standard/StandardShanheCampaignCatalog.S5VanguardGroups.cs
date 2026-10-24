using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static ShanheCampaignDefinition WithS5VanguardGroupEncounters(ShanheCampaignDefinition definition) => definition with
    {
        IntroText = definition.IntroText + "\n另有忠烈双卫、盟中夏侯、司马隐谋与黄巾余匪的完整S5基础阵容。" +
            "典韦按本轮强袭次数加伤，卫兹基础形态没有技能；夏侯惇红判反伤、黑判弃牌，夏侯渊按已跳阶段强化神速；" +
            "司马懿每点伤害或每张弃牌阶段手牌获得四忍，张牛角保留无技能基础身体。投放及奖励为本地编排。",
        Nodes = definition.Nodes.Select(node => node.Id switch
        {
            "dagu" => AddS5BaseLordGroup(node, "dianwei-weizi", "忠烈双卫",
                "采用主组2650原序典韦4/4、卫兹3/3双敌。强袭每个出牌阶段至多两次，真实失去体力或弃武器后，" +
                "按本轮已付发动次数加伤；保留狞恶，卫兹不提前获得中等难度技能。",
                [new(StandardShanheS5DianweiWeiziPackage.DianweiId), new(StandardShanheS5DianweiWeiziPackage.WeiziId)]),
            "hulao" => AddS5BaseLordGroup(node, "xiahou-pair", "盟中夏侯",
                "采用主组2651原序夏侯惇、夏侯渊双敌，各4/4。专版刚烈逐伤害点判定，红色对来源造成两点伤害、黑色弃其两张牌；" +
                "神速分别可跳判定与摸牌、跳出牌、跳弃牌并翻面，每次沿真实无距杀并按已跳阶段加伤。",
                [new(StandardShanheS5XiahouPairPackage.XiahoudunId), new(StandardShanheS5XiahouPairPackage.XiahouyuanId)]),
            "huanyuan" => AddS5BaseLordGroup(node, "zhangniujiao", "黄巾余匪",
                "采用主组2653张牛角3/3基础形态，无基础技能或固定牌，不预先授予中等及更高难度的技能。",
                [new(StandardShanheS5ZhangniujiaoPackage.ZhangniujiaoId)]),
            "luoyang" => AddS5BaseLordGroup(node, "simayi", "司马隐谋",
                "采用主组2677司马懿4/4基础形态，初始即拥有忍戒、拜印、连破、极略。每受一点伤害或弃牌阶段每弃一张手牌获得四忍；" +
                "拜印不附加普通版本的减体力上限代价，极略沿原版五项真实付款与技能流程。",
                [new(StandardShanheS5SimayiPackage.SimayiId)]),
            _ => node
        }).ToArray()
    };
}
