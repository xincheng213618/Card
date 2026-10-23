using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class AnniversaryGaoXiangChecks
{
    private const string Hero = "anniversary:gao-xiang", Skill = "anniversary:chiying", Peer = "fixture:gao-xiang-peer",
        Quiet = "fixture:gao-xiang-quiet", Watch = "fixture:gao-xiang-watch", Deck = "fixture:gao-xiang-deck";
    public static void ChosenParticipantPrivateDiscardsBasicAcquisitionAndCold()
    {
        var s = Scene.Start(); s.Play(); var ownerHand = s.V(0).HandCount;
        var peerOneCard = s.V(1).Hand.Single().Id; var peerThreeCard = s.V(3).Hand.Single().Id;
        s.Use(); s.Menu(GaoXiangChiyingStage.Actor);
        s.Need(s.P!.PlayerSeat == 0 && s.P.Choices.Any(c => c.Targets.SequenceEqual([0])) && s.P.Choices.Any(c => c.Targets.SequenceEqual([1])) &&
            s.P.Choices.All(c => !c.Targets.Contains(2) && !c.Targets.Contains(3)), "The real initial HP comparison includes self and the wounded peer, and excludes the two higher-HP characters.");
        s.Answer(c => c.Targets.SequenceEqual([0])); s.Menu(GaoXiangChiyingStage.Victims);
        s.Answer(c => c.Targets.SequenceEqual([1])); s.Menu(GaoXiangChiyingStage.Victims); s.Answer(c => c.Targets.SequenceEqual([3]));
        s.Answer(c => Mode(c) == "finish"); s.Menu(GaoXiangChiyingStage.Card);
        s.Need(s.P!.PlayerSeat == 0 && s.P.Choices.All(c => c.Cards.Count == 0 && c.Parameters["source-zone"] == "Hand" && c.Parameters["card-owner-seat"] == "1"),
            "The selected actor discards another character's actual private Hand through opaque slots, without learning its physical ID from the menu.");
        s.Answer(c => Mode(c) == "card");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch && s.Root?.GaoXiangChiying is { Stage: GaoXiangChiyingStage.DiscardChildren, Index: 0 });
        var selfId = s.Root!.Id; var first = s.Root.GaoXiangChiying!.Discards.Single();
        s.Need(first.CardId == peerOneCard && s.Zone(peerOneCard) == CardLocation.DiscardPile &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.ParentFrameId == selfId && e.ExecutorSeat == 0 && e.SubjectSeat == 1 && e.ActualCount == 1) == 1,
            "The original frame holds an actually paid single-card logical discard before its native movement child.");
        s.Cold(); s.Menu(GaoXiangChiyingStage.Card); s.Answer(c => Mode(c) == "card"); s.Play();
        s.Need(s.Zone(peerOneCard) == CardLocation.DiscardPile && s.Zone(peerThreeCard) == CardLocation.DiscardPile && s.V(0).HandCount == ownerHand &&
            s.E<GaoXiangChiyingMovedEvent>().Count(e => e.FrameId == selfId && !e.Gain) == 2 && !s.E<GaoXiangChiyingMovedEvent>().Any(e => e.FrameId == selfId && e.Gain) &&
            s.G.GetHumanLegalActions().All(a => a.ProgramSkillId != Skill), "Cold return pays the second participant once; selecting self never obtains even the actually discarded basic card, and spends the native phase quota.");

        s.NextPlay(); var originalHand = s.V(0).Hand.Select(c => c.Id).ToHashSet(); var drawFacts = s.E<CardsDrawnEvent>().Length;
        s.Use(); s.Menu(GaoXiangChiyingStage.Actor); s.Answer(c => c.Targets.SequenceEqual([1])); s.Menu(GaoXiangChiyingStage.Victims);
        s.Need(s.P!.PlayerSeat == 1 && s.P.Choices.Any(c => c.Targets.SequenceEqual([0])) && s.P.Choices.Any(c => c.Targets.SequenceEqual([2])) && s.P.Choices.All(c => !c.Targets.Contains(1) && !c.Targets.Contains(3)),
            "The other selected actor owns its actual menu and attack range; its candidates can include Gao Xiang but exclude itself and an out-of-range character.");
        // Advance the AI participant normally; never submit a human answer on its behalf.
        s.Reach(() => s.P is { PlayerSeat: 0 } && s.P.SkillPrompt?.SkillId == Watch && s.Root?.GaoXiangChiying is { Stage: GaoXiangChiyingStage.DiscardChildren, Actor: 1, Index: 0 });
        var otherId = s.Root!.Id; var paidOwnerCard = s.Root.GaoXiangChiying!.Discards.Single();
        s.Need(originalHand.Contains(paidOwnerCard.CardId) && paidOwnerCard.From == CardLocation.Hand(0) && s.Zone(paidOwnerCard.CardId) == CardLocation.DiscardPile &&
            s.Root.GaoXiangChiying.Victims.SequenceEqual([0, 2]), "The real other participant chooses and pays its frozen two-victim queue; acquisition waits for both discard children.");
        s.Continue(); s.Play();
        var discards = s.E<GaoXiangChiyingMovedEvent>().Where(e => e.FrameId == otherId && !e.Gain).ToArray();
        var gains = s.E<GaoXiangChiyingMovedEvent>().Where(e => e.FrameId == otherId && e.Gain).ToArray();
        s.Need(discards.Length == 2 && gains is [var gain] && gain.CardId == paidOwnerCard.CardId && gain.From == CardLocation.DiscardPile && gain.To == CardLocation.Hand(1) &&
            s.Zone(gain.CardId) == CardLocation.Hand(1) && s.V(0).HandCount == originalHand.Count - 1 && s.V(2).HandCount == 0 && s.E<CardsDrawnEvent>().Length == drawFacts &&
            discards.Single(e => e.Victim == 2).PrintedKind == CardKind.Dismantlement && s.Zone(discards.Single(e => e.Victim == 2).CardId) == CardLocation.DiscardPile &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.ParentFrameId == otherId && e.ExecutorSeat == 1 && e.ActualCount == 1) == 2,
            "Only the actual basic discard is obtained by the other actor; the nonbasic remains discarded, and both logical costs retain that actor as executor without a fabricated draw.");

        s.NextPlay(); var movements = s.G.CardMovements.Count; s.Use(); s.Menu(GaoXiangChiyingStage.Actor); s.Answer(c => c.Targets.SequenceEqual([0]));
        s.Menu(GaoXiangChiyingStage.Victims); var zeroId = s.Root!.Id; s.Answer(c => Mode(c) == "finish"); s.Play();
        s.Need(s.G.CardMovements.Count == movements && s.E<GaoXiangChiyingCommittedEvent>().Single(e => e.FrameId == zeroId).Count == 0 &&
            !s.E<GaoXiangChiyingMovedEvent>().Any(e => e.FrameId == zeroId), "The permitted empty participant set completes without a fake payment or gain.");
        s.Replay();
    }
    private static string? Mode(PromptChoice c) => c.Parameters.GetValueOrDefault("mode");
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.GaoXiangChiying is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public void Use() => Accept(new UseProgramSkillCommand(0, Skill, GaoXiangComposition.Activation, [], [], G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void Menu(GaoXiangChiyingStage stage) => Reach(() => Root?.GaoXiangChiying?.Stage == stage && P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "anniversary-gao-xiang") == true);
        public void NextPlay() { Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId)); Play(); }
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P!; var choice = p.Choices.FirstOrDefault(predicate); Need(choice is not null, "Missing the actual Gao Xiang participant choice.");
            Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, G.Revision));
        }
        public void Reach(Func<bool> ready)
        {
            for (var step = 0; step < 300; step++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.SkillPrompt?.SkillId == Watch) { Continue(); continue; }
                    if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    throw new InvalidOperationException("Unexpected Gao Xiang human boundary. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The short native scenario retains all participants."); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Gao Xiang did not reach the bounded original native continuation.");
        }
        public void Accept(GameCommand c) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected exact Gao Xiang command."); }
        public void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message + " " + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Root = Root?.GaoXiangChiying,
            Hands = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).HandCount }), Last = G.Events.TakeLast(8).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "The original paid discard and its native movement child cold-restore exactly."); }
        public void Replay() { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(State() == before, "All original actor choices, logical discards, acquisitions and empty selection replay identically."); }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(47, 1, "anniversary-gao-xiang", Hero, 4, 0, 0, 0, 0, [Watch],
                [new(CardKind.Dodge, Suit.Club, 2), new(CardKind.Peach, Suit.Heart, 3), new(CardKind.Slash, Suit.Heart, 4)], [],
                [new(Peer, Hp: 8) { StartingHand = [new(CardKind.Dismantlement, Suit.Club, 6)] }, new(Peer, Hp: 8) { StartingHand = [new(CardKind.Slash, Suit.Spade, 7)] }],
                [new(Peer, Hp: 3) { StartingHand = [new(CardKind.Crossbow, Suit.Spade, 5)] }], new string('a', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, AiPolicyVersion = AiDecisionPolicy.CurrentVersion, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-gao-xiang", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{{Watch}}}","revision":1,"triggers":[{"id":"paid-hand","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perCard","movementReasons":["skill-program.anniversary:chiying.discard"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var labels = new Dictionary<string, object> { [Quiet] = new { name = "安静同桌", description = "保留原实际回合，跳过出牌。" },
                [Watch] = new { name = "真实弃牌子窗", description = "原生实际弃牌后暂停。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "真实安静同桌", "", Quiet, "wei", 8) { AdditionalSkillIds = [Watch] });
            b.AddDeck(new(Deck, "固定无额外摸牌", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 8)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "高翔原生参与者弃牌小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
