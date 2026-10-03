using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using static BoundaryLiDianChecks;

internal static class BoundaryXunYuChecks
{
    private const string Jieming = "boundary:jieming-current";
    private const string DriverId = "fixture:xun-driver";

    public static void RunCappedHandRefreshAndColdReplay()
    {
        var (g, registry) = Start();
        var maximum = Math.Min(5, g.State.Players[0].MaxHp);
        Driver(g, "hurt-two");
        for (var occurrence = 0; occurrence < 2; occurrence++)
        {
            Reach(g, p => p.SkillPrompt?.SkillId == Jieming && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
            ReplayFourViews(g, registry); Activate(g); Answer(g, c => c.Targets.SequenceEqual([0]));
            var f = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.CappedHandRefresh != null);
            Require(f.CappedHandRefresh is { Stage: ProgramCappedHandRefreshStage.Selecting } && f.CappedHandRefresh.Maximum == maximum,
                "Each actual damage point draws its entire frozen X before target-owned discard selection.");
            var draw = g.Events.Select(e => e.Payload).OfType<ProgramCappedHandRefreshDrawnEvent>().Last();
            Require(draw.DrawCount == maximum && draw.Maximum == maximum,
                "Jieming draws X even when the target's original hand already exceeds X.");
            var ids = f.CappedHandRefresh!.CandidateCardIds.ToArray();
            var prompt = P(g)!;
            for (var viewer = 0; viewer < 4; viewer++)
                Require((g.CreateSnapshot(viewer).PendingDecision is not null) == (viewer == 0),
                    "The target's hand-selection prompt and card choices remain private to that target.");
            var revision = g.Revision;
            Require(!g.Submit(new AnswerPromptCommand(0, prompt.PromptId, new ChoiceId("capped-hand.foreign"), revision)).Accepted && g.Revision == revision,
                "A foreign discard choice is rejected without paying or advancing.");
            DrainDiscard(g, registry);
            Require(g.State.Players[0].Hand.Count == maximum &&
                g.CardMovements.Where(m => ids.Contains(m.CardId) && m.Reason.Value == "program.capped-hand-refresh.discard")
                    .GroupBy(m => m.CardId).All(group => group.Count() == 1),
                "The target discards the frozen surplus exactly once and retains X cards.");
        }
        Settle(g);
        Require(g.Events.Select(e => e.Payload).OfType<ProgramCappedHandRefreshCompletedEvent>().Count(e => e.Completed) == 2 &&
            g.Events.Select(e => e.Payload).OfType<ProgramCappedHandRefreshDrawnEvent>().Count() == 2,
            "A two-point damage produces two complete refreshes, with no repeated paid draw.");
        ReplayFourViews(g, registry);

        var (nested, nr) = Start(gainChild: true);
        var frozen = Math.Min(5, nested.State.Players[0].MaxHp);
        Driver(nested, "hurt-one"); Activate(nested); Answer(nested, c => c.Targets.SequenceEqual([0]));
        Require(P(nested)?.SkillPrompt?.SkillId == "fixture:xun-gain" &&
            nested.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Jieming).CappedHandRefresh is
                { Stage: ProgramCappedHandRefreshStage.Drawing, Maximum: var x } && x == frozen,
            "The actual gain observer suspends the paid draw before discard candidates are frozen.");
        ReplayFourViews(nested, nr); Answer(nested, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(nested, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "capped-hand-discard"));
        Require(nested.State.Players[0].MaxHp < frozen && Frame(nested).CappedHandRefresh!.Maximum == frozen,
            "A gain child changing the target's maximum HP does not recompute already frozen X.");
        DrainDiscard(nested, nr); Settle(nested); ReplayFourViews(nested, nr);
        Require(nested.State.Players[0].Hand.Count == frozen, "The returned child discards to the original threshold.");

