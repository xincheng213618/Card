using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static ShanheCampaignDefinition WithS5ArmedSourceEncounters(ShanheCampaignDefinition definition) => definition with
    {
        IntroText = definition.IntroText + "\n另有青年双俊、辽东猛将、两份不同基础体力的无双奉先，以及西凉暗军的完整S5基础阵容。" +
            "马休马铁保留固定距离一的马术、垦伤与暗器飞刃；徐荣保留凶镬、专版杀绝与妖刀赤瞳；" +
            "两份吕布保留无双、利驭及方天画戟，西凉暗军三人沿夷灭。固定装备额外入手，正常使用。投放及奖励为本地编排。",
        Nodes = definition.Nodes.Select(node => node.Id switch
        {
            "dagu" => AddS5BaseLordGroup(AddS5BaseLordGroup(node, "ma-pair", "青年双俊",
                "马休马铁以4/4体力登场，计算到其他角色的距离为1；垦伤可将至少两张牌当杀，并可改选等量目标。" +
                "随机起手四张之外另得黑桃5暗器飞刃，装备后每回合每名目标一次，使一个随机非锁定技失效，至该目标本人回合结束。",
                [new(StandardShanheS5MaPairPackage.MaPairId) { StartingCards = [new(CardKind.AnqiFeiren, Suit.Spade, 5)] }], true),
                "xurong", "辽东猛将",
                "徐荣以4/4体力登场，保留凶镬；专版杀绝仅在其他角色需要超过一张桃或酒救回时获得暴戾及造成此次濒死的牌。" +
                "随机起手四张之外另得黑桃6妖刀赤瞳；黑色杀加伤，指定目标后判定，黑色随机转移至其他合法目标。",
                [new(StandardShanheS5XuRongPackage.XurongId) { StartingCards = [new(CardKind.YaodaoChitong, Suit.Spade, 6)] }], true),
            "hulao" => AddS5BaseLordGroup(AddS5BaseLordGroup(node, "lvbu-2644", "无双奉先·五血",
                "吕布以5/5体力登场，保留经典无双与当前界版利驭，基础固定方天画戟额外入手。",
                [new(StandardShanheS5LvBuBasePackage.LvBuFiveId) { StartingCards = [new(CardKind.FangtianHalberd, Suit.Heart, 12)] }], true),
                "lvbu-2645", "无双奉先·四血",
                "吕布以4/4体力登场，与五血形态为独立战斗候选，不是变身阶段。保留无双、利驭及基础方天画戟，不附加困难或噩梦装备、技能。",
                [new(StandardShanheS5LvBuBasePackage.LvBuFourId) { StartingCards = [new(CardKind.FangtianHalberd, Suit.Heart, 12)] }], true),
            "huanyuan" => AddS5BaseLordGroup(node, "xiliang-dark", "西凉暗军",
                "秦谊、陈卫、李黑三敌均以3/4体力登场。保留夷灭：每回合一次先失去1点体力，按当前目标体力与伤害确定非负X，" +
                "增加该段伤害，并在实际伤害后令仍存活目标回复X点体力。",
                [new(StandardShanheS5XiliangDarkPackage.QinyiId), new(StandardShanheS5XiliangDarkPackage.ChenweiId),
                 new(StandardShanheS5XiliangDarkPackage.LiheiId)]),
            _ => node
        }).ToArray()
    };
}
