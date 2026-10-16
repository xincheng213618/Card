using System.Reflection;
using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

// Current OL Zhou Fang: docs/content/sources/ol-zhou-fang-2026-10-07.json.
internal static class OrdinaryZhouFangChecks
{
    private const string Duanfa = "ol:duanfa";
    private const string Youdi = "ol:youdi";
    private const string Driver = "fixture:zf-driver";

    public static void DuanfaRecyclesBlackCardsInPhaseLedgerSteps()
    {
        var (g, r) = Start("identity:zf-duanfa");
        var hand = CardsAt(g, CardLocation.Hand(0));
        var black = hand.Where(card => card.Suit is Suit.Spade or Suit.Club).Select(card => card.Id).Order().ToArray();
        Require(black.Length >= 3, "The fixture deals at least three black hand cards. black=" + black.Length);
        var maxHp = g.CreateSnapshot(0).Players[0].MaxHp;
        // First use recycles two black cards one-for-one.
        ActivateDuanfa(g, black.Take(2).ToArray());
        var recycled = g.Events.Select(e => e.Payload).OfType<ProgramDuanfaRecycledEvent>().ToList();
        Require(recycled.Count == 1 && recycled[0].DiscardedCardIds.Order().SequenceEqual(black.Take(2).Order()) &&
                recycled[0].DrawnCount == 2 && recycled[0].TurnNumber == CurrentTurn(g),
            "The first recycle discards the two selected black cards and draws two.");
        foreach (var id in black.Take(2))
            Require(g.CardMovements.Any(m => m.CardId == id && m.From == CardLocation.Hand(0) &&
                    m.To == CardLocation.DiscardPile &&
                    m.Reason.Value.Contains("duanfa", StringComparison.Ordinal)),
                "Both discarded black cards leave the hand for the discard pile under the Duanfa reason.");
        var afterFirst = CardsAt(g, CardLocation.Hand(0));
        Require(afterFirst.Count == hand.Count,
            "The recycle keeps the hand size. before=" + hand.Count + " after=" + afterFirst.Count);
        Require(black.Skip(2).All(id => afterFirst.Any(card => card.Id == id)),
            "The unselected black cards remain in hand beside the black replacements.");
        // Later uses keep recycling up to two black cards per settlement until the
        // play-phase ledger reaches the owner's maximum health.
        var recycledTotal = 2;
        for (var guard = 0; recycledTotal < maxHp; guard++)
        {
            Require(guard < 8, "The phase ledger never reached the maximum health. total=" + recycledTotal);
            var pick = Math.Min(2, maxHp - recycledTotal);
            var available = CardsAt(g, CardLocation.Hand(0))
                .Where(card => card.Suit is Suit.Spade or Suit.Club).Select(card => card.Id).Order().ToArray();
            Require(available.Length >= pick,
                "The hand still holds enough black cards. available=" + available.Length + " pick=" + pick);
            ActivateDuanfa(g, available.Take(pick).ToArray());
            recycledTotal += pick;
        }
        recycled = g.Events.Select(e => e.Payload).OfType<ProgramDuanfaRecycledEvent>().ToList();
        Require(recycled.Count > 1 && recycled.Sum(e => e.DiscardedCardIds.Count) == maxHp &&
                recycled.All(e => e.DrawnCount == e.DiscardedCardIds.Count),
            "Every recycle draws exactly the discarded count until maxHp cards moved. events=" +
            DescribeRecycled(recycled) + " maxHp=" + maxHp);
        // The play-phase ledger is exhausted: the activation is no longer offered.
        var playPrompt = AwaitPlayPrompt(g);
        var probe = g.Submit(new UseProgramSkillCommand(0, Duanfa, "duanfa-recycle", [], [], g.Revision,
            playPrompt.PromptId));
        Require(!probe.Accepted && probe.Error?.Code == CommandErrorCode.IllegalAction,
            "Duanfa is unavailable once maxHp cards were recycled this phase. accepted=" + probe.Accepted +
            " error=" + probe.Error?.Message);
        Replay(g, r);
    }

    private static string DescribeRecycled(IReadOnlyList<ProgramDuanfaRecycledEvent> events) =>
        string.Join(";", events.Select(e => "drawn" + e.DrawnCount + ":[" + string.Join(",", e.DiscardedCardIds) + "]"));

