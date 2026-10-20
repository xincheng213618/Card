using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record YunzhengInitialEntity(int OwnerSeat, int CardId, CardKind PrintedKind, long EntrySequence);
public sealed partial record ProgramLifecycleTriggerWindowFrame
{
    private readonly IReadOnlyList<YunzhengInitialEntity>? _yunzhengInitialEntities;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<YunzhengInitialEntity>? YunzhengInitialEntities
    { get => _yunzhengInitialEntities; init => _yunzhengInitialEntities = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public enum HuoxinStage { TargetChoice, CardChoice, ObtainChoice, ObtainChildren }
public sealed record HuoxinReceipt(CardConversionSource Source, string GameplayHash, long WindowId,
    long UseId, long ActionId, CardKind EffectiveKind, Suit EffectiveSuit)
{
    public HuoxinStage Stage { get; init; }
    public int TargetSeat { get; init; } = -1;
    private readonly IReadOnlyList<int> _candidates = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Candidates { get => _candidates; init => _candidates = Array.AsReadOnly(value.ToArray()); }
    public int? CardId { get; init; }
    public CardKind? PrintedKind { get; init; }
    public Suit ShownEffectiveSuit { get; init; }
    public bool WasMarked { get; init; }
    public bool MayObtain { get; init; }
    public long BatchId { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long PaidSequence { get; init; }
    public bool Paid { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HuoxinReceipt? Huoxin { get; init; } }
public sealed record YunzhengInitializedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int EntityCount) : IGameEvent;
public sealed record YunzhengInitialEntityMarkedEvent(long FrameId, int OriginalOwnerSeat, int CardId, CardKind PrintedKind, long EntrySequence) : IGameEvent;
public sealed record HuoxinStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long WindowId, long UseId, long ActionId, CardKind EffectiveKind, Suit EffectiveSuit) : IGameEvent;
public sealed record HuoxinEntityShownEvent(long FrameId, int TargetSeat, int CardId, CardKind PrintedKind,
    Suit EffectiveSuit, bool WasMarked, bool MayObtain) : IGameEvent;
public sealed record HuoxinEntityMarkedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int TargetSeat, int CardId, CardKind PrintedKind) : IGameEvent;
public sealed record HuoxinObtainPaidEvent(long FrameId, long BatchId, int CardId, CardKind PrintedKind,
    CardLocation From, CardLocation To, long MovementSequence, long Before, long After) : IGameEvent;
public sealed record HuoxinCompletedEvent(long FrameId, bool Marked, bool Obtained) : IGameEvent;

internal interface ILeZouShiHost
{
    SkillProgramStepOutcome InitializeYunzheng(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveHuoxin(ProgramSkillFrame frame);
}
internal abstract class LeZouShiDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(op == SkillProgramEffectOp.InitializeYunzhengInitialEntities ? SkillProgramTriggerWindow.GameStarting : SkillProgramTriggerWindow.CardUseCommitted)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override ISkillProgramEffectHandler Handler { get; } = new LeZouShiHandler(op);
}
internal sealed class InitializeYunzhengInitialEntitiesDescriptor() : LeZouShiDescriptor(SkillProgramEffectOp.InitializeYunzhengInitialEntities){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class ResolveHuoxinNonEquipmentUseDescriptor() : LeZouShiDescriptor(SkillProgramEffectOp.ResolveHuoxinNonEquipmentUse){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class LeZouShiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        op == SkillProgramEffectOp.InitializeYunzhengInitialEntities ? host.GetCapability<ILeZouShiHost>().InitializeYunzheng(frame) : host.GetCapability<ILeZouShiHost>().ResolveHuoxin(frame);
}
internal sealed class HuoxinCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => "le:huoxin";
    public void ValidateTrigger(SkillProgramTrigger t)
    {
        if (t.Id != "non-equipment-use" || t.Window != SkillProgramTriggerWindow.CardUseCommitted ||
            t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || !t.IncludeResponseUses ||
            !t.SingleActionInstance || !t.AllowNoEventTarget || !t.Optional || t.Subject is not null ||
            t.SourceSkillId is not null || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 ||
            t.Effects is not [{ Op: SkillProgramEffectOp.ResolveHuoxinNonEquipmentUse, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException("Huoxin requires its optional complete non-equipment true-use actor contract, including native response uses.");
    }
}
internal static class LeZouShiComposition
{
    [ProgramCatalogValidation(870)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var ops = p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects));
        if (p.Id is not ("le:yunzheng" or "le:huoxin") && !ops.Any(e => e.Op is SkillProgramEffectOp.InitializeYunzhengInitialEntities or SkillProgramEffectOp.ResolveHuoxinNonEquipmentUse)) return;
        if (p.Id is not ("le:yunzheng" or "le:huoxin") || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 ||
            p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.HpSkillGrants.Count != 0 || p.DamageModifiers.Count != 0 ||
            p.BooleanStates.Count != 0 || p.Contributions.Count != 0 || p.Triggers is not [var t] ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.UsageLimit is not null || t.UsageScope is not null ||
            t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null || t.ChoiceGroup is not null || t.OrderGroup is not null ||
            t.MarkerCost is not null || t.SourceViewAsId is not null || t.DamageCardKinds.Count != 0 || t.Suits.Count != 0 ||
            t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 || t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 ||
            t.ExcludedReasons.Count != 0 || t.MovementOccurrence is not null || t.DamageOccurrence is not null || t.GainPhaseQualification is not null ||
            t.EvaluateConditionAtResolution || t.NoDyingAtActivation || t.IgnoreOwnSkillMovements || t.DeferredTurnEndOnly ||
            t.RequireDamageSource is not null || t.RequireNoCardConversion is not null || t.OnlyDesignatedCardTargets || t.AllowOwnDiscardPhaseEnded)
            throw new InvalidOperationException(path + ": Le Zou Shi requires her complete native opening and true-use contracts without extra limits.");
        if (p.Id == "le:huoxin") { new HuoxinCommittedUseContract().ValidateTrigger(t); return; }
        if (t.Id != "mark-initial-entities" || t.Window != SkillProgramTriggerWindow.GameStarting || t.Subject != SkillProgramTriggerSubject.Owner ||
            t.Optional || t.OwnerRelation is not null || t.SourceSkillId is not null || t.IncludeResponseUses || t.SingleActionInstance ||
            t.AllowNoEventTarget || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 ||
            t.Effects is not [{ Op: SkillProgramEffectOp.InitializeYunzhengInitialEntities, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": Yunzheng requires its exact frozen opening-Hand initialization.");
    }
}
