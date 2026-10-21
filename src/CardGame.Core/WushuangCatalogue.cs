using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CardGame.Core;

public sealed record WushuangSkill(int Id, int SourceSpellId, string? SourceClass, string Name,
    string Description, int Cost, int Quality, string? SkillId)
{
    public bool IsAvailableInShop { get; init; }
}

public sealed record WushuangCard(int Id, int SourceCardId, string SourceClass, string Name,
    string Description, Suit Suit, int Rank, int Cost, int Quality, bool IsEquipment, bool AutoEquip, CardKind? Kind)
{
    public bool IsAvailableInShop { get; init; }
}

/// <summary>Single-duel prices, quality and versioned spell identities from the public client.</summary>
public static class WushuangCatalogue
{
    public static IReadOnlyList<WushuangSkill> Skills { get; }
    public static IReadOnlyList<WushuangTactic> Tactics { get; }
    public static IReadOnlyList<WushuangCard> Cards { get; }
    public static string Fingerprint { get; }
    public static IReadOnlyList<string> AdditionalGeneralIds { get; }
    private static readonly HashSet<CardKind> AllowedEquipmentKinds;
    private static readonly HashSet<CardKind> ExcludedCardKinds;
    private static readonly HashSet<string> ExcludedSkillIds;
    private static readonly HashSet<string> ExcludedGeneralIds;

    public static bool IsCardAllowed(CardKind kind) => !ExcludedCardKinds.Contains(kind) &&
        (!EquipmentCatalog.IsEquipment(kind) || AllowedEquipmentKinds.Contains(kind));

    public static bool IsSkillAllowed(ContentSkillDefinition skill) => !ExcludedSkillIds.Contains(skill.Id) &&
        !skill.Description.Contains("护甲", StringComparison.Ordinal);

    public static bool IsGeneralAllowed(string id) => !ExcludedGeneralIds.Contains(id);

