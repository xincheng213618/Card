namespace CardGame.Core;

internal interface ILuelveCounterSlashProgramHost
{
    SkillProgramStepOutcome SelectedTargetVirtualSlashAgainstOwner(ProgramSkillFrame frame);
}

// 掳掠 branch two: the flipped counterpart uses a virtual Slash against the
// skill owner under the counterpart's own distance and targeting rules.
internal sealed class SelectedTargetVirtualSlashAgainstOwnerDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectedTargetVirtualSlashAgainstOwner;
    public override ISkillProgramEffectHandler Handler { get; } = new SelectedTargetVirtualSlashAgainstOwnerHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.PhaseSubstitution;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.UseSelectedCardsAs,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException("The counterpart slash targets the skill owner.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition());
        if (effect.Condition.Kind is not (SkillProgramConditionKind.Always or SkillProgramConditionKind.ChoiceIs))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.condition: accepts always or a named-choice branch.");
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget()];
}

public sealed class SelectedTargetVirtualSlashAgainstOwnerHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SelectedTargetVirtualSlashAgainstOwner;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        ((ILuelveCounterSlashProgramHost)h).SelectedTargetVirtualSlashAgainstOwner(f);
}
