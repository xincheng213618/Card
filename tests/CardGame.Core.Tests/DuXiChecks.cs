using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DuXiChecks
{
    private const string Quxi = "ol:quxi", Bixiong = "ol:bixiong", Driver = "fixture:dx-driver", Quiet = "fixture:dx-quiet";
    private const string Watch = "fixture:dx-watch", Boundary = "fixture:dx-boundary", Rewrite = "fixture:dx-rewrite";
    private const string Health = "fixture:dx-health";
    private const string Owner = "fixture:dx-owner", Mode = "identity:classic-du-xi-fixture";

    public static void ActualUnequalPairDrawModifiersRoundTransferAndSameHolderCold()
    {
        var (g, registry) = Start("source"); Use(g, "draw-two", [1]); Play(g);
        var counts = Enumerable.Range(0, 4).Select(s => V(g, s).HandCount).ToArray();
        End(g); Reach(g, () => Optional(P(g), Quxi)); Activate(g); Reach(g, () => Branch(P(g), "pair"));
        Need(P(g)!.Choices.All(c => c.Targets is [var less, var more] && less != 0 && more != 0 && counts[less] < counts[more]) &&
            V(g, 0).IsFaceDown && E<DuXiQuxiAcceptedEvent>(g).Count() == 1,
            "The genuine limited play-ending branch turns its owner face down before exposing only unequal-hand pairs of other living players.");
        g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([2, 1])); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
        var receipt = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.DuXiQuxi is not null).DuXiQuxi!;
        Need(receipt is { Stage: DuXiQuxiStage.ObtainChildren, LessHandSeat: 2, MoreHandSeat: 1, ObtainedCardId: not null } &&
            E<DuXiQuxiCardObtainedEvent>(g).Count() == 1 && !E<DuXiQuxiMarkerMovedEvent>(g).Any(),
            "The lesser-hand participant really acquires an opaque native hand card before either persistent mark is assigned.");
        g = Cold(g, registry); Reach(g, () => Optional(P(g), Quxi));
        Need(E<DuXiQuxiDiscardSkippedEvent>(g).Count() == 1 && E<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == Boundary && e.OwnerSeat == 0) == 0 &&
            DrawCount(g, 2, 1) == 1 && DrawCount(g, 3, 2) == 3 && E<DuXiQuxiMarkerMovedEvent>(g).Count() == 2,
            "The actual initial turn skips the entire Discard boundary; the issued Qian and Feng genuinely change the next holders' normal Draw to one and three.");
        Activate(g); Reach(g, () => Branch(P(g), "transfer"));
        Answer(g, c => c.Targets.SequenceEqual([0])); g = Cold(g, registry);
        Need(P(g)!.Choices.Any(c => c.Targets.SequenceEqual([0])), "The second independent mark can move to the owner already holding the first mark.");
        Answer(g, c => c.Targets.SequenceEqual([0]));
        var skipped = g.State.TurnNumber; Reach(g, () => E<TurnEndedEvent>(g).Any(e => e.ActorSeat == 0 && e.TurnNumber == skipped));
        Need(DrawCount(g, skipped, 0) == 0 && Mark(g, 0, PlayerMarkerKind.QuxiFeng) == 1 && Mark(g, 0, PlayerMarkerKind.QuxiQian) == 1,
            "The real round-start transfer permits both marks on the owner, then the existing face-down whole turn still performs no Draw.");
        g = Cold(g, registry); Reach(g, () => Optional(P(g), Quxi)); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); Play(g);
        Need(DrawCount(g, g.State.TurnNumber, 0) == 2 && E<DuXiQuxiAcceptedEvent>(g).Count() == 1 && E<DuXiQuxiCardObtainedEvent>(g).Count() == 1,
            "The next genuine action turn applies Feng +1 and Qian -1 together as net zero, without repeating the limited branch or physical acquisition after cold restoration.");
        _ = Cold(g, registry);
    }

    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static int Mark(GameEngine g, int seat, PlayerMarkerKind kind) => V(g, seat).Markers?.FirstOrDefault(m => m.Kind == kind)?.Count ?? 0;
    private static bool Optional(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool Branch(PendingDecision? p, string branch) => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == branch) == true;
    private static int DrawCount(GameEngine g, int turn, int seat) => g.CardMovements.Count(m => m.TurnNumber == turn && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(seat) && m.Reason == CardMoveReasons.Draw);
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null, IReadOnlyList<int>? cards = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 300; step++)
        {
            if (done()) return; var p = P(g);
            if (g.State.Winner != Winner.None) throw new InvalidOperationException("Du Xi actual game completed before the required boundary: " + JsonSerializer.Serialize(new {
                g.State.TurnNumber, g.State.Winner, Players = Enumerable.Range(0, 4).Select(s => new { s, V(g, s).IsAlive, V(g, s).IsFaceDown, V(g, s).HandCount, Skills = V(g, s).Skills!.Select(x => x.Id) }),
                Starts = E<ProgramBindingStartedEvent>(g).Where(e => e.SkillId == Quxi), Accepted = E<DuXiQuxiAcceptedEvent>(g), Marks = E<DuXiQuxiMarkerMovedEvent>(g),
                Tail = g.Events.TakeLast(8).Select(e => e.Payload.ToString()) }));
            if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards");
            else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards");
            else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
            else if (p is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected actual Du Xi boundary: " + JsonSerializer.Serialize(p));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing actual Du Xi boundary: " + JsonSerializer.Serialize(new { Pending = P(g), Frames = g.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step }), Facts = g.Events.TakeLast(7).Select(e => e.Payload.ToString()) }));
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected actual Du Xi command."); }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(cold), "Cold Du Xi preserves exact source-attributed persistent markers, private opaque card choices, actual paid children, normal Draw and suit protections."); return cold; }
    private static (GameEngine, ContentRegistry) Start(string scenario)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-du-xi", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:du-xi", "ol:cao-song");
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"range","query":"slashDistanceLimit","operation":"unlimited","value":0,"priority":0},{"id":"hand","query":"handLimit","operation":"add","value":30,"priority":0}],"activations":[
                {"id":"draw-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":2}]},
                {"id":"stock","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":20}]},
                {"id":"flip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"turnOver","target":"selectedTarget"}]},
                {"id":"rewrite","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{Rewrite}}"]}]},
                {"id":"equip-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":1,"resultBind":"drawn"},{"op":"revealBoundCards","target":"owner","sourceBind":"drawn"},{"op":"filterBoundCards","target":"owner","sourceBind":"drawn","resultBind":"gear","categories":["equipment"]},{"op":"useBoundCardByTarget","target":"selectedTarget","sourceBind":"gear","condition":{"kind":"boundCardsMatchCategories","sourceBind":"gear","cardCategories":["equipment"]} }]},
                {"id":"empty","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"empty"},{"op":"moveBoundCards","target":"owner","sourceBind":"empty","destination":"discardPile","awaitMovementTriggers":true}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-quxi-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.du-xi.quxi.obtain"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}]}]},
              {"id":"{{Boundary}}","revision":1,"triggers":[{"id":"discard-start","window":"discardPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"start","options":[{"id":"continue"}]}]},{"id":"discard-end","window":"discardPhaseEnded","turnOwnerScope":"own","allowOwnDiscardPhaseEnded":true,"subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"end","options":[{"id":"continue"}]}]}]},
              {"id":"{{Rewrite}}","revision":1,"cardPolicies":[{"id":"effective-spades","kind":"rewriteSuit","inputSuit":"heart","outputSuit":"spade"}]},
              {"id":"{{Health}}","revision":1,"triggers":[{"id":"actual-health-child","window":"afterHealthChanged","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var ids = new[] { Driver, Quiet, Watch, Boundary, Rewrite, Health };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = ids.ToDictionary(id => id, id => new { name = id, description = "杜袭实际规则小夹具", optionLabels = id is Watch or Boundary or Health ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() }) }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "杜袭原生行为") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:dx-owner-pick", "本人", "固定公开选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? 100000d : -100000d) });
            b.AddSkill(new("fixture:dx-peer-pick", "其他", "固定公开选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -100000d : 100000d) });
            b.AddGeneral(new(Owner, "杜袭真实规则本人", "ol-du-xi", "fixture:dx-owner-pick", "wei", scenario == "equipment" ? 12 : 4,
                scenario is "source" or "multiple" ? [Quxi, Bixiong, Driver, Watch, Boundary] : scenario == "equipment" ? [Driver, Watch, Boundary, Health, "ol:yizheng-cao-song"] : [Bixiong, Driver, Watch, Boundary]));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:dx-peer-{i}").ToArray();
            foreach (var (id, index) in peers.Select((id, index) => (id, index)))
            {
                var pick = "fixture:dx-pick-" + index;
                b.AddSkill(new(pick, "固定其他顺序", "公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -100000d : 100003d - index) });
                var skills = new List<string> { Quiet, Watch }; if (scenario is "equipment" or "multiple" && index == 0) skills.Add(Quxi); if (scenario == "bixiong" && index == 0) skills.Add(Bixiong);
                if (scenario is "equipment" or "multiple" && index == 0) skills.Remove(Quiet);
                b.AddGeneral(new(id, "真实其他", "supporter", pick, "wei", 8, skills) { InitialHp = scenario == "bixiong" && index == 0 ? 1 : 8 });
            }
            b.AddCard(new("fixture:dx-lion", "白银狮子", "装备牌", "真实装备移除回复实体", CardKind.SilverLion));
            b.AddCard(new("fixture:dx-garden", "桃园结义", "锦囊牌", "真实全体回复实体", CardKind.PeachGarden));
            var cardIds = scenario == "bixiong" ? new[] { "standard:slash", "standard:duel", "standard:indulgence", "fixture:dx-garden" } : new[] { "fixture:dx-lion" };
            b.AddDeck(new("fixture:dx-deck", "固定实体非种子搜索", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 160).Select(i => new ContentDeckPhysicalCard(cardIds[i % cardIds.Length], Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "杜袭真实命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:dx-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
