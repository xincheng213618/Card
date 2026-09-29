using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryGuoJiaChecks
{
    private const string GeneralId = "boundary:guo-jia";
    private const string Yiji = "boundary:yiji";
    private const string Mode = "identity:classic-boundary-guo-jia-check-5";

    public static void YijiGivesZeroOneOrTwoCurrentHandCardsAndReplays()
    {
        var registry = Registry();
        var game = FindYiji(registry);
        var offer = Prompt(game);
        Require(offer.SkillPrompt?.SkillId == Yiji && offer.IsPrivate &&
                game.CreateSnapshot(1).PendingDecision is null &&
                game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                    .Last() is { TargetSeat: 0, Amount: 1 },
            "Actual one-point damage to Guo Jia must offer a private Yiji decision.");
        var checkpoint = RoundTrip(game.CreateCheckpoint());
        var before = game.CreateSnapshot(0, true).Players;
        var oldCardId = before[0].Hand.First().Id;

        var skipped = GameReplay.Restore(checkpoint, registry);
        AnswerAction(skipped, "skip");
        Require(skipped.CreateSnapshot(0, true).Players[0].HandCount == before[0].HandCount &&
                skipped.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Any(item => item.SkillId == Yiji && !item.Activated),
            "Declining Yiji must draw and give nothing.");

        var kept = GameReplay.Restore(checkpoint, registry);
        AnswerAction(kept, "activate");
        Require(Prompt(kept).IsPrivate && kept.CreateSnapshot(1).PendingDecision is null,
            "The first gift selection must remain private.");
        AnswerAction(kept, "keep-bound-cards");
        AnswerAction(kept, "keep-bound-cards");
        Require(kept.CreateSnapshot(0, true).Players[0].HandCount == before[0].HandCount + 2,
            "Keeping both optional gifts must retain the two drawn cards.");

        var one = GameReplay.Restore(checkpoint, registry);
        AnswerAction(one, "activate");
        var unchanged = State(one);
        var forgedPrompt = Prompt(one);
        Require(!one.Submit(new AnswerPromptCommand(0, forgedPrompt.PromptId,
                    new ChoiceId("boundary.yiji.forged"), one.Revision)).Accepted && State(one) == unchanged,
            "A fabricated gift choice must be rejected without moving cards.");
        var first = Prompt(one).Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "give-bound-card" &&
            choice.Cards.Single() == oldCardId);
        var firstRecipient = first.Targets.Single();
        Answer(one, first);
        AnswerAction(one, "keep-bound-cards");
        Require(one.CreateSnapshot(0, true).Players[0].HandCount == before[0].HandCount + 1 &&
                one.CreateCardZoneDiagnostics().Any(item => item.CardId == oldCardId &&
                    item.Location == CardLocation.Hand(firstRecipient)) &&
                one.Events.Select(item => item.Payload).OfType<ProgramBoundCardGivenEvent>()
                    .Count(item => item.SkillId == Yiji) == 1,
            "Yiji must allow exactly one gift, including a hand card owned before its draw.");

        var two = GameReplay.Restore(checkpoint, registry);
        AnswerAction(two, "activate");
        Answer(two, Prompt(two).Choices.Single(choice => choice.Id == first.Id));
        Require(Prompt(two).Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "give-bound-card" &&
                    choice.Targets.Single() == firstRecipient && choice.Cards.Single() != oldCardId) &&
                Prompt(two).Choices.All(choice => choice.Cards.Count == 0 || choice.Cards.Single() != oldCardId),
            "Second gift must permit the same recipient but never offer the already transferred card.");
        var second = Prompt(two).Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "give-bound-card" &&
            choice.Targets.Single() != firstRecipient && choice.Cards.Single() != oldCardId);
        var secondRecipient = second.Targets.Single();
        Answer(two, second);
        Require(two.CreateSnapshot(0, true).Players[0].HandCount == before[0].HandCount &&
                two.Events.Select(item => item.Payload).OfType<ProgramBoundCardGivenEvent>()
                    .Where(item => item.SkillId == Yiji).Select(item => item.TargetSeat)
                    .SequenceEqual([firstRecipient, secondRecipient]) &&
                two.CreateCardZoneDiagnostics().Any(item => item.CardId == oldCardId &&
                    item.Location == CardLocation.Hand(firstRecipient)),
            "Yiji must transfer two distinct current hand cards to two distinct legal others.");
        var replay = GameReplay.Restore(RoundTrip(two.CreateCheckpoint()), registry);
        Require(State(replay) == State(two) && Events(replay).SequenceEqual(Events(two)),
            "Completed two-target Yiji must replay from a checkpoint exactly.");
    }

    private static GameEngine FindYiji(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 128; seed++)
        {
            var game = Start(registry, seed);
            for (var step = 0; step < 1200 && game.State.Winner == Winner.None; step++)
            {
                if (game.PendingDecision?.SkillPrompt?.SkillId == Yiji &&
                    game.CreateSnapshot(0, true).Players[0].HandCount > 0)
                    return game;
                if (!AdvanceConservatively(game)) break;
            }
        }
        throw new InvalidOperationException("No bounded real-damage Guo Jia Yiji fixture was found.");
    }

    private static ContentRegistry Registry(bool lightningOnly = false, int deckSize = 160) => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new Scenario(lightningOnly, deckSize));

    private static GameEngine Start(ContentRegistry registry, int seed)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, PlayerCount = 5, ModeId = Mode, HumanSeat = 0,
            HumanRole = Role.Lord, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 12
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Guo Jia fixture failed to start.");
        var select = Prompt(game);
        Require(select.ValidContentIds.Contains(GeneralId) &&
                game.Submit(new SelectGeneralCommand(0, GeneralId, game.Revision, select.PromptId)).Accepted,
            "Guo Jia was not selectable.");
        return game;
    }

    private static bool AdvanceConservatively(GameEngine game)
    {
        var prompt = game.PendingDecision;
        if (prompt is null || prompt.PlayerSeat != 0)
            return game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted;
        if (prompt.Kind == DecisionKind.PlayCard)
            return game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)).Accepted;
        if (prompt.Kind == DecisionKind.DiscardCards)
            return game.Submit(new DiscardCardsCommand(0,
                prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                prompt.PromptId, game.Revision)).Accepted;
        var choice = prompt.Choices.FirstOrDefault(candidate =>
            candidate.Parameters.GetValueOrDefault("program-action") == "skip" ||
            candidate.Cards.Count == 0) ?? prompt.Choices.LastOrDefault();
        return choice is not null && game.Submit(new AnswerPromptCommand(0, prompt.PromptId,
            choice.Id, game.Revision)).Accepted;
    }

    private static PendingDecision Prompt(GameEngine game) =>
        game.PendingDecision ?? throw new InvalidOperationException("Guo Jia fixture lost its prompt.");
    private static void AnswerAction(GameEngine game, string action) => Answer(game,
        Prompt(game).Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == action));
    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = Prompt(game);
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Guo Jia prompt answer failed.");
    }
    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));
    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, true));
    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Scenario(bool lightningOnly, int deckSize) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("boundary-guo-jia-scenario", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddDeck(new ContentDeckRecipe("fixture:boundary-guo-jia-deck", "Guo Jia Deck", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, deckSize).Select(index =>
                    lightningOnly
                        ? new ContentDeckPhysicalCard("standard:lightning", Suit.Spade, 2)
                        : new ContentDeckPhysicalCard(index % 7 == 0 ? "standard:alcohol" : "standard:slash",
                            (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            builder.AddMode(new ContentModeDefinition(Mode, "2019 Guo Jia", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, "fixture:boundary-guo-jia-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [GeneralId, "classic:liu-bei", "classic:guan-yu",
                    "classic:zhang-fei", "classic:sun-quan"]));
        }
    }
}
