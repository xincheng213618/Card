using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static ShanheNodeDefinition WithS5LiruEncounter(ShanheNodeDefinition node) => node with
    {
        StoryText = node.StoryText + "\n李儒设下毒计，等候你独自破阵。灭计令有手牌的目标弃一张锦囊或弃全部手牌；" +
            "焚城从所选角色起依次逼迫其他角色弃全部手牌或承受手牌差值的火焰伤害，两次发动至少相隔两轮。" +
            "结束阶段，绝策可对手牌不多于自己的角色造成伤害。此候选采用S5毒士谋深组2668的3/3基础形态，" +
            "不附加成长、固定装备或同行；遭遇抽选和本地三档位置为本地编排。",
        Description = node.Description + "另有毒士谋深李儒单敌候选：专版灭计、焚城与绝策，无同行，三档保留3/3基础形态。",
        Encounters = node.Encounters!.Append(new ShanheEncounterDefinition(
            "luoyang:liru-s5", "洛阳 · 毒士谋深（基础李儒）",
            [new(StandardShanheS5LiruPackage.LiruId)], [],
            VictoryGold: 100, HealAfterVictory: 2)).ToArray()
    };

    private static bool RequiresLiruBossRegistry(ShanheBattleSetup setup) =>
        new[] { setup.PlayerGeneralId }.Concat(setup.AllyRoster.Concat(setup.EnemyRoster).Select(member => member.GeneralId))
            .Contains(StandardShanheS5LiruPackage.LiruId, StringComparer.Ordinal) ||
        setup.TacticSkillIds.Concat(setup.ExtraSkillIds)
            .Concat(setup.AllyRoster.Concat(setup.EnemyRoster).SelectMany(member => member.ExtraSkillIds ?? []))
            .Any(id => id is StandardShanheS5LiruPackage.MiejiId or StandardShanheS5LiruPackage.FenchengId);
}