    static WushuangCatalogue()
    {
        var reference = Read("reference");
        var bindings = Read("bindings");
        using var source = JsonDocument.Parse(reference);
        using var runtime = JsonDocument.Parse(bindings);
        using var eligibility = JsonDocument.Parse(Read("eligibility"));
        var restrictions = eligibility.RootElement;
        AdditionalGeneralIds = Array.AsReadOnly(restrictions.GetProperty("additionalGenerals").EnumerateArray()
            .Select(row => row.GetString()!).ToArray());
        AllowedEquipmentKinds = restrictions.GetProperty("allowedEquipmentKinds").EnumerateArray()
            .Select(row => Enum.Parse<CardKind>(row.GetString()!)).ToHashSet();
        ExcludedCardKinds = restrictions.GetProperty("excludedCardKinds").EnumerateArray()
            .Select(row => Enum.Parse<CardKind>(row.GetString()!)).ToHashSet();
        var excludedSkills = restrictions.GetProperty("excludedSkillOffers");
        var excludedTactics = restrictions.GetProperty("excludedTacticOffers");
        ExcludedGeneralIds = restrictions.GetProperty("excludedGenerals").EnumerateObject().Select(row => row.Name)
            .ToHashSet(StringComparer.Ordinal);
        var skillBindings = runtime.RootElement.GetProperty("skills");
        var tacticBindings = runtime.RootElement.GetProperty("tactics");
        var cardBindings = runtime.RootElement.GetProperty("cards");
        Cards = Array.AsReadOnly(source.RootElement.GetProperty("cards").EnumerateArray().Select(row =>
        {
            var id = row.GetProperty("id").GetInt32();
            var kind = Binding(cardBindings, id);
            return new WushuangCard(id, row.GetProperty("cardId").GetInt32(), Text(row, "class"), Text(row, "name"),
                Text(row, "description"), Enum.Parse<Suit>(Text(row, "suit")), row.GetProperty("rank").GetInt32(),
                row.GetProperty("tokens").GetInt32(), row.GetProperty("quality").GetInt32(),
                row.GetProperty("isEquipment").GetBoolean(), row.GetProperty("autoEquip").GetBoolean(),
                kind is null ? null : Enum.Parse<CardKind>(kind))
            { IsAvailableInShop = kind is not null && IsCardAllowed(Enum.Parse<CardKind>(kind)) };
        }).ToArray());
        Skills = Array.AsReadOnly(source.RootElement.GetProperty("skills").EnumerateArray().Select(row =>
        {
            var id = row.GetProperty("id").GetInt32();
            return new WushuangSkill(id, row.GetProperty("spellId").GetInt32(), row.GetProperty("class").GetString(),
                Text(row, "name"), Text(row, "description"), row.GetProperty("tokens").GetInt32(),
                row.GetProperty("quality").GetInt32(), Binding(skillBindings, id))
            { IsAvailableInShop = Binding(skillBindings, id) is not null && !excludedSkills.TryGetProperty(id.ToString(), out _) &&
                !Text(row, "description").Contains("护甲", StringComparison.Ordinal) };
        }).ToArray());
        ExcludedSkillIds = Skills.Where(skill => !skill.IsAvailableInShop && skill.SkillId is not null)
            .Select(skill => skill.SkillId!).ToHashSet(StringComparer.Ordinal);
        Tactics = Array.AsReadOnly(source.RootElement.GetProperty("tactics").EnumerateArray().Select(row =>
        {
            var id = row.GetProperty("id").GetInt32();
            var binding = Binding(tacticBindings, id);
            return new WushuangTactic(id, Text(row, "name"), Text(row, "description"), row.GetProperty("tokens").GetInt32(),
                row.GetProperty("quality").GetInt32(), binding?.StartsWith('@') == true ? null : binding)
            {
                IsImplemented = binding is not null,
                IsAvailableInShop = !excludedTactics.TryGetProperty(id.ToString(), out _) &&
                    !Text(row, "description").Contains("护甲", StringComparison.Ordinal),
                StartOnly = Text(row, "description").StartsWith("游戏开始时", StringComparison.Ordinal) ||
                    Text(row, "description").StartsWith("开局", StringComparison.Ordinal),
                RelatedIds = Array.AsReadOnly((row.TryGetProperty("relatedTacticIds", out var related) ? related.GetString() ?? "" : "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray())
            };
        }).ToArray());
        ExcludedSkillIds.UnionWith(Tactics.Where(t => !t.IsAvailableInShop && t.SkillId is not null).Select(t => t.SkillId!));
        // A new verification date or an unrelated client-table update must not
        // invalidate saves when the mode's actual catalogue is unchanged.
        Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { Skills, Tactics, Cards,
                EquipmentKinds = AllowedEquipmentKinds.Order().ToArray(),
                ExcludedCards = ExcludedCardKinds.Order().ToArray(),
                ExcludedGenerals = ExcludedGeneralIds.Order(StringComparer.Ordinal).ToArray(),
                AdditionalGeneralIds, NumericArmor = false, DistanceModifiers = false })))).ToLowerInvariant();
        if (Skills.Select(row => row.Id).Distinct().Count() != Skills.Count ||
            Tactics.Select(row => row.Id).Distinct().Count() != Tactics.Count ||
            Skills.Any(row => row.Cost < 0 || row.Quality is < 0 or > 4) ||
            Tactics.Any(row => row.Cost < 0 || row.Quality is < 1 or > 4) ||
            Cards.Select(row => row.Id).Distinct().Count() != Cards.Count ||
            AllowedEquipmentKinds.Count == 0 || AllowedEquipmentKinds.Any(kind => !EquipmentCatalog.IsEquipment(kind) ||
                EquipmentCatalog.Get(kind).Slot is not (EquipmentSlot.Weapon or EquipmentSlot.Armor)) ||
            AdditionalGeneralIds.Any(string.IsNullOrWhiteSpace) ||
            AdditionalGeneralIds.Distinct(StringComparer.Ordinal).Count() != AdditionalGeneralIds.Count ||
            Cards.Any(row => row.Cost < 0 || row.Quality is < 1 or > 4 || row.Rank is < 1 or > 13 ||
                row.Suit == Suit.None || row.Kind is { } kind && row.IsEquipment != EquipmentCatalog.IsEquipment(kind)))
            throw new InvalidOperationException("Invalid single-duel source catalogue.");
    }

    private static string? Binding(JsonElement rows, int id) =>
        rows.TryGetProperty(id.ToString(System.Globalization.CultureInfo.InvariantCulture), out var row) ? row.GetString() : null;
    private static string Text(JsonElement row, string name) => WebUtility.HtmlDecode(
        Regex.Replace((row.GetProperty(name).GetString() ?? "").Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase), "<[^>]*>", "")).Trim();
    private static string Read(string name)
    {
        using var stream = typeof(WushuangCatalogue).Assembly.GetManifestResourceStream($"CardGame.Core.Wushuang.{name}.json")
            ?? throw new InvalidOperationException($"Missing Wushuang {name} resource.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
