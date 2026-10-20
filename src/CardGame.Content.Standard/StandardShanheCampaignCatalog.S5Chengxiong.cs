using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static bool RequiresChengxiongBossRegistry(ShanheBattleSetup setup) =>
        setup.PlayerGeneralId == StandardShanheS5ChengxiongPackage.GaoshunId ||
        setup.AllyRoster.Concat(setup.EnemyRoster).Any(member =>
            member.GeneralId == StandardShanheS5ChengxiongPackage.GaoshunId) ||
        setup.TacticSkillIds.Concat(setup.ExtraSkillIds)
            .Concat(setup.AllyRoster.Concat(setup.EnemyRoster).SelectMany(member => member.ExtraSkillIds ?? []))
            .Contains(StandardShanheS5ChengxiongPackage.ChengxiongId, StringComparer.Ordinal);
}
