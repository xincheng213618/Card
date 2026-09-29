using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class SkillProgramChecks
{
    public static void LoaderCanonicalizationAndPresentationIsolation()
    {
        var first = SkillProgramCatalog.Load(RulesA, PresentationA);
        var second = SkillProgramCatalog.Load(RulesB, PresentationB);
        var program = first.Programs["scenario:composed"];

        Require(program.GameplayHash == second.Programs["scenario:composed"].GameplayHash,
            "Equivalent rule JSON must have the same gameplay hash regardless of formatting and property order.");
        Require(first.Presentations[program.Id].Description != second.Presentations[program.Id].Description,
            "The fixture must actually vary presentation text.");
        Require(program.GameplayHash.Length == 64 && program.GameplayHash.All(Uri.IsHexDigit) &&
                program.GameplayHash == program.GameplayHash.ToLowerInvariant(),
            "GameplayHash must be a lowercase SHA-256 value.");
        Require(program.Modifiers.Count == 2 &&
                program.Modifiers[0].Query == SkillRuleQuery.DrawCount &&
                program.Modifiers[0].Operation == SkillRuleOperation.Add &&
                program.Modifiers[0].Condition.Evaluate(new PlayerSkillContext(0, 4, 4, 2, TurnPhase.Draw, IsOwnTurn: true)) &&
                !program.Modifiers[0].Condition.Evaluate(new PlayerSkillContext(0, 3, 4, 2, TurnPhase.Play, IsOwnTurn: true)),
            "The loader must compile modifiers and nested conditions into typed definitions.");
        Require(program.ViewAs.Single().OutputKind == CardKind.Slash &&
                program.Activations.Single().Effects.Select(effect => effect.Op).SequenceEqual(
                    [SkillProgramEffectOp.GiveSelected, SkillProgramEffectOp.Recover, SkillProgramEffectOp.Draw]),
            "The loader must retain typed view-as and ordered active effects.");
    }

    public static void LoaderRejectsMalformedUnsupportedDefinitions()
    {
        AssertReject(RulesA.Replace("\"revision\":1", "\"revision\":1,\"mystery\":true", StringComparison.Ordinal),
            PresentationA, "mystery");
        AssertReject(RulesA.Replace("\"operation\":\"add\"", "\"operation\":\"multiply\"", StringComparison.Ordinal),
            PresentationA, "multiply");
        AssertReject(RulesA.Replace("\"op\":\"draw\"", "\"op\":\"judge\"", StringComparison.Ordinal),
            PresentationA, "judge");
        AssertReject(RulesA.Replace("\"op\":\"draw\"", "\"op\":\"causeDeath\"", StringComparison.Ordinal),
            PresentationA, "causeDeath");
        AssertReject(RulesA.Replace("\"outputKind\":\"slash\"", "\"outputKind\":\"crossbow\"", StringComparison.Ordinal),
            PresentationA, "outputKind");
        AssertReject(RulesA.Replace("\"revision\":1", "\"revision\":1,\"revision\":1", StringComparison.Ordinal),
            PresentationA, "duplicate property");
        AssertReject(RulesA.Replace("\"minTargets\":1", "\"minTargets\":0", StringComparison.Ordinal),
            PresentationA, "selectedTarget must be produced");
        AssertReject(RulesA, PresentationA.Replace("scenario:composed", "scenario:missing", StringComparison.Ordinal),
            "unknown skill");
        AssertReject("{", PresentationA, "rules");
    }

    public static void ConfiguredActiveSequenceIsAtomicAndReplayable()
    {
        var registry = ComposedSkillContentRegistry.CreateShowcase();
        var game = FindStartedGame(registry, "composed:giver");
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("Composed giver did not reach play.");
        var action = game.GetHumanLegalActions().Single(item =>
            item.Kind == LegalActionKind.UseProgramSkill &&
            item.ProgramSkillId == "composed:give-and-draw" &&
            item.ProgramActivationId == "gift");
        var cardId = action.SelectableCardIds.Order().First();
        var targetSeat = action.SelectableTargetSeats.Order().First();
        var before = game.CreateSnapshot(0, revealAll: true);
        var beforeState = SnapshotJson.Serialize(before);
        var beforeEvents = game.Events.Count;
        var beforeCommands = game.AcceptedCommands.Count;

        var forged = game.Submit(new UseProgramSkillCommand(
            0, action.ProgramSkillId!, action.ProgramActivationId!, [cardId], [0], game.Revision, prompt.PromptId));
        Require(!forged.Accepted && beforeState == SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                beforeEvents == game.Events.Count && beforeCommands == game.AcceptedCommands.Count,
            "A forged program target must be rejected without changing state, events, or the command journal.");

        var accepted = game.Submit(new UseProgramSkillCommand(
            0, action.ProgramSkillId!, action.ProgramActivationId!, [cardId], [targetSeat], game.Revision, prompt.PromptId));
        Require(accepted.Accepted, accepted.Error?.Message ?? "Configured active skill was rejected.");
        var after = game.CreateSnapshot(0, revealAll: true);
        var beforeOwner = before.Players.Single(player => player.Seat == 0);
        var afterOwner = after.Players.Single(player => player.Seat == 0);
        var beforeTarget = before.Players.Single(player => player.Seat == targetSeat);
        var afterTarget = after.Players.Single(player => player.Seat == targetSeat);
        Require(afterTarget.Hand.Any(card => card.Id == cardId) &&
                !afterOwner.Hand.Any(card => card.Id == cardId) &&
                afterOwner.HandCount == beforeOwner.HandCount &&
                afterTarget.HandCount == beforeTarget.HandCount + 1,
            "GiveSelected followed by Draw must move the exact card and draw exactly once.");
        Require(!game.GetHumanLegalActions().Any(item =>
                item.Kind == LegalActionKind.UseProgramSkill && item.ProgramSkillId == action.ProgramSkillId),
            "The configured once-per-turn activation must disappear after use.");

        var restored = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) == SnapshotJson.Serialize(after),
            "The accepted configured active sequence must replay to the same state.");
    }

    public static void ProgramHashControlsCheckpointCompatibility()
    {
        var original = BuildHashRegistry(RulesA, PresentationA);
        var presentationOnly = BuildHashRegistry(RulesA, PresentationB);
        var changedRules = BuildHashRegistry(
            RulesA.Replace("\"query\":\"drawCount\",\"operation\":\"add\",\"value\":1",
                "\"query\":\"drawCount\",\"operation\":\"add\",\"value\":2", StringComparison.Ordinal),
            PresentationA);
        Require(original.ContentHash == presentationOnly.ContentHash,
            "Changing only a program's display name or description must not change ContentHash.");
        Require(original.ContentHash != changedRules.ContentHash,
            "Changing executable program semantics must change ContentHash.");

        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17,
            PlayerCount = 5,
            ModeId = "identity:program-hash-5",
            HumanSeat = 0,
            HumanRole = Role.Lord,
            UseInteractiveSetup = false,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2
        }, original);
        Require(game.Submit(new StartGameCommand()).Accepted, "Program hash fixture failed to start.");
        var checkpoint = game.CreateCheckpoint();
        _ = GameReplay.Restore(checkpoint, presentationOnly);
        RequireThrows<InvalidOperationException>(() => GameReplay.Restore(checkpoint, changedRules));
    }

    public static void ProgramPausedDyingAndConsumedSelection()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new DyingProgramPackage());
        var game = CreateDyingProgramGame(registry, "identity:program-peach-5");
        var action = game.GetHumanLegalActions().Single(item => item.Kind == LegalActionKind.UseProgramSkill &&
            item.ProgramActivationId == "consume-after-dying");
        var cardId = action.SelectableCardIds.Single();
        Require(game.Submit(new UseProgramSkillCommand(0, action.ProgramSkillId!, action.ProgramActivationId!, [cardId], [],
            game.Revision, game.PendingDecision!.PromptId)).Accepted, "Selected dying activation was rejected.");
        var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
        var prompt = restored.PendingDecision ?? throw new InvalidOperationException("Paused dying prompt was not restored.");
        var peach = prompt.Choices.Single(choice => choice.Parameters.GetValueOrDefault("response") == "peach");
        Require(restored.Submit(new AnswerPromptCommand(0, prompt.PromptId, peach.Id, restored.Revision)).Accepted,
            "Restored dying rescue was rejected.");
        Require(restored.CreateSnapshot(0, true).Players[0] is { IsAlive: true, Hp: 1, HandCount: 0 } &&
                restored.Events.Select(item => item.Payload).OfType<CardMovedEvent>().All(item =>
                    item.From != CardLocation.DrawPile || item.To != CardLocation.Hand(0)),
            "A selected Peach spent on rescue must cancel later discard and draw steps.");
    }

    private static GameEngine FindStartedGame(
        ContentRegistry registry,
        string generalId,
        Func<GameEngine, bool>? extra = null)
    {
        for (var seed = 1; seed <= 16384; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                ModeId = "identity:composed-skills-5",
                HumanSeat = 0,
                HumanRole = Role.Lord,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2
            }, registry);
            if (game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).GeneralId != generalId ||
                !game.Submit(new StartGameCommand()).Accepted ||
                game.PendingDecision?.Kind != DecisionKind.PlayCard ||
                extra is not null && !extra(game))
            {
                continue;
            }

            return game;
        }

        throw new InvalidOperationException($"Could not find deterministic composed-skill fixture for '{generalId}'.");
    }

    private static ContentRegistry BuildHashRegistry(string rules, string presentation) =>
        ContentRegistry.Build(new StandardContentPackage(), new HashFixturePackage(rules, presentation));

    private static GameEngine CreateDyingProgramGame(ContentRegistry registry, string modeId)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 1, PlayerCount = 5, ModeId = modeId, HumanSeat = 0,
            HumanRole = Role.Lord, UseInteractiveSetup = false, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, AiPolicyVersion = 2
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted && game.PendingDecision?.Kind == DecisionKind.PlayCard,
            "Deterministic dying fixture did not reach play.");
        return game;
    }

    private sealed class HashFixturePackage(string rules, string presentation) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new(
            "program-hash-fixture", new Version(1, 0, 0),
            [new PackageDependency("standard", new Version(1, 11, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(rules, presentation);
            var program = catalog.Programs["scenario:composed"];
            var display = catalog.Presentations[program.Id];
            builder.AddSkill(new ContentSkillDefinition(program.Id, display.Name, display.Description)
            {
                Program = program
            });
            var generalIds = Enumerable.Range(0, 5).Select(index => $"scenario:program-general-{index}").ToArray();
            foreach (var generalId in generalIds)
                builder.AddGeneral(new ContentGeneralDefinition(
                    generalId, "Program General", "zhuge_liang", program.Id));
            builder.AddMode(new ContentModeDefinition(
                "identity:program-hash-5", "Program Hash", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId: "standard:basic-demo",
                GeneralCandidateCount: 1,
                GeneralPoolIds: generalIds));
        }
    }

    private sealed class DyingProgramPackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("program-dying-fixture", new Version(1, 0, 0),
            [new PackageDependency("standard", new Version(1, 11, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(DyingRules, DyingPresentation);
            var program = catalog.Programs["scenario:dying"];
            builder.AddSkill(new ContentSkillDefinition(program.Id, "Dying", "Lose HP then draw") { Program = program });
            var ids = Enumerable.Range(0, 5).Select(index => $"scenario:dying-{index}").ToArray();
            foreach (var id in ids)
                builder.AddGeneral(new ContentGeneralDefinition(id, "Dying", "hua_tuo", program.Id, BaseHp: 1));
            builder.AddDeck(new ContentDeckRecipe("scenario:no-rescue", "No Rescue", 1, 0,
                [new ContentDeckCardCount("standard:slash", 30)]));
            builder.AddDeck(new ContentDeckRecipe("scenario:all-peach", "All Peach", 1, 0,
                [new ContentDeckCardCount("standard:peach", 30)]));
            AddMode(builder, "identity:program-no-rescue-5", "scenario:no-rescue", ids);
            AddMode(builder, "identity:program-peach-5", "scenario:all-peach", ids);
        }

        private static void AddMode(IContentRegistryBuilder builder, string id, string deckId, string[] ids) =>
            builder.AddMode(new ContentModeDefinition(id, "Dying", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, DeckId: deckId, GeneralCandidateCount: 1, GeneralPoolIds: ids));
    }

    private const string DyingRules = """
        {"schemaVersion":62,"skills":[{"id":"scenario:dying","revision":1,"modifiers":[],"viewAs":[],
        "activations":[{"id":"invoke","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,
        "targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"loseHp","target":"owner","amount":5},
        {"op":"draw","target":"owner","amount":1}]},
        {"id":"consume-after-dying","minCards":1,"maxCards":1,"minTargets":0,"maxTargets":0,
        "targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"loseHp","target":"owner","amount":5},
        {"op":"discardSelected","target":"owner","amount":1},{"op":"draw","target":"owner","amount":1}]},
        {"id":"gift-any-living","minCards":1,"maxCards":1,"minTargets":1,"maxTargets":1,
        "targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"giveSelected","target":"selectedTarget","amount":1}]}]}]}
        """;
    private const string DyingPresentation = """
        {"schemaVersion":3,"skills":{"scenario:dying":{"name":"Dying","description":"Lose HP then draw"}}}
        """;

    private static void AssertReject(string rules, string presentation, string expectedMessage)
    {
        try
        {
            _ = SkillProgramCatalog.Load(rules, presentation);
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        throw new InvalidOperationException($"Loader accepted invalid skill program expected to mention '{expectedMessage}'.");
    }

    private static void RequireThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private const string PresentationA = """
        {"schemaVersion":3,"skills":{"scenario:composed":{"name":"Composed","description":"first text"}}}
        """;

    private const string PresentationB = """
        {
          "skills": {
            "scenario:composed": { "description": "updated display text", "name": "Renamed" }
          },
          "schemaVersion": 3
        }
        """;

    private const string RulesA = """
        {
          "schemaVersion":62,
          "skills":[{
            "id":"scenario:composed",
            "revision":1,
            "modifiers":[
              {"id":"extra-draw","priority":0,"query":"drawCount","operation":"add","value":1,"condition":{"kind":"all","children":[{"kind":"ownTurn"},{"kind":"not","children":[{"kind":"wounded"}]}]}},
              {"id":"unlimited-slash","priority":0,"query":"slashLimit","operation":"unlimited","value":0,"condition":{"kind":"any","children":[{"kind":"hpAtLeast","value":3},{"kind":"handCountAtLeast","value":2}]}}
            ],
            "viewAs":[{"id":"convert","inputKinds":["dodge"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false}],
            "activations":[{
              "id":"gift","minCards":1,"maxCards":1,"minTargets":1,"maxTargets":1,
              "targetKind":"otherLiving","usesPerTurn":1,"condition":{"kind":"wounded"},
              "effects":[
                {"op":"giveSelected","target":"selectedTarget","amount":1},
                {"op":"recover","target":"owner","amount":1},
                {"op":"draw","target":"owner","amount":1,"condition":{"kind":"hpAtLeast","value":1}}
              ]
            }]
          }]
        }
        """;

    private const string RulesB = """
        {
          "skills": [
            {
              "activations": [
                {
                  "usesPerTurn": 1,
                  "effects": [
                    { "amount": 1, "target": "selectedTarget", "op": "giveSelected" },
                    { "target": "owner", "amount": 1, "op": "recover" },
                    { "condition": { "value": 1, "kind": "hpAtLeast" }, "amount": 1, "op": "draw", "target": "owner" }
                  ],
                  "targetKind": "otherLiving", "maxTargets": 1, "minTargets": 1,
                  "maxCards": 1, "minCards": 1, "condition": { "kind": "wounded" }, "id": "gift"
                }
              ],
              "viewAs": [
                { "forResponse": false, "outputKind": "slash", "inputSuits": [], "forPlay": true, "inputKinds": ["dodge"], "id": "convert" }
              ],
              "modifiers": [
                { "condition": { "children": [{ "kind": "ownTurn" }, { "children": [{ "kind": "wounded" }], "kind": "not" }], "kind": "all" }, "value": 1, "operation": "add", "query": "drawCount", "priority": 0, "id": "extra-draw" },
                { "condition": { "children": [{ "value": 3, "kind": "hpAtLeast" }, { "kind": "handCountAtLeast", "value": 2 }], "kind": "any" }, "operation": "unlimited", "query": "slashLimit", "value": 0, "id": "unlimited-slash", "priority": 0 }
              ],
              "revision": 1,
              "id": "scenario:composed"
            }
          ],
          "schemaVersion": 62
        }
        """;
}
