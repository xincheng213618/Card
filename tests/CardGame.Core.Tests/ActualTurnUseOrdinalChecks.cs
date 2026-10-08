using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ActualTurnUseOrdinalChecks
{
    private const string Driver = "fixture:actual-turn-ordinal-driver";
    private const string Witness = "fixture:actual-turn-ordinal-witness";
    private const string Peer = "fixture:actual-turn-ordinal-peer";
    private const string Owner = "fixture:actual-turn-ordinal-owner";
    private const string Mode = "identity:actual-turn-ordinal-fixture";

    public static void FrozenDeclarationOrderCountsOnlyActualUsesAndColdRestores()
    {
        ValidateCondition();
        var (game, registry) = Start();
        ReachPlay(game);
        var draw = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.DrawTwo &&
            action.ConversionSource?.BindingId == "draw");
        Accept(game, new PlayCardCommand(0, draw.CardId!.Value, draw.TargetSeats, game.Revision,
            Prompt(game)!.PromptId, draw.PlayedCardKind, draw.TargetCardId) { ConversionSource = draw.ConversionSource });
        Reach(game, () => Prompt(game) is { PlayerSeat: 0, Kind: DecisionKind.Nullification } prompt &&
            prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("conversion-binding-id") == "counter"));
        var counter = Prompt(game)!.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("conversion-binding-id") == "counter");
        var counterCard = counter.Cards.Single();
        Cold(game, registry);
        Answer(game, counter);
        CompleteSecondWithNestedThird(game, registry, nullification: true);
        Require(Facts<CurrentTurnCardUseKindsRecordedEvent>(game).Where(fact => fact.ActorSeat == 0)
                .Select(fact => fact.ActualTurnActorUseOrdinal).SequenceEqual(new int?[] { 1, 2, 3 }) &&
            Facts<CurrentTurnCardUseKindsRecordedEvent>(game).Count(fact => fact.ActorSeat == 0 && fact.IsNullificationUse) == 1 &&
            game.CardMovements.Count(move => move.CardId == counterCard && move.From == CardLocation.Hand(0) &&
                move.To == CardLocation.Processing) == 1 &&
            game.CardMovements.Count(move => move.CardId == counterCard && move.From == CardLocation.Processing &&
                move.To == CardLocation.DiscardPile) == 1,
            "A genuine paid Nullification occupies declaration slot two and is paid and cleaned up once.");
        VerifyResponseAndActualTurnIsolation(game, registry);
        VerifyOwnSlashDefenseDodge();
        VerifyGroupDodgeIsOnlyAResponse();

        var (legacy, legacyRegistry) = Start(legacyPair: true);
        PrepareLegacyPair(legacy);
        var declared = Facts<CardUseDeclaredEvent>(legacy).Last(eventItem => eventItem.CardKind == CardKind.Slash);
        var legacyUse = legacy.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.Id == declared.ResolutionId);
        Require(legacyUse is { CardId: 0, Action: null, PhysicalCardIds.Count: 0 } &&
            Facts<CurrentTurnCardUseKindsRecordedEvent>(legacy).Single(fact => fact.OriginFrameId == legacyUse.Id) is
                { CardActionId: null, ActualTurnActorUseOrdinal: 2 },
            "The real legacy Slash is declared without an Action and occupies slot two before completion normalization.");
        Cold(legacy, legacyRegistry);
        Continue(legacy, "legacy-live");
        CompleteSecondWithNestedThird(legacy, legacyRegistry, nullification: false);
        Require(Facts<CardUseFinishedEvent>(legacy).Count(fact => fact.ResolutionId == declared.ResolutionId &&
                fact.CardId == 0 && fact.CardKind == CardKind.Slash) == 1,
            "The originally Action-null second use reaches its genuine whole-use completion once.");

        var (old, oldRegistry) = Start(ordinal: false, legacyPair: true);
        PrepareLegacyPair(old);
        Cold(old, oldRegistry);
        Continue(old, "legacy-live");
        ReachPlay(old);
        var oldFacts = Facts<CurrentTurnCardUseKindsRecordedEvent>(old).ToArray();
        Require(oldFacts.Length == 2 && oldFacts.All(fact => fact.ActualTurnActorUseOrdinal is null &&
                !JsonSerializer.Serialize(fact).Contains("ActualTurnActorUseOrdinal", StringComparison.Ordinal)) &&
            oldFacts.Single(fact => fact.CardActionId is null).CardCategoryMask == 1 &&
            !Facts<CardActionAcceptedEvent>(old).Any(fact => fact.Action.Type == CardActionType.Use &&
                fact.Action.EffectiveKind == CardKind.Slash && fact.Action.PhysicalCards.Count == 0),
            "A use-kinds-only registry keeps its old scalar event shape and Action-null virtual history.");
        Cold(old, oldRegistry);
    }

    private static void CompleteSecondWithNestedThird(GameEngine game, ContentRegistry registry, bool nullification)
    {
        ReachOption(game, "second-before");
        var second = Window(game, "second-before");
        var originalAction = second.Action.ActionId;
        Require(FrozenFacts(second, "second") is
                { ActualTurnActorUseOrdinal: 2, CardActionActorIsCurrentTurn: true } &&
            second.Action.Type == (nullification ? CardActionType.Response : CardActionType.Use) &&
            (nullification ? second.CompletedResponseReturn?.Kind == ProgramCompletedResponseKind.Nullification :
                second.Continuation == ProgramCardContinuation.CompletedSlash),
            "The second completed actual use owns one frozen declaration ordinal and its exact typed return.");
        Require(game.CreateSnapshot(1).PendingDecision is null,
            "The public ordinal does not expose the observer's private choices to another viewer.");
        var copy = Cold(game, registry);
        foreach (var branch in new[] { game, copy })
        {
            Continue(branch, "second-before");
            SelectTarget(branch, 0);
            var material = SelectEquipment(branch, "nested-equipment");
            ReachOption(branch, "third");
            var third = Window(branch, "third");
            var retained = branch.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(frame => frame.Id == second.Id);
            var suspended = branch.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame =>
                frame.SkillId == Witness && frame.WindowContext?.ParentFrameId == second.Id);
            Require(third.Action.ActionId != originalAction && third.Action.ActorSeat == 0 &&
                FrozenFacts(third, "third").ActualTurnActorUseOrdinal == 3 &&
                retained.Action.ActionId == originalAction && FrozenFacts(retained, "second").ActualTurnActorUseOrdinal == 2 &&
                suspended.WindowContext?.Facts?.ActualTurnActorUseOrdinal == 2 &&
                suspended.PendingMovementContinuation is { SubjectSeat: 0 } &&
                third.Action.PhysicalCards.Single().CardId == material,
                "A genuine nested third equipment use cannot replace the suspended second use's frozen facts or paid return.");
            Cold(branch, registry);
            Continue(branch, "third");
            ReachOption(branch, "second-after");
            Require(Window(branch, "second-after").Action.ActionId == originalAction &&
                branch.ResolutionStack.OfType<ProgramSkillFrame>().Last().WindowContext?.Facts?.ActualTurnActorUseOrdinal == 2 &&
                branch.CardMovements.Count(move => move.CardId == material && move.From == CardLocation.Hand(0) &&
                    move.To == CardLocation.Processing) == 1 &&
                branch.CardMovements.Count(move => move.CardId == material && move.From == CardLocation.Processing &&
                    move.To == CardLocation.Equipment(0)) == 1,
                "After third-use completion, the original second window resumes once with the same declaration ordinal.");
            Continue(branch, "second-after");
            ReachPlay(branch);
            Require(Facts<ProgramCardTriggerResolvedEvent>(branch).Count(fact => fact.SkillId == Witness &&
                    fact.TriggerId == "second" && fact.Activated) == 1 &&
                Facts<ProgramCardTriggerResolvedEvent>(branch).Count(fact => fact.SkillId == Witness &&
                    fact.TriggerId == "third" && fact.Activated) == 1,
                "Both exact completion observers finish once after the real nested child returns.");
        }
        Require(State(game) == State(copy), "Cold restoration at the second completion reproduces the full nested third-use return.");
    }

    private static void VerifyResponseAndActualTurnIsolation(GameEngine game, ContentRegistry registry)
    {
        Accept(game, new UseProgramSkillCommand(0, Driver, "foreign-duel", [], [1], game.Revision, Prompt(game)!.PromptId));
        Reach(game, () => Prompt(game) is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
        var response = Prompt(game)!.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("conversion-binding-id") == "slash-response");
        var cost = response.Cards.Single();
        var before = Facts<CurrentTurnCardUseKindsRecordedEvent>(game).Count(fact => fact.ActorSeat == 0);
        Cold(game, registry);
        Answer(game, response);
        ReachOption(game, "foreign-first");
        var foreign = Window(game, "foreign-first");
        Require(foreign.Action.ActorSeat == 1 && foreign.Action.Type == CardActionType.Use &&
            FrozenFacts(foreign, "foreign-first") is
                { ActualTurnActorUseOrdinal: 1, CardActionActorIsCurrentTurn: false, CardActionActorIsOwner: false } &&
            Facts<CurrentTurnCardUseKindsRecordedEvent>(game).Count(fact => fact.ActorSeat == 0) == before &&
            !Facts<CurrentTurnCardUseKindsRecordedEvent>(game).Any(fact => fact.CardActionId ==
                Facts<CardActionAcceptedEvent>(game).Single(fact => fact.Action.Type == CardActionType.Response &&
                    fact.Action.EffectiveKind == CardKind.Slash && fact.Action.PhysicalCards.Any(card => card.CardId == cost)).Action.ActionId) &&
            game.CardMovements.Count(move => move.CardId == cost && move.From == CardLocation.Processing &&
                move.To == CardLocation.DiscardPile) == 1,
            "A real paid ordinary Slash Response occupies no use slot, and a foreign actor gets its own first ordinal outside its turn.");
        Cold(game, registry);
        Continue(game, "foreign-first");
        ReachPlay(game);
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));
        ReachOption(game, "foreign-first");
        var next = Window(game, "foreign-first");
        var fact = Facts<CurrentTurnCardUseKindsRecordedEvent>(game).Single(item => item.CardActionId == next.Action.ActionId);
        Require(next.Action.ActorSeat == 1 && fact.TurnOwnerSeat == 1 && fact.ActorSeat == 1 &&
            fact.TurnNumber != Facts<CurrentTurnCardUseKindsRecordedEvent>(game).First().TurnNumber &&
            fact.ActualTurnActorUseOrdinal == 1 && FrozenFacts(next, "foreign-first") is
                { ActualTurnActorUseOrdinal: 1, CardActionActorIsCurrentTurn: true, CardActionActorIsOwner: false },
            "The next real actor turn starts at one without inheriting either participant's prior-turn uses or ordinary responses.");
        var restored = Cold(game, registry);
        Continue(game, "foreign-first");
        Continue(restored, "foreign-first");
        Require(State(game) == State(restored), "The foreign own-turn completion returns identically from a cold command prefix.");
    }

    private static void VerifyOwnSlashDefenseDodge()
    {
        var (game, registry) = Start(defense: true);
        ReachPlay(game);
        // Cardless damage prompts the other participant's real retaliatory Slash
        // while this defender is still the actual turn actor.
        Accept(game, new UseProgramSkillCommand(0, Driver, "poke", [], [1], game.Revision, Prompt(game)!.PromptId));
        Reach(game, () => Prompt(game) is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        var slash = game.ResolutionStack.OfType<CardUseFrame>().Last();
        Require(slash is { CardKind: CardKind.Slash, SourceSeat: 1, Action: not null } &&
            slash.CardAttack?.TargetSeat == 0 && slash.Action.ActorSeat == 1,
            "The own-turn Dodge must defend against the exact real other-actor Slash use.");
        var before = Facts<CurrentTurnCardUseKindsRecordedEvent>(game).Count(fact => fact.ActorSeat == 0);
        var choice = Prompt(game)!.Choices.First(item => item.Parameters.GetValueOrDefault("conversion-binding-id") == "dodge-response");
        var cost = choice.Cards.Single();
        var hp = game.CreateSnapshot(0).Players[0].Hp;
        var copy = Cold(game, registry);
        foreach (var branch in new[] { game, copy })
        {
            Answer(branch, Prompt(branch)!.Choices.Single(item => item.Id == choice.Id));
            ReachOption(branch, "dodge-completed");
            var completed = Window(branch, "dodge-completed");
            var action = completed.Action;
            var fact = Facts<CurrentTurnCardUseKindsRecordedEvent>(branch).Single(item => item.CardActionId == action.ActionId);
            Require(action is { Type: CardActionType.Response, EffectiveKind: CardKind.Dodge, ActorSeat: 0, ProviderSeat: 0,
                    ResponderSeat: 0, RequesterSeat: null, OpponentSeat: 1 } && action.ParentActionId == slash.Action!.ActionId &&
                completed.CompletedResponseReturn?.Kind == ProgramCompletedResponseKind.Dodge &&
                fact is { TurnOwnerSeat: 0, ActorSeat: 0, ActualTurnActorUseOrdinal: 1, IsNullificationUse: false } &&
                fact.OriginFrameId == slash.Id && FrozenFacts(completed, "own-dodge") is
                    { ActualTurnActorUseOrdinal: 1, CardActionActorIsCurrentTurn: true, CardActionActorIsOwner: true } &&
                Facts<CurrentTurnCardUseKindsRecordedEvent>(branch).Count(item => item.ActorSeat == 0) == before + 1 &&
                branch.CreateSnapshot(0).Players[0].Hp == hp && branch.CreateSnapshot(1).PendingDecision is null &&
                branch.CardMovements.Count(move => move.CardId == cost && move.From == CardLocation.Hand(0) &&
                    move.To == CardLocation.Processing) == 1 &&
                branch.CardMovements.Count(move => move.CardId == cost && move.From == CardLocation.Processing &&
                    move.To == CardLocation.DiscardPile) == 1,
                "Own Slash-defense Dodge occupies its actual turn's first slot, with exact parent, one cost and a private frozen completion.");
            Cold(branch, registry);
            Continue(branch, "dodge-completed");
            ReachOption(branch, "foreign-first");
            var retaliation = Window(branch, "foreign-first");
            Require(retaliation.Action.ActionId == slash.Action!.ActionId &&
                FrozenFacts(retaliation, "foreign-first") is
                    { ActualTurnActorUseOrdinal: 1, CardActionActorIsCurrentTurn: false, CardActionActorIsOwner: false },
                "The real retaliating actor and own-turn defender each receive their independent first ordinal.");
            Continue(branch, "foreign-first");
            ReachPlay(branch);
            Require(Facts<ProgramCardTriggerResolvedEvent>(branch).Count(item => item.SkillId == Witness &&
                    item.TriggerId == "own-dodge" && item.Activated) == 1 &&
                branch.CreateSnapshot(0).Players[0].Hp == hp,
                "The real Dodge completion returns to its Slash once without damage or a second ordinal.");
        }
        Require(State(game) == State(copy), "A cold real Dodge command prefix reproduces the frozen completion and exact Slash return.");
    }

    private static void VerifyGroupDodgeIsOnlyAResponse()
    {
        var (game, registry) = Start(groupResponse: true);
        ReachPlay(game);
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));
        Reach(game, () => Prompt(game) is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        var arrow = game.ResolutionStack.OfType<CardUseFrame>().Last();
        Require(arrow is { CardKind: CardKind.ArrowBarrage, SourceSeat: 1, Action: not null } &&
            arrow.CardAttack?.TargetSeat == 0, "The response-only Dodge must belong to a real Arrow Barrage child.");
        var before = Facts<CurrentTurnCardUseKindsRecordedEvent>(game).Count(item => item.ActorSeat == 0);
        var choice = Prompt(game)!.Choices.First(item => item.Parameters.GetValueOrDefault("conversion-binding-id") == "dodge-response");
        var cost = choice.Cards.Single();
        var hp = game.CreateSnapshot(0).Players[0].Hp;
        var copy = Cold(game, registry);
        foreach (var branch in new[] { game, copy })
        {
            Answer(branch, Prompt(branch)!.Choices.Single(item => item.Id == choice.Id));
            ReachOption(branch, "foreign-first");
            var response = Facts<CardActionAcceptedEvent>(branch).Single(item => item.Action.Type == CardActionType.Response &&
                item.Action.EffectiveKind == CardKind.Dodge && item.Action.PhysicalCards.Any(card => card.CardId == cost)).Action;
            var completed = Window(branch, "foreign-first");
            Require(response is { ActorSeat: 0, ProviderSeat: 0, ResponderSeat: 0, RequesterSeat: null } &&
                response.ParentActionId == arrow.Action!.ActionId && completed.Action.ActionId == arrow.Action.ActionId &&
                completed.CompletedResponseReturn is null &&
                Facts<CurrentTurnCardUseKindsRecordedEvent>(branch).Count(item => item.ActorSeat == 0) == before &&
                !Facts<CurrentTurnCardUseKindsRecordedEvent>(branch).Any(item => item.CardActionId == response.ActionId) &&
                !Facts<ProgramCardTriggerResolvedEvent>(branch).Any(item => item.SkillId == Witness &&
                    item.TriggerId == "own-dodge" && item.Activated) && branch.CreateSnapshot(0).Players[0].Hp == hp &&
                branch.CardMovements.Count(move => move.CardId == cost && move.From == CardLocation.Hand(0) &&
                    move.To == CardLocation.Processing) == 1 &&
                branch.CardMovements.Count(move => move.CardId == cost && move.From == CardLocation.Processing &&
                    move.To == CardLocation.DiscardPile) == 1,
                "Arrow Barrage accepts and cleans up the actual Dodge response once but issues no actual-use ordinal or Dodge completion.");
            Cold(branch, registry);
            Continue(branch, "foreign-first");
        }
        Require(State(game) == State(copy), "The response-only group Dodge and its real Arrow parent return cold-restore identically.");
    }

    private static void PrepareLegacyPair(GameEngine game)
    {
        SelectTarget(game, 0);
        SelectEquipment(game, "first-equipment");
        SelectTarget(game, 1);
        ReachOption(game, "legacy-live");
        // Stop before finishing the actual attack, while the original Action-null
        // CardUse and its already-issued declaration fact can still be inspected.
        Require(game.ResolutionStack.OfType<CardUseFrame>().Any(frame => frame.CardKind == CardKind.Slash && frame.Action is null),
            "The small phase-starting driver must issue the real legacy second use before advancing its attack.");
    }

    private static int SelectEquipment(GameEngine game, string binding)
    {
        Reach(game, () => Prompt(game)?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("result-bind") == binding &&
            choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards") == true);
        var choice = Prompt(game)!.Choices.First(item => item.Parameters.GetValueOrDefault("result-bind") == binding &&
            item.Parameters.GetValueOrDefault("program-action") == "select-owned-cards");
        var card = choice.Cards.Single();
        Answer(game, choice);
        if (Prompt(game)?.Choices.Any(item => item.Parameters.GetValueOrDefault("result-bind") == binding &&
            item.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards") == true)
            Answer(game, Prompt(game)!.Choices.Single(item => item.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards"));
        return card;
    }
    private static void SelectTarget(GameEngine game, int seat)
    {
        Reach(game, () => Prompt(game)?.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "select-target" && choice.Targets.SequenceEqual([seat])) == true);
        Answer(game, Prompt(game)!.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "select-target" && choice.Targets.SequenceEqual([seat])));
    }
    private static void ReachOption(GameEngine game, string binding) => Reach(game, () => Prompt(game)?.Choices.Any(choice =>
        choice.Parameters.GetValueOrDefault("result-bind") == binding && choice.Parameters.GetValueOrDefault("option-id") == "continue") == true);
    private static void Continue(GameEngine game, string binding)
    {
        var prompt = Prompt(game) ?? throw new InvalidOperationException("Missing ordinal completion prompt.");
        Answer(game, prompt.Choices.Single(choice => choice.Parameters.GetValueOrDefault("result-bind") == binding &&
            choice.Parameters.GetValueOrDefault("option-id") == "continue"));
    }
    private static ProgramCardTriggerWindowFrame Window(GameEngine game, string binding)
    {
        var program = game.ResolutionStack.OfType<ProgramSkillFrame>().Last();
        Require(program.SkillId == Witness && Prompt(game)!.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("result-bind") == binding), "The exact shared ordinal witness must own this prompt.");
        return game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(frame => frame.Id == program.WindowContext!.ParentFrameId);
    }
    private static SkillProgramTriggerFacts FrozenFacts(ProgramCardTriggerWindowFrame window, string triggerId) =>
        window.Candidates.Single(candidate => candidate.OwnerSeat == 0 && candidate.SkillId == Witness &&
            candidate.TriggerId == triggerId).FrozenContext!.Facts!;
    private static void ReachPlay(GameEngine game) => Reach(game, () => Prompt(game) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine game, Func<bool> reached)
    {
        for (var step = 0; step < 80 && !reached(); step++)
        {
            var prompt = Prompt(game);
            if (prompt is { PlayerSeat: 0 })
            {
                var pass = prompt.Choices.FirstOrDefault(choice =>
                    choice.Parameters.GetValueOrDefault("response") is "pass" or "take-damage");
                Require(pass is not null, "Unexpected human boundary in the small ordinal fixture: " + JsonSerializer.Serialize(prompt));
                Answer(game, pass!);
            }
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        Require(reached(), "The fixed ordinal fixture exceeded its bounded command budget: " + JsonSerializer.Serialize(Prompt(game)));
    }
    private static PendingDecision? Prompt(GameEngine game) => game.PendingDecision;
    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = Prompt(game)!;
        Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision));
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "A real ordinal fixture command was rejected.");
    }
    private static IEnumerable<T> Facts<T>(GameEngine game) => game.Events.Select(item => item.Payload).OfType<T>();
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack), game.CardMovements,
        Events = game.Events.Select(item => $"{item.Sequence}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray(),
        Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics()
    });
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(game) == State(copy), "Ordinal facts, all private views, typed children and real movements must cold-restore exactly.");
        return copy;
    }
    private static (GameEngine, ContentRegistry) Start(bool ordinal = true, bool legacyPair = false, bool groupResponse = false, bool defense = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(ordinal, legacyPair, groupResponse, defense));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false, MaxTurns = 3
        }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, Prompt(game)!.PromptId));
        return (game, registry);
    }
    private static void ValidateCondition()
    {
        var rules = Rules(true, false);
        SkillProgramCatalog.Load(rules, PresentationFor(legacyPair: false));
        foreach (var invalid in new[] { "0", "-1", "1.5", "\"2\"" })
            Reject(rules.Replace("\"value\":2", "\"value\":" + invalid, StringComparison.Ordinal));
        Reject(rules.Replace(",\"value\":2", "", StringComparison.Ordinal));
        Reject(rules.Replace(",\"singleActionInstance\":true", "", StringComparison.Ordinal)
            .Replace("\"window\":\"cardUseCompleted\"", "\"window\":\"cardUseCommitted\"", StringComparison.Ordinal));
        Reject(rules.Replace("\"kind\":\"cardActionActualTurnUseOrdinalIs\",\"value\":2",
            "\"kind\":\"always\",\"value\":2", StringComparison.Ordinal));
    }
    private static void Reject(string rules)
    {
        var rejected = false;
        try { SkillProgramCatalog.Load(rules, PresentationFor(legacyPair: false)); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "The shared loader must reject an absent/nonpositive/noninteger ordinal or unrelated trigger window.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private const string Equipment = """
        {"op":"selectTarget","target":"owner","targetKind":"anyLiving"},
        {"op":"selectOwnedCards","target":"owner","minimumCards":1,"maximumCards":1,"zones":["hand"],"cardKinds":["crossbow"],"resultBind":"nested-equipment"},
        {"op":"revealBoundCards","target":"owner","sourceBind":"nested-equipment"},
        {"op":"useBoundCardByTarget","target":"selectedTarget","sourceBind":"nested-equipment"}
        """;
    private static string Rules(bool ordinal, bool legacyPair, bool defense = false)
    {
        var ownSecond = """{"kind":"all","children":[{"kind":"cardActionActorIsCurrentTurn"},{"kind":"cardActionActorIsOwner"},{"kind":"cardActionActualTurnUseOrdinalIs","value":2}]}""";
        var ownThird = ownSecond.Replace("\"value\":2", "\"value\":3", StringComparison.Ordinal);
        var foreign = """{"kind":"all","children":[{"kind":"not","children":[{"kind":"cardActionActorIsOwner"}]},{"kind":"cardActionActualTurnUseOrdinalIs","value":1}]}""";
        var triggers = ordinal ? $$"""
            [{"id":"second","window":"cardUseCompleted","includeResponseUses":true,"ownerRelation":"observer","singleActionInstance":true,"optional":false,"condition":{{ownSecond}},"effects":[
              {"op":"chooseOption","target":"owner","resultBind":"second-before","options":[{"id":"continue"}]},{{Equipment}},
              {"op":"chooseOption","target":"owner","resultBind":"second-after","options":[{"id":"continue"}]}]},
             {"id":"third","window":"cardUseCompleted","includeResponseUses":true,"ownerRelation":"observer","singleActionInstance":true,"optional":false,"condition":{{ownThird}},"effects":[
              {"op":"chooseOption","target":"owner","resultBind":"third","options":[{"id":"continue"}]}]},
             {"id":"foreign-first","window":"cardUseCompleted","includeResponseUses":true,"ownerRelation":"observer","singleActionInstance":true,"optional":false,"condition":{{foreign}},"effects":[
              {"op":"chooseOption","target":"owner","resultBind":"foreign-first","options":[{"id":"continue"}]}]},
             {"id":"own-dodge","window":"cardUseCompleted","cardKinds":["dodge"],"includeResponseUses":true,"ownerRelation":"observer","singleActionInstance":true,"optional":false,
              "condition":{{ownSecond.Replace("\"value\":2", "\"value\":1", StringComparison.Ordinal)}},"effects":[
              {"op":"chooseOption","target":"owner","resultBind":"dodge-completed","options":[{"id":"continue"}]}]}]
            """ : "[]";
        var pair = legacyPair ? $$"""
            [{"id":"legacy-first-equipment","window":"playPhaseStarting","subject":"owner","priority":1,"optional":false,"effects":[
              {{Equipment.Replace("nested-equipment", "first-equipment", StringComparison.Ordinal)}}]},
             {"id":"legacy-pair","window":"playPhaseStarting","subject":"owner","optional":false,"effects":[
              {"op":"selectTarget","target":"owner","targetKind":"otherLiving"},
              {"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":false}]},
             {"id":"legacy-live","window":"afterDamageApplied","subject":"any","damageOccurrence":"perDamage","optional":false,"condition":{"kind":"damageSourceIsOwner"},"effects":[
              {"op":"chooseOption","target":"owner","resultBind":"legacy-live","options":[{"id":"continue"}]}]}]
            """ : "[]";
        var retaliate = !defense ? "" : """
            ,{"id":"cardless-retaliate","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","usageScope":"game","usageLimit":1,"optional":false,
              "condition":{"kind":"not","children":[{"kind":"directCardUseDamage"}]},"effects":[
              {"op":"selectTarget","target":"owner","targetKind":"eventSource"},{"op":"useVirtualSlash","target":"selectedTarget"}]}
            """;
        return $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"viewAs":[
                {"id":"draw","inputKinds":[],"inputSuits":[],"outputKind":"drawTwo","forPlay":true,"forResponse":false,"singleCardTrickUse":true},
                {"id":"counter","inputKinds":[],"inputSuits":[],"outputKind":"nullification","forPlay":false,"forResponse":true},
                {"id":"slash-response","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":false,"forResponse":true},
                {"id":"dodge-response","inputKinds":[],"inputSuits":[],"outputKind":"dodge","forPlay":false,"forResponse":true}],
                "activations":[{"id":"foreign-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
                  {"id":"poke","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}],"triggers":{{pair}}},
              {"id":"{{Witness}}","revision":1,"modifiers":[{"id":"old-use-kinds","query":"handLimit","operation":"add","valueExpression":"currentTurnUsedHandSuitCount","priority":0}],"triggers":{{triggers}}},
              {"id":"{{Peer}}","revision":1,"triggers":[{"id":"first-real-use","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[
                {"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"useVirtualSlash","target":"selectedTarget"}]}{{retaliate}}]}]}
            """;
    }
    private const string Presentation = """
        {"skills":{
          "fixture:actual-turn-ordinal-driver":{"name":"序号驱动","description":"真实转换、原实体装备与旧虚拟使用。","optionLabels":{"continue":"继续"}},
          "fixture:actual-turn-ordinal-witness":{"name":"序号观察","description":"仅共享实际回合序号机制。","optionLabels":{"continue":"继续"}},
          "fixture:actual-turn-ordinal-peer":{"name":"实际他人回合","description":"一次真实虚拟使用。"}}}
        """;
    private static string PresentationFor(bool legacyPair)
    {
        var presentation = JsonNode.Parse(Presentation)!;
        presentation["schemaVersion"] = SkillProgramCatalog.PresentationSchemaVersion;
        if (!legacyPair) presentation["skills"]![Driver]!.AsObject().Remove("optionLabels");
        return presentation.ToJsonString();
    }
    private sealed class Fixture(bool ordinal, bool legacyPair, bool groupResponse, bool defense) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("actual-turn-ordinal-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var presentation = JsonNode.Parse(PresentationFor(legacyPair))!;
            if (!ordinal) presentation["skills"]![Witness]!.AsObject().Remove("optionLabels");
            var catalog = SkillProgramCatalog.Load(Rules(ordinal, legacyPair, defense), presentation.ToJsonString());
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                    { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new(Owner, "实际回合序号", "supporter", Driver, "wei", 12, [Witness]));
            var others = Enumerable.Range(1, 3).Select(index => $"fixture:actual-turn-ordinal-other-{index}").ToArray();
            foreach (var id in others) builder.AddGeneral(new(id, "真实参与者", "supporter", groupResponse ? "standard:none" : Peer, "shu", 12));
            builder.AddDeck(new("fixture:actual-turn-ordinal-deck", "同质真实牌堆", 4, 2,
                [new(groupResponse ? "standard:arrow_barrage" : "standard:crossbow", 80)]));
            builder.AddMode(new(Mode, "实际回合序号共享检查", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:actual-turn-ordinal-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. others]));
        }
    }
}
