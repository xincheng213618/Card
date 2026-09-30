namespace CardGame.Core;

/// <summary>Card and turn rules supplied by an enabled program instance.</summary>
public enum SkillProgramCardPolicyKind
{
    ProhibitTarget,
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
    PindianClaim = 784
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
    Role? OwnerRole = null);
