using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static ShanheNodeDefinition WithS5DongshiEncounter(ShanheNodeDefinition node) => node with
    {
        StoryText = node.StoryText + "\n董氏乱朝阵中，霸心毁朝董卓与巧笑董姬并立。孙坚按剑低语：\"董姬的馈赠另有所图，切莫只顾眼前一牌。\"董卓以酒池、肉林、崩坏与盗书迎战；董姬展示赠牌，黑牌让受赠者选择令她摸X张或弃置X张，X为赠牌结算后受赠者的体力值。她赠出的黑牌受黠慧限制，暂时不能使用、打出或弃置，直到受赠者减少体力；护甲挡住全部伤害时，限制仍在。",
        Description = node.Description + "另可与孙坚共同迎战董氏乱朝：霸心毁朝董卓10/15与巧笑董姬8/8。留意连诛的赠牌抉择，以及黠慧对黑牌的限制。",
        Encounters = node.Encounters!.Append(new ShanheEncounterDefinition(
            "luoyang:dongshi-s5", "董氏乱朝 · 霸心毁朝与巧笑董姬",
            [new(StandardShanheS5DongshiPackage.DongZhuoBaxinId), new(StandardShanheS5DongshiPackage.DongJiQiaoxiaoId)],
            node.Encounters![0].Allies, VictoryGold: 100, HealAfterVictory: 2)).ToArray()
    };

    private static bool RequiresDongshiBossRegistry(ShanheBattleSetup setup) =>
        setup.PlayerGeneralId is StandardShanheS5DongshiPackage.DongZhuoBaxinId or StandardShanheS5DongshiPackage.DongJiQiaoxiaoId ||
        setup.AllyRoster.Concat(setup.EnemyRoster).Any(member =>
            member.GeneralId is StandardShanheS5DongshiPackage.DongZhuoBaxinId or StandardShanheS5DongshiPackage.DongJiQiaoxiaoId);
}
