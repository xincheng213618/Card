using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OrdinaryZhouChuChecks
{
    private const string Shanduan = "ol:shanduan", Yilie = "ol:yilie", Driver = "fixture:zhou-chu-driver";
    private const string Completed = "fixture:zhou-chu-completed", Foreign = "fixture:zhou-chu-foreign";
    private const string Suppress = "fixture:zhou-chu-suppress", Mode = "identity:classic-zhou-chu-fixture";
    private enum Scenario { Allocation, Boost, Pair, Dodge, Provision, Dying, Forced, ForcedFire, Colorless, Mixed, Grain }

    public static void PhaseDefaultsRemainSeparateFromNormalModifiersAndRepeatedPlay()
    {
        var (g, registry) = Create(Scenario.Allocation, stopAtAllocation: true);
        var initial = V(g, 0).HandCount; var turn = g.State.TurnNumber;
        Require(Stat(P(g)!) == TurnDefaultStatKind.DrawCount && E<TurnDefaultStatAssignedEvent>(g).Length == 0,
            "The real Draw-start candidate offers only Draw's default before any normal draw is paid.");
        var frame = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.TurnDefaultStatAllocation is not null);
        Require(frame.TurnDefaultStatAllocation!.Slots.SequenceEqual([1, 2, 3, 4]) &&
            frame.TurnDefaultStatAllocation.Origin.ParentFrameId == frame.WindowContext!.ParentFrameId &&
            frame.TurnDefaultStatAllocation.Origin.Window == SkillProgramTriggerWindow.DrawPhaseStarting,
            "The owning allocation receipt freezes the four stable slots and its exact native phase parent.");
        Frozen(frame.TurnDefaultStatAllocation.Slots); Private(g); g = Cold(g, registry); Allocate(g, 2);
        Reach(g, p => IsAllocation(p) && Stat(p) == TurnDefaultStatKind.AttackRange);
        Require(E<TurnDefaultStatAssignedEvent>(g) is [{ Stat: TurnDefaultStatKind.DrawCount, Value: 3, SlotIndex: 2, UsedSlotMask: 4 }] &&
            V(g, 0).HandCount == initial + 4, "Assigned Draw3 replaces the zero mode default, then the ordinary Draw+1 modifier still applies to the real draw.");
        Allocate(g, 0); Reach(g, p => IsAllocation(p) && Stat(p) == TurnDefaultStatKind.SlashLimit);
        Require(P(g)!.Choices.Select(c => c.Parameters["slot-index"]).SequenceEqual(["1", "3"]),
            "Play's second property receives only the two unused original slots, rather than a fresh four-number pool.");
        Allocate(g, 3); Play(g);
        Require(g.GetAttackRange(0) == 2 && E<TurnDefaultStatAssignedEvent>(g).Select(e => e.Stat).SequenceEqual(
            [TurnDefaultStatKind.DrawCount, TurnDefaultStatKind.AttackRange, TurnDefaultStatKind.SlashLimit]),
            "The assigned native range1 retains the separate range+1 contribution and HandLimit remains unassigned until Discard.");
        var weapon = V(g, 0).Hand[0].Id;
        Accept(g, new PlayCardCommand(0, weapon, [], g.Revision, P(g)!.PromptId)); Play(g);
        Require(V(g, 0).Equipment.Single().Id == weapon && g.GetAttackRange(0) == 4,
            "A real Qinglong weapon keeps its native range3 above assigned range1, with range+1 applied afterward.");
        for (var n = 0; n < 5; n++) { ConvertDriver(g, "slash", [1]); Play(g); }
        Require(V(g, 0).HandCount > 0 && !g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash) &&
            E<CardActionAcceptedEvent>(g).Count(e => e.Action.ActorSeat == 0 && e.Action.EffectiveKind == CardKind.Slash) == 5,
            "Assigned Slash4 plus the ordinary +1 permits five genuine paid uses and blocks a sixth while material remains.");
        End(g); Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        Require(g.State.TurnNumber == turn && E<TurnDefaultStatAssignedEvent>(g).Length == 3 &&
            g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "A genuine inserted Play phase preserves the same actual-turn default assignments while retaining the native fresh-Play Slash counter.");
        End(g); Reach(g, p => IsAllocation(p) && Stat(p) == TurnDefaultStatKind.HandLimit);
        Require(P(g)!.Choices is [var remaining] && remaining.Parameters["slot-index"] == "1" && remaining.Parameters["value"] == "2",
            "Discard receives exactly the final unused slot2 after both actual Play phases.");
        g = Cold(g, registry); Allocate(g, 1); Reach(g, p => p is { Kind: DecisionKind.DiscardCards, PlayerSeat: 0 });
        Require(P(g)!.RequiredCardCount == V(g, 0).HandCount - 3 &&
            E<TurnDefaultStatAssignedEvent>(g).Last() is { Stat: TurnDefaultStatKind.HandLimit, Value: 2, UsedSlotMask: 15 },
            "Assigned HandLimit2 replaces HP before the ordinary hand-limit+1 modifier, and the native discard prompt uses that exact result.");
        _ = Cold(g, registry);
    }
    private static bool IsAllocation(PendingDecision p) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "turn-default-stat");
    private static TurnDefaultStatKind Stat(PendingDecision p) => Enum.Parse<TurnDefaultStatKind>(p.Choices[0].Parameters["stat-kind"]);
    private static void Allocate(GameEngine g, int slot) => Answer(g, c => c.Parameters.GetValueOrDefault("slot-index") == slot.ToString());
    private static bool IsContinue(PendingDecision p) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand command) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(r.Accepted, r.Error?.Message ?? "A real Zhou Chu fixture command was rejected."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> select) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(select).Id, g.Revision)); }
    private static void SubmitPlay(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind) { ConversionSource = a.ConversionSource });
    private static void ConvertDriver(GameEngine g, string binding, int[] targets)
    {
        if (binding == "arrows")
        {
            var action = g.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.UseProgramSkill &&
                a.ProgramSkillId == Driver && a.ProgramActivationId == "arrows");
            var costs = V(g, 0).Hand.Where(c => action.SelectableCardIds.Contains(c.Id))
                .GroupBy(c => c.Suit).First(group => group.Count() >= 2).Take(2).Select(c => c.Id).ToArray();
            Accept(g, new UseProgramSkillCommand(0, Driver, "arrows", costs, targets, g.Revision, P(g)!.PromptId));
        }
        else if (binding == "barbarian")
        {
            var action = g.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == Driver &&
                a.ConversionSource.BindingId == binding && a.Kind == LegalActionKind.BarbarianAssault &&
                a.PlayedCardKind == CardKind.BarbarianAssault);
            Require(targets.Length == 0 && action.TargetSeats.SequenceEqual(g.CreateSnapshot(0).Players
                .Where(p => p.IsAlive && p.Seat != 0).Select(p => p.Seat)),
                "The converted native group trick publishes all living other targets and submits that exact target list.");
            SubmitPlay(g, action);
        }
        else SubmitPlay(g, g.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == Driver && a.ConversionSource.BindingId == binding && a.TargetSeats.SequenceEqual(targets)));
    }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> stop)
    {
        for (var n = 0; n < 180; n++) { var p = P(g); if (p is not null && stop(p)) return; Step(g); }
        throw new InvalidOperationException("The fixed Zhou Chu native boundary was not reached: " + State(g));
    }
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: not 0 }) Accept(g, new AdvanceOneStepCommand(g.Revision));
        else if (p is not null && IsAllocation(p)) Answer(g, c => true);
        else if (p?.SkillPrompt?.SkillId == "fixture:zhou-chu-extra-play" && p.Choices.Any(c => c.Targets.SequenceEqual([0]))) Answer(g, c => c.Targets.SequenceEqual([0]));
        else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { Kind: DecisionKind.DiscardCards, PlayerSeat: 0 }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification }) Answer(g, c => c.Cards.Count == 0 && !c.Parameters.Values.Contains(Yilie));
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Cards.Count == 0 && (c.Parameters.GetValueOrDefault("response") is "pass" or "let-die" || c.Parameters.GetValueOrDefault("action") == "pass"));
        else if (p is not null && IsContinue(p)) Continue(g);
        else if (p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) End(g);
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Frozen<T>(IReadOnlyList<T> items) => Require(items is System.Collections.IList { IsReadOnly: true }, "Prepared nested collections are read-only.");
    private static void Private(GameEngine g)
    {
        var p = P(g)!; Require(p.IsPrivate, "The native owning choice remains private.");
        foreach (var viewer in Enumerable.Range(0, 4).Where(s => s != p.PlayerSeat)) Require(g.CreateSnapshot(viewer).PendingDecision is null &&
            g.CreateSnapshot(viewer).Players[p.PlayerSeat].Hand.Count == 0, "Other views receive neither the private prompt nor foreign Hand entities.");
        Frozen(p.Choices); Frozen(p.ValidCardIds); Frozen(p.ValidTargetSeats); Frozen(p.ValidContentIds);
        foreach (var c in p.Choices) { Frozen(c.Cards); Frozen(c.Targets); Frozen(c.ContentIds); Require(c.Parameters is System.Collections.IDictionary { IsReadOnly: true }, "Nested choice parameters are frozen."); }
        var before = State(g); Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && State(g) == before,
            "An unpublished choice cannot allocate a slot, spend a name or pay any material.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements,
        Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(State(copy) == State(g), "Cold replay preserves all four prepared views, owning receipts, exact native parents, stable slots, actual-round names, events and physical provenance."); return copy; }

    private static (GameEngine, ContentRegistry) Create(Scenario scenario, bool stopAtAllocation = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, "fixture:zhou-chu-owner", g.Revision, P(g)!.PromptId));
        if (stopAtAllocation) Reach(g, IsAllocation); else Play(g);
        Require(V(g, 1).GeneralId == "fixture:zhou-chu-peer-1", "Native selection weights place the fixed first foreign actor at seat1 without a seed search.");
        return (g, registry);
    }
    private sealed class Fixture(Scenario scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ordinary-zhou-chu", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rr = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-zhou-chu.rules.json")!);
            using var pp = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-zhou-chu.presentation.json")!);
            var official = SkillProgramCatalog.Load(rr.ReadToEnd(), pp.ReadToEnd());
            foreach (var (id, program) in official.Programs) b.AddSkill(new(id, official.Presentations[id].Name, official.Presentations[id].Description)
                { Program = program, ProgramPresentation = official.Presentations[id] });
            var rules = JsonNode.Parse("""
            {"skills":[
              {"id":"fixture:zhou-chu-driver","revision":1,"viewAs":[
                {"id":"slash","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"useOnly":true},
                {"id":"arrows","inputKinds":[],"inputSuits":[],"inputCount":2,"sourceZones":["hand"],"sameSuit":true,"outputKind":"arrowBarrage","forPlay":true,"forResponse":false},
                {"id":"barbarian","inputKinds":[],"inputSuits":[],"sourceZones":["hand"],"outputKind":"barbarianAssault","forPlay":true,"forResponse":false,"singleCardTrickUse":true}],
                "activations":[
                  {"id":"arrows","minCards":2,"maxCards":2,"sourceZones":["hand"],"selectedCardsSameSuit":true,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"owner","sourceBind":"arrows","outputKind":"arrowBarrage"}]},
                  {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                  {"id":"self-damage","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
                  {"id":"die","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":5}]},
                  {"id":"die-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":4}]},
                  {"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]},
                  {"id":"remove","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:yilie"],"sourceBind":"fixture:zhou-chu-inert"}]},
                  {"id":"regain","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:yilie"]}]},
                  {"id":"opponent-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
                  {"id":"target-slash","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLivingWhoseAttackRangeIncludesOwner","usesPerTurn":null,"effects":[{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"answer"}]},
                  {"id":"nearest-slash","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashByNearest","target":"owner","targetKind":"otherLiving","amount":1}]},
                  {"id":"assisted-slash","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashAgainstChosenTarget","target":"selectedTarget","resultBind":"answer"}]},
                  {"id":"nearest-legal-slash","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestLegalSlashByNearest","target":"owner","targetKind":"otherLiving","amount":1}]}]},
              {"id":"fixture:zhou-chu-initial","revision":1,"modifiers":[{"id":"real-deal","query":"initialHandSize","operation":"add","value":12,"priority":0,"condition":{"kind":"always"}}]},
              {"id":"fixture:zhou-chu-peer-initial","revision":1,"modifiers":[{"id":"real-deal","query":"initialHandSize","operation":"add","value":4,"priority":0,"condition":{"kind":"always"}}]},
              {"id":"fixture:zhou-chu-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"fixture:zhou-chu-completed","revision":1,"triggers":[{"id":"child","window":"cardUseCompleted","ownerRelation":"actor","includeResponseUses":true,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:zhou-chu-initial-yilie","revision":1,"triggers":[{"id":"grant","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:yilie"]}]}]}
            ]}
            """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            var skills = rules["skills"]!.AsArray(); var initial = skills.Single(n => n!["id"]!.GetValue<string>() == "fixture:zhou-chu-initial")!;
            if (scenario is Scenario.Colorless or Scenario.Mixed) initial["modifiers"]![0]!["value"] = 2;
            if (scenario == Scenario.Allocation)
            {
                initial["modifiers"]![0]!["value"] = 8;
                var modifiers = initial["modifiers"]!.AsArray();
                foreach (var query in new[] { "drawCount", "attackRange", "slashLimit", "handLimit" }) modifiers.Add(JsonNode.Parse("""{"id":"addition","query":"drawCount","operation":"add","value":1,"priority":0,"condition":{"kind":"always"}}"""));
                for (var n = 1; n < 5; n++) { modifiers[n]!["id"] = "addition-" + n; modifiers[n]!["query"] = new[] { "drawCount", "attackRange", "slashLimit", "handLimit" }[n - 1]; }
                skills.Add(JsonNode.Parse("""{"id":"fixture:zhou-chu-extra-play","revision":1,"triggers":[{"id":"insert","window":"playEnding","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"insertPhase","target":"selectedTarget","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]}"""));
            }
            if (scenario == Scenario.Boost) skills.Add(JsonNode.Parse("""{"id":"fixture:zhou-chu-foreign","revision":1,"triggers":[{"id":"real-hits","window":"afterNormalDraw","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]},{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"damage","target":"selectedTarget","amount":2},{"op":"damage","target":"selectedTarget","amount":1},{"op":"loseHp","target":"selectedTarget","amount":1}]}]}"""));
            if (scenario == Scenario.Dodge) skills.Add(JsonNode.Parse("""{"id":"fixture:zhou-chu-foreign","revision":1,"viewAs":[{"id":"slash","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"useOnly":true}],"triggers":[{"id":"slash","window":"turnEnding","subject":"owner","turnOwnerScope":"otherLiving","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"useOwnerSlashAgainstTurnOwner","target":"owner","ignoreDistance":true}]}]}"""));
            if (scenario == Scenario.ForcedFire) skills.Add(JsonNode.Parse("""{"id":"fixture:zhou-chu-foreign","revision":1,"triggers":[{"id":"request","window":"afterNormalDraw","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]},{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"answer"}]}]}"""));
            if (scenario == Scenario.Provision) skills.Add(JsonNode.Parse("""{"id":"fixture:zhou-chu-provision-response","revision":1,"viewAs":[{"id":"slash","inputKinds":[],"inputSuits":[],"sourceZones":["hand"],"outputKind":"slash","forPlay":false,"forResponse":true},{"id":"dodge","inputKinds":[],"inputSuits":[],"sourceZones":["hand"],"outputKind":"dodge","forPlay":false,"forResponse":true}]}"""));
            if (scenario == Scenario.Pair)
            {
                var completed = skills.Single(n => n!["id"]!.GetValue<string>() == Completed)!;
                completed["triggers"]![0]!["usageScope"] = "game"; completed["triggers"]![0]!["usageLimit"] = 1;
                completed["triggers"]![0]!["effects"]!.AsArray().Add(JsonNode.Parse("""{"op":"grantSkills","target":"owner","skillIds":["fixture:zhou-chu-suppress"]}"""));
            }
            var presentation = skills.ToDictionary(n => n!["id"]!.GetValue<string>(), n =>
            {
                var id = n!["id"]!.GetValue<string>(); var p = new Dictionary<string, object> { ["name"] = id, ["description"] = "正式周处真实原生边界驱动" };
                if (id == Completed || id == Foreign && scenario is Scenario.Boost or Scenario.ForcedFire) p["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" };
                return (object)p;
            });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentation }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "固定原生驱动") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new(Suppress, "真实HP4资格抑制", "真实acquired来源失效") { SuppressionRule = new(4) });
            b.AddSkill(new("fixture:zhou-chu-inert", "替换来源", "无运行能力"));
            b.AddSkill(new("fixture:zhou-chu-first", "唯一首个其他角色", "固定原生选择") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 10000d) });
            b.AddSkill(new("fixture:zhou-chu-peer", "其他角色", "固定原生选择") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 100d) });
            var owner = new List<string> { "fixture:zhou-chu-initial" };
            if (scenario is Scenario.Allocation or Scenario.Boost) owner.Add(Shanduan);
            else owner.Add(scenario == Scenario.Pair ? "fixture:zhou-chu-initial-yilie" : Yilie);
            if (scenario is Scenario.Pair or Scenario.Dodge or Scenario.Dying or Scenario.Grain or Scenario.ForcedFire) owner.Add(Completed);
            if (scenario == Scenario.Allocation) owner.Add("fixture:zhou-chu-extra-play");
            if (scenario == Scenario.Provision) owner.Add("fixture:zhou-chu-provision-response");
            b.AddGeneral(new("fixture:zhou-chu-owner", "正式周处机制拥有者", "supporter", Driver, "jin", scenario == Scenario.Boost ? 8 : 4, owner, GeneralGender.Male));
            for (var seat = 1; seat < 4; seat++)
            {
                var peer = new List<string> { "fixture:zhou-chu-quiet" };
                if (scenario is not (Scenario.Colorless or Scenario.Mixed)) peer.Add("fixture:zhou-chu-peer-initial");
                if (seat == 1 && scenario is Scenario.Boost or Scenario.Dodge or Scenario.ForcedFire) peer.Add(Foreign);
                if (scenario is Scenario.Forced or Scenario.Provision) peer.Add(Yilie);
                if (scenario == Scenario.Forced) peer.Add(Completed);
                if (scenario == Scenario.Provision) peer.Add("fixture:zhou-chu-provision-response");
                b.AddGeneral(new($"fixture:zhou-chu-peer-{seat}", "固定其他角色", "supporter", seat == 1 ? "fixture:zhou-chu-first" : "fixture:zhou-chu-peer", "qun", scenario == Scenario.Allocation ? 8 : 4, peer));
            }
            b.AddCard(new("fixture:zhou-chu-qinglong", "青龙偃月刀", "装备牌", "原生范围3武器", CardKind.QinglongCrescentBlade));
            b.AddCard(new("fixture:zhou-chu-ox", "木牛流马", "装备牌", "原生手牌存粮", CardKind.WoodenOx));
            var physical = scenario switch { Scenario.Allocation => "fixture:zhou-chu-qinglong", Scenario.Pair => "standard:peach", Scenario.Grain => "fixture:zhou-chu-ox", _ => "standard:crossbow" };
            b.AddDeck(new("fixture:zhou-chu-deck", "固定真实小实体牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, scenario is Scenario.Colorless or Scenario.Mixed ? 2 : 64).Select(i =>
                new ContentDeckPhysicalCard(physical, scenario == Scenario.Colorless ? Suit.None : scenario == Scenario.Mixed && i == 1 ? Suit.Heart : Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "完整周处共享机制", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:zhou-chu-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:zhou-chu-owner", "fixture:zhou-chu-peer-1", "fixture:zhou-chu-peer-2", "fixture:zhou-chu-peer-3"]));
        }
    }
}
