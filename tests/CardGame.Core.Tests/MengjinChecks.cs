using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class MengjinChecks
{
    public static void HiddenHandPublicEquipmentChoiceAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 44, 0));
        var boundary = MengjinScenario.FindHumanTrigger();
        var game = boundary.Game;
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Mengjin fixture lost its private target-card prompt.");
        var full = game.CreateSnapshot(boundary.SourceSeat, revealAll: true);
        var target = full.Players[boundary.TargetSeat];
        var handChoices = prompt.Choices.Where(choice =>
            choice.Parameters.GetValueOrDefault("target-zone") == "hand").ToArray();
        var equipmentChoices = prompt.Choices.Where(choice =>
            choice.Parameters.GetValueOrDefault("target-zone") == "equipment").ToArray();

        Require(prompt.IsPrivate &&
                prompt.PlayerSeat == boundary.SourceSeat &&
                prompt.SourceSeat == boundary.SourceSeat &&
                prompt.TargetSeat == boundary.TargetSeat &&
                prompt.ValidCardIds.SequenceEqual(target.Equipment.Select(card => card.Id)) &&
                prompt.ValidTargetSeats.SequenceEqual([boundary.TargetSeat]) &&
                handChoices.Length == target.Hand.Count &&
                handChoices.All(choice => choice.Cards.Count == 0 &&
                    choice.Targets.SequenceEqual([boundary.TargetSeat])) &&
                equipmentChoices.SelectMany(choice => choice.Cards)
                    .SequenceEqual(target.Equipment.Select(card => card.Id)) &&
                target.Judgment.All(card => !prompt.ValidCardIds.Contains(card.Id)) &&
                game.CreateSnapshot(boundary.TargetSeat).PendingDecision is null,
            "Mengjin must privately expose opaque hand slots and exact public equipment, never judgment cards.");

        var checkpoint = RoundTrip(game.CreateCheckpoint());
        var restored = GameReplay.Restore(checkpoint, registry);
        Require(State(restored) == State(game) && Events(restored).SequenceEqual(Events(game)),
            "An in-flight Mengjin prompt must restore exactly.");

        var invalid = game.Submit(new AnswerPromptCommand(
            boundary.SourceSeat,
            prompt.PromptId,
            new ChoiceId("forged-mengjin-choice"),
            game.Revision));
        Require(!invalid.Accepted && State(game) == State(restored),
            "A forged Mengjin choice must be rejected without mutation.");

        var skipped = GameReplay.Restore(checkpoint, registry);
        var skippedPrompt = skipped.PendingDecision!;
        var skip = skippedPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "mengjin-skip");
        var targetBeforeSkip = skipped.CreateSnapshot(boundary.SourceSeat, revealAll: true)
            .Players[boundary.TargetSeat];
        var skippedResult = skipped.Submit(new AnswerPromptCommand(
            boundary.SourceSeat, skippedPrompt.PromptId, skip.Id, skipped.Revision));
        var targetAfterSkip = skipped.CreateSnapshot(boundary.SourceSeat, revealAll: true)
            .Players[boundary.TargetSeat];
        Require(skippedResult.Accepted &&
                skipped.Events.Select(item => item.Payload).OfType<MengjinResolvedEvent>()
                    .LastOrDefault() is { Used: false, DiscardedCardId: null } &&
                targetAfterSkip.Hp == targetBeforeSkip.Hp &&
                targetAfterSkip.Hand.Count == targetBeforeSkip.Hand.Count &&
                targetAfterSkip.Equipment.Count == targetBeforeSkip.Equipment.Count,
            skippedResult.Error?.Message ?? "Skipping Mengjin must end the canceled Slash without discarding.");

        var used = GameReplay.Restore(checkpoint, registry);
        var usedPrompt = used.PendingDecision!;
        var use = usedPrompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("action") == "mengjin-discard");
        var before = used.CreateSnapshot(boundary.SourceSeat, revealAll: true)
            .Players[boundary.TargetSeat];
        var expectedCardId = use.Parameters.GetValueOrDefault("target-zone") == "hand"
            ? before.Hand[int.Parse(use.Parameters["slot-index"], System.Globalization.CultureInfo.InvariantCulture)].Id
            : use.Cards.Single();
        var usedResult = used.Submit(new AnswerPromptCommand(
            boundary.SourceSeat, usedPrompt.PromptId, use.Id, used.Revision));
        var resolved = used.Events.Select(item => item.Payload).OfType<MengjinResolvedEvent>().LastOrDefault();
        var movement = used.CardMovements.LastOrDefault(move =>
            move.CardId == expectedCardId && move.Reason == CardMoveReasons.MengjinDiscard);
        var after = used.CreateSnapshot(boundary.SourceSeat, revealAll: true)
            .Players[boundary.TargetSeat];
        Require(usedResult.Accepted &&
                resolved is { Used: true } &&
                resolved.DiscardedCardId == expectedCardId &&
                movement?.To == CardLocation.DiscardPile &&
                after.Hp == before.Hp &&
                after.Hand.Count + after.Equipment.Count == before.Hand.Count + before.Equipment.Count - 1,
            usedResult.Error?.Message ?? "Using Mengjin must discard the selected target card after Dodge canceled Slash.");

        var replayed = GameReplay.Restore(RoundTrip(used.CreateCheckpoint()), registry);
        Require(State(replayed) == State(used) && Events(replayed).SequenceEqual(Events(used)),
            "A completed Mengjin use must replay exactly.");

        var legacy = GameReplay.Restore(RoundTrip(boundary.BeforeSlash) with { RulesVersion = 58 }, registry);
        var legacyPrompt = legacy.PendingDecision!;
        var legacyPlay = legacy.Submit(new PlayCardCommand(
            boundary.SourceSeat,
            boundary.SlashAction.CardId!.Value,
            boundary.SlashAction.TargetSeats,
            legacy.Revision,
            legacyPrompt.PromptId,
            boundary.SlashAction.PlayedCardKind));
        Require(legacyPlay.Accepted &&
                legacy.PendingDecision?.Kind != DecisionKind.Mengjin &&
                legacy.Events.All(item => item.Payload is not MengjinResolvedEvent),
            legacyPlay.Error?.Message ?? "Rules v58 must retain the pre-Mengjin Slash response path.");
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
