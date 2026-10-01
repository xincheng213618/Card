using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum CardDeclarationPurpose { Use, Dodge, Duel, Group, Counterspell, Recovery, FactionDefense, FactionSlash, ProvidedSlash, BorrowedSword, BorrowedSwordProvidedSlash, QinglongProvidedSlash }
public enum CardDeclarationStage { Paying, Challenging, Granting, Cleaning, Returning, Applying }

public sealed record CardDeclarationParentReturn(CardDeclarationPurpose Purpose, long ParentFrameId,
    ResolutionFrameKind? ParentKind, ResolutionFrameStep? ParentStep, int ActorSeat,
    LegalAction? UseAction = null, int? TargetSeat = null, int RecoveryAmount = 1,
    IReadOnlyList<ProgramRecoveryPolicySource>? RecoveryPolicies = null,
    DyingResponseEvent? DyingResponse = null, bool UsesZhuqueFan = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CardKind? FinalEffectiveKind = null);

/// <summary>Trusted paid-cost receipt owned by the actual continuation frame.</summary>
public sealed record DeclaredCardPayment(long DeclarationId, long OwnerFrameId, int ProviderSeat,
    CardActionCost Cost, CardConversionSource Source, CardKind DeclaredKind, bool Claimed = false, bool IsRevealed = false, int? ActorSeat = null, Suit? FrozenSuit = null);

public sealed record CardDeclarationFrame(long Id, int OwnerSeat, int TurnNumber,
    DeclaredCardPayment Payment, CardDeclarationParentReturn Return,
    IReadOnlyList<int> TargetSeats, CardDeclarationStage Stage = CardDeclarationStage.Paying,
    bool IsRevealed = false, int? ChallengerSeat = null, bool? Succeeded = null,
    long? ActiveChildFrameId = null, ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.CardDeclaration, Step);

public sealed record CardDeclarationChallengeFrame(long Id, long ParentFrameId,
    IReadOnlyList<int> EligibleSeats, int SeatIndex = 0,
    ResolutionFrameStep Step = ResolutionFrameStep.AwaitingResponse)
    : ResolutionFrame(Id, ResolutionFrameKind.CardDeclarationChallenge, Step);

/// <summary>Player surface; private cost is scoped to its owner, never to challenge AI.</summary>
public sealed record CardDeclarationSnapshot(long DeclarationId, int OwnerSeat, int ActorSeat,
    CardKind DeclaredKind, IReadOnlyList<int> TargetSeats, bool IsRevealed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CardSnapshot? RevealedCard = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CardSnapshot? OwnerCost = null);

public sealed record CardDeclarationCommittedEvent(long DeclarationId, int OwnerSeat, int ActorSeat,
    CardKind DeclaredKind, IReadOnlyList<int> TargetSeats) : IGameEvent;
public sealed record CardDeclarationRevealedEvent(long DeclarationId, int ChallengerSeat, CardSnapshot Card,
    bool Succeeded) : IGameEvent;
