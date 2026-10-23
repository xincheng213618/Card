using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static ShanheCampaignDefinition WithS5LordGroupEncounters(ShanheCampaignDefinition definition) => definition with
    {
        IntroText = definition.IntroText + "\n另有S5代汉涂高、白马将军、白马从龙与桃园三英的完整基础阵容。" +
            "袁耀、袁术、袁胤以清忠、无脏、同疾协作；公孙瓒按攻击范围发动趫猛，由公孙越或赵云同行；" +
            "桃园三英沿界技能，张飞准备阶段的替身没有限定次数。原版高难成长、固定牌不作推定，位置、抽选及奖励为本地编排。",
        Nodes = definition.Nodes.Select(node => node.Id switch
        {
            "guangcheng" => AddS5BaseLordGroup(node, "yuanshu", "代汉涂高",
                "采用主组2654的袁耀3/3、袁术4/4、袁胤3/3完整三敌。两名随从于出牌阶段可摸牌再交换手牌；" +
                "袁术以无脏替代摸牌、手牌上限为零，同疾在其手牌多于体力且处于用杀者攻击范围内时限制杀的目标。",
                [new(StandardShanheS5YuanshuPackage.YuanyaoId), new(StandardShanheS5YuanshuPackage.YuanshuId), new(StandardShanheS5YuanshuPackage.YuanyinId)]),
            "dagu" => AddS5BaseLordGroup(node, "gongsun-zan-yue", "白马将军",
                "采用主组2662完整公孙瓒、公孙越双敌，各4/4。趫猛在杀指定目标后按攻击范围弃置区域牌；" +
                "义从改变双向距离，公孙越可弃牌失去体力救援其他队友。",
                [new(StandardShanheS5GongsunZanPackage.GongsunZanId), new(StandardShanheS5GongsunZanPackage.GongsunYueId)]),
            "huanyuan" => AddS5BaseLordGroup(AddS5BaseLordGroup(node, "gongsun-zan-zhaoyun", "白马从龙",
                "采用主组2681完整公孙瓒4/4、赵云3/3双敌。公孙瓒沿同一趫猛、义从基础形态，赵云使用SP龙胆、冲阵。",
                [new(StandardShanheS5GongsunZanPackage.GongsunZanId), new(StandardShanheS5CaorenZhaoyunPackage.ZhaoyunId)]),
                "taoyuan", "桃园三英",
                "采用主组2683按张飞、刘备、关羽顺序登场的完整三敌，各4/4。咆哮、仁德、激将、武圣、义绝沿界技能；" +
                "张飞可在准备阶段回复至体力上限并按实际回复量摸牌，替身不限整局一次。",
                [new(StandardShanheS5TaoyuanPackage.ZhangfeiId),
                 new(StandardShanheS5TaoyuanPackage.LiubeiId) { QualifiedPrintedLordSkillIds = ["boundary:jijiang"] },
                 new(StandardShanheS5TaoyuanPackage.GuanyuId)]),
            _ => node
        }).ToArray()
    };

    private static ShanheNodeDefinition AddS5BaseLordGroup(ShanheNodeDefinition node, string id, string name,
        string story, IReadOnlyList<ShanheCombatantDefinition> enemies) => node with
    {
        StoryText = node.StoryText + "\n" + story + "三档均无额外成长、固定牌或友方同行；位置、抽选及奖励为本地编排。",
        Description = node.Description + "另有S5完整基础阵容：" + name + "。",
        Encounters = node.Encounters!.Append(new ShanheEncounterDefinition(
            node.Id + ":" + id + "-s5", node.Name + " · " + name + "（基础形态）",
            enemies, [], VictoryGold: enemies.Count == 3 ? 120 : 100, HealAfterVictory: 2)).ToArray()
    };
}
