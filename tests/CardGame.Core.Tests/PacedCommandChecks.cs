using CardGame.Content.Standard;
using CardGame.Core;

internal static class PacedCommandChecks
{
    private static readonly ContentRegistry Registry = StandardContentRegistry.Create();
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, true));

    public static void FullPacedReplay()
    {
        var responseCommand = new AnswerPromptCommand(0, new PromptId(3), new ChoiceId("response.take-damage"), 7);
        Require(CommandJson.Deserialize(CommandJson.Serialize([responseCommand])).Single() == responseCommand,
            "Response choice IDs must survive command JSON round trips.");
        var options = new GameOptions { Seed = 721019, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 90 };
        var game = GameEngine.CreateStandard(options, Registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Paced game did not start.");
        var observed = new HashSet<DecisionKind>();
        var mutatedCaller = false;
        for (var step = 0; step < 12000 && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is not null && observed.Add(prompt.Kind))
            {
                var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), Registry);
                Require(State(restored) == State(game), $"Paced {prompt.Kind} checkpoint differs.");
            }
            GameCommand command;
            if (prompt is null) command = new AdvanceOneStepCommand(game.Revision);
            else if (prompt.Kind == DecisionKind.SelectGeneral)
                command = new SelectGeneralCommand(0, prompt.ValidContentIds[0], game.Revision, prompt.PromptId);
            else if (prompt.Kind == DecisionKind.DiscardCards)
                command = new DiscardCardsCommand(0, prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(), prompt.PromptId, game.Revision);
            else if (prompt.Kind == DecisionKind.PlayCard)
            {
                var action = game.GetHumanLegalActions().FirstOrDefault(action =>
                    action.CardId is not null && action.Kind != LegalActionKind.UseProgramSkill);
                command = action is null ? new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)
                    : action.Kind == LegalActionKind.Recast ? new RecastCardCommand(0, action.CardId!.Value, game.Revision, prompt.PromptId)
                    : new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats.ToArray(), game.Revision,
                        prompt.PromptId, action.PlayedCardKind, action.TargetCardId)
                    {
                        ConversionSource = action.ConversionSource,
                        AdditionalConversionSources = action.AdditionalConversionSources
                    };
            }
            else command = new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices[0].Id, game.Revision);

            Action<GameSnapshot>? mutate = null;
            if (!mutatedCaller && command is PlayCardCommand { TargetSeats: int[] targets } && targets.Length > 0)
            {
                mutate = _ => targets[0] = -999;
                game.StateChanged += mutate;
                mutatedCaller = true;
            }
            var result = game.Submit(command);
            if (mutate is not null) game.StateChanged -= mutate;
            Require(result.Accepted, $"Paced command rejected: {result.Error?.Message}");
            if (command is SelectGeneralCommand or EndPlayPhaseCommand)
                Require(game.State.Status == EngineStatus.Running || game.State.Status == EngineStatus.Completed,
                    "A paced human command unexpectedly ran the next decision.");
        }
        Require(game.State.Status == EngineStatus.Completed && game.State.ProcessingCardCount == 0, "Paced game did not complete.");
        Require(mutatedCaller && observed.Count >= 4, "Fixture missed mutation isolation or human decision coverage.");
        Require(game.Revision == game.AcceptedCommands.Count && game.ObserverFailures.Count == 0, "Journal or observer boundary failed.");
        Require(game.AcceptedCommands.OfType<PlayCardCommand>().All(play => play.TargetSeats.All(seat => seat >= 0)), "Caller mutation corrupted the journal.");
        var replay = GameReplay.Restore(game.CreateCheckpoint(), Registry);
        Require(State(game) == State(replay), "Full paced game replay differs.");
    }
}
