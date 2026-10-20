using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouZhouYuMobileChecks
{
    public static void FrozenYingziAndActualCoveredGuessCold()
    {
        var s = Scene.Start(); s.Play();
        var issued = s.E<MouZhouYuMobileYingziIssuedEvent>().Single();
        s.Need(issued is { Hand: true, Hp: true, Equipment: true, Count: 3 } && s.V().HandCount == 7 && s.V().HandLimit == 6,
            "Mobile Yingzi freezes all three genuine starting conditions, adds three to real normal Draw2 and issues turn HandLimit+3.");
        var card = s.V().Hand.Single(c => c.Kind == CardKind.Slash && c.Suit == Suit.Spade).Id;
        s.Fanjian(1); s.Reach(() => s.P is { PlayerSeat: 0 } && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "cover"));
        s.Answer(c => c.Cards.SequenceEqual([card]) && c.Parameters.GetValueOrDefault("declared-suit") == "Heart");
        s.Reach(() => s.P is { PlayerSeat: 1 } && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "same"));
        var payment = s.E<MouZhouYuMobileCoveredEvent>().Single();
        var frame = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MouZhouYuMobile is not null);
        s.Need(payment is { ActualSuit: Suit.Spade, DeclaredSuit: Suit.Heart } && payment.CardId == card && payment.From == CardLocation.Hand(0) && payment.To == CardLocation.Processing &&
            frame.MouZhouYuMobile is { Stage: MouZhouYuMobileStage.TargetChoice, Revealed: false, Answer: null } && s.V().Hand.All(c => c.Id != card) &&
            s.P!.Choices.All(c => c.Cards.Count == 0) && Enumerable.Range(0, 4).All(i => s.G.CreateSnapshot(i).ProcessingCardCount == 0 && s.G.CreateSnapshot(i).PublicRevealedCards.All(c => c.Id != card)),
            "The selected real Hand entity is actually covered in Processing before the independent target question; neither card ID nor face leaks in target choices, public cards or public Processing count.");
        s.Cold(); s.Play();
        var answer = s.E<MouZhouYuMobileAnsweredEvent>().Single().Answer; var correct = answer == "different";
        var gift = s.E<MouZhouYuMobileGiftedEvent>().Single();
        s.Need(answer is "same" or "different" && gift.CardId == card && gift.From == CardLocation.Processing && gift.To == CardLocation.Hand(1) &&
            s.G.CardMovements.Any(m => m.Sequence == gift.Sequence && m.CardId == card && m.From == gift.From && m.To == gift.To) &&
            s.E<MouZhouYuMobileCompletedEvent>().Single() is { Paid: true } && s.V(1).Hp == (correct ? 12 : 11) &&
            (correct ? s.E<MouZhouYuMobileDisabledEvent>().Single().Reason == "correct-guess" : s.E<MouZhouYuMobileLossPaidEvent>() is [{ TargetSeat: 1, Before: 12, After: 11 }]),
            "The genuine NPC answers its own hidden-card question, the original entity is revealed and actually received, then a correct guess disables Fanjian or a wrong guess pays native HP Loss1 exactly once.");
        var facts = s.G.Events.Select(e => e.Payload).ToArray();
        var giftIndex = Array.FindIndex(facts, e => e is MouZhouYuMobileGiftedEvent);
        var consequenceIndex = Array.FindIndex(facts, e => e is MouZhouYuMobileDisabledEvent or MouZhouYuMobileLossIssuedEvent);
        s.Need(giftIndex >= 0 && consequenceIndex > giftIndex && s.G.CreateSnapshot(1).Players[1].HandCount == 1,
            "Guess consequences follow real reveal/gift, rather than paying HP before the target receives the card.");
        if (correct) s.Need(!s.Actions().Any(a => a.ProgramSkillId == Scene.FanjianId), "Correct-guess disable blocks further actual-turn activations.");
        else
        {
            s.Fanjian(2); s.Reach(() => s.P is { PlayerSeat: 0 } && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "cover"));
            s.Need(s.P!.Choices.Where(c => c.Cards.Count == 1).All(c => s.V().Hand.Single(x => x.Id == c.Cards[0]).Suit != Suit.Spade),
                "The real covered suit is spent even though the independent declared suit was Heart; repeated Spade payments are not published.");
            s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "cancel"); s.Play();
        }
        s.Driver(); s.Play();
        s.Need(s.V() is { Hp: 1, Equipment.Count: 0, HandLimit: 4 } && s.E<MouZhouYuMobileYingziIssuedEvent>().Single() == issued &&
            s.E<MouZhouYuMobileCoveredEvent>().Length == 1 && s.E<MouZhouYuMobileGiftedEvent>().Length == 1,
            "After real equipment loss and HP Loss2, the issued Yingzi contribution remains three for this turn, independently of now-failed live conditions. Cover and gift never repeat after Cold.");
    }
    private static ShanheStartingCard C(CardKind kind, Suit suit, int rank) => new(kind, suit, rank);
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public const string Hero = "mou:zhou-yu-mobile", FanjianId = "mou-mobile:fanjian";
        private const string DriverId = "fixture:mouzy-mobile-driver", Quiet = "fixture:mouzy-mobile-quiet", Ally = "fixture:mouzy-mobile-ally", EnemyA = "fixture:mouzy-mobile-enemy-a", EnemyB = "fixture:mouzy-mobile-enemy-b", Deck = "fixture:mouzy-mobile-deck";
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public LegalAction[] Actions() => G.GetHumanLegalActions().ToArray();
        public void Fanjian(int target) => Accept(new UseProgramSkillCommand(0, FanjianId, "cover-and-declare", [], [target], G.Revision, P!.PromptId));
        public void Driver() => Accept(new UseProgramSkillCommand(0, DriverId, "change-live-conditions", [], [], G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Answer(Func<PromptChoice, bool> match)
        { var p = P!; var c = p.Choices.FirstOrDefault(match); Need(p.PlayerSeat == 0 && c is not null, "Only a published human-owned choice is answered."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 140; i++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "Unexpected real terminal cutoff at " + line);
                var p = P;
                if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) Accept(new EndPlayPhaseCommand(0, G.Revision, p.PromptId));
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else throw new InvalidOperationException("Unexpected real human child at " + line + Diagnostic());
            }
            throw new InvalidOperationException("Bounded native fixture did not reach " + line + Diagnostic());
        }
        public void Accept(GameCommand command)
        { try { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Command rejected"); } catch (Exception e) { throw new InvalidOperationException(e.Message + Diagnostic(), e); } }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Zones = G.CreateCardZoneDiagnostics(), Moves = G.CardMovements, Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold restores the exact hidden covered entity, chooser, declaration, budget and native parent."); }
        public void Need(bool yes, string why) { if (!yes) throw new InvalidOperationException(why + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, Prompt = P, Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount, V(i).HandLimit, V(i).IsFaceDown }), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.Where(e => e.Payload.GetType().Name.StartsWith("MouZhouYuMobile", StringComparison.Ordinal)).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        public static Scene Start()
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(7, 1, "mouzy-mobile-native", Hero, 3, 0, 0, 0, 0, [DriverId], [C(CardKind.Slash, Suit.Spade, 7), C(CardKind.Dodge, Suit.Heart, 8)], [C(CardKind.Crossbow, Suit.Diamond, 9)],
                [new(EnemyA, Hp: 12) { StartingHand = [] }, new(EnemyB, Hp: 12) { StartingHand = [] }], [new(Ally, Hp: 12) { StartingHand = [] }], new string('c', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId,
                ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, r);
            var s = new Scene(g, r); s.Accept(new StartGameCommand()); return s;
        }
        private sealed class Fixture : IGameContentPackage
        {
            public PackageManifest Manifest { get; } = new("fixture-mou-zhou-yu-mobile-native", new(1, 0, 0));
            public void Register(IContentRegistryBuilder b)
            {
                CharacterFixtureContent.Register(b, Hero);
                foreach (var kind in new[] { CardKind.Slash, CardKind.Dodge, CardKind.Crossbow }) { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:mouzy-mobile-card-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
                var defs = new object[] {
                    new { id = DriverId, revision = 1, activations = new[] { new { id = "change-live-conditions", minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null,
                        effects = new object[] { new { op = "discardOwnedZoneCards", target = "owner", zones = new[] { "equipment" } }, new { op = "loseHp", target = "owner", amount = 2 } } } } },
                    new { id = Quiet, revision = 1, triggers = new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false, effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } }
                };
                var labels = new[] { DriverId, Quiet }.ToDictionary(id => id, id => new { name = id, description = "移动版谋周瑜真实原生夹具" });
                var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
                foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, labels[id].description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
                foreach (var (id, faction) in new[] { (Ally, "wu"), (EnemyA, "wei"), (EnemyB, "wei") }) b.AddGeneral(new(id, "真实同行", "", "standard:none", faction, 12, [Quiet]));
                b.AddDeck(new(Deck, "固定原生库存", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("fixture:mouzy-mobile-card-Dodge", Suit.Club, 7)).ToArray() });
                b.AddMode(new(ShanheBattleSetup.BattleModeId, "移动谋周瑜原生小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team,
                    TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
            }
        }
    }
}
