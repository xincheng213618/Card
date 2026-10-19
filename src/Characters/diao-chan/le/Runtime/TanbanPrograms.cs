using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record TanbanInitialEntity(int OwnerSeat, int CardId, CardKind PrintedKind, long EntrySequence);

public sealed partial record ProgramLifecycleTriggerWindowFrame
{
    private readonly IReadOnlyList<TanbanInitialEntity>? _tanbanInitialEntities;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TanbanInitialEntity>? TanbanInitialEntities
    {
        get => _tanbanInitialEntities;
        init => _tanbanInitialEntities = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}

// Entity changes become visible together only when the corresponding scalar
// initialization/swap commit is published. Observers cannot see half a swap.
public sealed record TanbanEntityMarkChangedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, int HolderSeat, int CardId, CardKind PrintedKind, long EntrySequence,
    bool Before, bool Marked) : IGameEvent;
public sealed record TanbanInitializedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, long WindowId, int EntityCount) : IGameEvent;
public sealed record TanbanHandMarksSwappedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, long WindowId, int TurnNumber, int HolderSeat, int EntityCount) : IGameEvent;

internal interface ILeDiaoChanTanbanHost
{
    SkillProgramStepOutcome InitializeTanban(ProgramSkillFrame frame);
    SkillProgramStepOutcome SwapTanbanHandMarks(ProgramSkillFrame frame);
}

internal abstract class TanbanDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(op == SkillProgramEffectOp.InitializeTanbanInitialEntities
            ? SkillProgramTriggerWindow.GameStarting : SkillProgramTriggerWindow.DrawPhaseEnded)];
    public override ProgramOperationAiPolicy AiPolicy { get; } =
        new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override ISkillProgramEffectHandler Handler { get; } = new TanbanHandler(op);
}
internal sealed class InitializeTanbanInitialEntitiesDescriptor() : TanbanDescriptor(SkillProgramEffectOp.InitializeTanbanInitialEntities);
internal sealed class SwapTanbanHandMarksDescriptor() : TanbanDescriptor(SkillProgramEffectOp.SwapTanbanHandMarks);
internal sealed class TanbanHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int target, ISkillProgramEffectHost host) => op == SkillProgramEffectOp.InitializeTanbanInitialEntities
            ? host.GetCapability<ILeDiaoChanTanbanHost>().InitializeTanban(frame)
            : host.GetCapability<ILeDiaoChanTanbanHost>().SwapTanbanHandMarks(frame);
}

internal static class TanbanComposition
{
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var effects = p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects));
        if (p.Id != "le:tanban" && !effects.Any(e => e.Op is
            SkillProgramEffectOp.InitializeTanbanInitialEntities or SkillProgramEffectOp.SwapTanbanHandMarks)) return;
        if (p.Id != "le:tanban" || p.Activations.Count != 0 || p.ViewAs.Count != 0 ||
            p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 ||
            p.HpSkillGrants.Count != 0 || p.EquipmentSuitSkillGrants.Count != 0 ||
            p.DamageModifiers.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0 ||
            p.LordSkillProjection || p.CannotChallengeDeclarations || p.Triggers.Count != 2 ||
            p.Triggers.SingleOrDefault(t => t.Id == "mark-initial-entities") is not { } initial ||
            p.Triggers.SingleOrDefault(t => t.Id == "swap-draw-end-hand-marks") is not { } swap ||
            initial.Window != SkillProgramTriggerWindow.GameStarting || initial.Optional ||
            initial.Effects is not [{ Op: SkillProgramEffectOp.InitializeTanbanInitialEntities,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            swap.Window != SkillProgramTriggerWindow.DrawPhaseEnded || !swap.Optional ||
            swap.Effects is not [{ Op: SkillProgramEffectOp.SwapTanbanHandMarks,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            !PlainTrigger(initial) || !PlainTrigger(swap))
            throw new InvalidOperationException(path + ": Tanban requires exact frozen opening entities and one optional own draw-ended whole-Hand inversion.");
    }

    private static bool PlainTrigger(SkillProgramTrigger t) =>
        t.Subject == SkillProgramTriggerSubject.Owner && t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own &&
        t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.Priority == 0 &&
        t.UsageLimit is null && t.UsageScope is null && t.DynamicUsageLimit is null && t.NamedUsageGroup is null &&
        t.MarkerCost is null && t.ChoiceGroup is null && t.OrderGroup is null && !t.EvaluateConditionAtResolution &&
        t.OwnerRelation is null && t.SourceSkillId is null && t.SourceViewAsId is null &&
        t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.DamageCardKinds.Count == 0 &&
        t.Suits.Count == 0 && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 &&
        t.MovementReasons.Count == 0 && t.ExcludedMovementReasons.Count == 0 && t.ExcludedReasons.Count == 0 &&
        t.JudgmentReasons.Count == 0 && t.JudgmentSource is null && t.MinimumRank == 1 && t.MaximumRank == 13 &&
        t.DrawPhaseMode == SkillProgramDrawPhaseMode.Additive && t.MovementOccurrence is null &&
        !t.MovementDiscardOnly && !t.IncludeResponseUses && !t.SingleActionInstance && !t.DeferredTurnEndOnly &&
        !t.IgnoreOwnSkillMovements && t.GainPhaseQualification is null && t.DamageOccurrence is null &&
        !t.NoDyingAtActivation && t.RequireDamageSource is null && t.RequireNoCardConversion is null &&
        !t.AllowNoEventTarget && !t.OnlyDesignatedCardTargets && !t.AllowOwnDiscardPhaseEnded;
}
