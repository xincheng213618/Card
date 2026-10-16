using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

// Current OL Yi Ji: docs/content/sources/ol-yi-ji-2026-10-05.json.
internal static class OrdinaryYiJiChecks
{
    private const string Jijie = "ol:jijie";
    private const string Jiyuan = "ol:jiyuan";
    private const string DriverId = "fixture:yj-driver";

    public static void JijieHandsDeckBottomToSelectedParticipant()
    {
        var (g, r) = Start();
        Settle(g);
        var deckBefore = ZoneCount(g, CardZoneKind.DrawPile);
        var recipientBefore = HandCount(g, 1);
        Accept(g, new UseProgramSkillCommand(0, Jijie, "give", [], [1], g.Revision, P(g)!.PromptId));
        Require(HandCount(g, 1) == recipientBefore + 1 && ZoneCount(g, CardZoneKind.DrawPile) == deckBefore - 1 &&
            g.CardMovements.Any(m => m.From.Zone == CardZoneKind.DrawPile &&
                m.To == CardLocation.Hand(1) && m.Reason.Value.Contains("jijie-give", StringComparison.Ordinal)),
            "Jijie moves the draw pile's bottom card into the selected player's hand exactly once.");
        Settle(g);
        var retry = g.Submit(new UseProgramSkillCommand(0, Jijie, "give", [], [2], g.Revision, P(g)!.PromptId));
        Require(!retry.Accepted, "The once-per-turn ledger refuses a second Jijie activation.");
        Require(HandCount(g, 1) == recipientBefore + 1 && ZoneCount(g, CardZoneKind.DrawPile) == deckBefore - 1,
            "The refused activation moves no card.");
        Replay(g, r);
    }

    private static int HandCount(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat].HandCount;

    private static int ZoneCount(GameEngine g, CardZoneKind zone) =>
        g.CreateCardZoneDiagnostics().Count(c => c.Location.Zone == zone);

    private static SkillProgram Load(string id, string members) => SkillProgramCatalog.Load(
        "{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"" + id +
        "\",\"revision\":1," + members + "}]}",
        "{\"schemaVersion\":3,\"skills\":{\"" + id + "\":{\"name\":\"fixture\",\"description\":\"fixture\"}}}").Programs[id];

    private static string Activation(string id, string effects, int targets = 0) =>
        "{\"id\":\"" + id + "\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":" + targets +
        ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"effects\":" + effects + "}";

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-yj", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:yi-ji");
            b.AddSkill(new("fixture:yj-driver", "fixture", "fixture")
            {
                Program = Load("fixture:yj-driver", "\"activations\":[" +
                    Activation("hurt", "[{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":5}]", 1) + "," +
                    Activation("give", "[{\"op\":\"selectOwnedCards\",\"target\":\"owner\",\"amount\":1,\"zones\":[\"hand\"],\"resultBind\":\"available\"},{\"op\":\"giveBoundCard\",\"target\":\"owner\",\"sourceBind\":\"available\",\"targetKind\":\"otherLiving\"}]") + "]")
            });
            b.AddGeneral(new("fixture:yj", "伊籍", "ol-yi-ji", Jijie, "shu", 3,
                new[] { Jiyuan, DriverId }));
            for (var i = 1; i < 4; i++)
                b.AddGeneral(new($"fixture:yj-{i}", "其他" + i, "supporter", "standard:none", "wei", 5, null) { InitialHp = 3 });
            b.AddDeck(new("fixture:yj-deck", "固定", 4, 1, [])
            {
                PhysicalCards = Enumerable.Range(0, 64).Select(i =>
                    new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 5)).ToArray()
            });
            b.AddMode(new("identity:yj", "固定", 4, 4,
                new Dictionary<string, int> { { nameof(Role.Lord), 1 }, { nameof(Role.Loyalist), 1 }, { nameof(Role.Rebel), 2 } },
                "fixture:yj-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:yj", "fixture:yj-1", "fixture:yj-2", "fixture:yj-3"]));
        }
    }

    private static (GameEngine, ContentRegistry) Start()
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 37, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:yj",
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, r);
        Accept(g, new StartGameCommand());
        Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:yj", g.Revision, P(g)!.PromptId));
        Settle(g);
        return (g, r);
    }
}
