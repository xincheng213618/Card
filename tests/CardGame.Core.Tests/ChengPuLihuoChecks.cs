using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ChengPuLihuoChecks
{
    private const int HumanSeat = 0;
    private const string OwnerGeneralId = "fixture:lihuo-owner";
    private const string LowHpOwnerGeneralId = "fixture:lihuo-low-hp-owner";
    private const string ChainedOwnerGeneralId = "fixture:lihuo-chained-owner";
    private const string PreWoundSkillId = "fixture:lihuo-pre-wound";
    private const string LihuoSkillId = "classic:lihuo";
    private const string ChunlaoSkillId = "classic:chunlao";

    public static void AcceptedConversionSourcesStayFrozenForReplay()
    {
        var registry = Registry();
        var game = CreateGame(ScenarioPackage.ChainedConversionModeId, seed: 2,
            ownerGeneralId: ChainedOwnerGeneralId);
        ReachHumanPlay(game);
        var actions = game.GetHumanLegalActions();
        var action = actions.FirstOrDefault(candidate =>
            candidate.Kind == LegalActionKind.Slash &&
            candidate.AdditionalConversionSources is { Count: > 0 }) ??
            throw new InvalidOperationException("No chained conversion action: " +
                string.Join("; ", actions.Select(candidate =>
                    $"{candidate.Kind}/{candidate.PlayedCardKind}/{candidate.ConversionSource?.SkillId}/" +
                    $"{candidate.AdditionalConversionSources?.Count ?? 0}")) +
                "; hand=" + string.Join(",", Player(game, HumanSeat).Hand.Select(card =>
                    $"{card.Kind}:{card.Suit}")));
        var expectedSources = action.AdditionalConversionSources!.ToArray();
        var callerSources = new List<CardConversionSource>(expectedSources);
        var accepted = game.Submit(new PlayCardCommand(
            HumanSeat,
            action.CardId ?? throw new InvalidOperationException("The converted Slash has no physical card."),
            action.TargetSeats,
            game.Revision,
            game.PendingDecision?.PromptId,
            action.PlayedCardKind,
            action.TargetCardId)
        {
            ConversionSource = action.ConversionSource,
            AdditionalConversionSources = callerSources
        });
        Require(accepted.Accepted, accepted.Error?.Message ?? "The chained conversion was rejected.");

        var checkpointJson = GameCheckpointJson.Serialize(game.CreateCheckpoint());
        var expectedState = State(game);
        var expectedEvents = Events(game).ToArray();
        callerSources.Clear();

        Require(game.AcceptedCommands.Last() is PlayCardCommand recorded &&
                recorded.AdditionalConversionSources?.SequenceEqual(expectedSources) == true &&
                GameCheckpointJson.Serialize(game.CreateCheckpoint()) == checkpointJson,
            "Changing the caller's conversion list must not alter the accepted command prefix.");
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(checkpointJson), registry);
        Require(State(restored) == expectedState && Events(restored).SequenceEqual(expectedEvents),
            "The frozen conversion command must replay to the same state and ordered events.");
    }






    public static void CompletedPenaltyCanEnterDyingAndReplay()
    {
        var registry = Registry();
        var game = CreateGame(ScenarioPackage.LethalPenaltyModeId, seed: 1,
            ownerGeneralId: LowHpOwnerGeneralId);
        ReachHumanPlay(game);
        Require(Player(game, HumanSeat).Hp == 1,
            "The Lihuo penalty fixture must pre-wound its owner to one HP.");
        var action = game.GetHumanLegalActions().First(candidate =>
            candidate.Kind == LegalActionKind.Slash &&
            candidate.ConversionSource?.SkillId == LihuoSkillId);
        Require(Play(game, action).Accepted,
            "The one-HP Lihuo owner could not use a converted Fire Slash.");
        var events = game.Events.Select(item => item.Payload).ToArray();
        Require(events.OfType<DamageAppliedEvent>().Any() &&
                events.OfType<ProgramSkillHpLostEvent>().Single(item =>
                    item.SkillId == LihuoSkillId) is { Amount: 1, RemainingHp: 0 } &&
                events.OfType<PlayerDyingEvent>().Any(item => item.VictimSeat == HumanSeat) &&
                game.PendingDecision is { Kind: DecisionKind.RescueDying, PlayerSeat: HumanSeat },
            $"A lethal configured Lihuo penalty must suspend the completed card use into dying: " +
            $"status={game.State.Status}, pending={game.PendingDecision?.Kind}, " +
            $"damage={events.OfType<DamageAppliedEvent>().Count()}, " +
            $"loss={events.OfType<ProgramSkillHpLostEvent>().Count(item => item.SkillId == LihuoSkillId)}, " +
            $"dying={events.OfType<PlayerDyingEvent>().Count(item => item.VictimSeat == HumanSeat)}.");

        var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "The configured Lihuo dying prompt must restore exactly.");
        foreach (var branch in new[] { game, paused })
        {
            var decision = RequirePrompt(branch, DecisionKind.RescueDying);
            Answer(branch, decision.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("response") == "let-die"));
        }
        Require(game.State.Status == EngineStatus.Completed &&
                State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "The Lihuo penalty dying continuation must finish once and replay deterministically.");
    }



    public static void ChunlaoStoresExactSlashesAndReplays()
    {
        var registry = Registry();
        var game = CreateGame(ScenarioPackage.SlashModeId, seed: 1);
        ReachHumanPlay(game);
        var handBefore = Player(game, HumanSeat).HandCount;
        EndHumanPlay(game);
        var activation = RequirePrompt(game, DecisionKind.ProgramTrigger);
        Require(activation.IsPrivate && activation.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "activate") &&
                activation.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "skip"),
            "An empty Chun pile must offer a generic optional turn-ending binding.");
        Require(game.CreateSnapshot(1).PendingDecision is null,
            "Other players must not receive the owner's private Chunlao hand candidates.");
        Answer(game, activation.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        var firstPrompt = RequirePrompt(game, DecisionKind.ProgramTrigger);
        Require(firstPrompt.ValidCardIds.Count == handBefore &&
                firstPrompt.Choices.All(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"),
            "The composed storage prompt must expose only the owner's Slash cards.");

        var first = firstPrompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards");
        Answer(game, first);
        var secondPrompt = RequirePrompt(game, DecisionKind.ProgramTrigger);
        Require(secondPrompt.ValidCardIds.All(id => id != first.Cards.Single()) &&
                game.CardMovements.All(move => move.To != CardLocation.Chunlao(HumanSeat)) &&
                secondPrompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards") &&
                secondPrompt.Choices.All(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") != "skip"),
            "After one selection Chunlao must preserve the exact card and expose finish instead of skip.");

        var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(State(paused) == State(game),
            "A paused multi-step Chunlao selection must replay exactly.");

        var second = secondPrompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards");
        var selectedIds = new[] { first.Cards.Single(), second.Cards.Single() };
        Answer(game, second);
        Answer(paused, RequirePrompt(paused, DecisionKind.ProgramTrigger).Choices.Single(choice =>
            choice.Cards.SequenceEqual(second.Cards)));
        Answer(game, RequirePrompt(game, DecisionKind.ProgramTrigger).Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards"));
        Answer(paused, RequirePrompt(paused, DecisionKind.ProgramTrigger).Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards"));

        var owner = Player(game, HumanSeat);
        Require(owner.Hand.All(card => !selectedIds.Contains(card.Id)) &&
                owner.ChunlaoCount == selectedIds.Length &&
                owner.ChunlaoCards?.Select(card => card.Id).SequenceEqual(selectedIds) == true &&
                game.CardMovements.Count(move =>
                    selectedIds.Contains(move.CardId) &&
                    move.From == CardLocation.Hand(HumanSeat) &&
                    move.To == CardLocation.Chunlao(HumanSeat) &&
                    move.Reason.Value == "skill-program.classic:chunlao.MoveBoundCards") == selectedIds.Length &&
                game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(item =>
                    item.SkillId == ChunlaoSkillId && item.OwnerSeat == HumanSeat && item.Completed),
            "Finishing Chunlao must atomically move the exact selected Slash cards into a public owner pile.");
        Require(State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "The completed Chunlao storage branch must replay exactly.");
    }

    public static void ChunlaoRescuesWithVirtualAlcoholAndReplays()
    {
        var fixture = FindChunlaoRescuePrompt();
        var game = fixture.Game;
        var prompt = RequirePrompt(game, DecisionKind.RescueDying);
        var choice = prompt.Choices.Single(candidate =>
            candidate.Parameters.GetValueOrDefault("response") == "program-trigger" &&
            candidate.Parameters.GetValueOrDefault("skill-id") == ChunlaoSkillId);
        var victimSeat = int.Parse(choice.Parameters["target-seat"],
            System.Globalization.CultureInfo.InvariantCulture);
        var victimBefore = Player(game, victimSeat);
        var chunCardId = Player(game, HumanSeat).ChunlaoCards!.Single().Id;
        Require(victimBefore.Hp <= 0 &&
                Player(game, HumanSeat).ChunlaoCards?.Any(card => card.Id == chunCardId) == true,
            "The dying prompt must expose a configured rescue over Cheng Pu's public Chun pile.");

        var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        Require(State(paused) == State(game),
            "A paused Chunlao dying response must restore exactly.");
        var eventStart = game.Events.Count;
        Answer(game, choice);
        Answer(paused, RequirePrompt(paused, DecisionKind.RescueDying).Choices.Single(candidate =>
            candidate.Parameters.GetValueOrDefault("response") == "program-trigger" &&
            candidate.Parameters.GetValueOrDefault("skill-id") == ChunlaoSkillId));
        Require(State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "The activated Chunlao rescue must replay at its source-card prompt.");
        Answer(game, RequirePrompt(game, DecisionKind.ProgramTrigger).Choices.Single(candidate =>
            candidate.Parameters.GetValueOrDefault("program-action") == "select-source-card" &&
            candidate.Cards.SequenceEqual([chunCardId])));
        Answer(paused, RequirePrompt(paused, DecisionKind.ProgramTrigger).Choices.Single(candidate =>
            candidate.Parameters.GetValueOrDefault("program-action") == "select-source-card" &&
            candidate.Cards.SequenceEqual([chunCardId])));

        var events = game.Events.Skip(eventStart).Select(item => item.Payload).ToArray();
        var rescued = events.OfType<ProgramDyingRescueEvent>().Single();
        var declared = events.OfType<CardUseDeclaredEvent>().Single(item => item.CardId == chunCardId);
        var targets = events.OfType<TargetsConfirmedEvent>().Single(item =>
            item.ResolutionId == declared.ResolutionId);
        Require(rescued.OwnerSeat == HumanSeat && rescued.VictimSeat == victimSeat &&
                rescued.CardId == chunCardId && rescued.SkillId == ChunlaoSkillId &&
                rescued.RecoveredHp == 1 &&
                rescued.VictimHp == Math.Min(victimBefore.MaxHp, victimBefore.Hp + 1) &&
                declared.CardKind == CardKind.Alcohol && declared.SourceSeat == victimSeat &&
                targets.TargetSeats.SequenceEqual([victimSeat]) &&
                events.OfType<CardUseFinishedEvent>().Any(item =>
                    item.CardId == chunCardId && item.CardKind == CardKind.Alcohol) &&
                game.CardMovements.Any(move =>
                    move.CardId == chunCardId &&
                    move.From == CardLocation.Chunlao(HumanSeat) &&
                    move.To == CardLocation.Processing &&
                    move.Reason.Value == "skill-program.classic:chunlao.UseBoundCardAsDyingAlcohol") &&
                game.CardMovements.Any(move =>
                    move.CardId == chunCardId &&
                    move.From == CardLocation.Processing &&
                    move.To == CardLocation.DiscardPile &&
                    move.Reason.Value == "skill-program.classic:chunlao.UseBoundCardAsDyingAlcohol"),
            "Chunlao must pay one public Chun and make the victim use a replayable virtual Alcohol on itself.");
        Require(State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "The completed Chunlao dying response must replay exactly.");
    }


    private static (GameEngine Game, ContentRegistry Registry) FindChunlaoRescuePrompt()
    {
        var registry = Registry();
        for (var seed = 1; seed <= 128; seed++)
        {
            var game = CreateGame(ScenarioPackage.ChunlaoModeId, seed);
            ReachHumanPlay(game);
            EndHumanPlay(game);
            var activation = RequirePrompt(game, DecisionKind.ProgramTrigger);
            Answer(game, activation.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "activate"));
            Answer(game, RequirePrompt(game, DecisionKind.ProgramTrigger).Choices.First(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"));
            Answer(game, RequirePrompt(game, DecisionKind.ProgramTrigger).Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards"));

            for (var step = 0; step < 256 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.RescueDying, PlayerSeat: HumanSeat } dying &&
                    dying.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "program-trigger" &&
                        choice.Parameters.GetValueOrDefault("skill-id") == ChunlaoSkillId))
                {
                    return (game, registry);
                }

                if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat } play)
                {
                    var slash = game.GetHumanLegalActions().FirstOrDefault(action =>
                        action.Kind == LegalActionKind.Slash &&
                        action.TargetSeats.Count == 1 &&
                        Player(game, action.TargetSeats[0]).Hp <= 1);
                    if (slash is not null)
                    {
                        Require(Play(game, slash).Accepted,
                            "The Chunlao rescue fixture could not use its lethal Slash.");
                    }
                    else
                    {
                        var ended = game.Submit(new EndPlayPhaseCommand(
                            HumanSeat,
                            game.Revision,
                            play.PromptId));
                        Require(ended.Accepted,
                            ended.Error?.Message ?? "The Chunlao rescue fixture could not end Play.");
                    }
                    continue;
                }

                if (game.PendingDecision is { PlayerSeat: HumanSeat } human)
                {
                    var decline = human.Choices.FirstOrDefault(choice =>
                        choice.Cards.Count == 0 && choice.Targets.Count == 0);
                    if (decline is null) break;
                    Answer(game, decline);
                    continue;
                }

                var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
                Require(advanced.Accepted,
                    advanced.Error?.Message ?? "The Chunlao rescue fixture could not advance.");
            }
        }
        throw new InvalidOperationException("No bounded Chunlao dying-response fixture was found.");
    }

    private static void EndHumanPlay(GameEngine game)
    {
        var play = RequirePrompt(game, DecisionKind.PlayCard);
        var ended = game.Submit(new EndPlayPhaseCommand(
            HumanSeat,
            game.Revision,
            play.PromptId));
        Require(ended.Accepted, ended.Error?.Message ?? "The Cheng Pu fixture could not end Play.");
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var answered = game.Submit(new AnswerPromptCommand(
            HumanSeat,
            game.PendingDecision?.PromptId ?? throw new InvalidOperationException("No human prompt is pending."),
            choice.Id,
            game.Revision));
        Require(answered.Accepted, answered.Error?.Message ?? "The Cheng Pu prompt answer was rejected.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { PlayerSeat: HumanSeat } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected human {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");


    private static CommandResult Play(GameEngine game, LegalAction action) => game.Submit(new PlayCardCommand(
        HumanSeat,
        action.CardId!.Value,
        action.TargetSeats,
        game.Revision,
        game.PendingDecision!.PromptId,
        action.PlayedCardKind,
        action.TargetCardId)
    {
        ConversionSource = action.ConversionSource,
        AdditionalConversionSources = action.AdditionalConversionSources,
    });

    private static GameEngine CreateGame(string modeId, int seed, int rulesVersion = GameCheckpoint.CurrentRulesVersion,
        string ownerGeneralId = OwnerGeneralId)
    {
        var registry = Registry();
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 4,
            ModeId = modeId,
            HumanSeat = HumanSeat,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = true,
            AiPolicyVersion = 2,
            MaxTurns = 20
        }, registry);
        if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
        {
            game = GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
        }
        Require(game.Submit(new StartGameCommand()).Accepted, "The Lihuo fixture failed to start.");
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("The Lihuo fixture has no setup prompt.");
        Require(prompt.Kind == DecisionKind.SelectGeneral && prompt.ValidContentIds.Contains(ownerGeneralId),
            "The Lihuo fixture did not offer its owner general.");
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat,
            ownerGeneralId,
            game.Revision,
            prompt.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "The Lihuo fixture could not select its owner.");
        return game;
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 512; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            Require(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while reaching Lihuo play.");
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "The Lihuo fixture could not advance.");
        }
        throw new InvalidOperationException("The Lihuo fixture did not reach human Play.");
    }

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new ScenarioPackage());


    private static PlayerSnapshot Player(GameEngine game, int seat) =>
        game.CreateSnapshot(HumanSeat, revealAll: true).Players.Single(player => player.Seat == seat);

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static string State(GameEngine game) =>
        SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true));

    private static IReadOnlyList<string> Events(GameEngine game) => game.Events
        .Select(item => $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string SlashModeId = "identity:classic-lihuo-slash-test-4";
        public const string FireModeId = "identity:classic-lihuo-fire-test-4";
        public const string ZhuqueModeId = "identity:classic-lihuo-zhuque-test-4";
        public const string DodgeModeId = "identity:classic-lihuo-dodge-test-4";
        public const string LethalPenaltyModeId = "identity:classic-lihuo-lethal-penalty-test-4";
        public const string ChainedConversionModeId = "identity:classic-lihuo-chained-conversion-test-4";
        public const string ChunlaoModeId = "identity:classic-chunlao-rescue-test-4";
        public const string ChunlaoAiModeId = "identity:classic-chunlao-ai-test-4";
        private const string SlashDeckId = "fixture:lihuo-slash-deck";
        private const string FireDeckId = "fixture:lihuo-fire-deck";
        private const string ZhuqueDeckId = "fixture:lihuo-zhuque-deck";
        private const string DodgeDeckId = "fixture:lihuo-dodge-deck";
        private const string LethalPenaltyDeckId = "fixture:lihuo-lethal-penalty-deck";
        private const string ChainedConversionDeckId = "fixture:lihuo-chained-conversion-deck";
        private const string ChunlaoDeckId = "fixture:chunlao-slash-deck";
        private static readonly string[] ChunlaoTargetIds =
            ["fixture:chunlao-target-1", "fixture:chunlao-target-2", "fixture:chunlao-target-3"];
        private static readonly string[] ChunlaoAiOwnerIds =
            ["fixture:chunlao-ai-1", "fixture:chunlao-ai-2", "fixture:chunlao-ai-3", "fixture:chunlao-ai-4"];

        public PackageManifest Manifest { get; } = new(
            "lihuo-scenario",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 91, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            const string woundRules = """
                {"schemaVersion":62,"skills":[{"id":"fixture:lihuo-pre-wound","revision":1,
                  "minimumRulesVersion": 171,"triggers":[{"id":"pre-wound",
                    "window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,
                    "effects":[{"op":"loseHp","target":"owner","amount":1}]}]}]}
                """;
            const string woundPresentation = """
                {"schemaVersion":3,"skills":{"fixture:lihuo-pre-wound":{
                  "name":"预伤","description":"测试开始时失去一点体力。"}}}
                """;
            var woundCatalog = SkillProgramCatalog.Load(woundRules, woundPresentation);
            builder.AddSkill(new ContentSkillDefinition(PreWoundSkillId, "预伤", "测试开始时失去一点体力。")
            {
                Program = woundCatalog.Programs[PreWoundSkillId],
                ProgramPresentation = woundCatalog.Presentations[PreWoundSkillId]
            });
            builder.AddGeneral(new ContentGeneralDefinition(
                OwnerGeneralId,
                "疠火测试",
                "lihuo_fixture",
                LihuoSkillId,
                "wu",
                BaseHp: 4,
                AdditionalSkillIds: [ChunlaoSkillId]));
            builder.AddGeneral(new ContentGeneralDefinition(
                LowHpOwnerGeneralId,
                "疠火失血测试",
                "lihuo_fixture",
                LihuoSkillId,
                "wu",
                BaseHp: 1,
                AdditionalSkillIds: [PreWoundSkillId]));
            builder.AddGeneral(new ContentGeneralDefinition(
                ChainedOwnerGeneralId,
                "疠火连续转化测试",
                "lihuo_fixture",
                LihuoSkillId,
                "wu",
                BaseHp: 4,
                AdditionalSkillIds: ["classic:wusheng"]));
            foreach (var id in ChunlaoTargetIds)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "醇醪测试目标",
                    "supporter",
                    "standard:none",
                    "wei",
                    BaseHp: 1));
            }
            foreach (var id in ChunlaoAiOwnerIds)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "醇醪AI",
                    "cheng_pu",
                    ChunlaoSkillId,
                    "wu",
                    BaseHp: 4));
            }
            AddDeck(builder, SlashDeckId, "疠火普通杀牌堆", [new("standard:slash", 48)]);
            AddDeck(builder, FireDeckId, "疠火火杀牌堆", [new("standard:fire_slash", 48)]);
            AddDeck(builder, ZhuqueDeckId, "疠火朱雀牌堆",
                [new("classic:zhuque-fan", 16), new("standard:slash", 32)]);
            AddDeck(builder, DodgeDeckId, "疠火闪避牌堆",
                [new("standard:slash", 8), new("standard:dodge", 56)]);
            AddDeck(builder, LethalPenaltyDeckId, "疠火失血濒死牌堆",
                [new("standard:slash", 32), new("standard:peach", 16)]);
            AddDeck(builder, ChainedConversionDeckId, "疠火连续转化牌堆",
                [new("standard:peach", 48)]);
            AddDeck(builder, ChunlaoDeckId, "醇醪濒死测试牌堆", [new("standard:slash", 96)]);
            AddMode(builder, SlashModeId, SlashDeckId);
            AddMode(builder, FireModeId, FireDeckId);
            AddMode(builder, ZhuqueModeId, ZhuqueDeckId);
            AddMode(builder, DodgeModeId, DodgeDeckId);
            AddMode(builder, LethalPenaltyModeId, LethalPenaltyDeckId,
                [LowHpOwnerGeneralId, "classic:sun-quan", "classic:huang-gai", "classic:gan-ning"]);
            AddMode(builder, ChainedConversionModeId, ChainedConversionDeckId,
                [ChainedOwnerGeneralId, "classic:sun-quan", "classic:huang-gai", "classic:gan-ning"]);
            AddMode(builder, ChunlaoModeId, ChunlaoDeckId, [OwnerGeneralId, .. ChunlaoTargetIds]);
            AddMode(builder, ChunlaoAiModeId, ChunlaoDeckId, ChunlaoAiOwnerIds);
        }

        private static void AddDeck(
            IContentRegistryBuilder builder,
            string id,
            string name,
            IReadOnlyList<ContentDeckCardCount> cards) =>
            builder.AddDeck(new ContentDeckRecipe(id, name, InitialHandSize: 4, DrawPerTurn: 0, cards));

        private static void AddMode(
            IContentRegistryBuilder builder,
            string modeId,
            string deckId,
            IReadOnlyList<string>? generalIds = null) =>
            builder.AddMode(new ContentModeDefinition(
                modeId,
                "四人经典身份（疠火场景）",
                MinPlayers: 4,
                MaxPlayers: 4,
                RoleCounts: new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId: deckId,
                GeneralCandidateCount: 4,
                GeneralPoolIds: generalIds ??
                    [
                        OwnerGeneralId,
                        "classic:sun-quan",
                        "classic:huang-gai",
                        "classic:gan-ning"
                    ]));
    }
}
