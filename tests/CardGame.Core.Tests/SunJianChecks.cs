using CardGame.Content.Standard;
using CardGame.Core;

internal static class SunJianChecks
{
    public static void YinghunChoiceAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 58, 0));
        for (var seed = 1; seed <= 4096; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5,
                ModeId = "identity:classic-5", UseInteractiveSetup = true,
                UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 80
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted ||
                game.PendingDecision is not { Kind: DecisionKind.SelectGeneral } setup ||
                !setup.ValidContentIds.Contains("classic:sun-jian") ||
                !game.Submit(new SelectGeneralCommand(0, "classic:sun-jian", game.Revision, setup.PromptId)).Accepted)
                continue;

            var offer = ReachWoundedYinghun(game, 900);
            if (offer is null) continue;
            var ownerState = game.CreateSnapshot(0, revealAll: true).Players[0];
            if (ownerState.MaxHp - ownerState.Hp <= 1) continue;
            var choice = offer.Choices.FirstOrDefault(candidate =>
                candidate.Parameters.GetValueOrDefault("mode") == "discard-many" &&
                candidate.Targets.Count == 1 &&
                game.CreateSnapshot(0, revealAll: true).Players[candidate.Targets[0]].HandCount >= 2);
            if (choice is null) continue;

            var checkpoint = game.CreateCheckpoint();
            var targetSeat = choice.Targets.Single();
            var drawManyGame = GameReplay.Restore(checkpoint, registry);
            var drawManyOffer = drawManyGame.PendingDecision!;
            var drawManyChoice = drawManyOffer.Choices.Single(candidate =>
                candidate.Parameters.GetValueOrDefault("mode") == "draw-many" &&
                candidate.Targets.SequenceEqual([targetSeat]));
            var drawManyBefore = drawManyGame.CreateSnapshot(0, revealAll: true).Players[targetSeat];
            var drawManyBeforeCount = drawManyBefore.HandCount + drawManyBefore.Equipment.Count;
            Require(drawManyGame.Submit(new AnswerPromptCommand(0, drawManyOffer.PromptId,
                    drawManyChoice.Id, drawManyGame.Revision)).Accepted,
                "Yinghun draw-many choice was rejected.");
            ReachAfterYinghun(drawManyGame, 32);
            var drawManyResolved = drawManyGame.Events.Select(item => item.Payload)
                .OfType<YinghunResolvedEvent>().Last();
            var drawManyAfter = drawManyGame.CreateSnapshot(0, revealAll: true).Players[targetSeat];
            Require(drawManyResolved.DrawCount == drawManyResolved.LostHp &&
                    drawManyResolved.DiscardedCardIds.Count == 1 &&
                    drawManyAfter.HandCount + drawManyAfter.Equipment.Count ==
                    drawManyBeforeCount + drawManyResolved.LostHp - 1,
                "Yinghun must draw X then discard one exact target card in its first branch.");

            var beforeTarget = game.CreateSnapshot(0, revealAll: true).Players[targetSeat];
            var before = beforeTarget.HandCount + beforeTarget.Equipment.Count;
            Require(game.Submit(new AnswerPromptCommand(0, offer.PromptId, choice.Id, game.Revision)).Accepted,
                "Yinghun discard-many choice was rejected.");
            ReachAfterYinghun(game, 32);
            var resolved = game.Events.Select(item => item.Payload).OfType<YinghunResolvedEvent>().LastOrDefault();
            Require(resolved is { OwnerSeat: 0, TargetSeat: var actualTarget, LostHp: > 1, DrawCount: 1 } &&
                    actualTarget == targetSeat && resolved.DiscardedCardIds.Count == resolved.LostHp &&
                    game.CreateSnapshot(0, revealAll: true).Players[targetSeat] is var afterTarget &&
                    afterTarget.HandCount + afterTarget.Equipment.Count == before + 1 - resolved.DiscardedCardIds.Count &&
                    resolved.DiscardedCardIds.All(id => game.CardMovements.Any(move =>
                        move.CardId == id && move.Reason == CardMoveReasons.YinghunDiscard)),
                "Yinghun must draw one then discard X exact target cards when Sun Jian has lost X HP.");
            var expectedResolved = resolved!;

            var restored = GameReplay.Restore(checkpoint, registry);
            var restoredOffer = restored.PendingDecision!;
            var restoredChoice = restoredOffer.Choices.Single(candidate => candidate.Id == choice.Id);
            Require(restored.Submit(new AnswerPromptCommand(0, restoredOffer.PromptId, restoredChoice.Id, restored.Revision)).Accepted,
                "Restored Yinghun choice was rejected.");
            ReachAfterYinghun(restored, 32);
            Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                    SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                    restored.Events.Select(item => item.Payload).OfType<YinghunResolvedEvent>().Last() is var restoredResolved &&
                    restoredResolved.OwnerSeat == expectedResolved.OwnerSeat &&
                    restoredResolved.TargetSeat == expectedResolved.TargetSeat &&
                    restoredResolved.LostHp == expectedResolved.LostHp &&
                    restoredResolved.DrawCount == expectedResolved.DrawCount &&
                    restoredResolved.DiscardedCardIds.SequenceEqual(expectedResolved.DiscardedCardIds),
                "A paused and completed Yinghun resolution must replay exactly.");
            return;
        }
        throw new InvalidOperationException("No bounded wounded Sun Jian fixture exposed Yinghun.");
    }

    private static PendingDecision? ReachWoundedYinghun(GameEngine game, int limit)
    {
        for (var step = 0; step < limit; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.Yinghun, PlayerSeat: 0 } yinghun)
                return yinghun;
            if (!AdvanceConservatively(game)) return null;
        }
        return null;
    }

    private static void ReachAfterYinghun(GameEngine game, int limit)
    {
        for (var step = 0; step < limit; step++)
        {
            if (game.Events.Any(item => item.Payload is YinghunResolvedEvent)) return;
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "Yinghun AI discard sequence could not advance.");
        }
        throw new InvalidOperationException("Yinghun did not finish within the bounded step count.");
    }

    private static bool AdvanceConservatively(GameEngine game)
    {
        if (game.PendingDecision is not { } pending)
            return game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted;
        if (pending.PlayerSeat != 0)
            return game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted;
        if (pending.Kind == DecisionKind.PlayCard)
            return game.Submit(new EndPlayPhaseCommand(0, game.Revision, pending.PromptId)).Accepted;
        if (pending.Kind == DecisionKind.DiscardCards)
            return game.Submit(new DiscardCardsCommand(0, pending.ValidCardIds.Take(pending.RequiredCardCount).ToArray(),
                pending.PromptId, game.Revision)).Accepted;
        var choice = pending.Choices.FirstOrDefault(candidate =>
                         candidate.Parameters.GetValueOrDefault("action")?.Contains("skip", StringComparison.Ordinal) == true ||
                         candidate.Cards.Count == 0 && candidate.Targets.Count == 0) ??
                     pending.Choices.LastOrDefault();
        return choice is not null &&
               game.Submit(new AnswerPromptCommand(0, pending.PromptId, choice.Id, game.Revision)).Accepted;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
