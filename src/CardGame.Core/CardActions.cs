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
    Slash, BeforeTargetEffects, BeforeTrickTargetEffects, CommittedSlash, Dodge, DuelSlash, GroupResponse, HujiaDodge, JijiangDuelSlash, JijiangGroupResponse, DelayedCard, CompletedSlash
}

/// <summary>Replay-safe ordinary-trick state retained while a public before-target-effects window is suspended.</summary>
public sealed record ProgramTrickContinuation(
    int EffectCardId,
    LegalActionKind ActionKind,
    int? TargetCardId = null,
    CardKind? RequiredCardKind = null);

/// <summary>A paid physical card and its original location, retained by the trusted rules host.</summary>
public sealed record CardActionCost(int CardId, CardKind CardKind, CardLocation From);

/// <summary>
/// Accepted rules input. Keeping provenance does not grant permission to apply
/// another conversion. Player views must not expose this trusted-host context.
/// </summary>
public sealed class CardActionContext
{
    public CardActionContext(long actionId, long? parentActionId, CardActionType type,
        int actorSeat, int providerSeat, int? requesterSeat, int? responderSeat,
        int? opponentSeat, CardKind effectiveKind, IReadOnlyList<int> targetSeats,
        IReadOnlyList<CardActionCost> physicalCards, IReadOnlyList<CardConversionSource> conversionChain)
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
        PhysicalCards = Array.AsReadOnly(physicalCards.ToArray());
        ConversionChain = Array.AsReadOnly(conversionChain.ToArray());
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
    public IReadOnlyList<CardActionCost> PhysicalCards { get; }
    public IReadOnlyList<CardConversionSource> ConversionChain { get; }
}

/// <summary>Trusted-host audit event; not a player-facing notification.</summary>
public sealed record CardActionAcceptedEvent(CardActionContext Action) : IGameEvent;

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
    string SkillInstanceId = "", bool UsesSharedExecutor = false, int Priority = 0,
    ProgramSkillWindowContext? FrozenContext = null);

public sealed record ProgramCardTriggerWindowFrame(
    long Id, long ParentFrameId, CardActionContext Action,
    ProgramCardContinuation Continuation, IReadOnlyList<ProgramCardTriggerCandidate> Candidates,
    int CandidateIndex = 0, int InstructionIndex = 0, bool Activated = false,
    int? SelectedTargetSeat = null,
    ProgramTrickContinuation? TrickContinuation = null)
    : ResolutionFrame(Id, ResolutionFrameKind.ProgramCardTriggerWindow, ResolutionFrameStep.ResolvingEffect);

public sealed record ProgramCardTriggerResolvedEvent(
    long FrameId, string SkillId, string TriggerId, int OwnerSeat, int OpponentSeat, bool Activated) : IGameEvent;

public sealed record ProgramCardTargetSelectedEvent(
    long FrameId, long ActionId, string SkillId, string TriggerId,
    int OwnerSeat, int TargetSeat) : IGameEvent;
