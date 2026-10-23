using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static ShanheCampaignDefinition WithS5StrategyBossEncounters(ShanheCampaignDefinition definition) => definition with
    {
        IntroText = definition.IntroText + "\n另有S5悍将朱灵、年少风华周瑜、谋臣双璧许攸与沮授、双雄际会曹仁与赵云的基础候选。" +
            "朱灵在摸牌结束后按目标条件发动急陷，暴力增加来源伤害；周瑜以英姿、反间应战，许攸与沮授保留完整双敌阵容。" +
            "原版章节、难度成长与高难装备不作推定，本地位置、抽选及奖励另行编排。",
        Nodes = definition.Nodes.Select(node => node.Id switch
        {
            "hulao" => AddS5BaseBossEncounter(node, "zhuling", "悍将朱灵", StandardShanheS5ZhulingPackage.ZhulingId,
                "朱灵于摸牌阶段结束时，可对满足防具、技能更多、未受伤条件之一的合法目标发动真实杀，" +
                "并按发动时满足条件数摸牌；没有伤害原目标则失去1点体力。暴力令其造成的伤害+1。采用组2664的4/4基础形态。"),
            "huanyuan" => AddS5BaseBossPair(AddS5BaseBossEncounter(node, "zhouyu", "年少风华", StandardShanheS5ZhouyuPackage.ZhouyuId,
                "周瑜以英姿补充手牌，以反间令对手在弃牌与失去体力之间抉择。采用组2652的3/3基础形态。"),
                "caoren-zhaoyun", "双雄际会",
                "曹仁与赵云共守关道：伪溃、励战与SP龙胆、冲阵均沿普通技能。采用主组2678完整双敌，曹仁4/4、赵云3/3。",
                StandardShanheS5CaorenZhaoyunPackage.CaorenId, StandardShanheS5CaorenZhaoyunPackage.ZhaoyunId),
            "dagu" => AddS5StrategistPair(node),
            _ => node
        }).ToArray()
    };

    private static ShanheNodeDefinition AddS5StrategistPair(ShanheNodeDefinition node) =>
        AddS5BaseBossPair(node, "xuyou-jushou", "谋臣双璧",
            "许攸与沮授并肩设策：成略、恃才、寸目与渐营、矢北均沿各自普通技能。采用主组2661完整双敌阵容，各为3/3。",
            StandardShanheS5XuyouJushouPackage.XuyouId, StandardShanheS5XuyouJushouPackage.JushouId);

    private static ShanheNodeDefinition AddS5BaseBossPair(ShanheNodeDefinition node, string id, string name,
        string story, params string[] generalIds) => node with
    {
        StoryText = node.StoryText + "\n" + story + "三档无额外成长、固定牌或同行，位置、抽选及奖励为本地编排。",
        Description = node.Description + "另有S5基础双敌候选：" + name + "。",
        Encounters = node.Encounters!.Append(new ShanheEncounterDefinition(
            node.Id + ":" + id + "-s5", node.Name + " · " + name + "（基础形态）",
            generalIds.Select(generalId => new ShanheCombatantDefinition(generalId)).ToArray(),
            [], VictoryGold: 100, HealAfterVictory: 2)).ToArray()
    };
}
