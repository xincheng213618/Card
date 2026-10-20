namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.TargetCountContributions, 10)]
    private RuleQueryContribution? NeifaTargetCountContribution(ProgramTargetCountQuery query) =>
        NeifaSlashTargetBonus(query.Actor.Seat, query.Kind) != 0
            ? new FiniteRuleQueryContribution($"turn:{query.Actor.Seat}:neifa:slash-target", SkillRuleOperation.Add, 1)
            : null;
}
