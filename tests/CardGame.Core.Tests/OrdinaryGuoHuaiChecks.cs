using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OrdinaryGuoHuaiChecks
{
    private const string Zhefu = "ol:zhefu", Yidu = "ol:yidu", Driver = "fixture:guo-huai-driver";
    private const string Loss = "fixture:guo-huai-loss", DamageLoss = "fixture:guo-huai-damage-loss";
    private const string Child = "fixture:guo-huai-discard-child", Suppress = "fixture:guo-huai-suppress", Mode = "fixture:guo-huai-mode";
    private const string ActiveRequest = "fixture:guo-huai-active-request";

    public static void FactionProvidersOwnTheirCompletedResponseExactlyOnce()
    {
        foreach (var required in new[] { CardKind.Dodge, CardKind.Slash })
        foreach (var lordHasZhefu in new[] { false, true })
        {
            var (g, registry) = Create(required, providerDirection: required, lordHasZhefu: lordHasZhefu);
            var lord = g.CreateSnapshot(0).Players.Single(p => p.Role == Role.Lord).Seat;
            var provider = g.CreateSnapshot(0).Players.Single(p => p.GeneralId == "fixture:guo-huai-peer-1").Seat;
            Require(lord != 0 && provider != lord && provider != 0 && V(g, lord).HandCount == 0 && V(g, provider).HandCount == 4 &&
                V(g, provider).Skills!.Any(s => s.Id == Zhefu) && V(g, lord).Skills!.Any(s => s.Id == Zhefu) == lordHasZhefu,
                "Real role-weighted selection preserves an outside-turn native Lord and a distinct qualified provider; only the provider owns Zhefu in the first branch.");
            if (required == CardKind.Dodge) Arrows(g); else Convert(g, "duel", [lord]);
            var responseKind = required == CardKind.Dodge ? DecisionKind.RespondDodge : DecisionKind.RespondSlash;
            var requestMarker = required == CardKind.Dodge ? "faction-defense-request" : "faction-slash-request";
            var supplyMarker = required == CardKind.Dodge ? "faction-defense-dodge" : "faction-slash-slash";
            Reach(g, p => p.PlayerSeat == lord && p.Kind == responseKind && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == requestMarker));
            var parent = g.ResolutionStack.OfType<CardUseFrame>().Last(f => f.CardKind == (required == CardKind.Dodge ? CardKind.ArrowBarrage : CardKind.Duel));
            var parentId = parent.Id; var parentAction = parent.Action!.ActionId;
            Accept(g, new AdvanceOneStepCommand(g.Revision));
            Reach(g, p => p.PlayerSeat == provider && p.Kind == responseKind && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == supplyMarker));
            var published = P(g)!.Choices.Where(c => c.Parameters.GetValueOrDefault("response") == supplyMarker).SelectMany(c => c.Cards).ToHashSet();
            g = Cold(g, registry);
            Accept(g, new AdvanceOneStepCommand(g.Revision)); Reach(g, p => Offer(p, Zhefu, provider));
            var completion = E<CardResponseCompletedEvent>(g).Single(e => e.NativeActorSeat == lord && e.ProviderSeat == provider && e.ParentFrameId == parentId);
            var native = E<CardActionAcceptedEvent>(g).Single(e => e.Action.ActionId == completion.ActionId).Action;
            Require(completion.ActorSeat == provider && completion.EffectiveKind == required && completion.ActualTurnOwnerSeat == 0 &&
                native.ActorSeat == lord && native.ProviderSeat == provider && native.RequesterSeat == lord && native.ResponderSeat == lord &&
                native.Type == CardActionType.Response && native.ParentActionId == parentAction &&
                native.PhysicalCards is [{ CardId: var card, From: var from }] && published.Contains(card) && from == CardLocation.Hand(provider) &&
                E<CardActionAcceptedEvent>(g).Count(e => e.Action.ActionId == completion.ActionId) == 1,
                "Native assistance retains the original requester action and real provider material; the completed-response actor belongs to the supplier without creating a second accepted action.");
            Answer(g, Activate); Reach(g, p => Demand(p, "target"));
            var started = E<SameNameHandStartedEvent>(g).Single(e => e.ActionId == completion.ActionId);
            Require(started.Source.OwnerSeat == provider && started.OriginalParentFrameId == parentId && started.NormalizedName == required,
                "The exact provider source, effective name and original incoming parent own the issued Zhefu demand.");
            Answer(g, c => c.Targets.SequenceEqual([0])); Reach(g, p => Demand(p, "discard"));
            Answer(g, c => c.Cards.Count == 1); Play(g); OnceDemand(g, started.FrameId, discarded: true);
            Require(E<SameNameHandStartedEvent>(g).Count(e => e.ActionId == completion.ActionId) == 1 &&
                !E<SameNameHandStartedEvent>(g).Any(e => e.ActionId == completion.ActionId && e.Source.OwnerSeat == lord),
                "The supplied physical response triggers once for its provider, including when the foreign-turn Lord also owns Zhefu; it cannot be attributed to both roles.");
        }
    }

    private static void OnceDemand(GameEngine g, long id, bool discarded) => Require(
        E<SameNameHandCompletedEvent>(g).Single(e => e.FrameId == id) is var done && done.Discarded == discarded && done.DamageIssued != discarded &&
        E<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == id && e.Completed) == 1 && !g.ResolutionStack.Any(f => f.Id == id),
        "The exact demand owning program returns and completes once with precisely its paid branch.");
    private static void Arrows(GameEngine g) => Use(g, "arrows", cards: V(g, 0).Hand.Take(2).Select(c => c.Id).ToArray());
    private static void Convert(GameEngine g, string binding, int[] targets)
    {
        var action = g.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == Driver && a.ConversionSource.BindingId == binding && a.TargetSeats.SequenceEqual(targets));
        Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind) { ConversionSource = action.ConversionSource });
    }
    private static bool Activate(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action") == "activate";
    private static bool Offer(PendingDecision p, string skill, int owner) => p.PlayerSeat == owner && p.SkillPrompt?.SkillId == skill && p.Choices.Any(Activate);
    private static bool Has(PendingDecision p, string action) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
    private static bool Demand(PendingDecision p, string step) => Has(p, "same-name-hand") && p.Choices.Any(c => c.Parameters.GetValueOrDefault("step") == step);
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "A real Guo Huai command was rejected."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> select) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(select).Id, g.Revision)); }
    private static void Use(GameEngine g, string id, int[]? targets = null, int[]? cards = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> stop)
    {
        for (var n = 0; n < 120; n++) { var p = P(g); if (p is not null && stop(p)) return; Step(g); }
        throw new InvalidOperationException("The fixed actual Guo Huai boundary was not reached: " + State(g));
    }
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) throw new InvalidOperationException("An expected nested boundary already returned to Play: " + State(g));
        if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification }) Answer(g, c => c.Cards.Count == 0);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Cards.Count == 0 && (c.Parameters.GetValueOrDefault("response") is "pass" or "let-die" || c.Parameters.GetValueOrDefault("action") == "pass"));
        else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { Kind: DecisionKind.DiscardCards, PlayerSeat: 0 }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack),
        Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(State(copy) == State(g), "Cold replay preserves all four prepared views, frozen choices, owning typed receipts, native children, events and physical card provenance."); return copy; }

    private static (GameEngine, ContentRegistry) Create(CardKind kind, bool yidu = false, bool sourceLoss = false, bool damageLoss = false, bool mixed = false, bool targetRed = false, bool emptySecond = false, CardKind? providerDirection = null, bool lordHasZhefu = false, bool activeSupply = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(kind, yidu, sourceLoss, damageLoss, mixed, targetRed, emptySecond, providerDirection, lordHasZhefu, activeSupply));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = providerDirection is null ? Role.Lord : Role.Renegade, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, "fixture:guo-huai-owner", g.Revision, P(g)!.PromptId));
        Play(g);
        Require(providerDirection is not null || V(g, 1).GeneralId == "fixture:guo-huai-peer-1", "Native role-weighted selection places the unique response skill owner at seat1.");
        return (g, registry);
    }
    private sealed class Fixture(CardKind kind, bool yidu, bool sourceLoss, bool damageLoss, bool mixed, bool targetRed, bool emptySecond, CardKind? providerDirection, bool lordHasZhefu, bool activeSupply) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:ordinary-guo-huai", "1.0.0", "完整哲妇遗毒的真实原生边界");
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rules = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-guo-huai.rules.json")!);
            using var labels = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-guo-huai.presentation.json")!);
            var formal = SkillProgramCatalog.Load(rules.ReadToEnd(), labels.ReadToEnd());
            foreach (var id in formal.Programs.Keys) b.AddSkill(new(id, formal.Presentations[id].Name, formal.Presentations[id].Description) { Program = formal.Programs[id], ProgramPresentation = formal.Presentations[id] });
            var fixture = JsonNode.Parse($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"viewAs":[
                {"id":"slash","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"allowSameKind":true},
                {"id":"duel","inputKinds":[],"inputSuits":[],"outputKind":"duel","forPlay":true,"forResponse":false,"singleCardTrickUse":true},
                {"id":"draw","inputKinds":[],"inputSuits":[],"outputKind":"drawTwo","forPlay":true,"forResponse":false,"singleCardTrickUse":true},
                {"id":"peach","inputKinds":[],"inputSuits":[],"outputKind":"peach","forPlay":false,"forResponse":true},
                {"id":"arrows","inputKinds":[],"inputSuits":[],"inputCount":2,"sameSuit":true,"outputKind":"arrowBarrage","forPlay":true,"forResponse":false}],
                "activations":[
                  {"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLivingWhoseAttackRangeIncludesOwner","usesPerTurn":null,"effects":[{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"answer"}]},
                  {"id":"arrows","minCards":2,"maxCards":2,"sourceZones":["hand"],"selectedCardsSameSuit":true,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"owner","sourceBind":"arrows","outputKind":"arrowBarrage"}]},
                  {"id":"hurt-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]},
                  {"id":"hurt-self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":4}]}]},
              {"id":"{{{Loss}}}","revision":1,"triggers":[{"id":"paid-source-loss","window":"discardPileReceived","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementDiscardOnly":true,"discardOwnerScope":"other","movementReasons":["skill-program.ol:zhefu.same-name-hand.discard"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]},{"op":"grantSkills","target":"owner","skillIds":["{{{Suppress}}}"]}]}]},
              {"id":"{{{DamageLoss}}}","revision":1,"triggers":[{"id":"issued-source-loss","window":"beforeDamageApplied","subject":"damageSource","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]},{"op":"grantSkills","target":"owner","skillIds":["{{{Suppress}}}"]}]}]},
              {"id":"{{{Child}}}","revision":1,"triggers":[{"id":"discard-child","window":"discardPileReceived","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementDiscardOnly":true,"discardOwnerScope":"own","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:guo-huai-red","revision":1,"cardPolicies":[{"id":"red-target","kind":"rewriteSuit","inputSuit":"spade","outputSuit":"heart"}]},
              {"id":"fixture:guo-huai-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """)!;
            if (sourceLoss || damageLoss) fixture["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:guo-huai-initial-zhefu","revision":1,"triggers":[{"id":"acquire-source","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:zhefu"]}]}]}""")!);
            if (emptySecond || providerDirection is not null) fixture["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:guo-huai-initial","revision":1,"modifiers":[{"id":"initial","query":"initialHandSize","operation":"add","value":4,"priority":0,"condition":{"kind":"always"}}]}""")!);
            if (providerDirection is { } required) fixture["skills"]!.AsArray().Add(JsonNode.Parse(JsonSerializer.Serialize(new {
                id = "fixture:guo-huai-faction", revision = 1, cardPolicies = new[] { new {
                    id = "native-assistance", kind = "factionResponseRequest", requiredCardKinds = new[] { required == CardKind.Dodge ? "dodge" : "slash" }, factionId = "wei", ownerRole = "lord" } }
            }))!);
            if (activeSupply)
            {
                fixture["skills"]!.AsArray().Add(JsonNode.Parse(JsonSerializer.Serialize(new {
                    id = ActiveRequest, revision = 1, modifiers = new[] { new { id = "provided-range", query = "attackRange", operation = "set", value = 3, priority = 0 } }
                }))!);
                fixture["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == Driver)!["activations"]!.AsArray().Add(JsonNode.Parse("""{"id":"assisted","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashAgainstChosenTarget","target":"selectedTarget","resultBind":"answer"}]}""")!);
            }
            // Twenty red and twenty black entities: twenty-two genuinely dealt target cards
            // leave both colors after its one native Dodge, independently of shuffle and AI cost.
            if (mixed) fixture["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:guo-huai-mixed-initial","revision":1,"modifiers":[{"id":"guaranteed-colors","query":"initialHandSize","operation":"add","value":18,"priority":0,"condition":{"kind":"always"}}]}""")!);
            if (emptySecond) fixture["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:guo-huai-extra-initial","revision":1,"modifiers":[{"id":"one-hidden","query":"initialHandSize","operation":"add","value":1,"priority":0,"condition":{"kind":"always"}}]}""")!);
            var presentation = fixture["skills"]!.AsArray().ToDictionary(n => n!["id"]!.GetValue<string>(), n =>
            {
                var id = n!["id"]!.GetValue<string>(); var p = new Dictionary<string, object> { ["name"] = id, ["description"] = "固定小实体原生行为驱动" };
                if (id is Loss or DamageLoss or Child) p["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" };
                return (object)p;
            });
            var catalog = SkillProgramCatalog.Load(fixture.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentation }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "真实原生夹具") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new(Suppress, "实际HP4资格抑制", "不修改隐藏状态") { SuppressionRule = new(4) });
            b.AddSkill(new("fixture:guo-huai-first-selection", "唯一首个AI响应拥有者", "原生选将排序") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 10000d) });
            b.AddSkill(new("fixture:guo-huai-peer-selection", "其余真实候选", "原生选将排序") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 100d) });
            if (emptySecond) b.AddSkill(new("fixture:guo-huai-second-selection", "唯一空手次个目标", "原生选将排序") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000d) });
            var ownerSkills = new List<string>(yidu ? new[] { Yidu, Zhefu } : new[] { Zhefu });
            if (emptySecond || providerDirection is not null) ownerSkills.Add("fixture:guo-huai-initial");
            b.AddGeneral(new("fixture:guo-huai-owner", "真实发动者", "supporter", Driver, "jin", 4, ownerSkills));
            var first = new List<string> { "fixture:guo-huai-quiet" };
            if (emptySecond || providerDirection is not null) first.Add("fixture:guo-huai-initial");
            if (emptySecond) first.Add("fixture:guo-huai-extra-initial");
            if (mixed) first.Add("fixture:guo-huai-mixed-initial");
            if (yidu) first.Add(Child); else first.Add(sourceLoss || damageLoss ? "fixture:guo-huai-initial-zhefu" : Zhefu);
            if (sourceLoss) first.Add(Loss); if (damageLoss) first.Add(DamageLoss); if (targetRed) first.Add("fixture:guo-huai-red");
            b.AddGeneral(new("fixture:guo-huai-peer-1", "唯一响应拥有者或展示目标", "supporter", "fixture:guo-huai-first-selection", providerDirection is null ? "qun" : "wei", 4, first));
            if (providerDirection is not null)
            {
                b.AddSkill(new("fixture:guo-huai-lord-selection", "唯一真实主公", "原生角色权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? 100000d : -10000d) });
                var lordSkills = new List<string> { "fixture:guo-huai-faction", "fixture:guo-huai-quiet" };
                if (lordHasZhefu) lordSkills.Add(Zhefu);
                if (activeSupply) lordSkills.Add(ActiveRequest);
                b.AddGeneral(new("fixture:guo-huai-peer-2", "真实护驾或激将主公", "supporter", "fixture:guo-huai-lord-selection", "wei", 4, lordSkills));
                b.AddGeneral(new("fixture:guo-huai-peer-3", "其他存活者", "supporter", "fixture:guo-huai-peer-selection", "qun", 4,
                    ["fixture:guo-huai-quiet", "fixture:guo-huai-initial"]));
            }
            else for (var seat = 2; seat < 4; seat++) b.AddGeneral(new($"fixture:guo-huai-peer-{seat}", "普通其他目标", "supporter", emptySecond && seat == 2 ? "fixture:guo-huai-second-selection" : "fixture:guo-huai-peer-selection", "qun", 4,
                emptySecond && seat == 3 ? ["fixture:guo-huai-quiet", "fixture:guo-huai-initial"] : ["fixture:guo-huai-quiet"]));
            var card = kind switch { CardKind.Slash => "standard:slash", CardKind.FireSlash => "standard:fire_slash", CardKind.Dodge => "standard:dodge", CardKind.Nullification => "standard:nullification", _ => throw new InvalidOperationException() };
            b.AddDeck(new("fixture:guo-huai-deck", "固定小实体牌堆", emptySecond || providerDirection is not null ? 0 : 4, 0, []) { PhysicalCards = Enumerable.Range(0, 40).Select(i =>
                new ContentDeckPhysicalCard(card, mixed && i % 2 == 1 ? Suit.Heart : Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "完整郭槐共享机制", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:guo-huai-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:guo-huai-owner", "fixture:guo-huai-peer-1", "fixture:guo-huai-peer-2", "fixture:guo-huai-peer-3"]));
        }
    }
}
