using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static ShanheNodeDefinition WithS5ShiduEncounters(ShanheNodeDefinition node, int difficulty)
    {
        if (difficulty < 2) return node;
        var additions = new List<ShanheEncounterDefinition>
        {
            new("guangcheng:caoxing-shidu-s5", "夺目一箭 · 曹性（识度）",
                [new(StandardShanheS5ShiduPackage.CaoxingShiduId)], [], VictoryGold: 80, HealAfterVictory: 2)
        };
        if (difficulty == 3) additions.Add(new("guangcheng:caoxing-shidu-tianbian-s5", "夺目一箭 · 曹性（识度、天辩）",
            [new(StandardShanheS5ShiduPackage.CaoxingTianbianId)], [], VictoryGold: 80, HealAfterVictory: 2));
        return node with
        {
            StoryText = node.StoryText + "\n曹性亦能以识度邀人拼点。若他获胜，便先取得对方全部手牌，再交回自己当前半数手牌。" +
                (difficulty == 3 ? "天辩又使他能够以牌堆顶的牌拼点，红桃按K计算；先前流矢置顶的一张红桃，便可能成为下一场拼点的伏笔。" : ""),
            Description = node.Description + "新增本地识度组合候选，拼点胜负会改变双方留牌。" +
                (difficulty == 3 ? "本地天辩组合还可用顶牌拼点，红桃按K计算。" : ""),
            Encounters = node.Encounters!.Concat(additions).ToArray()
        };
    }

    private static bool RequiresShiduBossRegistry(ShanheBattleSetup setup) =>
        new[] { setup.PlayerGeneralId }.Concat(setup.AllyRoster.Concat(setup.EnemyRoster).Select(m => m.GeneralId))
            .Any(id => id is StandardShanheS5ShiduPackage.CaoxingShiduId or StandardShanheS5ShiduPackage.CaoxingTianbianId) ||
        setup.TacticSkillIds.Concat(setup.ExtraSkillIds)
            .Concat(setup.AllyRoster.Concat(setup.EnemyRoster).SelectMany(m => m.ExtraSkillIds ?? []))
            .Contains(StandardShanheS5ShiduPackage.ShiduId, StringComparer.Ordinal);
}
