namespace CardGame.Core;

/// <summary>Reveal one draw-pile card, retaining it in a public owned pile only when its rank is new.</summary>
internal sealed class RevealUniqueRankForDyingProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RevealUniqueRankForDying;
    public override ISkillProgramEffectHandler Handler { get; } = new RevealUniqueRankForDyingSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.RevealUniqueRankForDying,
        static (effect, context) => context.DyingRescue(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "zone", "rescueHp", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        var zone = r.RequiredEnum<CardZoneKind>("zone");
        var rescueHp = r.RequiredInt("rescueHp");
        if (target != SkillProgramEffectTarget.Owner || zone != CardZoneKind.BuquWound ||
            rescueHp is < 1 or > 20)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}: unique-rank dying rescue requires owner, a public owned wound pile and rescueHp 1..20.");
        var effect = new SkillProgramEffect(Op, target, rescueHp, r.Condition(), destinationZone: zone);
        RequireAlways(effect, r.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireContext(ProgramContextCapability.Dying)];
}

public sealed class RevealUniqueRankForDyingSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RevealUniqueRankForDying;

    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.RevealUniqueRankForDying(frame, effect.DestinationZone!.Value, effect.Amount);
        return SkillProgramStepOutcome.Continue;
    }
}
