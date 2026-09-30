namespace CardGame.Core;

internal sealed class OfferVirtualSlashOrDrawProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferVirtualSlashOrDraw;
    public override ISkillProgramEffectHandler Handler { get; } = new OfferVirtualSlashOrDrawSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.UseSelectedCardsAs, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.SelectedTarget) throw new InvalidOperationException("A virtual Slash offer requires a selected actor.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition());
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new ReadSelectedTarget()];
}

public sealed class OfferVirtualSlashOrDrawSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.OfferVirtualSlashOrDraw;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) => host.OfferVirtualSlashOrDraw(effect, frame, targetSeat);
}

internal sealed class GrantTurnCardEffectImmunityProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnCardEffectImmunity;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantTurnCardEffectImmunitySkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnRuleModifier, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "cardKinds", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner) throw new InvalidOperationException("Turn card-effect immunity is owner-scoped.");
        var kinds = r.RequiredEnumArray<CardKind>("cardKinds");
        if (kinds.Count == 0 || kinds.Distinct().Count() != kinds.Count || kinds.Any(kind => !CardEffectImmunityKinds.Contains(kind))) throw new InvalidOperationException("Card-effect immunity requires Slash or ordinary trick kinds.");
        return new SkillProgramEffect(Op, target, 0, r.Condition(), cardKinds: kinds);
    }
    internal static IReadOnlyList<CardKind> CardEffectImmunityKinds { get; } = Array.AsReadOnly(Enum.GetValues<CardKind>().Where(kind => kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash || CardUseCategoryCatalog.Get(kind) == CardUseCategories.InstantTrick).ToArray());
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

public sealed class GrantTurnCardEffectImmunitySkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnCardEffectImmunity;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    {
        host.GrantTurnCardEffectImmunity(frame, targetSeat, effect.CardKinds);
        return SkillProgramStepOutcome.Continue;
    }
}
