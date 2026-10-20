namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.DirectedProhibition, 70)]
    private bool GuanFocusDirectedTargetProhibited(ProgramDirectedTargetQuery query) =>
        GuanFocusProhibitsSlash(query.ActorSeat, query.TargetSeat, query.Kind);
}

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaAfterLimit, 40)]
    private bool GuanFocusSlashQuotaAfterLimit(ProgramSlashQuotaQuery query) =>
        GuanFocusAllowsSlash(query.Actor.Seat, query.Target.Seat);
}
