using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static ShanheCampaignDefinition WithS5CoalitionBossEncounters(ShanheCampaignDefinition definition) => definition with
    {
        IntroText = definition.IntroText + "\n另有S5幽燕双骑吕旷吕翔与檄文雄才陈琳的单敌基础形态。" +
            "齐攻承接真实杀被闪抵消后的追击，列侯在范围内收牌后交给另一人；笔伐暗寄手牌，颂词按目标手牌与体力比较发动。" +
            "原版来自小平津与河内，本地关隘位置、抽选和奖励为本地编排，不加入高难技能或装备。",
        Nodes = definition.Nodes.Select(node => node.Id switch
        {
            "hulao" => AddS5BaseBossEncounter(node, "lukuang-luxiang", "幽燕双骑", StandardShanheS5LukuangLuxiangPackage.LukuangLuxiangId,
                "吕旷吕翔联骑迎战，齐攻可在单目标杀被闪抵消后令一名角色使用无距离限制且不可响应的真实杀，" +
                "列侯可在攻击范围内先收一张手牌，再交给另一名角色。采用组2667的4/4基础形态。"),
            "huanyuan" => AddS5BaseBossEncounter(node, "chenlin", "檄文雄才", StandardShanheS5ChenlinPackage.ChenlinId,
                "陈琳以檄文试你胆识。笔伐在结束阶段暗置手牌，于目标下次实际回合开始时选择同类交换或失去体力；" +
                "颂词按手牌与体力的大小关系令目标摸牌或弃牌，每名目标限一次。采用组2672的3/3基础形态。"),
            _ => node
        }).ToArray()
    };
}
