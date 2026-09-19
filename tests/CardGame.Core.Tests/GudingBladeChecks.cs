using System.Text.Json;
using CardGame.Core;

internal static class GudingBladeChecks
{
    public static void EmptyHandDamageAndLegacyBoundary()
    {
        var boundary = GudingBladeScenario.FindHumanSlash(requireEmptyTarget: true);
        var game = boundary.Game;
        var fullBefore = game.CreateSnapshot(boundary.SourceSeat, revealAll: true);
        var sourceBefore = fullBefore.Players[boundary.SourceSeat];
        var targetBefore = fullBefore.Players[boundary.TargetSeat];
        Require(boundary.TargetHandCount == 0 &&
                sourceBefore.Equipment.Any(card =>
                    card.Id == boundary.WeaponCardId && card.Kind == CardKind.GudingBlade),
            "The formal Guding Blade fixture must expose an equipped weapon and an empty-hand target under rules v50.");

        var ai = new SimpleAiBrain(boundary.SourceSeat, 50001, policyVersion: 2);
        var legalActions = game.GetHumanLegalActions();
        var (_, thought) = ai.ChoosePlay(
            game.CreateSnapshot(boundary.SourceSeat),
            legalActions,
            thoughtSequence: 1);
        var scoredCandidates = thought.Candidates.Where(candidate =>
            candidate.Action.CardId == boundary.SlashAction.CardId &&
            candidate.Action.TargetSeat == boundary.TargetSeat).ToArray();
        Require(scoredCandidates.Length == 1,
            $"Expected one Guding AI candidate, found {scoredCandidates.Length}.");
        var scored = scoredCandidates[0];
        Require(scored.Reason.Contains("古锭刀", StringComparison.Ordinal) &&
                scored.Reason.Contains("公开为空手", StringComparison.Ordinal),
            "Guding Blade AI scoring must use only the target's public hand count and the source's public equipment.");

        var played = PlaySlash(game, boundary.SlashAction);
        Require(played.Accepted, played.Error?.Message ?? "Guding Blade Slash was rejected.");
        var events = game.Events.Select(item => item.Payload).ToArray();
        var increases = events.OfType<GudingBladeDamageIncreasedEvent>().Where(item =>
            item.SourceSeat == boundary.SourceSeat && item.TargetSeat == boundary.TargetSeat).ToArray();
        Require(increases.Length == 1,
            $"Expected one Guding increase event, found {increases.Length}.");
        var increased = increases[0];
        var requestedEvents = events.OfType<DamageRequestedEvent>().Where(item =>
            item.SourceSeat == boundary.SourceSeat &&
            item.TargetSeat == boundary.TargetSeat &&
            item.Amount == 2).ToArray();
        var appliedEvents = events.OfType<DamageAppliedEvent>().Where(item =>
            item.SourceSeat == boundary.SourceSeat && item.TargetSeat == boundary.TargetSeat &&
            item.Amount == 2).ToArray();
        Require(requestedEvents.Length == 1 && appliedEvents.Length == 1,
            $"Expected one Guding damage request/application, found {requestedEvents.Length}/{appliedEvents.Length}.");
        var requested = requestedEvents[0];
        var applied = appliedEvents[0];
        Require(increased.EffectiveSlashKind == CardKind.Slash &&
                increased.BaseAmount == 1 && increased.ModifiedAmount == 2 &&
                requested.Amount == 2 && applied.Amount == 2 &&
                game.CreateSnapshot(boundary.SourceSeat, revealAll: true)
                    .Players[boundary.TargetSeat].Hp == targetBefore.Hp - 2 &&
                Array.IndexOf(events, increased) < Array.IndexOf(events, requested),
            "Guding Blade must publicly increase one direct Slash damage before the damage request is applied.");

        var replayed = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), boundary.Registry);
        Require(State(replayed) == State(game) && Events(replayed).SequenceEqual(Events(game)),
            "A completed Guding Blade damage increase must replay exactly.");

        VerifyNonEmptyTarget();
        VerifyRules49Boundary(boundary, targetBefore.Hp);
    }

    private static void VerifyNonEmptyTarget()
    {
        var boundary = GudingBladeScenario.FindHumanSlash(requireEmptyTarget: false);
        var game = boundary.Game;
        var targetBefore = game.CreateSnapshot(boundary.SourceSeat, revealAll: true)
            .Players[boundary.TargetSeat];
        Require(boundary.TargetHandCount > 0,
            "The Guding Blade control fixture must retain at least one target hand card.");

        var played = PlaySlash(game, boundary.SlashAction);
        Require(played.Accepted, played.Error?.Message ?? "Non-empty Guding Blade control Slash was rejected.");
        var applied = game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().Last(item =>
            item.SourceSeat == boundary.SourceSeat && item.TargetSeat == boundary.TargetSeat);
        Require(applied.Amount == 1 &&
                game.CreateSnapshot(boundary.SourceSeat, revealAll: true)
                    .Players[boundary.TargetSeat].Hp == targetBefore.Hp - 1 &&
                game.Events.Select(item => item.Payload).All(item => item is not GudingBladeDamageIncreasedEvent),
            "Guding Blade must not increase Slash damage while the target still has a hand card.");
    }

    private static void VerifyRules49Boundary(GudingBladeBoundary boundary, int targetHpBefore)
    {
        var legacy = GameReplay.Restore(
            RoundTrip(boundary.BeforeSlash) with { RulesVersion = 49 },
            boundary.Registry);
        var legacyActions = legacy.GetHumanLegalActions().Where(action =>
            action.Kind == LegalActionKind.Slash &&
            action.CardId == boundary.SlashAction.CardId &&
            action.TargetSeat == boundary.TargetSeat).ToArray();
        Require(legacyActions.Length == 1,
            $"Rules v49 expected one matching Slash action, found {legacyActions.Length}.");
        var legacyAction = legacyActions[0];
        var played = PlaySlash(legacy, legacyAction);
        Require(played.Accepted, played.Error?.Message ?? "Rules v49 Guding control Slash was rejected.");
        var applied = legacy.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().Last(item =>
            item.SourceSeat == boundary.SourceSeat && item.TargetSeat == boundary.TargetSeat);
        Require(applied.Amount == 1 &&
                legacy.CreateSnapshot(boundary.SourceSeat, revealAll: true)
                    .Players[boundary.TargetSeat].Hp == targetHpBefore - 1 &&
                legacy.Events.Select(item => item.Payload).All(item => item is not GudingBladeDamageIncreasedEvent),
            "Rules v49 must retain ordinary one-point Slash damage even when the current registry contains Guding Blade.");
    }

    private static CommandResult PlaySlash(GameEngine game, LegalAction action) =>
        game.Submit(new PlayCardCommand(
            game.State.HumanSeat,
            action.CardId!.Value,
            action.TargetSeats,
            game.Revision,
            game.PendingDecision!.PromptId,
            action.PlayedCardKind));

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
