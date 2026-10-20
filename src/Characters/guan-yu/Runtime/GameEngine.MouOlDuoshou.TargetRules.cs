namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaAfterLimit, 50)]
    private bool GuanFirstBasicSlashQuotaAfterLimit(ProgramSlashQuotaQuery query) =>
        GuanFirstBasicAvailable(query.Actor, query.Kind);
}
