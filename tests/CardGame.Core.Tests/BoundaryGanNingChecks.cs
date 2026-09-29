using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryGanNingChecks
{
    private const string GeneralId = "boundary:gan-ning";
    private const string SkillId = "boundary:fenwei";

    public static void DefinitionAndReusableSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        var general = current.Generals[GeneralId];
        var skill = current.Skills[SkillId];
        var trigger = skill.Program!.Triggers.Single();
        Require(current.Generals["classic:gan-ning"].SkillIds.SequenceEqual(["classic:qixi"]) &&
            general.Name == "界甘宁" && general.BaseHp == 4 && general.FactionId == "wu" &&
            general.SkillIds.SequenceEqual(["boundary:qixi", SkillId]) &&
            current.Skills["boundary:qixi"].Program?.ViewAs.Single().OutputKind == CardKind.Dismantlement &&
            current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(GeneralId) &&
            current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(GeneralId) &&
            !current.Modes["identity:classic-boundary-5"].GeneralPoolIds!.Contains(GeneralId) &&
            skill.Tags.HasFlag(SkillTag.Limited) &&
            trigger is { Window: SkillProgramTriggerWindow.CardUseBeforeTargetEffects,
                OwnerRelation: SkillProgramCardActionOwnerRelation.Observer,
                Optional: true, UsageScope: SkillUsageScope.Game, UsageLimit: 1,
                Effects: [{ Op: SkillProgramEffectOp.SelectTargets,
                    TargetKind: SkillProgramTargetKind.CurrentCardUseTargets },
                    { Op: SkillProgramEffectOp.NullifySelectedCardEffects }] } &&
            trigger.CardCategories.SequenceEqual([SkillProgramCardCategory.Trick]),
            "Boundary Gan Ning must have separate 1.140 skills, a limited generic trick observer and formal pools.");

        var rules = Resource("boundary-gan-ning.rules.json");
        var presentation = Resource("boundary-gan-ning.presentation.json");
        var generic = JsonNode.Parse(rules)!;
        generic["skills"]![0]!["id"] = "fixture:intervention";
        var gTrigger = generic["skills"]![0]!["triggers"]![0]!;
        gTrigger.AsObject().Remove("usageScope");
        gTrigger.AsObject().Remove("usageLimit");
        gTrigger["effects"]!.AsArray().Insert(1, JsonNode.Parse("""
            {"op":"draw","target":"owner","amount":1}
            """));
        var genericPresentation = """
            {"schemaVersion":3,"skills":{"fixture:intervention":{"name":"公共组合","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(generic.ToJsonString(), genericPresentation).Programs
                .ContainsKey("fixture:intervention"),
            "The card target source and nullification node must compose in a non-limited graph with another effect.");
        var emptyIntersection = JsonNode.Parse(generic.ToJsonString())!;
        emptyIntersection["skills"]![0]!["triggers"]![0]!["cardKinds"] =
            JsonNode.Parse("[\"slash\"]");
        Require(SkillProgramCatalog.Load(emptyIntersection.ToJsonString(), genericPresentation).Programs
                .ContainsKey("fixture:intervention"),
            "Independent card kind and category restrictions may have an empty runtime intersection.");
        var noSelection = JsonNode.Parse(generic.ToJsonString())!;
        noSelection["skills"]![0]!["triggers"]![0]!["effects"]!.AsArray().RemoveAt(0);
        Reject(noSelection.ToJsonString(), genericPresentation);
        var wrongWindow = JsonNode.Parse(generic.ToJsonString())!;
        wrongWindow["skills"]![0]!["triggers"]![0]!["window"] = "turnEnding";
        Reject(wrongWindow.ToJsonString(), genericPresentation);
    }

    public static void MultiTargetAssaultSubsetAndReplay()
    {
        var registry = Registry("standard:barbarian_assault");
        var game = Start(registry);
        ReachPlay(game);
        var action = game.GetHumanLegalActions().FirstOrDefault(item => item.Kind == LegalActionKind.BarbarianAssault);
        Require(action is not null,
            "Assault fixture has no legal card: " + string.Join(",",
                game.GetHumanLegalActions().Select(item => item.Kind)) + " | " +
            string.Join(",", game.CreateSnapshot(0, true).Players[0].Hand.Select(item => item.Kind)));
        Play(game, action!);
        var prompt = game.PendingDecision!;
        Require(prompt.SkillPrompt?.SkillId == SkillId && prompt.IsPrivate &&
            prompt.Choices.Select(item => item.Parameters.GetValueOrDefault("program-action"))
                .Order(StringComparer.Ordinal).SequenceEqual(["activate", "skip"]),
            "A real multi-target trick must offer the optional Fenwei observer before target effects.");
        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var resumed = GameReplay.Restore(checkpoint, registry);
        Require(State(resumed) == State(game) && resumed.PendingDecision?.SkillPrompt?.SkillId == SkillId,
            "Fenwei activation must survive checkpoint and replay.");
        Answer(game, "activate");
        var subset = game.PendingDecision!;
        Require(subset.SkillPrompt?.SkillId == SkillId && subset.IsPrivate &&
            subset.ValidTargetSeats.Count == 4 &&
            subset.Choices.Any(choice => choice.Targets.Count == 4) &&
            subset.Choices.All(choice => choice.Targets.Count is >= 1 and <= 4 &&
                choice.Parameters.GetValueOrDefault("maximum-targets") == "4"),
            "Fenwei must offer every nonempty subset up to the actual frozen target count.");
        var duringSelection = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(duringSelection) == State(game) &&
            duringSelection.PendingDecision?.Choices.Count == subset.Choices.Count,
            "The suspended public target set must replay exactly.");
        var invalidRevision = game.Revision;
        var invalid = game.Submit(new AnswerPromptCommand(0, subset.PromptId,
            new ChoiceId("program-targets.forged"), invalidRevision));
        Require(!invalid.Accepted && game.Revision == invalidRevision,
            "A forged cross-card target answer must fail atomically.");
        var selected = subset.Choices.Single(choice => choice.Targets.SequenceEqual([1, 2]));
        Answer(game, selected);
        var marked = game.Events.Select(item => item.Payload)
            .OfType<ProgramSelectedCardEffectsNullifiedEvent>().Single();
        Require(marked.TargetSeats.SequenceEqual([1, 2]),
            "Fenwei must record exactly the selected seats without deleting the other targets.");
        var afterMark = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(afterMark) == State(game), "Marked card effects must checkpoint after the original trick resumes.");
        AdvanceToResolved(game);
        Require(game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                .Where(item => item.SourceSeat == 0).Select(item => item.TargetSeat).SequenceEqual([3, 4]) &&
            game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                .Count(item => item.SkillId == SkillId && item.Activated) == 1 &&
            game.ResolutionStack.Count == 0 && State(afterMark) == State(game),
            "The untouched assault targets must resolve in order while protected targets take no damage.");
    }

    private static ContentRegistry Registry(string cardId, bool preEffectDeath = false,
        bool secondFenweiOwner = false, bool delayedTrickObserver = false) => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new Scenario(cardId, preEffectDeath, secondFenweiOwner, delayedTrickObserver));

    private static GameEngine Start(ContentRegistry registry, int seed = 1)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, PlayerCount = 5, ModeId = Scenario.ModeId, HumanSeat = 0,
            HumanRole = Role.Lord, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 10
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Fenwei fixture did not start.");
        var selection = game.PendingDecision!;
        var result = game.Submit(new SelectGeneralCommand(0, GeneralId, game.Revision, selection.PromptId));
        Require(result.Accepted, result.Error?.Message ?? "Fenwei fixture general selection failed.");
        return game;
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 40 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Advance(game);
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, "Fenwei fixture did not reach play.");
    }

    private static void AdvanceToResolved(GameEngine game)
    {
        for (var step = 0; step < 60 && game.ResolutionStack.Count > 0; step++)
            Advance(game);
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Fenwei fixture did not advance.");
    }

    private static void Play(GameEngine game, LegalAction action)
    {
        var result = game.Submit(new PlayCardCommand(0, action.CardId!.Value,
            action.TargetSeats, game.Revision, game.PendingDecision!.PromptId,
            action.PlayedCardKind, action.TargetCardId)
        {
            ConversionSource = action.ConversionSource,
            AdditionalConversionSources = action.AdditionalConversionSources,
        });
        Require(result.Accepted, result.Error?.Message ?? "Fenwei fixture card use failed.");
    }

    private static void Answer(GameEngine game, string action) => Answer(game,
        game.PendingDecision!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == action));
    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var result = game.Submit(new AnswerPromptCommand(0, game.PendingDecision!.PromptId,
            choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Fenwei answer failed.");
    }

    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, true));
    private static string Resource(string name)
    {
        using var stream = typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream(
            "CardGame.Content.Standard.SkillPrograms." + name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    private static void Reject(string rules, string presentation)
    {
        try { _ = SkillProgramCatalog.Load(rules, presentation); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected the invalid reusable card-effect graph to be rejected.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Scenario(string cardId, bool preEffectDeath, bool secondFenweiOwner,
        bool delayedTrickObserver) : IGameContentPackage
    {
        public const string ModeId = "identity:classic-boundary-gan-ning-check-5";
        public PackageManifest Manifest { get; } = new("boundary-gan-ning-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);
        public void Register(IContentRegistryBuilder builder)
        {
            if (preEffectDeath)
            {
                var catalog = SkillProgramCatalog.Load("""
                    {"schemaVersion":62,"skills":[{"id":"fixture:before-effect-death","revision":1,
                    "minimumRulesVersion": 171,"triggers":[{"id":"die-first",
                    "window":"cardUseBeforeTargetEffects","ownerRelation":"target",
                    "cardKinds":["barbarianAssault"],"optional":false,"priority":100,
                    "effects":[{"op":"loseHp","target":"owner","amount":4}]}]}]}
                    """, """
                    {"schemaVersion":3,"skills":{"fixture:before-effect-death":{"name":"先行死亡","description":"测试"}}}
                    """);
                builder.AddSkill(new ContentSkillDefinition("fixture:before-effect-death", "先行死亡", "测试")
                { Program = catalog.Programs["fixture:before-effect-death"] });
            }
            if (delayedTrickObserver)
            {
                var catalog = SkillProgramCatalog.Load("""
                    {"schemaVersion":62,"skills":[{"id":"fixture:delayed-trick-observer","revision":1,
                    "minimumRulesVersion": 171,"triggers":[{"id":"observe-delayed",
                    "window":"cardUseBeforeTargetEffects","ownerRelation":"observer",
                    "cardKinds":["indulgence"],"cardCategories":["trick"],"optional":false,
                    "effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
                    """, """
                    {"schemaVersion":3,"skills":{"fixture:delayed-trick-observer":{"name":"延时观察","description":"测试"}}}
                    """);
                builder.AddSkill(new ContentSkillDefinition("fixture:delayed-trick-observer", "延时观察", "测试")
                { Program = catalog.Programs["fixture:delayed-trick-observer"] });
            }
            builder.AddDeck(new ContentDeckRecipe("fixture:fenwei-deck", "奋威牌堆", 4, 1,
                cardId == "fixture:borrowed-mix"
                    ? [new ContentDeckCardCount("classic:borrowed-sword", 50),
                       new ContentDeckCardCount("standard:crossbow", 50),
                       new ContentDeckCardCount("standard:nullification", 20)]
                    : cardId == "fixture:assault-peach-mix"
                        ? [new ContentDeckCardCount("standard:barbarian_assault", 50),
                           new ContentDeckCardCount("standard:peach_garden", 50)]
                    : cardId == "fixture:assault-nullification-mix"
                        ? [new ContentDeckCardCount("standard:barbarian_assault", 50),
                           new ContentDeckCardCount("standard:nullification", 50)]
                    : [new ContentDeckCardCount(cardId, 100)]));
            var targets = Enumerable.Range(1, 4).Select(index => $"fixture:fenwei-target-{index}").ToArray();
            foreach (var target in targets)
                builder.AddGeneral(new ContentGeneralDefinition(target, "奋威目标", "supporter",
                    preEffectDeath && target == targets[0] ? "fixture:before-effect-death" :
                    delayedTrickObserver && target == targets[0] ? "fixture:delayed-trick-observer" :
                    secondFenweiOwner && target == targets[0] ? SkillId : "standard:none",
                    "wei", BaseHp: 4));
            builder.AddMode(new ContentModeDefinition(ModeId, "界甘宁测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, "fixture:fenwei-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [GeneralId, .. targets]));
        }
    }
}
