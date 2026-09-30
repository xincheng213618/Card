namespace CardGame.Core;

public sealed record ProgramDiscardChallengeDraft(string Mode, int ChooserSeat, int Cursor,
    int PreviousCount, int Remaining, bool ComplementOnly, IReadOnlyList<int> SelectedIds);

internal interface IDiscardChallengeProgramHost
{
    SkillProgramStepOutcome ExecuteDiscardChallenge(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat);
}

internal abstract class DiscardChallengeDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "secondaryAmount", "cardCategories", "zones", "nature", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        var amount = r.RequiredInt("amount");
        var categories = r.OptionalEnumArray<SkillProgramCardCategory>("cardCategories") ?? [];
        var zones = r.OptionalEnumArray<CardZoneKind>("zones") ?? [];
        var secondary = r.Has("secondaryAmount") ? r.RequiredInt("secondaryAmount") : 0;
        if (amount < 1 || amount > 64 || zones.Count == 0 || zones.Distinct().Count() != zones.Count ||
            zones.Any(z => z is not (CardZoneKind.Hand or CardZoneKind.Equipment)) ||
            Op == SkillProgramEffectOp.ChooseCategoryAlternativeDiscard &&
                (target != SkillProgramEffectTarget.SelectedTarget || amount != 1 || secondary < 1 || secondary > 64 || categories.Count == 0) ||
            Op == SkillProgramEffectOp.EscalatingDiscardOrDamage && (target != SkillProgramEffectTarget.Owner || categories.Count != 0 || secondary != 0))
            throw new InvalidOperationException($"Invalid discard challenge at {r.Path}.");
        return new(Op, target, amount, r.Condition(), minimumValue: secondary, zones: zones,
            cardCategories: categories, damageNature: r.Has("nature") ? r.RequiredEnum<DamageNature>("nature") : null);
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => WithSelectedTarget(effect);
}
internal sealed class ChooseCategoryAlternativeDiscardDescriptor : DiscardChallengeDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseCategoryAlternativeDiscard;
    public override ISkillProgramEffectHandler Handler { get; } = new ChooseCategoryAlternativeDiscardHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (effect, context) => context.CategoryAlternativeDiscard(effect));
}
internal sealed class EscalatingDiscardOrDamageDescriptor : DiscardChallengeDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.EscalatingDiscardOrDamage;
    public override ISkillProgramEffectHandler Handler { get; } = new EscalatingDiscardOrDamageHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (effect, context) => context.EscalatingDiscardOrDamage(effect));
}
public abstract class DiscardChallengeHandler : ISkillProgramEffectHandler
{
    public abstract SkillProgramEffectOp Op { get; }
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host) => ((IDiscardChallengeProgramHost)host).ExecuteDiscardChallenge(effect, frame, targetSeat);
}
public sealed class ChooseCategoryAlternativeDiscardHandler : DiscardChallengeHandler
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseCategoryAlternativeDiscard; }
public sealed class EscalatingDiscardOrDamageHandler : DiscardChallengeHandler
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.EscalatingDiscardOrDamage; }
