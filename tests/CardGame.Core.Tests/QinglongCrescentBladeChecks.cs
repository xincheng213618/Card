using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class QinglongCrescentBladeChecks
{
    public static void SameTargetFollowupAndReplay()
    {
        var boundary = QinglongCrescentBladeScenario.FindHumanTrigger();
        var registry = boundary.Registry;
        var game = boundary.Game;
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Qinglong fixture lost its private trigger prompt.");
        var full = game.CreateSnapshot(0, revealAll: true);
        var source = full.Players[0];
        var expectedCandidates = source.Hand
            .Where(card => card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)
            .Select(card => card.Id)
            .ToArray();
        var slashChoices = prompt.Choices.Where(choice =>
            choice.Parameters.GetValueOrDefault("action") == "qinglong-slash").ToArray();

        Require(prompt.IsPrivate &&
                game.CreateSnapshot(boundary.TargetSeat).PendingDecision is null &&
                prompt.ValidTargetSeats.SequenceEqual([boundary.TargetSeat]) &&
                prompt.ValidCardIds.SequenceEqual(expectedCandidates) &&
                slashChoices.SelectMany(choice => choice.Cards).SequenceEqual(expectedCandidates) &&
                slashChoices.All(choice =>
                    choice.Cards.Count == 1 &&
                    choice.Targets.SequenceEqual([boundary.TargetSeat])),
            "Qinglong must publish exact available Slashes for only the original target and source.");

        var pausedCheckpoint = RoundTrip(game.CreateCheckpoint());
        var paused = GameReplay.Restore(pausedCheckpoint, registry);
        Require(State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "An in-flight Qinglong follow-up prompt must restore exactly.");

        var skipped = GameReplay.Restore(pausedCheckpoint, registry);
        var skipPrompt = skipped.PendingDecision!;
        var skip = skipPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "qinglong-skip");
        var targetHp = skipped.CreateSnapshot(0, revealAll: true).Players
            .Single(player => player.Seat == boundary.TargetSeat).Hp;
        var skipResult = skipped.Submit(new AnswerPromptCommand(
            0,
            skipPrompt.PromptId,
            skip.Id,
            skipped.Revision));
        Require(skipResult.Accepted &&
                skipped.CreateSnapshot(0, revealAll: true).Players
                    .Single(player => player.Seat == boundary.TargetSeat).Hp == targetHp &&
                skipped.Events.Select(item => item.Payload)
                    .OfType<QinglongCrescentBladeResolvedEvent>()
                    .Any(resolved => !resolved.Used && resolved.SlashCardIds.Count == 0),
            skipResult.Error?.Message ??
            "Skipping Qinglong must preserve the successful Dodge without damage.");

        var used = GameReplay.Restore(pausedCheckpoint, registry);
        var usePrompt = used.PendingDecision!;
        var use = usePrompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("action") == "qinglong-slash");
        var previousSlashUses = used.Events.Select(item => item.Payload)
            .OfType<CardUsedEvent>()
            .Count(item => item.SourceSeat == 0 && item.TargetSeat == boundary.TargetSeat);
        var useResult = used.Submit(new AnswerPromptCommand(
            0,
            usePrompt.PromptId,
            use.Id,
            used.Revision));
        Require(useResult.Accepted, useResult.Error?.Message ??
            "The exact Qinglong follow-up Slash was rejected.");
        var resolved = used.Events.Select(item => item.Payload)
            .OfType<QinglongCrescentBladeResolvedEvent>()
            .LastOrDefault();
        var followupUses = used.Events.Select(item => item.Payload)
            .OfType<CardUsedEvent>()
            .Where(item => item.SourceSeat == 0 && item.TargetSeat == boundary.TargetSeat)
            .ToArray();
        Require(resolved is { Used: true } &&
                resolved.SlashCardIds.SequenceEqual(use.Cards) &&
                resolved.TargetSeat == boundary.TargetSeat &&
                followupUses.Length == previousSlashUses + 1 &&
                followupUses[^1].CardId == use.Cards.Single() &&
                used.CardMovements.Any(movement =>
                    movement.CardId == use.Cards.Single() &&
                    movement.To == CardLocation.Processing &&
                    movement.Reason == CardMoveReasons.Use),
            "Qinglong must finish the canceled Slash and open a real new Slash against the same target.");

        var replayed = GameReplay.Restore(RoundTrip(used.CreateCheckpoint()), registry);
        Require(State(replayed) == State(used) && Events(replayed).SequenceEqual(Events(used)),
            "A paused Qinglong follow-up Slash must replay exactly.");

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
