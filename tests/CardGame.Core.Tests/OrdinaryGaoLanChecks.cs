using System.Reflection;
using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

// Current OL Gao Lan: docs/content/sources/ol-gao-lan-2026-10-06.json.
internal static class OrdinaryGaoLanChecks
{
    private const string Xizhen = "ol:xizhen";

    public static void Definitions()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage());
        var general = registry.Generals["ol:gao-lan"];
        Require(general.Name == "高览" && general.FactionId == "qun" && general.BaseHp == 4 &&
            general.SkillIds.SequenceEqual([Xizhen]) &&
            general.VariantId == "ordinary" && general.RulesetId == "sanguosha-ol",
            "The OL Gao Lan general registers the current qun 4HP skill.");
        var xizhen = registry.GetSkill(Xizhen).Program!.Triggers;
        var launch = xizhen.Single(t => t.Id == "xizhen-launch");
        Require(launch.Window == SkillProgramTriggerWindow.PlayPhaseStarting && launch.Optional &&
            launch.Effects.Select(e => e.Op).SequenceEqual([
                SkillProgramEffectOp.SelectTarget, SkillProgramEffectOp.ChooseOption,
                SkillProgramEffectOp.UseVirtualCard, SkillProgramEffectOp.StartVirtualDuel,
                SkillProgramEffectOp.ChangeParticipantMarker]) &&
            launch.Effects[1].Options.Select(o => o.Id).SequenceEqual(["slash", "duel"]) &&
            launch.Effects[2].TargetRestriction == SkillProgramCardTargetRestriction.NormalSlashTarget &&
            launch.Effects[2].Condition.Kind == SkillProgramConditionKind.ChoiceIs &&
            launch.Effects[3].Condition.Kind == SkillProgramConditionKind.ChoiceIs &&
            launch.Effects[4].Marker == PlayerMarkerKind.XiZhen && launch.Effects[4].Amount == 1,
            "Xizhen offers the counterpart a slash-or-duel virtual card and marks it afterwards.");
        var response = xizhen.Single(t => t.Id == "xizhen-response");
        Require(response.Window == SkillProgramTriggerWindow.CardResponseAccepted &&
            response.OwnerRelation == SkillProgramCardActionOwnerRelation.Observer && !response.Optional &&
            response.Effects.Single().Op == SkillProgramEffectOp.XiZhenResponseBenefit,
            "Responses to the owner's play-phase cards pay the XiZhen benefit.");
        var expiry = xizhen.Single(t => t.Id == "xizhen-expiry");
        Require(expiry.Window == SkillProgramTriggerWindow.TurnEnding &&
            expiry.TurnOwnerScope == SkillProgramTurnOwnerScope.OtherLiving && !expiry.Optional &&
            expiry.Effects[0].Marker == PlayerMarkerKind.XiZhen && expiry.Effects[0].Amount == -1 &&
            expiry.Effects[0].TargetReference!.Kind == ProgramParticipantRef.EventTarget,
            "The XiZhen marker expires at the holder's turn end.");
        Require(registry.GetSkill(Xizhen).ProgramPresentation!.Name == "袭阵",
            "The presentation carries the current OL Xizhen wording.");
    }

    public static void XizhenDuelDamagesAndMarks()
    {
        var (g, r) = Start(peace: true);
        EndMyPlayPhase(g);
        SettleToXizhenPrompt(g);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-target" &&
            c.Targets.SequenceEqual([1]));
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "duel"));
        var hpBefore = g.CreateSnapshot(0).Players[1].Hp;
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "duel");
        SettleToNextOwnPlayPhase(g);
        Require(g.CreateSnapshot(0).Players[1].Hp == hpBefore - 1,
            "The duel branch damages the counterpart. hp=" + g.CreateSnapshot(0).Players[1].Hp +
                " markers=" + string.Join(";", g.Events.Select(e => e.Payload).OfType<PlayerMarkerChangedEvent>()
                    .Where(m => m.Marker == PlayerMarkerKind.XiZhen)));
        Replay(g, r);
    }

    public static void XizhenSlashBenefitPaysAfterSettlement()
    {
        var (g, r) = Start(peace: false);
        EndMyPlayPhase(g);
        SettleToXizhenPrompt(g);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-target" &&
            c.Targets.SequenceEqual([1]));
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "slash"));
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "slash");
        SettleToNextOwnPlayPhase(g);
        var marked = g.Events.Select(e => e.Payload).OfType<PlayerMarkerChangedEvent>()
            .Where(m => m.Marker == PlayerMarkerKind.XiZhen && m.PlayerSeat == 1 && m.Delta == 1).ToList();
        Require(marked.Count == 1, "The counterpart carries the XiZhen marker after settlement. " +
            string.Join(";", marked));
        var mySlash = Hand(g, 0).First(c => c.Kind is CardKind.Slash);
        var handBefore = HandCount(g, 0);
        var hpBefore = g.CreateSnapshot(0).Players[1].Hp;
        var eventsBaseline = g.Events.Count;
        Accept(g, new PlayCardCommand(0, mySlash.Id, [1], g.Revision, P(g)!.PromptId));
        SettleToNextOwnPlayPhase(g);
        var draws = g.CardMovements.Count(m => m.To == CardLocation.Hand(0) &&
            m.Reason.Value.Contains("xizhen", StringComparison.Ordinal));
