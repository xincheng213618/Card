using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class OlShenCaoPiChecks
{
    private const string Hero = "ol:shen-cao-pi", Driver = "fixture:ol-scp-driver", Quiet = "fixture:ol-scp-quiet", Peer = "fixture:ol-scp-peer", Deck = "fixture:ol-scp-deck";
    public static void ChuyuanNativeDrawForeignDepositCapacityAndCold()
    {
        var s = Scene.Start(); s.Play(); var limit = s.Limit();
        s.BeginDeposit("two"); var f = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(p => p.OlShenCaoPi is not null);
        s.Need(f.OlShenCaoPi is { TargetSeat: 2, DrawOutcome.ActualDrawIds.Count: 1 } && s.E<OlChuyuanDrawReturnedEvent>() is [{ Actual: 1 }] &&
            s.P is { PlayerSeat: 2, IsPrivate: true } && s.G.CreateSnapshot(0).PendingDecision is null,
            "A single actual two-point damage event gives one native draw and a recipient-only deposit choice on Cao Pi's original skill frame.");
        s.Cold(); s.Play();
        for (var i = 0; i < 4; i++) { s.BeginDeposit("two"); s.Play(); }
        s.Need(s.Pile().Length == 5 && s.E<OlChuyuanDrawReturnedEvent>().Length == 5 && s.E<OlChuyuanDepositedEvent>().Length == 5 && s.Limit() == limit,
            "Five genuine draws and chosen hand entities fill the five-HP Chu limit, without the deleted first-draft hand-limit bonus.");
        s.Use("two"); s.Play();
        s.Need(s.E<OlChuyuanDrawReturnedEvent>().Length == 5 && s.Pile().Length == 5 &&
            s.E<OlChuyuanDepositedEvent>().All(e => s.G.CardMovements.Count(m => m.CardId == e.CardId && m.From == CardLocation.Hand(2) && m.To == e.Pile && m.Sequence > e.Before && m.Sequence <= e.After) == 1),
            "Chuyuan cannot start when Chu equals maximum HP; every existing public Chu is backed by its recipient's once-paid real hand movement.");
        s.Replay();
    }
    public static void TwoActualAwakeningsObtainChuAndGrantPermanentSkillCold()
    {
        var s = Scene.Start(); s.Play();
        for (var i = 0; i < 3; i++) { s.BeginDeposit("one"); s.Play(); }
        var first = s.Pile(); var max = s.V(0).MaxHp; s.End();
        s.Reach(() => s.E<SkillAwakenedEvent>().Any(e => e.SkillId == "ol:dengji")); s.Play();
        s.Need(s.V(0).MaxHp == max - 1 && s.Pile().Length == 0 && first.All(id => s.V(0).Hand.Any(c => c.Id == id)) &&
            s.V(0).Skills!.Any(k => k.ContentId == "ol:jianxiong") && s.V(0).Skills!.Any(k => k.ContentId == "ol:tianxing") &&
            s.E<OlCaoPiPileObtainedEvent>() is [{ Count: 3 }],
            "The first actual preparation awakening pays one maximum HP, obtains the exact original Chu entities and genuinely acquires OL Jie Jianxiong plus Tianxing.");
        for (var i = 0; i < 3; i++) { s.BeginDeposit("one"); s.Play(); }
        var second = s.Pile(); s.End();
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("value") == "boundary:zhiheng") == true);
        s.Need(s.V(0).MaxHp == max - 2 && s.Pile().Length == 0 && second.All(id => s.V(0).Hand.Any(c => c.Id == id)) &&
            !s.V(0).Skills!.Any(k => k.ContentId == "ol:chuyuan") && s.P!.Choices.Count == 3 &&
            s.P.Choices.All(c => c.Parameters.GetValueOrDefault("value") != "boundary:fangquan"),
            "Tianxing's later original preparation pays once, obtains its second real Chu set, loses Chuyuan before choosing, and offers only the three skills retained by the formal May 29 revision.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("value") == "boundary:zhiheng"); s.Play();
        s.Need(s.E<OlTianxingGrantedEvent>() is [{ AcquiredSkillId: "boundary:zhiheng" }] && s.G.GetHumanLegalActions().Any(a => a.SkillId == "boundary:zhiheng") &&
            s.E<OlCaoPiPileObtainedEvent>().Length == 2 && s.G.CardMovements.Count(m => second.Contains(m.CardId) && m.To == CardLocation.Hand(0) && m.From.Zone == CardZoneKind.PublicPersistentPile) == 3,
            "The exact permanent selected skill is actually usable after cold return, without repeating either maximum-HP payment or any Chu gain.");
        s.Replay();
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ol-shen-cao-pi", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero, "boundary:liu-bei", "boundary:sun-quan", "boundary:yuan-shao");
            string Embedded(string suffix)
            {
                using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-cao-ying." + suffix + ".json")!;
                using var reader = new System.IO.StreamReader(stream); return reader.ReadToEnd();
            }
            var jianxiong = SkillProgramCatalog.Load(Embedded("rules"), Embedded("presentation"));
            b.AddSkill(new("ol:jianxiong", jianxiong.Presentations["ol:jianxiong"].Name, jianxiong.Presentations["ol:jianxiong"].Description)
                { Program = jianxiong.Programs["ol:jianxiong"], ProgramPresentation = jianxiong.Presentations["ol:jianxiong"] });
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":20,"priority":0}],"activations":[
                {"id":"one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Quiet }.ToDictionary(id => id, id => new { name = id, description = "OL神曹丕正式活动本体真实规则小局" }) }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "安静同桌", "", Quiet, "wei", 20, Gender: GeneralGender.Male));
            b.AddDeck(new(Deck, "固定真实闪实体", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 72).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "OL神曹丕真实小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(v => G.CreateSnapshot(v).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int[] Pile() => (V(0).PublicPersistentPileCards ?? []).Select(c => c.Id).ToArray();
        public int Limit() => (int)typeof(GameEngine).GetMethod("GetHandLimit", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(G,
            [((System.Collections.IList)typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(G)!)[0]])!;
        public void Use(string activation) => Accept(new UseProgramSkillCommand(0, Driver, activation, [], [2], G.Revision, P!.PromptId));
        public void BeginDeposit(string amount)
        {
            Use(amount); Reach(() => P?.SkillPrompt?.SkillId == "ol:chuyuan");
            Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(() => P is { PlayerSeat: 2 } && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "ol-shen-cao-pi"));
        }
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Reach(Func<bool> done)
        {
            for (var i = 0; i < 180; i++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "The fixed native fixture must reach its real boundary before match end.");
                if (P is not { PlayerSeat: 0 }) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (P.Kind == DecisionKind.PlayCard) End();
                else if (P.SkillPrompt?.SkillId != "ol:chuyuan" && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else throw new InvalidOperationException("Unexpected OL Shen Cao Pi boundary " + JsonSerializer.Serialize(P));
            }
            throw new InvalidOperationException("OL Shen Cao Pi did not reach a bounded occurrence " + JsonSerializer.Serialize(P));
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))).ToArray(), Frames = G.ResolutionStack,
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Moves = G.CardMovements });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold restoration preserves the original recipient-only draw/deposit or paid awakening skill choice."); }
        public void Replay() { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(before == State(), "Accepted-command replay preserves original native draws, public Chu entities and both permanent awakenings."); }
        public void Accept(GameCommand c) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected command."); }
        public void Need(bool ok, string text) { if (!ok) throw new InvalidOperationException(text + JsonSerializer.Serialize(new { G.State.CurrentSeat, G.State.TurnNumber, P, Facts = G.Events.TakeLast(6).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(37, 1, "ol-shen-cp-event", Hero, 4, 0, 0, 0, 0, [Driver], [], [],
                [new(Peer) { StartingHand = [] }, new(Peer) { StartingHand = [] }], [new(Peer) { StartingHand = [] }], new string('a', 64)) { DefaultGodFactionId = "wei" };
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
}
