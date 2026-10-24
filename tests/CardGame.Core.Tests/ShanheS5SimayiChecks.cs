using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class ShanheS5SimayiChecks
{
    private const string Hero = StandardShanheS5SimayiPackage.SimayiId,
        Renjie = StandardShanheS5SimayiPackage.RenjieId, Baiyin = StandardShanheS5SimayiPackage.BaiyinId,
        Jilue = StandardShanheS5SimayiPackage.JilueId, Lianpo = StandardShanheS5SimayiPackage.LianpoId,
        Peer = "fixture:s5-sima-peer", NoDraw = "fixture:s5-sima-no-draw", Quiet = "fixture:s5-sima-quiet",
        Driver = "fixture:s5-sima-driver", Discard = "fixture:s5-sima-discard", Hold = "fixture:s5-sima-hold",
        Deck = "fixture:s5-sima-deck";

    public static void FourRenPerActualPointAndPhaseDiscardPayOriginalJilueColdWithoutAwakeningCost()
    {
        var s = Scene.Start(); s.Play();
        s.Need(new[] { Renjie, Baiyin, Jilue, Lianpo }.All(s.Has) && s.Ren == 0 && s.V(0).Hp == 4 &&
            s.V(0).MaxHp == 4 && !s.G.GetHumanLegalActions().Any(a => a.SkillId == Jilue),
            "The actual source body starts with all four skills at 4/4; original Jilue is already owned and honestly cannot pay zero Ren.");
        s.Use(Driver, "hurt", [], []); s.Play();
        s.Need(s.V(0).Hp == 2 && s.Ren == 8 &&
            s.E<DamageAppliedEvent>().Count(e => e.TargetSeat == 0 && e.Amount == 2) == 1 &&
            s.E<PlayerMarkerChangedEvent>().Count(e => e.Marker == PlayerMarkerKind.Ren && e.Delta == 4 &&
                e.Reason == $"skill-program.{Renjie}.damage-ren.marker") == 2,
            "One native two-point damage grants four Ren separately for each actual point, without waiting for Baiyin.");

        var hand = s.V(0).HandCount;
        var material = s.V(0).Hand[0].Id;
        s.Use(Jilue, "exchange-owned-cards", [material], []);
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Hold);
        var producer = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Jilue);
        var draw = s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(f => f.Context.ParentFrameId == producer.Id);
        s.Need(producer.ActivationId == "exchange-owned-cards" && s.Ren == 7 && s.V(0).HandCount == hand - 1 &&
            draw.Context.RequestedCount == 1 && draw.Return.Kind == NativeDrawAttemptReturnKind.ProgramInstruction &&
            s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) == 1 &&
            s.E<PlayerMarkerChangedEvent>().Count(e => e.Delta == -1 && e.Reason == $"skill-program.{Jilue}.exchange-owned-cards.cost") == 1 &&
            s.E<ProgramBindingStartedEvent>().All(e => e.SkillId != Renjie || e.BindingId != "discard-ren"),
            "The new actual Ren pays original Zhiheng once before its native draw child; its Play-phase discard earns no Ren.");
        s.Cold(); s.Skip(); s.Play();
        s.Need(s.Ren == 7 && s.V(0).HandCount == hand &&
            s.E<NativeDrawAttemptDecidedEvent>().Count(e => e.AttemptFrameId == draw.Id && e.RequestedCount == 1 && !e.Replaced) == 1 &&
            s.E<PlayerMarkerChangedEvent>().Count(e => e.Delta == -1 && e.Reason == $"skill-program.{Jilue}.exchange-owned-cards.cost") == 1,
            "Cold returns to the original once-paid five-ability frame and applies its one-card native draw exactly once.");

        var beforeRen = s.Ren; var beforeMoves = s.G.CardMovements.Count; var turn = s.G.State.TurnNumber;
        s.End(); s.Reach(() => s.P?.SkillPrompt?.SkillId == Discard && s.P.Choices.Any(c => c.Cards.Count == 1));
        var phaseCard = s.P!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
        s.Answer(c => c.Cards.SequenceEqual([phaseCard]));
        s.Reach(() => s.G.State.TurnNumber > turn && s.P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        var discarded = s.G.CardMovements.Skip(beforeMoves).Where(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile).ToArray();
        s.Need(discarded.Length == hand - 2 && discarded.Any(m => m.CardId == phaseCard && m.Reason != CardMoveReasons.HandLimitDiscard) &&
            s.Ren == beforeRen + 4 * discarded.Length &&
            s.E<ProgramBindingResolvedEvent>().Count(e => e.SkillId == Renjie && e.BindingId == "discard-ren" && e.Completed) == discarded.Length &&
            s.V(0).Hp == 2 && s.V(0).MaxHp == 4 && s.V(0).Skills!.Count(k => k.Id == Jilue) == 1 &&
            s.V(0).SkillRuntimeStates!.Single(state => state.SkillId == Jilue).PublicRuleStates!
                .Count(state => state.StateId == "exchange-owned-cards" && state.Kind == ProgramPublicRuleStateKind.ActivationLimit) == 1 &&
            s.E<SkillAwakenedEvent>().Count(e => e.SkillId == Baiyin && e.MaximumHp == 4 && e.AcquiredSkillIds.Count == 0) == 1 &&
            s.E<SkillUsageConsumedEvent>().Count(e => e.SkillId == Baiyin && e.Scope == SkillUsageScope.Game && e.Count == 1) == 1,
            "A real Discard-phase skill discard plus normal hand-limit discards each grant four Ren; next preparation awakens once without reducing HP, maximum HP, or Ren and retains the initially owned Jilue.");
        s.Cold();
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-s5-simayi", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{NoDraw}}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{{Discard}}}","revision":1,"triggers":[{"id":"discard","window":"discardPhaseStarting","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectOwnedCards","target":"owner","minimumCards":1,"maximumCards":1,"zones":["hand"],"resultBind":"phase-card"},{"op":"moveBoundCards","target":"owner","sourceBind":"phase-card","destination":"discardPile","awaitMovementTriggers":true}]}]},
              {"id":"{{{Hold}}}","revision":1,"triggers":[{"id":"draw","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
              {"id":"{{{Driver}}}","revision":1,"activations":[{"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":2}]}]}
            ]}
            """;
            var c = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { NoDraw, Quiet, Discard, Hold, Driver }.ToDictionary(id => id,
                    id => new { name = id, description = "原生伤害、实际弃牌阶段与付忍摸牌暂停" })
            }));
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description)
            { Program = p, ProgramPresentation = c.Presentations[id] });
            b.AddGeneral(new(Peer, "保留真实阶段的角色", "supporter", Quiet, "wei", 8));
            b.AddDeck(new(Deck, "固定实体牌", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 32).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "S5司马懿原生忍与极略", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(v => G.CreateSnapshot(v).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int Ren => V(0).Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.Ren)?.Count ?? 0;
        public bool Has(string id) => V(0).Skills?.Any(k => k.Id == id) == true;
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Use(string skill, string binding, IReadOnlyList<int> cards, IReadOnlyList<int> targets) =>
            Accept(new UseProgramSkillCommand(0, skill, binding, cards, targets, G.Revision, P!.PromptId));
        public void Skip() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Reach(Func<bool> done)
        {
            for (var i = 0; i < 180; i++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId is Hold or Jilue) Skip();
                else if (P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End();
                else if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected S5 Sima boundary " + JsonSerializer.Serialize(P));
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "The bounded real S5 Sima flow was not reached.");
        }
        public void Cold()
        {
            var views = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))).ToArray();
            var frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(views.SequenceEqual(Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v)))) &&
                frames.SequenceEqual(G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))),
                "Cold retains native once-paid Jilue children and the exact four-Ren/awakening state.");
        }
        public void Accept(GameCommand command)
        { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected S5 Sima command."); }
        public void Need(bool ok, string why)
        { if (!ok) throw new InvalidOperationException(why + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, Ren, P,
            Owner = V(0), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Facts = G.Events.TakeLast(10).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
        public static Scene Start()
        {
            var registry = ComposedSkillContentRegistry.CreateShowcase(additionalPackages: [new StandardShanheS5SimayiPackage(), new Fixture()]);
            var setup = new ShanheBattleSetup(53, 1, "fixture-s5-simayi", Hero, 4, 0, 0, 0, 0,
                [NoDraw, Discard, Hold, Driver], [new(CardKind.Dodge, Suit.Club, 7), new(CardKind.Dodge, Suit.Club, 8)], [],
                [new(Peer, ExtraSkillIds: [NoDraw]) { StartingHand = [] }, new(Peer, ExtraSkillIds: [NoDraw]) { StartingHand = [] },
                 new(Peer, ExtraSkillIds: [NoDraw]) { StartingHand = [] }], [], new string('f', 64));
            var g = GameEngine.CreateStandard(new GameOptions
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 5
            }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
}
