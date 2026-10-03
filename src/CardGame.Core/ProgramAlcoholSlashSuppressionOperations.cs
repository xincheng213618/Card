namespace CardGame.Core;

internal interface IAlcoholSlashSuppressionProgramHost
{
    void SuppressOwnSkillAfterAlcoholSlashDamage(ProgramSkillFrame frame, string skillId);
    SkillProgramStepOutcome StartOwnedDamagePointJudgment(ProgramSkillFrame frame, SkillProgramEffect effect);
}

internal sealed class SuppressOwnSkillAfterAlcoholSlashDamageDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SuppressOwnSkillAfterAlcoholSlashDamage;
    public override ISkillProgramEffectHandler Handler { get; } = new SuppressOwnSkillAfterAlcoholSlashDamageHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnRuleModifier,
        static (_, context) => context.PublicControlValue(4d));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "skillIds", "condition");
        var target = reader.RequiredEnum<SkillProgramEffectTarget>("target");
        var skillIds = reader.RequiredIdentifierArray("skillIds");
        if (target != SkillProgramEffectTarget.Owner || skillIds.Count != 1)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}: alcohol-Slash suppression requires one named own skill.");
        var effect = new SkillProgramEffect(Op, target, 0, reader.Condition(), skillIds: skillIds);
        RequireAlways(effect, reader.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}

public sealed class SuppressOwnSkillAfterAlcoholSlashDamageHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SuppressOwnSkillAfterAlcoholSlashDamage;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host)
    {
        ((IAlcoholSlashSuppressionProgramHost)host).SuppressOwnSkillAfterAlcoholSlashDamage(frame, effect.SkillIds.Single());
        return SkillProgramStepOutcome.Continue;
    }
}

internal sealed class StartOwnedDamagePointJudgmentDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.StartOwnedDamagePointJudgment;
    public override ISkillProgramEffectHandler Handler { get; } = new StartOwnedDamagePointJudgmentHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage | ProgramContextCapability.Judgment;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.StartJudgment,
        static (effect, context) => context.StartJudgment(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "judgmentReason", "resultBind", "visibility", "condition");
        var target = reader.RequiredEnum<SkillProgramEffectTarget>("target");
        var visibility = reader.RequiredEnum<SkillProgramCardSetVisibility>("visibility");
        if (target != SkillProgramEffectTarget.Owner || visibility != SkillProgramCardSetVisibility.Public)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}: damage-point judgment requires its owner's public judgment.");
        var effect = new SkillProgramEffect(Op, target, 0, reader.Condition(),
            judgmentReason: reader.RequiredIdentifier("judgmentReason"), resultBind: reader.RequiredIdentifier("resultBind"),
            visibility: visibility, sourceRef: new ProgramParticipantReference(ProgramParticipantRef.EventSource));
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied), new CreateCardSet(effect.ResultBind!, 1, true)];
}

public sealed class StartOwnedDamagePointJudgmentHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.StartOwnedDamagePointJudgment;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host) => ((IAlcoholSlashSuppressionProgramHost)host).StartOwnedDamagePointJudgment(frame, effect);
}
