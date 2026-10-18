using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record MouBingcaiExtraEffectGrant(long FrameId, CardConversionSource Source,
    string GameplayHash, long WindowId, CardKind BasicKind, long RecastInvocationId,
    int MaterialId, CardKind PrintedKind, CardKind EffectiveKind, CardLocation From,
    long BatchId, long Before, long After, long MovementSequence);

public sealed record MouBingcaiExtraEffectRounds(long ActionId, ExtraCardEffectState Native)
{
    private readonly ExtraCardEffectState _native = Freeze(Native);
    public ExtraCardEffectState Native { get => _native; init => _native = Freeze(value); }
    private readonly IReadOnlyList<MouBingcaiExtraEffectGrant> _grants = Array.AsReadOnly(Array.Empty<MouBingcaiExtraEffectGrant>());
    public IReadOnlyList<MouBingcaiExtraEffectGrant> Grants { get => _grants; init => _grants = Array.AsReadOnly(value.ToArray()); }
    public int Started { get; init; }
    public bool Active { get; init; }

    // The owning frame freezes every nested native continuation, not just the outer list.
    private static ExtraCardEffectState Freeze(ExtraCardEffectState state) => state with
    {
        Simple = state.Simple is { } simple ? simple with
        { RecoveryPolicySources = simple.RecoveryPolicySources is { } sources ? Array.AsReadOnly(sources.ToArray()) : null } : null,
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

public enum MouBingcaiLegacyAlcoholStage { Targets, Committed, Recovery }
public sealed record MouBingcaiLegacyAlcoholReturn(long UseId, long ProgramParentId, long DyingId,
    int InstructionIndex, CardConversionSource ProducerSource, string ProducerHash,
    long ActionId, MouBingcaiLegacyAlcoholStage Stage);

public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MouBingcaiExtraEffectRounds? MouBingcaiRounds { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MouBingcaiLegacyAlcoholReturn? MouBingcaiLegacyAlcohol { get; init; }
}

// All committed payloads are scalar. The native seeds and grant list stay on their owning Use.
public sealed record MouBingcaiExtraEffectIssuedEvent(long FrameId, long UseId, long ActionId,
    CardConversionSource Source, string GameplayHash, long WindowId, CardKind BasicKind,
    long RecastInvocationId, int MaterialId, CardKind PrintedKind, CardKind EffectiveKind,
    CardLocation From, long BatchId, long Before, long After, long MovementSequence) : IGameEvent;
public sealed record MouBingcaiExtraEffectRoundStartedEvent(long UseId, long ActionId, long FrameId, int Round) : IGameEvent;
public sealed record MouBingcaiExtraEffectRoundCompletedEvent(long UseId, long ActionId, long FrameId, int Round) : IGameEvent;
public sealed record MouBingcaiLegacyAlcoholIssuedEvent(long UseId, long ProgramParentId,
    long DyingId, int InstructionIndex, CardConversionSource ProducerSource,
    string ProducerHash, long ActionId) : IGameEvent;
public sealed record MouBingcaiLegacyAlcoholStageEvent(long UseId, long ActionId, MouBingcaiLegacyAlcoholStage Stage) : IGameEvent;
