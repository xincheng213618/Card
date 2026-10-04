namespace CardGame.Core;

public sealed record ProgramOwnTrickDrawReceipt(int InstructionIndex, ActualUseTargetIdentity Use,
    CardConversionSource Source, string GameplayHash, int TargetCount, long SequenceBefore,
    long SequenceAfter, int ActualDrawCount, bool Applied = false);
public sealed record OwnTrickDrawIssuedEvent(long ProgramFrameId, ProgramOwnTrickDrawReceipt Receipt) : IGameEvent;
public sealed record OwnTrickTargetNullifiedEvent(long ProgramFrameId, long CardUseFrameId, int TargetSeat,
    CardConversionSource Source) : IGameEvent;
public sealed record TurnHandCategoryRestriction(long GrantSequence, int TurnNumber, int TurnSeat,
    long ProducerProgramFrameId, long DamageWindowFrameId, CardUseEffectSource Source,
    int AffectedSeat, SkillProgramCardCategory Category);
public sealed record HandCategoryRestrictionGrantedEvent(TurnHandCategoryRestriction Restriction) : IGameEvent;

internal interface IOwnTrickAndHandCategoryProgramHost
{
    SkillProgramStepOutcome DrawThenNullifyOwnMultiTargetTrick(ProgramSkillFrame frame);
    void RestrictDamageSourceHandCategory(ProgramSkillFrame frame, SkillProgramCardCategory category);
}
internal sealed class DrawThenNullifyOwnMultiTargetTrickDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawThenNullifyOwnMultiTargetTrick;
    public override ISkillProgramEffectHandler Handler { get; } = new DrawThenNullifyOwnMultiTargetTrickHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.NullifyCurrentCardEffect,
        static (_, c) => { c.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, new(SkillProgramConditionKind.Always, 0, []))); c.NullifyCurrentCardEffect(); });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 1, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.OtherActualUseTargeted)];
}
internal sealed class RestrictDamageSourceHandCategoryDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RestrictDamageSourceHandCategory;
    public override ISkillProgramEffectHandler Handler { get; } = new RestrictDamageSourceHandCategoryHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnHandColorRestriction,
        static (_, c) => c.PublicControlValue(8));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "cardCategories", "condition");
        var categories = r.RequiredEnumArray<SkillProgramCardCategory>("cardCategories");
        if (categories.Count != 1) throw new InvalidOperationException($"Invalid skill program at {r.Path}: declare exactly one hand-card category.");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), cardCategories: categories);
        if (e.Condition.Kind != SkillProgramConditionKind.ChoiceIs)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: category issuance requires its exact declaration choice.");
        return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
public sealed class DrawThenNullifyOwnMultiTargetTrickHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DrawThenNullifyOwnMultiTargetTrick;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) =>
        ((IOwnTrickAndHandCategoryProgramHost)h).DrawThenNullifyOwnMultiTargetTrick(f);
}
public sealed class RestrictDamageSourceHandCategoryHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RestrictDamageSourceHandCategory;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h)
    { ((IOwnTrickAndHandCategoryProgramHost)h).RestrictDamageSourceHandCategory(f, e.CardCategories.Single()); return SkillProgramStepOutcome.Continue; }
}
internal static class OwnTrickAndHandCategoryComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow window, SkillProgramTriggerSubject? subject, bool optional)
    {
        if (!effects.Any(e => e.Op is SkillProgramEffectOp.DrawThenNullifyOwnMultiTargetTrick or SkillProgramEffectOp.RestrictDamageSourceHandCategory)) return;
        var draw = effects.Any(e => e.Op == SkillProgramEffectOp.DrawThenNullifyOwnMultiTargetTrick);
        var options = new[] { "basic", "trick", "equipment" };
        if (!optional || subject != SkillProgramTriggerSubject.Owner ||
            draw && (effects.Count != 1 || effects[0].Op != SkillProgramEffectOp.DrawThenNullifyOwnMultiTargetTrick || window != SkillProgramTriggerWindow.OtherActualUseTargeted) ||
            !draw && (window != SkillProgramTriggerWindow.AfterDamageApplied || effects.Count != 4 ||
                effects[0] is not { Op: SkillProgramEffectOp.ChooseOption, Target: SkillProgramEffectTarget.Owner, ResultBind: "category" } ||
                !effects[0].Options.Select(o => o.Id).SequenceEqual(options) ||
                effects.Skip(1).Where((e, i) => e.Op != SkillProgramEffectOp.RestrictDamageSourceHandCategory ||
                    !e.CardCategories.SequenceEqual([(SkillProgramCardCategory)i]) || e.Condition.Kind != SkillProgramConditionKind.ChoiceIs ||
                    e.Condition.SourceBind != "category" || e.Condition.OptionId != options[i]).Any()))
            throw new InvalidOperationException($"Invalid skill program at {path}: own-trick draw/nullification and damage-source category declarations require one exact optional owning-window instruction.");
    }
}
