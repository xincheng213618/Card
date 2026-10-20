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
    private const string GeneralsResource = "CardGame.Content.Standard.SkillPrograms.composed-generals.json";
    private static SkillProgramCatalog Catalog =>
        EmbeddedSkillProgramCatalog.Catalog("composed-skills");
    private static readonly Lazy<IReadOnlyList<ComposedGeneralDefinition>> Generals = new(LoadGenerals);

    public PackageManifest Manifest { get; } = new(
        "standard-composed-skills",
        new Version(1, 0, 0),
        [new PackageDependency("standard", new Version(1, 11, 0))]);

    public void Register(IContentRegistryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        foreach (var (id, program) in Catalog.Programs.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var presentation = Catalog.Presentations[id];
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
    public static ContentRegistry CreateShowcase(bool includeNationalZhangJiao = true, bool includeActivities = false,
        bool includeBossActivities = false, bool historicalSelectionWeights = false, bool includeHulaoActivities = false,
        bool includeShenwuActivities = false, bool historicalGeneralSelectionPools = false, bool includeNianActivities = false,
        bool includeHellActivities = false, bool includeWenheActivities = false, bool includeDragonBoatActivities = false,
        bool includeGuanduActivities = false,
        IEnumerable<IGameContentPackage>? additionalPackages = null)
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
        if (includeActivities) packages.Add(new StandardJiangeActivityPackage());
        if (includeBossActivities) packages.Add(new StandardGhostActivityPackage());
        if (includeHulaoActivities) packages.Add(new StandardHulaoActivityPackage());
        if (includeShenwuActivities)
        {
            packages.Add(new StandardShenwuActivityPackage());
            packages.Add(new StandardShenwu2020ActivityPackage());
        }
        if (includeNianActivities) packages.Add(new StandardNianActivityPackage());
        if (includeHellActivities) packages.Add(new StandardHellActivityPackage());
        if (includeWenheActivities) packages.Add(new StandardWenheActivityPackage());
        if (includeDragonBoatActivities) packages.Add(new StandardDragonBoatActivityPackage());
        if (includeGuanduActivities) packages.Add(new StandardGuanduActivityPackage());
        if (additionalPackages is not null) packages.AddRange(additionalPackages);
        return ContentRegistry.Build((historicalSelectionWeights
            ? packages.Select(HistoricalGeneralSelectionContent.Wrap)
            : historicalGeneralSelectionPools ? packages.Select(HistoricalGeneralSelectionContent.RestoreSelectionPools) : packages).ToArray());
    }

    /// <summary>Chooses matching historical AI metadata while retaining strict replay fingerprint validation.</summary>
    public static ContentRegistry CreateShowcaseForCheckpoint(GameCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(checkpoint.ContentPackages);
        ArgumentNullException.ThrowIfNull(checkpoint.Options);
        if (checkpoint.Options.ShanheBattle is { } shanhe)
            return StandardShanheCampaignCatalog.CreateBattleRegistry(shanhe);
        bool Includes(IGameContentPackage package) => checkpoint.ContentPackages.Contains(
            $"{package.Manifest.Id}@{package.Manifest.Version}", StringComparer.Ordinal);
        var national = Includes(new StandardNationalZhangJiaoPackage());
        var activities = Includes(new StandardJiangeActivityPackage());
        var bosses = Includes(new StandardGhostActivityPackage());
        var hulao = Includes(new StandardHulaoActivityPackage());
        var shenwu = Includes(new StandardShenwuActivityPackage());
        var nian = Includes(new StandardNianActivityPackage());
        var hell = Includes(new StandardHellActivityPackage());
        var wenhe = Includes(new StandardWenheActivityPackage());
        var dragonBoat = Includes(new StandardDragonBoatActivityPackage());
        var guandu = Includes(new StandardGuanduActivityPackage());
        var current = CreateShowcase(national, activities, bosses, includeHulaoActivities: hulao, includeShenwuActivities: shenwu, includeNianActivities: nian, includeHellActivities: hell, includeWenheActivities: wenhe, includeDragonBoatActivities: dragonBoat, includeGuanduActivities: guandu);
        if (current.ContentHash == checkpoint.ContentHash) return current;
        var previousPools = CreateShowcase(national, activities, bosses, includeHulaoActivities: hulao,
            includeShenwuActivities: shenwu, historicalGeneralSelectionPools: true, includeNianActivities: nian, includeHellActivities: hell, includeWenheActivities: wenhe, includeDragonBoatActivities: dragonBoat, includeGuanduActivities: guandu);
        if (previousPools.ContentHash == checkpoint.ContentHash) return previousPools;
        if (checkpoint.Options.AiPolicyVersion >= AiDecisionPolicy.RuleBasedGeneralSelectionVersion) return current;
        var historical = CreateShowcase(national, activities, bosses, historicalSelectionWeights: true, includeHulaoActivities: hulao, includeShenwuActivities: shenwu, includeNianActivities: nian, includeHellActivities: hell, includeWenheActivities: wenhe, includeDragonBoatActivities: dragonBoat, includeGuanduActivities: guandu);
        return historical.ContentHash == checkpoint.ContentHash ? historical : current;
    }

}
