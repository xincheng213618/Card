using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class SharedPostEventChecks
{
    public static void GainsPauseBeforeTheNextInstructionAndReplay()
    {
        var (game, registry) = Create();
        var before = game.CreateSnapshot(0, true).Players[0].Hand.Count;
        Use(game, "draw");
        var window = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
        Require(window.ResumeProgramFrameId is not null && window.Contexts!.All(context =>
                context.Window == SkillProgramTriggerWindow.CardsGained), "Gains must suspend their producing instruction.");
        Require(game.CreateSnapshot(0, true).Players[0].Hp == game.CreateSnapshot(0, true).Players[0].MaxHp,
            "The next lose-HP instruction must wait for gained-card triggers.");
        Require(window.Batch.DestinationCounts!.Single(count => count.Location == CardLocation.Hand(0)).CountBefore == before,
            "Destination counts must be captured before the move.");
        var frozen = JsonSerializer.Serialize(window.Contexts);
        var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
        foreach (var branch in new[] { game, restored }) Drain(branch);
        Require(State(game) == State(restored), "Gained-card choices must replay without repeated draws.");
        Require(game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Count(item =>
                item.BindingId == "gain" && item.Activated) == 2,
            "Two gained cards must trigger twice; draws by this same skill must not recursively trigger itself.");
        Require(frozen == JsonSerializer.Serialize(window.Contexts), "Frozen movement facts must survive later draws.");
        Require(game.CreateSnapshot(0, true).Players[0].Hand.Count == before + 5,
            "Two gained cards, two gain rewards and one HP-loss reward must each resolve exactly once.");
    }

    public static void HpLossRecoveryAndActualAmountsReplay()
    {
        var (game, registry) = Create();
        Use(game, "wound");
        var loss = game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
        Require(loss.Change is { Kind: HpChangeKind.Loss, Amount: 2 } && loss.Candidates.Count == 2,
            "A two-point loss must retain one actual event with two per-point occurrences.");
        var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
        foreach (var branch in new[] { game, restored })
        {
            Answer(branch);
            Answer(branch);
            var recovery = branch.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
            Require(recovery.Change is { Kind: HpChangeKind.Recovery, Amount: 2 } &&
                    recovery.Change.HpAfter - recovery.Change.HpBefore == 2 && recovery.Candidates.Count == 1,
                "Overhealing must publish only the actual recovered amount, once per event.");
        }
        var recoveryReplay = GameReplay.Restore(game.CreateCheckpoint(), registry);
        foreach (var branch in new[] { game, restored, recoveryReplay }) Drain(branch);
        Require(State(game) == State(restored) && State(game) == State(recoveryReplay),
            "HP windows must replay from both loss and recovery prompts.");
        Require(game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                .Count(item => item.Window == SkillProgramTriggerWindow.AfterHpRecovered && item.Activated) == 1,
            "Recovery at full HP must not emit a second recovery event.");
    }

    public static void HpLossWaitsForDyingAndCardRecoveryFinishesFirst()
    {
        var (game, registry) = Create();
        Use(game, "dying");
        Require(game.PendingDecision?.Kind == DecisionKind.RescueDying &&
                !game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(),
            "After-loss effects must wait for dying rescue.");
        var rescue = game.PendingDecision!.Choices.First(choice => choice.Cards.Count != 0);
        Require(game.Submit(new AnswerPromptCommand(0, game.PendingDecision.PromptId, rescue.Id, game.Revision)).Accepted,
            "The owner must be able to rescue with Peach.");
        var recovery = game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
        Require(recovery.Change.Kind == HpChangeKind.Recovery && recovery.Continuation == PostEventContinuation.CardUse,
            "Peach recovery must expose its event before the use completes.");
        var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
        foreach (var branch in new[] { game, restored })
        {
            Answer(branch);
            Require(branch.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single().Change.Kind == HpChangeKind.Loss,
                "The suspended HP-loss event must resume after rescue completes.");
            Drain(branch);
        }
        Require(State(game) == State(restored), "Nested dying, recovery and after-loss must replay in the same order.");
    }

    public static void TransferFiltersAndBatchCountsUseTheRecipient()
    {
        foreach (var excluded in new[] { false, true })
        {
            var rules = Rules.Replace("\"movementOccurrence\":\"perCard\"", "\"movementOccurrence\":\"perBatch\"")
                .Replace("\"movementReasons\":[\"skill-program.fixture:driver.Draw\",\"skill-program.fixture:observer.Draw\"]",
                    excluded ? "\"excludedMovementReasons\":[\"skill-program.fixture:driver.GiveSelected\"]" :
                    "\"movementReasons\":[\"skill-program.fixture:driver.GiveSelected\"]");
            var (game, registry) = Create(rules);
            var cards = game.CreateSnapshot(0, true).Players[0].Hand.Take(2).Select(card => card.Id).ToArray();
            Require(game.Submit(new UseProgramSkillCommand(0, "fixture:driver", "give", cards, [1],
                game.Revision, game.PendingDecision!.PromptId)).Accepted, "Transfer activation failed.");
            if (excluded)
            {
                Require(!game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(),
                    "An excluded move reason must not start a gained-card trigger.");
                Drain(game);
                continue;
            }
            var movement = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
            var context = movement.Contexts!.Single();
            Require(context.OwnerSeat == 1 && context.Facts is { MovedCardCount: 2, DestinationZoneCountBefore: 3, DestinationZoneCountAfter: 5 },
                "One transfer batch must belong to its recipient with actual before/after counts.");
            Require(movement.Batch.AwaitingProgramFrameId is not null && movement.Candidates.Count == 1,
                "A two-card transfer with perBatch must produce exactly one awaited candidate.");
            var json = JsonSerializer.Serialize<ResolutionFrame>(movement);
            Require(JsonSerializer.Serialize(JsonSerializer.Deserialize<ResolutionFrame>(json)) == json,
                "Frozen recipient contexts and parent continuation must serialize exactly.");
            var replay = GameReplay.Restore(game.CreateCheckpoint(), registry);
            foreach (var branch in new[] { game, replay }) Drain(branch);
            Require(State(game) == State(replay), "Cross-owner transfer triggers must replay exactly.");
        }
    }

    public static void DamageDoesNotBecomeHpLossAndGroupRecoveryWaitsPerTarget()
    {
        var (damageGame, _) = Create();
        var hp = damageGame.CreateSnapshot(0, true).Players[0].Hp;
        Use(damageGame, "damage");
        for (var i = 0; i < 20 && damageGame.ResolutionStack.Count != 0; i++)
            Require(damageGame.Submit(new AdvanceCommand(damageGame.Revision)).Accepted, "Damage must finish.");
        Require(damageGame.CreateSnapshot(0, true).Players[0].Hp == hp - 1 &&
                !damageGame.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(item =>
                    item.Window == SkillProgramTriggerWindow.AfterHpLost),
            "Damage must not dispatch the lose-HP window.");

        var (game, registry) = Create(cardId: "standard:peach_garden");
        foreach (var seat in new[] { 0, 1 })
        {
            Require(game.Submit(new UseProgramSkillCommand(0, "fixture:driver", "wound-target", [], [seat],
                game.Revision, game.PendingDecision!.PromptId)).Accepted, "Target wound failed.");
            Drain(game);
            Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted, "Return to Play failed.");
        }
        var action = game.GetHumanLegalActions().First(item => item.CardId is not null);
        Require(game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId)).Accepted, "Group recovery use failed.");
        for (var i = 0; i < 30 && !game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(); i++)
        {
            var prompt = game.PendingDecision;
            Require(game.Submit(prompt is null ? new AdvanceCommand(game.Revision) :
                new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.Last().Id, game.Revision)).Accepted,
                "Group recovery must reach its first recovery event.");
        }
        var window = game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
        Require(window.Continuation == PostEventContinuation.GroupRecovery && window.Change.TargetSeat == 0 &&
                game.CreateSnapshot(0, true).Players[1].Hp < game.CreateSnapshot(0, true).Players[1].MaxHp,
            "The next target must wait for this target's after-recovery choices.");
        var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
        foreach (var branch in new[] { game, restored })
        {
            for (var i = 0; i < 50 && branch.ResolutionStack.Count != 0; i++)
            {
                if (branch.PendingDecision?.Kind == DecisionKind.ProgramTrigger) Answer(branch);
                else Require(branch.Submit(new AdvanceCommand(branch.Revision)).Accepted, "Group recovery continuation failed.");
            }
            Require(branch.ResolutionStack.Count == 0, "Group recovery must finish after both callbacks.");
        }
        Require(State(game) == State(restored), "A group recovery must replay through both target callbacks.");
    }

    public static void DefinitionFiltersRejectWrongContexts()
    {
        var valid = Rules;
        _ = SkillProgramCatalog.Load(valid, Presentation);
        Reject(valid.Replace("\"destinationZones\":[\"hand\"]", "\"destinationZones\":[\"equipment\"]"), "hand destination");
        Reject(valid.Replace("\"window\":\"cardsGained\"", "\"window\":\"turnEnding\""), "only by cardsMoved");
        Reject(valid.Replace("\"hpChangeOccurrence\":\"perPoint\"", "\"hpChangeOccurrence\":\"perCard\""), "hpChangeOccurrence");
        Reject(valid.Replace("\"window\":\"afterHpLost\"", "\"window\":\"turnEnding\""), "afterHpLost");
        Reject(valid.Replace("\"movementReasons\":[\"skill-program.fixture:driver.Draw\",\"skill-program.fixture:observer.Draw\"]",
            "\"movementReasons\":[\"bad\"]"), "namespaced");
    }

    private static (GameEngine, ContentRegistry) Create(string rules = Rules, string cardId = "standard:peach")
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(rules, cardId));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = "fixture:post-event", UseInteractiveSetup = false,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Fixture start failed.");
        for (var index = 0; index < 50 && game.PendingDecision?.Kind != DecisionKind.PlayCard; index++)
            Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted, "Fixture advance failed.");
        return (game, registry);
    }

    private static void Use(GameEngine game, string id)
    {
        Require(game.Submit(new UseProgramSkillCommand(0, "fixture:driver", id, [], [],
            game.Revision, game.PendingDecision!.PromptId)).Accepted, $"Activation {id} failed.");
    }

    private static void Answer(GameEngine game)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("Missing trigger prompt.");
        var choice = prompt.Choices.Single(item => item.Parameters.GetValueOrDefault("program-action") == "activate");
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Trigger answer failed.");
    }

    private static void Drain(GameEngine game)
    {
        for (var index = 0; index < 40 && game.ResolutionStack.Count != 0; index++)
        {
            if (game.PendingDecision?.Kind == DecisionKind.ProgramTrigger) Answer(game);
            else Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted, "An AI post-event trigger must advance.");
        }
        Require(game.ResolutionStack.Count == 0, "Shared post-event windows must completely resume their parent.");
    }

    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, true));
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(string rules, string fragment)
    {
        try { _ = SkillProgramCatalog.Load(rules, Presentation); }
        catch (InvalidOperationException error) when (error.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new InvalidOperationException($"Expected definition rejection containing {fragment}.");
    }

    private const string Rules = """
        {"schemaVersion":62,"skills":[
         {"id":"fixture:driver","revision":1,"minimumRulesVersion":176,"activations":[
          {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,
           "effects":[{"op":"draw","target":"owner","amount":2},{"op":"loseHp","target":"owner","amount":1}]},
          {"id":"wound","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,
           "effects":[{"op":"loseHp","target":"owner","amount":2},{"op":"recover","target":"owner","amount":9},
            {"op":"recover","target":"owner","amount":1}]},
          {"id":"give","minCards":2,"maxCards":2,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":1,
           "effects":[{"op":"giveSelected","target":"selectedTarget","amount":2},{"op":"loseHp","target":"owner","amount":1}]},
          {"id":"damage","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,
           "effects":[{"op":"damage","target":"owner","amount":1}]},
          {"id":"wound-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":3,
           "effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
          {"id":"dying","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,
           "effects":[{"op":"loseHp","target":"owner","amount":9}]}]},
         {"id":"fixture:observer","revision":1,"minimumRulesVersion":176,"triggers":[
          {"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],
           "movementOccurrence":"perCard","ignoreOwnSkillMovements":true,
           "movementReasons":["skill-program.fixture:driver.Draw","skill-program.fixture:observer.Draw"],
           "optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]},
          {"id":"loss","window":"afterHpLost","subject":"owner","hpChangeOccurrence":"perPoint",
           "optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]},
          {"id":"recovery","window":"afterHpRecovered","subject":"owner","optional":true,
           "condition":{"kind":"compare","left":{"kind":"hpChangeAmount"},"operator":"greaterThan","right":{"kind":"integerConstant","value":0}},
           "effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
        """;
    private const string Presentation = """
        {"schemaVersion":3,"skills":{"fixture:driver":{"name":"Driver","description":"Test"},
         "fixture:observer":{"name":"Observer","description":"Test"}}}
        """;

    private sealed class Fixture(string rules, string cardId) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("post-event-fixture", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(rules, Presentation);
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new ContentSkillDefinition(id, id, "Fixture") { Program = program });
            var generals = Enumerable.Range(0, 4).Select(index => $"fixture:post-event-{index}").ToArray();
            foreach (var id in generals)
                builder.AddGeneral(new ContentGeneralDefinition(id, "Test", "supporter", "fixture:driver",
                    BaseHp: 4, AdditionalSkillIds: ["fixture:observer"]));
            builder.AddDeck(new ContentDeckRecipe("fixture:post-event-deck", "Fixture", 3, 0,
                [new ContentDeckCardCount(cardId, 100)]));
            builder.AddMode(new ContentModeDefinition("fixture:post-event", "Fixture", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 },
                "fixture:post-event-deck", GeneralCandidateCount: 1, GeneralPoolIds: generals));
        }
    }
}
