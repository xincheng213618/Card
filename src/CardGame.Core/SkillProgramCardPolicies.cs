namespace CardGame.Core;

/// <summary>Card and turn rules supplied by an enabled program instance.</summary>
public enum SkillProgramCardPolicyKind
{
    AlcoholKingIdentityRank = 4902, ForeignTurnAlcoholUseProhibition = 4903,
    ClaimedEntitiesFaceDownUse = 4500,
    RandomRevealColorFireAttack = 4400, UnrespondableNullification = 4401,
    IgnoreUseDistanceBeforeDealingDamage = 3800,
    UnlimitedAlcoholUse = 3900,
    SuppressOthersNonLockedDuringDying = 4000, ExclusiveTurnPeachUse = 4001, ProhibitBlackTrickTarget = 4002,
    PindianOpponentRandomHand = 3100,
    PindianMaximumSlashClaim = 3101,
    RedirectOwnTurnFactionRecovery = 2700,
    SlashRangeFromEffectiveRank = 2100,
    PreventForeignEquipmentDiscard = 1980,
    IgnoreSlashUseDistanceBySuit = 1860,
    PreventEnteringChain = 1400,
    ProhibitPindianTarget = 1401,
    ProhibitDelayedTrickTarget = 1402,
    DrawFromBottom = 1420,
    FirstActualPlayUseDistanceUnlimited = 1340,
    PindianTopCardChoice = 1180,
    PindianRankBySuit = 1181,
    ProhibitTarget = 0,
    IgnoreUseDistance,
    MinimumResponseCount,
    OfferSkipDiscard,
    ExcludeGlobalTarget,
    AttributeGlobalDamage,
    ClaimResolvedGlobalCard,
    RewriteSuit,
    VirtualEquipment,
    FactionResponseRequest,
    RescueRecoveryBonus,
    FactionHandLimitBonus,
    PreventTrickDamage,
    NullifyBlackSlashWithoutArmor,
    ProhibitNearbyTargetResponse,
    ProhibitTargetSlashResponseBySuit,
    PreventIncomingTrickDamage,
    MinimumResponseCountAsTarget = 17,
    ProhibitTargetBySuit,
    ExclusiveDyingPeachRescue = 550,
    BypassSlashLimitBySuit = 551,
    NextCardUnlimitedAfterNonLockedSkill = 500,
    ForceChained = 450,
    ChainedHandLimitAura = 451,
    MarkerTurnBonuses = 452,
    WoundedPopulationBonuses = 453,
    WoundedInRangeHandLimitPenalty = 454,
    DamageBecomesHpLoss = 600,
    ForeignPublicPileSlash = 781,
    PindianClaim = 784,
    PindianClaimAllWhenSourceWins = 785,
    IgnoreTurnObtainedHandCardsForDiscard = 1000
}

public sealed record SkillProgramCardPolicy(
    string Id,
    SkillProgramCardPolicyKind Kind,
    IReadOnlyList<CardKind> CardKinds,
    IReadOnlyList<CardKind> RequiredCardKinds,
    int Value,
    Suit? InputSuit,
    Suit? OutputSuit,
    SkillProgramCondition Condition,
    string? FactionId = null,
    Role? OwnerRole = null)
{
    public int DiscardCost { get; init; }
    public int ProviderDrawCount { get; init; }
}
