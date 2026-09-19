using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhangbaChecks
{
    public static void ActiveUseAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 63, 0));
        var boundary = ZhangbaScenario.FindHumanActiveUse();
        var game = boundary.Game;
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Zhangba fixture lost its play prompt.");
        var source = game.CreateSnapshot(0, revealAll: true).Players[0];

        Require(boundary.Action.MinCardCount == 2 && boundary.Action.MaxCardCount == 2 &&
                boundary.Action.MinTargetCount == 1 && boundary.Action.MaxTargetCount == 1 &&
                boundary.Action.SelectableCardIds.Order().SequenceEqual(source.Hand.Select(card => card.Id).Order()) &&
                !boundary.Action.SelectableCardIds.Contains(boundary.WeaponCardId),
            "Zhangba must publish an exact two-hand-card and one-target active conversion contract.");

        var invalid = game.Submit(new UseEquipmentEffectCommand(
            0,
            CardKind.ZhangbaSerpentSpear,
            [boundary.CostCardIds[0], boundary.WeaponCardId],
            [boundary.TargetSeat],
            game.Revision,
            prompt.PromptId));
        Require(!invalid.Accepted && invalid.Error?.Code == CommandErrorCode.InvalidCard,
            "Zhangba must reject its equipped weapon as one of the two hand-card costs.");

        var targetHp = game.CreateSnapshot(0, revealAll: true).Players
            .Single(player => player.Seat == boundary.TargetSeat).Hp;
        var used = game.Submit(new UseEquipmentEffectCommand(
            0,
            CardKind.ZhangbaSerpentSpear,
            boundary.CostCardIds,
            [boundary.TargetSeat],
            game.Revision,
            prompt.PromptId));
        Require(used.Accepted, used.Error?.Message ?? "The exact Zhangba conversion was rejected.");

        for (var step = 0; step < 32 &&
                           game.PendingDecision?.Kind != DecisionKind.PlayCard &&
                           game.State.Status != EngineStatus.Completed; step++)
        {
            var result = game.PendingDecision is { PlayerSeat: 0 } decision &&
                         decision.Kind != DecisionKind.PlayCard && decision.Choices.Count > 0
                ? game.Submit(new AnswerPromptCommand(0, decision.PromptId, decision.Choices[0].Id, game.Revision))
                : game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(result.Accepted, "Zhangba Slash could not finish its response and damage chain.");
        }

        var converted = game.Events.Select(item => item.Payload)
            .OfType<ZhangbaSerpentSpearConvertedEvent>()
            .Single(item => item.IsUse && item.UserSeat == 0);
        var full = game.CreateSnapshot(0, revealAll: true);
        var targetAfter = full.Players.Single(player => player.Seat == boundary.TargetSeat);
        Require(converted.PhysicalCardIds.SequenceEqual(boundary.CostCardIds) &&
                boundary.CostCardIds.All(cardId => game.CardMovements.Any(move =>
                    move.CardId == cardId && move.From == CardLocation.Hand(0) &&
                    move.To == CardLocation.Processing && move.Reason == CardMoveReasons.Use)) &&
                boundary.CostCardIds.All(cardId => game.CardMovements.Any(move =>
                    move.CardId == cardId && move.To == CardLocation.DiscardPile &&
                    move.Reason == CardMoveReasons.UseFinished)) &&
                targetAfter.Hp is var hp && hp >= targetHp - 1,
            $"Zhangba must retain, move and finish both physical hand cards through one virtual Slash. " +
            $"Costs={string.Join(',', boundary.CostCardIds)}; " +
            $"moves={string.Join(" | ", game.CardMovements.Where(move => boundary.CostCardIds.Contains(move.CardId)).Select(move => $"{move.CardId}:{move.From}->{move.To}/{move.Reason.Value}"))}; " +
            $"converted={string.Join(',', converted.PhysicalCardIds)}; hp={targetAfter.Hp}/{targetHp}; " +
            $"pending={game.PendingDecision?.Kind}/{game.PendingDecision?.PlayerSeat}; status={game.State.Status}; " +
            $"stack={string.Join(" | ", game.ResolutionStack.Select(frame => $"{frame.Kind}:{frame.Step}"))}.");
        Require(game.AcceptedCommands.OfType<UseEquipmentEffectCommand>().Single().CardIds
                .SequenceEqual(boundary.CostCardIds),
            "The accepted journal must retain both exact Zhangba cost ids.");

        var replayed = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(State(replayed) == State(game) && Events(replayed).SequenceEqual(Events(game)),
            "A completed Zhangba active use must replay exactly.");

        var legacy = GameReplay.Restore(
            RoundTrip(boundary.BeforeUse) with { RulesVersion = 43 },
            registry);
        Require(legacy.GetHumanLegalActions().All(action =>
                    action.Kind != LegalActionKind.UseEquipmentEffect),
            "Rules v43 must not publish the Zhangba conversion action.");

        var previousPackage = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 24, 0));
        Require(!previousPackage.Cards.ContainsKey("classic:zhangba-serpent-spear") &&
                previousPackage.Decks["classic:standard-deck"].Cards.Sum(card => card.Count) == 93,
            "Classic package 1.24 must retain the pre-Zhangba 93-card content fingerprint.");
    }

    public static void SlashResponseAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 63, 0));
        var boundary = ZhangbaScenario.FindHumanResponse();
        var game = boundary.Game;
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Zhangba response fixture lost its prompt.");
        var handIds = game.CreateSnapshot(0, revealAll: true).Players[0].Hand
            .Select(card => card.Id)
            .Order()
            .ToArray();
        var pairChoices = prompt.Choices.Where(choice =>
            choice.Parameters.GetValueOrDefault("response") == "zhangba-slash").ToArray();
        Require(prompt.IsPrivate &&
                pairChoices.Length == handIds.Length * (handIds.Length - 1) / 2 &&
                pairChoices.All(choice =>
                    choice.Cards.Count == 2 &&
                    choice.Cards.Distinct().Count() == 2 &&
                    choice.Cards.All(handIds.Contains)) &&
                pairChoices.All(choice => !choice.Cards.Contains(boundary.WeaponCardId)),
            "A Slash response must publish every exact Zhangba hand pair without exposing or spending the weapon.");

        var paused = RoundTrip(game.CreateCheckpoint());
        var restored = GameReplay.Restore(paused, registry);
        Require(State(restored) == State(game) && Events(restored).SequenceEqual(Events(game)),
            "An in-flight private Zhangba response prompt must restore exactly.");

        var choice = pairChoices[0];
        var answered = game.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            choice.Id,
            game.Revision));
        Require(answered.Accepted, answered.Error?.Message ??
            "The exact Zhangba Slash response was rejected.");
        var converted = game.Events.Select(item => item.Payload)
            .OfType<ZhangbaSerpentSpearConvertedEvent>()
            .LastOrDefault();
        Require(converted is { IsUse: false } &&
                converted.PhysicalCardIds.SequenceEqual(choice.Cards) &&
                choice.Cards.All(cardId => game.CardMovements.Any(move =>
                    move.CardId == cardId && move.From == CardLocation.Hand(0) &&
                    move.To == CardLocation.Processing && move.Reason == CardMoveReasons.Respond)) &&
                choice.Cards.All(cardId => game.CardMovements.Any(move =>
                    move.CardId == cardId && move.To == CardLocation.DiscardPile &&
                    move.Reason == CardMoveReasons.ResponseFinished)),
            "Zhangba response must move and finish both exact physical hand cards.");

        var replayed = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(State(replayed) == State(game) && Events(replayed).SequenceEqual(Events(game)),
            "A completed Zhangba Slash response must replay exactly.");
    }

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static string State(GameEngine game) =>
        SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));

    private static IReadOnlyList<string> Events(GameEngine game) => game.Events
        .Select(item => $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
