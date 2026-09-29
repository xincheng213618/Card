namespace CardGame.Core;

/// <summary>
/// The selected character treats using one Slash against another character
/// designated by the program owner within the selected character's attack
/// range; declining (or having no in-range candidate) binds the declined
/// option so a following draw can serve as the official fallback.
/// </summary>
internal sealed class UseDesignatedVirtualSlashProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    internal const string UsedSlashOption = "used-slash";
    internal const string DeclinedOption = "declined";

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseDesignatedVirtualSlash;
    public override ISkillProgramEffectHandler Handler { get; } =
        new UseDesignatedVirtualSlashSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.UseDesignatedVirtualSlash,
        static (effect, context) => context.UseDesignatedVirtualSlash(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "resultBind", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.target: a designated virtual slash requires one selected target.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition(),
            resultBind: r.RequiredIdentifier("resultBind"));
        RequireAlways(effect, r.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
    [
        new ReadSelectedTarget(),
        new CreateChoiceResult(effect.ResultBind!, [UsedSlashOption, DeclinedOption])
    ];
}

public sealed class UseDesignatedVirtualSlashSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.UseDesignatedVirtualSlash;

    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) =>
        host.UseDesignatedVirtualSlash(frame, targetSeat, effect.ResultBind!);
}
