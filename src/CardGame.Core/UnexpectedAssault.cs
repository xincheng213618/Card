namespace CardGame.Core;

/// <summary>
/// One accepted reveal on the owning trick use. Both suits are captured at the
/// actual comparison boundary; the use suit was frozen before its material left
/// the provider. The target card remains in its hand.
/// </summary>
public sealed record UnexpectedAssaultRevealReceipt(
    long CardUseFrameId,
    long ActionId,
    int TargetIndex,
    int SourceSeat,
    int TargetSeat,
    int RevealedCardId,
    CardKind RevealedCardKind,
    Suit RevealedPrintedSuit,
    int RevealedRank,
    Suit EffectiveUseSuit,
    Suit EffectiveRevealedSuit)
{
    // Current official client card text explicitly requires this use to have a
    // suit. A colorless use therefore cannot damage even a suited hand card.
    public bool CanCauseDamage => EffectiveUseSuit != Suit.None && EffectiveUseSuit != EffectiveRevealedSuit;
}

/// <summary>Public only after the chosen hand card has actually been revealed.</summary>
public sealed record UnexpectedAssaultRevealedEvent(UnexpectedAssaultRevealReceipt Receipt) : IGameEvent;
