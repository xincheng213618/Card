using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class Ol2016ShenSimaYiChecks
{
    private const string Hero = "ol:shen-sima-yi-2016", Renjie = "ol:renjie-2016",
        Jilue = "ol:jilue-2016", Lianpo = "ol:lianpo-2016", Peer = "fixture:2016-sima-peer",
        NoDraw = "fixture:2016-sima-no-draw", Quiet = "fixture:2016-sima-quiet",
        Discard = "fixture:2016-sima-phase-discard", Driver = "fixture:2016-sima-driver",
        Hold = "fixture:2016-sima-draw-hold", Deck = "fixture:2016-sima-deck";

    public static void ActualPhaseDiscardsOriginalJiluePaidDrawAndLianpoCold()
    {
        var s = Scene.Start(); s.Play(); s.End();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Discard && s.P.Choices.Any(c => c.Cards.Count == 1));
        var firstDiscard = s.P!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
        s.Answer(c => c.Cards.SequenceEqual([firstDiscard]));
        s.Reach(() => s.G.State.TurnNumber > 1 && s.P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        s.Need(s.Ren == 4 && s.V(0).MaxHp == 3 && s.V(0).Hp == 3 && s.V(0).HandCount == 4 && s.Has(Jilue) &&
            s.G.CardMovements.Any(m => m.CardId == firstDiscard && m.From == CardLocation.Hand(0) &&
                m.To == CardLocation.DiscardPile && m.Reason != CardMoveReasons.HandLimitDiscard) &&
            s.E<ProgramBindingResolvedEvent>().Count(e => e.SkillId == Renjie && e.BindingId == "discard-ren" && e.Completed) == 4,
            "An actual Discard-phase skill hand discard and three ordinary hand-limit discards grant four Ren; the next real preparation reduces maximum HP and acquires the original Jilue.");

        var weapon = s.V(0).Equipment.Single(c => c.Kind == CardKind.Crossbow).Id;
        var hand = s.V(0).Hand.Select(c => c.Id).ToArray();
        s.Use(Driver, "judge", [], []);
        s.Reach(() => s.P?.Kind == DecisionKind.ProgramJudgmentReplacement);
        s.Need(s.P!.ValidCardIds.ToHashSet().SetEquals(hand) && !s.P.ValidCardIds.Contains(weapon) && s.Ren == 4,
            "Original Guicai offers exactly the current hand entities, excludes equipped Crossbow, and has not charged Ren before accepting a replacement.");
        s.Answer(c => c.Cards.SequenceEqual([hand[0]])); s.Play();
        s.Need(s.Ren == 3 && s.E<ProgramJudgmentReplacementResolvedEvent>().Count(e => e.SkillId == Jilue && e.Activated && e.ReplacementCardId == hand[0]) == 1,
            "The chosen real hand card replaces the native judgment once and spends one Ren.");

        var exchange = s.V(0).Hand.Select(c => c.Id).ToArray();
        s.Use(Jilue, "exchange-owned-cards", exchange, []); s.Play();
        s.Need(s.Ren == 2 && s.V(0).HandCount == exchange.Length && s.V(0).Equipment.Single().Id == weapon &&
            exchange.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) == 1) &&
            !s.G.GetHumanLegalActions().Any(a => a.SkillId == Jilue && a.ProgramActivationId == "exchange-owned-cards"),
            "Discarding every hand card through original Zhiheng spends one Ren and draws only the same count, with no boundary all-hand bonus or second activation this Play phase.");

        var beforeUse = s.V(0).HandCount;
        var trick = s.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.DrawTwo && a.ConversionSource is null);
        s.Accept(new PlayCardCommand(0, trick.CardId!.Value, trick.TargetSeats, s.G.Revision, s.P!.PromptId, trick.PlayedCardKind));
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Jilue); s.Activate();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Hold && s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == Jilue && f.TriggerId == "trick-used"));
        var producer = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Jilue && f.TriggerId == "trick-used");
        var draw = s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(f => f.Context.ParentFrameId == producer.Id);
        s.Need(s.Ren == 1 && draw.Context.RequestedCount == 1 && draw.Return.Kind == NativeDrawAttemptReturnKind.ProgramInstruction &&
            s.V(0).HandCount == beforeUse - 1,
            "Original Jizhi has already paid exactly one Ren before its real one-card native draw; it has no keep/discard or hand-limit branch.");
        s.Cold(); s.Skip(); s.Play();
        s.Need(s.Ren == 1 && s.V(0).HandCount == beforeUse + 2 && s.Limit() == s.V(0).Hp &&
            s.E<NativeDrawAttemptDecidedEvent>().Count(e => e.AttemptFrameId == draw.Id && e.RequestedCount == 1 && !e.Replaced) == 1 &&
            s.E<ProgramBindingResolvedEvent>().Count(e => e.FrameId == producer.Id && e.Completed) == 1,
            "Cold returns to the exact once-paid Jizhi producer, draws one plus the actual DrawTwo's two, and grants no boundary hand-limit bonus.");

        s.Use(Driver, "kill", [], [2]); s.Reach(() => s.P?.SkillPrompt?.SkillId == Lianpo); s.Activate(); s.Play();
        s.Use(Driver, "kill", [], [1]); s.Play();
        var turn = s.G.State.TurnNumber;
        s.Need(s.E<ProgramExtraTurnPendedEvent>().Count(e => e.SkillId == Lianpo) == 1 && !s.V(2).IsAlive && !s.V(1).IsAlive,
            "Two real kills in the same actual turn reserve one original Lianpo turn.");
        s.End(); s.Play();
        s.Need(s.G.State.CurrentSeat == 0 && s.G.State.TurnNumber == turn + 1 && s.V(3).IsAlive,
            "The reserved native extra turn really starts immediately after the current turn ends.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ol-2016-sima", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Discard}}","revision":1,"triggers":[{"id":"actual-discard","window":"discardPhaseStarting","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectOwnedCards","target":"owner","minimumCards":1,"maximumCards":1,"zones":["hand"],"resultBind":"phase-card"},{"op":"moveBoundCards","target":"owner","sourceBind":"phase-card","destination":"discardPile","awaitMovementTriggers":true}]}]},
              {"id":"{{Hold}}","revision":1,"triggers":[{"id":"draw","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"judge","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"startJudgment","target":"owner","judgmentReason":"fixture.ol-2016-sima","resultBind":"judged","visibility":"public"},{"op":"moveBoundCards","target":"owner","sourceBind":"judged","destination":"discardPile"}]},
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]}]}
            ]}
            """;
            var labels = new[] { NoDraw, Quiet, Discard, Hold, Driver }.ToDictionary(id => id, id => new { name = id, description = "2016原版神司马懿实际阶段与支付" });
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "保留真实回合的角色", "", Quiet, "wei", 12));
            b.AddDeck(new(Deck, "固定实体锦囊", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:draw_two", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "OL2016神司马懿实际阶段", 4, 4, new Dictionary<string, int>(), Deck,
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
        public int Ren => V(0).Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.Ren)?.Count ?? 0;
        public bool Has(string id) => V(0).Skills?.Any(k => k.Id == id) == true;
        public int Limit() => (int)typeof(GameEngine).GetMethod("GetHandLimit", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(G,
            [((System.Collections.IList)typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(G)!)[0]])!;
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Use(string skill, string binding, IReadOnlyList<int> cards, IReadOnlyList<int> targets) => Accept(new UseProgramSkillCommand(0, skill, binding, cards, targets, G.Revision, P!.PromptId));
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Skip() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Reach(Func<bool> done)
        {
            for (var i = 0; i < 200; i++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == Hold) Skip();
                else if (P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(c => c.Parameters.GetValueOrDefault("response") == "let-die");
                else if (P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End();
                else if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected original Sima boundary " + JsonSerializer.Serialize(P));
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded original Sima flow was not reached " + JsonSerializer.Serialize(P));
        }
        public void Cold()
        {
            var before = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before.SequenceEqual(Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v)))), "Cold preserves the exact paid original Jizhi draw child.");
        }
        public void Accept(GameCommand command) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected original Sima command."); }
        public void Need(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, Ren, Hand = V(0).HandCount, P, Facts = G.Events.TakeLast(6).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(37, 1, "ol-2016-sima", Hero, 4, 0, 0, 0, 0,
                [NoDraw, Discard, Hold, Driver], Enumerable.Range(0, 8).Select(_ => new ShanheStartingCard(CardKind.Slash, Suit.Club, 7)).ToArray(),
                [new(CardKind.Crossbow, Suit.Club, 1)],
                [new(Peer, ExtraSkillIds: [NoDraw]) { StartingHand = [] }, new(Peer, ExtraSkillIds: [NoDraw]) { StartingHand = [] }],
                [new(Peer, ExtraSkillIds: [NoDraw]) { StartingHand = [] }], new string('f', 64)) { DefaultGodFactionId = "wei" };
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
}
