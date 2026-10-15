using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CharacterContentCatalogChecks
{
    public static void OrderedRegistrationAndPackageIsolation()
    {
        var first = Document("fixture", "character:first", [Skill(0, "fixture:first-skill")],
            [General(3, "fixture:first", "fixture:first-skill", 2)]);
        var second = Document("fixture", "character:second", [Skill(2, "fixture:second-skill")],
            [General(1, "fixture:second", "fixture:second-skill", 0)]);
        var catalog = Load(("second", second.ToJsonString()), ("first", first.ToJsonString()));
        var forward = Load(("first", first.ToJsonString()), ("second", second.ToJsonString()));
        var declarations = Collect(catalog, "fixture");
        Require(declarations.Order.SequenceEqual([
                "fixture:first-skill", "fixture:second", "fixture:second-skill", "fixture:first"]) &&
                declarations.Order.SequenceEqual(Collect(forward, "fixture").Order),
            "Registration must preserve explicit cross-kind ordinals independently of resource discovery order.");
        Require(Pool(catalog, "fixture", "current").SequenceEqual(["fixture:second", "fixture:first"]) &&
                Pool(catalog, "fixture", "absent").Count == 0,
            "Pool order must come from its own ordinals, including gaps and intentionally empty pools.");
        Require(declarations.Generals.All(general => general.CharacterId is null && general.VariantId is null && general.RulesetId is null),
            "Folder identity must not fill legacy null content metadata.");
        var a = ContentRegistry.Build(new ManifestPackage(catalog, "fixture"));
        var b = ContentRegistry.Build(new ManifestPackage(forward, "fixture"));
        Require(a.ContentHash == b.ContentHash && !ReferenceEquals(a.Generals, b.Generals),
            "Each registration must enter an independently frozen registry with the same fingerprint.");

        var otherPackage = (JsonObject)first.DeepClone();
        otherPackage["packageId"] = "other-package";
        var separate = Load(("first", first.ToJsonString()), ("other", otherPackage.ToJsonString()));
        Require(Collect(separate, "fixture").Order.SequenceEqual(Collect(separate, "other-package").Order),
            "Ids and ordinals in different package namespaces must remain independent.");
        RequireThrows(() => Collect(separate, "missing"));

        // Discover the production documents without relying on a fixed roster size or a second central membership list.
        var assembly = typeof(StandardClassicGeneralPackage).Assembly;
        var sources = assembly.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);
                return (Name: name, Json: reader.ReadToEnd());
            }).ToArray();
        Require(sources.Length > 0, "Character folder manifests must be embedded in the content assembly.");
        var reversed = Load(sources.Reverse().ToArray());
        var embedded = CatalogType.GetProperty("Default", Static)!.GetValue(null)!;
        var expected = new Dictionary<string, List<(int Order, string Id)>>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            using var document = JsonDocument.Parse(source.Json);
            var root = document.RootElement;
            var packageId = root.GetProperty("packageId").GetString()!;
            if (!expected.TryGetValue(packageId, out var entries)) expected.Add(packageId, entries = []);
            foreach (var entry in root.GetProperty("skills").EnumerateArray().Concat(root.GetProperty("generals").EnumerateArray()))
                entries.Add((entry.GetProperty("registrationOrder").GetInt32(), entry.GetProperty("definition").GetProperty("id").GetString()!));
        }
        var showcase = ComposedSkillContentRegistry.CreateShowcase();
        var registeredPackages = showcase.Packages.Select(package => package.Id).ToHashSet(StringComparer.Ordinal);
        Require(expected.Keys.All(registeredPackages.Contains),
            "Every embedded character package must be included by the built-in showcase; a misspelled package cannot silently lose its characters.");
        foreach (var (packageId, entries) in expected)
        {
            var ids = entries.OrderBy(entry => entry.Order).Select(entry => entry.Id).ToArray();
            Require(Collect(embedded, packageId).Order.SequenceEqual(ids) && Collect(reversed, packageId).Order.SequenceEqual(ids),
                $"Production package '{packageId}' must load every character declaration in its original order.");
            var actual = Collect(embedded, packageId);
            Require(actual.Skills.All(skill => showcase.Skills.ContainsKey(skill.Id)) &&
                    actual.Generals.All(general => showcase.Generals.ContainsKey(general.Id)),
                $"Production package '{packageId}' must register all embedded character skills and generals into the actual showcase.");
        }
    }

    public static void ProgramBindingsAndInvalidInput()
    {
        var plain = Skill(0, "classic:qingxian", "classic-ji-kang", false);
        var shown = Skill(1, "classic:juexiang", "classic-ji-kang", true);
        var plainDefinition = (JsonObject)plain["definition"]!;
        plainDefinition["name"] = "Explicit name";
        plainDefinition["description"] = "Explicit description";
        plainDefinition["tags"] = "Locked, Limited";
        plainDefinition["executionForms"] = "State, Trigger";
        plainDefinition["actionForms"] = "Active";
        plainDefinition["suppressionRule"] = new JsonObject { ["ownerHpEquals"] = 2 };
        plainDefinition["selectionWeights"] = new JsonObject { ["Lord"] = -1.25 };
        plainDefinition["revealWeights"] = new JsonObject { ["base"] = 7, ["wounded"] = 2 };
        var general = General(2, "fixture:fu-huanghou", "classic:qingxian", 0);
        var generalDefinition = (JsonObject)general["definition"]!;
        generalDefinition["characterId"] = "character:fu-huanghou";
        generalDefinition["additionalSkillIds"] = new JsonArray("classic:juexiang");
        generalDefinition["baseHp"] = 3;
        generalDefinition["initialHp"] = 2;
        var document = Document("fixture", "character:fu-huang-hou", [plain, shown], [general]);
        var catalog = Load(("programs", document.ToJsonString()));
        var definitions = Collect(catalog, "fixture");
        var programCatalog = (SkillProgramCatalog)Invoke(EmbeddedCatalogType.GetMethod("Catalog", Static)!, null, ["classic-ji-kang"])!;
        var skill = definitions.Skills[0];
        Require(ReferenceEquals(skill.Program, programCatalog.Programs[skill.Id]) && skill.ProgramPresentation is null &&
                ReferenceEquals(definitions.Skills[1].ProgramPresentation, programCatalog.Presentations["classic:juexiang"]),
            "Program identity and the explicit presence or absence of presentation must survive folder loading.");
        Require(skill.Name == "Explicit name" && skill.Description == "Explicit description" &&
                skill.Tags == (SkillTag.Locked | SkillTag.Limited) &&
                skill.ExecutionForms == (SkillExecutionForm.State | SkillExecutionForm.Trigger) &&
                skill.ActionForms == SkillActionForm.Active && skill.SuppressionRule?.OwnerHpEquals == 2 &&
                skill.SelectionWeights![Role.Lord] == -1.25 && skill.RevealWeights?.Base == 7 &&
                definitions.Skills[1].SelectionWeights is null && definitions.Skills[1].RevealWeights is null,
            "Definition metadata must deserialize directly without injecting selection or reveal defaults.");
        Require(definitions.Generals[0].InitialHp == 2 && definitions.Generals[0].CharacterId == "character:fu-huanghou",
            "Initial HP and historical alias metadata must remain unchanged.");
        RequireMutationRejected(() => ((IDictionary<Role, double>)skill.SelectionWeights!)[Role.Lord] = 99);
        RequireMutationRejected(() => ((IList<string>)definitions.Generals[0].AdditionalSkillIds!)[0] = "fixture:changed");
        RequireMutationRejected(() => ((IList<string>)Pool(catalog, "fixture", "current"))[0] = "fixture:changed");
        Require(Collect(catalog, "fixture").Skills[0].SelectionWeights![Role.Lord] == -1.25,
            "A caller must not mutate cached definitions through collection fields.");

        var valid = Document("fixture", "character:first", [Skill(0, "fixture:skill")],
            [General(1, "fixture:first", "fixture:skill", 0)]);
        foreach (var mutate in new Action<JsonObject>[]
        {
            value => value["formatVersion"] = 2,
            value => value["unknown"] = true,
            value => value["skills"] = null,
            value => value.Remove("generals"),
            value => value["characterId"] = null,
            value => value["skills"]![0]!["registrationOrder"] = -1,
            value => value["generals"]![0]!["registrationOrder"] = 0,
            value => value["generals"]![0]!["pools"]!["current"] = -1,
            value => value["skills"]![0]!["includeProgramPresentation"] = true,
            value => value["skills"]![0]!["definition"]!["program"] = new JsonObject(),
            value => value["skills"]![0]!["definition"]!["programPresentation"] = new JsonObject(),
            value => value["skills"]![0]!["definition"]!["extra"] = true,
            value => value["skills"]![0]!["definition"]!["name"] = null,
            value => value["skills"]![0]!["definition"]!["implementationStatus"] = "Unknown",
            value => value["skills"]![0]!["definition"]!["tags"] = 1,
            value => value["generals"]![0]!["definition"]!["baseHp"] = 0,
            value => value["generals"]![0]!["definition"]!["initialHp"] = 5,
            value => value["generals"]![0]!["definition"]!["gender"] = "Unknown",
            value => value["generals"]![0]!["definition"]!["characterId"] = "character:another",
            value => value["generals"]![0]!["definition"]!["additionalSkillIds"] = new JsonArray("fixture:skill"),
            value => value["generals"]![0]!["definition"]!["skillIds"] = new JsonArray("fixture:skill")
        })
        {
            var invalid = (JsonObject)valid.DeepClone();
            mutate(invalid);
            RequireThrows(() => Load(("invalid", invalid.ToJsonString())));
        }
        RequireThrows(() => Load(("duplicate-properties", valid.ToJsonString().Replace("\"formatVersion\":1", "\"formatVersion\":1,\"formatVersion\":1", StringComparison.Ordinal))));
        RequireThrows(() => Load(("null", "null")));
        RequireThrows(() => Load(("empty", "")));
        RequireThrows(() => Load());
        RequireThrows(() => Load(("same-name", valid.ToJsonString()), ("same-name", valid.ToJsonString())));

        var duplicateOrder = Document("fixture", null, [Skill(0, "fixture:other")], []);
        RequireThrows(() => Load(("first", valid.ToJsonString()), ("duplicate-order", duplicateOrder.ToJsonString())));
        var duplicateId = Document("fixture", null, [Skill(2, "fixture:first")], []);
        RequireThrows(() => Load(("first", valid.ToJsonString()), ("duplicate-cross-kind-id", duplicateId.ToJsonString())));
        var duplicatePool = Document("fixture", "character:other", [], [General(2, "fixture:other", "fixture:skill", 0)]);
        RequireThrows(() => Load(("first", valid.ToJsonString()), ("duplicate-pool", duplicatePool.ToJsonString())));

        foreach (var (bundle, id) in new[] { ("missing-bundle", "fixture:missing"), ("classic-ji-kang", "fixture:missing") })
        {
            var bad = Document("fixture", null, [Skill(0, "fixture:before"), Skill(1, id, bundle, true)], []);
            var broken = Load(("bad-bundle", bad.ToJsonString()));
            var collector = new DefinitionCollector();
            RequireThrows(() => Register(broken, collector, "fixture"));
            Require(collector.Order.Count == 0, "A missing bundle or paired skill must fail before any builder mutation.");
        }
        var otherBrokenPackage = Document("broken-package", null, [Skill(0, "fixture:missing", "missing-bundle", true)], []);
        var isolated = Load(("valid", valid.ToJsonString()), ("broken", otherBrokenPackage.ToJsonString()));
        Require(Collect(isolated, "fixture").Order.Count == 2,
            "Registering one package must not bind an unrelated package's programs.");
        var marker = new MarkerException();
        try { Register(catalog, new ThrowingBuilder(marker), "fixture"); }
        catch (MarkerException exception) { Require(ReferenceEquals(exception, marker), "Builder failures must propagate without wrapping."); return; }
        throw new InvalidOperationException("Expected the builder's original exception.");
    }

    private const string ResourcePrefix = "CardGame.Content.Standard.Characters.";
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static Type CatalogType => typeof(StandardClassicGeneralPackage).Assembly.GetType("CardGame.Content.Standard.CharacterContentCatalog", true)!;
    private static Type EmbeddedCatalogType => typeof(StandardClassicGeneralPackage).Assembly.GetType("CardGame.Content.Standard.EmbeddedSkillProgramCatalog", true)!;
    private static object Load(params (string Name, string Json)[] sources) => Invoke(CatalogType.GetMethod("Load", Static)!, null, [sources])!;
    private static void Register(object catalog, IContentRegistryBuilder builder, string packageId) =>
        Invoke(CatalogType.GetMethod("Register", Instance)!, catalog, [builder, packageId]);
    private static IReadOnlyList<string> Pool(object catalog, string packageId, string pool) =>
        (IReadOnlyList<string>)Invoke(CatalogType.GetMethod("GetPoolIds", Instance)!, catalog, [packageId, pool])!;
    private static DefinitionCollector Collect(object catalog, string packageId)
    {
        var collector = new DefinitionCollector();
        Register(catalog, collector, packageId);
        return collector;
    }
    private static object? Invoke(MethodInfo method, object? target, object?[] arguments)
    {
        try { return method.Invoke(target, arguments); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
    }

    private static JsonObject Document(string packageId, string? characterId, JsonObject[] skills, JsonObject[] generals) => new()
    {
        ["formatVersion"] = 1, ["packageId"] = packageId, ["characterId"] = characterId,
        ["skills"] = new JsonArray(skills.Select(value => value.DeepClone()).ToArray()),
        ["generals"] = new JsonArray(generals.Select(value => value.DeepClone()).ToArray())
    };
    private static JsonObject Skill(int order, string id, string? bundle = null, bool includePresentation = false) => new()
    {
        ["registrationOrder"] = order, ["programBundle"] = bundle, ["includeProgramPresentation"] = includePresentation,
        ["definition"] = new JsonObject
        {
            ["id"] = id, ["name"] = id, ["description"] = "Skill definition",
            ["implementationStatus"] = "Complete", ["pendingImplementation"] = null,
            ["tags"] = "None", ["executionForms"] = "None", ["actionForms"] = "None",
            ["suppressionRule"] = null, ["selectionWeights"] = null, ["revealWeights"] = null
        }
    };
    private static JsonObject General(int order, string id, string skillId, int poolOrder) => new()
    {
        ["registrationOrder"] = order, ["pools"] = new JsonObject { ["current"] = poolOrder },
        ["definition"] = new JsonObject
        {
            ["id"] = id, ["name"] = id, ["portraitKey"] = id, ["skillId"] = skillId,
            ["factionId"] = "wei", ["baseHp"] = 4, ["additionalSkillIds"] = null,
            ["gender"] = "Male", ["characterId"] = null, ["variantId"] = null, ["rulesetId"] = null, ["initialHp"] = null
        }
    };

    private static void RequireThrows(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected character manifest validation to reject the input.");
    }
    private static void RequireMutationRejected(Action action)
    {
        try { action(); }
        catch (NotSupportedException) { return; }
        throw new InvalidOperationException("Cached character collections must reject mutation.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private sealed class ManifestPackage(object catalog, string packageId) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new(packageId, new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder) => CharacterContentCatalogChecks.Register(catalog, builder, packageId);
    }
    private sealed class DefinitionCollector : IContentRegistryBuilder
    {
        internal List<string> Order { get; } = [];
        internal List<ContentSkillDefinition> Skills { get; } = [];
        internal List<ContentGeneralDefinition> Generals { get; } = [];
        public void AddSkill(ContentSkillDefinition definition) { Skills.Add(definition); Order.Add(definition.Id); }
        public void AddGeneral(ContentGeneralDefinition definition) { Generals.Add(definition); Order.Add(definition.Id); }
        public void AddCard(ContentCardDefinition definition) => throw new InvalidOperationException("Unexpected card.");
        public void AddDeck(ContentDeckRecipe definition) => throw new InvalidOperationException("Unexpected deck.");
        public void AddMode(ContentModeDefinition definition) => throw new InvalidOperationException("Unexpected mode.");
    }
    private sealed class MarkerException : Exception;
    private sealed class ThrowingBuilder(Exception exception) : IContentRegistryBuilder
    {
        public void AddSkill(ContentSkillDefinition definition) => throw exception;
        public void AddGeneral(ContentGeneralDefinition definition) => throw exception;
        public void AddCard(ContentCardDefinition definition) => throw exception;
        public void AddDeck(ContentDeckRecipe definition) => throw exception;
        public void AddMode(ContentModeDefinition definition) => throw exception;
    }
}
