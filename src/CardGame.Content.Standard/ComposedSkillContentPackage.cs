using System.Text.Json;
using System.Text.Json.Serialization;
using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>
/// Opt-in, JSON-authored skill showcase. Keeping it in a separate package leaves
/// every existing standard registry fingerprint unchanged.
/// </summary>
public sealed class ComposedSkillContentPackage : IGameContentPackage
{
    private const string RulesResource = "CardGame.Content.Standard.SkillPrograms.composed-skills.rules.json";
    private const string PresentationResource = "CardGame.Content.Standard.SkillPrograms.composed-skills.presentation.json";
    private const string GeneralsResource = "CardGame.Content.Standard.SkillPrograms.composed-generals.json";
    private static readonly Lazy<SkillProgramCatalog> Catalog = new(LoadCatalog);
    private static readonly Lazy<IReadOnlyList<ComposedGeneralDefinition>> Generals = new(LoadGenerals);

    public PackageManifest Manifest { get; } = new(
        "standard-composed-skills",
        new Version(1, 0, 0),
        [new PackageDependency("standard", new Version(1, 11, 0))]);

    public void Register(IContentRegistryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        foreach (var (id, program) in Catalog.Value.Programs.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var presentation = Catalog.Value.Presentations[id];
            builder.AddSkill(new ContentSkillDefinition(
                id,
                presentation.Name,
                presentation.Description)
            {
                Program = program
            });
        }

        foreach (var general in Generals.Value)
            builder.AddGeneral(new ContentGeneralDefinition(
                general.Id!,
                general.Name!,
                general.PortraitKey!,
                general.SkillIds![0],
                general.FactionId,
                AdditionalSkillIds: general.SkillIds.Count == 1 ? null : general.SkillIds.Skip(1).ToArray()));

        var generalIds = Generals.Value.Select(general => general.Id!).ToArray();

        builder.AddMode(new ContentModeDefinition(
            Id: "identity:composed-skills-5",
            Name: "技能组合体验",
            MinPlayers: 5,
            MaxPlayers: 5,
            RoleCounts: new Dictionary<string, int>
            {
                [nameof(Role.Lord)] = 1,
                [nameof(Role.Loyalist)] = 1,
                [nameof(Role.Rebel)] = 2,
                [nameof(Role.Renegade)] = 1
            },
            DeckId: "standard:basic-demo",
            GeneralCandidateCount: 3,
            GeneralPoolIds: generalIds));
    }

    private static SkillProgramCatalog LoadCatalog() => SkillProgramCatalog.Load(
        ReadEmbeddedText(RulesResource),
        ReadEmbeddedText(PresentationResource));

    private static IReadOnlyList<ComposedGeneralDefinition> LoadGenerals()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        ComposedGeneralFile file;
        try
        {
            file = JsonSerializer.Deserialize<ComposedGeneralFile>(ReadEmbeddedText(GeneralsResource), options)
                ?? throw new InvalidOperationException("The composed-general file is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Invalid composed-general JSON: {exception.Message}", exception);
        }

        if (file.SchemaVersion != 1)
            throw new InvalidOperationException($"Unsupported composed-general schema version {file.SchemaVersion}; expected 1.");
        if (file.Generals is null || file.Generals.Count == 0)
            throw new InvalidOperationException("The composed-general file must contain at least one general.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var general in file.Generals)
        {
            if (general is null || string.IsNullOrWhiteSpace(general.Id) || string.IsNullOrWhiteSpace(general.Name) ||
                string.IsNullOrWhiteSpace(general.PortraitKey) || string.IsNullOrWhiteSpace(general.FactionId) ||
                general.SkillIds is not { Count: > 0 } || general.SkillIds.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("Each composed general requires id, name, portraitKey, factionId and at least one skillId.");
            if (!ids.Add(general.Id))
                throw new InvalidOperationException($"Duplicate composed general id '{general.Id}'.");
            if (general.SkillIds.Distinct(StringComparer.Ordinal).Count() != general.SkillIds.Count)
                throw new InvalidOperationException($"Composed general '{general.Id}' repeats a skill id.");
        }
        return file.Generals.AsReadOnly();
    }

    private static string ReadEmbeddedText(string name)
    {
        using var stream = typeof(ComposedSkillContentPackage).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded skill-program resource '{name}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed record ComposedGeneralFile(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("generals")] List<ComposedGeneralDefinition>? Generals);

    private sealed record ComposedGeneralDefinition(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("portraitKey")] string? PortraitKey,
        [property: JsonPropertyName("skillIds")] List<string>? SkillIds,
        [property: JsonPropertyName("factionId")] string? FactionId);
}

public static class ComposedSkillContentRegistry
{
    /// <summary>Builds the current full local showcase plus the composed-skill mode.</summary>
    public static ContentRegistry CreateShowcase(bool includeNationalZhangJiao = true)
    {
        var packages = new List<IGameContentPackage>
        {
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new StandardTeamModePackage(),
            new StandardNationalWarLitePackage(),
            new StandardNationalWarAmbitiousPackage()
        };
        if (includeNationalZhangJiao)
            packages.Add(new StandardNationalZhangJiaoPackage());
        packages.Add(new ComposedSkillContentPackage());
        return ContentRegistry.Build(packages.ToArray());
    }
}
