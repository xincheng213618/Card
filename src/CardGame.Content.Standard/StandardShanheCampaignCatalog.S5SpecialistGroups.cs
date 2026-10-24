using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static ShanheCampaignDefinition WithS5SpecialistGroupEncounters(ShanheCampaignDefinition definition) => definition with
    {
        IntroText = definition.IntroText + "\n另有枪王张绣、曹魏先驱与奇人异士完整S5基础阵容；文远之威另有携带刑天破军斧的完整基础候选。" +
            "雷震和刑天均在随机起手四张之外额外入手，再正常装备；投放、奖励及刑天范围四属于本地约定。",
        Nodes = definition.Nodes.Select(node => node.Id switch
        {
            "guangcheng" => AddS5BaseLordGroup(node, "zhangxiu", "枪王张绣",
                "张绣4/4，保留专版雄乱、从谏；判定区已废后仍可随机废除一个尚存装备栏发动雄乱。另得黑桃A雷震，装备后每用三张手牌，下一张杀加伤一且不叠加。",
                [new(StandardShanheS5ZhangxiuPackage.ZhangxiuId) { StartingCards = [new(CardKind.Leizhen, Suit.Spade, 1)] }], true),
            "huanyuan" => AddS5BaseLordGroup(node, "cao-wei-pioneers", "曹魏先驱",
                "曹真、曹操、曹洪按原序各4/4。司敌、援护沿基础版；曹操受伤后先摸已损体力数的牌，再取得仍可领取的致伤牌；护驾保留实际代闪及其他魏角色回合外使用或打出闪时的每回合一次奖励。",
                [new(StandardShanheS5CaoWeiPioneersPackage.CaozhenId),
                 new(StandardShanheS5CaoWeiPioneersPackage.CaocaoId) { QualifiedPrintedLordSkillIds = [StandardShanheS5CaoWeiPioneersPackage.HujiaId] },
                 new(StandardShanheS5CaoWeiPioneersPackage.CaohongId)]),
            "dagu" => AddS5BaseLordGroup(node, "zuoci-huatuo", "奇人异士",
                "左慈、华佗各3/3。左慈初始获得三张化身，每点受伤可再得三张；回合开始与结束可替换展示或交换至多两张未展示化身。华佗保留急救、除疠。",
                [new(StandardShanheS5ZuociHuatuoPackage.ZuociId), new(StandardShanheS5ZuociHuatuoPackage.HuatuoId)]),
            "hulao" => AddS5BaseLordGroup(node with
                { Encounters = node.Encounters!.Where(e => e.Id != "hulao:zhangliao-s5-base").ToArray() },
                "zhangliao-armed", "文远之威·完整基础形态",
                "张辽4/4，保留突袭、破军，另得红桃5刑天破军斧。出牌阶段对唯一目标用牌后可弃两张牌，使其本回合不能使用或打出牌且防具失效。基础斧替换原无装备基础候选，灵动及惶恐的本地技能形态另行保留。",
                [new(StandardShanheS5BossPackage.ZhangliaoBaseId) { StartingCards = [new(CardKind.ShanheXingtianAxe, Suit.Heart, 5)] }], true),
            _ => node
        }).ToArray()
    };
}
