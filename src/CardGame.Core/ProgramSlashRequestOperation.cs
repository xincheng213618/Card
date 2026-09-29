namespace CardGame.Core;

internal sealed class RequestSlashByTargetProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    internal const string UsedSlashOption = "used-slash";
    internal const string DeclinedOption = "declined";

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RequestSlashByTarget;
    public override ISkillProgramEffectHandler Handler { get; } = new RequestSlashByTargetSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.RequestSlashByTarget,
        static (effect, context) => context.RequestSlashByTarget(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "resultBind", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.target: a slash request requires one selected target.");
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

public sealed class RequestSlashByTargetSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RequestSlashByTarget;

    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) =>
        host.RequestSlashByTarget(frame, targetSeat, effect.ResultBind!);
}
