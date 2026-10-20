using System.Reflection;
using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

// Current OL Cao Xing: docs/content/sources/ol-cao-xing-2026-10-05.json.
internal static class OrdinaryCaoXingChecks
{
    private const string Liushi = "ol:liu-shi";
    private const string Zhanwan = "ol:zhan-wan";

    public static void LiushiDamagesAndZhanwanDrains()
    {
        var (g, r) = Start();
        var cost = g.CreateSnapshot(0).Players[0].Hand.First(card => card.Suit == Suit.Heart).Id;
        var victimHandBefore = HandCount(g, 1);
        var victimHpBefore = g.CreateSnapshot(0).Players[1].Hp;
        Accept(g, new UseProgramSkillCommand(0, Liushi, "topdeck-heart-slash", [cost], [1], g.Revision, P(g)!.PromptId));
        AwaitSettledDamage(g, 1, victimHpBefore - 1);
        AwaitProgramMarker(g);
        Require(!g.CreateSnapshot(0).Players[0].Hand.Any(card => card.Id == cost) &&
            g.CardMovements.Any(m => m.CardId == cost && m.From.Zone == CardZoneKind.Hand &&
                m.To.Zone == CardZoneKind.DrawPile),
            "Liushi moves the paid heart hand card onto the draw pile top.");
        Require(g.CreateSnapshot(0).Players[1].Hp == victimHpBefore - 1 &&
            HandLimit(g, 1) == victimHpBefore - 2 && victimHandBefore == 4,
            "The virtual slash deals its damage and the marker lowers the victim's hand limit by one.");
        SettleSkippingLiushi(g);
        var drained = DiscardCount(g, 1);
        var zhanwanDraws = g.CardMovements.Count(m => m.To == CardLocation.Hand(0) &&
            m.Reason.Value.Contains("ol:zhan-wan", StringComparison.Ordinal));
        Require(drained > 0 && zhanwanDraws == drained,
            "Zhanwan draws exactly the victim's discard-phase count at their phase end. drained=" + drained +
            " draws=" + zhanwanDraws + " limit=" + HandLimit(g, 1) + " seat1hand=" + HandCount(g, 1));
        Require(HandLimit(g, 1) == victimHpBefore - 1,
            "The drain removes the Liu Shi effect and restores the victim's hand limit.");
        var drawsAfterFirstDrain = zhanwanDraws;
        var discardsAfterFirstDrain = drained;
        SettleSkippingLiushi(g);
        var secondDrain = DiscardCount(g, 1) - discardsAfterFirstDrain;
        var secondDraws = g.CardMovements.Count(m => m.To == CardLocation.Hand(0) &&
            m.Reason.Value.Contains("ol:zhan-wan", StringComparison.Ordinal)) - drawsAfterFirstDrain;
        Require(HandLimit(g, 1) == victimHpBefore - 1 && secondDraws == 0,
            "A cleared marker neither drains again nor restores twice. limit=" + HandLimit(g, 1) +
            " secondDraws=" + secondDraws + " secondDrain=" + secondDrain);
        Replay(g, r);
    }

