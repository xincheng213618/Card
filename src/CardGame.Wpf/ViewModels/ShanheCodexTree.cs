using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

namespace CardGame.Wpf.ViewModels;

/// <summary>Season 5 topology and individual costs, checked against client screenshots.</summary>
public static class ShanheCodexTree
{
    public static IReadOnlyList<ShanheCodexEntry> Entries { get; } = Load();
    private static IReadOnlyList<ShanheCodexEntry> Load()
    {
        using var stream = typeof(ShanheCodexTree).Assembly.GetManifestResourceStream("CardGame.ShanheCodex2026.json")
            ?? throw new InvalidOperationException("Missing Shanhe codex tree.");
        var entries = JsonSerializer.Deserialize<ShanheCodexEntry[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return Array.AsReadOnly(entries);
    }
    public static string Key(int id) => "shanhe-codex:5:" + id;
    // Researched route nodes may acquire a runtime implementation in a later
    // content batch. Derive that paid entitlement without rewriting the account
    // or the options of an expedition that has already started.
    public static IReadOnlyList<string> ResolveUnlockedRewardIds(IReadOnlyCollection<string> unlocked,
        IEnumerable<string> availableRewardIds)
    {
        var researched = unlocked.ToHashSet(StringComparer.Ordinal);
        var available = availableRewardIds.ToHashSet(StringComparer.Ordinal);
        return Array.AsReadOnly(unlocked.Concat(Entries.Where(entry => researched.Contains(Key(entry.Id)))
                .Select(entry => RuntimeRewardId(entry.Id)).OfType<string>())
            .Where(available.Contains).Distinct(StringComparer.Ordinal).ToArray());
    }
    public static IReadOnlyList<ShanheCodexEntry> RequiredPath(int id, IReadOnlyCollection<string> unlocked)
    {
        var result = new List<ShanheCodexEntry>();
        var visited = new HashSet<int>();
        void Visit(int current)
        {
            if (unlocked.Contains(Key(current)) || !visited.Add(current)) return;
            var entry = Entries.Single(node => node.Id == current);
            foreach (var parent in entry.Parents.Concat(entry.Conditions).Distinct()) Visit(parent);
            result.Add(entry);
        }
        Visit(id);
        return result.AsReadOnly();
    }
    // Explicit semantic matches only. Same-name variants must not substitute for another version.
    public static string? RuntimeRewardId(int id) => StandardShanheCampaignCatalog.CodexRewardIdsByNodeId.TryGetValue(id, out var mapped) ? mapped : id switch
    {
        4 => "original-shizhan", 7 => "original-jueqing", 16 => "tactic-ruoxi",
        20 => "tactic-duichu-2026", 26 => "original-sanyao", 33 => "tactic-zengshou",
        37 => "tactic-fanci", 44 => "tactic-jiban-2026", 71 => "tactic-shixue",
        79 => "original-qingxian", 87 => "original-huituo", 119 => "tactic-xueren",
        _ => null
    };
    public static CardKind? EquipmentKind(int id) => RuntimeRewardId(id) is { } rewardId &&
        StandardShanheCampaignCatalog.ShopMetadataByRewardId.TryGetValue(rewardId, out var metadata) ? metadata.CardKind : null;
}
public sealed record ShanheCodexEntry(int Id, int SeasonId, int Layer, int Lane, int Cost,
    int[] Parents, int[] Conditions, int RewardId, string Name, string Description, int Rarity, string RewardType);
