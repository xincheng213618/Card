using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2011FaZhengChecks
{
    public static void TransferRewardsRepaymentsAndPhysicalSlashReplay()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        var slash = Create(registry, replaceDraw: true);
        var before = slash.CreateSnapshot(0, true);
        Require(before.Players[0].HandCount == 4, "Replacement must not draw owner's normal two cards.");
        Require(before.Players[1].HandCount == 6, "Replacement must give the chosen other character two cards.");
        var targetPrompt = slash.PendingDecision!;
        Require(targetPrompt.Choices.Any(c => c.Parameters.GetValueOrDefault("request-option") == "target"), "Owner must specify the real Slash target.");
        var restored = GameReplay.Restore(slash.CreateCheckpoint(), registry);
        foreach (var branch in new[] { slash, restored })
        {
            var target = branch.PendingDecision!.Choices.First(c => c.Targets.Contains(2));
            Require(branch.Submit(new AnswerPromptCommand(0, branch.PendingDecision.PromptId, target.Id, branch.Revision)).Accepted, "Target choice failed.");
            DrivePlay(branch);
        }
        EqualBranches(slash, restored, "Actual Slash target prompt");
        Require(slash.Events.Any(e => e.Payload is CardUsedEvent used && used.SourceSeat == 1 && used.CardKind == CardKind.Slash && used.CardId != 0), "Requested Slash must use a physical card owned by the recipient.");
        Require(slash.CardMovements.Any(m => m.From == CardLocation.Hand(1) && m.To == CardLocation.Processing) && slash.CardMovements.Any(m => m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile), "Requested Slash must pay and finish its physical source.");
        ReplayEqual(slash, registry);

        var declined = Create(registry, replaceDraw: true, mode: "fixture:fa-no-slash");
        Answer(declined, declined.PendingDecision!.Choices.First(c => c.Targets.Contains(2)));
        var declinedRestore = GameReplay.Restore(declined.CreateCheckpoint(), registry);
        foreach (var branch in new[] { declined, declinedRestore }) DrivePlay(branch);
        EqualBranches(declined, declinedRestore, "Declined physical Slash");
        Require(!declined.Events.Any(e => e.Payload is CardUsedEvent used && used.SourceSeat == 1 && used.CardKind == CardKind.Slash), "A recipient without a Slash source must not manufacture a virtual Slash.");
        Require(declined.CreateSnapshot(0, true).Players[0].HandCount == 6 && declined.CreateSnapshot(0, true).Players[1].HandCount == 5, "Decline must atomically take two cards, then reward their original owner once.");
        ReplayEqual(declined, registry);
        var typedRegistry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(typedSlash: true));
        var typed = Create(typedRegistry, true, "fixture:fa-no-slash");
        var typedRestore = GameReplay.Restore(typed.CreateCheckpoint(), typedRegistry);
        foreach (var branch in new[] { typed, typedRestore })
        {
            Answer(branch, branch.PendingDecision!.Choices.First(c => c.Targets.Contains(2)));
            DrivePlay(branch);
        }
        EqualBranches(typed, typedRestore, "Single-card elemental viewAs");
        Require(typed.Events.Any(e => e.Payload is CardUsedEvent used && used.SourceSeat == 1 && used.CardKind == CardKind.FireSlash), "A ForPlay FireSlash-only conversion must be available to the instructed actor.");
        Require(typed.CardMovements.Count(m => m.From == CardLocation.Hand(1) && m.To == CardLocation.Processing) == 1, "Elemental viewAs must pay exactly one actual card.");
        ReplayEqual(typed, typedRegistry);
        var nativeRegistry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(nativeOverlay: true));
        var native = Create(nativeRegistry, false, stopOnSlashSource: true);
        for (var i = 0; i < 80 && native.PendingDecision?.Choices.Any(c => c.Parameters.GetValueOrDefault("request-option") == "use") != true; i++) Step(native);
        var nativePrompt = native.PendingDecision!;
        Require(nativePrompt.PlayerSeat == 0 && nativePrompt.Choices.Any(c => c.Parameters.ContainsKey("conversion-skill-id")), "Native Slash fixture must also publish a same-kind conversion source.");
        var nativeRestore = GameReplay.Restore(native.CreateCheckpoint(), nativeRegistry);
        foreach (var branch in new[] { native, nativeRestore })
        {
            Answer(branch, branch.PendingDecision!.Choices.First(c => c.Parameters.GetValueOrDefault("request-option") == "use" && !c.Parameters.ContainsKey("conversion-skill-id")));
            DrivePlay(branch);
        }
        EqualBranches(native, nativeRestore, "Native Slash beside optional same-kind conversion");
        Require(native.Events.Any(e => e.Payload is CardUsedEvent used && used.SourceSeat == 0 && used.CardKind == CardKind.Slash), "Published native Slash must remain legal alongside an optional conversion.");
        ReplayEqual(native, nativeRegistry);
        var gain = Create(registry, replaceDraw: false);
        var starting = gain.CreateSnapshot(0, true);
        Activate(gain, "take-two", [1]);
        Require(gain.PendingDecision!.Choices.All(c => c.Cards.Count == 0), "Other hand cards must expose only opaque slots.");
        var pristineDraft = gain.CreateCheckpoint();
        void RejectTamper(Action<GameEngine> tamper)
        {
            var corrupted = GameReplay.Restore(pristineDraft, registry);
            tamper(corrupted);
            try { typeof(GameEngine).GetMethod("AssertCoreInvariants", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(corrupted, []); }
            catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is InvalidOperationException) { return; }
            throw new InvalidOperationException("Suspended assisted payment corruption was not rejected.");
        }
        RejectTamper(corrupted =>
        {
            var stack = (System.Collections.IList)typeof(GameEngine).GetField("_resolutionStack", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(corrupted)!;
            var frame = (ProgramSkillFrame)stack[stack.Count - 1]!;
            stack[stack.Count - 1] = frame with { OtherCardSelection = frame.OtherCardSelection! with { RequiredCount = 1 } };
        });
        RejectTamper(corrupted =>
        {
            var stack = (System.Collections.IList)typeof(GameEngine).GetField("_resolutionStack", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(corrupted)!;
            stack[stack.Count - 1] = ((ProgramSkillFrame)stack[stack.Count - 1]!) with { OtherCardSelection = null };
        });
        RejectTamper(corrupted =>
        {
            var prompt = corrupted.PendingDecision!;
            var choices = prompt.Choices.ToArray();
            choices[0] = choices[0] with { Parameters = new Dictionary<string, string>(choices[0].Parameters) { ["slot-index"] = "3" } };
            typeof(GameEngine).GetField("_pendingDecision", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(corrupted, prompt with { Choices = choices });
        });
        Answer(gain, gain.PendingDecision.Choices.First());
        var draftCheckpoint = GameReplay.Restore(gain.CreateCheckpoint(), registry);
        foreach (var branch in new[] { gain, draftCheckpoint }) DrivePlay(branch);
        EqualBranches(gain, draftCheckpoint, "Two-card hidden selection");
        var ending = gain.CreateSnapshot(0, true);
        Require(ending.Players[0].HandCount == starting.Players[0].HandCount + 2 && ending.Players[1].HandCount == starting.Players[1].HandCount - 1, "Taking two from one owner must reward that source with exactly one draw.");
        Require(gain.Events.Count(e => e.Payload is ProgramBindingStartedEvent started && started.SkillId == "classic:enyuan" && started.BindingId == "reward-card-giver") == 1, "Same-source gain must trigger once for the two-card batch.");
        ReplayEqual(gain, registry);

        var equipmentGain = Create(registry, false, "identity:classic-fa-spear");
        Activate(equipmentGain, "gift-equipment", [1]); DrivePlay(equipmentGain);
        var rewardCount = equipmentGain.Events.Count(e => e.Payload is ProgramBindingStartedEvent started && started.BindingId == "reward-card-giver");
        Activate(equipmentGain, "take-two", [1]);
        Answer(equipmentGain, equipmentGain.PendingDecision!.Choices.First(c => c.Cards.Count == 0));
        Answer(equipmentGain, equipmentGain.PendingDecision!.Choices.First(c => c.Cards.Count == 1));
        DrivePlay(equipmentGain);
        Require(equipmentGain.Events.Count(e => e.Payload is ProgramBindingStartedEvent started && started.BindingId == "reward-card-giver") == rewardCount + 1, "One source's hand and equipment must aggregate into exactly one reward.");
        Require(equipmentGain.CardMovements.Any(m => m.From == CardLocation.Equipment(1) && m.To == CardLocation.Hand(0)), "The equipment selection must actually move the source's equipped card.");
        ReplayEqual(equipmentGain, registry);
        var spear = Create(registry, false, "identity:classic-fa-spear");
        Activate(spear, "gift-equipment", [1]); DrivePlay(spear);
        Activate(spear, "request-slash", [1]);
        var spearRestore = GameReplay.Restore(spear.CreateCheckpoint(), registry);
        foreach (var branch in new[] { spear, spearRestore })
        {
            Answer(branch, branch.PendingDecision!.Choices.First(c => c.Targets.Contains(2)));
            DrivePlay(branch);
        }
        EqualBranches(spear, spearRestore, "Spear payment");
        Require(spear.Events.Any(e => e.Payload is CardUsedEvent used && used.SourceSeat == 1 && used.CardKind == CardKind.Slash), "Recipient without printed Slash must use the actual Spear conversion.");
        Require(spear.CardMovements.Count(m => m.From == CardLocation.Hand(1) && m.To == CardLocation.Processing) == 2, "Spear Slash must move both physical payment cards.");
        ReplayEqual(spear, registry);
        var grouping = Create(registry, false);
        var trigger = registry.GetSkill("classic:enyuan").Program!.Triggers.Single(t => t.Id == "reward-card-giver");
        var candidate = new ProgramTriggerCandidate(0, "classic:enyuan", trigger.Id, "template:primary:classic:enyuan", "fixture", 0);
        var collector = typeof(GameEngine).GetMethod("CollectSourceOwnerGainCandidates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        int Candidates(params CardMovementRecord[] moves)
        {
            var count = new CardMovementSourceCount(CardLocation.Hand(0), 6, 8);
            var batch = new CardMovementBatchContext(999, null, null, 1, moves, [], DestinationCounts: [count]);
            return ((IEnumerable<ProgramTriggerCandidate>)collector.Invoke(grouping, [batch, candidate, trigger, count])!).Count();
        }
        CardMovementRecord Movement(int sequence, CardLocation source) => new(sequence, 1, sequence + 1000, CardKind.Slash, source, CardLocation.Hand(0), new("fixture.grouping"));
        Require(Candidates(Movement(0, CardLocation.Hand(1)), Movement(1, CardLocation.Hand(2))) == 0, "One card each from two sources must not combine into a two-card Enyuan reward.");
        Require(Candidates(Movement(0, CardLocation.Hand(1)), Movement(1, CardLocation.Equipment(1))) == 1, "Two source zones owned by the same character must combine.");
        Activate(grouping, "kill-source", [1]); DrivePlay(grouping);
        Require(!grouping.CreateSnapshot(0, true).Players[1].IsAlive, "Dead-source negative must use an actually deceased character.");
        Require(Candidates(Movement(0, CardLocation.Hand(1)), Movement(1, CardLocation.Equipment(1))) == 0, "A dead original source must not receive an optional draw prompt.");
        ReplayEqual(grouping, registry);
        var repayment = Create(registry, replaceDraw: false);
        var hp = repayment.CreateSnapshot(0, true).Players[1].Hp;
        Activate(repayment, "hurt-owner", [1]);
        for (var i = 0; i < 80 && repayment.PendingDecision?.Choices.Any(c => c.Parameters.GetValueOrDefault("binding-id") == "repay-each-damage") != true; i++) Step(repayment);
        var repayPrompt = repayment.PendingDecision!;
        Require(repayPrompt.PlayerSeat == 0 && repayPrompt.Choices.Any(c => c.Parameters.GetValueOrDefault("binding-id") == "repay-each-damage"), "Each damage point must offer Enyuan to the damaged owner.");
        var replayRepay = GameReplay.Restore(repayment.CreateCheckpoint(), registry);
        foreach (var branch in new[] { repayment, replayRepay }) DrivePlay(branch);
        EqualBranches(repayment, replayRepay, "Damage repayment source choice");
        Require(repayment.Events.Count(e => e.Payload is ProgramBindingStartedEvent started && started.SkillId == "classic:enyuan" && started.BindingId == "repay-each-damage") == 2, "Two damage must produce two distinct optional repayment opportunities.");
        var answers = repayment.Events.Select(e => e.Payload).OfType<ProgramOptionChosenEvent>().Where(e => e.SkillId == "classic:enyuan").ToArray();
        Require(answers.Length == 2 && repayment.CreateSnapshot(0, true).Players[1].Hp == hp - answers.Count(e => e.OptionId == "lose-hp"), "Each repayment choice must apply exactly one gift or HP loss.");
        ReplayEqual(repayment, registry);
        var emptySource = Create(registry, replaceDraw: false);
        for (var i = 0; i < 4; i++) { Activate(emptySource, "take-two", [1]); DrivePlay(emptySource); }
        Require(emptySource.CreateSnapshot(0, true).Players[1].HandCount == 0, "Empty-source fixture must exhaust its actual hand.");
        var beforeLoss = emptySource.CreateSnapshot(0, true).Players[1].Hp;
        Activate(emptySource, "hurt-owner", [1]);
        DrivePlay(emptySource);
        Require(emptySource.CreateSnapshot(0, true).Players[1].Hp == beforeLoss - 2, "A source with no hand cards must lose one HP per damage point.");
        ReplayEqual(emptySource, registry);
    }
    private static GameEngine Create(ContentRegistry registry, bool replaceDraw, string mode = "fixture:fa-zheng", bool stopOnSlashSource = false)
    {
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4, ModeId = mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Start failed.");
        var setup = game.PendingDecision!;
        Require(game.Submit(new SelectGeneralCommand(0, "fixture:fa-0", game.Revision, setup.PromptId)).Accepted, "General choice failed.");
        for (var i = 0; i < 100 && game.PendingDecision?.Kind != DecisionKind.PlayCard; i++)
        {
            var prompt = game.PendingDecision;
            if (stopOnSlashSource && prompt?.Choices.Any(c => c.Parameters.GetValueOrDefault("request-option") == "use") == true) return game;
            if (prompt?.PlayerSeat == 0 && prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("binding-id") == "replace-draw-with-request"))
                Answer(game, prompt.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == (replaceDraw ? "activate" : "skip")));
            else if (replaceDraw && prompt?.Choices.Any(c => c.Parameters.GetValueOrDefault("request-option") == "target") == true) return game;
            else if (replaceDraw && prompt?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target") == true)
                Answer(game, prompt.Choices.First(c => c.Targets.Contains(1)));
            else Step(game);
        }
        return game;
    }
    private static void Activate(GameEngine game, string id, int[] targets)
    {
        Require(game.Submit(new UseProgramSkillCommand(0, "fixture:fa-driver", id, [], targets, game.Revision, game.PendingDecision!.PromptId)).Accepted, "Activation failed.");
        for (var i = 0; i < 50 && game.PendingDecision?.Kind == DecisionKind.PlayCard; i++) Step(game);
    }
    private static void DrivePlay(GameEngine game) { for (var i = 0; i < 160 && (game.ResolutionStack.Count != 0 || game.PendingDecision?.Kind != DecisionKind.PlayCard); i++) Step(game); Require(game.ResolutionStack.Count == 0, "Resolution did not finish."); }
    private static void Step(GameEngine game)
    {
        var prompt = game.PendingDecision;
        if (prompt is { PlayerSeat: 0 } && prompt.Kind != DecisionKind.PlayCard) Answer(game, prompt.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") != "skip"));
        else Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Advance failed.");
    }
    private static void Answer(GameEngine game, PromptChoice choice) { var result = game.Submit(new AnswerPromptCommand(game.PendingDecision!.PlayerSeat, game.PendingDecision.PromptId, choice.Id, game.Revision)); Require(result.Accepted, result.Error?.Message ?? "Answer failed."); }
    private static void EqualBranches(GameEngine first, GameEngine second, string label)
    {
        Require(SnapshotJson.Serialize(first.CreateSnapshot(0, true)) == SnapshotJson.Serialize(second.CreateSnapshot(0, true)) && first.CardMovements.SequenceEqual(second.CardMovements) && first.Events.Select(e => System.Text.Json.JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).SequenceEqual(second.Events.Select(e => System.Text.Json.JsonSerializer.Serialize(e.Payload, e.Payload.GetType()))), label + " checkpoint replay diverged.");
    }
    private static void ReplayEqual(GameEngine game, ContentRegistry registry) { var replay = GameReplay.Replay(game.CreateCheckpoint().Options, CommandJson.Deserialize(CommandJson.Serialize(game.AcceptedCommands)), registry); EqualBranches(game, replay, "Command journal"); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Fixture(bool typedSlash = false, bool nativeOverlay = false) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fa-zheng-fixture", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var fixtureRules = nativeOverlay ? Rules.Replace("\"activations\":[", "\"viewAs\":[{\"id\":\"native-overlay\",\"inputKinds\":[\"slash\",\"peach\"],\"inputSuits\":[],\"outputKind\":\"slash\",\"forPlay\":true,\"forResponse\":false,\"usesPerPhase\":1,\"inheritPreviousPlaySuit\":true}],\"triggers\":[{\"id\":\"instruct-damager\",\"window\":\"playPhaseStarting\",\"subject\":\"owner\",\"turnOwnerScope\":\"otherLiving\",\"optional\":false,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"currentTurnPlayer\"},{\"op\":\"requestSlashAgainstChosenTarget\",\"target\":\"selectedTarget\",\"resultBind\":\"native-result\"}]}],\"activations\":[") : Rules;
            var catalog = SkillProgramCatalog.Load(typedSlash ? fixtureRules.Replace("\"activations\":[", "\"viewAs\":[{\"id\":\"fire-only\",\"inputKinds\":[\"peach\"],\"inputSuits\":[],\"outputKind\":\"fireSlash\",\"forPlay\":true,\"forResponse\":false,\"extendedUse\":true}],\"activations\":[") : fixtureRules, """{"schemaVersion":3,"skills":{"fixture:fa-driver":{"name":"Driver","description":"Fixture"}}}""");
            builder.AddSkill(new ContentSkillDefinition("fixture:fa-driver", "Driver", "Fixture") { Program = catalog.Programs["fixture:fa-driver"] });
            var ids = Enumerable.Range(0, 4).Select(i => $"fixture:fa-{i}").ToArray();
            foreach (var id in ids) builder.AddGeneral(new ContentGeneralDefinition(id, "Fixture", "supporter", "fixture:fa-driver", AdditionalSkillIds: id == ids[0] ? ["classic:enyuan", "classic:xuanhuo"] : []));
            builder.AddDeck(new ContentDeckRecipe("fixture:fa-deck", "Fixture", 4, 2, [new ContentDeckCardCount("standard:slash", 100)]));
            builder.AddDeck(new ContentDeckRecipe("fixture:fa-no-slash-deck", "Fixture", 4, 2, [new ContentDeckCardCount("standard:peach", 100)]));
            builder.AddMode(new ContentModeDefinition("fixture:fa-no-slash", "Fixture", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 }, "fixture:fa-no-slash-deck", GeneralCandidateCount: 4, GeneralPoolIds: ids));
            builder.AddDeck(new ContentDeckRecipe("fixture:fa-spear-deck", "Fixture", 4, 2, [new ContentDeckCardCount("classic:zhangba-serpent-spear", 100)]));
            builder.AddMode(new ContentModeDefinition("identity:classic-fa-spear", "Fixture", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 }, "fixture:fa-spear-deck", GeneralCandidateCount: 4, GeneralPoolIds: ids));
            builder.AddMode(new ContentModeDefinition("fixture:fa-zheng", "Fixture", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 }, "fixture:fa-deck", GeneralCandidateCount: 4, GeneralPoolIds: ids));
        }
    }
    private const string Rules = """
    {"schemaVersion":62,"skills":[{"id":"fixture:fa-driver","revision":1,"minimumRulesVersion":190,"activations":[
    {"id":"take-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"takeSelectedTargetCards","target":"selectedTarget","amount":2}]},
    {"id":"gift-equipment","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand"],"count":1,"cardCategories":["equipment"],"destination":"selectedTargetCorrespondingZone","targetRef":{"kind":"selectedTarget"},"prohibitReplacingEquipment":true}]},
    {"id":"request-slash","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashAgainstChosenTarget","target":"selectedTarget","resultBind":"request-result"}]},
    {"id":"kill-source","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":5}]},
    {"id":"hurt-owner","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]}]}]}
    """;
}
