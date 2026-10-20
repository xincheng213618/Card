using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static ShanheNodeDefinition WithS5CaoxingEncounter(ShanheNodeDefinition node) => node with
    {
        StoryText = node.StoryText + "\n关前一箭掠过，曹性已搭上第二支箭。他的流矢可将红桃牌置顶，接连使出无距离、次数限制的杀；受伤者的手牌上限逐层降低。待受影响者在弃牌阶段实际弃牌，斩腕便使曹性摸等量牌，并清除其流矢效果。留牌、应闪与弃牌的取舍，也会改变他下一轮的攻势。",
        Description = node.Description + "另可迎战夺目一箭曹性：流矢连续压低手限，斩腕借真实弃牌补充手牌。",
        Encounters = node.Encounters!.Append(new ShanheEncounterDefinition(
            "guangcheng:caoxing-s5", "夺目一箭 · 曹性",
            [new(StandardShanheS5CaoxingPackage.CaoxingId)], [],
            VictoryGold: 80, HealAfterVictory: 2)).ToArray()
    };

    private static bool RequiresCaoxingBossRegistry(ShanheBattleSetup setup) =>
        setup.PlayerGeneralId == StandardShanheS5CaoxingPackage.CaoxingId ||
        setup.AllyRoster.Concat(setup.EnemyRoster).Any(member =>
            member.GeneralId == StandardShanheS5CaoxingPackage.CaoxingId);
}
