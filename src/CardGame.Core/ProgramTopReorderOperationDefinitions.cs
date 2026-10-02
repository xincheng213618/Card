namespace CardGame.Core;

internal sealed class ReorderTopCardsProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReorderTopCards;
    public override ISkillProgramEffectHandler Handler { get; } = new ReorderTopCardsSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Subset,
        static (effect, context) => { if (effect.PopulationThresholdCount is not null) context.PublicControlValue(4d); });

    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "amount", "numberExpression", "exactTopCount", "populationThreshold", "belowPopulationAmount", "allBottomStateId", "condition");
        var target = reader.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}.target: top ordering requires owner.");
        var maximum = reader.RequiredInt("amount");
        if (maximum is < 1 or > 16)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}.amount: top ordering requires 1..16 cards.");
        var expression = reader.Has("numberExpression")
            ? reader.RequiredEnum<SkillProgramNumberExpression>("numberExpression")
            : (SkillProgramNumberExpression?)null;
        if (expression is not null and not SkillProgramNumberExpression.LivingPlayerCount)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}.numberExpression: only livingPlayerCount is supported.");
        var exact = reader.Has("exactTopCount") ? reader.RequiredInt("exactTopCount") : (int?)null;
        if (exact is { } required && (required < 1 || required > maximum))
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}.exactTopCount: requires 1..amount.");
        ProgramPopulationThresholdCount? population = null;
        if (reader.Has("populationThreshold") || reader.Has("belowPopulationAmount"))
        {
            var threshold = reader.RequiredInt("populationThreshold");
            var below = reader.RequiredInt("belowPopulationAmount");
            if (threshold is < 1 or > 16 || below < 1 || below > maximum || expression is not null || exact is not null)
                throw new InvalidOperationException($"Invalid skill program at {reader.Path}: population count requires threshold 1..16, below amount 1..amount and no other count mode.");
            population = new(threshold, below);
        }
        var completion = reader.OptionalIdentifier("allBottomStateId");
        if (completion is not null && population is null)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}.allBottomStateId: requires population count opt-in.");
        var effect = new SkillProgramEffect(Op, target, maximum, reader.Condition(), numberExpression: expression)
        { ExactTopCount = exact, PopulationThresholdCount = population, AllBottomStateId = completion };
        RequireAlways(effect, reader.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}
