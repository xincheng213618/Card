using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class GroupClaimChecks
{
    public static void ClaimantDeathContinues()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("group-claim-before-death.json")))!;
        using var reader = new StreamReader(stream);
        var registry = StandardContentRegistry.CreateWithTeamModes();
        var game = GameReplay.Restore(GameCheckpointJson.Deserialize(reader.ReadToEnd()), registry);
        var claim = game.Events.Select(e => e.Payload).OfType<DamageCardClaimedEvent>().Last();
        var use = game.Events.Select(e => e.Payload).OfType<CardUseDeclaredEvent>().Last();
        Require(claim.CardId == use.CardId && claim.CardKind == CardKind.ArrowBarrage &&
            game.State.Players.Single(player => player.Seat == claim.OwnerSeat) is { IsAlive: true, Hp: 0 },
            "Fixture must suspend a real claimed group card before its owner dies.");
        var start = game.Events.Count;
        for (var step = 0; step < 100 && !game.Events.Skip(start).Any(e =>
            e.Payload is CardUseFinishedEvent finished && finished.ResolutionId == use.ResolutionId); step++)
        {
            GameCommand command = game.PendingDecision is { } prompt
                ? new AnswerPromptCommand(0, prompt.PromptId, (prompt.Choices.FirstOrDefault(choice => choice.Cards.Count == 0) ?? prompt.Choices[0]).Id, game.Revision)
                : new AdvanceOneStepCommand(game.Revision);
            Require(game.Submit(command).Accepted, "Claimed group effect could not continue after the claimant's death.");
        }
        Require(!game.State.Players.Single(player => player.Seat == claim.OwnerSeat).IsAlive, "Claimant did not die.");
        var events = game.Events.Skip(start).Select(e => e.Payload).ToArray();
        Require(events.OfType<CardMovedEvent>().Any(e => e.CardId == claim.CardId &&
            e.From == CardLocation.Hand(claim.OwnerSeat) && e.To == CardLocation.DiscardPile), "Death did not clean up the claimed physical card.");
        Require(events.OfType<GroupResponseEvent>().Any(e => e.ResolutionId == use.ResolutionId && e.ResponderSeat == 7),
            "The surviving next target never responded to the existing group effect.");
        Require(events.OfType<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.ResolutionId) == 1 &&
            game.ResolutionStack.Count == 0 && game.State.ProcessingCardCount == 0, "Group resolution did not finish exactly once.");
        var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, true)) == SnapshotJson.Serialize(game.CreateSnapshot(0, true)),
            "The corrected group continuation did not replay deterministically.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
