using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum JiguStage { Drawing, DrawChildren }
public sealed record JiguInitialEntity(int OwnerSeat, int CardId, CardKind PrintedKind, long DealSequence);

public sealed partial record ProgramLifecycleTriggerWindowFrame
{
    private readonly IReadOnlyList<JiguInitialEntity>? _jiguInitialEntities;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<JiguInitialEntity>? JiguInitialEntities
    {
        get => _jiguInitialEntities;
        init => _jiguInitialEntities = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}

public sealed record JiguReceipt(CardConversionSource Source, string GameplayHash,
    long DamageFrameId, long DamageWindowId, int RoundNumber, int ActualTurn, int TurnOwner,
    int MarkedHandCount, int EquipmentCount, int EmptySlotCount, int StartedSeatCount,
    int UseNumber, bool DisabledForRound, JiguStage Stage)
{
    public bool Paid { get; init; }
    public NativeDrawOutcome? DrawOutcome { get; init; }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JiguReceipt? Jigu { get; init; }
}

public sealed record JiguInitializedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, int EntityCount) : IGameEvent;
public sealed record JiguInitialEntityEvent(long FrameId, int OwnerSeat, int CardId,
    CardKind PrintedKind, long DealSequence) : IGameEvent;
public sealed record JiguActivationPaidEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, long DamageFrameId, long DamageWindowId, int RoundNumber,
    int ActualTurn, int TurnOwner, int MarkedHandCount, int EquipmentCount, int EmptySlotCount,
    int StartedSeatCount, int UseNumber, bool DisabledForRound) : IGameEvent;
public sealed record JiguDrawReturnedEvent(long FrameId, int ActualCount, int ReplacementCount,
    long Before, long After, long? AttemptId) : IGameEvent;
public sealed record JiguCompletedEvent(long FrameId, long DamageFrameId) : IGameEvent;

internal interface ILeMiHengJiguHost
{
    SkillProgramStepOutcome Initialize(ProgramSkillFrame frame);
    SkillProgramStepOutcome Draw(ProgramSkillFrame frame);
}

internal abstract class JiguDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } =
        new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(op == SkillProgramEffectOp.InitializeJiguInitialEntities
            ? SkillProgramTriggerWindow.GameStarting : SkillProgramTriggerWindow.AfterDamageApplied)];
    public override ISkillProgramEffectHandler Handler { get; } = new JiguHandler(op);
}
internal sealed class InitializeJiguInitialEntitiesDescriptor()
    : JiguDescriptor(SkillProgramEffectOp.InitializeJiguInitialEntities)
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
}
internal sealed class ResolveJiguDamageEquipmentDrawDescriptor()
    : JiguDescriptor(SkillProgramEffectOp.ResolveJiguDamageEquipmentDraw)
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
}
internal sealed class JiguHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int target, ISkillProgramEffectHost host) => op == SkillProgramEffectOp.InitializeJiguInitialEntities
            ? host.GetCapability<ILeMiHengJiguHost>().Initialize(frame)
            : host.GetCapability<ILeMiHengJiguHost>().Draw(frame);
}

internal static class JiguComposition
{
    [ProgramCatalogValidation(840)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var operations = p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects));
        if (!operations.Any(e => e.Op is SkillProgramEffectOp.InitializeJiguInitialEntities or
            SkillProgramEffectOp.ResolveJiguDamageEquipmentDraw)) return;
        if (p.Id != "le:jigu" || p.Activations.Count != 0 || p.ViewAs.Count != 0 ||
            p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 ||
            p.HpSkillGrants.Count != 0 || p.DamageModifiers.Count != 0 || p.BooleanStates.Count != 0 ||
            p.Contributions.Count != 0 || p.Triggers.Count != 2 ||
            p.Triggers.SingleOrDefault(t => t.Id == "mark-initial-entities") is not { } initial ||
            initial.Window != SkillProgramTriggerWindow.GameStarting || initial.Subject != SkillProgramTriggerSubject.Owner ||
            initial.DamageOccurrence is not null ||
            initial.Effects is not [{ Op: SkillProgramEffectOp.InitializeJiguInitialEntities,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            p.Triggers.SingleOrDefault(t => t.Id == "damage-equipment-draw") is not { } damage ||
            damage.Window != SkillProgramTriggerWindow.AfterDamageApplied || damage.Subject != SkillProgramTriggerSubject.Any ||
            damage.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage ||
            damage.Effects is not [{ Op: SkillProgramEffectOp.ResolveJiguDamageEquipmentDraw,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            !Plain(initial) || !Plain(damage))
            throw new InvalidOperationException(path + ": Jigu requires its permanent initial entities and one whole source-or-target damage occurrence.");
    }

    private static bool Plain(SkillProgramTrigger t) => !t.Optional &&
        t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageLimit is null &&
        t.UsageScope is null && t.DynamicUsageLimit is null && t.NamedUsageGroup is null &&
        t.MarkerCost is null && t.ChoiceGroup is null && t.OrderGroup is null && !t.EvaluateConditionAtResolution &&
        t.OwnerRelation is null && t.SourceSkillId is null && t.SourceViewAsId is null &&
        t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.DamageCardKinds.Count == 0 &&
        t.Suits.Count == 0 && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 &&
        t.MovementReasons.Count == 0 && t.ExcludedMovementReasons.Count == 0 && t.ExcludedReasons.Count == 0 &&
        t.MovementOccurrence is null && !t.MovementDiscardOnly && !t.IncludeResponseUses && !t.SingleActionInstance &&
        !t.DeferredTurnEndOnly && !t.IgnoreOwnSkillMovements && t.GainPhaseQualification is null &&
        !t.NoDyingAtActivation && t.RequireDamageSource is null && t.RequireNoCardConversion is null &&
        !t.AllowNoEventTarget && !t.OnlyDesignatedCardTargets && !t.AllowOwnDiscardPhaseEnded;
}
