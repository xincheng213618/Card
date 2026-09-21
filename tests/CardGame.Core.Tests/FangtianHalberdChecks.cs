using System.Text.Json;
using CardGame.Core;

internal static class FangtianHalberdChecks
{
    public static void LastHandTargetsResolveSequentiallyAndReplay()
    {
        var boundary = FangtianHalberdScenario.FindHumanLastHandSlash();
        var game = boundary.Game;
        var sourceBefore = game.CreateSnapshot(0, revealAll: true).Players[0];
        var singles = boundary.SlashActions.Where(action => action.TargetSeats.Count == 1).ToArray();
        var pairs = boundary.SlashActions.Where(action => action.TargetSeats.Count == 2).ToArray();
        var triples = boundary.SlashActions.Where(action => action.TargetSeats.Count == 3).ToArray();
        var orderedTargets = singles.Select(action => action.TargetSeats.Single()).ToArray();
        Require(sourceBefore.Hand.Select(card => card.Id).SequenceEqual([boundary.Slash.Id]) &&
                sourceBefore.Equipment.Any(card =>
                    card.Id == boundary.WeaponCardId && card.Kind == CardKind.FangtianHalberd) &&
                singles.Length == 4 &&
                pairs.Length == 6 &&
                triples.Length == 4 &&
                orderedTargets.SequenceEqual([1, 2, 3, 4]) &&
                pairs.All(action => action.TargetSeat == action.TargetSeats[0]) &&
                triples.All(action => action.TargetSeat == action.TargetSeats[0]) &&
                boundary.SlashActions.All(action => action.TargetSeats.Distinct().Count() == action.TargetSeats.Count),
            "Fangtian Halberd must publish every exact two/three-target combination only for the last hand Slash, in action order.");

        var legalWithEnd = game.GetHumanLegalActions();
        var ai = new SimpleAiBrain(0, 49001, policyVersion: 2);
        var (aiAction, thought) = ai.ChoosePlay(game.CreateSnapshot(0), legalWithEnd, thoughtSequence: 1);
        Require(aiAction.Kind == LegalActionKind.Slash &&
                aiAction.TargetSeats.Count == 3 &&
                thought.Candidates.Any(candidate =>
                    candidate.Action.TargetSeats.Count == 3 &&
                    candidate.Reason.Contains("方天画戟", StringComparison.Ordinal)),
            "The tactical AI must score Fangtian's exact public target combinations without reading hidden hands.");

        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Fangtian Halberd fixture lost its play prompt.");
        var stateBeforeForgery = State(game);
        var forged = game.Submit(new PlayCardCommand(
            0,
            boundary.Slash.Id,
            [orderedTargets[0], orderedTargets[0]],
            game.Revision,
            prompt.PromptId));
        Require(!forged.Accepted && State(game) == stateBeforeForgery,
            "A duplicated Fangtian target list must be rejected atomically.");

        var action = triples[0];
        var targetHpBefore = game.CreateSnapshot(0, revealAll: true).Players
            .Where(player => action.TargetSeats.Contains(player.Seat))
            .ToDictionary(player => player.Seat, player => player.Hp);
        var result = game.Submit(new PlayCardCommand(
            0,
            boundary.Slash.Id,
            action.TargetSeats,
            game.Revision,
            game.PendingDecision!.PromptId,
            action.PlayedCardKind));
        Require(result.Accepted, result.Error?.Message ?? "Fangtian Halberd Slash was rejected.");

        var used = game.Events.Select(item => item.Payload)
            .OfType<FangtianHalberdUsedEvent>()
            .Single(item => item.SlashCardId == boundary.Slash.Id);
        var declared = game.Events.Select(item => item.Payload)
            .OfType<CardUseDeclaredEvent>()
            .Single(item => item.ResolutionId == used.ResolutionId);
        var confirmed = game.Events.Select(item => item.Payload)
            .OfType<TargetsConfirmedEvent>()
            .Single(item => item.ResolutionId == used.ResolutionId);
        var finished = game.Events.Select(item => item.Payload)
            .OfType<CardUseFinishedEvent>()
            .Count(item => item.ResolutionId == used.ResolutionId);
        var legacyUses = game.Events.Select(item => item.Payload)
            .OfType<CardUsedEvent>()
            .Count(item => item.CardId == boundary.Slash.Id);
        var damages = game.Events.Select(item => item.Payload)
            .OfType<DamageAppliedEvent>()
            .Where(item => item.SourceSeat == 0 && action.TargetSeats.Contains(item.TargetSeat))
            .ToArray();
        var targetHpAfter = game.CreateSnapshot(0, revealAll: true).Players
            .Where(player => action.TargetSeats.Contains(player.Seat))
            .ToDictionary(player => player.Seat, player => player.Hp);
        var movements = game.CardMovements.Where(move => move.CardId == boundary.Slash.Id).ToArray();
        Require(used.TargetSeats.SequenceEqual(action.TargetSeats) &&
                declared.CardKind == CardKind.Slash &&
                confirmed.TargetSeats.SequenceEqual(action.TargetSeats) &&
                finished == 1 &&
                legacyUses == 1 &&
                damages.Select(item => item.TargetSeat).SequenceEqual(action.TargetSeats) &&
                action.TargetSeats.All(seat => targetHpAfter[seat] == targetHpBefore[seat] - 1) &&
                movements.Count(move => move.Reason == CardMoveReasons.Use) == 1 &&
                movements.Count(move => move.Reason == CardMoveReasons.UseFinished) == 1 &&
                movements.Last().To == CardLocation.DiscardPile,
            "One Fangtian Slash must move once, count as one card use, and resolve every exact target in order.");

        var replayed = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), boundary.Registry);
        Require(State(replayed) == State(game) && Events(replayed).SequenceEqual(Events(game)),
            "A completed multi-target Fangtian Slash must replay exactly.");

        VerifySequentialDodgeBoundary();
    }

    private static void VerifySequentialDodgeBoundary()
    {
        var boundary = FangtianHalberdScenario.FindHumanLastHandSlash(requireFirstTargetDodge: true);
        var game = boundary.Game;
        var full = game.CreateSnapshot(0, revealAll: true);
        var action = boundary.SlashActions.First(candidate =>
            candidate.TargetSeats.Count == 3 &&
            full.Players[candidate.TargetSeats[0]].Hand.Any(card => card.Kind == CardKind.Dodge));
        var result = game.Submit(new PlayCardCommand(
            0,
            boundary.Slash.Id,
            action.TargetSeats,
            game.Revision,
            game.PendingDecision!.PromptId,
            action.PlayedCardKind));
        Require(result.Accepted &&
                game.ResolutionStack.LastOrDefault() is ResponseWindowFrame response &&
                response.ResponderSeat == action.TargetSeats[0] &&
                response.RequiredCardKind == CardKind.Dodge,
            result.Error?.Message ??
            "Fangtian must pause at the first target's ordinary Dodge response window.");

        var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), boundary.Registry);
        Require(State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "An in-flight Fangtian target response must restore exactly.");

        for (var step = 0; step < 64 &&
             !game.Events.Select(item => item.Payload).OfType<CardUseFinishedEvent>()
                 .Any(item => item.CardId == boundary.Slash.Id); step++)
        {
            GameCommand command;
            if (game.PendingDecision is { PlayerSeat: 0 } humanPrompt)
            {
                if (humanPrompt.Kind == DecisionKind.PlayCard)
                {
                    break;
                }

                var choice = humanPrompt.Choices.FirstOrDefault(candidate => candidate.Cards.Count == 0) ??
                    humanPrompt.Choices[0];
                command = new AnswerPromptCommand(0, humanPrompt.PromptId, choice.Id, game.Revision);
            }
            else
            {
                command = new AdvanceOneStepCommand(game.Revision);
            }

            var advanced = game.Submit(command);
            Require(advanced.Accepted,
                advanced.Error?.Message ?? "Fangtian target response could not continue.");
        }

        var responses = game.Events.Select(item => item.Payload)
            .OfType<CardRespondedEvent>()
            .Where(item => action.TargetSeats.Contains(item.ResponderSeat) &&
                           item.EffectiveCardKind == CardKind.Dodge)
            .Select(item => item.ResponderSeat)
            .ToHashSet();
        var damaged = game.Events.Select(item => item.Payload)
            .OfType<DamageAppliedEvent>()
            .Where(item => item.SourceSeat == 0 && action.TargetSeats.Contains(item.TargetSeat))
            .Select(item => item.TargetSeat)
            .ToHashSet();
        Require(responses.Contains(action.TargetSeats[0]) &&
                !damaged.Contains(action.TargetSeats[0]) &&
                action.TargetSeats.All(seat => responses.Contains(seat) || damaged.Contains(seat)) &&
                game.Events.Select(item => item.Payload).OfType<CardUseFinishedEvent>()
                    .Count(item => item.CardId == boundary.Slash.Id) == 1,
            "Each Fangtian target must independently finish Dodge or damage before the shared card use finishes once.");

        var replayed = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), boundary.Registry);
        Require(State(replayed) == State(game) && Events(replayed).SequenceEqual(Events(game)),
            "A Fangtian Slash with per-target responses must replay exactly.");
    }

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static string State(GameEngine game) =>
        SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));

    private static IReadOnlyList<string> Events(GameEngine game) => game.Events
        .Select(item =>
            $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
