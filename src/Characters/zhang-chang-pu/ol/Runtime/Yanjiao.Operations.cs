namespace CardGame.Core;

internal sealed class YanjiaoRevealTopCardsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.YanjiaoRevealTopCards;
    public override ISkillProgramEffectHandler Handler { get; } = new YanjiaoRevealTopCardsHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Reveal,
        static (effect, context) => context.Reveal(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "marker", "resultBind", "visibility", "condition");
        var target = FilterBoundCardsProgramOperationDescriptor.Owner(r);
        var amount = r.RequiredInt("amount");
        if (amount is < 1 or > 4)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: the Yanjiao reveal takes a base count of 1..4.");
        var effect = new SkillProgramEffect(Op, target, amount, r.Condition(),
            marker: r.RequiredEnum<PlayerMarkerKind>("marker"),
            resultBind: r.RequiredIdentifier("resultBind"),
            visibility: r.RequiredEnum<SkillProgramCardSetVisibility>("visibility"));
        if (effect.Visibility != SkillProgramCardSetVisibility.Public)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: the Yanjiao reveal must be public.");
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting),
         new CreateCardSet(effect.ResultBind!, effect.Amount + 4, true)];
}

internal sealed class YanjiaoSplitRevealedCardsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.YanjiaoSplitRevealedCards;
    public override ISkillProgramEffectHandler Handler { get; } = new YanjiaoSplitRevealedCardsHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift,
        static (effect, context) => context.Gift(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "ownerBind", "chooserBind", "leftoverBind", "condition");
        var target = FilterBoundCardsProgramOperationDescriptor.Owner(r);
        var source = r.RequiredIdentifier("sourceBind");
        var owner = r.RequiredIdentifier("ownerBind");
        var chooser = r.RequiredIdentifier("chooserBind");
        var leftover = r.RequiredIdentifier("leftoverBind");
        if (owner == source || chooser == source || leftover == source ||
            owner == chooser || owner == leftover || chooser == leftover)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: the Yanjiao split requires four distinct binds.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition(), sourceBind: source)
        { OwnerBind = owner, ChooserBind = chooser, LeftoverBind = leftover };
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting),
         new PartitionCardSet(effect.SourceBind!, effect.OwnerBind!, effect.ChooserBind!, effect.LeftoverBind!)];
}

public sealed class YanjiaoRevealTopCardsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.YanjiaoRevealTopCards;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host)
    { host.GetCapability<IZhangChangPuProgramHost>().YanjiaoRevealTopCards(f, e); return SkillProgramStepOutcome.Continue; }
}
public sealed class YanjiaoSplitRevealedCardsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.YanjiaoSplitRevealedCards;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IZhangChangPuProgramHost>().YanjiaoSplitRevealedCards(f, e);
}
