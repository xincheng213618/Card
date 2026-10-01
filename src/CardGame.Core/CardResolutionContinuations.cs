namespace CardGame.Core;

public sealed record CardAppearanceReference(int Id, CardKind Kind, Suit Suit, int Rank);

public sealed record FactionDefenseState
{
    public bool Active { get; init; } = true;
    public FactionResponsePolicySource Source { get; init; } = null!;
    public int OwnerSeat { get; init; }
    public IReadOnlyList<int> CandidateSeats { get; init; } = [];
    public int CandidateIndex { get; init; }
}

public sealed record StoneAxeState
{
    public bool Active { get; init; } = true;
    public IReadOnlyList<int> CandidateCardIds { get; init; } = [];
}

public sealed record CixiongDoubleSwordsState
{
    public bool Active { get; init; } = true;
    public CixiongDoubleSwordsStage Stage { get; init; }
}

public sealed record QinglongCrescentBladeState
{
    public bool Active { get; init; } = true;
    public bool FactionSlashAttempted { get; init; }
}

public sealed record IceSwordState
{
    public bool Active { get; init; } = true;
    public int PreventedDamageAmount { get; init; }
    public bool Activated { get; init; }
    public IReadOnlyList<int> DiscardedCardIds { get; init; } = [];
}

public sealed record QilinBowState
{
    public bool Active { get; init; } = true;

}

public sealed record FangtianHalberdState
{
    public bool Active { get; init; } = true;
    public int SourceSeat { get; init; }
    public CardAppearanceReference Card { get; init; } = null!;
    public CardKind EffectiveCardKind { get; init; }
    public bool IgnoresArmor { get; init; }
    public int DamageAmount { get; init; }
    public bool UsesFangtian { get; init; }
    public bool CountedTowardSlashLimit { get; init; }
    public CardConversionSource? ConversionSource { get; init; }
    public IReadOnlyList<int> TargetSeats { get; init; } = [];
    public long? CurrentAttackOwnerId { get; init; }
}

public sealed record BorrowedSwordState
{
    public bool Active { get; init; } = true;
    public int SourceSeat { get; init; }
    public int WeaponOwnerSeat { get; init; }
    public int SlashTargetSeat { get; init; }
    public CardAppearanceReference Card { get; init; } = null!;
    public bool AwaitingSlashChoice { get; init; }
    public bool FactionSlashAttempted { get; init; }
    public long? ActiveAttackOwnerId { get; init; }
    public int? SlashCardId { get; init; }
    public CardKind? EffectiveSlashKind { get; init; }
}

public sealed record FactionCardRequestState
{
    public bool Active { get; init; } = true;
    public FactionResponsePolicySource? PolicySource { get; init; }
    public string? AssistedResultBind { get; init; }
    public FactionCardRequestPurpose Purpose { get; init; }
    public int OwnerSeat { get; init; }
    public IReadOnlyList<int> CandidateSeats { get; init; } = [];
    public long? ResponseAttackOwnerId { get; init; }
    public int? TargetSeat { get; init; }
    public long? ProgramSkillFrameId { get; init; }
    public string ProviderFactionId { get; init; } = string.Empty;
    public CardKind RequiredKind { get; init; }
    public long? BorrowedSwordOwnerId { get; init; }
    public long? QinglongCrescentBladeOwnerId { get; init; }
    public int CandidateIndex { get; init; }
    public bool AwaitingProviders { get; init; } = true;
    public bool CostPaid { get; init; }
    public bool ProviderRewarded { get; init; }
    public bool AwaitingZhuqueFanChoice { get; init; }
    public int? ZhuqueFanProviderSeat { get; init; }
    public IReadOnlyList<int> ZhuqueFanPhysicalCardIds { get; init; } = [];
    public long? ActiveAttackOwnerId { get; init; }
}

public sealed record DuelState
{
    public bool Active { get; init; } = true;
    public int ResponderSeat { get; init; }
    public bool FactionSlashAttempted { get; init; }
    public int SuccessfulSlashResponses { get; init; }
}

public sealed record GroupCardState
{
    public bool Active { get; init; } = true;
    public int SourceSeat { get; init; }
    public int DamageSourceSeat { get; init; }
    public CardAppearanceReference Card { get; init; } = null!;
    public IReadOnlyList<int> TargetSeats { get; init; } = [];
    public GroupCardEffect Effect { get; init; }
    public CardKind? RequiredCardKind { get; init; }
    public IReadOnlyList<int> PhysicalCardIds { get; init; } = [];
    public long? CurrentAttackOwnerId { get; init; }
    public IReadOnlyList<int> DamageClaimedPhysicalCardIds { get; init; } = [];
    public IReadOnlyList<int> RevealedCardIds { get; init; } = [];
}

public sealed record CardResolutionContinuations
{
    public YingboGiftState? YingboGift { get; init; }
    public QinglongFollowupState? QinglongFollowup { get; init; }
    public FactionDefenseState? FactionDefense { get; init; }
    public StoneAxeState? StoneAxe { get; init; }
    public CixiongDoubleSwordsState? CixiongDoubleSwords { get; init; }
    public QinglongCrescentBladeState? QinglongCrescentBlade { get; init; }
    public IceSwordState? IceSword { get; init; }
    public QilinBowState? QilinBow { get; init; }
    public FangtianHalberdState? FangtianHalberd { get; init; }
    public BorrowedSwordState? BorrowedSword { get; init; }
    public FactionCardRequestState? FactionCardRequest { get; init; }
    public DuelState? Duel { get; init; }
    public GroupCardState? GroupCard { get; init; }
}

public enum CixiongDoubleSwordsStage
    {
        SourceActivation,
        TargetChoice
    }

public enum FactionCardRequestPurpose
    {
        ProgramSkillUse,
        DuelResponse,
        GroupResponse,
        BorrowedSwordUse,
        QinglongCrescentBladeUse,
        AssistedProgramUse = 701
    }

public enum GroupCardEffect
    {
        ResponseAttack,
        Recovery,
        PublicDraft
    }

public sealed record FactionResponsePolicySource(string SkillId, string SkillInstanceId,
        string PolicyId, string FactionId, CardKind RequiredKind, int DiscardCost = 0, int ProviderDrawCount = 0);

public sealed record QinglongFollowupState(long? NextAttackOwnerId, SuspendedDecisionState? Decision,
    long? FangtianOwnerId, EngineStatus Status, bool Active = true);
public sealed record SuspendedDecisionState(DecisionKind Kind, int PlayerSeat, string Prompt,
    IReadOnlyList<int> ValidCardIds, IReadOnlyList<int> ValidTargetSeats, int? SourceSeat, CardKind? IncomingCard,
    SkillPromptPresentation? SkillPrompt, PromptId PromptId, long Revision, int RequiredCardCount,
    IReadOnlyList<PromptChoice> Choices, bool IsPrivate, IReadOnlyList<string> ValidContentIds,
    int? TargetSeat, CardKind? RequiredCardKind);

public enum YingboGiftContinuation { Attack, GroupAttack }
public sealed record YingboGiftState(int SourceSeat, CardAppearanceReference Card, CardKind CardKind,
    YingboGiftContinuation Continuation, long? AttackOwnerId, long? GroupOwnerId, bool Active = true);
