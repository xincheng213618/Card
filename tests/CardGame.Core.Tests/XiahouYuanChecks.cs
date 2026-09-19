using CardGame.Content.Standard;
using CardGame.Core;

internal static class XiahouYuanChecks
{
    public static void ShensuPhaseSkipsAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 54, 0));
        var game = FindFixture(registry);
        var prompt = game.PendingDecision!;
        var paused = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(paused.PendingDecision is { Kind: DecisionKind.Shensu } restoredPrompt &&
                restoredPrompt.Choices.Select(choice => choice.Id).SequenceEqual(prompt.Choices.Select(choice => choice.Id)),
            "A paused private Shensu phase choice must restore with the same published choices.");
        var handBefore = game.CreateSnapshot(0, revealAll: true).Players[0].Hand.Select(card => card.Id).Order().ToArray();
        var use = prompt.Choices.First(choice => choice.Parameters.GetValueOrDefault("action") == "shensu-use");
        var accepted = game.Submit(new AnswerPromptCommand(0, prompt.PromptId, use.Id, game.Revision));
        Require(accepted.Accepted, accepted.Error?.Message ?? "Shensu's first option was rejected.");
        DriveToHumanBoundary(game);

        var eventItem = game.Events.Select(item => item.Payload).OfType<ShensuUsedEvent>().Single();
        Require(eventItem.Stage == 1 && eventItem.DiscardedEquipmentCardId is null &&
                game.CreateSnapshot(0, revealAll: true).Players[0].Hand.Select(card => card.Id).Order().SequenceEqual(handBefore) &&
                game.CreateSnapshot(0, revealAll: true).Phase == TurnPhase.Play && game.ResolutionStack.Count == 0,
            "Shensu option one must skip judgment/draw, consume no card and resume at Play after its virtual Slash.");

        var replay = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            "Completed Shensu option one must replay exactly.");

        var stageTwoGame = FindFixture(registry);
        EquipFirstHandEquipment(stageTwoGame);
        var nextStageOne = stageTwoGame.PendingDecision!;
        var skipOne = nextStageOne.Choices.Single(choice => choice.Parameters.GetValueOrDefault("action") == "shensu-skip");
        Require(stageTwoGame.Submit(new AnswerPromptCommand(0, nextStageOne.PromptId, skipOne.Id, stageTwoGame.Revision)).Accepted,
            "Could not skip Shensu option one before the option-two fixture.");
        DriveToStageTwo(stageTwoGame);
        Require(stageTwoGame.PendingDecision is { Kind: DecisionKind.Shensu } &&
                stageTwoGame.PendingDecision.Choices.Any(choice => choice.Cards.Count == 1),
            "An equipped card must open Shensu option two before the play phase.");
        var stageTwo = stageTwoGame.PendingDecision!;
        var useTwo = stageTwo.Choices.First(choice => choice.Cards.Count == 1);
        var stageTwoEquipmentId = useTwo.Cards.Single();
        Require(stageTwoGame.Submit(new AnswerPromptCommand(0, stageTwo.PromptId, useTwo.Id, stageTwoGame.Revision)).Accepted,
            "Shensu option two was rejected.");
        DriveUntilResolutionFinishes(stageTwoGame);
        Require(stageTwoGame.Events.Select(item => item.Payload).OfType<ShensuUsedEvent>()
                    .Any(item => item.Stage == 2 && item.DiscardedEquipmentCardId == stageTwoEquipmentId) &&
                stageTwoGame.CardMovements.Any(move => move.CardId == stageTwoEquipmentId &&
                    move.From == CardLocation.Equipment(0) && move.To == CardLocation.DiscardPile &&
                    move.Reason == CardMoveReasons.ShensuDiscard),
            "Shensu option two must discard the exact equipped card and resolve another virtual Slash.");

    }

    private static GameEngine FindFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 4096; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5,
                ModeId = "identity:classic-5", UseInteractiveSetup = true,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 100
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted ||
                game.PendingDecision is not { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } setup ||
                !setup.ValidContentIds.Contains("classic:xiahou-yuan") ||
                !game.Submit(new SelectGeneralCommand(0, "classic:xiahou-yuan", game.Revision, setup.PromptId)).Accepted)
                continue;
            for (var step = 0; step < 64 && game.PendingDecision?.Kind != DecisionKind.Shensu; step++)
                if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted) break;
            if (game.PendingDecision is { Kind: DecisionKind.Shensu, PlayerSeat: 0 } prompt &&
                prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("action") == "shensu-use") &&
                game.CreateSnapshot(0, revealAll: true).Players[0].Hand.Any(card => EquipmentCatalog.IsEquipment(card.Kind)))
                return game;
        }
        throw new InvalidOperationException("No bounded Xiahou Yuan fixture reached the first Shensu choice.");
    }

    private static void DriveToHumanBoundary(GameEngine game)
    {
        for (var step = 0; step < 256 && (game.ResolutionStack.Count > 0 || game.PendingDecision?.Kind != DecisionKind.PlayCard); step++)
        {
            GameCommand command;
            if (game.PendingDecision is { PlayerSeat: 0 } prompt)
            {
                var choice = prompt.Choices.FirstOrDefault(item =>
                    item.Parameters.GetValueOrDefault("response")?.Contains("decline", StringComparison.Ordinal) == true ||
                    item.Parameters.Values.Any(value => value.Contains("skip", StringComparison.Ordinal) || value.Contains("decline", StringComparison.Ordinal)))
                    ?? prompt.Choices.Last();
                command = new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision);
            }
            else command = new AdvanceOneStepCommand(game.Revision);
            var result = game.Submit(command);
            Require(result.Accepted, result.Error?.Message ?? "Could not finish the Shensu virtual Slash.");
        }
    }

    private static void DriveUntilResolutionFinishes(GameEngine game)
    {
        for (var step = 0; step < 256 && game.ResolutionStack.Count > 0; step++)
        {
            GameCommand command = game.PendingDecision is { PlayerSeat: 0 } prompt
                ? new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.Last().Id, game.Revision)
                : new AdvanceOneStepCommand(game.Revision);
            var result = game.Submit(command);
            Require(result.Accepted, result.Error?.Message ?? "Could not finish Shensu option two.");
        }
        Require(game.ResolutionStack.Count == 0, "Shensu option two left an unfinished resolution frame.");
    }

    private static void DriveToStageTwo(GameEngine game)
    {
        for (var step = 0; step < 256; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.Shensu } prompt &&
                prompt.Choices.Any(choice => choice.Cards.Count == 1)) return;
            GameCommand command = game.PendingDecision is { PlayerSeat: 0 } decision
                ? new AnswerPromptCommand(0, decision.PromptId, decision.Choices.Last().Id, game.Revision)
                : new AdvanceOneStepCommand(game.Revision);
            var result = game.Submit(command);
            Require(result.Accepted, result.Error?.Message ?? "Could not reach Shensu option two.");
        }
    }

    private static void EquipFirstHandEquipment(GameEngine game)
    {
        var zones = typeof(GameEngine).GetField("_cardZones", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(game)!;
        var cards = (IEnumerable<Card>)zones.GetType().GetMethod("CardsAt")!.Invoke(zones, [CardLocation.Hand(0)])!;
        var equipment = cards.First(card => EquipmentCatalog.IsEquipment(card.Kind));
        typeof(GameEngine).GetMethod("MoveCard", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(game, [equipment, CardLocation.Hand(0), CardLocation.Equipment(0), CardMoveReasons.EquipmentEnter]);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
