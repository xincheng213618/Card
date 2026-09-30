using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2014ControlChecks
{
    private const string Mode = "identity:fame2014-control-check-5";
    public static void PindiPhysicalCategoriesTargetLimitsAndReplay()
    {
        var registry = Registry();
        for (var seed = 1; seed < 120; seed++)
        {
            var game = Start(registry, "classic:chen-qun", seed);
            ReachPlay(game);
            var hand = game.CreateSnapshot(0, true).Players[0].Hand;
            var basic = hand.FirstOrDefault(card => card.Kind is CardKind.Slash or CardKind.Dodge);
            var equipment = hand.FirstOrDefault(card => EquipmentCatalog.IsEquipment(card.Kind));
            var trick = hand.FirstOrDefault(card => card.Kind == CardKind.Duel);
            if (basic is null || equipment is null || trick is null) continue;
            var equipAction = game.GetHumanLegalActions().Single(item => item.CardId == equipment.Id && item.Kind == LegalActionKind.Equip);
            Accept(game.Submit(new PlayCardCommand(0, equipAction.CardId!.Value, equipAction.TargetSeats,
                game.Revision, Pending(game)!.PromptId, equipAction.PlayedCardKind, equipAction.TargetCardId)));
            Finish(game);
            ReachPlay(game);
            var before = game.CreateSnapshot(0, true).Players[1].Hand.Count;
            ActivatePindi(game, basic.Id, 1);
            ReachProgramPrompt(game);
            Require(Pending(game)?.Kind == DecisionKind.ProgramTrigger, "Pindi must pause for its draw/discard choice.");
            AssertReplay(game, registry);
            AnswerOption(game, "draw");
            Finish(game);
            ReachPlay(game);
            Require(game.CreateSnapshot(0, true).Players[1].Hand.Count == before + 1, "First Pindi must include the current activation in X.");
            var action = game.GetHumanLegalActions().Single(item => item.ProgramSkillId == "classic:pindi");
            Require(!action.SelectableTargetSeats.Contains(1) && !action.SelectableCardIds.Contains(basic.Id),
                "Pindi must exclude the used target and basic category.");
            var state = State(game);
            var rejected = game.Submit(new UseProgramSkillCommand(0, "classic:pindi", "rank-category-target",
                [equipment.Id], [1], game.Revision, Pending(game)!.PromptId));
            Require(!rejected.Accepted && State(game) == state, "A repeated target must fail without payment or state changes.");
            var secondBefore = game.CreateSnapshot(0, true).Players[2].Hand.Count;
            ActivatePindi(game, equipment.Id, 2);
            ReachProgramPrompt(game);
            AnswerOption(game, "draw");
            Finish(game);
            ReachPlay(game);
            Require(game.CreateSnapshot(0, true).Players[2].Hand.Count == secondBefore + 2, "Second Pindi must draw two.");
            Require(game.CardMovements.Any(move => move.CardId == equipment.Id && move.From == CardLocation.Equipment(0) && move.To == CardLocation.DiscardPile),
                "Pindi must pay real equipped cards from their equipment location.");
            ActivatePindi(game, trick.Id, 3);
            ReachProgramPrompt(game);
            AnswerOption(game, "discard");
            AssertReplay(game, registry);
            while (Pending(game) is { Kind: DecisionKind.ProgramTrigger } prompt &&
                prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"))
                Answer(game, prompt.Choices.First());
            Finish(game);
            Require(!game.GetHumanLegalActions().Any(item => item.ProgramSkillId == "classic:pindi"), "All three physical categories must exhaust this phase.");
            AssertReplay(game, registry);
            return;
        }
        throw new InvalidOperationException("No mixed physical-category Pindi fixture was found.");
    }





    private static ContentRegistry Registry() => ContentRegistry.Build(new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(), new Scenario());
    private static GameEngine Start(ContentRegistry registry, string general, int seed, string mode = Mode)
    {
        var game = GameEngine.CreateStandard(new GameOptions { Seed = seed, PlayerCount = 5, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, general, game.Revision, Pending(game)!.PromptId)));
        return game;
    }
    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 80 && Pending(game)?.Kind != DecisionKind.PlayCard; step++) Advance(game);
        Require(Pending(game)?.Kind == DecisionKind.PlayCard, "Control fixture did not reach Play.");
    }
    private static void ReachProgramPrompt(GameEngine game)
    {
        for (var step = 0; step < 40 && Pending(game)?.Kind != DecisionKind.ProgramTrigger; step++) Advance(game);
        Require(Pending(game)?.Kind == DecisionKind.ProgramTrigger, "Control fixture did not reach its program decision.");
    }
    private static void ActivatePindi(GameEngine game, int card, int target) => Accept(game.Submit(new UseProgramSkillCommand(0,
        "classic:pindi", "rank-category-target", [card], [target], game.Revision, Pending(game)!.PromptId)));
    private static void Driver(GameEngine game, string activation, IReadOnlyList<int> targets) => Accept(game.Submit(new UseProgramSkillCommand(0,
        "fixture:control-driver", activation, [], targets, game.Revision, Pending(game)!.PromptId)));
    private static void AnswerOption(GameEngine game, string option) => Answer(game,
        Pending(game)!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("option-id") == option));
    private static void Answer(GameEngine game, PromptChoice choice) => Accept(game.Submit(new AnswerPromptCommand(
        Pending(game)!.PlayerSeat, Pending(game)!.PromptId, choice.Id, game.Revision)));
    private static PendingDecision? Pending(GameEngine game) => game.PendingDecision ?? Enumerable.Range(0, 5).Select(seat => game.CreateSnapshot(seat, true).PendingDecision).FirstOrDefault(prompt => prompt is not null);
    private static void Advance(GameEngine game) => Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
    private static void Finish(GameEngine game)
    {
        for (var step = 0; step < 180 && game.ResolutionStack.Count > 0; step++)
            if (Pending(game) is { } prompt && (prompt.Kind == DecisionKind.ProgramTrigger || prompt.PlayerSeat == 0))
                Answer(game, prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip") ??
                    prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("response") == "take-damage") ?? prompt.Choices.First());
            else Advance(game);
        Require(game.ResolutionStack.Count == 0, "Control fixture resolution did not finish.");
    }
    private static void AssertReplay(GameEngine game, ContentRegistry registry)
    {
        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var replay = GameReplay.Restore(checkpoint, registry);
        Require(Enumerable.Range(0, 5).All(seat =>
                JsonSerializer.Serialize(game.CreateSnapshot(seat, true)) == JsonSerializer.Serialize(replay.CreateSnapshot(seat, true))) &&
            JsonSerializer.Serialize(game.ResolutionStack) == JsonSerializer.Serialize(replay.ResolutionStack) &&
            JsonSerializer.Serialize(game.CardMovements) == JsonSerializer.Serialize(replay.CardMovements) &&
            Events(game).SequenceEqual(Events(replay)),
            "Paused/final checkpoint must restore full state, movements and typed event JSON identically.");
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(game.CreateSnapshot(0, true));
    private static string[] Events(GameEngine game) => game.Events.Select(item => $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Control command failed.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class Scenario : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fame2014-control-check", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            const string driverRules = """
            {"schemaVersion":62,"skills":[{"id":"fixture:control-driver","revision":1,"minimumRulesVersion":190,
            "activations":[
            {"id":"chain","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"owner","chained":true}]},
            {"id":"unchain","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"owner","chained":false}]},
            {"id":"face-down","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"setFaceState","target":"owner","faceDown":true}]},
            {"id":"face-up","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"setFaceState","target":"owner","faceDown":false}]},
            {"id":"damage-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
            {"id":"damage-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]},
            {"id":"injure-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
            {"id":"equip-player","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetHand","targetRef":{"kind":"selectedTarget"},"resultBind":"weapon","revealBeforeMove":true,"awaitMovementTriggers":true},{"op":"useBoundCardByTarget","target":"selectedTarget","sourceBind":"weapon"}]}]}]}
            """;
            var driver = SkillProgramCatalog.Load(driverRules, """{"schemaVersion":3,"skills":{"fixture:control-driver":{"name":"状态测试","description":"状态与伤害原语"}}}""");
            builder.AddSkill(new ContentSkillDefinition("fixture:control-driver", "状态测试", "测试") { Program = driver.Programs["fixture:control-driver"] });
            builder.AddGeneral(new ContentGeneralDefinition("fixture:control-driver-general", "测试控制角色", "supporter", "fixture:control-driver", "qun", BaseHp: 8,
                AdditionalSkillIds: ["classic:faen", "classic:jiaojin", "classic:pindi"], Gender: GeneralGender.Female));
            var targets = Enumerable.Range(1, 3).Select(index => $"fixture:control-target-{index}").ToArray();
            foreach (var target in targets) builder.AddGeneral(new ContentGeneralDefinition(target, "目标", "supporter", "standard:none", "qun", BaseHp: 8,
                Gender: target.EndsWith('3') ? GeneralGender.Female : GeneralGender.Male));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:control-target-4", "目标", "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:control-multiple-play", "多出牌阶段", "supporter", "classic:pindi", "qun", BaseHp: 8,
                AdditionalSkillIds: ["classic:dangxian"]));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:control-compound-role", "复合牌目标", "supporter", "classic:zenhui", "qun", BaseHp: 8,
                AdditionalSkillIds: ["fixture:control-driver"]));
            var kinds = new[] { "standard:slash", "standard:bagua", "standard:duel", "standard:dodge" };
            builder.AddDeck(new ContentDeckRecipe("fixture:control-deck", "控制测试", 5, 2, []) {
                PhysicalCards = Enumerable.Range(0, 200).Select(index => new ContentDeckPhysicalCard(kinds[index % 4], (Suit)((index / 4) % 4), index % 13 + 1)).ToArray() });
            builder.AddMode(new ContentModeDefinition(Mode, "2014控制测试", 5, 5,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 }, "fixture:control-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: ["classic:chen-qun", "classic:sun-lu-ban", "fixture:control-driver-general", .. targets]));
            builder.AddDeck(new ContentDeckRecipe("fixture:compound-deck", "复合测试", 9, 2, []) {
                PhysicalCards = Enumerable.Range(0, 200).Select(index => new ContentDeckPhysicalCard(
                    index % 3 == 0 ? "classic:borrowed-sword" : index % 3 == 1 ? "standard:qinggang_sword" : "standard:slash",
                    index % 2 == 0 ? Suit.Spade : Suit.Club, index % 13 + 1)).ToArray() });
            foreach (var (mode, general, deck) in new[] {
                ("identity:control-multiple-play", "fixture:control-multiple-play", "fixture:control-deck"),
                ("identity:classic-control-compound-role", "fixture:control-compound-role", "fixture:compound-deck") })
                builder.AddMode(new ContentModeDefinition(mode, "控制共享机制测试", 5, 5,
                    new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                        [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 }, deck, GeneralCandidateCount: 5,
                    GeneralPoolIds: [general, .. targets, "fixture:control-target-4"]));
        }
    }
}
