namespace CardGame.Core;

[ProgramRuntimeModule]
internal sealed class MouYangshiRangeRuntime(IDistanceRuleRuntimeQueries queries, ISelectionParticipantQueries participants)
{
    internal int RangeBonus(int ownerSeat) =>
        (queries.HasTriggerOperation(SkillProgramEffectOp.ResolveMouTianYuYangshi) ||
         queries.HasTriggerOperation(SkillProgramEffectOp.ResolveMouTianYuZhenxi)) && participants.IsAlive(ownerSeat)
            ? queries.History<MouTianYuRangeIncreasedEvent>().Count(fact => fact.OwnerSeat == ownerSeat) : 0;

    internal void AddRangeContribution(int ownerSeat, List<RuleQueryContribution> contributions)
    {
        var bonus = RangeBonus(ownerSeat);
        if (bonus != 0)
            contributions.Add(new FiniteRuleQueryContribution(
                $"issued:{ownerSeat}:mou-tian-yu:yangshi:attack-range", SkillRuleOperation.Add, bonus));
    }
}
