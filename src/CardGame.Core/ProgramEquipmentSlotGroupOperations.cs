namespace CardGame.Core;

internal interface IEquipmentSlotGroupProgramHost
{
    void AbolishEquipmentSlotGroup(ProgramSkillFrame frame, IReadOnlyList<EquipmentSlot> slots);
    void RecastSelectedEquipment(ProgramSkillFrame frame);
    void ReplaceSkillsOnPreparation(ProgramSkillFrame frame, IReadOnlyList<string> lost, string acquired);
}

internal sealed class AbolishEquipmentSlotGroupDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AbolishEquipmentSlotGroup;
    public override ISkillProgramEffectHandler Handler { get; } = new AbolishEquipmentSlotGroupHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "equipmentSlots", "condition");
        var slots = r.OptionalEnumArray<EquipmentSlot>("equipmentSlots") ?? [];
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner ||
            slots.Count is < 1 or > 5 || slots.Distinct().Count() != slots.Count)
            throw new InvalidOperationException("Equipment-slot payment requires a distinct nonempty owner slot group.");
        var result = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, 0, r.Condition(), equipmentSlots: slots);
        RequireAlways(result, r.Path);
        return result;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireEquipmentSlotActivation()];
}

internal sealed class RecastSelectedEquipmentDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecastSelectedEquipment;
    public override ISkillProgramEffectHandler Handler { get; } = new RecastSelectedEquipmentHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner,
            1, new(SkillProgramConditionKind.Always, 0, []))));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException("Equipment recasting requires owner.");
        var result = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, 1, r.Condition());
        RequireAlways(result, r.Path);
        return result;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireEquipmentRecastActivation(), new ConsumeSelectedCards(1)];
}

internal sealed class ReplaceSkillsOnPreparationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReplaceSkillsOnPreparation;
    public override ISkillProgramEffectHandler Handler { get; } = new ReplaceSkillsOnPreparationHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnSkills, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "skillIds", "sourceBind", "condition");
        var lost = r.RequiredIdentifierArray("skillIds");
        var acquired = r.RequiredIdentifier("sourceBind");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner ||
            lost.Count == 0 || lost.Distinct().Count() != lost.Count || lost.Contains(acquired))
            throw new InvalidOperationException("Preparation replacement requires distinct lost and acquired owner skills.");
        var result = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, 0, r.Condition(),
            skillIds: lost, sourceBind: acquired);
        RequireAlways(result, r.Path);
        return result;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}

public sealed class AbolishEquipmentSlotGroupHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.AbolishEquipmentSlotGroup;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        ((IEquipmentSlotGroupProgramHost)host).AbolishEquipmentSlotGroup(frame, effect.EquipmentSlots);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class RecastSelectedEquipmentHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RecastSelectedEquipment;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        ((IEquipmentSlotGroupProgramHost)host).RecastSelectedEquipment(frame);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class ReplaceSkillsOnPreparationHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ReplaceSkillsOnPreparation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        ((IEquipmentSlotGroupProgramHost)host).ReplaceSkillsOnPreparation(frame, effect.SkillIds, effect.SourceBind!);
        return SkillProgramStepOutcome.Continue;
    }
}
