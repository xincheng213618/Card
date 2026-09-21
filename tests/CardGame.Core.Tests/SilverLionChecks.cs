using CardGame.Core;

internal static class SilverLionChecks
{
    public static void DamageCapRemovalRecoveryAndLegacyBoundary()
    {
        var boundary = SilverLionScenario.FindAlcoholSlash();
        var game = boundary.Game;
        var hp = game.CreateSnapshot(boundary.SourceSeat, revealAll: true).Players[boundary.TargetSeat].Hp;
        var result = game.Submit(new PlayCardCommand(boundary.SourceSeat, boundary.Action.CardId!.Value, [],
            game.Revision, game.PendingDecision!.PromptId));
        Require(result.Accepted, result.Error?.Message ?? "Alcohol was rejected.");
        if (game.PendingDecision is null)
        {
            result = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(result.Accepted, result.Error?.Message ?? "Play did not resume after Alcohol.");
        }
        var slash = game.GetHumanLegalActions().SingleOrDefault(candidate =>
            candidate.CardId == boundary.SlashCardId && candidate.TargetSeat == boundary.TargetSeat);
        Require(slash is not null, "The prepared Silver Lion Slash disappeared after Alcohol resolved.");
        var ai = new SimpleAiBrain(boundary.SourceSeat, 53001, policyVersion: 2);
        var (_, thought) = ai.ChoosePlay(game.CreateSnapshot(boundary.SourceSeat), game.GetHumanLegalActions(), 1);
        var scoredSlash = thought.Candidates.Single(candidate =>
            candidate.Action.CardId == slash!.CardId && candidate.Action.TargetSeat == boundary.TargetSeat);
        Require(scoredSlash.Reason.Contains("白银狮子", StringComparison.Ordinal),
            "AI Slash scoring must account for the target's public Silver Lion damage cap.");
        result = game.Submit(new PlayCardCommand(boundary.SourceSeat, slash!.CardId!.Value, slash.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, slash.PlayedCardKind));
        Require(result.Accepted, result.Error?.Message ?? "Silver Lion Slash was rejected.");
        var capped = game.Events.Select(item => item.Payload).OfType<SilverLionDamageCappedEvent>()
            .LastOrDefault(item => item.TargetSeat == boundary.TargetSeat);
        var damage = game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
            .LastOrDefault(item => item.TargetSeat == boundary.TargetSeat);
        Require(capped is not null && damage is not null, "The prepared Silver Lion Slash did not publish capped damage.");
        Require(capped!.BaseAmount == 2 && capped.ModifiedAmount == 1 && damage!.Amount == 1 &&
                game.CreateSnapshot(boundary.SourceSeat, revealAll: true).Players[boundary.TargetSeat].Hp == hp - 1,
            "Silver Lion must cap an Alcohol-enhanced Slash from two damage to one.");
        var replayed = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), boundary.Registry);
        Require(SnapshotJson.Serialize(replayed.CreateSnapshot(boundary.SourceSeat, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(boundary.SourceSeat, revealAll: true)),
            "A completed Silver Lion damage cap must replay exactly.");

        VerifyRemovalRecovery();
    }

    private static void VerifyRemovalRecovery()
    {
        var boundary = SilverLionScenario.FindRemoval();
        var game = boundary.Game;
        var before = game.CreateSnapshot(boundary.SourceSeat, revealAll: true).Players[boundary.TargetSeat];
        var result = game.Submit(new PlayCardCommand(boundary.SourceSeat, boundary.Action.CardId!.Value,
            boundary.Action.TargetSeats, game.Revision, game.PendingDecision!.PromptId));
        Require(result.Accepted, result.Error?.Message ?? "Silver Lion Dismantlement was rejected.");
        if (game.PendingDecision is { Kind: DecisionKind.SelectTargetCard, PlayerSeat: var seat } prompt)
        {
            var armor = prompt.Choices.Single(choice => choice.Parameters.GetValueOrDefault("card-kind") == CardKind.SilverLion.ToString());
            result = game.Submit(new AnswerPromptCommand(seat, prompt.PromptId, armor.Id, game.Revision));
            Require(result.Accepted, result.Error?.Message ?? "Silver Lion target selection was rejected.");
        }
        var after = game.CreateSnapshot(boundary.SourceSeat, revealAll: true).Players[boundary.TargetSeat];
        Require(after.Hp == before.Hp + 1 &&
                after.Equipment.All(card => card.Kind != CardKind.SilverLion) &&
                game.Events.Select(item => item.Payload).OfType<SilverLionRemovedRecoveryEvent>()
                    .Any(item => item.PlayerSeat == boundary.TargetSeat && item.RecoveredAmount == 1),
            "Losing equipped Silver Lion while wounded must recover exactly one HP.");
    }

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
