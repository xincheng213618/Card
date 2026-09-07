namespace CardGame.Core;

public readonly record struct EventId(long Value)
{
    public bool IsValid => Value > 0;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Typed event payloads emitted by the trusted host event projection.</summary>
public interface IGameEvent;

public sealed record GameStartedEvent(int PlayerCount, string ModeId = "") : IGameEvent;

public sealed record SetupStartedEvent(int PlayerCount, string ModeId) : IGameEvent;

public sealed record GeneralSelectionRequestedEvent(
    int ActorSeat,
    IReadOnlyList<string> CandidateIds) : IGameEvent;

public sealed record GeneralSelectedEvent(int ActorSeat, string GeneralId) : IGameEvent;

public sealed record SetupCompletedEvent(int PlayerCount, string ModeId) : IGameEvent;

public sealed record TurnStartedEvent(int TurnNumber, int ActorSeat) : IGameEvent;

public sealed record TurnEndedEvent(int TurnNumber, int ActorSeat) : IGameEvent;

public sealed record PhaseChangedEvent(TurnPhase Phase, int ActorSeat) : IGameEvent;

public sealed record CardUsedEvent(
    int CardId,
    CardKind CardKind,
    int SourceSeat,
    int TargetSeat) : IGameEvent;

public sealed record GroupCardUsedEvent(
    long ResolutionId,
    int CardId,
    CardKind CardKind,
    int SourceSeat,
    IReadOnlyList<int> TargetSeats) : IGameEvent;

public sealed record CardsRevealedEvent(
    long ResolutionId,
    IReadOnlyList<CardSnapshot> Cards) : IGameEvent;

public sealed record HarvestCardSelectedEvent(
    long ResolutionId,
    int PlayerSeat,
    int CardId) : IGameEvent;

/// <summary>
/// Public effect notification for a hidden target-card discard. It intentionally
/// carries no card id or kind; the trusted movement ledger retains that detail.
/// </summary>
public sealed record TargetCardDiscardedEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    CardZoneKind FromZone) : IGameEvent;

/// <summary>
/// Public effect notification for a hidden target-card transfer. It intentionally
/// carries no card id or kind; the trusted movement ledger and source private
/// snapshot retain that detail.
/// </summary>
public sealed record TargetCardTakenEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    CardZoneKind FromZone) : IGameEvent;

/// <summary>
/// Publicly announces the card intentionally revealed by FireAttack. The
/// preceding card-id choice remains private to the target; the card becomes
/// public only after this event is committed.
/// </summary>
public sealed record FireAttackCardRevealedEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    int CardId,
    CardKind CardKind,
    Suit Suit) : IGameEvent;

/// <summary>Trusted-host outcome for the bounded FireAttack resolution.</summary>
public sealed record FireAttackResolvedEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    int RevealedCardId,
    Suit RevealedSuit,
    int? MatchingDiscardCardId,
    bool CausedDamage) : IGameEvent;

public sealed record CardUseDeclaredEvent(
    long ResolutionId,
    int CardId,
    CardKind CardKind,
    int SourceSeat) : IGameEvent;

public sealed record TargetsConfirmedEvent(
    long ResolutionId,
    IReadOnlyList<int> TargetSeats) : IGameEvent;

public sealed record CardUseFinishedEvent(
    long ResolutionId,
    int CardId,
    CardKind CardKind) : IGameEvent;

/// <summary>
/// Public equipment lifecycle result. Equipment cards are visible to every
/// player, so the physical ids are safe here; hidden hands remain redacted.
/// </summary>
public sealed record EquipmentChangedEvent(
    long ResolutionId,
    int PlayerSeat,
    EquipmentSlot Slot,
    int CardId,
    CardKind CardKind,
    int? ReplacedCardId = null) : IGameEvent;

public sealed record ResponseRequestedEvent(
    int SourceSeat,
    int TargetSeat,
    CardKind IncomingCard,
    CardKind? RequiredCardKind = null) : IGameEvent;

public sealed record CardRespondedEvent(
    int CardId,
    int ResponderSeat,
    int SourceSeat,
    CardKind? EffectiveCardKind = null) : IGameEvent;

public sealed record DamageAppliedEvent(
    int SourceSeat,
    int TargetSeat,
    int Amount,
    int RemainingHp,
    DamageNature Nature = DamageNature.Normal) : IGameEvent;

public sealed record DamageRequestedEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    int Amount,
    CardKind? SourceCard,
    DamageNature Nature = DamageNature.Normal) : IGameEvent;

public sealed record AfterDamageEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    int Amount,
    int RemainingHp,
    DamageNature Nature = DamageNature.Normal) : IGameEvent;

