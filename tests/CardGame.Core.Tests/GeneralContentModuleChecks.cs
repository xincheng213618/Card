using System.Reflection;
using System.Runtime.ExceptionServices;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class GeneralContentModuleChecks
{
    public static void DeclaredRosterAndCatalogPreserveRegistryBoundaries()
    {
        var declarationCount = 0;
        var module = CreateModule(builder =>
        {
            declarationCount++;
            builder.AddSkill(new ContentSkillDefinition("module:skill", "skill", "skill"));
            builder.AddGeneral(new ContentGeneralDefinition("module:first", "first", "first", "module:skill"));
            builder.AddGeneral(new ContentGeneralDefinition("module:second", "second", "second", "module:skill"));
        });
        var roster = (IEnumerable<string>)ModuleType.GetProperty("GeneralIds", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(module)!;
        Require(roster.SequenceEqual(["module:first", "module:second"]),
            "Pool membership and order must come from the actual general declarations.");

        var first = ContentRegistry.Build(new ModulePackage("module-first", module));
        var second = ContentRegistry.Build(new ModulePackage("module-second", module));
        Require(declarationCount == 1 && first.Generals.Keys.SequenceEqual(roster) &&
                second.Generals.Keys.SequenceEqual(roster) && !ReferenceEquals(first.Generals, second.Generals),
            "Compiled declarations must still enter a separately frozen registry on every registration.");
        RequireThrows(() => ContentRegistry.Build(new ModulePackage("module-first", module),
            new ModulePackage("module-second", module)));
        var brokenModule = CreateModule(builder => builder.AddGeneral(
            new ContentGeneralDefinition("module:broken", "broken", "broken", "module:missing")));
        RequireThrows(() => ContentRegistry.Build(new ModulePackage("module-broken", brokenModule)));

        var catalogType = typeof(StandardClassicGeneralPackage).Assembly.GetType(
            "CardGame.Content.Standard.EmbeddedSkillProgramCatalog", throwOnError: true)!;
        var registerBundle = catalogType.GetMethod("RegisterBundle", BindingFlags.Static | BindingFlags.NonPublic)!;
        var catalog = (SkillProgramCatalog)catalogType.GetMethod("Catalog", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, ["classic-ji-kang"])!;
        var definitions = new DefinitionCollector();
        Func<ContentSkillDefinition, ContentSkillDefinition> configure = definition => definition with
        {
            ExecutionForms = SkillExecutionForm.Trigger,
            ActionForms = SkillActionForm.None
        };
        Invoke(registerBundle, null, [definitions, "classic-ji-kang", configure, null]);
        Require(definitions.Skills.Select(skill => skill.Id).SequenceEqual(catalog.Programs.Keys) &&
                definitions.Skills.Any(skill => skill.Id == "classic:jixian") &&
                definitions.Skills.Any(skill => skill.Id == "classic:hexian"),
            "Granted skills must register from the catalog without a second manual membership list.");
        foreach (var definition in definitions.Skills)
        {
            var original = (ContentSkillDefinition)catalogType.GetMethod("Definition", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, ["classic-ji-kang", definition.Id])!;
            Require(ReferenceEquals(definition.Program, original.Program) &&
                    ReferenceEquals(definition.ProgramPresentation, original.ProgramPresentation) &&
                    Equals(definition.SelectionWeights, original.SelectionWeights) &&
                    Equals(definition.RevealWeights, original.RevealWeights),
                "Metadata configuration must retain the catalog's program, presentation and AI weights.");
        }

        var invalidOverrides = new DefinitionCollector();
        RequireThrows(() => Invoke(registerBundle, null,
            [invalidOverrides, "classic-ji-kang", configure, new Dictionary<string, SkillTag> { ["module:missing"] = SkillTag.Locked }]));
        Require(invalidOverrides.Skills.Count == 0, "An unknown metadata target must fail before partial registration.");
        Func<ContentSkillDefinition, ContentSkillDefinition> changeIdentity = definition => definition with { Id = "module:changed" };
        RequireThrows(() => Invoke(registerBundle, null, [new DefinitionCollector(), "classic-ji-kang", changeIdentity, null]));
    }

    private static Type ModuleType => typeof(StandardClassicGeneralPackage).Assembly.GetType(
        "CardGame.Content.Standard.GeneralContentModule", throwOnError: true)!;

    private static object CreateModule(Action<IContentRegistryBuilder> declare) =>
        ModuleType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single()
            .Invoke([declare, false, false, null]);

    private static void Invoke(MethodInfo method, object? target, object?[] arguments)
    {
        try { method.Invoke(target, arguments); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); }
    }

    private static void RequireThrows(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected registry or module validation to reject the declaration.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ModulePackage(string id, object module) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new(id, new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder) => Invoke(
            ModuleType.GetMethod("Register", BindingFlags.Instance | BindingFlags.NonPublic)!, module, [builder]);
    }

    private sealed class DefinitionCollector : IContentRegistryBuilder
    {
        internal List<ContentSkillDefinition> Skills { get; } = [];
        public void AddSkill(ContentSkillDefinition definition) => Skills.Add(definition);
        public void AddGeneral(ContentGeneralDefinition definition) => throw new InvalidOperationException("Unexpected general.");
        public void AddCard(ContentCardDefinition definition) => throw new InvalidOperationException("Unexpected card.");
        public void AddDeck(ContentDeckRecipe definition) => throw new InvalidOperationException("Unexpected deck.");
        public void AddMode(ContentModeDefinition definition) => throw new InvalidOperationException("Unexpected mode.");
    }
}
