using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record GuYinInitialEntity(int OriginalOwnerSeat, int CardId, CardKind PrintedKind, long EntrySequence);
public sealed record GuYinSourceSnapshot(CardConversionSource Source, string GameplayHash);
public sealed record GuYinDiscardEntity(int OriginalOwnerSeat, int CardId, CardKind PrintedKind, long MovementSequence);
public enum GuYinStage { Drawing, DrawChildren }
public sealed record GuYinReceipt(CardConversionSource Source, string GameplayHash, long BatchId,
    int ActualTurn, int TurnOwner)
{
    private readonly IReadOnlyList<GuYinDiscardEntity> _entities = Array.AsReadOnly(Array.Empty<GuYinDiscardEntity>());
    public IReadOnlyList<GuYinDiscardEntity> Entities
    { get => _entities; init => _entities = Array.AsReadOnly(value.ToArray()); }
    public int Index { get; init; }
    public GuYinStage Stage { get; init; }
    public NativeDrawOutcome? DrawOutcome { get; init; }
}
public sealed partial record ProgramLifecycleTriggerWindowFrame
{
    private readonly IReadOnlyList<GuYinInitialEntity>? _guYinInitialEntities;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<GuYinInitialEntity>? GuYinInitialEntities
    { get => _guYinInitialEntities; init => _guYinInitialEntities = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public GuYinReceipt? GuYin { get; init; }
}
public sealed record GuYinInitializedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int EntityCount) : IGameEvent;
public sealed record GuYinOpeningCapturedEvent(long SnapshotId, int EntityCount) : IGameEvent;
public sealed record GuYinOpeningEntityEvent(long SnapshotId, int OriginalOwnerSeat, int CardId, CardKind PrintedKind, long EntrySequence) : IGameEvent;
public sealed record GuYinInitialEntityRegisteredEvent(long FrameId, int OwnerSeat, int OriginalOwnerSeat,
    int CardId, CardKind PrintedKind, long EntrySequence) : IGameEvent;
public sealed record GuYinDiscardQualifiedEvent(long BatchId, CardConversionSource Source, string GameplayHash,
    int OriginalOwnerSeat, int CardId, CardKind PrintedKind, long MovementSequence, int ActualTurn, int TurnOwner) : IGameEvent;
public sealed record GuYinDrawStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, long BatchId, int EntityCount) : IGameEvent;
public sealed record GuYinDrawRequestedEvent(long FrameId, int Index, int CardId, long MovementSequence) : IGameEvent;
public sealed record GuYinDrawReturnedEvent(long FrameId, int Index, int CardId, int ActualCount,
    int ReplacementCount, long Before, long After, long? AttemptFrameId) : IGameEvent;
public sealed record GuYinCompletedEvent(long FrameId, long BatchId, int DrawCount) : IGameEvent;

internal interface ILeZhouYuGuYinHost
{
    SkillProgramStepOutcome Initialize(ProgramSkillFrame frame);
    SkillProgramStepOutcome Draw(ProgramSkillFrame frame);
}
internal abstract class GuYinDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, c) => c.PublicControlValue(1));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(op == SkillProgramEffectOp.InitializeGuYinInitialEntities
            ? SkillProgramTriggerWindow.GameStarting : SkillProgramTriggerWindow.CardsMoved)];
    public override ISkillProgramEffectHandler Handler { get; } = new GuYinHandler(op);
}
internal sealed class InitializeGuYinInitialEntitiesDescriptor() : GuYinDescriptor(SkillProgramEffectOp.InitializeGuYinInitialEntities);
internal sealed class ResolveGuYinInitialEntityDiscardDrawDescriptor() : GuYinDescriptor(SkillProgramEffectOp.ResolveGuYinInitialEntityDiscardDraw);
internal sealed class GuYinHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target,
        ISkillProgramEffectHost host) => op == SkillProgramEffectOp.InitializeGuYinInitialEntities
        ? host.GetCapability<ILeZhouYuGuYinHost>().Initialize(frame) : host.GetCapability<ILeZhouYuGuYinHost>().Draw(frame);
}
internal static class GuYinComposition
{
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (p.Id != "le:guyin" && !p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects))
            .Any(e => e.Op is SkillProgramEffectOp.InitializeGuYinInitialEntities or SkillProgramEffectOp.ResolveGuYinInitialEntityDiscardDraw)) return;
        if (p.Id != "le:guyin" || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 ||
            p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.HpSkillGrants.Count != 0 || p.DamageModifiers.Count != 0 ||
            p.BooleanStates.Count != 0 || p.Contributions.Count != 0 || p.Triggers.Count != 2 ||
            p.Triggers.SingleOrDefault(t => t.Id == "mark-initial-entities") is not { } initial ||
            p.Triggers.SingleOrDefault(t => t.Id == "initial-entity-discard-draw") is not { } discard ||
            !Exact(initial, true) || !Exact(discard, false))
            throw new InvalidOperationException(path + ": GuYin requires its exact native opening entities and per-batch original-entity discard observer.");
    }
    private static bool Exact(SkillProgramTrigger t, bool initial) =>
        t.Window == (initial ? SkillProgramTriggerWindow.GameStarting : SkillProgramTriggerWindow.CardsMoved) &&
        t.Subject == (initial ? SkillProgramTriggerSubject.Owner : SkillProgramTriggerSubject.Any) && !t.Optional &&
        t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageLimit is null && t.UsageScope is null &&
        t.DynamicUsageLimit is null && t.NamedUsageGroup is null && t.MarkerCost is null && t.ChoiceGroup is null &&
        t.OrderGroup is null && !t.EvaluateConditionAtResolution && t.OwnerRelation is null && t.SourceSkillId is null &&
        t.SourceViewAsId is null && t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.DamageCardKinds.Count == 0 &&
        t.Suits.Count == 0 && t.SourceZones.Count == 0 &&
        (initial ? t.DestinationZones.Count == 0 : t.DestinationZones.SequenceEqual([CardZoneKind.DiscardPile])) &&
        t.MovementReasons.Count == 0 && t.ExcludedMovementReasons.Count == 0 && t.ExcludedReasons.Count == 0 &&
        t.MovementOccurrence == (initial ? null : SkillProgramMovementOccurrence.PerBatch) &&
        !t.MovementDiscardOnly && !t.IncludeResponseUses && !t.SingleActionInstance && !t.DeferredTurnEndOnly &&
        !t.IgnoreOwnSkillMovements && t.GainPhaseQualification is null && t.DamageOccurrence is null &&
        !t.NoDyingAtActivation && t.RequireDamageSource is null && t.RequireNoCardConversion is null &&
        !t.AllowNoEventTarget && !t.OnlyDesignatedCardTargets && !t.AllowOwnDiscardPhaseEnded &&
        t.Effects is [var e] && e.Op == (initial ? SkillProgramEffectOp.InitializeGuYinInitialEntities : SkillProgramEffectOp.ResolveGuYinInitialEntityDiscardDraw) &&
        e.Target == SkillProgramEffectTarget.Owner && e.Condition.Kind == SkillProgramConditionKind.Always;
}
