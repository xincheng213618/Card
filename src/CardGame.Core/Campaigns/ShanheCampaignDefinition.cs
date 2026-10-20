using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace CardGame.Core.Campaigns;

public enum ShanheNodeKind { Battle, EliteBattle, BossBattle, Event, Shop, Camp }
public enum ShanheCampaignPhase { Map, Battle, Reward, NodeChoice, Won, Lost, OpeningSkillDraft }

/// <summary>Content-defined, additive bonuses. A campaign never changes the global rules epoch.</summary>
public sealed record ShanheTacticBonuses(int MaxHp = 0, int InitialHand = 0, int Draw = 0, int Slash = 0);

public sealed record ShanheStartingCard(CardKind Kind, Suit Suit = Suit.Spade, int Rank = 1);

public sealed record ShanheCombatantDefinition(
    string GeneralId, int MaxHpBonus = 0, int? Hp = null, int InitialHandBonus = 0,
    int DrawBonus = 0, int SlashBonus = 0, IReadOnlyList<string>? ExtraSkillIds = null)
{
    private readonly IReadOnlyList<ShanheStartingCard>? _startingHand;
    private readonly IReadOnlyList<string>? _qualifiedPrintedLordSkillIds;
    /// <summary>Explicit scenario qualification for these original primary-general Lord skills only.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? QualifiedPrintedLordSkillIds
    {
        get => _qualifiedPrintedLordSkillIds;
        init => _qualifiedPrintedLordSkillIds = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
    /// <summary>Replaces the ordinary opening hand; null retains the deck's usual base. Bonuses add random cards.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ShanheStartingCard>? StartingHand
    {
        get => _startingHand;
        init => _startingHand = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}

/// <summary>The entire immutable input to one encounter, including its run identity.</summary>
public sealed record ShanheBattleSetup(
    int RunSeed, int EncounterOrdinal, string NodeId, string PlayerGeneralId, int Hp,
    int MaxHpBonus, int InitialHandBonus, int DrawBonus, int SlashBonus,
    IReadOnlyList<string> ExtraSkillIds, IReadOnlyList<ShanheStartingCard> StartingCards,
    IReadOnlyList<ShanheStartingCard> StartingEquipment,
    IReadOnlyList<ShanheCombatantDefinition> EnemyRoster,
    IReadOnlyList<ShanheCombatantDefinition> AllyRoster, string CampaignHash)
{
    public const string BattleModeId = "campaign:shanhe-battle";
    public const string PlayerTeamId = "team:shanhe-player";
    public const string EnemyTeamId = "team:shanhe-enemy";
    public IReadOnlyList<string> TacticSkillIds { get; init; } = Array.Empty<string>();
    /// <summary>Fallback for a printed God general with no chosen or native effective faction.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultGodFactionId { get; init; }

    public int BattleSeed => DeriveSeed(RunSeed, NodeId, EncounterOrdinal);

    public static bool ValueEquals(ShanheBattleSetup? left, ShanheBattleSetup? right) =>
        left is null || right is null ? left is null && right is null :
        ShanheCampaignJson.SerializeValue(Freeze(left)) == ShanheCampaignJson.SerializeValue(Freeze(right));

    public static ShanheBattleSetup Freeze(ShanheBattleSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        GodFactionSelectionRules.ValidateDefault(setup.DefaultGodFactionId);
        ArgumentNullException.ThrowIfNull(setup.ExtraSkillIds);
        ArgumentNullException.ThrowIfNull(setup.StartingCards);
        ArgumentNullException.ThrowIfNull(setup.StartingEquipment);
        ArgumentNullException.ThrowIfNull(setup.EnemyRoster);
        ArgumentNullException.ThrowIfNull(setup.AllyRoster);
        ArgumentNullException.ThrowIfNull(setup.TacticSkillIds);
        if (setup.EnemyRoster.Any(value => value is null) || setup.AllyRoster.Any(value => value is null))
            throw new ArgumentException("A campaign roster cannot contain null combatants.", nameof(setup));
        if (setup.StartingCards.Any(value => value is null) || setup.StartingEquipment.Any(value => value is null))
            throw new ArgumentException("A campaign opening cannot contain null physical cards.", nameof(setup));
        return setup with
        {
            ExtraSkillIds = ShanheCampaignContent.Freeze(setup.ExtraSkillIds),
            StartingCards = ShanheCampaignContent.Freeze(setup.StartingCards),
            StartingEquipment = ShanheCampaignContent.Freeze(setup.StartingEquipment),
            TacticSkillIds = ShanheCampaignContent.Freeze(setup.TacticSkillIds),
            EnemyRoster = ShanheCampaignContent.Freeze(setup.EnemyRoster.Select(ShanheCampaignContent.FreezeCombatant)),
            AllyRoster = ShanheCampaignContent.Freeze(setup.AllyRoster.Select(ShanheCampaignContent.FreezeCombatant))
        };
    }

    internal static int DeriveSeed(int seed, string nodeId, int ordinal)
    {
        // String.GetHashCode and System.Random are deliberately absent from persisted paths.
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{nodeId}"));
        return unchecked((int)((uint)bytes[0] | (uint)bytes[1] << 8 | (uint)bytes[2] << 16 | (uint)bytes[3] << 24));
    }
}

public sealed record ShanheEncounterDefinition(
    string Id, string Name, IReadOnlyList<ShanheCombatantDefinition> Enemies,
    IReadOnlyList<ShanheCombatantDefinition>? Allies = null, int VictoryGold = 0, int HealAfterVictory = 0);

public sealed record ShanheRewardDefinition(
    string Id, string Name, int Gold = 0, int Heal = 0, ShanheTacticBonuses? Bonuses = null,
    string? SkillId = null, IReadOnlyList<ShanheStartingCard>? StartingCards = null,
    IReadOnlyList<ShanheStartingCard>? StartingEquipment = null, int AddSkillSlots = 0)
{
    public string Description { get; init; } = "";
    /// <summary>A campaign tactic program. This never consumes an extra general-skill slot.</summary>
    public string? TacticSkillId { get; init; }
    private readonly IReadOnlyList<string>? _openingSkillRewardIds;
    /// <summary>An initial tactic's one-time three-choice skill pool; it is not a battle program.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? OpeningSkillRewardIds
    {
        get => _openingSkillRewardIds;
        init => _openingSkillRewardIds = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}

public sealed record ShanheNodeChoiceDefinition(
    string Id, string Name, int GoldCost = 0, int HpCost = 0, string? RewardId = null, bool LeavesNode = true)
{
    public string Description { get; init; } = "";
}

public sealed record ShanheNodeDefinition(
    string Id, string Name, ShanheNodeKind Kind, IReadOnlyList<string> NextNodeIds,
    IReadOnlyList<ShanheEncounterDefinition>? Encounters = null, IReadOnlyList<string>? RewardIds = null,
    int RewardChoiceCount = 3, IReadOnlyList<ShanheNodeChoiceDefinition>? Choices = null)
{
    public string StoryText { get; init; } = "";
    public string Description { get; init; } = "";
    public string VictoryText { get; init; } = "";
    public int AddSkillSlotsOnVictory { get; init; }
    /// <summary>Compensation for declining this node's published reward offer; zero disables it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int RewardSkipGold { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ChapterNumber { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChapterTitle { get; init; }
    /// <summary>Optional completion bundle. Fixed credits replace ordinary encounter credits.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShanheBossRewardDefinition? BossRewards { get; init; }
}

/// <summary>One named edition and difficulty. Sources and narrative belong to content, not the view model.</summary>
public sealed record ShanheCampaignDefinition(
    string Id, string Name, IReadOnlyList<string> PlayerGeneralIds, IReadOnlyList<string> StartingNodeIds,
    IReadOnlyList<ShanheNodeDefinition> Nodes, IReadOnlyList<ShanheRewardDefinition> Rewards,
    int StartingGold = 0, int ExtraSkillSlots = 2, ShanheTacticBonuses? StartingBonuses = null)
{
    public string Description { get; init; } = "";
    public string IntroText { get; init; } = "";
    public string VictoryText { get; init; } = "";
    public string DefeatText { get; init; } = "";
    public bool RandomStartingNode { get; init; }
    /// <summary>Optional edition-specific default; explicit effective factions retain precedence.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultGodFactionId { get; init; }
}
