namespace CardGame.Core;

internal sealed class ReplaceJudgmentProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReplaceJudgment;
    public override ISkillProgramEffectHandler Handler { get; } = new ReplaceJudgmentSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.JudgmentReplacement;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ReplaceJudgment,
        static (_, _) => { });

    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "zones", "suits", "oldCardDestination", "condition");
        var target = reader.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}.target: judgment replacement requires owner.");
        var zones = reader.RequiredEnumArray<CardZoneKind>("zones");
        if (zones.Count == 0 || zones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)))
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}.zones: replacement requires hand or equipment.");
        var suits = reader.RequiredEnumArray<Suit>("suits");
        if (suits.Count == 0)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}.suits: replacement requires at least one suit.");
        var destination = reader.RequiredEnum<SkillProgramOldJudgmentCardDestination>("oldCardDestination");
        var effect = new SkillProgramEffect(Op, target, 0, reader.Condition(), zones: zones, suits: suits,
            oldCardDestination: destination);
        RequireAlways(effect, reader.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}
