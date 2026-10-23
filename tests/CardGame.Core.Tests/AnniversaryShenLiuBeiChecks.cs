using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class AnniversaryShenLiuBeiChecks
{
    private const string Hero = "anniversary:shen-liu-bei", Longnu = "anniversary:longnu",
        Jieying = "anniversary:shen-liu-bei-jieying", Watch = "fixture:anniversary-liubei-watch",
        Quiet = "fixture:anniversary-liubei-quiet", Peer = "fixture:anniversary-liubei-peer",
        Deck = "fixture:anniversary-liubei-deck";

    public static void LiveHpDrawAndOptionalChainedRecipientsCold()
    {
        var s = Scene.Start();
        s.DrawOffer(Longnu, "yang");
        var yang = s.Producer(Longnu);
        s.Need(s.V(0).Hp == 2 && s.V(0).MaxHp == 6 && s.V(0).HandCount == 0 &&
            yang.WindowContext?.Facts is { CurrentHp: 3, CurrentMaxHp: 6 } &&
            yang.NativeDraw is { RequestedCount: 4, Recipients: [0], RecipientApplied: false },
            "Yang first loses actual HP, then requests four cards from live lost HP instead of the three-HP opening snapshot or the old fixed draw one.");
        var yangId = yang.Id;
        s.Cold(); s.Continue(); s.Play();
        s.Need(s.V(0).IsChained && s.V(0).HandCount == 4 && s.V(0).HandLimit == 4,
            "At two actual HP, Jieying keeps the four-card hand at its real four-card limit after cold recovery.");
        s.Need(s.V(0).HandCount == 4 && s.E<NativeDrawAttemptDecidedEvent>().Count(e =>
                e.ParentFrameId == yangId && e.RecipientSeat == 0 && e.RequestedCount == 4 && !e.Replaced) == 1,
            "The original live-HP request returns once after cold recovery and obtains exactly four real deck cards.");

        s.EndPlay(); s.Chain(1); s.Recipients();
        s.Need(s.V(0).IsChained && s.V(1).IsChained && !s.V(2).IsChained && !s.V(3).IsChained &&
            s.P!.Choices.Select(c => string.Join(",", c.Targets)).Order(StringComparer.Ordinal)
                .SequenceEqual(new[] { "", "0", "0,1", "1" }.Order(StringComparer.Ordinal)),
            "The same ending first chains the chosen other role, then offers every subset of the actual chained roles, including self and nobody.");
        var beforeGroup = s.G.CardMovements.Select(m => m.Sequence).DefaultIfEmpty().Max();
        s.Answer(c => c.Targets.SequenceEqual([0, 1])); s.DrawOffer(Jieying, "chain-other");
        var endingId = s.Producer(Jieying).Id;
        s.Need(s.Producer(Jieying).NativeDraw is { RequestedCount: 1, Recipients: [0, 1] },
            "One original ending frame owns both chosen recipients and each native one-card draw.");
        s.Continue(); s.Ended(1);
        s.Need(s.V(0).HandCount == 5 && s.V(1).HandCount == 1 &&
            s.E<ProgramBindingResolvedEvent>().Any(e => e.FrameId == endingId && e.Completed) &&
            s.G.CardMovements.Where(m => m.Sequence > beforeGroup && m.From == CardLocation.DrawPile &&
                m.To.Zone == CardZoneKind.Hand && m.Reason.Value == $"skill-program.{Jieying}.Draw")
                .Select(m => m.To.OwnerSeat).SequenceEqual(new int?[] { 0, 1 }),
            "Self and the chosen newly chained peer each draw once, in the original recipient cursor order.");

        s.DrawOffer(Longnu, "yin");
        var yin = s.Producer(Longnu);
        s.Need(s.V(0).Hp == 2 && s.V(0).MaxHp == 5 &&
            yin.NativeDraw is { RequestedCount: 2, Recipients: [0] },
            "Yin first reduces the real maximum, then requests the current two HP rather than one card or the lost-HP count.");
        s.Continue(); s.Play();
        s.Need(s.V(0).HandCount == 7, "The second actual play phase receives the real two-card Yin draw.");
        var turn = s.G.State.TurnNumber;
        s.EndPlay(); s.Chain(1); s.Recipients();
        var emptyId = s.Producer(Jieying).Id;
        var hands = Enumerable.Range(0, 4).Select(i => s.V(i).HandCount).ToArray();
        s.Answer(c => c.Targets.Count == 0); s.Ended(turn);
        s.Need(hands.SequenceEqual(Enumerable.Range(0, 4).Select(i => s.V(i).HandCount)) &&
            !s.E<NativeDrawAttemptStartedEvent>().Any(e => e.Context.ParentFrameId == emptyId) &&
            s.E<ProgramBindingResolvedEvent>().Any(e => e.FrameId == emptyId && e.Completed) &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().All(f => f.Id != emptyId),
            "The genuine zero-recipient answer finishes the same binding without an empty draw cursor, extra payment, or a phantom draw request.");
        s.Replay();
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-shen-liu-bei", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"pre-draw","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var labels = new Dictionary<string, object>
            {
                [Quiet] = new { name = "保留真实回合", description = "跳过其他角色出牌阶段。" },
                [Watch] = new { name = "真实摸牌请求", description = "暂停原帧的真实摸牌请求。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
            };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
                { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs)
                b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                    { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "真实安静角色", "", Quiet, "wei", 6));
            b.AddDeck(new(Deck, "固定实体牌", 0, 0, [])
                { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "周年龙怒与结营实结算", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public ProgramSkillFrame Producer(string skill) => G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == skill);
        public void DrawOffer(string skill, string binding) => Reach(() => P?.SkillPrompt?.SkillId == Watch &&
            G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == skill && f.TriggerId == binding && f.NativeDraw is not null));
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void EndPlay() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Ended(int turn) => Reach(() => E<TurnEndedEvent>().Any(e => e.TurnNumber == turn));
        public void Chain(int seat)
        {
            Reach(() => P?.SkillPrompt?.SkillId == Jieying && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
            Answer(c => c.Targets.SequenceEqual([seat]));
        }
        public void Recipients() => Reach(() => P?.SkillPrompt?.SkillId == Jieying &&
            P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-targets"));
        public void Answer(Func<PromptChoice, bool> matches)
        {
            var p = P!;
            Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(matches).Id, G.Revision));
        }
        private void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 200; i++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.SkillPrompt?.SkillId == Watch) { Continue(); continue; }
                    throw new InvalidOperationException("Unexpected anniversary Liu Bei boundary " + JsonSerializer.Serialize(p));
                }
                Need(G.State.Winner == Winner.None, "The small real-turn fixture must retain its next rule boundary.");
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Anniversary Liu Bei missed a bounded native boundary " + JsonSerializer.Serialize(P));
        }
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(),
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), G.CardMovements
        });
        public void Cold()
        {
            var state = State();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(state == State(), "Cold restoration preserves the original paid HP, native draw request, cursor and private player views.");
        }
        public void Replay()
        {
            var state = State();
            G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry);
            Need(state == State(), "Command replay reproduces both live formulas, chosen recipient entities and the zero-recipient ending.");
        }
        private void Accept(GameCommand command)
        {
            var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
            Need(result.Accepted, result.Error?.Message ?? "Rejected anniversary Liu Bei command.");
        }
        public void Need(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message + JsonSerializer.Serialize(new
                { G.State.TurnNumber, G.State.CurrentSeat, P, LastFacts = G.Events.TakeLast(5).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) }));
        }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(37, 1, "anniversary-liu-bei", Hero, 3, 0, 0, 0, 0, [Watch], [], [],
                [new(Peer) { StartingHand = [] }, new(Peer) { StartingHand = [] }],
                [new(Peer) { StartingHand = [] }], new string('a', 64)) { DefaultGodFactionId = "shu" };
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8
            }, registry);
            var scene = new Scene(game, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }
}
