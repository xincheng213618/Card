namespace CardGame.Core;

internal interface IPhaseStartCardControlHost
{
    SkillProgramStepOutcome GrantOtherPlayUseRestriction(ProgramSkillFrame frame, string skillId);
    SkillProgramStepOutcome ChooseStartingEquipmentControl(ProgramSkillFrame frame);
}
internal sealed class GrantOtherPlayUseRestrictionDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantOtherPlayUseRestriction;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantOtherPlayUseRestrictionHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnSkills,
        static (_, context) => context.PublicControlValue(10d));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "skillId", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0,
            reader.Condition(), skillIds: [reader.RequiredIdentifier("skillId")]);
        RequireAlways(effect, reader.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];
}
internal sealed class ChooseStartingEquipmentControlDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseStartingEquipmentControl;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler { get; } = new ChooseStartingEquipmentControlHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.DiscardOwnedZoneCards,
        static (_, context) => context.PublicControlValue(8d));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0, reader.Condition());
        RequireAlways(effect, reader.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];
}
internal sealed class GrantOtherPlayUseRestrictionHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantOtherPlayUseRestriction;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        ((IPhaseStartCardControlHost)host).GrantOtherPlayUseRestriction(frame, effect.SkillIds.Single());
}
internal sealed class ChooseStartingEquipmentControlHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseStartingEquipmentControl;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        ((IPhaseStartCardControlHost)host).ChooseStartingEquipmentControl(frame);
}
internal static class PhaseStartCardControlContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        var restriction = trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.GrantOtherPlayUseRestriction);
        var equipment = trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ChooseStartingEquipmentControl);
        if (!restriction && !equipment) return;
        if (restriction == equipment || trigger.Window != SkillProgramTriggerWindow.PlayPhaseStarting ||
            trigger.Subject != SkillProgramTriggerSubject.Owner || !trigger.Optional || trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            trigger.TurnOwnerScope != (restriction ? SkillProgramTurnOwnerScope.OtherLiving : SkillProgramTurnOwnerScope.Own) ||
            trigger.Effects.Count != 1 || trigger.Effects[0].Condition.Kind != SkillProgramConditionKind.Always ||
            trigger.CardKinds.Count != 0 || trigger.CardCategories.Count != 0 || trigger.IncludeResponseUses ||
            trigger.UsageScope is not null || trigger.UsageLimit is not null || trigger.DynamicUsageLimit is not null ||
            trigger.NamedUsageGroup is not null || trigger.MarkerCost is not null || trigger.ChoiceGroup is not null ||
            trigger.EvaluateConditionAtResolution || trigger.SingleActionInstance || trigger.NoDyingAtActivation ||
            trigger.AllowNoEventTarget || trigger.DeferredTurnEndOnly)
            throw new InvalidOperationException($"Invalid skill program at {path}: phase-starting card control requires its exact optional own/other boundary and one unconditional operation.");
    }
}
