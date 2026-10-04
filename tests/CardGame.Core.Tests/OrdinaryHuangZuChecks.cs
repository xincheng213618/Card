using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

// Current OL Huang Zu: docs/content/sources/ol-huang-zu-2026-10-05.json.
internal static class OrdinaryHuangZuChecks
{
    private const string Wangong = "ol:wangong";
    private static readonly CardKind[] SlashKinds = [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash];

    public static void Definitions()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage());
        var general = registry.Generals["ol:huang-zu"];
        Require(general.Name == "黄祖" && general.FactionId == "qun" && general.BaseHp == 4 &&
            general.SkillIds.SequenceEqual([Wangong]) &&
            general.VariantId == "ordinary" && general.RulesetId == "sanguosha-ol",
            "The OL Huang Zu general registers the current qun 4HP single skill.");
        var wangong = registry.GetSkill(Wangong).Program!;
        Require(wangong.Modifiers.Count == 1 &&
            wangong.Modifiers[0].Query == SkillRuleQuery.SlashLimit &&
            wangong.Modifiers[0].Operation == SkillRuleOperation.Unlimited &&
            wangong.Modifiers[0].Condition.Kind == SkillProgramConditionKind.PreviousPlayCardIsBasic &&
            wangong.CardPolicies.Count == 4 &&
            wangong.CardPolicies.All(policy =>
                policy.Kind == SkillProgramCardPolicyKind.IgnoreSlashUseDistanceBySuit &&
                policy.InputSuit is { } suit && suit != Suit.None &&
                policy.CardKinds.Count == 3 && SlashKinds.All(kind => policy.CardKinds.Contains(kind)) &&
                policy.Condition.Kind == SkillProgramConditionKind.PreviousPlayCardIsBasic) &&
            wangong.CardPolicies.Select(policy => policy.InputSuit).Distinct().Count() == 4,
            "Wangong lifts the slash count and every suit's slash distance while the previous play was a basic card.");
        var trigger = wangong.Triggers.Single();
        Require(trigger.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized &&
            trigger.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor &&
            trigger.CardKinds.Count == 3 && SlashKinds.All(kind => trigger.CardKinds.Contains(kind)) &&
            trigger.Condition.Kind == SkillProgramTriggerConditionKind.PreviousPlayCardIsBasic &&
            trigger.Effects.Single().Op == SkillProgramEffectOp.AddCurrentTargetSlashDamage &&
            trigger.Effects.Single().Amount == 1 &&
            trigger.Effects.Single().FinalTargetComparison == ProgramFinalTargetComparison.Always,
            "Wangong adds one slash damage on the same condition.");
        var presentation = registry.GetSkill(Wangong).ProgramPresentation!;
        Require(presentation.Name == "挽弓" && presentation.Description.Contains("基本牌", StringComparison.Ordinal),
            "The presentation carries the current OL Wangong wording.");
    }

    public static void WangongUnlocksAfterEachBasicPlay()
    {
        var (g, r) = Start();
        Settle(g);
        var first = ReachSlash(g, 1);
        Require(!P(g)!.ValidTargetSeats.Contains(2),
            "Without a previous basic card the distance limit still applies.");
        var firstHp = g.CreateSnapshot(0).Players[1].Hp;
        Accept(g, new PlayCardCommand(0, first.CardId!.Value, first.TargetSeats, g.Revision, P(g)!.PromptId,
            first.PlayedCardKind, first.TargetCardId));
        AwaitSettledHp(g, 1, firstHp - 1);
        Settle(g);
        Require(g.CreateSnapshot(0).Players[1].Hp == firstHp - 1,
            "The opening slash deals its plain one damage.");
        var second = ReachSlash(g, 2);
        Require(P(g)!.ValidTargetSeats.Contains(2),
            "After a basic play the slash distance limit is lifted.");
        var secondHp = g.CreateSnapshot(0).Players[2].Hp;
        Accept(g, new PlayCardCommand(0, second.CardId!.Value, second.TargetSeats, g.Revision, P(g)!.PromptId,
            second.PlayedCardKind, second.TargetCardId));
        AwaitSettledHp(g, 2, secondHp - 2);
        Settle(g);
        Require(g.CreateSnapshot(0).Players[2].Hp == secondHp - 2,
            "The follow-up slash deals two damage at any distance.");
        Replay(g, r);
    }

    private static LegalAction ReachSlash(GameEngine g, int targetSeat)
    {
        for (var i = 0; i < 60; i++)
        {
            if (P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } &&
                g.GetHumanLegalActions().FirstOrDefault(a => a.Kind == LegalActionKind.Slash &&
                    a.TargetSeats.Contains(targetSeat)) is { } action)
                return action;
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The Huang Zu fixture never offered a slash action: targets " +
            System.Text.Json.JsonSerializer.Serialize(P(g)!.ValidTargetSeats) + " slashActions " +
            System.Text.Json.JsonSerializer.Serialize(g.GetHumanLegalActions().Where(a => a.Kind == LegalActionKind.Slash)
                .Select(a => a.TargetSeats.ToArray())));
    }

    private static void AwaitSettledHp(GameEngine g, int seat, int expectedHp)
    {
        for (var i = 0; i < 120; i++)
        {
            if (g.CreateSnapshot(0).Players[seat].Hp == expectedHp) return;
            var step = g.Submit(new AdvanceOneStepCommand(g.Revision));
            if (!step.Accepted) break;
        }
        throw new InvalidOperationException("The slash damage never settled: seat " + seat + " hp " +
            g.CreateSnapshot(0).Players[seat].Hp + " damages " +
            string.Join(",", g.Events.Select(e => e.Payload).OfType<CardGame.Core.DamageAppliedEvent>()
                .Select(d => d.TargetSeat + ":" + d.Amount)) + " receipts " +
            g.Events.Select(e => e.Payload).OfType<CardGame.Core.ProgramTargetSlashReceiptIssuedEvent>().Count());
    }

    private static SkillProgram Load(string id, string members) => SkillProgramCatalog.Load(
        "{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"" + id +
        "\",\"revision\":1," + members + "}]}",
        "{\"schemaVersion\":3,\"skills\":{\"" + id + "\":{\"name\":\"fixture\",\"description\":\"fixture\"}}}").Programs[id];

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-hz", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            try
            {
                typeof(StandardContentPackage).Assembly.GetType("CardGame.Content.Standard.OrdinaryHuangZuContent")!
                    .GetMethod("Register", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(null, [b]);
            }
            catch (Exception diag)
            {
                Console.WriteLine("DIAG-INNER: " + (diag.InnerException?.ToString() ?? diag.ToString()));
                throw;
            }
            b.AddGeneral(new("fixture:hz", "黄祖", "ol-huang-zu", Wangong, "qun", 4, null));
            for (var i = 1; i < 4; i++)
                b.AddGeneral(new($"fixture:hz-{i}", "其他" + i, "supporter", "standard:none", "wei", 5, null));
            b.AddDeck(new("fixture:hz-deck", "固定", 4, 1, [])
            {
                PhysicalCards = Enumerable.Range(0, 64).Select(i =>
                    new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 5)).ToArray()
            });
            b.AddMode(new("identity:hz", "固定", 4, 4,
                new Dictionary<string, int> { { nameof(Role.Lord), 1 }, { nameof(Role.Loyalist), 1 }, { nameof(Role.Rebel), 2 } },
                "fixture:hz-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:hz", "fixture:hz-1", "fixture:hz-2", "fixture:hz-3"]));
        }
    }

    private static (GameEngine, ContentRegistry) Start()
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 37, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:hz",
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, r);
        Accept(g, new StartGameCommand());
        Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:hz", g.Revision, P(g)!.PromptId));
        Settle(g);
        return (g, r);
    }
}
