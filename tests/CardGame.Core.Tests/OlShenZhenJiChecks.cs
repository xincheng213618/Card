using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class OlShenZhenJiChecks
{
    private const string Hero = "ol:shen-zhen-ji", Shenfu = "ol:shenfu", Peer = "fixture:ol-shen-zhen-ji-peer",
        NoDraw = "fixture:ol-shen-zhen-ji-no-draw", Quiet = "fixture:ol-shen-zhen-ji-quiet",
        Hold = "fixture:ol-shen-zhen-ji-hold", Deck = "fixture:ol-shen-zhen-ji-deck";
    public static void EvenDrawAndOpaqueDiscardRepeatFromOriginalFrameCold()
    {
        var s = Scene.Begin(odd: false);
        s.Need(s.V(0).HandLimit == 7 && s.V(0).HandCount == 0 && !s.Frame.OlShenfu!.Odd,
            "The official four-HP event general really starts the fixed even Shenfu branch with Qixian's seven-card limit.");
        s.Choose("draw:2");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Hold && s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any());
        var frame = s.Frame;
        s.Need(frame.OlShenfu is { Stage: OlShenfuStage.Drawing, TargetSeat: 2, Ordinal: 1 } && s.V(2).HandCount == 0,
            "The original Shenfu instruction holds its accepted target while the real one-card producer is suspended.");
        s.Cold(); s.Reach(() => s.P is { PlayerSeat: 0 } && s.P.SkillPrompt?.SkillId == Shenfu);
        s.Need(s.V(2).HandCount == 1 && s.V(2).Hp == 1 && !s.P!.Choices.Any(c => c.Targets.Contains(2)) &&
            s.E<OlShenfuDrawReturnedEvent>().Count(e => e.FrameId == frame.Id && e.Ordinal == 1 && e.Actual == 1) == 1,
            "Cold completes one actual draw exactly once; hand equals HP permits another flow, excluding the first target.");
        s.Choose("discard:1");
        s.Need(s.Frame.OlShenfu is { Stage: OlShenfuStage.HandCard, TargetSeat: 1 } &&
            s.P!.Choices.Count == 2 && s.P.Choices.All(c => c.Cards.Count == 0 && c.Description.Contains("手牌", StringComparison.Ordinal)),
            "Shenfu chooses an actual legal foreign Hand slot without revealing its physical identity.");
        s.Answer(c => c.Parameters.GetValueOrDefault("option") == "card");
        s.Reach(() => s.P is { PlayerSeat: 0 } && s.P.SkillPrompt?.SkillId == Shenfu);
        s.Need(s.V(1).HandCount == 1 && s.V(1).Hp == 1 && !s.P!.Choices.Any(c => c.Targets.Contains(1) || c.Targets.Contains(2)) &&
            s.E<OlShenfuDiscardedEvent>().Count(e => e.FrameId == frame.Id && e.Ordinal == 2 && e.After > e.Before) == 1 &&
            s.G.CardMovements.Count(m => m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill.ol-shen-zhen-ji.shenfu.discard") == 1,
            "One real logical discard leaves equal hand and HP and opens the next flow without reusing either target.");
        s.Choose("draw:0"); s.Reach(() => s.E<TurnEndedEvent>().Any(e => e.TurnNumber == 1));
        var results = s.E<OlShenfuFlowResolvedEvent>().Where(e => e.FrameId == frame.Id).ToArray();
        s.Need(s.V(0).HandCount == 1 && s.V(0).Hp == 4 && results.Select(e => e.MayRepeat).SequenceEqual([true, true, false]) &&
            s.E<OlShenfuStartedEvent>().Count(e => e.FrameId == frame.Id && !e.Odd) == 1 &&
            s.E<OlShenfuTargetSelectedEvent>().Where(e => e.FrameId == frame.Id).Select(e => e.TargetSeat).SequenceEqual([2, 1, 0]) &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().All(f => f.OlShenfu is null),
            "The initial even branch remains fixed when self draw changes hand parity; a nonmatching hand/HP result stops and cleans the original instruction.");
    }
    public static void OddNativeDyingDeathRepeatsAndSurvivorStopsCold()
    {
        var s = Scene.Begin(odd: true); var frameId = s.Frame.Id;
        s.Need(s.V(0).HandCount == 1 && s.Frame.OlShenfu!.Odd && !s.P!.Choices.Any(c => c.Targets.Contains(0)),
            "The actual odd branch allows only other living roles as thunder targets.");
        s.Choose("thunder:2"); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
        var dying = s.G.ResolutionStack.OfType<DyingFrame>().Single();
        s.Need(s.Frame is { Id: var id, OlShenfu: { Stage: OlShenfuStage.Damaging, KilledByDamage: false } } && id == frameId &&
            dying.VictimSeat == 2 && s.G.ResolutionStack.OfType<DamageFrame>().Any(d => d.Id == dying.ParentFrameId && d.ParentFrameId == frameId),
            "A genuine unresolved rescue stays beneath the original thunder flow; entering dying alone does not grant repetition.");
        s.Cold(); s.Answer(c => c.Id.Value == "dying.let-die" && c.Parameters.GetValueOrDefault("response") == "let-die" && c.Cards.Count == 0);
        s.Reach(() => s.P is { PlayerSeat: 0 } && s.P.SkillPrompt?.SkillId == Shenfu);
        s.Need(!s.V(2).IsAlive && !s.P!.Choices.Any(c => c.Targets.Contains(2)) &&
            s.E<OlShenfuDeathConfirmedEvent>().Count(e => e.FrameId == frameId && e.Ordinal == 1 && e.TargetSeat == 2 && e.DyingFrameId == dying.Id) == 1 &&
            s.E<OlShenfuFlowResolvedEvent>().Count(e => e.FrameId == frameId && e.Ordinal == 1 && e.MayRepeat) == 1,
            "Only the exact primary damage's failed native rescue enables repetition, once after cold return.");
        s.Choose("thunder:3"); s.Reach(() => s.E<TurnEndedEvent>().Any(e => e.TurnNumber == 1));
        var results = s.E<OlShenfuFlowResolvedEvent>().Where(e => e.FrameId == frameId).ToArray();
        s.Need(s.V(3).IsAlive && s.V(3).Hp == 3 && results.Select(e => e.MayRepeat).SequenceEqual([true, false]) &&
            s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat is 2 or 3 && e.Amount == 1 && e.Nature == DamageNature.Thunder) == 2 &&
            s.E<OlShenfuTargetSelectedEvent>().Where(e => e.FrameId == frameId).Select(e => e.TargetSeat).SequenceEqual([2, 3]) &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().All(f => f.OlShenfu is null),
            "The next real thunder hit leaves its target alive, so no further flow is offered and the original ending completes.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ol-shen-zhen-ji", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Hold}}","revision":1,"triggers":[{"id":"draw","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]}
            ]}
            """;
            var labels = new[] { NoDraw, Quiet, Hold }.ToDictionary(id => id, id => new { name = id, description = "神赋真实结束与原生子帧" });
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "保留真实回合的角色", "", Quiet, "wei", 4));
            b.AddDeck(new(Deck, "固定实体牌", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "OL神武再世神甄姬本体小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(v => G.CreateSnapshot(v).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public ProgramSkillFrame Frame => G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.OlShenfu is not null);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Choose(string option) => Answer(c => c.Parameters.GetValueOrDefault("option") == option);
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 120; i++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p) throw new InvalidOperationException("Unexpected OL Shenfu human boundary " + JsonSerializer.Serialize(p));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded OL Shenfu occurrence was not reached " + JsonSerializer.Serialize(P));
        }
        public void Cold()
        {
            var before = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before.SequenceEqual(Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v)))), "Cold restore preserves the actual original Shenfu producer or native rescue boundary.");
        }
        public void Accept(GameCommand command)
        { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected OL Shenfu command."); }
        public void Need(bool ok, string reason)
        { if (!ok) throw new InvalidOperationException(reason + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Facts = G.Events.TakeLast(7).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
        public static Scene Begin(bool odd)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(37, 1, "ol-shenfu", Hero, 4, 0, 0, 0, 0, [NoDraw],
                odd ? [new(CardKind.Peach, Suit.Heart, 3)] : [], [],
                [new(Peer, Hp: 1, ExtraSkillIds: odd ? [NoDraw] : [NoDraw, Hold]) { StartingHand = [] },
                 new(Peer, ExtraSkillIds: [NoDraw]) { StartingHand = [] }],
                [new(Peer, Hp: 1, ExtraSkillIds: [NoDraw]) { StartingHand = odd ? [] : [new(CardKind.Slash, Suit.Club, 7), new(CardKind.Slash, Suit.Club, 7)] }],
                new string('b', 64)) { DefaultGodFactionId = "wei" };
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand());
            s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
            s.Accept(new EndPlayPhaseCommand(0, g.Revision, s.P!.PromptId)); s.Reach(() => s.P?.SkillPrompt?.SkillId == Shenfu);
            s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            s.Reach(() => s.P?.Choices.Any(c => c.Parameters.ContainsKey("option")) == true); return s;
        }
    }
}
