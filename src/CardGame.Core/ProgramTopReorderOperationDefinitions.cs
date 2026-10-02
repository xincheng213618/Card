namespace CardGame.Core;

internal sealed class ReorderTopCardsProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReorderTopCards;
    public override ISkillProgramEffectHandler Handler { get; } = new ReorderTopCardsSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Subset,
        static (_, _) => { });

    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "amount", "numberExpression", "exactTopCount", "condition");
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
        var effect = new SkillProgramEffect(Op, target, maximum, reader.Condition(), numberExpression: expression)
        { ExactTopCount = exact };
        RequireAlways(effect, reader.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}
