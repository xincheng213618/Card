using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DingYuanChecks
{
    private const string Driver = "fixture:dingyuan-driver", Cost = "fixture:dingyuan-cost";
    private const string Gain = "fixture:dingyuan-gain", Before = "fixture:dingyuan-before";
    private const string Mode = "identity:classic-dingyuan-fixture";
    private const string TransferReason = "skill-program.ol:cixiao.SelectAndMoveOwnedCard";

    public static void RelationshipTransferPaysOnceAndMarkerGrantSurvivesCixiaoLoss()
    {
        var (game, registry) = Create("relationship");
        Reach(game, p => IsTrigger(p, "ol:cixiao", "appoint-child")); Activate(game);
        Reach(game, p => IsTarget(p, "AdoptedChildSuccessor"));
        Require(P(game)!.Choices.All(c => !c.Targets.Contains(0)), "The initial appointment excludes its owner.");
        Target(game, 1); ReachPlay(game);
        Require(Mark(game, 1) == 1 && HasSkill(game, 1, "ol:panshi") && E<AdoptedChildAssignedEvent>(game) is [{ PreviousHolder: null, HolderSeat: 1 }],
            "A real preparation appointment grants one global marker and its derived Panshi source.");
        Use(game, "extra"); ReachPlay(game); End(game);
        Reach(game, p => IsTrigger(p, "ol:cixiao", "transfer-child")); Activate(game);
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card"));
        var paidCard = P(game)!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
        Answer(game, c => c.Cards.SequenceEqual([paidCard])); Reach(game, p => p.SkillPrompt?.SkillId == Cost);
        Require(game.CardMovements.Count(m => m.CardId == paidCard && m.Reason.Value == TransferReason && m.To == CardLocation.DiscardPile) == 1 &&
            game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "ol:cixiao").AdoptedChildRelationship is { Transfer: true, PreviousHolder: 1 },
            "The transfer payment suspends on the original Cixiao frame with its original holder frozen.");
        game = Cold(game, registry); Continue(game); Reach(game, p => IsTarget(p, "AdoptedChildSuccessor"));
        Require(P(game)!.Choices.Any(c => c.Targets.SequenceEqual([0])) && P(game)!.Choices.All(c => !c.Targets.Contains(1)),
            "Transfer permits the owner and excludes the previous marker holder.");
        Target(game, 0); ReachPlay(game);
        Require(Mark(game, 0) == 1 && Mark(game, 1) == 0 && HasSkill(game, 0, "ol:panshi") &&
            !HasSkill(game, 1, "ol:panshi") && game.CardMovements.Count(m => m.CardId == paidCard && m.Reason.Value == TransferReason) == 1,
            "Returning from a cold paid movement transfers exactly one marker and cannot charge the card again.");
        Use(game, "lose-cixiao"); ReachPlay(game);
        Require(!HasSkill(game, 0, "ol:cixiao") && Mark(game, 0) == 1 && HasSkill(game, 0, "ol:panshi"),
            "Losing the original Cixiao skill leaves the adopted-child marker and its Panshi grant intact.");
        Cold(game, registry);
    }

    private static T[] E<T>(GameEngine game) where T : IGameEvent => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static int Mark(GameEngine game, int seat) => game.CreateSnapshot(0).Players[seat].Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.AdoptedChild)?.Count ?? 0;
    private static bool HasSkill(GameEngine game, int seat, string id) => game.CreateSnapshot(0).Players[seat].Skills?.Any(skill => skill.ContentId == id) == true;
    private static PendingDecision? P(GameEngine game) => game.CreateSnapshot(0).PendingDecision;
    private static bool IsTrigger(PendingDecision p, string skill, string binding) => p.SkillPrompt?.SkillId == skill &&
        p.Choices.Any(c => c.Parameters.GetValueOrDefault("binding-id") == binding && c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool IsTarget(PendingDecision p, string kind) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("target-kind") == kind);
    private static void Activate(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Target(GameEngine game, int seat) => Answer(game, c => c.Targets.SequenceEqual([seat]));
    private static void Continue(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    { var p = P(game)!; Accept(game, new AnswerPromptCommand(0, p.PromptId, p.Choices.First(predicate).Id, game.Revision)); }
    private static void Use(GameEngine game, string binding, IReadOnlyList<int>? targets = null) =>
        Accept(game, new UseProgramSkillCommand(0, Driver, binding, [], targets ?? [], game.Revision, P(game)!.PromptId));
    private static void End(GameEngine game) => Accept(game, new EndPlayPhaseCommand(0, game.Revision, P(game)!.PromptId));
    private static void ReachPlay(GameEngine game) => Reach(game, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 200; step++)
        {
            var p = P(game); if (p is not null && predicate(p)) return;
            if (p is { Kind: DecisionKind.DiscardCards }) Accept(game, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, game.Revision));
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true) Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass") == true) Answer(game, c => c.Parameters.GetValueOrDefault("response") == "pass");
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        var promptText = JsonSerializer.Serialize(P(game));
        throw new InvalidOperationException("Ding Yuan fixture did not reach its actual boundary: " +
            promptText[..Math.Min(500, promptText.Length)] + "; state=" + JsonSerializer.Serialize(new {
                game.State.TurnNumber, game.State.Phase,
                Players = game.CreateSnapshot(0).Players.Select(player => new { player.Seat, player.Hp, player.MaxHp,
                    Skills = player.Skills?.Select(skill => skill.ContentId).ToArray() }).ToArray(),
                Frames = game.ResolutionStack.Select(frame => new { frame.Id, frame.Kind, frame.Step }).ToArray(),
                Facts = game.Events.TakeLast(8).Select(item => item.Payload.ToString()).ToArray() }));
    }
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        static string State(GameEngine value) => JsonSerializer.Serialize(new {
            Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(value.CreateSnapshot(seat))).ToArray(),
            Frames = JsonSerializer.Serialize(value.ResolutionStack), Events = value.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), value.CardMovements });
        Require(State(game) == State(restored), "Cold command replay preserves every viewer, source grant, paid card movement, native damage invoice and owning frame.");
        return restored;
    }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected Ding Yuan command."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static (GameEngine Game, ContentRegistry Registry) Create(string scenario)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, "fixture:dingyuan-owner", game.Revision, P(game)!.PromptId)); return (game, registry);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ding-yuan", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "ol:ding-yuan");
            var conditionalGrants = scenario == "first-own"
                ? new[] { new { id = "first-damage", skillId = "ol:xianshuai", minimumHp = 12 } }
                : [new { id = "father", skillId = "ol:cixiao", minimumHp = 12 }, new { id = "child", skillId = "ol:panshi", minimumHp = 12 }];
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"range","query":"slashDistanceLimit","operation":"unlimited","value":0,"priority":0}],
                "viewAs":[{"id":"fire","inputKinds":["slash"],"inputSuits":[],"outputKind":"fireSlash","forPlay":true,"forResponse":false}],
                "triggers":[{"id":"stock","window":"turnStartBeforeNormalFlow","subject":"owner","priority":1000,"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"draw","target":"owner","amount":12}]}],"activations":[
                {"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]},
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"mark-self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setMarkerAmount","target":"owner","marker":"adoptedChild","amount":1}]},
                {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
                {"id":"lose-cixiao","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:cixiao"],"sourceBind":"fixture:dingyuan-quiet"}]}]},
              {"id":"{{Cost}}","revision":1,"triggers":[{"id":"paid-transfer","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["{{TransferReason}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{Gain}}","revision":1,"triggers":[{"id":"lost-during-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.ol:xianshuai.Draw"],"optional":false,"effects":[{"op":"loseHp","target":"owner","amount":1},{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"{{Before}}","revision":1,"triggers":[{"id":"transfer-and-lose","window":"beforeDamageApplied","subject":"damageSource","optional":false,"usageScope":"game","usageLimit":1,"effects":[
                {"op":"chooseOption","target":"owner","resultBind":"issued","options":[{"id":"continue"}]},
                {"op":"selectTarget","target":"owner","targetKind":"anyLiving"},
                {"op":"setMarkerAmount","target":"owner","marker":"adoptedChild","amount":-1},
                {"op":"setMarkerAmount","target":"selectedTarget","marker":"adoptedChild","amount":1},
                {"op":"loseHp","target":"owner","amount":1},
                {"op":"chooseOption","target":"owner","resultBind":"lost","options":[{"id":"after-loss"}]}]}]},
              {"id":"fixture:dingyuan-conditional","revision":1,"hpSkillGrants":{{JsonSerializer.Serialize(conditionalGrants)}}},
              {"id":"fixture:dingyuan-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"fixture:dingyuan-first-other","revision":1,"triggers":[{"id":"first-other","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"damage","target":"selectedTarget","amount":1}]}]}
            ]}
            """;
            var ids = new[] { Driver, Cost, Gain, Before, "fixture:dingyuan-conditional", "fixture:dingyuan-quiet", "fixture:dingyuan-first-other" };
            var presentation = JsonSerializer.Serialize(new { schemaVersion = 3, skills = ids.ToDictionary(id => id,
                id => new { name = id, description = "丁原真实边界驱动", optionLabels = id is Cost or Gain ? new Dictionary<string, string> { ["continue"] = "继续" } :
                    id == Before ? new Dictionary<string, string> { ["continue"] = "继续", ["after-loss"] = "继续已发起伤害" } : [] }) });
            var catalog = SkillProgramCatalog.Load(rules, presentation);
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, id, "丁原实际机制夹具") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var skills = new List<string> { Driver };
            if (scenario == "relationship") skills.Add(Cost);
            if (scenario == "first-other") skills.Add("ol:xianshuai");
            if (scenario == "first-own") { skills.Add("fixture:dingyuan-conditional"); skills.Add(Gain); }
            if (scenario == "panshi") skills.Add(Before);
            builder.AddGeneral(new("fixture:dingyuan-owner", "丁原机制", "ol-ding-yuan",
                scenario.StartsWith("panshi", StringComparison.Ordinal) ? "fixture:dingyuan-conditional" : "ol:cixiao", "qun", 12, skills) { InitialHp = 11 });
            for (var seat = 1; seat < 4; seat++)
            {
                var primary = scenario == "panshi" || scenario == "panshi-chain" && seat == 1 ? "ol:cixiao" : "fixture:dingyuan-quiet";
                var additional = new List<string>();
                if (scenario == "first-other") additional.Add("fixture:dingyuan-first-other");
                if (primary != "fixture:dingyuan-quiet") additional.Add("fixture:dingyuan-quiet");
                builder.AddGeneral(new($"fixture:dingyuan-peer-{seat}", "其他", "supporter", primary, "qun", 12, additional));
            }
            builder.AddDeck(new("fixture:dingyuan-deck", "固定原生杀", 4, 1, []) { PhysicalCards = Enumerable.Range(0, 128)
                .Select(i => new ContentDeckPhysicalCard("standard:slash", i % 2 == 0 ? Suit.Spade : Suit.Heart, i % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "丁原实际机制", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:dingyuan-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:dingyuan-owner", "fixture:dingyuan-peer-1", "fixture:dingyuan-peer-2", "fixture:dingyuan-peer-3"]));
        }
    }
}
