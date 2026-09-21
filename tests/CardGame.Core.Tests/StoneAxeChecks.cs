using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class StoneAxeChecks
{
    public static void ExactCostDamageAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 63, 0));
        var boundary = StoneAxeScenario.FindHumanTrigger();
        var game = boundary.Game;
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Stone Axe fixture lost its private trigger prompt.");
        var source = game.CreateSnapshot(0, revealAll: true).Players[0];
        var target = game.CreateSnapshot(0, revealAll: true).Players
            .Single(player => player.Seat == boundary.TargetSeat);
        var expectedCandidates = source.Hand.Concat(source.Equipment).Select(card => card.Id).ToArray();
        var useChoices = prompt.Choices.Where(choice =>
            choice.Parameters.GetValueOrDefault("action") == "stone-axe-use").ToArray();

        Require(prompt.IsPrivate &&
                game.CreateSnapshot(boundary.TargetSeat).PendingDecision is null &&
                prompt.ValidCardIds.SequenceEqual(expectedCandidates) &&
                useChoices.Length == expectedCandidates.Length * (expectedCandidates.Length - 1) / 2 &&
                useChoices.All(choice =>
                    choice.Cards.Count == 2 &&
                    choice.Cards.Distinct().Count() == 2 &&
                    choice.Cards.All(expectedCandidates.Contains)),
            "Stone Axe must publish every exact two-card hand/equipment cost only to its source.");
        Require(useChoices.Any(choice => choice.Cards.Contains(boundary.StoneAxeCardId)),
            "Stone Axe itself must remain a legal member of the two-card discard cost.");

        var pausedCheckpoint = RoundTrip(game.CreateCheckpoint());
        var paused = GameReplay.Restore(pausedCheckpoint, registry);
        Require(State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "An in-flight Stone Axe cost prompt must restore exactly.");

        var skipped = GameReplay.Restore(pausedCheckpoint, registry);
        var skippedTargetHp = skipped.CreateSnapshot(0, revealAll: true).Players
            .Single(player => player.Seat == boundary.TargetSeat).Hp;
        var skipPrompt = skipped.PendingDecision!;
        var skipChoice = skipPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "stone-axe-skip");
        var skipResult = skipped.Submit(new AnswerPromptCommand(
            0,
            skipPrompt.PromptId,
            skipChoice.Id,
            skipped.Revision));
        Require(skipResult.Accepted &&
                skipped.CreateSnapshot(0, revealAll: true).Players
                    .Single(player => player.Seat == boundary.TargetSeat).Hp == skippedTargetHp &&
                skipped.Events.Select(item => item.Payload).OfType<StoneAxeResolvedEvent>()
                    .Any(resolved => !resolved.Used && resolved.DiscardedCardIds.Count == 0),
            skipResult.Error?.Message ??
            "Skipping Stone Axe must preserve the successful Dodge without damage.");

        var used = GameReplay.Restore(pausedCheckpoint, registry);
        var usedPrompt = used.PendingDecision!;
        var costChoice = usedPrompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("action") == "stone-axe-use" &&
            choice.Cards.Contains(boundary.StoneAxeCardId));
        var targetHpBefore = used.CreateSnapshot(0, revealAll: true).Players
            .Single(player => player.Seat == boundary.TargetSeat).Hp;
        var usedResult = used.Submit(new AnswerPromptCommand(
            0,
            usedPrompt.PromptId,
            costChoice.Id,
            used.Revision));
        Require(usedResult.Accepted, usedResult.Error?.Message ??
            "The exact Stone Axe two-card cost was rejected.");
        var after = used.CreateSnapshot(0, revealAll: true);
        var resolved = used.Events.Select(item => item.Payload)
            .OfType<StoneAxeResolvedEvent>()
            .LastOrDefault();
        Require(after.Players.Single(player => player.Seat == boundary.TargetSeat).Hp == targetHpBefore - 1 &&
                after.Players[0].Equipment.All(card => card.Id != boundary.StoneAxeCardId) &&
                costChoice.Cards.All(cardId => used.CardMovements.Any(movement =>
                    movement.CardId == cardId &&
                    movement.To == CardLocation.DiscardPile &&
                    movement.Reason == CardMoveReasons.StoneAxeDiscard)) &&
                resolved is { Used: true } &&
                resolved.DiscardedCardIds.SequenceEqual(costChoice.Cards),
            "Stone Axe must discard the exact published pair and resume the same Slash as damage.");

        var completed = GameReplay.Restore(RoundTrip(used.CreateCheckpoint()), registry);
        Require(State(completed) == State(used) && Events(completed).SequenceEqual(Events(used)),
            "A completed Stone Axe damage branch must replay exactly.");

    }

    public static void AiUsesPrivatePublishedChoices()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 56, 0));
        GameEngine? witnessed = null;
        for (var seed = 1; seed <= 48 && witnessed is null; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                HumanSeat = -1,
                HumanRole = null,
                PlayerCount = 5,
                ModeId = "identity:classic-5",
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 220,
                AiPolicyVersion = 2
            }, registry);
            Require(game.Submit(new StartGameCommand()).Accepted,
                "AI Stone Axe fixture failed to start.");
            for (var step = 0; step < 16_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
                Require(advanced.Accepted,
                    advanced.Error?.Message ?? "AI Stone Axe fixture failed to finish.");
            }
            Require(game.State.Status == EngineStatus.Completed,
                "AI Stone Axe fixture exceeded its bounded step budget.");
            if (game.Events.Any(item => item.Payload is StoneAxeResolvedEvent))
            {
                witnessed = game;
            }
        }

        Require(witnessed is not null,
            "No bounded AI match reached a Stone Axe decision.");
        Require(witnessed!.State.Status == EngineStatus.Completed &&
                witnessed.AiThoughts.Any(thought => thought.Decision.Contains("贯石斧", StringComparison.Ordinal)),
            "AI Stone Axe must finish through an explainable private-choice decision.");
        var replayed = GameReplay.Restore(RoundTrip(witnessed.CreateCheckpoint()), registry);
        Require(State(replayed) == State(witnessed) && Events(replayed).SequenceEqual(Events(witnessed)),
            "An AI Stone Axe match must replay exactly.");
    }

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static string State(GameEngine game) =>
        SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));

    private static IReadOnlyList<string> Events(GameEngine game) => game.Events
        .Select(item => $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
