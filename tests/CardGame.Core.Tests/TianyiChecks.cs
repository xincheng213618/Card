using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class TianyiChecks
{
    public static void WinLossSlashRulesAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 49, 0));
        var win = Find(registry, sourceWins: true, requireSlash: true);
        var legacy = GameReplay.Restore(win.BeforeUse with { RulesVersion = 63 }, registry);
        Require(legacy.GetHumanLegalActions().All(action => action.Skill != SkillKind.Tianyi),
            "Rules v63 must not expose Tianyi.");

        var winEvent = win.Game.Events.Select(item => item.Payload).OfType<PindianResolvedEvent>().Last();
        var actions = win.Game.GetHumanLegalActions();
        Require(winEvent is { Skill: SkillKind.Tianyi, InitiatorWon: true } &&
                actions.Any(action => action.Kind == LegalActionKind.Slash && action.TargetSeats.Count == 2) &&
                actions.Where(action => action.Kind == LegalActionKind.Slash).SelectMany(action => action.TargetSeats)
                    .Any(seat => win.Game.GetCombatDistance(0, seat) > win.Game.GetAttackRange(0)),
            "Winning Tianyi must publish two-target and unlimited-distance Slash actions.");

        var multi = actions.First(action => action.Kind == LegalActionKind.Slash && action.TargetSeats.Count == 2);
        var multiBranch = GameReplay.Restore(win.Game.CreateCheckpoint(), registry);
        var usedMulti = multiBranch.Submit(new PlayCardCommand(0, multi.CardId!.Value, multi.TargetSeats,
            multiBranch.Revision, multiBranch.PendingDecision!.PromptId, multi.PlayedCardKind));
        Require(usedMulti.Accepted && multiBranch.Events.Select(item => item.Payload)
                .OfType<TargetsConfirmedEvent>().Any(item => item.TargetSeats.SequenceEqual(multi.TargetSeats)),
            usedMulti.Error?.Message ?? "Winning Tianyi two-target Slash was rejected.");

        SetSlashCount(win.Game, 1);
        Require(win.Game.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.Slash),
            "Winning Tianyi must permit one additional Slash this turn.");
        var restored = GameReplay.Restore(win.Game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(win.Game.CreateSnapshot(0, revealAll: true)) &&
                restored.GetHumanLegalActions().Count(action => action.Kind == LegalActionKind.Slash) ==
                win.Game.GetHumanLegalActions().Count(action => action.Kind == LegalActionKind.Slash),
            "Winning Tianyi state and legal Slash actions must replay exactly.");

        var loss = Find(registry, sourceWins: false, requireSlash: false);
        var lossEvent = loss.Game.Events.Select(item => item.Payload).OfType<PindianResolvedEvent>().Last();
        Require(lossEvent is { Skill: SkillKind.Tianyi, InitiatorWon: false } &&
                loss.Game.GetHumanLegalActions().All(action => action.Kind != LegalActionKind.Slash),
            "Losing or tying Tianyi must prohibit Slash for the rest of the turn.");
    }

    private static Fixture Find(ContentRegistry registry, bool sourceWins, bool requireSlash)
    {
        for (var seed = 1; seed <= 32_768; seed++)
        {
            var game = Create(seed, registry);
            if (!Select(game)) continue;
            var play = Reach(game, DecisionKind.PlayCard, 64);
            if (play is null) continue;
            var full = game.CreateSnapshot(0, revealAll: true);
            var sourceHand = full.Players[0].Hand;
            if (requireSlash && sourceHand.All(card => card.Kind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash))) continue;
            foreach (var target in full.Players.Where(player => player.Seat != 0 && player.IsAlive && player.Hand.Count > 0))
            {
                var opponentMax = target.Hand.Max(card => card.Rank);
                var sourceCard = sourceWins
                    ? sourceHand.Where(card => card.Rank > opponentMax).OrderByDescending(card => card.Rank).FirstOrDefault()
                    : sourceHand.Where(card => card.Rank <= opponentMax).OrderBy(card => card.Rank).FirstOrDefault();
                if (sourceCard is null) continue;
                var before = game.CreateCheckpoint();
                var used = game.Submit(new UseSkillCommand(0, SkillKind.Tianyi, [sourceCard.Id], [target.Seat],
                    game.Revision, play.PromptId));
                if (!used.Accepted) continue;
                for (var step = 0; step < 8 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                    Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                        "Tianyi could not return to the initiator's play phase.");
                var result = game.Events.Select(item => item.Payload).OfType<PindianResolvedEvent>()
                    .LastOrDefault(item => item.Skill == SkillKind.Tianyi);
                if (result?.InitiatorWon == sourceWins && game.PendingDecision?.Kind == DecisionKind.PlayCard)
                    return new Fixture(game, before);
            }
        }
        throw new InvalidOperationException($"No bounded Tianyi {(sourceWins ? "win" : "loss")} fixture was found.");
    }

    private static GameEngine Create(int seed, ContentRegistry registry) => GameEngine.CreateStandard(new GameOptions
    {
        Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5,
        ModeId = "identity:classic-5", UseInteractiveSetup = true,
        UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 80
    }, registry);

    private static bool Select(GameEngine game)
    {
        if (!game.Submit(new StartGameCommand()).Accepted) return false;
        var prompt = game.PendingDecision;
        return prompt is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } &&
               prompt.ValidContentIds.Contains("classic:taishi-ci") &&
               game.Submit(new SelectGeneralCommand(0, "classic:taishi-ci", game.Revision, prompt.PromptId)).Accepted;
    }

    private static PendingDecision? Reach(GameEngine game, DecisionKind kind, int limit)
    {
        for (var step = 0; step < limit; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0 } pending && pending.Kind == kind) return pending;
            if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted) return null;
        }
        return null;
    }

    private static void SetSlashCount(GameEngine game, int value) =>
        typeof(GameEngine).GetField("_slashCountThisTurn", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(game, value);

    private sealed record Fixture(GameEngine Game, GameCheckpoint BeforeUse);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
