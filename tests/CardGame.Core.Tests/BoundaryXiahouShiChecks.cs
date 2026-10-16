using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

// Future command checks. No compiler, loader or check has run for this stage.
internal static class BoundaryXiahouShiChecks
{
    private const string Qiaoshi = "boundary:qiaoshi-current", Yanyu = "boundary:yanyu-current";
    private const string Driver = "fixture:xhs-driver", First = "fixture:xhs-first", Second = "fixture:xhs-second";
    private const string Hp = "fixture:xhs-hp", Cost = "fixture:xhs-cost", Reward = "fixture:xhs-reward";
    private const string SourceSuppression = "fixture:xhs-source-suppression", RecastSuppression = "fixture:xhs-recast-suppression";
    private const string Mode = "identity:classic-xiahou-shi-fixture";
    private const string FirstReason = "skill-program." + Qiaoshi + ".ending-pair.0", SecondReason = "skill-program." + Qiaoshi + ".ending-pair.1";

    public static void OwnAndExtraEndingKeepTwoSeparateDrawsAndColdChildren()
    {
        var (g, r) = Create(); Play(g); Use(g, "extra"); Play(g); End(g); Activate(g, Qiaoshi);
        Reach(g, p => p.SkillPrompt?.SkillId == First); var id = Pair(g).Id; var first = Pair(g).EndingPairDraw!;
        Require(first is { Cursor: 0, AwaitingMovement: true, CurrentActorSeat: 0, FirstDraw.ActualCount: 1, SecondDraw: null } &&
            first.RoundNumber == 1 && Draws(g, FirstReason).Length == 1 && Draws(g, SecondReason).Length == 0 &&
            F<RoundStartedEvent>(g).Length == 1, "An independent non-Classic package really opts into Round tracking; own Ending issues only the first of two same-seat real draws.");
        CheckMovement(g, id, FirstReason); Private(g); Reject(g); g = RestoreAfterCold(g, r); Continue(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        var firstChild = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == First);
        Require(Pair(g).Id == id && Pair(g).EndingPairDraw is { Cursor: 0, SecondDraw: null } &&
            g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(h => h.Change.TargetSeat == 0 && h.Change.ParentFrameId == firstChild.Id &&
                h.ResumeFrameId == firstChild.Id && h.Continuation == PostEventContinuation.Program),
            "The first actual gain observer's real Recover child completes before the second same-seat draw is issued.");
        g = RestoreAfterCold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Second);
        Require(Pair(g).Id == id && Pair(g).EndingPairDraw is { Cursor: 1, AwaitingMovement: true, FirstDraw.ActualCount: 1, SecondDraw.ActualCount: 1 } &&
            Draws(g, FirstReason).Length == 1 && Draws(g, SecondReason).Length == 1 && !F<EndingPairDrawComparedEvent>(g).Any(),
            "The exact second invoice retains the same Ending parent and waits for its distinct gain child before final comparison.");
        CheckMovement(g, id, SecondReason); g = RestoreAfterCold(g, r); Continue(g); Play(g);
        var compared = F<EndingPairDrawComparedEvent>(g).Single();
        Require(compared.ProgramFrameId == id && !compared.BlockedForRound && compared.OwnerHandCount == compared.CurrentActorHandCount &&
            F<TurnStartedEvent>(g).Take(2).Select(e => e.ActorSeat).SequenceEqual([0, 0]) && F<RoundStartedEvent>(g).Length == 1,
            "Both same-seat invoices finish once; the actual extra turn starts without advancing the true Round.");
        End(g); Activate(g, Qiaoshi); Reach(g, p => p.SkillPrompt?.SkillId == First); g = RestoreAfterCold(g, r); Continue(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Hp); g = RestoreAfterCold(g, r); Continue(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Second); g = RestoreAfterCold(g, r); Continue(g);
        Until(g, e => F<EndingPairDrawComparedEvent>(e).Length == 2);
        Require(F<EndingPairOneDrawIssuedEvent>(g).Length == 4 && F<EndingPairOneDrawIssuedEvent>(g).All(e => e.RecipientSeat == 0 && e.ActualCount == 1) &&
            F<EndingPairDrawComparedEvent>(g).All(e => !e.BlockedForRound && e.RoundNumber == 1),
            "The genuine extra Ending also performs two distinct same-seat Draw1 benefits, rather than being skipped or deduplicated."); Cold(g, r);
    }

    private static (GameEngine, ContentRegistry) Create(bool removeForeignSource = false, bool removeRecastSource = false)
    {
        // Register only the production bundle, not the full Classic general pool:
        // Round tracking must follow its capability rather than package presence.
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(removeForeignSource, removeRecastSource));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Require(P(g)!.ValidContentIds.Contains("fixture:xhs-owner"), "The fixed published candidates retain the actual owner.");
        Accept(g, new SelectGeneralCommand(0, "fixture:xhs-owner", g.Revision, P(g)!.PromptId)); return (g, r);
    }
    private static ProgramSkillFrame Pair(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.EndingPairDraw is not null);
    private static CardMovementRecord[] Draws(GameEngine g, string reason) => g.CardMovements.Where(m => m.Reason.Value == reason && m.From == CardLocation.DrawPile).ToArray();
    private static void CheckMovement(GameEngine g, long parent, string reason) => Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f =>
        f.Id == parent && f.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } pending && pending.SubjectSeat == f.OwnerSeat) &&
        g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(w =>
            w.Batch.ParentFrameId == parent && w.ResumeProgramFrameId is null && (w.Batch.AwaitingProgramFrameId is null || w.Batch.AwaitingProgramFrameId == parent) && w.Batch.Movements.Count == 1 &&
            w.Batch.Movements.Single().Reason.Value == reason && g.CardMovements.Contains(w.Batch.Movements.Single())), "The real single-card batch retains its exact waiting owner, original parent, frozen ledger entry and nullable awaiting identity without a second direct program return.");
    private static void Activate(GameEngine g, string skill) { Reach(g, p => p.SkillPrompt?.SkillId == skill && Action(p, "activate")); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); }
    private static void Use(GameEngine g, string id) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], [], g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static T[] F<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Action(PendingDecision p, string id) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == id);
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Continue(GameEngine g) { if (P(g)!.PlayerSeat == 0) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate) => Until(g, e => P(e) is { } p && predicate(p));
    private static void Until(GameEngine g, Func<GameEngine, bool> predicate)
    { for (var i = 0; i < 240; i++) { if (predicate(g)) return; Advance(g); } throw new InvalidOperationException("Fixed Xiahou Shi boundary absent: " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack, LastFacts = g.Events.TakeLast(4).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0 } && p.SkillPrompt?.SkillId is First or Second or Hp or Cost or Reward) Continue(g);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0 } && Action(p, "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack),
        Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine RestoreAfterCold(GameEngine g, ContentRegistry r) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(State(restored) == State(g), "Four private views, immutable prepared choices, exact receipts/parents, movement stamps and command history cold-restore before actual resumed commands."); return restored; }
    private static void Cold(GameEngine g, ContentRegistry r) => _ = RestoreAfterCold(g, r);
    private static void Private(GameEngine g) { var p = P(g)!; foreach (var s in Enumerable.Range(0, 4).Where(s => s != p.PlayerSeat)) Require(g.CreateSnapshot(s).PendingDecision is null, "Only the original chooser sees its private published choices.");
        Require(p.Choices is System.Collections.IList { IsReadOnly: true } && p.ValidCardIds is System.Collections.IList { IsReadOnly: true } && p.Choices.All(c => c.Cards is System.Collections.IList { IsReadOnly: true } && c.Targets is System.Collections.IList { IsReadOnly: true }), "Prepared outer and nested selections are frozen."); }
    private static void Reject(GameEngine g) { var old = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished"), g.Revision)).Accepted && State(g) == old, "An unpublished answer cannot pay or advance either draw cursor."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool removeForeignSource, bool removeRecastSource) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:boundary-xiahou-shi", "1.0.0", "当前OL真实命令草稿");
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rulesStream = assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.boundary-xiahou-shi.rules.json") ?? throw new InvalidOperationException("Missing production rules resource.");
            using var presentationStream = assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.boundary-xiahou-shi.presentation.json") ?? throw new InvalidOperationException("Missing production presentation resource.");
            using var rulesReader = new StreamReader(rulesStream); using var presentationReader = new StreamReader(presentationStream);
            var production = SkillProgramCatalog.Load(rulesReader.ReadToEnd(), presentationReader.ReadToEnd());
            foreach (var id in new[] { Qiaoshi, Yanyu }) b.AddSkill(new(id, id, "完整当前OL共享能力") { Program = production.Programs[id] });
            var rules = JsonNode.Parse("""
            {"skills":[
              {"id":"fixture:xhs-driver","revision":1,"activations":[
                {"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]},
                {"id":"regain","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"grantSkills","target":"owner","skillIds":["boundary:qiaoshi-current"]}]},
                {"id":"give","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardKinds":["slash","fireSlash","thunderSlash"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget","amount":1}]}]},
              {"id":"fixture:xhs-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"fixture:xhs-first","revision":1,"triggers":[{"id":"first","window":"cardsGained","subject":"owner","optional":false,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:qiaoshi-current.ending-pair.0"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]},{"op":"recover","target":"owner","amount":1}]}]},
              {"id":"fixture:xhs-second","revision":1,"triggers":[{"id":"second","window":"cardsGained","subject":"owner","optional":false,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:qiaoshi-current.ending-pair.1"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:xhs-hp","revision":1,"triggers":[{"id":"hp","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:xhs-cost","revision":1,"triggers":[{"id":"cost","window":"cardsMoved","subject":"owner","optional":false,"sourceZones":["hand"],"movementOccurrence":"perCard","movementReasons":["card.recast.discard"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:xhs-reward","revision":1,"triggers":[{"id":"reward","window":"cardsGained","subject":"owner","optional":false,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["card.recast.draw","skill-program.boundary:yanyu-current.Draw"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:xhs-source-suppression","revision":1,"activations":[{"id":"replace-original-source","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["boundary:qiaoshi-current","fixture:xhs-source-suppression"],"sourceBind":"fixture:xhs-noop"}]}]}
            ]}
            """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            if (removeForeignSource)
            {
                var trigger = rules["skills"]![2]!["triggers"]![0]!;
                trigger["usageScope"] = "game"; trigger["usageLimit"] = 1;
                trigger["condition"] = JsonNode.Parse("""{"kind":"not","children":[{"kind":"ownerIsTurnPlayer"}]}""");
                ((JsonArray)trigger["effects"]!).Add(JsonNode.Parse("""{"op":"pendExtraTurn","target":"owner"}"""));
                ((JsonArray)trigger["effects"]!).Add(JsonNode.Parse("""{"op":"grantSkills","target":"owner","skillIds":["fixture:xhs-source-suppression"]}"""));
            }
            if (removeRecastSource)
                ((JsonArray)rules["skills"]![5]!["triggers"]![0]!["effects"]!).Add(JsonNode.Parse("""{"op":"grantSkills","target":"owner","skillIds":["fixture:xhs-recast-suppression"]}"""));
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object> {
                [Driver] = new { name = "真实命令", description = "额外回合、真实给牌及重获技能" }, ["fixture:xhs-quiet"] = new { name = "安静回合", description = "真实跳过出牌" },
                [First] = Pause("第一份摸牌"), [Second] = Pause("第二份摸牌"), [Hp] = Pause("真实HP孩子"), [Cost] = Pause("真实重铸成本"), [Reward] = Pause("真实重铸及男性收益"),
                [SourceSuppression] = new { name = "原来源资格抑制及独立替换", description = "HP3抑制资格；后续独立主动技真实删除旧实例" } } }));
            foreach (var id in new[] { Driver, "fixture:xhs-quiet", First, Second, Hp, Cost, Reward, SourceSuppression }) b.AddSkill(new(id, id, "真实子程序")
                { Program = catalog.Programs[id], SuppressionRule = id == SourceSuppression ? new SkillSuppressionRule(3) : null });
            b.AddSkill(new(RecastSuppression, "重铸成本来源资格抑制", "HP2只抑制资格，不删除原技能实例") { SuppressionRule = new(2) });
            b.AddSkill(new("fixture:xhs-noop", "旧来源替换", "无运行能力"));
            b.AddSkill(new("fixture:xhs-selection", "固定选将", "无运行能力") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            b.AddGeneral(new("fixture:xhs-owner", "界夏侯氏机制", "supporter", Qiaoshi, "shu", 3, [Yanyu, Driver, First, Second, Hp, Cost, Reward], Gender: GeneralGender.Female) { InitialHp = 1 });
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:xhs-target-{i}", "固定男性", "supporter", "fixture:xhs-selection", "qun", 6,
                ["fixture:xhs-quiet", Second, Reward], Gender: GeneralGender.Male) { InitialHp = 5 });
            b.AddDeck(new("fixture:xhs-deck", "固定真实杀", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard("standard:slash", Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "当前OL樵拾燕语", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:xhs-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:xhs-owner", "fixture:xhs-target-1", "fixture:xhs-target-2", "fixture:xhs-target-3"]));
        }
        private static object Pause(string name) => new { name, description = "真实子结算继续", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
    }
}
