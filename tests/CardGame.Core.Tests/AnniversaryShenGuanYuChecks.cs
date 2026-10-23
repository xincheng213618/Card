using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class AnniversaryShenGuanYuChecks
{
    private const string Hero = "anniversary:shen-guan-yu", Wushen = "anniversary-god:wushen", Wuhun = "anniversary-god:wuhun",
        Peer = "fixture:anniversary-guanyu-peer", NoDraw = "fixture:anniversary-guanyu-no-draw",
        Quiet = "fixture:anniversary-guanyu-quiet", Hold = "fixture:anniversary-guanyu-draw-hold",
        Driver = "fixture:anniversary-guanyu-driver", Deck = "fixture:anniversary-guanyu-deck";

    public static void RespondableHeartSlashesDrawOnceBidirectionalNightmareAndDeathCold()
    {
        var s = Scene.Start(); s.Play();
        var club = s.V(0).Hand.Single(c => c.Suit == Suit.Club).Id;
        var heartPeach = s.V(0).Hand.Single(c => c.Suit == Suit.Heart && c.Rank == 3).Id;
        var heartDodge = s.V(0).Hand.Single(c => c.Suit == Suit.Heart && c.Rank == 2).Id;
        var heartSlash = s.V(0).Hand.Single(c => c.Suit == Suit.Heart && c.Rank == 6).Id;
        var alcohol = s.V(0).Hand.Single(c => c.Suit == Suit.Spade).Id;
        var dodge = s.V(2).Hand.Single().Id;
        s.Card(club, LegalActionKind.Slash, 1); s.Play();
        s.Need(s.Nightmare(1) == 1 && s.E<NativeDrawAttemptStartedEvent>().All(e => e.Context.Reason.Value.IndexOf(Wushen, StringComparison.Ordinal) < 0),
            "An ordinary non-heart Slash uses the normal quota and grants outgoing Nightmare without a Wushen draw.");
        s.Need(s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.CardId == heartPeach && a.TargetSeats.SequenceEqual([2])),
            "After spending the ordinary Slash quota, the actual heart Peach identity still offers a Slash against opposite seat two at distance two.");
        s.Card(heartPeach, LegalActionKind.Slash, 2); s.Play();
        s.Need(s.E<CardRespondedEvent>().Any(e => e.CardId == dodge && e.ResponderSeat == 2 && e.EffectiveCardKind == CardKind.Dodge) &&
            s.Nightmare(2) == 0 && !s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 2),
            "The anniversary heart Slash is genuinely answered by the NPC's physical Dodge; it inherits no OL response prohibition and causes no false draw or Nightmare.");
        s.Card(alcohol, LegalActionKind.Alcohol); s.Play();
        s.Card(heartDodge, LegalActionKind.Slash, 2);
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Hold && s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == Wushen));
        var producer = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Wushen);
        var draw = s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(f => f.Context.ParentFrameId == producer.Id);
        var handBeforeDraw = s.V(0).HandCount;
        s.Need(s.E<DamageAppliedEvent>().Single(e => e.TargetSeat == 2).Amount == 2 && s.Nightmare(2) == 2 &&
            draw.Context.RequestedCount == 1 && draw.Return.Kind == NativeDrawAttemptReturnKind.ProgramInstruction &&
            s.E<PlayerMarkerChangedEvent>().Count(e => e.PlayerSeat == 2 && e.Marker == PlayerMarkerKind.Nightmare && e.SkillOwnerSeat == 0 && e.Delta == 1) == 2,
            "One real two-point heart Slash grants two separate source-owned Nightmare points but opens only one native one-card Wushen draw.");
        s.Cold(); s.Skip(); s.Play();
        s.Need(s.V(0).HandCount == handBeforeDraw + 1 && s.Nightmare(2) == 2 &&
            s.E<NativeDrawAttemptDecidedEvent>().Count(e => e.AttemptFrameId == draw.Id && e.RequestedCount == 1 && !e.Replaced) == 1 &&
            !s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.CardId != heartSlash),
            "The original paid draw child cold-restores once, keeps both real Nightmare points and leaves ordinary Slashes exhausted.");
        s.Card(heartSlash, LegalActionKind.Slash, 2); s.Play();
        s.Need(s.Nightmare(2) == 3 && s.V(0).HandCount == handBeforeDraw + 1 &&
            s.E<ProgramBindingResolvedEvent>().Count(e => e.SkillId == Wushen && e.Completed) == 2,
            "A later heart Slash still ignores quota and gives its own one-card damage draw.");

        s.Use("incoming", [3]); s.Play();
        s.Need(s.V(0).Hp == 3 && s.Nightmare(1) == 1 && s.Nightmare(2) == 3 && s.Nightmare(3) == 2 &&
            s.E<PlayerMarkerChangedEvent>().Count(e => e.PlayerSeat == 3 && e.Marker == PlayerMarkerKind.Nightmare && e.SkillOwnerSeat == 0 && e.Delta == 1) == 2,
            "Receiving two actual damage points gives the real source two Nightmare, sharing the same Guan Yu-owned comparison with outgoing damage marks.");
        s.Use("die", []);
        s.Reach(() => !s.V(0).IsAlive && !s.V(2).IsAlive && s.E<JudgmentResolvedEvent>().Any(e => e.Reason == "skill.wuhun.death"));
        s.Need(s.V(1).IsAlive && s.V(3).IsAlive &&
            s.E<JudgmentResolvedEvent>().Single(e => e.Reason == "skill.wuhun.death") is { TargetSeat: 2, CardKind: CardKind.Slash } &&
            s.E<ProgramSkillCauseDeathDeclaredEvent>().Single(e => e.SkillId == Wuhun) is { SourceSeat: 0, TargetSeat: 2 },
            "The actual owner death chooses the outgoing-damage target with the largest combined Nightmare count, runs the native non-Peach judgment, and kills that target through the original Wuhun death chain.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-shen-guanyu", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Hold}}","revision":1,"triggers":[{"id":"draw","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]},
                {"id":"die","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":20}]}]}
            ]}
            """;
            var labels = new[] { NoDraw, Quiet, Hold, Driver }.ToDictionary(id => id, id => new { name = id, description = "周年神关羽实际伤害和原生死亡链" });
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "保留真实回合的角色", "", Quiet, "wei", 12));
            b.AddDeck(new(Deck, "固定实体牌", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "周年神关羽实际小局", 4, 4, new Dictionary<string, int>(), Deck,
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
        public int Nightmare(int seat) => V(seat).Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.Nightmare)?.Count ?? 0;
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Use(string binding, IReadOnlyList<int> targets) => Accept(new UseProgramSkillCommand(0, Driver, binding, [], targets, G.Revision, P!.PromptId));
        public void Card(int id, LegalActionKind kind, int? target = null)
        {
            var action = G.GetHumanLegalActions().Single(a => a.CardId == id && a.Kind == kind && a.TargetSeats.SequenceEqual(target is { } seat ? [seat] : Array.Empty<int>()));
            Accept(new PlayCardCommand(0, id, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId)
            { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
        }
        public void Skip() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Reach(Func<bool> done)
        {
            for (var i = 0; i < 160; i++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == Hold) Skip();
                else if (P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(c => c.Parameters.GetValueOrDefault("response") == "let-die");
                else if (P is { PlayerSeat: 0 } target && target.SkillPrompt?.SkillId == Wuhun && target.Choices.Any(c => c.Targets.SequenceEqual([2]))) Answer(c => c.Targets.SequenceEqual([2]));
                else if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected anniversary Guan Yu boundary " + JsonSerializer.Serialize(P));
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded anniversary Guan Yu flow was not reached " + JsonSerializer.Serialize(P));
        }
        public void Cold()
        {
            var before = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before.SequenceEqual(Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v)))), "Cold retains the exact original heart-Slash damage draw child.");
        }
        public void Accept(GameCommand command) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected anniversary Guan Yu command."); }
        public void Need(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, Nightmares = Enumerable.Range(0, 4).Select(Nightmare), P, Facts = G.Events.TakeLast(6).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(37, 1, "anniversary-guanyu", Hero, 5, 0, 0, 0, 0, [NoDraw, Hold, Driver],
                [new(CardKind.Slash, Suit.Club, 7), new(CardKind.Peach, Suit.Heart, 3), new(CardKind.Dodge, Suit.Heart, 2),
                 new(CardKind.Slash, Suit.Heart, 6), new(CardKind.Alcohol, Suit.Spade, 9)], [],
                [new(Peer, ExtraSkillIds: [NoDraw]) { StartingHand = [new(CardKind.Dodge, Suit.Diamond, 2)] },
                 new(Peer, ExtraSkillIds: [NoDraw]) { StartingHand = [] }],
                [new(Peer, ExtraSkillIds: [NoDraw]) { StartingHand = [] }], new string('f', 64)) { DefaultGodFactionId = "shu" };
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
}
