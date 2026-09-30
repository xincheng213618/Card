using CardGame.Content.Standard;
using CardGame.Core;

internal static class OlGodSharedChecks
{
    private const string Driver = "fixture:god-shared-driver";
    private const string General = "fixture:god-shared-owner";
    private const string Mode = "identity:classic-god-shared";

    public static void MarkerCostsAllHandBonusAndReplay()
    {
        var (game, registry) = Create("jilue");
        Require(!game.GetHumanLegalActions().Any(action => action.ProgramSkillId == "ol:jilue"),
            "An unaffordable marker payment must not advertise an action.");
        Use(game, Driver, "markers");
        Require(game.GetHumanLegalActions().Any(action => action.ProgramSkillId == "ol:jilue" && action.Description.Contains("制衡")) &&
                game.GetHumanLegalActions().Any(action => action.ProgramSkillId == "ol:jilue" && action.Description.Contains("完杀")),
            "Different activations of one parent skill must have distinct presentation labels.");
        var hand = Hand(game).Select(card => card.Id).ToArray();
        var before = game.Revision;
        var rejected = game.Submit(new UseProgramSkillCommand(0, "ol:jilue", "exchange-owned-cards",
            [-123], [], game.Revision, game.PendingDecision!.PromptId));
        Require(!rejected.Accepted && game.Revision == before && Ren(game) == 3,
            "Rejected selection must not pay markers or mutate the accepted boundary.");
        Use(game, "ol:jilue", "exchange-owned-cards", hand);
        Require(Hand(game).Count == hand.Length + 1 && Ren(game) == 2,
            "Selecting every hand card must pay one Ren and grant the extra Zhiheng draw.");
        Require(game.Events.Any(item => item.Payload is PlayerMarkerChangedEvent { Marker: PlayerMarkerKind.Ren, Delta: -1 }),
            "Marker payment must be represented by attributed marker events.");
        EqualReplay(game, registry);
        Use(game, "ol:jilue", "turn-wansha");
        Require(Ren(game) == 1 && game.GetHumanLegalActions().All(action =>
                action.ProgramSkillId != "ol:jilue" || action.ProgramActivationId != "turn-wansha"),
            "Wansha must pay one marker and enforce its turn allowance.");
        EqualReplay(game, registry);
        var (partial, partialRegistry) = Create("jilue");
        Use(partial, Driver, "markers");
        var count = Hand(partial).Count;
        Use(partial, "ol:jilue", "exchange-owned-cards", [Hand(partial)[0].Id]);
        Require(Hand(partial).Count == count && Ren(partial) == 2,
            "A partial hand selection must not receive the all-hand bonus.");
        EqualReplay(partial, partialRegistry);
    }

