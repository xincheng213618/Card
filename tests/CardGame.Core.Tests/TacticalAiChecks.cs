using CardGame.Content.Standard;
using CardGame.Core;

internal static class TacticalAiChecks
{
    public static void SupportDrawAllowsPublishedEmptyTargetSets()
    {
        var view = View(Role.Rebel) with { ModeKind = ContentModeKind.Team };
        view = view with { Players = view.Players.Select(player => player with
            { TeamId = player.Seat is 0 or 2 ? "ally" : "enemy" }).ToArray() };
        var brain = new SimpleAiBrain(0, 37);
        PromptChoice Choice(string id, params int[] targets) =>
            new(new(id), id, [], targets, new Dictionary<string, string> { ["program-action"] = "select-targets" });
        var empty = Choice("empty");
        var enemy = Choice("enemy", 1);
        var self = Choice("self", 0);
        var ally = Choice("ally", 2);
        var both = Choice("both", 0, 2);
        var choices = new[] { empty, enemy, self, ally, both };
        var before = SnapshotJson.Serialize(view);
        var (selected, thought) = brain.ChooseSupportDrawTargets(view, choices, 1);
        Require(selected == both.Id && thought.Candidates.Count == choices.Length &&
            thought.Candidates.Single(c => c.Action.TargetSeats.Count == 0) is
                { Score: 0, Action.TargetSeat: null },
            "A published empty draw subset has zero utility and no invented target; actual allies still win.");
        Require(brain.ChooseSupportDrawTargets(view, [enemy, empty], 2).Choice == empty.Id &&
            brain.ChooseSupportDrawTargets(view, [empty], 3).Choice == empty.Id &&
            brain.ChooseSupportDrawTargets(view, [enemy, self], 4).Choice == self.Id,
            "AI may decline every enemy beneficiary or accept a sole empty choice, while mandatory targets keep their ranking.");
        Require(before == SnapshotJson.Serialize(view) && choices.Select(c => c.Id)
            .SequenceEqual(new[] { empty.Id, enemy.Id, self.Id, ally.Id, both.Id }),
            "Support scoring preserves the player view and the exact published choices.");
        try { brain.ChooseSupportDrawTargets(view, [], 5); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("A prompt with no published choices must remain invalid.");
    }







    public static void PolicyReplay()
    {
        foreach (var version in new[] { 1, 2, 3 })
        {
            var game = GameEngine.CreateStandard(new GameOptions { Seed = 561923, HumanSeat = -1, HumanRole = null, AiPolicyVersion = version, UseInteractiveSetup = true }, StandardContentRegistry.Create());
            Require(game.Submit(new StartGameCommand()).Accepted, "Full AI match failed.");
            Require(game.State.Status == EngineStatus.Completed, "Full AI match did not complete.");
            var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
            Require(checkpoint.Options.AiPolicyVersion == version, "Checkpoint lost its decision policy.");
            var restored = GameReplay.Restore(checkpoint, StandardContentRegistry.Create());
            Require(string.Join("\n", restored.Events.Select(item => System.Text.Json.JsonSerializer.Serialize(item))) == string.Join("\n", game.Events.Select(item => System.Text.Json.JsonSerializer.Serialize(item))) && SnapshotJson.Serialize(restored.State) == SnapshotJson.Serialize(game.State), "Policy replay changed a match.");
        }
        foreach (var version in new[] { 0, AiDecisionPolicy.CurrentVersion + 1, -1 })
        {
            try { GameEngine.CreateStandard(new GameOptions { AiPolicyVersion = version }, StandardContentRegistry.Create()); }
            catch (ArgumentOutOfRangeException) { continue; }
            throw new InvalidOperationException("Unsupported AI policy was accepted.");
        }
    }







    private static GameSnapshot View(Role role, params CardKind[] cards)
    {
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 721019, HumanSeat = 0, HumanRole = role }, StandardContentRegistry.Create());
        var view = game.CreateSnapshot(0) with { Status = EngineStatus.AwaitingHumanPlay, Phase = TurnPhase.Play, CurrentSeat = 0, TurnNumber = 1 };
        return Change(view, 0, player => player with
        {
            Hand = cards.Select((kind, index) => new CardSnapshot(1000 + index, kind, Suit.Heart, 7, CardCatalog.Get(kind).DisplayName, "7")).ToArray(),
            HandCount = cards.Length
        });
    }


    private static GameSnapshot Change(GameSnapshot view, int seat, Func<PlayerSnapshot, PlayerSnapshot> change) =>
        view with { Players = view.Players.Select(player => player.Seat == seat ? change(player) : player).ToArray() };
    private sealed record PlayCase(string Name, GameSnapshot View, IReadOnlyList<LegalAction> Actions, LegalActionKind Expected);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
