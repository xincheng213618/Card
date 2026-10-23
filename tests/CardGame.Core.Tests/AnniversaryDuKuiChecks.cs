using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class AnniversaryDuKuiChecks
{
    private const string Hero = "anniversary:du-kui", Fanyin = "anniversary:fanyin", Peiqi = "anniversary:peiqi";
    private const string Driver = "fixture:ann-du-kui-driver", Watch = "fixture:ann-du-kui-watch", Wide = "fixture:ann-du-kui-wide", Deck = "fixture:ann-du-kui-deck";

    public static void RevealedRankLoopNativeUseAndMutualRangeMovesCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(Options(), registry); Accept(g, new StartGameCommand());
        Offer(g, Fanyin); Answer(g, c => Action(c) == "activate"); Menu(g, DuKuiStage.Choice);
        var first = Root(g)!.DuKui!;
        Need(first.Rank == 1 && Kind(g, first.CardId) == CardKind.DrawTwo && Zone(g, first.CardId) == CardLocation.Processing &&
            g.CardMovements.Count(m => m.CardId == first.CardId && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing) == 1 &&
            g.CardMovements.All(m => m.CardId != first.CardId || m.To != CardLocation.Hand(0)),
            "Fanyin actually reveals the current minimum printed rank into public Processing, without first gaining it into Hand.");
        Answer(g, c => Verb(c) == "use");
        Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        var root = Root(g)!;
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.DuKuiOrigin?.FrameId == root.Id);
        Need(root.DuKui is { Stage: DuKuiStage.NativeUse, NativeReturned: false } && use.CardId == first.CardId &&
            use.Action is { Type: CardActionType.Use, EffectiveKind: CardKind.DrawTwo } a && a.TargetSeats.SequenceEqual([0]) &&
            a.PhysicalCards is [{ CardId: var id, From: var from }] && id == first.CardId && from == CardLocation.Processing &&
            E<DuKuiNativeIssuedEvent>(g).Count(e => e.Origin.FrameId == root.Id && e.Origin.CardId == first.CardId) == 1,
            "The published entity enters a genuine native DrawTwo Use, including its implicit self target, before the native before-target-effects child returns. Actual=" +
            JsonSerializer.Serialize(new { Receipt = root.DuKui, FirstCard = first.CardId, Use = new { use.Id, use.CardId, use.CardKind, use.TargetSeats, use.DuKuiOrigin, use.Action },
                Issued = E<DuKuiNativeIssuedEvent>(g), Prompt = new { P(g)!.Kind, P(g)!.SkillPrompt } }));
        g = Cold(g, registry); Continue(g); Menu(g, DuKuiStage.Choice);
        Need(Root(g)!.DuKui!.Rank == 2 && Kind(g, Root(g)!.DuKui!.CardId) == CardKind.Dodge &&
            P(g)!.Choices.All(c => Verb(c) != "use") && Zone(g, first.CardId) == CardLocation.DiscardPile &&
            E<DuKuiNativeReturnedEvent>(g).Count(e => e.UseId == use.Id) == 1,
            "Native DrawTwo finishes once after cold restore; the doubled-rank Dodge cannot be used as a proactive card.");
        var declined = Root(g)!.DuKui!.CardId; Answer(g, c => Verb(c) == "extra"); Menu(g, DuKuiStage.Choice);
        Need(Root(g)!.DuKui!.Rank == 4 && Zone(g, declined) == CardLocation.DiscardPile &&
            P(g)!.Choices.Any(c => Verb(c) == "use" && c.Targets.SequenceEqual([2, 3])) &&
            P(g)!.Choices.All(c => c.Targets.Count <= 2),
            "An unused shown card genuinely leaves Processing and grants exactly one extra legal target to the next revealed Use.");
        var slash = Root(g)!.DuKui!.CardId; var hp2 = V(g, 2).Hp; var hp3 = V(g, 3).Hp;
        Answer(g, c => Verb(c) == "use" && c.Targets.SequenceEqual([2, 3])); Menu(g, DuKuiStage.Choice);
        Need(Root(g)!.DuKui!.Rank == 8 && Kind(g, Root(g)!.DuKui!.CardId) == CardKind.Slash &&
            V(g, 2).Hp == hp2 - 1 && V(g, 3).Hp == hp3 - 1 &&
            E<CardActionAcceptedEvent>(g).Any(e => e.Action.PhysicalCards.Any(c => c.CardId == slash) && e.Action.TargetSeats.SequenceEqual([2, 3])) &&
            P(g)!.Choices.All(c => Verb(c) != "use"),
            "The real Slash reaches the distance-two target, retains both adjusted targets, deals native damage, and consumes the ordinary Slash quota before the rank-eight Slash.");
        Answer(g, c => Verb(c) == "extra"); Play(g);
        Need(E<DuKuiRevealedEvent>(g).Select(e => e.Rank).SequenceEqual([1, 2, 4, 8]) &&
            E<DuKuiRevealedEvent>(g).All(e => g.CardMovements.All(m => m.CardId != e.CardId || m.To.Zone != CardZoneKind.Hand)),
            "The exact doubling chain stops when the next rank is impossible; no shown entity is converted into a fake hand gain.");
        var drawTwo = V(g).Hand.First(c => c.Kind == CardKind.DrawTwo).Id;
        var draw = g.GetHumanLegalActions().First(a => a.CardId == drawTwo && a.Kind == LegalActionKind.DrawTwo && a.TargetSeats.SequenceEqual([0, 1]));
        var ownHand = V(g).HandCount; var allyHand = V(g, 1).HandCount;
        Native(g, draw); Play(g);
        var declared = E<CardUseDeclaredEvent>(g).Single(e => e.CardId == drawTwo);
        Need(V(g).HandCount == ownHand + 1 && V(g, 1).HandCount == allyHand + 2 &&
            E<TargetsConfirmedEvent>(g).Count(e => e.ResolutionId == declared.ResolutionId && e.TargetSeats.SequenceEqual([0, 1])) == 1 &&
            E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == declared.ResolutionId && e.CardId == drawTwo && e.CardKind == CardKind.DrawTwo) == 1 &&
            g.CardMovements.Count(m => m.CardId == drawTwo && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1 &&
            g.CardMovements.Count(m => m.CardId == drawTwo && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
            "The final unused rank-eight entity leaves its nonstacking +1 target for the next actual hand-card Use, which really draws for both recipients. Actual=" +
            JsonSerializer.Serialize(new { CardId = drawTwo, OwnBefore = ownHand, OwnAfter = V(g).HandCount, AllyBefore = allyHand, AllyAfter = V(g, 1).HandCount,
                Accepted = E<CardActionAcceptedEvent>(g).Select(e => e.Action), Declared = E<CardUseDeclaredEvent>(g),
                Targets = E<TargetsConfirmedEvent>(g), NativeDraws = E<NativeCardEffectDrawAppliedEvent>(g),
                Grants = E<DuKuiTargetGrantedEvent>(g), CardMoves = g.CardMovements.Where(m => m.CardId == drawTwo) }));

        var weapon = V(g).Hand.Single(c => c.Kind == CardKind.QinggangSword).Id;
        var armor = V(g).Equipment.Single(c => c.Kind == CardKind.BaguaFormation).Id;
        Use(g, "place", [1]);
        Reach(g, p => p.Kind == DecisionKind.PlayCard || p.Choices.Any(c => c.Cards.SequenceEqual([weapon])));
        if (P(g)!.Kind != DecisionKind.PlayCard) Answer(g, c => c.Cards.SequenceEqual([weapon])); Play(g);
        Need(Zone(g, weapon) == CardLocation.Equipment(1), "The fixed real weapon is first moved to the other character's actual equipment zone.");
        Use(g, "damage", []); Offer(g, Peiqi); Answer(g, c => Action(c) == "activate"); Menu(g, DuKuiStage.MoveChoice);
        Answer(g, c => Verb(c) == "field" && c.Cards.SequenceEqual([weapon]) && c.Targets.SequenceEqual([0])); Menu(g, DuKuiStage.MoveChoice);
        Need(Root(g)!.DuKui!.Moves == 1 && Zone(g, weapon) == CardLocation.Equipment(0) && E<DuKuiFieldMovedEvent>(g).Length == 1 &&
            P(g)!.Choices.Any(c => Verb(c) == "field" && c.Cards.SequenceEqual([armor]) && c.Targets.SequenceEqual([3])),
            "Only after the first real field move installs the range-two sword does the all-players mutual-range condition allow a second field move.");
        Answer(g, c => Verb(c) == "field" && c.Cards.SequenceEqual([armor]) && c.Targets.SequenceEqual([3])); Play(g);
        Need(V(g).Hp == 2 && Zone(g, armor) == CardLocation.Equipment(3) &&
            E<DuKuiFieldMovedEvent>(g).Select(e => (e.Index, e.CardId, e.To)).SequenceEqual(new[] { (1, weapon, CardLocation.Equipment(0)), (2, armor, CardLocation.Equipment(3)) }) &&
            g.CardMovements.Count(m => m.CardId == weapon && m.Reason.Value == "anniversary.peiqi.move") == 1 &&
            g.CardMovements.Count(m => m.CardId == armor && m.Reason.Value == "anniversary.peiqi.move") == 1,
            "Peiqi completes exactly two genuine legal field moves after actual damage; each entity moves once and the original damage remains paid.");
        Need(State(g) == State(GameReplay.Replay(Options(), g.AcceptedCommands, registry)),
            "The rank RNG, published Processing entity, implicit-target native Use, cold return, real Slash quota, extra-target grant and range-dependent field moves replay from commands.");
    }

    private static GameOptions Options()
    {
        var setup = new ShanheBattleSetup(7, 1, "anniversary-du-kui", Hero, 3, 0, 0, 0, 0, [Driver, Watch],
            [new(CardKind.DrawTwo, Suit.Heart, 6), new(CardKind.QinggangSword, Suit.Spade, 6)], [new(CardKind.BaguaFormation, Suit.Club, 2)],
            [new(Peer(2)) { StartingHand = [] }, new(Peer(3)) { StartingHand = [] }], [new(Peer(1)) { StartingHand = [] }], new string('c', 64));
        return new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 4 };
    }
    private static string Peer(int seat) => "fixture:ann-du-kui-peer-" + seat;
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static ProgramSkillFrame? Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.DuKui is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static CardLocation Zone(GameEngine g, int id) => g.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
    private static CardKind Kind(GameEngine g, int id) => E<CardsRevealedEvent>(g).SelectMany(e => e.Cards).Last(c => c.Id == id).Kind;
    private static string? Action(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private static string? Verb(PromptChoice c) => c.Parameters.GetValueOrDefault("verb");
    private static void Need(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected native Du Kui command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; var c = p.Choices.FirstOrDefault(pick); Need(c is not null, "Missing exact Du Kui choice: " + JsonSerializer.Serialize(p)); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Use(GameEngine g, string activation, int[] targets) => Accept(g, new UseProgramSkillCommand(0, Driver, activation, [], targets, g.Revision, P(g)!.PromptId));
    private static void Native(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId)
        { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Menu(GameEngine g, DuKuiStage stage) => Reach(g, p => Root(g)?.DuKui?.Stage == stage && p.Choices.Any(c => Action(c) == "anniversary-du-kui"));
    private static void Offer(GameEngine g, string skill) => Reach(g, p => p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => Action(c) == "activate"));
    private static void Reach(GameEngine g, Func<PendingDecision, bool> ready)
    {
        for (var n = 0; n < 160; n++)
        {
            if (P(g) is { } p)
            {
                if (ready(p)) return;
                if (p.Kind == DecisionKind.SelectFaction) { Answer(g, _ => true); continue; }
                if (p.SkillPrompt?.SkillId == Watch) { Continue(g); continue; }
                if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Du Kui boundary: " + JsonSerializer.Serialize(new { Prompt = p, Root = Root(g)?.DuKui,
                    Hp = Enumerable.Range(0, 4).Select(s => V(g, s).Hp), Revealed = E<DuKuiRevealedEvent>(g), Issued = E<DuKuiNativeIssuedEvent>(g), Returned = E<DuKuiNativeReturnedEvent>(g) }));
            }
            Need(g.State.Winner == Winner.None, "The small Du Kui scene must keep its genuine native boundary."); Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Du Kui missed its bounded native boundary.");
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(cold), "Du Kui cold restore keeps the published card, native Use receipt, actual child, all zones and viewer snapshots."); return cold; }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        Zones = g.CreateCardZoneDiagnostics(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands) });

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-du-kui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"place","targetKind":"otherLiving","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment","awaitMovementTriggers":true}]},
                {"id":"damage","targetKind":"anyLiving","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"published-native-use","window":"cardUseBeforeTargetEffects","ownerRelation":"actor","cardKinds":["drawTwo"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"native-use","options":[{"id":"continue"}]}]}]},
              {"id":"{{Wide}}","revision":1,"modifiers":[{"id":"native-peer-range","query":"attackRange","operation":"set","value":4,"priority":0}]}]}
            """;
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实场上移动驱动", description = "原生装备转交与实际受伤。" },
                [Watch] = new { name = "原生使用子窗", description = "在公开实体的真实原生使用中暂停。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Wide] = new { name = "固定真实攻击范围", description = "其他角色的公开攻击范围为四。" } };
            var c = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = p, ProgramPresentation = c.Presentations[id] });
            for (var seat = 1; seat < 4; seat++) b.AddGeneral(new(Peer(seat), "真实场上角色", "supporter", Wide, "wei", 8));
            b.AddDeck(new(Deck, "固定点数倍增牌堆", 0, 0, []) { PhysicalCards = new[] { ("standard:draw_two", 1), ("standard:dodge", 2), ("standard:slash", 4), ("standard:slash", 8) }
                .SelectMany(x => Enumerable.Range(0, 8).Select(_ => new ContentDeckPhysicalCard(x.Item1, Suit.Spade, x.Item2))).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "周年杜夔真实亮牌与移牌", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer(1), Peer(2), Peer(3)], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
