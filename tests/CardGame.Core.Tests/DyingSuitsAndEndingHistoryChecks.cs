using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DyingSuitsAndEndingHistoryChecks
{
    private const string Chenqing = "ol:chenqing", Moshi = "ol:moshi", Driver = "fixture:dseh-driver";
    private const string Gain = "fixture:dseh-gain", Entry = "fixture:dseh-entry", Mode = "identity:classic-dying-suits-ending-history-fixture";

    public static void ChenqingOriginalDyingFourSuitsPrivateCostAndColdReplay()
    {
        var (g, registry) = Start(); Prepare(g, 2); EnterOriginal(g);
        var original = g.ResolutionStack.OfType<DyingFrame>().Single();
        Activate(g, Chenqing, "round-original-dying-suits"); Answer(g, c => c.Targets.SequenceEqual([2]));
        Reach(g, p => Action(p, "dying-suits") && p.PlayerSeat == 2);
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Chenqing);
        var draw = Events<DyingSuitsDrawIssuedEvent>(g).Single();
        Require(draw.ActualCount == 4 && g.State.Players[1] is { IsAlive: true, Hp: 0 } &&
            g.ResolutionStack.OfType<DyingFrame>().Single(f => f.Id == original.Id) == original,
            "Four actual draw entities retain the living original Dying token and every original cursor.");
        Require(g.CreateSnapshot(0).PendingDecision is null && g.CreateSnapshot(1).PendingDecision is null &&
            g.CreateSnapshot(3).PendingDecision is null && P(g)!.PlayerSeat == 2,
            "Only the actual recipient receives its private legal HE discard selection.");
        Cold(g, registry); Reject(g);
        var own = g.CreateSnapshot(2).Players[2];
        var ids = own.Hand.Concat(own.Equipment).Where(c => P(g)!.ValidCardIds.Contains(c.Id)).GroupBy(c => c.Suit)
            .Where(group => group.Key != Suit.None).Select(group => group.First().Id).Take(4).ToArray();
        Require(ids.Length == 4, "The fixed actual deck and preparation must offer all four legal suits.");
        foreach (var id in ids) { Answer(g, c => c.Cards.SequenceEqual([id])); if (P(g) is not null) Cold(g, registry); }
        ReachPlay(g);
        var paid = Events<DyingSuitsDiscardPaidEvent>(g).Single(); var issued = Events<DyingSuitsPeachIssuedEvent>(g).Single().Return;
        Require(paid.CardIds.SequenceEqual(ids) && paid.Suits.Distinct().Count() == 4 &&
            ids.All(id => g.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.DiscardPile &&
                m.Sequence > paid.Before && m.Sequence <= paid.After) == 1) && issued.ActorSeat == 2 && issued.VictimSeat == 1 && issued.DyingFrameId == original.Id &&
            Events<CardUseDeclaredEvent>(g).Count(e => e.ResolutionId == issued.CardUseFrameId && e.CardId == 0 && e.CardKind == CardKind.Peach && e.SourceSeat == 2) == 1 &&
            Events<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == issued.CardUseFrameId) == 1 &&
            Events<DyingSuitsPeachReturnedEvent>(g).Count(e => e.Return == issued) == 1 && g.State.Players[1] is { IsAlive: true, Hp: 1 },
            "Exactly four actual discard costs issue and finish one zero-material real Peach for the original victim.");
        Cold(g, registry);
    }

    private static (GameEngine, ContentRegistry) Start(bool duplicateSuits = false, string? nestedOp = null, bool ending = false, string? scenario = null)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(duplicateSuits, nestedOp, ending, scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 5 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(g, new SelectGeneralCommand(0, "fixture:dseh-owner", g.Revision, P(g)!.PromptId)); ReachPlay(g); return (g, registry);
    }
    private static void Prepare(GameEngine g, int seat) { UseDriver(g, "prepare", [seat]); ReachPlay(g); }
    private static void EnterOriginal(GameEngine g) { UseDriver(g, "dying", [1]); Reach(g, p => Activation(p, Chenqing, "round-original-dying-suits")); }
    private static void UseDriver(GameEngine g, string activation, IReadOnlyList<int> targets) => Accept(g,
        new UseProgramSkillCommand(0, Driver, activation, [], targets, g.Revision, P(g)!.PromptId));
    private static T[] Events<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Action(PendingDecision p, string name) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == name);
    private static bool Activation(PendingDecision p, string skill, string binding) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill && c.Parameters.GetValueOrDefault("binding-id") == binding);
    private static void Activate(GameEngine g, string skill, string binding) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill && c.Parameters.GetValueOrDefault("binding-id") == binding);
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void ReachPlay(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    { for (var step = 0; step < 240; step++) { var p = P(g); if (p is not null && predicate(p)) return; Advance(g); } throw new InvalidOperationException("Fixed real boundary not reached: " + JsonSerializer.Serialize(P(g))); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { Kind: DecisionKind.Nullification } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass")) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.FireAttackDiscard }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "fire-attack-discard");
        else if (p is { PlayerSeat: 0 } && Action(p, "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static void Reject(GameEngine g) { var before = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && State(g) == before, "An unpublished command leaves all actual frames, invoices and private projections unchanged."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static void Cold(GameEngine g, ContentRegistry r) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)), "Exact frame-owned costs, original cursors, issued events and all four private projections cold-restore identically.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool duplicateSuits, string? nestedOp, bool ending, string? scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-dying-suits-ending-history", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var nested = nestedOp is null ? "" : $$""",{"id":"{{Gain}}","revision":1,"triggers":[{"id":"actual-draw","window":"cardsGained","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.ol:chenqing.DrawThenDiscardSuitsForDyingPeach.draw"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-seen","options":[{"id":"continue"}]},{"op":"grantSkills","target":"owner","skillIds":["{{Entry}}"]},{"op":"{{nestedOp}}","target":"owner","amount":1}]}]},{"id":"{{Entry}}","revision":1,"triggers":[{"id":"actual-entry","window":"dyingEntering","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"entry-seen","options":[{"id":"continue"}]},{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":3,"clampToMaxHp":true}]}]}""";
            var rules = $$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Driver}}","revision":1,"activations":[{"id":"prepare","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":20}]},{"id":"dying","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},{"id":"multi","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"cardTargetCount","ruleOperation":"add","amount":1,"cardKinds":["slash"]},{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"attackRange","ruleOperation":"unlimited"}]}]},{"id":"fixture:dseh-quiet","revision":1,"triggers":[{"id":"quiet","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]}{{nested}}]}""";
            var names = new Dictionary<string, object> { [Driver] = new { name = "真实准备与濒死", description = "固定种子的实体付款夹具" }, ["fixture:dseh-quiet"] = new { name = "安静其他回合", description = "有限杀额度" } };
            if (nestedOp is not null) { names[Gain] = Observer("实际摸牌中濒死"); names[Entry] = Observer("嵌套原生濒死入口"); }
            var tree = JsonNode.Parse(rules)!;
            tree["skills"]![0]!["activations"]!.AsArray().Add(JsonNode.Parse("""{"id":"trim","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]}"""));
            var catalog = SkillProgramCatalog.Load(tree.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = 3, skills = names }));
            foreach (var pair in catalog.Programs) b.AddSkill(new(pair.Key, pair.Key, "真实共享能力夹具") { Program = pair.Value });
            foreach (var owner in new[] { false, true }) b.AddSkill(new(owner ? "fixture:dseh-owner-pick" : "fixture:dseh-peer-pick", "固定选将", "公开选将评分")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => (role == Role.Lord) == owner ? 10000d : -10000d) });
            b.AddGeneral(new("fixture:dseh-owner", "原始濒死与历史用牌", "supporter", "fixture:dseh-owner-pick", "wei", 6,
                scenario is "next" or "duel" ? [Chenqing, Moshi, Driver, "boundary:qiaoshui-current", "classic:tianbian"] : [Chenqing, Moshi, Driver]));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:dseh-peer-{i}", "真实其他角色", "supporter", "fixture:dseh-peer-pick", "wei", 8,
                nestedOp is null ? ["fixture:dseh-quiet"] : ["fixture:dseh-quiet", Gain]) { InitialHp = ending ? 8 : 1 });
            // Dying needs one real HP loss from HP1 in every variant.
            b.AddDeck(new("fixture:dseh-deck", "固定实体的四花色", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 96)
                .Select(i => new ContentDeckPhysicalCard(CardIdentity(i), scenario is not null ? Suit.Heart : duplicateSuits ? Suit.Club : (Suit)(i % 4), scenario is not null ? 7 : i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "原始濒死与结束历史", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:dseh-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:dseh-owner", "fixture:dseh-peer-1", "fixture:dseh-peer-2", "fixture:dseh-peer-3"]));
        }
        private string CardIdentity(int index)
        {
            // Seed31 consumes two role and three general shuffle draws before
            // CompleteSetup's 96-card shuffle. This fixed source-derived map is
            // recorded in fixture-layout.json; DrawPerTurn is zero.
            if (scenario is null) return index % 2 == 0 ? "standard:slash" : "standard:draw_two";
            var own = Array.IndexOf(OwnerInitialRecipeIndices, index);
            if (own < 0) return InitialRecipeIndices.Contains(index) ? "standard:draw_two" :
                scenario == "duel" ? "standard:nullification" : "standard:slash";
            return scenario switch
            {
                "fire" => own == 0 ? "standard:fire_attack" : "standard:slash",
                "duel" => own == 0 ? "standard:duel" : own == 1 ? "standard:nullification" : "standard:draw_two",
                "fangtian" => own == 0 ? "classic:fangtian-halberd" : "standard:slash",
                _ => "standard:slash"
            };
        }
        private static readonly int[] InitialRecipeIndices = [60, 23, 57, 71, 79, 39, 48, 85, 94, 73, 84, 65, 30, 88, 9, 12];
        private static readonly int[] OwnerInitialRecipeIndices = [60, 79, 94, 30];
        private static object Observer(string name) => new { name, description = "真实事件暂停与原始帧返回", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
    }
}
