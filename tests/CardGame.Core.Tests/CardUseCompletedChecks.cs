using CardGame.Content.Standard;
using CardGame.Core;

internal static class CardUseCompletedChecks
{
    public static void FinishedSlashOpensReplayableProgramWindow()
    {
        const string rules = """
            {"schemaVersion":62,"skills":[{"id":"fixture:after-slash","revision":1,
              "minimumRulesVersion": 171,"triggers":[{"id":"draw-after-finish",
              "window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["slash"],
              "optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
            """;
        const string presentation = """
            {"schemaVersion":3,"skills":{"fixture:after-slash":{"name":"结算后摸牌","description":"杀结算完毕后可以摸一张牌。"}}}
            """;
        try
        {
            SkillProgramCatalog.Load(rules.Replace("\"cardKinds\":[\"slash\"]",
                "\"cardKinds\":[\"dodge\"]", StringComparison.Ordinal), presentation);
            throw new InvalidOperationException("A completion window accepted an unsupported card kind.");
        }
        catch (InvalidOperationException error) when (error.Message.Contains(
            "unsupported by cardUseCompleted", StringComparison.Ordinal))
        {
        }

        var registry = ContentRegistry.Build(new StandardContentPackage(), new FixturePackage(rules, presentation));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 1, PlayerCount = 4, ModeId = "identity:completed-slash-fixture", HumanSeat = 0,
            HumanRole = Role.Lord, UseInteractiveSetup = false, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, AiPolicyVersion = 2
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted &&
                game.PendingDecision?.Kind == DecisionKind.PlayCard,
            "The completed-use fixture did not start at human Play.");
        var slash = game.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.Slash && action.TargetSeats.Count == 1);
        var cardId = slash.CardId!.Value;
        var play = game.Submit(new PlayCardCommand(0, cardId, slash.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, slash.PlayedCardKind));
        Require(play.Accepted, play.Error?.Message ?? "The fixture Slash was rejected.");
        var prompt = game.PendingDecision;
        Require(prompt is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } &&
                game.Events.Select(item => item.Payload).OfType<CardUseFinishedEvent>()
                    .Any(item => item.CardId == cardId) &&
                game.State.ProcessingCardCount == 0 &&
                game.CardMovements.Any(move => move.CardId == cardId &&
                    move.To == CardLocation.DiscardPile),
            "The trigger must wait until the Slash finished event and physical discard, then publish its prompt.");

        var paused = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(paused.CreateSnapshot(0, true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, true)),
            "The completed-use choice must restore exactly.");
        var beforeHand = game.CreateSnapshot(0, true).Players[0].HandCount;
        foreach (var branch in new[] { game, paused })
        {
            var decision = branch.PendingDecision!;
            var activate = decision.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "activate");
            var answer = branch.Submit(new AnswerPromptCommand(0, decision.PromptId,
                activate.Id, branch.Revision));
            Require(answer.Accepted, answer.Error?.Message ?? "The completed-use choice was rejected.");
        }
        Require(game.CreateSnapshot(0, true).Players[0].HandCount == beforeHand + 1 &&
                game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Single(item => item.SkillId == "fixture:after-slash") is
                    { Window: SkillProgramTriggerWindow.CardUseCompleted, Completed: true } &&
                SnapshotJson.Serialize(paused.CreateSnapshot(0, true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, true)),
            "The completed-use trigger must draw once and replay with the same result.");
    }

    public static void CompletedUseFreezesActualDamageFact()
    {
        const string rules = """
            {"schemaVersion":62,"skills":[{"id":"fixture:after-slash","revision":2,
              "minimumRulesVersion": 171,"triggers":[
                {"id":"after-damage","window":"cardUseCompleted","ownerRelation":"actor",
                 "cardKinds":["slash"],"condition":{"kind":"cardUseCausedDamage"},
                 "optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]},
                {"id":"after-no-damage","window":"cardUseCompleted","ownerRelation":"actor",
                 "cardKinds":["slash"],"condition":{"kind":"not","children":[
                   {"kind":"cardUseCausedDamage"}]},"optional":true,
                 "effects":[{"op":"draw","target":"owner","amount":2}]}]}]}
            """;
        const string presentation = """
            {"schemaVersion":3,"skills":{"fixture:after-slash":{"name":"结算伤害测试","description":"按整张杀的实际伤害结果摸牌。"}}}
            """;
        try
        {
            SkillProgramCatalog.Load(rules.Replace("cardUseCompleted", "cardUseCommitted",
                StringComparison.Ordinal), presentation);
            throw new InvalidOperationException("A pre-completion window accepted the damage-result condition.");
        }
        catch (InvalidOperationException error) when (error.Message.Contains(
            "cardUseCausedDamage requires a cardUseCompleted trigger", StringComparison.Ordinal))
        {
        }

        var catalog = SkillProgramCatalog.Load(rules, presentation);
        var fact = catalog.Programs["fixture:after-slash"].Triggers[0].Condition;
        Require(fact.Evaluate(new SkillProgramTriggerFacts(0, 4, true, CardUseCausedDamage: true)) &&
                !fact.Evaluate(new SkillProgramTriggerFacts(0, 4, true, CardUseCausedDamage: false)),
            "The frozen damage-result condition must distinguish actual damage from a fully dodged use.");
        var registry = ContentRegistry.Build(new StandardContentPackage(), new FixturePackage(rules, presentation));
        var game = CreateGame(registry);
        Require(game.Submit(new StartGameCommand()).Accepted &&
                game.PendingDecision?.Kind == DecisionKind.PlayCard,
            "The damage-result fixture did not start at human Play.");
        var slash = game.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.Slash && action.TargetSeats.Count == 1);
        var play = game.Submit(new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, slash.PlayedCardKind));
        Require(play.Accepted, play.Error?.Message ?? "The damage-result Slash was rejected.");
        Require(game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 },
            "An actual-damage use must expose its matching completed-use trigger.");
        var paused = GameReplay.Restore(game.CreateCheckpoint(), registry);
        var beforeHand = game.CreateSnapshot(0, true).Players[0].HandCount;
        foreach (var branch in new[] { game, paused })
        {
            var decision = branch.PendingDecision!;
            var activate = decision.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "activate");
            var answer = branch.Submit(new AnswerPromptCommand(0, decision.PromptId,
                activate.Id, branch.Revision));
            Require(answer.Accepted, answer.Error?.Message ?? "The damage-result choice was rejected.");
        }
        var resolved = game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
            .Where(item => item.SkillId == "fixture:after-slash").ToArray();
        Require(game.CreateSnapshot(0, true).Players[0].HandCount == beforeHand + 1 &&
                resolved.Length == 1 && resolved[0].BindingId == "after-damage" &&
                SnapshotJson.Serialize(paused.CreateSnapshot(0, true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, true)),
            "Only the actual-damage branch may resolve, once, across checkpoint restore.");
    }



    private static GameEngine CreateGame(ContentRegistry registry) =>
        GameEngine.CreateStandard(new GameOptions
        {
            Seed = 1, PlayerCount = 4, ModeId = "identity:completed-slash-fixture", HumanSeat = 0,
            HumanRole = Role.Lord, UseInteractiveSetup = false, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, AiPolicyVersion = 2
        }, registry);

    private sealed class FixturePackage(string rules, string presentation, bool allDodge = false) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("completed-slash-fixture", new Version(1, 0, 0),
            [new PackageDependency("standard", new Version(1, 11, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(rules, presentation);
            var program = catalog.Programs["fixture:after-slash"];
            var text = catalog.Presentations[program.Id];
            builder.AddSkill(new ContentSkillDefinition(program.Id, text.Name, text.Description)
            {
                Program = program
            });
            var generalIds = Enumerable.Range(0, 4)
                .Select(index => $"fixture:after-slash-general-{index}").ToArray();
            foreach (var generalId in generalIds)
                builder.AddGeneral(new ContentGeneralDefinition(generalId, "结算测试",
                    "zhao_yun", "fixture:after-slash"));
            builder.AddDeck(new ContentDeckRecipe("fixture:after-slash-deck",
                allDodge ? "全闪" : "全杀", 4, 0,
                [new ContentDeckCardCount(allDodge ? "standard:dodge" : "standard:slash", 60)]));
            builder.AddMode(new ContentModeDefinition("identity:completed-slash-fixture", "结算测试", 4, 4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1
                }, DeckId: "fixture:after-slash-deck", GeneralCandidateCount: 1,
                GeneralPoolIds: generalIds));
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
