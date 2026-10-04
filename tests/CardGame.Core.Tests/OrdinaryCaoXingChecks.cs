using System.Reflection;
using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

// Current OL Cao Xing: docs/content/sources/ol-cao-xing-2026-10-05.json.
internal static class OrdinaryCaoXingChecks
{
    private const string Liushi = "ol:liu-shi";
    private const string Zhanwan = "ol:zhan-wan";

    public static void Definitions()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage());
        var general = registry.Generals["ol:cao-xing"];
        Require(general.Name == "曹性" && general.FactionId == "qun" && general.BaseHp == 4 &&
            general.SkillIds.SequenceEqual([Liushi, Zhanwan]) &&
            general.VariantId == "ordinary" && general.RulesetId == "sanguosha-ol",
            "The OL Cao Xing general registers the current qun 4HP pair.");
        var liushi = registry.GetSkill(Liushi).Program!;
        Require(liushi.Activations.Count == 0, "Liushi is a play-phase trigger pair rather than an activation.");
        var play = liushi.Triggers.Single(t => t.Window == SkillProgramTriggerWindow.PlayPhaseStarting);
        Require(play.Subject == SkillProgramTriggerSubject.Owner && play.Optional &&
            play.Effects.Select(e => e.Op).SequenceEqual(
                [SkillProgramEffectOp.SelectTarget, SkillProgramEffectOp.SelectAndMoveOwnedCard, SkillProgramEffectOp.UseVirtualCard]) &&
            play.Effects[0].TargetKind == SkillProgramTargetKind.OtherLivingVirtualSlashTarget &&
            play.Effects[1].Suits.SequenceEqual([Suit.Heart]) &&
            play.Effects[1].Destination == SkillProgramCardDestination.DrawPileTop &&
            play.Effects[1].Zones.SequenceEqual([CardZoneKind.Hand]) &&
            play.Effects[2].TargetRestriction == SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget,
            "Liushi pays one heart hand card onto the deck top for an unlimited-distance virtual slash.");
        var damage = liushi.Triggers.Single(t => t.Window == SkillProgramTriggerWindow.CardUseCompleted);
        Require(damage.Window == SkillProgramTriggerWindow.CardUseCompleted &&
            damage.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor &&
            damage.CardKinds.SequenceEqual([CardKind.Slash]) &&
            damage.Effects[0].Op == SkillProgramEffectOp.ChangeParticipantMarker &&
            damage.Effects[0].Marker == PlayerMarkerKind.LiuShi && damage.Effects[0].Amount == 1 &&
            damage.Effects[0].TargetReference!.Kind == ProgramParticipantRef.EventTarget &&
            damage.Effects[1].Op == SkillProgramEffectOp.AdjustPersistentHandLimit &&
            damage.Effects[1].Amount == -1 && damage.Effects[1].StateId == "liu-shi-limit",
            "A damaging Liushi slash marks the victim and lowers their persistent hand limit.");
        var zhanwan = registry.GetSkill(Zhanwan).Program!.Triggers.Single();
        Require(zhanwan.Window == SkillProgramTriggerWindow.DiscardPhaseEnded && !zhanwan.Optional &&
            zhanwan.TurnOwnerScope == SkillProgramTurnOwnerScope.OtherLiving &&
            zhanwan.Effects[0].Op == SkillProgramEffectOp.Draw &&
            zhanwan.Effects[0].NumberExpression == SkillProgramNumberExpression.TurnOwnerDiscardPhaseHandDiscardCount &&
            zhanwan.Effects[1].Op == SkillProgramEffectOp.ChangeParticipantMarker &&
            zhanwan.Effects[1].Marker == PlayerMarkerKind.LiuShi && zhanwan.Effects[1].Amount == -1 &&
            zhanwan.Effects[1].TargetReference!.Kind == ProgramParticipantRef.EventTarget &&
            zhanwan.Effects[2].Op == SkillProgramEffectOp.AdjustPersistentHandLimit &&
            zhanwan.Effects[2].Amount == 1 && zhanwan.Effects[2].StateId == "liu-shi-limit",
            "Zhanwan draws the discard count, clears the marker, and restores the hand limit.");
        Require(registry.GetSkill(Liushi).ProgramPresentation!.Name == "流矢" &&
            registry.GetSkill(Zhanwan).ProgramPresentation!.Name == "斩腕",
            "The presentation carries the current OL Liushi/Zhanwan wording.");
    }

    public static void LiushiDamagesAndZhanwanDrains()
    {
        var (g, r) = Start();
        if (Environment.GetEnvironmentVariable("CX_PROBE") is not null)
        {
            Skip(g);
            Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
            Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
            for (var i = 0; i < 40; i++)
            {
                Accept(g, new AdvanceOneStepCommand(g.Revision));
                Console.WriteLine("PROBE " + i + ": pending=" + P(g)?.PlayerSeat + ":" + P(g)?.Kind +
                    " turns=" + g.Events.Select(e => e.Payload).OfType<CardGame.Core.TurnStartedEvent>().Count() +
                    " phases=" + string.Join(",", g.Events.Select(e => e.Payload).OfType<CardGame.Core.PhaseChangedEvent>().Select(p => p.ActorSeat + ":" + p.Phase).ToArray().TakeLast(3)));
                if (P(g) is { } probe && probe.PlayerSeat != 0) break;
            }
            throw new InvalidOperationException("PROBE-STOP");
        }
        var cost = g.CreateSnapshot(0).Players[0].Hand.First(card => card.Suit == Suit.Heart).Id;
        var victimHandBefore = HandCount(g, 1);
        var victimHpBefore = g.CreateSnapshot(0).Players[1].Hp;
        Activate(g);
        SelectTarget(g, 1);
        Reach(g, p => p.SkillPrompt?.SkillId == Liushi && p.Choices.Any(c => c.Cards.Count == 1));
        Answer(g, c => c.Cards.SequenceEqual([cost]));
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

    private static void SelectTarget(GameEngine g, int seat)
    {
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target" &&
            c.Targets.SequenceEqual([seat])));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-target" &&
            c.Targets.SequenceEqual([seat]));
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
            try
            {
                typeof(StandardContentPackage).Assembly.GetType("CardGame.Content.Standard.OrdinaryCaoXingContent")!
                    .GetMethod("Register", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(null, [b]);
            }
            catch (Exception diag)
            {
                Console.WriteLine("DIAG-INNER: " + (diag.InnerException?.ToString() ?? diag.ToString()));
                throw;
            }
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
        Reach(g, p => p.SkillPrompt?.SkillId == Liushi &&
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        return (g, r);
    }
}
