namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void AccumulateProgramSelectedCardCount(
        ProgramSkillFrame frame, string usageId, int threshold, string resultBind)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.TriggerId is not null || active.SelectedCardIds.Count == 0)
            throw new InvalidOperationException("Only an activation with selected cards can accumulate its card count.");
        if (threshold <= 0 || string.IsNullOrWhiteSpace(usageId) || string.IsNullOrWhiteSpace(resultBind))
            throw new InvalidOperationException("An activation counter needs a usage id, positive threshold and result binding.");
        if (active.ChoiceBindings.Any(binding => binding.Name == resultBind))
            throw new InvalidOperationException("An activation counter result cannot be bound twice.");

        var previous = _skillRuntimeState.GetUsage(
            active.OwnerSeat, active.SkillId, usageId, SkillUsageScope.Phase);
        var current = checked(previous + active.SelectedCardIds.Count);
        for (var index = 0; index < active.SelectedCardIds.Count; index++)
        {
            if (!_skillRuntimeState.TryConsumeUsage(
                    active.OwnerSeat, active.SkillId, usageId, SkillUsageScope.Phase, int.MaxValue))
                throw new InvalidOperationException("The validated activation counter could not advance.");
        }

        var result = previous < threshold && current >= threshold ? "crossed" : "not-crossed";
        _resolutionStack[^1] = active with
        {
            ChoiceBindings = Array.AsReadOnly(active.ChoiceBindings.Append(
                new ProgramChoiceResultBinding(resultBind, result, active.OwnerSeat)).ToArray())
        };
        QueueGameEvent(new SkillUsageConsumedEvent(
            active.OwnerSeat, active.SkillId, usageId, SkillUsageScope.Phase, current));
    }
}
