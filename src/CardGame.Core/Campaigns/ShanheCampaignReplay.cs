using System.Text.Json;
using System.Text.Json.Serialization;

namespace CardGame.Core.Campaigns;

public sealed record ShanheCampaignOptions(int Seed, string PlayerGeneralId)
{
    public const int MaximumEquippedTactics = 6;
    private readonly IReadOnlyList<string>? _unlockedRewardIds;
    private readonly IReadOnlyList<string> _equippedTacticIds = Array.Empty<string>();

    /// <summary>Optional host identity for settling a completed expedition once.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpeditionId { get; init; }

    /// <summary>Null preserves the first-rank recipe and JSON of older expeditions.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PlayerRank { get; init; }

    /// <summary>Opts new expeditions into the verified codex skill and equipment reward pool.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool UseCodexRewardPool { get; init; }

    /// <summary>Null preserves unrestricted reward access for expeditions saved before the codex.</summary>
    public IReadOnlyList<string>? UnlockedRewardIds
    {
        get => _unlockedRewardIds;
        init => _unlockedRewardIds = value is null ? null : ShanheCampaignContent.Freeze(value);
    }

    /// <summary>Reward ids for the tactic programs carried into this expedition.</summary>
    public IReadOnlyList<string> EquippedTacticIds
    {
        get => _equippedTacticIds;
        init => _equippedTacticIds = ShanheCampaignContent.Freeze(value ?? throw new ArgumentNullException(nameof(value)));
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(MoveShanheNodeCommand), "move-node")]
[JsonDerivedType(typeof(ChooseShanheRewardCommand), "choose-reward")]
[JsonDerivedType(typeof(ChooseShanheOpeningSkillCommand), "choose-opening-skill")]
[JsonDerivedType(typeof(ChooseShanheBossRewardCommand), "choose-boss-reward")]
[JsonDerivedType(typeof(ConfirmShanheBossRewardCommand), "confirm-boss-reward")]
[JsonDerivedType(typeof(SkipShanheRewardCommand), "skip-reward")]
[JsonDerivedType(typeof(RefreshShanheShopCommand), "refresh-shop")]
[JsonDerivedType(typeof(ChooseShanheNodeOptionCommand), "choose-option")]
[JsonDerivedType(typeof(CompleteShanheBattleCommand), "complete-battle")]
public abstract record ShanheCampaignCommand(long ExpectedRevision);

public sealed record MoveShanheNodeCommand(string NodeId, long ExpectedRevision) : ShanheCampaignCommand(ExpectedRevision);
public sealed record ChooseShanheRewardCommand(string RewardId, string? ReplaceSkillId, long ExpectedRevision) : ShanheCampaignCommand(ExpectedRevision);
public sealed record ChooseShanheOpeningSkillCommand(string RewardId, long ExpectedRevision) : ShanheCampaignCommand(ExpectedRevision);
public sealed record ChooseShanheBossRewardCommand(string GroupId, string RewardId, string? ReplaceSkillId, long ExpectedRevision) : ShanheCampaignCommand(ExpectedRevision);
public sealed record ConfirmShanheBossRewardCommand(long ExpectedRevision) : ShanheCampaignCommand(ExpectedRevision);
public sealed record SkipShanheRewardCommand(long ExpectedRevision) : ShanheCampaignCommand(ExpectedRevision);
public sealed record RefreshShanheShopCommand(long ExpectedRevision) : ShanheCampaignCommand(ExpectedRevision);
public sealed record ChooseShanheNodeOptionCommand(string ChoiceId, string? ReplaceSkillId, long ExpectedRevision) : ShanheCampaignCommand(ExpectedRevision);

/// <summary>A trusted-host battle journal, rather than a caller-supplied success flag.</summary>
public sealed record CompleteShanheBattleCommand(string CheckpointJson, long ExpectedRevision) : ShanheCampaignCommand(ExpectedRevision);

public sealed record ShanheCampaignCheckpoint(
    int SchemaVersion, ShanheCampaignOptions Options, IReadOnlyList<ShanheCampaignCommand> Commands,
    long Revision, string CampaignId, string CampaignHash)
{
    public const int CurrentSchemaVersion = 1;
    private readonly IReadOnlyList<string>? _playerGeneralIds;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TacticAcquisitionHash { get; init; }

    /// <summary>The exact selectable roster fingerprinted when this expedition began. Null identifies legacy fixed-roster saves.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? PlayerGeneralIds
    {
        get => _playerGeneralIds;
        init => _playerGeneralIds = value is null ? null : ShanheCampaignContent.Freeze(value);
    }
}

