using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class AnniversaryLeiBoChecks
{
    private const string Hero = "anniversary:lei-bo", Silue = "anniversary:silue", Shuaijie = "anniversary:shuaijie", Driver = "fixture:lei-bo-driver", Quiet = "fixture:lei-bo-quiet", NoDraw = "fixture:lei-bo-no-draw", Watch = "fixture:lei-bo-paid-watch", Deck = "fixture:lei-bo-deck";
    private static string Peer(int seat) => "fixture:lei-bo-peer-" + seat;
    private static string? Branch(PromptChoice c) => c.Parameters.GetValueOrDefault("branch");

    public static void ActualVictimTurnLootNativeSlashPenaltyAndDeckSeizureCold()
    {
        var s = Scene.Start(); s.Mark(); s.Play();
        s.Need(s.Marked(1) == 1 && s.Marked(0) == 0, "Opening Silue puts this owner's one real attributed mark on the chosen other participant.");
        s.Equip(); s.Play();
        var sameHand = s.V(0).Hand!.First(c => c.Kind == CardKind.Peach).Id;
        var beforeHand = s.V(0).HandCount;
        s.Driver("incoming", [1]); s.Offer(); s.Activate(); s.Owned();
        var sameMoves = s.G.CardMovements.Count(m => m.CardId == sameHand);
        s.Answer(c => c.Cards.Contains(sameHand)); s.Play();
        s.Need(s.V(0).HandCount == beforeHand && s.Zone(sameHand) == CardLocation.Hand(0) && s.G.CardMovements.Count(m => m.CardId == sameHand) == sameMoves && s.E<AnniversaryLeiBoLootInvokedEvent>().Single().Victim == 0,
            "When the marked source really damages Lei Bo, obtaining his same Hand entity is satisfied without manufacturing a loss, gain or new movement.");
        s.Driver("crush", [1]); s.Menu(AnniversaryLeiBoStage.PenaltyChoice);
        var discard = s.V(0).Hand.First(c => c.Kind == CardKind.Peach).Id;
        s.Answer(c => c.Cards.Contains(discard)); s.Play();
        s.Need(s.Zone(discard) == CardLocation.DiscardPile && s.E<NativeLogicalDiscardMaterialCompletedEvent>().Any(e => e.CardId == discard && e.From == CardLocation.Hand(0)) && s.E<AnniversaryLeiBoSlashUsedEvent>().Length == 0,
            "When Lei Bo himself is the actual source that hurt the marked participant, no self-target Slash is invented; he pays one real native logical hand discard.");
        var maximum = s.V(0).MaxHp;
        s.UseShuaijie(); s.Menu(AnniversaryLeiBoStage.RewardChoice);
        s.Need(s.V(0).MaxHp == maximum - 1 && s.E<MaximumHpChangedEvent>().Count(e => e.SkillId == Shuaijie) == 1,
            "The limited activation first pays one actual native maximum-HP reduction before offering its seizure choice.");
        s.Answer(c => Branch(c) == "deck"); s.Watch();
        var paid = s.Root!.AnniversaryLeiBo!; var frame = s.Root.Id; var rewardIds = paid.Selected.ToArray();
        s.Need(rewardIds.Length == 3 && rewardIds.Select(id => s.V(0).Hand.Single(c => c.Id == id).Kind).Select(k => CardCatalog.Get(k).CategoryName).Distinct().Count() == 3 && rewardIds.All(id => s.Zone(id) == CardLocation.Hand(0)) && s.Marked(1) == 1 && s.Marked(0) == 0,
            "Shuaijie obtains three existing distinct-type deck entities as one actual acquisition; the old marker remains until the paid movement observer returns.");
        s.Cold(); s.Continue(); s.Play();
        s.Need(s.Marked(1) == 0 && s.Marked(0) == 1 && s.E<MaximumHpChangedEvent>().Count(e => e.SkillId == Shuaijie) == 1 && s.E<AnniversaryLeiBoMovementSettledEvent>().Count(e => e.FrameId == frame) == 1 && s.E<AnniversaryLeiBoCompletedEvent>().Count(e => e.FrameId == frame) == 1,
            "Cold returns to the same already-paid frame, without re-paying maximum HP or obtaining again, and transfers its original mark to Lei Bo once.");
        s.Driver("injure", [3]); s.Offer(); s.Activate(); s.Owned(); s.Answer(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)); s.Play();
        var claims = s.E<AnniversaryLeiBoLootInvokedEvent>().Length;
        var hand = s.V(0).HandCount;
        s.Driver("injure", [3]); s.Play();
        s.Need(s.E<AnniversaryLeiBoLootInvokedEvent>().Length == claims && s.V(0).HandCount == hand,
            "A second actual damage to the same victim in the same actual turn cannot take a second card.");
        s.Driver("injure", [2]); s.Offer(); s.Activate(); s.Owned(); s.Answer(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)); s.Play();
        s.Need(s.E<AnniversaryLeiBoLootInvokedEvent>().Where(e => e.Turn == 1).Select(e => e.Victim).Order().SequenceEqual([0, 2, 3]),
            "Each different real victim has its independent actual-turn opportunity, including the original self victim.");
        var slash = s.V(0).Hand.First(c => c.Kind == CardKind.Slash).Id;
        var usedBefore = s.E<AnniversaryLeiBoSlashUsedEvent>().Length;
        s.Driver("incoming", [2]); s.FixedSlash();
        var handBeforeUse = s.V(0).HandCount;
        s.Answer(c => c.Parameters.GetValueOrDefault("request-option") == "use" && c.Cards.SequenceEqual([slash])); s.Play();
        var used = s.E<AnniversaryLeiBoSlashUsedEvent>().Single(e => e.UseId > 0);
        s.Need(s.E<AnniversaryLeiBoSlashUsedEvent>().Length == usedBefore + 1 && used.Target == 2 && s.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == used.UseId && e.SourceSeat == 0 && e.CardKind == CardKind.Slash) == 1 && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == used.UseId) == 1 && s.Zone(slash) == CardLocation.DiscardPile && s.V(0).HandCount == handBeforeUse - 1 && !s.E<AnniversaryLeiBoMovementSettledEvent>().Any(e => e.FrameId == used.FrameId && e.Branch == "discard"),
            "Only the exact original real Slash Use against the original source exempts the penalty; its physical card and whole native Use finish once, with no extra hand discard.");
        s.End(); s.Reach(() => s.G.State.TurnNumber == 5 && s.P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        s.Driver("injure", [2]); s.Offer(); s.Activate(); s.Owned(); s.Answer(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)); s.Play();
        s.Need(s.E<AnniversaryLeiBoLootInvokedEvent>().Where(e => e.Victim == 2).Select(e => e.Turn).SequenceEqual([1, 5]), "The same victim becomes eligible again in a later real turn without clearing unrelated victim opportunities.");
        s.Replay();
    }

    public static void NativeLimitedMarkedSeizureAtMostThreeKeepsJudgment()
    {
        var s = Scene.Start(); s.Mark(); s.Play(); s.Equip(); s.Play();
        var delayed = s.V(0).Hand!.Single(c => c.Kind == CardKind.Indulgence).Id;
        s.Card(delayed, [1]); s.Play();
        s.Driver("crush", [1]); s.Menu(AnniversaryLeiBoStage.PenaltyChoice); s.Answer(c => c.Cards.Contains(s.V(0).Hand.First(x => x.Kind == CardKind.Peach).Id)); s.Play();
        s.UseShuaijie(); s.Menu(AnniversaryLeiBoStage.RewardChoice); s.Answer(c => Branch(c) == "marked"); s.Menu(AnniversaryLeiBoStage.PickReward);
        var selected = new List<int>();
        for (var i = 0; i < 2; i++)
        {
            s.Need(s.P!.Choices.All(c => c.Parameters.GetValueOrDefault("source-zone") != nameof(CardZoneKind.Judgment)) && s.Zone(delayed) == CardLocation.Judgment(1),
                "The real delayed card remains in Judgment; obtaining the character's cards only offers Hand and Equipment.");
            var index = int.Parse(s.P.Choices.First(c => Branch(c) == "card").Parameters["slot-index"], System.Globalization.CultureInfo.InvariantCulture);
            selected.Add(s.V(1).Hand![index].Id); s.Answer(c => Branch(c) == "card"); s.Menu(AnniversaryLeiBoStage.PickReward);
        }
        var before = s.V(0).HandCount;
        s.Answer(c => Branch(c) == "finish"); s.Play();
        s.Need(selected.Distinct().Count() == 2 && selected.All(id => s.Zone(id) == CardLocation.Hand(0)) && s.V(0).HandCount == before + 2 && s.Zone(delayed) == CardLocation.Judgment(1) && s.Marked(0) == 1 && s.Marked(1) == 0 && s.E<AnniversaryLeiBoMovementSettledEvent>().Single(e => e.Branch == "marked").Count == 2 && !s.G.GetHumanLegalActions().Any(a => a.SkillId == Shuaijie),
            "Finishing after two exact original cards really obtains only those two, leaves the third owned card and Judgment untouched, moves the marker, and consumes the original limited activation.");
        s.Replay();
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().FirstOrDefault(f => f.AnniversaryLeiBo is not null);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int Marked(int seat) => V(seat).Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.AnniversaryLeiBoSilei)?.Count ?? 0;
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void Mark() { Reach(() => P?.SkillPrompt?.SkillId == Silue && P.Choices.Any(c => c.Targets.SequenceEqual([1]))); Answer(c => c.Targets.SequenceEqual([1])); }
        public void Offer() => Reach(() => P?.SkillPrompt?.SkillId == Silue && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Owned() => Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true);
        public void Menu(AnniversaryLeiBoStage stage) => Reach(() => Root?.AnniversaryLeiBo?.Stage == stage && P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "anniversary-lei-bo") == true);
        public void FixedSlash() => Reach(() => P?.SkillPrompt?.SkillId == Silue && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "fixed-target-slash"));
        public void Watch() => Reach(() => P?.SkillPrompt?.SkillId == AnniversaryLeiBoChecks.Watch);
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Driver(string binding, int[] targets) => Accept(new UseProgramSkillCommand(0, AnniversaryLeiBoChecks.Driver, binding, [], targets, G.Revision, P!.PromptId));
        public void UseShuaijie() => Accept(new UseProgramSkillCommand(0, Shuaijie, "seize-and-return-mark", [], [], G.Revision, P!.PromptId));
        public void Equip()
        {
            var id = V(0).Hand!.Single(c => c.Kind == CardKind.Crossbow).Id;
            var action = G.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.Equip && a.CardId == id);
            Accept(new PlayCardCommand(0, id, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
        }
        public void Card(int id, int[] targets)
        {
            var a = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId == id && a.TargetSeats.SequenceEqual(targets) && a.Kind != LegalActionKind.Recast);
            Need(a is not null, "The fixed physical card has its exact native published action.");
            Accept(new PlayCardCommand(0, id, a!.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        }
        public void Answer(Func<PromptChoice, bool> match)
        { var p = P!; var c = p.Choices.FirstOrDefault(match); Need(c is not null, "The exact original native published choice exists."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
        public void Accept(GameCommand c)
        { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected native Lei Bo command."); }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 220; i++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification) { Answer(c => c.Cards.Count == 0); continue; }
                    throw new InvalidOperationException("Unexpected original Lei Bo boundary at " + line + Dump());
                }
                Need(G.State.Winner == Winner.None, "The short native table retains its participants."); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The actual Lei Bo flow did not reach " + line + Dump());
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold preserves the original paid maximum, exact gain entities, owning movement observer and marker still awaiting transfer."); }
        public void Replay() { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(before == State(), "Actual damage victims, per-turn opportunities, native Slash/discard, maximum payment and marker transfer replay exactly."); }
        public void Need(bool valid, string why) { if (!valid) throw new InvalidOperationException(why + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, P, Root = Root?.AnniversaryLeiBo, Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).MaxHp, V(i).HandCount, Mark = Marked(i) }), Last = G.Events.TakeLast(12).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(17, 1, "lei-bo-actual-native", Hero, 4, 0, 0, 0, 0, new[] { AnniversaryLeiBoChecks.Driver, NoDraw, AnniversaryLeiBoChecks.Watch },
                [new(CardKind.Crossbow, Suit.Club, 1), new(CardKind.Slash, Suit.Heart, 7), new(CardKind.Slash, Suit.Club, 9), new(CardKind.Peach, Suit.Heart, 3), new(CardKind.Peach, Suit.Diamond, 4), new(CardKind.Indulgence, Suit.Heart, 6)], [],
                [new(Peer(2), Hp: 8) { StartingHand = [new(CardKind.Peach, Suit.Club, 9), new(CardKind.Peach, Suit.Heart, 8), new(CardKind.Peach, Suit.Diamond, 7)] }, new(Peer(3), Hp: 8) { StartingHand = [new(CardKind.Peach, Suit.Club, 5), new(CardKind.Peach, Suit.Diamond, 6)] }],
                [new(Peer(1), Hp: 8) { StartingHand = [new(CardKind.Peach, Suit.Spade, 11), new(CardKind.Peach, Suit.Club, 12), new(CardKind.Slash, Suit.Heart, 10), new(CardKind.Peach, Suit.Diamond, 9)] }], new string('a', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 14 }, registry);
            var scene = new Scene(g, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-lei-bo", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"none","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"preserve-originals","query":"handLimit","operation":"add","value":20,"priority":0},{"id":"native-request-range","query":"attackRange","operation":"add","value":2,"priority":0}],"activations":[
                {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
                {"id":"injure","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"crush","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":6}]}
              ]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-original","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.lei-bo.deck"],"priority":1000,"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var names = new Dictionary<string, object>();
            foreach (var id in new[] { Quiet, NoDraw, Driver, Watch }) names[id] = new { name = "雷薄固定原生输入", description = "保留真实伤害、上限、实体取牌、杀和移动观察者。" };
            names[Watch] = new { name = "雷薄已付取牌观察者", description = "在真实取牌移动后保留原帧等待。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            for (var seat = 1; seat < 4; seat++) b.AddGeneral(new(Peer(seat), "安静原生同桌" + seat, "standard-guanyu", Quiet, "qun", 8, AdditionalSkillIds: [NoDraw]));
            var cards = Enumerable.Range(0, 60).Select(i => new ContentDeckPhysicalCard(i % 3 == 0 ? "standard:crossbow" : i % 3 == 1 ? "standard:peach" : "standard:iron_chain", Suit.Club, i % 13 + 1)).ToArray();
            b.AddDeck(new(Deck, "固定三类型原生库存", 0, 2, []) { PhysicalCards = cards });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "雷薄真实私掠与衰劫", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer(1), Peer(2), Peer(3)], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
