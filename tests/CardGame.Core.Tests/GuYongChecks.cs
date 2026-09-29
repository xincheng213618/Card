using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class GuYongChecks
{
    private const string GeneralId = "classic:gu-yong";
    private const string Shenxing = "classic:shenxing";
    private const string Bingyi = "classic:bingyi";

    public static void BingyiRevealsThenSharesAndReplays()
    {
        var (game, registry) = Create(initialHand: 3, drawPerTurn: 0, allRed: true);
        ReachBingyi(game);
        var activation = RequirePrompt(game, DecisionKind.ProgramTrigger);
        Require(activation.IsPrivate && activation.SkillPrompt?.SkillId == Bingyi,
            "Bingyi activation must be private.");
        AnswerAction(game, "activate");
        var targets = RequirePrompt(game, DecisionKind.ProgramTrigger);
        Require(targets.IsPrivate && targets.Choices.Any(choice => choice.Targets.SequenceEqual([0, 1, 2])) &&
                targets.Choices.All(choice => choice.Targets.Count <= 3) &&
                !game.Events.Any(item => item.Payload is ProgramCardsRevealedEvent { SkillId: Bingyi }),
            "Bingyi must offer self and multiple targets up to the frozen hand count before revealing cards.");
        var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        ChooseTargets(game, [0, 1, 2]);
        ChooseTargets(paused, [0, 1, 2]);
        Require(State(game) == State(paused), "Target-choice checkpoint diverged.");
        while (game.PendingDecision?.Choices.Any(choice =>
                   choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards") == true)
        {
            var prompt = game.PendingDecision!;
            var select = prompt.Choices.First(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards");
            Require(!game.Events.Any(item => item.Payload is ProgramCardsRevealedEvent { SkillId: Bingyi }),
                "Bingyi must keep hand cards private during selection.");
            Answer(game, select);
            Answer(paused, paused.PendingDecision!.Choices.Single(choice => choice.Cards.SequenceEqual(select.Cards)));
        }
        if (game.PendingDecision?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards") == true)
        {
            AnswerAction(game, "finish-owned-cards");
            AnswerAction(paused, "finish-owned-cards");
        }
        var revealed = game.Events.Select(item => item.Payload).OfType<ProgramCardsRevealedEvent>()
            .SingleOrDefault(item => item.SkillId == Bingyi);
        Require(revealed is not null &&
                game.CardMovements.Count(item => item.Reason.Value == $"skill-program.{Bingyi}.Draw") == 3 &&
                State(game) == State(paused) && Events(game).SequenceEqual(Events(paused)),
            $"Same-color Bingyi mismatch: revealed={revealed?.Cards.Count}, suits={string.Join(',', revealed?.Cards.Select(card => card.Suit) ?? [])}, reasons={string.Join(',', game.CardMovements.TakeLast(6).Select(move => move.Reason.Value))}, resolved={string.Join(',', game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Where(item => item.SkillId == Bingyi).Select(item => $"{item.Activated}/{item.Completed}"))}, draws={game.CardMovements.Count(item => item.Reason.Value == $"skill-program.{Bingyi}.Draw")}, state={State(game) == State(paused)}, events={Events(game).SequenceEqual(Events(paused))}.");
    }

    private static (GameEngine Game, ContentRegistry Registry) Create(int initialHand, int drawPerTurn,
        bool allRed, int seed = 17)
    {
        var scenario = new Scenario(initialHand, drawPerTurn, allRed);
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), scenario);
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4, ModeId = Scenario.ModeId,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, GeneralId, game.Revision, game.PendingDecision!.PromptId)));
        ReachPlay(game);
        return (game, registry);
    }
    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 128 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, "Fixture did not reach Play.");
    }
    private static void ReachBingyi(GameEngine game)
    {
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
        for (var step = 0; step < 64 && game.PendingDecision?.SkillPrompt?.SkillId != Bingyi; step++)
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        Require(game.PendingDecision?.SkillPrompt?.SkillId == Bingyi, "Fixture did not reach Bingyi.");
    }
    private static LegalAction Action(GameEngine game) => game.GetHumanLegalActions().Single(item => item.ProgramSkillId == Shenxing);
    private static void Use(GameEngine game, IReadOnlyList<int> cards) => Accept(game.Submit(
        new UseProgramSkillCommand(0, Shenxing, "discard-two-draw-one", cards, [],
            game.Revision, game.PendingDecision!.PromptId)));
    private static void ChooseTargets(GameEngine game, IReadOnlyList<int> seats) =>
        Answer(game, game.PendingDecision!.Choices.Single(choice => choice.Targets.SequenceEqual(seats)));
    private static void AnswerAction(GameEngine game, string action) =>
        Answer(game, game.PendingDecision!.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == action));
    private static void Answer(GameEngine game, PromptChoice choice) => Accept(game.Submit(
        new AnswerPromptCommand(0, game.PendingDecision!.PromptId, choice.Id, game.Revision)));
    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { } prompt && prompt.Kind == kind ? prompt :
            throw new InvalidOperationException($"Expected {kind}, got {game.PendingDecision?.Kind}.");
    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));
    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
    private static IReadOnlyList<string> Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Rejected command.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Scenario(int initialHand, int drawPerTurn, bool allRed) : IGameContentPackage
    {
        public const string ModeId = "identity:classic-gu-yong-check-4";
        public PackageManifest Manifest { get; } = new("gu-yong-scenario", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var targets = new[] { "fixture:gu-yong-1", "fixture:gu-yong-2", "fixture:gu-yong-3" };
            foreach (var id in targets)
                builder.AddGeneral(new ContentGeneralDefinition(id, "测试目标", "supporter", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe("fixture:gu-yong-deck", "顾雍测试牌堆", initialHand,
                drawPerTurn, [])
            {
                PhysicalCards = Enumerable.Range(0, 120)
                    .Select(index => new ContentDeckPhysicalCard("standard:crossbow",
                        allRed ? Suit.Heart : index % 2 == 0 ? Suit.Heart : Suit.Spade, index % 13 + 1))
                    .ToArray()
            });
            builder.AddMode(new ContentModeDefinition(ModeId, "顾雍场景", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2 }, "fixture:gu-yong-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [GeneralId, .. targets]));
        }
    }
}
