using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MobileShenGanNingChecks
{
    private const string Hero = "mobile:shen-gan-ning", Jieying = "mobile-god:jieying", Quiet = "fixture:mobile-gn-quiet",
        NoDraw = "fixture:mobile-gn-no-draw", GainHold = "fixture:mobile-gn-gain", Peer = "fixture:mobile-gn-peer", Deck = "fixture:mobile-gn-deck";
    public static void EndedCampRemovalPrecedesGainAndResumesCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(37, 1, "mobile-gn", Hero, 3, 0, 0, 0, 0, [NoDraw, GainHold], [], [],
            [new(Peer, ExtraSkillIds: [NoDraw]) { StartingHand = [new(CardKind.Dodge, Suit.Heart, 7), new(CardKind.Peach, Suit.Heart, 3)] },
             new(Peer, ExtraSkillIds: [NoDraw]) { StartingHand = [] }],
            [new(Peer, ExtraSkillIds: [NoDraw]) { StartingHand = [] }], new string('f', 64)) { DefaultGodFactionId = "wu" };
        var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0,
            HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId,
            ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Jieying);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, () => P(g)?.Choices.Any(c => c.Targets.SequenceEqual([2])) == true);
        Answer(g, c => c.Targets.SequenceEqual([2]));
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == GainHold && g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.MobileCampClaim is not null));
        var frame = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MobileCampClaim is not null);
        var receipt = frame.MobileCampClaim!; var facts = g.Events.Select(e => e.Payload).ToArray();
        var ended = Array.FindIndex(facts, e => e is TurnEndedEvent t && t.TurnNumber == receipt.ActualTurn && t.ActorSeat == 2);
        var removed = Array.FindIndex(facts, e => e is PlayerMarkerChangedEvent m && m.ResolutionId == frame.Id && m.PlayerSeat == 2 && m.Marker == PlayerMarkerKind.Camp && m.Count == 0);
        var firstGain = Array.FindIndex(facts, e => e is CardMovedEvent m && m.Reason.Value == "skill.mobile-god.jieying.ended-hand");
        Need(ended >= 0 && removed > ended && firstGain > removed && receipt.EndedSeat == 2 && receipt.Cards.Count == 4 &&
            V(g, 2).HandCount == 0 && Enumerable.Range(0, 4).All(s => (V(g, s).Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.Camp)?.Count ?? 0) == 0),
            "Mobile Jieying waits for the actual TurnEnded, includes the real ending-stage draw, and removes Camp before gaining the hand without returning it to Gan Ning.");
        var before = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray();
        g = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(before.SequenceEqual(Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s)))), "The original paid camp collection and gained-card child survive cold replay.");
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        Reach(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        Need(g.Events.Count(e => e.Payload is MobileCampClaimedEvent c && c.ProgramFrameId == frame.Id) == 1 &&
            receipt.Cards.All(id => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(2) && m.To == CardLocation.Hand(0) && m.Reason.Value == "skill.mobile-god.jieying.ended-hand") == 1) &&
            V(g, 0).Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.Camp)?.Count == 1 &&
            g.Events.Count(e => e.Payload is PlayerMarkerChangedEvent m && m.PlayerSeat == 0 && m.Marker == PlayerMarkerKind.Camp && m.Count == 1) == 2,
            "Returning the gain child never repeats the paid hand or returns Camp; a new Camp is generated only at Gan Ning's next real turn start.");
    }
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(c); Need(r.Accepted, r.Error?.Message ?? "Mobile Gan Ning command rejected."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Reach(GameEngine g, Func<bool> ready)
    {
        for (var i = 0; i < 180; i++)
        {
            if (ready()) return;
            if (P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } play) Accept(g, new EndPlayPhaseCommand(0, g.Revision, play.PromptId));
            else if (P(g) is { PlayerSeat: 0 } p) throw new InvalidOperationException("Unexpected mobile Gan Ning boundary: " + JsonSerializer.Serialize(p));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Mobile Gan Ning did not reach the bounded real occurrence: " + SnapshotJson.Serialize(g.CreateSnapshot(0)));
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mobile-shen-gan-ning", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[
                {"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]},
                {"id":"ending-draw","window":"turnEnding","subject":"owner","optional":false,"effects":[{"op":"draw","target":"owner","amount":1}]}]},
              {"id":"{{GainHold}}","revision":1,"triggers":[{"id":"gain","isSkillInvocation":false,"window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.mobile-god.jieying.ended-hand"],"optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]}
            ]}
            """;
            var labels = new[] { NoDraw, Quiet, GainHold }.ToDictionary(id => id, id => new { name = id, description = "劫营真实回合结束、获得牌窗口" });
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "有真实结束阶段的角色", "", Quiet, "wei", 12));
            b.AddDeck(new(Deck, "固定实体牌", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "移动版神甘宁小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
