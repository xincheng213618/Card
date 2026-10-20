using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class MouHuaXiongOlChecks
{
    private const string Hero = "mou:hua-xiong-ol", Bojue = "mou-ol:bojue", Driver = "fixture:mhxo-driver", Quiet = "fixture:mhxo-quiet";

    public static void NativeCommittedPairMutualUseAndPersistentDirectionalTicketsCold()
    {
        var s = Scene.Start();
        s.Play();
        s.Driver("draw-two"); s.Play();
        s.Need(s.E<MouHxYangweiTicketEvent>() is [{ Outgoing: true, Total: 2 }], "Two actual outside-Draw entities issue one outgoing ticket.");
        s.Bojue(); s.Reach(() => s.Branch("draw")); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "draw"); s.Play();
        var first = s.E<MouHxBojueStartedEvent>().Single();
        var firstUses = s.E<MouHxBojueSlashIssuedEvent>().Where(e => e.Origin.FrameId == first.FrameId).Select(e => e.Origin).ToArray();
        s.Need(s.E<MouHxBojueDeltaEvent>().Single().AbsoluteChange == 2 && firstUses.Length == 2 && firstUses[0] is { Actor: 0, Target: 1 } && firstUses[1] is { Actor: 1, Target: 0 } &&
            firstUses.All(u => s.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == u.UseId && e.CardId == 0 && e.CardKind == CardKind.Slash && e.SourceSeat == u.Actor) == 1 &&
                s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == u.UseId) == 1 && s.E<MouHxBojueSlashReturnedEvent>().Count(e => e.UseId == u.UseId && e.ActionId == u.ActionId) == 1) &&
            s.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 2) && s.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 1 && e.TargetSeat == 0 && e.Amount == 1),
            "The true +2 Hand delta issues two separate native zero-material Slashes in owner/peer order; one real outgoing ticket boosts only the first attack and each whole Use returns once.");

        s.Bojue(); s.Reach(() => s.Branch("discard")); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "discard");
        s.Reach(() => s.G.ResolutionStack.LastOrDefault() is ProgramSkillFrame { MouHxBojue.Stage: MouHxBojueStage.Discard });
        var paidParent = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Last();
        s.Need(paidParent.MouHxBojue is { Votes.Count: 2 } && s.P!.IsPrivate && s.G.CreateSnapshot(2).PendingDecision is null &&
            s.E<MouHxBojueVoteRevealedEvent>().Count(e => e.FrameId == paidParent.Id) == 2,
            "Both independent private commitments are fixed before any real discard material is paid.");
        s.Cold();
        var own = s.P!.Choices.First(c => c.Cards.Count == 1 && s.V().Hand.Any(h => h.Id == c.Cards[0] && h.Kind == CardKind.Dodge));
        s.Answer(c => c.Id == own.Id);
        s.Reach(() => s.G.ResolutionStack.LastOrDefault() is ProgramSkillFrame { MouHxBojue.Stage: MouHxBojueStage.OtherDiscard });
        s.Need(s.P!.Choices.All(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("source-zone") == "Hand" && c.Parameters.ContainsKey("slot-index")),
            "Mutual discard uses the native foreign-Hand opaque slots, with no hidden entity identity exposed.");
        s.Answer(_ => true); s.Play();
        var second = s.E<MouHxBojueStartedEvent>().Last();
        var discards = s.E<MouHxBojueDiscardedEvent>().Where(e => e.FrameId == second.FrameId).ToArray();
        s.Need(s.E<MouHxBojueDeltaEvent>().Single(e => e.FrameId == second.FrameId).AbsoluteChange == 0 && discards.Length == 3 &&
            discards.Count(e => e.Executor == 0) == 2 && discards.Count(e => e.Executor == 1) == 1 &&
            discards.All(e => s.G.CardMovements.Count(m => m.Sequence == e.Sequence && m.CardId == e.CardId && m.From == e.From && m.To == e.To) == 1) &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.ParentFrameId == second.FrameId && e.ExecutorSeat == 0 && e.ActualCount == 1) == 2 &&
            s.E<MouHxYangweiTicketEvent>().Count(e => !e.Outgoing) == 1 && !s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == Bojue),
            "Draw/discard nets0 only after both native branches return; real self-discard and first mutual discard count the actual executor and issue one incoming ticket, without refreshing the twice-per-Play activation.");
        var damageCount = s.E<DamageAppliedEvent>().Length;
        s.Driver("direct-duel", [1, 0]); s.Play();
        s.Need(s.E<DamageAppliedEvent>().Skip(damageCount).Single() is { SourceSeat: 1, TargetSeat: 0, Amount: 2 } &&
            s.E<MouHxYangweiConsumedEvent>().Count(e => !e.Outgoing) == 1 && s.E<MouHxBojueSlashIssuedEvent>().Length == 2,
            "A later real Duel damage consumes the independent incoming ticket; its ordinary Played response frontier creates no Bojue Slash.");

        s.End(); s.Play();
        var middleTurn = s.G.State.TurnNumber;
        s.Driver("draw-two"); s.Play();
        s.Need(s.V().ActiveEffects?.Any(e => e.SkillId == "mou-ol:yangwei" && e.Description.Contains("造成的伤害+1", StringComparison.Ordinal)) == true,
            "The newly armed outstanding outgoing ticket is public and survives until a positive native damage attempt.");
        s.End(); s.Play();
        s.Need(!s.E<MouHxYangweiTicketEvent>().Any(e => e.Turn == middleTurn && !e.Outgoing) &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Any(e => e.TurnNumber == middleTurn && e.ExecutorSeat == 0 && e.Phase?.Kind == ActualDiscardRecoveryPhaseKind.Discard),
            "Actual Discard-phase logical payments are frozen on their invoice and do not arm the outside-Discard direction.");
        s.Driver("draw-two"); s.Play();
        var old = s.E<DamageAppliedEvent>().Length;
        var material = s.V().Hand.First(c => c.Kind == CardKind.Slash).Id;
        s.Slash(material, 3); s.Play();
        s.Need(s.E<DamageAppliedEvent>().Skip(old).Single() is { SourceSeat: 0, TargetSeat: 3, Amount: 3 } &&
            s.E<MouHxYangweiTicketEvent>().Count(e => e.Outgoing) == 3 && s.E<MouHxYangweiConsumedEvent>().Count(e => e.Outgoing) == 3 &&
            s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 &&
            s.V().ActiveEffects?.Any(e => e.SkillId == "mou-ol:yangwei") != true,
            "Unused outgoing tickets from two different actual turns stack independently and are all consumed by one genuinely paid ordinary Slash, without leaking into later damage.");
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G = game;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool Branch(string branch) => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mou-hua-xiong-bojue" && c.Parameters.GetValueOrDefault("branch") == branch) == true;
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Bojue() => Accept(new UseProgramSkillCommand(0, MouHuaXiongOlChecks.Bojue, "simultaneous-choice", [], [1], G.Revision, P!.PromptId));
        public void Driver(string branch, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, MouHuaXiongOlChecks.Driver, branch, [], targets ?? [], G.Revision, P!.PromptId));
        public void Slash(int id, int target)
        {
            var a = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId == id && a.ConversionSource is null && a.TargetSeats.SequenceEqual([target]));
            Need(a is not null, "One real printed Slash retains native legality after the next actual Play starts.");
            Accept(new PlayCardCommand(0, id, a!.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId));
        }
        public void Answer(Func<PromptChoice, bool> match)
        {
            var p = P!; var c = p.Choices.FirstOrDefault(match);
            Need(p.PlayerSeat == 0 && c is not null, "Only a published real human choice can be submitted.");
            Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision));
        }
        public void Accept(GameCommand c)
        {
            try { var result = G.Submit(c); Need(result.Accepted, result.Error?.Message ?? "Real native command rejected."); }
            catch (Exception e) { throw new InvalidOperationException(e.Message + Diagnostic(), e); }
        }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var n = 0; n < 220; n++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "Unexpected native cutoff at " + line);
                if (P is not { PlayerSeat: 0 } p) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)
                    Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage");
                else throw new InvalidOperationException("Unexpected genuine prompt at " + line + Diagnostic());
            }
            throw new InvalidOperationException("Small native Hua Xiong flow exceeded its bounded cursor at " + line + Diagnostic());
        }
        public void Cold()
        {
            var view = SnapshotJson.Serialize(G.CreateSnapshot(0));
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(view == SnapshotJson.Serialize(G.CreateSnapshot(0)), "The committed pair and unpaid private discard selection survive one Cold without paying either branch twice.");
        }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, Owner = V(), Prompt = P,
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Facts = G.Events.Where(e => e.Payload.GetType().Name.StartsWith("MouHx", StringComparison.Ordinal) || e.Payload is DamageAppliedEvent or NativeLogicalDiscardInvocationRecordedEvent or NativeDrawInvocationRecordedEvent).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheStartingCard C(CardKind kind, int rank) => new(kind, Suit.Spade, rank);
            var setup = new ShanheBattleSetup(7, 1, "mou-hua-xiong-ol-real", Hero, 6, 0, 0, 0, 0, [MouHuaXiongOlChecks.Driver],
                [C(CardKind.Slash, 2), C(CardKind.Slash, 3), C(CardKind.Dodge, 4), C(CardKind.Dodge, 5), C(CardKind.Dodge, 6), C(CardKind.Dodge, 7)], [],
                [new("fixture:mhxo-enemy-a", Hp: 12) { StartingHand = [] }, new("fixture:mhxo-enemy-b", Hp: 12) { StartingHand = [] }],
                [new("fixture:mhxo-ally", Hp: 12) { StartingHand = [C(CardKind.Peach, 8), C(CardKind.Peach, 9)] }], new string('c', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 14 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-hua-xiong-ol", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var defs = new object[] {
                new { id = Driver, revision = 1, activations = new object[] {
                    new { id = "draw-two", minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null, effects = new[] { new { op = "draw", target = "owner", amount = 2 } } },
                    new { id = "direct-duel", minCards = 0, maxCards = 0, minTargets = 2, maxTargets = 2, targetKind = "anyLiving", usesPerTurn = (int?)null, effects = new[] { new { op = "startVirtualDuel", target = "owner" } } }
                } },
                new { id = Quiet, revision = 1, triggers = new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false, effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } }
            };
            var labels = new Dictionary<string, object> { [Driver] = new { name = "原生摸牌与决斗", description = "既有真实操作", activationLabels = new Dictionary<string, string> { ["draw-two"] = "真实摸二", ["direct-duel"] = "真实双人决斗" } },
                [Quiet] = new { name = "安静同行", description = "原生跳过同行出牌阶段" } };
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            foreach (var kind in new[] { CardKind.Slash, CardKind.Dodge, CardKind.Peach })
            { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:mhxo-card-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            foreach (var peer in new[] { "fixture:mhxo-ally", "fixture:mhxo-enemy-a", "fixture:mhxo-enemy-b" })
                b.AddGeneral(new(peer, "真实同行", "supporter", Quiet, peer.EndsWith("ally", StringComparison.Ordinal) ? "qun" : "wei", 12));
            b.AddDeck(new("fixture:mhxo-deck", "固定四十八张真实杀", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(i => new ContentDeckPhysicalCard("fixture:mhxo-card-Slash", Suit.Club, i % 13 + 1)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋华雄OL真实小局", 4, 4, new Dictionary<string, int>(), "fixture:mhxo-deck",
                GeneralPoolIds: [Hero, "fixture:mhxo-ally", "fixture:mhxo-enemy-a", "fixture:mhxo-enemy-b"], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
