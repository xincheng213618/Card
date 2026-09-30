using System.Text.Json.Nodes;
using System.Text.Json;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ProgramTriggerResourceChecks
{
    public static void TriggerWindowAndFrozenSuitContracts()
    {
        var sidi = Read("classic-cao-zhen");
        var han = Read("classic-han-hao-shi-huan");
        SkillProgramCatalog.Load(sidi.Rules, sidi.Presentation);
        SkillProgramCatalog.Load(han.Rules, han.Presentation);

        var wrongDiscard = JsonNode.Parse(han.Rules)!;
        var discardTrigger = wrongDiscard["skills"]![0]!["triggers"]![0]!;
        discardTrigger["window"] = "cardsGained";
        discardTrigger.AsObject().Remove("discardOwnerScope");
        discardTrigger.AsObject().Remove("movementDiscardOnly");
        discardTrigger.AsObject().Remove("cardCategories");
        discardTrigger.AsObject().Remove("suits");
        discardTrigger["destinationZones"] = new JsonArray("hand");
        Reject(wrongDiscard, han.Presentation, "requires trigger window DiscardPileReceived");

        var wrongPhase = JsonNode.Parse(sidi.Rules)!;
        wrongPhase["skills"]![0]!["triggers"]![0]!["window"] = "judgmentPhaseStarting";
        Reject(wrongPhase, sidi.Presentation, "requires trigger window PlayPhaseStarting");

        var unfrozen = JsonNode.Parse(sidi.Rules)!;
        unfrozen["skills"]![0]!["triggers"]![0]!["effects"]![0] = JsonNode.Parse(
            """{"op":"selectOwnedCards","target":"owner","zones":["hand"],"amount":1,"resultBind":"cost"}""");
        Reject(unfrozen, sidi.Presentation, "previously frozen single-card metadata binding");
        OptionalFactsPreserveLegacyJson();
    }

    private static void OptionalFactsPreserveLegacyJson()
    {
        const string id = "fixture:optional-facts";
        var rules = $$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{id}}","revision":1,"triggers":[{"id":"before","window":"beforeDamageApplied","subject":"damageTarget","optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]}]}""";
        const string presentation = """{"schemaVersion":3,"skills":{"fixture:optional-facts":{"name":"Facts","description":"Facts"}}}""";
        var program = SkillProgramCatalog.Load(rules, presentation).Programs[id];
        var registry = ContentRegistry.Build(new StandardContentPackage(), new FactsPackage(program));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 5, ModeId = "identity:standard-5", HumanSeat = 0,
            HumanRole = Role.Lord, UseInteractiveSetup = false, AdvanceAfterHumanCommands = false }, registry);
        if (!game.Submit(new StartGameCommand()).Accepted) throw new InvalidOperationException("Facts fixture failed to start.");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var players = (IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", flags)!.GetValue(game)!;
        var action = new CardActionContext(1, null, CardActionType.Use, 0, 0, null, null, 1,
            CardKind.Slash, [1], [], [], effectiveSuit: Suit.Spade);
        var capture = typeof(GameEngine).GetMethods(flags).Single(method => method.Name == "CaptureProgramTriggerFacts" && method.GetParameters().Length == 2);
        var facts = (SkillProgramTriggerFacts)capture.Invoke(game, [players[0], action])!;
        AssertOmitted(facts, "CardActionCardIsBlack");
        players[0].SkillGrants.Grant(new SkillGrant("fixture:facts", id, "fixture:facts-instance", "fixture:facts"));
        var stack = (List<ResolutionFrame>)typeof(GameEngine).GetField("_resolutionStack", flags)!.GetValue(game)!;
        stack.Add(new DamageFrame(1000, 999, 1, 0, 1));
        var before = typeof(GameEngine).GetMethod("TryBeginBeforeDamageProgramWindow", flags)!;
        if (!(bool)before.Invoke(game, [1, 0, 1, DamageNature.Normal, BeforeDamageProgramContinuation.Attack])!)
            throw new InvalidOperationException("Legacy before-damage fixture failed to pause.");
        var window = stack.OfType<BeforeDamageProgramWindowFrame>().Last();
        foreach (var candidate in window.Candidates) AssertOmitted(candidate.Facts, "DamageSourceGender");

        var invalid = JsonNode.Parse(rules)!;
        invalid["skills"]![0]!["triggers"]![0]!["window"] = "playPhaseStarting";
        invalid["skills"]![0]!["triggers"]![0]!["subject"] = "owner";
        invalid["skills"]![0]!["triggers"]![0]!["condition"] = JsonNode.Parse("""{"kind":"damageSourceGenderIs","gender":"male"}""");
        Reject(invalid, presentation, "requires a beforeDamageApplied trigger");
    }

    private static void AssertOmitted(SkillProgramTriggerFacts facts, string property)
    {
        if (JsonSerializer.Serialize(facts).Contains(property, StringComparison.Ordinal))
            throw new InvalidOperationException($"Legacy facts unexpectedly serialized {property} without an opt-in condition.");
    }

    private sealed class FactsPackage(SkillProgram program) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("optional-facts-test", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder) => builder.AddSkill(new ContentSkillDefinition(
            "fixture:optional-facts", "Facts", "Facts") { Program = program, ExecutionForms = SkillExecutionForm.Trigger });
    }

    private static void Reject(JsonNode rules, string presentation, string expected)
    {
        try { SkillProgramCatalog.Load(rules.ToJsonString(), presentation); }
        catch (InvalidOperationException error) when (error.Message.Contains(expected, StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException($"Invalid composition was accepted instead of rejecting: {expected}");
    }

    private static (string Rules, string Presentation) Read(string bundle)
    {
        var assembly = typeof(StandardContentPackage).Assembly;
        string Text(string suffix)
        {
            using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
                .Single(name => name.EndsWith(bundle + suffix, StringComparison.Ordinal)))!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        return (Text(".rules.json"), Text(".presentation.json"));
    }
}
