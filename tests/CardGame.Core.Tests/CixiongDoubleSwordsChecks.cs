using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CixiongDoubleSwordsChecks
{
    public static void StagedChoiceAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 63, 0));
        var boundary = CixiongDoubleSwordsScenario.FindHumanTrigger();
        var game = boundary.Game;
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Cixiong Double Swords fixture lost its activation prompt.");
        var full = game.CreateSnapshot(0, revealAll: true);
        var source = full.Players[0];
        var target = full.Players.Single(player => player.Seat == boundary.TargetSeat);

        Require(prompt.IsPrivate &&
                prompt.PlayerSeat == 0 &&
                prompt.SourceSeat == 0 &&
                prompt.TargetSeat == boundary.TargetSeat &&
                prompt.ValidCardIds.Count == 0 &&
                prompt.ValidTargetSeats.Count == 0 &&
                prompt.Choices.Count == 2 &&
                prompt.Choices.Count(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "cixiong-use") == 1 &&
                prompt.Choices.Count(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "cixiong-skip") == 1 &&
                game.CreateSnapshot(boundary.TargetSeat).PendingDecision is null &&
                source.Equipment.Any(card =>
                    card.Id == boundary.WeaponCardId && card.Kind == CardKind.CixiongDoubleSwords) &&
                registry.Generals[source.GeneralId].Gender != registry.Generals[target.GeneralId].Gender,
            "Cixiong Double Swords must open one private source activation prompt only for an opposite-gender Slash.");

        var pausedCheckpoint = RoundTrip(game.CreateCheckpoint());
        var paused = GameReplay.Restore(pausedCheckpoint, registry);
        Require(State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "An in-flight Cixiong Double Swords activation prompt must restore exactly.");

        var skipped = GameReplay.Restore(pausedCheckpoint, registry);
        var skippedPrompt = skipped.PendingDecision!;
        var skippedChoice = skippedPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "cixiong-skip");
        var skippedResult = skipped.Submit(new AnswerPromptCommand(
            0,
            skippedPrompt.PromptId,
            skippedChoice.Id,
            skipped.Revision));
        var skippedEvent = skipped.Events.Select(item => item.Payload)
            .OfType<CixiongDoubleSwordsResolvedEvent>()
            .LastOrDefault();
        Require(skippedResult.Accepted &&
                skippedEvent is
                {
                    Activated: false,
                    TargetDiscarded: false,
                    DiscardedCardId: null,
                    SourceDrawCount: 0
                },
            skippedResult.Error?.Message ??
            "Skipping Cixiong Double Swords must continue the same Slash without a resource effect.");

        var activated = GameReplay.Restore(pausedCheckpoint, registry);
        var activationPrompt = activated.PendingDecision!;
        var activationChoice = activationPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "cixiong-use");
        var activatedResult = activated.Submit(new AnswerPromptCommand(
            0,
            activationPrompt.PromptId,
            activationChoice.Id,
            activated.Revision));
        Require(activatedResult.Accepted,
            activatedResult.Error?.Message ?? "Cixiong Double Swords activation was rejected.");

        var targetPrompt = activated.CreateSnapshot(boundary.TargetSeat).PendingDecision ??
            throw new InvalidOperationException("Cixiong Double Swords lost its target resource prompt.");
        var targetHand = activated.CreateSnapshot(boundary.TargetSeat, revealAll: true)
            .Players.Single(player => player.Seat == boundary.TargetSeat).Hand;
        var targetHandIds = targetHand.Select(card => card.Id).Order().ToArray();
        var discardChoices = targetPrompt.Choices.Where(choice =>
            choice.Parameters.GetValueOrDefault("action") == "cixiong-discard").ToArray();
        Require(targetPrompt.IsPrivate &&
                targetPrompt.PlayerSeat == boundary.TargetSeat &&
                targetPrompt.SourceSeat == 0 &&
                targetPrompt.TargetSeat == boundary.TargetSeat &&
                targetPrompt.ValidCardIds.Order().SequenceEqual(targetHandIds) &&
                discardChoices.Length == targetHandIds.Length &&
                discardChoices.All(choice =>
                    choice.Cards.Count == 1 && targetHandIds.Contains(choice.Cards[0])) &&
                targetPrompt.Choices.Count(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "cixiong-draw" &&
                    choice.Cards.Count == 0) == 1 &&
                activated.CreateSnapshot(0).PendingDecision is null &&
                activated.CreateSnapshot(boundary.TargetSeat).PendingDecision?.Choices.Count ==
                    targetHandIds.Length + 1,
            "An activated Cixiong Double Swords must publish exact private discard cards plus one draw alternative only to the target.");

        var targetPaused = GameReplay.Restore(RoundTrip(activated.CreateCheckpoint()), registry);
        Require(State(targetPaused) == State(activated) && Events(targetPaused).SequenceEqual(Events(activated)),
            "An in-flight Cixiong Double Swords target prompt must restore exactly.");

        var sourceHandBefore = activated.CreateSnapshot(0, revealAll: true).Players[0].Hand.Count;
        var targetHandBefore = targetHand.Count;
        var advanced = activated.Submit(new AdvanceOneStepCommand(activated.Revision));
        Require(advanced.Accepted,
            advanced.Error?.Message ?? "AI could not resolve the Cixiong Double Swords target choice.");
        var resolved = activated.Events.Select(item => item.Payload)
            .OfType<CixiongDoubleSwordsResolvedEvent>()
            .LastOrDefault();
        var after = activated.CreateSnapshot(0, revealAll: true);
        var sourceAfter = after.Players[0];
        var targetAfter = after.Players.Single(player => player.Seat == boundary.TargetSeat);
        var resolvedResourceExactly = resolved switch
        {
            { Activated: true, TargetDiscarded: true, DiscardedCardId: { } cardId, SourceDrawCount: 0 } =>
                targetHandIds.Contains(cardId) &&
                targetAfter.Hand.Count == targetHandBefore - 1 &&
                activated.CardMovements.Any(move =>
                    move.CardId == cardId &&
                    move.From == CardLocation.Hand(boundary.TargetSeat) &&
                    move.To == CardLocation.DiscardPile &&
                    move.Reason == CardMoveReasons.CixiongDiscard),
            { Activated: true, TargetDiscarded: false, DiscardedCardId: null, SourceDrawCount: 1 } =>
                sourceAfter.Hand.Count == sourceHandBefore + 1 &&
                activated.CardMovements.Any(move =>
                    move.To == CardLocation.Hand(0) &&
                    move.Reason == CardMoveReasons.CixiongDraw),
            _ => false
        };
        Require(resolvedResourceExactly &&
                activated.AiThoughts.Any(thought =>
                    thought.Summary.Contains("雌雄双股剑", StringComparison.Ordinal)),
            "The target AI must resolve exactly one published Cixiong resource branch with an explainable thought.");

        var replayed = GameReplay.Restore(RoundTrip(activated.CreateCheckpoint()), registry);
        Require(State(replayed) == State(activated) && Events(replayed).SequenceEqual(Events(activated)),
            "A resolved Cixiong Double Swords branch must replay exactly.");

        var legacy = GameReplay.Restore(
            RoundTrip(boundary.BeforeSlash) with { RulesVersion = 44 },
            registry);
        var legacyPrompt = legacy.PendingDecision ??
            throw new InvalidOperationException("Rules v44 Cixiong fixture lost its play prompt.");
        var legacySlash = legacy.Submit(new PlayCardCommand(
            0,
            boundary.SlashAction.CardId!.Value,
            boundary.SlashAction.TargetSeats,
            legacy.Revision,
            legacyPrompt.PromptId,
            boundary.SlashAction.PlayedCardKind));
        Require(legacySlash.Accepted &&
                legacy.PendingDecision?.Kind != DecisionKind.CixiongDoubleSwords &&
                legacy.Events.Select(item => item.Payload)
                    .All(item => item is not CixiongDoubleSwordsResolvedEvent),
            legacySlash.Error?.Message ??
            "Rules v44 must preserve the historical Slash flow without Cixiong Double Swords.");
    }

    public static void AiRelationBranches()
    {
        VerifyAiBranch(Role.Loyalist, expectedDiscard: false);
        VerifyAiBranch(Role.Rebel, expectedDiscard: true);
    }

    private static void VerifyAiBranch(Role targetRole, bool expectedDiscard)
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 63, 0));
        var boundary = CixiongDoubleSwordsScenario.FindHumanTrigger(targetRole);
        var game = boundary.Game;
        var activation = game.PendingDecision ??
            throw new InvalidOperationException("Cixiong relation fixture lost its activation prompt.");
        var use = activation.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "cixiong-use");
        Require(game.Submit(new AnswerPromptCommand(
                0,
                activation.PromptId,
                use.Id,
                game.Revision)).Accepted,
            "Cixiong relation fixture could not activate the weapon.");
        Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
            "Cixiong relation fixture AI could not choose its resource branch.");
        var resolved = game.Events.Select(item => item.Payload)
            .OfType<CixiongDoubleSwordsResolvedEvent>()
            .LastOrDefault();
        Require(resolved is { Activated: true } &&
                resolved.TargetDiscarded == expectedDiscard &&
                (expectedDiscard
                    ? resolved.DiscardedCardId is not null && resolved.SourceDrawCount == 0
                    : resolved.DiscardedCardId is null && resolved.SourceDrawCount == 1),
            $"A {targetRole} target must choose the expected Cixiong relation branch.");
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
