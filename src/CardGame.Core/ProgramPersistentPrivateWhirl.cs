namespace CardGame.Core;

internal sealed class DepositPersistentPrivateWhirlDescriptor : PrivateWhirlDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DepositPersistentPrivateWhirl;

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPhaseStarting)];
}

internal static class PersistentPrivateWhirlContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.DepositPersistentPrivateWhirl)) return;
        if (trigger.Effects is not [var effect] || effect.Target != SkillProgramEffectTarget.Owner ||
            effect.Condition.Kind != SkillProgramConditionKind.Always ||
            trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always || trigger.IncludeResponseUses ||
            trigger.UsageScope is not null || trigger.CardKinds.Count != 0 || trigger.CardCategories.Count != 0 ||
            trigger.Subject != SkillProgramTriggerSubject.Owner ||
            trigger.Window != SkillProgramTriggerWindow.DiscardPhaseStarting || !trigger.Optional)
            throw new InvalidOperationException($"{path}: persistent private Whirl requires its optional own discard boundary and one unfiltered deposit.");
    }
}
