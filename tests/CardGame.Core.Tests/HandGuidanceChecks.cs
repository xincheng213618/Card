using CardGame.Content.Standard;
using CardGame.Core;

internal static class HandGuidanceChecks
{
    public static void ReadOnlyAndPrivate()
    {
        var seen = new HashSet<HandGuidanceReason>();
        foreach (var seed in Enumerable.Range(1, 48))
        {
            var game = GameEngine.CreateStandard(new GameOptions { Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, AiPolicyVersion = 2, AdvanceAfterHumanCommands = false }, StandardContentRegistry.Create());
            Require(game.Submit(new StartGameCommand()).Accepted, "Game did not start.");
            var state = SnapshotJson.Serialize(game.CreateSnapshot(0, true));
            var checkpoint = GameCheckpointJson.Serialize(game.CreateCheckpoint());
            var eventCount = game.Events.Count;
            var ownHand = game.State.Players.Single(player => player.IsHuman).Hand.Select(card => card.Id).Order().ToArray();
            var playable = game.GetHumanLegalActions().Where(action => action.CardId is not null).Select(action => action.CardId!.Value).ToHashSet();
            var hints = game.GetHumanHandGuidance();
            Require(hints.Select(hint => hint.CardId).Order().SequenceEqual(ownHand), "Guidance must contain exactly the local human's hand.");
            Require(hints.All(hint => hint.CanPlay == playable.Contains(hint.CardId)), "Guidance disagrees with published legal actions.");
            foreach (var hint in hints) { seen.Add(hint.Reason); Require(!string.IsNullOrWhiteSpace(hint.Message), "A hand card has no explanation."); }
            for (var read = 0; read < 10; read++) Require(game.GetHumanHandGuidance().SequenceEqual(hints), "Repeated reads changed guidance.");
            Require(game.Events.Count == eventCount && GameCheckpointJson.Serialize(game.CreateCheckpoint()) == checkpoint && SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == state, "A guidance query mutated the game.");
        }
        Require(new[] { HandGuidanceReason.Playable, HandGuidanceReason.ConversionOnly, HandGuidanceReason.HealthFull, HandGuidanceReason.ResponseOnly }.All(seen.Contains), "Fixtures missed common hand restrictions or skill conversions.");
        var aiOnly = GameEngine.CreateStandard(new GameOptions { HumanSeat = -1, HumanRole = null }, StandardContentRegistry.Create());
        Require(aiOnly.GetHumanHandGuidance().Count == 0, "Human guidance must not expose an AI hand.");
    }



    private static void Play(GameEngine game, LegalAction action) => Require(game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision, game.PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId)).Accepted, "Legal card was rejected.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
