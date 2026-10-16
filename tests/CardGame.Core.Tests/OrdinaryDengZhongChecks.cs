using System.Reflection;
using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

// Current OL Deng Zhong: docs/content/sources/ol-deng-zhong-2026-10-05.json.
internal static class OrdinaryDengZhongChecks
{
    private const string Kanpo = "ol:kanpo";
    private const string Gengzhan = "ol:gengzhan";

    public static void KanpoTakesMatchingSuitCard()
    {
        var (g, r) = Start();
        var victimHandBefore = HandCount(g, 1);
        var mySlash = Hand(g, 0).First(c => c.Kind is CardKind.Slash);
        Accept(g, new PlayCardCommand(0, mySlash.Id, [1], g.Revision, P(g)!.PromptId));
        try
        {
            ReachProgramChoice(g);
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine("DIAG victimhp=" + g.CreateSnapshot(0).Players[1].Hp +
                " events=" + string.Join(";", g.Events.Select(e => e.Payload).TakeLast(24)
                    .Select(p => p.GetType().Name.Replace("Event", "") + ":" +
                        (p is CardGame.Core.DamageAppliedEvent d ? d.SourceSeat + ">" + d.TargetSeat + "@" + d.Amount :
                         p is CardUsedEvent u ? u.SourceSeat + ">" + u.TargetSeat + ":" + u.CardKind :
                         p is CardGame.Core.ProgramBindingStartedEvent b ? b.SkillId + "/" + b.BindingId :
                         ""))));
            throw;
        }
        var matching = Hand(g, 1).Where(c => c.Suit == Suit.Heart).ToList();
        Require(matching.Count > 0, "The victim holds a heart card to take. hand=" + Dump(g, 1));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        // The take prompt lists one slot choice per matching-suit card, in hand order.
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card"));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card" &&
            c.Parameters.GetValueOrDefault("slot-index") == "0");
        ReachPlay(g);
        Require(g.CreateSnapshot(0).Players[1].HandCount == victimHandBefore - 1 &&
            Hand(g, 0).Any(c => c.Id == matching[0].Id) &&
            g.CardMovements.Any(m => m.CardId == matching[0].Id && m.From == CardLocation.Hand(1) &&
                m.To == CardLocation.Hand(0) && m.Reason.Value.Contains("kanpo", StringComparison.Ordinal)),
            "Kanpo moves the matching-suit card from the victim's hand to the owner's. hand0=" + Dump(g, 0) +
                " hand1=" + Dump(g, 1) +
                " moves=" + string.Join(";", g.CardMovements.Where(m => m.CardId == matching[0].Id)
                    .Select(m => m.From.Zone + "->" + m.To.Zone + ":" + m.Reason.Value)));
        Replay(g, r);
    }

    private static IReadOnlyList<Card> DiscardCards(GameEngine g) => ((CardZoneStore)typeof(GameEngine)
        .GetField("_cardZones", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g)!)
        .CardsAt(CardLocation.DiscardPile);

    private static IReadOnlyList<Card> Hand(GameEngine g, int seat) => ((CardZoneStore)typeof(GameEngine)
        .GetField("_cardZones", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g)!)
        .CardsAt(CardLocation.Hand(seat));

    private static int HandCount(GameEngine g, int seat) => Hand(g, seat).Count;

    private static string Dump(GameEngine g, int seat) => string.Join(",",
        Hand(g, seat).Select(c => c.Kind.ToString().ToLowerInvariant()[0] + ":" + c.Suit.ToString().ToLowerInvariant()[0] + c.Id));

    private static void ReachProgramChoice(GameEngine g)
    {
        try
        {
            Reach(g, p =>
                p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") is "activate" or "skip") &&
                (p.SkillPrompt?.SkillId is Kanpo or Gengzhan ||
                 p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") is Kanpo or Gengzhan)));
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine("DIAG-PROMPT " + System.Text.Json.JsonSerializer.Serialize(P(g),
                new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            throw;
        }
    }

    private static void ReachPlay(GameEngine g) => Reach(g, p =>
        p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0 ||
        p.Kind == DecisionKind.DiscardCards && p.PlayerSeat == 0 && p.RequiredCardCount > 0 ||
        p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-dz", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            RegisterCore(b, peace: false);
        }

        internal static void RegisterCore(IContentRegistryBuilder b, bool peace)
        {
            CharacterFixtureContent.Register(b, "ol:deng-zhong");
            b.AddGeneral(new("fixture:dz", "邓忠", "ol-deng-zhong", Kanpo, "wei", 4, [Gengzhan]));
            for (var i = 1; i < 4; i++)
                b.AddGeneral(new($"fixture:dz-{i}", "其他" + i, "supporter", "standard:none", "wei", 6, null));
            var cards = new System.Collections.Generic.List<ContentDeckPhysicalCard>();
            for (var i = 0; i < 64; i++)
            {
                cards.Add(!peace && i % 4 == 2
                    ? new ContentDeckPhysicalCard("standard:dismantlement", Suit.Spade, 3)
                    : peace
                        ? new ContentDeckPhysicalCard("standard:dismantlement", Suit.Spade, 3)
                        : new ContentDeckPhysicalCard("standard:slash", Suit.Heart, 5));
            }
            b.AddDeck(new("fixture:dz-deck", "固定", 4, 1, [])
            {
                PhysicalCards = cards.ToArray()
            });
            b.AddDeck(new("fixture:dz-deck-peace", "固定和平", 4, 1, [])
            {
                PhysicalCards = Enumerable.Range(0, 64).Select(_ =>
                    new ContentDeckPhysicalCard("standard:dismantlement", Suit.Spade, 3)).ToArray()
            });
            b.AddMode(new("identity:dz", "固定", 4, 4,
                new Dictionary<string, int> { { nameof(Role.Lord), 1 }, { nameof(Role.Loyalist), 1 }, { nameof(Role.Rebel), 2 } },
                "fixture:dz-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:dz", "fixture:dz-1", "fixture:dz-2", "fixture:dz-3"]));
            b.AddMode(new("identity:dz-peace", "固定和平", 4, 4,
                new Dictionary<string, int> { { nameof(Role.Lord), 1 }, { nameof(Role.Loyalist), 1 }, { nameof(Role.Rebel), 2 } },
                "fixture:dz-deck-peace", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:dz", "fixture:dz-1", "fixture:dz-2", "fixture:dz-3"]));
        }
    }

    private static (GameEngine, ContentRegistry) Start(bool peace = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new PeaceFixture(peace));
        var g = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = peace ? "identity:dz-peace" : "identity:dz",
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 12
        }, r);
        Accept(g, new StartGameCommand());
        Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:dz", g.Revision, P(g)!.PromptId));
        ReachPlay(g);
        return (g, r);
    }

    private sealed class PeaceFixture : IGameContentPackage
    {
        private readonly bool _peace;
        public PeaceFixture(bool peace) => _peace = peace;
        public PackageManifest Manifest { get; } = new("fixture-dz", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            try
            {
                Fixture.RegisterCore(b, _peace);
            }
            catch (Exception diag)
            {
                Console.WriteLine("DIAG-INNER: " + (diag.InnerException?.ToString() ?? diag.ToString()));
                throw;
            }
        }
    }
}
