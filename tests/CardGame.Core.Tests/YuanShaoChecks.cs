using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class YuanShaoChecks
{
    public static void LuanjiAndXueyiReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var game = FindFixture(registry);
        var prompt = game.PendingDecision!;
        var owner = game.CreateSnapshot(0, revealAll: true).Players[0];
        var pair = owner.Hand.GroupBy(card => card.Suit).First(group => group.Count() >= 2).Take(2).ToArray();
        var differentSuitPair = owner.Hand
            .SelectMany((first, index) => owner.Hand.Skip(index + 1)
                .Where(second => second.Suit != first.Suit)
                .Select(second => new[] { first, second }))
            .First();
        var qunOthers = game.CreateSnapshot(0, revealAll: true).Players.Count(player =>
            player.Seat != 0 && player.IsAlive &&
            registry.Generals.TryGetValue(player.GeneralId, out var general) && general.FactionId == "qun");
        Require(ReadHandLimit(game, 0) == owner.Hp + qunOthers * 2,
            "Xueyi must add twice the number of other living Qun characters to the lord's hand limit.");

        var beforeInvalid = game.SerializeState();
        var acceptedCommandsBeforeInvalid = game.AcceptedCommands.Count;
        var eventsBeforeInvalid = game.Events.Count;
        var rejected = game.Submit(new UseSkillCommand(
            0, SkillKind.Luanji, differentSuitPair.Select(card => card.Id).ToArray(), [],
            game.Revision, prompt.PromptId));
        Require(!rejected.Accepted && rejected.Error is not null &&
                game.SerializeState() == beforeInvalid &&
                game.AcceptedCommands.Count == acceptedCommandsBeforeInvalid &&
                game.Events.Count == eventsBeforeInvalid,
            "An off-suit Luanji pair must be rejected atomically.");

        var used = game.Submit(new UseSkillCommand(
            0, SkillKind.Luanji, pair.Select(card => card.Id).ToArray(), [], game.Revision, prompt.PromptId));
        Require(used.Accepted, used.Error?.Message ?? "The legal Luanji pair was rejected.");
        DriveUntilLuanjiFinishes(game);

        var converted = game.Events.Select(item => item.Payload).OfType<LuanjiConvertedEvent>().Single();
        Require(converted.PhysicalCardIds.Order().SequenceEqual(pair.Select(card => card.Id).Order()) &&
                converted.Suit == pair[0].Suit &&
                pair.All(card => game.CreateCardZoneDiagnostics().Single(zone => zone.CardId == card.Id).Location.Zone !=
                    CardZoneKind.Processing) &&
                pair.All(card => game.CardMovements.Any(move => move.CardId == card.Id &&
                    move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing &&
                    move.Reason == CardMoveReasons.Use)) &&
                game.Events.Select(item => item.Payload).OfType<GroupCardUsedEvent>()
                    .Any(item => item.CardKind == CardKind.ArrowBarrage),
            "Luanji must consume the exact same-suit pair as one Arrow Barrage through the group response chain.");

        var replay = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                replay.CreateCardZoneDiagnostics().SequenceEqual(game.CreateCardZoneDiagnostics()),
            "A completed Luanji group attack must replay with exact physical-card zones.");
    }

    private static GameEngine FindFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 8_192; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5,
                ModeId = "identity:classic-5", UseInteractiveSetup = true,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 100
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted ||
                game.PendingDecision is not { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } setup ||
                !setup.ValidContentIds.Contains("classic:yuan-shao") ||
                !game.Submit(new SelectGeneralCommand(0, "classic:yuan-shao", game.Revision, setup.PromptId)).Accepted)
                continue;
            for (var step = 0; step < 64 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted) break;
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } &&
                game.GetHumanLegalActions().Any(action => action is { Kind: LegalActionKind.UseSkill, Skill: SkillKind.Luanji }) &&
                game.CreateSnapshot(0, revealAll: true).Players[0].Hand.Select(card => card.Suit).Distinct().Count() >= 2)
                return game;
        }
        throw new InvalidOperationException("No bounded Yuan Shao fixture had an opening same-suit Luanji pair.");
    }

    private static void DriveUntilLuanjiFinishes(GameEngine game)
    {
        for (var step = 0; step < 256 && game.ResolutionStack.Count > 0; step++)
        {
            GameCommand command;
            if (game.PendingDecision is { PlayerSeat: 0 } prompt)
            {
                var choice = prompt.Choices.FirstOrDefault(item => item.Cards.Count == 0) ?? prompt.Choices.First();
                command = new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision);
            }
            else
            {
                command = new AdvanceOneStepCommand(game.Revision);
            }
            var result = game.Submit(command);
            Require(result.Accepted, result.Error?.Message ?? "Could not finish the Luanji group response chain.");
        }
        Require(game.ResolutionStack.Count == 0, "Luanji left an unfinished resolution frame.");
    }

    private static int ReadHandLimit(GameEngine game, int seat)
    {
        var players = (System.Collections.IList)typeof(GameEngine)
            .GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!;
        return (int)typeof(GameEngine).GetMethod("GetHandLimit", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(game, [players[seat]])!;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
