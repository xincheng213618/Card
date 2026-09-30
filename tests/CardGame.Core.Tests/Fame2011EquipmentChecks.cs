using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2011EquipmentChecks
{
    private const string Driver = "fixture:fame-equipment-driver";
    private const string General = "fixture:fame-equipment-owner";
    private const string Mode = "identity:classic-fame-equipment-fixture";

    public static void GanluAtomicEquipmentAndReplay()
    {
        var (game, registry) = Create();
        Require(!game.GetHumanLegalActions().Any(action => action.ProgramSkillId == "classic:ganlu"),
            "Ganlu must have no action when every equipment area is empty.");
        Use(game, "weapon", [0]);
        Use(game, "other-weapon", [1]);
        var first = Equipment(game, 0).Single().Id;
        var second = Equipment(game, 1).Single().Id;
        StartGanlu(game);
        var before = GameCheckpointJson.Serialize(game.CreateCheckpoint());
        var rejected = game.Submit(new AnswerPromptCommand(1, game.PendingDecision!.PromptId,
            game.PendingDecision.Choices.First().Id, game.Revision));
        Require(!rejected.Accepted && before == GameCheckpointJson.Serialize(game.CreateCheckpoint()),
            "A foreign actor must not consume a Ganlu target prompt or mutate the journal.");
        var restored = Restore(game, registry);
        ChoosePair(game, 0, 1); ChoosePair(restored, 0, 1);
        Drain(game); Drain(restored);
        Equal(game, restored);
        Require(Equipment(game, 0).Single().Id == second && Equipment(game, 1).Single().Id == first,
            "Ganlu must exchange actual physical equipment and allow its own skill owner as target.");
        var moves = game.CardMovements.Where(move => move.Reason.Value.EndsWith("equipment-exchange", StringComparison.Ordinal)).ToArray();
        Require(moves.Length == 2 && moves.All(move => move.From.Zone == CardZoneKind.Equipment && move.To.Zone == CardZoneKind.Equipment),
            "An atomic equipment swap must record each card's actual equipment departure once.");
        Require(!game.GetHumanLegalActions().Any(action => action.ProgramSkillId == "classic:ganlu"), "Ganlu is once per play phase.");
        Equal(game, Restore(game, registry));
    }

    public static void GanluCapacityLossAndMovementOccurrences()
    {
        var (game, registry) = Create();
        Use(game, "double-slot", [0]);
        Use(game, "weapon", [0]); Use(game, "other-weapon", [0]);
        Require(Equipment(game, 0).Count == 2, "Fixture must expose two physical weapons in a widened slot.");
        Use(game, "lose-two");
        Use(game, "abolish", [1]);
        var handBefore = game.CreateSnapshot(0, true).Players[0].HandCount;
        StartGanlu(game); ChoosePair(game, 0, 1); Drain(game);
        Require(Equipment(game, 0).Count == 0 && Equipment(game, 1).Count == 0,
            "Incoming equipment must not exceed an abolished slot's physical capacity.");
        var moves = game.CardMovements.Where(move => move.Reason.Value.EndsWith("equipment-exchange", StringComparison.Ordinal)).ToArray();
        Require(moves.Length == 2 && moves.All(move => move.To == CardLocation.DiscardPile),
            "Every failed equipment entry must discard the physical card exactly once.");
        Require(game.CreateSnapshot(0, true).Players[0].HandCount == handBefore + 7,
            "Two equipment departures must cause one batch draw, two per-card draws and four Xiaoji cards.");
        Require(game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
            .Count(item => item.SkillId == "classic:xuanfeng" && item.Activated) == 1 &&
            game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
            .Count(item => item.SkillId == "classic:xiaoji" && item.Activated) == 2,
            "An atomic two-equipment exchange must offer Xuanfeng once and Xiaoji once per departed card.");
        Equal(game, Restore(game, registry));
        var (unwounded, _) = Create();
        Use(unwounded, "weapon", [0]);
        Require(!unwounded.GetHumanLegalActions().Any(action => action.ProgramSkillId == "classic:ganlu"),
            "At full HP a one-versus-zero equipment difference is illegal.");
        var (lion, lionRegistry) = Create(physicalKind: "classic:silver-lion");
        var armor = lion.CreateSnapshot(0, true).Players[0].Hand.First();
        Accept(lion.Submit(new PlayCardCommand(0, armor.Id, [], lion.Revision, lion.PendingDecision!.PromptId)));
        Drain(lion); Use(lion, "lose-two");
        var hpBefore = lion.CreateSnapshot(0, true).Players[0].Hp;
        StartGanlu(lion); ChoosePair(lion, 0, 1); Drain(lion);
        Require(lion.CreateSnapshot(0, true).Players[0].Hp == hpBefore + 1 &&
            Equipment(lion, 1).Single().Id == armor.Id,
            "A Silver Lion exchanged to another area must recover its old owner exactly once.");
        Equal(lion, Restore(lion, lionRegistry));
        var (generated, generatedRegistry) = Create();
        Use(generated, "general-weapon"); Use(generated, "weapon", [1]);
        var created = Equipment(generated, 0).Single();
        Require(created.Kind == CardKind.GeneralWeapon, "Fixture must produce a generated general weapon.");
        StartGanlu(generated); ChoosePair(generated, 0, 1); Drain(generated);
        Require(Equipment(generated, 1).Count == 0 && generated.CreateCardZoneDiagnostics()
            .Single(item => item.CardId == created.Id).Location == CardLocation.OutsideGame,
            "A generated general weapon must be destroyed on equipment departure rather than re-equipped.");
        Equal(generated, Restore(generated, generatedRegistry));
    }

    public static void BuyiSelfChoiceBlindOtherPrivacyAndReplay()
    {
        foreach (var target in new[] { 0, 1 })
        {
            var (game, registry) = Create();
            var hp = game.CreateSnapshot(0, true).Players[target].Hp;
            for (var point = 1; point < hp; point++) Use(game, "damage-one", [target]);
            BeginUse(game, "dying", [target]);
            Until(game, () => game.PendingDecision?.SkillPrompt?.SkillId == "classic:buyi");
            Equal(game, Restore(game, registry));
            Answer(game, game.PendingDecision!.Choices.First(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
            Until(game, () => game.PendingDecision?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "select-target") == true);
            Answer(game, game.PendingDecision!.Choices.Single());
            if (target == 0)
            {
                Require(game.PendingDecision?.Choices.All(choice => choice.Parameters.GetValueOrDefault("program-action") == "reveal-target-hand-card") == true,
                    "Self Buyi must allow choosing an actual own hand card.");
                Require(game.CreateSnapshot(2).PendingDecision is null,
                    "An observer must not receive the owner's private reveal choice.");
                var restored = Restore(game, registry);
                Answer(game, game.PendingDecision!.Choices.First());
                Answer(restored, restored.PendingDecision!.Choices.First());
                Drain(game); Drain(restored); Equal(game, restored);
            }
            else
            {
                Require(game.PendingDecision?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "reveal-target-hand-card") != true,
                    "Other Buyi must reveal one blind random card without showing a hand selection prompt.");
                Drain(game);
                Require(game.CreateSnapshot(0).Players[1].Hand.Count == 0 && game.CreateSnapshot(2).Players[1].Hand.Count == 0,
                    "Buyi must never expose the victim's remaining hand to owner or observers.");
            }
            Require(game.CreateSnapshot(0, true).Players[target].IsAlive && game.CreateSnapshot(0, true).Players[target].Hp == 1,
                "A nonbasic Buyi reveal must discard that card and recover before ordinary rescue.");
            Require(game.CardMovements.Count(move => move.From == CardLocation.Hand(target) &&
                move.Reason.Value.Contains("classic:buyi", StringComparison.Ordinal) && move.To == CardLocation.DiscardPile) == 1,
                "Buyi must discard only its one publicly revealed nonbasic card.");
            Equal(game, Restore(game, registry));
        }
        var (basicGame, basicRegistry) = Create(basicHand: true);
        var basicHp = basicGame.CreateSnapshot(0, true).Players[0].Hp;
        for (var point = 1; point < basicHp; point++) Use(basicGame, "damage-one", [0]);
        BeginUse(basicGame, "dying", [0]);
        Until(basicGame, () => basicGame.PendingDecision?.SkillPrompt?.SkillId == "classic:buyi");
        Answer(basicGame, basicGame.PendingDecision!.Choices.First(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Until(basicGame, () => basicGame.PendingDecision?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "select-target") == true);
        Answer(basicGame, basicGame.PendingDecision!.Choices.Single());
        Answer(basicGame, basicGame.PendingDecision!.Choices.First());
        Until(basicGame, () => basicGame.PendingDecision?.Kind == DecisionKind.RescueDying || basicGame.State.Winner != Winner.None);
        Require(basicGame.CreateSnapshot(0, true).Players[0].Hp == 0 &&
            !basicGame.CardMovements.Any(move => move.Reason.Value.Contains("classic:buyi", StringComparison.Ordinal)),
            "A basic Buyi card must remain in hand and ordinary rescue must continue without recovery.");
        Equal(basicGame, Restore(basicGame, basicRegistry));
    }

    private static IReadOnlyList<CardSnapshot> Equipment(GameEngine game, int seat) => game.CreateSnapshot(0, true).Players[seat].Equipment;
    private static void StartGanlu(GameEngine game) => BeginUse(game, "exchange-equipment", [], "classic:ganlu");
    private static void ChoosePair(GameEngine game, int first, int second) => Answer(game,
        game.PendingDecision!.Choices.Single(choice => choice.Targets.SequenceEqual(new[] { first, second })));
    private static void Use(GameEngine game, string id, IReadOnlyList<int>? targets = null) { BeginUse(game, id, targets ?? []); Drain(game); }
    private static void BeginUse(GameEngine game, string id, IReadOnlyList<int> targets, string skill = Driver) =>
        Accept(game.Submit(new UseProgramSkillCommand(0, skill, id, [], targets, game.Revision, game.PendingDecision!.PromptId)));
    private static void Answer(GameEngine game, PromptChoice choice) => Accept(game.Submit(new AnswerPromptCommand(
        game.PendingDecision!.PlayerSeat, game.PendingDecision.PromptId, choice.Id, game.Revision)));
    private static void Step(GameEngine game)
    {
        if (game.PendingDecision is not { } prompt) { Accept(game.Submit(new AdvanceOneStepCommand(game.Revision))); return; }
        if (prompt.Kind == DecisionKind.PlayCard)
            throw new InvalidOperationException("Unexpected play boundary while waiting for a focused skill prompt.");
        Answer(game, prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate") ??
            prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("response") is "pass" or "take-damage") ?? prompt.Choices.First());
    }
    private static void Until(GameEngine game, Func<bool> done)
    { for (var i = 0; i < 300; i++) { if (done()) return; Step(game); } throw new InvalidOperationException("Fame equipment resolution timed out."); }
    private static void Drain(GameEngine game) => Until(game, () => game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && game.ResolutionStack.Count == 0);
    private static GameEngine Restore(GameEngine game, ContentRegistry registry) => GameReplay.Restore(
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
    private static void Equal(GameEngine game, GameEngine replay) => Require(
        SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(replay.CreateSnapshot(0, true)) &&
        game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).SequenceEqual(
            replay.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType()))), "Fame equipment checkpoint replay must preserve snapshot and event order.");
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Rejected.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Create(bool basicHand = false, string? physicalKind = null)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(basicHand, physicalKind));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, General, game.Revision, game.PendingDecision!.PromptId)));
        Drain(game); return (game, registry);
    }
    private sealed class Fixture(bool basicHand, string? physicalKind) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fame-equipment-fixture", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);
        public void Register(IContentRegistryBuilder builder)
        {
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
            {"id":"fixture:fame-equipment-driver","revision":1,"minimumRulesVersion":{{GameCheckpoint.CurrentRulesVersion}},"activations":[
            {"id":"weapon","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"placeNamedWeapon","target":"selectedTarget","outputKind":"crossbow"}]},
            {"id":"other-weapon","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"placeNamedWeapon","target":"selectedTarget","outputKind":"qinggangSword"}]},
            {"id":"double-slot","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"alterEquipmentSlots","target":"selectedTarget","equipmentSlots":["weapon"],"amount":2}]},
            {"id":"abolish","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"alterEquipmentSlots","target":"selectedTarget","equipmentSlots":["weapon"],"amount":0}]},
            {"id":"lose-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":2}]},
            {"id":"general-weapon","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"equipSampledGenerals","target":"owner","amount":1}]},
            {"id":"damage-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
            {"id":"dying","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}
            ]},
            {"id":"fixture:fame-batch","revision":1,"minimumRulesVersion":{{GameCheckpoint.CurrentRulesVersion}},"triggers":[{"id":"batch","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perBatch","optional":false,"priority":0,"effects":[{"op":"draw","target":"owner","amount":1}]}]},
            {"id":"fixture:fame-card","revision":1,"minimumRulesVersion":{{GameCheckpoint.CurrentRulesVersion}},"triggers":[{"id":"card","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perCard","optional":false,"priority":0,"effects":[{"op":"draw","target":"owner","amount":1}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, """{"schemaVersion":3,"skills":{"fixture:fame-equipment-driver":{"name":"Fixture","description":"Fixture"},"fixture:fame-batch":{"name":"Batch","description":"Batch"},"fixture:fame-card":{"name":"Card","description":"Card"}}}""");
            foreach (var program in catalog.Programs.Values) builder.AddSkill(new ContentSkillDefinition(program.Id, "Fixture", "Fixture") { Program = program });
            builder.AddGeneral(new ContentGeneralDefinition(General, "测试吴国太", "wu_guo_tai", "classic:ganlu", "wu", BaseHp: 3,
                AdditionalSkillIds: ["classic:buyi", "classic:xuanfeng", "classic:xiaoji", Driver, "fixture:fame-batch", "fixture:fame-card"], Gender: GeneralGender.Female));
            var peers = Enumerable.Range(1, 4).Select(index => $"fixture:fame-equipment-peer-{index}").ToArray();
            foreach (var id in peers) builder.AddGeneral(new ContentGeneralDefinition(id, "Fixture", "supporter", "standard:none", "wu", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe("fixture:fame-equipment-deck", "Fixture", 4, 2, []) {
                PhysicalCards = Enumerable.Range(0, 208).Select(index => new ContentDeckPhysicalCard(physicalKind ?? (basicHand ? "standard:slash" : "standard:crossbow"), (Suit)(index % 4), index % 13 + 1)).ToArray() });
            builder.AddMode(new ContentModeDefinition(Mode, "Fixture", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 },
                "fixture:fame-equipment-deck", GeneralCandidateCount: 5, GeneralPoolIds: [General, .. peers]));
        }
    }
}
