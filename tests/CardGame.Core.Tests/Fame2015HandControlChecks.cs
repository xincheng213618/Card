using System.Text.Json;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2015HandControlChecks
{
    private const string Mode = "identity:classic-hand-control-check";

    public static void RevealColorRealCostOpaqueGainAndHpLossReplay()
    {
        var registry = Registry();
        var game = Start(registry, 11);
        ReachPlay(game);
        var hand = game.CreateSnapshot(0, true).Players[0].Hand.ToArray();
        var countBefore = hand.Length;
        var hpBefore = game.CreateSnapshot(0, true).Players[0].Hp;
        Activate(game, "classic:huaiyi", "reveal-color-take");
        var prompt = Pending(game)!;
        Require(game.Events.Any(item => item.Payload is ProgramCardsRevealedEvent revealed && revealed.OwnerSeat == 0 && revealed.SkillId == "classic:huaiyi" &&
                revealed.Cards.Select(card => card.Id).SequenceEqual(hand.Select(card => card.Id))),
            "All physical hand cards must be publicly revealed before selecting color.");
        Require(Enumerable.Range(0, 5).All(seat =>
                JsonSerializer.Serialize(game.CreateSnapshot(seat, revealAll: false).PublicRevealedCards.OrderBy(card => card.Id)) ==
                JsonSerializer.Serialize(hand.OrderBy(card => card.Id)) &&
                (seat == 0 || game.CreateSnapshot(seat, revealAll: false).PendingDecision is null)),
            "Every ordinary viewer must see the complete actual revealed faces while only the owner sees its private color choices.");
        AssertReplay(game, registry);
        var red = hand.Count(card => card.Suit is Suit.Heart or Suit.Diamond) >= 2;
        var paid = hand.Where(card => (card.Suit is Suit.Heart or Suit.Diamond) == red).ToArray();
        Require(paid.Length >= 2, "The fixture needs a real two-card color cost.");
        AnswerAction(game, red ? "red" : "black");
        ReachHandChoice(game, "take-targets");
        Require(paid.All(card => game.CardMovements.Any(move => move.CardId == card.Id && move.From == CardLocation.Hand(0) && move.To == CardLocation.DiscardPile)),
            "Color payment must discard every physical card of that color.");
        Require(Enumerable.Range(0, 5).All(seat => game.CreateSnapshot(seat, revealAll: false).PublicRevealedCards.Count == 0),
            "The displayed hand must leave every public reveal panel after real color payment, before taking any target card.");
        AssertReplay(game, registry);
        for (var index = 0; index < 2; index++)
        {
            var target = Pending(game)!.Choices.First(choice => choice.Parameters.GetValueOrDefault("hand-control-action") == "take-target");
            var seat = target.Targets.Single();
            Answer(game, target);
            Require(Pending(game)!.Choices.Where(choice => choice.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)).All(choice => choice.Cards.Count == 0),
                "Another participant's hand must use opaque positions without card IDs.");
            AssertReplay(game, registry);
            Answer(game, Pending(game)!.Choices.First(choice => choice.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)));
            if (index == 0) ReachHandChoice(game, "take-targets");
            Require(game.CardMovements.Any(move => move.From == CardLocation.Hand(seat) && move.To == CardLocation.Hand(0)), "The gain must move a real target-owned card.");
        }
        Finish(game);
        if (Pending(game)?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("stage") == "take-targets") == true) AnswerAction(game, "finish");
        Finish(game); ReachPlay(game);
        Require(game.CreateSnapshot(0, true).Players[0].Hp == hpBefore - 1 && game.CreateSnapshot(0, true).Players[0].Hand.Count == countBefore - paid.Length + 2,
            "Obtaining two cards must lose exactly one HP after real color payment and gains.");
        Require(!game.GetHumanLegalActions().Any(action => action.ProgramSkillId == "classic:huaiyi"), "The phase usage ledger must retain Huaiyi's paid activation.");
        AssertReplay(game, registry);

        var monoRegistry = Registry(["classic:huaiyi"], monochrome: true);
        var mono = Start(monoRegistry, 13); ReachPlay(mono);
        var monoHand = mono.CreateSnapshot(0, true).Players[0].Hand.ToArray();
        var monoHp = mono.CreateSnapshot(0, true).Players[0].Hp;
        Activate(mono, "classic:huaiyi", "reveal-color-take");
        Require(Pending(mono)!.Choices.Count == 1, "A monochrome actual hand must allow payment of its one existing color in the current edition.");
        Require(Enumerable.Range(0, 5).All(seat => mono.CreateSnapshot(seat, revealAll: false).PublicRevealedCards.Count == monoHand.Length),
            "A monochrome reveal must also publish every actual face to all ordinary viewers.");
        AssertReplay(mono, monoRegistry);
        AnswerAction(mono, "red"); ReachHandChoice(mono, "take-targets"); AnswerAction(mono, "finish"); Finish(mono); ReachPlay(mono);
        Require(mono.CreateSnapshot(0, true).Players[0].Hand.Count == 0 && mono.CreateSnapshot(0, true).Players[0].Hp == monoHp &&
            monoHand.All(card => mono.CardMovements.Any(move => move.CardId == card.Id && move.To == CardLocation.DiscardPile)) &&
            Enumerable.Range(0, 5).All(seat => mono.CreateSnapshot(seat, revealAll: false).PublicRevealedCards.Count == 0),
            "A zero-target choice must still pay all actual monochrome cards and must not lose HP.");
        AssertReplay(mono, monoRegistry);
    }

    public static void ParticipantDrawTopOrderEquipmentAndReplay()
    {
        var registry = Registry(equipmentDeck: true);
        var game = Start(registry, 19);
        ReachPlay(game);
        var equipment = game.CreateSnapshot(0, true).Players[0].Hand.First();
        Accept(game.Submit(new PlayCardCommand(0, equipment.Id, [], game.Revision, Pending(game)!.PromptId)));
        Finish(game); ReachPlay(game);
        Require(game.CreateSnapshot(0, true).Players[0].Equipment.Any(card => card.Id == equipment.Id), "The top-placement fixture must have a real equipped source.");
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, Pending(game)!.PromptId)));
        ReachSkillOffer(game, "classic:xingxue");
        Answer(game, Pending(game)!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        ReachHandChoice(game, "participants");
        // Deliberately select in the opposite order to prove seat order, not click order.
        Answer(game, Pending(game)!.Choices.Single(choice => choice.Targets.SequenceEqual([1])));
        Answer(game, Pending(game)!.Choices.Single(choice => choice.Targets.SequenceEqual([0])));
        AnswerAction(game, "finish");
        ReachHandChoice(game, "participant-top");
        Require(Pending(game)!.PlayerSeat == 0, "The current turn owner must finish its draw/top placement before the next seat draws.");
        Require(game.CreateSnapshot(0, true).Players[0].Hand.Count <= game.CreateSnapshot(0, true).Players[0].Hp,
            "The current edition must still require top placement when actual hand count is at most HP.");
        var top = Pending(game)!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Equipment));
        var topId = top.Cards.Single();
        AssertReplay(game, registry);
        Answer(game, top);
        Require(game.CreateSnapshot(0, true).Players[0].Equipment.Count == 0, "Top placement must really remove the selected equipment card.");
        ReachHandChoice(game, "participant-top");
        Require(Pending(game)!.PlayerSeat == 1 && game.CardMovements.Any(move => move.CardId == topId && move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(1)),
            "The second participant must really draw the physical card just put on top.");
        AssertReplay(game, registry);
        Advance(game);
        Require(game.Events.Count(item => item.Payload is ProgramParticipantCardPlacedOnTopEvent placed && placed.ParticipantSeat == 1) == 1,
            "The generic AI must answer the paused participant-owned top placement with a real owned card.");
        Finish(game);
        Require(game.Events.Count(item => item.Payload is ProgramParticipantCardPlacedOnTopEvent placed && placed.SkillId == "classic:xingxue") == 2,
            "Both participants must commit one typed placement event.");
        AssertReplay(game, registry);
    }

    public static void RealPlayDamageHandLimitAndPermanentFactionTargetsReplay()
    {
        var registry = Registry(["classic:jigong", "classic:zhaofu"]);
        var game = Start(registry, 37);
        ReachSkillOffer(game, "classic:jigong");
        var before = game.CreateSnapshot(0, true).Players[0].Hand.Count;
        Answer(game, Pending(game)!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Finish(game); ReachPlay(game);
        Require(game.CreateSnapshot(0, true).Players[0].Hand.Count == before + 2 && ReadHandLimit(game, 0) == 0,
            "Jigong must draw two physical cards and initially set this turn's hand limit to zero.");
        Driver(game, "damage-other", [1]); Finish(game); ReachPlay(game);
        Require(ReadHandLimit(game, 0) == 1, "Committed source damage in this play phase must raise the promised limit.");
        Driver(game, "damage-self", [1]); Finish(game); ReachPlay(game);
        Require(ReadHandLimit(game, 0) == 1, "Receiving another source's damage must not raise the promised limit.");
        var farDistance = ReadDistance(game, 2, 0);
        Require(farDistance > 1, "The range fixture requires a distant Wu participant.");
        Accept(game.Submit(new UseProgramSkillCommand(0, "classic:zhaofu", "permanent-faction-targets", [], [0, 2], game.Revision, Pending(game)!.PromptId)));
        Finish(game); ReachPlay(game);
        Require(ReadFactionRangeTarget(game, 2, 0) && ReadWithinAttackRange(game, 2, 0) &&
            !ReadFactionRangeTarget(game, 0, 2) && !ReadWithinAttackRange(game, 0, 2) &&
            !ReadFactionRangeTarget(game, 2, 3) && ReadDistance(game, 2, 0) == farDistance,
            "The permanent target grant must cover other Wu actors and only selected targets while retaining normal distance.");
        Require(!game.GetHumanLegalActions().Any(action => action.ProgramSkillId == "classic:zhaofu"), "The permanent grant must consume its game ledger.");
        AssertReplay(game, registry);
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, Pending(game)!.PromptId)));
        for (var count = 0; count < 150 && game.CreateSnapshot(0, true).CurrentSeat == 0; count++) Advance(game);
        Require(ReadHandLimit(game, 0) == game.CreateSnapshot(0, true).Players[0].Hp && ReadFactionRangeTarget(game, 2, 0),
            "The damage limit must expire with the turn while selected faction targets persist into the next turn.");
        AssertReplay(game, registry);
    }

    private static int ReadHandLimit(GameEngine game, int seat)
    {
        var players = (System.Collections.IList)typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!;
        return (int)typeof(GameEngine).GetMethod("GetHandLimit", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(game, [players[seat]])!;
    }
    private static int ReadDistance(GameEngine game, int source, int target) => game.GetCombatDistance(source, target);
    private static bool ReadFactionRangeTarget(GameEngine game, int actor, int target) => (bool)typeof(GameEngine).GetMethod("IsGameFactionAttackRangeTarget",
        BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(game, [actor, target])!;
    private static bool ReadWithinAttackRange(GameEngine game, int actor, int target) => (bool)typeof(GameEngine).GetMethod("IsWithinAttackRange",
        BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(game, [actor, target])!;

    private static ContentRegistry Registry(string[]? skills = null, bool equipmentDeck = false, bool slashOnly = false, int ownerHp = 8, bool monochrome = false,
        bool arrowOnly = false, bool factionDefense = false, bool publicCosts = false) => ContentRegistry.Build(new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(), new Scenario(skills, equipmentDeck, slashOnly, ownerHp, monochrome, arrowOnly, factionDefense, publicCosts));
    private static GameEngine Start(ContentRegistry registry, int seed) => TryStart(registry, seed, Role.Lord) ??
        throw new InvalidOperationException("The human fixture general is unavailable.");
    private static GameEngine? TryStart(ContentRegistry registry, int seed, Role humanRole)
    {
        var game = GameEngine.CreateStandard(new GameOptions { Seed = seed, PlayerCount = 5, HumanSeat = 0, HumanRole = humanRole,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(game.Submit(new StartGameCommand()));
        if (!Pending(game)!.ValidContentIds.Contains("fixture:hand-control-owner")) return null;
        Accept(game.Submit(new SelectGeneralCommand(0, "fixture:hand-control-owner", game.Revision, Pending(game)!.PromptId)));
        return game;
    }
    private static void Activate(GameEngine game, string skill, string activation) => Accept(game.Submit(new UseProgramSkillCommand(0,
        skill, activation, [], [], game.Revision, Pending(game)!.PromptId)));
    private static void Driver(GameEngine game, string activation, IReadOnlyList<int> seats) => Accept(game.Submit(new UseProgramSkillCommand(0,
        "fixture:hand-control-driver", activation, [], seats, game.Revision, Pending(game)!.PromptId)));
    private static PendingDecision? Pending(GameEngine game) => game.PendingDecision ?? Enumerable.Range(0, 5).Select(seat => game.CreateSnapshot(seat, true).PendingDecision).FirstOrDefault(item => item is not null);
    private static void Answer(GameEngine game, PromptChoice choice) => Accept(game.Submit(new AnswerPromptCommand(Pending(game)!.PlayerSeat, Pending(game)!.PromptId, choice.Id, game.Revision)));
    private static void AnswerAction(GameEngine game, string action) => Answer(game, Pending(game)!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("hand-control-action") == action));
    private static void Advance(GameEngine game) => Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
    private static void ReachPlay(GameEngine game)
    {
        for (var count = 0; count < 100 && Pending(game)?.Kind != DecisionKind.PlayCard; count++)
            if (Pending(game) is { Kind: DecisionKind.ProgramTrigger } prompt) Answer(game, prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip") ?? prompt.Choices.First());
            else Advance(game);
        Require(Pending(game)?.Kind == DecisionKind.PlayCard, "Fixture did not reach Play.");
    }
    private static void ReachSkillOffer(GameEngine game, string skill)
    {
        for (var count = 0; count < 100 && !(Pending(game) is { Kind: DecisionKind.ProgramTrigger } prompt && prompt.SkillPrompt?.SkillId == skill); count++) Advance(game);
        Require(Pending(game)?.SkillPrompt?.SkillId == skill, $"Missing offer for {skill}.");
    }
    private static void ReachHandChoice(GameEngine game, string stage)
    {
        for (var count = 0; count < 100 && Pending(game)?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("stage") == stage) != true; count++) Advance(game);
        Require(Pending(game)?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("stage") == stage) == true, $"Missing hand-control stage {stage}.");
    }
    private static void Finish(GameEngine game)
    {
        for (var count = 0; count < 150 && game.ResolutionStack.Count > 0; count++)
            if (Pending(game) is { Kind: DecisionKind.ProgramTrigger } prompt)
                Answer(game, prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("hand-control-action") == "finish") ??
                    prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip") ?? prompt.Choices.First());
            else Advance(game);
    }
    private static void AssertReplay(GameEngine game, ContentRegistry registry)
    {
        var replay = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, 5).All(seat => JsonSerializer.Serialize(game.CreateSnapshot(seat, true)) == JsonSerializer.Serialize(replay.CreateSnapshot(seat, true))) &&
            Enumerable.Range(0, 5).All(seat => JsonSerializer.Serialize(game.CreateSnapshot(seat, revealAll: false)) ==
                JsonSerializer.Serialize(replay.CreateSnapshot(seat, revealAll: false))) &&
            JsonSerializer.Serialize(game.ResolutionStack) == JsonSerializer.Serialize(replay.ResolutionStack) &&
            JsonSerializer.Serialize(game.CardMovements) == JsonSerializer.Serialize(replay.CardMovements) &&
            game.Events.Select(EventJson).SequenceEqual(replay.Events.Select(EventJson)) &&
            Enumerable.Range(0, 5).All(seat => ReadHandLimit(game, seat) == ReadHandLimit(replay, seat)) &&
            Enumerable.Range(0, 5).All(actor => Enumerable.Range(0, 5).All(target =>
                ReadWithinAttackRange(game, actor, target) == ReadWithinAttackRange(replay, actor, target))),
            "Checkpoint command JSON must replay every ordinary private/public viewer, paused and final state, typed events, movements and effective hand-limit/range queries exactly.");
    }
    private static string EventJson(EventEnvelope item) => $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}";
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Command rejected.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class Scenario(string[]? skills, bool equipmentDeck, bool slashOnly, int ownerHp, bool monochrome, bool arrowOnly, bool factionDefense, bool publicCosts) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("hand-control-check", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var driver = SkillProgramCatalog.Load("""
                {"schemaVersion":62,"skills":[{"id":"fixture:hand-control-driver","revision":1,"minimumRulesVersion":190,"activations":[
                {"id":"damage-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"damage-self","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
                {"id":"draw-owner","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":2}]},
                {"id":"equip-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment"}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:hand-control-driver":{"name":"规则测试","description":"真实伤害测试"}}}""");
            builder.AddSkill(new ContentSkillDefinition("fixture:hand-control-driver", "规则测试", "测试") { Program = driver.Programs["fixture:hand-control-driver"] });
            if (factionDefense)
            {
                var defense = SkillProgramCatalog.Load("""
                    {"schemaVersion":62,"skills":[{"id":"fixture:faction-defense","revision":1,"cardPolicies":[
                    {"id":"allied-dodge","kind":"factionResponseRequest","requiredCardKinds":["dodge"],"factionId":"wu","ownerRole":"lord"}]}]}
                    """, """{"schemaVersion":3,"skills":{"fixture:faction-defense":{"name":"阵营响应","description":"验证虚拟闪实际提供给主公"}}}""");
                builder.AddSkill(new ContentSkillDefinition("fixture:faction-defense", "阵营响应", "验证") { Program = defense.Programs["fixture:faction-defense"], Tags = SkillTag.Lord,
                    SelectionWeights = new Dictionary<Role, double> { [Role.Lord] = 100 } });
            }
            builder.AddGeneral(new ContentGeneralDefinition("fixture:hand-control-owner", "规则测试角色", "supporter", "fixture:hand-control-driver", "wu", BaseHp: ownerHp,
                AdditionalSkillIds: skills ?? ["classic:huaiyi", "classic:xingxue", "classic:yaoming", "classic:yanzhu"]));
            var targets = Enumerable.Range(1, 4).Select(index => $"fixture:hand-control-target-{index}").ToArray();
            foreach (var target in targets) builder.AddGeneral(new ContentGeneralDefinition(target, "规则目标", "supporter", factionDefense ? "fixture:faction-defense" : "standard:none", "wu", BaseHp: 8));
            builder.AddDeck(new ContentDeckRecipe("fixture:hand-control-deck", "控制测试", 6, 2, []) {
                PhysicalCards = Enumerable.Range(0, 300).Select(index => new ContentDeckPhysicalCard(equipmentDeck || publicCosts && index % 6 == 0 ? "standard:crossbow" : publicCosts && index % 6 == 1 ? "standard:lightning" : arrowOnly ? "standard:arrow_barrage" : !slashOnly && index % 3 == 0 ? "standard:dodge" : "standard:slash", monochrome ? Suit.Heart : (Suit)(index % 4), index % 13 + 1)).ToArray() });
            builder.AddMode(new ContentModeDefinition(Mode, "控制测试", 5, 5, new Dictionary<string, int> {
                [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 },
                "fixture:hand-control-deck", GeneralCandidateCount: 5, GeneralPoolIds: ["fixture:hand-control-owner", .. targets]));
        }
    }
}
