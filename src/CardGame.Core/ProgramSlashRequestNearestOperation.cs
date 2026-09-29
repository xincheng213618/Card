namespace CardGame.Core;

internal sealed class RequestNearestSlashProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    internal const string UsedSlashOption = "used-slash";
    internal const string DeclinedOption = "declined";

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RequestNearestSlash;
    public override ISkillProgramEffectHandler Handler { get; } = new RequestNearestSlashSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.RequestNearestSlash,
        static (_, _) => { });

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.target: a nearest-slash request is owner-driven and derives its responders itself.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition());
        RequireAlways(effect, r.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

public sealed class RequestNearestSlashSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RequestNearestSlash;

    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) =>
        host.RequestNearestSlash(frame);
}
