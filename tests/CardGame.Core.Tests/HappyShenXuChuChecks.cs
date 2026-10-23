using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class HappyShenXuChuChecks
{
    private const string Hero = "happy:shen-xu-chu", Zhengqing = "happy:zhengqing", Zhuangpo = "happy:zhuangpo",
        Peer = "fixture:happy-xuchu-peer", NoDraw = "fixture:happy-xuchu-no-draw", Quiet = "fixture:happy-xuchu-quiet",
        Driver = "fixture:happy-xuchu-driver", Hold = "fixture:happy-xuchu-draw-hold", Deck = "fixture:happy-xuchu-deck";

    public static void RecordDrawsMarkedDuelAndEqualRecordResumeCold()
    {
        var s = Scene.Start(); s.Play();
        s.Need(s.V(0).Hp == 5 && s.V(0).MaxHp == 5, "Happy Xu Chu keeps the actual 5/5 printed body.");
        s.Use("other-two", [1]); s.Play(); s.End(); s.Play();
        var first = s.Settled(1);
        s.Need(first is { WinnerSeat: 1, MaximumDamage: 2 } && s.Marks(1) == 2 && s.Marks(0) == 0 &&
            s.Draws(first.FrameId).Select(e => (e.Seat, e.Requested)).SequenceEqual([(1, 1), (0, 1)]),
            "Actual out-of-turn source damage wins round one; the other recipient and Xu Chu each receive one native draw.");

        var hand = s.V(0).HandCount; var hp = s.V(1).Hp; var drawn = s.E<ShenXuChuDrawReturnedEvent>().Length;
        s.Duel(1); s.Play();
        var marked = s.E<ZhuangpoTargetIssuedEvent>().Last();
        s.Need(marked.Source.SkillId == Zhuangpo && marked.TargetMarked && s.V(1).Hp == hp - 2 && s.V(0).HandCount == hand - 1 &&
            s.E<ShenXuChuDrawReturnedEvent>().Length == drawn,
            "The real printed-Sha conversion against a marked target deals two Duel damage and gives no anniversary-edition extra draw.");
        s.Use("four", [2]); s.Play(); s.End(); s.Play();
        var record = s.Settled(2);
        s.Need(record is { WinnerSeat: 0, MaximumDamage: 6 } && s.Marks(0) == 6 && s.Marks(1) == 0 &&
            s.Draws(record.FrameId) is [{ Seat: 0, Requested: 5, Actual: 5 }],
            "Round two clears the old winner's Qing, awards all six real marks, and caps the first personal record at five native cards.");

        var targetCards = s.V(2).HandCount; hp = s.V(2).Hp;
        s.Duel(2); s.Reach(() => s.Root?.ShenXuChu is { Stage: ShenXuChuStage.MarkChoice });
        s.Answer(c => c.Parameters.GetValueOrDefault("option") == "2"); s.Play();
        s.Need(s.Marks(0) == 4 && s.V(2).HandCount == targetCards - 2 && s.V(2).Hp == hp - 1 &&
            s.E<ZhuangpoMarksPaidEvent>() is [{ OwnerSeat: 0, TargetSeat: 2, Removed: 2 }] &&
            s.G.CardMovements.Count(m => m.From == CardLocation.Hand(2) && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.happy:zhuangpo.discard") == 2,
            "The accepted conversion pays exactly two actual Qing before the target's own two real hand discards and an ordinary one-damage Duel.");
        s.Use("two", [2]); s.Play(); s.End();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Hold && s.Root?.ShenXuChu is { Operation: SkillProgramEffectOp.SettleZhengqingRound, DrawIndex: 0 });
        var lower = s.Settled(3); var producer = s.Root!;
        var native = s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(f => f.Context.ParentFrameId == producer.Id);
        s.Need(lower is { WinnerSeat: 0, MaximumDamage: 3 } && producer.ShenXuChu!.Draws.SequenceEqual([new ShenXuChuDraw(0, 1), new ShenXuChuDraw(0, 1)]) &&
            s.Marks(0) == 3 && native.Context.RequestedCount == 1,
            "A personal award below the true prior six-Qing record keeps both original one-card entitlements instead of replacing them with three cards.");
        s.Cold(); s.Skip();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Hold && s.Root?.ShenXuChu is { DrawIndex: 1 });
        s.Need(s.Draws(lower.FrameId) is [{ Index: 0, Requested: 1, Actual: 1 }], "The exact first native producer returned once before the second same-seat entitlement begins.");
        s.Skip(); s.Play();
        s.Need(s.Draws(lower.FrameId).Select(e => (e.Index, e.Seat, e.Requested, e.Actual)).SequenceEqual([(0, 0, 1, 1), (1, 0, 1, 1)]) &&
            s.E<NativeDrawAttemptDecidedEvent>().Count(e => e.AttemptFrameId == native.Id && e.RequestedCount == 1 && !e.Replaced) == 1 && s.Marks(0) == 3,
            "Cold preserves the lower-record draw plan, original round and mark award without replaying the first draw or payment.");
        s.Use("six", [2]); s.Play(); s.End(); s.Play();
        var equal = s.Settled(4);
        s.Need(equal is { WinnerSeat: 0, MaximumDamage: 6 } && s.Marks(0) == 6 &&
            s.Draws(equal.FrameId) is [{ Seat: 0, Requested: 5, Actual: 5 }] &&
            s.E<ZhengqingSettledEvent>().Count(e => e.Source.SkillId == Zhengqing) == 4,
            "Matching the historical maximum also satisfies 'a greatest award': it replaces both base draws with one capped five-card native producer.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-happy-xuchu", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var presentation = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { NoDraw, Quiet, Driver, Hold }.ToDictionary(id => id, id => new { name = id, description = "真实轮次争擎纪录和壮魄实体支付" }) });
            var catalog = SkillProgramCatalog.Load(Rules, presentation);
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "安静同桌", "", Quiet, "wei", 20));
            b.AddDeck(new(Deck, "固定真实闪牌", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 96).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "欢乐许褚真实轮末", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer],
                ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
        public static string Rules => $$"""
        {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
          {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
          {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
          {"id":"{{Hold}}","revision":1,"triggers":[{"id":"draw","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
          {"id":"{{Driver}}","revision":1,"activations":[
            {"id":"other-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]},
            {"id":"two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
            {"id":"four","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":4}]},
            {"id":"six","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":6}]}]}
        ]}
        """;
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.ShenXuChu is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int Marks(int seat) => V(seat).Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.QingBanner)?.Count ?? 0;
        public ZhengqingSettledEvent Settled(int round) => E<ZhengqingSettledEvent>().Single(e => e.Round == round && e.Source.SkillId == Zhengqing);
        public ShenXuChuDrawReturnedEvent[] Draws(long frame) => E<ShenXuChuDrawReturnedEvent>().Where(e => e.FrameId == frame).OrderBy(e => e.Index).ToArray();
        public void Use(string binding, IReadOnlyList<int> targets) => Accept(new UseProgramSkillCommand(0, Driver, binding, [], targets, G.Revision, P!.PromptId));
        public void Duel(int target)
        {
            var action = G.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == Zhuangpo && a.PlayedCardKind == CardKind.Duel && a.TargetSeats.SequenceEqual([target]));
            Accept(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
        }
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Skip() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; var choice = p.Choices.FirstOrDefault(predicate); Need(choice is not null, "Missing exact Happy Xu Chu choice."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, G.Revision)); }
        public void Reach(Func<bool> done)
        {
            for (var n = 0; n < 180; n++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == Hold) Skip();
                else if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected Happy Xu Chu boundary " + JsonSerializer.Serialize(P));
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Missed the bounded Happy Xu Chu real-round boundary.");
        }
        public void Cold()
        {
            var views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray();
            var frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(views.SequenceEqual(Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i)))) &&
                frames.SequenceEqual(G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))), "Cold preserves the actual Qing award, historical record cutoff, duplicate-seat entitlements and exact native child.");
        }
        public void Accept(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Happy Xu Chu command."); }
        public void Need(bool ok, string why)
        { if (!ok) throw new InvalidOperationException(why + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Root = Root?.ShenXuChu, Awards = E<ZhengqingSettledEvent>(), Draws = E<ShenXuChuDrawReturnedEvent>(), Damage = E<DamageAppliedEvent>() })); }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var cards = Enumerable.Range(0, 3).Select(_ => new ShanheStartingCard(CardKind.Dodge, Suit.Club, 7)).ToArray();
            var peer = new ShanheCombatantDefinition(Peer, ExtraSkillIds: [NoDraw]) { StartingHand = cards };
            var setup = new ShanheBattleSetup(37, 1, "happy-xuchu", Hero, 5, 0, 0, 0, 0, [NoDraw, Driver, Hold], cards, [], [peer, peer], [peer], new string('f', 64)) { DefaultGodFactionId = "wei" };
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
}
