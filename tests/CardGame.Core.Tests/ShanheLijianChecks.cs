using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheLijianChecks
{
    private const string Skill = "shanhe:s5-lijian-68", Binding = "discard-and-issue-ordered-duel";
    private const string Owner = "fixture:lijian-owner", Actor = "fixture:lijian-actor", Target = "fixture:lijian-target";
    private const string Driver = "fixture:lijian-driver", Watch = "fixture:lijian-cost-watch", Gate = "fixture:lijian-hp-gate";
    private const string Deck = "fixture:lijian-deck";
    private const string Wushuang = "shanhe:shengui-wushuang", Liyu = "shanhe:s5-liyu-14128";
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new(83, 1, "fixture-lijian-content", "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [],
            [new("shanhe:s5-diao-chan-biyue")], [], new string('a', 64))));

    public static void RealOrderedDuelRunsUseWindowsAndPaysOriginalEntitiesCold()
    {
        VerifyParserRejectsUnpaidOrUnpairedOptIn();
        VerifyDirectedNativeLegalSelections();
        var s = Scene.Start("normal");
        var cost = s.View(0).Hand.Single(card => card.Kind == CardKind.Peach).Id;
        var actorSlash = s.View(1).Hand.Single(card => card.Kind == CardKind.Slash).Id;
        var targetSlash = s.View(2).Hand.Single(card => card.Kind == CardKind.Slash).Id;
        var counters = Enumerable.Range(0, 4).SelectMany(seat => s.View(seat).Hand)
            .Where(card => card.Kind == CardKind.Nullification).Select(card => card.Id).ToArray();
        s.Use(cost); s.Until(() => s.G.ResolutionStack.OfType<CardUseFrame>().Any(use => use.SelectedActorDuelOrigin?.OrderedPairPayment is not null));
        var use = s.G.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.SelectedActorDuelOrigin?.OrderedPairPayment is not null);
        Need(use.SourceSeat == 1 && use.TargetSeats.SequenceEqual([2]) && use.CardId == 0 && use.PhysicalCardIds?.Count == 0 &&
            use.Action is { Type: CardActionType.Use, ActorSeat: 1, EffectiveKind: CardKind.Duel, PhysicalCards.Count: 0 } &&
            use.SelectedActorDuelOrigin is { OwnerSeat: 0, InitialActorSeat: 1, OrderedPairPayment: { CardId: var paidCard } } && paidCard == cost,
            "The first ordered other male really Uses an independent zero-entity Duel on the second; Diao Chan's paid discard is not its material.");
        s.Cold(); s.Finish();
        Need(s.Events<CardUseDeclaredEvent>().Count(fact => fact.ResolutionId == use.Id && fact.SourceSeat == 1 && fact.CardKind == CardKind.Duel && fact.CardId == 0) == 1 &&
            s.Events<CardUseFinishedEvent>().Count(fact => fact.ResolutionId == use.Id && fact.CardKind == CardKind.Duel) == 1 &&
            s.Events<ProgramBindingStartedEvent>().Any(fact => fact.SkillId == "classic:jizhi" && fact.OwnerSeat == 1),
            "The true actor's native Jizhi use window runs and the actual Duel declares and completes exactly once: " + s.Diagnostic());
        Need(s.Events<DuelResponseEvent>().Any(fact => fact.ResolutionId == use.Id && fact.ResponderSeat == 2 && fact.UsedSlash && fact.SlashCardId == targetSlash) &&
            s.Events<DuelResponseEvent>().Any(fact => fact.ResolutionId == use.Id && fact.ResponderSeat == 1 && fact.UsedSlash && fact.SlashCardId == actorSlash) &&
            s.Events<DamageAppliedEvent>().Any(fact => fact.SourceSeat == 1 && fact.TargetSeat == 2 && fact.Amount == 1),
            "Unnullifiable still permits both real Slash responses and resolves native Duel damage with dynamic Wushuang's minimum-one response rule.");
        Need(counters.Length == 4 && counters.All(id => s.G.CardMovements.All(move => move.CardId != id || move.To != CardLocation.Processing)) &&
            s.Events<ProgramBindingStartedEvent>().All(fact => fact.SkillId != Liyu) && s.Events<DamageTargetRegionObtainPaidEvent>().Length == 0,
            "Held physical Nullifications cannot respond, while Duel damage cannot activate the actor's real Slash-only Liyu.");
        s.AssertCost(cost); s.AssertResponse(actorSlash, 1); s.AssertResponse(targetSlash, 2);
        Need(s.Events<OrderedSelectedActorDuelPaidEvent>().Length == 1 && s.Events<OrderedSelectedActorDuelIssuedEvent>().Length == 1,
            "The paid instruction and independent native use retain separate once-only receipts.");
        s.Cold();
        VerifyNativeTwoSlashResponseSet();
        VerifyEquipmentCostRunsNativeSilverLionRemoval();
    }

    private static void VerifyEquipmentCostRunsNativeSilverLionRemoval()
    {
        var s = Scene.Start("equipment-cost");
        var cost = s.View(0).Equipment.Single(card => card.Kind == CardKind.SilverLion).Id;
        Need(s.View(0).Hp == 5 && s.View(0).MaxHp == 6 &&
            s.G.CreateCardZoneDiagnostics().Single(card => card.CardId == cost).Location == CardLocation.Equipment(0),
            "The wounded owner starts with one genuine Silver Lion entity in its actual equipment zone.");
        s.Use(cost);
        s.Until(() => s.Events<SilverLionRemovedRecoveryEvent>().Any(fact => fact.PlayerSeat == 0) &&
            s.G.ResolutionStack.OfType<CardUseFrame>().Any(frame => frame.SelectedActorDuelOrigin?.OrderedPairPayment is not null));
        var payment = s.Events<OrderedSelectedActorDuelPaidEvent>().Single().Payment;
        var duel = s.G.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.SelectedActorDuelOrigin?.OrderedPairPayment is not null);
        Need(payment.CardId == cost && payment.PrintedKind == CardKind.SilverLion && payment.From == CardLocation.Equipment(0) &&
            payment.To == CardLocation.DiscardPile && duel.SourceSeat == 1 && duel.TargetSeats.SequenceEqual([2]) &&
            duel.PhysicalCardIds?.Count == 0 && duel.Action is { PhysicalCards.Count: 0 } && s.View(0).Hp == 6 &&
            s.Events<SilverLionRemovedRecoveryEvent>() is [{ PlayerSeat: 0, RecoveredAmount: 1, RemainingHp: 6 }] &&
            s.Events<RecoveryAppliedEvent>().Count(fact => fact.SourceSeat == 0 && fact.TargetSeat == 0 && fact.Amount == 1 && fact.RemainingHp == 6) == 1,
            "The real equipment discard freezes its original payment, invokes native Silver Lion removal recovery once, and independently issues the zero-entity Duel.");
        s.AssertCost(cost, CardLocation.Equipment(0)); s.Cold(); s.Finish();
        Need(s.View(0).Hp == 6 && s.Events<SilverLionRemovedRecoveryEvent>().Length == 1 &&
            s.Events<RecoveryAppliedEvent>().Count(fact => fact.TargetSeat == 0) == 1 &&
            s.Events<OrderedSelectedActorDuelPaidEvent>().Length == 1 && s.Events<OrderedSelectedActorDuelIssuedEvent>().Length == 1 &&
            s.Events<CardUseFinishedEvent>().Count(fact => fact.ResolutionId == duel.Id) == 1,
            "The paid equipment movement and recovery remain once-only across the actual native child return and cold replay: " + s.Diagnostic());
        s.AssertCost(cost, CardLocation.Equipment(0)); s.Cold();
    }

    private static void VerifyNativeTwoSlashResponseSet()
    {
        var s = Scene.Start("double-response"); var cost = s.View(0).Hand.Single(card => card.Kind == CardKind.Peach).Id;
        var originals = s.View(2).Hand.Where(card => card.Kind == CardKind.Slash).Select(card => card.Id).ToArray();
        var actorSlash = s.View(1).Hand.Single(card => card.Kind == CardKind.Slash).Id;
        Need(originals.Length == 2 && s.View(1).Hp == 1 && s.View(2).Hp == 1,
            "Native Wushuang has two actual opposing Slash entities and lethal pressure on both real Duel participants.");
        s.Use(cost);
        s.Until(() => s.Events<DuelResponseEvent>().Count(fact => fact.ResponderSeat == 2 && fact.UsedSlash) == 1);
        var duel = s.Events<OrderedSelectedActorDuelIssuedEvent>().Single().CardUseFrameId;
        Need(s.Events<DuelResponseEvent>().All(fact => fact.ResolutionId != duel || fact.ResponderSeat != 1 || !fact.UsedSlash),
            "The first actual Slash has not completed native Wushuang's two-Slash set or prematurely swapped the responder.");
        s.Cold();
        s.Until(() => s.Events<DuelResponseEvent>().Count(fact => fact.ResolutionId == duel && fact.ResponderSeat == 2 && fact.UsedSlash) == 2);
        s.Cold(); s.Finish();
        Need(s.Events<RequiredResponseProgressEvent>().Any(fact => fact.ResolutionId == duel && fact.ResponderSeat == 2 &&
                fact.ResponseCount == 2 && fact.RequiredResponseCount == 2) &&
            s.Events<DuelResponseEvent>().Where(fact => fact.ResolutionId == duel && fact.ResponderSeat == 2 && fact.UsedSlash)
                .All(fact => fact.SlashCardId.HasValue) &&
            s.Events<DuelResponseEvent>().Where(fact => fact.ResolutionId == duel && fact.ResponderSeat == 2 && fact.UsedSlash)
                .Select(fact => fact.SlashCardId!.Value).Order().SequenceEqual(originals.Order()) &&
            s.Events<DuelResponseEvent>().Any(fact => fact.ResolutionId == duel && fact.ResponderSeat == 1 && fact.UsedSlash && fact.SlashCardId == actorSlash) &&
            s.Events<DamageAppliedEvent>().Any(fact => fact.SourceSeat == 1 && fact.TargetSeat == 2 && fact.Amount == 1),
            "The real first Duel actor's classic Wushuang requires two distinct physical Slash responses before its own response and final native damage: " + s.Diagnostic());
        foreach (var id in originals) s.AssertResponse(id, 2);
        s.AssertResponse(actorSlash, 1); s.AssertCost(cost);
        Need(s.Events<CardUseFinishedEvent>().Count(fact => fact.ResolutionId == duel) == 1 &&
            s.Events<OrderedSelectedActorDuelPaidEvent>().Length == 1 && s.Events<OrderedSelectedActorDuelIssuedEvent>().Length == 1,
            "Cold restores between actual responses preserve the paid outer discard and finish the independent native Duel once.");
        s.Cold();
    }

    private static void VerifyDirectedNativeLegalSelections()
    {
        var normal = Scene.Start("normal");
        var action = normal.G.GetHumanLegalActions().Single(candidate => candidate.ProgramSkillId == Skill);
        Need(action.SelectableTargetSequences is { } pairs && pairs.Any(pair => pair.SequenceEqual([1, 2])) &&
            pairs.Any(pair => pair.SequenceEqual([2, 1])) && pairs.All(pair => pair.Count == 2 && pair.Distinct().Count() == 2 && !pair.Contains(0)),
            "Human and AI legal actions publish complete ordered other-male pairs, retaining both directions rather than sorting the chosen actors.");
        var s = Scene.Start("asymmetric-pair");
        var asymmetric = s.G.GetHumanLegalActions().Single(candidate => candidate.ProgramSkillId == Skill);
        Need(s.View(2).HandCount == 0 && s.View(2).Skills?.Any(skill => skill.ContentId == "classic:kongcheng") == true &&
            asymmetric.SelectableTargetSequences is { } legal && !legal.Any(pair => pair.SequenceEqual([1, 2])) &&
            legal.Any(pair => pair.SequenceEqual([2, 1])),
            "Actual native Kongcheng forbids the empty-handed second target but permits that same player as the first Duel actor.");
        var cost = s.View(0).Hand.Single(card => card.Kind == CardKind.Peach).Id;
        var aiPair = new SimpleAiBrain(0, 83, AiDecisionPolicy.CurrentVersion)
            .ChooseActiveSkillTargets(s.G.CreateSnapshot(0), asymmetric, [cost]);
        Need(asymmetric.SelectableTargetSequences!.Any(pair => pair.SequenceEqual(aiPair)),
            "The real public AI consumer chooses one complete published legal direction, never an invalid recombination of individually selectable seats.");
        var before = GameCheckpointJson.Serialize(s.G.CreateCheckpoint());
        var rejected = s.G.Submit(new UseProgramSkillCommand(0, Skill, Binding, [cost], [1, 2], s.G.Revision, s.P!.PromptId));
        Need(!rejected.Accepted && GameCheckpointJson.Serialize(s.G.CreateCheckpoint()) == before &&
            s.G.CreateCardZoneDiagnostics().Single(card => card.CardId == cost).Location == CardLocation.Hand(0),
            "Submitting a real prohibited direction rejects before paying or changing the command/revision/history/state.");
        s.Cold(); s.Use(cost, 2, 1);
        s.Until(() => s.G.ResolutionStack.OfType<CardUseFrame>().Any(frame => frame.SelectedActorDuelOrigin?.OrderedPairPayment is not null));
        var use = s.G.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.SelectedActorDuelOrigin?.OrderedPairPayment is not null);
        Need(use.SourceSeat == 2 && use.TargetSeats.SequenceEqual([1]) &&
            use.SelectedActorDuelOrigin?.OrderedPairPayment is { ActorSeat: 2, TargetSeat: 1 },
            "The accepted reverse command preserves actor 2 to target 1 in the actual independently declared native Duel.");
        s.Cold(); s.Finish(); s.AssertCost(cost); s.Cold();
    }

    public static void PaidDiscardSurvivesOwnerLossOrDeathAndDeadParticipantsCold()
    {
        foreach (var scene in new[] { "loss", "owner-death", "actor-death", "target-death" })
        {
            var s = Scene.Start(scene); var cost = s.View(0).Hand.Single(card => card.Kind == CardKind.Peach).Id;
            s.Use(cost); s.Until(() => s.P?.SkillPrompt?.SkillId == Watch && s.P.Choices.Any(choice => Action(choice) == "activate"));
            Need(s.Events<OrderedSelectedActorDuelPaidEvent>().Length == 1 && s.Events<CardUseDeclaredEvent>().All(fact => fact.CardKind != CardKind.Duel),
                "The actual one-card payment precedes its real discard observer and any later Duel issuance.");
            s.Cold(); s.Answer(choice => Action(choice) == "activate");
            if (scene is "actor-death" or "target-death")
            {
                s.Until(() => s.P?.PlayerSeat == 0 && s.P.Choices.Any(choice => choice.Targets.Count == 1));
                s.Answer(choice => choice.Targets.SequenceEqual([scene == "actor-death" ? 1 : 2]));
            }
            if (scene == "loss")
            {
                s.Until(() => s.View(0).Hp == 5 && s.P?.SkillPrompt?.SkillId == Watch && s.P.Choices.Any(choice => choice.Parameters.GetValueOrDefault("option-id") == "continue"));
                Need(s.View(0).Skills?.Any(skill => skill.ContentId == Skill) != true,
                    "An actual HP loss withdraws the conditional Lijian instance after its physical discard was paid.");
                s.Cold(); s.Answer(choice => choice.Parameters.GetValueOrDefault("option-id") == "continue");
            }
            else
            {
                var deadSeat = scene == "owner-death" ? 0 : scene == "actor-death" ? 1 : 2;
                s.Until(() => !s.View(deadSeat).IsAlive); s.Cold();
            }
            s.Finish();
            var issued = s.Events<OrderedSelectedActorDuelIssuedEvent>();
            Need(issued.Length == (scene is "loss" or "owner-death" ? 1 : 0),
                "Paid source loss/death preserves the remaining other-participant effect, while a dead actor or target opens no new Duel: " + s.Diagnostic());
            if (issued.Length == 1)
                Need(s.Events<CardUseDeclaredEvent>().Any(fact => fact.ResolutionId == issued[0].CardUseFrameId && fact.SourceSeat == 1 && fact.CardKind == CardKind.Duel) &&
                    s.Events<CardUseFinishedEvent>().Count(fact => fact.ResolutionId == issued[0].CardUseFrameId) == 1,
                    "A dead or skill-less original owner still receives its exact native child return once, without becoming the Duel's actor or target.");
            s.AssertCost(cost); Need(s.Events<OrderedSelectedActorDuelPaidEvent>().Length == 1,
                "Neither cold recovery nor source lifetime changes repeat the original selected-card cost."); s.Cold();
        }
    }

    private static string Action(PromptChoice choice) => choice.Parameters.GetValueOrDefault("program-action") ?? "";
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void VerifyParserRejectsUnpaidOrUnpairedOptIn()
    {
        const string paired = "\"participantMode\":\"orderedSelectedPair\",\"prohibitNullification\":true";
        foreach (var (members, cards) in new[] { ("\"participantMode\":\"orderedSelectedPair\"", 1),
            ("\"prohibitNullification\":true", 1), (paired.Replace(":true", ":false"), 1), (paired, 0) })
        {
            var rejected = false;
            try
            {
                SkillProgramCatalog.Load("{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion +
                    ",\"skills\":[{\"id\":\"fixture:ordered-duel-invalid\",\"revision\":1,\"activations\":[{\"id\":\"ordered\",\"minCards\":" + cards +
                    ",\"maxCards\":" + cards + ",\"minTargets\":2,\"maxTargets\":2,\"targetKind\":\"otherLivingMale\",\"usesPerTurn\":null,\"usesPerPhase\":1,\"effects\":[{\"op\":\"discardSelected\",\"target\":\"owner\",\"amount\":1},{\"op\":\"useSelectedActorDuel\",\"target\":\"owner\"," + members + "}]}]}]}",
                    "{\"schemaVersion\":" + SkillProgramCatalog.PresentationSchemaVersion + ",\"skills\":{\"fixture:ordered-duel-invalid\":{\"name\":\"fixture\",\"description\":\"fixture\"}}}");
            }
            catch (InvalidOperationException) { rejected = true; }
            Need(rejected, "Ordered real-use opt-in rejects unpaired flags, false prohibition and an unpaid zero-card activation.");
        }
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry, string scene)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot View(int seat) => G.CreateSnapshot(seat).Players.Single(player => player.Seat == seat);
        public T[] Events<T>() where T : IGameEvent => G.Events.Select(fact => fact.Payload).OfType<T>().ToArray();
        public void Accept(GameCommand command) { var result = G.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Actual command rejected."); }
        public void Answer(Func<PromptChoice, bool> predicate)
        { var prompt = P ?? throw new InvalidOperationException("No actual owner prompt."); Accept(new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.First(predicate).Id, G.Revision)); }
        public void Use(int cost, int first = 1, int second = 2) => Accept(new UseProgramSkillCommand(0, Skill, Binding, [cost], [first, second], G.Revision, P!.PromptId));
        public void Until(Func<bool> done)
        {
            for (var step = 0; step < 180; step++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } prompt)
                {
                    if (prompt.Kind == DecisionKind.RescueDying) { Answer(choice => choice.Parameters.GetValueOrDefault("response") is "pass" or "let-die"); continue; }
                    throw new InvalidOperationException("Unexpected actual Lijian boundary: " + Diagnostic());
                }
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Missing bounded actual Lijian boundary: " + Diagnostic());
        }
        public void Finish() => Until(() => !G.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.SkillId == Skill) &&
            !G.ResolutionStack.OfType<CardUseFrame>().Any(frame => frame.SelectedActorDuelOrigin?.OrderedPairPayment is not null));
        public void AssertCost(int id, CardLocation? source = null) => Need(G.CardMovements.Count(move => move.CardId == id && move.From == (source ?? CardLocation.Hand(0)) &&
            move.To == CardLocation.DiscardPile && move.Reason.Value == $"skill-program.{Skill}.{SkillProgramEffectOp.DiscardSelected}") == 1 &&
            G.CardMovements.All(move => move.CardId != id || move.To != CardLocation.Processing),
            "Diao Chan's original cost leaves its exact original owner zone for Discard once and never becomes a virtual Duel/response material.");
        public void AssertResponse(int id, int seat) => Need(G.CardMovements.Count(move => move.CardId == id && move.From == CardLocation.Hand(seat) && move.To == CardLocation.Processing) == 1 &&
            G.CardMovements.Count(move => move.CardId == id && move.From == CardLocation.Processing && move.To == CardLocation.DiscardPile) == 1,
            "Each real Duel response pays and clears its responding player's original Slash entity once.");
        public void Cold()
        {
            var checkpoint = G.CreateCheckpoint(); var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint)), registry);
            Need(restored.Revision == G.Revision && JsonSerializer.Serialize(restored.CreateCheckpoint().Commands) == JsonSerializer.Serialize(checkpoint.Commands), "Cold command history and revision match.");
            for (var viewer = 0; viewer < 4; viewer++) Need(JsonSerializer.Serialize(restored.CreateSnapshot(viewer)) == JsonSerializer.Serialize(G.CreateSnapshot(viewer)), "Cold viewer projections match.");
            G = restored;
        }
        public string Diagnostic() => JsonSerializer.Serialize(new { scene, Prompt = P is null ? null : new { P.Kind, P.PlayerSeat, Skill = P.SkillPrompt?.SkillId },
            Paid = Events<OrderedSelectedActorDuelPaidEvent>(), Issued = Events<OrderedSelectedActorDuelIssuedEvent>(), Uses = Events<CardUseDeclaredEvent>(),
            Responses = Events<DuelResponseEvent>(), Frames = G.ResolutionStack.Select(frame => new { frame.Id, frame.Kind }) });
        public static Scene Start(string scene)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
            var pressure = scene is "normal" or "double-response";
            ShanheStartingCard[] targetCards = scene == "asymmetric-pair" ? [] : scene == "double-response"
                ? [new(CardKind.Slash, Suit.Spade, 5), new(CardKind.Slash, Suit.Club, 6), new(CardKind.Nullification, Suit.Spade, 12)]
                : [new(CardKind.Slash, Suit.Spade, 5), new(CardKind.Nullification, Suit.Spade, 12)];
            var setup = new ShanheBattleSetup(83, 1, "fixture-lijian-" + scene, Owner, scene == "equipment-cost" ? 5 : 6, 0, 0, 0, 0, [],
                [new(CardKind.Peach, Suit.Heart, 3), new(CardKind.Nullification, Suit.Club, 11)],
                scene == "equipment-cost" ? [new(CardKind.SilverLion, Suit.Club, 1)] : [],
                [new(Target, Hp: pressure ? 1 : null) { StartingHand = targetCards },
                 new(Target) { StartingHand = [new(CardKind.Nullification, Suit.Club, 13)] }],
                [new(Actor, Hp: pressure ? 1 : null) { StartingHand = [new(CardKind.Slash, Suit.Club, 4), new(CardKind.Nullification, Suit.Spade, 10)] }], new string('a', 64));
            var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var result = new Scene(game, registry, scene); result.Accept(new StartGameCommand());
            result.Until(() => result.P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }); return result;
        }
    }
    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-lijian", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in new[] { Skill, Wushuang, Liyu, "classic:wushuang", "classic:jizhi", "classic:kongcheng" }) builder.AddSkill(Production.Value.GetSkill(id));
            if (scene == "equipment-cost")
                builder.AddCard(Production.Value.Cards.Values.First(card => card.LegacyKind == CardKind.SilverLion));
            object[] watchEffects = scene is "actor-death" or "target-death"
                ? [new { op = "selectTarget", target = "owner", targetKind = "otherLiving" }, new { op = "loseHp", target = "selectedTarget", amount = 12 }]
                : scene == "owner-death" ? [new { op = "loseHp", target = "owner", amount = 6 }]
                : [new { op = "loseHp", target = "owner", amount = 1 }, new { op = "chooseOption", target = "owner", resultBind = "continue", options = new[] { new { id = "continue" } } }];
            var definitions = new object[] {
                new { id = Driver, revision = 1, modifiers = new[] { new { id = "keep", query = "handLimit", operation = "add", value = 20, priority = 0 } } },
                new { id = Gate, revision = 1, hpSkillGrants = new[] { new { id = "full-health-lijian", skillId = Skill, minimumHp = 6 } } },
                new { id = Watch, revision = 1, triggers = new[] { new { id = "paid-original-discard", window = "cardsMoved", subject = "owner", sourceZones = new[] { "hand" },
                    movementReasons = new[] { $"skill-program.{Skill}.{SkillProgramEffectOp.DiscardSelected}" },
                    movementOccurrence = "perBatch", optional = true, effects = watchEffects } } }
            };
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实原牌夹具", description = "固定实体和自然阶段" },
                [Gate] = new { name = "真实体力来源", description = "满体力授予原版离间" },
                [Watch] = scene is not ("owner-death" or "actor-death" or "target-death") ? new { name = "实际弃牌子流程", description = "只在本次原牌已弃置后发生", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } :
                    (object)new { name = "实际弃牌子流程", description = "只在本次原牌已弃置后发生" } };
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new(Owner, "真实貂蝉技能拥有者", "", scene == "loss" ? Gate : Skill, "qun", 6,
                new[] { Driver }.Concat(scene is "loss" or "owner-death" or "actor-death" or "target-death" ? new[] { Watch } : []).ToArray(), GeneralGender.Female));
            builder.AddGeneral(new(Actor, "真实第一男性使用者", "", scene == "double-response" ? "classic:wushuang" : Wushuang,
                "qun", 12, ["classic:jizhi", Liyu], GeneralGender.Male));
            builder.AddGeneral(new(Target, "真实第二男性目标", "", "standard:none", "wei", 12,
                scene == "asymmetric-pair" ? ["classic:kongcheng"] : [], GeneralGender.Male));
            builder.AddDeck(new(Deck, "固定自然零摸牌", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64)
                .Select(index => new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, index % 13 + 1)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "真实离间有序原生决斗", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Actor, Target], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
