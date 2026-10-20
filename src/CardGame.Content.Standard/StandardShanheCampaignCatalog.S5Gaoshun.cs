using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static ShanheNodeDefinition WithS5GaoshunEncounter(ShanheNodeDefinition node, int difficulty)
    {
        var chengxiong = difficulty == 3;
        return node with
        {
            StoryText = node.StoryText + "\n关前又见高顺整军列阵。他以陷阵邀人拼点：若赢，便能对败者连续用牌、无视距离与防具，还可将其追加为杀或普通锦囊的目标；若未赢，他本回合便不能对其用杀。禁酒令他的酒视为点数K的杀，在他的回合，其他角色也不能用酒。出阵前，须权衡拼点所用的手牌与留给应战的余力。" +
                (chengxiong ? "此战高顺还带逞凶：奇数轮造成的伤害加一，偶数轮减一，攻守之机随轮次交替。" : ""),
            Description = node.Description + "另可迎战高顺与陷阵营：拼点的胜负决定其后续攻势，禁酒使酒成为点数K的杀。" +
                (chengxiong ? "逞凶使其伤害随奇偶轮交替增减。" : ""),
            Encounters = node.Encounters!.Append(new ShanheEncounterDefinition(
                chengxiong ? "hulao:gaoshun-chengxiong-s5" : "hulao:gaoshun-s5",
                chengxiong ? "陷阵营 · 高顺（逞凶）" : "陷阵营 · 高顺",
                [new(chengxiong ? StandardShanheS5ChengxiongPackage.GaoshunId : StandardShanheS5GaoshunPackage.GaoshunId)], [],
                VictoryGold: 100, HealAfterVictory: 2)).ToArray()
        };
    }

    private static bool RequiresGaoshunBossRegistry(ShanheBattleSetup setup) =>
        setup.PlayerGeneralId == StandardShanheS5GaoshunPackage.GaoshunId ||
        setup.AllyRoster.Concat(setup.EnemyRoster).Any(member =>
            member.GeneralId == StandardShanheS5GaoshunPackage.GaoshunId);
}
