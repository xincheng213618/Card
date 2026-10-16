using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class FirstSlashDuelBenefitChecks
{
    private const string Skill = "ol:fengpo", Driver = "fixture:first-diamond-driver", Quiet = "fixture:first-diamond-quiet";
    private const string Owner = "fixture:first-diamond-owner", Watch = "fixture:first-diamond-watch", Loss = "fixture:first-diamond-loss";
    private const string Extra = "fixture:first-diamond-extra", Mode = "identity:classic-first-diamond";
    private const string Fire = "fixture:first-diamond-fire";
    public static void ActualFirstSlashPrivateDrawAndCombinedOrdinal()
    {
        var (g, registry) = Start(); var target = 1; var hand = V(g, 0).Hand.Count; var hp = V(g, target).Hp;
        Native(g, CardKind.Slash, target); Reach(g, () => Optional(P(g), Skill)); Activate(g); Reach(g, () => Choice(g));
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.FirstSlashDuelBenefit is not null);
        var r = root.FirstSlashDuelBenefit!; var prompt = P(g)!;
        Need(r.FrozenDiamondCount == V(g, target).Hand.Count && r.FrozenDiamondCount > 0 &&
            prompt.Choices.All(c => c.Cards.Count == 0 && c.Targets.Count == 0 && c.Parameters.Keys.All(k => k is "program-action" or "frame-id" or "branch")) &&
            prompt.Choices.Select(c => c.Label).SequenceEqual(["摸X张牌", "令此牌伤害+X"]),
            "The private actual choice reveals neither an opponent entity nor the frozen suit count before selection.");
        Need(Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null), "Only the real chooser sees this pending choice.");
        var before = State(g); Need(!g.Submit(new AnswerPromptCommand(1, prompt.PromptId, prompt.Choices[0].Id, g.Revision)).Accepted && before == State(g),
            "A foreign chooser cannot settle or mutate the first-use benefit.");
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "draw"); Play(g);
        var issued = E<FirstSlashDuelBenefitIssuedEvent>(g).Single();
        Need(issued.Receipt.SelectedBranch == "draw" && issued.Receipt.DrawCount == r.FrozenDiamondCount &&
            V(g, 0).Hand.Count == hand - 1 + r.FrozenDiamondCount && V(g, target).Hp == hp - 1 &&
            g.CardMovements.Count(m => m.Reason.Value == "skill-program.first-slash-duel.draw") == r.FrozenDiamondCount,
            "The first actual Slash draws exactly the frozen Diamond count and keeps its original one damage.");
        Native(g, CardKind.Duel, 2); Play(g);
        Need(E<FirstSlashDuelBenefitIssuedEvent>(g).Count() == 1 && E<FirstSlashDuelUseRecordedEvent>(g).Select(e => e.Ordinal).SequenceEqual([1, 2]),
            "Slash and Duel share a declaration ordinal even when the next effective kind differs.");
        Need(E<CardActionAcceptedEvent>(g).Count(e => e.Action.ActionId == r.ActionId) == 1 &&
            E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == r.CardUseFrameId) == 1,
            "The benefit never recommits or finishes its native owning use twice.");
        _ = Cold(g, registry);
    }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>();
    private static bool Optional(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool Choice(GameEngine g) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "first-slash-duel-benefit") == true;
    private static void Need(bool condition, string why) { if (!condition) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected native first-use command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(pick).Id, g.Revision)); }
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Native(GameEngine g, CardKind kind, int target)
    {
        var hand = V(g, 0).Hand;
        var action = g.GetHumanLegalActions().First(a => a.CardId is { } id &&
            (a.PlayedCardKind == kind || a.PlayedCardKind is null && hand.Any(c => c.Id == id && c.Kind == kind)) &&
            a.TargetSeats.SequenceEqual([target]));
        Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind, action.TargetCardId)
            { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
    }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 180; step++)
        {
            if (done()) return; var p = P(g); Need(g.State.Winner == Winner.None, "The small first-use fixture ended before its expected boundary.");
            if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification })
                Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixed first-use fixture exceeded its small actual-command bound. " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack }));
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), g.CardMovements,
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(restored), "Cold command replay preserves every safe viewer, actual declaration, private choice, source and paid child cursor."); return restored; }
    private static string Embedded(string suffix)
    { using var stream = typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-ma-yun-lu." + suffix)!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
    private static (GameEngine, ContentRegistry) Start(string scenario = "slash", bool stopAtVirtual = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scenario.EndsWith("paid-death", StringComparison.Ordinal) ? Role.Rebel : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId));
        if (stopAtVirtual) Reach(g, () => Optional(P(g), Driver)); else Play(g); return (g, registry);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("first-slash-duel-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            void Add(SkillProgramCatalog catalog)
            { foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id], SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => id == Quiet ? 10000d : -10000d) }); }
            if (scenario != "baseline") Add(SkillProgramCatalog.Load(Embedded("rules.json"), Embedded("presentation.json")));
            var driver = $$"""
            {"id":"{{Driver}}","revision":1,"viewAs":[{"id":"duel","inputKinds":["slash"],"inputSuits":[],"outputKind":"duel","forPlay":true,"forResponse":false,"singleCardTrickUse":true}],
             "activations":[{"id":"clear-hand","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
               "effects":[{"op":"selectOwnedCards","target":"selectedTarget","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"clear"},
               {"op":"moveBoundCards","target":"owner","sourceBind":"clear","destination":"discardPile","awaitMovementTriggers":true}]},
               {"id":"wound-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
               "effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]},
               {"id":"chain-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
               "effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]}],
             "triggers":[{"id":"slash-quota","window":"playPhaseStarting","subject":"owner","optional":false,
               "effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":10}]}]}
            """;
            var defs = new List<string> { driver, $$"""
            {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            """ };
            if (scenario == "virtual" || scenario.StartsWith("legacy", StringComparison.Ordinal))
                defs[0] = JsonNode.Parse(driver)!.AsObject().ApplyVirtual(scenario == "virtual", scenario.StartsWith("legacy-normal", StringComparison.Ordinal)).ToJsonString();
            if (scenario == "extra") defs.Add($$"""
                {"id":"{{Extra}}","revision":1,"triggers":[{"id":"extra","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardCategories":["basic","instantTrick"],"singleActionInstance":true,"optional":true,"priority":100,
                "effects":[{"op":"addOneDistanceFreeCurrentUseTarget","target":"owner"}]}]}
                """);
            if (scenario == "committed-fire") defs.Add($$"""
                {"id":"{{Fire}}","revision":1,"triggers":[{"id":"actual-committed-fire","window":"cardUseCommitted","ownerRelation":"actor",
                "cardKinds":["slash","fireSlash","thunderSlash"],"optional":false,"effects":[{"op":"offerCurrentSlashFireAndExtraTarget","target":"owner"}]}]}
                """);
            if (scenario == "duel-loss") defs.Add($$"""
                {"id":"{{Loss}}","revision":1,"triggers":[{"id":"actual-loss","window":"cardUseBeforeTargetEffects","ownerRelation":"actor","cardKinds":["duel"],"optional":false,
                "effects":[{"op":"loseHp","target":"owner","amount":20}]},
                {"id":"actual-recover-and-remove","window":"dyingEntering","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,
                "effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true},
                {"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Skill}}"],"sourceBind":"standard:none"}]}]}
                """);
            if (scenario.EndsWith("paid-loss", StringComparison.Ordinal) || scenario.EndsWith("paid-death", StringComparison.Ordinal)) defs.Add($$"""
                {"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],
                 "movementOccurrence":"perBatch","movementReasons":["skill-program.first-slash-duel.draw"],"usageScope":"game","usageLimit":1,"optional":false,
                 "effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}
                """);
            if (scenario.EndsWith("paid-loss", StringComparison.Ordinal)) defs.Add($$"""
                {"id":"{{Loss}}","revision":1,"triggers":[{"id":"actual-loss","window":"dyingEntering","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,
                "effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true},
                {"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Skill}}"],"sourceBind":"standard:none"}]}]}
                """);
            var rules = JsonNode.Parse($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',', defs)}}]}""")!;
            var labels = rules["skills"]!.AsArray().ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "真实首用收益驱动",
                optionLabels = n.ToJsonString().Contains("chooseOption", StringComparison.Ordinal) ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() });
            Add(SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels })));
            var skills = new List<string>(); if (scenario != "baseline") skills.Add(Skill); if (scenario == "extra") skills.Add(Extra);
            if (scenario == "committed-fire") skills.Add(Fire);
            if (scenario.EndsWith("paid-loss", StringComparison.Ordinal) || scenario.EndsWith("paid-death", StringComparison.Ordinal)) skills.Add(Watch);
            if (scenario == "duel-loss" || scenario.EndsWith("paid-loss", StringComparison.Ordinal)) skills.Add(Loss);
            b.AddGeneral(new(Owner, "真实首用拥有者", "supporter", Driver, "shu", 12, skills));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:first-diamond-peer-{i}").ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "真实其他角色", "supporter", Quiet, "wei", 12));
            b.AddDeck(new("fixture:first-diamond-deck", "固定真实方片杀", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Diamond, 7)).ToArray() });
            b.AddMode(new(Mode, "真实首用方片收益", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:first-diamond-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
    private static JsonObject ApplyVirtual(this JsonObject n, bool nativeWindows, bool normalDistance)
    {
        var trigger = JsonNode.Parse("""
            {"id":"virtual","window":"playPhaseStarting","subject":"owner","turnOwnerScope":"own","usageScope":"game","usageLimit":1,"optional":true,
             "effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true}]}
            """)!;
        trigger["effects"]![1]!["useCardActionWindows"] = nativeWindows;
        if (normalDistance) trigger["effects"]![1]!["targetRestriction"] = "normalSlashTarget";
        n["triggers"]!.AsArray().Add(trigger); return n;
    }
}
