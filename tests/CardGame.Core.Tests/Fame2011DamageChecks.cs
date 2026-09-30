using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2011DamageChecks
{
    public static void ReplacementAndRefillProgramsLoadAndRun()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = "fixture:fame-damage", UseInteractiveSetup = false, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Start failed.");
        for (var i = 0; i < 50 && game.PendingDecision?.Kind != DecisionKind.PlayCard; i++)
            Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted, "Advance failed.");
        var immediate = registry.GetSkill("classic:shangshi").Program!.Triggers.Where(trigger => trigger.Window is SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardResponseAccepted).ToArray();
        var handFacts = new SkillProgramTriggerFacts(0, 1, true, CurrentMaxHp: 5, CurrentHandCount: 0, CardActionHandCardCount: 0);
        Require(immediate.All(trigger => !trigger.Condition.Evaluate(handFacts, "classic:shangshi", "fixture")) &&
            immediate.All(trigger => trigger.Condition.Evaluate(handFacts with { CardActionHandCardCount = 1 }, "classic:shangshi", "fixture")),
            "A zero-physical or equipment-only action must not offer refill; an actual hand payment must.");
        var before = game.CreateSnapshot(0, true).Players[0].Hp;
        Require(game.Submit(new UseProgramSkillCommand(0, "fixture:fame-driver", "damage", [], [], game.Revision,
            game.PendingDecision!.PromptId)).Accepted, "Damage activation failed.");
        for (var i = 0; i < 50 && game.ResolutionStack.Count != 0; i++) Advance(game);
        Require(game.CreateSnapshot(0, true).Players[0].Hp == before - 1, "Replacement must lose one HP.");
        Require(game.Events.Any(e => e.Payload is DamageReplacedWithHpLossEvent), "Damage must use the opt-in replacement.");
        Require(!game.Events.Any(e => e.Payload is DamageAppliedEvent), "HP loss must not publish damage.");
        for (var i = 0; i < 50 && game.PendingDecision?.Kind != DecisionKind.PlayCard; i++) Advance(game);
        Require(game.Submit(new UseProgramSkillCommand(0, "fixture:fame-driver", "wound", [], [], game.Revision,
            game.PendingDecision!.PromptId)).Accepted, "Wound activation failed.");
        for (var i = 0; i < 50 && game.PendingDecision?.Kind != DecisionKind.ProgramTrigger; i++) Advance(game);
        Require(game.PendingDecision?.Kind == DecisionKind.ProgramTrigger, "Health loss with insufficient hand must offer refill.");
        var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
        foreach (var branch in new[] { game, restored })
        {
            for (var i = 0; i < 50 && branch.ResolutionStack.Count != 0; i++) Advance(branch);
            var owner = branch.CreateSnapshot(0, true).Players[0];
            Require(owner.Hand.Count == owner.MaxHp - Math.Max(0, owner.Hp), "Refill must reach actual missing HP once.");
            Require(branch.ResolutionStack.Count == 0, "Refill must not recursively suspend itself.");
        }
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(restored.CreateSnapshot(0, true)),
            "Refill must replay from the frozen optional prompt.");
        WineArmorAndChain();
        DyingRefillBeforeRescueAndContinuations();
        ZeroPhysicalVirtualUseDoesNotRefill();
    }
    private static void WineArmorAndChain()
    {
        foreach (var armor in new[] { "classic:silver-lion", "classic:tengjia" })
        {
            var found = false;
            for (var seed = 1; seed <= 1 && !found; seed++)
            {
                var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
                    new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(true, armor));
                var game = GameEngine.CreateStandard(new GameOptions { Seed = seed, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
                    ModeId = "fixture:fame-damage", UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, registry);
                Require(game.Submit(new StartGameCommand()).Accepted, "Combat fixture failed.");
                Require(game.Submit(new SelectGeneralCommand(0, "fixture:fame-damage-0", game.Revision, game.PendingDecision!.PromptId)).Accepted, "Combat general failed.");
                for (var step = 0; step < 30 && game.State.Status != EngineStatus.Completed; step++)
                {
                    if (game.PendingDecision?.Kind != DecisionKind.PlayCard || game.PendingDecision.PlayerSeat != 0) { Advance(game); continue; }
                    var snapshot = game.CreateSnapshot(0, true);
                    var wine = snapshot.Players[0].Hand.FirstOrDefault(card => card.Kind == CardKind.Alcohol);
                    var slash = game.GetHumanLegalActions().FirstOrDefault(action => action.CardId is { } id && action.TargetSeat is { } seat &&
                        snapshot.Players[0].Hand.Any(card => card.Id == id && card.Kind == CardKind.FireSlash) &&
                        snapshot.Players[seat].Hp > 2 && snapshot.Players[seat].Equipment.Any(card => card.Kind ==
                            (armor == "classic:silver-lion" ? CardKind.SilverLion : CardKind.Tengjia)));
                    if (wine is null || slash is null) { Require(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision.PromptId)).Accepted, "Wait failed."); continue; }
                    var target = slash.TargetSeat!.Value;
                    var chainPeer = Enumerable.Range(1, 3).First(seat => seat != target && snapshot.Players[seat].IsAlive);
                    foreach (var seat in new[] { target, chainPeer })
                    {
                        Require(game.Submit(new UseProgramSkillCommand(0, "fixture:fame-driver", "chain", [], [seat], game.Revision, game.PendingDecision!.PromptId)).Accepted, "Chain failed.");
                        for (var i = 0; i < 50 && game.PendingDecision?.Kind != DecisionKind.PlayCard; i++) Advance(game);
                    }
                    Require(game.Submit(new PlayCardCommand(0, wine.Id, [], game.Revision, game.PendingDecision!.PromptId)).Accepted, "Wine failed.");
                    for (var i = 0; i < 50 && game.PendingDecision?.Kind != DecisionKind.PlayCard; i++) Advance(game);
                    var before = game.CreateSnapshot(0, true);
                    var eventStart = game.Events.Count;
                    Require(game.Submit(new PlayCardCommand(0, slash.CardId!.Value, [target], game.Revision, game.PendingDecision!.PromptId)).Accepted, "Fire Slash failed.");
                    var replay = GameReplay.Restore(game.CreateCheckpoint(), registry);
                    foreach (var branch in new[] { game, replay })
                    {
                        for (var i = 0; i < 200 && branch.ResolutionStack.Count != 0; i++) Advance(branch);
                        var after = branch.CreateSnapshot(0, true);
                        Require(after.Players[target].Hp == before.Players[target].Hp - 2, "Wine must remain two HP loss through armor.");
                        Require(after.Players[chainPeer].Hp == before.Players[chainPeer].Hp && after.Players[target].IsChained && after.Players[chainPeer].IsChained,
                            "Replacement must preserve chains without propagating elemental damage.");
                        var events = branch.Events.Skip(eventStart).Select(e => e.Payload).ToArray();
                        Require(events.OfType<DamageReplacedWithHpLossEvent>().Single().Amount == 2 && !events.OfType<DamageAppliedEvent>().Any(),
                            "Wine replacement must publish only a two-point HP-loss event.");
                    }
                    Require(SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(replay.CreateSnapshot(0, true)), "Wine replacement must replay.");
                    found = true; break;
                }
            }
            Require(found, "No bounded actual wine and armor combat boundary found: " + armor);
        }
    }
    private static void DyingRefillBeforeRescueAndContinuations()
    {
        foreach (var (replacement, activation) in new[] { (true, "lethal"), (false, "lethal"), (true, "lethal-loss") })
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
                new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(replacement: replacement));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
                ModeId = "fixture:fame-damage", UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false }, registry);
            Require(game.Submit(new StartGameCommand()).Accepted, "Dying fixture failed.");
            for (var i = 0; i < 50 && game.PendingDecision?.Kind != DecisionKind.PlayCard; i++) Advance(game);
            foreach (var seat in new[] { 1, 2, 3 }) {
                Require(game.Submit(new UseProgramSkillCommand(0, "fixture:fame-driver", "supply", [], [seat], game.Revision, game.PendingDecision!.PromptId)).Accepted, "Rescue supply failed.");
                for (var i = 0; i < 50 && game.PendingDecision?.Kind != DecisionKind.PlayCard; i++) Advance(game);
            }
            Require(game.Submit(new UseProgramSkillCommand(0, "fixture:fame-driver", activation, [], [], game.Revision, game.PendingDecision!.PromptId)).Accepted, "Lethal failed.");
            for (var i = 0; i < 50 && game.PendingDecision?.Kind != DecisionKind.ProgramTrigger; i++) Advance(game);
            var owner = game.CreateSnapshot(0, true).Players[0];
            Require(owner.Hp == (replacement && activation == "lethal" ? -1 : 0) && owner.Hand.Count == 0 && game.PendingDecision?.SkillPrompt?.SkillId == "classic:shangshi",
                $"Negative-HP Shangshi {replacement}/{activation}: HP {owner.Hp}, hand {owner.Hand.Count}, prompt {game.PendingDecision?.Kind}, skill {game.PendingDecision?.SkillPrompt?.SkillId}.");
            var entry = game.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single();
            Require(entry.ResumeDyingFrameId is { } dyingId && game.ResolutionStack.OfType<DyingFrame>().Single().Id == dyingId,
                "All dying origins must retain their explicit entry parent.");
            var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
            foreach (var branch in new[] { game, restored })
            {
                Advance(branch);
                for (var i = 0; i < 50 && branch.PendingDecision?.Kind != DecisionKind.RescueDying; i++) Advance(branch);
                Require(branch.CreateSnapshot(0, true).Players[0].Hand.Count == 5,
                    "Shangshi must draw to the existing lost-HP cap at maximum HP even while HP is negative before asking for Peach.");
                for (var i = 0; i < 200 && branch.ResolutionStack.Count != 0; i++)
                {
                    var prompt = branch.PendingDecision;
                    if (prompt?.Kind == DecisionKind.RescueDying && prompt.PlayerSeat == 0)
                    {
                        var peach = prompt.Choices.First(choice => choice.Cards.Count > 0);
                        var hpBeforePeach = branch.CreateSnapshot(0, true).Players[0].Hp;
                        Require(branch.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, peach.Id, branch.Revision)).Accepted,
                            "Freshly drawn Peach must rescue the same dying victim.");
                        Require(branch.PendingDecision?.SkillPrompt?.SkillId == "classic:shangshi" && branch.CreateSnapshot(0, true).Players[0].Hp == hpBeforePeach,
                            "Actual hand-paid Peach must offer Shangshi at committed use before healing.");
                        var use = branch.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last();
                        Require(use.Candidates[use.CandidateIndex].FrozenContext?.Facts?.CardActionHandCardCount == 1, "Frozen facts must count the actual hand payment.");
                        var decline = branch.PendingDecision!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip");
                        Require(branch.Submit(new AnswerPromptCommand(0, branch.PendingDecision.PromptId, decline.Id, branch.Revision)).Accepted, "Refill decline failed.");
                    }
                    else Advance(branch);
                }
                Require(branch.ResolutionStack.Count == 0 && branch.CreateSnapshot(0, true).Players[0].Hp == 1,
                    $"The original {replacement}/{activation} instruction must finish after two Peach recoveries: HP {branch.CreateSnapshot(0, true).Players[0].Hp}, frames {branch.ResolutionStack.Count}, top {branch.ResolutionStack.LastOrDefault()?.GetType().Name}, prompt {branch.PendingDecision?.Kind}.");
                if (replacement && activation == "lethal")
                    Require(!branch.Events.Any(e => e.Payload is DamageAppliedEvent) && branch.Events.Any(e => e.Payload is PlayerDyingEvent { KillerSeat: null }),
                        "Replacement dying must remain unattributed and bypass damage events.");
            }
            Require(SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(restored.CreateSnapshot(0, true)), "Dying entry and nested rescues must replay.");
        }
    }
    private static void ZeroPhysicalVirtualUseDoesNotRefill()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(virtualUse: true));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = "fixture:fame-damage", UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Virtual fixture failed.");
        for (var i = 0; i < 50 && game.PendingDecision?.SkillPrompt?.SkillId != "fixture:fame-driver"; i++) Advance(game);
        Require(game.PendingDecision?.Kind == DecisionKind.ProgramTrigger, "Virtual phase trigger missing.");
        Advance(game);
        Require(game.PendingDecision?.SkillPrompt?.SkillId == "classic:shangshi", "Virtual preparation must first offer loss refill.");
        var decline = game.PendingDecision!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip");
        Require(game.Submit(new AnswerPromptCommand(0, game.PendingDecision.PromptId, decline.Id, game.Revision)).Accepted, "Virtual preparation decline failed.");
        var eventStart = game.Events.Count;
        for (var i = 0; i < 100 && game.ResolutionStack.Count != 0; i++) Advance(game);
        Require(game.ResolutionStack.Count == 0 && game.CreateSnapshot(0, true).Players[0].Hand.Count == 0,
            "A handless wounded owner must not draw solely because its zero-physical virtual Slash was used.");
        var events = game.Events.Skip(eventStart).Select(e => e.Payload).ToArray();
        Require(events.OfType<CardActionAcceptedEvent>().Any(e => e.Action.ActorSeat == 0 && e.Action.PhysicalCards.Count == 0),
            "The negative case must execute an actual accepted zero-physical use.");
        Require(!events.OfType<ProgramBindingResolvedEvent>().Any(e => e.OwnerSeat == 0 && e.BindingId == "refill-on-cardUseCommitted" && e.Activated) &&
            !events.OfType<ProgramCardTriggerResolvedEvent>().Any(e => e.OwnerSeat == 0 && e.TriggerId == "refill-on-cardUseCommitted" && e.Activated),
            "Zero-physical use must not offer or resolve a committed-hand-loss refill.");
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(GameReplay.Restore(game.CreateCheckpoint(), registry).CreateSnapshot(0, true)),
            "Zero-physical rejection must preserve checkpoint replay.");
    }
    private static void Advance(GameEngine game)
    {
        var prompt = game.PendingDecision;
        var command = prompt is null || prompt.PlayerSeat != 0 ? (GameCommand)new AdvanceCommand(game.Revision) :
            new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First().Id, game.Revision);
        Require(game.Submit(command).Accepted, "Continuation failed.");
    }
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private sealed class Fixture(bool combat = false, string armor = "classic:silver-lion", bool replacement = true, bool virtualUse = false) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fame-damage-fixture", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            const string preparation = """
                "triggers":[{"id":"virtual","window":"playPhaseStarting","subject":"owner","optional":true,"effects":[{"op":"loseHp","target":"owner","amount":2},{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true}]}],"activations":[
                """;
            var rules = virtualUse ? Rules.Replace("\"activations\":[", preparation) : Rules;
            var catalog = SkillProgramCatalog.Load(rules, """{"schemaVersion":3,"skills":{"fixture:fame-driver":{"name":"Driver","description":"Test"}}}""");
            builder.AddSkill(new ContentSkillDefinition("fixture:fame-driver", "Driver", "Test") { Program = catalog.Programs["fixture:fame-driver"] });
            var generals = Enumerable.Range(0, 4).Select(i => $"fixture:fame-damage-{i}").ToArray();
            foreach (var id in generals) builder.AddGeneral(new ContentGeneralDefinition(id, "Test", "supporter", combat && id != generals[0] ? "standard:none" : "fixture:fame-driver",
                BaseHp: 4, AdditionalSkillIds: replacement ? ["classic:jueqing", "classic:shangshi"] : ["classic:shangshi"]));
            builder.AddDeck(new ContentDeckRecipe("fixture:fame-damage-deck", "Fixture", combat ? 6 : 0, combat ? 2 : 0, combat ? [new ContentDeckCardCount(armor, 30), new ContentDeckCardCount("standard:fire_slash", 20), new ContentDeckCardCount("standard:alcohol", 20), new ContentDeckCardCount("standard:peach", 30)] : [new ContentDeckCardCount("standard:peach", 100)]));
            builder.AddMode(new ContentModeDefinition("fixture:fame-damage", "Fixture", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 }, "fixture:fame-damage-deck",
                GeneralCandidateCount: combat ? 4 : 1, GeneralPoolIds: generals));
        }
    }
    private static readonly string Rules = $$"""
        {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:fame-driver","revision":1,"minimumRulesVersion":{{GameCheckpoint.CurrentRulesVersion}},"activations":[
        {"id":"supply","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":2}]},
        {"id":"lethal","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"damage","target":"owner","amount":6}]},
        {"id":"lethal-loss","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"loseHp","target":"owner","amount":6}]},
        {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,
        "effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
        {"id":"damage","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,
        "effects":[{"op":"damage","target":"owner","amount":1}]},
        {"id":"wound","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,
        "effects":[{"op":"loseHp","target":"owner","amount":2}]}]}]}
        """;
}
