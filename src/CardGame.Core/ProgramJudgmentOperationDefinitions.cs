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

internal sealed class ClaimJudgmentCardProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimJudgmentCard;
    public override ISkillProgramEffectHandler Handler { get; } = new ClaimJudgmentCardSkillProgramEffectHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Judgment;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, _) => { });

    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        var target = reader.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}.target: judgment claim requires owner.");
        var effect = new SkillProgramEffect(Op, target, 0, reader.Condition());
        RequireAlways(effect, reader.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

internal sealed class RepeatJudgmentProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RepeatJudgment;
    public override ISkillProgramEffectHandler Handler { get; } = new RepeatJudgmentSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Judgment;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.StartJudgment,
        static (effect, context) =>
        {
            // The new free black-repeat claim uses a public two-suit prior: one expected card.
            // The old nullable policy retains its existing inert estimate.
            if (effect.ClaimHandLimitExemption == SkillProgramClaimHandLimitExemption.ActualTurn)
                context.Draw(new SkillProgramEffect(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition));
        });

    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "judgmentReason", "resultBind", "suits", "condition", "claimHandLimitExemption");
        var target = reader.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}.target: repeated judgment requires owner.");
        var suits = reader.RequiredEnumArray<Suit>("suits");
        if (suits.Count == 0)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}.suits: repeated judgment requires success suits.");
        var effect = new SkillProgramEffect(Op, target, 0, reader.Condition(),
            judgmentReason: reader.RequiredIdentifier("judgmentReason"),
            resultBind: reader.RequiredIdentifier("resultBind"), suits: suits)
        { ClaimHandLimitExemption = reader.Has("claimHandLimitExemption") ? reader.RequiredEnum<SkillProgramClaimHandLimitExemption>("claimHandLimitExemption") : null };
        RequireAlways(effect, reader.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        effect.ClaimHandLimitExemption is null ? [] : [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
