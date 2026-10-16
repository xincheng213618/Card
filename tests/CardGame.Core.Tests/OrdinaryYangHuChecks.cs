using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OrdinaryYangHuChecks
{
    private const string Huaiyuan = "ol:huaiyuan", Chongxin = "ol:chongxin", Dezhang = "ol:dezhang", Weishu = "ol:weishu";
    private const string Driver = "fixture:yang-hu-driver", Gain = "fixture:yang-hu-gain", Health = "fixture:yang-hu-health";
    private const string DiscardChild = "fixture:yang-hu-discard-child", Suppress = "fixture:yang-hu-suppress", Mode = "fixture:yang-hu-mode";
    private static readonly Lazy<ContentRegistry> Official = new(StandardContentRegistry.CreateWithClassicGenerals);
    private enum Scenario { Original, Nested, Awake, Death, Draw, EmptyDraw, Discard }

    public static void OriginalInitialEntitiesDepartOnceAndChongxinReallyRecasts()
    {
        var (g, registry) = Create();
        var initial = V(g, 0).Hand.Select(c => c.Id).ToArray();
        Require(E<OriginalHandEntitiesInitializedEvent>(g) is [{ OriginalCount: 4, RemainingCount: 4, StateId: "sui" }] &&
            !E<OriginalHandAwakeningMaximumPaidEvent>(g).Any(), "Only the four actual initial-deal entities initialize Sui; the first real turn does not awaken while they remain.");
        Tags(g, initial); g = Cold(g, registry);
        Use(g, "equip", [0], [initial[0]]);
        Benefit(g, 0, "hand-limit"); Play(g);
        Require(V(g, 0).Equipment.Select(c => c.Id).SequenceEqual([initial[0]]) &&
            E<OriginalHandEntityConsumedEvent>(g) is [{ RemainingCount: 3 }] &&
            E<OriginalHandBenefitStartedEvent>(g).Single().MovementSequence == g.CardMovements.Single(m => m.CardId == initial[0] &&
                m.From == CardLocation.Hand(0) && m.To == CardLocation.Equipment(0)).Sequence,
            "Real own-Hand to own-Equipment movement consumes that physical Sui once and awards its mandatory benefit.");
        Tags(g, initial.Skip(1).ToArray());
        Use(g, "return-equipment"); Reach(g, p => p.Choices.Any(c => c.Cards.SequenceEqual([initial[0]])));
        Answer(g, c => c.Cards.SequenceEqual([initial[0]])); Play(g);
        Require(V(g, 0).Hand.Any(c => c.Id == initial[0]), "The same real installed entity returns to its original owner's Hand through a native move.");
        Tags(g, initial.Skip(1).ToArray());
        Accept(g, new PlayCardCommand(0, initial[0], [], g.Revision, P(g)!.PromptId)); Play(g);
        Require(E<OriginalHandEntityConsumedEvent>(g).Length == 1 && E<OriginalHandBenefitStartedEvent>(g).Length == 1,
            "Returning and losing the same physical card cannot restore its initial mark or award a second benefit.");

        var partner = V(g, 1).Hand[0].Id;
        Accept(g, new UseProgramSkillCommand(0, Chongxin, "paired-hand-recast", [initial[1]], [1], g.Revision, P(g)!.PromptId));
        Reach(g, p => p.PlayerSeat == 1 && Has(p, "paired-hand-recast")); Private(g);
        Answer(g, c => c.Cards.SequenceEqual([partner])); Benefit(g, 0, "hand-limit"); Play(g);
        var pair = E<PairedHandRecastStartedEvent>(g).Single();
        Require(E<PairedHandRecastCompletedEvent>(g).Single(e => e.FrameId == pair.FrameId) is
                { OwnerPaid: true, PartnerPaid: true, OwnerDrawCount: 1, PartnerDrawCount: 1 } &&
            g.CardMovements.Count(m => m.CardId == initial[1] && m.Reason == CardMoveReasons.RecastDiscard) == 1 &&
            E<OriginalHandPermanentBonusGrantedEvent>(g).Count(e => e.RecipientSeat == 0 && e.Kind == OriginalHandBenefitKind.HandLimit) == 2 &&
            !g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Chongxin),
            "Formal Chongxin pays two real Hand recasts and draws once per payer; its owner's original entity independently awards the same repeatable hand-limit option and the real phase quota remains spent.");
        Tags(g, initial.Skip(2).ToArray());
        Use(g, "discard-two", cards: initial.Skip(2).ToArray());
        Benefit(g, 1, "attack-range"); Benefit(g, 1, "attack-range"); Play(g);
        var last = E<OriginalHandBenefitStartedEvent>(g).TakeLast(2).ToArray();
        Require(last.Length == 2 && last[0].BatchId == last[1].BatchId && last[0].MovementSequence != last[1].MovementSequence &&
            E<OriginalHandEntityConsumedEvent>(g).Select(e => e.RemainingCount).SequenceEqual([3, 2, 1, 0]) &&
            g.GetAttackRange(1) == 3 && E<OriginalHandBenefitCompletedEvent>(g).Length == 4,
            "One true two-card discard batch consumes each distinct original entity and grants two separately completed, repeatable range increases.");
        Tags(g, []); _ = Cold(g, registry);
    }

    private static void Benefit(GameEngine g, int seat, string option)
    {
        Reach(g, p => Entity(p, "benefit-target")); Answer(g, c => c.Targets.SequenceEqual([seat]));
        Reach(g, p => Entity(p, "benefit-option")); Answer(g, c => c.Parameters.GetValueOrDefault("option") == option);
    }
    private static bool Has(PendingDecision p, string action) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
    private static bool Entity(PendingDecision p, string action) => Has(p, "original-hand-entity") && p.Choices.Any(c => c.Parameters.GetValueOrDefault("entity-action") == action);
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand command) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(r.Accepted, r.Error?.Message ?? "A legal fixed Yang Hu command was rejected."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> select) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(select).Id, g.Revision)); }
    private static void Use(GameEngine g, string id, int[]? targets = null, int[]? cards = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> stop)
    {
        for (var step = 0; step < 180; step++)
        {
            var p = P(g); if (p is not null && stop(p)) return;
            if (p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) break;
            if (p is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 })
                Answer(g, c => c.Cards.Count == 0 && (c.Parameters.GetValueOrDefault("response") is "let-die" or "pass" || c.Parameters.GetValueOrDefault("action") == "pass"));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The real fixed Yang Hu boundary was not reached: " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack, Recent = g.Events.TakeLast(8).Select(e => e.Payload) }));
    }
    private static void Tags(GameEngine g, int[] ids)
    {
        Frozen(V(g, 0).OriginalHandEntities!);
        var state = V(g, 0).OriginalHandEntities!.Single(s => s.SkillId == Huaiyuan && s.StateId == "sui");
        Require(state.Name == "绥" && state.RemainingCount == ids.Length && state.CardIds is not null && state.CardIds.ToHashSet().SetEquals(ids),
            "The owner sees exactly its still-unconsumed original physical Sui identities, including no returned or newly drawn substitute.");
        Frozen(state.CardIds!);
        foreach (var viewer in new[] { 1, 2, 3 })
        {
            var other = g.CreateSnapshot(viewer).Players[0].OriginalHandEntities!.Single();
            Require(other.SkillId == Huaiyuan && other.StateId == "sui" && other.Name == "绥" && other.RemainingCount == ids.Length && other.CardIds is null,
                "Other prepared views receive only the public Sui count and name, never the owner's private initial entity list.");
        }
    }
    private static void Frozen<T>(IReadOnlyList<T> list) => Require(list is System.Collections.IList { IsReadOnly: true }, "The prepared nested collection is frozen.");
    private static void Private(GameEngine g)
    {
        var p = P(g)!; Require(p.IsPrivate, "The native private material or gain child remains private.");
        foreach (var viewer in Enumerable.Range(0, 4).Where(s => s != p.PlayerSeat)) Require(g.CreateSnapshot(viewer).PendingDecision is null &&
            g.CreateSnapshot(viewer).Players[p.PlayerSeat].Hand.Count == 0, "Foreign views expose neither the private pending prompt nor held Hand identities.");
        Frozen(p.Choices); Frozen(p.ValidCardIds); Frozen(p.ValidTargetSeats); Frozen(p.ValidContentIds);
        foreach (var c in p.Choices) { Frozen(c.Cards); Frozen(c.Targets); Frozen(c.ContentIds); Require(c.Parameters is System.Collections.IDictionary { IsReadOnly: true }, "Choice parameters are deeply frozen."); }
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements,
        Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(State(copy) == State(g), "Cold command replay preserves all four prepared views, durable original entities, frozen receipts, native children, scalar facts, card provenance and accepted commands."); return copy; }
    private static (GameEngine, ContentRegistry) Create(Scenario scenario = Scenario.Original)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = scenario == Scenario.Death ? Role.Renegade : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Require(P(g)!.ValidContentIds.Contains("fixture:yang-hu-owner"), "The fixed small mode genuinely publishes its intended owner under native role-weighted selection.");
        Accept(g, new SelectGeneralCommand(0, "fixture:yang-hu-owner", g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private sealed class Fixture(Scenario scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ordinary-yang-hu", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            foreach (var id in new[] { Huaiyuan, Chongxin, Dezhang, Weishu, "ol:sanchen" }) b.AddSkill(Official.Value.GetSkill(id));
            var rules = JsonNode.Parse($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"activations":[
                {"id":"equip","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"equipment"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"equipment"}]},
                {"id":"give","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget","amount":1}]},
                {"id":"give-all","minCards":4,"maxCards":4,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget","amount":4}]},
                {"id":"discard-two","minCards":2,"maxCards":2,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":2}]},
                {"id":"return-equipment","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","amount":1,"zones":["equipment"],"resultBind":"returned"},{"op":"moveBoundCards","target":"owner","sourceBind":"returned","destination":"ownerHand","awaitMovementTriggers":true}]},
                {"id":"collect","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelectedTargetHand","target":"selectedTarget"}]},
                {"id":"draw-three","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":3}]},
                {"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"pendExtraTurn","target":"owner"}]},
                {"id":"die","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":4}]}]},
              {"id":"{{{Gain}}}","revision":1,"triggers":[{"id":"native-benefit-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.ol:huaiyuan.original-hand-benefit.draw"],"optional":true,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]},{"op":"grantSkills","target":"owner","skillIds":["{{{Suppress}}}"]}]}]},
              {"id":"{{{Health}}}","revision":1,"triggers":[{"id":"real-max-hp-child","window":"afterHealthChanged","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
              {"id":"{{{DiscardChild}}}","revision":1,"triggers":[{"id":"real-weishu-discard-child","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.ol:weishu.outside-phase-discard"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:yang-hu-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """)!;
            var presentation = rules["skills"]!.AsArray().ToDictionary(n => n!["id"]!.GetValue<string>(), n =>
            {
                var id = n!["id"]!.GetValue<string>(); var p = new Dictionary<string, object> { ["name"] = id, ["description"] = "正式技能与真实原生边界夹具" };
                if (id is Gain or Health or DiscardChild) p["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" };
                return (object)p;
            });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentation }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "固定原生驱动") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new(Suppress, "实际HP5资格抑制", "不修改任何隐藏状态") { SuppressionRule = new(5) });
            b.AddSkill(new("fixture:yang-hu-owner-selection", "固定拥有者候选", "正式身份评分")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Renegade ? 10000d : -10000d) });
            b.AddSkill(new("fixture:yang-hu-peer-selection", "固定其他候选", "正式身份评分")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? 10000d : 100d) });
            var owner = new List<string> { Driver };
            if (scenario is Scenario.Draw or Scenario.EmptyDraw or Scenario.Discard) owner.Add(Weishu);
            else owner.AddRange([Huaiyuan, Chongxin, Dezhang]);
            if (scenario == Scenario.Nested) owner.Add(Gain);
            if (scenario == Scenario.Awake) owner.Add(Health);
            if (scenario == Scenario.Discard) owner.AddRange([Chongxin, "ol:sanchen"]);
            b.AddGeneral(new("fixture:yang-hu-owner", "羊祜真实机制拥有者", "supporter", "fixture:yang-hu-owner-selection", "jin", 4, owner));
            var peers = Enumerable.Range(1, 3).Select(s => $"fixture:yang-hu-peer-{s}").ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "固定原生其他角色", "supporter", "fixture:yang-hu-peer-selection", "qun", 4,
                scenario == Scenario.Discard ? ["fixture:yang-hu-quiet", DiscardChild] : ["fixture:yang-hu-quiet"]));
            b.AddDeck(new("fixture:yang-hu-deck", "同质小实体牌堆", 4, scenario == Scenario.Draw ? 2 : 0, [])
                { PhysicalCards = Enumerable.Range(0, scenario == Scenario.EmptyDraw ? 16 : 64).Select(i => new ContentDeckPhysicalCard("standard:crossbow", Suit.Club, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "小四人正式羊祜边界", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:yang-hu-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:yang-hu-owner", .. peers]));
        }
    }
}
