namespace CardGame.Core;

public sealed record ProgramAllHandChallengeQuotaPaidEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, int RoundNumber, int ActualTurnNumber, int ActualTurnOwnerSeat) : IGameEvent;

internal abstract class AllHandChallengeDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        var amount = r.RequiredInt("amount");
        var ring = Op == SkillProgramEffectOp.AllHandDiscardOrDifferenceDamageFromSelected;
        if (target != (ring ? SkillProgramEffectTarget.Owner : SkillProgramEffectTarget.SelectedTarget) || amount != (ring ? 2 : 1))
            throw new InvalidOperationException($"Invalid all-hand challenge contract at {r.Path}.");
        var effect = new SkillProgramEffect(Op, target, amount, r.Condition(), zones: [CardZoneKind.Hand],
            cardCategories: ring ? [] : [SkillProgramCardCategory.Trick], damageNature: ring ? DamageNature.Fire : null);
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new ReadSelectedTarget()];
}
internal sealed class ChooseTrickOrAllHandDiscardDescriptor : AllHandChallengeDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseTrickOrAllHandDiscard;
    public override ISkillProgramEffectHandler Handler { get; } = new ChooseTrickOrAllHandDiscardHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (e, c) => c.CategoryAlternativeDiscard(e));
}
internal sealed class AllHandDiscardOrDifferenceDamageFromSelectedDescriptor : AllHandChallengeDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AllHandDiscardOrDifferenceDamageFromSelected;
    public override ISkillProgramEffectHandler Handler { get; } = new AllHandDiscardOrDifferenceDamageFromSelectedHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (e, c) => c.EscalatingDiscardOrDamage(e));
}
public sealed class ChooseTrickOrAllHandDiscardHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseTrickOrAllHandDiscard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<ISequentialDiscardProgramHost>().RunSequentialDiscard(f, e, seat);
}
public sealed class AllHandDiscardOrDifferenceDamageFromSelectedHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.AllHandDiscardOrDifferenceDamageFromSelected;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<ISequentialDiscardProgramHost>().RunSequentialDiscard(f, e, seat);
}

internal static class AllHandChallengeComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects, SkillProgramTriggerWindow? window,
        int selectedCardCount, bool selectedTarget, int initialTargetSetMaximum)
    {
        if (effects.Any(e => e.Op is SkillProgramEffectOp.ChooseTrickOrAllHandDiscard or SkillProgramEffectOp.AllHandDiscardOrDifferenceDamageFromSelected) &&
            (window is not null || selectedCardCount != 0 || !selectedTarget || initialTargetSetMaximum != 0 || effects.Count != 1))
            throw new InvalidOperationException($"{path}: an all-hand challenge requires a standalone zero-card, original one-target activation.");
    }
}
