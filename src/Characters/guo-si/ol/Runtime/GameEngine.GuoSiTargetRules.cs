namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.DirectedProhibition, 50)]
    private bool GuoSiDirectedTargetProhibited(ProgramDirectedTargetQuery query) =>
        HasGuoSiTargetEffect(query.ActorSeat, query.TargetSeat, DirectedTurnCardPolicyEffect.ForbidTarget);

    [ProgramTargetRule(ProgramTargetRulePoint.QuotaAllowance, 20)]
    private bool GuoSiTargetQuotaAllowed(ProgramTargetQuotaQuery query) =>
        HasGuoSiTargetEffect(query.ActorSeat, query.TargetSeat, DirectedTurnCardPolicyEffect.BypassSlashLimit);
}
