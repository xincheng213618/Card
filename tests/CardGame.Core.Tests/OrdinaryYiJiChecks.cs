using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

// Current OL Yi Ji: docs/content/sources/ol-yi-ji-2026-10-05.json.
internal static class OrdinaryYiJiChecks
{
    private const string Jijie = "ol:jijie";
    private const string Jiyuan = "ol:jiyuan";
    private const string DriverId = "fixture:yj-driver";
    private static bool hurtSelf;

    public static void Definitions()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage());
        var general = registry.Generals["ol:yi-ji"];
        Require(general.Name == "伊籍" && general.FactionId == "shu" && general.BaseHp == 3 &&
            general.SkillIds.SequenceEqual([Jijie, Jiyuan]) &&
            general.VariantId == "ordinary" && general.RulesetId == "sanguosha-ol",
            "The OL Yi Ji general registers the current shu 3HP pair.");
        var jijie = registry.GetSkill(Jijie).Program!;
        var activation = jijie.Activations.Single();
        Require(activation.UsesPerTurn == 1 && activation.TargetKind == SkillProgramTargetKind.AnyLiving &&
            activation.MinTargets == 1 && activation.MaxTargets == 1 &&
            activation.Effects.Single().Op == SkillProgramEffectOp.GiveDrawPileBottomCard &&
            activation.Effects.Single().Target == SkillProgramEffectTarget.SelectedTarget,
            "Jijie is a once-per-turn activation handing the deck bottom to the selected participant.");
        var jiyuan = registry.GetSkill(Jiyuan).Program!;
        var dying = jiyuan.Triggers.Single(t => t.Window == SkillProgramTriggerWindow.DyingEntering);
        var give = jiyuan.Triggers.Single(t => t.Window == SkillProgramTriggerWindow.CardsMoved);
        Require(dying.Subject == SkillProgramTriggerSubject.Any && dying.Optional &&
            dying.Effects.Select(e => e.Op).SequenceEqual(
                [SkillProgramEffectOp.SelectTarget, SkillProgramEffectOp.Draw]) &&
            give.MovementOccurrence == SkillProgramMovementOccurrence.PerOwnerSourceHandGain &&
            give.Optional && give.SourceZones.SequenceEqual([CardZoneKind.Hand]) &&
            give.Effects.Select(e => e.Op).SequenceEqual(
                [SkillProgramEffectOp.SelectTarget, SkillProgramEffectOp.Draw]),
            "Jiyuan observes every dying character and the owner's own hand gifts, drawing the recipient one card.");
        var presentation = registry.GetSkill(Jiyuan).ProgramPresentation!;
        Require(presentation.Name == "急援" && presentation.Description.Contains("濒死", StringComparison.Ordinal) &&
            registry.GetSkill(Jijie).ProgramPresentation!.Name == "机捷",
            "The presentation carries the current OL Jijie/Jiyuan wording.");
    }

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

    public static void JiyuanCoversDyingEntriesAndOwnGifts()
    {
        var (g, r) = Start();
        Settle(g);
        Driver(g, "hurt", [1]);
        var activate = Await(g, p => p.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("skill-id") == Jiyuan &&
            c.Parameters.GetValueOrDefault("program-action") == "activate"));
        Accept(g, new AnswerPromptCommand(0, activate.PromptId,
            activate.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "activate").Id, g.Revision));
        AnswerFirst(g, "select-target");
        Require(g.CardMovements.Any(m => m.From.Zone == CardZoneKind.DrawPile && m.To == CardLocation.Hand(1)),
            "An accepted Jiyuan draws the dying character one card.");
        Settle(g);
        Replay(g, r);
        var (g2, r2) = Start();
        Settle(g2);
        var recipientBefore = HandCount(g2, 1);
        Driver(g2, "give");
        var activate2 = Await(g2, p => p.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("skill-id") == Jiyuan &&
            c.Parameters.GetValueOrDefault("program-action") == "activate"), sweepDriverPrompts: true);
        Accept(g2, new AnswerPromptCommand(0, activate2.PromptId,
            activate2.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "activate").Id, g2.Revision));
        AnswerFirst(g2, "select-target");
        Settle(g2);
        Require(HandCount(g2, 1) == recipientBefore + 2 &&
            g2.CardMovements.Count(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(1)) == 1,
            "A completed own gift pays the recipient one extra drawn card exactly once.");
        Replay(g2, r2);
    }

    private static int HandCount(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat].HandCount;

    private static int ZoneCount(GameEngine g, CardZoneKind zone) =>
        g.CreateCardZoneDiagnostics().Count(c => c.Location.Zone == zone);

    private static PendingDecision Await(GameEngine g, Func<PendingDecision, bool> goal, bool sweepDriverPrompts = false)
    {
        for (var i = 0; i < 160; i++)
        {
            var current = P(g);
            if (current is { } p && goal(p)) return p;
            if (sweepDriverPrompts && current is { PlayerSeat: 0 } &&
                current.Choices.Any(c => c.Parameters.ContainsKey("program-action") &&
                    c.Parameters.GetValueOrDefault("skill-id") != Jiyuan))
            {
                Accept(g, new AnswerPromptCommand(0, current.PromptId, current.Choices[0].Id, g.Revision));
                continue;
            }
            var step = g.Submit(new AdvanceOneStepCommand(g.Revision));
            if (!step.Accepted) break;
        }
        throw new InvalidOperationException("The Yi Ji fixture never reached its target prompt: " +
            System.Text.Json.JsonSerializer.Serialize(P(g)));
    }

    private static void AnswerFirst(GameEngine g, string programAction)
    {
        var p = Await(g, pending => pending.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("program-action") == programAction));
        Accept(g, new AnswerPromptCommand(0, p.PromptId,
            p.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == programAction).Id, g.Revision));
    }

    private static void Driver(GameEngine g, string id, int[]? targets = null)
    {
        if (P(g) is null) Settle(g);
        var p = P(g) ?? throw new InvalidOperationException("No pending decision for the fixture driver.");
        Accept(g, new UseProgramSkillCommand(0, DriverId, id, [], targets ?? [], g.Revision, p.PromptId));
    }

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
            try
            {
                typeof(StandardContentPackage).Assembly.GetType("CardGame.Content.Standard.OrdinaryYiJiContent")!
                    .GetMethod("Register", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(null, [b]);
            }
            catch (Exception diag)
            {
                Console.WriteLine("DIAG-INNER: " + (diag.InnerException?.ToString() ?? diag.ToString()));
                throw;
            }
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
