namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_pan_shu_ol_PanShuPrograms
{
    internal static bool IsOtherLastHandLossTrigger(SkillProgramTrigger trigger) =>
        trigger.Window == SkillProgramTriggerWindow.CardsMoved &&
        trigger.MovementOccurrence == SkillProgramMovementOccurrence.PerOtherLastHandLoss;

    internal static bool LostLastHandCard(CardMovementBatchContext batch, int sourceSeat)
    {
        var location = CardLocation.Hand(sourceSeat);
        var count = batch.SourceCounts.FirstOrDefault(item => item.Location == location);
        return count is not null && count.CountBefore > 0 && count.CountAfter == 0;
    }
}
