using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum SameNameHandStage { ChoosingTarget, ChoosingPayment, DiscardChildren, DamageIssued, Complete }
public sealed record SameNameHandMaterial(int CardId, CardKind PrintedKind, CardLocation From);
public sealed record SameNameHandStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long ActionId, long CardWindowId, long OriginalParentFrameId, CardKind EffectiveKind, CardKind NormalizedName,
    int ActualTurnNumber, int ActualTurnOwnerSeat) : IGameEvent;
public sealed record SameNameHandTargetSelectedEvent(long FrameId, int TargetSeat) : IGameEvent;
public sealed record SameNameHandDiscardPaidEvent(long FrameId, int TargetSeat, int CardId,
    long SequenceBefore, long SequenceAfter, long BatchId) : IGameEvent;
public sealed record SameNameHandDamageIssuedEvent(long FrameId, int SourceSeat, int TargetSeat, int Amount) : IGameEvent;
public sealed record SameNameHandCompletedEvent(long FrameId, int? TargetSeat, bool Discarded, bool DamageIssued) : IGameEvent;

public sealed record ProgramSameNameHandReceipt
{
    private IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<SameNameHandMaterial> _materials = Array.AsReadOnly(Array.Empty<SameNameHandMaterial>());
    public required CardConversionSource Source { get; init; }
    public required string GameplayHash { get; init; }
    public long ActionId { get; init; }
    public long CardWindowId { get; init; }
    public long OriginalParentFrameId { get; init; }
    public CardKind EffectiveKind { get; init; }
    public CardKind NormalizedName { get; init; }
    public int ActualTurnNumber { get; init; }
    public int ActualTurnOwnerSeat { get; init; }
    public SameNameHandStage Stage { get; init; }
    public IReadOnlyList<int> CandidateSeats { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public int? TargetSeat { get; init; }
    public IReadOnlyList<SameNameHandMaterial> EligibleMaterials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
    public SameNameHandMaterial? PaidMaterial { get; init; }
    public long SequenceBefore { get; init; }
    public long SequenceAfter { get; init; }
    public long? BatchId { get; init; }
    public bool DamageIssued { get; init; }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramSameNameHandReceipt? SameNameHandDiscardOrDamage { get; init; }
}
internal interface ISameNameHandDiscardOrDamageProgramHost
{
    SkillProgramStepOutcome OfferSameNameHandDiscardOrDamage(ProgramSkillFrame frame);
}
internal sealed class OfferSameNameHandDiscardOrDamageDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferSameNameHandDiscardOrDamage;
    public override ISkillProgramEffectHandler Handler { get; } = new OfferSameNameHandDiscardOrDamageHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "condition");
        if (r.RequiredInt("amount") != 1) throw new InvalidOperationException("A same-name hand demand deals exactly one damage.");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 1, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindows([SkillProgramTriggerWindow.CardUseCompleted, SkillProgramTriggerWindow.CardResponseCompleted, SkillProgramTriggerWindow.CardSupplyCompleted])];
}
public sealed class OfferSameNameHandDiscardOrDamageHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.OfferSameNameHandDiscardOrDamage;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((ISameNameHandDiscardOrDamageProgramHost)host).OfferSameNameHandDiscardOrDamage(f);
}
internal static class SameNameHandDiscardOrDamageComposition
{
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        if (program.Activations.Any(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.OfferSameNameHandDiscardOrDamage)))
            throw new InvalidOperationException($"Invalid skill program at {path}: a same-name demand requires a completed actual action.");
        var triggers = program.Triggers.Where(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.OfferSameNameHandDiscardOrDamage)).ToArray();
        if (triggers.Length == 0) return;
        if (triggers.Length != 3 || triggers.Count(t => t.Window == SkillProgramTriggerWindow.CardUseCompleted) != 1 ||
            triggers.Count(t => t.Window == SkillProgramTriggerWindow.CardResponseCompleted) != 1 ||
            triggers.Count(t => t.Window == SkillProgramTriggerWindow.CardSupplyCompleted) != 1 || triggers.Any(t =>
                t.Effects is not [{ Op: SkillProgramEffectOp.OfferSameNameHandDiscardOrDamage, Target: SkillProgramEffectTarget.Owner,
                    Amount: 1, Condition.Kind: SkillProgramConditionKind.Always }] || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor ||
                !t.Optional || t.IncludeResponseUses || t.UsageScope is not null || t.UsageLimit is not null ||
                t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null || t.MarkerCost is not null || t.ChoiceGroup is not null ||
                t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.SourceSkillId is not null || t.SourceViewAsId is not null ||
                t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.TurnOwnerScope != SkillProgramTurnOwnerScope.Own))
            throw new InvalidOperationException($"Invalid skill program at {path}: a same-name hand demand requires exactly three optional unlimited actor completions, with no response-use duplication or unrelated filters.");
    }
}
