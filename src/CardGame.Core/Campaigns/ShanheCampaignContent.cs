using System.Security.Cryptography;
using System.Text;

namespace CardGame.Core.Campaigns;

/// <summary>Validates and detaches every nested input before a campaign is published.</summary>
public static partial class ShanheCampaignContent
{
    public static ShanheCampaignDefinition FreezeAndValidate(ShanheCampaignDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        RequireText(definition.Id, "campaign id");
        RequireText(definition.Name, "campaign name");
        GodFactionSelectionRules.ValidateDefault(definition.DefaultGodFactionId);
        Require(definition.Nodes is { Count: > 0 and <= 256 } && definition.Rewards is { Count: <= 512 }, "A campaign requires a bounded node and reward catalogue.");
        Require(definition.PlayerGeneralIds is { Count: > 0 and <= 2048 } && definition.StartingNodeIds is { Count: > 0 }, "A campaign requires player generals and entry nodes.");
        UniqueIds(definition.PlayerGeneralIds, "player general");
        UniqueIds(definition.StartingNodeIds, "entry node");
        Require(definition.StartingGold is >= 0 and <= 1_000_000 && definition.ExtraSkillSlots is >= 0 and <= 8, "Invalid starting gold or skill capacity.");
        ValidateBonuses(definition.StartingBonuses);
        Require(definition.Nodes!.All(n => n is not null) && definition.Rewards!.All(r => r is not null), "Content entries cannot be null.");
        UniqueIds(definition.Nodes.Select(n => n.Id), "node");
        UniqueIds(definition.Rewards.Select(r => r.Id), "reward");
        var nodeIds = definition.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var rewardIds = definition.Rewards.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        Require(definition.StartingNodeIds.All(nodeIds.Contains), "An entry references an unknown node.");
        foreach (var reward in definition.Rewards)
        {
            ValidateOpeningSkillReward(definition, reward);
            RequireText(reward.Name, "reward name");
            Require(reward.Gold is >= 0 and <= 100_000 && reward.Heal is >= 0 and <= 1000 && reward.AddSkillSlots is >= 0 and <= 8, "Invalid reward amounts.");
            if (reward.SkillId is not null) RequireText(reward.SkillId, "reward skill");
            if (reward.TacticSkillId is not null) RequireText(reward.TacticSkillId, "tactic skill");
            ValidateBonuses(reward.Bonuses);
            ValidateCards(reward.StartingCards, equipmentOnly: false);
            ValidateCards(reward.StartingEquipment, equipmentOnly: true);
        }
        foreach (var node in definition.Nodes)
        {
            RequireText(node.Name, "node name");
            Require(Enum.IsDefined(node.Kind), "Unknown campaign node kind.");
            ValidateBossRewards(node, rewardIds);
            Require(node.AddSkillSlotsOnVictory is >= 0 and <= 8 && (IsBattle(node.Kind) || node.AddSkillSlotsOnVictory == 0), "Only battle nodes can grant victory skill slots.");
            Require(node.NextNodeIds is not null, "Next nodes cannot be null.");
            UniqueIds(node.NextNodeIds!, "next node");
            Require(node.NextNodeIds!.All(nodeIds.Contains), "A route references an unknown node.");
            Require(node.RewardChoiceCount is >= 1 and <= 12, "A reward offer must contain one to twelve choices.");
            UniqueIds(node.RewardIds ?? [], "node reward");
            Require((node.RewardIds ?? []).All(rewardIds.Contains), "A node references an unknown reward.");
            var battle = IsBattle(node.Kind);
            Require(node.RewardSkipGold is >= 0 and <= 100_000 && (battle || node.RewardSkipGold == 0),
                "Only battle nodes can grant bounded reward-skip compensation.");
            Require(battle ? node.Encounters is { Count: > 0 and <= 64 } && (node.Choices?.Count ?? 0) == 0 :
                (node.Encounters?.Count ?? 0) == 0 && node.Choices is { Count: > 0 } &&
                node.Choices.Count <= (node.Kind == ShanheNodeKind.Shop ? 512 : 64) && (node.RewardIds?.Count ?? 0) == 0,
                "Battle nodes need encounters; other nodes need explicit choices.");
            UniqueIds((node.Encounters ?? []).Select(e => e?.Id ?? ""), "encounter");
            foreach (var encounter in node.Encounters ?? [])
            {
                RequireText(encounter.Name, "encounter name");
                Require(encounter.Enemies is { Count: > 0 and <= 7 } && (encounter.Allies?.Count ?? 0) <= 6 &&
                    encounter.Enemies.Count + (encounter.Allies?.Count ?? 0) + 1 <= 8, "An encounter requires one to seven enemies and at most eight total seats.");
                Require(encounter.VictoryGold is >= 0 and <= 100_000 && encounter.HealAfterVictory is >= 0 and <= 1000, "Invalid encounter rewards.");
                foreach (var npc in encounter.Enemies!.Concat(encounter.Allies ?? [])) ValidateCombatant(npc);
            }
            UniqueIds((node.Choices ?? []).Select(c => c?.Id ?? ""), "node choice");
            foreach (var choice in node.Choices ?? [])
            {
                RequireText(choice.Name, "choice name");
                Require(choice.Id != "$leave" && choice.GoldCost is >= 0 and <= 100_000 && choice.HpCost is >= 0 and <= 1000, "Invalid choice cost or reserved choice id.");
                Require(choice.RewardId is null || rewardIds.Contains(choice.RewardId), "A choice references an unknown reward.");
                Require(node.Kind == ShanheNodeKind.Shop || choice.LeavesNode, "Only shops may remain open after a choice.");
            }
        }
        ValidateRoutes(definition);
        return definition with
        {
            PlayerGeneralIds = Freeze(definition.PlayerGeneralIds), StartingNodeIds = Freeze(definition.StartingNodeIds),
            StartingBonuses = definition.StartingBonuses ?? new(),
            Rewards = Freeze(definition.Rewards.Select(FreezeReward)),
            Nodes = Freeze(definition.Nodes.Select(node => node with
            {
                NextNodeIds = Freeze(node.NextNodeIds), RewardIds = Freeze(node.RewardIds ?? []),
                Choices = Freeze(node.Choices ?? []),
                BossRewards = FreezeBossRewards(node.BossRewards),
                Encounters = Freeze((node.Encounters ?? []).Select(encounter => encounter with
                {
                    Enemies = Freeze(encounter.Enemies.Select(FreezeCombatant)),
                    Allies = Freeze((encounter.Allies ?? []).Select(FreezeCombatant))
                }))
            }))
        };
    }

