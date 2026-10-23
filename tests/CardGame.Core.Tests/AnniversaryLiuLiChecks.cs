using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class AnniversaryLiuLiChecks
{
    private const string Hero = "anniversary:liu-li", Fuli = "anniversary:fuli", Dehua = "anniversary:dehua", Peer = "fixture:liu-li-peer", Quiet = "fixture:liu-li-quiet", Watch = "fixture:liu-li-watch", Driver = "fixture:liu-li-driver", Deck = "fixture:liu-li-deck";
    public static void CategoryDiscardDynamicDrawAndNativeRoundNamesCold()
    {
        var s = Scene.Start(); s.Menu(AnniversaryLiuLiStage.UseChoice); s.Cold();
        var victimHp = s.V(1).Hp;
        s.Native(CardKind.FireSlash, 1); s.Play();
        s.Need(s.E<AnniversaryLiuLiNameChosenEvent>() is [{ Name: CardKind.Slash }] && s.V(1).Hp == victimHp - 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.CardKind == CardKind.FireSlash && e.CardId == 0) == 1 &&
            s.G.GetHumanLegalActions().All(a => a.Kind != LegalActionKind.Slash) && s.HandLimit() == s.V(0).Hp + 1,
            "One whole native fire Slash records the shared Slash name, bans both printed Slash variants in Hand, and adds one actual hand-limit point.");
        var handBefore = s.V(0).HandCount; var trickIds = s.V(0).Hand!.Where(c => c.Kind is CardKind.Dismantlement or CardKind.BorrowedSword).Select(c => c.Id).ToArray();
        s.Need(trickIds.Length == 2, "The fixed real opening hand retains both distinct four-character tricks.");
        s.ActivateFuli(); s.Menu(AnniversaryLiuLiStage.Category); s.Answer(c => c.Parameters.GetValueOrDefault("option") == "category-1");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch);
        var paidFrame = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.AnniversaryLiuLi?.Stage == AnniversaryLiuLiStage.CostChildren);
        s.Need(paidFrame.AnniversaryLiuLi is { NameSum: 8, DamageDiscarded: false, Category: 1 } && paidFrame.AnniversaryLiuLi.Cards.SequenceEqual(trickIds) &&
            trickIds.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.anniversary:fuli.discard") == 1) &&
            s.V(0).HandCount == handBefore - 2 + 3,
            "Fuli reveals and truly discards the whole trick category once, then the actual movement child draws three before Fuli freezes its own cap.");
        s.Cold(); s.ContinueWatch(); s.Menu(AnniversaryLiuLiStage.RangeTarget);
        var issued = s.E<AnniversaryLiuLiDrawIssuedEvent>().Single(e => e.FrameId == paidFrame.Id);
        var expectedMaximum = Math.Max(handBefore - 2 + 3, Enumerable.Range(1, 3).Max(seat => s.V(seat).HandCount));
        s.Need(issued.NameSum == 8 && issued.MaximumHandCount == expectedMaximum && issued.Count == Math.Min(8, expectedMaximum) &&
            s.V(0).HandCount == handBefore - 2 + 3 + issued.Count && s.E<AnniversaryLiuLiDiscardPaidEvent>().Count(e => e.FrameId == paidFrame.Id) == 1,
            "The true post-payment child state determines the native draw cap; cold return never repeats the discard or child draw.");
        s.Answer(c => c.Targets.SequenceEqual([1])); s.Play();
        s.Need(s.Range(1) == 0 && s.E<AnniversaryLiuLiRangeIssuedEvent>().Last() is { TargetSeat: 1, SetZero: false }, "A non-damage category applies the real minus-one range effect.");

        s.NextRound(CardKind.Duel); s.Play();
        s.Need(s.Range(1) == 1 && s.E<AnniversaryLiuLiNameChosenEvent>().Select(e => e.Name).SequenceEqual([CardKind.Slash, CardKind.Duel]),
            "The next real round issues a distinct native Duel and the preceding range effect expires at Liu Li's next actual turn start.");
        s.ActivateFuli(); s.Menu(AnniversaryLiuLiStage.Category); s.Answer(c => c.Parameters.GetValueOrDefault("option") == "category-0");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch); s.ContinueWatch(); s.Menu(AnniversaryLiuLiStage.RangeTarget); s.Answer(c => c.Targets.SequenceEqual([2])); s.Play();
        s.Need(s.Range(2) == 0 && s.E<AnniversaryLiuLiRangeIssuedEvent>().Last() is { TargetSeat: 2, SetZero: true } && s.E<AnniversaryLiuLiDiscardPaidEvent>().Last().DamageDiscarded,
            "Discarding the actual basic Slash category chooses the set-to-zero branch instead of subtracting a damage bonus or inventing virtual payment.");

        s.NextRound(CardKind.BarbarianAssault); s.Play();
        s.NextRound(CardKind.ArrowBarrage); s.Play();
        s.NextRound(CardKind.FireAttack); s.Play();
        s.Need(s.E<AnniversaryLiuLiNameChosenEvent>().Select(e => e.Name).SequenceEqual([CardKind.Slash, CardKind.Duel, CardKind.BarbarianAssault, CardKind.ArrowBarrage, CardKind.FireAttack]) &&
            s.E<AnniversaryLiuLiNativeIssuedEvent>().All(e => s.E<CardUseFinishedEvent>().Count(done => done.ResolutionId == e.UseId && done.CardKind == e.Kind && done.CardId == 0) == 1) &&
            s.E<AnniversaryLiuLiDehuaCompletedEvent>() is [{ Names: 5 }] && s.V(0).Skills!.All(skill => skill.Id != Dehua) && s.DiscardEligible().Length == 0,
            "All five distinct normal damage names complete their genuine native Uses, then Dehua is actually lost and remaining damage Hand entities become permanently exempt.");
        s.Stock(); s.Play(); var retained = s.V(0).HandCount; var turn = s.G.State.TurnNumber;
        s.Need(retained > s.HandLimit() && s.DiscardEligible().Length == 0 && s.G.GetHumanLegalActions().All(a => a.Kind != LegalActionKind.Slash),
            "Both issued consequences survive skill loss: damage Hand cards are exempt while the selected-name Use prohibition remains.");
        s.End(); s.Reach(() => s.E<TurnEndedEvent>().Any(e => e.TurnNumber == turn));
        s.Need(s.V(0).HandCount == retained && s.E<AnniversaryLiuLiStartedEvent>().Count(e => e.Source.SkillId == Dehua) == 5,
            "The normal discard phase keeps all exempt cards, with exactly one native Dehua invocation in each of the five real rounds.");
        s.Replay();
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        private ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.AnniversaryLiuLi is not null);
        private CharacterState Player(int seat) => ((IEnumerable<CharacterState>)typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(G)!).Single(p => p.Seat == seat);
        public int HandLimit() => (int)typeof(GameEngine).GetMethod("GetHandLimit", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(G, [Player(0)])!;
        public int Range(int seat) => G.GetAttackRange(seat);
        public Card[] DiscardEligible() => ((IEnumerable<Card>)typeof(GameEngine).GetMethod("GetDiscardEligibleHand", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(G, [Player(0)])!).ToArray();
        public void Menu(AnniversaryLiuLiStage stage) => Reach(() => P?.SkillPrompt?.SkillId is Fuli or Dehua && Root?.AnniversaryLiuLi?.Stage == stage);
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void ActivateFuli() => Accept(new UseProgramSkillCommand(0, Fuli, "reveal-discard-draw", [], [], G.Revision, P!.PromptId));
        public void Stock() => Accept(new UseProgramSkillCommand(0, Driver, "stock", [], [], G.Revision, P!.PromptId));
        public void ContinueWatch() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue" || c.Parameters.GetValueOrDefault("program-option") == "continue" || c.Label == "继续");
        public void Native(CardKind kind, int target)
        {
            var choice = P!.Choices.FirstOrDefault(c => c.Label.Contains("【" + CardCatalog.Get(kind).DisplayName + "】", StringComparison.Ordinal) &&
                (kind is CardKind.BarbarianAssault or CardKind.ArrowBarrage || c.Targets.SequenceEqual([target])));
            Need(choice is not null, "Missing published original damage-name plan."); Answer(c => c.Id == choice!.Id);
        }
        public void NextRound(CardKind kind) { End(); Menu(AnniversaryLiuLiStage.UseChoice); Native(kind, 1); }
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P!; var choice = p.Choices.FirstOrDefault(predicate); Need(choice is not null, "Missing exact original Liu Li choice.");
            Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, G.Revision));
        }
        public void Reach(Func<bool> ready)
        {
            for (var step = 0; step < 220; step++)
            {
                if (ready()) return;
                if (P is { } p)
                {
                    if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    if (p.PlayerSeat == 0 && p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    { Answer(c => c.Cards.Count == 0); continue; }
                    if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected real Liu Li boundary: " + Dump());
                }
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "The bounded real Liu Li flow did not reach its native continuation.");
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(),
            Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var state = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(state == State(), "Cold preserves the original private category, once-paid real movement and full native child cursor."); }
        public void Replay() { var state = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(state == State(), "The real whole damage Uses, payment children and persistent effects replay identically."); }
        public void Accept(GameCommand c) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected real Liu Li command."); }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Receipt = Root?.AnniversaryLiuLi, Chosen = E<AnniversaryLiuLiNameChosenEvent>(), Draws = E<AnniversaryLiuLiDrawIssuedEvent>(), Last = G.Events.TakeLast(8).Select(e => e.Payload).ToArray() });
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var hand = new[] { new ShanheStartingCard(CardKind.Dismantlement, Suit.Spade, 3), new ShanheStartingCard(CardKind.BorrowedSword, Suit.Club, 4), new ShanheStartingCard(CardKind.Slash, Suit.Club, 7), new ShanheStartingCard(CardKind.FireSlash, Suit.Diamond, 8) };
            var peerHand = new[] { new ShanheStartingCard(CardKind.Peach, Suit.Heart, 1), new ShanheStartingCard(CardKind.Peach, Suit.Heart, 2), new ShanheStartingCard(CardKind.Peach, Suit.Heart, 3) };
            var setup = new ShanheBattleSetup(43, 1, "anniversary-liuli", Hero, 3, 0, 0, 0, 0, [Driver, Watch], hand, [],
                [new(Peer, Hp: 20) { StartingHand = peerHand }, new(Peer, Hp: 20) { StartingHand = peerHand }], [new(Peer, Hp: 20) { StartingHand = peerHand }], new string('b', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 24 }, registry);
            var scene = new Scene(game, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-liuli", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            b.AddCard(new("classic:borrowed-sword", "借刀杀人", "锦囊牌", CardCatalog.Get(CardKind.BorrowedSword).Description, CardKind.BorrowedSword,
                AiTags: new Dictionary<string, string> { ["action"] = "force-slash-or-take-weapon", ["targets"] = "ordered-two" }));
            var catalog = SkillProgramCatalog.Load(Rules, Presentation);
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "真实安静同桌", "", Quiet, "wei", 20));
            b.AddDeck(new(Deck, "固定原生杀实体", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard("standard:slash", Suit.Club, i % 13 + 1)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "刘理真实独立轮牌名", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
        public static string Rules => $$"""
        {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
          {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
          {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-category","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.anniversary:fuli.discard"],"optional":false,
            "effects":[{"op":"draw","target":"owner","amount":3},{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
          {"id":"{{Driver}}","revision":1,"activations":[{"id":"stock","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]}]}
        ]}
        """;
        public static string Presentation => JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
        {
            [Quiet] = new { name = "安静同桌", description = "真实跳过出牌。" }, [Driver] = new { name = "真实补牌", description = "原生摸牌。" },
            [Watch] = new { name = "弃牌真实子事件", description = "原生获得后继续。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
        } });
    }
}
