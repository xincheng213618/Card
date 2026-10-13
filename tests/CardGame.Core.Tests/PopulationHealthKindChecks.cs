using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class PopulationHealthKindChecks
{
    private const string Gongao = "classic:gongao", Juyi = "classic:juyi", Weizhong = "classic:weizhong";
    private const string Driver = "fixture:population-health-driver", Gain = "fixture:population-health-gain";
    private const string Owner = "fixture:population-health-owner", Mode = "identity:classic-population-health";

    public static void TypedConditionsRejectWrongContextsAndFreezeExactKinds()
    {
        var rules = Embedded("classic-zhuge-dan", "rules.json");
        var presentation = Embedded("classic-zhuge-dan", "presentation.json");
        var catalog = SkillProgramCatalog.Load(rules, presentation);
        var condition = catalog.Programs[Weizhong].Triggers.Single().Condition;
        foreach (var kind in Enum.GetValues<HpChangeKind>())
            Require(condition.Evaluate(new(0, 4, true) { HealthChangeKind = kind }) == (kind == HpChangeKind.MaximumHp),
                "The typed condition matches only the actual MaximumHp occurrence, including loss, recovery and damage exclusions.");
        Require(!condition.Evaluate(new(0, 4, true)), "An absent health occurrence cannot match a maximum-HP trigger.");
        var omitted = JsonSerializer.Serialize(new SkillProgramTriggerFacts(0, 4, true));
        Require(!omitted.Contains("HealthChangeKind", StringComparison.Ordinal) && !omitted.Contains("LivingPlayerCount", StringComparison.Ordinal),
            "Optional population and health facts leave old checkpoint projections unchanged when absent.");
        foreach (var invalid in new[]
        {
            rules.Replace("\"afterHealthChanged\"", "\"turnEnding\"", StringComparison.Ordinal),
            rules.Replace("\"maximumHp\"", "\"invalid-kind\"", StringComparison.Ordinal),
            rules.Replace("\"hpChangeKind\": \"maximumHp\"", "\"hpChangeKind\": 601", StringComparison.Ordinal),
            rules.Replace(", \"hpChangeKind\": \"maximumHp\"", "", StringComparison.Ordinal),
            rules.Replace("\"healthChangeKindIs\"", "\"always\"", StringComparison.Ordinal),
            rules.Replace("\"turnStartBeforeNormalFlow\"", "\"turnEnding\"", StringComparison.Ordinal)
        })
        {
            try { _ = SkillProgramCatalog.Load(invalid, presentation); }
            catch (InvalidOperationException) { continue; }
            throw new InvalidOperationException("The loader accepted an unsupported health-kind or population context.");
        }
        var population = catalog.Programs[Juyi].Triggers.Single().Condition;
        var frozen = new SkillProgramTriggerFacts(0, 3, true, CurrentMaxHp: 5) { LivingPlayerCount = 4 };
        Require(population.Evaluate(frozen) && !population.Evaluate(frozen with { LivingPlayerCount = 5 }) &&
            !population.Evaluate(frozen with { CurrentHp = 5 }), "Classic awakening requires wounded HP and strictly greater frozen population.");
    }

    public static void ActualMaximumHpChangesDrawOnceAndColdReplayPaidChildren()
    {
        var (g, registry) = Start(initialWeizhong: true, awakening: false, baseHp: 4, wounded: true);
        foreach (var activation in new[] { "grow", "shrink", "clamp" })
        {
            var before = E<ProgramBindingStartedEvent>(g).Count(e => e.SkillId == Weizhong);
            Use(g, activation);
            Reach(g, p => p.SkillPrompt?.SkillId == Gain);
            var hp = g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
            var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Driver);
            Require(hp.Change.Kind == HpChangeKind.MaximumHp && hp.Change.ParentFrameId == root.Id &&
                hp.Continuation == PostEventContinuation.Program && hp.Contexts.Single().Facts?.HealthChangeKind == HpChangeKind.MaximumHp &&
                E<ProgramBindingStartedEvent>(g).Count(e => e.SkillId == Weizhong) == before + 1,
                "Each nonzero maximum change pauses one typed health child under its exact actual command, even when its delta exceeds one.");
            RejectWrongActor(g); Private(g); g = Cold(g, registry);
            Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Driver); g = Cold(g, registry);
            Continue(g); Play(g);
            Require(E<ProgramBindingStartedEvent>(g).Count(e => e.SkillId == Weizhong) == before + 1,
                "Cold returns cannot repeat the maximum mutation, observer draw or the paid parent instruction.");
        }
        Require(V(g).MaxHp == 1, "The real clamp reaches the existing minimum maximum HP.");
        var draws = Draws(g, Weizhong);
        Use(g, "clamp"); Reach(g, p => p.SkillPrompt?.SkillId == Driver); Continue(g); Play(g);
        Require(Draws(g, Weizhong) == draws && E<MaximumHpChangedEvent>(g).Last().Delta == 0,
            "A clamped zero-delta event cannot trigger the observer.");

        var (health, healthRegistry) = Start(initialWeizhong: true, awakening: false, baseHp: 4, wounded: true);
        foreach (var activation in new[] { "hurt", "heal", "damage" })
        {
            var drawBefore = Draws(health, Weizhong);
            Use(health, activation, activation == "damage" ? [0] : []);
            Reach(health, p => p.SkillPrompt?.SkillId == Driver); health = Cold(health, healthRegistry);
            Continue(health); Play(health);
            Require(Draws(health, Weizhong) == drawBefore, "Actual HP loss, recovery and damage never masquerade as maximum-HP changes.");
        }
        Use(health, "lose-weizhong"); Play(health);
        var remaining = Draws(health, Weizhong); Use(health, "grow"); Reach(health, p => p.SkillPrompt?.SkillId == Driver); Continue(health); Play(health);
        Require(Draws(health, Weizhong) == remaining && !V(health).Skills!.Any(s => s.ContentId == Weizhong),
            "A removed actual observer source cannot open later maximum-HP choices.");
        _ = Cold(health, healthRegistry);
    }

    public static void PreparationPopulationAwakeningGrantsAfterDrawAndKeepsGameUsage()
    {
        foreach (var scenario in new[] { "qualified", "equal-population", "healthy" })
        {
            var (g, registry) = Start(initialWeizhong: false, awakening: true,
                baseHp: scenario == "equal-population" ? 3 : 4, wounded: scenario != "healthy", awaitFirstPlay: false);
            if (scenario != "qualified")
            {
                Play(g);
                Require(!E<ProgramBindingStartedEvent>(g).Any(e => e.SkillId == Juyi),
                    "Equality or full HP preserves the unspent preparation awakening.");
                Use(g, scenario == "healthy" ? "hurt" : "grow"); Reach(g, p => p.SkillPrompt?.SkillId == Driver); Continue(g); Play(g);
                NextTurn(g);
            }
            Reach(g, p => p.SkillPrompt?.SkillId == Gain);
            var awake = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Juyi);
            var original = awake.Id;
            Require(awake.WindowContext?.Facts is { LivingPlayerCount: 4 } facts && facts.CurrentMaxHp > 4 && facts.CurrentHp < facts.CurrentMaxHp &&
                V(g).HandCount == V(g).MaxHp && !V(g).Skills!.Any(s => s.ContentId is Weizhong or "classic:benghuai"),
                "The classic draw-to-maximum pays actual entities before either derived grant, under frozen strict preparation facts.");
            Private(g); RejectWrongActor(g); g = Cold(g, registry);
            for (var child = 0; child < 12; child++)
            {
                Continue(g);
                Reach(g, p => p.SkillPrompt?.SkillId == Gain || p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
                if (P(g)!.Kind == DecisionKind.PlayCard) break;
                g = Cold(g, registry);
            }
            Require(P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }, "All finite paid-draw children return to the native Play boundary.");
            Require(V(g).Skills!.Count(s => s.ContentId == Weizhong) == 1 && V(g).Skills!.Count(s => s.ContentId == "classic:benghuai") == 1 &&
                E<SkillAwakenedEvent>(g).Count(e => e.SkillId == Juyi) == 1 &&
                E<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == original && e.SkillId == Juyi && e.Completed) == 1,
                "One native return grants both actual derived skills once after completing the paid draw.");
            var drawBefore = Draws(g, Juyi); NextTurn(g); Play(g);
            Require(E<SkillAwakenedEvent>(g).Count(e => e.SkillId == Juyi) == 1 && Draws(g, Juyi) == drawBefore,
                "The same qualifying next preparation cannot reset the actual game-once awakening.");
            Use(g, "lose-juyi"); Play(g);
            Require(V(g).Skills!.All(s => s.ContentId != Juyi) && V(g).Skills!.Any(s => s.ContentId == Weizhong) &&
                V(g).Skills!.Any(s => s.ContentId == "classic:benghuai"), "Already acquired derived skills persist after their completed awakening source is removed.");
            _ = Cold(g, registry);
        }
    }

    public static void ActualOtherDeathGrowthResumesHealthChildBeforeRecovery()
    {
        var (g, registry) = Start(initialWeizhong: true, awakening: false, baseHp: 4, wounded: true);
        var hpBefore = V(g).Hp; var maxBefore = V(g).MaxHp;
        Use(g, "kill", [1]); Reach(g, p => p.SkillPrompt?.SkillId == Gain);
        var gongao = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Gongao);
        var health = g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
        Require(!g.CreateSnapshot(0).Players[1].IsAlive && V(g).MaxHp == maxBefore + 1 && V(g).Hp == hpBefore &&
            gongao.InstructionIndex == 1 && gongao.WindowContext?.TargetSeat == 1 && health.Change.ParentFrameId == gongao.Id &&
            health.Change.Kind == HpChangeKind.MaximumHp,
            "A real other death grows maximum once, then its health child suspends before the separate recovery instruction.");
        g = Cold(g, registry); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Driver); Continue(g); Play(g);
        Require(V(g).Hp == hpBefore + 1 && E<MaximumHpChangedEvent>(g).Count(e => e.SkillId == Gongao) == 1 &&
            E<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == Gongao && e.Completed) == 1 && Draws(g, Weizhong) == 1,
            "The same death, maximum change, observer draw and recovery each complete exactly once through cold typed returns.");
        Use(g, "lose-gongao"); Play(g); var after = V(g).MaxHp;
        Use(g, "kill", [2]); Reach(g, p => p.SkillPrompt?.SkillId == Driver); Continue(g); Play(g);
        Require(!g.CreateSnapshot(0).Players[2].IsAlive && V(g).MaxHp == after &&
            E<MaximumHpChangedEvent>(g).Count(e => e.SkillId == Gongao) == 1, "Source loss prevents later real deaths from issuing another growth.");
        _ = Cold(g, registry);
    }

    private static string Embedded(string bundle, string suffix)
    {
        using var stream = typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.{bundle}.{suffix}")
            ?? throw new InvalidOperationException("The actual shared-rule bundle is missing.");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static PlayerSnapshot V(GameEngine g) => g.CreateSnapshot(0).Players[0];
    private static int Draws(GameEngine g, string skill) => g.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == $"skill-program.{skill}.Draw");
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Accept(GameEngine g, GameCommand command)
    {
        var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "The actual shared health command failed.");
    }
    private static void Use(GameEngine g, string activation, IReadOnlyList<int>? targets = null) => Accept(g,
        new UseProgramSkillCommand(0, Driver, activation, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Continue(GameEngine g)
    {
        var p = P(g)!; var c = p.Choices.First(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c.Id, g.Revision));
    }
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void NextTurn(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Reach(GameEngine g, Func<PendingDecision, bool> done)
    {
        for (var step = 0; step < 140; step++)
        {
            var p = P(g); if (p is not null && done(p)) return;
            if (p is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 })
            {
                var pass = p.Choices.Single(c => c.Parameters.GetValueOrDefault("response") == "pass");
                Accept(g, new AnswerPromptCommand(0, p.PromptId, pass.Id, g.Revision)); continue;
            }
            if (p is { PlayerSeat: 0 }) throw new InvalidOperationException($"Unexpected shared health boundary {p.Kind}: {p.Prompt}");
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The bounded shared health fixture did not reach its real child.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())).ToArray(), g.CardMovements,
        Events = g.Events.Select(e => $"{e.Sequence}|{JsonSerializer.Serialize(e.Payload, e.Payload.GetType())}").ToArray(),
        Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics()
    });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(State(g) == State(restored), "Accepted-prefix cold recovery preserves every actual frame, frozen kind, viewer projection and physical entity.");
        return restored;
    }
    private static void RejectWrongActor(GameEngine g)
    {
        var p = P(g)!; var before = State(g);
        Require(!g.Submit(new AnswerPromptCommand(1, p.PromptId, p.Choices.First().Id, g.Revision)).Accepted && State(g) == before,
            "A wrong actor cannot consume the actual owner choice or alter its accepted prefix.");
    }
    private static void Private(GameEngine g) => Require(P(g) is { IsPrivate: true, PlayerSeat: 0 } &&
        Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null && g.CreateSnapshot(s).Players[0].Hand.Count == 0),
        "The private gain continuation exposes no owner hand identities or decision to another viewer.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Start(bool initialWeizhong, bool awakening, int baseHp, bool wounded, bool awaitFirstPlay = true)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(initialWeizhong, awakening, baseHp, wounded));
        var g = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12
        }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId));
        if (awaitFirstPlay) Play(g); return (g, registry);
    }

    private sealed class Fixture(bool initialWeizhong, bool awakening, int baseHp, bool wounded) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("population-health-kind-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var actual = SkillProgramCatalog.Load(Embedded("classic-zhuge-dan", "rules.json"), Embedded("classic-zhuge-dan", "presentation.json"));
            foreach (var (id, program) in actual.Programs) b.AddSkill(new(id, actual.Presentations[id].Name, actual.Presentations[id].Description)
                { Program = program, ProgramPresentation = actual.Presentations[id], Tags = id == Juyi ? SkillTag.Awakening : SkillTag.Locked });
            var dong = SkillProgramCatalog.Load(Embedded("classic-dong-zhuo", "rules.json"), Embedded("classic-dong-zhuo", "presentation.json"));
            b.AddSkill(new("classic:benghuai", dong.Presentations["classic:benghuai"].Name, dong.Presentations["classic:benghuai"].Description)
                { Program = dong.Programs["classic:benghuai"], ProgramPresentation = dong.Presentations["classic:benghuai"], Tags = SkillTag.Locked });
            var rules = JsonNode.Parse($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"activations":[]},
              {"id":"{{{Gain}}}","revision":1,"triggers":[{"id":"paid-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.{{{Weizhong}}}.Draw","skill-program.{{{Juyi}}}.Draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-tail","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:population-health-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """)!;
            var acts = rules["skills"]![0]!["activations"]!.AsArray();
            foreach (var (id, op, amount, targets) in new[]
            {
                ("grow", "changeMaximumHp", 2, 0), ("shrink", "changeMaximumHp", -2, 0), ("clamp", "changeMaximumHp", -20, 0),
                ("hurt", "loseHp", 1, 0), ("heal", "recover", 1, 0), ("damage", "damage", 1, 1), ("kill", "damage", 20, 1)
            })
                acts.Add(JsonNode.Parse($$"""{"id":"{{id}}","minCards":0,"maxCards":0,"minTargets":{{targets}},"maxTargets":{{targets}},"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"{{op}}","target":"{{(targets == 0 ? "owner" : "selectedTarget")}}","amount":{{amount}}},{"op":"chooseOption","target":"owner","resultBind":"driver-tail","options":[{"id":"continue"}]}]}"""));
            foreach (var (id, skill) in new[] { ("lose-weizhong", Weizhong), ("lose-juyi", Juyi), ("lose-gongao", Gongao) })
                acts.Add(JsonNode.Parse($$"""{"id":"{{id}}","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{skill}}"],"sourceBind":"standard:none"}]}"""));
            var skills = new[] { Driver, Gain, "fixture:population-health-quiet" }.ToDictionary(id => id, id =>
            {
                var label = new Dictionary<string, object> { ["name"] = id, ["description"] = "真实公共人口与体力事件" };
                if (id != "fixture:population-health-quiet") label["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" };
                return label;
            });
            var fixture = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills }));
            foreach (var (id, program) in fixture.Programs) b.AddSkill(new(id, id, "真实公共人口与体力事件") { Program = program, ProgramPresentation = fixture.Presentations[id] });
            var secondary = new List<string> { Driver, Gain };
            if (initialWeizhong) secondary.Add(Weizhong); if (awakening) secondary.Add(Juyi);
            b.AddGeneral(new(Owner, "实际经典能力拥有者", "supporter", Gongao, "wei", baseHp, secondary, GeneralGender.Male)
                { InitialHp = wounded ? 1 : null });
            const string peerSkill = "fixture:population-health-peer";
            b.AddSkill(new(peerSkill, "固定其他角色", "固定选将")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Lord ? -10000d : 10000d) });
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:population-health-peer-{i}").ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "固定其他角色", "supporter", peerSkill, "shu", 4, ["fixture:population-health-quiet"]));
            b.AddDeck(new("fixture:population-health-deck", "固定无攻击实体牌堆", 4, 0, [])
                { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "真实经典准备与死亡", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:population-health-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
