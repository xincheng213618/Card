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
    ProhibitDyingPeachByOthers = 18
}

public sealed record SkillProgramCardPolicy
{
    internal SkillProgramCardPolicy(string id, SkillProgramCardPolicyKind kind,
        IReadOnlyList<CardKind> cardKinds, IReadOnlyList<CardKind> requiredCardKinds,
        int value, Suit? inputSuit, Suit? outputSuit, SkillProgramCondition condition,
        string? factionId = null, Role? ownerRole = null,
        IReadOnlyList<SkillProgramCardCategory>? cardCategories = null,
        IReadOnlyList<Suit>? suits = null) =>
        (Id, Kind, CardKinds, RequiredCardKinds, Value, InputSuit, OutputSuit, Condition,
            FactionId, OwnerRole, CardCategories, Suits) =
        (id, kind, cardKinds, requiredCardKinds, value, inputSuit, outputSuit, condition,
            factionId, ownerRole, cardCategories ?? [], suits ?? []);

    public string Id { get; }
    public SkillProgramCardPolicyKind Kind { get; }
    public IReadOnlyList<CardKind> CardKinds { get; }
    public IReadOnlyList<CardKind> RequiredCardKinds { get; }
    public int Value { get; }
    public Suit? InputSuit { get; }
    public Suit? OutputSuit { get; }
    public SkillProgramCondition Condition { get; }
    public string? FactionId { get; }
    public Role? OwnerRole { get; }
    public IReadOnlyList<SkillProgramCardCategory> CardCategories { get; }
    public IReadOnlyList<Suit> Suits { get; }
}
