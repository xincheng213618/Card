namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.DirectedProhibition, 60)]
    private bool XuRongDirectedTargetProhibited(ProgramDirectedTargetQuery query) =>
        IsXuRongSlashTargetForbidden(query.ActorSeat, query.TargetSeat, query.Kind);
}
