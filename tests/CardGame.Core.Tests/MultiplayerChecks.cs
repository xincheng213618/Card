using CardGame.Content.Standard;
using CardGame.Core;

internal static class MultiplayerChecks
{
    public static void MultipleHumansWaitRespondAndReplay()
    {
        var humans = new List<int> { 0, 1, 2, 3, 4 };
        var registry = StandardContentRegistry.Create();
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 337, PlayerCount = 5, HumanSeat = 0, HumanSeats = humans,
            HumanRole = null, UseInteractiveSetup = true, AiPolicyVersion = AiDecisionPolicy.CurrentVersion
        }, registry);
        humans.Clear(); // Caller mutation must not change seat control or the checkpoint.
        Send(game, new StartGameCommand());
        var selected = new HashSet<int>();
        var played = new HashSet<int>();
        var responses = new HashSet<int>();
        for (var step = 0; step < 150 && game.State.Status != EngineStatus.Completed; step++)
        {
            var views = Enumerable.Range(0, 5).Select(seat => game.CreateSnapshot(seat)).ToArray();
            if (views.All(v => v.PendingDecision is null))
            {
                Send(game, new AdvanceCommand(game.Revision));
                continue;
            }
            var view = views.Single(v => v.PendingDecision is not null);
            var p = view.PendingDecision!;
            Require(view.HumanSeat == p.PlayerSeat, "The viewer must own its private prompt.");
            Require(views.All(v => v.Seed is null && v.Players.All(player =>
                player.Seat == v.HumanSeat || player.Hand.Count == 0)), "Another seat's hand was exposed.");
            var revision = game.Revision;
            var forged = game.Submit(new AnswerPromptCommand((p.PlayerSeat + 1) % 5, p.PromptId,
                p.Choices.FirstOrDefault()?.Id ?? new ChoiceId("wrong"), revision));
            Require(!forged.Accepted && game.Revision == revision, "A different seat answered the prompt.");
            GameCommand command;
            if (p.Kind == DecisionKind.SelectGeneral)
            {
                selected.Add(p.PlayerSeat);
                command = new SelectGeneralCommand(p.PlayerSeat, p.ValidContentIds[0], revision, p.PromptId);
            }
            else if (p.Kind == DecisionKind.PlayCard)
            {
                played.Add(p.PlayerSeat);
                var slash = game.GetLegalActions(p.PlayerSeat).FirstOrDefault(a => a.Kind == LegalActionKind.Slash && a.CardId is > 0);
                command = slash is null ? new EndPlayPhaseCommand(p.PlayerSeat, revision, p.PromptId)
                    : new PlayCardCommand(p.PlayerSeat, slash.CardId!.Value, slash.TargetSeats, revision, p.PromptId,
                        slash.PlayedCardKind, slash.TargetCardId) { ConversionSource = slash.ConversionSource };
            }
            else if (p.Kind == DecisionKind.DiscardCards)
                command = new DiscardCardsCommand(p.PlayerSeat, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, revision);
            else
            {
                responses.Add(p.PlayerSeat);
                command = new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices[0].Id, revision);
            }
            Send(game, command);
            if (selected.Count == 5 && played.Count == 5 && responses.Count >= 2) break;
        }
        Require(selected.Count == 5 && played.Count == 5 && responses.Count >= 2,
            $"Expected all seats to select/play and multiple responders; got {selected.Count}/{played.Count}/{responses.Count}.");
        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        Require(checkpoint.Options.HumanSeats?.Count == 5, "Control map was not frozen and recorded.");
        var restored = GameReplay.Restore(checkpoint, registry);
        foreach (var seat in Enumerable.Range(0, 5))
            Require(SnapshotJson.Serialize(game.CreateSnapshot(seat)) == SnapshotJson.Serialize(restored.CreateSnapshot(seat)), "Multiplayer replay changed a viewer.");
    }

    private static void Send(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.Message ?? "Command rejected.");
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
