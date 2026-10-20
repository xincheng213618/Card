using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouDengAiOlChecks
{
    private const string Hero = "mou:deng-ai-ol", Owner = "fixture:mda-owner", Driver = "fixture:mda-driver", Quiet = "fixture:mda-quiet", Ally = "fixture:mda-ally", EnemyA = "fixture:mda-enemy-a", EnemyB = "fixture:mda-enemy-b", Deck = "fixture:mda-deck";
    public static void NativeGuExchangeAndJiewan()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(7, 1, "mou-deng-ai", Owner, 4, 0, 0, 0, 0, [],
            [new(CardKind.Dodge, Suit.Club, 2), new(CardKind.Slash, Suit.Spade, 3), new(CardKind.Dodge, Suit.Club, 4), new(CardKind.Dodge, Suit.Spade, 5)], [],
            [new(EnemyA, Hp: 5) { StartingHand = [new(CardKind.Dodge, Suit.Club, 6), new(CardKind.Dodge, Suit.Spade, 7), new(CardKind.Dodge, Suit.Club, 8)] },
             new(EnemyB, Hp: 5) { StartingHand = [new(CardKind.Dodge, Suit.Club, 9), new(CardKind.Dodge, Suit.Spade, 10), new(CardKind.Dodge, Suit.Club, 11)] }],
            [new(Ally, Hp: 5) { StartingHand = [new(CardKind.Dodge, Suit.Club, 6), new(CardKind.Dodge, Suit.Spade, 7), new(CardKind.Dodge, Suit.Club, 8)] }], new string('a', 64));
        var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        PendingDecision? P() => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        T[] E<T>() => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
        MouDengAiOlReceipt? R() => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.MouDengAiOl;
        string Diagnostic() => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.CurrentSeat, g.State.Phase, Prompt = P(), Players = g.CreateSnapshot(0).Players,
            Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = g.Events.Where(e => e.Payload.GetType().Name.StartsWith("MouDengAi", StringComparison.Ordinal) || e.Payload is CardUseDeclaredEvent or CardUseFinishedEvent).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message + Diagnostic()); }
        void Accept(GameCommand command) { var r = g.Submit(command); Need(r.Accepted, r.Error?.Message ?? "Native command rejected."); }
        void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P()!; Need(p.PlayerSeat == 0, "Only the real human answers.");
            var c = p.Choices.FirstOrDefault(predicate); Need(c is not null, "Expected actual prompt choice absent.");
            Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, g.Revision));
        }
        void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 220; i++)
            {
                if (done()) return; Need(g.State.Winner == Winner.None, "Game ended before main flow at " + line);
                if (P() is not { PlayerSeat: 0 } p) { Accept(new AdvanceOneStepCommand(g.Revision)); continue; }
                if (p.Kind == DecisionKind.SelectTargetCard) { Answer(c => true); continue; }
                if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification) { Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("response") is not "bagua"); continue; }
                throw new InvalidOperationException("Unexpected human input at " + line + Diagnostic());
            }
            throw new InvalidOperationException("Bounded native Deng Ai flow not reached at " + line + Diagnostic());
        }
        void Stage(MouDengAiOlStage stage) => Reach(() => P() is { PlayerSeat: 0 } && R()?.Stage == stage);
        void Branch(string branch) => Answer(c => c.Parameters.GetValueOrDefault("branch") == branch);
        void Snatch(int target)
        {
            Stage(MouDengAiOlStage.Material); Branch("material"); Stage(MouDengAiOlStage.Snatch);
            Answer(c => c.Parameters.GetValueOrDefault("branch") == "snatch" && c.Targets[0] == target);
        }
        Accept(new StartGameCommand()); Stage(MouDengAiOlStage.Cost); Branch("maxhp"); Snatch(3);
        Reach(() => P() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Need(E<MouDengAiSnatchReturnedEvent>().Length == 1 && E<MouDengAiGuCollectedEvent>().Length == 1 && g.CreateSnapshot(0).Players[0] is { MaxHp: 4, Hp: 4 },
            "Real preparation cost reduces maximum HP, converts exactly one Hand card to a native Snatch, returns, then collects the non-Heart used material outside its own Play.");
        var slash = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([3]));
        var slashId = slash.CardId!.Value; Accept(new PlayCardCommand(0, slashId, slash.TargetSeats, g.Revision, P()!.PromptId, slash.PlayedCardKind, slash.TargetCardId) { ConversionSource = slash.ConversionSource });
        Reach(() => P() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Need(E<MouDengAiGuCollectedEvent>().Length == 2 && E<MouDengAiGuCollectedEvent>().All(e => e.CardId != slashId),
            "The opponent's genuine Dodge Use is outside that actor's Play and becomes Gu, while the original own-Play Slash remains discarded.");
        var hand = g.CreateSnapshot(0).Players[0].Hand.Select(c => c.Id).ToArray(); Need(hand.Length >= 3, "A genuine current Hand supports the small balancing discard.");
        foreach (var id in hand.Take(hand.Length - 2))
        {
            Accept(new UseProgramSkillCommand(0, Driver, "discard", [id], [], g.Revision, P()!.PromptId)); Reach(() => P() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        }
        Accept(new EndPlayPhaseCommand(0, g.Revision, P()!.PromptId));
        Stage(MouDengAiOlStage.ExchangeHand);
        Need(E<MouDengAiGrownEvent>() is [{ PreviousMaximumHp: 4, MaximumHp: 5 }] && g.CreateSnapshot(0).Players[0].Hp == 4,
            "The actual ending equality increases maximum HP without recovering HP; the next foreign equal-maximum turn opens exchange.");
        Branch("hand"); Branch("confirm"); Stage(MouDengAiOlStage.ExchangeGu);
        var view = SnapshotJson.Serialize(g.CreateSnapshot(0)); var beforeExchange = E<MouDengAiExchangeEvent>().Length;
        g = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(view == SnapshotJson.Serialize(g.CreateSnapshot(0)) && R() is { Stage: MouDengAiOlStage.ExchangeGu }, "Representative pending exchange Cold preserves exact selected Hand entities.");
        Branch("gu"); Stage(MouDengAiOlStage.Cost); Branch("maxhp"); Snatch(2);
        Stage(MouDengAiOlStage.Cost);
        Need(E<MouDengAiExchangeEvent>().Length == beforeExchange + 1 && E<MouDengAiDistanceFrozenEvent>().Select(e => e.Distance).SequenceEqual([1, 2]) && E<MouDengAiSnatchReturnedEvent>().Length == 2,
            "The foreign Preparation exchanges real entities and issues distance2 Snatch against the actual far target after the maximum-HP cost children.");
        Branch("remove"); Stage(MouDengAiOlStage.RemoveGu); Branch("gu"); Branch("gu"); Snatch(3);
        Reach(() => E<MouDengAiGrownEvent>().Length == 2);
        var removed = E<MouDengAiCostPaidEvent>().Where(e => e.Cost == "remove-gu").ToArray();
        Need(removed.Length == 2 && removed.All(e => g.CardMovements.Any(m => m.CardId == e.CardId && m.To == CardLocation.OutsideGame && m.Sequence == e.Sequence)) &&
            E<MouDengAiDistanceFrozenEvent>().Select(e => e.Distance).SequenceEqual([1, 2, 1]) && E<MouDengAiSnatchReturnedEvent>().Length == 3 && E<MouDengAiSnatchIssuedEvent>().All(e => E<CardUseFinishedEvent>().Count(f => f.ResolutionId == e.Origin.UseId) == 1) &&
            E<MouDengAiGuCollectedEvent>().Length == 4 && E<MouDengAiGrownEvent>().Last() is { PreviousMaximumHp: 4, MaximumHp: 5 },
            "Two actual Gu leave the game without discarding, X freezes after removal at1, the genuine native Snatch returns once, and foreign actual Ending also grows maximum HP without a fake recovery.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-deng-ai-ol", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            foreach (var k in new[] { CardKind.Dodge, CardKind.Slash, CardKind.Snatch }) { var c = CardCatalog.Get(k); b.AddCard(new("fixture:mda-card-" + k, c.DisplayName, c.CategoryName, c.Description, k)); }
            var programs = new object[]
            {
                new { id = Quiet, revision = 1, triggers = new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false, effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } },
                new { id = Driver, revision = 1, activations = new[] { new { id = "discard", minCards = 1, maxCards = 1, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", sourceZones = new[] { "hand" }, usesPerTurn = (int?)null, effects = new[] { new { op = "discardSelected", target = "owner", amount = 1 } } } } }
            };
            var labels = new Dictionary<string, object> { [Quiet] = new { name = "安静同行", description = "跳过真实出牌阶段" }, [Driver] = new { name = "整理", description = "真实弃一张手牌" } };
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = programs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Owner, "谋邓艾主流程", "mou-deng-ai-ol", "mou-ol:jigu", "wei", 5, ["mou-ol:jiewan", Driver]) { InitialHp = 4 });
            foreach (var peer in new[] { Ally, EnemyA, EnemyB }) b.AddGeneral(new(peer, "真实同行", "supporter", Quiet, "wei", 5));
            b.AddDeck(new(Deck, "固定小牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 28).Select(_ => new ContentDeckPhysicalCard("fixture:mda-card-Dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋邓艾原生主流程", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
