using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class Bao2025Checks
{
    private const string Wuniang = "ol:wuniang", Zhennan = "ol:zhennan", Xushen = "ol:xushen";
    private const string Driver = "fixture:bao-driver", Quiet = "fixture:bao-quiet", Owner = "fixture:bao-owner", Mode = "identity:classic-bao";
    public static void ActualClaimedMaterialProvenanceAndNonBasicPoliciesCoexistCold()
    {
        var (g, registry) = Start("provenance"); Play(g); Use(g, "judge-other", [1]); Reach(g, () => Optional(P(g), "boundary:luoying"));
        Activate(g); Play(g); var claimed = E<ProgramDiscardedEntityClaimedEvent>(g).Single();
        Need(V(g, 0).Hand.Any(c => c.Id == claimed.CardId && c.Kind == CardKind.DrawTwo), "A genuine foreign Club judgment reaches its real discard disposition and is actually claimed into the owner's hand.");
        Use(g, "back-face", []); Play(g); Need(V(g, 0).IsFaceDown, "The same native entity's provenance policy is made active by a real face-state command.");
        var a = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.CardId == claimed.CardId && a.TargetSeats.SequenceEqual([2]) && a.ConversionSource?.SkillId == Wuniang);
        Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId)
            { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Action?.PhysicalCards.Any(c => c.CardId == claimed.CardId) == true);
        Need(use.ProvenanceUseSource is not null && use.IssuedNoResponse?.Source == use.ProvenanceUseSource &&
            E<ConvertedNonBasicSlashIssuedEvent>(g).Count(e => e.CardUseFrameId == use.Id && e.MaterialId == claimed.CardId && e.Source.SkillId == Wuniang) == 1 &&
            E<IssuedCardNoResponseEvent>(g).Count(e => e.Effect.ParentFrameId == use.Id) == 1,
            "The same real claimed non-basic material keeps its mature provenance invoice and the independent newly issued Wuniang invoice, without overwriting or duplicating either policy.");
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); CompleteGain(g, 2); Play(g);
        Need(E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1 && g.CardMovements.Count(m => m.CardId == claimed.CardId && m.Reason == CardMoveReasons.Use) == 1,
            "The combined policy and native converted use pay and return once through cold restore."); _ = Cold(g, registry);
    }
    private static void CompleteGain(GameEngine g, int target)
    {
        Reach(g, () => BaoStage(g, ProgramBao2025Stage.TakeTarget)); Answer(g, c => c.Targets.SequenceEqual([target]));
        Reach(g, () => BaoStage(g, ProgramBao2025Stage.TakeCard)); Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == "Hand");
        Reach(g, () => BaoStage(g, ProgramBao2025Stage.ResetChoice)); Answer(g, c => Branch(c, "decline"));
    }
    private static PlayerSnapshot V(GameEngine g, int s) => g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>();
    private static bool BaoStage(GameEngine g, ProgramBao2025Stage stage) => g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Bao2025?.Stage == stage) && P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "bao2025") == true;
    private static bool Optional(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool Branch(PromptChoice c, string branch) => c.Parameters.GetValueOrDefault("branch") == branch;
    private static void Need(bool value, string why) { if (!value) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected actual Bao2025 command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(pick).Id, g.Revision)); }
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Use(GameEngine g, string id, IReadOnlyList<int> targets) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets, g.Revision, P(g)!.PromptId));
    private static void Slash(GameEngine g, int target)
    {
        var a = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([target]) && a.ConversionSource?.SkillId == Wuniang);
        Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId)
            { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 160; step++)
        {
            if (done()) return; Need(g.State.Winner == Winner.None, "The fixed small native Bao fixture ended early."); var p = P(g);
            if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying or DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification }) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixed native Bao command bound was exceeded. " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack }));
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), g.CardMovements,
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(restored), "Cold serialized commands preserve every safe viewer, native use, private chooser and once-paid parent cursor."); return restored; }
    private static string Embedded(string bundle, string suffix)
    { using var stream = typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.{bundle}.{suffix}")!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
    private static (GameEngine, ContentRegistry) Start(string scenario = "normal")
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scenario.EndsWith("death", StringComparison.Ordinal) ? Role.Rebel : Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); return (g, registry);
    }
    private const string Watch = "fixture:bao-paid-watch", Loss = "fixture:bao-paid-loss", HpWatch = "fixture:bao-hp-watch";
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("bao2025-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            void Add(SkillProgramCatalog catalog, IReadOnlyList<string>? only = null)
            { foreach (var (id, program) in catalog.Programs.Where(x => only is null || only.Contains(x.Key))) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id], Tags = id is Zhennan or Xushen ? SkillTag.Limited : SkillTag.None,
                    SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => id == Quiet ? 10000d : -10000d) }); }
            foreach (var (bundle, id) in new[] { ("boundary-guan-yu", "boundary:wusheng"), ("classic-lifecycle-skills", "classic:dangxian"), ("classic-ma-su", "classic:zhiman") })
                Add(SkillProgramCatalog.Load(Embedded(bundle, "rules.json"), Embedded(bundle, "presentation.json")), [id]);
            Add(SkillProgramCatalog.Load(Embedded("ol-guan-suo", "rules.json"), Embedded("ol-guan-suo", "presentation.json")));
            if (scenario == "provenance") Add(SkillProgramCatalog.Load(Embedded("boundary-cao-zhi", "rules.json"), Embedded("boundary-cao-zhi", "presentation.json")));
            var baoRules = JsonNode.Parse(Embedded("ol-bao-san-niang", "rules.json"))!;
            if (scenario == "bad-reference") baoRules["skills"]![0]!["triggers"]![0]!["effects"]![0]!["sourceBind"] = Quiet;
            Add(SkillProgramCatalog.Load(baoRules.ToJsonString(), Embedded("ol-bao-san-niang", "presentation.json")));
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"{{Driver}}","revision":1,"activations":[{"id":"enter-dying","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":20}]},
                 {"id":"wound","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":2}]},
                 {"id":"heal","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true}]},
                 {"id":"draw-extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":4}]}]},
                {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """;
            var node = JsonNode.Parse(rules)!;
            if (scenario == "provenance")
            {
                node["skills"]![0]!["activations"]!.AsArray().Add(JsonNode.Parse("""
                    {"id":"judge-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
                     "effects":[{"op":"startJudgment","target":"selectedTarget","judgmentReason":"fixture.bao.provenance-judgment","resultBind":"judged","visibility":"public"},
                     {"op":"moveBoundCards","target":"owner","sourceBind":"judged","destination":"discardPile"}]}
                    """));
                node["skills"]![0]!["activations"]!.AsArray().Add(JsonNode.Parse("""
                    {"id":"back-face","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
                     "effects":[{"op":"setFaceState","target":"owner","faceDown":true}]}
                    """));
                node["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                    {"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-combined-use","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],"optional":false,
                     "effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}]}]}
                    """));
            }
            if (scenario is "fire" or "actor") node["skills"]!.AsArray().Add(JsonNode.Parse(scenario == "fire" ? $$"""
                {"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-fire-change","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash","fireSlash","thunderSlash"],"optional":false,
                "effects":[{"op":"offerCurrentSlashFireAndExtraTarget","target":"owner"}]}]}
                """ : $$"""
                {"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-source-replacement","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash"],"optional":false,
                "effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"replaceCurrentCardUseActor","target":"selectedTarget"}]}]}
                """));
            if (scenario == "quota") node["skills"]![1]!["triggers"]!.AsArray().Add(JsonNode.Parse("""
                {"id":"native-assault-nullify","window":"cardUseBeforeTargetEffects","ownerRelation":"target","cardKinds":["barbarianAssault"],"optional":false,"effects":[{"op":"nullifyCurrentCardEffect","target":"owner"}]}
                """));
            if (scenario.StartsWith("paid-", StringComparison.Ordinal))
            {
                var gain = scenario.Contains("gain", StringComparison.Ordinal);
                node["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                    {"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-paid-child","window":"{{(gain ? "cardsGained" : "cardsMoved")}}","subject":"owner",
                     {{(gain ? "\"destinationZones\":[\"hand\"]," : "\"sourceZones\":[\"hand\"],")}}"movementOccurrence":"perBatch",
                     "movementReasons":["{{(gain ? "skill-program.converted-nonbasic-slash.obtain" : "skill-program.chosen-assault.cost")}}"],"usageScope":"game","usageLimit":1,"optional":false,
                     "effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}
                    """));
                if (scenario.EndsWith("loss", StringComparison.Ordinal)) node["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                    {"id":"{{Loss}}","revision":1,"triggers":[{"id":"real-source-loss","window":"dyingEntering","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"priority":1000,
                     "effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true},
                     {"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{(gain ? Wuniang : Zhennan)}}"],"sourceBind":"standard:none"}]}]}
                    """));
            }
            if (scenario == "lion") node["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                {"id":"{{HpWatch}}","revision":1,"triggers":[{"id":"real-lion-hp-child","window":"afterHpRecovered","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}]}]}
                """));
            var labels = node["skills"]!.AsArray().ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "真实native owning子窗夹具",
                optionLabels = n.ToJsonString().Contains("chooseOption", StringComparison.Ordinal) ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() });
            Add(SkillProgramCatalog.Load(node.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels })));
            var skills = new List<string> { Wuniang, Zhennan, Xushen }; if (scenario.StartsWith("paid-", StringComparison.Ordinal)) skills.Add(Watch);
            if (scenario == "provenance") skills.AddRange(["boundary:luoying", "boundary:jiushi", Watch]);
            if (scenario is "fire" or "actor") skills.Add(Watch);
            if (scenario.EndsWith("loss", StringComparison.Ordinal)) skills.Add(Loss); if (scenario == "lion") skills.Add(HpWatch);
            b.AddGeneral(new(Owner, "真实鲍三娘技能拥有者", "supporter", Driver, "shu", 12, skills, GeneralGender.Female));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:bao-peer-{i}").ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "真实其他角色", "supporter", Quiet, "wei", 12));
            if (scenario == "lion") b.AddCard(new("fixture:bao-silver-lion", "白银狮子", "装备牌", "实际移除恢复子窗", CardKind.SilverLion));
            b.AddDeck(new("fixture:bao-deck", "固定非基本牌", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard(scenario == "lion" ? "fixture:bao-silver-lion" : "standard:draw_two", Suit.Club, 7)).ToArray() });
            b.AddMode(new(Mode, "真实鲍三娘native路径", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:bao-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
