using CardGame.Content.Standard;
using CardGame.Core;

internal static class CardUseCompletedChecks
{
    public static void FinishedSlashOpensReplayableProgramWindow()
    {
        const string rules = """
            {"schemaVersion":60,"skills":[{"id":"fixture:after-slash","revision":1,
              "minimumRulesVersion":170,"triggers":[{"id":"draw-after-finish",
              "window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["slash"],
              "optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
            """;
        const string presentation = """
            {"schemaVersion":3,"skills":{"fixture:after-slash":{"name":"结算后摸牌","description":"杀结算完毕后可以摸一张牌。"}}}
            """;
        try
        {
            SkillProgramCatalog.Load(rules.Replace("\"cardKinds\":[\"slash\"]",
                "\"cardKinds\":[\"lightning\"]", StringComparison.Ordinal), presentation);
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
            {"schemaVersion":60,"skills":[{"id":"fixture:after-slash","revision":2,
              "minimumRulesVersion":170,"triggers":[
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

    public static void CompletedUseFiltersFrozenConversionSource()
    {
        const string rules = """
            {"schemaVersion":60,"skills":[{"id":"fixture:after-slash","revision":3,
              "minimumRulesVersion":170,"viewAs":[{"id":"dodge-as-slash",
                "inputKinds":["dodge"],"inputSuits":[],"outputKind":"slash",
                "forPlay":true,"forResponse":false}],
              "triggers":[{"id":"after-conversion","window":"cardUseCompleted",
                "ownerRelation":"actor","cardKinds":["slash"],
                "condition":{"kind":"cardUseConversionSkillIs",
                  "conversionSkillId":"fixture:after-slash"},
                "optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
            """;
        const string presentation = """
            {"schemaVersion":3,"skills":{"fixture:after-slash":{"name":"转化来源测试","description":"只在本技能转化的杀结算后摸牌。"}}}
            """;
        try
        {
            SkillProgramCatalog.Load(rules.Replace("cardUseCompleted", "cardUseCommitted",
                StringComparison.Ordinal), presentation);
            throw new InvalidOperationException("A pre-completion window accepted the conversion-source condition.");
        }
        catch (InvalidOperationException error) when (error.Message.Contains(
            "cardUseConversionSkillIs requires a cardUseCompleted trigger", StringComparison.Ordinal))
        {
        }
        var condition = SkillProgramCatalog.Load(rules, presentation)
            .Programs["fixture:after-slash"].Triggers.Single().Condition;
        Require(condition.Evaluate(new SkillProgramTriggerFacts(0, 4, true,
                    CardUseConversionSkillIds: ["fixture:after-slash"])) &&
                !condition.Evaluate(new SkillProgramTriggerFacts(0, 4, true,
                    CardUseConversionSkillIds: ["another:conversion"])),
            "The conversion-source condition must distinguish exact skill IDs.");

        var convertedRegistry = ContentRegistry.Build(new StandardContentPackage(),
            new FixturePackage(rules, presentation, allDodge: true));
        var converted = CreateGame(convertedRegistry);
        Require(converted.Submit(new StartGameCommand()).Accepted &&
                converted.PendingDecision?.Kind == DecisionKind.PlayCard,
            "The conversion fixture did not reach human Play.");
        var slash = converted.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.Slash &&
            action.ConversionSource?.SkillId == "fixture:after-slash");
        var play = converted.Submit(new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats,
            converted.Revision, converted.PendingDecision!.PromptId, slash.PlayedCardKind)
        { ConversionSource = slash.ConversionSource });
        for (var step = 0; play.Accepted && step < 32 &&
             converted.PendingDecision is not { Kind: DecisionKind.ProgramTrigger } &&
             !converted.Events.Select(item => item.Payload).OfType<CardUseFinishedEvent>().Any(); step++)
        {
            var advanced = converted.Submit(new AdvanceOneStepCommand(converted.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ??
                "The converted Slash could not advance through its target response.");
        }
        Require(play.Accepted && converted.PendingDecision is
                { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 },
            play.Error?.Message ??
            $"The exact converted Slash did not open its completion trigger: pending={converted.PendingDecision?.Kind}, " +
            $"finished={converted.Events.Select(item => item.Payload).OfType<CardUseFinishedEvent>().Count()}, " +
            $"sources={string.Join(',', converted.Events.Select(item => item.Payload)
                .OfType<CardActionAcceptedEvent>().SelectMany(item => item.Action.ConversionChain)
                .Select(item => item.SkillId))}.");
        var paused = GameReplay.Restore(converted.CreateCheckpoint(), convertedRegistry);
        foreach (var branch in new[] { converted, paused })
        {
            var decision = branch.PendingDecision!;
            var activate = decision.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "activate");
            Require(branch.Submit(new AnswerPromptCommand(0, decision.PromptId,
                activate.Id, branch.Revision)).Accepted,
                "The converted completion trigger was not accepted.");
        }
        Require(converted.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Single(item => item.SkillId == "fixture:after-slash").BindingId ==
                "after-conversion" &&
                SnapshotJson.Serialize(paused.CreateSnapshot(0, true)) ==
                SnapshotJson.Serialize(converted.CreateSnapshot(0, true)),
            "The converted completion trigger must replay with its frozen source identity.");

        var nativeRegistry = ContentRegistry.Build(new StandardContentPackage(),
            new FixturePackage(rules, presentation));
        var native = CreateGame(nativeRegistry);
        Require(native.Submit(new StartGameCommand()).Accepted &&
                native.PendingDecision?.Kind == DecisionKind.PlayCard,
            "The native Slash comparison fixture did not reach human Play.");
        var nativeSlash = native.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.Slash && action.ConversionSource is null);
        var nativePlay = native.Submit(new PlayCardCommand(0, nativeSlash.CardId!.Value,
            nativeSlash.TargetSeats, native.Revision, native.PendingDecision!.PromptId,
            nativeSlash.PlayedCardKind));
        Require(nativePlay.Accepted && native.PendingDecision?.Kind != DecisionKind.ProgramTrigger &&
                native.Events.Select(item => item.Payload).OfType<CardUseFinishedEvent>().Any() &&
                native.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .All(item => item.SkillId != "fixture:after-slash"),
            "A native Slash must not satisfy the configured conversion-source condition.");
    }

    public static void ConfiguredSlashCanBecomeFireSlashWithoutZhuqueFan()
    {
        const string rules = """
            {"schemaVersion":60,"skills":[{"id":"fixture:after-slash","revision":4,
              "minimumRulesVersion":170,"viewAs":[{"id":"slash-as-fire-slash",
                "inputKinds":["slash"],"inputSuits":[],"outputKind":"fireSlash",
                "forPlay":true,"forResponse":false}]}]}
            """;
        const string presentation = """
            {"schemaVersion":3,"skills":{"fixture:after-slash":{
              "name":"火杀转化测试","description":"普通杀可改为火杀。"}}}
            """;
        try
        {
            SkillProgramCatalog.Load(rules.Replace("\"inputKinds\":[\"slash\"]",
                "\"inputKinds\":[\"dodge\"]", StringComparison.Ordinal), presentation);
            throw new InvalidOperationException("Fire Slash viewAs accepted a non-Slash source.");
        }
        catch (InvalidOperationException error) when (error.Message.Contains(
            "fireSlash viewAs currently requires one physical slash for play only", StringComparison.Ordinal))
        {
        }

        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new FixturePackage(rules, presentation));
        var game = CreateGame(registry);
        Require(game.Submit(new StartGameCommand()).Accepted &&
                game.PendingDecision?.Kind == DecisionKind.PlayCard,
            "The Fire Slash viewAs fixture did not reach human Play.");
        var actions = game.GetHumanLegalActions().Where(action =>
            action.Kind == LegalActionKind.Slash).ToArray();
        var configured = actions.First(action =>
            action.PlayedCardKind == CardKind.FireSlash &&
            action.ConversionSource?.BindingId == "slash-as-fire-slash");
        Require(actions.Any(action => action.CardId == configured.CardId &&
                    action.PlayedCardKind is null && action.ConversionSource is null),
            "Configured Fire Slash conversion must preserve the ordinary Slash option.");
        var played = game.Submit(new PlayCardCommand(0, configured.CardId!.Value,
            configured.TargetSeats, game.Revision, game.PendingDecision!.PromptId,
            configured.PlayedCardKind)
        { ConversionSource = configured.ConversionSource });
        Require(played.Accepted, played.Error?.Message ??
            "The configured Fire Slash conversion was rejected.");
        var events = game.Events.Select(item => item.Payload).ToArray();
        Require(events.OfType<CardUsedEvent>().Any(item => item.CardKind == CardKind.FireSlash) &&
                events.OfType<ZhuqueFanConvertedEvent>().Count() == 0 &&
                events.OfType<CardActionAcceptedEvent>().Any(item =>
                    item.Action.ConversionChain.Any(source =>
                        source.SkillId == "fixture:after-slash" &&
                        source.BindingId == "slash-as-fire-slash")),
            "A program Fire Slash must retain exact provenance without a Zhuque Fan event.");
        var replay = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, true)),
            "The configured Fire Slash must replay exactly.");
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
