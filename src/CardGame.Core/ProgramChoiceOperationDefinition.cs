namespace CardGame.Core;

/// <summary>A named, conditional choice; effects remain ordinary subsequent instructions.</summary>
internal sealed class ChooseOptionProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseOption;
    public override ISkillProgramEffectHandler Handler { get; } = new ChooseOptionSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.ChooseOption, static (_, _) => { });

    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "resultBind", "options", "condition");
        var effect = new SkillProgramEffect(Op, reader.RequiredEnum<SkillProgramEffectTarget>("target"),
            0, reader.Condition(), resultBind: reader.RequiredIdentifier("resultBind"),
            options: reader.ChoiceOptions());
        RequireAlways(effect, reader.Path);
        foreach (var option in effect.Options)
            ValidateOptionCondition(option.Condition, reader.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        WithSelectedTarget(effect, [new CreateChoiceResult(effect.ResultBind!, effect.Options.Select(option => option.Id).ToArray())]);

    private static void ValidateOptionCondition(SkillProgramCondition condition, string path)
    {
        if (condition.Kind is not (SkillProgramConditionKind.Always or SkillProgramConditionKind.Wounded or
            SkillProgramConditionKind.HpAtLeast or SkillProgramConditionKind.HandCountAtLeast or
            SkillProgramConditionKind.FaceDown or SkillProgramConditionKind.Chained or
            SkillProgramConditionKind.HasClaimableDamageCards or SkillProgramConditionKind.HasOwnedCardCategory or
            SkillProgramConditionKind.BoundCardCountAtLeast or
            SkillProgramConditionKind.All or SkillProgramConditionKind.Any or SkillProgramConditionKind.Not))
            throw new InvalidOperationException($"Invalid skill program at {path}: option conditions require public chooser state.");
        foreach (var child in condition.Children) ValidateOptionCondition(child, path);
    }
}
