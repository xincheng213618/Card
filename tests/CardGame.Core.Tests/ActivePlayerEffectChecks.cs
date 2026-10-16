using System.Collections;
using System.Reflection;
using System.Text.Json;
using CardGame.Core;

internal static class ActivePlayerEffectChecks
{
    public static void LiveDurationOwnershipAndFreezing()
    {
        var (game, registry) = ActiveEffectScenario.Luoyi();
        var effect = Effect(game);
        Require(effect is { Expiry: ActivePlayerEffectExpiry.NextSourceTurnStart, RemainingUses: null, SourceSeatDisplayNumber: 1 } &&
            effect.Description.Contains("伤害+1") && Enumerable.Range(-1, 5).All(viewer =>
                game.CreateSnapshot(viewer).Players[0].ActiveEffects!.Single() == effect),
            "Real Luoyi must publish its next-turn expiry to every viewer, without a one-use counter.");
        var held = game.CreateSnapshot(0).Players[0].ActiveEffects!;
        Require(held is IList { IsReadOnly: true } && !JsonSerializer.Serialize(held).Contains("SkillInstanceId"),
            "Public effects must contain scalar display facts and a frozen list, without trusted binding identities.");
        try { ((IList)held)[0] = effect with { Description = "tampered" }; throw new InvalidOperationException("Effect mutation succeeded."); }
        catch (NotSupportedException) { }
        Replay();
        var slash = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash && action.TargetSeat == 1);
        ActiveEffectScenario.Accept(game, new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats,
            game.Revision, ActiveEffectScenario.Prompt(game)!.PromptId, slash.PlayedCardKind));
        ActiveEffectScenario.Reach(game, () => ActiveEffectScenario.Prompt(game) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        Require(Effect(game) == effect && game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.Amount == 2),
            "A real augmented Slash must retain Luoyi; this effect applies throughout its declared lifetime.");
        Replay();
        var turn = game.State.TurnNumber;
        ActiveEffectScenario.Accept(game, new EndPlayPhaseCommand(0, game.Revision, ActiveEffectScenario.Prompt(game)!.PromptId));
        ActiveEffectScenario.Reach(game, () => game.State.TurnNumber > turn && game.State.CurrentSeat == 0 &&
            game.CreateSnapshot(0).Players[0].ActiveEffects?.Any(item => item.SkillId == "boundary:luoyi") != true);
        Require(game.CreateSnapshot(0).Players[0].ActiveEffects is null && held.Single() == effect,
            "The next real owner turn must retire the old effect without rewriting previously exposed snapshots.");
        Replay();
        ProjectionOwnership(game);

        void Replay()
        {
            var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Require(Enumerable.Range(-1, 5).All(viewer => SnapshotJson.Serialize(game.CreateSnapshot(viewer)) == SnapshotJson.Serialize(cold.CreateSnapshot(viewer))),
                "Cold command replay must reproduce public active effects and each viewer's private boundary.");
        }
    }

    private static void ProjectionOwnership(GameEngine game)
    {
        // Isolated projection-host cases, not fabricated accepted-command gameplay.
        var store = (TurnCardUseEffectStore)typeof(GameEngine).GetField("_turnCardUseEffects", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        var source = new CardUseEffectSource("boundary:luoyi", "private-binding", 0, "private-instance");
        var turn = game.State.TurnNumber;
        var current = game.State.CurrentSeat;
        ((List<TurnHandCardColorRestriction>)store.HandColorRestrictions).Add(new(901, turn, current, 901, 0, source, 1, true));
        ((List<TurnCardTargetRestriction>)store.TargetRestrictions).Add(new(902, turn, current, 902, 0, source,
            SkillProgramCardTargetRestriction.ArmorIneffectiveForTurn, 1));
        ((List<TurnRuleModifier>)store.RuleModifiers).Add(new(903, turn - 1, current, 903, 0, source, SkillRuleQuery.HandLimit, SkillRuleOperation.Add, 9));
        var view = game.CreateSnapshot(0);
        Require(view.Players[1].ActiveEffects is { Count: 2 } affected &&
            affected.Any(item => item.Description.Contains("红色手牌")) && affected.Any(item => item.Description == "防具失效。") &&
            view.Players[0].ActiveEffects is null &&
            !JsonSerializer.Serialize(view.Players.Select(player => player.ActiveEffects)).Contains("private-"),
            "Color restrictions and armor loss belong to the affected recipient; stale modifiers and private bindings must not enter public effect facts.");
        var damageLimitType = typeof(GameEngine).GetNestedType("ProgramPlayDamageHandLimit", BindingFlags.NonPublic)!;
        var limits = (IDictionary)typeof(GameEngine).GetField("_programPlayDamageHandLimits", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        limits[0] = Activator.CreateInstance(damageLimitType, [0, "boundary:luoyi", "private-instance", turn, 1, 3]);
        Require(game.CreateSnapshot(0).Players[0].ActiveEffects?.Single().Description.Contains("伤害点数计算：3") == true,
            "A damage-derived hand-limit effect must show its live accumulated value without exposing its bookkeeping identity.");
    }

    private static ActivePlayerEffectSnapshot Effect(GameEngine game) => game.CreateSnapshot(0).Players[0].ActiveEffects!.Single();
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