/// <summary>
/// Trusted-host lifecycle event for a frozen after-damage trigger window. The
/// candidate list is intentionally not part of a player snapshot because it
/// can reveal private skill ownership and ordering information.
/// </summary>
public sealed record DamageTriggerWindowOpenedEvent(
    long ResolutionId,
    long DamageFrameId,
    int SourceSeat,
    int TargetSeat,
    int CardId,
    CardKind CardKind,
    IReadOnlyList<DamageTriggerCandidate> Candidates) : IGameEvent;

/// <summary>Trusted-host cursor movement for an after-damage trigger window.</summary>
public sealed record DamageTriggerWindowAdvancedEvent(
    long ResolutionId,
    int CandidateIndex,
    bool Completed) : IGameEvent;

public sealed record DamageCardClaimedEvent(
    long ResolutionId,
    int OwnerSeat,
    int SourceSeat,
    int CardId,
    CardKind CardKind,
    SkillKind Skill) : IGameEvent;

public sealed record DamageSkillRequestedEvent(
    long ResolutionId,
    int OwnerSeat,
    int SourceSeat,
    int CardId,
    CardKind CardKind,
    SkillKind Skill,
    string CandidateId = "",
    int Priority = 0) : IGameEvent;

public sealed record DamageSkillResolvedEvent(
    long ResolutionId,
    int OwnerSeat,
    int SourceSeat,
    int CardId,
    CardKind CardKind,
    SkillKind Skill,
    bool Used,
    string CandidateId = "",
    int Priority = 0,
    int? EffectTargetSeat = null) : IGameEvent;

public sealed record DamageSkillCardsDrawnEvent(
    long ResolutionId,
    int OwnerSeat,
    SkillKind Skill,
    IReadOnlyList<int> CardIds,
    int? TargetSeat = null) : IGameEvent;

/// <summary>
/// Trusted-host cost notification for a damage-trigger skill. The card id is
/// intentionally absent from ordinary player views; the movement ledger and
/// this typed event are the authoritative audit trail.
/// </summary>
public sealed record DamageSkillCardDiscardedEvent(
    long ResolutionId,
    int OwnerSeat,
    int CardId,
    CardKind CardKind,
    SkillKind Skill) : IGameEvent;

public sealed record DamageSkillCardGivenEvent(
    long ResolutionId,
    int OwnerSeat,
    int TargetSeat,
    int CardId,
    CardKind CardKind,
    SkillKind Skill) : IGameEvent;

public sealed record AlcoholAppliedEvent(
    long ResolutionId,
    int SourceSeat,
    int DamageBonus) : IGameEvent;

public sealed record AlcoholExpiredEvent(int PlayerSeat) : IGameEvent;

public sealed record RecoveryAppliedEvent(
    int SourceSeat,
    int TargetSeat,
    int Amount,
    int RemainingHp) : IGameEvent;

public sealed record PlayerDiedEvent(int VictimSeat, int? KillerSeat) : IGameEvent;

public sealed record PlayerDyingEvent(
    long ResolutionId,
    int VictimSeat,
    int? KillerSeat) : IGameEvent;

public sealed record DyingResolvedEvent(
    long ResolutionId,
    int VictimSeat,
    bool Survived) : IGameEvent;

public sealed record DyingResponseEvent(
    long ResolutionId,
    int ResponderSeat,
    bool UsedPeach,
    int? PeachCardId,
    bool UsedAlcohol = false,
    int? AlcoholCardId = null) : IGameEvent;

public sealed record DuelResponseEvent(
    long ResolutionId,
    int ResponderSeat,
    bool UsedSlash,
    int? SlashCardId,
    CardKind? ResponseCardKind = null) : IGameEvent;

public sealed record GroupResponseEvent(
    long ResolutionId,
    CardKind IncomingCard,
    CardKind RequiredCardKind,
    int ResponderSeat,
    bool UsedResponse,
    int? ResponseCardId,
    CardKind? ResponseCardKind = null) : IGameEvent;

public sealed record RoleRevealedEvent(int Seat, Role Role) : IGameEvent;

public sealed record WinnerDeterminedEvent(Winner Winner) : IGameEvent;

public sealed record GameEndedEvent(Winner Winner) : IGameEvent;

public sealed record CardMovedEvent(
    int CardId,
    CardKind CardKind,
    CardLocation From,
    CardLocation To,
    CardMoveReason Reason) : IGameEvent;

/// <summary>
/// Immutable event envelope. Events are a trusted-host projection; the ordinary
/// PlayerGameView/GameSnapshot never includes this stream or hidden card payloads.
/// </summary>
public sealed record EventEnvelope(
    EventId Id,
    EventId? ParentId,
    long Sequence,
    long Revision,
    string CorrelationId,
    IGameEvent Payload);
