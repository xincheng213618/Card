using System.Collections;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class GongsunZanChecks
{
    public static void YicongDistanceAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var game = FindFixture(registry);
        Require(game.GetSeatDistance(0, 2) == 2 &&
                game.GetCombatDistance(0, 2) == 1 &&
                game.GetCombatDistance(2, 0) == 2,
            "Healthy Yicong must reduce only Gongsun Zan's outgoing distance by one.");

        SetHp(game, 0, 2);
        Require(game.GetCombatDistance(0, 2) == 2 && game.GetCombatDistance(2, 0) == 3,
            "At two HP, Yicong must stop its outgoing reduction and increase incoming distance by one.");
        var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
        SetHp(restored, 0, 2);
        Require(restored.GetCombatDistance(0, 2) == 2 && restored.GetCombatDistance(2, 0) == 3,
            "The same restored Yicong state must deterministically reproduce its low-health distances.");
    }

    private static GameEngine FindFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 4096; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5,
                ModeId = "identity:classic-5", UseInteractiveSetup = true,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
            }, registry);
            if (game.Submit(new StartGameCommand()).Accepted &&
                game.PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } setup &&
                setup.ValidContentIds.Contains("classic:gongsun-zan") &&
                game.Submit(new SelectGeneralCommand(0, "classic:gongsun-zan", game.Revision, setup.PromptId)).Accepted)
                return game;
        }
        throw new InvalidOperationException("No bounded Gongsun Zan selection fixture was found.");
    }

    private static void SetHp(GameEngine game, int seat, int hp)
    {
        var players = ((IEnumerable)typeof(GameEngine)
            .GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(game)!).Cast<object>().ToArray();
        players[seat].GetType().GetProperty("Hp")!.SetValue(players[seat], hp);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
