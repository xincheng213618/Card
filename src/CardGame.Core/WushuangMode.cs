namespace CardGame.Core;

/// <summary>Local single-match adaptation; economy and timeout scoring are intentionally explicit.</summary>
public static class WushuangMode
{
    public const string ModeId = "wushuang:solo";
    public const string PlayerTeamId = "team:blue";
    public const string AiTeamId = "team:red";
    public const int TimeLimitMilliseconds = 15 * 60 * 1000;
    public const int MaximumExtraSkills = 2;
    public const int StartingTokens = 0;
}

public enum WushuangOfferKind { Skill, Draw, Recover, MaxHp, Tactic, Card }
public enum WushuangShopPhase { Closed, OpeningTactic, Shopping }

public sealed partial record PlayerSnapshot
{
    /// <summary>Public acquired tactics, independent of the private shop and extra skill slots.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<int>? WushuangTacticIds { get; init; }
}

public sealed record WushuangShopOffer(string Id, string Name, string Description, int Cost,
    string? SkillId, WushuangOfferKind Kind, int RequiredLevel)
{
    public int Quality { get; init; } = 1;
    public int? TacticId { get; init; }
    public CardKind? CardKind { get; init; }
}

/// <summary>Only the requested seat's shop is exposed; collections are immutable projections.</summary>
public sealed record WushuangSnapshot(int Seat, int Level, int Experience, int ExperienceToNextLevel,
    int Tokens, IReadOnlyList<string> ExtraSkillIds, IReadOnlyList<WushuangShopOffer> Offers,
    bool CanShop, int RerollCost, int LevelUpCost, int RemainingMilliseconds, long Revision)
{
    public WushuangShopPhase Phase { get; init; }
    public long WindowId { get; init; }
    public bool IsLocked { get; init; }
    public IReadOnlyList<WushuangShopOffer> PurchasedOffers { get; init; } = Array.AsReadOnly(Array.Empty<WushuangShopOffer>());
    public IReadOnlyList<int> TacticIds { get; init; } = Array.AsReadOnly(Array.Empty<int>());
}

public sealed record BuyWushuangOfferCommand(int ActorSeat, string OfferId, string? ReplaceSkillId,
    long ExpectedRevision) : GameCommand(ActorSeat, ExpectedRevision);
public sealed record RerollWushuangShopCommand(int ActorSeat, long ExpectedRevision) : GameCommand(ActorSeat, ExpectedRevision);
public sealed record UpgradeWushuangShopCommand(int ActorSeat, long ExpectedRevision) : GameCommand(ActorSeat, ExpectedRevision);
public sealed record FinishWushuangShopCommand(int ActorSeat, long ExpectedRevision) : GameCommand(ActorSeat, ExpectedRevision);
public sealed record ToggleWushuangShopLockCommand(int ActorSeat, long ExpectedRevision) : GameCommand(ActorSeat, ExpectedRevision);
/// <summary>Only elapsed durations supplied by the trusted local host enter the replay journal.</summary>
public sealed record TickWushuangClockCommand(int ElapsedMilliseconds, long ExpectedRevision) : GameCommand(-1, ExpectedRevision);

public sealed record WushuangTokensChangedEvent(int Seat, int Delta, int Tokens, string Reason) : IGameEvent;
public sealed record WushuangLevelChangedEvent(int Seat, int Level, int Experience) : IGameEvent;
public sealed record WushuangShopRefreshedEvent(int Seat, int RefreshNumber) : IGameEvent;
public sealed record WushuangOfferBoughtEvent(int Seat, string OfferId, string? SkillId,
    string? ReplacedSkillId, int Cost) : IGameEvent;
public sealed record WushuangClockAdvancedEvent(int ElapsedMilliseconds, int TotalMilliseconds) : IGameEvent;
public sealed record WushuangClockExpiredEvent(int TotalMilliseconds, int? WinnerSeat, string ScoringRule) : IGameEvent;
public sealed record WushuangResolutionCancelledEvent(long FrameId, ResolutionFrameKind Kind) : IGameEvent;
public sealed record WushuangShopLockChangedEvent(int Seat, bool IsLocked) : IGameEvent;
public sealed record WushuangTacticAcquiredEvent(int Seat, int TacticId, bool IsOpening) : IGameEvent;

public sealed record WushuangIntermissionFrame(long Id, bool Opening, int EndedSeat, bool Skipped, int SeatCursor = 0)
    : ResolutionFrame(Id, ResolutionFrameKind.WushuangIntermission, ResolutionFrameStep.ResolvingEffect);
