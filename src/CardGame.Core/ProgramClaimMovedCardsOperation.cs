namespace CardGame.Core;

/// <summary>Claims the frozen discarded card of a discardPileReceived occurrence into the owner's hand.</summary>
internal sealed class ClaimMovedCardsProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimMovedCards;
    public override ISkillProgramEffectHandler Handler { get; } = new ClaimMovedCardsSkillProgramEffectHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.TurnEffects;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ClaimMovedCards,
        static (_, context) => context.ClaimMovedCards());

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.target: a moved-card claim belongs to the owner.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition());
        RequireAlways(effect, r.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireAnyContext(ProgramContextCapability.TurnEffects)];
}

public sealed class ClaimMovedCardsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimMovedCards;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.ClaimMovedCards(frame);
        return SkillProgramStepOutcome.Continue;
    }
}
