namespace CardGame.Core;

public sealed partial class GameEngine
{
    private RuleQueryEvaluation ApplyHpOrderedDistance(CharacterState source, CharacterState target, RuleQueryEvaluation original)
    {
        if (source.Seat == target.Seat || target.Hp > source.Hp) return original;
        var policies = CardPolicies(source, SkillProgramCardPolicyKind.DistanceOneToNotHigherHp).ToArray();
        if (policies.Length == 0) return original;
        // A final, directed Set deliberately follows both horses and additive
        // distance contributions: the printed current rule says 'treated as 1'.
        return RuleQueryService.Evaluate(SkillRuleQuery.OutgoingDistance, new RuleQueryBounds(1, int.MaxValue),
            [new RuleQueryBaseTerm("distance:before-hp-ordered-policy", ConvertRuleValue(original))],
            policies.Select(p => (RuleQueryContribution)new FiniteRuleQueryContribution(
                $"skill:{p.Source.SkillId}:{p.Source.SkillInstanceId}:policy:{p.Policy.Id}", SkillRuleOperation.Set, 1)).ToArray());
    }
}
