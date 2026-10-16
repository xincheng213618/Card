using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
internal static class CaoYingChecks
{
    private const string Owner = "fixture:cao-ying-owner", Driver = "fixture:cao-ying-driver", Quiet = "fixture:cao-ying-quiet", Mode = "identity:classic-cao-ying";
    public static void GrantedCurrentJianxiongDrawChildThenClaimsNativeDamageCard()
    {
        var (g, registry) = Start(child: "linked"); Play(g);
        Slash(g, 1); Reach(g, () => P(g)?.SkillPrompt?.SkillId == "ol:lingren" && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate")); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, () => Stage(g, CaoYingStage.Target)); Answer(g, c => c.Targets.SequenceEqual([1])); Reach(g, () => Stage(g, CaoYingStage.Guess)); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "guess-1"); Play(g);
        Accept(g, new UseProgramSkillCommand(0, Driver, "wound-other", [], [1], g.Revision, P(g)!.PromptId)); Play(g);
        var action = g.GetHumanLegalActions().First(a => a.PlayedCardKind == CardKind.Duel && a.TargetSeats.SequenceEqual([1])); var material = action.CardId!.Value;
        Accept(g, new PlayCardCommand(0, material, action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == "ol:jianxiong" && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate")); var hand = V(g, 0).Hand.Count;
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == "fixture:cao-ying-linked-child" && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
        Need(V(g, 0).Hand.Count == hand + 1 && !V(g, 0).Hand.Any(c => c.Id == material), "Current717 draws before claim and pauses under its real draw movement child.");
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); Play(g);
        Need(V(g, 0).Hand.Count == hand + 2 && V(g, 0).Hand.Any(c => c.Id == material), "Current717 claims the original actual Duel material after resuming its single draw child."); _ = Cold(g, registry);
    }
    private static bool Stage(GameEngine g, CaoYingStage stage) => g.ResolutionStack.LastOrDefault() is ProgramSkillFrame { CaoYing: { } r } && r.Stage == stage && P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "cao-ying") == true;
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Need(bool b, string why) { if (!b) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected native Cao Ying command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Slash(GameEngine g, int target) { var a = g.GetHumanLegalActions().First(a => a.PlayedCardKind is null or CardKind.Slash && a.CardId is { } id && V(g, 0).Hand.Any(c => c.Id == id && c.Kind == CardKind.Slash) && a.TargetSeats.SequenceEqual([target])); Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId)); }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var i = 0; i < 220; i++)
        {
            if (done()) return; var p = P(g); Need(g.State.Winner == Winner.None, "Small Cao Ying fixture ended early.");
            if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "cao-ying") == true) Answer(g, _ => true);
            else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification }) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Small Cao Ying fixture exceeded its command bound.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var r = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(r), "Cold command replay preserves private guesses, full inspected hand, RNG and original use."); return r; }
    private static (GameEngine, ContentRegistry) Start(bool preventFirst = false, string child = "")
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(preventFirst, child)); var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = child == "death" ? Role.Rebel : Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); return (g, registry);
    }
    private sealed class Fixture(bool preventFirst, string child) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("cao-ying-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            string Embedded(string bundle, string suffix) { using var stream = typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.{bundle}.{suffix}")!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            foreach (var bundle in new[] { "ol-cao-ying", "classic-cao-pi" })
            {
                var real = SkillProgramCatalog.Load(Embedded(bundle, "rules.json"), Embedded(bundle, "presentation.json"));
                foreach (var (id, program) in real.Programs.Where(pair => bundle == "ol-cao-ying" || pair.Key == "classic:xingshang")) b.AddSkill(new(id, real.Presentations[id].Name, real.Presentations[id].Description) { Program = program, ProgramPresentation = real.Presentations[id] });
            }
            var rules = $$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Driver}}","revision":1,"triggers":[{"id":"quota","window":"playPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":4}]}]},{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}""";
            var rulesNode = JsonNode.Parse(rules)!; var driverNode = rulesNode["skills"]![0]!;
            driverNode["viewAs"] = JsonNode.Parse("""[{"id":"duel","inputKinds":["slash"],"inputSuits":[],"outputKind":"duel","forPlay":true,"forResponse":false,"singleCardTrickUse":true}]""");
            driverNode["activations"] = JsonNode.Parse("""[{"id":"wound-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]}]""");
            var catalog = SkillProgramCatalog.Load(rulesNode.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> { [Driver] = new { name = "驱动", description = "真实次数" }, [Quiet] = new { name = "静默", description = "跳过出牌" } } }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id], SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => id == Quiet ? 10000d : -10000d) });
            if (preventFirst)
            {
                var prevention = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:cao-ying-prevent","revision":1,"triggers":[{"id":"prevent-first","window":"beforeDamageApplied","subject":"damageTarget","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"preventCurrentDamage","target":"owner"}]}]}]}""", """{"schemaVersion":3,"skills":{"fixture:cao-ying-prevent":{"name":"真实防伤","description":"首次防伤"}}}""");
                b.AddSkill(new("fixture:cao-ying-prevent", "真实防伤", "首次防伤") { Program = prevention.Programs["fixture:cao-ying-prevent"], ProgramPresentation = prevention.Presentations["fixture:cao-ying-prevent"] });
            }
            if (child is "loss" or "death")
            {
                var effect = """{"op":"loseHp","target":"owner","amount":20}""";
                var watcher = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:cao-ying-child","revision":1,"triggers":[{"id":"paid-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.cao-ying.draw"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]},{{effect}}]}]}]}""", """{"schemaVersion":3,"skills":{"fixture:cao-ying-child":{"name":"真实子结算","description":"失源或死亡","optionLabels":{"continue":"继续"}}}}""");
                b.AddSkill(new("fixture:cao-ying-child", "真实子结算", "失源或死亡") { Program = watcher.Programs["fixture:cao-ying-child"], ProgramPresentation = watcher.Presentations["fixture:cao-ying-child"] });
                if (child == "loss")
                {
                    var loss = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:cao-ying-loss","revision":1,"triggers":[{"id":"loss-after-dying","window":"dyingEntering","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"priority":1000,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:lingren"],"sourceBind":"standard:none"}]}]}]}""", """{"schemaVersion":3,"skills":{"fixture:cao-ying-loss":{"name":"真实失源","description":"濒死回复后失去凌人"}}}""");
                    b.AddSkill(new("fixture:cao-ying-loss", "真实失源", "濒死回复后失去凌人") { Program = loss.Programs["fixture:cao-ying-loss"], ProgramPresentation = loss.Presentations["fixture:cao-ying-loss"] });
                }
            }
            if (child == "linked")
            {
                var watcher = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:cao-ying-linked-child","revision":1,"triggers":[{"id":"draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.ol:jianxiong.Draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}]}]}]}""", """{"schemaVersion":3,"skills":{"fixture:cao-ying-linked-child":{"name":"奸雄子结算","description":"真实摸牌先于伤害牌获得","optionLabels":{"continue":"继续"}}}}""");
                b.AddSkill(new("fixture:cao-ying-linked-child", "奸雄子结算", "真实摸牌先于伤害牌获得") { Program = watcher.Programs["fixture:cao-ying-linked-child"], ProgramPresentation = watcher.Presentations["fixture:cao-ying-linked-child"] });
            }
            var ownerSkills = new List<string> { "ol:lingren", "ol:fujian" }; if (child is "loss" or "death") ownerSkills.Add("fixture:cao-ying-child"); if (child == "loss") ownerSkills.Add("fixture:cao-ying-loss"); if (child == "linked") ownerSkills.Add("fixture:cao-ying-linked-child");
            b.AddGeneral(new(Owner, "曹婴驱动", "supporter", Driver, "wei", 12, ownerSkills)); var peers = Enumerable.Range(1, 3).Select(i => $"fixture:cao-ying-peer-{i}").ToArray(); foreach (var peer in peers) b.AddGeneral(new(peer, "静默角色", "supporter", Quiet, "wei", 12, preventFirst ? ["fixture:cao-ying-prevent"] : []));
            b.AddDeck(new("fixture:cao-ying-deck", "基本牌固定手牌", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Diamond, 7)).ToArray() });
            b.AddMode(new(Mode, "曹婴真实规则", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:cao-ying-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