    private static void SettleSkippingLiushi(GameEngine g)
    {
        var entryTurnNumber = g.Events.Select(e => e.Payload).OfType<CardGame.Core.TurnStartedEvent>().LastOrDefault()?.TurnNumber ?? 0;
        for (var i = 0; i < 400; i++)
        {
            var lastTurn = g.Events.Select(e => e.Payload).OfType<CardGame.Core.TurnStartedEvent>().LastOrDefault();
            if (lastTurn is { } turn && turn.ActorSeat == 0 && turn.TurnNumber > entryTurnNumber &&
                P(g) is { } next && next.Kind == DecisionKind.PlayCard && next.PlayerSeat == 0) return;
            if (P(g) is { } play && play.Kind == DecisionKind.PlayCard && play.PlayerSeat == 0)
            {
                g.Submit(new EndPlayPhaseCommand(0, g.Revision, play.PromptId));
                continue;
            }
            if (P(g) is { } skill && skill.SkillPrompt?.SkillId == Liushi &&
                skill.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"))
            {
                Skip(g);
                continue;
            }
            if (P(g) is { } discard && discard.Kind == DecisionKind.DiscardCards && discard.PlayerSeat == 0)
            {
                Accept(g, new DiscardCardsCommand(0,
                    discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray(), discard.PromptId, g.Revision));
                continue;
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixture never returned to the Cao Xing play phase.");
    }

    private static int DiscardCount(GameEngine g, int seat) =>
        g.CardMovements.Count(m => m.From == CardLocation.Hand(seat) && m.To.Zone == CardZoneKind.DiscardPile &&
            m.Reason.Value.Contains("discard", StringComparison.Ordinal));

    private static void AwaitProgramMarker(GameEngine g)
    {
        for (var i = 0; i < 200; i++)
        {
            if (g.Events.Any(e => e.Payload.GetType().Name.Contains("PlayerMarkerChanged", StringComparison.Ordinal))) return;
            if (P(g) is { } offer && offer.SkillPrompt?.SkillId == Liushi &&
                offer.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"))
            {
                Skip(g);
                continue;
            }
            g.Submit(new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The Liushi damage trigger never marked its victim. prompt=" + P(g)?.Prompt +
            " events=" + string.Join(";", g.Events.Select(e => e.Payload.GetType().Name).Distinct().OrderBy(n => n)));
    }

    private static void AwaitSettledDamage(GameEngine g, int seat, int expectedHp)
    {
        for (var i = 0; i < 120; i++)
        {
            if (g.CreateSnapshot(0).Players[seat].Hp == expectedHp) return;
            var step = g.Submit(new AdvanceOneStepCommand(g.Revision));
            if (!step.Accepted) break;
        }
        throw new InvalidOperationException("The Liushi slash damage never settled: seat " + seat + " hp " +
            g.CreateSnapshot(0).Players[seat].Hp + " events " +
            string.Join(",", g.Events.Select(e => e.Payload).OfType<CardGame.Core.DamageAppliedEvent>()
                .Select(d => d.TargetSeat + ":" + d.Amount)));
    }

    private static int HandLimit(GameEngine g, int seat) => (int)typeof(GameEngine)
        .GetMethod("GetHandLimit", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(g, [((IReadOnlyList<CharacterState>)typeof(GameEngine)
            .GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g)!)[seat]])!;

    private static int HandCount(GameEngine g, int seat) => g.State.Players[seat].HandCount;

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-cx", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:cao-xing");
            b.AddGeneral(new("fixture:cx", "曹性", "ol-cao-xing", Liushi, "qun", 4, [Zhanwan]));
            for (var i = 1; i < 4; i++)
                b.AddGeneral(new($"fixture:cx-{i}", "其他" + i, "supporter", "standard:none", "wei", 5, null));
            // A deck of heart nullifications: the human always holds a Liushi cost,
            // the AI never plays them, and no dodge ever answers the virtual slash.
            b.AddDeck(new("fixture:cx-deck", "固定", 4, 1, [])
            {
                PhysicalCards = Enumerable.Range(0, 64).Select(i =>
                    new ContentDeckPhysicalCard("standard:nullification", Suit.Heart, 5)).ToArray()
            });
            b.AddMode(new("identity:cx", "固定", 4, 4,
                new Dictionary<string, int> { { nameof(Role.Lord), 1 }, { nameof(Role.Loyalist), 1 }, { nameof(Role.Rebel), 2 } },
                "fixture:cx-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:cx", "fixture:cx-1", "fixture:cx-2", "fixture:cx-3"]));
        }
    }

    private static (GameEngine, ContentRegistry) Start()
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 41, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:cx",
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 12
        }, r);
        Accept(g, new StartGameCommand());
        Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:cx", g.Revision, P(g)!.PromptId));
        Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        return (g, r);
    }
}
