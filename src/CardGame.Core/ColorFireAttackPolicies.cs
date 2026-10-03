namespace CardGame.Core;

// All policy receipts are scalars. The physical reveal becomes public only at
// the normal FireAttackCardRevealedEvent; no other target hand is published.
public sealed record ColorFireAttackReceipt(CardConversionSource Source, long ActionId,
    int? TargetSeat = null, int? RevealedCardId = null, Suit? RevealedSuit = null,
    bool? RevealedIsRed = null, int? PaidCardId = null, CardLocation? PaidFrom = null,
    bool? PaidIsRed = null, long PaymentStartSequence = 0, long PaymentEndSequence = 0);

public sealed record UnrespondableCounterspellReceipt(CardConversionSource Source,
    long WindowFrameId, long ParentCardUseFrameId, long ActionId, int ResponderSeat,
    int ChainDepth);

public sealed record CounterspellPaidUseReceipt(CardActionContext Action);

public sealed record ColorFireAttackPolicyIssuedEvent(long CardUseFrameId, long ActionId,
    CardConversionSource Source) : IGameEvent;
public sealed record ColorFireAttackPaidEvent(long CardUseFrameId, int SourceSeat,
    int TargetSeat, bool IsRed, bool FromEquipment) : IGameEvent;
public sealed record UnrespondableCounterspellIssuedEvent(long WindowFrameId,
    long ActionId, int ResponderSeat, int ChainDepth, CardConversionSource Source) : IGameEvent;
public sealed record ActualTurnTrickUseRecordedEvent(int TurnNumber, int ActorSeat,
    long ActionId, CardKind EffectiveKind) : IGameEvent;
