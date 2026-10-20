namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.DirectedProhibition, 10)]
    private bool WeiwanDirectedTargetProhibited(ProgramDirectedTargetQuery query) =>
        IsWeiwanTargetForbidden(query.ActorSeat, query.TargetSeat, query.Kind, query.ExistingUseFrameId);

    [ProgramTargetRule(ProgramTargetRulePoint.QuotaAllowance, 10)]
    private bool WeiwanTargetQuotaAllowed(ProgramTargetQuotaQuery query) =>
        HasWeiwanQuota(_players[query.ActorSeat], _players[query.TargetSeat]);
}
