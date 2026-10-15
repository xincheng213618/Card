using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class TriggerWindowProtocolChecks
{
    private const string Skill = "fixture:window-protocol";
    private const string PaymentReason = "skill-program." + Skill + ".MoveBoundCards";

    public static void OrderedSkipAndTypedReturnsPreserveBoundaries()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        foreach (var skipGroup in new[] { true, false })
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
                ModeId = "identity:window-protocol", UseInteractiveSetup = true,
                AdvanceAfterHumanCommands = false, MaxTurns = 2
            }, registry);
            Accept(game, new StartGameCommand());
            Reach(game, prompt => prompt.Kind == DecisionKind.SelectGeneral);
            Accept(game, new SelectGeneralCommand(0, "fixture:window-owner", game.Revision, Pending(game)!.PromptId));
            Reach(game, prompt => IsActivation(prompt, "early"));
            var initialHand = game.CreateSnapshot(0).Players[0].HandCount;
            var initialHp = game.CreateSnapshot(0).Players[0].Hp;
            var early = Pending(game)!;
            Answer(game, choice => Action(choice) == "skip");
            Reach(game, prompt => prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("choice-group") == "opening"));
            var parent = game.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>()
                .Single(frame => frame.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow);
            Require(parent.CandidateIndex == 1 && parent.Candidates.Select(candidate => candidate.BindingId)
                    .SequenceEqual(["early", "group-a", "group-paid", "late"]),
                "Skipping the first candidate must advance exactly once and preserve the frozen priority/group order.");
            var beforeStale = State(game);
            Require(!game.Submit(new AnswerPromptCommand(0, early.PromptId, early.Choices[0].Id, game.Revision)).Accepted &&
                    State(game) == beforeStale,
                "A retired optional prompt cannot answer the next group or change its cursor.");

            if (skipGroup)
            {
                Answer(game, choice => Action(choice) == "skip");
                Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
                Require(Resolved(game).Select(item => item.BindingId)
                        .SequenceEqual(["early", "group-a", "group-paid", "late"]) &&
                        Resolved(game).Take(3).All(item => !item.Activated && !item.Completed) &&
                        Resolved(game).Last() is { Activated: true, Completed: true } &&
                        Started(game).Select(item => item.BindingId).SequenceEqual(["late"]) &&
                        game.CreateSnapshot(0).Players[0].HandCount == initialHand + 1 &&
                        !game.CardMovements.Any(move => move.Reason.Value == PaymentReason),
                    "Group skip must resolve both original branches once, pay nothing and continue after the entire group.");
                Replay(game, registry);
                continue;
            }

            Answer(game, choice => Action(choice) == "activate" &&
                choice.Parameters.GetValueOrDefault("binding-id") == "group-paid");
            Reach(game, prompt => prompt.Choices.Any(choice => Action(choice) == "select-owned-cards"));
            var selected = Pending(game)!.Choices.First(choice => Action(choice) == "select-owned-cards");
            var paidCard = selected.Cards.Single();
            var paidFrame = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.TriggerId == "group-paid");
            Require(paidFrame.WindowContext is { ResumeCandidateIndex: 3 } context && context.ParentFrameId == parent.Id,
                "The selected branch must retain the exact group return cursor and owning lifecycle frame.");
            Answer(game, choice => choice.Id == selected.Id);
            Reach(game, prompt => IsOption(prompt, "movement-continue"));
            Require(Payments(game, paidCard) == 1 && game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any() &&
                    game.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.Id == paidFrame.Id) &&
                    game.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(frame => frame.Id == parent.Id).CandidateIndex == 1 &&
                    !Resolved(game).Any(item => item.BindingId is "group-paid" or "late"),
                "The paid movement child must suspend the original branch and group cursor without completing either parent.");
            Replay(game, registry);
            var frozenPayment = State(game);
            var movementPrompt = Pending(game)!;
            Require(!game.Submit(new AnswerPromptCommand(0, movementPrompt.PromptId, new ChoiceId("unpublished"), game.Revision)).Accepted &&
                    State(game) == frozenPayment && Payments(game, paidCard) == 1,
                "A rejected child choice must not replay the already committed physical payment.");
            Answer(game, choice => choice.Parameters.GetValueOrDefault("option-id") == "movement-continue");
            Reach(game, prompt => IsOption(prompt, "hp-continue"));
            Require(Payments(game, paidCard) == 1 && game.CreateSnapshot(0).Players[0].Hp == initialHp - 1 &&
                    game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any() &&
                    game.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.Id == paidFrame.Id) &&
                    !Resolved(game).Any(item => item.BindingId is "group-paid" or "late"),
                "Returning from movement must resume at the next instruction and suspend again on exactly one real HP-change child.");
            Replay(game, registry);
            Answer(game, choice => choice.Parameters.GetValueOrDefault("option-id") == "hp-continue");
            Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
            // Turn-start historically advances one position on return. The paid
            // branch is then visited as an ineligible window item. That skipped
            // window fact is separate from the child program's single completion.
            Require(Resolved(game).Select(item => item.BindingId)
                    .SequenceEqual(["early", "group-a", "payment-seen", "hp-seen", "group-paid", "group-paid", "late"]) &&
                    Resolved(game).Where(item => item.Activated).All(item => item.Completed) &&
                    Resolved(game).Where(item => !item.Activated).Select(item => item.BindingId)
                        .SequenceEqual(["early", "group-a", "group-paid"]) &&
                    Resolved(game).Where(item => !item.Activated).All(item => !item.Completed) &&
                    Started(game).Select(item => item.BindingId)
                        .SequenceEqual(["group-paid", "payment-seen", "hp-seen", "late"]) &&
                    Resolved(game).Single(item => item.BindingId == "group-paid" && item.Activated).FrameId == paidFrame.Id &&
                    Resolved(game).Single(item => item.BindingId == "group-paid" && !item.Activated).FrameId == parent.Id &&
                    Payments(game, paidCard) == 1 && game.CreateSnapshot(0).Players[0].HandCount == initialHand + 1 &&
                    game.CreateSnapshot(0).Players[0].Hp == initialHp - 1 &&
                    !game.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.SkillId == Skill) &&
                    !game.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(frame => frame.Id == parent.Id),
                "Nested returns must complete each activated child once, preserve the historical window-level qualification skip and continue without repeating cost or HP loss. " +
                JsonSerializer.Serialize(new
                {
                    InitialHand = initialHand, Hand = game.CreateSnapshot(0).Players[0].HandCount,
                    InitialHp = initialHp, Hp = game.CreateSnapshot(0).Players[0].Hp,
                    PaymentCount = Payments(game, paidCard), PaidCard = paidCard, ParentId = parent.Id, PaidFrameId = paidFrame.Id,
                    Resolved = Resolved(game), Started = Started(game),
                    Frames = game.ResolutionStack.Select(frame => new { frame.Id, frame.Kind,
                        Binding = (frame as ProgramSkillFrame)?.TriggerId })
                }));
            Replay(game, registry);
        }
    }

    private static IReadOnlyList<ProgramBindingResolvedEvent> Resolved(GameEngine game) => game.Events
        .Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Where(item => item.SkillId == Skill).ToArray();
    private static IReadOnlyList<ProgramBindingStartedEvent> Started(GameEngine game) => game.Events
        .Select(item => item.Payload).OfType<ProgramBindingStartedEvent>().Where(item => item.SkillId == Skill).ToArray();
    private static int Payments(GameEngine game, int card) => game.CardMovements.Count(move => move.CardId == card &&
        move.From == CardLocation.Hand(0) && move.To == CardLocation.DiscardPile && move.Reason.Value == PaymentReason);
    private static string? Action(PromptChoice choice) => choice.Parameters.GetValueOrDefault("program-action");
    private static bool IsActivation(PendingDecision prompt, string binding) => prompt.Choices.Any(choice =>
        Action(choice) == "activate" && choice.Parameters.GetValueOrDefault("binding-id") == binding);
    private static bool IsOption(PendingDecision prompt, string option) => prompt.Choices.Any(choice =>
        choice.Parameters.GetValueOrDefault("option-id") == option);
    private static PendingDecision? Pending(GameEngine game) => game.PendingDecision ?? game.CreateSnapshot(0).PendingDecision;
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 80; step++)
        {
            if (Pending(game) is { } prompt)
            {
                if (predicate(prompt)) return;
                throw new InvalidOperationException("Unexpected protocol prompt: " + prompt.Kind + " / " + prompt.Prompt);
            }
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The protocol fixture did not reach its next command boundary.");
    }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    {
        var prompt = Pending(game)!;
        Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(predicate).Id, game.Revision));
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.Message ?? "The protocol fixture rejected its command.");
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Append(-1).Select(viewer => SnapshotJson.Serialize(game.CreateSnapshot(viewer))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack),
        Events = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(),
        game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics()
    });
    private static void Replay(GameEngine game, ContentRegistry registry) => Require(State(game) == State(GameReplay.Restore(
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry)),
        "Every actual player and spectator view, exact frames, paid movements and committed prefix must survive a cold restore.");
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("window-protocol-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Skill}}","revision":1,"triggers":[
                {"id":"early","window":"turnStartBeforeNormalFlow","subject":"owner","optional":true,"priority":30,"effects":[{"op":"draw","target":"owner","amount":1}]},
                {"id":"group-a","window":"turnStartBeforeNormalFlow","subject":"owner","optional":true,"priority":20,"choiceGroup":"opening","usageScope":"game","usageLimit":1,"effects":[{"op":"draw","target":"owner","amount":3}]},
                {"id":"group-paid","window":"turnStartBeforeNormalFlow","subject":"owner","optional":true,"priority":20,"choiceGroup":"opening","usageScope":"game","usageLimit":1,"effects":[
                {"op":"selectOwnedCards","target":"owner","amount":1,"zones":["hand"],"resultBind":"payment"},
                {"op":"moveBoundCards","target":"owner","sourceBind":"payment","destination":"discardPile","awaitMovementTriggers":true},
                {"op":"loseHp","target":"owner","amount":1},{"op":"draw","target":"owner","amount":1}]},
                {"id":"late","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"priority":10,"effects":[{"op":"draw","target":"owner","amount":1}]},
                {"id":"payment-seen","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{PaymentReason}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"movement-return","options":[{"id":"movement-continue"}]}]},
                {"id":"hp-seen","window":"afterHpLost","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-return","options":[{"id":"hp-continue"}]}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object> { [Skill] = new { name = "Window protocol", description = "Exact command boundaries",
                        triggerChoices = new Dictionary<string, string> { ["group-a"] = "Draw branch", ["group-paid"] = "Paid branch" },
                        optionLabels = new Dictionary<string, string> { ["movement-continue"] = "Return movement", ["hp-continue"] = "Return HP change" } } } }));
            builder.AddSkill(new(Skill, "Window protocol", "Exact command boundaries") { Program = catalog.Programs[Skill] });
            var generals = new[] { "fixture:window-owner", "fixture:window-peer-1", "fixture:window-peer-2", "fixture:window-peer-3" };
            foreach (var id in generals) builder.AddGeneral(new(id, id, "supporter", id == generals[0] ? Skill : "standard:none", "wei"));
            builder.AddDeck(new("fixture:window-deck", "Window deck", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 48).Select(index => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, index % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:window-protocol", "Window protocol", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:window-deck", GeneralCandidateCount: 4, GeneralPoolIds: generals));
        }
    }
}
