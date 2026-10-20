namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQualification, 10)]
    private bool MoGangquanSlashTargetQualified(ProgramSlashTargetQuery query) =>
        MoGangquanTargetAllowed(query.Actor, query.Target, query.Kind, query.Source, query.PhysicalCardIds);
}
