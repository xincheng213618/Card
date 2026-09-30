namespace CardGame.Core;

internal sealed class ExchangeSelectedTargetEquipmentProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ExchangeSelectedTargetEquipment;
    public override ISkillProgramEffectHandler Handler { get; } = new ExchangeSelectedTargetEquipmentSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ExchangeSelectedTargetHands,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var target = FilterBoundCardsProgramOperationDescriptor.Owner(r);
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition());
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadTargetSet(2, 2)];
}

public sealed class ExchangeSelectedTargetEquipmentSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ExchangeSelectedTargetEquipment;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) => host.ExchangeSelectedTargetEquipment(frame);
}
