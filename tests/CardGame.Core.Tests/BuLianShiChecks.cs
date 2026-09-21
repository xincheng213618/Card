using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BuLianShiChecks
{
    private const int HumanSeat = 0;
    private const string GeneralId = "classic:bu-lian-shi";
    private const string AnxuSkillId = "classic:anxu";
    private const string ZhuiyiSkillId = "classic:zhuiyi";

    public static void ContentAndRulesBoundary()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 90, 0));
        var previous = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 89, 0));
        var general = current.Generals[GeneralId];
        var anxu = current.Skills[AnxuSkillId];
        var zhuiyi = current.Skills[ZhuiyiSkillId];

        Require(general is
                {
                    Name: "步练师",
                    PortraitKey: "bu_lian_shi",
                    FactionId: "wu",
                    BaseHp: 3,
                    Gender: GeneralGender.Female
                } && general.SkillIds.SequenceEqual([AnxuSkillId, ZhuiyiSkillId]),
            "Classic Bu Lian Shi metadata drifted.");
        Require(anxu.LegacyKind == SkillKind.Anxu &&
                anxu.Tags == SkillTag.None &&
                anxu.ActionForms == SkillActionForm.Active &&
                anxu.ExecutionForms == SkillExecutionForm.None,
            "Anxu must remain an untagged active action instead of an automatic trigger.");
        Require(zhuiyi.LegacyKind == SkillKind.Zhuiyi &&
                zhuiyi.Tags == SkillTag.None &&
                zhuiyi.ActionForms == SkillActionForm.None &&
                zhuiyi.ExecutionForms == SkillExecutionForm.Trigger,
            "Zhuiyi must remain an optional trigger instead of a locked state effect.");
        Require(!previous.Generals.ContainsKey(GeneralId) &&
                !previous.Skills.ContainsKey(AnxuSkillId) &&
                !previous.Skills.ContainsKey(ZhuiyiSkillId) &&
                current.ContentHash != previous.ContentHash,
            "Package 1.90.0 must add Bu Lian Shi without mutating package 1.89.0.");

    }

    public static void AnxuUsesOpaqueReceiverChoiceAndEffectiveSuit()
    {
        var registry = CreateRegistry();
        var game = FindAnxuGame();
        var ownerBefore = Player(game, HumanSeat);
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseSkill && candidate.Skill == SkillKind.Anxu);
        var pair = FindUnequalPair(game, action.SelectableTargetSeats);
        var receiverBefore = Player(game, pair.ReceiverSeat);
        var donorBefore = Player(game, pair.DonorSeat);
        var prompt = RequirePrompt(game, DecisionKind.PlayCard);

        var started = game.Submit(new UseSkillCommand(
            HumanSeat,
            SkillKind.Anxu,
            [],
            [pair.ReceiverSeat, pair.DonorSeat],
            game.Revision,
            prompt.PromptId));
        Require(started.Accepted, started.Error?.Message ?? "The exact Anxu pair was rejected.");

        var selection = game.CreateSnapshot(pair.ReceiverSeat).PendingDecision ??
            throw new InvalidOperationException("Anxu did not publish the receiver's card choice.");
        Require(selection is
                {
                    Kind: DecisionKind.Anxu,
                    PlayerSeat: var responder,
                    IsPrivate: true,
                    TargetSeat: var donor
                } &&
                responder == pair.ReceiverSeat && donor == pair.DonorSeat &&
                selection.ValidCardIds.Count == 0 &&
                selection.Choices.Count == donorBefore.HandCount &&
                selection.Choices.All(choice =>
                    choice.Cards.Count == 0 &&
                    choice.Targets.SequenceEqual([pair.DonorSeat]) &&
                    choice.Parameters.GetValueOrDefault("action") == "anxu-hand-slot"),
            "Anxu must let the lower-hand receiver choose only opaque donor-hand slots.");
        Require(game.CreateSnapshot(HumanSeat).PendingDecision is null &&
                game.CreateSnapshot(pair.DonorSeat).PendingDecision is null &&
                game.CreateSnapshot(pair.ReceiverSeat).PendingDecision?.Kind == DecisionKind.Anxu,
            "Anxu's hidden-card candidates must be visible only to the receiving player.");

        var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(State(paused) == State(game),
            "A paused Anxu receiver choice must restore from the accepted command prefix.");
        AdvanceOne(game);
        AdvanceOne(paused);

        var resolved = game.Events.Select(item => item.Payload).OfType<AnxuResolvedEvent>().Single();
        Require(resolved.OwnerSeat == HumanSeat &&
                resolved.ReceiverSeat == pair.ReceiverSeat &&
                resolved.DonorSeat == pair.DonorSeat &&
                resolved.EffectiveSuit == Suit.Heart &&
                resolved.OwnerDrewCard &&
                Player(game, pair.ReceiverSeat).HandCount == receiverBefore.HandCount + 1 &&
                Player(game, pair.DonorSeat).HandCount == donorBefore.HandCount - 1 &&
                Player(game, HumanSeat).HandCount == ownerBefore.HandCount + 1 &&
                game.CardMovements.Count(move =>
                    move.CardId == resolved.CardId &&
                    move.Reason == CardMoveReasons.AnxuTransfer) == 2 &&
                game.Events.Select(item => item.Payload).OfType<CardsRevealedEvent>()
                    .Any(reveal => reveal.ResolutionId == resolved.ResolutionId &&
                                   reveal.Cards.Single().Id == resolved.CardId) &&
                game.GetHumanLegalActions().All(candidate => candidate.Skill != SkillKind.Anxu),
            "Anxu must transfer then reveal one card, apply Hongyan to the receiver's Spade, draw once and consume its phase limit.");
        Require(State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "The completed Anxu branch must replay exactly.");
    }

    public static void ZhuiyiExcludesKillerAndAllowsFullHealthTarget()
    {
        var registry = CreateRegistry();
        var game = FindZhuiyiPrompt();
        var death = game.Events.Select(item => item.Payload).OfType<PlayerDiedEvent>()
            .Last(item => item.VictimSeat == HumanSeat);
        var prompt = RequirePrompt(game, DecisionKind.ZhuiyiTarget);
        var fullHealthChoice = prompt.Choices.FirstOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("action") == "zhuiyi-target" &&
            choice.Targets.Count == 1 &&
            Player(game, choice.Targets[0]).Hp == Player(game, choice.Targets[0]).MaxHp) ??
            throw new InvalidOperationException("The Zhuiyi fixture exposed no full-health legal target.");
        var targetSeat = fullHealthChoice.Targets[0];
        var targetBefore = Player(game, targetSeat);

        Require(death.KillerSeat is { } killerSeat &&
                !prompt.ValidTargetSeats.Contains(killerSeat) &&
                prompt.ValidTargetSeats.All(seat => seat != HumanSeat) &&
                prompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "zhuiyi-skip") &&
                game.ResolutionStack.OfType<DeathSkillFrame>().Last() is
                {
                    Skill: SkillKind.Zhuiyi,
                    OwnerSeat: HumanSeat,
                    Step: ResolutionFrameStep.AwaitingResponse
                },
            "Zhuiyi must be optional and exclude the actual killer from its frozen living targets.");

        var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(State(paused) == State(game),
            "A dead owner's private Zhuiyi target prompt must restore exactly.");
        var used = game.Submit(new AnswerPromptCommand(
            HumanSeat,
            prompt.PromptId,
            fullHealthChoice.Id,
            game.Revision));
        Require(used.Accepted, used.Error?.Message ?? "The full-health Zhuiyi target was rejected.");

        var pausedPrompt = RequirePrompt(paused, DecisionKind.ZhuiyiTarget);
        var replayChoice = pausedPrompt.Choices.Single(choice => choice.Targets.SequenceEqual([targetSeat]));
        var replayUsed = paused.Submit(new AnswerPromptCommand(
            HumanSeat,
            pausedPrompt.PromptId,
            replayChoice.Id,
            paused.Revision));
        Require(replayUsed.Accepted, replayUsed.Error?.Message ?? "The restored Zhuiyi target was rejected.");

        var resolved = game.Events.Select(item => item.Payload).OfType<ZhuiyiResolvedEvent>().Single(item =>
            item.OwnerSeat == HumanSeat);
        Require(resolved.TargetSeat == targetSeat &&
                resolved.DrawnCardCount == 3 &&
                resolved.RecoveredHp == 0 &&
                Player(game, targetSeat).HandCount == targetBefore.HandCount + 3 &&
                Player(game, targetSeat).Hp == targetBefore.Hp &&
                game.CardMovements.Count(move =>
                    move.To == CardLocation.Hand(targetSeat) &&
                    move.Reason == CardMoveReasons.ZhuiyiDraw) == 3 &&
                game.Events.Select(item => item.Payload).OfType<DeathSkillResolvedEvent>().Any(item =>
                    item.OwnerSeat == HumanSeat &&
                    item.Skill == SkillKind.Zhuiyi &&
                    item.TargetSeat == targetSeat),
            "Zhuiyi must allow a full-health non-killer to draw three cards without fabricating recovery.");
        Require(State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "The completed Zhuiyi death branch must replay exactly.");
    }

    private static GameEngine FindAnxuGame(int rulesVersion = GameCheckpoint.CurrentRulesVersion)
    {
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 128; seed++)
        {
            var game = CreateGame(
                registry,
                ScenarioPackage.AnxuModeId,
                seed,
                Role.Loyalist,
                GeneralId,
                rulesVersion);
            ReachHumanPlay(game);
            var hasUnequalPair = FindAction(game, SkillKind.Anxu) is not null;
            if (rulesVersion < 112)
            {
                var targetCounts = game.CreateSnapshot(HumanSeat, revealAll: true).Players
                    .Where(player => player.IsAlive && player.Seat != HumanSeat)
                    .Select(player => player.HandCount)
                    .Distinct()
                    .Count();
                if (targetCounts > 1) return game;
            }
            else if (hasUnequalPair)
            {
                return game;
            }
        }
        throw new InvalidOperationException("No bounded unequal-hand Anxu fixture was found.");
    }

    private static GameEngine FindZhuiyiPrompt()
    {
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 256; seed++)
        {
            var game = CreateGame(
                registry,
                ScenarioPackage.ZhuiyiModeId,
                seed,
                Role.Rebel,
                ScenarioPackage.ZhuiyiOwnerId);
            for (var step = 0; step < 1_024 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.ZhuiyiTarget, PlayerSeat: HumanSeat } prompt &&
                    prompt.Choices.Any(choice =>
                        choice.Targets.Count == 1 &&
                        Player(game, choice.Targets[0]).Hp == Player(game, choice.Targets[0]).MaxHp))
                {
                    return game;
                }

                if (game.PendingDecision is { PlayerSeat: HumanSeat } human)
                {
                    if (human.Kind == DecisionKind.PlayCard)
                    {
                        var ended = game.Submit(new EndPlayPhaseCommand(
                            HumanSeat,
                            game.Revision,
                            human.PromptId));
                        Require(ended.Accepted, ended.Error?.Message ?? "The Zhuiyi fixture could not end Play.");
                        continue;
                    }

                    var skip = human.Choices.FirstOrDefault(choice =>
                        choice.Cards.Count == 0 && choice.Targets.Count == 0);
                    if (skip is null) break;
                    var answered = game.Submit(new AnswerPromptCommand(
                        HumanSeat,
                        human.PromptId,
                        skip.Id,
                        game.Revision));
                    Require(answered.Accepted, answered.Error?.Message ?? "The Zhuiyi fixture could not skip a response.");
                    continue;
                }

                AdvanceOne(game);
            }
        }
        throw new InvalidOperationException("No bounded human Zhuiyi death prompt was found.");
    }

    private static LegalAction? FindAction(GameEngine game, SkillKind skill) =>
        game.GetHumanLegalActions().FirstOrDefault(action =>
            action.Kind == LegalActionKind.UseSkill && action.Skill == skill);

    private static (int ReceiverSeat, int DonorSeat) FindUnequalPair(
        GameEngine game,
        IReadOnlyList<int> selectableSeats)
    {
        var players = selectableSeats.Select(seat => Player(game, seat)).ToArray();
        for (var first = 0; first < players.Length; first++)
        {
            for (var second = first + 1; second < players.Length; second++)
            {
                if (players[first].HandCount == players[second].HandCount) continue;
                return players[first].HandCount < players[second].HandCount
                    ? (players[first].Seat, players[second].Seat)
                    : (players[second].Seat, players[first].Seat);
            }
        }
        throw new InvalidOperationException("Anxu published no unequal-hand pair.");
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 2_048; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            Require(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while reaching Bu Lian Shi play.");
            AdvanceOne(game);
        }
        throw new InvalidOperationException("The Bu Lian Shi fixture did not reach human Play.");
    }

    private static void AdvanceOne(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "The Bu Lian Shi fixture could not advance.");
    }

    private static GameEngine CreateGame(
        ContentRegistry registry,
        string modeId,
        int seed,
        Role humanRole,
        string generalId,
        int rulesVersion = GameCheckpoint.CurrentRulesVersion)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 4,
            ModeId = modeId,
            HumanSeat = HumanSeat,
            HumanRole = humanRole,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 40
        }, registry);
        if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
        {
            game = GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
        }

        Require(game.Submit(new StartGameCommand()).Accepted, "The Bu Lian Shi fixture failed to start.");
        var prompt = RequirePrompt(game, DecisionKind.SelectGeneral);
        Require(prompt.ValidContentIds.Contains(generalId, StringComparer.Ordinal),
            $"The fixture did not offer {generalId}.");
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat,
            generalId,
            game.Revision,
            prompt.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "The fixture could not select its owner general.");
        return game;
    }

    private static ContentRegistry CreateRegistry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new ScenarioPackage());

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { PlayerSeat: HumanSeat } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected human {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");

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
        public const string AnxuModeId = "identity:classic-bu-lian-shi-anxu-test-4";
        public const string ZhuiyiModeId = "identity:classic-bu-lian-shi-zhuiyi-test-4";
        public const string ZhuiyiOwnerId = "fixture:zhuiyi-owner";
        private const string AnxuDeckId = "fixture:anxu-spade-peach-deck";
        private const string ZhuiyiDeckId = "fixture:zhuiyi-slash-deck";
        private static readonly string[] AnxuTargets =
            ["fixture:anxu-target-1", "fixture:anxu-target-2", "fixture:anxu-target-3"];
        private static readonly string[] ZhuiyiTargets =
            ["fixture:zhuiyi-target-1", "fixture:zhuiyi-target-2", "fixture:zhuiyi-target-3"];

        public PackageManifest Manifest { get; } = new(
            "bu-lian-shi-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 90, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in AnxuTargets)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "安恤测试目标",
                    "supporter",
                    "classic:yingzi",
                    "wu",
                    BaseHp: 4,
                    AdditionalSkillIds: ["classic:hongyan"],
                    Gender: GeneralGender.Female));
            }
            builder.AddGeneral(new ContentGeneralDefinition(
                ZhuiyiOwnerId,
                "追忆测试拥有者",
                "bu_lian_shi",
                ZhuiyiSkillId,
                "wu",
                BaseHp: 1,
                Gender: GeneralGender.Female));
            foreach (var id in ZhuiyiTargets)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "追忆测试目标",
                    "supporter",
                    "standard:none",
                    "wei",
                    BaseHp: 4));
            }

            builder.AddDeck(new ContentDeckRecipe(
                AnxuDeckId,
                "安恤有效花色测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = Enumerable.Range(1, 192)
                    .Select(index => new ContentDeckPhysicalCard(
                        "standard:peach",
                        Suit.Spade,
                        index % 13 + 1))
                    .ToArray()
            });
            builder.AddDeck(new ContentDeckRecipe(
                ZhuiyiDeckId,
                "追忆死亡测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = Enumerable.Range(1, 192)
                    .Select(index => new ContentDeckPhysicalCard(
                        "standard:slash",
                        index % 2 == 0 ? Suit.Spade : Suit.Club,
                        index % 13 + 1))
                    .ToArray()
            });
            AddMode(builder, AnxuModeId, AnxuDeckId, [GeneralId, .. AnxuTargets]);
            AddMode(builder, ZhuiyiModeId, ZhuiyiDeckId, [ZhuiyiOwnerId, .. ZhuiyiTargets]);
        }

        private static void AddMode(
            IContentRegistryBuilder builder,
            string modeId,
            string deckId,
            IReadOnlyList<string> generalIds) =>
            builder.AddMode(new ContentModeDefinition(
                modeId,
                "步练师测试",
                4,
                4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1,
                    [nameof(Role.Renegade)] = 1
                },
                deckId,
                GeneralCandidateCount: 4,
                GeneralPoolIds: generalIds));
    }
}
