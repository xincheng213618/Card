using System.Text.Json.Serialization;

namespace CardGame.Core;

/// <summary>The exact, owner-scoped conversion selected from a legal action.</summary>
public sealed record CardConversionSource(
    string SkillId,
    string BindingId,
    int OwnerSeat,
    string SkillInstanceId);

public enum CardActionType { Use, Response }

public enum ProgramCardContinuation
{
    Slash, BeforeTargetEffects, BeforeTrickTargetEffects, CommittedSlash, Dodge, DuelSlash, GroupResponse, FactionDefenseDodge, FactionSlashDuelSlash, FactionSlashGroupResponse, DelayedCard, CompletedSlash, NullificationResponse, SlashTargetRedirecting, SlashBeforeResponse, SlashFullyDodged,
    CommittedTrick, CommittedSimpleCard, CompletedCard, FinalizedTrick = 824, FinalizedSimpleCard = 900
}

public enum ProgramCompletedResponseKind { Dodge, Nullification }

/// <summary>The completed use of an accepted response returns to its original parent.</summary>
public sealed record ProgramCompletedResponseReturn(ProgramCompletedResponseKind Kind, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool IsCommitted = false);

public enum SimpleCardUseEffect { Equipment, Alcohol, Recovery, EquipmentPlacement = 900 }

public sealed record ProgramRecoveryPolicySource(string SkillId, string PolicyId);

/// <summary>Typed continuation for basic and equipment uses; never a skill-specific callback.</summary>
public sealed record ProgramSimpleCardContinuation(
    int CardId, SimpleCardUseEffect Effect, int RecoveryAmount = 1,
    IReadOnlyList<ProgramRecoveryPolicySource>? RecoveryPolicySources = null);

/// <summary>Replay-safe ordinary-trick state retained while a public before-target-effects window is suspended.</summary>
public sealed record ProgramTrickContinuation(
    int EffectCardId,
    LegalActionKind ActionKind,
    int? TargetCardId = null,
    CardKind? RequiredCardKind = null);

/// <summary>A paid physical card and its original location, retained by the trusted rules host.</summary>
public sealed record CardActionCost(int CardId, CardKind CardKind, CardLocation From,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? EffectiveIsRed = null);

/// <summary>
/// Accepted rules input. Keeping provenance does not grant permission to apply
/// another conversion. Player views must not expose this trusted-host context.
/// </summary>
public sealed class CardActionContext
{
    public CardActionContext(long actionId, long? parentActionId, CardActionType type,
        int actorSeat, int providerSeat, int? requesterSeat, int? responderSeat,
        int? opponentSeat, CardKind effectiveKind, IReadOnlyList<int> targetSeats,
        IReadOnlyList<CardActionCost> physicalCards, IReadOnlyList<CardConversionSource> conversionChain,
        IReadOnlyList<int>? designatedTargetSeats = null,
        Suit? effectiveSuit = null, int? effectiveRank = null, bool? effectiveIsRed = null, CardActionFactionOrigin? factionOrigin = null)
    {
        ActionId = actionId;
        ParentActionId = parentActionId;
        Type = type;
        ActorSeat = actorSeat;
        ProviderSeat = providerSeat;
        RequesterSeat = requesterSeat;
        ResponderSeat = responderSeat;
        OpponentSeat = opponentSeat;
        EffectiveKind = effectiveKind;
        TargetSeats = Array.AsReadOnly(targetSeats.ToArray());
        DesignatedTargetSeats = designatedTargetSeats is null ? null : Array.AsReadOnly(designatedTargetSeats.ToArray());
        PhysicalCards = Array.AsReadOnly(physicalCards.ToArray());
        ConversionChain = Array.AsReadOnly(conversionChain.ToArray());
        EffectiveSuit = effectiveSuit;
        EffectiveRank = effectiveRank;
        EffectiveIsRed = effectiveIsRed;
        FactionOrigin = factionOrigin;
    }

    public long ActionId { get; }
    public long? ParentActionId { get; }
    public CardActionType Type { get; }
    public int ActorSeat { get; }
    public int ProviderSeat { get; }
    public int? RequesterSeat { get; }
    public int? ResponderSeat { get; }
    public int? OpponentSeat { get; }
    public CardKind EffectiveKind { get; }
    public IReadOnlyList<int> TargetSeats { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<int>? DesignatedTargetSeats { get; }
    [JsonIgnore]
    public IReadOnlyList<int> EffectiveDesignatedTargetSeats => DesignatedTargetSeats ?? TargetSeats;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardActionFactionOrigin? FactionOrigin { get; }
    public IReadOnlyList<CardActionCost> PhysicalCards { get; }
    public IReadOnlyList<CardConversionSource> ConversionChain { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Suit? EffectiveSuit { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? EffectiveRank { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? EffectiveIsRed {get;}
}

/// <summary>Trusted-host audit event; not a player-facing notification.</summary>
public sealed record CardActionAcceptedEvent(CardActionContext Action) : IGameEvent;

/// <summary>Opt-in frozen appearance at use commitment, including basic and equipment cards.</summary>
public sealed record CardUseAppearanceCapturedEvent(CardActionContext Action) : IGameEvent;

public sealed record ProgramViewAsConvertedEvent(
    long FrameId,
    string SkillId,
    string BindingId,
    int OwnerSeat,
    IReadOnlyList<int> PhysicalCardIds,
    CardKind OutputKind,
    bool IsUse,
    IReadOnlyList<int> TargetSeats) : IGameEvent
{
    public int OpponentSeat => TargetSeats.FirstOrDefault(-1);
}

public sealed record ProgramCardTriggerCandidate(
    int OwnerSeat, int OpponentSeat, string SkillId, string TriggerId, string GameplayHash,
    string SkillInstanceId = "", int Priority = 0,
    ProgramSkillWindowContext? FrozenContext = null);

public sealed partial record ProgramCardTriggerWindowFrame(
    long Id, long ParentFrameId, CardActionContext Action,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ProgramCardContinuation? Continuation,
    IReadOnlyList<ProgramCardTriggerCandidate> Candidates,
    int CandidateIndex = 0, bool Activated = false,
    ProgramTrickContinuation? TrickContinuation = null,
    ProgramSimpleCardContinuation? SimpleContinuation = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ProgramCompletedResponseReturn? CompletedResponseReturn = null, long? AttackOwnerFrameId = null)
    : ResolutionFrame(Id, ResolutionFrameKind.ProgramCardTriggerWindow, ResolutionFrameStep.ResolvingEffect);

public sealed record ProgramCardTriggerResolvedEvent(
    long FrameId, string SkillId, string TriggerId, int OwnerSeat, int OpponentSeat, bool Activated) : IGameEvent;
