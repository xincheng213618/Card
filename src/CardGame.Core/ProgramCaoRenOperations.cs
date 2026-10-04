namespace CardGame.Core;

internal interface ICaoRenProgramHost
{
    SkillProgramStepOutcome DiscardHandOrUseEquipment(ProgramSkillFrame frame);
    SkillProgramStepOutcome MoveFieldEquipment(ProgramSkillFrame frame);
}

// 据守's closing step: the owner names one hand card; equipment is used, anything else is discarded.
internal sealed class DiscardHandOrUseEquipmentDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardHandOrUseEquipment;
    public override ISkillProgramEffectHandler Handler { get; } = new DiscardHandOrUseEquipmentHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 1, r.Condition());
        RequireAlways(e, r.Path);
        return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
}

// 解围's relocation: one field equipment card travels to the selected destination's free slot.
internal sealed class MoveFieldEquipmentDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.MoveFieldEquipment;
    public override ISkillProgramEffectHandler Handler { get; } = new MoveFieldEquipmentHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 1, r.Condition());
        RequireAlways(e, r.Path);
        return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget()];
}

public sealed class DiscardHandOrUseEquipmentHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardHandOrUseEquipment;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        ((ICaoRenProgramHost)h).DiscardHandOrUseEquipment(f);
}

public sealed class MoveFieldEquipmentHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.MoveFieldEquipment;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        ((ICaoRenProgramHost)h).MoveFieldEquipment(f);
}
