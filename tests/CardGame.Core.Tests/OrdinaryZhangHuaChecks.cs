using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OrdinaryZhangHuaChecks
{
    private const string Bihun = "ol:bihun", Jianhe = "ol:jianhe", Chuanwu = "ol:chuanwu";
    private const string Driver = "fixture:zhang-hua-driver", Gain = "fixture:zhang-hua-gain";
    private const string Health = "fixture:zhang-hua-health", Changed = "fixture:zhang-hua-changed";
    private const string Mode = "identity:classic-zhang-hua-focused";
    private enum Scenario { Unique, Pair, Group, Self, Boundary, Canonical, Mixed, Equipment, Prefix, Lease }

    public static void OverflowUniquePhysicalAndPairMaterialsReturnThroughNativeGain()
    {
        foreach (var scenario in new[] { Scenario.Unique, Scenario.Pair })
        {
            var (g, registry) = Create(scenario);
            var cards = V(g, 0).Hand.Take(scenario == Scenario.Pair ? 2 : 1).Select(c => c.Id).ToArray();
            var before = V(g, 1).HandCount;
            if (scenario == Scenario.Pair) Use(g, "pair-slash", [1], cards);
            else SubmitPlay(g, g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.CardId == cards[0] && a.TargetSeats.SequenceEqual([1])));
            Reach(g, p => p.SkillPrompt?.SkillId == Gain && IsContinue(p));
            var fact = E<OverflowUseTargetsCanceledEvent>(g).Single();
            var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == fact.ProgramFrameId);
            var r = root.OverflowTargetCancellation!;
            var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == r.CardUseFrameId);
            var movement = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f => f.Batch.Id == r.MovementBatchId);
            Require(r.Source.SkillId == Bihun && r.Source.OwnerSeat == 0 && r.RecipientSeat == 1 && r.HandCount > r.HandLimit &&
                r.OriginalTargetSeats.SequenceEqual([1]) && r.BeforeTargetSeats.SequenceEqual([1]) &&
                r.CanceledPrimaryTargetSeats.SequenceEqual([1]) && r.ResultTargetSeats.Count == 0 &&
                r.MaterialCardIds.SequenceEqual(cards) && !fact.Receipt.MovementIssued && fact.Receipt.MovementBatchId is null &&
                fact.Receipt.ActionId == r.ActionId && fact.Receipt.MaterialCardIds.SequenceEqual(cards) && r.MovementIssued &&
                movement.Batch.ParentFrameId == root.Id && movement.Batch.AwaitingProgramFrameId == root.Id &&
                use.TargetSeats.Count == 0 && use.Action!.ActionId == r.ActionId &&
                use.Action.PhysicalCards.Select(c => c.CardId).SequenceEqual(cards) &&
                V(g, 1).HandCount == before + cards.Length && V(g, 1).Hp == 4 &&
                cards.All(id => g.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location == CardLocation.Hand(1)) &&
                !E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == use.Id) && E<DamageAppliedEvent>(g).Length == 0,
                "The accepted original unique target obtains every real paid material before its native gain child; cancellation keeps the original action and pending parent intact.");
            Frozen(r.OriginalTargetSeats); Frozen(r.BeforeTargetSeats); Frozen(r.CanceledPrimaryTargetSeats);
            Frozen(r.ResultTargetSeats); Frozen(r.MaterialCardIds); Private(g); g = Cold(g, registry);
            Continue(g); Play(g);
            Require(E<OverflowTargetCancellationCompletedEvent>(g).Count(e => e.ProgramFrameId == root.Id && e.CardUseFrameId == use.Id && e.ActionId == r.ActionId) == 1 &&
                E<OverflowUseMaterialTransferIssuedEvent>(g).Count(e => e.ProgramFrameId == root.Id && e.ActualCount == cards.Length) == 1 &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1 &&
                E<CardActionAcceptedEvent>(g).Count(e => e.Action.ActionId == r.ActionId) == 1 &&
                !g.ResolutionStack.Any(f => f.Id == root.Id || f.Id == use.Id) &&
                cards.All(id => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1 &&
                    g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.Hand(1)) == 1 &&
                    !g.CardMovements.Any(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile)),
                "The exact cancellation, transfer and native used-card completion each return once, without reclaiming or discarding the recipient's new hand cards.");
        }
    }

    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static bool IsContinue(PendingDecision p) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Require(result.Accepted, result.Error?.Message ?? "A genuine Zhang Hua fixture command was rejected."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> select) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(select).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Use(GameEngine g, string id, int[]? targets = null, int[]? cards = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void SubmitPlay(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind) { ConversionSource = a.ConversionSource });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> stop)
    {
        for (var i = 0; i < 140; i++)
        {
            var p = P(g); if (p is not null && stop(p)) return;
            Require(g.CreateSnapshot(0).Status != EngineStatus.Completed, "The fixed native Zhang Hua boundary was not reached before game completion: " + State(g));
            Step(g);
        }
        throw new InvalidOperationException("The fixed native Zhang Hua boundary was not reached: " + State(g));
    }
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p is not null && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is not null && IsContinue(p)) Continue(g);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification }) Answer(g, c => c.Cards.Count == 0);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Cards.Count == 0 && (c.Parameters.GetValueOrDefault("response") is "pass" or "let-die" || c.Parameters.GetValueOrDefault("action") == "pass"));
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Frozen<T>(IReadOnlyList<T> list) => Require(list is System.Collections.IList { IsReadOnly: true }, "Every exposed nested receipt and choice collection is read-only.");
    private static void Private(GameEngine g)
    {
        var p = P(g)!; Require(p.IsPrivate, "The owning native selection is private.");
        foreach (var viewer in Enumerable.Range(0, 4).Where(s => s != p.PlayerSeat)) Require(g.CreateSnapshot(viewer).PendingDecision is null &&
            g.CreateSnapshot(viewer).Players[p.PlayerSeat].Hand.Count == 0, "Other prepared views receive neither private choices nor foreign Hand identities.");
        Frozen(p.Choices); Frozen(p.ValidCardIds); Frozen(p.ValidTargetSeats); Frozen(p.ValidContentIds);
        foreach (var c in p.Choices) { Frozen(c.Cards); Frozen(c.Targets); Frozen(c.ContentIds); Require(c.Parameters is System.Collections.IDictionary { IsReadOnly: true }, "Choice parameters are frozen."); }
        var before = State(g); Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && State(g) == before,
            "An unpublished choice cannot transfer a material, consume a target ledger or change an outstanding native return.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements,
        Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(State(copy) == State(g), "Cold replay preserves all four prepared views, exact typed parents, frozen real materials, current printed order, original grant identities and native once-issued obligations."); return copy;
    }
    private static (GameEngine, ContentRegistry) Create(Scenario scenario)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, "fixture:zhang-hua-owner", g.Revision, P(g)!.PromptId)); Play(g);
        Require(V(g, 1).GeneralId == "fixture:zhang-hua-peer-1" && V(g, 0).MaxHp == 4 && V(g, 0).Hp == 4,
            "Native selection weights place the fixed peer at seat1 and ordinary base3 plus the real Lord mode produces initial HP4. " +
            JsonSerializer.Serialize(new { Scenario = scenario.ToString(), Players = Enumerable.Range(0, 4).Select(seat => new {
                Seat = seat, V(g, seat).GeneralId, V(g, seat).Role, V(g, seat).Hp, V(g, seat).MaxHp }).ToArray() }));
        return (g, registry);
    }
    private sealed class Fixture(Scenario scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ordinary-zhang-hua", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rr = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-zhang-hua.rules.json")!);
            using var pp = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-zhang-hua.presentation.json")!);
            var formal = SkillProgramCatalog.Load(rr.ReadToEnd(), pp.ReadToEnd());
            foreach (var (id, program) in formal.Programs) b.AddSkill(new(id, formal.Presentations[id].Name, formal.Presentations[id].Description)
                { Program = program, ProgramPresentation = formal.Presentations[id] });
            var rules = JsonNode.Parse("""
            {"skills":[
              {"id":"fixture:zhang-hua-driver","revision":1,"viewAs":[
                {"id":"two-slash","inputKinds":[],"inputSuits":[],"inputCount":2,"sourceZones":["hand"],"allowSameKind":true,"outputKind":"slash","forPlay":true,"forResponse":false},
                {"id":"two-arrows","inputKinds":[],"inputSuits":[],"inputCount":2,"sourceZones":["hand"],"sameSuit":true,"outputKind":"arrowBarrage","forPlay":true,"forResponse":false},
                {"id":"draw-two","inputKinds":[],"inputSuits":[],"sourceZones":["hand"],"outputKind":"drawTwo","forPlay":true,"forResponse":false,"singleCardTrickUse":true}],
                "activations":[
                  {"id":"pair-slash","minCards":2,"maxCards":2,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"selectedTarget","sourceBind":"two-slash","outputKind":"slash"}]},
                  {"id":"pair-arrows","minCards":2,"maxCards":2,"sourceZones":["hand"],"selectedCardsSameSuit":true,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"owner","sourceBind":"two-arrows","outputKind":"arrowBarrage"}]},
                  {"id":"lose-hp","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                  {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                  {"id":"self-damage","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
                  {"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]}]},
              {"id":"fixture:zhang-hua-initial","revision":1,"modifiers":[{"id":"initial","query":"initialHandSize","operation":"add","value":8,"priority":0,"condition":{"kind":"always"}}]},
              {"id":"fixture:zhang-hua-peer-initial","revision":1,"modifiers":[{"id":"initial","query":"initialHandSize","operation":"add","value":4,"priority":0,"condition":{"kind":"always"}}]},
              {"id":"fixture:zhang-hua-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"fixture:zhang-hua-gain","revision":1,"triggers":[{"id":"native-draw-or-gift-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.overflow-target-cancellation.give"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:zhang-hua-health","revision":1,"triggers":[{"id":"native-armor-recovery-child","window":"afterHpRecovered","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:zhang-hua-changed","revision":1,"triggers":[{"id":"native-printed-loss-child","window":"skillsChanged","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"condition":{"kind":"compare","left":{"kind":"currentHp"},"operator":"equal","right":{"kind":"integerConstant","value":3}},"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:zhang-hua-extra-play","revision":1,"triggers":[{"id":"insert","window":"playEnding","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"insertPhase","target":"selectedTarget","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]}
            ]}
            """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion; var skills = rules["skills"]!.AsArray();
            JsonNode Skill(string id) => skills.Single(n => n!["id"]!.GetValue<string>() == id)!;
            if (scenario == Scenario.Boundary) Skill("fixture:zhang-hua-initial")["modifiers"]![0]!["value"] = 5;
            if (scenario is Scenario.Canonical or Scenario.Mixed)
            {
                Skill("fixture:zhang-hua-initial")["modifiers"]![0]!["value"] = 17;
                Skill("fixture:zhang-hua-peer-initial")["modifiers"]![0]!["value"] = 1;
            }
            if (scenario is Scenario.Canonical or Scenario.Mixed or Scenario.Equipment or Scenario.Prefix or Scenario.Lease)
                Skill(Driver)["modifiers"] = JsonNode.Parse("""[{"id":"hand-limit","query":"handLimit","operation":"add","value":20,"priority":0,"condition":{"kind":"always"}}]""");
            if (scenario == Scenario.Lease)
            {
                Skill(Driver)["triggers"] = JsonNode.Parse("""[{"id":"acquire-duplicates","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:bihun","ol:jianhe"]}]}]""");
                Skill(Gain)["triggers"]![0]!["movementReasons"] = JsonSerializer.SerializeToNode(new[] { "skill-program.ol:chuanwu.ordered-printed-skill-loss.draw" });
            }
            var labels = skills.ToDictionary(n => n!["id"]!.GetValue<string>(), n =>
            {
                var id = n!["id"]!.GetValue<string>(); var p = new Dictionary<string, object> { ["name"] = id, ["description"] = "固定小实体正式张华原生行为驱动" };
                if (id is Gain or Health or Changed) p["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" };
                return (object)p;
            });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "固定原生边界") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:zhang-hua-first", "唯一首个其他角色", "真实选将权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 10000d) });
            b.AddSkill(new("fixture:zhang-hua-peer", "其他角色", "真实选将权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 100d) });
            var owner = new List<string> { Jianhe, Chuanwu, Driver, "fixture:zhang-hua-initial" };
            if (scenario == Scenario.Canonical) owner.Add("fixture:zhang-hua-extra-play");
            if (scenario == Scenario.Equipment) owner.Add(Health);
            if (scenario == Scenario.Lease) { owner.Add(Changed); owner.Add(Gain); }
            b.AddGeneral(new("fixture:zhang-hua-owner", "完整正式张华能力", "supporter", Bihun, "jin", 3, owner, GeneralGender.Male));
            for (var seat = 1; seat < 4; seat++)
            {
                var peer = new List<string> { "fixture:zhang-hua-peer-initial", "fixture:zhang-hua-quiet" };
                if (seat == 1 && scenario is Scenario.Unique or Scenario.Pair) peer.Add(Gain);
                b.AddGeneral(new($"fixture:zhang-hua-peer-{seat}", "固定真实其他角色", "supporter", seat == 1 ? "fixture:zhang-hua-first" : "fixture:zhang-hua-peer", "qun", 4, peer));
            }
            b.AddCard(new("fixture:zhang-hua-silver-lion", "白银狮子", "装备牌", "原生装备离区回复", CardKind.SilverLion));
            b.AddCard(new("fixture:zhang-hua-qinglong", "青龙偃月刀", "装备牌", "原生攻击范围3", CardKind.QinglongCrescentBlade));
            var count = scenario == Scenario.Canonical ? 24 : scenario == Scenario.Mixed ? 32 : 64;
            var physical = Enumerable.Range(0, count).Select(i => new ContentDeckPhysicalCard(scenario switch
            {
                Scenario.Canonical => new[] { "standard:slash", "standard:fire_slash", "standard:thunder_slash" }[i / 8],
                Scenario.Mixed => i < 16 ? "standard:slash" : "standard:duel",
                Scenario.Equipment => "fixture:zhang-hua-silver-lion",
                Scenario.Lease => "fixture:zhang-hua-qinglong",
                _ => "standard:slash"
            }, Suit.Spade, i % 13 + 1)).ToArray();
            b.AddDeck(new("fixture:zhang-hua-deck", "固定真实小实体牌堆", 0, 0, []) { PhysicalCards = physical });
            b.AddMode(new(Mode, "完整张华三能力原生共享机制", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:zhang-hua-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:zhang-hua-owner", "fixture:zhang-hua-peer-1", "fixture:zhang-hua-peer-2", "fixture:zhang-hua-peer-3"]));
        }
    }
}
