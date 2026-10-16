using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class SunRuOlChecks
{
    private const string Chi = "ol:chishi", Wei = "ol:weimian", Driver = "fixture:sr-driver", Quiet = "fixture:sr-quiet";
    private const string Watch = "fixture:sr-watch", Gain = "fixture:sr-gain", Hp = "fixture:sr-hp", Owner = "fixture:sr-owner", Mode = "identity:classic-sunru-ol-fixture";

    public static void ActualCurrentActorLastZoneLossOncePerTurnAndColdReturn()
    {
        var (g, registry) = Start("loss");
        for (var i = 0; i < 2; i++)
        { Use(g, "foreign-discard", [1]); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("source-zone") == "Hand") == true); Answer(g, _ => true); Play(g); }
        Need(V(g, 1).HandCount == 0 && !E<SunRuOlChishiAppliedEvent>(g).Any() && !E<ProgramBindingStartedEvent>(g).Any(e => e.SkillId == Chi),
            "Losing another actor's last hand entity during Sun Ru's actual turn never offers Chishi.");
        var lastDiscarded = V(g, 0).Hand.Last().Id;
        Use(g, "empty-hand", []); Reach(g, () => Optional(P(g), Chi));
        var boundary = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Last();
        Need(boundary.Batch.SunRuOlLastZoneLoss is { OwnerSeat: 0 } token && token.TurnNumber == g.State.TurnNumber &&
            boundary.Batch.SourceCounts.Any(c => c.Location == CardLocation.Hand(0) && c.CountBefore == 1 && c.CountAfter == 0) &&
            boundary.Batch.Movements is [var last] && last.CardId == lastDiscarded,
            "The real native atomic batch freezes the actual current-turn owner and positive-before/zero-after region loss.");
        g = Cold(g, registry); Activate(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Gain);
        Need(V(g, 0).HandCount == 2 && E<SunRuOlChishiAppliedEvent>(g).Count() == 1 &&
            E<TurnRuleModifierGrantedEvent>(g).Single(e => e.Modifier.Source.SkillId == Chi).Modifier is { AffectedSeat: 0, Amount: 2, Query: SkillRuleQuery.HandLimit },
            "An actual accepted Chishi draws two and issues one native current-turn +2 hand limit to the losing actor before its gain child.");
        g = Cold(g, registry); Continue(g); Play(g); Use(g, "empty-hand", []); Play(g);
        Need(V(g, 0).HandCount == 0 && E<SunRuOlChishiAppliedEvent>(g).Count() == 1 &&
            E<ProgramBindingStartedEvent>(g).Count(e => e.SkillId == Chi) == 1,
            "A second real last-zone loss in the same actual turn cannot activate again, including after cold child return.");
        var first = E<SunRuOlChishiAppliedEvent>(g).Single(); End(g); Play(g);
        Use(g, "draw", []); Play(g); Use(g, "empty-hand", []); Reach(g, () => Optional(P(g), Chi));
        Activate(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Gain); Continue(g); Play(g);
        Need(E<SunRuOlChishiAppliedEvent>(g).Count() == 2 && E<SunRuOlChishiAppliedEvent>(g).Last().Turn.TurnNumber > first.Turn.TurnNumber &&
            E<TurnCardUseEffectsExpiredEvent>(g).Any(), "The next actual game turn has a fresh quota and native current-turn grants expire normally.");
        _ = Cold(g, registry);
    }

    private static PlayerSnapshot V(GameEngine g, int s) => g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static bool Optional(PendingDecision? p, string id) => p?.SkillPrompt?.SkillId == id && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> choose) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(choose).Id, g.Revision)); }
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, IReadOnlyList<int> targets, IReadOnlyList<int>? cards = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets, g.Revision, P(g)!.PromptId));
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var i = 0; i < 400; i++)
        {
            if (done()) return;
            if (P(g) is { } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
            else if (P(g) is { PlayerSeat: 0 } human)
            {
                if (human.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else throw new InvalidOperationException("Unexpected Sun Ru OL boundary: " + JsonSerializer.Serialize(human));
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Sun Ru OL bounded native boundary was not reached: " + JsonSerializer.Serialize(P(g)));
    }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected actual Sun Ru OL command."); }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(cold), "All private player views, paid slot receipts, native children, events, physical entities and accepted commands restore identically."); return cold; }
    private static string Embedded(string s) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.ol-sun-ru.{s}")!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
    private static (GameEngine, ContentRegistry) Start(string scenario)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = scenario == "replacement" ? Role.Renegade : Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 18 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, r);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-sunru-ol", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            void Add(SkillProgramCatalog c) { foreach (var (id, p) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = p, ProgramPresentation = c.Presentations[id] }); }
            Add(SkillProgramCatalog.Load(Embedded("rules.json"), Embedded("presentation.json")));
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"empty-hand","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]},
                {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":1}]},
                {"id":"foreign-discard","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]},
                {"id":"equip","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-slot-child","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["equipment.slot-abolished"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{Gain}}","revision":1,"triggers":[{"id":"native-gain-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.sun-ru.ol.chishi.draw","skill-program.sun-ru.ol.weimian.draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}]}]},
              {"id":"{{Hp}}","revision":1,"triggers":[{"id":"native-heal-child","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:sr-whole","revision":1,"triggers":[{"id":"whole-equipment-area","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"abolishOwnerAreas","target":"owner","zones":["equipment"]}]}]},
              {"id":"fixture:sr-recovery","revision":1,"cardPolicies":[{"id":"real-lord-recovery","kind":"redirectOwnTurnFactionRecovery","factionId":"wei","ownerRole":"lord","value":1,"providerDrawCount":1}]}]}
            """;
            var ids = new[] { Driver, Quiet, Watch, Gain, Hp, "fixture:sr-whole", "fixture:sr-recovery" };
            var labels = ids.ToDictionary(id => id, id => new { name = id, description = "孙茹真实命令与原生区域/子窗口", optionLabels = id is Watch or Gain or Hp ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() });
            Add(SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels })));
            var humanRole = scenario == "replacement" ? Role.Renegade : Role.Lord;
            b.AddSkill(new("fixture:sr-owner-pick", "固定孙茹", "公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == humanRole ? 100000d : -100000d) });
            b.AddSkill(new("fixture:sr-peer-pick", "固定其他", "公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == humanRole ? -100000d : 100000d) });
            var ownerSkills = new List<string> { Wei, Driver, Watch, Gain, Hp };
            if (scenario != "replacement") ownerSkills.Add("fixture:sr-recovery");
            if (scenario != "cross") ownerSkills.Add(Chi);
            b.AddGeneral(new(Owner, "孙茹真实规则", "ol-sun-ru", "fixture:sr-owner-pick", "wei", 6, ownerSkills) { InitialHp = 2 });
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:sr-peer-{i}").ToArray();
            foreach (var id in peers)
            {
                var peerSkills = new List<string> { Quiet, Gain, Hp };
                if (scenario == "whole") peerSkills.Add("fixture:sr-whole");
                if (scenario == "cross" && id == peers[0]) peerSkills.Add(Chi);
                if (scenario == "replacement") peerSkills.Add("fixture:sr-recovery");
                b.AddGeneral(new(id, "固定其他", "supporter", "fixture:sr-peer-pick", "wei", 6, peerSkills) { InitialHp = scenario == "replacement" ? 1 : scenario == "silver" ? 2 : 6 });
            }
            b.AddCard(new("fixture:sr-lion", "白银狮子", "装备牌", "实际移除回复", CardKind.SilverLion));
            b.AddDeck(new("fixture:sr-deck", "固定实际实体", 2, 0, []) { PhysicalCards = Enumerable.Range(0, 120).Select(i => new ContentDeckPhysicalCard(scenario == "silver" ? "fixture:sr-lion" : "standard:dodge", Suit.Club, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "孙茹OL真实命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:sr-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
