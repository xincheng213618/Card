namespace CardGame.Core;

/// <summary>Transfer one finalized pending damage occurrence to an explicitly selected living recipient.</summary>
internal sealed class RedirectCurrentDamageProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RedirectCurrentDamage;
    public override ISkillProgramEffectHandler Handler { get; } = new RedirectCurrentDamageSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.RedirectCurrentDamage,
        static (effect, context) => context.RedirectCurrentDamage(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "drawLostHpAfterDamage", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.target: damage redirection requires a selected recipient.");
        var drawLostHp = r.Has("drawLostHpAfterDamage") && r.RequiredBool("drawLostHpAfterDamage");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition(),
            sourceBind: r.RequiredIdentifier("sourceBind"), booleanValue: drawLostHp);
        RequireAlways(effect, r.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireContext(ProgramContextCapability.Damage), new ReadSelectedTarget(),
            new ReadCardSet(effect.SourceBind!)];
}

public sealed class RedirectCurrentDamageSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RedirectCurrentDamage;

    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.RedirectCurrentDamage(frame, effect.SourceBind!, effect.BooleanValue == true);
        return SkillProgramStepOutcome.Continue;
    }
}
