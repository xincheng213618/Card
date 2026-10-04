namespace CardGame.Core;

public sealed partial class GameEngine
{
    private RuleQueryEvaluation ApplyEquipmentSuitHandLimit(CharacterState player, RuleQueryEvaluation ordinary)
    {
        var policies = CardPolicies(player, SkillProgramCardPolicyKind.EquipmentSuitHandLimitAtMaxHp)
            .Where(item => GetEquipment(player).Any(card => EffectiveSuit(player, card) == item.Policy.InputSuit))
            .Select(item => (RuleQueryContribution)new FiniteRuleQueryContribution(
                $"skill:{item.Source.SkillId}:{item.Source.SkillInstanceId}:policy:{item.Policy.Id}",
                SkillRuleOperation.Set, player.MaxHp, 100)).ToArray();
        if (policies.Length == 0) return ordinary;
        // This policy says exactly MaxHP, after ordinary additive hand-limit arithmetic.
        var frozen = Array.AsReadOnly(ordinary.Value.Contributions.Concat(policies)
            .OrderBy(item => item.SourceId, StringComparer.Ordinal).ToArray());
        var stage = new RuleQueryStageSnapshot("equipment-suit-max-hp",
            ordinary.Value is FiniteRuleQueryValue finite ? finite.Value : 0, false, Math.Max(0, player.MaxHp),
            Array.AsReadOnly(policies));
        return new RuleQueryEvaluation(new FiniteRuleQueryValue(Math.Max(0, player.MaxHp), frozen),
            ordinary.BaseTerms, Array.AsReadOnly(ordinary.Stages.Concat([stage]).ToArray()));
    }
}
