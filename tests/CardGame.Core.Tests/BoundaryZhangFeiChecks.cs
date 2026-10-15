using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class BoundaryZhangFeiChecks
{
    private static readonly ContentRegistry Catalog = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage());
    public static void CancellationAndUseFreeze()
    {
        var (g, r) = Start();
        Require(!g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeat == 2), "Unlimited uses retain ordinary distance.");
        Driver(g, "draw"); Settle(g);
        Slash(g, [1]); Settle(g); Replay(g, r);
        Require(Reserved(g).Count() == 1 && !Consumed(g).Any(), "Real fully satisfied Dodge issues one reserve.");
        Slash(g, [1]);
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single();
        Require(use.NextSlashDamage == 1 && Consumed(g).Count() == 1, "Next accepted real Use freezes and consumes once before response.");
        Settle(g); Replay(g, r);
        Require(Reserved(g).Count() == 2, "Canceled boosted Use consumes its old reserve and creates a fresh one.");
        for (var i = 0; i < 3; i++) { Slash(g, [1]); Settle(g); }
        Require(g.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Any(e => e.TargetSeat == 1 && e.Amount == 2), "After the four real Dodges the fifth Slash deals the frozen bonus.");
        Replay(g, r);
    }

    public static void RecoveryReceiptAndLimited()
    {
        var (g, r) = Start("recovery", stopAtPreparation: true);
        var before = g.State.Players[0].HandCount;
        var frozen = GameCheckpointJson.Serialize(g.CreateCheckpoint()); var revision = g.Revision;
        var invalid = g.Submit(new AnswerPromptCommand(0, P(g)!.PromptId, new ChoiceId("foreign"), revision));
        Require(!invalid.Accepted && revision == g.Revision && frozen == GameCheckpointJson.Serialize(g.CreateCheckpoint()), "Invalid preparation input rejects atomically.");
        Activate(g); Reach(g, p => p.Kind == DecisionKind.ProgramTrigger && p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == "fixture:zf-child"));
        Require(g.State.Players[0].HandCount == before && g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "boundary:tishen").RecoveryReceipt?.Recovered == 2,
            "Real recovery receipt exists before its nested HP child; no premature reward."); Replay(g, r);
        Activate(g); Settle(g);
        Require(g.State.Players[0].Hp == g.State.Players[0].MaxHp - 1 && g.State.Players[0].HandCount == before + 4,
            "Nested Loss does not change the two recovered points; receipt draws two then normal draw two.");
        Require(g.Events.Select(e => e.Payload).OfType<RecoveryAppliedEvent>().Single().Amount == 2 &&
            g.Events.Select(e => e.Payload).OfType<SkillUsageConsumedEvent>().Any(e => e.SkillId == "boundary:tishen" && e.Scope == SkillUsageScope.Game), "First-turn wound may use the named limited quota."); Replay(g, r);
        var players = Players(g); var grant = players[0].SkillGrants.Grants.Single(x => x.SkillId == "boundary:tishen");
        players[0].SkillGrants.RemoveGrant(grant.GrantId); players[0].SkillGrants.Grant(grant with { GrantId = "fixture:renewed", SkillInstanceId = "fixture:renewed", SourceId = "fixture:renewed" });
        var runtime = (SkillRuntimeStateStore)typeof(GameEngine).GetField("_skillRuntimeState", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g)!;
        Require(runtime.GetUsage(0, "boundary:tishen", "named-group:limited-recovery", SkillUsageScope.Game) == 1, "Host lifecycle audit: regrant preserves named limited usage.");
        var (full, fr) = Start("full", stopAtPreparation: true); var hand = full.State.Players[0].HandCount;
        Activate(full); Settle(full);
        Require(!full.Events.Select(e => e.Payload).OfType<RecoveryAppliedEvent>().Any() && full.State.Players[0].HandCount == hand + 2,
            "Full HP zero receipt never gives free cards."); Replay(full, fr);
        var (lost, _) = Start("recovery", stopAtPreparation: true); hand = lost.State.Players[0].HandCount; Activate(lost);
        Reach(lost, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == "fixture:zf-child"));
        var tishen = Players(lost)[0].SkillGrants.Grants.Single(x => x.SkillId == "boundary:tishen"); Players(lost)[0].SkillGrants.RemoveGrant(tishen.GrantId);
        Activate(lost); Settle(lost);
        Require(lost.State.Players[0].HandCount == hand + 2, "Host source-lifetime audit: a lost recovery source cannot issue a tail draw after its HP child.");
    }

    private static IEnumerable<NextSlashDamageReservedEvent> Reserved(GameEngine g) => g.Events.Select(e => e.Payload).OfType<NextSlashDamageReservedEvent>();
    private static IEnumerable<NextSlashDamageConsumedEvent> Consumed(GameEngine g) => g.Events.Select(e => e.Payload).OfType<NextSlashDamageConsumedEvent>();
    private static CharacterState[] Players(GameEngine g) => ((IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(g)!).ToArray();
    private static void Slash(GameEngine g, int[] targets)
    {
        var a = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeat == targets[0]);
        Accept(g, new PlayCardCommand(0, a.CardId!.Value, targets, g.Revision, P(g)!.PromptId, a.PlayedCardKind) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    }
    private static (GameEngine, ContentRegistry) Start(string scenario = "normal", bool stopAtPreparation = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:classic-zf", UseInteractiveSetup = true, UseInteractiveDiscard = scenario != "extra", AdvanceAfterHumanCommands = false, MaxTurns = 6 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral); Accept(g, new SelectGeneralCommand(0, "fixture:zf", g.Revision, P(g)!.PromptId));
        Reach(g, p => p.Kind == DecisionKind.ProgramTrigger && p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == "boundary:tishen"));
        if (!stopAtPreparation) { var p = P(g)!; Accept(g, new AnswerPromptCommand(0, p.PromptId, p.Choices.Single(c => c.Parameters.GetValueOrDefault("program-action") == "skip").Id, g.Revision)); Settle(g); }
        return (g, r);
    }
    private static void Activate(GameEngine g) { var p = P(g)!; Accept(g, new AnswerPromptCommand(0, p.PromptId, p.Choices.Single(c => c.Parameters.GetValueOrDefault("program-action") == "activate").Id, g.Revision)); }
    private static PendingDecision? P(GameEngine g) => g.PendingDecision ?? Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s)).Select(s => s.PendingDecision).FirstOrDefault(p => p is not null);
    private static void Driver(GameEngine g, string id, int[]? targets = null) => Accept(g, new UseProgramSkillCommand(0, "fixture:zf-driver", id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Settle(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> goal) { for (var i = 0; i < 100; i++) { if (P(g) is { } p && goal(p)) return; Accept(g, new AdvanceOneStepCommand(g.Revision)); } throw new InvalidOperationException("Boundary not reached: " + P(g)?.Kind); }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected"); }
    private static void Replay(GameEngine g, ContentRegistry r) { var h = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(Enumerable.Range(0, 4).All(s => SnapshotJson.Serialize(g.CreateSnapshot(s)) == SnapshotJson.Serialize(h.CreateSnapshot(s))) && JsonSerializer.Serialize(g.ResolutionStack) == JsonSerializer.Serialize(h.ResolutionStack) && JsonSerializer.Serialize(g.Events) == JsonSerializer.Serialize(h.Events), "Four viewers, typed stack and issued facts restore exactly."); Require(g.CreateCardZoneDiagnostics().Select(c => c.CardId).Distinct().Count() == 100, "All physical card entities conserved."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-zf", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            typeof(StandardContentPackage).Assembly.GetType("CardGame.Content.Standard.BoundaryZhangFeiContent")!.GetMethod("Register", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [b]);
            b.AddSkill(Catalog.GetSkill("classic:wusheng")); if (scenario == "wushuang") b.AddSkill(Catalog.GetSkill("classic:wushuang"));
            if (scenario == "uncancelable") b.AddSkill(Catalog.GetSkill("boundary:zhaxiang"));
            var weapon = scenario == "armor" ? "standard:renwang_shield" : scenario is "axe" or "borrowed" ? "classic:stone-axe" : "classic:qinglong-crescent-blade";
            if (scenario is "axe" or "qinglong" or "borrowed") b.AddCard(Catalog.GetCard(weapon)); if (scenario == "borrowed") b.AddCard(Catalog.GetCard("classic:borrowed-sword"));
            var multi = scenario == "multi" ? ",\"modifiers\":[{\"id\":\"extra\",\"query\":\"cardTargetCount\",\"operation\":\"add\",\"value\":1,\"priority\":0,\"cardKinds\":[\"slash\"]}]" : "";
            var wound = scenario == "recovery" ? "[{\"id\":\"wound\",\"window\":\"turnStartBeforeNormalFlow\",\"subject\":\"owner\",\"optional\":false,\"priority\":100,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":2}]}]" : scenario == "uncancelable" ? "[{\"id\":\"wound\",\"window\":\"playPhaseStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]}]" : "[]";
            var driver = SkillProgramCatalog.Load("{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"fixture:zf-driver\",\"revision\":1" + multi + ",\"triggers\":" + wound + ",\"activations\":[{\"id\":\"draw\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"effects\":[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":12}]},{\"id\":\"equip\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":1,\"maxTargets\":1,\"targetKind\":\"anyLiving\",\"effects\":[{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"resultBind\":\"gear\"}]}]}]}", "{\"schemaVersion\":3,\"skills\":{\"fixture:zf-driver\":{\"name\":\"固定链\",\"description\":\"固定链\"}}}");
            b.AddSkill(new("fixture:zf-driver", "固定链", "固定链") { Program = driver.Programs["fixture:zf-driver"] });
            var actorTrigger = scenario is "actor" or "borrowed" ? "[{\"id\":\"replace\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"slash\"],\"optional\":true,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLegalCurrentCardTarget\"},{\"op\":\"replaceCurrentCardUseActor\",\"target\":\"selectedTarget\"}]}]" : "[]";
            var extras = SkillProgramCatalog.Load("{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"fixture:zf-extras\",\"revision\":1,\"triggers\":" + actorTrigger + ",\"activations\":[{\"id\":\"duel\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":2,\"maxTargets\":2,\"targetKind\":\"anyLiving\",\"effects\":[{\"op\":\"startVirtualDuel\",\"target\":\"owner\"}]},{\"id\":\"extra\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"effects\":[{\"op\":\"pendExtraTurn\",\"target\":\"owner\"}]}]}]}", "{\"schemaVersion\":3,\"skills\":{\"fixture:zf-extras\":{\"name\":\"实际分支\",\"description\":\"实际分支\"}}}");
            b.AddSkill(new("fixture:zf-extras", "实际分支", "实际分支") { Program = extras.Programs["fixture:zf-extras"] });
            if (scenario == "prevent")
            {
                var shield = SkillProgramCatalog.Load("{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"fixture:zf-prevent\",\"revision\":1,\"triggers\":[{\"id\":\"prevent\",\"window\":\"beforeDamageApplied\",\"subject\":\"damageTarget\",\"optional\":false,\"effects\":[{\"op\":\"preventCurrentDamage\",\"target\":\"owner\"}]}]}]}", "{\"schemaVersion\":3,\"skills\":{\"fixture:zf-prevent\":{\"name\":\"防止伤害\",\"description\":\"防止伤害\"}}}");
                b.AddSkill(new("fixture:zf-prevent", "防止伤害", "防止伤害") { Program = shield.Programs["fixture:zf-prevent"] });
            }
            if (scenario == "recovery")
            {
                var child = SkillProgramCatalog.Load("{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"fixture:zf-child\",\"revision\":1,\"triggers\":[{\"id\":\"child\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"optional\":true,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]}]}]}", "{\"schemaVersion\":3,\"skills\":{\"fixture:zf-child\":{\"name\":\"子窗口\",\"description\":\"子窗口\"}}}");
                b.AddSkill(new("fixture:zf-child", "子窗口", "子窗口") { Program = child.Programs["fixture:zf-child"] });
            }
            b.AddSkill(new("fixture:zf-marker", "固定", "固定") { SelectionWeights = new Dictionary<Role, double> { [Role.Lord] = -1000 } });
            b.AddGeneral(new("fixture:zf", "张飞", "supporter", "boundary:paoxiao", "shu", 4,
                new[] { "boundary:tishen", "classic:wusheng", "fixture:zf-driver", "fixture:zf-extras", "fixture:zf-marker" }.Concat(scenario == "recovery" ? ["fixture:zf-child"] : scenario == "wushuang" ? ["classic:wushuang"] : scenario == "uncancelable" ? ["boundary:zhaxiang"] : Array.Empty<string>()).ToArray()));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:zf-{i}", "其他" + i, "supporter", scenario == "played" ? "classic:wusheng" : scenario is "actor" or "borrowed" ? "boundary:paoxiao" : scenario == "prevent" ? "fixture:zf-prevent" : "standard:none", "wei", 4, scenario == "borrowed" ? ["classic:wusheng"] : []));
            b.AddDeck(new("fixture:zf-deck", "固定", scenario == "wushuang" ? 1 : 4, 2, []) { PhysicalCards = Enumerable.Range(0, 100).Select(i => new ContentDeckPhysicalCard(scenario is "axe" or "qinglong" or "borrowed" or "armor" && i < 20 ? weapon : scenario == "borrowed" && i < 40 ? "classic:borrowed-sword" : scenario is "armor" or "prevent" ? "standard:slash" : "standard:dodge", scenario == "armor" ? Suit.Spade : Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new("identity:classic-zf", "固定", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 }, "fixture:zf-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:zf", "fixture:zf-1", "fixture:zf-2", "fixture:zf-3"]));
        }
    }
}
