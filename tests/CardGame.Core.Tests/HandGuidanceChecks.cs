using CardGame.Content.Standard;
using CardGame.Core;
using System.Text.Json;

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
            var actions = game.GetHumanLegalActions();
            var playable = actions.Where(action => action.CardId is not null).Select(action => action.CardId!.Value).ToHashSet();
            var hints = game.GetHumanHandGuidance();
            var view = game.GetHumanActionView();
            Require(view.Revision == game.Revision && view.HandGuidance.SequenceEqual(hints) &&
                    JsonSerializer.Serialize(view.LegalActions) == JsonSerializer.Serialize(actions),
                "The shared action view changed the independent legal actions or hand explanations.");
            ReadOnly(view.LegalActions);
            ReadOnly(view.HandGuidance);
            foreach (var action in view.LegalActions)
            {
                ReadOnly(action.TargetSeats);
                ReadOnly(action.SelectableCardIds);
                ReadOnly(action.SelectableTargetSeats);
                if (action.AdditionalConversionSources is { } sources) ReadOnly(sources);
            }
            Require(hints.Select(hint => hint.CardId).Order().SequenceEqual(ownHand), "Guidance must contain exactly the local human's hand.");
            Require(hints.All(hint => hint.CanPlay == playable.Contains(hint.CardId)), "Guidance disagrees with published legal actions.");
            foreach (var hint in hints) { seen.Add(hint.Reason); Require(!string.IsNullOrWhiteSpace(hint.Message), "A hand card has no explanation."); }
            for (var read = 0; read < 10; read++) Require(game.GetHumanActionView().HandGuidance.SequenceEqual(hints), "Repeated reads changed guidance.");
            Require(game.Events.Count == eventCount && GameCheckpointJson.Serialize(game.CreateCheckpoint()) == checkpoint && SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == state, "A guidance query mutated the game.");
        }
        Require(new[] { HandGuidanceReason.Playable, HandGuidanceReason.ConversionOnly, HandGuidanceReason.HealthFull, HandGuidanceReason.ResponseOnly }.All(seen.Contains), "Fixtures missed common hand restrictions or skill conversions.");
        var aiOnly = GameEngine.CreateStandard(new GameOptions { HumanSeat = -1, HumanRole = null }, StandardContentRegistry.Create());
        Require(aiOnly.GetHumanHandGuidance().Count == 0, "Human guidance must not expose an AI hand.");
        Require(aiOnly.GetHumanActionView() is { LegalActions.Count: 0, HandGuidance.Count: 0 },
            "The combined view must not expose AI choices or hands.");

        var targets = new List<int> { 1, 2 };
        var cards = new List<int> { 3, 4 };
        var conversions = new List<CardConversionSource> { new("skill", "binding", 0, "instance") };
        var draft = new LegalAction(LegalActionKind.UseProgramSkill, null, null, "draft")
        {
            TargetSeats = targets, SelectableTargetSeats = targets, SelectableCardIds = cards,
            AdditionalConversionSources = conversions
        };
        var frozen = new HumanActionView(0, [draft], []);
        targets.Clear(); cards.Clear(); conversions.Clear();
        var saved = frozen.LegalActions.Single();
        Require(saved.TargetSeats.SequenceEqual([1, 2]) && saved.SelectableTargetSeats.SequenceEqual([1, 2]) &&
                saved.SelectableCardIds.SequenceEqual([3, 4]) && saved.AdditionalConversionSources?.Count == 1,
            "An action view retained a caller-owned nested list.");
        ReadOnly(saved.TargetSeats);
        ReadOnly(saved.SelectableTargetSeats);
        ReadOnly(saved.SelectableCardIds);
        ReadOnly(saved.AdditionalConversionSources!);
    }

    private static void ReadOnly<T>(IReadOnlyList<T> values)
    {
        try
        {
            var list = (IList<T>)values;
            if (list.Count > 0) list[0] = values[0];
            else list.Add(default!);
        }
        catch (NotSupportedException) { return; }
        throw new InvalidOperationException("An action view collection accepted an observer mutation.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
