using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShanheS5ZhangheChecks
{
    private const string Skill = StandardShanheS5ZhanghePackage.ZhouxuanId;
    private const string Owner = "fixture:s5-zhanghe-owner", Quiet = "fixture:s5-zhanghe-quiet";
    private const string Child = "fixture:s5-zhanghe-draw-child", Mode = "identity:classic-s5-zhanghe";

    public static void UncappedPersistentPileAndLargeNativeUseDrawResumeCold()
    {
        var (g, registry) = Start();
        Play(g);
        g = Deposit(g, 8, registry);
        Need(View(g, 0).PrivateWhirlPiles is [{ Count: 8, Cards.Count: 8 }] &&
            Enumerable.Range(1, 3).All(seat => g.CreateSnapshot(seat).Players[0].PrivateWhirlPiles is [{ Count: 8, Cards: null }]),
            "Eight real hand entities become private Whirl, without inheriting the OL five-card cap.");

        NextPlay(g, 2);
        Need(View(g, 0).PrivateWhirlPiles is [{ Count: 8 }], "The original eight remain available next turn.");
        g = Deposit(g, 1, registry);
        NextPlay(g, 3);
        Need(View(g, 0).PrivateWhirlPiles is [{ Count: 9 }] &&
            !Events<PrivateWhirlRemovedEvent>(g).Any(),
            "Whirl survives the entire next Play ending and accumulates a new deposit instead of expiring or replacing the old pile.");
        g = Cold(g, registry);

        var beforeHand = View(g, 0).Hand.Count;
        var beforeHp = View(g, 1).Hp;
        var slash = g.GetHumanLegalActions().First(action => action.PlayedCardKind is null or CardKind.Slash &&
            action.CardId is { } id && View(g, 0).Hand.Any(card => card.Id == id && card.Kind == CardKind.Slash) &&
            action.TargetSeats.SequenceEqual([1]));
        Accept(g, new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, g.Revision, Prompt(g)!.PromptId,
            slash.PlayedCardKind, slash.TargetCardId));
        Reach(g, () => WhirlChoice(g, PrivateWhirlStage.SelectRemove));
        Answer(g, _ => true);
        Reach(g, () => Prompt(g)?.SkillPrompt?.SkillId == Child);
        var first = WhirlRoot(g);
        Need(first.PrivateWhirl is { Stage: PrivateWhirlStage.FirstDrawChildren, DrawCount: 1 } &&
            View(g, 0).PrivateWhirlPiles is [{ Count: 8 }],
            "The real use pays one removal before its first native draw and awaits that draw's actual child.");
        g = Cold(g, registry);
        Continue(g);
        Reach(g, () => Prompt(g)?.SkillPrompt?.SkillId == Child);
        Need(WhirlRoot(g).PrivateWhirl is
            { Stage: PrivateWhirlStage.ExtraDrawChildren, RemainingCount: 8, NotUniqueMaximum: true, DrawCount: 8 },
            "After the first draw and its child, the current hand comparison funds eight extra cards from the remaining eight Whirl.");
        g = Cold(g, registry);
        Continue(g);
        Play(g);

        var segments = Events<PrivateWhirlSegmentIssuedEvent>(g).Where(item => item.ActionId is not null).ToArray();
        Need(segments.Select(item => (item.Stage, item.Count)).SequenceEqual(new[]
            {
                (PrivateWhirlStage.RemovalChildren, 1),
                (PrivateWhirlStage.FirstDrawChildren, 1),
                (PrivateWhirlStage.ExtraDrawChildren, 8)
            }) && Events<PrivateWhirlRemovedEvent>(g).Count() == 1 &&
            View(g, 0).Hand.Count == beforeHand + 8 && View(g, 1).Hp == beforeHp - 1 &&
            View(g, 0).PrivateWhirlPiles is [{ Count: 8 }],
            "Restoring both paid children never repeats removal or draws, and the original physical Slash still resolves once.");
        _ = Cold(g, registry);
    }

    private static PlayerSnapshot View(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? Prompt(GameEngine g) => Enumerable.Range(0, 4)
        .Select(seat => g.CreateSnapshot(seat).PendingDecision).FirstOrDefault(prompt => prompt is not null);
    private static IEnumerable<T> Events<T>(GameEngine g) where T : IGameEvent => g.Events.Select(item => item.Payload).OfType<T>();
    private static ProgramSkillFrame WhirlRoot(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>()
        .Single(frame => frame.PrivateWhirl is not null);
    private static bool WhirlChoice(GameEngine g, PrivateWhirlStage stage) =>
        g.ResolutionStack.LastOrDefault() is ProgramSkillFrame { PrivateWhirl: { } receipt } && receipt.Stage == stage &&
        Prompt(g)?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "private-whirl") == true;
    private static void Need(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
    private static void Accept(GameEngine g, GameCommand command)
    {
        var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Need(result.Accepted, result.Error?.Message ?? "Rejected native S5 Whirl command.");
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> match)
    {
        var prompt = Prompt(g)!;
        Accept(g, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(match).Id, g.Revision));
    }
    private static void Continue(GameEngine g) => Answer(g, choice => choice.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, Prompt(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => Prompt(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void NextPlay(GameEngine g, int turn) => Reach(g, () => Prompt(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } &&
        Events<TurnStartedEvent>(g).Count(item => item.ActorSeat == 0) >= turn);

    private static GameEngine Deposit(GameEngine g, int count, ContentRegistry registry)
    {
        End(g);
        Reach(g, () => Prompt(g)?.SkillPrompt?.SkillId == Skill && Prompt(g)!.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Answer(g, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, () => WhirlChoice(g, PrivateWhirlStage.SelectDeposit));
        for (var index = 0; index < count; index++)
        {
            Answer(g, choice => choice.Parameters["branch"].StartsWith("add-", StringComparison.Ordinal));
            if (index == 5) g = Cold(g, registry);
        }
        Answer(g, choice => choice.Parameters["branch"] == "confirm");
        Reach(g, () => !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.PrivateWhirl is not null));
        return g;
    }

    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 240; step++)
        {
            if (done()) return;
            Need(g.State.Winner == Winner.None, "Small S5 Whirl fixture ended before its expected boundary.");
            var prompt = Prompt(g);
            // Native DrawCards may expose several actual gain batches. The two
            // representative paid-child pauses above remain explicit; drain any
            // further observer children before expecting the original use back.
            if (prompt?.SkillPrompt?.SkillId == Child) Continue(g);
            else if (prompt is { PlayerSeat: 0 } && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip"))
                Answer(g, choice => choice.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (prompt is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
            else if (prompt is { PlayerSeat: 0, Kind: DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification })
                Answer(g, choice => choice.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Small S5 Whirl fixture exceeded its command bound: " + JsonSerializer.Serialize(Prompt(g)));
    }

    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(g.CreateSnapshot(seat))),
        Frames = g.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())),
        Events = g.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())),
        Commands = CommandJson.Serialize(g.AcceptedCommands)
    });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(State(g) == State(restored), "Cold recovery retains the exact unbounded private selection, persistent pile and once-paid native draw child.");
        return restored;
    }
    private static (GameEngine, ContentRegistry) Start()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardShanheS5ZhanghePackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 16
        }, registry);
        Accept(g, new StartGameCommand());
        Reach(g, () => Prompt(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, Prompt(g)!.PromptId));
        return (g, registry);
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("s5-zhanghe-native-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Quiet}}","revision":1,"triggers":[
                    {"id":"skip-play","window":"afterNormalDraw","subject":"owner","optional":false,
                     "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
                  {"id":"{{Child}}","revision":1,"triggers":[
                    {"id":"paid-draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],
                     "movementOccurrence":"perBatch","movementReasons":["skill-program.private-whirl.draw"],"optional":false,
                     "effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}]}]}
                ]}
                """;
            var labels = new Dictionary<string, object>
            {
                [Quiet] = new { name = "静默", description = "跳过其他角色出牌" },
                [Child] = new { name = "真实摸牌子步骤", description = "等待已发出摸牌后的原生子步骤", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
            };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                {
                    Program = program, ProgramPresentation = catalog.Presentations[id],
                    SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => id == Quiet ? 10000d : -10000d)
                });
            builder.AddGeneral(new(Owner, "专版周旋拥有者", "supporter", Skill, "qun", 12, [Child]));
            var peers = Enumerable.Range(1, 3).Select(index => $"fixture:s5-zhanghe-peer-{index}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "真实静默其他角色", "supporter", Quiet, "wei", 12));
            builder.AddDeck(new("fixture:s5-zhanghe-deck", "固定原生牌", 8, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 128).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Diamond, 7)).ToArray()
            });
            builder.AddMode(new(Mode, "专版周旋真实命令", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:s5-zhanghe-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