public sealed record ShanheCampaignSnapshot(
    string CampaignId, string CampaignName, ShanheCampaignPhase Phase, long Revision,
    string? CurrentNodeId, int Hp, int MaxHp, int Gold, int ExtraSkillSlots,
    ShanheTacticBonuses Bonuses, IReadOnlyList<string> ExtraSkillIds,
    IReadOnlyList<ShanheNodeDefinition> AvailableNodes, IReadOnlyList<ShanheRewardDefinition> RewardOffers,
    IReadOnlyList<ShanheNodeChoiceDefinition> NodeChoices, IReadOnlyList<string> CompletedNodeIds,
    ShanheBattleSetup? PendingBattleSetup, string StoryText, string EndingText)
{
    public string PlayerGeneralId { get; init; } = "";
    public int PlayerRank { get; init; } = 1;
    public bool RebirthUsed { get; init; }
    public IReadOnlyList<ShanheStartingCard> StartingCards { get; init; } = Array.Empty<ShanheStartingCard>();
    public IReadOnlyList<ShanheStartingCard> StartingEquipment { get; init; } = Array.Empty<ShanheStartingCard>();
    public int EncounterOrdinal { get; init; }
    public int ChapterNumber { get; init; }
    public string? ChapterTitle { get; init; }
    public ShanheBossRewardSummary? BossRewardSummary { get; init; }
    private readonly IReadOnlyList<ShanheRewardDefinition> _openingSkillOffers = Array.Empty<ShanheRewardDefinition>();
    public IReadOnlyList<ShanheRewardDefinition> OpeningSkillOffers
    {
        get => _openingSkillOffers;
        init => _openingSkillOffers = ShanheCampaignContent.Freeze(value);
    }
    /// <summary>Refreshes left at the active shop; legacy fixed-stock shops have none.</summary>
    public int ShopRefreshRemaining { get; init; }
    public int ShopRefreshCost { get; init; }
    /// <summary>Exact compensation for the currently published reward offer; zero outside it.</summary>
    public int RewardSkipGold { get; init; }
    public bool CanSkipReward => Phase == ShanheCampaignPhase.Reward && RewardSkipGold > 0;
    /// <summary>Acquired reward ids, retaining tactic names and their content provenance.</summary>
    public IReadOnlyList<string> TacticIds { get; init; } = Array.Empty<string>();
    private readonly IReadOnlyList<ShanheShopItemSnapshot> _shopItems = Array.Empty<ShanheShopItemSnapshot>();
    /// <summary>All products at the current shop, including purchased and unavailable stock.</summary>
    public IReadOnlyList<ShanheShopItemSnapshot> ShopItems
    {
        get => _shopItems;
        init => _shopItems = Array.AsReadOnly(value.ToArray());
    }
}

public enum ShanheShopItemState { Available, Purchased, AlreadyOwned, InsufficientResources, NoSkillSlot, RewardLimitReached }

public sealed record ShanheShopItemSnapshot(
    string ChoiceId, string Name, string Description, int GoldCost, int HpCost,
    ShanheRewardDefinition? Reward, ShanheShopItemState State, bool CanAfford,
    bool RequiresSkillReplacement, long Revision)
{
    private readonly IReadOnlyList<string> _replaceableSkillIds = Array.Empty<string>();
    public bool CanPurchase => State == ShanheShopItemState.Available;
    public bool IsPurchased => State == ShanheShopItemState.Purchased;
    public bool IsAlreadyOwned => State == ShanheShopItemState.AlreadyOwned;
    public IReadOnlyList<string> ReplaceableSkillIds
    {
        get => _replaceableSkillIds;
        init => _replaceableSkillIds = Array.AsReadOnly(value.ToArray());
    }
}

public sealed record ShanheCampaignResult(bool Accepted, string? Error, long Revision, ShanheCampaignSnapshot State);

/// <summary>The campaign's own journal format. Individual battles retain their existing checkpoint format.</summary>
public static class ShanheCampaignJson
{
    internal const int MaximumJsonCharacters = 128 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(ShanheCampaignCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return SerializeValue(checkpoint);
    }

    public static ShanheCampaignCheckpoint Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (json.Length > MaximumJsonCharacters) throw new ArgumentException("Campaign checkpoint is too large.", nameof(json));
        return JsonSerializer.Deserialize<ShanheCampaignCheckpoint>(json, Options) ?? throw new ArgumentException("Campaign checkpoint is empty.", nameof(json));
    }

    internal static string SerializeValue<T>(T value) => JsonSerializer.Serialize(value, Options);
}
