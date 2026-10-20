using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CardGame.Core;

public sealed record WushuangSkill(int Id, int SourceSpellId, string? SourceClass, string Name,
    string Description, int Cost, int Quality, string? SkillId);

/// <summary>Single-duel prices, quality and versioned spell identities from the public client.</summary>
public static class WushuangCatalogue
{
    public static IReadOnlyList<WushuangSkill> Skills { get; }
    public static IReadOnlyList<WushuangTactic> Tactics { get; }
    public static string Fingerprint { get; }

    static WushuangCatalogue()
    {
        var reference = Read("reference");
        var bindings = Read("bindings");
        Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reference + "\n" + bindings))).ToLowerInvariant();
        using var source = JsonDocument.Parse(reference);
        using var runtime = JsonDocument.Parse(bindings);
        var skillBindings = runtime.RootElement.GetProperty("skills");
        var tacticBindings = runtime.RootElement.GetProperty("tactics");
        Skills = Array.AsReadOnly(source.RootElement.GetProperty("skills").EnumerateArray().Select(row =>
        {
            var id = row.GetProperty("id").GetInt32();
            return new WushuangSkill(id, row.GetProperty("spellId").GetInt32(), row.GetProperty("class").GetString(),
                Text(row, "name"), Text(row, "description"), row.GetProperty("tokens").GetInt32(),
                row.GetProperty("quality").GetInt32(), Binding(skillBindings, id));
        }).ToArray());
        Tactics = Array.AsReadOnly(source.RootElement.GetProperty("tactics").EnumerateArray().Select(row =>
        {
            var id = row.GetProperty("id").GetInt32();
            var binding = Binding(tacticBindings, id);
            return new WushuangTactic(id, Text(row, "name"), Text(row, "description"), row.GetProperty("tokens").GetInt32(),
                row.GetProperty("quality").GetInt32(), binding is "@economy" ? null : binding)
            {
                IsImplemented = binding is not null,
                RelatedIds = Array.AsReadOnly((row.TryGetProperty("relatedTacticIds", out var related) ? related.GetString() ?? "" : "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray())
            };
        }).ToArray());
        if (Skills.Select(row => row.Id).Distinct().Count() != Skills.Count ||
            Tactics.Select(row => row.Id).Distinct().Count() != Tactics.Count ||
            Skills.Any(row => row.Cost < 0 || row.Quality is < 0 or > 4) ||
            Tactics.Any(row => row.Cost < 0 || row.Quality is < 1 or > 4))
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
