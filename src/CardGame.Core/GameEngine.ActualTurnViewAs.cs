namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static string ActualTurnViewAsUsageId(SkillProgramViewAs rule) =>
        $"view-as-actual-turn:{rule.UsageGroup ?? rule.Id}";

    private bool CanUseActualTurnLimitedViewAs(IndexedSkillProgramInstance instance, SkillProgramViewAs rule, CharacterState owner) =>
        rule.UsesPerActualTurn is not { } limit ||
        _skillRuntimeState.GetUsage(owner.Seat, instance.SkillId, ActualTurnViewAsUsageId(rule), SkillUsageScope.Turn) < limit;

    private void ConsumeActualTurnLimitedViewAs(CardConversionSource source)
    {
        if (ViewAsRule(source) is not { UsesPerActualTurn: { } limit } rule) return;
        var usageId = ActualTurnViewAsUsageId(rule);
        if (!_skillRuntimeState.TryConsumeUsage(source.OwnerSeat, source.SkillId, usageId, SkillUsageScope.Turn, limit))
            throw new InvalidOperationException("The actual-turn conversion allowance was already consumed.");
        AdvanceEventRulesAndQueueFact(new SkillUsageConsumedEvent(source.OwnerSeat, source.SkillId,
            usageId, SkillUsageScope.Turn, 1));
    }
}
