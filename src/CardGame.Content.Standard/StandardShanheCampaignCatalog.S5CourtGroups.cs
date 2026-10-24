using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static ShanheCampaignDefinition WithS5CourtGroupEncounters(ShanheCampaignDefinition definition) => definition with
    {
        IntroText = definition.IntroText + "\n另有汉末双帝、贤侣虎将、狂傲荀粲的完整S5基础阵容，以及急功悍将、董氏凶僚两份同配4/4董卓。" +
            "刘协沿天命、密诏，刘辩按体力关系发动诗怨并保留毒逝、余威；孙坚与吴国太沿基础英魂、武烈、甘露、补益；" +
            "荀粲沿熨身、伤神、分钗，曹氏沿准备阶段雷劫与鬼道。投放、抽选及奖励均为本地编排。",
        Nodes = definition.Nodes.Select(node => node.Id switch
        {
            "dagu" => AddS5BaseLordGroup(node, "han-emperors", "汉末双帝",
                "采用主组2643完整刘协、刘辩双敌，各3/3。刘协保留天命、密诏；刘辩诗怨按来源体力摸三、二或一张牌，" +
                "保留毒逝，并以独立印刷资格在其他群势力角色整个实际回合内提升诗怨额度。",
                [new(StandardShanheS5HanEmperorsPackage.LiuxieId),
                 new(StandardShanheS5HanEmperorsPackage.LiubianId) { QualifiedPrintedLordSkillIds = ["ol:yuwei"] }]),
            "huanyuan" => AddS5BaseLordGroup(AddS5BaseLordGroup(node, "sunjian-wuguotai", "贤侣虎将",
                "采用主组2658完整孙坚4/4、吴国太3/3双敌。孙坚保留英魂、武烈，吴国太保留甘露、补益，不附加高难技能。",
                [new(StandardShanheS5SunJianWuguotaiPackage.SunjianId), new(StandardShanheS5SunJianWuguotaiPackage.WuguotaiId)]),
                "xuncan-caoshi", "狂傲荀粲",
                "采用主组2657完整荀粲、曹氏双敌，各3/3。熨身先回复再使用真实冰杀；伤神对本回合首次属性伤害判定并补手牌；" +
                "分钗根据首次技能目标的异性存亡改变判定花色，曹氏保留准备阶段雷劫与鬼道。",
                [new(StandardShanheS5XuncanCaoshiPackage.XuncanId), new(StandardShanheS5XuncanCaoshiPackage.CaoshiId)]),
            "luoyang" => AddS5BaseLordGroup(AddS5BaseLordGroup(node, "dongzhuo-2670", "急功悍将",
                "原表2670急功悍将与2671董氏凶僚均为同配的4/4董卓单敌。保留酒池、女性双向肉林、崩坏与逐点暴虐，" +
                "不以其他董卓形态的额外技能或体力替代。",
                [new(StandardShanheS5DongZhuoBasePackage.DongzhuoId) { QualifiedPrintedLordSkillIds = ["boundary:baonue-current"] }]),
                "dongzhuo-2671", "董氏凶僚",
                "采用原表2671独立战斗候选，与2670共享已核实的同一基础角色配置；不推断为变身或追加阶段。",
                [new(StandardShanheS5DongZhuoBasePackage.DongzhuoId) { QualifiedPrintedLordSkillIds = ["boundary:baonue-current"] }]),
            _ => node
        }).ToArray()
    };
}
