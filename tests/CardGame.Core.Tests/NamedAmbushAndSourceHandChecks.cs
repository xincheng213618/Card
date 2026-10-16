using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class NamedAmbushAndSourceHandChecks
{
    private const string Shefu = "ol:shefu", Benyu = "ol:benyu", Driver = "fixture:ambush-driver", Mode = "identity:classic-ambush";
    public static void PrivateNamesAndAllTargetUseStateSurviveColdRecovery()
    {
        var (g, registry) = Start(CardKind.BarbarianAssault);
        Deposit(ref g, registry, 0, CardKind.BarbarianAssault);
        var d = E<NamedAmbushDepositedEvent>(g).Single().Deposit;
        var own = V(g, 0).NamedAmbushPiles!.Single(); var foreign = g.CreateSnapshot(1).Players[0].NamedAmbushPiles!.Single();
        Need(own.Count == 1 && own.Entries is [{ RecordedKind: CardKind.BarbarianAssault }] && own.Entries[0].Card.Id == d.CardId &&
            foreign.Count == 1 && foreign.Entries is null && !g.CreateSnapshot(1).Players[0].Hand.Any(c => c.Id == d.CardId),
            "Only the owner sees each recorded name and private entity; other viewers see only the pile count.");
        g = Cold(g, registry);
        Reach(g, () => Optional(P(g), Shefu)); Activate(g); Reach(g, () => Branch(P(g), "named-ambush", "remove"));
        var window = g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last(); var id = window.ParentFrameId; var before = g.CreateSnapshot(0).Players.Select(p => p.Hp).ToArray();
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "remove");
        Reach(g, () => E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == id));
        Need(E<NamedAmbushUseInvalidatedEvent>(g).Count(e => e.Use.ParentFrameId == id) == 1 && E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == id) == 1 &&
            g.CreateSnapshot(0).Players.Select(p => p.Hp).SequenceEqual(before) &&
            g.CreateCardZoneDiagnostics().Single(c => c.CardId == d.CardId).Location == CardLocation.DiscardPile,
            "One real hand-use state cancels all actual AOE targets and retains one paid entity and one use completion."); Cold(g, registry);
    }
    private static (GameEngine, ContentRegistry) Start(CardKind kind, bool mixed = false, bool benyu = false, bool sourceLoss = false, bool zero = false, bool reuse = false, bool deathOwner = false, bool quiet = false, bool benyuPeer = false, bool actorSwap = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(kind, mixed, benyu, sourceLoss, zero, reuse, deathOwner, quiet, benyuPeer, actorSwap));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 16 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, "fixture:ambush-owner", g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private static void Deposit(ref GameEngine g, ContentRegistry registry, int owner, CardKind name)
    {
        var engine = g; End(engine); Reach(engine, () => Optional(P(engine), Shefu) && P(engine)!.PlayerSeat == owner);
        Activate(engine); Reach(engine, () => Branch(P(engine), "named-ambush", "card"));
        engine = Cold(engine, registry); Answer(engine, c => c.Parameters.GetValueOrDefault("branch") == "card");
        Reach(engine, () => Branch(P(engine), "named-ambush", "name")); Answer(engine, c => c.Parameters.GetValueOrDefault("recorded-kind") == name.ToString());
        Reach(engine, () => E<NamedAmbushDepositedEvent>(engine).Any(e => e.Deposit.Source.OwnerSeat == owner)); g = engine;
    }
    private static PlayerSnapshot V(GameEngine g, int s) => g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static bool Optional(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool Branch(PendingDecision? p, string action, string branch) => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action && c.Parameters.GetValueOrDefault("branch") == branch) == true;
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(pick).Id, g.Revision)); }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string activation, IReadOnlyList<int> cards)
    { if (activation == "trim" && cards.Count == 0) return; Accept(g, new UseProgramSkillCommand(0, Driver, activation, cards, [], g.Revision, P(g)!.PromptId)); }
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine g, Func<bool> stop)
    {
        for (var i = 0; i < 300; i++)
        {
            if (stop()) return; var p = P(g);
            if (g.State.Winner != Winner.None) throw new InvalidOperationException("Ambush fixture ended before boundary: " + JsonSerializer.Serialize(p));
            if (p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) End(g);
            else if (p is { Kind: DecisionKind.DiscardCards, PlayerSeat: 0 }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else if (p?.Choices.Count > 0 && (p.PlayerSeat == 0 || p.Kind == DecisionKind.ProgramTrigger))
            {
                var c = p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "skip") ??
                    p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "decline") ??
                    p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die") ??
                    p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("option-id") == "continue") ?? p.Choices[0];
                Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c.Id, g.Revision));
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Ambush exceeded bounded real prompts: " + JsonSerializer.Serialize(new { prompt = P(g), stack = g.ResolutionStack, events = E<NamedAmbushDepositedEvent>(g) }));
    }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected actual ambush command."); }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(Enumerable.Range(0, 4).All(s => SnapshotJson.Serialize(g.CreateSnapshot(s)) == SnapshotJson.Serialize(copy.CreateSnapshot(s))) &&
            JsonSerializer.Serialize(g.ResolutionStack) == JsonSerializer.Serialize(copy.ResolutionStack) && JsonSerializer.Serialize(g.CardMovements) == JsonSerializer.Serialize(copy.CardMovements),
            "All viewer-filtered private choices, exact receipts and movements restore cold unchanged."); return copy;
    }
    private static void Need(bool yes, string why) { if (!yes) throw new InvalidOperationException(why); }
    private sealed class Fixture(CardKind kind, bool mixed, bool benyu, bool sourceLoss, bool zero, bool reuse, bool deathOwner, bool quiet, bool benyuPeer, bool actorSwap) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ambush", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-cheng-yu.rules.json")!;
            using var reader = new StreamReader(stream); using var ls = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-cheng-yu.presentation.json")!;
            using var lr = new StreamReader(ls); RegisterCatalog(SkillProgramCatalog.Load(reader.ReadToEnd(), lr.ReadToEnd()));
            void RegisterCatalog(SkillProgramCatalog catalog)
            { foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] }); }
            void Add(string id, string body, bool label = false)
            {
                var rule = "{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"" + id + "\",\"revision\":1," + body + "}]}";
                var entry = new Dictionary<string, object> { ["name"] = id, ["description"] = "Actual ambush fixture" };
                if (label) entry["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" };
                var presentation = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object> { [id] = entry } });
                RegisterCatalog(SkillProgramCatalog.Load(rule, presentation));
            }
            Add(Driver, """
            "activations":[{"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":12}]},
             {"id":"trim","minCards":1,"maxCards":16,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"trimmed"},{"op":"moveBoundCards","target":"owner","sourceBind":"trimmed","destination":"discardPile","awaitMovementTriggers":true}]},
             {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"loseHp","target":"owner","amount":20}]}]
            """);
            if (reuse)
            {
                Add("fixture:ambush-recycle", """
                "triggers":[{"id":"recycle","window":"discardPileReceived","subject":"owner","movementReasons":["skill-program.named-ambush.remove"],"optional":false,"effects":[{"op":"claimMovedCards","target":"owner"}]}]
                """);
                Add("fixture:ambush-gift", """
                "triggers":[{"id":"return-recovered","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.fixture:ambush-recycle.ClaimMovedCards"],"optional":false,
                "effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"selectSourceCard","target":"owner","cardSource":"owner","zones":["hand"],"resultBind":"recovered"},{"op":"moveBoundCards","target":"owner","sourceBind":"recovered","destination":"selectedTargetHand"}]}]
                """);
            }
            if (zero) Add("fixture:ambush-zero", """
            "triggers":[{"id":"zero","window":"turnStartBeforeNormalFlow","subject":"owner","optional":true,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingVirtualSlashTarget"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true}]}]
            """);
            if (quiet)
            {
                var body = JsonSerializer.Serialize(new { triggers = new[] { new { id = "quiet-real-use", window = "drawPhaseStarting", subject = "owner", optional = false,
                    effects = new[] { new { op = "grantTurnCardActionProhibition", target = "owner", cardKinds = new[] { JsonNamingPolicy.CamelCase.ConvertName(kind.ToString()) }, actionTypes = new[] { "use" } } } } } });
                Add("fixture:ambush-quiet", body[1..^1]);
            }
            if (actorSwap) Add("fixture:ambush-swap", """
            "triggers":[{"id":"real-finalized-actor-swap","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["duel"],"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"replaceCurrentCardUseActor","target":"selectedTarget"},{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}]}]
            """, true);
            if (sourceLoss)
            {
                Add("fixture:ambush-loss", """
                "triggers":[{"id":"lose","window":"cardsMoved","subject":"owner","sourceZones":["privatePersistentPile"],"movementOccurrence":"perBatch","movementReasons":["skill-program.named-ambush.remove"],"optional":false,"effects":[{"op":"loseHp","target":"owner","amount":20}]},
                {"id":"lose-after-hand-payment","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.damage-source-hand.discard"],"optional":false,"effects":[{"op":"loseHp","target":"owner","amount":20}]}]
                """);
                Add("fixture:ambush-replace", """
                "triggers":[{"id":"replacement","window":"dyingEntering","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":10,"clampToMaxHp":true},{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:shefu","ol:benyu"],"sourceBind":"fixture:ambush-spent"}]}]
                """, true); b.AddSkill(new("fixture:ambush-spent", "Spent", "Actually removed source"));
            }
            b.AddSkill(new("fixture:ambush-peer", "Peer", "Fixed role choices") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -10000d : 10000d) });
            b.AddGeneral(new("fixture:ambush-owner", "Ambush owner", "supporter", Driver, "wei", 12,
                [Shefu, .. (benyu ? new[] { Benyu } : []), .. (sourceLoss && !deathOwner ? new[] { "fixture:ambush-loss", "fixture:ambush-replace" } : []),
                    .. (actorSwap ? new[] { "fixture:ambush-swap" } : [])]));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:ambush-peer-{i}", "Actual peer", "supporter", "fixture:ambush-peer", "wei", 12,
                [Shefu, .. (zero ? new[] { "fixture:ambush-zero" } : []), .. (reuse && i == 1 ? new[] { "fixture:ambush-recycle", "fixture:ambush-gift" } : []),
                    .. (sourceLoss && deathOwner ? new[] { "fixture:ambush-loss" } : []), .. (quiet ? new[] { "fixture:ambush-quiet" } : []), .. (benyuPeer ? new[] { Benyu } : [])]));
            var cards = mixed ? new[] { "standard:slash", "standard:dodge", "standard:duel", "standard:nullification", "standard:slash" } : [kind switch
            { CardKind.BarbarianAssault => "standard:barbarian_assault", CardKind.Indulgence => "standard:indulgence", CardKind.SupplyShortage => "standard:supply_shortage",
                CardKind.Lightning => "standard:lightning", CardKind.Alcohol => "standard:alcohol", CardKind.DrawTwo => "standard:draw_two", CardKind.Duel => "standard:duel", _ => "standard:slash" }];
            b.AddDeck(new("fixture:ambush-deck", "Fixed real ambush deck", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 100).Select(i => new ContentDeckPhysicalCard(cards[i % cards.Length], Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "Ambush", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:ambush-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:ambush-owner", "fixture:ambush-peer-1", "fixture:ambush-peer-2", "fixture:ambush-peer-3"]));
        }
    }
}
