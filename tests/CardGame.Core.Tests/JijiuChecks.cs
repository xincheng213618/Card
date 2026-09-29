using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class JijiuChecks
{
    public static void EquipmentFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var game = JijiuEquipmentScenario.Find();
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("The classic Jijiu equipment fixture lost its dying prompt.");
        var owner = game.CreateSnapshot(0, revealAll: true).Players[0];
        var choice = prompt.Choices.Single(candidate =>
            candidate.Parameters.GetValueOrDefault("response") == "peach" &&
            candidate.Cards.Count == 1 &&
            owner.Equipment.Any(card => card.Id == candidate.Cards[0]));
        var equipment = owner.Equipment.Single(card => card.Id == choice.Cards[0]);

        Require(owner.GeneralId == "classic:hua-tuo" &&
                owner.Skills?.Any(skill => skill.ContentId == "classic:jijiu") == true &&
                equipment.Suit is Suit.Heart or Suit.Diamond &&
                game.State.CurrentSeat != 0 &&
                choice.Description.Contains("当作【桃】", StringComparison.Ordinal),
            "Classic Hua Tuo must publish a red equipped card as Peach only outside his turn.");
        Require(game.CreateSnapshot(1).PendingDecision is null,
            "The equipped Jijiu candidate must remain private to Hua Tuo.");

        var paused = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(paused.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                paused.Events.Select(EventSignature).SequenceEqual(game.Events.Select(EventSignature)),
            "An in-flight equipped Jijiu rescue must replay exactly.");

        var dyingFrame = game.ResolutionStack.OfType<DyingFrame>().Single();
        var accepted = game.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            choice.Id,
            game.Revision));
        Require(accepted.Accepted, accepted.Error?.Message ??
            "Classic Hua Tuo could not use red equipment through Jijiu.");
        Require(game.Events.Select(item => item.Payload).OfType<DyingResponseEvent>().Any(response =>
                    response.ResolutionId == dyingFrame.Id &&
                    response.ResponderSeat == 0 &&
                    response.UsedPeach &&
                    response.PeachCardId == equipment.Id &&
                    response.UsedPeachPhysicalCardKind == equipment.Kind) &&
                game.Events.Select(item => item.Payload).OfType<CardUseDeclaredEvent>().Any(declared =>
                    declared.CardId == equipment.Id && declared.CardKind == CardKind.Peach),
            "Equipped Jijiu must publish Peach as the effective card while retaining the physical kind.");
        Require(game.CardMovements.Any(movement =>
                    movement.CardId == equipment.Id &&
                    movement.CardKind == equipment.Kind &&
                    movement.From == CardLocation.Equipment(0) &&
                    movement.To == CardLocation.Processing &&
                    movement.Reason == CardMoveReasons.Use) &&
                game.CardMovements.Any(movement =>
                    movement.CardId == equipment.Id &&
                    movement.From == CardLocation.Processing &&
                    movement.To == CardLocation.DiscardPile &&
                    movement.Reason == CardMoveReasons.UseFinished),
            "Equipped Jijiu must pay the physical card through Equipment -> Processing -> DiscardPile.");

        var completed = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(completed.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                completed.Events.Select(EventSignature).SequenceEqual(game.Events.Select(EventSignature)),
            "A completed equipped Jijiu rescue must replay exactly.");
        AssertInventory(game);
        AssertInventory(completed);
    }

    private static (GameEngine Game, PendingDecision Prompt, Card ConvertedCard) FindConvertedPrompt(
        ContentRegistry registry)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = GameEngine.CreateStandard(
                new GameOptions
                {
                    Seed = seed,
                    PlayerCount = 5,
                    ModeId = "identity:active-skills-5",
                    HumanSeat = 0,
                    HumanRole = Role.Lord,
                    UseInteractiveDiscard = false,
                    MaxTurns = 180
                },
                registry);
            var result = game.Submit(new StartGameCommand());
            if (!result.Accepted)
            {
                throw new InvalidOperationException(result.Error?.Message ?? "The Jijiu fixture failed to start.");
            }

            for (var step = 0; result.Status != EngineStatus.Completed && step < 3_000; step++)
            {
                if (result.Status == EngineStatus.AwaitingHumanDying &&
                    game.PendingDecision is { } prompt &&
                    game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Skills?
                        .Any(skill => skill.ContentId == "standard:jijiu") == true)
                {
                    var hand = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Hand;
                    var converted = prompt.Choices
                        .Where(choice => choice.Parameters.GetValueOrDefault("response") == "peach")
                        .Select(choice => choice.Cards.SingleOrDefault(cardId =>
                            hand.Any(card => card.Id == cardId &&
                                card.Kind != CardKind.Peach &&
                                card.Suit is Suit.Heart or Suit.Diamond)))
                        .Where(cardId => cardId != 0)
                        .Select(cardId => hand.Single(card => card.Id == cardId))
                        .FirstOrDefault();
                    if (converted is not null)
                    {
                        return (game, prompt, new Card(
                            converted.Id,
                            converted.Kind,
                            converted.Suit,
                            converted.Rank));
                    }
                }

                result = Continue(game, result);
            }
        }

        throw new InvalidOperationException("No deterministic Jijiu dying prompt was found.");
    }

    private static CommandResult Continue(GameEngine game, CommandResult current)
    {
        if (current.Status == EngineStatus.AwaitingHumanPlay)
        {
            var prompt = game.PendingDecision ?? throw new InvalidOperationException("Missing play prompt.");
            return game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId));
        }

        if (current.Status == EngineStatus.AwaitingHumanResponse ||
            current.Status == EngineStatus.AwaitingHumanDying ||
            current.Status == EngineStatus.AwaitingHumanCardSelection)
        {
            var prompt = game.PendingDecision ?? throw new InvalidOperationException("Missing human prompt.");
            var choice = prompt.Choices.FirstOrDefault(item =>
                             item.Parameters.GetValueOrDefault("response") == "take-damage" ||
                             item.Parameters.GetValueOrDefault("response") == "let-die") ??
                         prompt.Choices.FirstOrDefault() ??
                         throw new InvalidOperationException("The human prompt has no choices.");
            return game.Submit(new AnswerPromptCommand(
                prompt.PlayerSeat,
                prompt.PromptId,
                choice.Id,
                game.Revision));
        }

        if (current.Status == EngineStatus.AwaitingHumanDiscard)
        {
            var prompt = game.PendingDecision ?? throw new InvalidOperationException("Missing discard prompt.");
            return game.Submit(new DiscardCardsCommand(
                prompt.PlayerSeat,
                prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                prompt.PromptId,
                game.Revision));
        }

        return game.Submit(new AdvanceOneStepCommand(game.Revision));
    }

    private static void AssertInventory(GameEngine game)
    {
        var diagnostics = game.CreateCardZoneDiagnostics();
        var snapshot = game.CreateSnapshot(0, revealAll: true);
        Require(diagnostics.Count == diagnostics.Select(card => card.CardId).Distinct().Count(),
            "The Jijiu fixture duplicated a physical card.");
        Require(snapshot.DrawPileCount == diagnostics.Count(card => card.Location == CardLocation.DrawPile) &&
                snapshot.DiscardPileCount == diagnostics.Count(card => card.Location == CardLocation.DiscardPile) &&
                snapshot.ProcessingCardCount == diagnostics.Count(card => card.Location == CardLocation.Processing),
            "The Jijiu fixture's shared zone counts diverged from its snapshot.");
        foreach (var player in snapshot.Players)
        {
            var hand = diagnostics
                .Where(card => card.Location == CardLocation.Hand(player.Seat))
                .Select(card => card.CardId)
                .OrderBy(cardId => cardId);
            Require(hand.SequenceEqual(player.Hand.Select(card => card.Id).OrderBy(cardId => cardId)),
                "The Jijiu fixture's hand projection diverged from its zone store.");
        }
    }

    private static string EventSignature(EventEnvelope eventItem) =>
        JsonSerializer.Serialize(new
        {
            eventItem.Id,
            eventItem.ParentId,
            eventItem.Sequence,
            eventItem.Revision,
            eventItem.CorrelationId,
            Payload = JsonSerializer.Serialize(eventItem.Payload, eventItem.Payload.GetType())
        });

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
