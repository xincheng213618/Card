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
    NullifyBlackSlashWithoutArmor
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
