using CardGame.Content.Standard;
using CardGame.Core;

internal static class YuJinChecks
{
    public static void YizhongBlackSlashAndReplay()
    {
        Require(GameEngine.IsBlackCardUse([new Card(1, CardKind.Slash, Suit.Spade, 7)]) &&
                GameEngine.IsBlackCardUse([
                    new Card(2, CardKind.Dodge, Suit.Spade, 2),
                    new Card(3, CardKind.Peach, Suit.Club, 3)]) &&
                !GameEngine.IsBlackCardUse([
                    new Card(4, CardKind.Dodge, Suit.Spade, 2),
                    new Card(5, CardKind.Peach, Suit.Heart, 3)]),
            "A single black Slash and two-black Zhangba conversion must be black; mixed colors must be colorless.");
        Require(GameEngine.CanYizhongNullify([new Card(6, CardKind.Slash, Suit.Club, 8)], hasArmor: false) &&
                !GameEngine.CanYizhongNullify([new Card(7, CardKind.Slash, Suit.Club, 8)], hasArmor: true),
            "Yizhong must stop applying while any armor remains equipped, even if that armor is ignored.");

        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        for (var seed = 1; seed <= 4096; seed++)
        {
            var game = Create(seed, registry);
            if (!SelectYuJin(game)) continue;
            for (var step = 0; step < 1000 && game.State.Winner == Winner.None; step++)
            {
                var before = game.CreateCheckpoint();
                var beforeHp = game.CreateSnapshot(0, revealAll: true).Players[0].Hp;
                var eventCount = game.Events.Count;
                if (!AdvanceWithoutArmor(game)) break;
                var yizhong = game.Events.Skip(eventCount).Select(e => e.Payload)
                    .OfType<YizhongNullifiedEvent>().SingleOrDefault();
                if (yizhong is null) continue;

                Require(yizhong.TargetSeat == 0 &&
                        game.CreateSnapshot(0, revealAll: true).Players[0].Hp == beforeHp &&
                        game.Events.Skip(eventCount).Select(e => e.Payload).All(e =>
                            e is not ResponseRequestedEvent { TargetSeat: 0 }),
                    "Yizhong must nullify a black Slash before Yu Jin is asked for Dodge or takes damage.");

                var restored = GameReplay.Restore(before, registry);
                Require(restored.Submit(new AdvanceOneStepCommand(restored.Revision)).Accepted &&
                        SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                        SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
                    "A Yizhong boundary must replay deterministically from its preceding checkpoint.");

                return;
            }
        }
        throw new InvalidOperationException("No bounded Yu Jin fixture received a black Slash for Yizhong.");
    }

    private static GameEngine Create(int seed, ContentRegistry registry) => GameEngine.CreateStandard(new GameOptions
    {
        Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5,
        ModeId = "identity:classic-5", UseInteractiveSetup = true, UseInteractiveDiscard = true,
        AdvanceAfterHumanCommands = false, MaxTurns = 80
    }, registry);

    private static bool SelectYuJin(GameEngine game) =>
        game.Submit(new StartGameCommand()).Accepted &&
        game.PendingDecision is { Kind: DecisionKind.SelectGeneral } setup &&
        setup.ValidContentIds.Contains("classic:yu-jin") &&
        game.Submit(new SelectGeneralCommand(0, "classic:yu-jin", game.Revision, setup.PromptId)).Accepted;

    private static bool AdvanceWithoutArmor(GameEngine game)
    {
        if (game.PendingDecision is not { } pending || pending.PlayerSeat != 0)
            return game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted;
        if (pending.Kind == DecisionKind.PlayCard)
            return game.Submit(new EndPlayPhaseCommand(0, game.Revision, pending.PromptId)).Accepted;
        if (pending.Kind == DecisionKind.DiscardCards)
            return game.Submit(new DiscardCardsCommand(0, pending.ValidCardIds.Take(pending.RequiredCardCount).ToArray(),
                pending.PromptId, game.Revision)).Accepted;
        var choice = pending.Choices.FirstOrDefault(c =>
            c.Parameters.GetValueOrDefault("action")?.Contains("skip", StringComparison.Ordinal) == true) ??
            pending.Choices.LastOrDefault();
        return choice is not null &&
               game.Submit(new AnswerPromptCommand(0, pending.PromptId, choice.Id, game.Revision)).Accepted;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
