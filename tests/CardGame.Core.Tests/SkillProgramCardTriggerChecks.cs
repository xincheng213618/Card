using CardGame.Content.Standard;
using CardGame.Core;

internal static class SkillProgramCardTriggerChecks
{
    internal static void Run()
    {
        ExactUseConversionSourceAndOptionalDrawReplay();
        ResponseTriggerPausesAtOpaqueTakeAndReplays();
    }

    private static void ResponseTriggerPausesAtOpaqueTakeAndReplays()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new TriggerFixturePackage());
        var game = FindResponse(registry);
        var response = game.PendingDecision ?? throw new InvalidOperationException("Response fixture lost its prompt.");
        var otherSource = response.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("conversion-skill-id") == "trigger-test:response-source-b");
        var otherBranch = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(otherBranch.Submit(new AnswerPromptCommand(0, otherBranch.PendingDecision!.PromptId,
            otherBranch.PendingDecision.Choices.Single(choice => choice.Id == otherSource.Id).Id,
            otherBranch.Revision)).Accepted, "Second response conversion was rejected.");
        Require(otherBranch.PendingDecision?.Kind != DecisionKind.ProgramCardTrigger &&
                otherBranch.Events.Select(item => item.Payload).OfType<ProgramCardTriggerResolvedEvent>().Count() == 0,
            "A response trigger bound to source A must not run for source B.");
        var converted = response.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("conversion-skill-id") == "trigger-test:response-source");
        Require(game.Submit(new AnswerPromptCommand(0, response.PromptId, converted.Id, game.Revision)).Accepted,
            "Converted Dodge response was rejected.");
        var invoke = game.PendingDecision ?? throw new InvalidOperationException("Response trigger did not pause.");
        var activate = invoke.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "program-trigger-activate");
        Require(game.Submit(new AnswerPromptCommand(0, invoke.PromptId, activate.Id, game.Revision)).Accepted,
            "Response trigger activation was rejected.");
        var take = game.PendingDecision ?? throw new InvalidOperationException("Opponent-hand take prompt was not published.");
        Require(take.Kind == DecisionKind.ProgramCardTrigger && take.Choices.Count > 0 &&
                game.State.ProcessingCardCount >= 1 &&
                take.Choices.All(choice => choice.Cards.Count == 0 &&
                    choice.Parameters.GetValueOrDefault("action") == "program-trigger-take" &&
                    int.TryParse(choice.Parameters.GetValueOrDefault("slot"), out _)),
            $"Opponent hand candidates must be opaque slots without card ids (processing={game.State.ProcessingCardCount}, " +
            $"choices={string.Join(';', take.Choices.Select(choice => $"{choice.Cards.Count}:{choice.Parameters.GetValueOrDefault("action")}:{choice.Parameters.GetValueOrDefault("slot")}"))}).");
        var acceptedAction = game.Events.Select(item => item.Payload).OfType<CardActionAcceptedEvent>()
            .Single(item => item.Action.Type == CardActionType.Response).Action;
        Require(acceptedAction.Type == CardActionType.Response && acceptedAction.ConversionChain.Count == 1 &&
                acceptedAction.ConversionChain[0].SkillId == "trigger-test:response-source",
            "The response must commit exactly one trusted card action with its exact conversion source.");
        var opponent = take.Choices[0].Targets.Single();
        var otherViewer = game.CreateSnapshot(opponent, revealAll: false);
        var observer = game.CreateSnapshot(-1, revealAll: false);
        Require(otherViewer.PendingDecision is null && observer.PendingDecision is null &&
                otherViewer.Players.Where(player => player.Seat != opponent).All(player => player.Hand.Count == 0) &&
                observer.Players.All(player => player.Hand.Count == 0),
            "Other seats and observers must not receive the private slot prompt or hidden hand identities.");
        var paused = game.CreateCheckpoint();
        var restored = GameReplay.Restore(paused, registry);
        var beforeOwner = restored.CreateSnapshot(0, true).Players[0].HandCount;
        var beforeOpponent = restored.CreateSnapshot(0, true).Players[opponent].HandCount;
        var restoredTake = restored.PendingDecision!;
        Require(restored.Submit(new AnswerPromptCommand(0, restoredTake.PromptId,
            restoredTake.Choices[0].Id, restored.Revision)).Accepted, "Opaque take selection was rejected.");
        var after = restored.CreateSnapshot(0, true);
        Require(after.Players[0].HandCount == beforeOwner + 1 &&
                after.Players[opponent].HandCount == beforeOpponent - 1 &&
                restored.Events.Select(item => item.Payload).OfType<ProgramCardTriggerResolvedEvent>()
                    .Single() is { SkillId: "trigger-test:obtain", TriggerId: "after-response", Activated: true },
            "Response trigger must transfer one opponent hand card and resolve exactly once.");
        var replay = GameReplay.Restore(restored.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, true)) == SnapshotJson.Serialize(after) &&
                replay.Events.Select(item => item.Payload).OfType<ProgramCardTriggerResolvedEvent>().Count() == 1,
            "Opaque response-trigger continuation did not replay exactly once.");
    }

    private static GameEngine FindResponse(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 512; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed, PlayerCount = 5, ModeId = "identity:program-response-trigger-5", HumanSeat = 0,
                HumanRole = Role.Rebel, UseInteractiveSetup = false, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, AiPolicyVersion = 2
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted) continue;
            for (var step = 0; step < 200; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.RespondDodge } prompt &&
                    prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("conversion-skill-id") ==
                        "trigger-test:response-source"))
                    return game;
                var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
                if (!result.Accepted || game.State.Status == EngineStatus.Completed) break;
            }
        }
        throw new InvalidOperationException("Could not find deterministic converted Dodge response fixture.");
    }

    private static void ExactUseConversionSourceAndOptionalDrawReplay()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new TriggerFixturePackage());
        var probe = Create(registry);
        var actions = probe.GetHumanLegalActions().Where(action =>
            action.Kind == LegalActionKind.Slash && action.ConversionSource is not null).ToArray();
        var sourceA = actions.First(action => action.ConversionSource!.SkillId == "trigger-test:source-a");
        var cardId = sourceA.CardId!.Value;
        var target = sourceA.TargetSeats.Single();
        var sourceB = actions.Single(action => action.CardId == cardId &&
            action.TargetSeats.SequenceEqual([target]) &&
            action.ConversionSource!.SkillId == "trigger-test:source-b");
        Require(sourceA.CardId == sourceB.CardId && sourceA.TargetSeats.SequenceEqual(sourceB.TargetSeats),
            "The fixture must publish two exact conversions for one physical card and target.");

        var missing = Create(registry);
        var before = SnapshotJson.Serialize(missing.CreateSnapshot(0, true));
        var rejectedMissing = missing.Submit(new PlayCardCommand(0, cardId, [target], missing.Revision,
            missing.PendingDecision!.PromptId, CardKind.Slash));
        Require(!rejectedMissing.Accepted && before == SnapshotJson.Serialize(missing.CreateSnapshot(0, true)),
            "An ambiguous converted play without its exact source must be rejected atomically.");

        var forged = Create(registry);
        before = SnapshotJson.Serialize(forged.CreateSnapshot(0, true));
        var rejectedForged = forged.Submit(new PlayCardCommand(0, cardId, [target], forged.Revision,
            forged.PendingDecision!.PromptId, CardKind.Slash)
        {
            ConversionSource = sourceA.ConversionSource! with { BindingId = "forged" }
        });
        Require(!rejectedForged.Accepted && before == SnapshotJson.Serialize(forged.CreateSnapshot(0, true)),
            "A forged conversion binding must be rejected atomically.");

        var unrelated = Create(registry);
        Require(unrelated.Submit(Command(unrelated, sourceB)).Accepted,
            "The second legal conversion source was rejected.");
        Require(unrelated.PendingDecision?.Kind != DecisionKind.ProgramCardTrigger &&
                unrelated.Events.Select(item => item.Payload).OfType<ProgramCardTriggerResolvedEvent>().Count() == 0,
            "A trigger bound to source A must not run for source B.");

        var started = Create(registry);
        Require(started.Submit(Command(started, sourceA)).Accepted, "The trigger source play was rejected.");
        Require(started.PendingDecision is { Kind: DecisionKind.ProgramCardTrigger },
            "The exact source must pause at the optional trigger prompt.");
        var paused = started.CreateCheckpoint();

        var skipped = GameReplay.Restore(paused, registry);
        var skipPrompt = skipped.PendingDecision!;
        var skip = skipPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "program-trigger-skip");
        var skipHand = skipped.CreateSnapshot(0, true).Players[0].HandCount;
        Require(skipped.Submit(new AnswerPromptCommand(0, skipPrompt.PromptId, skip.Id, skipped.Revision)).Accepted,
            "Skipping the optional trigger was rejected.");
        Require(skipped.CreateSnapshot(0, true).Players[0].HandCount == skipHand &&
                skipped.Events.Select(item => item.Payload).OfType<ProgramCardTriggerResolvedEvent>()
                    .Single() is { Activated: false },
            "Skipping must neither consume nor execute the trigger.");

        var activated = GameReplay.Restore(paused, registry);
        var prompt = activated.PendingDecision!;
        var accept = prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "program-trigger-activate");
        var handBefore = activated.CreateSnapshot(0, true).Players[0].HandCount;
        Require(activated.Submit(new AnswerPromptCommand(0, prompt.PromptId, accept.Id, activated.Revision)).Accepted,
            "Activating the generic draw trigger was rejected.");
        Require(activated.CreateSnapshot(0, true).Players[0].HandCount == handBefore + 1 &&
                activated.Events.Select(item => item.Payload).OfType<ProgramCardTriggerResolvedEvent>()
                    .Single() is { SkillId: "trigger-test:draw", TriggerId: "after-use", Activated: true },
            "The generic draw consumer must execute exactly once for its exact conversion source.");
        var replay = GameReplay.Restore(activated.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, true)) ==
                SnapshotJson.Serialize(activated.CreateSnapshot(0, true)) &&
                replay.Events.Select(item => item.Payload).OfType<ProgramCardTriggerResolvedEvent>().Count() == 1,
            "The completed trigger must replay exactly once.");
    }

    private static PlayCardCommand Command(GameEngine game, LegalAction action) => new(
        0, action.CardId!.Value, action.TargetSeats, game.Revision, game.PendingDecision!.PromptId,
        action.PlayedCardKind) { ConversionSource = action.ConversionSource };

    private static GameEngine Create(ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 1, PlayerCount = 5, ModeId = "identity:program-trigger-5", HumanSeat = 0,
            HumanRole = Role.Lord, UseInteractiveSetup = false, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, AiPolicyVersion = 2
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted && game.PendingDecision?.Kind == DecisionKind.PlayCard,
            "Trigger fixture did not reach human play.");
        return game;
    }

    private sealed class TriggerFixturePackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("program-trigger-fixture", new Version(1, 0, 0),
            [new PackageDependency("standard", new Version(1, 11, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(Rules, Presentation);
            foreach (var program in catalog.Programs.Values)
            {
                var text = catalog.Presentations[program.Id];
                builder.AddSkill(new ContentSkillDefinition(program.Id, text.Name, text.Description) { Program = program });
            }
            var generalIds = Enumerable.Range(0, 5).Select(index => $"trigger-test:general-{index}").ToArray();
            foreach (var id in generalIds)
                builder.AddGeneral(new ContentGeneralDefinition(id, "Trigger", "zhao_yun", "trigger-test:source-a",
                    AdditionalSkillIds: ["trigger-test:source-b", "trigger-test:draw",
                        "trigger-test:response-source", "trigger-test:response-source-b", "trigger-test:obtain"]));
            builder.AddDeck(new ContentDeckRecipe("trigger-test:dodge-deck", "Dodge", 2, 0,
                [new ContentDeckCardCount("standard:dodge", 30)]));
            builder.AddDeck(new ContentDeckRecipe("trigger-test:slash-deck", "Slash", 4, 2,
                [new ContentDeckCardCount("standard:slash", 60)]));
            builder.AddMode(new ContentModeDefinition("identity:program-trigger-5", "Trigger", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, DeckId: "trigger-test:dodge-deck", GeneralCandidateCount: 1, GeneralPoolIds: generalIds));
            builder.AddMode(new ContentModeDefinition("identity:program-response-trigger-5", "Response Trigger", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, DeckId: "trigger-test:slash-deck", GeneralCandidateCount: 1, GeneralPoolIds: generalIds));
        }
    }

    private const string Rules = """
        {"schemaVersion":2,"skills":[
          {"id":"trigger-test:source-a","revision":1,"viewAs":[{"id":"slash","inputKinds":["dodge"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false}]},
          {"id":"trigger-test:source-b","revision":1,"viewAs":[{"id":"slash","inputKinds":["dodge"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false}]},
          {"id":"trigger-test:draw","revision":1,"triggers":[{"id":"after-use","window":"cardUseTargetsFinalized","sourceSkillId":"trigger-test:source-a","sourceViewAsId":"slash","optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]},
          {"id":"trigger-test:response-source","revision":1,"viewAs":[{"id":"dodge","inputKinds":["slash"],"inputSuits":[],"outputKind":"dodge","forPlay":false,"forResponse":true}]},
          {"id":"trigger-test:response-source-b","revision":1,"viewAs":[{"id":"dodge","inputKinds":["slash"],"inputSuits":[],"outputKind":"dodge","forPlay":false,"forResponse":true}]},
          {"id":"trigger-test:obtain","revision":1,"triggers":[{"id":"after-response","window":"cardResponseAccepted","sourceSkillId":"trigger-test:response-source","sourceViewAsId":"dodge","optional":true,"effects":[{"op":"obtainOpponentHandCard","target":"owner","amount":1}]}]}
        ]}
        """;
    private const string Presentation = """
        {"schemaVersion":1,"skills":{
          "trigger-test:source-a":{"name":"A","description":"A"},
          "trigger-test:source-b":{"name":"B","description":"B"},
          "trigger-test:draw":{"name":"Draw","description":"Draw"},
          "trigger-test:response-source":{"name":"Dodge","description":"Dodge"},
          "trigger-test:response-source-b":{"name":"Dodge B","description":"Dodge B"},
          "trigger-test:obtain":{"name":"Obtain","description":"Obtain"}
        }}
        """;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
