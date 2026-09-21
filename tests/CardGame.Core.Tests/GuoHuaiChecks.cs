using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class GuoHuaiChecks
{
    private const int HumanSeat = 0;
    private const string GeneralId = "classic:guo-huai";

    public static void ContentAndPackageBoundary()
    {
        var previous = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 94, 0));
        var current = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 95, 0));
        Require(!previous.Generals.ContainsKey(GeneralId) &&
                !previous.Skills.ContainsKey("classic:jingce"),
            "Package 1.94.0 must retain the pre-Guo-Huai content boundary.");
        Require(current.Packages.Single(package => package.Id == "standard-classic-generals").Version ==
                    new Version(1, 95, 0) &&
                current.Generals[GeneralId] is
                {
                    BaseHp: 4,
                    FactionId: "wei",
                    Gender: GeneralGender.Male,
                    PortraitKey: "guo_huai"
                } guoHuai &&
                guoHuai.SkillIds.SequenceEqual(["classic:jingce"]) &&
                current.Skills["classic:jingce"] is
                {
                    LegacyKind: SkillKind.Jingce,
                    ExecutionForms: SkillExecutionForm.Trigger,
                    ActionForms: SkillActionForm.None
                } &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(GeneralId),
            "Package 1.95.0 must publish complete classic Guo Huai and optional Jingce.");
        Require(GameCheckpoint.CurrentRulesVersion == 101,
            "Adding package-owned Guo Huai content must not create another global replay-rules gate.");
    }

    public static void JingceCountsTurnUsesDrawsAndReplays()
    {
        var (game, registry) = CreateReadyGame();
        PlayCrossbows(game, count: 5);
        var beforeEnd = game.CreateSnapshot(HumanSeat, revealAll: true);
        Require(beforeEnd.Players[HumanSeat].Hp == 5,
            "The Lord fixture must expose Guo Huai's current 5 HP threshold.");

        EndPlay(game);
        var prompt = RequirePrompt(game, DecisionKind.Jingce);
        var use = prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "jingce-use");
        Require(prompt.IsPrivate && prompt.TargetSeat == HumanSeat &&
                prompt.Choices.Count == 2 &&
                use.Parameters.GetValueOrDefault("used-card-count") == "5" &&
                use.Parameters.GetValueOrDefault("current-hp") == "5",
            "Jingce must compare the exact turn-wide card-use count with current HP at Play end.");

        var revision = game.Revision;
        var forged = game.Submit(new AnswerPromptCommand(
            HumanSeat,
            prompt.PromptId,
            new ChoiceId("jingce.forged"),
            revision));
        Require(!forged.Accepted && game.Revision == revision,
            "An unpublished Jingce answer must be rejected without changing state.");

        var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        var skipped = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Answer(game, use);
        Answer(paused, RequirePrompt(paused, DecisionKind.Jingce).Choices.Single(choice => choice.Id == use.Id));
        AnswerAction(skipped, "jingce-skip");

        var resolved = game.Events.Select(item => item.Payload).OfType<JingceResolvedEvent>().Last();
        Require(resolved is
                {
                    OwnerSeat: HumanSeat,
                    UsedCardCount: 5,
                    CurrentHp: 5,
                    Used: true
                } &&
                resolved.DrawnCardIds.Count == 2 &&
                game.CardMovements.Count(move =>
                    resolved.DrawnCardIds.Contains(move.CardId) &&
                    move.Reason == CardMoveReasons.JingceDraw &&
                    move.To == CardLocation.Hand(HumanSeat)) == 2 &&
                game.State.Phase == TurnPhase.Discard &&
                game.PendingDecision is null,
            "Jingce must draw two exact physical cards and then continue to the Discard phase.");
        Require(skipped.Events.Select(item => item.Payload).OfType<JingceResolvedEvent>().Last() is
                { Used: false, DrawnCardIds.Count: 0 } &&
                skipped.CardMovements.All(move => move.Reason != CardMoveReasons.JingceDraw),
            "Skipping Jingce must not draw a card.");
        Require(State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "A paused Jingce choice must replay to the same state and event stream.");
    }

    public static void JingceRequiresUseCountAtLeastCurrentHp()
    {
        var (game, _) = CreateReadyGame();
        PlayCrossbows(game, count: 4);
        EndPlay(game);
        Require(game.PendingDecision is null &&
                game.State.Phase == TurnPhase.Discard &&
                !game.Events.Select(item => item.Payload).OfType<JingceResolvedEvent>().Any(),
            "Four uses must not trigger Jingce while the Lord's current HP is five.");
    }

    private static (GameEngine Game, ContentRegistry Registry) CreateReadyGame()
    {
        var registry = Registry();
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 670021,
            PlayerCount = 6,
            ModeId = ScenarioPackage.ModeId,
            HumanSeat = HumanSeat,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 20
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "The Guo Huai fixture failed to start.");
        var selection = RequirePrompt(game, DecisionKind.SelectGeneral);
        Require(selection.ValidContentIds.Contains(GeneralId),
            "The Guo Huai fixture must offer its only formal general.");
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat,
            GeneralId,
            game.Revision,
            selection.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "The fixture could not select Guo Huai.");
        ReachHumanPlay(game);
        return (game, registry);
    }

    private static void PlayCrossbows(GameEngine game, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var prompt = RequirePrompt(game, DecisionKind.PlayCard);
            var hand = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand;
            var action = game.GetHumanLegalActions().First(candidate =>
                candidate.CardId is { } cardId &&
                hand.Single(card => card.Id == cardId).Kind == CardKind.Crossbow);
            var played = game.Submit(new PlayCardCommand(
                HumanSeat,
                action.CardId!.Value,
                action.TargetSeats,
                game.Revision,
                prompt.PromptId,
                action.PlayedCardKind,
                action.TargetCardId)
            {
                ConversionSource = action.ConversionSource,
                CardKindModifierSkill = action.CardKindModifierSkill,
                TargetCountModifierSkill = action.TargetCountModifierSkill
            });
            Require(played.Accepted, played.Error?.Message ?? "The fixture could not use Crossbow.");
            ReachHumanPlay(game);
        }
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 128; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat })
            {
                return;
            }
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "The fixture could not advance to Play.");
        }
        throw new InvalidOperationException("The fixture did not reach the human Play prompt.");
    }

    private static void EndPlay(GameEngine game)
    {
        var prompt = RequirePrompt(game, DecisionKind.PlayCard);
        var ended = game.Submit(new EndPlayPhaseCommand(
            HumanSeat,
            game.Revision,
            prompt.PromptId));
        Require(ended.Accepted, ended.Error?.Message ?? "The fixture could not end Play.");
    }

    private static void AnswerAction(GameEngine game, string action)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("No prompt is pending.");
        Answer(game, prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == action));
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("No prompt is pending.");
        var result = game.Submit(new AnswerPromptCommand(
            prompt.PlayerSeat,
            prompt.PromptId,
            choice.Id,
            game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "The Jingce answer was rejected.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new ScenarioPackage());

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static string State(GameEngine game) =>
        SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true));

    private static IReadOnlyList<string> Events(GameEngine game) => game.Events
        .Select(item =>
            $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-guo-huai-test-6";
        private const string DeckId = "fixture:guo-huai-card-use";
        private static readonly string[] TargetIds =
        [
            "fixture:guo-huai-target-1", "fixture:guo-huai-target-2",
            "fixture:guo-huai-target-3", "fixture:guo-huai-target-4",
            "fixture:guo-huai-target-5"
        ];

        public PackageManifest Manifest { get; } = new(
            "guo-huai-scenario",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 95, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in TargetIds)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "郭淮测试目标",
                    "supporter",
                    "standard:none",
                    "wei",
                    BaseHp: 4));
            }
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "郭淮用牌计数测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                [new ContentDeckCardCount("standard:crossbow", 96)]));
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "六人经典身份（郭淮场景）",
                MinPlayers: 6,
                MaxPlayers: 6,
                RoleCounts: new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 3,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId,
                GeneralCandidateCount: 6,
                GeneralPoolIds: [GeneralId, .. TargetIds]));
        }
    }
}