    private static int CurrentTurn(GameEngine g) =>
        (int)typeof(GameEngine).GetField("_turnNumber", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(g)!;

    private static PendingDecision AwaitPlayPrompt(GameEngine g)
    {
        for (var i = 0; i < 100; i++)
        {
            if (P(g) is { } p && p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0) return p;
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixture never reached the play prompt: " + P(g)?.Prompt);
    }

    // Activates Duanfa at the play prompt and picks the requested black cards in
    // the private owned-card prompt; selection happens inside the program, so
    // the command itself carries no cards.
    private static void ActivateDuanfa(GameEngine g, IReadOnlyList<int> wantedCardIds)
    {
        var settledBefore = g.Events.Select(e => e.Payload).OfType<ProgramDuanfaRecycledEvent>().Count();
        var prompt = AwaitPlayPrompt(g);
        Accept(g, new UseProgramSkillCommand(0, Duanfa, "duanfa-recycle", [], [], g.Revision, prompt.PromptId));
        var pending = new List<int>(wantedCardIds);
        for (var i = 0; i < 200; i++)
        {
            if (g.Events.Select(e => e.Payload).OfType<ProgramDuanfaRecycledEvent>().Count() > settledBefore) return;
            var p = P(g);
            if (p is not null && p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0) return;
            if (p is null) { Accept(g, new AdvanceOneStepCommand(g.Revision)); continue; }
            if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"))
            {
                if (pending.Count > 0 && p.Choices.Any(c =>
                        c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards" &&
                        c.Cards.Contains(pending[0])))
                {
                    var next = pending[0];
                    pending.RemoveAt(0);
                    Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards" &&
                        c.Cards.Contains(next));
                    continue;
                }
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards");
                continue;
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Duanfa never settled its selection: " + P(g)?.Prompt);
    }

    private static IReadOnlyList<Card> CardsAt(GameEngine g, CardLocation location) =>
        ((CardZoneStore)typeof(GameEngine).GetField("_cardZones", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(g)!).CardsAt(location);

    private static int stripUses;

    private static (GameEngine, ContentRegistry) Start(string modeId, bool stripChooserHand = false,
        int growHandStrips = 0)
    {
        stripUses = 0;
        var r = ContentRegistry.Build(new StandardContentPackage(), new ZhouFangFixture());
        var g = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = modeId, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 24
        }, r);
        Accept(g, new StartGameCommand());
        Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:zf", g.Revision, P(g)!.PromptId));
        for (var i = 0; i < 100; i++)
        {
            if (P(g) is not { } p) { Accept(g, new AdvanceOneStepCommand(g.Revision)); continue; }
            if (p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0) break;
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        // The opening deal completes only after every general is chosen, so the
        // driver strips run once the first play prompt is pending.
        if (stripChooserHand)
            for (var i = 0; i < 10 && CardsAt(g, CardLocation.Hand(1)).Count > 0; i++)
            {
                AcceptDriver(g, "strip-hands", []);
                stripUses++;
            }
        for (var i = 0; i < growHandStrips; i++)
            AcceptDriver(g, "strip-hands", []);
        return (g, r);
    }

    private static void AcceptDriver(GameEngine g, string activation, int[] targets)
    {
        for (var i = 0; i < 100; i++)
        {
            if (P(g) is { } p && p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0)
            {
                Accept(g, new UseProgramSkillCommand(0, Driver, activation, [], targets, g.Revision, p.PromptId));
                return;
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The driver never reached the play prompt: " + P(g)?.Prompt);
    }

    private sealed class ZhouFangFixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-zf", new(1, 0, 0), []);

        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:zhou-fang");
            // The strip driver removes one random hand card from every other
            // living character per use, so repeated uses empty seat 1's hand.
            b.AddSkill(new(Driver, "周鲂夹具", "fixture")
            {
                Program = SkillProgramCatalog.Load(
                    "{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion +
                    ",\"skills\":[{\"id\":\"" + Driver + "\",\"revision\":1,\"activations\":[" +
                    "{\"id\":\"strip-hands\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"effects\":[{\"op\":\"takeRandomCardFromEveryOtherCharacter\",\"target\":\"owner\",\"zones\":[\"hand\"]}]}]}]}",
                    "{\"schemaVersion\":3,\"skills\":{\"" + Driver + "\":{\"name\":\"fixture\",\"description\":\"fixture\"}}}").Programs[Driver]
            });
            b.AddGeneral(new("fixture:zf", "周鲂", "ol-zhou-fang", Duanfa, "wu", 3,
                new[] { Youdi, Driver }));
            for (var i = 1; i < 4; i++)
                b.AddGeneral(new($"fixture:zf-{i}", "fixture-peer-" + i, "supporter", "standard:none", "wei", 6, null));
            // The spade deck keeps the Duanfa phase ledger deterministic; the club
            // peach decks bait a black non-Slash for both Youdi branches; the
            // heart deck baits a red Slash that pays nothing.
            b.AddDeck(new("fixture:zf-deck-duanfa", "fixed", 4, 1, [])
            {
                PhysicalCards = UniformDeck("standard:slash", Suit.Spade)
            });
            b.AddDeck(new("fixture:zf-deck-youdi", "fixed", 4, 1, [])
            {
                PhysicalCards = UniformDeck("standard:peach", Suit.Club)
            });
            b.AddDeck(new("fixture:zf-deck-youdi-slash", "fixed", 4, 1, [])
            {
                PhysicalCards = UniformDeck("standard:slash", Suit.Heart)
            });
            b.AddDeck(new("fixture:zf-deck-youdi-black", "fixed", 4, 1, [])
            {
                PhysicalCards = UniformDeck("standard:peach", Suit.Club)
            });
            string[] pool = ["fixture:zf", "fixture:zf-1", "fixture:zf-2", "fixture:zf-3"];
            foreach (var (deck, id) in new[]
                     {
                         ("fixture:zf-deck-duanfa", "identity:zf-duanfa"),
                         ("fixture:zf-deck-youdi", "identity:zf-youdi"),
                         ("fixture:zf-deck-youdi-slash", "identity:zf-youdi-slash"),
                         ("fixture:zf-deck-youdi-black", "identity:zf-youdi-black")
                     })
                b.AddMode(new(id, id, 4, 4,
                    new Dictionary<string, int>
                        { { nameof(Role.Lord), 1 }, { nameof(Role.Loyalist), 1 }, { nameof(Role.Rebel), 2 } },
                    deck, GeneralCandidateCount: 4, GeneralPoolIds: pool));
        }

        private static ContentDeckPhysicalCard[] UniformDeck(string kind, Suit suit) =>
            Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard(kind, suit, 5)).ToArray();
    }
}
