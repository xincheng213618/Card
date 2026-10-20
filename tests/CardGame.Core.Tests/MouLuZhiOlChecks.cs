using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouLuZhiOlChecks
{
    private const string Hero = "mou:lu-zhi-ol", Driver = "fixture:mlz-driver", Quiet = "fixture:mlz-quiet";
    public static void RealRedPressureCounterAndRoundDrawCold()
    {
        var s = Scene.Start(false); s.Play();
        s.Need(s.V().Hp == 3 && s.V().MaxHp == 4, "Official initial3/maximum4 is preserved.");
        s.Use(CardKind.Slash, 2); s.Reach(() => s.Branch("card"));
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "card" && s.V().Hand.Any(h => h.Id == c.Cards.Single() && h.Kind == CardKind.Peach));
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "confirm"); s.Play();
        var press = s.E<MouLuZhiOlPressUsedEvent>().Single(); var first = s.E<MouLuZhiOlStartedEvent>().Single(e => e.FrameId == press.FrameId);
        s.Need(press.Required == 1 && s.E<MouLuZhiOlResponseBannedEvent>().Single().Ban.TargetSeat == 2 && s.V(2).Hp == 11 && s.V(2).HandCount == 1 && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == first.OriginalId) == 1 && s.E<NativeLogicalDiscardInvocationRecordedEvent>().Any(e => e.ExecutorSeat == 0 && e.SubjectSeat == 0 && e.ActualCount == 1 && e.Reason.Value == "skill-program.mou-ol:sibing.discard"),
            "Real red payment leaves the opponent's black Dodge unspent; its independent refusal prohibits only this original target's response and the original Slash really deals1.");
        s.Accept(new UseProgramSkillCommand(0, Driver, "direct-duel", [], [3, 0], s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
        var response = s.V().Hand.First(c => c.Kind == CardKind.Slash).Id; s.Answer(c => c.Cards.Contains(response));
        s.Reach(() => s.Branch("card") && s.G.ResolutionStack.OfType<ProgramSkillFrame>().Last().MouLuZhiOl?.Stage == MouLuZhiOlStage.Black);
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "card" && s.V().Hand.Any(h => h.Id == c.Cards.Single() && h.Kind == CardKind.Dodge && h.Suit == Suit.Club)); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "confirm");
        s.Reach(() => s.Branch("slash")); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "slash" && c.Targets.SequenceEqual([2])); s.Play();
        var counter = s.E<MouLuZhiOlCounterIssuedEvent>().Single().Origin;
        s.Need(counter.ActorSeat == 0 && counter.TargetSeat == 2 && s.E<MouLuZhiOlDirectDeclaredEvent>().Single().Duel is { Actor: 3, Target: 0 } && s.E<MouLuZhiOlDirectResumedEvent>().Count(e => e.Completed) == 1 && s.E<MouLuZhiOlCounterReturnedEvent>().Count(e => e.UseId == counter.UseId) == 1 && s.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == counter.UseId && e.CardId == 0 && e.CardKind == CardKind.Slash) == 1 && s.E<MouLuZhiOlPressUsedEvent>().Length == 1,
            "A genuine legacy direct Duel uses the human's real Slash as Played, damages its original actor instead, then independently accepts one real black cost. Cold preserves that paid cost and a genuine zero-material Slash may target another legal character, without refreshing red pressure or fabricating a Duel Use.");

        var t = Scene.Start(true); t.Reach(() => t.P is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == "mou-ol:liance"));
        t.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); t.Play();
        t.Need(t.E<MouLuZhiOlDrawReturnedEvent>().Single() is { Requested: 1, Actual: 1, Replacement: 0 } && t.E<MouLuZhiOlLianceArmedEvent>().Length == 1 && t.V().HandCount == 4,
            "A real tied-minimum Hand change accepts the shared once-per-round opportunity, draws to maximum4 and arms only after its native draw returns.");
        t.Use(CardKind.Duel, 2); t.Reach(() => t.Branch("card"));
        t.Answer(c => c.Parameters.GetValueOrDefault("branch") == "card" && t.V().Hand.Any(h => h.Id == c.Cards.Single() && h.Suit == Suit.Heart)); t.Answer(c => c.Parameters.GetValueOrDefault("branch") == "confirm"); t.Play();
        t.Need(t.E<MouLuZhiOlDiscardedEvent>().Any(e => e.Chooser == 2 && e.Purpose == "target-red" && e.Actual == 1) && t.E<NativeLogicalDiscardInvocationRecordedEvent>().Any(e => e.ExecutorSeat == 2 && e.SubjectSeat == 2 && e.ActualCount == 1 && e.Reason.Value == "skill-program.mou-ol:sibing.discard") && t.E<MouLuZhiOlResponseBannedEvent>().Length == 0 && t.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount == 2) && t.E<MouLuZhiOlLianceConsumedEvent>().Length == 1 && t.E<MouLuZhiOlLianceArmedEvent>().Length == 1,
            "The target actually selects and discards its own red Hand entity. Real Duel damage consumes the one global next-damage increment; subsequent cost/loss changes do not refresh the shared round budget.");
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int i = 0) => G.CreateSnapshot(i).Players[i];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool Branch(string b) => P is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mou-lu-zhi-ol" && c.Parameters.GetValueOrDefault("branch") == b);
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Use(CardKind kind, int target)
        {
            var ids = V().Hand.Where(c => c.Kind == kind).Select(c => c.Id).ToHashSet(); var a = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId is { } id && ids.Contains(id) && a.TargetSeats.SequenceEqual([target]));
            Need(a is not null, "Missing actual physical " + kind + " action."); Accept(new PlayCardCommand(0, a!.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource });
        }
        public void Answer(Func<PromptChoice, bool> match) { var p = P!; var c = p.Choices.FirstOrDefault(match); Need(p.PlayerSeat == 0 && c is not null, "Only a published human choice is answered."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Accept(GameCommand c) { try { var r = G.Submit(c); Need(r.Accepted, r.Error?.Message ?? "Native command rejected."); } catch (Exception e) { throw new InvalidOperationException(e.Message + Diagnostic(), e); } }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var n = 0; n < 180; n++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "Unexpected real cutoff at " + line);
                if (P is not { PlayerSeat: 0 } p) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage");
                else if (p.Kind == DecisionKind.ProgramTrigger && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else throw new InvalidOperationException("Unexpected true prompt at " + line + Diagnostic());
            }
            throw new InvalidOperationException("Bounded native Lu Zhi flow not reached at " + line + Diagnostic());
        }
        public void Cold() { var before = SnapshotJson.Serialize(G.CreateSnapshot(0)); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == SnapshotJson.Serialize(G.CreateSnapshot(0)), "Paid black discard and original direct Duel parent survive Cold exactly once."); }
        public void Need(bool b, string text) { if (!b) throw new InvalidOperationException(text + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, Prompt = P, Owner = V(), Actions = P?.Kind == DecisionKind.PlayCard ? G.GetHumanLegalActions().ToArray() : [], Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.Where(e => e.Payload.GetType().Name.StartsWith("MouLuZhiOl", StringComparison.Ordinal) || e.Payload is DamageAppliedEvent or NativeLogicalDiscardInvocationRecordedEvent).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        public static Scene Start(bool liance)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var own = liance ? new ShanheStartingCard[] { new(CardKind.Duel, Suit.Spade, 7) } : [new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Slash, Suit.Club, 8), new(CardKind.Peach, Suit.Heart, 4), new(CardKind.Dodge, Suit.Club, 5)];
            var setup = new ShanheBattleSetup(7, 1, "moulz-real", Hero, 3, 0, 0, 0, 0, [Driver], own, [new(CardKind.QilinBow, Suit.Club, 7)],
                [new("fixture:mlz-enemy-a", Hp: 12) { StartingHand = liance ? [new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.Dodge, Suit.Club, 3), new(CardKind.Dodge, Suit.Spade, 4)] : [new(CardKind.Dodge, Suit.Club, 3)] }, new("fixture:mlz-enemy-b", Hp: 12) { StartingHand = liance ? [new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.Dodge, Suit.Club, 3), new(CardKind.Dodge, Suit.Spade, 4)] : [] }],
                [new("fixture:mlz-ally", Hp: 12) { StartingHand = liance ? [new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.Dodge, Suit.Club, 3), new(CardKind.Dodge, Suit.Spade, 4)] : [] }], new string('b', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 2 }, registry); var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-lu-zhi-ol", new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            foreach (var kind in new[] { CardKind.Slash, CardKind.Dodge, CardKind.Peach, CardKind.Duel, CardKind.QilinBow }) { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:mlz-card-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            var defs = new object[] {
                new { id = Driver, revision = 1, modifiers = new[] { new { id = "second-slash", query = "slashLimit", operation = "add", value = 1, priority = 0 } }, activations = new[] { new { id = "direct-duel", minCards = 0, maxCards = 0, minTargets = 2, maxTargets = 2, targetKind = "anyLiving", usesPerTurn = (int?)null, effects = new[] { new { op = "startVirtualDuel", target = "owner" } } } } },
                new { id = Quiet, revision = 1, triggers = new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false, effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } }
            };
            var labels = new Dictionary<string, object> { [Driver] = new { name = "原生决斗", description = "真实有序两角色零牌决斗与额外一次普通杀限额", activationLabels = new Dictionary<string,string> { ["direct-duel"] = "原生决斗" } }, [Quiet] = new { name = "安静同行", description = "原生跳过出牌阶段" } };
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            foreach (var peer in new[] { "fixture:mlz-ally", "fixture:mlz-enemy-a", "fixture:mlz-enemy-b" }) b.AddGeneral(new(peer, "真实同行", "supporter", Quiet, peer.EndsWith("ally", StringComparison.Ordinal) ? "shu" : "wei", 12));
            b.AddDeck(new("fixture:mlz-deck", "固定真实红闪牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 36).Select(_ => new ContentDeckPhysicalCard("fixture:mlz-card-Dodge", Suit.Heart, 9)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋卢植完整真实主流", 4, 4, new Dictionary<string,int>(), "fixture:mlz-deck", GeneralPoolIds: [Hero, "fixture:mlz-ally", "fixture:mlz-enemy-a", "fixture:mlz-enemy-b"], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string,int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
