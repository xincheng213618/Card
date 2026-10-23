using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class AnniversaryZhangXunChecks
{
    private const string Hero = "anniversary:zhang-xun", Skill = "anniversary:suizheng", Peer = "fixture:zhang-xun-peer",
        Quiet = "fixture:zhang-xun-quiet", Deck = "fixture:zhang-xun-deck";
    public static void NextActualTurnSlashBenefitsNativeFollowupAndCold()
    {
        var s = Scene.Start(); s.Play(); var firstTurn = s.G.State.TurnNumber;
        var materials = s.V(0).Hand.OrderBy(c => c.Rank).Select(c => c.Id).ToArray();
        s.Need(materials.Length == 4 && s.V(0).Hand.All(c => c.Kind == CardKind.Slash) && !s.SlashActions.Any(a => a.TargetSeats.Contains(2)),
            "Before any issued next-turn lease, the actual distance-two target is outside the native Slash range.");
        s.End(); s.Offer(AnniversaryZhangXunComposition.Arm); s.Activate();
        s.Reach(() => s.P?.Choices.Any(c => c.Targets.SequenceEqual([0])) == true); s.Answer(c => c.Targets.SequenceEqual([0]));
        s.Play(); var dueTurn = s.G.State.TurnNumber; var lease = s.E<ZhangXunSuizhengLeaseEvent>().Single();
        s.Need(dueTurn > firstTurn && s.G.State.CurrentSeat == 0 && lease.IssuedTurn == firstTurn && lease.RecipientSeat == 0 &&
            s.E<TurnStartedEvent>().First(e => e.ActorSeat == 0 && e.TurnNumber > firstTurn).TurnNumber == dueTurn &&
            s.SlashActions.Any(a => a.CardId == materials[0] && a.TargetSeats.SequenceEqual([2])),
            "Choosing self is legal, and the benefit begins in precisely that recipient's next actual turn, permitting a real distant Slash.");

        var targetHp = s.V(2).Hp; s.Slash(materials[0], 2); s.Play(); s.Slash(materials[1], 2); s.Play();
        s.Need(s.V(2).Hp == targetHp - 2 && s.SlashActions.Length == 0 && s.V(0).Hand.Select(c => c.Id).Order().SequenceEqual(materials.Skip(2).Order()) &&
            materials.Take(2).All(id => s.Zone(id) == CardLocation.DiscardPile),
            "The issued actual Play permits exactly two native physical Slashes and really spends their own original Hand materials.");
        s.End(); s.Offer(AnniversaryZhangXunComposition.Followup); s.Activate();
        s.Reach(() => s.Root?.ZhangXunSuizheng is { SelectedTarget: null } && s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "anniversary-zhang-xun-followup") == true);
        var root = s.Root!; var receipt = root.ZhangXunSuizheng!; var beforeHand = s.V(0).HandCount; var beforeMoves = s.G.CardMovements.Count;
        s.Need(receipt.LeaseId == lease.FrameId && receipt.ActualTurn == dueTurn && receipt.RecipientSeat == 0 && receipt.Victims is [{ TargetSeat: 2 }] &&
            s.P!.Choices.All(c => c.Targets.SequenceEqual([2])) && !s.P.Choices.Any(c => c.Targets.Contains(0) || c.Targets.Contains(1) || c.Targets.Contains(3)),
            "The original issuer can choose only an actual positive-damage victim from this due turn, without duplicate entries for repeated damage.");
        s.Cold(); s.Answer(c => c.Targets.SequenceEqual([2])); s.Offer(AnniversaryZhangXunComposition.Arm);
        var followup = s.E<ZhangXunSuizhengTargetChosenEvent>().Single();
        var used = s.E<CardUseDeclaredEvent>().Where(e => e.SourceSeat == 0 && e.CardKind == CardKind.Slash).ToArray();
        s.Need(s.V(2).Hp == targetHp - 3 && s.V(0).HandCount == beforeHand && s.G.CardMovements.Count == beforeMoves && used.Length == 3 &&
            followup.FrameId == root.Id && followup.LeaseId == lease.FrameId && followup.TargetSeat == 2 &&
            s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == used.Last().ResolutionId && e.CardKind == CardKind.Slash) &&
            s.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount == 1 && e.SourceCardUseResolutionId == used.Last().ResolutionId) &&
            s.E<ZhangXunSuizhengFollowupStartedEvent>().Single().FrameId == root.Id && s.E<ZhangXunSuizhengPhaseClosedEvent>() is [{ Activated: true }],
            "Cold return continues one actual zero-material native Slash through response/damage/completion, without another payment, grant or followup choice.");

        s.Skip(); s.Play();
        s.Need(s.G.State.TurnNumber > dueTurn && !s.SlashActions.Any(a => a.TargetSeats.Contains(2)) && s.SlashActions.Any(a => a.TargetSeats.SequenceEqual([1])),
            "After the recipient's due actual turn ends, the old distance benefit expires while ordinary nearby Slash remains available.");
        s.Slash(materials[2], 1); s.Play();
        s.Need(s.SlashActions.Length == 0 && s.V(0).Hand.Single().Id == materials[3], "The later actual Play has its ordinary one-Slash quota rather than the expired extra use.");
        s.End(); s.Offer(AnniversaryZhangXunComposition.Arm);
        s.Need(s.E<ZhangXunSuizhengLeaseEvent>().Length == 1 && s.E<ZhangXunSuizhengFollowupStartedEvent>().Length == 1,
            "Declining a later grant leaves no outstanding new lease; old actual-turn damage cannot produce another followup menu.");
        s.Skip(); s.Replay();
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.ZhangXunSuizheng is not null);
        public LegalAction[] SlashActions => G.GetHumanLegalActions().Where(a => a.Kind == LegalActionKind.Slash).ToArray();
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Offer(string binding) => Reach(() => P?.SkillPrompt?.SkillId == Skill && P.Choices.Any(c => Key(c) == "activate" && c.Parameters.GetValueOrDefault("binding-id") == binding));
        private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
        public void Activate() => Answer(c => Key(c) == "activate");
        public void Skip() => Answer(c => Key(c) == "skip");
        public void Slash(int id, int target)
        {
            var a = SlashActions.Single(a => a.CardId == id && a.TargetSeats.SequenceEqual([target]));
            Accept(new PlayCardCommand(0, id, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId)
            { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        }
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P!; var choice = p.Choices.FirstOrDefault(predicate); Need(choice is not null, "The genuine Suizheng choice must exist.");
            Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, G.Revision));
        }
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 320; i++)
            {
                if (ready()) return;
                if (P is { Kind: DecisionKind.SelectFaction }) { Answer(_ => true); continue; }
                if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected Zhang Xun native boundary. " + Dump());
                Need(G.State.Winner == Winner.None, "The short original-turn scenario retains its participants.");
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Zhang Xun did not reach the bounded original native continuation.");
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
            Moves = G.CardMovements, Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "The original due-turn lease, victim witnesses and native choice cold-restore exactly."); }
        public void Replay() { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(State() == before, "The grant, actual-turn benefits, native followup and expiration replay identically."); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Root = Root?.ZhangXunSuizheng,
            Views = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).HandCount }), Leases = E<ZhangXunSuizhengLeaseEvent>(),
            Last = G.Events.TakeLast(8).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        public void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message + " " + Dump()); }
        private void Accept(GameCommand c) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected genuine Zhang Xun command."); }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(47, 1, "anniversary-zhang-xun", Hero, 4, 0, 0, 0, 0, [],
                [new(CardKind.Slash, Suit.Heart, 2), new(CardKind.Slash, Suit.Club, 3), new(CardKind.Slash, Suit.Diamond, 4), new(CardKind.Slash, Suit.Spade, 5)], [],
                [new(Peer) { StartingHand = [] }, new(Peer) { StartingHand = [] }], [new(Peer) { StartingHand = [] }], new string('c', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, AiPolicyVersion = AiDecisionPolicy.CurrentVersion, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-zhang-xun", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var catalog = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Quiet}}","revision":1,"triggers":[
                {"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,
                 "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
            { [Quiet] = new { name = "安静同桌", description = "保留真实实际回合，跳过出牌阶段。" } } }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "真实安静同桌", "", Quiet, "wei", 10));
            b.AddDeck(new(Deck, "无额外摸牌的真实牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "张勋下一实际回合原生随征", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
