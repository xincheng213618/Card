namespace CardGame.Core;

internal interface ILiangXingProgramHost
{
    SkillProgramStepOutcome GiveSelectedTargetHand(ProgramSkillFrame frame, int targetSeat);
}

// 掳掠 branch one: the chosen counterpart hands their entire hand to the owner.
internal sealed class GiveSelectedTargetHandDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GiveSelectedTargetHand;
    public override ISkillProgramEffectHandler Handler { get; } = new GiveSelectedTargetHandHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException("Whole-hand giving requires the selected counterpart.");
        return new SkillProgramEffect(Op, target, 0, r.Condition());
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget()];
}

public sealed class GiveSelectedTargetHandHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GiveSelectedTargetHand;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        ((ILiangXingProgramHost)h).GiveSelectedTargetHand(f, seat);
}
