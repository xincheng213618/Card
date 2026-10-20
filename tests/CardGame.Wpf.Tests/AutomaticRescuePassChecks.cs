using System.Text.Json;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class AutomaticRescuePassChecks
{
    public static void UnavailableRescuePass(string output)
    {
        var (fixture, _) = StableHandResponseFixture.Create(DecisionKind.RescueDying, output);
        using (fixture)
        {
            var offered = Program.Engine(fixture);
            var before = Boundary(offered);
            Program.Assert(!fixture.IsAutoAdvance && offered.PendingDecision!.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("response") == "peach"),
                "The real rescue fixture must begin paused with a legal physical Peach.");
            Pump();
            Program.Assert(Boundary(offered) == before,
                "A legal rescue must remain a human choice when the dispatcher runs.");
        }

        // Ordinary empty hands already pass inside Core. A genuine sole-choice
        // prompt remains after a real optional top-deck rescue attempt fails.
        var (prepared, registry, available) = CreateFailedDeckRescue();
        var unavailable = prepared.CreateCheckpoint();
        var unavailablePrompt = prepared.CreateSnapshot(0).PendingDecision!;
        Program.Assert(unavailablePrompt is { Kind: DecisionKind.RescueDying, PlayerSeat: 0, Choices.Count: 1 } &&
                       unavailablePrompt.Choices[0].Parameters.GetValueOrDefault("response") == "let-die",
            "Declining an actual failed top-deck rescue must return the native dying response with only its pass choice.");

        var store = new MemorySaveStore();
        Save(unavailable);
        using var model = new MainViewModel(false, prepared.Seed, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        var retired = Program.Engine(model);
        var retiredBoundary = Boundary(retired);
        Program.Assert(!model.HasSaveError && !model.IsAutoAdvance && retired.PendingDecision?.PromptId == unavailablePrompt.PromptId,
            "Loading an unavailable rescue must queue its answer after the current load and commit complete.");

        // Replace the engine before that queued callback runs. The old game's public
        // prompt remains valid in its own engine and must still never be answered.
        Save(available);
        model.LoadManualGameCommand.Execute(null);
        var replacement = Program.Engine(model);
        var replacementBoundary = Boundary(replacement);
        Pump();
        Program.Assert(!ReferenceEquals(retired, replacement) && !model.HasSaveError &&
                       Boundary(retired) == retiredBoundary && Boundary(replacement) == replacementBoundary,
            "A deferred rescue pass must not answer either a retired engine or its replacement's legal rescue prompt.");

        Save(unavailable);
        model.LoadManualGameCommand.Execute(null);
        var actual = Program.Engine(model);
        var promptBeforePump = actual.CreateSnapshot(0).PendingDecision!;
        var expected = GameReplay.Restore(unavailable, registry);
        var command = new AnswerPromptCommand(0, promptBeforePump.PromptId,
            promptBeforePump.Choices.Single().Id, actual.Revision);
        Accept(expected, command);
        Program.Assert(!model.IsAutoAdvance && actual.AcceptedCommands.Count == unavailable.Commands.Count &&
                       actual.PendingDecision?.PromptId == unavailablePrompt.PromptId,
            "The unavailable rescue must remain unchanged until its dispatcher callback runs, even with automatic advance paused.");
        Pump();
        Program.Assert(actual.AcceptedCommands.Count == unavailable.Commands.Count + 1 &&
                       actual.AcceptedCommands.Last() is AnswerPromptCommand answer && answer == command &&
                       Boundary(actual) == Boundary(expected),
            "An unavailable rescue must submit exactly the normal published pass command and match the independent native result.");
        var after = Boundary(actual);
        Pump();
        Program.Assert(Boundary(actual) == after,
            "A completed deferred rescue pass must not answer the retired prompt twice.");
        StableHandResponseFixture.RecordAndReplay(actual, registry, output, "automatic-unavailable-rescue.after");

        // A legal conversion remains a rescue option even alongside an unusable
        // ordinary card. The exact engine-published choices govern the UI decision.
        var (conversionFixture, _) = StableHandResponseFixture.Create(DecisionKind.RescueDying, output, jijiu: true);
        using (conversionFixture)
        {
            var game = Program.Engine(conversionFixture);
            Program.Assert(game.PendingDecision!.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("response") == "peach" &&
                    choice.Parameters.GetValueOrDefault("conversion-skill-id") == "standard:jijiu"),
                "The real outside-turn fixture must offer its registered Jijiu rescue conversion.");
            var before = Boundary(game);
            Pump();
            Program.Assert(Boundary(game) == before,
                "A legal registered rescue conversion must remain available for the human to choose.");
        }
        Console.WriteLine("Automatic rescue pass: unavailable native rescue answered once while paused; legal Peach/conversion and replaced engines preserved; native result and cold replay matched.");

        void Save(GameCheckpoint checkpoint) => store.Write(GameSaveSlot.Manual,
            new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, checkpoint));
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static (GameEngine Game, ContentRegistry Registry, GameCheckpoint Available) CreateFailedDeckRescue()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new DeckRescuePackage());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = "identity:automatic-rescue", UseInteractiveSetup = true,
            UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 3
        }, registry);
        Accept(game, new StartGameCommand());
        Reach(DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, "fixture:auto-rescue-owner", game.Revision, game.PendingDecision!.PromptId));
        Reach(DecisionKind.PlayCard);
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
        Reach(DecisionKind.RescueDying);
        var available = game.CreateCheckpoint();
        Answer(choice => choice.Parameters.GetValueOrDefault("deck-basic-source") == "activate");
        Program.Assert(game.PendingDecision is { Kind: DecisionKind.ProgramTrigger } top &&
                       top.Choices.Count == 1 && top.Choices[0].Parameters.GetValueOrDefault("deck-basic-source") == "decline",
            "The fixed all-Slash deck must expose a real private top-deck view with no usable Peach.");
        Answer(choice => choice.Parameters.GetValueOrDefault("deck-basic-source") == "decline");
        return (game, registry, available);

        void Answer(Func<PromptChoice, bool> predicate)
        {
            var prompt = game.CreateSnapshot(0).PendingDecision!;
            Accept(game, new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.First(predicate).Id, game.Revision));
        }
        void Reach(DecisionKind kind)
        {
            for (var step = 0; step < 32; step++)
            {
                if (game.PendingDecision is { PlayerSeat: 0 } prompt && prompt.Kind == kind) return;
                if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards } discard)
                    Accept(game, new DiscardCardsCommand(0, discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray(),
                        discard.PromptId, game.Revision));
                else Accept(game, new AdvanceOneStepCommand(game.Revision));
            }
            throw new InvalidOperationException($"The fixed native rescue did not reach {kind}.");
        }
    }

    private sealed class DeckRescuePackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-automatic-rescue", new(1, 0, 0), []);

        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"fixture:auto-rescue-supply","revision":1,"cardPolicies":[{"id":"top-rescue","kind":"privateTopBasicRequest","cardKinds":["slash","dodge","peach","alcohol"],"value":2}]},
                {"id":"fixture:auto-rescue-dying","revision":1,"triggers":[{"id":"dying","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"loseHp","target":"owner","amount":4}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object>
                    {
                        ["fixture:auto-rescue-supply"] = new { name = "真实顶牌救援", description = "回合外观看顶牌救援" },
                        ["fixture:auto-rescue-dying"] = new { name = "真实濒死", description = "回合开始实际失去体力" }
                    } }));
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, id, id) { Program = program });
            builder.AddGeneral(new("fixture:auto-rescue-owner", "固定救援者", "supporter", "standard:none", "qun", 4,
                ["fixture:auto-rescue-supply"]));
            for (var seat = 1; seat < 4; seat++)
                builder.AddGeneral(new($"fixture:auto-rescue-{seat}", "固定濒死者", "supporter", "standard:none", "wei", 4,
                    ["fixture:auto-rescue-dying"]));
            builder.AddDeck(new("fixture:auto-rescue-deck", "无桃实体牌库", 4, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 80).Select(index =>
                    new ContentDeckPhysicalCard("standard:slash", Suit.Spade, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new("identity:automatic-rescue", "固定真实救援", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:auto-rescue-deck",
                GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:auto-rescue-owner", "fixture:auto-rescue-1", "fixture:auto-rescue-2", "fixture:auto-rescue-3"]));
        }
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "The native rescue preparation rejected a command.");
    }

    private static string Boundary(GameEngine game) => JsonSerializer.Serialize(new
    {
        game.Revision,
        Commands = CommandJson.Serialize(game.AcceptedCommands),
        game.CardMovements,
        Events = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(),
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray()
    });
}
