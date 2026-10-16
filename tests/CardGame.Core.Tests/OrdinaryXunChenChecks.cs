using System.Reflection;
using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

// Current OL Xun Chen: docs/content/sources/ol-xun-chen-2026-10-05.json.
internal static class OrdinaryXunChenChecks
{
    private const string Fenglve = "ol:fenglve";
    private const string Moushi = "ol:moushi";

    public static void FenglveContestsAndMoushiMarks()
    {
        var (g, r) = Start();
        // Turn 1 offers both play-phase triggers; decline Moushi, run Fenglve.
        var moushiOffer = P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Moushi);
        if (moushiOffer) Skip(g);
        Activate(g);
        SelectTarget(g, 1);
        // Answer the owner's pindian-card pick, the optional settlement gift, and
        // any win/loss payment prompt until the play phase resumes.
        var entryMovements = g.CardMovements.Count;
        for (var i = 0; i < 80; i++)
        {
            if (P(g) is { } p && p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0) break;
            if (P(g) is { } option && option.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") is "give" or "keep"))
            {
                Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "keep");
                continue;
            }
            if (P(g) is { } pick && pick.Choices.Any(c => c.Cards.Count == 1))
            {
                Answer(g, c => c.Cards.Count == 1);
                continue;
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        Require(P(g) is { } resumed && resumed.Kind == DecisionKind.PlayCard && resumed.PlayerSeat == 0,
            "The contest settles back into the Cao Xing play phase.");
        var pindianMoved = g.CardMovements.Skip(entryMovements)
            .Any(m => m.Reason.Value.Contains("pindian", StringComparison.Ordinal));
        Require(pindianMoved,
            "Fenglve resolves a real pindian and settles its branch movements.");
        var paidToCounterpart = g.CardMovements.Skip(entryMovements)
            .Any(m => m.To == CardLocation.Hand(1) && m.Reason.Value.Contains("ol:fenglve", StringComparison.Ordinal));
        Require(paidToCounterpart,
            "Every equal-rank contest is a loss, so Fenglve pays the counterpart one hand card.");
        Replay(g, r);
    }

    private static void SelectTarget(GameEngine g, int seat)
    {
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target" &&
            c.Targets.SequenceEqual([seat])));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-target" &&
            c.Targets.SequenceEqual([seat]));
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-xc", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:xun-chen");
            // Elevated fixture HP keeps the lord alive across the AI slash cycle (no peaches exist).
            b.AddGeneral(new("fixture:xc", "荀谌", "ol-xun-chen", Fenglve, "qun", 6, [Moushi]));
            for (var i = 1; i < 4; i++)
                b.AddGeneral(new($"fixture:xc-{i}", "其他" + i, "supporter", "standard:none", "wei", 5, null));
            // Alternating hearts: ranks stay equal (pindian ties are losses) while
            // the AI still holds slashes it will play during its own turns.
            b.AddDeck(new("fixture:xc-deck", "固定", 4, 1, [])
            {
                PhysicalCards = Enumerable.Range(0, 64).Select(i =>
                    new ContentDeckPhysicalCard(i % 2 == 0 ? "standard:nullification" : "standard:slash",
                        Suit.Heart, 5)).ToArray()
            });
            b.AddMode(new("identity:xc", "固定", 4, 4,
                new Dictionary<string, int> { { nameof(Role.Lord), 1 }, { nameof(Role.Loyalist), 1 }, { nameof(Role.Rebel), 2 } },
                "fixture:xc-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:xc", "fixture:xc-1", "fixture:xc-2", "fixture:xc-3"]));
        }
    }

    private static (GameEngine, ContentRegistry) Start()
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 47, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:xc",
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 12
        }, r);
        Accept(g, new StartGameCommand());
        Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:xc", g.Revision, P(g)!.PromptId));
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        return (g, r);
    }
}
