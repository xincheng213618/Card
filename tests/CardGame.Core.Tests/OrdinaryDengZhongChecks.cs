using System.Reflection;
using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

// Current OL Deng Zhong: docs/content/sources/ol-deng-zhong-2026-10-05.json.
internal static class OrdinaryDengZhongChecks
{
    private const string Kanpo = "ol:kanpo";
    private const string Gengzhan = "ol:gengzhan";

    public static void Definitions()
    {
        ContentRegistry registry;
        try
        {
            registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
                new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage());
        }
        catch (Exception diag)
        {
            Console.WriteLine("DIAG-REGISTRY: " + (diag.InnerException?.ToString() ?? diag.ToString()));
            throw;
        }
        var general = registry.Generals["ol:deng-zhong"];
        Require(general.Name == "邓忠" && general.FactionId == "wei" && general.BaseHp == 4 &&
            general.SkillIds.SequenceEqual([Kanpo, Gengzhan]) &&
            general.VariantId == "ordinary" && general.RulesetId == "sanguosha-ol",
            "The OL Deng Zhong general registers the current wei 4HP pair.");
        var kanpo = registry.GetSkill(Kanpo).Program!;
        Require(kanpo.Activations.Count == 0, "Kanpo pairs its trigger with a play-flow view-as conversion.");
        var viewAs = kanpo.ViewAs.Single();
        Require(viewAs.InputCount == 1 && viewAs.SourceZones.SequenceEqual([CardZoneKind.Hand]) &&
            viewAs.OutputKind == CardKind.Slash && viewAs.ForPlay && !viewAs.ForResponse &&
            viewAs.UsesPerPhase == 1 && viewAs.UsageGroup == "kanpo-view-slash",
            "Kanpo turns one hand card into a normal slash once per play phase.");
        var take = kanpo.Triggers.Single();
        Require(take.Window == SkillProgramTriggerWindow.AfterDamageApplied && take.Optional &&
            take.DamageCardKinds.SequenceEqual([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) &&
            take.Effects[0].Op == SkillProgramEffectOp.SelectAndMoveOwnedCard &&
            take.Effects[0].SuitFrom == SkillProgramSuitSource.DamageCard &&
            take.Effects[0].CardOwnerRef!.Kind == ProgramParticipantRef.EventTarget &&
            take.Effects[0].Destination == SkillProgramCardDestination.OwnerHand,
            "Kanpo takes one event-target hand card matching the damaging slash suit.");
        var gengzhan = registry.GetSkill(Gengzhan).Program!.Triggers;
        var claim = gengzhan.Single(t => t.Id == "gengzhan-claim");
        Require(claim.Window == SkillProgramTriggerWindow.DiscardPileReceived && claim.Optional &&
            claim.UsageScope == SkillUsageScope.Turn && claim.UsageLimit == 1 &&
            claim.CardKinds.SequenceEqual([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) &&
            claim.Effects[0].Op == SkillProgramEffectOp.ClaimDiscardedEntityWithProvenance &&
            claim.Effects[1].Op == SkillProgramEffectOp.OfferFaceUpForOutsideClaims,
            "Gengzhan claims one play-phase discarded slash per turn.");
        var tally = gengzhan.Single(t => t.Id == "gengzhan-tally");
        Require(tally.Window == SkillProgramTriggerWindow.TurnEnding &&
            tally.TurnOwnerScope == SkillProgramTurnOwnerScope.OtherLiving && !tally.Optional &&
            tally.Effects[0].Op == SkillProgramEffectOp.ChangeParticipantMarker &&
            tally.Effects[0].Marker == PlayerMarkerKind.GengZhan && tally.Effects[0].Amount == 1 &&
            tally.Effects[0].TargetReference is null,
            "A slash-free other turn grants the owner one Gengzhan marker.");
        var release = gengzhan.Single(t => t.Id == "gengzhan-release");
        Require(release.Window == SkillProgramTriggerWindow.PlayPhaseStarting && !release.Optional &&
            release.Effects[0].Op == SkillProgramEffectOp.GrantTurnRuleModifier &&
            release.Effects[0].RuleQuery == SkillRuleQuery.SlashLimit &&
            release.Effects[0].AmountFromMarker == PlayerMarkerKind.GengZhan &&
            release.Effects[1].Op == SkillProgramEffectOp.ChangeParticipantMarker &&
            release.Effects[1].ClearMarker && release.Effects[1].Marker == PlayerMarkerKind.GengZhan,
            "The owner's play phase converts all Gengzhan markers into slash limit.");
        Require(registry.GetSkill(Kanpo).ProgramPresentation!.Name == "勘破" &&
            registry.GetSkill(Gengzhan).ProgramPresentation!.Name == "更战",
            "The presentation carries the current OL Kanpo/Gengzhan wording.");
    }

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

    public static void KanpoActivatesOncePerTurn()
    {
        var (g, r) = Start();
        var card = Hand(g, 0).First(c => c.Kind is CardKind.Slash);
        var hpBefore = g.CreateSnapshot(0).Players[1].Hp;
        var action = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash &&
            a.CardId == card.Id && a.ConversionSource?.SkillId == Kanpo && a.TargetSeats.Contains(1));
        Accept(g, new PlayCardCommand(0, card.Id, [1], g.Revision, P(g)!.PromptId, action.PlayedCardKind)
        {
            ConversionSource = action.ConversionSource
        });
        ReachProgramChoice(g);
        Require(g.CreateSnapshot(0).Players[1].Hp == hpBefore - 1,
            "The converted slash damages the selected target. hp=" + g.CreateSnapshot(0).Players[1].Hp);
        Skip(g);
        ReachPlay(g);
        Require(!g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash &&
                a.ConversionSource?.SkillId == Kanpo),
            "The once-per-turn conversion is not offered again in the same play phase.");
        Replay(g, r);
    }

    public static void GengzhanClaimsDiscardedSlash()
    {
        var (g, r) = Start();
        EndMyPlayPhase(g);
        // The seat-1 AI spends its play phase on its own; a pending claim prompt
        // proves the discarded entity matched the trigger's slash kind filter.
        ReachProgramChoice(g);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        ReachPlay(g);
        var claim = g.CardMovements.Last(m => m.To == CardLocation.Hand(0) &&
            m.Reason.Value.Contains("gengzhan", StringComparison.Ordinal));
        var claimedCard = Hand(g, 0).First(c => c.Id == claim.CardId);
        Require(claim.From == CardLocation.DiscardPile && claimedCard.Kind is CardKind.Slash,
            "Gengzhan claims the discarded slash into the owner's hand. moves=" +
                string.Join(";", g.CardMovements.Where(m => m.CardId == claim.CardId)
                    .Select(m => m.From.Zone + "->" + m.To.Zone + ":" + m.Reason.Value)));
        Replay(g, r);
    }

    public static void GengzhanTallyGrantsSlashLimit()
    {
        var (g, r) = Start(peace: true);
        EndMyPlayPhase(g);
        try
        {
            SettleToNextOwnPlayPhase(g);
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine("DIAG-TALLY events=" + string.Join(";", g.Events.Select(e => e.Payload).TakeLast(14)
                .Select(p => p.GetType().Name.Replace("Event", "") +
                    (p is PlayerMarkerChangedEvent m ? ":" + m.PlayerSeat + "/" + m.Marker + "/" + m.Delta : ""))));
            Console.WriteLine("DIAG-STACK " + string.Join(";", g.ResolutionStack.Select(f =>
                f.GetType().Name + "#" + f.Id + (f is ProgramSkillFrame ps ? ":" + ps.SkillId + "/" + ps.TriggerId + "/win=" + ps.WindowContext?.Window : ""))));
            throw;
        }
        var turns = g.Events.Select(e => e.Payload).OfType<TurnStartedEvent>().ToList();
        var slashUses = g.Events.Select(e => e.Payload).OfType<CardUsedEvent>()
            .Where(u => u.CardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)
            .Select(u => u.SourceSeat).ToHashSet();
        // Every AI turn that ended since my first turn began: the tally grants one
        // marker exactly when its owner used no slash, so derive the expectation.
        var myTurnIndex = turns.FindIndex(t => t.ActorSeat == 0);
        var settled = turns.Skip(myTurnIndex + 1).TakeWhile(t => t.ActorSeat != 0 || turns.IndexOf(t) > myTurnIndex)
            .Where(t => t.ActorSeat != 0).ToList();
        var expected = settled.Count(t => !slashUses.Contains(t.ActorSeat));
        Require(expected >= 1, "At least one slash-free AI turn exists in this seed. turns=" +
            string.Join(";", turns.Select(t => t.ActorSeat)) + " slashUsers=" + string.Join(";", slashUses));
        var markers = g.Events.Select(e => e.Payload).OfType<PlayerMarkerChangedEvent>()
            .Where(m => m.PlayerSeat == 0 && m.Marker == PlayerMarkerKind.GengZhan).ToList();
        Require(markers.Count(m => m.Delta > 0) == expected && markers.All(m => m.Delta == 1 || m.Delta == -expected),
            "Slash-free AI turns each granted one marker. expected=" + expected + " markers=" + string.Join(";", markers));
        var grant = g.Events.Select(e => e.Payload).OfType<TurnRuleModifierGrantedEvent>()
            .Single(e => e.Modifier.Query == SkillRuleQuery.SlashLimit);
        Require(grant.Modifier.Amount == expected && grant.Modifier.TurnSeat == 0,
            "The owner's play phase converts all markers into the slash limit. amount=" + grant.Modifier.Amount);
        // The peace deck denies every character a slash, so the tallies above prove
        // the per-turn accounting and the grant carries the accumulated marker count.
        Require(markers.Last(m => m.Delta < 0).Count == 0,
            "The release clears every Gengzhan marker. markers=" + string.Join(";", markers));
        Replay(g, r);
    }

    private static void SettleToNextOwnPlayPhase(GameEngine g)
    {
        for (var i = 0; i < 400; i++)
        {
            if (P(g) is not { } p) { Accept(g, new AdvanceOneStepCommand(g.Revision)); continue; }
            if (p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0) return;
            if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") is "activate" or "skip") &&
                (p.SkillPrompt?.SkillId is Kanpo or Gengzhan ||
                 p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") is Kanpo or Gengzhan)))
            {
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                continue;
            }
            if (p.Kind == DecisionKind.DiscardCards && p.PlayerSeat == 0)
            {
                Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
                continue;
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixture never returned to the owner's play phase: " + P(g)?.Prompt);
    }

    private static IReadOnlyList<Card> PileCards(GameEngine g) => ((CardZoneStore)typeof(GameEngine)
        .GetField("_cardZones", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g)!)
        .CardsAt(CardLocation.DrawPile);

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

    private static void EndMyPlayPhase(GameEngine g)
    {
        ReachPlay(g);
        var p = P(g)!;
        if (p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0)
            Accept(g, new EndPlayPhaseCommand(0, g.Revision, p.PromptId));
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-dz", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            RegisterCore(b, peace: false);
        }

        internal static void RegisterCore(IContentRegistryBuilder b, bool peace)
        {
            try
            {
                typeof(StandardContentPackage).Assembly.GetType("CardGame.Content.Standard.OrdinaryDengZhongContent")!
                    .GetMethod("Register", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(null, [b]);
            }
            catch (Exception diag)
            {
                Console.WriteLine("DIAG-INNER: " + (diag.InnerException?.ToString() ?? diag.ToString()));
                throw;
            }
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
