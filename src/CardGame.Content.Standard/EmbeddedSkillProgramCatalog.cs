using System.Collections.Concurrent;
using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>
/// Loads one embedded rules/presentation pair as one catalog. Bundle identity remains
/// part of every lookup so equal skill ids in historical or parallel bundles never merge.
/// </summary>
internal static class EmbeddedSkillProgramCatalog
{
    private const string ResourcePrefix = "CardGame.Content.Standard.SkillPrograms.";
    private static readonly ConcurrentDictionary<string, Lazy<SkillProgramCatalog>> Catalogs =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Registers every paired definition in the declared bundle, including granted
    /// and upgraded skills that are not printed on a general's initial skill list.
    /// Metadata remains explicit at the caller; ids come from the catalog itself.
    /// </summary>
    internal static void RegisterBundle(
        IContentRegistryBuilder builder,
        string bundleResourceName,
        Func<ContentSkillDefinition, ContentSkillDefinition> configure,
        IReadOnlyDictionary<string, SkillTag>? tagOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        var catalog = Catalog(bundleResourceName);
        if (tagOverrides is not null)
        {
            foreach (var id in tagOverrides.Keys)
                if (!catalog.Programs.ContainsKey(id))
                    throw new InvalidOperationException(
                        $"Skill-program bundle '{bundleResourceName}' has no metadata override target '{id}'.");
        }

        foreach (var id in catalog.Programs.Keys)
        {
            var definition = Definition(bundleResourceName, id);
            if (tagOverrides is not null && tagOverrides.TryGetValue(id, out var tags))
                definition = definition with { Tags = tags };
            var configured = configure(definition);
            if (configured.Id != id)
                throw new InvalidOperationException("Bundle metadata cannot change a skill identity.");
            builder.AddSkill(configured);
        }
    }

    internal static ContentSkillDefinition Definition(string bundleResourceName, string skillId)
    {
        if (string.IsNullOrWhiteSpace(bundleResourceName))
            throw new ArgumentException("A skill-program bundle resource name is required.", nameof(bundleResourceName));
        if (string.IsNullOrWhiteSpace(skillId))
            throw new ArgumentException("A skill id is required.", nameof(skillId));

        var catalog = Catalog(bundleResourceName);
        if (!catalog.Programs.TryGetValue(skillId, out var program) ||
            !catalog.Presentations.TryGetValue(skillId, out var presentation))
            throw new InvalidOperationException(
                $"Skill-program bundle '{bundleResourceName}' does not define paired skill '{skillId}'.");
        return new ContentSkillDefinition(skillId, presentation.Name, presentation.Description)
        {
            Program = program,
            ProgramPresentation = presentation,
            SelectionWeights = SkillSelectionPreferences.For(skillId),
            RevealWeights = SkillSelectionPreferences.RevealFor(skillId)
        };
    }

    internal static SkillProgramCatalog Catalog(string bundleResourceName)
    {
        if (string.IsNullOrWhiteSpace(bundleResourceName))
            throw new ArgumentException("A skill-program bundle resource name is required.", nameof(bundleResourceName));

        return Catalogs.GetOrAdd(bundleResourceName, static name =>
            new Lazy<SkillProgramCatalog>(() => Load(name), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static SkillProgramCatalog Load(string bundleResourceName)
    {
        var assembly = typeof(EmbeddedSkillProgramCatalog).Assembly;
        var rulesName = ResourcePrefix + bundleResourceName + ".rules.json";
        var presentationName = ResourcePrefix + bundleResourceName + ".presentation.json";
        using var rulesStream = assembly.GetManifestResourceStream(rulesName);
        using var presentationStream = assembly.GetManifestResourceStream(presentationName);
        if (rulesStream is null || presentationStream is null)
            throw new InvalidOperationException(
                $"Missing embedded skill-program resource pair '{rulesName}' and '{presentationName}'.");
        using var rulesReader = new StreamReader(rulesStream);
        using var presentationReader = new StreamReader(presentationStream);
        return SkillProgramCatalog.Load(rulesReader.ReadToEnd(), presentationReader.ReadToEnd());
    }
}
