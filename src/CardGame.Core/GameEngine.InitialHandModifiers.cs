namespace CardGame.Core;

public sealed partial class GameEngine
{
    // The caller freezes every participant's count before moving the first
    // initial entity. Evaluation itself publishes no fact or gain window.
    private int GetInitialHandSize(CharacterState player) =>
        ConvertRuleValue(EvaluateInitialHandSize(player));

    private RuleQueryEvaluation EvaluateInitialHandSize(CharacterState player) =>
        RuleQueryService.Evaluate(
            SkillRuleQuery.InitialHandSize,
            new RuleQueryBounds(0, int.MaxValue),
            [new RuleQueryBaseTerm($"mode:{_modeDefinition.Id}:initial-hand-size", _initialHandSize)],
            CollectNumericRuleContributions(player, SkillRuleQuery.InitialHandSize));
}
