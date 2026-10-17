using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record QiqinInitialEntity(int OwnerSeat, int CardId, CardKind PrintedKind, long DealSequence);
public sealed record QiqinCollectedEntity(int CardId, CardKind PrintedKind);
public enum QiqinStage { ObtainChildren }

public sealed record QiqinReceipt(CardConversionSource Source, string GameplayHash, long WindowId,
    int InstructionIndex, int ActualTurn, int TurnOwner, long SequenceBefore)
{
    public QiqinStage Stage { get; init; }
    public long BatchId { get; init; }
    public long SequenceAfter { get; init; }
    private readonly IReadOnlyList<QiqinCollectedEntity> _entities = Array.AsReadOnly(Array.Empty<QiqinCollectedEntity>());
    public IReadOnlyList<QiqinCollectedEntity> Entities
    { get => _entities; init => _entities = Array.AsReadOnly(value.ToArray()); }
}

public sealed partial record ProgramLifecycleTriggerWindowFrame
{
    private readonly IReadOnlyList<QiqinInitialEntity>? _qiqinInitialEntities;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<QiqinInitialEntity>? QiqinInitialEntities
    { get => _qiqinInitialEntities; init => _qiqinInitialEntities = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public QiqinReceipt? Qiqin { get; init; }
}

public sealed record QiqinInitializedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int EntityCount) : IGameEvent;
public sealed record QiqinInitialEntityMarkedEvent(long FrameId, int OriginalOwnerSeat, int CardId, CardKind PrintedKind, long EntrySequence) : IGameEvent;
public sealed record QiqinCollectStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long WindowId, int ActualTurn, int TurnOwner, int EntityCount, long SequenceBefore) : IGameEvent;
public sealed record QiqinCollectEntityIssuedEvent(long FrameId, int Index, int CardId, CardKind PrintedKind) : IGameEvent;
public sealed record QiqinCollectPaidEvent(long FrameId, long BatchId, int EntityCount, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record QiqinCollectedEntityEvent(long FrameId, int Index, int CardId, CardKind PrintedKind, long MovementSequence) : IGameEvent;
public sealed record QiqinCollectCompletedEvent(long FrameId, long WindowId, int EntityCount) : IGameEvent;

internal interface ILeXiaoQiaoQiqinHost
{
    SkillProgramStepOutcome Initialize(ProgramSkillFrame frame);
    SkillProgramStepOutcome Collect(ProgramSkillFrame frame);
}
internal abstract class QiqinDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(op == SkillProgramEffectOp.InitializeQiqinInitialEntities
            ? SkillProgramTriggerWindow.GameStarting : SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
    public override ISkillProgramEffectHandler Handler { get; } = new QiqinHandler(op);
}
internal sealed class InitializeQiqinInitialEntitiesDescriptor() : QiqinDescriptor(SkillProgramEffectOp.InitializeQiqinInitialEntities);
internal sealed class ResolveQiqinDiscardPileQinDescriptor() : QiqinDescriptor(SkillProgramEffectOp.ResolveQiqinDiscardPileQin);
internal sealed class QiqinHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        op == SkillProgramEffectOp.InitializeQiqinInitialEntities
            ? host.GetCapability<ILeXiaoQiaoQiqinHost>().Initialize(frame)
            : host.GetCapability<ILeXiaoQiaoQiqinHost>().Collect(frame);
}
internal static class QiqinComposition
{
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (p.Id != "le:qiqin" && !p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects))
                .Any(e => e.Op is SkillProgramEffectOp.InitializeQiqinInitialEntities or SkillProgramEffectOp.ResolveQiqinDiscardPileQin)) return;
        if (p.Id != "le:qiqin" || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 ||
            p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.HpSkillGrants.Count != 0 || p.DamageModifiers.Count != 0 ||
            p.BooleanStates.Count != 0 || p.Contributions.Count != 0 || p.Triggers.Count != 2 ||
            p.Triggers.SingleOrDefault(t => t.Id == "mark-initial-entities") is not { } initial ||
            p.Triggers.SingleOrDefault(t => t.Id == "collect-discard-qin") is not { } prep ||
            !Exact(initial, SkillProgramTriggerWindow.GameStarting, SkillProgramEffectOp.InitializeQiqinInitialEntities) ||
            !Exact(prep, SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, SkillProgramEffectOp.ResolveQiqinDiscardPileQin))
            throw new InvalidOperationException(path + ": Qiqin requires its exact permanent initial entities and mandatory preparation discard-pile obtain.");
    }
    private static bool Exact(SkillProgramTrigger t, SkillProgramTriggerWindow window, SkillProgramEffectOp op) =>
        t.Window == window && t.Subject == SkillProgramTriggerSubject.Owner && !t.Optional &&
        t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageLimit is null && t.UsageScope is null &&
        t.DynamicUsageLimit is null && t.NamedUsageGroup is null && t.MarkerCost is null && t.ChoiceGroup is null &&
        t.OrderGroup is null && !t.EvaluateConditionAtResolution && t.OwnerRelation is null && t.SourceSkillId is null &&
        t.SourceViewAsId is null && t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.DamageCardKinds.Count == 0 &&
        t.Suits.Count == 0 && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 && t.MovementReasons.Count == 0 &&
        t.ExcludedMovementReasons.Count == 0 && t.ExcludedReasons.Count == 0 && t.MovementOccurrence is null &&
        !t.MovementDiscardOnly && !t.IncludeResponseUses && !t.SingleActionInstance && !t.DeferredTurnEndOnly &&
        !t.IgnoreOwnSkillMovements && t.GainPhaseQualification is null && t.DamageOccurrence is null &&
        !t.NoDyingAtActivation && t.RequireDamageSource is null && t.RequireNoCardConversion is null &&
        !t.AllowNoEventTarget && !t.OnlyDesignatedCardTargets && !t.AllowOwnDiscardPhaseEnded &&
        t.Effects is [var e] && e.Op == op && e.Target == SkillProgramEffectTarget.Owner && e.Condition.Kind == SkillProgramConditionKind.Always;
}
