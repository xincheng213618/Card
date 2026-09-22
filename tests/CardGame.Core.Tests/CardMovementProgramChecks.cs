using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CardMovementProgramChecks
{
    private const int HumanSeat = 0;

    public static void DefinitionsAndVersionBoundary()
    {
        var program = SkillProgramCatalog.Load(Rules, Presentation).Programs[ScenarioPackage.SkillId];
        var perCard = program.Triggers.Single(trigger => trigger.Id == "per-card");
        var emptyBatch = program.Triggers.Single(trigger => trigger.Id == "empty-batch");
        Require(program.RuntimeVersion == "skill-program-v14" && program.MinimumRulesVersion == 119 &&
                perCard is
                {
                    Window: SkillProgramTriggerWindow.CardsMoved,
                    MovementOccurrence: SkillProgramMovementOccurrence.PerCard,
                    Priority: 100
                } &&
                perCard.SourceZones.SequenceEqual([CardZoneKind.Hand]) &&
                emptyBatch.MovementOccurrence == SkillProgramMovementOccurrence.PerBatch &&
                emptyBatch.Condition.Evaluate(new SkillProgramTriggerFacts(0, 4, true, 2, 2, 0)) &&
                !emptyBatch.Condition.Evaluate(new SkillProgramTriggerFacts(0, 4, true, 1, 2, 1)),
            "Schema 14 must freeze source-zone, occurrence, ordering and movement-count semantics.");

        Reject(Rules.Replace("\"schemaVersion\":14", "\"schemaVersion\":13", StringComparison.Ordinal),
            "cardsMoved requires schema version 14");
        Reject(Rules.Replace("\"sourceZones\":[\"hand\"]", "\"sourceZones\":[]", StringComparison.Ordinal),
            "exactly one owner-scoped source zone");
        Reject(Rules.Replace("\"sourceZones\":[\"hand\"]",
                "\"sourceZones\":[\"hand\",\"equipment\"]", StringComparison.Ordinal),
            "exactly one owner-scoped source zone");
        Reject(Rules.Replace("\"movementOccurrence\":\"perCard\",", string.Empty, StringComparison.Ordinal),
            "movementOccurrence");
        Reject(Rules.Replace("\"op\":\"draw\",\"target\":\"owner\",\"amount\":1",
                "\"op\":\"recover\",\"target\":\"owner\",\"amount\":1", StringComparison.Ordinal),
            "only owner draw effects");

        const string wrongWindow = """
            {"schemaVersion":14,"skills":[{"id":"fixture:wrong-window","revision":1,
            "minimumRulesVersion":119,"modifiers":[],"viewAs":[],"activations":[],"triggers":[
            {"id":"binding","window":"turnEnding","subject":"owner","optional":true,"priority":0,
            "condition":{"kind":"compare","left":{"kind":"movedCardCount"},"operator":"greaterThan",
            "right":{"kind":"integerConstant","value":0}},
            "effects":[{"op":"draw","target":"owner","amount":1}]}],
            "contributions":[],"cardIdentities":[]}]}
            """;
        const string wrongPresentation =
            "{\"schemaVersion\":1,\"skills\":{\"fixture:wrong-window\":{\"name\":\"Wrong\",\"description\":\"Wrong\"}}}";
        Reject(wrongWindow, wrongPresentation, "only by cardsMoved");

        var historical = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 99, 0));
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        foreach (var skillId in new[] { "classic:xiaoji", "classic:lianying" })
        {
            Require(historical.Skills[skillId].Program is null,
                $"Package 1.99 must retain historical {skillId} metadata without reviving its removed route.");
            Require(current.Skills[skillId].Program is
                    { RuntimeVersion: "skill-program-v14", MinimumRulesVersion: 119 } migrated &&
                    migrated.Triggers.Single().Window == SkillProgramTriggerWindow.CardsMoved,
                $"Package 1.100 must publish {skillId} through the schema-14 card-movement window.");
        }
    }

    public static void AtomicBatchRunsPerCardAndPerBatchAndReplays()
    {
        var registry = Registry();
        var game = CreateAndSelect(registry, ScenarioPackage.ModeId, ScenarioPackage.OwnerGeneralId);
        ReachHumanPlay(game);
        var play = RequirePrompt(game, DecisionKind.PlayCard);
        var hand = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand;
        Require(hand.Count == 2, "The movement fixture must begin Play with exactly two hand cards.");
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseProgramSkill &&
            candidate.ProgramSkillId == ScenarioPackage.SkillId &&
            candidate.ProgramActivationId == "discard-two");
        var used = game.Submit(new UseProgramSkillCommand(
            HumanSeat,
            action.ProgramSkillId!,
            action.ProgramActivationId!,
            hand.Select(card => card.Id).Order().ToArray(),
            [],
            game.Revision,
            play.PromptId));
        Require(used.Accepted, used.Error?.Message ?? "The movement fixture activation was rejected.");

        var prompt = RequireSkillPrompt(game, ScenarioPackage.SkillId);
        var frame = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
        var handCount = frame.Batch.SourceCounts.Single(item => item.Location == CardLocation.Hand(HumanSeat));
        Require(frame.Batch.Movements.Count == 2 &&
                frame.Batch.Movements.All(item => item.From == CardLocation.Hand(HumanSeat)) &&
                handCount is { CountBefore: 2, CountAfter: 0 } &&
                frame.Batch.ParentFrameId is not null && frame.Batch.ParentBatchId is null &&
                frame.Candidates.Count == 3 &&
                frame.Candidates.Take(2).All(item => item.BindingId == "per-card") &&
                frame.Candidates.Take(2).Select(item => item.OccurrenceIndex).SequenceEqual([0, 1]) &&
                frame.Candidates[2] is { BindingId: "empty-batch", OccurrenceIndex: 0 } &&
                prompt.Choices.Count == 2,
            "One physical MoveCards call must freeze one two-card batch with two per-card and one per-batch candidates.");

        var serialized = JsonSerializer.Serialize(game.ResolutionStack);
        var roundTrip = JsonSerializer.Deserialize<ResolutionFrame[]>(serialized) ?? [];
        var restoredFrame = roundTrip.Single() as CardsMovedTriggerWindowFrame ??
            throw new InvalidOperationException("The cards-moved frame lost its concrete type after serialization.");
        Require(restoredFrame.Id == frame.Id && restoredFrame.CandidateIndex == frame.CandidateIndex &&
                restoredFrame.Step == frame.Step && restoredFrame.Batch.Id == frame.Batch.Id &&
                restoredFrame.Batch.ParentFrameId == frame.Batch.ParentFrameId &&
                restoredFrame.Batch.ParentBatchId == frame.Batch.ParentBatchId &&
                restoredFrame.Batch.TurnNumber == frame.Batch.TurnNumber,
            "The cards-moved frame lost scalar parent, batch or cursor data after serialization.");
        Require(restoredFrame.Batch.Movements.SequenceEqual(frame.Batch.Movements),
            "The cards-moved frame lost physical movement records after serialization: " +
            $"expected={JsonSerializer.Serialize(frame.Batch.Movements)}, " +
            $"actual={JsonSerializer.Serialize(restoredFrame.Batch.Movements)}.");
        Require(restoredFrame.Batch.SourceCounts.SequenceEqual(frame.Batch.SourceCounts),
            "The cards-moved frame lost frozen source counts after serialization.");
        Require(restoredFrame.Candidates.SequenceEqual(frame.Candidates),
            "The cards-moved frame lost frozen candidates after serialization.");
        Require(JsonSerializer.Serialize(roundTrip) == serialized,
            "The cards-moved frame JSON must be stable after one round trip.");

        var checkpoint = RoundTrip(game.CreateCheckpoint());
        var replay = GameReplay.Restore(checkpoint, registry);
        for (var index = 0; index < 3; index++)
        {
            AnswerProgram(game, "activate");
            AnswerProgram(replay, "activate");
        }
        Require(game.ResolutionStack.Count == 0 && game.PendingDecision is null &&
                replay.ResolutionStack.Count == 0 && replay.PendingDecision is null,
            "The last movement trigger must return to a clean Play-phase continuation boundary.");
        AdvanceOne(game);
        AdvanceOne(replay);

        var completed = game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
            .Where(item => item.SkillId == ScenarioPackage.SkillId &&
                           item.Window == SkillProgramTriggerWindow.CardsMoved)
            .ToArray();
        Require(completed.Length == 3 && completed.All(item => item is { Activated: true, Completed: true }) &&
                game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand.Count == 3 &&
                game.CardMovements.Count(item => item.To == CardLocation.Hand(HumanSeat) &&
                    item.Reason.Value.StartsWith(
                        $"skill-program.{ScenarioPackage.SkillId}.", StringComparison.Ordinal)) == 3 &&
                game.ResolutionStack.Count == 0 &&
                game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat },
            "The two per-card candidates and one empty-batch candidate must each execute once and resume Play: " +
            $"resolved={completed.Length}, completed={completed.Count(item => item.Completed)}, " +
            $"hand={game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand.Count}, " +
            $"drawMoves={game.CardMovements.Count(item => item.To == CardLocation.Hand(HumanSeat) && item.Reason.Value.StartsWith($"skill-program.{ScenarioPackage.SkillId}.", StringComparison.Ordinal))}, " +
            $"stack={game.ResolutionStack.Count}, prompt={game.PendingDecision?.Kind.ToString() ?? "none"}.");
        Require(State(replay) == State(game) && Events(replay).SequenceEqual(Events(game)),
            "A checkpoint paused at the first card-movement prompt must replay to identical state and events.");

        var invalidated = GameReplay.Restore(checkpoint, registry);
        var owner = Players(invalidated)[HumanSeat];
        foreach (var grant in owner.SkillGrants.Grants
                     .Where(grant => grant.SkillId == ScenarioPackage.SkillId).ToArray())
            owner.SkillGrants.SetEnabled(grant.GrantId, false);
        AnswerProgram(invalidated, "activate");
        if (invalidated.PendingDecision is null) AdvanceOne(invalidated);
        var skipped = invalidated.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
            .Where(item => item.SkillId == ScenarioPackage.SkillId &&
                           item.Window == SkillProgramTriggerWindow.CardsMoved)
            .ToArray();
        Require(skipped.Length == 3 && skipped.All(item => item is { Activated: false, Completed: false }) &&
                invalidated.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand.Count == 0 &&
                invalidated.ResolutionStack.Count == 0 &&
                invalidated.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat },
            "A skill instance lost while prompted must skip every frozen remaining occurrence without drawing.");
    }

    public static void NestedBatchesRetainImmediateParentIdentity()
    {
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new NestedScenarioPackage());
        var game = CreateAndSelect(registry, NestedScenarioPackage.ModeId, NestedScenarioPackage.OwnerGeneralId);
        ReachHumanPlay(game);

        var store = (CardZoneStore)(typeof(GameEngine)
            .GetField("_cardZones", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(game) ?? throw new InvalidOperationException("The movement fixture card store is unavailable."));
        var diagnostics = game.CreateCardZoneDiagnostics();
        var oxDiagnostic = diagnostics.Single(item => item.CardKind == CardKind.WoodenOx);
        var grainDiagnostic = diagnostics.First(item => item.CardKind == CardKind.Crossbow &&
            item.CardId != oxDiagnostic.CardId);
        var ox = store.CardsAt(oxDiagnostic.Location).Single(card => card.Id == oxDiagnostic.CardId);
        _ = store.Move(ox.Id, oxDiagnostic.Location, CardLocation.Equipment(HumanSeat));
        _ = store.Move(grainDiagnostic.CardId, grainDiagnostic.Location, CardLocation.WoodenOxGrain(HumanSeat));

        var moveCard = typeof(GameEngine).GetMethod(
            "MoveCard", BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The movement fixture could not find MoveCard.");
        _ = moveCard.Invoke(game,
            [ox, CardLocation.Equipment(HumanSeat), CardLocation.Equipment(1), CardMoveReasons.WoodenOxTransfer]);

        var pending = (IReadOnlyList<CardMovementBatchContext>)(typeof(GameEngine)
            .GetField("_pendingCardsMovedBatches", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(game) ?? throw new InvalidOperationException("The movement batch queue is unavailable."));
        var outer = pending.Single(batch => batch.Movements.Any(item => item.CardId == ox.Id));
        var child = pending.Single(batch => batch.Movements.Any(item => item.CardId == grainDiagnostic.CardId));
        Require(outer.ParentBatchId is null && child.ParentBatchId == outer.Id && child.Id > outer.Id &&
                outer.SourceCounts.Single(item => item.Location == CardLocation.Equipment(HumanSeat)) is
                    { CountBefore: 1, CountAfter: 0 } &&
                child.SourceCounts.Single(item => item.Location == CardLocation.WoodenOxGrain(HumanSeat)) is
                    { CountBefore: 1, CountAfter: 0 } &&
                store.GetLocation(grainDiagnostic.CardId) == CardLocation.WoodenOxGrain(1),
            "A Wooden Ox hook must retain a distinct child batch linked to its immediate outer movement batch.");
    }

    private static ContentRegistry Registry()
    {
        var program = SkillProgramCatalog.Load(Rules, Presentation).Programs[ScenarioPackage.SkillId];
        return ContentRegistry.Build(new StandardContentPackage(), new ScenarioPackage(program));
    }

    private static GameEngine CreateAndSelect(ContentRegistry registry, string modeId, string generalId)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 140119,
            PlayerCount = 5,
            ModeId = modeId,
            HumanSeat = HumanSeat,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 20
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "The movement fixture failed to start.");
        var selection = RequirePrompt(game, DecisionKind.SelectGeneral);
        Require(selection.ValidContentIds.Contains(generalId), "The movement fixture did not offer its owner.");
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat, generalId, game.Revision, selection.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "The movement fixture could not select its owner.");
        return game;
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 256; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            Require(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while reaching Play.");
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "The movement fixture could not advance.");
        }
        throw new InvalidOperationException("The movement fixture did not reach human Play.");
    }

    private static void AdvanceOne(GameEngine game)
    {
        var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(advanced.Accepted, advanced.Error?.Message ?? "The movement fixture could not advance.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { PlayerSeat: HumanSeat } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");

    private static PendingDecision RequireSkillPrompt(GameEngine game, string skillId) =>
        game.PendingDecision is
            { Kind: DecisionKind.ProgramTrigger, PlayerSeat: HumanSeat, SkillPrompt.SkillId: var actual } prompt &&
        actual == skillId
            ? prompt
            : throw new InvalidOperationException(
                $"Expected ProgramTrigger for {skillId}, found {game.PendingDecision?.Kind} / " +
                $"{game.PendingDecision?.SkillPrompt?.SkillId}.");

    private static void AnswerProgram(GameEngine game, string action)
    {
        var prompt = RequirePrompt(game, DecisionKind.ProgramTrigger);
        var choice = prompt.Choices.Single(item =>
            item.Parameters.GetValueOrDefault("program-action") == action);
        var answered = game.Submit(new AnswerPromptCommand(
            HumanSeat, prompt.PromptId, choice.Id, game.Revision));
        Require(answered.Accepted, answered.Error?.Message ?? "The movement program answer was rejected.");
    }

    private static IReadOnlyList<CharacterState> Players(GameEngine game) =>
        (IReadOnlyList<CharacterState>)(typeof(GameEngine)
            .GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(game) ?? throw new InvalidOperationException("The movement fixture players are unavailable."));

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static string State(GameEngine game) =>
        SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true));

    private static IReadOnlyList<string> Events(GameEngine game) => game.Events
        .Select(item => $"{item.Sequence}|{item.Payload.GetType().Name}|" +
                        JsonSerializer.Serialize(item.Payload, item.Payload.GetType()))
        .ToArray();

    private static void Reject(string rules, string expectedMessage) =>
        Reject(rules, Presentation, expectedMessage);

    private static void Reject(string rules, string presentation, string expectedMessage)
    {
        try
        {
            _ = SkillProgramCatalog.Load(rules, presentation);
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        throw new InvalidOperationException(
            $"The invalid movement program was expected to mention '{expectedMessage}'.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private const string Rules = """
        {"schemaVersion":14,"skills":[{"id":"fixture:card-movement","revision":1,
        "minimumRulesVersion":119,"modifiers":[],"viewAs":[],
        "activations":[{"id":"discard-two","minCards":2,"maxCards":2,"minTargets":0,
        "maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,
        "effects":[{"op":"discardSelected","target":"owner","amount":2}]}],
        "triggers":[
        {"id":"per-card","window":"cardsMoved","subject":"owner","sourceZones":["hand"],
        "movementOccurrence":"perCard","optional":true,"priority":100,
        "effects":[{"op":"draw","target":"owner","amount":1}]},
        {"id":"empty-batch","window":"cardsMoved","subject":"owner","sourceZones":["hand"],
        "movementOccurrence":"perBatch","optional":true,"priority":0,
        "condition":{"kind":"all","children":[
        {"kind":"compare","left":{"kind":"sourceZoneCountBefore"},"operator":"greaterThan",
        "right":{"kind":"integerConstant","value":0}},
        {"kind":"compare","left":{"kind":"sourceZoneCountAfter"},"operator":"equal",
        "right":{"kind":"integerConstant","value":0}}]},
        "effects":[{"op":"draw","target":"owner","amount":1}]}],
        "contributions":[],"cardIdentities":[]}]}
        """;

    private const string Presentation =
        "{\"schemaVersion\":1,\"skills\":{\"fixture:card-movement\":{\"name\":\"Movement\",\"description\":\"Fixture\"}}}";

    private sealed class ScenarioPackage(SkillProgram program) : IGameContentPackage
    {
        public const string SkillId = "fixture:card-movement";
        public const string OwnerGeneralId = "fixture:card-movement-owner";
        public const string ModeId = "identity:card-movement-test-5";
        private const string DeckId = "fixture:card-movement-deck";

        public PackageManifest Manifest { get; } = new("card-movement-scenario", new Version(1, 0, 0));

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddSkill(new ContentSkillDefinition(SkillId, "Movement", "Fixture")
            {
                Program = program,
                ExecutionForms = SkillExecutionForm.Trigger,
                ActionForms = SkillActionForm.Active
            });
            var targets = Enumerable.Range(1, 4)
                .Select(index => $"fixture:card-movement-target-{index}").ToArray();
            builder.AddGeneral(new ContentGeneralDefinition(
                OwnerGeneralId, "Movement Owner", "supporter", SkillId, "wei", BaseHp: 4));
            foreach (var target in targets)
                builder.AddGeneral(new ContentGeneralDefinition(
                    target, "Movement Target", "supporter", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "Movement Deck",
                InitialHandSize: 2,
                DrawPerTurn: 0,
                [new ContentDeckCardCount("standard:crossbow", 64)]));
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "Card Movement Program Test",
                5,
                5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId,
                GeneralCandidateCount: 5,
                GeneralPoolIds: [OwnerGeneralId, .. targets]));
        }
    }

    private sealed class NestedScenarioPackage : IGameContentPackage
    {
        public const string OwnerGeneralId = "fixture:nested-movement-owner";
        public const string ModeId = "identity:classic-nested-movement-test-5";
        private const string DeckId = "fixture:nested-movement-deck";

        public PackageManifest Manifest { get; } = new(
            "nested-movement-scenario",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

        public void Register(IContentRegistryBuilder builder)
        {
            var generals = Enumerable.Range(0, 5)
                .Select(index => index == 0 ? OwnerGeneralId : $"fixture:nested-movement-target-{index}")
                .ToArray();
            foreach (var general in generals)
                builder.AddGeneral(new ContentGeneralDefinition(
                    general, "Nested Movement", "supporter", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "Nested Movement Deck",
                InitialHandSize: 2,
                DrawPerTurn: 0,
                [
                    new ContentDeckCardCount("classic:wooden-ox", 1),
                    new ContentDeckCardCount("standard:crossbow", 63)
                ]));
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "Nested Card Movement Test",
                5,
                5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId,
                GeneralCandidateCount: 5,
                GeneralPoolIds: generals));
        }
    }
}