    public static void HeartSlashAllowanceAndReplay()
    {
        var (game, registry) = Create("wushen");
        Use(game, Driver, "draw");
        Slash(game, Hand(game).First(card => card.Suit == Suit.Club).Id);
        Require(!game.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.Slash &&
                Hand(game).Any(card => card.Id == action.CardId && card.Suit == Suit.Club)),
            "An ordinary Slash must consume the ordinary allowance.");
        for (var index = 0; index < 2; index++)
        {
            Slash(game, Hand(game).First(card => card.Suit == Suit.Heart).Id);
            EqualReplay(game, registry);
        }
        Require(game.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.Slash &&
                Hand(game).Any(card => card.Id == action.CardId && card.Suit == Suit.Heart)),
            "OL heart Slashes must remain usable after the ordinary allowance is spent.");
    }

    public static void InitialHpAndContentFingerprint()
    {
        var (game, registry) = Create("initial", 3);
        var owner = game.CreateSnapshot(0, true).Players[0];
        Require(owner.Hp == 4 && owner.MaxHp == 7,
            "Initial HP and maximum HP must remain distinct, including the existing lord bonus.");
        EqualReplay(game, registry);
        var full = Registry("initial", null);
        Require(registry.ContentHash != full.ContentHash,
            "Initial HP changes must be covered by the content fingerprint.");
        foreach (var invalid in new[] { 0, 7 })
        {
            try { _ = Registry("initial", invalid); }
            catch (ArgumentOutOfRangeException) { continue; }
            throw new InvalidOperationException("Invalid initial HP was accepted.");
        }
    }

    public static void KillExtraTurnAtTurnEndAndReplay()
    {
        var (game, registry) = Create("lianpo");
        Use(game, Driver, "kill", targets: [1]);
        Require(game.Events.Any(item => item.Payload is PlayerDiedEvent { KillerSeat: 0 }),
            "The extra-turn condition must use an actual attributed kill.");
        Require(game.PendingDecision?.SkillPrompt?.SkillId != "ol:lianpo",
            "OL Lianpo must wait until the turn ends.");
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
        for (var step = 0; step < 1000 && game.PendingDecision?.SkillPrompt?.SkillId != "ol:lianpo"; step++)
        {
            if (game.PendingDecision is not { } prompt) { Accept(game.Submit(new AdvanceOneStepCommand(game.Revision))); continue; }
            if (prompt.Kind == DecisionKind.PlayCard) { Accept(game.Submit(new EndPlayPhaseCommand(prompt.PlayerSeat, game.Revision, prompt.PromptId))); continue; }
            var choice = prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip") ?? prompt.Choices.First();
            Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision)));
        }
        Require(game.PendingDecision?.SkillPrompt?.SkillId == "ol:lianpo", "An attributed kill must offer Lianpo at turn end.");
        EqualReplay(game, registry);
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        var turn = game.CreateSnapshot(0).TurnNumber;
        foreach (var branch in new[] { game, restored })
        {
            var prompt = branch.PendingDecision!;
            var choice = prompt.Choices.First(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
            Accept(branch.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, branch.Revision)));
            Drain(branch);
        }
        Require(game.CreateSnapshot(0).CurrentSeat == 0 && game.CreateSnapshot(0).TurnNumber > turn,
            "Lianpo must queue one extra owner turn after the completed turn.");
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(restored.CreateSnapshot(0, true)),
            "Lianpo's accepted extra turn must continue deterministically from its prompt checkpoint.");
    }

    public static void ActivationPresentationAndValidation()
    {
        string Read(string part)
        {
            using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream(
                $"CardGame.Content.Standard.SkillPrograms.ol-shen-sima-yi.{part}.json")!;
            using var reader = new StreamReader(stream); return reader.ReadToEnd();
        }
        var rules = Read("rules");
        var presentation = Read("presentation");
        var original = SkillProgramCatalog.Load(rules, presentation);
        var relabeled = SkillProgramCatalog.Load(rules, presentation.Replace("制衡：弃牌换牌", "选择弃置并摸牌", StringComparison.Ordinal));
        Require(original.Programs["ol:jilue"].GameplayHash == relabeled.Programs["ol:jilue"].GameplayHash &&
                relabeled.Presentations["ol:jilue"].ActivationLabels["exchange-owned-cards"] == "选择弃置并摸牌",
            "Activation labels must remain presentation-only metadata.");
        try { _ = SkillProgramCatalog.Load(rules, presentation.Replace("exchange-owned-cards", "unknown-activation", StringComparison.Ordinal)); }
        catch (InvalidOperationException error) when (error.Message.Contains("unknown activation")) { return; }
        throw new InvalidOperationException("An activation label referencing an unknown binding was accepted.");
    }

    private static ContentRegistry Registry(string flavor, int? initialHp) =>
        ContentRegistry.Build(new StandardContentPackage(), new Fixture(flavor, initialHp));
    private static (GameEngine, ContentRegistry) Create(string flavor, int? initialHp = null)
    {
        var registry = Registry(flavor, initialHp);
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 17, PlayerCount = 4,
            HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 30 }, registry);
        Accept(game.Submit(new StartGameCommand()));
        if (initialHp is not null)
            Require(game.PendingDecision!.Choices.Single(choice => choice.ContentIds.Contains(General))
                    .Parameters.GetValueOrDefault("health-preview")?.Contains($"{initialHp + 1}/7") == true,
                "General selection must explain distinct initial and maximum HP before choosing.");
        Accept(game.Submit(new SelectGeneralCommand(0, General, game.Revision, game.PendingDecision!.PromptId)));
        Drain(game);
        return (game, registry);
    }
    private static IReadOnlyList<CardSnapshot> Hand(GameEngine game) => game.CreateSnapshot(0, true).Players[0].Hand;
    private static int Ren(GameEngine game) => game.CreateSnapshot(0, true).Players[0].Markers?
        .SingleOrDefault(marker => marker.Kind == PlayerMarkerKind.Ren)?.Count ?? 0;
    private static void Use(GameEngine game, string skill, string activation, int[]? cards = null, int[]? targets = null)
    {
        Accept(game.Submit(new UseProgramSkillCommand(0, skill, activation, cards ?? [], targets ?? [],
            game.Revision, game.PendingDecision!.PromptId)));
        Drain(game);
    }
    private static void Slash(GameEngine game, int cardId)
    {
        var action = game.GetHumanLegalActions().First(action => action.CardId == cardId &&
            action.Kind == LegalActionKind.Slash && action.TargetSeats.SequenceEqual([1]));
        Accept(game.Submit(new PlayCardCommand(0, cardId, action.TargetSeats, game.Revision,
            game.PendingDecision!.PromptId, action.PlayedCardKind) { ConversionSource = action.ConversionSource }));
        Drain(game);
    }
    private static void Drain(GameEngine game)
    {
        for (var step = 0; step < 1000; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && game.ResolutionStack.Count == 0) return;
            if (game.PendingDecision is not { } prompt) { Accept(game.Submit(new AdvanceOneStepCommand(game.Revision))); continue; }
            var choice = prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip") ??
                prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("response") is "take-damage" or "pass") ?? prompt.Choices.First();
            Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision)));
        }
        throw new InvalidOperationException("Shared god fixture did not settle.");
    }
    private static void EqualReplay(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(restored.CreateSnapshot(0, true)),
            "Shared god mechanisms must restore exactly from their checkpoint.");
    }
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Command rejected.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(string flavor, int? initialHp) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("god-shared-fixture", new Version(1, 0, 0),
            [new PackageDependency("standard", new Version(1, 0, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            var bundle = flavor == "wushen" ? "ol-shen-guan-yu" : "ol-shen-sima-yi";
            string Read(string part)
            {
                using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream(
                    $"CardGame.Content.Standard.SkillPrograms.{bundle}.{part}.json")!;
                using var reader = new StreamReader(stream); return reader.ReadToEnd();
            }
            var catalog = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new ContentSkillDefinition(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var driver = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:god-shared-driver","revision":1,
                "minimumRulesVersion":{{GameCheckpoint.CurrentRulesVersion}},"activations":[
                {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]},
                {"id":"markers","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setMarkerAmount","target":"owner","marker":"ren","amount":3}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:god-shared-driver":{"name":"Fixture","description":"Fixture"}}}""");
            builder.AddSkill(new ContentSkillDefinition(Driver, "Fixture", "Fixture") { Program = driver.Programs[Driver] });
            builder.AddGeneral(new ContentGeneralDefinition(General, "Fixture", "supporter",
                flavor == "wushen" ? "ol:wushen" : flavor == "jilue" ? "ol:jilue" : flavor == "lianpo" ? "ol:lianpo" : "standard:none",
                "qun", BaseHp: 6, AdditionalSkillIds: [Driver]) { InitialHp = initialHp });
            var opponents = Enumerable.Range(1, 3).Select(index => "fixture:god-shared-opponent-" + index).ToArray();
            foreach (var id in opponents) builder.AddGeneral(new ContentGeneralDefinition(id, "Opponent", "supporter", "standard:none", "qun", 20));
            builder.AddDeck(new ContentDeckRecipe("fixture:god-shared-deck", "Fixture", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 208).Select(index => new ContentDeckPhysicalCard(
                flavor is "wushen" or "lianpo" ? "standard:slash" : "standard:peach", (Suit)(index % 4), index % 13 + 1)).ToArray() });
            builder.AddMode(new ContentModeDefinition(Mode, "Fixture", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 }, "fixture:god-shared-deck",
                GeneralCandidateCount: 4, GeneralPoolIds: [General, .. opponents]));
        }
    }
}
