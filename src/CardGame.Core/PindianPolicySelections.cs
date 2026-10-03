namespace CardGame.Core;

/// <summary>Trusted owning-frame state. Unrevealed card identities never enter a player view or event.</summary>
public sealed record PindianRandomCandidate(int OwnerSeat, int TargetSeat, CardConversionSource Source);
public sealed record PindianRandomReceipt(int OwnerSeat, int TargetSeat, CardConversionSource Source,
    int CardId, uint RandomBefore, uint RandomAfter);
public sealed record PindianRandomSelection(
    IReadOnlyList<PindianRandomCandidate> Candidates,
    IReadOnlyList<PindianRandomReceipt> Receipts,
    int Index = 0, int? ForcedOpponentCardId = null,
    bool DeferredSourceTop = false, int? DeferredSourceCardId = null);

/// <summary>Card identities here have already been revealed by the ordinary Pindian result.</summary>
public sealed record PindianPolicyClaim(int OwnerSeat, CardConversionSource Source,
    SkillProgramCardPolicyKind Kind, IReadOnlyList<int> CardIds);
public sealed record PindianPolicyClaims(IReadOnlyList<PindianPolicyClaim> Claims, int Index = 0);

// The activation is public; the sampled hand card and RNG receipt remain host-private until reveal.
public sealed record PindianRandomHandCommittedEvent(long FrameId, int OwnerSeat, int TargetSeat,
    CardConversionSource Source) : IGameEvent;
