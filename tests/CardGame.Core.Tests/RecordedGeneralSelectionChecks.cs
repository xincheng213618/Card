using CardGame.Content.Standard;
using CardGame.Core;

internal static class RecordedGeneralSelectionChecks
{
    public static void RecordedScoringAndFingerprintsRemainStrict()
    {
        var current = ComposedSkillContentRegistry.CreateShowcase(includeActivities: true);
        var historical = ComposedSkillContentRegistry.CreateShowcase(includeActivities: true, historicalSelectionWeights: true);
        Need(current.ContentHash != historical.ContentHash && current.GetSkill("classic:guicai").SelectionWeights is null &&
            historical.GetSkill("classic:guicai").SelectionWeights![Role.Lord] == 41,
            "Recorded selection metadata retains its original fingerprint without restoring priors to new content.");
        var view = GameEngine.CreateStandard(new GameOptions { HumanSeat = 0, HumanRole = Role.Lord }, current).CreateSnapshot(0);
        var recorded = new GeneralDefinition("fixture:recorded-sima", "旧评分", "supporter",
        [new("鬼才", "改判") { ContentId = "classic:guicai", SelectionWeights = historical.GetSkill("classic:guicai").SelectionWeights },
         new("反馈", "获得牌") { ContentId = "classic:feedback" }], "wei", 3);
        var active = new GeneralDefinition("fixture:active", "主动候选", "supporter",
            [new("主动", "基础主动") { ActionForms = SkillActionForm.Active }], "wei", 4);
        for (var policy = 1; policy < AiDecisionPolicy.RuleBasedGeneralSelectionVersion; policy++)
        {
            var result = new SimpleAiBrain(0, 31, policy).ChooseGeneral(view, Role.Lord, [recorded, active], 1,
                contentRegistry: historical);
            Need(result.General.Id == recorded.Id && Math.Abs(result.Thought.Candidates[0].Score - 42.8) < .002,
                "Recorded policies keep the original printed scoring and do not acquire the new fragile-Lord penalty.");
        }
        var currentThought = new SimpleAiBrain(0, 31, AiDecisionPolicy.CurrentVersion).ChooseGeneral(view, Role.Lord,
            [recorded, active], 1, contentRegistry: current).Thought;
        Need(currentThought.Candidates.Single(candidate => candidate.GeneralId == recorded.Id).Score < 42.79 &&
            currentThought.Candidates.Single(candidate => candidate.GeneralId == recorded.Id).Reason.Contains("低体力"),
            "New-policy games still use rule-based scores and independent Lord safety.");

        // Record a real setup prefix rather than pinning random candidate/card
        // IDs to a catalogue that intentionally grows with new character batches.
        var prefix = GameEngine.CreateStandard(new GameOptions { Seed = 755686063, PlayerCount = 8,
            HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:classic-8", DeckId = "classic:standard-deck",
            UseInteractiveSetup = true, ConfirmOpeningHand = true, AdvanceAfterHumanCommands = false,
            AiPolicyVersion = AiDecisionPolicy.CorrelatedActiveSkillVersion }, historical);
        Need(prefix.Submit(new StartGameCommand()).Accepted, "Recorded setup starts.");
        var selection = prefix.PendingDecision!;
        Need(prefix.Submit(new RequestGeneralRosterCommand(0, true, prefix.Revision, selection.PromptId)).Accepted,
            "Historical full roster retains old editions.");
        Need(prefix.Submit(new SelectGeneralCommand(0, "classic:sima-yi", prefix.Revision, prefix.PendingDecision!.PromptId)).Accepted,
            "A real recorded command can retain its exact old edition without pinning today's random catalogue order.");
        for (var step = 0; step < 24 && prefix.PendingDecision is not { PlayerSeat: 0, Kind: DecisionKind.OpeningHand }; step++)
            Need(prefix.Submit(new AdvanceOneStepCommand(prefix.Revision)).Accepted, "Historical setup advances.");
        var recordedCheckpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(prefix.CreateCheckpoint()));
        var recordedCopy = GameReplay.Restore(recordedCheckpoint,
            ComposedSkillContentRegistry.CreateShowcaseForCheckpoint(recordedCheckpoint));
        Need(Enumerable.Range(0, prefix.PlayerCount).All(seat => SnapshotJson.Serialize(prefix.CreateSnapshot(seat)) ==
            SnapshotJson.Serialize(recordedCopy.CreateSnapshot(seat))), "The recorded response prefix cold replays every viewer.");

        foreach (var registry in new[] { historical, current })
        {
            var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, HumanSeat = 0, HumanRole = Role.Lord,
                AiPolicyVersion = AiDecisionPolicy.CorrelatedActiveSkillVersion, UseInteractiveSetup = true,
                AdvanceAfterHumanCommands = false }, registry);
            Need(game.Submit(new StartGameCommand()).Accepted, "Recorded setup starts.");
            var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
            var json = GameCheckpointJson.Serialize(checkpoint);
            var matched = ComposedSkillContentRegistry.CreateShowcaseForCheckpoint(checkpoint);
            var copy = GameReplay.Restore(checkpoint, matched);
            Need(matched.ContentHash == registry.ContentHash && json == GameCheckpointJson.Serialize(checkpoint) &&
                Enumerable.Range(0, game.PlayerCount).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == SnapshotJson.Serialize(copy.CreateSnapshot(seat))),
                "Both old and current fingerprints select matching definitions and replay every viewer without rewriting the save.");
            var invalid = checkpoint with { ContentHash = "unrecognized-content" };
            var rejected = false;
            try { GameReplay.Restore(invalid, ComposedSkillContentRegistry.CreateShowcaseForCheckpoint(invalid)); }
            catch (InvalidOperationException) { rejected = true; }
            Need(rejected, "Historical metadata must never bypass the ordinary content fingerprint gate.");
        }
    }

    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
