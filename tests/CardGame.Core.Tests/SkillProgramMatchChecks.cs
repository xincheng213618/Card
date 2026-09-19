using CardGame.Content.Standard;
using CardGame.Core;

internal static class SkillProgramMatchChecks
{
    public static void ComposedMatchesCompleteAndReplay()
    {
        var registry = ComposedSkillContentRegistry.CreateShowcase();
        var seenPrograms = new HashSet<string>(StringComparer.Ordinal);
        var convertedResponses = 0;
        for (var seed = 1; seed <= 12; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed, PlayerCount = 5, HumanSeat = -1, HumanRole = null,
                ModeId = "identity:composed-skills-5", MaxTurns = 80,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AiPolicyVersion = 3
            }, registry);
            Require(game.Submit(new StartGameCommand()).Accepted, "Composed AI match failed to start.");
            if (game.State.Status != EngineStatus.Completed)
                Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted, "Composed AI match failed to advance.");
            Require(game.State.Status == EngineStatus.Completed && game.ResolutionStack.Count == 0,
                $"Composed AI match {seed} left an unfinished resolution.");
            seenPrograms.UnionWith(game.Events.Select(envelope => envelope.Payload).OfType<ProgramSkillStartedEvent>()
                .Select(payload => payload.SkillId));
            var physicalKinds = game.Events.Select(envelope => envelope.Payload).OfType<CardMovedEvent>()
                .GroupBy(payload => payload.CardId).ToDictionary(group => group.Key, group => group.First().CardKind);
            convertedResponses += game.Events.Select(envelope => envelope.Payload).OfType<CardRespondedEvent>()
                .Count(payload => payload.EffectiveCardKind is { } effective && physicalKinds[payload.CardId] != effective);
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(
                GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Require(SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) ==
                    SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)),
                $"Composed AI match {seed} changed when replayed.");
        }
        Require(seenPrograms.Count >= 2, "The match sample did not actually exercise multiple configured active skills.");
        Require(convertedResponses > 0, "The match sample did not exercise configured response conversion.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
