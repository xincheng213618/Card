using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static ShanheCampaignDefinition WithS5HebeiBossEncounters(ShanheCampaignDefinition definition) => definition with
    {
        IntroText = definition.IntroText + "\n河北双雄、麹义与张郃另有S5专版基础候选。" +
            "双雄的异色决斗伤害随本轮发动次数确定；麹义封锁其他角色的响应，手牌并列最多时伤害随轮数增长；" +
            "张郃可暗置任意张手牌为旋，之后使用牌时移去旋并摸牌。原版高难装备和成长不作推定，位置、抽选及奖励为本地编排。",
        Nodes = definition.Nodes.Select(node => node.Id switch
        {
            "dagu" => AddS5BaseBossEncounter(node, "yanliang-wenchou", "河北双雄", StandardShanheS5YanliangWenchouPackage.YanliangWenchouId,
                "颜良文丑在摸牌阶段结束弃牌后，可将异色牌作专版决斗，本轮发动越多，本次决斗伤害越高；" +
                "结束阶段收取本回合曾对其造成伤害的真实牌。采用主组2665的4/4基础形态。"),
            "hulao" => AddS5BaseBossEncounter(node, "quyi", "凉州猛将", StandardShanheS5QuyiPackage.QuyiId,
                "麹义的伏骑令其他角色不能使用或打出牌响应他的用牌；骄恣在其手牌并列全场最多时，" +
                "令其造成或受到的伤害增加当前轮数。采用主组2666的4/4基础形态。"),
            "huanyuan" => AddS5BaseBossEncounter(node, "zhanghe", "俊乂张郃", StandardShanheS5ZhanghePackage.ZhangheId,
                "张郃在弃牌阶段开始可将任意张手牌暗置为旋，使用牌时移去旋并摸牌，" +
                "按是否手牌唯一最多及旋数量追加摸牌；没有普通周旋的五张上限或下个出牌阶段结束清理。采用主组2673的4/4基础形态。"),
            _ => node
        }).ToArray()
    };
}
