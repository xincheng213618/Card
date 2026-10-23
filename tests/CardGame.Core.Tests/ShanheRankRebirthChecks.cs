using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheRankRebirthChecks
{
    private const string Rebirth = ShanheGeneralRanks.RebirthSkillId, Driver = "fixture:shanhe-rank-driver";
    private const string Health = "fixture:shanhe-rank-health", Gain = "fixture:shanhe-rank-gain", Quiet = "fixture:shanhe-rank-quiet";
    private const string Mode = "identity:classic-shanhe-rank-fixture";

    public static void NativeOptionalRebirthRecoversThenGainsFiveWithoutClearingStateCold()
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new StandardShanheRankPrivilegePackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 3 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(g, new SelectGeneralCommand(0, "fixture:shanhe-rank-owner", g.Revision, P(g)!.PromptId)); Play(g);
        Use(g, "prepare"); Play(g); var original = V(g).Hand.Select(c => c.Id).ToArray();
        Use(g, "fall"); Reach(g, p => p.Kind == DecisionKind.RescueDying && p.PlayerSeat == 0);
        Require(P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "program-trigger" && c.Parameters.GetValueOrDefault("skill-id") == Rebirth),
            "The rank privilege appears as an optional ordinary self-dying program response.");
        g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("response") == "program-trigger" && c.Parameters.GetValueOrDefault("skill-id") == Rebirth);
        Reach(g, p => p.SkillPrompt?.SkillId == Health);
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Rebirth); var frameId = root.Id;
        Require(V(g) is { Hp: 5, IsChained: true, IsFaceDown: true } && V(g).Hand.Select(c => c.Id).SequenceEqual(original) &&
            F<SkillUsageConsumedEvent>(g).Count(e => e.SkillId == Rebirth && e.Scope == SkillUsageScope.Game) == 1 &&
            !g.CardMovements.Any(m => m.Reason.Value == "skill-program." + Rebirth + ".Draw"),
            "The single entitlement pays on the original generic-skill frame and genuine recovery finishes before drawing, retaining hand and face/chain state.");
        g = Cold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Gain);
        var drawn = g.CardMovements.Where(m => m.Reason.Value == "skill-program." + Rebirth + ".Draw").ToArray();
        Require(V(g).HandCount == original.Length + 5 && drawn.Length == 5 && original.All(id => V(g).Hand.Any(c => c.Id == id)) &&
            g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Rebirth) is { Id: var paidFrame, NativeDraw.RequestedCount: 5 } && paidFrame == frameId &&
            F<RecoveryAppliedEvent>(g).Length == 1,
            "Native Draw gains five new physical hand entities after recovery; it does not refill to a hand total of five or repeat the recovery.");
        g = Cold(g, r); Continue(g); Play(g);
        Require(F<ProgramBindingResolvedEvent>(g).Single(e => e.FrameId == frameId) is { Activated: true, Completed: true } &&
            F<SkillUsageConsumedEvent>(g).Count(e => e.SkillId == Rebirth) == 1 && V(g) is { Hp: 5, IsChained: true, IsFaceDown: true },
            "Paid recovery and gain children return once through their original dying and generic-skill parents.");
        g = Cold(g, r); Use(g, "fall");
        for (var step = 0; step < 100 && V(g).IsAlive; step++) Accept(g, new AdvanceOneStepCommand(g.Revision));
        Require(!V(g).IsAlive && F<PlayerDiedEvent>(g).Count(e => e.VictimSeat == 0) == 1 &&
            F<SkillUsageConsumedEvent>(g).Count(e => e.SkillId == Rebirth && e.Scope == SkillUsageScope.Game) == 1 &&
            F<ProgramBindingStartedEvent>(g).Count(e => e.SkillId == Rebirth) == 1 && F<RecoveryAppliedEvent>(g).Length == 1,
            "The second lethal HP loss causes actual death without a second rebirth activation, payment or recovery.");
        _ = Cold(g, r);
    }

    private static PlayerSnapshot V(GameEngine g) => g.CreateSnapshot(0).Players[0];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] F<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Use(GameEngine g, string activation) => Accept(g, new UseProgramSkillCommand(0, Driver, activation, [], [], g.Revision, P(g)!.PromptId));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Play(GameEngine g) => Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    {
        for (var i = 0; i < 100; i++)
        {
            var p = P(g); if (p is not null && predicate(p)) return;
            if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue") == true) Continue(g);
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Rank rebirth boundary absent: " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack }));
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        string State(GameEngine engine) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(engine.CreateSnapshot(s))).ToArray(),
            Frames = JsonSerializer.Serialize(engine.ResolutionStack), Events = engine.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), engine.CardMovements,
            Commands = CommandJson.Serialize(engine.AcceptedCommands) });
        Require(State(g) == State(restored), "The actual generic skill, paid recovery/draw children, prepared views and command prefix cold-restore identically."); return restored;
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real rank command."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-rank-rebirth", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = JsonNode.Parse("""
                {"schemaVersion":0,"skills":[
                  {"id":"fixture:shanhe-rank-driver","revision":1,"activations":[
                    {"id":"prepare","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"owner","chained":true},{"op":"setFaceState","target":"owner","faceDown":true}]},
                    {"id":"fall","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":10}]}]},
                  {"id":"fixture:shanhe-rank-health","revision":1,"triggers":[{"id":"paid","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:shanhe-rank-gain","revision":1,"triggers":[{"id":"paid","window":"cardsGained","subject":"owner","optional":false,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.shanhe:rank-five-rebirth.Draw"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:shanhe-rank-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
                ]}
                """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            var presentation = JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                { [Driver] = new { name = "真实濒死", description = "固定命令" }, [Quiet] = new { name = "安静回合", description = "跳过出牌" },
                  [Health] = new { name = "真实回复", description = "回复孩子暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                  [Gain] = new { name = "真实得牌", description = "得牌孩子暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), presentation);
            foreach (var id in new[] { Driver, Health, Gain, Quiet }) b.AddSkill(new(id, id, "真实程序夹具") { Program = catalog.Programs[id] });
            b.AddGeneral(new("fixture:shanhe-rank-owner", "五阶重生", "supporter", Rebirth, "qun", 4, [Driver, Health, Gain]));
            for (var seat = 1; seat < 4; seat++) b.AddGeneral(new($"fixture:shanhe-rank-other-{seat}", "安静目标", "supporter", Quiet, "qun", 4));
            b.AddDeck(new("fixture:shanhe-rank-deck", "固定实体", 4, 0,
                [new ContentDeckCardCount("standard:dismantlement", 36)]));
            b.AddMode(new(Mode, "五阶重生真实响应", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:shanhe-rank-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:shanhe-rank-owner", "fixture:shanhe-rank-other-1", "fixture:shanhe-rank-other-2", "fixture:shanhe-rank-other-3"]));
        }
    }
}
