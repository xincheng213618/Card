using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class WenhePackageChecks
{
    public static void RealOptInOfficialRosterPrivateDraftAndColdRestore()
    {
        const string modeId = "activity:wenhe-2019";
        var without = ComposedSkillContentRegistry.CreateShowcase();
        var registry = ComposedSkillContentRegistry.CreateShowcase(includeWenheActivities: true);
        var mode = registry.Modes[modeId];
        Need(!without.Modes.ContainsKey(modeId) && without.Packages.All(p => p.Id != "wenhe-activity") &&
            registry.Packages.Count(p => p.Id == "wenhe-activity") == 1 && registry.ContentHash != without.ContentHash,
            "The real 2019 survival package must be independently opt-in without changing the existing default package set.");
        var pool = mode.GeneralPoolIds!;
        Need(mode is { ModeKind: ContentModeKind.Survival, MinPlayers: 8, MaxPlayers: 8, GeneralCandidateCount: 5,
                WenheActivity: { PlayerTier: 5, EnableAspirations: true }, TeamCounts: null } &&
            mode.RoleCounts.Count == 0 && pool.All(id => registry.Generals[id].FactionId != "god") &&
            new[] { "classic:hua-tuo", "classic:liu-bei", "boundary:liu-bei", "classic:yu-ji", "classic:zuo-ci",
                "classic:xu-huang", "classic:wolong-zhuge-liang", "boundary:wolong-zhuge-liang", "classic:cao-ren",
                "classic:da-qiao", "boundary:da-qiao", "classic:meng-huo", "classic:xun-you", "classic:guo-huanghou",
                "boundary:guo-huang-hou", "classic:zhuge-dan", "classic:chen-gong", "boundary:gan-ning",
                "classic:xiao-qiao", "classic:zhou-tai", "classic:lu-ji", "classic:zhuge-zhan", "classic:dong-zhuo",
                "ol:wu-tu-gu", "classic:pang-tong", "classic:dong-yun", "classic:sun-lu-ban", "classic:ta-dun",
                "classic:yuan-shao", "classic:cao-pi", "classic:man-chong", "classic:jia-xu", "classic:cai-wen-ji",
                "ol:ling-ju", "ol:bu-zhi", "classic:cao-jie", "ol:liu-yan" }.All(id => !pool.Contains(id)) &&
            new[] { "classic:gan-ning", "classic:zhuge-liang", "classic:guan-yu", "classic:huang-gai", "ol:wang-yun" }
                .All(pool.Contains),
            "The exact official bans must cover legacy null identities and both Guo Huanghou spellings while ordinary Gan Ning and Zhuge Liang remain legal.");
        Need(registry.Skills["wenhe:rules"].Program!.Triggers.Count == 5 &&
            registry.Skills["wenhe:chaos-resolution"].Program!.Activations.Single().Id == "card-effect" &&
            new[] { CardKind.StarShift, CardKind.BeamSwap, CardKind.HandRedistribution, CardKind.WenheChaos, CardKind.Poison }
                .All(kind => registry.Cards.Values.Any(card => card.LegacyKind == kind)),
            "The activity must register the actual lifecycle bundle, private-mark entry, native chaos bridge and physical card identities.");

        foreach (var human in new[] { 0, 2 })
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 17, PlayerCount = 8, HumanSeat = human, HumanRole = null, ModeId = modeId,
                WenheAspirationRank = 4, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false,
                UseInteractiveDiscard = false, MaxTurns = 4
            }, registry);
            Accept(game, new StartGameCommand());
            Until(game, () => game.PendingDecision is { Kind: DecisionKind.SelectGeneral } p && p.PlayerSeat == human);
            var prompt = game.PendingDecision!;
            Need(prompt.IsPrivate && prompt.ValidContentIds.Count == 5 && prompt.ValidContentIds.All(pool.Contains) &&
                Enumerable.Range(0, 8).Where(seat => seat != human).All(seat => game.CreateSnapshot(seat).PendingDecision is null),
                "Only the actual human viewer may see the five legal ordinary-general offers, including nonzero human seats.");
            var before = SnapshotJson.Serialize(game.CreateSnapshot(human));
            var forged = game.Submit(new SelectGeneralCommand(human, "classic:shen-lu-bu", game.Revision, prompt.PromptId));
            Need(!forged.Accepted && SnapshotJson.Serialize(game.CreateSnapshot(human)) == before,
                "A forged god selection must reject without mutating the survival draft or private aspiration.");
            Accept(game, new RequestGeneralRosterCommand(human, true, game.Revision, prompt.PromptId));
            Need(game.PendingDecision!.ValidContentIds.Contains("classic:guan-yu") &&
                game.PendingDecision.ValidContentIds.All(pool.Contains), "The expanded roster must use the same explicit ban-filtered pool.");
            Accept(game, new SelectGeneralCommand(human, "classic:guan-yu", game.Revision, game.PendingDecision.PromptId));
            Until(game, () => game.Events.Any(e => e.Payload is SetupCompletedEvent));
            var view = game.CreateSnapshot(human);
            Need(view.Players.Count == 8 && view.Players.Count(p => p.IsHuman) == 1 &&
                view.Players.All(p => p.TeamId is null && p.Role == Role.Contestant && pool.Contains(p.GeneralId) &&
                    p.MaxHp == registry.Generals[p.GeneralId].BaseHp + 2 &&
                    game.CardMovements.Count(m => m.To == CardLocation.Hand(p.Seat) && m.Reason == CardMoveReasons.InitialDeal) == 5 &&
                    (p.IsHuman || p.Hand.Count == 0)) && view.WenheAspiration is { GoalRank: 4, CanUseChampionMark: false } &&
                view.Players[human].Skills!.All(skill => skill.ContentId != "wenhe:champion-mark") &&
                Enumerable.Range(0, 8).Where(seat => seat != human).All(seat => game.CreateSnapshot(seat).WenheAspiration?.GoalRank != 4),
                "All eight independent contestants must receive five real cards and tier-five health, with no team or foreign private aspiration leakage.");
            var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
            var reconstructed = ComposedSkillContentRegistry.CreateShowcaseForCheckpoint(checkpoint);
            var restored = GameReplay.Restore(checkpoint, reconstructed);
            Need(reconstructed.ContentHash == registry.ContentHash && restored.ModeId == modeId &&
                Enumerable.Range(0, 8).All(seat => SnapshotJson.Serialize(restored.CreateSnapshot(seat)) == SnapshotJson.Serialize(game.CreateSnapshot(seat))),
                "The package-tagged checkpoint must rebuild and cold-replay the exact survival registry and all private viewer states.");
        }
    }

    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Wenhe package command rejected."); }
    private static void Until(GameEngine game, Func<bool> ready)
    {
        for (var step = 0; step < 160; step++) { if (ready()) return; Accept(game, new AdvanceOneStepCommand(game.Revision)); }
        throw new InvalidOperationException("The real survival draft did not reach its bounded setup boundary.");
    }
}
