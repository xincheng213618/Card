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

    }

    public static void AiUsesPrivatePublishedChoice()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
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

    public static void FactionSlashProviderOpensFollowupSlash()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var boundary = QinglongCrescentBladeScenario.FindHumanTrigger(requireFactionSlash: true);
        var game = boundary.Game;
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Qinglong FactionSlash fixture lost its trigger prompt.");
        var jijiang = prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "qinglong-jijiang");
        Require(jijiang.Cards.Count == 0 &&
                jijiang.Targets.SequenceEqual([boundary.TargetSeat]) &&
                game.CreateSnapshot(boundary.TargetSeat).PendingDecision is null,
            "Qinglong FactionSlash must be a private same-target option without exposing provider cards.");

        var requested = game.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            jijiang.Id,
            game.Revision));
        Require(requested.Accepted, requested.Error?.Message ??
            "Qinglong FactionSlash request was rejected.");
        var requestEvent = game.Events.Select(item => item.Payload)
            .OfType<FactionSlashRequestedEvent>()
            .LastOrDefault();
        var providerPrompts = requestEvent?.CandidateSeats
            .Select(seat => new { Seat = seat, Prompt = game.CreateSnapshot(seat).PendingDecision })
            .Where(item => item.Prompt is { Kind: DecisionKind.RespondSlash })
            .ToArray() ?? [];
        Require(requestEvent is { IsActiveUse: true, OwnerSeat: 0 } &&
                requestEvent.TargetSeat == boundary.TargetSeat &&
                providerPrompts.Length == 1 &&
                game.PendingDecision is null,
            "Qinglong FactionSlash must open one ordered private Shu provider response.");

        var providerCheckpoint = RoundTrip(game.CreateCheckpoint());
        var restoredProvider = GameReplay.Restore(providerCheckpoint, registry);
        var providerSeat = providerPrompts.Single().Seat;
        Require(State(restoredProvider) == State(game) &&
                SnapshotJson.Serialize(restoredProvider.CreateSnapshot(providerSeat)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(providerSeat)) &&
                Events(restoredProvider).SequenceEqual(Events(game)),
            "An in-flight Qinglong FactionSlash provider prompt must restore exactly.");

        for (var step = 0; step < 16 &&
                           game.Events.Select(item => item.Payload)
                               .OfType<QinglongCrescentBladeResolvedEvent>()
                               .All(item => !item.Used); step++)
        {
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ??
                "Qinglong FactionSlash provider flow could not advance.");
            if (game.PendingDecision is { Kind: DecisionKind.QinglongCrescentBlade })
            {
                break;
            }
        }

        var resolvedFactionSlash = game.Events.Select(item => item.Payload)
            .OfType<FactionSlashResolvedEvent>()
            .LastOrDefault(item => item.ResolutionId == requestEvent!.ResolutionId);
        var resolvedQinglong = game.Events.Select(item => item.Payload)
            .OfType<QinglongCrescentBladeResolvedEvent>()
            .LastOrDefault(item => item.ResolutionId == requestEvent!.ResolutionId && item.Used);
        Require(resolvedFactionSlash is { Succeeded: true, IsActiveUse: true, ProviderSeat: not null } &&
                resolvedFactionSlash.TargetSeat == boundary.TargetSeat &&
                resolvedQinglong is { Used: true } &&
                resolvedQinglong.TargetSeat == boundary.TargetSeat &&
                resolvedQinglong.SlashCardIds.Contains(resolvedFactionSlash.SlashCardId!.Value) &&
                game.Events.Select(item => item.Payload).OfType<CardUsedEvent>().Any(use =>
                    use.SourceSeat == 0 &&
                    use.TargetSeat == boundary.TargetSeat &&
                    use.CardId == resolvedFactionSlash.SlashCardId) &&
                game.CardMovements.Any(movement =>
                    movement.CardId == resolvedFactionSlash.SlashCardId &&
                    movement.From == CardLocation.Hand(resolvedFactionSlash.ProviderSeat.Value) &&
                    movement.To == CardLocation.Processing &&
                    movement.Reason == CardMoveReasons.Use),
            "A Shu provider must spend its exact Slash while Liu Bei opens the same-target Qinglong attack.");

        var replayed = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(State(replayed) == State(game) && Events(replayed).SequenceEqual(Events(game)),
            "A Qinglong FactionSlash follow-up Slash must replay exactly.");
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
