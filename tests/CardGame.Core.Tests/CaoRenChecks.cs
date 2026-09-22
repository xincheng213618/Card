using CardGame.Content.Standard;
using CardGame.Core;

internal static class CaoRenChecks
{
    public static void JushouDrawFlipSkipAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var game = CreateSelectedCaoRen(registry);
        ReachHumanPlay(game);
        Require(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)).Accepted,
            "Cao Ren must receive a private optional Jushou window at the end phase.");
        for (var step = 0; step < 4 && game.PendingDecision?.SkillPrompt?.SkillId != "classic:jushou"; step++)
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Could not reach the Jushou window.");
        Require(game.PendingDecision is
                { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0, IsPrivate: true,
                  SkillPrompt.SkillId: "classic:jushou" },
            "Cao Ren must receive a private optional Jushou window at the end phase.");
        var prompt = game.PendingDecision!;
        var before = game.CreateSnapshot(0, revealAll: true).Players[0].HandCount;
        var paused = GameReplay.Restore(game.CreateCheckpoint(), registry);
        var use = prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Require(game.Submit(new AnswerPromptCommand(0, prompt.PromptId, use.Id, game.Revision)).Accepted,
            "Jushou use choice was rejected.");
        var after = game.CreateSnapshot(0, revealAll: true).Players[0];
        Require(after.HandCount == before + 3 && after.IsFaceDown &&
                game.CardMovements.Count(move => move.Reason.Value == "skill-program.classic:jushou.Draw" &&
                    move.To == CardLocation.Hand(0)) == 3,
            "Jushou must draw exactly three physical cards and turn Cao Ren face down.");

        var pausedPrompt = paused.PendingDecision!;
        Require(paused.Submit(new AnswerPromptCommand(0, pausedPrompt.PromptId,
                    pausedPrompt.Choices.Single(choice =>
                        choice.Parameters.GetValueOrDefault("program-action") == "activate").Id,
                    paused.Revision)).Accepted &&
                SnapshotJson.Serialize(paused.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            "A paused Jushou choice must replay to the same face-down state.");

        DriveUntilCaoRenSkipped(game);
        Require(!game.CreateSnapshot(0, revealAll: true).Players[0].IsFaceDown,
            "Cao Ren must turn face up after skipping his next turn.");
    }

    private static GameEngine CreateSelectedCaoRen(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 4096; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5,
                ModeId = "identity:classic-5", UseInteractiveSetup = true,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 80
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted) continue;
            var setup = game.PendingDecision;
            if (setup is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } &&
                setup.ValidContentIds.Contains("classic:cao-ren") &&
                game.Submit(new SelectGeneralCommand(0, "classic:cao-ren", game.Revision, setup.PromptId)).Accepted)
                return game;
        }
        throw new InvalidOperationException("Cao Ren was not selectable in the bounded fixture search.");
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 64 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Could not reach Cao Ren's play phase.");
        Require(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }, "Cao Ren never reached play.");
    }

    private static void DriveUntilCaoRenSkipped(GameEngine game)
    {
        var starts = game.Events.Select(item => item.Payload).OfType<TurnStartedEvent>().Count(item => item.ActorSeat == 0);
        for (var step = 0; step < 2048; step++)
        {
            var newStarts = game.Events.Select(item => item.Payload).OfType<TurnStartedEvent>().Count(item => item.ActorSeat == 0);
            if (newStarts > starts && !game.CreateSnapshot(0, revealAll: true).Players[0].IsFaceDown) return;
            if (game.PendingDecision is { PlayerSeat: 0 } prompt)
            {
                if (prompt.Kind == DecisionKind.PlayCard)
                    Require(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)).Accepted, "Could not end human play.");
                else if (prompt.Kind == DecisionKind.SelectHarvestCard)
                    game.HumanSelectHarvestCard(prompt.ValidCardIds[0], advanceToHumanBoundary: false);
                else
                {
                    var choice = prompt.Choices.FirstOrDefault();
                    Require(choice is not null && game.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision)).Accepted,
                        $"Could not answer human {prompt.Kind} while waiting for the flipped turn.");
                }
            }
            else Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Could not advance to the flipped turn.");
        }
        throw new InvalidOperationException("Cao Ren did not reach his skipped face-down turn.");
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
