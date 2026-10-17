using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum ZixiEffectsStage { Drawing, DrawChildren, ObtainChildren, DamageChildren }
public sealed record ZixiEffectsEntity(int CardId, CardKind PrintedKind, CardLocation From);
public sealed record ZixiEffectsReceipt(CardConversionSource Source, string GameplayHash,
    long UseId, long ActionId, long WindowId, int TargetSeat, int JudgmentCount,
    CardKind CardKind, long SequenceBefore)
{
    public ZixiEffectsStage Stage { get; init; }
    private IReadOnlyList<ZixiEffectsEntity> _entities = Array.AsReadOnly(Array.Empty<ZixiEffectsEntity>());
    public IReadOnlyList<ZixiEffectsEntity> Entities { get => _entities; init => _entities = Array.AsReadOnly(value.ToArray()); }
    public long BatchId { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int ActualCount { get; init; } = -1;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativeDrawOutcome? DrawOutcome { get; init; }
}
public sealed record ZixiExtraEffectGrant(long FrameId, CardConversionSource Source, string GameplayHash,
    long WindowId, int TargetSeat, int JudgmentCount)
{ public int CurrentTargetSeat { get; init; } = TargetSeat; }
public sealed record ZixiExtraEffectRounds(long ActionId, ExtraCardEffectState Native)
{
    private ExtraCardEffectState _native = FreezeNative(Native);
    public ExtraCardEffectState Native { get => _native; init => _native = FreezeNative(value); }
    private IReadOnlyList<ZixiExtraEffectGrant> _grants = [];
    public IReadOnlyList<ZixiExtraEffectGrant> Grants { get => _grants; init => _grants = Array.AsReadOnly(value.ToArray()); }
    public int Started { get; init; }
    public bool Active { get; init; }
    public int ActiveTargetIndex { get; init; } = -1;
    public int ActiveTargetSeat { get; init; } = -1;
    private static ExtraCardEffectState FreezeNative(ExtraCardEffectState state) => state with
    {
        Simple = state.Simple is { } simple ? simple with { RecoveryPolicySources = simple.RecoveryPolicySources is { } policies
            ? Array.AsReadOnly(policies.ToArray()) : null } : null,
        Fangtian = state.Fangtian is { } fang ? fang with { TargetSeats = Array.AsReadOnly(fang.TargetSeats.ToArray()) } : null,
        OriginalAttacks = state.OriginalAttacks.Select(seed => seed with { Attack = seed.Attack with
        {
            PhysicalCardIds = Array.AsReadOnly(seed.Attack.PhysicalCardIds.ToArray()),
            ChainedTargetSeats = Array.AsReadOnly(seed.Attack.ChainedTargetSeats.ToArray()),
            ResponseProhibitingSkillNames = Array.AsReadOnly(seed.Attack.ResponseProhibitingSkillNames.ToArray()),
            RangePreventionVisitedTargets = seed.Attack.RangePreventionVisitedTargets is { } visited ? Array.AsReadOnly(visited.ToArray()) : null
        } }).ToArray(),
        MaterialClaims = state.MaterialClaims.ToArray()
    };
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ZixiEffectsReceipt? ZixiEffects { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ZixiExtraEffectRounds? ZixiEffectRounds { get; init; } }
public sealed record ZixiEffectsStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long UseId, long ActionId, long WindowId, int TargetSeat, int JudgmentCount, CardKind CardKind, long SequenceBefore) : IGameEvent;
public sealed record ZixiExtraEffectIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long UseId, long ActionId, int TargetSeat, int JudgmentCount) : IGameEvent;
public sealed record ZixiExtraEffectRoundStartedEvent(long UseId, long ActionId, long FrameId, int Round, int TargetSeat) : IGameEvent;
public sealed record ZixiExtraEffectRoundCompletedEvent(long UseId, long ActionId, long FrameId, int Round, int TargetSeat) : IGameEvent;
public sealed record ZixiExtraEffectRedirectedEvent(long UseId, long ActionId, long FrameId, long RedirectFrameId,
    CardConversionSource Source, string GameplayHash, int TargetIndex, int PreviousSeat, int TargetSeat) : IGameEvent;
