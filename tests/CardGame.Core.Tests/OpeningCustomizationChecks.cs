using CardGame.Content.Standard;
using CardGame.Core;
using static BoundaryLiDianChecks;

internal static class OpeningCustomizationChecks
{
    public static void NonLordSelectsAfterPublicLordBeforePrivateAi()
    {
        const int human = 3;
        const string modeId = "identity:standard-5";
        var registry = StandardContentRegistry.CreateWithTeamModes();
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 5, HumanSeat = human, HumanRole = Role.Rebel,
            ModeId = modeId, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false
        }, registry);
        Accept(game, new StartGameCommand());
        ReachHumanSelection(game, human);
        var lord = game.CreateSnapshot(human).Players.Single(player => player.Role == Role.Lord).Seat;
        var selected = Selections(game);
        Require(selected is [var first] && first.ActorSeat == lord,
            "A nonzero human non-Lord must select immediately after the Lord, before any other AI commits a general.");
        var lordGeneral = selected[0].GeneralId;
        AssertSelectionPrivacy(game, lord, lordGeneral);
        Require(Enumerable.Range(0, game.PlayerCount).Append(-1).Where(seat => seat != human)
                .All(seat => game.CreateSnapshot(seat).PendingDecision is null),
            "The human's candidate prompt remains private after the Lord's public selection.");
        game = Cold(game, registry);

        var original = game.CreateSnapshot(human).PendingDecision!;
        Accept(game, new RequestGeneralRosterCommand(human, true, game.Revision, original.PromptId));
        var expanded = game.CreateSnapshot(human).PendingDecision!;
        var expected = registry.Modes[modeId].GeneralPoolIds!.Where(id => id != lordGeneral).ToHashSet(StringComparer.Ordinal);
        Require(expanded.ValidContentIds.Count == expected.Count && expected.SetEquals(expanded.ValidContentIds) &&
                expanded.ValidContentIds.Distinct(StringComparer.Ordinal).Count() == expanded.ValidContentIds.Count &&
                expanded.Choices.Select(choice => choice.Parameters["general-id"]).ToHashSet(StringComparer.Ordinal).SetEquals(expected),
            "The expanded identity roster is exactly the mode's shared pool minus the publicly selected Lord, with no hidden AI reservations or duplicate exact IDs.");
        game = Cold(game, registry);
        Accept(game, new RequestGeneralRosterCommand(human, false, game.Revision, expanded.PromptId));
        Require(game.CreateSnapshot(human).PendingDecision!.ValidContentIds.SequenceEqual(original.ValidContentIds),
            "Returning from the expanded roster preserves the original private offers without selecting another seat.");
        Accept(game, new RequestGeneralRosterCommand(human, true, game.Revision, game.PendingDecision!.PromptId));
        var chosen = game.PendingDecision!.ValidContentIds.Last();
        Accept(game, new SelectGeneralCommand(human, chosen, game.Revision, game.PendingDecision.PromptId));
        Require(Selections(game).Select(selection => selection.ActorSeat).SequenceEqual([lord, human]) &&
                game.CreateSnapshot(human).Players[human].GeneralId == chosen,
            "The human's exact expanded-roster choice commits second, before the remaining AI choose from the shared pool.");
        AssertSelectionPrivacy(game, lord, lordGeneral);
        game = Cold(game, registry);

        var observedAiSelection = false;
        for (var step = 0; step < 16 && !game.Events.Any(item => item.Payload is SetupCompletedEvent); step++)
        {
            Accept(game, new AdvanceOneStepCommand(game.Revision));
            selected = Selections(game);
            Require(selected.Select(selection => selection.GeneralId).Distinct(StringComparer.Ordinal).Count() == selected.Length,
                "Every AI removes its chosen exact ID from the same shared pool; neither the Lord nor human choice can be reused.");
            if (!game.Events.Any(item => item.Payload is SetupCompletedEvent))
            {
                AssertSelectionPrivacy(game, lord, lordGeneral);
                if (!observedAiSelection && selected.Length > 2)
                {
                    observedAiSelection = true;
                    game = Cold(game, registry);
                }
            }
        }
        selected = Selections(game);
        Require(observedAiSelection && game.Events.Count(item => item.Payload is SetupCompletedEvent) == 1 &&
                selected.Length == game.PlayerCount && selected.Take(2).Select(selection => selection.ActorSeat).SequenceEqual([lord, human]),
            "The bounded setup exposes a private AI selection boundary and completes once after Lord, human and remaining AI.");
        foreach (var viewer in Enumerable.Range(0, game.PlayerCount).Append(-1))
            Require(game.CreateSnapshot(viewer).Players.All(player => player.IsGeneralPublic &&
                    player.GeneralId == selected.Single(selection => selection.ActorSeat == player.Seat).GeneralId),
                "Identity setup completion publicly reveals every exact committed general to all seats and the spectator.");
        game = Cold(game, registry);
        AssertFirstTurn(game, lord);
    }

    public static void TeamAndNationalHumanChooseBeforePrivateAi()
    {
        const int human = 3;
        var registry = StandardContentRegistry.CreateWithTeamModesAndNationalWarAmbitious();
        foreach (var modeId in new[] { "team:standard-2v2", "national:lite-4" })
        {
            var national = modeId.StartsWith("national:", StringComparison.Ordinal);
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 31, PlayerCount = 4, HumanSeat = human, HumanRole = null,
                ModeId = modeId, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false
            }, registry);
            Accept(game, new StartGameCommand());
            ReachHumanSelection(game, human);
            Require(Selections(game).Length == 0 && !game.Events.Any(item => item.Payload is SecondaryGeneralSelectedEvent) &&
                    game.CreateSnapshot(human).Players.All(player => national ? player.Role is null :
                        !string.IsNullOrWhiteSpace(player.TeamId) && player.IsTeamRevealed),
                "National identity roles remain hidden and team membership remains public while the nonzero human selects before any AI general.");
            var faction = game.CreateSnapshot(human).Players[human].FactionId;
            var expected = registry.Modes[modeId].GeneralPoolIds!
                .Where(id => !national || registry.Generals[id].FactionId == faction).ToHashSet(StringComparer.Ordinal);
            var humanIds = new List<string>();
            for (var slot = 0; slot < (national ? 2 : 1); slot++)
            {
                ReachHumanSelection(game, human);
                Require(Selections(game).All(selection => selection.ActorSeat == human) &&
                        game.Events.Select(item => item.Payload).OfType<SecondaryGeneralSelectedEvent>()
                            .All(selection => selection.ActorSeat == human),
                    "Both national human choices remain consecutive; no AI may consume the shared faction pool between them.");
                var prompt = game.CreateSnapshot(human).PendingDecision!;
                Accept(game, new RequestGeneralRosterCommand(human, true, game.Revision, prompt.PromptId));
                prompt = game.CreateSnapshot(human).PendingDecision!;
                Require(prompt.ValidContentIds.Count == expected.Count && expected.SetEquals(prompt.ValidContentIds) &&
                        prompt.ValidContentIds.Distinct(StringComparer.Ordinal).Count() == prompt.ValidContentIds.Count &&
                        Enumerable.Range(0, game.PlayerCount).Append(-1).Where(seat => seat != human)
                            .All(seat => game.CreateSnapshot(seat).PendingDecision is null),
                    "The private full roster contains exactly the mode/faction pool minus the human's own earlier slot, with no hidden AI reservations.");
                game = Cold(game, registry);
                var chosen = prompt.ValidContentIds.Last();
                Accept(game, new SelectGeneralCommand(human, chosen, game.Revision, prompt.PromptId));
                humanIds.Add(chosen);
                expected.Remove(chosen);
                AssertNoLordSelectionPrivacy(game);
                game = Cold(game, registry);
            }
            Require(Selections(game) is [var primary] && primary.ActorSeat == human && primary.GeneralId == humanIds[0] &&
                    (national ? game.Events.Select(item => item.Payload).OfType<SecondaryGeneralSelectedEvent>()
                        .Single().GeneralId == humanIds[1] : humanIds.Count == 1),
                "The intended human completes its exact primary, and national secondary, before any AI selects.");

            var observedAiSelection = false;
            for (var step = 0; step < 20 && !game.Events.Any(item => item.Payload is SetupCompletedEvent); step++)
            {
                Accept(game, new AdvanceOneStepCommand(game.Revision));
                var ids = Selections(game).Select(selection => selection.GeneralId).Concat(game.Events.Select(item => item.Payload)
                    .OfType<SecondaryGeneralSelectedEvent>().Select(selection => selection.GeneralId)).ToArray();
                Require(ids.Distinct(StringComparer.Ordinal).Count() == ids.Length,
                    "All primary and secondary slots consume distinct exact IDs from the same mode pool.");
                if (!game.Events.Any(item => item.Payload is SetupCompletedEvent))
                {
                    AssertNoLordSelectionPrivacy(game);
                    if (!observedAiSelection && Selections(game).Any(selection => selection.ActorSeat != human))
                    {
                        observedAiSelection = true;
                        game = Cold(game, registry);
                    }
                }
            }
            Require(observedAiSelection && game.Events.Count(item => item.Payload is SetupCompletedEvent) == 1 &&
                    Selections(game).Length == game.PlayerCount &&
                    game.Events.Count(item => item.Payload is SecondaryGeneralSelectedEvent) == (national ? game.PlayerCount : 0),
                "The bounded no-Lord setup completes each required slot once after the human, without playing a match.");
            if (national) AssertNoLordSelectionPrivacy(game);
            else
                foreach (var viewer in Enumerable.Range(0, game.PlayerCount).Append(-1))
                    Require(game.CreateSnapshot(viewer).Players.All(player => player.IsGeneralPublic &&
                            player.GeneralId == Selections(game).Single(selection => selection.ActorSeat == player.Seat).GeneralId),
                        "Team setup completion publishes all exact selections together.");
            var expectedFirst = national ? 0 : game.CreateSnapshot(human).Players
                .Where(player => player.TeamId == "team:blue").Min(player => player.Seat);
            game = Cold(game, registry);
            AssertFirstTurn(game, expectedFirst);
        }
    }

    public static void PrivateRosterAndOpeningHandReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 5, HumanRole = Role.Lord,
            ModeId = "identity:standard-5", DeckId = "classic:standard-deck", UseInteractiveSetup = true, ConfirmOpeningHand = true,
            AdvanceAfterHumanCommands = false }, registry);
        Accept(game, new StartGameCommand());
        var original = game.PendingDecision!;
        Require(original.Kind == DecisionKind.SelectGeneral, "The real setup starts with its human candidate prompt.");
        Accept(game, new RequestGeneralRosterCommand(0, true, game.Revision, original.PromptId));
        var expanded = game.PendingDecision!;
        var custom = expanded.ValidContentIds.Except(original.ValidContentIds).First();
        Require(expanded.PromptId != original.PromptId && Enumerable.Range(1, 4)
                .All(seat => game.CreateSnapshot(seat).PendingDecision is null),
            "The available mode roster replaces the private prompt without exposing it to other seats.");
        Cold(game, registry);
        Accept(game, new RequestGeneralRosterCommand(0, false, game.Revision, expanded.PromptId));
        Require(game.PendingDecision!.ValidContentIds.SequenceEqual(original.ValidContentIds) &&
                game.PendingDecision.Choices.All(choice => !choice.Parameters.ContainsKey("full-roster")),
            "Returning to candidates restores the original limited offers without selecting a general.");
        Accept(game, new RequestGeneralRosterCommand(0, true, game.Revision, game.PendingDecision.PromptId));
        expanded = game.PendingDecision!;
        var before = GameCheckpointJson.Serialize(game.CreateCheckpoint());
        Require(!game.Submit(new SelectGeneralCommand(0, custom, game.Revision, original.PromptId)).Accepted &&
                before == GameCheckpointJson.Serialize(game.CreateCheckpoint()), "The old candidate prompt is rejected atomically.");
        Accept(game, new SelectGeneralCommand(0, custom, game.Revision, expanded.PromptId));
        for (var step = 0; step < 30 && game.PendingDecision?.Kind != DecisionKind.OpeningHand; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Require(game.PendingDecision?.Kind == DecisionKind.OpeningHand && game.State.TurnNumber == 0 &&
                !game.Events.Any(e => e.Payload is GameStartedEvent or TurnStartedEvent),
            "Initial hands wait before game-start programs and the first turn.");
        var others = Enumerable.Range(1, 4).Select(seat => game.CreateSnapshot(seat).Players[seat].Hand!.Select(c => c.Id).ToArray()).ToArray();
        var hand = game.State.Players[0].Hand!.Select(c => c.Id).ToArray();
        for (var reroll = 0; reroll < 2; reroll++)
        {
            var prompt = game.PendingDecision!;
            game = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Accept(game, new AnswerPromptCommand(0, prompt.PromptId,
                prompt.Choices.Single(c => c.Parameters["action"] == "reroll").Id, game.Revision));
            Require(game.State.TurnNumber == 0 && game.State.Players[0].Hand!.Count == hand.Length &&
                    game.PendingDecision!.PromptId != prompt.PromptId,
                "Each reroll keeps the actual initial hand count and publishes a fresh pre-game prompt.");
            for (var seat = 1; seat < 5; seat++)
                Require(game.CreateSnapshot(seat).Players[seat].Hand!.Select(c => c.Id).SequenceEqual(others[seat - 1]) &&
                        game.CreateSnapshot(seat).Players[0].Hand!.Count == 0,
                    "Reroll leaves other private hands untouched and does not expose the human's cards.");
            before = GameCheckpointJson.Serialize(game.CreateCheckpoint());
            Require(!game.Submit(new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices[0].Id, game.Revision)).Accepted &&
                    before == GameCheckpointJson.Serialize(game.CreateCheckpoint()), "A retired reroll prompt cannot draw again.");
            Cold(game, registry);
        }
        Require(!game.State.Players[0].Hand!.Select(c => c.Id).SequenceEqual(hand), "The verified seed produces a newly randomized hand.");
        var zones = game.CreateCardZoneDiagnostics().ToArray();
        var spear = zones.Single(card => card.CardKind == CardKind.ZhangbaSerpentSpear);
        if (spear.Location != CardLocation.Hand(0))
            Replace(CardKind.ZhangbaSerpentSpear);
        Require(game.State.Players[0].Hand.Any(card => card.Kind == CardKind.ZhangbaSerpentSpear) &&
                !game.PendingDecision!.Choices.Any(choice => choice.Parameters.GetValueOrDefault("kind") == nameof(CardKind.ZhangbaSerpentSpear)),
            "Selecting the unique Zhangba puts its real entity in the hand and cannot create a second copy.");
        Require(game.CreateCardZoneDiagnostics().Select(card => card.CardId).Order().SequenceEqual(zones.Select(card => card.CardId).Order()) &&
                game.CreateCardZoneDiagnostics().Count == zones.Length,
            "Opening replacements preserve every physical entity and total deck size.");
        var confirmation = game.PendingDecision!;
        Accept(game, new AnswerPromptCommand(0, confirmation.PromptId,
            confirmation.Choices.Single(c => c.Parameters["action"] == "confirm").Id, game.Revision));
        Require(game.Events.Count(e => e.Payload is GameStartedEvent) == 1 && game.Events.Count(e => e.Payload is SetupCompletedEvent) == 1,
            "Confirmation starts the match exactly once after the final hand is chosen.");
        Accept(game, new AdvanceOneStepCommand(game.Revision));
        before = GameCheckpointJson.Serialize(game.CreateCheckpoint());
        Require(!game.Submit(new RequestGeneralRosterCommand(0, true, game.Revision, confirmation.PromptId)).Accepted &&
                !game.Submit(new AnswerPromptCommand(0, confirmation.PromptId, confirmation.Choices[0].Id, game.Revision)).Accepted &&
                before == GameCheckpointJson.Serialize(game.CreateCheckpoint()), "Setup edits are unavailable after confirmation.");
        Cold(game, registry);
        ReplaceFromDealtHand();

        void Replace(CardKind kind)
        {
            var prompt = game.PendingDecision!;
            var choice = prompt.Choices.First(choice => choice.Parameters.GetValueOrDefault("kind") == kind.ToString() &&
                game.State.Players[0].Hand.Single(card => card.Id == choice.Cards.Single()).Kind != CardKind.ZhangbaSerpentSpear);
            var counts = Enumerable.Range(0, 5).Select(seat => game.CreateSnapshot(seat).Players[seat].Hand.Count).ToArray();
            var ownIds = game.State.Players[0].Hand.Select(card => card.Id).ToHashSet();
            Require(prompt.Choices.Where(choice => choice.Parameters["action"] == "replace").All(choice =>
                    choice.Cards.Count == 1 && ownIds.Contains(choice.Cards[0]) && choice.Targets.Count == 0 &&
                    choice.Parameters.Keys.Order().SequenceEqual(new[] { "action", "kind" })),
                "Published replacements contain only an owned slot and a card kind, never the source entity, owner or deck order.");
            Accept(game, new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision));
            Require(game.State.Players[0].Hand.Any(card => card.Kind == kind && !ownIds.Contains(card.Id)) &&
                    game.PendingDecision!.PromptId != prompt.PromptId && game.State.TurnNumber == 0 &&
                    Enumerable.Range(0, 5).All(seat => game.CreateSnapshot(seat).Players[seat].Hand.Count == counts[seat]) &&
                    Enumerable.Range(1, 4).Append(-1).All(seat => game.CreateSnapshot(seat).PendingDecision is null &&
                        game.CreateSnapshot(seat).Players[0].Hand.Count == 0),
                "A replacement changes exactly one human hand slot, preserves every hand count, stays private and waits for confirmation.");
            var unchanged = GameCheckpointJson.Serialize(game.CreateCheckpoint());
            Require(!game.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision)).Accepted &&
                    unchanged == GameCheckpointJson.Serialize(game.CreateCheckpoint()), "A retired replacement cannot move cards twice.");
            game = Cold(game, registry);
        }
    }

    private static void ReplaceFromDealtHand()
    {
        const int human = 3;
        var registry = ContentRegistry.Build(new StandardContentPackage(), new OpeningDeckFixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 5, HumanSeat = human,
            HumanRole = Role.Rebel, ModeId = "identity:standard-5", DeckId = "fixture:opening-dealt-deck",
            UseInteractiveSetup = true, ConfirmOpeningHand = true, AdvanceAfterHumanCommands = false }, registry);
        Accept(game, new StartGameCommand());
        ReachHumanSelection(game, human);
        Accept(game, new SelectGeneralCommand(human, game.PendingDecision!.ValidContentIds.First(), game.Revision, game.PendingDecision.PromptId));
        for (var step = 0; step < 16 && game.PendingDecision?.Kind != DecisionKind.OpeningHand; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        var before = game.CreateCardZoneDiagnostics().ToArray();
        Require(before.All(card => card.Location.Zone == CardZoneKind.Hand),
            "The small physical fixture deals every card, so replacement must come from another private hand without a seed search.");
        var prompt = game.PendingDecision!;
        var choice = prompt.Choices.First(choice => choice.Parameters["action"] == "replace");
        var kind = Enum.Parse<CardKind>(choice.Parameters["kind"]);
        var ownIds = before.Where(card => card.Location.OwnerSeat == human).Select(card => card.CardId).ToHashSet();
        Accept(game, new AnswerPromptCommand(human, prompt.PromptId, choice.Id, game.Revision));
        var after = game.CreateCardZoneDiagnostics();
        var incoming = after.Single(card => card.Location == CardLocation.Hand(human) && !ownIds.Contains(card.CardId));
        var source = before.Single(card => card.CardId == incoming.CardId);
        Require(incoming.CardKind == kind && source.Location != CardLocation.Hand(human) &&
                after.Single(card => card.CardId == choice.Cards.Single()).Location == source.Location &&
                after.Select(card => card.CardId).Order().SequenceEqual(before.Select(card => card.CardId).Order()) &&
                Enumerable.Range(0, 5).All(seat => after.Count(card => card.Location == CardLocation.Hand(seat)) == 4) &&
                game.State.TurnNumber == 0 && Enumerable.Range(0, 5).Append(-1).Where(seat => seat != human)
                    .All(seat => game.CreateSnapshot(seat).PendingDecision is null && game.CreateSnapshot(seat).Players[human].Hand.Count == 0),
            "A nonzero human can swap a dealt physical card without exposing other hands, changing counts or starting a turn.");
        Cold(game, registry);
    }

    private sealed class OpeningDeckFixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:opening-dealt", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder) => builder.AddDeck(new("fixture:opening-dealt-deck",
            "All cards dealt", 4, 2, [new("standard:slash", 10), new("standard:dodge", 10)]));
    }

    private static GeneralSelectedEvent[] Selections(GameEngine game) =>
        game.Events.Select(item => item.Payload).OfType<GeneralSelectedEvent>().ToArray();

    private static void AssertFirstTurn(GameEngine game, int expectedSeat)
    {
        Require(!game.Events.Any(item => item.Payload is TurnStartedEvent),
            "Completing selection must leave the match before its first actual turn.");
        for (var step = 0; step < 8 && !game.Events.Any(item => item.Payload is TurnStartedEvent); step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        var turns = game.Events.Select(item => item.Payload).OfType<TurnStartedEvent>().ToArray();
        Require(turns is [var first] && first.TurnNumber == 1 && first.ActorSeat == expectedSeat &&
                !game.Events.Any(item => item.Payload is TurnEndedEvent),
            "Human-first selection must preserve the mode's original initiative: identity Lord, first blue team seat, or national seat zero.");
        foreach (var viewer in Enumerable.Range(0, game.PlayerCount).Append(-1))
        {
            var snapshot = game.CreateSnapshot(viewer);
            Require(snapshot.TurnNumber == 1 && snapshot.CurrentSeat == expectedSeat,
                "Every public player and spectator view must show the same actual first turn without advancing an entire match.");
        }
    }

    private static void ReachHumanSelection(GameEngine game, int human)
    {
        for (var step = 0; step < 16 && game.CreateSnapshot(human).PendingDecision?.Kind != DecisionKind.SelectGeneral; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Require(game.CreateSnapshot(human).PendingDecision is { Kind: DecisionKind.SelectGeneral } prompt && prompt.PlayerSeat == human,
            "The bounded setup must reach the intended nonzero human seat's private selection prompt.");
    }

    private static void AssertSelectionPrivacy(GameEngine game, int lord, string lordGeneral)
    {
        var selected = Selections(game);
        foreach (var viewer in Enumerable.Range(0, game.PlayerCount).Append(-1))
        {
            var snapshot = game.CreateSnapshot(viewer);
            Require(snapshot.Players[lord].IsGeneralPublic && snapshot.Players[lord].GeneralId == lordGeneral,
                "The selected Lord's exact general is public immediately, before any later player selects.");
            foreach (var selection in selected.Where(selection => selection.ActorSeat != lord))
            {
                var player = snapshot.Players[selection.ActorSeat];
                Require(!player.IsGeneralPublic && (viewer == selection.ActorSeat
                        ? player.GeneralId == selection.GeneralId : player.GeneralId != selection.GeneralId),
                    "A committed non-Lord general remains visible only to its owner until setup completes.");
            }
        }
    }

    private static void AssertNoLordSelectionPrivacy(GameEngine game)
    {
        foreach (var viewer in Enumerable.Range(0, game.PlayerCount).Append(-1))
        {
            var snapshot = game.CreateSnapshot(viewer);
            foreach (var selection in Selections(game))
            {
                var player = snapshot.Players[selection.ActorSeat];
                Require(!player.IsGeneralPublic && (viewer == selection.ActorSeat
                        ? player.GeneralId == selection.GeneralId : player.GeneralId != selection.GeneralId),
                    "A no-Lord primary selection remains visible only to its owner until its mode permits a public reveal.");
            }
            foreach (var selection in game.Events.Select(item => item.Payload).OfType<SecondaryGeneralSelectedEvent>())
            {
                var player = snapshot.Players[selection.ActorSeat];
                Require(!player.IsSecondaryGeneralPublic && (viewer == selection.ActorSeat
                        ? player.SecondaryGeneralId == selection.GeneralId : player.SecondaryGeneralId != selection.GeneralId),
                    "A national secondary selection remains private to its owner throughout setup and after setup completion.");
            }
        }
    }

    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        foreach (var seat in Enumerable.Range(0, game.PlayerCount).Append(-1))
            Require(SnapshotJson.Serialize(restored.CreateSnapshot(seat)) == SnapshotJson.Serialize(game.CreateSnapshot(seat)),
                "Every seat and spectator snapshot matches the saved command replay.");
        return restored;
    }
}
