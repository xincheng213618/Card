using CardGame.Core;

internal static class PrivateReserveOrderChecks
{
    public static void EndSkillsChooseOrderPayOnceAndReplay()
    {
        var registry = PrivateReserveScenario.Registry();
        foreach (var first in new[] { PrivateReserveScenario.Gale, PrivateReserveScenario.Mist, "skip" })
        {
            var game = PrivateReserveScenario.Start(registry);
            Require(game.CreateSnapshot(0).Players[0].PrivateReserveCards?.Count == 7 &&
                game.CreateSnapshot(1).Players[0].PrivateReserveCards is null && game.CreateSnapshot(1).Players[0].PrivateReserveCount == 7,
                "The owner sees seven physical stars; other seats see only the pile count.");
            PrivateReserveScenario.Reach(game, g => g.PendingDecision?.Choices.Any(c => c.Parameters.ContainsKey("order-group")) == true);
            var prompt = game.PendingDecision!;
            Require(prompt.Prompt == "选择率先发动的技能" && prompt.Choices.Count == 3 &&
                prompt.Choices.Where(c => c.Parameters.GetValueOrDefault("program-action") == "activate")
                    .Select(c => c.Parameters["skill-id"]).ToHashSet().SetEquals([PrivateReserveScenario.Gale, PrivateReserveScenario.Mist]),
                "The actual ending boundary offers either skill first and one cancellation.");
            Replay();
            if (first == "skip")
            {
                PrivateReserveScenario.Answer(game, prompt.Choices.Single(c => c.Parameters["program-action"] == "skip"));
                Require(game.CreateSnapshot(0).Players[0].PrivateReserveCount == 7 &&
                    !game.CreateSnapshot(0).Players.Any(p => p.Markers?.Any(m => m.Kind is PlayerMarkerKind.Gale or PlayerMarkerKind.Mist) == true),
                    "Cancelling retires both optional opportunities without paying or marking.");
                Replay();
                continue;
            }
            var second = first == PrivateReserveScenario.Gale ? PrivateReserveScenario.Mist : PrivateReserveScenario.Gale;
            PrivateReserveScenario.Answer(game, prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("skill-id") == first && c.Parameters["program-action"] == "activate"));
            PayAndTarget(first, 1);
            Replay();
            Require(game.PendingDecision?.SkillPrompt?.SkillId == second &&
                game.PendingDecision.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"),
                "The unchosen skill remains optional after the first binding returns.");
            PrivateReserveScenario.Answer(game, game.PendingDecision!.Choices.Single(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
            PayAndTarget(second, 2);
            var owner = game.CreateSnapshot(0).Players[0];
            Require(owner.PrivateReserveCount == 5 && game.CardMovements.Count(m => m.From.Zone == CardZoneKind.PrivateReserve && m.To == CardLocation.DiscardPile) == 2,
                "Each actual skill pays one exact star once, in either chosen order.");
            Replay();
            PrivateReserveScenario.Reach(game, g => g.State.CurrentSeat == 0 && g.State.TurnNumber > 1 && g.State.Phase == TurnPhase.Play);
            Require(!game.CreateSnapshot(0).Players.Any(p => p.Markers?.Any(m => m.Kind is PlayerMarkerKind.Gale or PlayerMarkerKind.Mist) == true),
                "Both attributed markers expire at their source's next actual turn start.");
            Replay();

            void PayAndTarget(string skill, int target)
            {
                var payment = game.PendingDecision!;
                Require(payment.SkillPrompt?.SkillId == skill, "The chosen binding owns the exact private star payment prompt.");
                PrivateReserveScenario.Answer(game, payment.Choices.First(c => c.Cards.Count == 1));
                if (game.PendingDecision?.Choices.SingleOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards") is { } finish)
                    PrivateReserveScenario.Answer(game, finish);
                Replay();
                PrivateReserveScenario.Answer(game, game.PendingDecision!.Choices.Single(c => c.Targets.SequenceEqual([target])));
                Require(game.CreateSnapshot(0).Players[target].Markers?.Any(m => m.Kind == (skill == PrivateReserveScenario.Gale ? PlayerMarkerKind.Gale : PlayerMarkerKind.Mist)) == true,
                    "The selected target receives the matching public marker after payment.");
            }
            void Replay()
            {
                var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
                Require(SnapshotJson.Serialize(restored.CreateSnapshot(0)) == SnapshotJson.Serialize(game.CreateSnapshot(0)),
                    "The ordered cursor, private payments and public markers replay exactly at each pause.");
            }
        }
        var automated = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 19, PlayerCount = 4, HumanSeat = -1, HumanRole = null, ModeId = "identity:private-reserve-ui",
            UseInteractiveSetup = false, AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        PrivateReserveScenario.Accept(automated.Submit(new StartGameCommand()));
        for (var step = 0; step < 160 && automated.State.Status != EngineStatus.Completed && !HasOrderEstimate(); step++)
        {
            PrivateReserveScenario.Accept(automated.Submit(new AdvanceOneStepCommand(automated.Revision)));
        }
        Require(HasOrderEstimate() && automated.Events.Any(e => e.Payload is ProgramBindingResolvedEvent
                { Window: SkillProgramTriggerWindow.TurnEnding }),
            "AI must price both real ending choices together and resolve their shared owning frame.");
        RejectInvalidOrderGroup();

        bool HasOrderEstimate() => automated.AiThoughts.Any(thought =>
            thought.Candidates.Any(candidate => candidate.Action.Description == "发动【狂风】") &&
            thought.Candidates.Any(candidate => candidate.Action.Description == "发动【大雾】"));
    }

    private static void RejectInvalidOrderGroup()
    {
        foreach (var configuration in new[] { "\"window\":\"gameStarting\",\"optional\":true", "\"window\":\"turnEnding\",\"optional\":false" })
        {
            var rejected = false;
            try { SkillProgramCatalog.Load("{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"fixture:order\",\"revision\":1,\"triggers\":[{\"id\":\"order\"," + configuration + ",\"subject\":\"owner\",\"orderGroup\":\"ending\",\"effects\":[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}]}]}]}", "{\"schemaVersion\":" + SkillProgramCatalog.PresentationSchemaVersion + ",\"skills\":{\"fixture:order\":{\"name\":\"order\",\"description\":\"order\"}}}"); }
            catch (InvalidOperationException exception) when (exception.Message.Contains("orderGroup", StringComparison.Ordinal)) { rejected = true; }
            Require(rejected, "Order groups must reject mandatory or non-ending triggers.");
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
