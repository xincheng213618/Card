namespace CardGame.Core;

internal interface IPublicPileCollectionProgramHost
{
    SkillProgramStepOutcome ExecutePublicPileCollection(SkillProgramEffect effect, ProgramSkillFrame frame);
}

internal sealed class CollectPublicPileDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.CollectPublicPile;
    public override ISkillProgramEffectHandler Handler { get; } = new CollectPublicPileHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    // Public-only acquisition prior. Selection never inspects an opponent's
    // private card faces; actual cards remain in a face-up persistent pile.
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "zones", "destinationZone", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        var amount = r.RequiredInt("amount");
        var zones = r.OptionalEnumArray<CardZoneKind>("zones") ?? [];
        var destination = r.RequiredEnum<CardZoneKind>("destinationZone");
        if (target != SkillProgramEffectTarget.Owner || amount is < 1 or > 2 ||
            destination != CardZoneKind.Authority || zones.Count == 0 ||
            zones.Distinct().Count() != zones.Count || zones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)))
            throw new InvalidOperationException("Public pile collection requires owner, one or two sources, hand/equipment slots and a public persistent destination.");
        return new(Op, target, amount, r.Condition(), zones: zones, destinationZone: destination);
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

public sealed class CollectPublicPileHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.CollectPublicPile;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((IPublicPileCollectionProgramHost)host).ExecutePublicPileCollection(effect, frame);
}
