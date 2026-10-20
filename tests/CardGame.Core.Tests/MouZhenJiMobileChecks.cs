using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class MouZhenJiMobileChecks
{
    public static void SeatedRevealColorMovementHandLimitCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(7, 1, "mou-zhen-ji", Owner, 3, 0, 0, 0, 0, [],
            [C(CardKind.Peach, Suit.Heart), C(CardKind.Slash, Suit.Heart), C(CardKind.Dodge, Suit.Diamond)], [],
            [new(EnemyA, Hp: 8) { StartingHand = [C(CardKind.Slash, Suit.Heart)] }, new(EnemyB, Hp: 8) { StartingHand = [] }],
            [new(Ally, Hp: 8) { StartingHand = [C(CardKind.Dodge, Suit.Spade)] }], new string('a', 64));
        var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
            UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        PendingDecision? P() => Enumerable.Range(0, 4).Select(i => g.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        T[] E<T>() => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
        void Need(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); }
        void Accept(GameCommand c) { var r = g.Submit(c); Need(r.Accepted, r.Error?.Message ?? "Native command rejected."); }
        void Reach(Func<bool> done)
        {
            for (var i = 0; i < 180; i++)
            {
                if (done()) return;
                Need(g.State.Winner == Winner.None, "Native game ended before the representative Luoshen boundary.");
                if (P() is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected human boundary: " + P()!.Prompt);
                Accept(new AdvanceOneStepCommand(g.Revision));
            }
            throw new InvalidOperationException("Luoshen failed to reach its bounded native flow.");
        }
        void Answer(Func<PromptChoice, bool> match)
        {
            var p = P()!; Need(p.PlayerSeat == 0, "Only actual human choices are answered.");
            var c = p.Choices.Single(match); Accept(new AnswerPromptCommand(0, p.PromptId, c.Id, g.Revision));
        }
        Accept(new StartGameCommand());
        Reach(() => P()?.SkillPrompt?.SkillId == Luoshen);
        var initialHandCount = g.CreateSnapshot(0).Players[0].HandCount;
        var beforeRouteSequence = g.CardMovements.LastOrDefault()?.Sequence ?? 0;
        Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(() => P()?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "start") == true);
        Answer(c => c.Targets.SequenceEqual([1]));
        Reach(() => P()?.SkillPrompt?.SkillId == Watch);
        var route = E<MouZhenJiLuoshenSeatQueuedEvent>();
        Need(route.Length == 2 && route.OrderBy(e => e.Index).Select(e => e.Seat).SequenceEqual([1, 2]), "Four living players freeze the selected starting seat and next other seat, once each.");
        var black = E<MouZhenJiLuoshenMovedEvent>().Single();
        Need(black.Black && black.From == CardLocation.Hand(1) && black.To == CardLocation.Hand(0) &&
            E<TurnHandLimitExemptCardGrantedEvent>().Any(e => e.Grant.CardId == black.CardId && e.Grant.Source.SkillId == Luoshen),
            "A real black hand reveal is obtained and the same physical entity receives the existing actual-turn hand-limit exemption.");
        var before = JsonSerializer.Serialize(g.CreateSnapshot(0));
        g = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(before == JsonSerializer.Serialize(g.CreateSnapshot(0)), "A representative pending gain child restores its frozen route and paid obtain exactly.");
        Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(() => P() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        var moved = E<MouZhenJiLuoshenMovedEvent>();
        Need(moved.Length == 2 && moved[1] is { Black: false, Index: 1 } red && red.From == CardLocation.Hand(2) && red.To == CardLocation.DiscardPile &&
            E<MouZhenJiLuoshenRevealedEvent>().Length == 2 && E<MouZhenJiLuoshenCompletedEvent>().Single().ProcessedCount == 2,
            "The next participant really shows and discards its red entity, after the black gain child completes, without a second payment.");
        var normalDraws = g.CardMovements.Count(m => m.Sequence > beforeRouteSequence && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0));
        var actualHand = g.CreateSnapshot(0).Players[0];
        Need(normalDraws == 2 && actualHand.HandCount == initialHandCount + 1 + normalDraws,
            $"Native normal Draw2 and exactly one obtained entity augment the real starting Hand: initial={initialHandCount}, draws={normalDraws}, actual={actualHand.HandCount}, cards={string.Join(',', actualHand.Hand.Select(c => c.Id))}.");
        Accept(new EndPlayPhaseCommand(0, g.Revision, P()!.PromptId));
        Reach(() => E<TurnEndedEvent>().Any(e => e.TurnNumber == 1 && e.ActorSeat == 0));
        Need(g.CreateSnapshot(0).Players[0].HandCount == 4 && g.CreateCardZoneDiagnostics().Single(c => c.CardId == black.CardId).Location == CardLocation.Hand(0),
            "Native discard counts only three ordinary cards; the exact Luoshen entity remains beyond the ordinary three-card hand limit.");
    }
    private const string Luoshen = "mou-mobile:luoshen", Watch = "fixture:mzj-watch", Quiet = "fixture:mzj-quiet",
        Owner = "fixture:mzj-owner", Ally = "fixture:mzj-ally", EnemyA = "fixture:mzj-enemy-a", EnemyB = "fixture:mzj-enemy-b", Deck = "fixture:mzj-deck";
    private static ShanheStartingCard C(CardKind kind, Suit suit) => new(kind, suit, 7);
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-zhen-ji-mobile", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, ["mou:zhen-ji-mobile"]);
            foreach (var k in new[] { CardKind.Slash, CardKind.Dodge, CardKind.Peach })
            { var d = CardCatalog.Get(k); b.AddCard(new("fixture:mzj-card-" + k, d.DisplayName, d.CategoryName, d.Description, k)); }
            var rules = "{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[" +
                "{\"id\":\"" + Watch + "\",\"revision\":1,\"triggers\":[{\"id\":\"gain-child\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill-program.mou-mobile:luoshen.obtain\"],\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"pause\",\"options\":[{\"id\":\"continue\"}]}]}]}," +
                "{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}]}";
            var labels = new Dictionary<string, (string name, string description)> { [Watch] = ("停点", "展示获得后的真实子结算"), [Quiet] = ("静候", "跳过出牌") };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = labels.ToDictionary(p => p.Key, p => new { name = p.Value.name, description = p.Value.description,
                    optionLabels = p.Key == Watch ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() }) }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, labels[id].name, labels[id].description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Owner, "谋甄姬真实检查", "", "standard:none", "wei", 3, [Luoshen, "mou-mobile:qingguo", Watch]));
            foreach (var id in new[] { Ally, EnemyA, EnemyB }) b.AddGeneral(new(id, "展示角色", "", "standard:none", "wei", 8, [Quiet]));
            b.AddDeck(new(Deck, "固定小牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 32).Select(_ => new ContentDeckPhysicalCard("fixture:mzj-card-Dodge", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋甄姬原生小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
