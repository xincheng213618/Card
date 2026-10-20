using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class MouGuoYouZhiMobileChecks
{
    private const string Hero = "mou:guo-you-zhi-mobile", Bique = "mou-mobile:bique", Zhongchun = "mou-mobile:zhongchun", Driver = "fixture:mougyz-driver", Quiet = "fixture:mougyz-quiet";
    public static void RealPrivateGiftHandAdjustmentAndExactDiscardReuseCold()
    {
        var s = Scene.Start(); s.Play();
        s.UseBique(); s.Reach(() => s.Branch("gift-confirm"));
        s.Need((s.G.CreateSnapshot(0).PrivateRevealedCards?.Count ?? 0) == 1 && Enumerable.Range(1, 3).All(i => (s.G.CreateSnapshot(i).PrivateRevealedCards?.Count ?? 0) == 0),
            "Bique really inspects the other Hand for the owner before choosing gifts. The unchanged opponent Hand stays private to every other viewer.");
        s.Cold();
        var giftIds = new[] { CardKind.Slash, CardKind.Peach, CardKind.IronChain, CardKind.Crossbow }.Select(kind => s.V().Hand.Single(c => c.Kind == kind).Id).ToArray();
        foreach (var id in giftIds) s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "gift-card" && c.Cards.SequenceEqual([id]));
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "gift-confirm"); s.Reach(() => s.Branch("adjust"));
        var first = s.E<MouGuoYouZhiStartedEvent>().Single(e => e.Op == SkillProgramEffectOp.ResolveMouGuoYouZhiBique);
        s.Need(s.E<MouGuoYouZhiGiftReturnedEvent>().Single(e => e.FrameId == first.FrameId) is { ActualCount: 4, DistinctNames: 4 } && giftIds.All(id => s.V(1).Hand.Any(c => c.Id == id)) && s.V().Hand.Count == 3,
            "Four absent names move as their actual Hand originals. Distinct actual gifted names are frozen at four after real gift children, with only three cards left in the owner Hand.");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "adjust"); s.Play();
        s.Need(s.V().Hand.Count == 4 && s.E<MouGuoYouZhiDrawReturnedEvent>() is [{ Requested: 1, Actual: 1 }] && s.E<MouGuoYouZhiCompletedEvent>().Count(e => e.FrameId == first.FrameId) == 1 && !s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == Bique),
            "The optional adjustment really draws one to four, and the already performed inspection consumed this actual Play activation.");

        var discarded = s.V().Hand.Single(c => c.Kind == CardKind.DrawTwo).Id;
        s.Accept(new UseProgramSkillCommand(0, Driver, "discard-one", [discarded], [], s.G.Revision, s.P!.PromptId)); s.Play();
        var invoice = s.E<NativeLogicalDiscardInvocationRecordedEvent>().Single(e => e.ExecutorSeat == 0 && s.G.CardMovements.Any(m => m.CardId == discarded && m.Sequence > e.SequenceBefore && m.Sequence <= e.SequenceAfter && m.To == CardLocation.DiscardPile));
        s.End(); s.Reach(() => s.P?.SkillPrompt?.SkillId == Zhongchun);
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); s.Reach(() => s.Branch("obtain"));
        s.Need(s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "obtain" && c.Cards.SequenceEqual([discarded])), "The real own Ending recognizes the original entity discarded by this actor during this actual turn.");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "obtain" && c.Cards.SequenceEqual([discarded])); s.Reach(() => s.Branch("use")); s.Cold();
        var z = s.E<MouGuoYouZhiStartedEvent>().Single(e => e.Op == SkillProgramEffectOp.ResolveMouGuoYouZhiZhongchun);
        s.Need(z.ActorSeat == 0 && s.V().Hand.Any(c => c.Id == discarded) && s.E<MouGuoYouZhiMovementEvent>().Count(e => e.FrameId == z.FrameId && e.CardId == discarded && e.From == CardLocation.DiscardPile && e.To == CardLocation.Hand(0)) == 1,
            "Zhongchun first really obtains the exact discarded entity, drains its gain children, then independently offers the beneficiary a genuine Use or refusal.");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "use");
        s.Reach(() => s.E<MouGuoYouZhiCompletedEvent>().Any(e => e.FrameId == z.FrameId));
        var native = s.E<MouGuoYouZhiNativeIssuedEvent>().Single(e => e.FrameId == z.FrameId);
        s.Need(native.CardId == discarded && native.ActorSeat == 0 && s.E<MouGuoYouZhiNativeReturnedEvent>().Count(e => e.UseId == native.UseId) == 1 &&
            s.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == native.UseId && e.CardId == discarded && e.CardKind == CardKind.DrawTwo && e.SourceSeat == 0) == 1 && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == native.UseId) == 1 &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.InvocationId == invoice.InvocationId) == 1,
            "The obtained Draw Two uses its original entity and native actor exactly once outside Play, returns through its actual use frame, and does not repay or mistake use cleanup for the original discard.");

        s.Reach(() => s.G.State.TurnNumber == 5 && s.IsPlay()); s.UseBique(); s.Reach(() => s.Branch("gift-confirm"));
        s.Need(s.P!.Choices.All(c => c.Parameters.GetValueOrDefault("branch") != "gift-card"), "The target already has Dodge: current watched Hand names exclude the owner's repeated Dodge gifts on the next actual Play.");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "gift-confirm"); s.Reach(() => s.Branch("adjust")); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "adjust");
        s.Reach(() => s.Branch("discard-card"));
        var last = s.E<MouGuoYouZhiStartedEvent>().Last(e => e.Op == SkillProgramEffectOp.ResolveMouGuoYouZhiBique); var excess = s.V().Hand.Select(c => c.Id).ToArray();
        foreach (var id in excess) s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "discard-card" && c.Cards.SequenceEqual([id]));
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "discard-confirm"); s.Play();
        s.Need(s.E<MouGuoYouZhiGiftReturnedEvent>().Single(e => e.FrameId == last.FrameId) is { ActualCount: 0, DistinctNames: 0 } && s.V().Hand.Count == 0 &&
            s.E<MouGuoYouZhiMovementEvent>().Where(e => e.FrameId == last.FrameId && e.Reason.Value == "skill-program.mou-mobile:bique.discard" && e.From == CardLocation.Hand(0) && e.To == CardLocation.DiscardPile).Select(e => e.CardId).Order().SequenceEqual(excess.Order()) &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.ParentFrameId == last.FrameId && e.ExecutorSeat == 0 && e.ActualCount == excess.Length) == 1,
            "A real zero-card gift still spends inspection. Optional normalization to zero discards every excess actual Hand entity through one logical native discard, rather than only offering extra draws.");
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int i = 0) => G.CreateSnapshot(i).Players[i];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool Branch(string branch) => P is { PlayerSeat: 0 } && P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == branch);
        public bool IsPlay() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard };
        public void Play() => Reach(IsPlay);
        public void UseBique() => Accept(new UseProgramSkillCommand(0, Bique, "inspect-and-give", [], [1], G.Revision, P!.PromptId));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> match) { var p = P!; var c = p.Choices.FirstOrDefault(match); Need(p.PlayerSeat == 0 && c is not null, "Only a published human-owned choice is submitted."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Accept(GameCommand command) { try { var r = G.Submit(command); Need(r.Accepted, r.Error?.Message ?? "Rejected native command."); } catch (Exception e) { throw new InvalidOperationException(e.Message + Diagnostic(), e); } }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var n = 0; n < 220; n++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "Unexpected terminal cutoff at " + line);
                if (P is not { PlayerSeat: 0 } p) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else throw new InvalidOperationException("Unexpected real human prompt at " + line + Diagnostic());
            }
            throw new InvalidOperationException("Bounded real Guo You Zhi flow not reached at " + line + Diagnostic());
        }
        public void Cold() { var before = SnapshotJson.Serialize(G.CreateSnapshot(0)); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == SnapshotJson.Serialize(G.CreateSnapshot(0)), "The actual private watch or beneficiary exact-use request survives Cold without another gift or obtain."); }
        public void Need(bool result, string message) { if (!result) throw new InvalidOperationException(message + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, Prompt = P, Hand = V().Hand, Actions = G.GetHumanLegalActions(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.Where(e => e.Payload.GetType().Name.StartsWith("MouGuoYouZhi", StringComparison.Ordinal) || e.Payload is NativeLogicalDiscardInvocationRecordedEvent or TurnStartedEvent).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(7, 1, "mougyz-real", Hero, 3, 0, 0, 0, 0, [Driver],
                [new(CardKind.Slash, Suit.Heart, 7), new(CardKind.Peach, Suit.Heart, 7), new(CardKind.IronChain, Suit.Club, 7), new(CardKind.Crossbow, Suit.Club, 7), new(CardKind.DrawTwo, Suit.Heart, 7)], [],
                [new("fixture:mougyz-enemy-a", Hp: 12) { StartingHand = [] }, new("fixture:mougyz-enemy-b", Hp: 12) { StartingHand = [] }], [new("fixture:mougyz-ally", Hp: 12) { StartingHand = [new(CardKind.Dodge, Suit.Club, 7)] }], new string('d', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-guo-you-zhi-mobile", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            foreach (var kind in new[] { CardKind.Dodge, CardKind.Slash, CardKind.Peach, CardKind.Crossbow, CardKind.IronChain, CardKind.DrawTwo }) { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:mougyz-card-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            var definitions = new object[] {
                new { id = Quiet, revision = 1, triggers = new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false, effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } },
                new { id = Driver, revision = 1, activations = new[] { new { id = "discard-one", minCards = 1, maxCards = 1, sourceZones = new[] { "hand" }, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null, effects = new[] { new { op = "discardSelected", target = "owner", amount = 1 } } } } }
            };
            var labels = new Dictionary<string, object> { [Quiet] = new { name = "安静同行", description = "实际跳过同行出牌阶段" }, [Driver] = new { name = "实际弃牌", description = "以原生指令弃置一张手牌", activationLabels = new Dictionary<string, string> { ["discard-one"] = "实际弃一张手牌" } } };
            var cata = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in cata.Programs) b.AddSkill(new(id, cata.Presentations[id].Name, cata.Presentations[id].Description) { Program = p, ProgramPresentation = cata.Presentations[id] });
            foreach (var id in new[] { "fixture:mougyz-ally", "fixture:mougyz-enemy-a", "fixture:mougyz-enemy-b" }) b.AddGeneral(new(id, "同行", "", "standard:none", id.EndsWith("ally", StringComparison.Ordinal) ? "shu" : "wei", 12, [Quiet]));
            b.AddDeck(new("fixture:mougyz-deck", "固定真实牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("fixture:mougyz-card-" + CardKind.Dodge, Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋郭攸之真实主流程", 4, 4, new Dictionary<string, int>(), "fixture:mougyz-deck", GeneralPoolIds: [Hero, "fixture:mougyz-ally", "fixture:mougyz-enemy-a", "fixture:mougyz-enemy-b"], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
