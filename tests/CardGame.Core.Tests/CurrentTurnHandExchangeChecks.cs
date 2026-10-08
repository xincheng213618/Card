using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CurrentTurnHandExchangeChecks
{
    private const string Junbing = "ol:junbing";
    private const string Watch = "fixture:current-turn-exchange-watch";
    private const string HpWatch = "fixture:current-turn-exchange-hp";
    private const string Neutral = "fixture:current-turn-exchange-neutral";
    private const string Owner = "fixture:current-turn-exchange-owner";
    private const string Mode = "identity:classic-current-turn-exchange";
    private static string Reason(string part) => $"skill-program.{Junbing}.{SkillProgramEffectOp.OfferCurrentTurnHandExchange}.{part}";

    public static void ForeignGiftFreezesQuantityAndReturnsThroughNativeChildren()
    {
        foreach (var equipment in new[] { false, true })
        {
            var (game, registry) = Start(equipment: equipment);
            int? lion = null;
            if (equipment)
            {
                var equip = game.GetHumanLegalActions().First(action => action.CardId is not null && action.Kind == LegalActionKind.Equip);
                lion = equip.CardId;
                Accept(game, new PlayCardCommand(0, lion!.Value, equip.TargetSeats ?? [], game.Revision, P(game)!.PromptId,
                    equip.PlayedCardKind, equip.TargetCardId));
                Reach(game, prompt => prompt is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
                Require(game.CreateSnapshot(0).Players[0].Equipment.Any(card => card.Id == lion), "A true physical Silver Lion is equipped before the exchange.");
            }
            EndHumanPlay(game);
            Reach(game, prompt => IsExchange(prompt) && prompt.PlayerSeat == 0);
            var ownId = Exchange(game).Id;
            Answer(game, choice => choice.Parameters.GetValueOrDefault("option") == "accept");
            Reach(game, prompt => IsExchange(prompt) && prompt.PlayerSeat == 1);
            Require(Facts<CurrentTurnHandExchangeResolvedEvent>(game).Single(fact => fact.FrameId == ownId).Outcome == "self-draw",
                "The owner's actual turn only draws one and does not manufacture a self gift or self return.");
            var foreign = Exchange(game); var foreignId = foreign.Id;
            var instance = foreign.SkillInstanceId; var hash = foreign.GameplayHash;
            var offered = foreign.CurrentTurnHandExchange!;
            Require(offered.ParticipantSeat == 1 && foreign.OwnerSeat == 0 && foreign.WindowContext!.SourceSeat == 1 &&
                    foreign.WindowContext.TargetSeat == 1 && P(game)!.PlayerSeat == 1,
                "The foreign actual turn holder privately decides whether to draw; the skill owner does not answer on its behalf.");
            AssertPrivate(game, 1);
            Reject(game, new AnswerPromptCommand(0, P(game)!.PromptId, P(game)!.Choices[0].Id, game.Revision));
            game = Cold(game, registry);
            Accept(game, new AdvanceOneStepCommand(game.Revision));
            Reach(game, prompt => IsWatch(prompt));
            foreign = Exchange(game);
            var gift = foreign.CurrentTurnHandExchange!;
            var frozen = gift.FrozenGivenCount;
            Require(gift.Stage == CurrentTurnHandExchangeStage.GiftChildren && frozen == (equipment ? 1 : 2) &&
                    gift.GivenCardIds.Count == frozen && gift.GivenCardIds.Distinct().Count() == frozen &&
                    foreign.InstructionIndex == 1 && foreign.SkillInstanceId == instance && foreign.GameplayHash == hash &&
                    foreign.PendingMovementContinuation is not null && !Facts<CurrentTurnHandExchangeReturnStartedEvent>(game).Any(fact => fact.FrameId == foreignId),
                "One actual draw drains first, then the entire foreign hand moves once and freezes its exact count before any return selection.");
            var moved = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(frame => frame.Batch.ParentFrameId == foreignId);
            Require(moved.ResumeProgramFrameId is null && moved.Batch.AwaitingProgramFrameId == foreignId &&
                    moved.Batch.OriginOwnerSeat == 0 && moved.Batch.OriginSkillId == Junbing &&
                    moved.Batch.OriginSkillInstanceId == instance && moved.Batch.Movements.Select(record => record.CardId).Order().SequenceEqual(gift.GivenCardIds.Order()) &&
                    moved.Batch.Movements.All(record => record.From == CardLocation.Hand(1) && record.To == CardLocation.Hand(0) && record.Reason.Value == Reason("gift")),
                "The real atomic whole-hand gift belongs to its exact original program and typed awaited movement return.");
            AssertPrivate(game, 0);
            RejectWrongActor(game);
            game = Cold(game, registry);
            Answer(game, choice => choice.Parameters.GetValueOrDefault("option-id") == "continue");
            Reach(game, prompt => IsExchange(prompt) && prompt.PlayerSeat == 0 && Exchange(game).Id == foreignId);
            foreign = Exchange(game); var returned = foreign.CurrentTurnHandExchange!;
            Require(returned.Stage == CurrentTurnHandExchangeStage.SelectingReturn && returned.FrozenGivenCount == frozen &&
                    returned.RequiredReturnCount == frozen && returned.CandidateCardIds.Count == game.CreateSnapshot(0).Players[0].HandCount +
                        game.CreateSnapshot(0).Players[0].Equipment.Count &&
                    returned.CandidateCardIds.Count > frozen && P(game)!.Choices.Count == returned.CandidateCardIds.Count &&
                    game.CardMovements.Count(record => record.Reason.Value == $"skill-program.{Watch}.Draw") == 1,
                "A real gift child increases the owner's inventory, but the return still owes the original gift count and offers every current HE entity.");
            AssertPrivate(game, 0);
            AssertFrozen(game, returned);
            var retained = SnapshotJson.Serialize(game.CreateSnapshot(0));
            var retainedSnapshot = game.CreateSnapshot(0);
            RejectWrongActor(game);
            game = Cold(game, registry);
            var selected = new List<int>();
            for (var pick = 0; pick < frozen; pick++)
            {
                var choice = pick == 0 && lion is not null ? P(game)!.Choices.Single(choice => choice.Cards.SequenceEqual([lion.Value])) : P(game)!.Choices[0];
                selected.Add(choice.Cards.Single());
                Answer(game, candidate => candidate.Id == choice.Id);
                if (pick + 1 < frozen)
                {
                    Require(Exchange(game).CurrentTurnHandExchange!.SelectedCardIds.SequenceEqual(selected) &&
                            !game.CardMovements.Any(record => record.Reason.Value == Reason("return")),
                        "A partial selection cannot transfer any entity, finish early or repeat a previously selected card.");
                    game = Cold(game, registry);
                }
            }
            if (equipment)
            {
                Reach(game, prompt => prompt.SkillPrompt?.SkillId == HpWatch);
                foreign = Exchange(game);
                Require(foreign.CurrentTurnHandExchange!.Stage == CurrentTurnHandExchangeStage.ReturnChildren &&
                        foreign.CurrentTurnHandExchange.FrozenGivenCount == frozen && foreign.PendingMovementContinuation is not null &&
                        !Facts<CurrentTurnHandExchangeResolvedEvent>(game).Any(fact => fact.FrameId == foreignId) &&
                        game.CreateSnapshot(0).Players[0].Hp == 4,
                    "Removing the chosen actual equipment pays once, then its genuine recovery child suspends completion of the original exchange.");
                AssertPrivate(game, 0);
                RejectWrongActor(game);
                game = Cold(game, registry);
                Answer(game, choice => choice.Parameters.GetValueOrDefault("option-id") == "continue");
            }
            ReachResolved(game, foreignId);
            Require(SnapshotJson.Serialize(retainedSnapshot) == retained &&
                    Facts<CurrentTurnHandExchangeDrawIssuedEvent>(game).Count(fact => fact.FrameId == foreignId && fact.ActualCount == 1) == 1 &&
                    Facts<CurrentTurnHandExchangeGiftIssuedEvent>(game).Count(fact => fact.FrameId == foreignId && fact.FrozenGivenCount == frozen) == 1 &&
                    Facts<CurrentTurnHandExchangeReturnIssuedEvent>(game).Count(fact => fact.FrameId == foreignId && fact.FrozenGivenCount == frozen && fact.ActualCount == frozen) == 1 &&
                    Facts<CurrentTurnHandExchangeResolvedEvent>(game).Count(fact => fact.FrameId == foreignId && fact.Outcome == "returned" && fact.ReturnedCount == frozen) == 1 &&
                    game.CardMovements.Count(record => record.Reason.Value == Reason("gift")) == frozen &&
                    game.CardMovements.Count(record => record.Reason.Value == Reason("return")) == frozen &&
                    selected.All(id => game.CardMovements.Count(record => record.CardId == id && record.To == CardLocation.Hand(1) && record.Reason.Value == Reason("return")) == 1),
                "Cold replay and every real child return preserve earlier frozen views and one draw, one entire gift, one exact physical return, and one completion.");
            if (equipment)
                Require(game.CardMovements.Single(record => record.CardId == lion && record.Reason.Value == Reason("return")).From == CardLocation.Equipment(0) &&
                        Facts<RecoveryAppliedEvent>(game).Count(fact => fact.SourceSeat == 0 && fact.TargetSeat == 0 && fact.Amount == 1) == 1,
                    "The ordinary return accepts equipped material and runs its native removal recovery exactly once.");
            _ = Cold(game, registry);
        }
        ExerciseAiOwnerReturn();
    }

    private static void ExerciseAiOwnerReturn()
    {
        var (game, registry) = Start(aiOwner: true);
        var owner = game.CreateSnapshot(0).Players.Single(player => player.GeneralId == Owner).Seat;
        Require(owner != 0, "The fixed draft assigns the single actual exchange skill to a genuine AI owner.");
        EndHumanPlay(game);
        Reach(game, prompt => IsExchange(prompt) && prompt.PlayerSeat == 0);
        var id = Exchange(game).Id;
        Require(Exchange(game).OwnerSeat == owner && Exchange(game).CurrentTurnHandExchange!.ParticipantSeat == 0,
            "A human actual turn holder receives the foreign AI owner's private offer.");
        Answer(game, choice => choice.Parameters.GetValueOrDefault("option") == "accept");
        Reach(game, prompt => IsWatch(prompt));
        AssertPrivate(game, owner);
        game = Cold(game, registry);
        Accept(game, new AdvanceOneStepCommand(game.Revision));
        Reach(game, prompt => IsExchange(prompt) && prompt.PlayerSeat == owner);
        var receipt = Exchange(game).CurrentTurnHandExchange!;
        Require(receipt is { FrozenGivenCount: 2, RequiredReturnCount: 2, Stage: CurrentTurnHandExchangeStage.SelectingReturn } &&
                receipt.CandidateCardIds.Count == 4 && game.CreateSnapshot(0).PendingDecision is null,
            "The real AI owner selects two of all four current private HE cards, using the original paid quantity instead of its changed inventory.");
        AssertPrivate(game, owner);
        game = Cold(game, registry);
        Accept(game, new AdvanceOneStepCommand(game.Revision));
        Require(Exchange(game).CurrentTurnHandExchange!.SelectedCardIds.Count == 1 &&
                !Facts<CurrentTurnHandExchangeReturnIssuedEvent>(game).Any(fact => fact.FrameId == id),
            "One true AI step consumes one exact private card choice without an early partial return.");
        game = Cold(game, registry);
        Accept(game, new AdvanceOneStepCommand(game.Revision));
        ReachResolved(game, id);
        Require(Facts<CurrentTurnHandExchangeReturnIssuedEvent>(game).Single(fact => fact.FrameId == id) is
                    { FrozenGivenCount: 2, ActualCount: 2, ParticipantSeat: 0 } &&
                Facts<CurrentTurnHandExchangeResolvedEvent>(game).Count(fact => fact.FrameId == id && fact.ReturnedCount == 2) == 1 &&
                game.CreateSnapshot(0).Players[0].HandCount == 2,
            "Actual AI selection and cold recovery return both paid entities to the original human participant exactly once.");
        _ = Cold(game, registry);
    }

    public static void SelfAndDeclineKeepOriginalEntities()
    {
        RejectInvalidPrograms();
        foreach (var accept in new[] { false, true })
        {
            var (game, registry) = Start();
            var original = game.CreateSnapshot(0).Players[0].Hand.Select(card => card.Id).ToArray();
            EndHumanPlay(game);
            Reach(game, prompt => IsExchange(prompt) && prompt.PlayerSeat == 0);
            var id = Exchange(game).Id;
            Require(original.Length == 1 && Exchange(game).WindowContext!.Facts!.EventTargetHandCount == original.Length,
                "The own TurnEnding candidate retains its actual pre-offer one-card hand count instead of falling back to the boundary's default zero.");
            AssertPrivate(game, 0);
            game = Cold(game, registry);
            Require(Exchange(game).WindowContext!.Facts!.EventTargetHandCount == 1 &&
                    Exchange(game).CurrentTurnHandExchange!.Stage == CurrentTurnHandExchangeStage.Offered,
                "Accepted-prefix cold replay preserves the genuine own-turn one-card eligibility fact before any draw or exchange.");
            RejectWrongActor(game);
            Answer(game, choice => choice.Parameters.GetValueOrDefault("option") == (accept ? "accept" : "decline"));
            ReachResolved(game, id);
            Require(original.All(card => game.CreateSnapshot(0).Players[0].Hand.Any(entity => entity.Id == card)) &&
                    game.CreateSnapshot(0).Players[0].HandCount == original.Length + (accept ? 1 : 0) &&
                    Facts<CurrentTurnHandExchangeDrawIssuedEvent>(game).Count(fact => fact.FrameId == id) == (accept ? 1 : 0) &&
                    !Facts<CurrentTurnHandExchangeGiftIssuedEvent>(game).Any(fact => fact.FrameId == id) &&
                    !Facts<CurrentTurnHandExchangeReturnIssuedEvent>(game).Any(fact => fact.FrameId == id) &&
                    Facts<CurrentTurnHandExchangeResolvedEvent>(game).Single(fact => fact.FrameId == id).Outcome == (accept ? "self-draw" : "declined"),
                "Decline spends nothing; own-turn acceptance draws one actual entity without moving existing entities between the same hand.");
            _ = Cold(game, registry);
        }
    }

    public static void GiftChildExhaustionRetainsTheOriginalGivenCount()
    {
        var (game, registry) = Start(shortfall: true);
        EndHumanPlay(game);
        Reach(game, prompt => IsExchange(prompt) && prompt.PlayerSeat == 0);
        Answer(game, choice => choice.Parameters.GetValueOrDefault("option") == "accept");
        Reach(game, prompt => IsExchange(prompt) && prompt.PlayerSeat == 1);
        var id = Exchange(game).Id;
        Accept(game, new AdvanceOneStepCommand(game.Revision));
        Reach(game, prompt => IsWatch(prompt));
        Require(Exchange(game).CurrentTurnHandExchange is { FrozenGivenCount: 2, Stage: CurrentTurnHandExchangeStage.GiftChildren },
            "The real two-card gift is already irrevocably paid before the child consumes its recipient's inventory.");
        game = Cold(game, registry);
        Answer(game, choice => choice.Parameters.GetValueOrDefault("option-id") == "continue");
        ReachResolved(game, id);
        Require(game.CreateSnapshot(0).Players[0].HandCount == 0 && game.CreateSnapshot(0).Players[0].Equipment.Count == 0 &&
                Facts<CurrentTurnHandExchangeReturnStartedEvent>(game).Single(fact => fact.FrameId == id) is
                    { FrozenGivenCount: 2, AvailableCount: 0, RequiredReturnCount: 0 } &&
                Facts<CurrentTurnHandExchangeReturnIssuedEvent>(game).Single(fact => fact.FrameId == id) is { FrozenGivenCount: 2, ActualCount: 0 } &&
                Facts<CurrentTurnHandExchangeResolvedEvent>(game).Single(fact => fact.FrameId == id) is { Outcome: "returned", FrozenGivenCount: 2, ReturnedCount: 0 } &&
                game.CardMovements.Count(record => record.Reason.Value == Reason("gift")) == 2 &&
                !game.CardMovements.Any(record => record.Reason.Value == Reason("return")),
            "After genuine child exhaustion the recipient returns every payable entity (none), preserves the original two-card invoice, and completes once without a fabricated payment or cross-turn debt.");
        _ = Cold(game, registry);
    }

    private static ProgramSkillFrame Exchange(GameEngine game) => game.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.CurrentTurnHandExchange is not null);
    private static bool IsExchange(PendingDecision prompt) => prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "current-turn-hand-exchange");
    private static bool IsWatch(PendingDecision prompt) => prompt.SkillPrompt?.SkillId == Watch;
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(prompt => prompt is not null);
    private static IEnumerable<T> Facts<T>(GameEngine game) => game.Events.Select(item => item.Payload).OfType<T>();
    private static void EndHumanPlay(GameEngine game) => Accept(game, new EndPlayPhaseCommand(0, game.Revision, P(game)!.PromptId));
    private static void Answer(GameEngine game, Func<PromptChoice, bool> choose)
    {
        var prompt = P(game)!;
        Require(prompt.PlayerSeat == 0, "Only the real human may answer a player-owned prompt.");
        Accept(game, new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.First(choose).Id, game.Revision));
    }
    private static void Step(GameEngine game)
    {
        var prompt = P(game);
        if (prompt is { PlayerSeat: 0 })
        {
            if (prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"))
                Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards");
            else if (prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("option-id") == "continue"))
                Answer(game, choice => choice.Parameters.GetValueOrDefault("option-id") == "continue");
            else throw new InvalidOperationException("Unexpected exchange boundary: " + JsonSerializer.Serialize(prompt));
        }
        else Accept(game, new AdvanceOneStepCommand(game.Revision));
    }
    private static void Reach(GameEngine game, Func<PendingDecision, bool> expected)
    {
        for (var step = 0; step < 120; step++)
        {
            if (P(game) is { } prompt && expected(prompt)) return;
            Step(game);
        }
        throw new InvalidOperationException("The fixed exchange fixture missed its small native boundary: " + JsonSerializer.Serialize(P(game)));
    }
    private static void ReachResolved(GameEngine game, long id)
    {
        for (var step = 0; step < 120; step++)
        {
            if (Facts<CurrentTurnHandExchangeResolvedEvent>(game).Any(fact => fact.FrameId == id)) return;
            Step(game);
        }
        throw new InvalidOperationException("The already-paid exchange failed to return to its original ending cursor.");
    }
    private static void AssertPrivate(GameEngine game, int actor) => Require(P(game) is { IsPrivate: true } prompt && prompt.PlayerSeat == actor &&
        Enumerable.Range(0, 4).Where(seat => seat != actor).All(seat => game.CreateSnapshot(seat).PendingDecision is null &&
            game.CreateSnapshot(seat).Players[actor].Hand.Count == 0), "Private choices and actual hand identities remain visible only to their true selecting actor.");
    private static void AssertFrozen(GameEngine game, ProgramCurrentTurnHandExchangeReceipt receipt)
    {
        var prompt = P(game)!;
        foreach (var list in new[] { receipt.GivenCardIds, receipt.CandidateCardIds, prompt.Choices[0].Cards, prompt.Choices[0].Targets })
        {
            var frozen = false;
            try { ((IList<int>)list)[0] = -1; } catch (NotSupportedException) { frozen = true; }
            Require(frozen, "Private receipt and prepared nested choice collections must reject observer mutation.");
        }
        var immutable = false;
        try { ((IDictionary<string, string>)prompt.Choices[0].Parameters)["frame-id"] = "-1"; } catch (NotSupportedException) { immutable = true; }
        Require(immutable, "Prepared exact-answer parameters are frozen as well as the entity collections.");
    }
    private static void RejectWrongActor(GameEngine game) => Reject(game,
        new AnswerPromptCommand(1, P(game)!.PromptId, P(game)!.Choices[0].Id, game.Revision));
    private static void Reject(GameEngine game, GameCommand command)
    {
        var before = State(game); var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(!result.Accepted && result.Error is not null && State(game) == before,
            "Wrong actor input rejects atomically without changing prepared views, physical payment, private selection or accepted command history.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "The real exchange command was rejected.");
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack), game.CardMovements,
        Facts = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(),
        Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics()
    });
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(restored) == State(game), "Accepted-prefix replay reconstructs all private actors, frozen invoices, exact entities, native children and paid cursors.");
        return restored;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static void RejectInvalidPrograms()
    {
        const string skill = "fixture:exchange-loader";
        var valid = JsonNode.Parse($$$"""
          {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"{{{skill}}}","revision":1,"triggers":[
            {"id":"offer","window":"turnEnding","subject":"owner","turnOwnerScope":"otherLiving","optional":false,
             "condition":{"kind":"compare","left":{"kind":"eventTargetHandCount"},"operator":"lessThanOrEqual","right":{"kind":"integerConstant","value":1}},
             "effects":[{"op":"offerCurrentTurnHandExchange","target":"owner"}]}]}]}
          """)!;
        var presentation = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
            skills = new Dictionary<string, object> { [skill] = new { name = "共享交换", description = "真实回合边界" } } });
        foreach (var mutation in new Action<JsonObject>[]
        {
            trigger => trigger["optional"] = true,
            trigger => trigger["subject"] = "any",
            trigger => trigger["condition"] = new JsonObject { ["kind"] = "always" },
            trigger => trigger["condition"]!["right"]!["value"] = 2,
            trigger => trigger["effects"]!.AsArray().Add(JsonNode.Parse("{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}"))
        })
        {
            var invalid = JsonNode.Parse(valid.ToJsonString())!;
            mutation(invalid["skills"]![0]!["triggers"]![0]!.AsObject());
            try { _ = SkillProgramCatalog.Load(invalid.ToJsonString(), presentation); }
            catch (InvalidOperationException) { continue; }
            throw new InvalidOperationException("A widened exchange actor, quantity gate, optional wrapper or later unpaid tail must fail strict loading.");
        }
    }

    private static (GameEngine, ContentRegistry) Start(bool equipment = false, bool shortfall = false, bool aiOwner = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(equipment, shortfall));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4
        }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, prompt => prompt is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(game, new SelectGeneralCommand(0, aiOwner ? "fixture:current-turn-exchange-peer-1" : Owner, game.Revision, P(game)!.PromptId));
        Reach(game, prompt => prompt is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        return (game, registry);
    }

    private sealed class Fixture(bool equipment, bool shortfall) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:current-turn-hand-exchange", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var afterWatch = shortfall ? """
              {"op":"selectOwnedCards","target":"owner","numberExpression":"allOwnedZoneCards","zones":["hand","equipment"],"resultBind":"spent"},
              {"op":"moveBoundCards","target":"owner","sourceBind":"spent","destination":"discardPile","awaitMovementTriggers":true}
              """ : "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}";
            var catalog = SkillProgramCatalog.Load($$$"""
              {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
                {"id":"{{{Watch}}}","revision":1,"triggers":[{"id":"gift-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],
                  "movementReasons":["{{{Reason("gift")}}}"],"movementOccurrence":"perBatch","optional":false,"effects":[
                  {"op":"chooseOption","target":"owner","resultBind":"gift-watch","options":[{"id":"continue"}]},{{{afterWatch}}}]}]},
                {"id":"{{{HpWatch}}}","revision":1,"triggers":[{"id":"native-recovery-child","window":"afterHpRecovered","subject":"owner","optional":false,
                  "effects":[{"op":"chooseOption","target":"owner","resultBind":"recovery-watch","options":[{"id":"continue"}]}]}]}]}
              """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Watch, HpWatch }.ToDictionary(id => id, id => new
                { name = id, description = "真实交换子窗", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }) }));
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, id, "原生交换观察") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddSkill(new(Neutral, "无技能角色", "稳定小牌堆"));
            builder.AddGeneral(new(Owner, "真实郡兵持有人", "supporter", Junbing, "wei", 4, [Watch, HpWatch]) { InitialHp = equipment ? 2 : null });
            var peers = Enumerable.Range(1, 3).Select(index => $"fixture:current-turn-exchange-peer-{index}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "原回合参与者", "supporter", Neutral, "wei", 4));
            const string deck = "fixture:current-turn-exchange-deck";
            builder.AddDeck(new(deck, "固定小真实实体牌堆", 1, 0, [])
            { PhysicalCards = Enumerable.Range(0, 24).Select(_ => new ContentDeckPhysicalCard(equipment ? "classic:silver-lion" : "standard:dodge", Suit.Heart, 7)).ToArray() });
            builder.AddMode(new(Mode, "共享实际回合全手牌交换", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, deck,
                GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
