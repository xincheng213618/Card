using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouCaoShuangAnniversaryChecks
{
    private const string Hero = "mou:cao-shuang", Plain = "fixture:mcs-plain", Quiet = "fixture:mcs-quiet", Driver = "fixture:mcs-incoming", Deck = "fixture:mcs-deck";
    public static void ActualUseItemsRecastColdDeletionAwakeningAndHistoricalTarget()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var hand = Enumerable.Range(1, 12).Select(n => new ShanheStartingCard(CardKind.DrawTwo, Suit.Club, n)).ToList();
        hand.Add(new(CardKind.IronChain, Suit.Heart, 7)); hand.Add(new(CardKind.IronChain, Suit.Spade, 8));
        var setup = new ShanheBattleSetup(7, 1, "mou-cao-shuang-native", Hero, 4, 0, 0, 0, 0, [Driver], hand, [],
            [new(Plain, Hp: 20) { StartingHand = [] }, new(Plain, Hp: 20) { StartingHand = [] }], [new(Plain, Hp: 20) { StartingHand = [] }], new string('d', 64));
        var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
            UseInteractiveSetup = false, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 16 }, registry);
        PendingDecision? Prompt() => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        PlayerSnapshot Player(int seat) => game.CreateSnapshot(0).Players[seat];
        T[] Events<T>() => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
        MouCaoShuangAnniversaryReceipt? Receipt() => game.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.MouCaoShuangAnniversary is not null)?.MouCaoShuangAnniversary;
        string Diagnostic() => JsonSerializer.Serialize(new { game.State.TurnNumber, game.State.CurrentSeat, game.State.Phase, Prompt = Prompt(), Receipt = Receipt(),
            Players = game.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.GeneralId, p.Hp, p.MaxHp, p.HandCount }),
            Frames = game.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = game.Events.TakeLast(30).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why + Diagnostic()); }
        void Submit(GameCommand command)
        {
            try { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
            catch (Exception ex) { throw new InvalidOperationException(ex.Message + Diagnostic(), ex); }
        }
        void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 180; i++)
            {
                if (done()) return; Need(game.State.Winner == Winner.None, "Unexpected terminal result at " + line);
                if (Prompt() is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards } discard)
                {
                    var ids = discard.ValidCardIds.OrderBy(id => Player(0).Hand.Single(c => c.Id == id).Kind == CardKind.IronChain ? 1 : 0).Take(discard.RequiredCardCount).ToArray();
                    Submit(new DiscardCardsCommand(0, ids, discard.PromptId, game.Revision)); continue;
                }
                if (Prompt()?.PlayerSeat == 0 && Receipt() is { Operation: SkillProgramEffectOp.ResolveMouCaoShuangFanshi } awakening)
                {
                    if (awakening.Stage == MouCaoShuangAnniversaryStage.Target) { Answer(c => c.Targets.SequenceEqual([3])); continue; }
                    if (awakening.Stage == MouCaoShuangAnniversaryStage.Cards) { Material(); continue; }
                }
                if (Prompt() is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected actual Human input at " + line + Diagnostic());
                Submit(new AdvanceOneStepCommand(game.Revision));
            }
            throw new InvalidOperationException("Bounded native Jianzhuan flow not reached at " + line + Diagnostic());
        }
        void Answer(Func<PromptChoice, bool> match)
        {
            var p = Prompt(); Need(p?.PlayerSeat == 0, "Only the actual published Human input is answered.");
            var choice = p!.Choices.FirstOrDefault(match); Need(choice is not null, "Required current Cao Shuang choice missing.");
            Submit(new AnswerPromptCommand(0, p.PromptId, choice!.Id, game.Revision));
        }
        void Play() => Reach(() => Prompt() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        void Material() => Answer(c => c.Cards is [var id] && Player(0).Hand.Any(h => h.Id == id && h.Kind == CardKind.DrawTwo));
        void ResolveItem()
        {
            while (Receipt() is { } r && Prompt()?.PlayerSeat == 0)
            {
                if (r.Stage == MouCaoShuangAnniversaryStage.Target) { Answer(c => c.Targets.SequenceEqual([3])); continue; }
                if (r.Stage == MouCaoShuangAnniversaryStage.Cards) { Material(); continue; }
                throw new InvalidOperationException("Unexpected native item input." + Diagnostic());
            }
            Play();
        }
        void UseItem(int item)
        {
            var id = Player(0).Hand.First(c => c.Kind == CardKind.DrawTwo).Id;
            var action = game.GetHumanLegalActions().Single(a => a.CardId == id && a.Kind == LegalActionKind.DrawTwo && a.ConversionSource is null);
            Submit(new PlayCardCommand(0, id, action.TargetSeats, game.Revision, Prompt()!.PromptId));
            Reach(() => Receipt()?.Stage == MouCaoShuangAnniversaryStage.Option && Prompt()?.PlayerSeat == 0);
            Answer(c => c.Parameters.GetValueOrDefault("item") == item.ToString());
        }
        void Incoming(int source)
        {
            Submit(new UseProgramSkillCommand(0, Driver, "incoming", [], [source], game.Revision, Prompt()!.PromptId)); Play();
        }
        bool Offer(string skill) => Prompt() is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill);
        void Fudou(bool red, int target)
        {
            var chain = Player(0).Hand.Single(c => c.Kind == CardKind.IronChain && (c.Suit is Suit.Heart or Suit.Diamond) == red);
            var action = game.GetHumanLegalActions().First(a => a.CardId == chain.Id && a.ConversionSource is null && a.TargetSeats.SequenceEqual([target]));
            Submit(new PlayCardCommand(0, chain.Id, action.TargetSeats, game.Revision, Prompt()!.PromptId)); Reach(() => Offer("mou:fudou"));
            Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == "mou:fudou"); Play();
        }
        Submit(new StartGameCommand()); Play();
        Need(Player(0).Skills?.All(s => s.Id != "mou:fudou") == true && Player(3).HandCount == 0, "Fudou is initially derived only and the first actual affected holder has no Hand or equipment.");
        UseItem(1); ResolveItem();
        Need(Events<MouCaoShuangAnniversaryItemCompletedEvent>() is [{ Option: 1, X: 1, Payer: 3, Actual: 0 }] && Events<MouCaoShuangAnniversaryMaterialEvent>().Length == 0,
            "The user-authorized empty holder still permits item1 and completes zero real discards without fabricating payment.");
        UseItem(3); Reach(() => Receipt()?.Stage == MouCaoShuangAnniversaryStage.Cards && Prompt()?.PlayerSeat == 0); Material();
        Need(Receipt() is { X: 2, Required: 2, Cards.Count: 1 }, "The second true Use freezes X2 and a partial two-Hand-entity recast selection.");
        var before = SnapshotJson.Serialize(game.CreateSnapshot(0));
        game = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(before == SnapshotJson.Serialize(game.CreateSnapshot(0)) && Receipt()?.Cards.Count == 1 && Events<CardRecastEvent>().Length == 0,
            "Cold retains the exact private partial recast selection before physical movement without issuing payment or draw.");
        ResolveItem();
        var recast = Events<MouCaoShuangAnniversaryMaterialEvent>().Where(e => e.Recast).ToArray();
        Need(recast.Length == 2 && recast.All(e => e.From == CardLocation.Hand(0)) && Events<CardRecastEvent>().Length == 2 && Events<MouCaoShuangAnniversaryDrawReturnedEvent>().Any(e => e.Requested == 2 && e.Actual == 2),
            "The exact two Hand entities really recast, each native recast fact occurs once and the typed native draw returns two actual cards.");
        UseItem(2); ResolveItem(); UseItem(4); ResolveItem();
        Need(Events<MouCaoShuangAnniversaryOptionEvent>().Select(e => (e.Option, e.X)).SequenceEqual([(1,1),(3,2),(2,3),(4,4)]), "Each option executes once and X counts accepted actual Uses of this phase.");
        Incoming(3); Need(Player(0).Hp == 3 && Events<DamageAppliedEvent>().Any(e => e.SourceSeat == 3 && e.TargetSeat == 0 && e.Amount == 1), "The future black target has actual sourced Damage history before awakening.");
        for (var phase = 0; phase < 3; phase++)
        {
            if (phase > 0)
            {
                var deleted = Events<MouCaoShuangAnniversaryDeletedEvent>().Aggregate(15, (mask, e) => mask & ~(1 << (e.Option - 1)));
                var available = Enumerable.Range(1, 4).Where(i => (deleted & 1 << (i - 1)) != 0).ToArray();
                foreach (var item in available) { UseItem(item); ResolveItem(); }
            }
            var turn = game.State.TurnNumber; Submit(new EndPlayPhaseCommand(0, game.Revision, Prompt()!.PromptId)); Play();
            var deletions = Events<MouCaoShuangAnniversaryDeletedEvent>();
            Need(deletions.Length == phase + 1 && deletions.Last().BeforeMask != deletions.Last().AfterMask && Events<TurnEndedEvent>().Any(e => e.ActorSeat == 0 && e.TurnNumber == turn),
                "Only a genuine completed owner Play deletes exactly one actual remaining item, followed by a real TurnEnded.");
        }
        var awakened = Events<MouCaoShuangAnniversaryAwakenedEvent>().Single();
        Need(Events<MouCaoShuangAnniversaryItemCompletedEvent>().Count(e => e.FrameId == awakened.FrameId && e.X == 1) == 3 && Player(0).MaxHp == 6 && Player(0).Hp == 5 &&
            Player(0).Skills?.Any(s => s.Id == "mou:jianzhuan") == false && Player(0).Skills?.Any(s => s.Id == "mou:fudou") == true && Events<MouCaoShuangAnniversaryGrantedEvent>().Length == 1,
            "Three actual completed phases leave one item; Fanshi executes it three times at X1 before native maxHP+2 and recovery2, then genuinely loses Jianzhuan and grants Fudou once.");
        var ownerHand = Player(0).HandCount; var redPeerHand = Player(2).HandCount; Fudou(true, 2);
        Need(Player(0).HandCount == ownerHand && Player(2).HandCount == redPeerHand + 1 && Events<MouCaoShuangAnniversaryStartedEvent>().Any(e => e.Operation == SkillProgramEffectOp.ResolveMouCaoShuangFudou && e.Other == 2 && e.Red),
            "A real red unique-other-target use draws1 for each role only where that target never actually damaged the owner; paying the original card offsets the owner's draw.");
        var hp = Player(0).Hp; var peerHp = Player(3).Hp; Fudou(false, 3);
        Need(Player(0).Hp == hp - 1 && Player(3).Hp == peerHp - 1 && Events<ProgramSkillHpLostEvent>().Count(e => e.SkillId == "mou:fudou" && e.Amount == 1) == 2 &&
            Events<MouCaoShuangAnniversaryStartedEvent>().Any(e => e.Operation == SkillProgramEffectOp.ResolveMouCaoShuangFudou && e.Other == 3 && !e.Red),
            "A real black finalized unique target consults the actual prior sourced damage relation and issues two genuine native HP losses without inventing Damage.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-cao-shuang", new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            foreach (var kind in new[] { CardKind.DrawTwo, CardKind.IronChain }) { var card = CardCatalog.Get(kind); b.AddCard(new("fixture:mcs-" + kind, card.DisplayName, card.CategoryName, card.Description, kind)); }
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
                  {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep-target-cards-and-repeats","query":"handLimit","operation":"add","value":4,"priority":0}],"activations":[{"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]}]}
                ]}
                """;
            var labels = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string,object> {
                [Quiet] = new { name = "安静同行", description = "正常摸牌后跳过出牌阶段。" }, [Driver] = new { name = "真实来源伤害", description = "所选角色对自己造成1点伤害。" } } });
            var catalog = SkillProgramCatalog.Load(rules, labels);
            foreach (var id in new[] { Quiet, Driver }) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Plain, "真实渐专同行", "", "standard:none", "wei", 20, [Quiet]));
            b.AddDeck(new(Deck, "固定真实摸牌牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 100).Select(_ => new ContentDeckPhysicalCard("fixture:mcs-" + CardKind.DrawTwo, Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋曹爽真实主流程", 4, 4, new Dictionary<string,int>(), Deck, GeneralPoolIds: [Hero,Plain], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string,int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
