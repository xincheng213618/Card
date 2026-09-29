using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhangbaChecks
{
    public static void ActiveUseAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
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
