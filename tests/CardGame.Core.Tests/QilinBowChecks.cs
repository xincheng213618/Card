using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class QilinBowChecks
{
    public static void ExactMountChoiceAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 29, 0));
        var boundary = QilinBowScenario.FindHumanTrigger();
        var game = boundary.Game;
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Qilin Bow fixture lost its private mount prompt.");
        var full = game.CreateSnapshot(boundary.SourceSeat, revealAll: true);
        var source = full.Players[boundary.SourceSeat];
        var target = full.Players[boundary.TargetSeat];
        var currentMountIds = target.Equipment
            .Where(card => EquipmentCatalog.Get(card.Kind).Slot is
                EquipmentSlot.OffensiveHorse or EquipmentSlot.DefensiveHorse)
            .Select(card => card.Id)
            .ToArray();
        var discardChoices = prompt.Choices.Where(IsDiscard).ToArray();

        Require(prompt.IsPrivate &&
                prompt.PlayerSeat == boundary.SourceSeat &&
                prompt.SourceSeat == boundary.SourceSeat &&
                prompt.TargetSeat == boundary.TargetSeat &&
                prompt.ValidCardIds.SequenceEqual(currentMountIds) &&
                prompt.ValidTargetSeats.SequenceEqual([boundary.TargetSeat]) &&
                discardChoices.SelectMany(choice => choice.Cards).SequenceEqual(currentMountIds) &&
                discardChoices.All(choice =>
                    choice.Cards.Count == 1 &&
                    choice.Targets.SequenceEqual([boundary.TargetSeat])) &&
                prompt.Choices.Count(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "qilin-bow-skip" &&
                    choice.Cards.Count == 0 &&
                    choice.Targets.Count == 0) == 1 &&
                game.CreateSnapshot(boundary.TargetSeat).PendingDecision is null &&
                source.Equipment.Any(card =>
                    card.Id == boundary.WeaponCardId && card.Kind == CardKind.QilinBow),
            "Qilin Bow must expose only the target's exact public mounts plus one skip choice to its source.");

        var pausedCheckpoint = RoundTrip(game.CreateCheckpoint());
        var paused = GameReplay.Restore(pausedCheckpoint, registry);
        Require(State(paused, boundary.SourceSeat) == State(game, boundary.SourceSeat) &&
                Events(paused).SequenceEqual(Events(game)),
            "An in-flight Qilin Bow mount prompt must restore exactly.");

        var skipped = GameReplay.Restore(pausedCheckpoint, registry);
        var skippedPrompt = skipped.PendingDecision!;
        var skip = skippedPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "qilin-bow-skip");
        var skippedTargetBefore = skipped.CreateSnapshot(boundary.SourceSeat, revealAll: true)
            .Players[boundary.TargetSeat];
        var skippedResult = skipped.Submit(new AnswerPromptCommand(
            boundary.SourceSeat,
            skippedPrompt.PromptId,
            skip.Id,
            skipped.Revision));
        var skippedEvent = skipped.Events.Select(item => item.Payload)
            .OfType<QilinBowResolvedEvent>()
            .LastOrDefault();
        var skippedTargetAfter = skipped.CreateSnapshot(boundary.SourceSeat, revealAll: true)
            .Players[boundary.TargetSeat];
        Require(skippedResult.Accepted &&
                skippedEvent is { Used: false, DiscardedMountCardId: null } &&
                skippedTargetAfter.Hp < skippedTargetBefore.Hp &&
                currentMountIds.All(id => skippedTargetAfter.Equipment.Any(card => card.Id == id)) &&
                skipped.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().Any(damage =>
                    damage.SourceSeat == boundary.SourceSeat &&
                    damage.TargetSeat == boundary.TargetSeat),
            skippedResult.Error?.Message ??
            "Skipping Qilin Bow must keep the mount and apply the original Slash damage.");

        var used = GameReplay.Restore(pausedCheckpoint, registry);
        var usedPrompt = used.PendingDecision!;
        var use = usedPrompt.Choices.First(IsDiscard);
        var mountId = use.Cards.Single();
        var hpBefore = used.CreateSnapshot(boundary.SourceSeat, revealAll: true)
            .Players[boundary.TargetSeat].Hp;
        var usedResult = used.Submit(new AnswerPromptCommand(
            boundary.SourceSeat,
            usedPrompt.PromptId,
            use.Id,
            used.Revision));
        var resolved = used.Events.Select(item => item.Payload)
            .OfType<QilinBowResolvedEvent>()
            .LastOrDefault();
        var movement = used.CardMovements.LastOrDefault(move =>
            move.CardId == mountId && move.Reason == CardMoveReasons.QilinBowDiscard);
        var usedTarget = used.CreateSnapshot(boundary.SourceSeat, revealAll: true)
            .Players[boundary.TargetSeat];
        var resolvedSequence = used.Events.Single(item => ReferenceEquals(item.Payload, resolved)).Sequence;
        var damageSequence = used.Events.Where(item => item.Payload is DamageAppliedEvent damage &&
                damage.SourceSeat == boundary.SourceSeat &&
                damage.TargetSeat == boundary.TargetSeat)
            .Select(item => item.Sequence)
            .Last();
        Require(usedResult.Accepted &&
                resolved is { Used: true, DiscardedMountCardId: not null } &&
                resolved.DiscardedMountCardId == mountId &&
                movement is not null &&
                movement.From == CardLocation.Equipment(boundary.TargetSeat) &&
                movement.To == CardLocation.DiscardPile &&
                usedTarget.Equipment.All(card => card.Id != mountId) &&
                usedTarget.Hp < hpBefore &&
                resolvedSequence < damageSequence,
            usedResult.Error?.Message ??
            "Using Qilin Bow must discard the selected public mount before the original Slash damage continues.");

        var replayed = GameReplay.Restore(RoundTrip(used.CreateCheckpoint()), registry);
        Require(State(replayed, boundary.SourceSeat) == State(used, boundary.SourceSeat) &&
                Events(replayed).SequenceEqual(Events(used)),
            "A completed Qilin Bow use must replay exactly.");

    }

    public static void AiUsesPublicMountChoices()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 29, 0));
        GameEngine? witnessed = null;
        for (var seed = 1; seed <= 64 && witnessed is null; seed++)
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
                "AI Qilin Bow fixture failed to start.");
            for (var step = 0; step < 16_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
                Require(advanced.Accepted,
                    advanced.Error?.Message ?? "AI Qilin Bow fixture failed to finish.");
            }
            Require(game.State.Status == EngineStatus.Completed,
                "AI Qilin Bow fixture exceeded its bounded step budget.");
            if (game.Events.Any(item => item.Payload is QilinBowResolvedEvent))
            {
                witnessed = game;
            }
        }

        Require(witnessed is not null,
            "No bounded AI match reached a Qilin Bow decision.");
        Require(witnessed!.AiThoughts.Any(thought =>
                thought.Summary.Contains("麒麟弓", StringComparison.Ordinal)),
            "AI Qilin Bow must resolve through an explainable public-mount choice.");
        var replayed = GameReplay.Restore(RoundTrip(witnessed.CreateCheckpoint()), registry);
        Require(State(replayed, 0) == State(witnessed, 0) &&
                Events(replayed).SequenceEqual(Events(witnessed)),
            "An AI Qilin Bow match must replay exactly.");
    }

    private static bool IsDiscard(PromptChoice choice) =>
        choice.Parameters.GetValueOrDefault("action") == "qilin-bow-discard";

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static string State(GameEngine game, int viewerSeat) =>
        SnapshotJson.Serialize(game.CreateSnapshot(viewerSeat, revealAll: true));

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
