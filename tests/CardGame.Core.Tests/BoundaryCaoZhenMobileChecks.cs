using System.Text.Json;
using CardGame.Core;
using static BoundaryCaoZhenScenario;

internal static class BoundaryCaoZhenMobileChecks
{
    public static void SecretForecastDelayedExclusionAndFirstFailureCold()
    {
        var scene = CreateScene(); var game = scene.Game;
        var cursor = game.Events.Count;
        BeginForecast(game); Answer(game, choice => choice.Targets.SequenceEqual([1]));
        Cold(ref game, scene.Registry);
        Answer(game, choice => choice.Targets.SequenceEqual([2])); Play(game, 0);
        Need(game.CreateSnapshot(0).BoundarySidiPredictions is [{ HolderSeat: 1, TargetSeat: 2 }] &&
            Enumerable.Range(1, 3).All(viewer => game.CreateSnapshot(viewer).BoundarySidiPredictions is null),
            "Only the actual issuer sees its holder and secret predicted target after the original own Use completed.");
        var published = JsonSerializer.Serialize(game.Events.Skip(cursor).Select(envelope => envelope.Payload));
        Need(!published.Contains("\"TargetSeat\":2", StringComparison.Ordinal) && !published.Contains("\"PredictedSeat\":2", StringComparison.Ordinal),
            "The secret target is absent from published event payloads as well as foreign player views.");
        Cold(ref game, scene.Registry);
        EndPlay(game, 0); Play(game, 1);
        UseCard(game, 1, CardKind.Indulgence, [3]); Play(game, 1);
        Need(game.CreateSnapshot(0).BoundarySidiPredictions is [{ HolderSeat: 1, TargetSeat: 2 }],
            "An actual delayed trick does not consume the first non-delayed-use prediction.");
        var hand = game.CreateSnapshot(0).Players[0].HandCount;
        UseCard(game, 1, CardKind.DrawTwo, []); Play(game, 1);
        Need(game.CreateSnapshot(0).BoundarySidiPredictions is null && game.CreateSnapshot(0).Players[0].HandCount == hand &&
            game.Events.Select(envelope => envelope.Payload).OfType<BoundarySidiResultEvent>().Last().Outcome == "none",
            "The first non-delayed Use consumes the failed unique-target prediction without rewarding a later matching card.");
        Cold(ref game, scene.Registry);
    }

    public static void SelfAndOtherDrawRewardsLeaveTheOriginalUseLive()
    {
        foreach (var target in new[] { 0, 2 })
        {
            var scene = CreateScene(); var game = scene.Game;
            Forecast(game, target); EndPlay(game, 0); Play(game, 1);
            var before = game.CreateSnapshot(0).Players[0].HandCount;
            UseCard(game, 1, CardKind.Slash, [target]);
            if (target != 0)
            { Reach(game, prompt => Benefit(prompt)); Answer(game, choice => choice.Parameters.GetValueOrDefault("branch") == "draw"); }
            Reach(game, prompt => prompt is { Kind: DecisionKind.RespondDodge } && prompt.PlayerSeat == target);
            Need(game.CreateSnapshot(0).Players[0].HandCount == before + (target == 0 ? 1 : 2) &&
                game.CreateSnapshot(0).BoundarySidiPredictions is null &&
                !game.Events.Select(envelope => envelope.Payload).OfType<BoundarySidiCancelledEvent>().Any(),
                "The self prediction draws one and the other-target draw choice draws two while the original Slash still requires its native Dodge.");
            Cold(ref game, scene.Registry);
        }
    }

    public static void CancelOriginalUseThenDamageActualUserCold()
    {
        var scene = CreateScene(); var game = scene.Game;
        Forecast(game, 2); EndPlay(game, 0); Play(game, 1);
        var actorHp = game.CreateSnapshot(0).Players[1].Hp; var targetHp = game.CreateSnapshot(0).Players[2].Hp;
        var card = game.CreateSnapshot(1).Players[1].Hand.First(card => card.Kind == CardKind.Slash).Id;
        UseCard(game, 1, CardKind.Slash, [2]); Reach(game, Benefit);
        Cold(ref game, scene.Registry);
        Answer(game, choice => choice.Parameters.GetValueOrDefault("branch") == "cancel"); Play(game, 1);
        Need(game.CreateSnapshot(0).Players[1].Hp == actorHp - 1 && game.CreateSnapshot(0).Players[2].Hp == targetHp &&
            game.CardMovements.Count(move => move.CardId == card && move.To == CardLocation.DiscardPile) == 1 &&
            game.Events.Select(envelope => envelope.Payload).OfType<BoundarySidiCancelledEvent>().Count() == 1 &&
            game.Events.Select(envelope => envelope.Payload).OfType<DamageAppliedEvent>().Any(damage => damage.SourceSeat == 0 && damage.TargetSeat == 1 && damage.Amount == 1),
            "Cancelling the exact original paid Slash preserves its material settlement and causes one native damage to its actual user without harming its old target.");
        Cold(ref game, scene.Registry);

        foreach (var kind in new[] { CardKind.DrawTwo, CardKind.Crossbow })
        {
            scene = CreateScene(); game = scene.Game;
            Forecast(game, 1); EndPlay(game, 0); Play(game, 1);
            var originalHand = game.CreateSnapshot(1).Players[1].HandCount;
            actorHp = game.CreateSnapshot(0).Players[1].Hp;
            card = game.CreateSnapshot(1).Players[1].Hand.First(card => card.Kind == kind).Id;
            UseCard(game, 1, kind, []); Reach(game, Benefit);
            Cold(ref game, scene.Registry);
            Answer(game, choice => choice.Parameters.GetValueOrDefault("branch") == "cancel"); Play(game, 1);
            Need(game.CreateSnapshot(1).Players[1].HandCount == originalHand - 1 && game.CreateSnapshot(0).Players[1].Hp == actorHp - 1 &&
                game.CardMovements.Count(move => move.CardId == card && move.To == CardLocation.DiscardPile) == 1 &&
                game.Events.Select(envelope => envelope.Payload).OfType<BoundarySidiCancelledEvent>().Count() == 1,
                "A real self-targeted " + kind + " matches its forecast; cancellation settles the same material once without drawing two cards or installing the equipment.");
            Cold(ref game, scene.Registry);
        }
    }

    private static bool Benefit(PendingDecision? prompt) => prompt?.SkillPrompt?.SkillId == Skill && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("branch") == "cancel");
    private static void BeginForecast(GameEngine game)
    { Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"); Reach(game, prompt => prompt?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("branch") == "holder") == true); }
    private static void Forecast(GameEngine game, int target)
    { BeginForecast(game); Answer(game, choice => choice.Targets.SequenceEqual([1])); Answer(game, choice => choice.Targets.SequenceEqual([target])); Play(game, 0); }
    private static void Need(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private static void Cold(ref GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        for (var viewer = 0; viewer < 4; viewer++) Need(JsonSerializer.Serialize(game.CreateSnapshot(viewer)) == JsonSerializer.Serialize(restored.CreateSnapshot(viewer)),
            "The actual accepted-command prefix restores this Sidi selection, secret record or native use child for every viewer.");
        game = restored;
    }
}
