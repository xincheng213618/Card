namespace CardGame.Core;

internal interface IConfiguredConversionProgramHost
{
    SkillProgramStepOutcome DeclareBoundCardName(ProgramSkillFrame frame, string sourceBind, string stateId);
    void UpgradeConversionTier(ProgramSkillFrame frame, string stateId);
}

internal sealed class DeclareBoundCardNameUntilTurnEndProgramDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DeclareBoundCardNameUntilTurnEnd;
    public override ISkillProgramEffectHandler Handler { get; } = new DeclareBoundCardNameUntilTurnEndProgramHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "stateId", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException("A declared conversion requires its owner.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, 0, r.Condition(),
            sourceBind: r.RequiredIdentifier("sourceBind"), stateId: r.RequiredIdentifier("stateId"));
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSingleCardSet(effect.SourceBind!), new RetainOwnedCardSet(effect.SourceBind!)];
}

internal sealed class UpgradeConversionTierProgramDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.UpgradeConversionTier;
    public override ISkillProgramEffectHandler Handler { get; } = new UpgradeConversionTierProgramHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stateId", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException("A conversion tier belongs to its owner.");
        return new(Op, SkillProgramEffectTarget.Owner, 0, r.Condition(), stateId: r.RequiredIdentifier("stateId"));
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

public sealed class DeclareBoundCardNameUntilTurnEndProgramHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DeclareBoundCardNameUntilTurnEnd;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((IConfiguredConversionProgramHost)host).DeclareBoundCardName(frame, effect.SourceBind!, effect.StateId!);
}

public sealed class UpgradeConversionTierProgramHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.UpgradeConversionTier;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    {
        ((IConfiguredConversionProgramHost)host).UpgradeConversionTier(frame, effect.StateId!);
        return SkillProgramStepOutcome.Continue;
    }
}
