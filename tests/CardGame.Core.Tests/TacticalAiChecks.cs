using CardGame.Content.Standard;
using CardGame.Core;

internal static class TacticalAiChecks
{
    private static ProgramAiEstimate EstimateDraw(GameSnapshot view, SkillProgramTrigger trigger, int normalDraw) =>
        ProgramCompositionAi.Estimate(trigger.Effects, DrawContext(view), publicContext:
            new ProgramAiPublicContext(
                view.Players.Where(player => player.IsAlive && player.FactionId is not null)
                    .Select(player => player.FactionId).Distinct(StringComparer.Ordinal).Count(),
                NormalDrawCount: normalDraw,
                ReplacesNormalDraw: trigger.DrawPhaseMode == SkillProgramDrawPhaseMode.Replacement,
                EligibleTargetCount: view.Players.Count(player => player.IsAlive && player.Seat != 0 && player.HandCount > 0)));

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
        foreach (var version in new[] { 0, 4, -1 })
        {
            try { GameEngine.CreateStandard(new GameOptions { AiPolicyVersion = version }, StandardContentRegistry.Create()); }
            catch (ArgumentOutOfRangeException) { continue; }
            throw new InvalidOperationException("Unsupported AI policy was accepted.");
        }
    }

    private static void Pick(GameSnapshot view, IReadOnlyList<LegalAction> actions, LegalActionKind expected) =>
        Require(new SimpleAiBrain(0, 271, 2).ChoosePlay(view, actions, 1).Action.Kind == expected, $"Expected {expected} for {string.Join(',', actions.Select(action => action.Kind))}.");

    private static PlayerSkillContext DrawContext(GameSnapshot view)
    {
        var self = view.Players.Single(player => player.Seat == 0);
        return new PlayerSkillContext(
            self.Seat, self.Hp, self.MaxHp, self.HandCount, TurnPhase.Draw, IsOwnTurn: true);
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
    private static LegalAction Action(LegalActionKind kind, int card, int? target = null) => new(kind, card, target, kind.ToString());
    private static LegalAction End() => new(LegalActionKind.EndPlay, null, null, "结束出牌");
    private sealed record PlayCase(string Name, GameSnapshot View, IReadOnlyList<LegalAction> Actions, LegalActionKind Expected);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
