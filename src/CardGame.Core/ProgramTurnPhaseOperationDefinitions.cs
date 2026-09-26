namespace CardGame.Core;

internal sealed class SkipTurnPhasesProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SkipTurnPhases;
    public override ISkillProgramEffectHandler Handler { get; } = new SkipTurnPhasesSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.PhaseSubstitution;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.InsertPhase,
        static (_, _) => { });

    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "phases", "condition");
        var target = reader.RequiredEnum<SkillProgramEffectTarget>("target");
        var phases = reader.RequiredEnumArray<SkillProgramTurnPhase>("phases");
        if (target != SkillProgramEffectTarget.Owner || phases.Count == 0 ||
            phases.Distinct().Count() != phases.Count)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}: phase substitution requires an owner and distinct phases.");
        var effect = new SkillProgramEffect(Op, target, 0, reader.Condition(), skippedPhases: phases);
        RequireAlways(effect, reader.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

internal sealed class UseVirtualCardProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseVirtualCard;
    public override ISkillProgramEffectHandler Handler { get; } = new UseVirtualCardSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.PhaseSubstitution;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.UseSelectedCardsAs,
        static (_, _) => { });

    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "outputKind", "targetRestriction", "condition");
        var target = reader.RequiredEnum<SkillProgramEffectTarget>("target");
        var kind = reader.RequiredEnum<CardKind>("outputKind");
        var restriction = reader.RequiredEnum<SkillProgramCardTargetRestriction>("targetRestriction");
        if (target != SkillProgramEffectTarget.SelectedTarget || kind != CardKind.Slash ||
            restriction != SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}: virtual card use currently supports one selected unlimited-distance Slash.");
        var effect = new SkillProgramEffect(Op, target, 0, reader.Condition(), outputKind: kind,
            targetRestriction: restriction);
        RequireAlways(effect, reader.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget()];
}
