using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class TargetCardChecks
{
    public static void OpaqueSlotFlow()
    {
        var (game, action) = FindHumanFixture();
        var targetSeat = action.TargetSeat ??
            throw new InvalidOperationException("Target-card fixture lost its target seat.");
        var targetBefore = game.CreateSnapshot(targetSeat, revealAll: true)
            .Players.Single(player => player.Seat == targetSeat);
        var sourceBefore = game.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == 0);

        var play = game.Submit(new PlayCardCommand(
            0,
            action.CardId!.Value,
            [targetSeat],
            game.Revision,
            game.PendingDecision!.PromptId));
        True(play.Accepted, play.Error?.Message ?? "Target-card play was rejected.");
        Equal(EngineStatus.AwaitingHumanCardSelection, game.State.Status);
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Target-card action did not publish a prompt.");
        Equal(DecisionKind.SelectTargetCard, prompt.Kind);
        True(prompt.IsPrivate, "Target-card prompt was not marked private.");
        Equal(0, prompt.ValidCardIds.Count);
        True(prompt.ValidTargetSeats.SequenceEqual([targetSeat]), "Target-card prompt published the wrong target seat.");
        Equal(targetBefore.HandCount, prompt.Choices.Count);
        True(prompt.Choices.All(choice =>
            choice.Cards.Count == 0 &&
            choice.Targets.SequenceEqual([targetSeat]) &&
            choice.Parameters.GetValueOrDefault("action") == "target-card-slot" &&
            choice.Parameters.ContainsKey("slot-index") &&
            !choice.Parameters.ContainsKey("card-id")), "Target-card prompt leaked a hidden identity.");

        var targetViewer = game.CreateSnapshot(targetSeat);
        var ordinaryViewer = game.CreateSnapshot(Enumerable.Range(0, game.PlayerCount)
            .First(seat => seat != game.State.HumanSeat && seat != targetSeat));
        Equal<PendingDecision?>(null, targetViewer.PendingDecision);
        Equal<PendingDecision?>(null, ordinaryViewer.PendingDecision);

        var frame = game.ResolutionStack.OfType<TargetCardSelectionFrame>().Single();
        Equal(action.CardId.Value, frame.EffectCardId);
        Equal(targetSeat, frame.TargetSeat);
        True(frame.CandidateSlots.SequenceEqual(Enumerable.Range(0, targetBefore.HandCount)), "Target-card frame has the wrong opaque slot cursor.");
        var serializedFrames = JsonSerializer.Serialize(game.ResolutionStack);
        True(serializedFrames.Contains("target-card-selection", StringComparison.Ordinal), "Target-card frame was not serialized polymorphically.");

        var checkpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restored = GameReplay.Restore(checkpoint, StandardContentRegistry.Create());
        Equal(
            SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)));
        Equal(JsonSerializer.Serialize(game.ResolutionStack), JsonSerializer.Serialize(restored.ResolutionStack));

        var invalidBefore = game.SerializeState();
        var invalid = game.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            new ChoiceId("target-card.slot-999"),
            game.Revision));
        True(!invalid.Accepted && invalid.Error?.Code == CommandErrorCode.InvalidChoice, "Forged target-card choice was accepted.");
        Equal(invalidBefore, game.SerializeState());

        var selected = prompt.Choices.Last();
        var accepted = game.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            selected.Id,
            game.Revision));
        True(accepted.Accepted, accepted.Error?.Message ?? "Target-card slot was rejected.");
        True(game.PendingDecision?.Kind != DecisionKind.SelectTargetCard &&
             game.ResolutionStack.All(frame => frame is not TargetCardSelectionFrame),
            "Accepted target-card choice left its prompt or frame.");
        Equal(0, game.State.ProcessingCardCount);
        var outcomes = game.Events.Select(item => item.Payload).ToArray();
        var request = outcomes.OfType<TargetCardSelectionRequestedEvent>().Single();
        Equal(targetBefore.HandCount, request.CandidateCount);
        var targetResult = outcomes.FirstOrDefault(payload =>
            payload is TargetCardDiscardedEvent or TargetCardTakenEvent);
        True(targetResult is not null, "Target-card selection did not publish a hand outcome.");
        switch (targetResult)
        {
            case TargetCardDiscardedEvent discarded:
                Equal(CardZoneKind.Hand, discarded.FromZone);
                Equal<int?>(null, discarded.PublicCardId);
                True(discarded.PublicCardKind is null, "Hidden-hand discard leaked a public card kind.");
                Equal(targetBefore.HandCount - 1, game.CreateSnapshot(targetSeat, true).Players.Single(player => player.Seat == targetSeat).HandCount);
                break;
            case TargetCardTakenEvent taken:
                Equal(CardZoneKind.Hand, taken.FromZone);
                Equal<int?>(null, taken.PublicCardId);
                True(taken.PublicCardKind is null, "Hidden-hand transfer leaked a public card kind.");
                Equal(sourceBefore.HandCount, game.State.Players.Single(player => player.Seat == 0).HandCount);
                break;
        }
        True(game.AiThoughts.All(thought =>
            !thought.Summary.Contains("暗牌", StringComparison.Ordinal) ||
            thought.Candidates.All(candidate => candidate.Action.CardId is null)),
            "An AI target-card thought included a hidden card id.");
    }

    public static void AiUsesOpaqueSlots()
    {
        for (var seed = 1; seed <= 256; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                HumanSeat = -1,
                HumanRole = null,
                UseInteractiveSetup = true,
                AiPolicyVersion = 2,
                MaxTurns = 120
            }, StandardContentRegistry.Create());
            True(game.Submit(new StartGameCommand()).Accepted, "AI target-card fixture failed to start.");
            var request = game.Events.Select(item => item.Payload)
                .OfType<TargetCardSelectionRequestedEvent>()
                .FirstOrDefault();
            if (request is null) continue;

            var outcome = game.Events.Select(item => item.Payload)
                .FirstOrDefault(payload =>
                    payload is TargetCardDiscardedEvent discarded && discarded.ResolutionId == request.ResolutionId ||
                    payload is TargetCardTakenEvent taken && taken.ResolutionId == request.ResolutionId);
            True(outcome is not null, "AI target-card choice did not reach a typed result.");
            var thought = game.AiThoughts.FirstOrDefault(item =>
                item.Summary.Contains("不透明牌位", StringComparison.Ordinal));
            True(thought is not null, "AI target-card choice did not record its opaque-slot reasoning.");
            True(thought!.Candidates.All(candidate =>
                candidate.Action.CardId is null &&
                candidate.Action.TargetSeat == request.TargetSeat &&
                candidate.Reason.Contains("不可见", StringComparison.Ordinal)));
            return;
        }

        throw new InvalidOperationException("Could not find an AI target-card slot fixture.");
    }

    private static (GameEngine Game, LegalAction Action) FindHumanFixture()
    {
        for (var seed = 1; seed <= 1_024; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                UseInteractiveDiscard = false,
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                MaxTurns = 180
            }, StandardContentRegistry.Create());
            var result = game.Submit(new StartGameCommand());
            if (!result.Accepted || result.State.Status != EngineStatus.AwaitingHumanPlay) continue;
            var full = game.CreateSnapshot(0, revealAll: true);
            var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
                (candidate.Kind is LegalActionKind.Dismantlement or LegalActionKind.Snatch) &&
                candidate.CardId is not null &&
                candidate.TargetSeat is not null &&
                candidate.TargetCardId is null &&
                full.Players.Single(player => player.Seat == candidate.TargetSeat).HandCount > 0 &&
                full.Players.All(player => player.Hand.All(card => card.Kind != CardKind.Nullification)));
            if (action is not null) return (game, action);
        }

        throw new InvalidOperationException("Could not find a human target-card slot fixture.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    private static void True(bool condition, string message = "Expected condition to be true.")
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
