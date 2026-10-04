using System.Reflection;
using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

// Current OL Xun Chen: docs/content/sources/ol-xun-chen-2026-10-05.json.
internal static class OrdinaryXunChenChecks
{
    private const string Fenglve = "ol:fenglve";
    private const string Moushi = "ol:moushi";

    public static void Definitions()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage());
        var general = registry.Generals["ol:xun-chen"];
        Require(general.Name == "荀谌" && general.FactionId == "qun" && general.BaseHp == 3 &&
            general.SkillIds.SequenceEqual([Fenglve, Moushi]) &&
            general.VariantId == "ordinary" && general.RulesetId == "sanguosha-ol",
            "The OL Xun Chen general registers the current qun 3HP pair.");
        var fenglve = registry.GetSkill(Fenglve).Program!;
        var contest = fenglve.Triggers.Single();
        Require(contest.Window == SkillProgramTriggerWindow.PlayPhaseStarting && contest.Optional &&
            contest.Effects.Select(e => e.Op).SequenceEqual([
                SkillProgramEffectOp.SelectTarget, SkillProgramEffectOp.StartPindian,
                SkillProgramEffectOp.SelectAndMoveOwnedCard, SkillProgramEffectOp.SelectAndMoveOwnedCard,
                SkillProgramEffectOp.SelectAndMoveOwnedCard, SkillProgramEffectOp.SelectAndMoveOwnedCard,
                SkillProgramEffectOp.ChooseOption, SkillProgramEffectOp.GivePindianCard]) &&
            contest.Effects[1].ResultBind == "fenglve-contest" &&
            contest.Effects[2].Destination == SkillProgramCardDestination.OwnerHand &&
            contest.Effects[6].Op == SkillProgramEffectOp.ChooseOption &&
            contest.Effects[7].Condition.Kind == SkillProgramConditionKind.ChoiceIs,
            "Fenglve contests, takes one card per zone on a win, pays one card on a loss, and may gift its pindian card.");
        var moushi = registry.GetSkill(Moushi).Program!;
        var gift = moushi.Triggers.Single(t => t.Window == SkillProgramTriggerWindow.PlayPhaseStarting);
        Require(gift.Optional && gift.Condition.Kind == SkillProgramTriggerConditionKind.All &&
            gift.Effects.Select(e => e.Op).SequenceEqual([
                SkillProgramEffectOp.SetBooleanState, SkillProgramEffectOp.SelectTarget,
                SkillProgramEffectOp.SelectAndMoveOwnedCard, SkillProgramEffectOp.ChangeParticipantMarker]) &&
            gift.Effects[3].Marker == PlayerMarkerKind.MouShi && gift.Effects[3].Amount == 1,
            "Moushi gifts one hand card once per turn and marks the recipient.");
        var drain = moushi.Triggers.Single(t => t.Window == SkillProgramTriggerWindow.AfterDamageApplied);
        Require(!drain.Optional && drain.Subject == SkillProgramTriggerSubject.Any &&
            drain.Effects[0].Op == SkillProgramEffectOp.Draw && drain.Effects[0].Amount == 1 &&
            drain.Effects[1].Op == SkillProgramEffectOp.ChangeParticipantMarker &&
            drain.Effects[1].Amount == -1 &&
            drain.Effects[1].TargetReference!.Kind == ProgramParticipantRef.EventSource,
            "The marked player's first play-phase damage draws the owner one card and consumes the marker.");
        var expiry = moushi.Triggers.Single(t => t.Window == SkillProgramTriggerWindow.TurnEnding);
        Require(expiry.TurnOwnerScope == SkillProgramTurnOwnerScope.OtherLiving && !expiry.Optional &&
            expiry.Effects.Single().Op == SkillProgramEffectOp.ChangeParticipantMarker &&
            expiry.Effects.Single().Amount == -1,
            "The marker expires at the end of the marked player's turn.");
        Require(registry.GetSkill(Fenglve).ProgramPresentation!.Name == "锋略" &&
            registry.GetSkill(Moushi).ProgramPresentation!.Name == "谋识",
            "The presentation carries the current OL Fenglve/Moushi wording.");
    }

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

    public static void MoushiGiftMarksDrainsAndExpires()
    {
        var (g, r) = Start();
        // Decline Fenglve, answer Moushi's offer: gift one card to seat 1.
        for (var i = 0; i < 20; i++)
        {
            if (P(g) is { } offer && offer.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Moushi &&
                    c.Parameters.GetValueOrDefault("program-action") == "activate")) break;
            if (P(g) is { } fenglve && fenglve.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Fenglve &&
                    c.Parameters.GetValueOrDefault("program-action") == "activate"))
            {
                Skip(g);
                continue;
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        Activate(g);
        SelectTarget(g, 1);
        var entryMovements = g.CardMovements.Count;
        if (P(g) is { } pay && pay.Choices.Any(c => c.Cards.Count == 1))
            Answer(g, c => c.Cards.Count == 1);
        var gifted = g.CardMovements.Skip(entryMovements)
            .Any(m => m.To == CardLocation.Hand(1) && m.Reason.Value.Contains("ol:moushi", StringComparison.Ordinal));
        var marked = g.Events.Select(e => e.Payload).OfType<CardGame.Core.PlayerMarkerChangedEvent>()
            .Any(m => m.PlayerSeat == 1 && m.Marker == PlayerMarkerKind.MouShi && m.Delta == 1);
        Require(gifted && marked,
            "Moushi hands one card to the counterpart and marks them for their next turn.");
        // Their next turn: the AI plays a slash during the play phase, the drain
        // draws the owner one card and consumes the marker once.
        var entryHand = g.State.Players[0].HandCount;
        SettleToNextLordTurn(g);
        var drained = g.CardMovements.Skip(entryMovements)
            .Count(m => m.To == CardLocation.Hand(0) && m.Reason.Value.Contains("ol:moushi", StringComparison.Ordinal));
        var markerEvents = g.Events.Select(e => e.Payload).OfType<CardGame.Core.PlayerMarkerChangedEvent>()
            .Where(m => m.PlayerSeat == 1 && m.Marker == PlayerMarkerKind.MouShi).ToList();
        Require(drained == 1,
            "The marked player's play-phase damage draws Xun Chen exactly one card.");
        Require(markerEvents.Count(e => e.Delta == -1) == 1,
            "The drain consumes the marker exactly once and the expiry leaves nothing to remove.");
        Require(g.State.Players[0].HandCount >= entryHand,
            "The drain keeps the owner's hand from shrinking across the cycle.");
        Replay(g, r);
    }

    private static void SettleToNextLordTurn(GameEngine g)
    {
        for (var i = 0; i < 400; i++)
        {
            var lastTurn = g.Events.Select(e => e.Payload).OfType<CardGame.Core.TurnStartedEvent>().LastOrDefault();
            if (lastTurn is { } turn && turn.ActorSeat == 0 && turn.TurnNumber > 1 &&
                P(g) is { } next && next.Kind == DecisionKind.PlayCard && next.PlayerSeat == 0) return;
            if (P(g) is { } play && play.Kind == DecisionKind.PlayCard && play.PlayerSeat == 0)
            {
                g.Submit(new EndPlayPhaseCommand(0, g.Revision, play.PromptId));
                continue;
            }
            if (P(g) is { } skill && skill.SkillPrompt?.SkillId is Fenglve or Moushi &&
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
        throw new InvalidOperationException("The fixture never reached the lord's next play phase.");
    }

    private static void SelectTarget(GameEngine g, int seat)
    {
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target" &&
            c.Targets.SequenceEqual([seat])));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-target" &&
            c.Targets.SequenceEqual([seat]));
    }

    private static int HandLimit(GameEngine g, int seat) => (int)typeof(GameEngine)
        .GetMethod("GetHandLimit", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(g, [((IReadOnlyList<CharacterState>)typeof(GameEngine)
            .GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g)!)[seat]])!;

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-xc", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            try
            {
                typeof(StandardContentPackage).Assembly.GetType("CardGame.Content.Standard.OrdinaryXunChenContent")!
                    .GetMethod("Register", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(null, [b]);
            }
            catch (Exception diag)
            {
                Console.WriteLine("DIAG-INNER: " + (diag.InnerException?.ToString() ?? diag.ToString()));
                throw;
            }
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
