using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class MouGanNingMobileChecks
{
    private const string Hero = "mou:gan-ning-mobile", Qixi = "mou-mobile:qixi", Fenwei = "mou-mobile:fenwei";
    public static void RealRepeatedGuessesAndIndependentWeiTargetsCold()
    {
        var s = Scene.Start(); s.Play();
        s.Accept(new UseProgramSkillCommand(0, Fenwei, "place-wei", [], [], s.G.Revision, s.P!.PromptId));
        foreach (var (kind, seat) in new[] { (CardKind.Peach, 1), (CardKind.Dodge, 2) })
        {
            s.Reach(() => s.Branch("card"));
            var card = s.V().Hand.First(c => c.Kind == kind && c.Suit == Suit.Heart).Id;
            s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "card" && c.Cards.SequenceEqual([card]));
            s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "recipient" && c.Targets.SequenceEqual([seat]));
        }
        s.Reach(() => s.Branch("done")); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "done"); s.Play();
        var placed = s.E<MouGanNingWeiPlacedEvent>().Where(e => e.Source.OwnerSeat == 0).ToArray();
        s.Need(placed.Length == 2 && placed.Select(e => e.RecipientSeat).SequenceEqual([1, 2]) && placed.All(e => e.From == CardLocation.Hand(0) && e.To.Zone == CardZoneKind.PublicPersistentPile && e.To.OwnerSeat == e.RecipientSeat) &&
            s.E<MouGanNingDrawReturnedEvent>() is [{ Requested: 2, Actual: 2 }] &&
            Enumerable.Range(0, 4).All(viewer => placed.All(e => s.G.CreateSnapshot(viewer).Players[e.RecipientSeat].PublicPersistentPileCards?.Any(c => c.Id == e.CardId) == true)) &&
            !s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == Fenwei),
            "The accepted limited skill moves two real owned entities to distinct public Wei piles and actually draws two. Every viewer sees those same Wei entities and the game-limited activation stays spent.");
        var iron = s.V().Hand.Single(c => c.Kind == CardKind.IronChain);
        var action = s.G.GetHumanLegalActions().Single(a => a.CardId == iron.Id && a.TargetSeats.SequenceEqual([1, 2]));
        s.Accept(new PlayCardCommand(0, iron.Id, action.TargetSeats, s.G.Revision, s.P!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
        s.Reach(() => s.Branch("obtain")); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "obtain" && c.Targets.SequenceEqual([1]));
        s.Reach(() => s.Branch("cancel")); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "cancel" && c.Targets.SequenceEqual([2])); s.Play();
        var spent = s.E<MouGanNingWeiSpentEvent>(); var cancellation = s.E<MouGanNingTargetCancelledEvent>();
        s.Need(spent.Length == 2 && spent[0].To == CardLocation.Hand(1) && spent[0].CardId == placed[0].CardId && spent[1].To == CardLocation.DiscardPile && spent[1].CardId == placed[1].CardId &&
            spent.Select(e => e.UseId).Distinct().Count() == 1 && cancellation is [{ TargetSeat: 2 }] && cancellation[0].UseId == spent[0].UseId &&
            s.V(1).IsChained && !s.V(2).IsChained && s.V(1).PublicPersistentPileCount == 0 && s.V(2).PublicPersistentPileCount == 0 &&
            s.E<CardUseDeclaredEvent>().Count(e => e.CardKind == CardKind.IronChain) == 1 && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == spent[0].UseId) == 1,
            "The same genuine two-target Iron Chain independently returns the first Wei and discards the second. Only the second target is cancelled; the original use completes once, with no synthetic use and no repayment after Cold.");
        s.End();
        s.Reach(() => s.P is { PlayerSeat: 0 } && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "guess"));
        s.Need(s.G.ResolutionStack.OfType<ProgramSkillFrame>().Last().OwnerSeat == 2 && s.E<MouGanNingGuessedEvent>().Length == 0,
            "The real opposing AI independently activates Qixi on the human, rather than being answered by the fixture.");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "guess" && c.Parameters.GetValueOrDefault("suit") == "Heart");
        s.Reach(() => s.P is { PlayerSeat: 0 } && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "guess"));
        s.Need(s.P!.Choices.All(c => c.Parameters.GetValueOrDefault("suit") != "Heart") && s.E<MouGanNingGuessedEvent>() is [{ Correct: false, Wrong: 1 }] && s.E<ProgramCardsRevealedEvent>().All(e => e.SkillId != Qixi),
            "A genuinely wrong suit is not offered again. The real skill owner elected to continue, and the private Hand was not revealed on the wrong answer.");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "guess" && c.Parameters.GetValueOrDefault("suit") == "Club");
        s.Reach(() => s.E<MouGanNingCompletedEvent>().Any(e => s.E<MouGanNingStartedEvent>().Any(start => start.FrameId == e.FrameId && start.Source.OwnerSeat == 2 && start.Op == SkillProgramEffectOp.ResolveMouGanNingQixi)));
        var q = s.E<MouGanNingStartedEvent>().Single(e => e.Source.OwnerSeat == 2 && e.Op == SkillProgramEffectOp.ResolveMouGanNingQixi);
        s.Need(s.E<MouGanNingGuessedEvent>().Where(e => e.FrameId == q.FrameId).Select(e => e.Correct).SequenceEqual([false, true]) &&
            s.E<MouGanNingRepeatEvent>().Single(e => e.FrameId == q.FrameId).Continue && s.E<ProgramCardsRevealedEvent>().Count(e => e.FrameId == q.FrameId && e.SkillId == Qixi) == 1 &&
            s.E<MouGanNingDiscardedEvent>().Where(e => e.FrameId == q.FrameId).ToArray() is [{ From.Zone: CardZoneKind.Hand, To.Zone: CardZoneKind.DiscardPile }] &&
            s.E<MouGanNingCompletedEvent>().Single(e => e.FrameId == q.FrameId) is { Wrong: 1, Discarded: 1 },
            "The subsequent correct maximum suit reveals the actual Hand once, then Qixi really discards one current region entity for the earlier wrong guess.");
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool Branch(string b) => P is { PlayerSeat: 0 } && P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == b);
        public bool IsPlay() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard };
        public void Play() => Reach(IsPlay);
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> match)
        { var p = P!; var choice = p.Choices.FirstOrDefault(match); Need(p.PlayerSeat == 0 && choice is not null, "Only a published real human-owned choice is answered."); Accept(new AnswerPromptCommand(0, p.PromptId, choice!.Id, G.Revision)); }
        public void Accept(GameCommand command) { try { var r = G.Submit(command); Need(r.Accepted, r.Error?.Message ?? "Command rejected."); } catch (Exception e) { throw new InvalidOperationException(e.Message + Diagnostic(), e); } }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 180; i++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "Unexpected native cutoff at " + line);
                if (P is not { PlayerSeat: 0 } p) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else throw new InvalidOperationException("Unexpected human prompt at " + line + Diagnostic());
            }
            throw new InvalidOperationException("Bounded Gan Ning native flow not reached at " + line + Diagnostic());
        }
        public void Cold() { var before = SnapshotJson.Serialize(G.CreateSnapshot(0)); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == SnapshotJson.Serialize(G.CreateSnapshot(0)), "The accepted Wei target cursor and actual first payment survive Cold without repayment."); }
        public void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, Prompt = P, Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount, V(i).IsChained }), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.Where(e => e.Payload.GetType().Name.StartsWith("MouGanNing", StringComparison.Ordinal) || e.Payload is ProgramCardsRevealedEvent).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(7, 1, "mougn-real", Hero, 4, 0, 0, 0, 0, [],
                [new(CardKind.IronChain, Suit.Heart, 7), new(CardKind.Peach, Suit.Heart, 7), new(CardKind.Dodge, Suit.Heart, 7)], [],
                [new("fixture:mougn-guesser", Hp: 12) { StartingHand = [new(CardKind.Dodge, Suit.Club, 7), new(CardKind.Dodge, Suit.Club, 7)] }, new("fixture:mougn-enemy", Hp: 12) { StartingHand = [] }],
                [new("fixture:mougn-ally", Hp: 12) { StartingHand = [] }], new string('b', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-gan-ning-mobile", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            foreach (var kind in new[] { CardKind.Dodge, CardKind.Peach, CardKind.IronChain }) { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:mougn-card-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            var rules = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = new[] { new { id = "fixture:mougn-quiet", revision = 1, triggers = new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false, effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } } } });
            var programs = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> { ["fixture:mougn-quiet"] = new { name = "安静同行", description = "真实跳过同行的出牌阶段" } } }));
            var p = programs.Programs["fixture:mougn-quiet"]; b.AddSkill(new("fixture:mougn-quiet", "安静同行", "真实跳过同行出牌阶段") { Program = p, ProgramPresentation = programs.Presentations[p.Id] });
            b.AddGeneral(new("fixture:mougn-ally", "同行", "", "standard:none", "wu", 12, [p.Id]));
            b.AddGeneral(new("fixture:mougn-enemy", "敌方同行", "", "standard:none", "wei", 12, [p.Id]));
            b.AddGeneral(new("fixture:mougn-guesser", "真实奇袭发动者", "", Qixi, "wei", 12));
            b.AddDeck(new("fixture:mougn-deck", "固定库存", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("fixture:mougn-card-Dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋甘宁真实技能小局", 4, 4, new Dictionary<string, int>(), "fixture:mougn-deck", GeneralPoolIds: [Hero, "fixture:mougn-ally", "fixture:mougn-guesser", "fixture:mougn-enemy"], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
