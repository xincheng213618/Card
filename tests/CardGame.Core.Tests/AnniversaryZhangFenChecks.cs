using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class AnniversaryZhangFenChecks
{
    private const string Hero = "anniversary:zhang-fen", Cart = "anniversary:dagong-che", Upgrade = "anniversary:xianzhu";
    private const string Watch = "fixture:ann-zhang-fen-watch", Deck = "fixture:ann-zhang-fen-deck";

    public static void RealCartUpgradesInsertedPlayDestructionAndNativeReturnsCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(Options(), registry); Accept(g, new StartGameCommand());
        Offer(g, Cart);
        var made = E<DagongCheCreatedEvent>(g).Single(); var cart = made.CardId; var firstTurn = E<TurnStartedEvent>(g).Last().TurnNumber;
        Need(Zone(g, cart) == CardLocation.Equipment(0) && V(g).Equipment.Single(c => c.Id == cart) is { Kind: CardKind.DagongChe, Suit: Suit.Spade, Rank: 9 } &&
            g.CardMovements.Count(m => m.CardId == cart && m.From == CardLocation.OutsideGame && m.To == CardLocation.Equipment(0)) == 1,
            "Wanglu places one genuine generated Spade-nine treasure in Preparation before the actual Play-start trigger.");
        Answer(g, c => Action(c) == "activate"); Menu(g, ZhangFenStage.Targets);
        Answer(g, c => c.Targets.SequenceEqual([3])); Offer(g, Upgrade);
        var nativeUse = E<DagongCheSlashIssuedEvent>(g).Single().Origin;
        Need(V(g, 3).Hp == 7 && g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == nativeUse.UseId) is
            { CardId: 0, PhysicalCardIds.Count: 0, Action.Type: CardActionType.Use } use && use.DagongCheSlash == nativeUse,
            "The equipped cart issues a genuine zero-material Slash and actual damage before its source's optional upgrade.");
        Answer(g, c => Action(c) == "activate"); Menu(g, ZhangFenStage.Upgrade);
        g = Cold(g, registry); Answer(g, c => Option(c) == "range"); Menu(g, ZhangFenStage.DiscardChoice);
        Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == "Hand"); Play(g);
        Need(E<DagongCheUpgradedEvent>(g) is [{ CardId: var upgraded, Ordinal: 1, Choice: "range" }] && upgraded == cart &&
            g.CardMovements.Count(m => m.From == CardLocation.Hand(3) && m.Reason.Value == "anniversary.dagong-che.discard") == 1 &&
            E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == nativeUse.UseId && e.CardId == 0) == 1 &&
            g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([3])),
            "The paid original-use return discards exactly one real opaque hand card; the free cart Slash does not spend the ordinary Slash quota.");
        var slash = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([3]));
        Native(g, slash); Offer(g, Upgrade); Answer(g, c => Action(c) == "activate"); Menu(g, ZhangFenStage.Upgrade);
        Need(P(g)!.Choices.All(c => Option(c) != "range"), "The binary distance-and-armor upgrade cannot be repeatedly selected on the same real cart.");
        Answer(g, c => Option(c) == "discard"); Play(g);
        Need(V(g, 3).Hp == 6 && E<DagongCheUpgradedEvent>(g).Select(e => e.Choice).SequenceEqual(["range", "discard"]) &&
            g.CardMovements.Count(m => m.From == CardLocation.Hand(3) && m.Reason.Value == "anniversary.dagong-che.discard") == 1 &&
            V(g).Equipment.Single(c => c.Id == cart).SkillMarkLabel is { } label && label.Contains("升级2/5", StringComparison.Ordinal) && label.Contains("弃牌2", StringComparison.Ordinal),
            "An ordinary physical Slash also upgrades the held cart, without pretending to be its native-use discard effect; the actual card exposes its upgrade state.");
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Offer(g, Cart);
        Need(E<TurnStartedEvent>(g).Last(e => e.ActorSeat == 0).TurnNumber > firstTurn &&
            E<ProgramPhaseScheduledEvent>(g).Count(e => e.SkillId == "anniversary:wanglu" && e.Started) == 1 &&
            E<DagongCheCreatedEvent>(g).Length == 1 && Zone(g, cart) == CardLocation.Equipment(0),
            "Keeping the genuine cart grants a real extra Play during the next Preparation instead of creating another entity.");
        Answer(g, c => Action(c) == "activate"); Menu(g, ZhangFenStage.Targets);
        Need(P(g)!.Choices.Any(c => c.Targets.SequenceEqual([2])), "The installed range upgrade admits the real distance-two enemy on the next cart use.");
        Answer(g, c => c.Targets.SequenceEqual([3])); Offer(g, Upgrade); Answer(g, c => Action(c) == "activate"); Menu(g, ZhangFenStage.Upgrade);
        Answer(g, c => Option(c) == "target"); Menu(g, ZhangFenStage.DiscardChoice);
        Need(Root(g)!.ZhangFen!.Count == 2, "The issued cart hit uses its actual upgraded discard count, rather than silently retaining the initial one-card effect.");
        Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == "Hand"); Menu(g, ZhangFenStage.DiscardChoice);
        Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == "Hand"); Play(g);
        var before = V(g).HandCount; var ox = V(g).Hand.Single(c => c.Kind == CardKind.WoodenOx).Id;
        var equip = g.GetHumanLegalActions().Single(a => a.CardId == ox);
        Native(g, equip); Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        var destroyed = E<DagongCheDestroyedEvent>(g).Single();
        Need(destroyed.CardId == cart && destroyed.Upgrades == 3 && Zone(g, cart) == CardLocation.OutsideGame &&
            V(g).HandCount == before - 1 + 3 && E<ZhangFenDrawReturnedEvent>(g) is [{ Requested: 3, Actual: 3 }] &&
            Root(g)!.ZhangFen is { Stage: ZhangFenStage.DrawChildren, Count: 3 },
            "Replacing the upgraded treasure genuinely destroys it and pays exactly three native draw cards before the movement child's response.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(Zone(g, ox) == CardLocation.Equipment(0) && Zone(g, cart) == CardLocation.OutsideGame &&
            g.CardMovements.Count(m => m.CardId == cart && m.From == CardLocation.Equipment(0) && m.To == CardLocation.OutsideGame) == 1 &&
            E<ZhangFenDrawReturnedEvent>(g).Length == 1,
            "Restoring the paid draw child resumes the original equipment use without recreating the destroyed cart or drawing twice.");
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Play(g);
        Need(E<ProgramPhaseScheduledEvent>(g).Count(e => e.SkillId == "anniversary:wanglu" && !e.Started) == 1 &&
            E<DagongCheCreatedEvent>(g).Length == 1 && Zone(g, ox) == CardLocation.Equipment(0),
            "Ending the inserted Play returns to the genuine normal turn flow and normal Play without rerunning Wanglu or creating a fresh cart mid-turn.");
        Need(State(g) == State(GameReplay.Replay(Options(), g.AcceptedCommands, registry)),
            "All commands replay the generated entity, cold native Slash upgrade, real discard invoice, ordinary Slash quota, inserted phase and once-paid destruction draw.");
    }

    private static GameOptions Options()
    {
        var cards = Enumerable.Range(2, 4).Select(n => new ShanheStartingCard(CardKind.Nullification, Suit.Club, n)).ToArray();
        var setup = new ShanheBattleSetup(7, 1, "anniversary-zhang-fen", Hero, 4, 0, 0, 0, 0, [Watch],
            [new(CardKind.Slash, Suit.Heart, 7), new(CardKind.WoodenOx, Suit.Club, 6)], [],
            [new(Peer(2)) { StartingHand = cards }, new(Peer(3)) { StartingHand = cards }], [new(Peer(1)) { StartingHand = [] }], new string('b', 64));
        return new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 8 };
    }
    private static string Peer(int seat) => "fixture:ann-zhang-fen-peer-" + seat;
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static ProgramSkillFrame? Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.ZhangFen is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static CardLocation Zone(GameEngine g, int id) => g.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
    private static string? Action(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private static string? Option(PromptChoice c) => c.Parameters.GetValueOrDefault("option");
    private static void Need(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected native Zhang Fen command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; var c = p.Choices.FirstOrDefault(pick); Need(c is not null, "Missing exact Zhang Fen choice: " + JsonSerializer.Serialize(p)); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Native(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Menu(GameEngine g, ZhangFenStage stage) => Reach(g, p => Root(g)?.ZhangFen?.Stage == stage && p.Choices.Any(c => Action(c) == "anniversary-zhang-fen"));
    private static void Offer(GameEngine g, string skill) => Reach(g, p => p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => Action(c) == "activate"));
    private static void Reach(GameEngine g, Func<PendingDecision, bool> ready)
    {
        for (var n = 0; n < 140; n++)
        {
            if (P(g) is { } p)
            {
                if (ready(p)) return;
                if (p.SkillPrompt?.SkillId == Watch) { Continue(g); continue; }
                if (p.Kind == DecisionKind.SelectFaction) { Answer(g, _ => true); continue; }
                if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Zhang Fen boundary: " + JsonSerializer.Serialize(new { Prompt = p, Root = Root(g)?.ZhangFen,
                    Created = E<DagongCheCreatedEvent>(g), Upgraded = E<DagongCheUpgradedEvent>(g), Destroyed = E<DagongCheDestroyedEvent>(g), Started = E<ZhangFenStartedEvent>(g) }));
            }
            Need(g.State.Winner == Winner.None, "Zhang Fen must retain its genuine native boundary."); Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Zhang Fen missed the bounded native boundary.");
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(restored), "Cold restore preserves the original real cart, exact native Use/draw receipt, child and all views."); return restored; }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        Zones = g.CreateCardZoneDiagnostics(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-zhang-fen", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            b.AddCard(new("fixture:ann-zhang-fen-ox", "木牛流马", "装备", "真实宝物替换。", CardKind.WoodenOx));
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"chaixie-paid","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["anniversary.chaixie.draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"native-draw-paid","options":[{"id":"continue"}]}]}]}]}
            """;
            var c = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Watch] = new { name = "原生摸牌已付子窗", description = "在真实拆械摸牌之后暂停。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } }));
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = p, ProgramPresentation = c.Presentations[id] });
            for (var seat = 1; seat < 4; seat++) b.AddGeneral(new(Peer(seat), "真实无杀目标", "supporter", Watch, "wei", 8));
            b.AddDeck(new(Deck, "固定无反击牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 70).Select(i => new ContentDeckPhysicalCard("standard:nullification", Suit.Club, 2 + i % 4)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "周年张奋真实大攻车", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer(1), Peer(2), Peer(3)], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
