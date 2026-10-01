namespace CardGame.Core;

internal interface IDeckEquipmentProgramHost
{
    SkillProgramStepOutcome UseRandomDeckEquipment(ProgramSkillFrame frame, int targetSeat, string resultBind);
    void GrantRandomSkillAndSuitShield(ProgramSkillFrame frame, int targetSeat, IReadOnlyList<string> skills, Suit suit);
}

internal sealed class UseRandomDeckEquipmentDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseRandomDeckEquipment;
    public override ISkillProgramEffectHandler Handler { get; } = new UseRandomDeckEquipmentHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Recover, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "resultBind", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner) throw new InvalidOperationException("Random deck equipment uses an owner placeholder and the selected participant.");
        var condition = r.Condition();
        if (condition.Kind is not (SkillProgramConditionKind.Always or SkillProgramConditionKind.ChoiceIs)) throw new InvalidOperationException("Random deck equipment requires an unconditional or exact choice branch.");
        return new(Op, target, 0, condition, resultBind: r.RequiredIdentifier("resultBind"));
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new ReadSelectedTarget(), new CreateCardSet(e.ResultBind!, 1, false, true)];
}

internal sealed class GrantRandomSkillAndSuitShieldDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantRandomSkillAndSuitShield;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantRandomSkillAndSuitShieldHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantSkills, static (e, c) => c.GrantSkills(e));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "skillIds", "suits", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        var suits = r.RequiredEnumArray<Suit>("suits");
        var skills = r.RequiredIdentifierArray("skillIds");
        if (target != SkillProgramEffectTarget.SelectedTarget || suits.Count != 1 || suits[0] == Suit.None || skills.Count == 0 || skills.Distinct().Count() != skills.Count)
            throw new InvalidOperationException("Random skill/shield requires one selected participant, one real suit and a distinct nonempty skill pool.");
        var e = new SkillProgramEffect(Op, target, 0, r.Condition(), skillIds: skills, suits: suits);
        RequireAlways(e, r.Path);
        return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.OwnerDied), new ReadSelectedTarget()];
}

public sealed class UseRandomDeckEquipmentHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.UseRandomDeckEquipment;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        ((IDeckEquipmentProgramHost)h).UseRandomDeckEquipment(f, f.SelectedTargetSeats.Single(), e.ResultBind!);
}
public sealed class GrantRandomSkillAndSuitShieldHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantRandomSkillAndSuitShield;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h)
    {
        ((IDeckEquipmentProgramHost)h).GrantRandomSkillAndSuitShield(f, seat, e.SkillIds, e.Suits[0]);
        return SkillProgramStepOutcome.Continue;
    }
}