    public static string GetContentHash(ShanheCampaignDefinition definition)
    {
        var frozen = FreezeAndValidate(definition);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ShanheCampaignJson.SerializeValue(frozen)))).ToLowerInvariant();
    }

    internal static IReadOnlyList<T> Freeze<T>(IEnumerable<T> source) => Array.AsReadOnly(source.ToArray());
    internal static ShanheCombatantDefinition FreezeCombatant(ShanheCombatantDefinition value) => value with
    {
        ExtraSkillIds = Freeze(value.ExtraSkillIds ?? []),
        QualifiedPrintedLordSkillIds = value.QualifiedPrintedLordSkillIds is null ? null :
            Freeze(value.QualifiedPrintedLordSkillIds.Order(StringComparer.Ordinal)),
        StartingHand = value.StartingHand is null ? null : Freeze(value.StartingHand)
    };
    internal static ShanheRewardDefinition FreezeReward(ShanheRewardDefinition value) => value with
    {
        Bonuses = value.Bonuses ?? new(), StartingCards = Freeze(value.StartingCards ?? []), StartingEquipment = Freeze(value.StartingEquipment ?? [])
    };
    internal static bool IsBattle(ShanheNodeKind kind) => kind is ShanheNodeKind.Battle or ShanheNodeKind.EliteBattle or ShanheNodeKind.BossBattle;
    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new ArgumentException(message);
    }
    private static void RequireText(string? value, string label) => Require(!string.IsNullOrWhiteSpace(value) && value.Length <= 512, $"Invalid {label}.");
    private static void UniqueIds(IEnumerable<string> values, string label)
    {
        var ids = values.ToArray();
        foreach (var id in ids) RequireText(id, label);
        Require(ids.Distinct(StringComparer.Ordinal).Count() == ids.Length, $"Duplicate {label} ids.");
    }
    private static void ValidateBonuses(ShanheTacticBonuses? value)
    {
        if (value is null) return;
        Require(value.MaxHp is >= 0 and <= 40 && value.InitialHand is >= 0 and <= 20 && value.Draw is >= 0 and <= 10 && value.Slash is >= 0 and <= 10,
            "Campaign bonuses must be nonnegative and bounded.");
    }
    private static void ValidateCombatant(ShanheCombatantDefinition npc)
    {
        Require(npc is not null, "An encounter cannot contain a null combatant.");
        RequireText(npc!.GeneralId, "combatant general");
        Require(npc.MaxHpBonus is >= 0 and <= 40 && (npc.Hp is null or > 0 and <= 60) &&
            npc.InitialHandBonus is >= 0 and <= 20 && npc.DrawBonus is >= 0 and <= 10 && npc.SlashBonus is >= 0 and <= 10,
            "Invalid combatant health or bonuses.");
        Require((npc.ExtraSkillIds?.Count ?? 0) <= 8, "Too many combatant skills.");
        UniqueIds(npc.ExtraSkillIds ?? [], "combatant skill");
        UniqueIds(npc.QualifiedPrintedLordSkillIds ?? [], "qualified printed Lord skill");
        ValidateCards(npc.StartingHand, equipmentOnly: false);
    }
    private static void ValidateCards(IReadOnlyList<ShanheStartingCard>? cards, bool equipmentOnly)
    {
        Require((cards?.Count ?? 0) <= (equipmentOnly ? 5 : 24), "Too many campaign starting cards.");
        var slots = new HashSet<EquipmentSlot>();
        foreach (var card in cards ?? [])
        {
            Require(card is not null && Enum.IsDefined(card.Kind) && Enum.IsDefined(card.Suit) && card.Suit != Suit.None && card.Rank is >= 1 and <= 13, "Invalid campaign starting card.");
            _ = CardCatalog.Get(card!.Kind);
            if (equipmentOnly)
            {
                Require(EquipmentCatalog.IsEquipment(card.Kind), "A starting equipment recipe contains a non-equipment card.");
                Require(slots.Add(EquipmentCatalog.Get(card.Kind).Slot), "Starting equipment must use distinct slots.");
            }
        }
    }
    private static void ValidateRoutes(ShanheCampaignDefinition definition)
    {
        var nodes = definition.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string id)
        {
            if (visited.Contains(id)) return;
            Require(visiting.Add(id), "Campaign routes must not contain cycles.");
            foreach (var next in nodes[id].NextNodeIds) Visit(next);
            visiting.Remove(id);
            visited.Add(id);
        }
        foreach (var entry in definition.StartingNodeIds) Visit(entry);
        Require(visited.Count == nodes.Count && definition.Nodes.Any(n => IsBattle(n.Kind)), "All nodes must be reachable and the campaign must include a battle.");
    }
}
