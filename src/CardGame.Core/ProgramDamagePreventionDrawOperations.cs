namespace CardGame.Core;

internal interface IDamagePreventionDrawProgramHost
{
    SkillProgramStepOutcome PreventDamageAndDrawMultiple(ProgramSkillFrame frame, int multiplier);
}

internal sealed class PreventCurrentDamageAndDrawMultipleDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PreventCurrentDamageAndDrawMultiple;
    public override ISkillProgramEffectHandler Handler { get; } = new PreventCurrentDamageAndDrawMultipleHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.PreventCurrentDamage,
        static (effect, context) => context.PreventCurrentDamage(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "amount", "condition");
        var amount = reader.RequiredInt("amount");
        if (reader.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner || amount is < 1 or > 8)
            throw new InvalidOperationException("Damage prevention draw requires its own damage target and multiplier1..8.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, amount, reader.Condition());
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];
}

public sealed class PreventCurrentDamageAndDrawMultipleHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.PreventCurrentDamageAndDrawMultiple;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((IDamagePreventionDrawProgramHost)host).PreventDamageAndDrawMultiple(frame, effect.Amount);
}
