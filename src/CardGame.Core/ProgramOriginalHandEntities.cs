using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum OriginalHandBenefitKind { HandLimit, AttackRange, Draw }
public enum OriginalHandBenefitStage { ChoosingTarget, ChoosingOption, DrawChildren, Complete }
public enum OriginalHandInheritanceStage { ChoosingTarget, Complete }
public enum OriginalHandAwakeningStage { MaximumPaid, GrantsIssued, Complete }
public sealed record OriginalHandPermanentBonus(long BonusId, int SourceOwnerSeat, string SourceSkillId,
    string StateId, int RecipientSeat, OriginalHandBenefitKind Kind);
public sealed record OriginalHandEntitySnapshot
{
    private IReadOnlyList<int>? _cardIds;
    public OriginalHandEntitySnapshot(string skillId, string stateId, string name, int remainingCount, IReadOnlyList<int>? cardIds) =>
        (SkillId, StateId, Name, RemainingCount, CardIds) = (skillId, stateId, name, remainingCount, cardIds);
    public string SkillId { get; init; }
    public string StateId { get; init; }
    public string Name { get; init; }
    public int RemainingCount { get; init; }
    public IReadOnlyList<int>? CardIds { get => _cardIds; init => _cardIds = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record PlayerSnapshot
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<OriginalHandEntitySnapshot>? OriginalHandEntities { get; init; }
}

public sealed record ProgramOriginalHandBenefitReceipt
{
    private IReadOnlyList<int> _candidates = Array.AsReadOnly(Array.Empty<int>());
    public int InstructionIndex { get; init; }
    public required CardConversionSource Source { get; init; }
    public required string GameplayHash { get; init; }
    public required string StateId { get; init; }
    public long MovementWindowId { get; init; }
    public long BatchId { get; init; }
    public long MovementSequence { get; init; }
    public OriginalHandBenefitStage Stage { get; init; }
    public IReadOnlyList<int> CandidateSeats { get => _candidates; init => _candidates = Array.AsReadOnly(value.ToArray()); }
    public int? TargetSeat { get; init; }
    public OriginalHandBenefitKind? Option { get; init; }
    public bool BonusApplied { get; init; }
    public bool DrawIssued { get; init; }
    public int ActualDrawCount { get; init; }
    public long DrawBefore { get; init; }
    public long DrawAfter { get; init; }
}
public sealed record ProgramOriginalHandInheritanceReceipt
{
    private IReadOnlyList<int> _candidates = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<OriginalHandPermanentBonus> _bonuses = Array.AsReadOnly(Array.Empty<OriginalHandPermanentBonus>());
    public int InstructionIndex { get; init; }
    public required CardConversionSource Source { get; init; }
    public required string GameplayHash { get; init; }
    public required string StateId { get; init; }
    public long DeathFrameId { get; init; }
    public long DeathWindowId { get; init; }
    public OriginalHandInheritanceStage Stage { get; init; }
    public IReadOnlyList<int> CandidateSeats { get => _candidates; init => _candidates = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<OriginalHandPermanentBonus> Bonuses { get => _bonuses; init => _bonuses = Array.AsReadOnly(value.ToArray()); }
    public int? TargetSeat { get; init; }
    public bool Applied { get; init; }
}
public sealed record ProgramOriginalHandAwakeningReceipt
{
    private IReadOnlyList<string> _grants = Array.AsReadOnly(Array.Empty<string>());
    public int InstructionIndex { get; init; }
    public required CardConversionSource Source { get; init; }
    public required string GameplayHash { get; init; }
    public required string SourceSkillId { get; init; }
    public required string StateId { get; init; }
    public long InitializationFrameId { get; init; }
    public int OriginalCount { get; init; }
    public long LifecycleFrameId { get; init; }
    public int ActualTurnNumber { get; init; }
    public int ActualTurnOwnerSeat { get; init; }
    public int MaximumBefore { get; init; }
    public int MaximumAfter { get; init; }
    public int HpBefore { get; init; }
    public int HpAfter { get; init; }
    public IReadOnlyList<string> SkillIds { get => _grants; init => _grants = Array.AsReadOnly(value.ToArray()); }
    public OriginalHandAwakeningStage Stage { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramOriginalHandBenefitReceipt? OriginalHandBenefit { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramOriginalHandInheritanceReceipt? OriginalHandInheritance { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramOriginalHandAwakeningReceipt? OriginalHandAwakening { get; init; }
}

// The actual initial card ids live only in the engine's private durable state.
// A movement sequence identifies an already existing native invoice; these new
// public facts never serialize an initial-hand entity list or selected card id.
public sealed record OriginalHandEntitiesInitializedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, string StateId, int OriginalCount, int RemainingCount) : IGameEvent;
public sealed record OriginalHandEntityConsumedEvent(int OwnerSeat, string SkillId, string StateId,
    long MovementSequence, int RemainingCount) : IGameEvent;
public sealed record OriginalHandLossEligibleEvent(int OwnerSeat, string SkillId, string StateId,
    string SkillInstanceId, string GameplayHash, string BindingId, long MovementSequence) : IGameEvent;
public sealed record OriginalHandBenefitStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    string StateId, long MovementWindowId, long BatchId, long MovementSequence) : IGameEvent;
public sealed record OriginalHandBenefitChosenEvent(long FrameId, int TargetSeat, OriginalHandBenefitKind Kind) : IGameEvent;
public sealed record OriginalHandPermanentBonusGrantedEvent(long FrameId, int SourceOwnerSeat, string SourceSkillId,
    string StateId, int RecipientSeat, OriginalHandBenefitKind Kind) : IGameEvent;
public sealed record OriginalHandBenefitDrawIssuedEvent(long FrameId, int TargetSeat, int ActualCount,
    long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record OriginalHandBenefitCompletedEvent(long FrameId, int? TargetSeat, OriginalHandBenefitKind? Kind,
    bool BonusApplied, bool DrawIssued, int ActualDrawCount) : IGameEvent;
public sealed record OriginalHandInheritanceStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    string StateId, long DeathFrameId, long DeathWindowId, int BonusCount) : IGameEvent;
public sealed record OriginalHandBonusTransferredEvent(long FrameId, long BonusId, int SourceOwnerSeat,
    string SourceSkillId, string StateId, int PreviousRecipientSeat, int RecipientSeat, OriginalHandBenefitKind Kind) : IGameEvent;
public sealed record OriginalHandInheritanceCompletedEvent(long FrameId, int? TargetSeat, int TransferredCount) : IGameEvent;
public sealed record OriginalHandAwakeningMaximumPaidEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    string SourceSkillId, string StateId, long InitializationFrameId, int OriginalCount, long LifecycleFrameId,
    int ActualTurnNumber, int ActualTurnOwnerSeat, int MaximumBefore, int MaximumAfter, int HpBefore, int HpAfter) : IGameEvent;
public sealed record OriginalHandAwakeningGrantIssuedEvent(long FrameId, int OwnerSeat, string SourceSkillId, string GrantedSkillId) : IGameEvent;
public sealed record OriginalHandAwakeningCompletedEvent(long FrameId, bool GrantsIssued, bool OwnerAlive) : IGameEvent;

internal interface IOriginalHandEntitiesProgramHost
{
    SkillProgramStepOutcome ExecuteOriginalHandEntityOperation(ProgramSkillFrame frame, SkillProgramEffect effect);
}
internal abstract class OriginalHandEntityDescriptorBase : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => Op switch {
        SkillProgramEffectOp.InitializeOriginalHandEntities => new InitializeOriginalHandEntitiesHandler(),
        SkillProgramEffectOp.OfferOriginalHandLossBenefit => new OfferOriginalHandLossBenefitHandler(),
        SkillProgramEffectOp.InheritOriginalHandBonuses => new InheritOriginalHandBonusesHandler(),
        _ => new AwakenWhenOriginalHandEmptyHandler() };
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override ProgramOperationInteraction Interaction => Op is SkillProgramEffectOp.OfferOriginalHandLossBenefit or
        SkillProgramEffectOp.InheritOriginalHandBonuses ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        if (Op == SkillProgramEffectOp.AwakenWhenOriginalHandEmpty)
        {
            r.AllowOnly("op", "target", "stateId", "sourceSkillId", "skillIds", "condition");
            var ids = r.RequiredIdentifierArray("skillIds");
            if (ids.Count == 0 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
                throw new InvalidOperationException("Original-hand awakening needs distinct nonempty grants.");
            var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), -1, r.Condition(),
                sourceBind: r.RequiredIdentifier("sourceSkillId"), stateId: r.RequiredIdentifier("stateId"), skillIds: ids);
            RequireAlways(e, r.Path); return e;
        }
        r.AllowOnly("op", "target", "stateId", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 1,
            r.Condition(), stateId: r.RequiredIdentifier("stateId"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(Op switch {
            SkillProgramEffectOp.InitializeOriginalHandEntities => SkillProgramTriggerWindow.GameStarting,
            SkillProgramEffectOp.OfferOriginalHandLossBenefit => SkillProgramTriggerWindow.CardsMoved,
            SkillProgramEffectOp.InheritOriginalHandBonuses => SkillProgramTriggerWindow.OwnerDied,
            _ => SkillProgramTriggerWindow.TurnStartBeforeNormalFlow })];
}
internal sealed class InitializeOriginalHandEntitiesDescriptor : OriginalHandEntityDescriptorBase
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.InitializeOriginalHandEntities; }
internal sealed class OfferOriginalHandLossBenefitDescriptor : OriginalHandEntityDescriptorBase
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferOriginalHandLossBenefit; }
internal sealed class InheritOriginalHandBonusesDescriptor : OriginalHandEntityDescriptorBase
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.InheritOriginalHandBonuses; }
internal sealed class AwakenWhenOriginalHandEmptyDescriptor : OriginalHandEntityDescriptorBase
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.AwakenWhenOriginalHandEmpty; }
public abstract class OriginalHandEntityHandler : ISkillProgramEffectHandler
{
    public abstract SkillProgramEffectOp Op { get; }
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        ((IOriginalHandEntitiesProgramHost)host).ExecuteOriginalHandEntityOperation(frame, effect);
}
public sealed class InitializeOriginalHandEntitiesHandler : OriginalHandEntityHandler
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.InitializeOriginalHandEntities; }
public sealed class OfferOriginalHandLossBenefitHandler : OriginalHandEntityHandler
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferOriginalHandLossBenefit; }
public sealed class InheritOriginalHandBonusesHandler : OriginalHandEntityHandler
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.InheritOriginalHandBonuses; }
public sealed class AwakenWhenOriginalHandEmptyHandler : OriginalHandEntityHandler
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.AwakenWhenOriginalHandEmpty; }
internal static class OriginalHandEntitiesComposition
{
    internal static bool IsOperation(SkillProgramEffectOp op) => op is SkillProgramEffectOp.InitializeOriginalHandEntities or
        SkillProgramEffectOp.OfferOriginalHandLossBenefit or SkillProgramEffectOp.InheritOriginalHandBonuses or SkillProgramEffectOp.AwakenWhenOriginalHandEmpty;
    internal static void ValidatePrograms(IReadOnlyDictionary<string, SkillProgram> programs)
    {
        foreach (var program in programs.Values)
        {
            if (program.Activations.Any(a => a.Effects.Any(e => IsOperation(e.Op))))
                throw new InvalidOperationException("Original-hand entity operations are trigger-only.");
            var bindings = program.Triggers.Where(t => t.Effects.Any(e => IsOperation(e.Op))).ToArray();
            foreach (var trigger in bindings)
            {
                if (trigger.Effects.Count != 1 || trigger.Effects[0] is not { Target: SkillProgramEffectTarget.Owner,
                        Condition.Kind: SkillProgramConditionKind.Always } e || trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
                    trigger.MarkerCost is not null || trigger.DynamicUsageLimit is not null || trigger.ChoiceGroup is not null || trigger.NamedUsageGroup is not null ||
                    trigger.SourceSkillId is not null || trigger.SourceViewAsId is not null)
                    throw new InvalidOperationException("Original-hand entity operations require their sole unconditional owner instruction.");
                var valid = e.Op switch {
                    SkillProgramEffectOp.InitializeOriginalHandEntities => trigger.Window == SkillProgramTriggerWindow.GameStarting && trigger.Subject == SkillProgramTriggerSubject.Owner &&
                        !trigger.Optional && trigger.UsageScope is null && trigger.UsageLimit is null,
                    SkillProgramEffectOp.OfferOriginalHandLossBenefit => trigger.Window == SkillProgramTriggerWindow.CardsMoved && trigger.Subject == SkillProgramTriggerSubject.Owner &&
                        !trigger.Optional && trigger.MovementOccurrence == SkillProgramMovementOccurrence.PerCard && trigger.SourceZones.SequenceEqual([CardZoneKind.Hand]) &&
                        trigger.UsageScope is null && trigger.UsageLimit is null && trigger.MovementReasons.Count == 0 &&
                        trigger.ExcludedMovementReasons.Count == 0 && trigger.ExcludedReasons.Count == 0 && trigger.DestinationZones.Count == 0 && !trigger.IgnoreOwnSkillMovements &&
                        !trigger.MovementDiscardOnly && trigger.CardKinds.Count == 0 && trigger.CardCategories.Count == 0 && trigger.Suits.Count == 0,
                    SkillProgramEffectOp.InheritOriginalHandBonuses => trigger.Window == SkillProgramTriggerWindow.OwnerDied && trigger.Subject == SkillProgramTriggerSubject.Owner &&
                        trigger.Optional && trigger.UsageScope is null && trigger.UsageLimit is null,
                    _ => trigger.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && !trigger.Optional &&
                        trigger.Subject is null or SkillProgramTriggerSubject.Owner && trigger.UsageScope == SkillUsageScope.Game && trigger.UsageLimit == 1 &&
                        trigger.TurnOwnerScope == SkillProgramTurnOwnerScope.Own };
                if (!valid) throw new InvalidOperationException($"Invalid original-hand entity trigger '{program.Id}/{trigger.Id}'.");
                if (e.Op == SkillProgramEffectOp.AwakenWhenOriginalHandEmpty && (!programs.TryGetValue(e.SourceBind!, out var source) ||
                    !source.Triggers.Any(t => t.Effects is [{ Op: SkillProgramEffectOp.InitializeOriginalHandEntities } init] && init.StateId == e.StateId)))
                    throw new InvalidOperationException("Original-hand awakening requires an existing source skill and initialized state.");
            }
            foreach (var group in bindings.Where(t => t.Effects[0].Op != SkillProgramEffectOp.AwakenWhenOriginalHandEmpty).GroupBy(t => t.Effects[0].StateId))
                if (group.Count() != 3 || group.Count(t => t.Effects[0].Op == SkillProgramEffectOp.InitializeOriginalHandEntities) != 1 ||
                    group.Count(t => t.Effects[0].Op == SkillProgramEffectOp.OfferOriginalHandLossBenefit) != 1 ||
                    group.Count(t => t.Effects[0].Op == SkillProgramEffectOp.InheritOriginalHandBonuses) != 1)
                    throw new InvalidOperationException("Each original-hand state requires exactly one initializer, loss benefit and inheritance binding.");
        }
    }
}
