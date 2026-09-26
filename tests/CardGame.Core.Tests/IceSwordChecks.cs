using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class IceSwordChecks
{
    public static void SequentialDiscardPreventsDamageAndReplays()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var boundary = IceSwordScenario.FindHumanTrigger();
        var game = boundary.Game;
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Ice Sword fixture lost its first private discard prompt.");
        var full = game.CreateSnapshot(0, revealAll: true);
        var source = full.Players[0];
        var target = full.Players.Single(player => player.Seat == boundary.TargetSeat);
        var targetEquipmentIds = target.Equipment.Select(card => card.Id).ToArray();
        var discardChoices = prompt.Choices.Where(IsDiscard).ToArray();
        var handChoices = discardChoices.Where(choice =>
            choice.Parameters.GetValueOrDefault("target-zone") == "hand").ToArray();
        var equipmentChoices = discardChoices.Where(choice =>
            choice.Parameters.GetValueOrDefault("target-zone") == "equipment").ToArray();

        Require(prompt.IsPrivate &&
                prompt.PlayerSeat == 0 &&
                prompt.SourceSeat == 0 &&
                prompt.TargetSeat == boundary.TargetSeat &&
                prompt.ValidCardIds.SequenceEqual(targetEquipmentIds) &&
                prompt.ValidTargetSeats.SequenceEqual([boundary.TargetSeat]) &&
                handChoices.Length == target.Hand.Count &&
                handChoices.All(choice =>
                    choice.Cards.Count == 0 &&
                    choice.Targets.SequenceEqual([boundary.TargetSeat])) &&
                equipmentChoices.SelectMany(choice => choice.Cards)
                    .SequenceEqual(targetEquipmentIds) &&
                prompt.Choices.Count(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "ice-sword-damage") == 1 &&
                game.CreateSnapshot(boundary.TargetSeat).PendingDecision is null &&
                source.Equipment.Any(card =>
                    card.Id == boundary.WeaponCardId && card.Kind == CardKind.IceSword),
            "Ice Sword must expose opaque hand slots, exact public equipment and one damage alternative only to its source.");

        var pausedCheckpoint = RoundTrip(game.CreateCheckpoint());
        var paused = GameReplay.Restore(pausedCheckpoint, registry);
        Require(State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "An in-flight first Ice Sword discard prompt must restore exactly.");

        var skipped = GameReplay.Restore(pausedCheckpoint, registry);
        var skippedPrompt = skipped.PendingDecision!;
        var retainDamage = skippedPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "ice-sword-damage");
        var hpBeforeDamage = skipped.CreateSnapshot(0, revealAll: true).Players
            .Single(player => player.Seat == boundary.TargetSeat).Hp;
        var skippedResult = skipped.Submit(new AnswerPromptCommand(
            0,
            skippedPrompt.PromptId,
            retainDamage.Id,
            skipped.Revision));
        var skippedEvent = skipped.Events.Select(item => item.Payload)
            .OfType<IceSwordResolvedEvent>()
            .LastOrDefault();
        Require(skippedResult.Accepted &&
                skippedEvent is
                {
                    Used: false,
                    PreventedDamageAmount: 0
                } &&
                skippedEvent.DiscardedCardIds.Count == 0 &&
                skipped.CreateSnapshot(0, revealAll: true).Players
                    .Single(player => player.Seat == boundary.TargetSeat).Hp < hpBeforeDamage &&
                skipped.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().Any(damage =>
                    damage.SourceSeat == 0 && damage.TargetSeat == boundary.TargetSeat),
            skippedResult.Error?.Message ??
            "Skipping Ice Sword must preserve and apply the original Slash damage.");

        var used = GameReplay.Restore(pausedCheckpoint, registry);
        var firstPrompt = used.PendingDecision!;
        var first = firstPrompt.Choices.FirstOrDefault(choice =>
                        IsDiscard(choice) && choice.Parameters.GetValueOrDefault("target-zone") == "equipment") ??
                    firstPrompt.Choices.First(IsDiscard);
        var hpBeforePrevention = used.CreateSnapshot(0, revealAll: true).Players
            .Single(player => player.Seat == boundary.TargetSeat).Hp;
        var firstResult = used.Submit(new AnswerPromptCommand(
            0,
            firstPrompt.PromptId,
            first.Id,
            used.Revision));
        Require(firstResult.Accepted,
            firstResult.Error?.Message ?? "The first Ice Sword discard was rejected.");
        var firstMove = used.CardMovements.LastOrDefault(move =>
            move.Reason == CardMoveReasons.IceSwordDiscard);
        var secondPrompt = used.PendingDecision ??
            throw new InvalidOperationException("Ice Sword did not require its available second discard.");
        Require(secondPrompt.Kind == DecisionKind.IceSword &&
                secondPrompt.IsPrivate &&
                secondPrompt.PlayerSeat == 0 &&
                secondPrompt.Choices.All(choice =>
                    choice.Parameters.GetValueOrDefault("action") != "ice-sword-damage") &&
                secondPrompt.Choices.Count(IsDiscard) == discardChoices.Length - 1 &&
                firstMove is not null &&
                firstMove.To == CardLocation.DiscardPile &&
                firstMove.Reason == CardMoveReasons.IceSwordDiscard &&
                used.CreateSnapshot(0, revealAll: true).Players
                    .Single(player => player.Seat == boundary.TargetSeat).Hp == hpBeforePrevention,
            "After the first discard, Ice Sword must preserve HP and publish a mandatory reduced second choice.");

        var secondPaused = GameReplay.Restore(RoundTrip(used.CreateCheckpoint()), registry);
        Require(State(secondPaused) == State(used) && Events(secondPaused).SequenceEqual(Events(used)),
            "An in-flight second Ice Sword discard prompt must restore exactly.");

        var second = secondPrompt.Choices.First(IsDiscard);
        var secondResult = used.Submit(new AnswerPromptCommand(
            0,
            secondPrompt.PromptId,
            second.Id,
            used.Revision));
        var resolved = used.Events.Select(item => item.Payload)
            .OfType<IceSwordResolvedEvent>()
            .LastOrDefault();
        var iceSwordMoves = used.CardMovements.Where(move =>
            move.Reason == CardMoveReasons.IceSwordDiscard).TakeLast(2).ToArray();
        Require(secondResult.Accepted &&
                resolved is { Used: true, PreventedDamageAmount: > 0 } &&
                resolved.DiscardedCardIds.Count == 2 &&
                iceSwordMoves.Select(move => move.CardId)
                    .SequenceEqual(resolved.DiscardedCardIds) &&
                iceSwordMoves.All(move => move.To == CardLocation.DiscardPile) &&
                used.CreateSnapshot(0, revealAll: true).Players
                    .Single(player => player.Seat == boundary.TargetSeat).Hp == hpBeforePrevention &&
                used.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().All(damage =>
                    damage.SourceSeat != 0 || damage.TargetSeat != boundary.TargetSeat) &&
                used.Events.Select(item => item.Payload).OfType<AfterDamageEvent>().All(damage =>
                    damage.ResolutionId != resolved.ResolutionId),
            secondResult.Error?.Message ??
            "Two sequential Ice Sword discards must prevent all damage and suppress after-damage triggers.");

        var replayed = GameReplay.Restore(RoundTrip(used.CreateCheckpoint()), registry);
        Require(State(replayed) == State(used) && Events(replayed).SequenceEqual(Events(used)),
            "A completed Ice Sword prevention must replay exactly.");

    }

    public static void AiUsesPrivateOpaqueChoices()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
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
                "AI Ice Sword fixture failed to start.");
            for (var step = 0; step < 16_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
                Require(advanced.Accepted,
                    advanced.Error?.Message ?? "AI Ice Sword fixture failed to finish.");
            }
            Require(game.State.Status == EngineStatus.Completed,
                "AI Ice Sword fixture exceeded its bounded step budget.");
            if (game.Events.Any(item => item.Payload is IceSwordResolvedEvent))
            {
                witnessed = game;
            }
        }

        Require(witnessed is not null,
            "No bounded AI match reached an Ice Sword decision.");
        Require(witnessed!.AiThoughts.Any(thought =>
                thought.Summary.Contains("寒冰剑", StringComparison.Ordinal)),
            "AI Ice Sword must resolve through an explainable private opaque-card choice.");
        var replayed = GameReplay.Restore(RoundTrip(witnessed.CreateCheckpoint()), registry);
        Require(State(replayed) == State(witnessed) && Events(replayed).SequenceEqual(Events(witnessed)),
            "An AI Ice Sword match must replay exactly.");
    }

    private static bool IsDiscard(PromptChoice choice) =>
        choice.Parameters.GetValueOrDefault("action") == "ice-sword-discard";

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