Require(draws == 2 && HandCount(g, 0) == handBefore - 1 + 2 &&
            g.CreateSnapshot(0).Players[1].Hp == hpBefore,
            "A response to the owner's card recovers the target and draws two. draws=" + draws +
                " hand=" + HandCount(g, 0) + " hp=" + g.CreateSnapshot(0).Players[1].Hp +
                " moves=" + string.Join(";", g.CardMovements.Where(m => m.To == CardLocation.Hand(0))
                    .Select(m => m.CardId + ":" + m.Reason.Value)));
        Replay(g, r);
    }

    private static IReadOnlyList<Card> Hand(GameEngine g, int seat) => ((CardZoneStore)typeof(GameEngine)
        .GetField("_cardZones", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g)!)
        .CardsAt(CardLocation.Hand(seat));

    private static int HandCount(GameEngine g, int seat) => Hand(g, seat).Count;

    private static void EndMyPlayPhase(GameEngine g)
    {
        Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0 ||
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
        var p = P(g)!;
        if (p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0)
            Accept(g, new EndPlayPhaseCommand(0, g.Revision, p.PromptId));
    }

    private static void SettleToXizhenPrompt(GameEngine g)
    {
        for (var i = 0; i < 500; i++)
        {
            if (P(g) is not { } p) { Accept(g, new AdvanceOneStepCommand(g.Revision)); continue; }
            if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Xizhen) &&
                p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") is "activate" or "skip"))
                return;
            if (p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0)
                Accept(g, new EndPlayPhaseCommand(0, g.Revision, p.PromptId));
            else if (p.Kind == DecisionKind.DiscardCards && p.PlayerSeat == 0)
                Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else if (p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "take-damage"))
                Answer(g, c => c.Parameters.GetValueOrDefault("response") == "take-damage");
            else
                Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixture never reached the Xizhen prompt: " + P(g)?.Prompt);
    }

    private static void SettleToNextOwnPlayPhase(GameEngine g)
    {
        for (var i = 0; i < 500; i++)
        {
            if (P(g) is not { } p) { Accept(g, new AdvanceOneStepCommand(g.Revision)); continue; }
            if (p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0) return;
            if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Xizhen) &&
                p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") is "activate" or "skip"))
            {
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                continue;
            }
            if (p.Kind == DecisionKind.DiscardCards && p.PlayerSeat == 0)
            {
                Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
                continue;
            }
            if (p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "take-damage"))
            {
                Answer(g, c => c.Parameters.GetValueOrDefault("response") == "take-damage");
                continue;
            }
            if (p.PlayerSeat == 0 && p.Choices.Count > 0 && p.Kind != DecisionKind.PlayCard)
            {
                Answer(g, _ => true);
                continue;
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixture never returned to the owner's play phase: " + P(g)?.Prompt);
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-gl", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b) => RegisterCore(b, peace: false);

        internal static void RegisterCore(IContentRegistryBuilder b, bool peace)
        {
            try
            {
                typeof(StandardContentPackage).Assembly.GetType("CardGame.Content.Standard.OrdinaryGaoLanContent")!
                    .GetMethod("Register", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(null, [b]);
            }
            catch (Exception diag)
            {
                Console.WriteLine("DIAG-INNER: " + (diag.InnerException?.ToString() ?? diag.ToString()));
                throw;
            }
            b.AddGeneral(new("fixture:gl", "高览", "ol-gao-lan", Xizhen, "qun", 4, null));
            for (var i = 1; i < 4; i++)
                b.AddGeneral(new($"fixture:gl-{i}", "fixture-peer-" + i, "supporter", "standard:none", "wei", 6, null));
            b.AddDeck(new("fixture:gl-deck", "fixed", 4, 1, [])
            {
                PhysicalCards = Enumerable.Range(0, 64).Select(i => i % 4 == 0
                    ? new ContentDeckPhysicalCard("standard:slash", Suit.Heart, 5)
                    : new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 6)).ToArray()
            });
            b.AddDeck(new("fixture:gl-deck-peace", "fixed-peace", 4, 1, [])
            {
                PhysicalCards = Enumerable.Range(0, 64).Select(_ =>
                    new ContentDeckPhysicalCard("standard:peach", Suit.Heart, 5)).ToArray()
            });
            b.AddMode(new("identity:gl", "fixed", 4, 4,
                new Dictionary<string, int> { { nameof(Role.Lord), 1 }, { nameof(Role.Loyalist), 1 }, { nameof(Role.Rebel), 2 } },
                "fixture:gl-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:gl", "fixture:gl-1", "fixture:gl-2", "fixture:gl-3"]));
            b.AddMode(new("identity:gl-peace", "fixed-peace", 4, 4,
                new Dictionary<string, int> { { nameof(Role.Lord), 1 }, { nameof(Role.Loyalist), 1 }, { nameof(Role.Rebel), 2 } },
                "fixture:gl-deck-peace", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:gl", "fixture:gl-1", "fixture:gl-2", "fixture:gl-3"]));
        }
    }

    private static (GameEngine, ContentRegistry) Start(bool peace)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new PeaceFixture(peace));
        var g = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = peace ? "identity:gl-peace" : "identity:gl",
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 12
        }, r);
        Accept(g, new StartGameCommand());
        Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:gl", g.Revision, P(g)!.PromptId));
        Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0 ||
            (p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Xizhen) &&
             p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") is "activate" or "skip")));
        if (P(g)!.Kind != DecisionKind.PlayCard)
            Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        return (g, r);
    }

    private sealed class PeaceFixture : IGameContentPackage
    {
        private readonly bool _peace;
        public PeaceFixture(bool peace) => _peace = peace;
        public PackageManifest Manifest { get; } = new("fixture-gl", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b) => Fixture.RegisterCore(b, _peace);
    }
}
