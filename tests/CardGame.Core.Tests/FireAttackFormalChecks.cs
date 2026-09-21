using CardGame.Content.Standard;
using CardGame.Core;

internal static class FireAttackFormalChecks
{
    public static void SelfTargetAndReplay()
    {
        Require(GameCheckpoint.CurrentRulesVersion >= 19,
            "Formal FireAttack requires rules version 19 or newer.");
        var registry = StandardContentRegistry.Create();
        var fixture = FindSelfTargetFixture(registry);
        var formal = fixture.Game;
        var fireAttackId = fixture.CardId;
        Require(formal.GetHumanLegalActions().Any(action =>
                action.Kind == LegalActionKind.FireAttack &&
                action.CardId == fireAttackId &&
                action.TargetSeat == 0),
            "Current rules did not expose the formal self-target FireAttack action.");

        formal = ResolveFormalSelfTarget(formal, fireAttackId, registry);
        AssertReplay(formal, registry);
    }

    private static (GameEngine Game, int Seed, int CardId) FindSelfTargetFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 8_192; seed++)
        {
            var game = CreateStartedGame(seed, registry, GameCheckpoint.CurrentRulesVersion);
            var human = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
            if (human.Skill != SkillKind.None)
                continue;

            var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
                candidate.Kind == LegalActionKind.FireAttack &&
                candidate.TargetSeat == 0);
            if (action?.CardId is { } cardId &&
                game.GetHumanLegalActions().Any(candidate =>
                    candidate.Kind == LegalActionKind.FireAttack &&
                    candidate.CardId == cardId &&
                    candidate.TargetSeat is not null and not 0))
            {
                return (game, seed, cardId);
            }
        }

        throw new InvalidOperationException("Could not find a deterministic formal FireAttack self-target fixture.");
    }

    private static GameEngine ResolveFormalSelfTarget(
        GameEngine game,
        int fireAttackId,
        ContentRegistry registry)
    {
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.FireAttack &&
            candidate.CardId == fireAttackId &&
            candidate.TargetSeat == 0);
        var hpBefore = game.State.Players.Single(player => player.Seat == 0).Hp;
        Require(Play(game, action).Accepted, "Formal self-target FireAttack was rejected.");
        AdvanceUntil(game, DecisionKind.FireAttackReveal);

        var reveal = game.PendingDecision!;
        var revealChoice = reveal.Choices.First();
        var revealedCardId = revealChoice.Cards.Single();
        var handCountBeforeReveal = game.State.Players.Single(player => player.Seat == 0).HandCount;
        Require(game.Submit(new AnswerPromptCommand(
            0,
            reveal.PromptId,
            revealChoice.Id,
            game.Revision)).Accepted,
            "Formal FireAttack reveal was rejected.");

        var discard = game.PendingDecision is { Kind: DecisionKind.FireAttackDiscard, PlayerSeat: 0 } prompt
            ? prompt
            : throw new InvalidOperationException(
                "Formal self-target FireAttack did not open the matching-suit discard prompt.");
        Require(game.State.Players.Single(player => player.Seat == 0).HandCount == handCountBeforeReveal,
            "Showing a FireAttack card incorrectly removed it from the hand.");
        Require(game.CreateCardZoneDiagnostics().Single(card => card.CardId == revealedCardId).Location == CardLocation.Hand(0),
            "The shown FireAttack card did not remain in the target hand.");
        var ordinary = game.CreateSnapshot(1);
        Require(ordinary.PublicRevealedCards.Single().Id == revealedCardId,
            "The shown FireAttack card was not publicly visible.");
        Require(ordinary.Players.Single(player => player.Seat == 0).Hand.All(card => card.Id != revealedCardId),
            "An ordinary viewer learned the rest of the target hand.");

        var pausedSnapshot = SnapshotJson.Serialize(game.CreateSnapshot(1));
        game = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(game.CreateSnapshot(1)) == pausedSnapshot,
            "The formal public reveal boundary did not replay exactly.");
        discard = game.PendingDecision is { Kind: DecisionKind.FireAttackDiscard, PlayerSeat: 0 } restoredPrompt
            ? restoredPrompt
            : throw new InvalidOperationException(
                "The restored formal FireAttack lost its discard prompt.");
        Require(game.CreateCardZoneDiagnostics().Single(card => card.CardId == revealedCardId).Location == CardLocation.Hand(0) &&
                game.CreateSnapshot(1).PublicRevealedCards.Single().Id == revealedCardId,
            "The restored formal FireAttack lost the shown card's hand/public dual state.");

        var discardShownCard = discard.Choices.SingleOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("response") == "fire-attack-discard" &&
            choice.Cards.SequenceEqual([revealedCardId])) ??
            throw new InvalidOperationException(
                "The self-target may use the shown same-suit card as FireAttack's discard cost.");
        Require(game.Submit(new AnswerPromptCommand(
            0,
            discard.PromptId,
            discardShownCard.Id,
            game.Revision)).Accepted,
            "Discarding the shown FireAttack card was rejected.");
        for (var step = 0; step < 80 && game.ResolutionStack.Count > 0; step++)
        {
            Require(game.PendingDecision is null,
                $"Formal FireAttack unexpectedly paused at {game.PendingDecision?.Kind}.");
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "Formal FireAttack damage continuation was rejected.");
        }

        var resolved = game.Events.Select(item => item.Payload).OfType<FireAttackResolvedEvent>().Single(item =>
            item.SourceSeat == 0 && item.TargetSeat == 0 && item.RevealedCardId == revealedCardId);
        Require(resolved.CausedDamage && resolved.MatchingDiscardCardId == revealedCardId,
            "Formal self-target FireAttack did not record the shown card as its matching discard.");
        var applied = game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().Single(item =>
            item.SourceSeat == 0 && item.TargetSeat == 0 && item.Nature == DamageNature.Fire);
        Require(applied.Amount == 1 && applied.RemainingHp == hpBefore - 1,
            "Formal self-target FireAttack did not apply one point of fire damage.");
        Require(!game.CardMovements.Any(movement =>
                movement.CardId == revealedCardId &&
                movement.Reason == CardMoveReasons.FireAttackReveal),
            "Formal FireAttack must not move a card merely because it was shown.");
        Require(game.CardMovements.Any(movement =>
                movement.CardId == revealedCardId &&
                movement.From == CardLocation.Hand(0) &&
                movement.To == CardLocation.Processing &&
                movement.Reason == CardMoveReasons.FireAttackDiscard),
            "The shown card did not pay the formal matching-suit discard cost.");
        Require(game.State.ProcessingCardCount == 0 && game.ResolutionStack.Count == 0,
            $"Formal FireAttack left processing={game.State.ProcessingCardCount}, " +
            $"stack=[{string.Join(',', game.ResolutionStack.Select(frame => frame.Kind))}], " +
            $"prompt={game.PendingDecision?.Kind}.");
        Require(game.CreateCardZoneDiagnostics().Count == 90,
            "Formal FireAttack lost a physical card.");
        return game;
    }

    private static CommandResult Play(GameEngine game, LegalAction action) => game.Submit(new PlayCardCommand(
        ActorSeat: 0,
        CardId: action.CardId!.Value,
        TargetSeats: action.TargetSeats,
        ExpectedRevision: game.Revision,
        PromptId: game.PendingDecision!.PromptId));

    private static void AdvanceUntil(GameEngine game, DecisionKind kind)
    {
        for (var step = 0; step < 80 && game.PendingDecision?.Kind != kind; step++)
        {
            if (game.PendingDecision is { } prompt)
            {
                Require(prompt.Kind == DecisionKind.Nullification && prompt.PlayerSeat == 0,
                    $"Unexpected FireAttack prompt {prompt.Kind}.");
                var pass = prompt.Choices.Single(choice =>
                    choice.Parameters.GetValueOrDefault("response") == "pass");
                Require(game.Submit(new AnswerPromptCommand(
                    0,
                    prompt.PromptId,
                    pass.Id,
                    game.Revision)).Accepted,
                    "FireAttack Nullification pass was rejected.");
            }
            else
            {
                Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                    "FireAttack continuation step was rejected.");
            }
        }

        Require(game.PendingDecision is { PlayerSeat: 0 } finalPrompt && finalPrompt.Kind == kind,
            $"FireAttack did not reach the expected {kind} prompt.");
    }

    private static GameEngine CreateStartedGame(int seed, ContentRegistry registry, int rulesVersion)
    {
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2
            },
            registry);
        Require(rulesVersion == GameCheckpoint.CurrentRulesVersion,
            "FireAttack fixtures must use the current development rules version.");
        Require(game.Submit(new StartGameCommand()).Accepted, "FireAttack fixture failed to start.");
        return game;
    }

    private static void AssertReplay(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(
            SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
            SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            $"Rules v{game.RulesVersion} FireAttack checkpoint did not replay exactly.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
