using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class QinglongCrescentBladeChecks
{
    public static void SameTargetFollowupAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var boundary = QinglongCrescentBladeScenario.FindHumanTrigger();
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

        var legacy = GameReplay.Restore(
            RoundTrip(boundary.BeforeSlash) with { RulesVersion = 45 },
            registry);
        var legacyPrompt = legacy.PendingDecision ??
            throw new InvalidOperationException("Rules v45 Qinglong fixture lost its play prompt.");
        var legacyPlay = legacy.Submit(new PlayCardCommand(
            0,
            boundary.SlashAction.CardId!.Value,
            boundary.SlashAction.TargetSeats,
            legacy.Revision,
            legacyPrompt.PromptId,
            boundary.SlashAction.PlayedCardKind));
        Require(legacyPlay.Accepted, legacyPlay.Error?.Message ??
            "Rules v45 could not replay the Qinglong Slash fixture.");
        for (var step = 0; step < 16 &&
                           legacy.PendingDecision?.Kind != DecisionKind.PlayCard &&
                           legacy.State.Status != EngineStatus.Completed; step++)
        {
            Require(legacy.Submit(new AdvanceOneStepCommand(legacy.Revision)).Accepted,
                "Rules v45 could not finish the target's Dodge response.");
        }
        Require(legacy.PendingDecision?.Kind != DecisionKind.QinglongCrescentBlade &&
                legacy.Events.Select(item => item.Payload)
                    .All(item => item is not QinglongCrescentBladeResolvedEvent),
            "Rules v45 must retain the historical successful-Dodge result without Qinglong.");
    }

    public static void AiUsesPrivatePublishedChoice()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 56, 0));
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
                "AI Qinglong fixture failed to start.");
            for (var step = 0; step < 16_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
                Require(advanced.Accepted,
                    advanced.Error?.Message ?? "AI Qinglong fixture failed to finish.");
            }
            Require(game.State.Status == EngineStatus.Completed,
                "AI Qinglong fixture exceeded its bounded step budget.");
            if (game.Events.Any(item => item.Payload is QinglongCrescentBladeResolvedEvent))
            {
                witnessed = game;
            }
        }

        Require(witnessed is not null,
            "No bounded AI match reached a Qinglong Crescent Blade decision.");
        Require(witnessed!.AiThoughts.Any(thought =>
                thought.Summary.Contains("青龙偃月刀", StringComparison.Ordinal)),
            "AI Qinglong must finish through an explainable private-choice decision.");
        var replayed = GameReplay.Restore(RoundTrip(witnessed.CreateCheckpoint()), registry);
        Require(State(replayed) == State(witnessed) && Events(replayed).SequenceEqual(Events(witnessed)),
            "An AI Qinglong match must replay exactly.");
    }

    public static void JijiangProviderOpensFollowupSlash()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var boundary = QinglongCrescentBladeScenario.FindHumanTrigger(requireJijiang: true);
        var game = boundary.Game;
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Qinglong Jijiang fixture lost its trigger prompt.");
        var jijiang = prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "qinglong-jijiang");
        Require(jijiang.Cards.Count == 0 &&
                jijiang.Targets.SequenceEqual([boundary.TargetSeat]) &&
                game.CreateSnapshot(boundary.TargetSeat).PendingDecision is null,
            "Qinglong Jijiang must be a private same-target option without exposing provider cards.");

        var requested = game.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            jijiang.Id,
            game.Revision));
        Require(requested.Accepted, requested.Error?.Message ??
            "Qinglong Jijiang request was rejected.");
        var requestEvent = game.Events.Select(item => item.Payload)
            .OfType<JijiangRequestedEvent>()
            .LastOrDefault();
        var providerPrompts = requestEvent?.CandidateSeats
            .Select(seat => new { Seat = seat, Prompt = game.CreateSnapshot(seat).PendingDecision })
            .Where(item => item.Prompt is { Kind: DecisionKind.RespondSlash })
            .ToArray() ?? [];
        Require(requestEvent is { IsActiveUse: true, OwnerSeat: 0 } &&
                requestEvent.TargetSeat == boundary.TargetSeat &&
                providerPrompts.Length == 1 &&
                game.PendingDecision is null,
            "Qinglong Jijiang must open one ordered private Shu provider response.");

        var providerCheckpoint = RoundTrip(game.CreateCheckpoint());
        var restoredProvider = GameReplay.Restore(providerCheckpoint, registry);
        var providerSeat = providerPrompts.Single().Seat;
        Require(State(restoredProvider) == State(game) &&
                SnapshotJson.Serialize(restoredProvider.CreateSnapshot(providerSeat)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(providerSeat)) &&
                Events(restoredProvider).SequenceEqual(Events(game)),
            "An in-flight Qinglong Jijiang provider prompt must restore exactly.");

        for (var step = 0; step < 16 &&
                           game.Events.Select(item => item.Payload)
                               .OfType<QinglongCrescentBladeResolvedEvent>()
                               .All(item => !item.Used); step++)
        {
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ??
                "Qinglong Jijiang provider flow could not advance.");
            if (game.PendingDecision is { Kind: DecisionKind.QinglongCrescentBlade })
            {
                break;
            }
        }

        var resolvedJijiang = game.Events.Select(item => item.Payload)
            .OfType<JijiangResolvedEvent>()
            .LastOrDefault(item => item.ResolutionId == requestEvent!.ResolutionId);
        var resolvedQinglong = game.Events.Select(item => item.Payload)
            .OfType<QinglongCrescentBladeResolvedEvent>()
            .LastOrDefault(item => item.ResolutionId == requestEvent!.ResolutionId && item.Used);
        Require(resolvedJijiang is { Succeeded: true, IsActiveUse: true, ProviderSeat: not null } &&
                resolvedJijiang.TargetSeat == boundary.TargetSeat &&
                resolvedQinglong is { Used: true } &&
                resolvedQinglong.TargetSeat == boundary.TargetSeat &&
                resolvedQinglong.SlashCardIds.Contains(resolvedJijiang.SlashCardId!.Value) &&
                game.Events.Select(item => item.Payload).OfType<CardUsedEvent>().Any(use =>
                    use.SourceSeat == 0 &&
                    use.TargetSeat == boundary.TargetSeat &&
                    use.CardId == resolvedJijiang.SlashCardId) &&
                game.CardMovements.Any(movement =>
                    movement.CardId == resolvedJijiang.SlashCardId &&
                    movement.From == CardLocation.Hand(resolvedJijiang.ProviderSeat.Value) &&
                    movement.To == CardLocation.Processing &&
                    movement.Reason == CardMoveReasons.Use),
            "A Shu provider must spend its exact Slash while Liu Bei opens the same-target Qinglong attack.");

        var replayed = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(State(replayed) == State(game) && Events(replayed).SequenceEqual(Events(game)),
            "A Qinglong Jijiang follow-up Slash must replay exactly.");
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
