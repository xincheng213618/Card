using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record DamageTargetBudgetReceipt
{
    private IReadOnlyList<int> _before = [], _added = [];
    private IReadOnlyList<PromptChoice> _choices = [];
    public long ProgramFrameId { get; init; }
    public long WindowFrameId { get; init; }
    public long CardUseFrameId { get; init; }
    public long ActionId { get; init; }
    public CardConversionSource Source { get; init; } = null!;
    public string GameplayHash { get; init; } = "";
    public PlayerMarkerKind Marker { get; init; }
    public int FrozenBudget { get; init; }
    public bool Completed { get; init; }
    public bool Settled { get; init; }
    public IReadOnlyList<int> OriginalTargets { get => _before; init => _before = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> AddedTargets { get => _added; init => _added = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<PromptChoice> Choices { get => _choices; init => _choices = Array.AsReadOnly(value.Select(PromptChoiceProjection.Freeze).ToArray()); }
}
public sealed record DamageTargetBudgetGainedEvent(long ProgramFrameId, long DamageWindowFrameId,
    CardConversionSource Source, PlayerMarkerKind Marker, int? DamageSourceSeat, int DamageTargetSeat, int DamageAmount, int NewCount) : IGameEvent;
public sealed record DamageTargetBudgetOfferedEvent(long ProgramFrameId, DamageTargetBudgetReceipt Receipt) : IGameEvent;
public sealed record DamageTargetBudgetResolvedEvent(long ProgramFrameId, DamageTargetBudgetReceipt Receipt,
    IReadOnlyList<int> ResultTargets) : IGameEvent;
public sealed record DamageTargetBudgetSettledEvent(long CardUseFrameId, long ActionId, CardConversionSource Source,
    PlayerMarkerKind Marker, int FrozenBudget, int MarkersBeforeSettlement, int FinalTargetCount, int Removed, int NewCount) : IGameEvent;
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DamageTargetBudgetReceipt? DamageTargetBudget { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DamageTargetBudgetReceipt? DamageTargetBudget { get; init; } }
internal interface IDamageTargetBudgetHost
{ SkillProgramStepOutcome ApplyDamageTargetBudget(SkillProgramEffect effect, ProgramSkillFrame frame); }
internal abstract class DamageTargetBudgetDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new DamageTargetBudgetHandler(Op);
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.AddDamageMarkerBudgetTargets
        ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(4));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "marker", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), marker: r.RequiredEnum<PlayerMarkerKind>("marker"));
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(Op == SkillProgramEffectOp.GainDamageOccurrenceTargetBudget
            ? SkillProgramTriggerWindow.AfterDamageApplied : SkillProgramTriggerWindow.CardUseTargetsFinalized)];
}
internal sealed class GainDamageOccurrenceTargetBudgetDescriptor : DamageTargetBudgetDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.GainDamageOccurrenceTargetBudget; }
internal sealed class AddDamageMarkerBudgetTargetsDescriptor : DamageTargetBudgetDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.AddDamageMarkerBudgetTargets; }
internal sealed class DamageTargetBudgetHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IDamageTargetBudgetHost)host).ApplyDamageTargetBudget(e, f);
}
internal static class DamageTargetBudgetContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => e.Op is SkillProgramEffectOp.GainDamageOccurrenceTargetBudget or SkillProgramEffectOp.AddDamageMarkerBudgetTargets)) return;
        if (t.Effects is not [var e] || t.Optional || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            t.UsageScope is not null || t.MarkerCost is not null || t.NamedUsageGroup is not null || t.ChoiceGroup is not null ||
            t.IncludeResponseUses || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 ||
            (e.Op == SkillProgramEffectOp.GainDamageOccurrenceTargetBudget
                ? t.Window != SkillProgramTriggerWindow.AfterDamageApplied || t.Subject != SkillProgramTriggerSubject.Any || t.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage
                : t.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor))
            throw new InvalidOperationException($"{path}: damage budget requires one mandatory exact per-damage OR-participant or actual actor designation binding.");
    }
}