public sealed record ZixiEffectsDrawReturnedEvent(long FrameId, int RecipientSeat, int RequestedCount,
    int ActualCount, int ReplacementCount, long Before, long After, long? AttemptFrameId) : IGameEvent;
public sealed record ZixiEffectsEntityObtainedEvent(long FrameId, int Index, int CardId, CardKind PrintedKind,
    CardLocation From, CardLocation To, long Sequence, long BatchId) : IGameEvent;
public sealed record ZixiEffectsObtainPaidEvent(long FrameId, long BatchId, int SelectedCount, int ActualCount, long Before, long After) : IGameEvent;
public sealed record ZixiEffectsDamageIssuedEvent(long FrameId, int SourceSeat, int TargetSeat, int Amount) : IGameEvent;
public sealed record ZixiEffectsCompletedEvent(long FrameId, long UseId, int JudgmentCount) : IGameEvent;

internal interface IZixiEffectsHost
{ SkillProgramStepOutcome ResolveZixiUniqueTargetJudgmentEffects(ProgramSkillFrame frame); }
internal sealed class ResolveZixiUniqueTargetJudgmentEffectsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveZixiUniqueTargetJudgmentEffects;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(4));
    public override ISkillProgramEffectHandler Handler { get; } = new ZixiEffectsHandler();
}
internal sealed class ZixiEffectsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveZixiUniqueTargetJudgmentEffects;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IZixiEffectsHost>().ResolveZixiUniqueTargetJudgmentEffects(frame);
}
internal static class ZixiEffectsComposition
{
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var effects = p.Triggers.Where(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveZixiUniqueTargetJudgmentEffects)).ToArray();
        if (p.Id != "le:zixi" && effects.Length == 0) return;
        if (p.Id != "le:zixi" || p.Triggers.Count != 3 || effects is not [var t] ||
            !p.Triggers.Any(t => t.Id == "start-place-qin") || !p.Triggers.Any(t => t.Id == "end-place-qin") ||
            p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 ||
            p.CardIdentities.Count != 0 || p.HpSkillGrants.Count != 0 || p.DamageModifiers.Count != 0 ||
            p.BooleanStates.Count != 0 || p.Contributions.Count != 0 || t.Id != "unique-target-judgment-effects" ||
            t.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor ||
            !t.Optional || !t.SingleActionInstance || t.Priority != 0 ||
            !t.CardCategories.SequenceEqual(new[] { SkillProgramCardCategory.Basic, SkillProgramCardCategory.InstantTrick }) ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.UsageLimit is not null || t.UsageScope is not null ||
            t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null || t.MarkerCost is not null ||
            t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 || t.CardKinds.Count != 0 || t.DamageCardKinds.Count != 0 ||
            t.Suits.Count != 0 || t.SourceSkillId is not null || t.SourceViewAsId is not null ||
            t.ChoiceGroup is not null || t.OrderGroup is not null || t.MovementOccurrence is not null || t.DamageOccurrence is not null ||
            t.EvaluateConditionAtResolution || t.GainPhaseQualification is not null || t.AllowNoEventTarget || t.OnlyDesignatedCardTargets ||
            t.IncludeResponseUses || t.DeferredTurnEndOnly || t.NoDyingAtActivation || t.IgnoreOwnSkillMovements ||
            t.RequireDamageSource is not null || t.RequireNoCardConversion is not null ||
            t.Effects is not [{ Op: SkillProgramEffectOp.ResolveZixiUniqueTargetJudgmentEffects,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": Zixi requires its complete two placement windows and optional accepted single-target Basic/ordinary-trick judgment-count effect.");
    }
}
