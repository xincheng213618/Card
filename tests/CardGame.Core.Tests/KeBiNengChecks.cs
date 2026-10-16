using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class KeBiNengChecks
{
    private const string Skill = "ol:pingduan", Owner = "fixture:kbn-owner", Driver = "fixture:kbn-driver", Quiet = "fixture:kbn-quiet",
        Watch = "fixture:kbn-watch", Health = "fixture:kbn-health", Before = "fixture:kbn-before", Conditional = "fixture:kbn-source", Grain = "fixture:kbn-grain",
        Swap = "fixture:kbn-swap", Mode = "identity:classic-ke-bi-neng-fixture";
    public static void OrderedItemsAndPerItemDrawsRecastEquipmentCold()
    {
        var (g, r) = Start(); Draw(g); Equip(g, CardKind.SilverLion); Use(g, "lose"); Play(g); var hp = V(g).Hp; var hand = V(g).HandCount;
        var lion = V(g).Equipment.Single(c => c.Kind == CardKind.SilverLion).Id; Begin(g); BasicBoundary(g);
        var prompt = P(g)!; var before = State(g);
        Need(prompt is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null) &&
            !g.Submit(new AnswerPromptCommand(1, prompt.PromptId, prompt.Choices[0].Id, g.Revision)).Accepted && State(g) == before,
            "The original recipient privately chooses its genuine legal basic actions; another seat cannot see or answer that choice.");
        g = Cold(g, r); ChooseBasic(g, a => a.Kind == LegalActionKind.Slash && a.CardId > 0 && a.ConversionSource is null); RewardBoundary(g, 1);
        Need(Current(g)!.CompletedItems == 1 && E<KeBiNengPingduanDrawPaidEvent>(g) is [{ Item: 1, RecastDraw: false, DrawCount: 1 }] && V(g).HandCount == hand,
            "The whole real Slash finishes before its separate first reward draw and before any recast is requested.");
        g = Cold(g, r); Continue(g); RecastBoundary(g); var recast = Current(g)!.Cards.First(c => c.From.Zone == CardZoneKind.Hand); var recastId = recast.CardId;
        Answer(g, c => c.Cards.SequenceEqual([recastId])); Reach(g, () => Current(g)?.Stage == KeBiNengPingduanStage.RecastDrawChildren && P(g)?.SkillPrompt?.SkillId == Watch);
        Need(E<CardRecastEvent>(g).Single().CardId == recastId && E<KeBiNengPingduanDrawPaidEvent>(g).Count(e => e.RecastDraw) == 1,
            "A real trick entity is recast, and its ordinary recast draw has its own native CardsGained child before the second reward.");
        g = Cold(g, r); Continue(g); RewardBoundary(g, 2);
        Need(E<KeBiNengPingduanDrawPaidEvent>(g).Count(e => e.Item == 2) == 2 && V(g).HandCount == hand + 1,
            "Recast's own draw and the completed item's extra draw are two separate actual native draws, rather than one combined final reward.");
        g = Cold(g, r); Continue(g); EquipmentBoundary(g); Answer(g, c => c.Cards.SequenceEqual([lion])); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health);
        Need(V(g).Hp == hp + 1 && V(g).Equipment.All(c => c.Id != lion) && V(g).Hand.Any(c => c.Id == lion) && Current(g)!.CompletedItems == 3,
            "The selected equipped SilverLion genuinely moves to the owner's hand and heals its former owner before its native HP child.");
        g = Cold(g, r); Continue(g); RewardBoundary(g, 3); g = Cold(g, r); Continue(g); Play(g);
        Need(E<KeBiNengPingduanCompletedEvent>(g) is [{ CompletedItems: 3 }] && E<KeBiNengPingduanDrawPaidEvent>(g).Length == 4 && V(g).HandCount == hand + 3 &&
            g.CardMovements.Count(m => m.CardId == lion && m.Reason.Value == "skill-program.ke-bi-neng.pingduan.equipment") == 1 && !g.GetHumanLegalActions().Any(a => a.SkillId == Skill),
            "All three items settle once, with exactly three rewards plus the recast draw, and consume only the accepted Play allowance."); _ = Cold(g, r);
    }
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static KeBiNengPingduanReceipt? Current(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.KeBiNengPingduan is not null)?.KeBiNengPingduan;
    private static void Need(bool yes, string why) { if (!yes) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected actual Ke Bi Neng command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick)
    { var p = P(g)!; Need(V(g, p.PlayerSeat).IsHuman, "Only the actual human answers a private request."); var c = p.Choices.FirstOrDefault(pick); Need(c is not null, "Missing exact Ke Bi Neng choice: " + Diagnostic(g)); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, g.Revision)); }
    private static void Continue(GameEngine g) { if (V(g, P(g)!.PlayerSeat).IsHuman) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void Begin(GameEngine g, int target = 0) => Accept(g, new UseProgramSkillCommand(0, Skill, "ordered-three-items", [], [target], g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, int[]? targets = null, int[]? cards = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Draw(GameEngine g) { Use(g, "draw"); Play(g); }
    private static void Equip(GameEngine g, CardKind kind)
    { var id = V(g).Hand.First(c => c.Kind == kind).Id; Use(g, "place", [0], [id]); Play(g); }
    private static void BasicBoundary(GameEngine g) => Reach(g, () => Current(g)?.Stage == KeBiNengPingduanStage.BasicChoice);
    private static void RecastBoundary(GameEngine g) => Reach(g, () => Current(g)?.Stage == KeBiNengPingduanStage.RecastChoice);
    private static void EquipmentBoundary(GameEngine g) => Reach(g, () => Current(g)?.Stage == KeBiNengPingduanStage.EquipmentChoice);
    private static void RewardBoundary(GameEngine g, int item) => Reach(g, () => Current(g)?.CompletedItems == item && P(g)?.SkillPrompt?.SkillId == Watch && Current(g)?.Stage != KeBiNengPingduanStage.RecastDrawChildren);
    private static void ChooseBasic(GameEngine g, Func<LegalAction, bool> predicate)
    { var r = Current(g)!; var index = Enumerable.Range(0, r.BasicActions.Count).FirstOrDefault(i => predicate(r.BasicActions[i]), -1); Need(index >= 0, "No expected real native basic: " + Diagnostic(g)); Answer(g, c => c.Parameters.GetValueOrDefault("option") == index.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 260; step++)
        {
            if (done()) return; Need(g.State.Winner == Winner.None, "Ke Bi Neng ended before its actual boundary: " + Diagnostic(g)); var p = P(g);
            if (p is not null && !V(g, p.PlayerSeat).IsHuman) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p is { Kind: DecisionKind.PlayCard }) End(g);
            else if (p is { Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification }) Answer(g, c => c.Cards.Count == 0);
            else if (p is { Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue") == true) Continue(g);
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "after-child") == true) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "after-child");
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("declaration-answer") == "pass") == true) Answer(g, c => c.Parameters.GetValueOrDefault("declaration-answer") == "pass");
            else if (p is not null) throw new InvalidOperationException("Unexpected Ke Bi Neng boundary: " + Diagnostic(g));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing bounded Ke Bi Neng boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.Winner, Prompt = P(g) is { } p ? new { p.PlayerSeat, p.Kind, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Select(c => c.Parameters) } : null,
        Receipt = Current(g)?.Stage, Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).GeneralId, V(g, s).Hp, V(g, s).HandCount, V(g, s).IsAlive }), Tail = g.Events.TakeLast(6).Select(e => e.Payload.ToString()) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(copy), "Cold replay retains every private native option, original actor and exact paid use/movement/reward child."); return copy; }
    private static (GameEngine, ContentRegistry) Start(string scenario = "plain")
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario)); var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scenario == "source-death" ? Role.Renegade : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, r);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ke-bi-neng", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:ke-bi-neng");
            if (scenario == "slot-cancel") CharacterFixtureContent.Register(b, "ol:wo-long-feng-chu");
            if (scenario == "furong-alcohol") CharacterFixtureContent.Register(b, "ol:fu-rong");
            var rules = JsonNode.Parse($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
             {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":100,"priority":0}],"activations":[
              {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
              {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
              {"id":"chain","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"owner","chained":true}]},
              {"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]},
              {"id":"gift","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget"}]},
              {"id":"trim","minCards":1,"maxCards":64,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"trimmed"},{"op":"moveBoundCards","target":"owner","sourceBind":"trimmed","destination":"discardPile","awaitMovementTriggers":true}]}]},
             {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{{Health}}}","revision":1,"triggers":[{"id":"native-health","window":"afterHpRecovered","subject":"owner","hpChangeOccurrence":"perEvent","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]}
            ]}
            """)!;
            var names = new Dictionary<string, object> { [Driver] = new { name = "真实准备", description = "仅原生指令" }, [Quiet] = new { name = "安静其他回合", description = "跳实际出牌" },
                [Health] = new { name = "真实回复子窗", description = "保留真实HP回复", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            void Add(string id, string body, string[]? options = null) { rules["skills"]!.AsArray().Add(JsonNode.Parse("{\"id\":\"" + id + "\",\"revision\":1," + body + "}")); names[id] = new { name = id, description = "原生边界", optionLabels = (options ?? []).ToDictionary(x => x, x => x) }; }
            var skills = new List<string> { Driver, Health };
            if (scenario == "furong-alcohol") skills.Add("ol:xiaosi");
            if (scenario == "slot-cancel")
            {
                skills.Add("ol:youlong"); Add(Watch, "\"triggers\":[{\"id\":\"actual-slot-child\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"equipment\"],\"movementReasons\":[\"equipment.slot-abolished\"],\"movementOccurrence\":\"perBatch\",\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"slot\",\"options\":[{\"id\":\"continue\"}]},{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"},{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":20}]}]", ["continue"]); skills.Add(Watch);
            }
            if (scenario == "paid-loss") { Add(Conditional, "\"hpSkillGrants\":[{\"id\":\"full-source\",\"skillId\":\"ol:pingduan\",\"minimumHp\":13}]"); skills.Add(Conditional); } else skills.Add(Skill);
            if (scenario is "plain" or "paid-loss" or "source-death" or "reward-damage")
            {
                var child = scenario == "paid-loss" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}" : scenario == "source-death" ? ",{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}" : scenario == "reward-damage" ? ",{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}" : "";
                var limit = scenario == "plain" ? "" : "\"usageScope\":\"game\",\"usageLimit\":1,";
                Add(Watch, "\"triggers\":[{\"id\":\"actual-draw-child\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill-program.ke-bi-neng.pingduan.reward\",\"card.recast.draw\"]," + limit + "\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"child\",\"options\":[{\"id\":\"continue\"}]}" + child + "]}]", ["continue"]); skills.Add(Watch);
            }
            if (scenario == "funded") { Add(Watch, "\"triggers\":[{\"id\":\"real-paid-draw\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementReasons\":[\"skill-program.draw-funded-distinct-basic.draw\"],\"movementOccurrence\":\"perBatch\",\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"cost\",\"options\":[{\"id\":\"continue\"}]}]}]", ["continue"]); skills.Add(Watch); }
            if (scenario == "ox") { Add(Grain, "\"triggers\":[{\"id\":\"native-grain\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"woodenOxGrain\"],\"movementReasons\":[\"equipment.wooden-ox.grain-discard\"],\"movementOccurrence\":\"perBatch\",\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"grain\",\"options\":[{\"id\":\"continue\"}]},{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}]}]", ["continue"]); skills.Add(Grain); }
            var driver = rules["skills"]![0]!;
            string? conversion = scenario switch
            {
                "converted" => """{"id":"converted-basic","inputKinds":["dodge"],"inputSuits":[],"outputKind":"fireSlash","forPlay":true,"forResponse":false,"extendedUse":true}""",
                "round" => """{"id":"round-basic","inputKinds":[],"inputSuits":[],"inputCount":0,"sourceZones":[],"outputKind":"slash","forPlay":true,"forResponse":false,"roundOnceCardlessBasic":{"usageGroup":"fixture-pingduan-round","usesPerRound":1}}""",
                "funded" => """{"id":"funded-basic","inputKinds":[],"inputSuits":[],"inputCount":0,"sourceZones":[],"outputKind":"slash","forPlay":true,"forResponse":false,"useOnly":true,"drawFundedDistinctBasic":{"methodLedgerId":"fixture:pingduan-funded"}}""",
                "chained" => """{"id":"release-basic","inputKinds":[],"inputSuits":[],"inputCount":0,"sourceZones":[],"outputKind":"slash","forPlay":true,"forResponse":false,"useOnly":true,"chainedStateCost":false}""", _ => null
            };
            if (conversion is not null) driver["viewAs"] = new JsonArray(JsonNode.Parse(conversion));
            if (scenario == "furong-alcohol") driver["viewAs"] = JsonNode.Parse("""[{"id":"alcohol-slash","inputKinds":["alcohol"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false}]""");
            if (scenario == "identity") driver["viewAs"] = JsonNode.Parse("""[{"id":"optional-trick","inputKinds":["dodge"],"inputSuits":[],"inputCount":1,"sourceZones":["hand"],"outputKind":"duel","forPlay":true,"forResponse":false,"singleCardTrickUse":true},{"id":"equipment-basic","inputKinds":["silverLion"],"inputSuits":[],"inputCount":1,"sourceZones":["hand","equipment"],"outputKind":"fireSlash","forPlay":true,"forResponse":false,"extendedUse":true}]""");
            if (scenario == "actor") { Add(Swap, "\"triggers\":[{\"id\":\"actual-actor\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"slash\"],\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLegalCurrentCardTarget\"},{\"op\":\"replaceCurrentCardUseActor\",\"target\":\"selectedTarget\"},{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"role\",\"options\":[{\"id\":\"continue\"}]}]}]", ["continue"]); skills.Add(Swap); }
            if (scenario == "reward-damage") { Add(Before, "\"triggers\":[{\"id\":\"native-damage\",\"window\":\"beforeDamageApplied\",\"subject\":\"damageTarget\",\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"before\",\"options\":[{\"id\":\"continue\"}]}]}]", ["continue"]); skills.Add(Before); }
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:kbn-pick-owner", "本人选将", "固定小夹具") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -100000000d) });
            b.AddGeneral(new(Owner, "真实轲比能", "ol-ke-bi-neng", "fixture:kbn-pick-owner", "qun", scenario == "source-death" ? 13 : 12, skills));
            var peers = Enumerable.Range(0, 3).Select(i => "fixture:kbn-peer-" + i).ToArray();
            foreach (var (peer, index) in peers.Select((peer, index) => (peer, index))) { var pick = "fixture:kbn-pick-" + index; b.AddSkill(new(pick, "其他角色", "固定公开次序") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 1000000d * (3 - index)) }); b.AddGeneral(new(peer, "真实其他角色", "supporter", pick, "wei", 12, scenario == "foreign-stale" && index == 0 ? [Skill] : [Quiet])); }
            b.AddCard(new("fixture:kbn-lion", "白银狮子", "装备牌", "真实离场回复", CardKind.SilverLion));
            b.AddCard(new("fixture:kbn-ox", "木牛流马", "装备牌", "真实粮移动", CardKind.WoodenOx));
            b.AddCard(new("fixture:kbn-spear", "丈八蛇矛", "装备牌", "真实两牌杀", CardKind.ZhangbaSerpentSpear));
            var ids = scenario is "no-basic" or "converted" or "round" or "funded" or "chained" ? new[] { "standard:dodge" } : scenario == "peach" ? ["standard:peach"] : scenario is "alcohol" or "foreign-alcohol" or "foreign-stale" or "furong-alcohol" ? ["standard:alcohol"] : scenario == "zhangba" ? new[] { "standard:dodge", "fixture:kbn-spear" } :
                scenario == "no-recast" ? ["standard:slash"] : scenario == "identity" ? ["standard:slash", "standard:dodge", "standard:lightning", "standard:duel", "fixture:kbn-lion"] : scenario == "slot-cancel" ? ["standard:slash", "standard:duel", "fixture:kbn-lion"] : scenario == "ox" ? ["standard:slash", "standard:duel", "fixture:kbn-ox"] :
                new[] { "standard:slash", "standard:peach", "standard:alcohol", "standard:lightning", "standard:duel", "fixture:kbn-lion" };
            b.AddDeck(new("fixture:kbn-deck", "固定实体小牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 120).Select(i => new ContentDeckPhysicalCard(ids[i % ids.Length], Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "轲比能真实原生小局", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:kbn-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
