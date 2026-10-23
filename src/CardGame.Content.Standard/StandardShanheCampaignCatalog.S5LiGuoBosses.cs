using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static ShanheCampaignDefinition WithS5LiGuoBossEncounters(ShanheCampaignDefinition definition) => definition with
    {
        IntroText = definition.IntroText + "\n广成另有乱世恶徒李傕与祸国凶贼郭汜，大谷另有其实难副许靖、徐璆的完整S5基础阵容。" +
            "李傕每名角色准备阶段可发动1至2点随机伤害的狼袭，保留亦算与赤血青锋；郭汜以贪狈、伺盗及斩魂刀应战。" +
            "固定装备额外加入手牌，保留原随机起手，再由正常用牌安装。三档沿基础配置，高难牌与技能不加入；位置、抽选和奖励为本地编排。",
        Nodes = definition.Nodes.Select(node => node.Id switch
        {
            "guangcheng" => AddS5SourceRosterEncounter(AddS5SourceRosterEncounter(node, "lijue", "乱世恶徒",
                "采用主组2636的4/4李傕。每名角色准备阶段可对体力不大于自己的其他角色造成1至2点随机伤害，亦算可支付体力上限取回实际使用的锦囊；" +
                "起手4张之外额外加入梅花6赤血青锋。",
                new ShanheCombatantDefinition(StandardShanheS5LiJuePackage.LijueId) { StartingCards = [new(CardKind.ScarletBloodSword, Suit.Club, 6)] }),
                "guosi", "祸国凶贼",
                "采用主组2637的4/4郭汜，贪狈、伺盗沿普通OL技能；起手4张之外额外加入黑桃2斩魂刀。" +
                "实际装备后，造成伤害时若目标已受伤，可防止该段伤害并令其体力上限减一。",
                new ShanheCombatantDefinition(StandardShanheS5GuosiPackage.GuosiId) { StartingCards = [new(CardKind.ZhanhunDao, Suit.Spade, 2)] }),
            "dagu" => AddS5SourceRosterEncounter(node, "xujing-xuqiu", "其实难副",
                "采用主组2634完整双敌：许靖3/3、徐璆4/4，保留誉虚、实荐、奇袭、遗计的对应普通版本，无基础固定牌。",
                new(StandardShanheS5XujingXuqiuPackage.XujingId), new(StandardShanheS5XujingXuqiuPackage.XuqiuId)),
            _ => node
        }).ToArray()
    };

    private static ShanheNodeDefinition AddS5SourceRosterEncounter(ShanheNodeDefinition node, string id, string name,
        string story, params ShanheCombatantDefinition[] enemies) => node with
    {
        StoryText = node.StoryText + "\n" + story + "三档沿基础面板与牌，不加高难配置；本地投放、抽选及奖励另行编排。",
        Description = node.Description + "另有S5完整基础阵容：" + name + "。",
        Encounters = node.Encounters!.Append(new ShanheEncounterDefinition(node.Id + ":" + id + "-s5",
            node.Name + " · " + name + "（基础形态）", enemies, [],
            VictoryGold: enemies.Length == 1 ? 80 : 100, HealAfterVictory: 2)).ToArray()
    };

    private static IEnumerable<ShanheStartingCard> ShanheRecipeCards(ShanheBattleSetup setup) =>
        setup.StartingCards.Concat(setup.StartingEquipment).Concat(setup.AllyRoster.Concat(setup.EnemyRoster)
            .SelectMany(member => (member.StartingHand ?? []).Concat(member.StartingCards ?? [])));
}