        var (dying, dr) = Start(gainChild: true, gainDying: true);
        while (dying.State.Players[0].Hp > 3) { Driver(dying, "trim"); Settle(dying); }
        Require(dying.State.Players[0].Hp == 3, "Real HP-loss commands prepare an exact three-HP source before damage.");
        Driver(dying, "hurt-one"); Activate(dying); Answer(dying, c => c.Targets.SequenceEqual([0]));
        Require(dying.State.Players[0].Hp == 2, "Actual damage leaves exactly two HP for the gain child's two-HP payment.");
        ReplayFourViews(dying, dr); Answer(dying, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(dying, p => p.SkillPrompt?.SkillId == "fixture:xun-source-pulse");
        var paid = dying.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Jieming);
        var gain = dying.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "fixture:xun-gain");
        var exactDying = dying.ResolutionStack.OfType<DyingFrame>().Single();
        Require(exactDying.ParentFrameId == gain.Id && exactDying.ResumesProgramSkill &&
            paid.CappedHandRefresh is { Stage: ProgramCappedHandRefreshStage.Drawing } &&
            dying.ResolutionStack.OfType<DamageTriggerWindowFrame>().Any(window => window.Id == paid.WindowContext!.ParentFrameId),
            "Real gain-child HP loss owns a dying response while the once-paid refresh and original damage ancestors remain suspended.");
        ReplayFourViews(dying, dr); Answer(dying, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(dying, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "capped-hand-discard"));
        DrainDiscard(dying, dr); Settle(dying); ReplayFourViews(dying, dr);
        Require(dying.State.Players[0].IsAlive && dying.State.Players[0].Hp == 3 &&
            dying.Events.Select(e => e.Payload).OfType<ProgramCappedHandRefreshDrawnEvent>().Count() == 1 &&
            dying.Events.Select(e => e.Payload).OfType<ProgramCappedHandRefreshCompletedEvent>().Single().Completed,
            "The actual generic dying response returns to the same paid refresh, without drawing again or replacing its target.");
    }

    public static void RunDeathHandRefreshAndColdReplay()
    {
        var (g, registry) = Start(gainChild: true, humanRole: Role.Renegade);
        Require(g.CreateSnapshot(0).Players[0].Role == Role.Renegade && g.State.Players[0].MaxHp == 3,
            "The fixed dead-owner fixture is the selected non-Lord source, so its death cannot decide the winner.");
        Driver(g, "die");
        ReachWithDecline(g, p => p.SkillPrompt?.SkillId == Jieming &&
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        Require(!g.State.Players[0].IsAlive && g.ResolutionStack.OfType<ProgramDeathTriggerWindowFrame>().Any(f => f.OwnerSeat == 0),
            "Actual death opens the dead owner's exact typed death-program opportunity.");
        ReplayFourViews(g, registry); Activate(g); Answer(g, c => c.Targets.SequenceEqual([1]));
        var f = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.SkillId == Jieming);
        Require(f.CappedHandRefresh is { Stage: ProgramCappedHandRefreshStage.Drawing, Maximum: 5 } &&
            f.PendingMovementContinuation?.SubjectSeat == 1 &&
            g.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.SkillId == "fixture:xun-gain"),
            "The dead exact owner permits a paid living-target draw capped at five through its real gain child.");
        ReplayFourViews(g, registry);
        for (var step = 0; step < 100 && g.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.SkillId == Jieming); step++)
        { Accept(g, new AdvanceOneStepCommand(g.Revision)); ReplayFourViews(g, registry); }
        var drawn = g.Events.Select(e => e.Payload).OfType<ProgramCappedHandRefreshDrawnEvent>().Single();
        var completed = g.Events.Select(e => e.Payload).OfType<ProgramCappedHandRefreshCompletedEvent>().Single();
        var target = g.CreateSnapshot(1).Players[1];
        var diagnostic = JsonSerializer.Serialize(new
        {
            drawn, completed,
            players = g.CreateSnapshot(0, revealAll: true).Players.Select(p => new
                { p.Seat, p.Role, p.GeneralId, p.Hp, p.MaxHp, p.IsAlive, p.HandCount }),
            targetViewerHandCount = target.Hand.Count,
            residualFrames = g.ResolutionStack.Select(frame => new
                { type = frame.GetType().Name, frame.Id })
        });
        Require(drawn.DrawCount == 5 && completed is { Completed: true, Maximum: 5 } &&
            g.State.Players[1].HandCount == 5 && target.Hand.Count == 5 &&
            !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.SkillId == Jieming),
            "The real dead-owner continuation pays once and finishes living-target discard after the child returns: " + diagnostic);
        ReplayFourViews(g, registry);
    }

    private static void DrainDiscard(GameEngine g, ContentRegistry registry)
    {
        for (var step = 0; step < 24 && P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "capped-hand-discard") == true; step++)
        { ReplayFourViews(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "capped-hand-discard"); }
    }
    private static void ReachWithDecline(GameEngine g, Func<PendingDecision, bool> goal)
    {
        for (var step = 0; step < 100; step++)
        {
            if (P(g) is { } p && goal(p)) return;
            if (P(g) is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 })
                Answer(g, c => c.Cards.Count == 0);
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The bounded real death did not reach its owner opportunity: " + P(g)?.Prompt);
    }
    private static void Driver(GameEngine g, string id) =>
        Accept(g, new UseProgramSkillCommand(0, DriverId, id, [], [], g.Revision, P(g)!.PromptId));
    private static (GameEngine, ContentRegistry) Start(bool gainChild = false, bool gainDying = false, Role humanRole = Role.Lord)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(gainChild, gainDying));
        var g = GameEngine.CreateStandard(new GameOptions
        { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = humanRole, ModeId = "identity:classic-xun-current",
          UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 5 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:xun", g.Revision, P(g)!.PromptId)); Settle(g);
        return (g, r);
    }
    private static SkillProgram Load(string id, string members)
    {
        var presentation = new Dictionary<string, object> { ["name"] = "fixture", ["description"] = "fixture" };
        if (members.Contains("\"chooseOption\"", StringComparison.Ordinal))
            presentation["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" };
        return SkillProgramCatalog.Load(
            "{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"" + id + "\",\"revision\":1," + members + "}]}",
            JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object> { [id] = presentation } })).Programs[id];
    }
    private static string Activation(string id, string op, int amount) => JsonSerializer.Serialize(new
    { id, usesPerTurn = (int?)null, minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving",
      effects = new[] { new { op, target = "owner", amount } } });
    private sealed class Fixture(bool gainChild, bool gainDying) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-xun-current", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            typeof(StandardContentPackage).Assembly.GetType("CardGame.Content.Standard.BoundaryXunYuContent")!
                .GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [b]);
            b.AddSkill(new(DriverId, "fixture", "fixture") { Program = Load(DriverId, "\"activations\":[" +
                Activation("hurt-one", "damage", 1) + "," + Activation("hurt-two", "damage", 2) + "," +
                Activation("trim", "loseHp", 1) + "," + Activation("die", "loseHp", 20) + "]") });
            b.AddSkill(new("fixture:xun-selection", "固定选将", "只固定 AI 选将，不参与运行规则")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            if (gainChild) b.AddSkill(new("fixture:xun-gain", "实际获得子窗", "实际获得子窗")
            { Program = Load("fixture:xun-gain", "\"triggers\":[{\"id\":\"child\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementReasons\":[\"program.capped-hand-refresh.draw\"],\"movementOccurrence\":\"perBatch\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"choice\",\"options\":[{\"id\":\"continue\"}]},{\"op\":\"" + (gainDying ? "loseHp" : "changeMaximumHp") + "\",\"target\":\"owner\",\"amount\":" + (gainDying ? 2 : -1) + "}]}]") });
            if (gainDying) b.AddSkill(new("fixture:xun-source-pulse", "SourcePulse", "实际通用濒死返回")
            { Program = Load("fixture:xun-source-pulse", "\"triggers\":[{\"id\":\"return\",\"window\":\"selfDyingResponse\",\"subject\":\"owner\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"continue\",\"options\":[{\"id\":\"continue\"}]},{\"op\":\"recoverTo\",\"target\":\"owner\",\"numberExpression\":\"integerConstant\",\"minimumValue\":3,\"clampToMaxHp\":true}]}]") });
            b.AddGeneral(new("fixture:xun", "荀彧", "boundary_xun_yu", "standard:none", "wei", 3,
                new[] { Jieming, DriverId }.Concat(gainChild ? ["fixture:xun-gain"] : Array.Empty<string>())
                    .Concat(gainDying ? ["fixture:xun-source-pulse"] : Array.Empty<string>()).ToArray()));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:xun-{i}", "其他" + i, "supporter", "fixture:xun-selection", "wei", 8,
                gainChild ? ["fixture:xun-gain"] : null));
            b.AddDeck(new("fixture:xun-deck", "固定", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new("identity:classic-xun-current", "固定", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 },
                "fixture:xun-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:xun", "fixture:xun-1", "fixture:xun-2", "fixture:xun-3"]));
        }
    }
}
