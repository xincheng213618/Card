using CardGame.Content.Standard;
using CardGame.Core;

internal static class RecastChecks
{
    private static readonly ContentRegistry Registry = StandardContentRegistry.Create();

    private static GameEngine Opening()
    {
        for (var seed = 1; seed <= 200; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                AdvanceAfterHumanCommands = false,
                UseInteractiveDiscard = false
            }, Registry);
            Require(game.Submit(new StartGameCommand()).Accepted, "Opening failed.");
            for (var step = 0; step < 30 && game.PendingDecision is null; step++)
                Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Opening step failed.");
            if (game.PendingDecision?.Kind == DecisionKind.PlayCard && game.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.IronChain))
                return game;
        }
        throw new InvalidOperationException("No real opening with Iron Chain found.");
    }

    public static void CommandAndReplay()
    {
        var game = Opening();
        var cardId = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Recast).CardId!.Value;
        var prompt = game.PendingDecision!;
        var before = State(game);
        var commandsBefore = game.AcceptedCommands.Count;
        var cardCount = game.CreateCardZoneDiagnostics().Count;
        var oldHand = game.CreateSnapshot(0).Players[0].Hand.Select(card => card.Id).ToHashSet();
        foreach (var invalid in new GameCommand[]
        {
            new RecastCardCommand(0, cardId, game.Revision - 1, prompt.PromptId),
            new RecastCardCommand(1, cardId, game.Revision, prompt.PromptId),
            new RecastCardCommand(0, cardId, game.Revision, new PromptId(prompt.PromptId.Value + 1)),
            new RecastCardCommand(0, int.MaxValue, game.Revision, prompt.PromptId),
            new RecastCardCommand(0, game.CreateSnapshot(0).Players[0].Hand.First(card => card.Kind != CardKind.IronChain).Id, game.Revision, prompt.PromptId),
            new PlayCardCommand(0, cardId, [], game.Revision, prompt.PromptId)
        })
            Require(!game.Submit(invalid).Accepted && State(game) == before && game.AcceptedCommands.Count == commandsBefore,
                "Invalid recast or implicit targetless play changed the match.");

        var eventStart = game.Events.Count;
        var command = new RecastCardCommand(0, cardId, game.Revision, prompt.PromptId);
        Require(CommandJson.Deserialize(CommandJson.Serialize([command])).Single() == command, "Recast command JSON lost fields.");
        Require(game.Submit(command).Accepted, "Legal recast failed.");
        var events = game.Events.Skip(eventStart).Select(entry => entry.Payload).ToArray();
        Require(events.OfType<CardRecastEvent>().Single() is { ActorSeat: 0, DrawCount: 1 } recast && recast.CardId == cardId,
            "Recast did not publish one exact public outcome.");
        Require(!events.OfType<CardUseDeclaredEvent>().Any() && game.ResolutionStack.Count == 0 && game.State.ProcessingCardCount == 0,
            "Recast entered the card-use or nullification stack.");
        var hand = game.CreateSnapshot(0).Players[0].Hand.Select(card => card.Id).ToHashSet();
        Require(hand.Count == oldHand.Count && !hand.Contains(cardId) && hand.Except(oldHand).Count() == 1, "Recast did not replace exactly one card.");
        var zones = game.CreateCardZoneDiagnostics();
        Require(zones.Count == cardCount && zones.Select(card => card.CardId).Distinct().Count() == cardCount &&
            zones.Single(card => card.CardId == cardId).Location == CardLocation.DiscardPile, "Recast broke card inventory.");
        Require(game.CreateSnapshot(1).Players[0].Hand.Count == 0, "Recast revealed the replacement hand card to another viewer.");
        var replay = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), Registry);
        Require(State(replay) == State(game) && replay.Events.Select(e => e.Payload.ToString()).SequenceEqual(game.Events.Select(e => e.Payload.ToString())),
            "Recast checkpoint changed state or committed events on replay.");
        Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted && game.PendingDecision?.Kind == DecisionKind.PlayCard,
            "Recast did not return to the same player's play phase.");
    }

    public static void CurrentRulesAndAi()
    {
        var game = Opening();
        var actions = game.GetHumanLegalActions();
        Require(actions.Any(action => action.Kind == LegalActionKind.IronChain && action.TargetSeats.SequenceEqual([0])) &&
            actions.Any(action => action.Kind == LegalActionKind.IronChain && action.TargetSeats.Count == 2 && action.TargetSeats.Contains(0)),
            "Current rules do not allow self-only and self-plus-other Iron Chain.");
        var recast = actions.First(action => action.Kind == LegalActionKind.Recast);
        var end = actions.Single(action => action.Kind == LegalActionKind.EndPlay);
        var view = game.CreateSnapshot(0);
        foreach (var policy in new[] { 1, 2 })
        {
            var brain = new SimpleAiBrain(0, 7, policy);
            Require(brain.ChoosePlay(view, [recast, end], 1).Action.Kind == LegalActionKind.Recast, "AI did not prefer a useful recast to passing.");
            brain.ObserveRecast(view.TurnNumber, recast.CardId!.Value);
            Require(brain.ChoosePlay(view, [recast, end], 2).Action.Kind == LegalActionKind.EndPlay, "AI repeats the same recast within one turn.");
            Require(brain.ChoosePlay(view with { TurnNumber = view.TurnNumber + 1 }, [recast, end], 3).Action.Kind == LegalActionKind.Recast,
                "AI permanently banned a card after recasting it once.");
        }
    }

    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, true));
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
