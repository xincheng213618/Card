using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class FrameOwnedBatchChecks
{
    public static void StrategicFirstDamageInstructionKeepsCommittedCursorAcrossChildren()
    {
        foreach (var afterDamage in new[] { false, true })
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(),
                new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
                new StandardClassicGeneralPackage(),
                new Fixture(false, firstInstruction: true, afterDamagePause: afterDamage));
            Require(registry.Skills["fixture:batch-damage"].Program!.Activations.Single().Effects[0].Op ==
                    SkillProgramEffectOp.DamageOtherLiving,
                "The regression must suspend a real first damage instruction without a preceding cursor increment.");
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
                ModeId = "identity:classic-frame-batch-check", UseInteractiveSetup = true,
                UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 10
            }, registry);
            Submit(game, new StartGameCommand());
            Reach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
            Submit(game, new SelectGeneralCommand(0, "fixture:batch-owner", game.Revision, game.PendingDecision!.PromptId));
            Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
            game = Cold(game, registry);
            var initialHp = game.CreateSnapshot(0).Players.Select(player => player.Hp).ToArray();
            var initialHand = game.CreateSnapshot(0).Players[0].HandCount;
            var initialRng = Rng(game);
            Submit(game, new UseProgramSkillCommand(0, "fixture:batch-damage", "all-others", [], [],
                game.Revision, game.PendingDecision!.PromptId));
            var parentId = game.Events.Select(envelope => envelope.Payload).OfType<ProgramSkillStartedEvent>()
                .Single(item => item.SkillId == "fixture:batch-damage").FrameId;
            var pauses = 0;
            for (var step = 0; step < 100 && game.ResolutionStack.Count > 0; step++)
            {
                if (game.PendingDecision is { } decision)
                {
                    var parent = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.Id == parentId);
                    var child = game.ResolutionStack.OfType<ProgramSkillFrame>()
                        .Single(frame => frame.SkillId == "fixture:batch-pause");
                    var target = pauses + 1;
                    Require(pauses < 3 && parent.InstructionIndex == 1 &&
                            parent.StrategicDamageBatch is { InstructionIndex: 1 } batch &&
                            batch.RemainingTargetSeats.SequenceEqual(Enumerable.Range(target + 1, 3 - target)) &&
                            parent.AttackAttempt is { SourceSeat: 0 } attempt && attempt.TargetSeat == target &&
                            child.WindowContext?.Window == (afterDamage
                                ? SkillProgramTriggerWindow.AfterDamageApplied : SkillProgramTriggerWindow.BeforeDamageApplied),
                        "Each genuine before/after-damage child must retain the first committed parent and its ordered remaining targets.");
                    var applied = game.Events.Select(envelope => envelope.Payload).OfType<DamageAppliedEvent>()
                        .Where(item => item.SourceSeat == 0).ToArray();
                    Require(applied.Select(item => item.TargetSeat).SequenceEqual(
                                Enumerable.Range(1, afterDamage ? target : target - 1)) &&
                            game.CreateSnapshot(0).Players[target].Hp == initialHp[target] - (afterDamage ? 1 : 0) &&
                            game.CreateSnapshot(0).Players[0].HandCount == initialHand &&
                            !game.CardMovements.Any(move => move.Reason.Value == "skill-program.fixture:batch-damage.Draw") &&
                            Rng(game) == initialRng,
                        "The paused damage window must expose the exact paid HP prefix without drawing or consuming another random value.");
                    Private(game);
                    game = Cold(game, registry);
                    decision = game.PendingDecision!;
                    var choice = decision.Choices.Single(item =>
                        item.Parameters.GetValueOrDefault("program-action") == "choose-option");
                    Submit(game, new AnswerPromptCommand(decision.PlayerSeat, decision.PromptId, choice.Id, game.Revision));
                    pauses++;
                }
                else Submit(game, new AdvanceOneStepCommand(game.Revision));
            }
            var damage = game.Events.Select(envelope => envelope.Payload).OfType<DamageAppliedEvent>()
                .Where(item => item.SourceSeat == 0).ToArray();
            var resolved = game.Events.Select(envelope => envelope.Payload).OfType<ProgramSkillResolvedEvent>()
                .Where(item => item.FrameId == parentId).ToArray();
            Require(pauses == 3 && game.ResolutionStack.Count == 0 &&
                    damage.Select(item => item.TargetSeat).SequenceEqual(new[] { 1, 2, 3 }) && damage.All(item => item.Amount == 1) &&
                    resolved is [{ Completed: true }] &&
                    Enumerable.Range(1, 3).All(seat => game.CreateSnapshot(0).Players[seat].Hp == initialHp[seat] - 1) &&
                    game.CreateSnapshot(0).Players[0].Hp == initialHp[0] &&
                    game.CreateSnapshot(0).Players[0].HandCount == initialHand + 1 &&
                    game.CardMovements.Count(move => move.Reason.Value == "skill-program.fixture:batch-damage.Draw") == 1 &&
                    Rng(game) == initialRng,
                "Cold child returns must hit every frozen target once, retire the parent once and pay its following draw once.");
            Private(game);
            _ = Cold(game, registry);
        }
    }

    public static void StrategicDamageCompletesAndCancelsWithItsFrame()
    {
        foreach (var cancels in new[] { false, true })
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(),
                new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
                new StandardClassicGeneralPackage(), new Fixture(cancels));
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
                ModeId = "identity:classic-frame-batch-check", UseInteractiveSetup = true,
                UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 10
            }, registry);
            Submit(game, new StartGameCommand());
            Reach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
            Submit(game, new SelectGeneralCommand(0, "fixture:batch-owner", game.Revision, game.PendingDecision!.PromptId));
            Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
            Submit(game, new UseProgramSkillCommand(0, "fixture:batch-damage", "all-others", [], [],
                game.Revision, game.PendingDecision!.PromptId));

            var observedBatch = false;
            for (var step = 0; step < 100 && game.ResolutionStack.Count > 0; step++)
            {
                if (game.ResolutionStack.OfType<ProgramSkillFrame>()
                    .SingleOrDefault(frame => frame.SkillId == "fixture:batch-damage") is { StrategicDamageBatch: { } batch })
                {
                    observedBatch = true;
                    Require(batch.InstructionIndex == 2 && batch.RemainingTargetSeats.All(seat => seat != 0),
                        "A nested damage window must preserve only the unprocessed frozen targets on its program.");
                    var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
                    Require(JsonSerializer.Serialize(game.ResolutionStack) == JsonSerializer.Serialize(restored.ResolutionStack),
                        "Checkpoint replay must reconstruct the same parent batch during a child window.");
                }
                if (game.PendingDecision is { } decision)
                {
                    var choice = decision.Kind == DecisionKind.RescueDying
                        ? decision.Choices.First(item => item.Cards.Count == 0)
                        : decision.Choices.Single(item => item.Parameters.GetValueOrDefault("program-action") == "choose-option");
                    Submit(game, new AnswerPromptCommand(decision.PlayerSeat, decision.PromptId, choice.Id, game.Revision));
                }
                else Submit(game, new AdvanceOneStepCommand(game.Revision));
            }
            Require(observedBatch && game.ResolutionStack.Count == 0,
                "The observed batch and its children must retire together on completion or cancellation.");
            var damage = game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                .Where(item => item.SourceSeat == 0).ToArray();
            Require(damage.Length == (cancels ? 1 : 3) && damage.Select(item => item.TargetSeat).Distinct().Count() == damage.Length,
                $"Death-triggered skill loss must cancel the remaining batch; ordinary completion must hit each frozen target once. cancels={cancels}, damage={JsonSerializer.Serialize(damage)}");
            Require(game.CardMovements.Count(move => move.Reason.Value == "skill-program.fixture:batch-damage.Draw") == (cancels ? 0 : 1),
                "A cancelled program must not pay its later draw; a completed program must pay it once.");
            var copy = GameReplay.Restore(game.CreateCheckpoint(), registry);
            Require(game.Events.Select(EventBytes).SequenceEqual(copy.Events.Select(EventBytes)) &&
                    Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) ==
                        SnapshotJson.Serialize(copy.CreateSnapshot(seat))),
                "Completion and nested skill loss must preserve exact event bytes and every viewer on replay.");
        }
        ConditionalBatchRetiresBeforeFollowingInstruction();
    }

    private static void ConditionalBatchRetiresBeforeFollowingInstruction()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new Fixture(false, conditionChanges: true));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = "identity:classic-frame-batch-check", UseInteractiveSetup = true,
            UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 10
        }, registry);
        Submit(game, new StartGameCommand());
        Reach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Submit(game, new SelectGeneralCommand(0, "fixture:batch-owner", game.Revision, game.PendingDecision!.PromptId));
        Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        var initialHp = game.CreateSnapshot(0).Players[0].Hp;
        Require(initialHp == game.CreateSnapshot(0).Players[0].MaxHp,
            "The conditional first batch must start while its owner is unwounded.");
        Submit(game, new UseProgramSkillCommand(0, "fixture:batch-damage", "all-others", [], [],
            game.Revision, game.PendingDecision!.PromptId));

        var observedChangedCondition = false;
        var observedFollowingBatch = false;
        for (var step = 0; step < 100 && game.ResolutionStack.Count > 0; step++)
        {
            if (game.ResolutionStack.OfType<ProgramSkillFrame>()
                .SingleOrDefault(frame => frame.SkillId == "fixture:batch-damage") is { StrategicDamageBatch: { } batch })
            {
                Require(batch.InstructionIndex is 2 or 3 && batch.RemainingTargetSeats.All(seat => seat != 0),
                    "Every surviving batch must belong to its own mass-damage instruction.");
                if (batch.InstructionIndex == 2)
                {
                    Require(batch.RemainingTargetSeats.SequenceEqual(new[] { 2, 3 }),
                        "The first batch must retain its frozen remaining seats while its damage child is active.");
                    if (game.PendingDecision is { } pause &&
                        pause.Choices.Any(item => item.Parameters.GetValueOrDefault("program-action") == "choose-option"))
                    {
                        observedChangedCondition = game.CreateSnapshot(0).Players[0].Hp < initialHp;
                        Require(observedChangedCondition && game.ResolutionStack.Count > 1,
                            "The first damage child must make its owner's condition false before returning.");
                    }
                }
                else observedFollowingBatch = true;
                var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
                Require(JsonSerializer.Serialize(game.ResolutionStack) == JsonSerializer.Serialize(restored.ResolutionStack),
                    "Replay must preserve the correct instruction-owned batch throughout both child windows.");
            }
            if (game.PendingDecision is { } decision)
            {
                var choice = decision.Choices.Single(item => item.Parameters.GetValueOrDefault("program-action") == "choose-option");
                Submit(game, new AnswerPromptCommand(decision.PlayerSeat, decision.PromptId, choice.Id, game.Revision));
            }
            else Submit(game, new AdvanceOneStepCommand(game.Revision));
        }
        Require(observedChangedCondition && observedFollowingBatch && game.ResolutionStack.Count == 0,
            "Skipping the conditional repeat must retire it and let the next unconditional batch complete.");
        var damage = game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
            .Where(item => item.SourceSeat == 0).ToArray();
        Require(damage.Select(item => item.TargetSeat).SequenceEqual(new[] { 1, 1, 2, 3 }) &&
                damage.All(item => item.Amount == 1),
            "The skipped first batch must hit only its initial seat; the next batch must independently hit every other seat once.");
        Require(game.CreateSnapshot(0).Players[0].Hp == initialHp - 4 &&
                game.CardMovements.Count(move => move.Reason.Value == "skill-program.fixture:batch-damage.Draw") == 1,
            "Each resolved damage child must pay its HP loss once and the completed parent must draw once.");
        var copy = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(game.Events.Select(EventBytes).SequenceEqual(copy.Events.Select(EventBytes)) &&
                Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) ==
                    SnapshotJson.Serialize(copy.CreateSnapshot(seat))),
            "Conditional batch retirement must replay the same ordered facts, card movement and every viewer.");
    }

    private static string EventBytes(EventEnvelope envelope) => JsonSerializer.Serialize(new
    {
        envelope.Id, envelope.ParentId, envelope.Sequence, envelope.Revision, envelope.CorrelationId,
        PayloadType = envelope.Payload.GetType().FullName,
        Payload = JsonSerializer.Serialize(envelope.Payload, envelope.Payload.GetType())
    });

    private static uint Rng(GameEngine game)
    {
        var random = typeof(GameEngine).GetField("_random",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(game)!;
        return (uint)random.GetType().GetProperty("State")!.GetValue(random)!;
    }

    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        game.Revision, RandomState = Rng(game),
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack),
        Events = game.Events.Select(EventBytes).ToArray(), game.CardMovements,
        Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics(),
        Checkpoint = GameCheckpointJson.Serialize(game.CreateCheckpoint())
    });

    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(game) == State(restored),
            "Cold checkpoint restoration must preserve four safe views, exact frames, event bytes, payments, RNG, commands and content identity.");
        return restored;
    }

    private static void Private(GameEngine game)
    {
        foreach (var seat in Enumerable.Range(0, 4))
            Require(game.CreateSnapshot(seat).Players.Where(player => player.Seat != seat).All(player => player.Hand.Count == 0),
                "Damage child and batch diagnostics must not expose foreign hand entities in any player view.");
    }

    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 30; step++)
        {
            if (game.PendingDecision is { } decision && predicate(decision)) return;
            Submit(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The verified small fixture did not reach its setup/play boundary.");
    }

    private static void Submit(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.Message ?? "The batch fixture command was rejected.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture(bool cancels, bool conditionChanges = false,
        bool firstInstruction = false, bool afterDamagePause = false) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-frame-batches", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var rules = $$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
                  {"id":"fixture:batch-damage","revision":1,"activations":[
                    {"id":"all-others","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,
                     "targetKind":"anyLiving","usesPerTurn":1,"effects":[
                       {"op":"setMarkerAmount","target":"owner","amount":0,"marker":"junlue"},
                       {"op":"damageOtherLiving","target":"owner","amount":1},
                       {"op":"draw","target":"owner","amount":1}]}]},
                  {"id":"fixture:batch-pause","revision":1,"triggers":[
                    {"id":"pause-before-damage","window":"beforeDamageApplied","subject":"damageSource","optional":false,
                     "effects":[{"op":"chooseOption","target":"owner","resultBind":"damage-pause","options":[{"id":"continue"}]}]}]}]}
                """;
            if (conditionChanges)
            {
                var document = System.Text.Json.Nodes.JsonNode.Parse(rules)!;
                var effects = document["skills"]![0]!["activations"]![0]!["effects"]!.AsArray();
                effects[1]!["condition"] = System.Text.Json.Nodes.JsonNode.Parse("""{"kind":"not","children":[{"kind":"wounded"}]}""");
                effects.Insert(2, System.Text.Json.Nodes.JsonNode.Parse("""{"op":"damageOtherLiving","target":"owner","amount":1}"""));
                document["skills"]![1]!["triggers"]![0]!["effects"]!.AsArray().Insert(0,
                    System.Text.Json.Nodes.JsonNode.Parse("""{"op":"loseHp","target":"owner","amount":1}"""));
                rules = document.ToJsonString();
            }
            if (firstInstruction || afterDamagePause)
            {
                var document = System.Text.Json.Nodes.JsonNode.Parse(rules)!;
                if (firstInstruction)
                    document["skills"]![0]!["activations"]![0]!["effects"]!.AsArray().RemoveAt(0);
                if (afterDamagePause)
                {
                    var trigger = document["skills"]![1]!["triggers"]![0]!;
                    trigger["window"] = "afterDamageApplied";
                    trigger["damageOccurrence"] = "perDamage";
                }
                rules = document.ToJsonString();
            }
            var catalog = SkillProgramCatalog.Load(rules, """{"schemaVersion":3,"skills":{"fixture:batch-damage":{"name":"群体结算","description":"逐人伤害后摸牌"},"fixture:batch-pause":{"name":"伤害暂停","description":"伤害前暂停","optionLabels":{"continue":"继续"}}}}""");
            builder.AddSkill(new("fixture:batch-damage", "群体结算", "逐人伤害后摸牌")
                { Program = catalog.Programs["fixture:batch-damage"] });
            builder.AddSkill(new("fixture:batch-pause", "伤害暂停", "伤害前暂停")
                { Program = catalog.Programs["fixture:batch-pause"] });
            builder.AddGeneral(new("fixture:batch-owner", "结算持有人", "supporter", "fixture:batch-damage", "shu", 20,
                ["fixture:batch-pause"]));
            for (var seat = 1; seat < 4; seat++)
                builder.AddGeneral(new($"fixture:batch-{seat}", $"结算目标{seat}", "supporter",
                    cancels ? "classic:duanchang" : "standard:none", "wei", cancels ? 1 : 20));
            builder.AddDeck(new("fixture:batch-deck", "小结算牌堆", 8, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 80).Select(index =>
                    new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new("identity:classic-frame-batch-check", "结算帧测试", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:batch-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:batch-owner", "fixture:batch-1", "fixture:batch-2", "fixture:batch-3"]));
        }
    }
}
