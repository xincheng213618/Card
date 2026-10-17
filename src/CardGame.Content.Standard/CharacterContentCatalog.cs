using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>Character folders own definitions; packages supply the shared cards, decks and modes.</summary>
internal sealed class CharacterContentCatalog
{
    private const string ResourcePrefix = "CardGame.Content.Standard.Characters.";
    private static readonly Lazy<CharacterContentCatalog> Embedded = new(
        () => Load(ReadEmbeddedSources()), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    private readonly IReadOnlyDictionary<string, PackageEntries> _packages;

    private CharacterContentCatalog(IReadOnlyDictionary<string, PackageEntries> packages) => _packages = packages;

    internal static CharacterContentCatalog Default => Embedded.Value;

    internal void Register(IContentRegistryBuilder builder, string packageId)
    {
        ArgumentNullException.ThrowIfNull(builder);
        // Resolve the whole package before the first write. A bad bundle cannot leave a partial registration.
        foreach (var definition in Package(packageId).Definitions.Value)
        {
            if (definition is ContentSkillDefinition skill) builder.AddSkill(skill);
            else builder.AddGeneral((ContentGeneralDefinition)definition);
        }
    }

    internal IReadOnlyList<string> GetPoolIds(string packageId, string pool)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pool);
        var entries = Package(packageId);
        return entries.Pools.TryGetValue(pool, out var ids) ? ids : System.Array.Empty<string>();
    }

    internal IReadOnlyList<ContentGeneralDefinition> GetPoolGenerals(string packageId, string pool)
    {
        var ids = GetPoolIds(packageId, pool).ToHashSet(StringComparer.Ordinal);
        return Package(packageId).Definitions.Value.OfType<ContentGeneralDefinition>().Where(g => ids.Contains(g.Id)).ToArray();
    }

    private PackageEntries Package(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        return _packages.TryGetValue(packageId, out var entries) ? entries :
            throw new InvalidOperationException($"No character content is declared for package '{packageId}'.");
    }

    internal static CharacterContentCatalog Load(IEnumerable<(string Name, string Json)> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var packages = new Dictionary<string, List<Declaration>>(StringComparer.Ordinal);
        var sourceNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, json) in sources)
        {
            if (string.IsNullOrWhiteSpace(name) || !sourceNames.Add(name) || string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("Character manifest sources require distinct names and non-empty JSON.");
            try
            {
                using var document = JsonDocument.Parse(json);
                RejectDuplicateProperties(document.RootElement);
                var root = Object(document.RootElement, "formatVersion", "packageId", "characterId", "skills", "generals");
                if (Integer(root, "formatVersion") != 1)
                    throw new InvalidOperationException("Unsupported character manifest formatVersion.");
                var packageId = Text(root, "packageId");
                var characterId = NullableText(root, "characterId");
                if (characterId is not null) ValidateCharacterId(characterId);
                var skillEntries = Array(root, "skills");
                var generalEntries = Array(root, "generals");
                if (generalEntries.GetArrayLength() > 1 ||
                    skillEntries.GetArrayLength() + generalEntries.GetArrayLength() == 0 ||
                    (generalEntries.GetArrayLength() != 0 && characterId is null))
                    throw new InvalidOperationException("A character manifest requires one general or a non-empty shared skill list.");
                if (!packages.TryGetValue(packageId, out var declarations))
                    packages.Add(packageId, declarations = []);
                foreach (var element in skillEntries.EnumerateArray())
                {
                    var entry = Object(element, "registrationOrder", "programBundle", "includeProgramPresentation", "definition");
                    var definitionElement = Required(entry, "definition");
                    Object(definitionElement, "id", "name", "description", "implementationStatus", "pendingImplementation",
                        "tags", "executionForms", "actionForms", "suppressionRule", "selectionWeights", "revealWeights",
                        "program", "programPresentation");
                    foreach (var property in new[] { "program", "programPresentation" })
                        if (definitionElement.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null)
                            throw new InvalidOperationException($"'{property}' must be bound through programBundle.");
                    var skill = definitionElement.Deserialize<ContentSkillDefinition>(JsonOptions) ??
                        throw new InvalidOperationException("A skill definition is required.");
                    ValidateSkill(skill);
                    var bundle = NullableText(entry, "programBundle");
                    var includePresentation = Boolean(entry, "includeProgramPresentation");
                    if (bundle is null && includePresentation)
                        throw new InvalidOperationException($"Skill '{skill.Id}' cannot include a presentation without a program bundle.");
                    if (bundle is not null && skill.ImplementationStatus == SkillImplementationStatus.Planned)
                        throw new InvalidOperationException($"Planned skill '{skill.Id}' cannot expose an executable program.");
                    declarations.Add(new Declaration(Ordinal(entry, "registrationOrder"), Freeze(skill), bundle,
                        includePresentation, null));
                }
                foreach (var element in generalEntries.EnumerateArray())
                {
                    var entry = Object(element, "registrationOrder", "pools", "definition");
                    var definitionElement = Required(entry, "definition");
                    Object(definitionElement, "id", "name", "portraitKey", "skillId", "factionId", "baseHp",
                        "additionalSkillIds", "gender", "characterId", "variantId", "rulesetId", "initialHp");
                    var general = definitionElement.Deserialize<ContentGeneralDefinition>(JsonOptions) ??
                        throw new InvalidOperationException("A general definition is required.");
                    ValidateGeneral(general);
                    // Legacy definitions intentionally retain null identity metadata in the content fingerprint.
                    if (general.CharacterId is not null &&
                        CanonicalCharacter(general.CharacterId) != CanonicalCharacter(characterId!))
                        throw new InvalidOperationException($"General '{general.Id}' does not belong to character '{characterId}'.");
                    var poolsElement = Required(entry, "pools");
                    if (poolsElement.ValueKind != JsonValueKind.Object)
                        throw new InvalidOperationException("General pools must be an object of non-negative ordinals.");
                    var pools = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (var pool in poolsElement.EnumerateObject())
                    {
                        if (string.IsNullOrWhiteSpace(pool.Name) || !pool.Value.TryGetInt32(out var order) || order < 0)
                            throw new InvalidOperationException("General pools require non-empty names and non-negative ordinals.");
                        pools.Add(pool.Name, order);
                    }
                    declarations.Add(new Declaration(Ordinal(entry, "registrationOrder"), Freeze(general), null,
                        false, new ReadOnlyDictionary<string, int>(pools)));
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
            {
                throw new InvalidOperationException($"Invalid character manifest '{name}': {exception.Message}", exception);
            }
        }
        if (packages.Count == 0) throw new InvalidOperationException("At least one character manifest is required.");
        var result = new Dictionary<string, PackageEntries>(StringComparer.Ordinal);
        foreach (var (packageId, declarations) in packages)
        {
            var ordered = declarations.OrderBy(entry => entry.Order).ToArray();
            if (ordered.Select(entry => entry.Order).Distinct().Count() != ordered.Length)
                throw new InvalidOperationException($"Package '{packageId}' has duplicate character registration ordinals.");
            if (ordered.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
                throw new InvalidOperationException($"Package '{packageId}' has duplicate character content ids.");
            var pools = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var pool in ordered.Where(entry => entry.Pools is not null)
                         .SelectMany(entry => entry.Pools!.Select(pair => (Name: pair.Key, Order: pair.Value, entry.Id)))
                         .GroupBy(entry => entry.Name, StringComparer.Ordinal))
            {
                if (pool.Select(entry => entry.Order).Distinct().Count() != pool.Count())
                    throw new InvalidOperationException($"Package '{packageId}' has duplicate ordinals in pool '{pool.Key}'.");
                pools.Add(pool.Key, System.Array.AsReadOnly(pool.OrderBy(entry => entry.Order).Select(entry => entry.Id).ToArray()));
            }
            result.Add(packageId, new PackageEntries(new Lazy<IReadOnlyList<object>>(
                () => System.Array.AsReadOnly(ordered.Select(Bind).ToArray()), LazyThreadSafetyMode.ExecutionAndPublication),
                new ReadOnlyDictionary<string, IReadOnlyList<string>>(pools)));
        }
        return new CharacterContentCatalog(new ReadOnlyDictionary<string, PackageEntries>(result));
    }

    private static object Bind(Declaration declaration)
    {
        if (declaration.Definition is not ContentSkillDefinition skill || declaration.Bundle is null)
            return declaration.Definition;
        var catalog = EmbeddedSkillProgramCatalog.Catalog(declaration.Bundle);
        if (!catalog.Programs.TryGetValue(skill.Id, out var program) ||
            !catalog.Presentations.TryGetValue(skill.Id, out var presentation))
            throw new InvalidOperationException($"Skill-program bundle '{declaration.Bundle}' does not define paired skill '{skill.Id}'.");
        return skill with { Program = program, ProgramPresentation = declaration.IncludePresentation ? presentation : null };
    }

    private static IEnumerable<(string Name, string Json)> ReadEmbeddedSources()
    {
        var assembly = typeof(CharacterContentCatalog).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name) ??
                throw new InvalidOperationException($"Missing character manifest resource '{name}'.");
            using var reader = new StreamReader(stream);
            yield return (name, reader.ReadToEnd());
        }
    }

    private static ContentSkillDefinition Freeze(ContentSkillDefinition skill) => skill.SelectionWeights is null ? skill :
        skill with { SelectionWeights = new ReadOnlyDictionary<Role, double>(new Dictionary<Role, double>(skill.SelectionWeights)) };
    private static ContentGeneralDefinition Freeze(ContentGeneralDefinition general) => general with
    {
        AdditionalSkillIds = general.AdditionalSkillIds is null ? null : System.Array.AsReadOnly(general.AdditionalSkillIds.ToArray())
    };

    private static void ValidateSkill(ContentSkillDefinition skill)
    {
        ValidateId(skill.Id);
        ValidateText(skill.Name, "Skill name");
        ValidateText(skill.Description, "Skill description");
        const SkillTag tags = SkillTag.Lord | SkillTag.Locked | SkillTag.Limited | SkillTag.Awakening | SkillTag.Conversion;
        if (!Enum.IsDefined(skill.ImplementationStatus) || (skill.Tags & ~tags) != 0 ||
            (skill.ExecutionForms & ~(SkillExecutionForm.State | SkillExecutionForm.Trigger)) != 0 ||
            (skill.ActionForms & ~SkillActionForm.Active) != 0 ||
            (skill.ImplementationStatus != SkillImplementationStatus.Complete && string.IsNullOrWhiteSpace(skill.PendingImplementation)))
            throw new InvalidOperationException($"Skill '{skill.Id}' has invalid implementation metadata.");
        if (skill.SelectionWeights is { } weights && weights.Any(pair => !Enum.IsDefined(pair.Key) || !double.IsFinite(pair.Value)))
            throw new InvalidOperationException($"Skill '{skill.Id}' has invalid selection weights.");
        if (skill.RevealWeights is { } reveal &&
            new[] { reveal.Base, reveal.Wounded, reveal.EmptyHand, reveal.RepeatedSlash }.Any(value => !double.IsFinite(value)))
            throw new InvalidOperationException($"Skill '{skill.Id}' has invalid reveal weights.");
    }

    private static void ValidateGeneral(ContentGeneralDefinition general)
    {
        ValidateId(general.Id);
        ValidateText(general.Name, "General name");
        ValidateText(general.PortraitKey, "General portraitKey");
        if (!Enum.IsDefined(general.Gender) || general.BaseHp is < 1 or > 20 ||
            (general.InitialHp is { } hp && (hp < 1 || hp > general.BaseHp)))
            throw new InvalidOperationException($"General '{general.Id}' has invalid gender or HP.");
        foreach (var id in general.SkillIds) ValidateId(id);
        if (general.SkillIds.Distinct(StringComparer.Ordinal).Count() != general.SkillIds.Count)
            throw new InvalidOperationException($"General '{general.Id}' requires distinct skill ids.");
        if (general.CharacterId is not null) ValidateCharacterId(general.CharacterId);
        if (general.FactionId is not null) ValidateText(general.FactionId, "General factionId");
        if (general.VariantId is not null) ValidateText(general.VariantId, "General variantId");
        if (general.RulesetId is not null) ValidateText(general.RulesetId, "General rulesetId");
    }

    private static void ValidateId(string? id)
    {
        ValidateText(id, "Content id");
        var separator = id!.IndexOf(':');
        if (separator <= 0 || separator == id.Length - 1)
            throw new InvalidOperationException($"Content id '{id}' requires a namespace and a local name.");
    }

    private static void ValidateCharacterId(string id)
    {
        ValidateId(id);
        if (!id.StartsWith("character:", StringComparison.Ordinal))
            throw new InvalidOperationException($"Invalid character id '{id}'.");
    }

    private static string CanonicalCharacter(string id) => id switch
    {
        "character:gao-da-yi-hao" => "character:zhao-yun",
        "character:fu-huanghou" => "character:fu-huang-hou",
        "character:wolong-zhuge-liang" => "character:zhuge-liang",
        _ => id
    };

    private static void ValidateText(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException($"{label} is required.");
    }

    private static JsonElement Object(JsonElement value, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("A JSON object is required.");
        foreach (var property in value.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
                throw new InvalidOperationException($"Unknown character manifest property '{property.Name}'.");
        return value;
    }

    private static JsonElement Required(JsonElement value, string name) => value.TryGetProperty(name, out var property) ? property :
        throw new InvalidOperationException($"Character manifest property '{name}' is required.");
    private static JsonElement Array(JsonElement value, string name)
    {
        var array = Required(value, name);
        return array.ValueKind == JsonValueKind.Array ? array : throw new InvalidOperationException($"'{name}' must be an array.");
    }
    private static int Integer(JsonElement value, string name) => Required(value, name).TryGetInt32(out var number) ? number :
        throw new InvalidOperationException($"'{name}' must be an integer.");
    private static int Ordinal(JsonElement value, string name) => Integer(value, name) is var number && number >= 0 ? number :
        throw new InvalidOperationException($"'{name}' must be non-negative.");
    private static bool Boolean(JsonElement value, string name) => Required(value, name).ValueKind switch
    {
        JsonValueKind.True => true, JsonValueKind.False => false,
        _ => throw new InvalidOperationException($"'{name}' must be a boolean.")
    };
    private static string Text(JsonElement value, string name) => NullableText(value, name) ??
        throw new InvalidOperationException($"'{name}' must be a non-empty string.");
    private static string? NullableText(JsonElement value, string name)
    {
        var property = Required(value, name);
        if (property.ValueKind == JsonValueKind.Null) return null;
        if (property.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.GetString())) return property.GetString();
        throw new InvalidOperationException($"'{name}' must be a non-empty string or null.");
    }

    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidOperationException($"Duplicate JSON property '{property.Name}'.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var element in value.EnumerateArray()) RejectDuplicateProperties(element);
    }

    private sealed record Declaration(int Order, object Definition, string? Bundle, bool IncludePresentation,
        IReadOnlyDictionary<string, int>? Pools)
    {
        internal string Id => Definition is ContentSkillDefinition skill ? skill.Id : ((ContentGeneralDefinition)Definition).Id;
    }
    private sealed record PackageEntries(Lazy<IReadOnlyList<object>> Definitions,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Pools);
}
