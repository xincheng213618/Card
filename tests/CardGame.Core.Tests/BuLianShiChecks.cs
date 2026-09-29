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
        var current = StandardContentRegistry.CreateWithClassicGenerals();
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
        Require(anxu.Program?.Activations.Single() is
                { Id: "unequal-hand-transfer", Effects.Count: 5 } &&
                anxu.Tags == SkillTag.None &&
                anxu.ActionForms == SkillActionForm.Active &&
                anxu.ExecutionForms == SkillExecutionForm.None,
            "Current Anxu must be an untagged composed active action.");
        Require(zhuiyi.Tags == SkillTag.None &&
                zhuiyi.ActionForms == SkillActionForm.None &&
                zhuiyi.ExecutionForms == SkillExecutionForm.Trigger &&
                zhuiyi.Program?.Triggers.Single() is
                {
                    Window: SkillProgramTriggerWindow.OwnerDied,
                    Optional: true
                },
            "Current Zhuiyi and Anxu must use programs.");

        var rules = ReadResource(
            "CardGame.Content.Standard.SkillPrograms.death-benefit-skills.rules.json");
        var presentation = ReadResource(
            "CardGame.Content.Standard.SkillPrograms.death-benefit-skills.presentation.json");
        var transferRules = ReadResource(
            "CardGame.Content.Standard.SkillPrograms.unequal-hand-transfer-skills.rules.json");
        var transferPresentation = ReadResource(
            "CardGame.Content.Standard.SkillPrograms.unequal-hand-transfer-skills.presentation.json");
        RequireLoadFailure(
            transferRules.Replace("\"targetKind\": \"otherLivingUnequalHandPair\"",
                "\"targetKind\": \"otherLivingWithHand\"", StringComparison.Ordinal),
            transferPresentation,
            "support-first order requires an eligible pair");

    }

    public static void AnxuUsesOpaqueReceiverChoiceAndEffectiveSuit()
    {
        var registry = CreateRegistry();
        var game = FindAnxuGame();
        var ownerBefore = Player(game, HumanSeat);
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseProgramSkill && candidate.ProgramSkillId == AnxuSkillId);
        Require(action is { MinTargetCount: 0, MaxTargetCount: 0, ProgramActivationId: "unequal-hand-transfer" },
            "Current Anxu must publish only its composed active entry.");
        var prompt = RequirePrompt(game, DecisionKind.PlayCard);
        var started = game.Submit(new UseProgramSkillCommand(
            HumanSeat, AnxuSkillId, "unequal-hand-transfer", [], [], game.Revision, prompt.PromptId));
        Require(started.Accepted, started.Error?.Message ?? "The composed Anxu entry was rejected.");

        var targetPrompt = RequirePrompt(game, DecisionKind.ProgramTrigger);
        var (aiChoiceId, aiThought) = new SimpleAiBrain(HumanSeat, 17)
            .ChooseSupportFirstTransferTargets(game.CreateSnapshot(HumanSeat), targetPrompt.Choices, 1);
        Require(targetPrompt.Choices.Any(choice => choice.Id == aiChoiceId) &&
                aiThought.Candidates.Count == targetPrompt.Choices.Count &&
                aiThought.Candidates[0].Action.TargetSeats.SequenceEqual(
                    targetPrompt.Choices.Single(choice => choice.Id == aiChoiceId).Targets),
            "The generic transfer AI must rank only published ordered target pairs.");
        var pairChoice = targetPrompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "select-targets");
        var pair = (ReceiverSeat: pairChoice.Targets[0], DonorSeat: pairChoice.Targets[1]);
        var receiverBefore = Player(game, pair.ReceiverSeat);
        var donorBefore = Player(game, pair.DonorSeat);
        Require(receiverBefore.HandCount < donorBefore.HandCount &&
                targetPrompt.Choices.All(choice => choice.Targets.Count == 2 &&
                    Player(game, choice.Targets[0]).HandCount < Player(game, choice.Targets[1]).HandCount),
            "The target-set prompt must expose only ordered unequal-hand pairs.");
        var pausedTargets = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        var selectedPair = game.Submit(new AnswerPromptCommand(
            HumanSeat, targetPrompt.PromptId, pairChoice.Id, game.Revision));
        Require(selectedPair.Accepted, selectedPair.Error?.Message ?? "The unequal-hand pair was rejected.");
        var restoredTargets = RequirePrompt(pausedTargets, DecisionKind.ProgramTrigger);
        var restoredPair = restoredTargets.Choices.Single(choice => choice.Targets.SequenceEqual(pairChoice.Targets));
        Require(pausedTargets.Submit(new AnswerPromptCommand(HumanSeat, restoredTargets.PromptId,
            restoredPair.Id, pausedTargets.Revision)).Accepted,
            "The restored target selection was rejected.");
        Require(State(pausedTargets) == State(game) && Events(pausedTargets).SequenceEqual(Events(game)),
            "The selected target set must replay exactly.");

        var selection = game.CreateSnapshot(pair.ReceiverSeat).PendingDecision ??
            throw new InvalidOperationException("Anxu did not publish the receiver's card choice.");
        Require(selection is
                {
                    Kind: DecisionKind.ProgramTrigger,
                    PlayerSeat: var responder,
                    IsPrivate: true,
                    TargetSeat: var donor
                } &&
                responder == pair.ReceiverSeat && donor == pair.DonorSeat &&
                selection.ValidCardIds.Count == 0 &&
                selection.Choices.Count == donorBefore.HandCount &&
                selection.Choices.All(choice =>
                    choice.Cards.Count == 0 &&
                    choice.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card"),
            "Anxu must let the lower-hand receiver choose only opaque donor-hand slots.");
        Require(game.CreateSnapshot(HumanSeat).PendingDecision is null &&
                game.CreateSnapshot(pair.DonorSeat).PendingDecision is null &&
                game.CreateSnapshot(pair.ReceiverSeat).PendingDecision?.Kind == DecisionKind.ProgramTrigger,
            "Anxu's hidden-card candidates must be visible only to the receiving player.");

        var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(State(paused) == State(game),
            "A paused Anxu receiver choice must restore from the accepted command prefix.");
        AdvanceOne(game);
        AdvanceOne(paused);

        var resolved = game.Events.Select(item => item.Payload).OfType<ProgramSkillResolvedEvent>()
            .Single(item => item.SkillId == AnxuSkillId);
        var revealed = game.Events.Select(item => item.Payload).OfType<ProgramCardsRevealedEvent>()
            .Single(item => item.SkillId == AnxuSkillId);
        Require(resolved is { OwnerSeat: HumanSeat, Completed: true } &&
                revealed.Cards is [var transferred] && transferred.Suit == Suit.Spade &&
                Player(game, pair.ReceiverSeat).HandCount == receiverBefore.HandCount + 1 &&
                Player(game, pair.DonorSeat).HandCount == donorBefore.HandCount - 1 &&
                Player(game, HumanSeat).HandCount == ownerBefore.HandCount + 1 &&
                game.CardMovements.Count(move =>
                    move.CardId == transferred.Id &&
                    move.Reason.Value == "skill-program.classic:anxu.SelectAndMoveOwnedCard") == 2 &&
                game.GetHumanLegalActions().All(candidate => candidate.ProgramSkillId != AnxuSkillId),
            "Anxu must transfer then reveal a Spade, apply the receiver's Hongyan, draw once and consume its turn limit.");
        Require(State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "The completed Anxu branch must replay exactly.");
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
            if (game is null) continue;
            ReachHumanPlay(game);
            if (game.GetHumanLegalActions().Any(action => action.ProgramSkillId == AnxuSkillId)) return game;
        }
        throw new InvalidOperationException("No bounded unequal-hand Anxu fixture was found.");
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

    private static GameEngine? CreateGame(
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
        if (!prompt.ValidContentIds.Contains(generalId, StringComparer.Ordinal)) return null;
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

    private static string ReadResource(string resourceName)
    {
        using var stream = typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException($"Missing embedded resource '{resourceName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void RequireLoadFailure(string rules, string presentation, string expectedMessage)
    {
        try
        {
            _ = SkillProgramCatalog.Load(rules, presentation);
            throw new InvalidOperationException("The invalid owner-death program unexpectedly loaded.");
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase))
        {
        }
    }

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
            builder.AddSkill(new ContentSkillDefinition(
                "fixture:zhuiyi-ai-decoy",
                "追忆测试诱饵技能",
                "仅用于令非玩家座位稳定选择测试目标。"));
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
                    "fixture:zhuiyi-ai-decoy",
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
                GeneralCandidateCount: modeId == AnxuModeId ? 1 : 4,
                GeneralPoolIds: generalIds));
    }
}
