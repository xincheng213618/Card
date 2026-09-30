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

    public static void ZenhuiExtraTargetAndActorProviderReplay()
    {
        var registry = Registry();
        foreach (var actionKind in new[] { LegalActionKind.Duel, LegalActionKind.Slash })
        foreach (var option in new[] { "extra-target", "become-user" })
        {
            var found = false;
            for (var seed = 1; seed < 80 && !found; seed++)
            {
                var game = Start(registry, "classic:sun-lu-ban", seed);
                ReachPlay(game);
                var action = game.GetHumanLegalActions().FirstOrDefault(item => item.Kind == actionKind &&
                    (actionKind == LegalActionKind.Duel
                        ? game.CreateSnapshot(0, true).Players[0].Hand.Single(card => card.Id == item.CardId).Suit is Suit.Spade or Suit.Club
                        : game.CreateSnapshot(0, true).Players[0].Hand.Single(card => card.Id == item.CardId).Suit is Suit.Heart or Suit.Diamond));
                if (action is null) continue;
                Accept(game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
                    game.Revision, Pending(game)!.PromptId, action.PlayedCardKind, action.TargetCardId)));
                ReachProgramPrompt(game);
                var trigger = Pending(game)!;
                Require(trigger.Kind == DecisionKind.ProgramTrigger && trigger.SkillPrompt?.SkillId == "classic:zenhui", "Black unique-target Duel or red Slash must offer Zenhui.");
                Answer(game, trigger.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
                var targetPrompt = Pending(game)!;
                var selected = targetPrompt.Choices.First(choice => choice.Targets.Count == 1);
                var seat = selected.Targets.Single();
                Answer(game, selected);
                Require(Pending(game) is not null, "Zenhui must privately prompt its selected participant.");
                AssertReplay(game, registry);
                AnswerOption(game, option);
                if (option == "become-user") Answer(game, Pending(game)!.Choices.First(choice => choice.Cards.Count == 1));
                var use = game.ResolutionStack.OfType<CardUseFrame>().Last();
                Require(use.Action!.ProviderSeat == 0 && use.Action.PhysicalCards.Single().From == CardLocation.Hand(0),
                    "Zenhui must preserve the original physical provider and payment location.");
                Require(option == "become-user" ? use.SourceSeat == seat && use.Action.ActorSeat == seat : use.TargetSeats.Contains(seat),
                    "Zenhui must apply the selected actor/extra-target branch.");
                AssertReplay(game, registry);
                Finish(game);
                Require(game.Events.Any(item => option == "become-user" ? item.Payload is ProgramCardUseActorReplacedEvent : item.Payload is ProgramCardUseTargetAddedEvent),
                    "Role changes must retain typed audit events.");
                AssertReplay(game, registry);
                found = true;
            }
            Require(found, $"No eligible {actionKind} fixture for {option} was found.");
        }
    }

    public static void FaenStateEdgesAndJiaojinGenderReductionReplay()
    {
        var registry = Registry();
        var game = Start(registry, "fixture:control-driver-general", 17);
        ReachPlay(game);
        var before = game.CreateSnapshot(0, true).Players[0].Hand.Count;
        Driver(game, "chain", []);
        ReachProgramPrompt(game);
        Require(Pending(game)!.SkillPrompt?.SkillId == "classic:faen", "Entering chain must offer Faen.");
        AssertReplay(game, registry);
        Answer(game, Pending(game)!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Finish(game); ReachPlay(game);
        Require(game.CreateSnapshot(0, true).Players[0].Hand.Count == before + 1, "Faen chain entry draws once.");
        Driver(game, "chain", []); Finish(game); ReachPlay(game);
        Require(game.CreateSnapshot(0, true).Players[0].Hand.Count == before + 1, "Setting already chained state must not trigger Faen.");
        Driver(game, "unchain", []); Finish(game); ReachPlay(game);
        Require(game.CreateSnapshot(0, true).Players[0].Hand.Count == before + 1, "Leaving chain must not trigger Faen.");
        Driver(game, "face-down", []); Finish(game); ReachPlay(game);
        Require(game.CreateSnapshot(0, true).Players[0].Hand.Count == before + 1, "Turning face down must not trigger current Faen.");
        Driver(game, "face-up", []); ReachProgramPrompt(game);
        Answer(game, Pending(game)!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Finish(game); ReachPlay(game);
        Require(game.CreateSnapshot(0, true).Players[0].Hand.Count == before + 2, "Turning face up must trigger Faen exactly once.");
        Driver(game, "face-up", []); Finish(game); ReachPlay(game);
        Require(game.CreateSnapshot(0, true).Players[0].Hand.Count == before + 2, "Already face-up setter must not emit a state edge.");
        AssertReplay(game, registry);

        var male = game.CreateSnapshot(0, true).Players.First(player => player.Seat != 0 && registry.Generals[player.GeneralId].Gender == GeneralGender.Male).Seat;
        var female = game.CreateSnapshot(0, true).Players.First(player => player.Seat != 0 && registry.Generals[player.GeneralId].Gender == GeneralGender.Female).Seat;
        foreach (var amount in new[] { 1, 2 })
        {
            int? equippedCost = null;
            if (amount == 2)
            {
                var equipment = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Equip);
                equippedCost = equipment.CardId;
                Accept(game.Submit(new PlayCardCommand(0, equipment.CardId!.Value, equipment.TargetSeats,
                    game.Revision, Pending(game)!.PromptId, equipment.PlayedCardKind, equipment.TargetCardId)));
                Finish(game); ReachPlay(game);
            }
            // The fixture draws enough real equipment to exercise two independent costs.
            var hp = game.CreateSnapshot(0, true).Players[0].Hp;
            Driver(game, amount == 1 ? "damage-one" : "damage-two", [male]);
            ReachProgramPrompt(game);
            Require(Pending(game)!.SkillPrompt?.SkillId == "classic:jiaojin", "Male source must offer Jiaojin.");
            Answer(game, Pending(game)!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
            AssertReplay(game, registry);
            Answer(game, Pending(game)!.Choices.First(choice => choice.Cards.Count == 1 &&
                (equippedCost is null || choice.Cards.Single() == equippedCost)));
            Finish(game); ReachPlay(game);
            Require(game.CreateSnapshot(0, true).Players[0].Hp == hp - (amount - 1), "Jiaojin must reduce frozen damage by one, including prevention at zero.");
            if (equippedCost is { } paid)
                Require(game.CardMovements.Any(move => move.CardId == paid && move.From == CardLocation.Equipment(0) && move.To == CardLocation.DiscardPile),
                    "Jiaojin must pay real equipped equipment from its owned equipment zone.");
            AssertReplay(game, registry);
        }
        var femaleHp = game.CreateSnapshot(0, true).Players[0].Hp;
        var eventsBefore = game.Events.Count;
        Driver(game, "damage-one", [female]); Finish(game); ReachPlay(game);
        Require(game.CreateSnapshot(0, true).Players[0].Hp == femaleHp - 1 &&
            !game.Events.Skip(eventsBefore).Any(item => item.Payload is ProgramSkillStartedEvent started && started.SkillId == "classic:jiaojin"),
            "Female source must not invoke Jiaojin.");
        AssertReplay(game, registry);
        Driver(game, "injure-target", [male]); Finish(game); ReachPlay(game);
        var cost = game.GetHumanLegalActions().Single(action => action.ProgramSkillId == "classic:pindi").SelectableCardIds.First();
        ActivatePindi(game, cost, male); ReachProgramPrompt(game); AnswerOption(game, "draw");
        ReachProgramPrompt(game);
        Require(Pending(game)!.SkillPrompt?.SkillId == "classic:faen", "Pindi on a wounded target must chain its owner and offer Faen after its effect.");
        AssertReplay(game, registry);
        Answer(game, Pending(game)!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Finish(game); ReachPlay(game);
        var draws = game.CreateSnapshot(0, true).Players[0].Hand.Count;
        Driver(game, "injure-target", [female]); Finish(game); ReachPlay(game);
        var secondCost = game.GetHumanLegalActions().Single(action => action.ProgramSkillId == "classic:pindi").SelectableCardIds.First();
        ActivatePindi(game, secondCost, female); ReachProgramPrompt(game); AnswerOption(game, "draw");
        Finish(game); ReachPlay(game);
        Require(game.CreateSnapshot(0, true).Players[0].Hand.Count == draws - 1,
            "Pindi while its owner is already chained must pay its new physical category without a duplicate Faen draw.");
        AssertReplay(game, registry);
    }

    public static void PindiInsertedPlayResetsPhaseLedgerRetainsTurnOrdinal()
    {
        var registry = Registry();
        for (var seed = 1; seed < 100; seed++)
        {
            var game = Start(registry, "fixture:control-multiple-play", seed, "identity:control-multiple-play");
            ReachPlay(game);
            var basics = game.CreateSnapshot(0, true).Players[0].Hand.Where(card => card.Kind is CardKind.Slash or CardKind.Dodge).ToArray();
            if (basics.Length < 2) continue;
            var turn = game.CreateSnapshot(0, true).TurnNumber;
            var firstBefore = game.CreateSnapshot(0, true).Players[1].Hand.Count;
            ActivatePindi(game, basics[0].Id, 1); ReachProgramPrompt(game); AnswerOption(game, "draw");
            Finish(game); ReachPlay(game);
            Require(game.CreateSnapshot(0, true).Players[1].Hand.Count == firstBefore + 1, "Inserted Play starts Pindi at X=1.");
            Require(!game.GetHumanLegalActions().Single(action => action.ProgramSkillId == "classic:pindi").SelectableTargetSeats.Contains(1),
                "The target ledger must be closed inside the inserted Play.");
            Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, Pending(game)!.PromptId)));
            ReachPlay(game);
            Require(game.CreateSnapshot(0, true).TurnNumber == turn, "Normal Play must remain in the same turn as inserted Play.");
            var normal = game.GetHumanLegalActions().Single(action => action.ProgramSkillId == "classic:pindi");
            Require(normal.SelectableTargetSeats.Contains(1) && normal.SelectableCardIds.Contains(basics[1].Id),
                "A new Play phase must reset both the target and physical card category ledgers.");
            var secondBefore = game.CreateSnapshot(0, true).Players[1].Hand.Count;
            ActivatePindi(game, basics[1].Id, 1); ReachProgramPrompt(game);
            AssertReplay(game, registry);
            AnswerOption(game, "draw"); Finish(game); ReachPlay(game);
            Require(game.CreateSnapshot(0, true).Players[1].Hand.Count == secondBefore + 2,
                "Pindi X must retain the whole-turn count after both phase ledgers reset.");
            AssertReplay(game, registry);
            return;
        }
        throw new InvalidOperationException("No multiple-Play fixture with two physical basic cards was found.");
    }

    public static void ZenhuiBorrowedSwordCompoundTargetsCompleteAndReplay()
    {
        var registry = Registry();
        for (var seed = 1; seed < 100; seed++)
        {
            var game = Start(registry, "fixture:control-compound-role", seed, "identity:classic-control-compound-role");
            ReachPlay(game);
            var hand = game.CreateSnapshot(0, true).Players[0].Hand;
            var weapons = hand.Where(card => card.Kind == CardKind.QinggangSword).Take(2).ToArray();
            var borrowed = hand.FirstOrDefault(card => card.Kind == CardKind.BorrowedSword && card.Suit is Suit.Spade or Suit.Club);
            if (weapons.Length != 2 || borrowed is null) continue;
            for (var index = 0; index < 2; index++)
            {
                Driver(game, "equip-player", [index + 1]); ReachProgramPrompt(game);
                Answer(game, Pending(game)!.Choices.Single(choice => choice.Cards.SequenceEqual([weapons[index].Id])));
                Finish(game); ReachPlay(game);
            }
            var action = game.GetHumanLegalActions().FirstOrDefault(item => item.CardId == borrowed.Id && item.Kind == LegalActionKind.BorrowedSword &&
                item.TargetSeats[0] == 1 && item.TargetSeats[1] == 0) ?? throw new InvalidOperationException("An equipped weapon owner must have its legal borrowed-sword pair.");
            Accept(game.Submit(new PlayCardCommand(0, borrowed.Id, action.TargetSeats, game.Revision, Pending(game)!.PromptId,
                action.PlayedCardKind, action.TargetCardId)));
            ReachProgramPrompt(game);
            Answer(game, Pending(game)!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
            Answer(game, Pending(game)!.Choices.Single(choice => choice.Targets.SequenceEqual([2])));
            AnswerOption(game, "extra-target");
            Require(Pending(game)!.Choices.All(choice => choice.Parameters.GetValueOrDefault("program-action") == "add-compound-card-target"),
                "An additional borrowed-sword weapon owner must prompt for its own legal Slash victim.");
            AssertReplay(game, registry);
            Answer(game, Pending(game)!.Choices.Single(choice => choice.Targets.SequenceEqual([3])));
            var use = game.ResolutionStack.OfType<CardUseFrame>().Last(frame => frame.Action?.EffectiveKind == CardKind.BorrowedSword);
            Require(use.TargetSeats.SequenceEqual([1, 0, 2, 3]) && use.Action!.EffectiveDesignatedTargetSeats.SequenceEqual([1, 2]) &&
                use.SourceSeat == 0 && use.Action.ActorSeat == 0 && use.Action.ProviderSeat == 0 &&
                use.Action.PhysicalCards.Single().From == CardLocation.Hand(0),
                "Compound borrowed-sword pairs must preserve two designated owners and original actor/provider/payment.");
            AssertReplay(game, registry);
            Finish(game);
            Require(game.Events.Count(item => item.Payload is BorrowedSwordResolvedEvent resolved && resolved.SourceSeat == 0) == 2,
                "Both borrowed-sword owner/victim pairs must actually finish their response and effect.");
            Require(game.CardMovements.Count(move => move.CardId == borrowed.Id && move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing) == 1 &&
                game.CardMovements.Count(move => move.CardId == borrowed.Id && move.From == CardLocation.Processing && move.To == CardLocation.DiscardPile) == 1,
                "The physical borrowed sword must be paid and discarded once after both compound pairs.");
            AssertReplay(game, registry);
            return;
        }
        throw new InvalidOperationException("No equipped compound borrowed-sword fixture was found.");
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
