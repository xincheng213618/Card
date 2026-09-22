using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class TurnEndingBoundaryChecks
{
    private const int HumanSeat = 0;

    public static void ContentPreservesLegacyBoundaryAndPublishesPrograms()
    {
        var legacy = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 98, 0));
        var current = StandardContentRegistry.CreateWithClassicGenerals();

        foreach (var (skillId, legacyKind, priority) in new[]
                 {
                     ("classic:jushou", SkillKind.Jushou, 100),
                     ("classic:biyue", SkillKind.Biyue, -100)
                 })
        {
            var historical = legacy.Skills[skillId];
            var migrated = current.Skills[skillId];
            Require(historical.LegacyKind == legacyKind && historical.Program is null &&
                    historical.PhaseSkill is null,
                $"Package 1.98 must retain historical {skillId} metadata without the removed runtime path.");
            Require(migrated.LegacyKind is null && migrated.PhaseSkill is null &&
                    migrated.Program is { UsesCompositionKernel: true, MinimumRulesVersion: 128 } program &&
                    program.Triggers.Single() is
                    {
                        Window: SkillProgramTriggerWindow.TurnEnding,
                        Priority: var actualPriority,
                        UsageScope: SkillUsageScope.Turn,
                        UsageLimit: 1
                    } && actualPriority == priority,
                $"Package 1.99 must publish {skillId} as a shared-kernel TurnEnding program.");
        }

        var jushou = current.Skills["classic:jushou"].Program!.Triggers.Single();
        Require(jushou.Effects is
                [
                    { Op: SkillProgramTriggerEffectOp.Draw, Amount: 3 },
                    { Op: SkillProgramTriggerEffectOp.SetFaceState, FaceDown: true }
                ],
            "Jushou must draw three then use the exact face-down state primitive, not the toggle primitive.");
    }

    public static void OrdersJushouJujianBiyueAndReplays()
    {
        var registry = CombinedRegistry();
        var game = CreateAndSelect(registry, CombinedScenarioPackage.ModeId,
            CombinedScenarioPackage.OwnerGeneralId);
        ReachHumanPlay(game);
        EndPlayAndReachSkill(game, "classic:jushou");

        var frame = game.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Single();
        Require(frame.Items.Select(item => item.StableIdentity).SequenceEqual(
                [
                    frame.Items[0].StableIdentity,
                    "legacy:classic:jujian",
                    frame.Items[2].StableIdentity
                ]) &&
                frame.Items[0].Candidate?.SkillId == "classic:jushou" &&
                frame.Items[2].Candidate?.SkillId == "classic:biyue" &&
                frame.Step == ResolutionFrameStep.AwaitingResponse,
            "The frozen boundary must order Jushou, the restricted Jujian bridge, then Biyue.");
        var serializedFrames = JsonSerializer.Serialize(game.ResolutionStack);
        var serializedRoundTrip = JsonSerializer.Deserialize<ResolutionFrame[]>(serializedFrames) ?? [];
        Require(serializedRoundTrip.Single() is TurnEndingBoundaryFrame serializedBoundary &&
                serializedBoundary.Id == frame.Id && serializedBoundary.OwnerSeat == frame.OwnerSeat &&
                serializedBoundary.TurnNumber == frame.TurnNumber &&
                serializedBoundary.ItemIndex == frame.ItemIndex && serializedBoundary.Step == frame.Step &&
                serializedBoundary.Facts == frame.Facts && serializedBoundary.Items.SequenceEqual(frame.Items) &&
                JsonSerializer.Serialize(serializedRoundTrip) == serializedFrames,
            "The TurnEnding parent and its union items must be plain serializable resolution data.");

        var owner = Players(game)[HumanSeat];
        owner.IsFaceDown = true;
        var handBefore = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand.Count;
        var turnEndsBefore = game.Events.Select(item => item.Payload).OfType<TurnEndedEvent>().Count();
        AnswerProgram(game, "activate");

        var jushouDraws = game.CardMovements
            .Where(move => move.Reason.Value == "skill-program.classic:jushou.Draw" &&
                           move.To == CardLocation.Hand(HumanSeat))
            .Select(move => move.CardId)
            .ToArray();
        Require(jushouDraws.Length == 3 &&
                game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].IsFaceDown &&
                game.PendingDecision is { Kind: DecisionKind.Jujian, PlayerSeat: HumanSeat },
            "Jushou must draw exactly three and keep an already face-down owner face down before Jujian.");

        var jujian = game.PendingDecision!;
        var useDrawnCard = jujian.Choices.FirstOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("action") == "jujian-use" &&
            choice.Cards.Count == 1 && jushouDraws.Contains(choice.Cards[0]));
        Require(useDrawnCard is not null,
            "Jujian must compute its costs on arrival so a card drawn by Jushou is immediately legal.");
        Answer(game, useDrawnCard!);
        AdvanceOne(game);

        var biyue = RequireSkillPrompt(game, "classic:biyue");
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        var replayFrame = replay.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Single();
        Require(replayFrame.ItemIndex == 2 && replayFrame.Step == ResolutionFrameStep.AwaitingResponse &&
                replayFrame.Facts == frame.Facts,
            "A paused Biyue prompt must preserve the same serialized parent cursor and frozen facts.");

        AnswerProgram(game, "activate");
        AnswerProgram(replay, "activate");
        var jujianEvent = game.Events.Select(item => item.Payload).OfType<JujianResolvedEvent>().Last();
        Require(jujianEvent.DiscardedCardId == useDrawnCard!.Cards.Single() &&
                game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand.Count == handBefore + 3 &&
                game.CardMovements.Count(move =>
                    move.Reason.Value == "skill-program.classic:biyue.Draw" &&
                    move.To == CardLocation.Hand(HumanSeat)) == 1 &&
                game.Events.Select(item => item.Payload).OfType<TurnEndedEvent>().Count() == turnEndsBefore + 1 &&
                game.ResolutionStack.Count == 0 && game.PendingDecision is null,
            "The full boundary must spend one new Jushou card, draw once for Biyue, and finalize exactly one turn.");
        Require(State(replay) == State(game) && Events(replay).SequenceEqual(Events(game)),
            "The paused TurnEnding boundary must replay to identical state and events.");

        var orderedEvents = game.Events.Select(item => item.Payload).ToArray();
        var jushouResolved = Array.FindIndex(orderedEvents, item =>
            item is ProgramBindingResolvedEvent { SkillId: "classic:jushou", Completed: true });
        var jujianResolved = Array.FindIndex(orderedEvents, item => item is JujianResolvedEvent { Used: true });
        var biyueStarted = Array.FindIndex(orderedEvents, item =>
            item is ProgramBindingStartedEvent { SkillId: "classic:biyue" });
        Require(jushouResolved >= 0 && jushouResolved < jujianResolved && jujianResolved < biyueStarted,
            "Audit events must retain the Jushou -> Jujian -> Biyue ordering.");
    }

    public static void DeduplicatesSourcesAndRechecksOwnership()
    {
        var registry = CombinedRegistry();
        var game = CreateAndSelect(registry, CombinedScenarioPackage.ModeId,
            CombinedScenarioPackage.OwnerGeneralId);
        ReachHumanPlay(game);
        var owner = Players(game)[HumanSeat];
        var primary = owner.SkillGrants.Grants.Single(grant =>
            grant.SkillId == "classic:jushou" && grant.SourceId == CharacterState.PrimarySkillSource);
        owner.SkillGrants.Grant(new SkillGrant(
            "fixture:jushou-duplicate",
            primary.SkillId,
            primary.SkillInstanceId,
            "fixture:jushou-duplicate"));
        owner.SkillGrants.Grant(new SkillGrant(
            "fixture:jushou-distinct",
            primary.SkillId,
            "fixture:jushou-distinct-instance",
            "fixture:jushou-distinct"));

        EndPlayAndReachSkill(game, "classic:jushou");
        var frame = game.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Single();
        Require(frame.Items.Count(item => item.Candidate?.SkillId == "classic:jushou") == 2,
            "Duplicate grants of one instance must collapse while a distinct Jushou instance composes.");
        AnswerProgram(game, "activate");

        var second = RequireSkillPrompt(game, "classic:jushou");
        var secondInstance = second.Choices[0].Parameters["skill-instance-id"];
        foreach (var grant in owner.SkillGrants.Grants.Where(grant =>
                     grant.SkillId == "classic:jushou" && grant.SkillInstanceId == secondInstance).ToArray())
            owner.SkillGrants.SetEnabled(grant.GrantId, false);
        AnswerProgram(game, "activate");

        var resolved = game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
            .Where(item => item.SkillId == "classic:jushou").ToArray();
        Require(resolved.Count(item => item is { Activated: true, Completed: true }) == 1 &&
                resolved.Count(item => item is { Activated: false, Completed: false }) == 1 &&
                game.CardMovements.Count(move =>
                    move.Reason.Value == "skill-program.classic:jushou.Draw" &&
                    move.To == CardLocation.Hand(HumanSeat)) == 3 &&
                game.PendingDecision is { Kind: DecisionKind.Jujian },
            "A Jushou instance lost while prompted must skip safely and advance once to the bridge.");
    }

    public static void FreezesFactsAcrossEarlierTurnEndingEffects()
    {
        var registry = FrozenFactsRegistry();
        var game = CreateAndSelect(registry, FrozenFactsScenarioPackage.ModeId,
            FrozenFactsScenarioPackage.OwnerGeneralId);
        ReachHumanPlay(game);
        var hpAtBoundary = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hp;
        EndPlayAndReachSkill(game, FrozenFactsScenarioPackage.SkillId);

        var prompt = RequireSkillPrompt(game, FrozenFactsScenarioPackage.SkillId);
        var frame = game.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Single();
        Require(game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hp == hpAtBoundary - 1 &&
                frame.Facts.CurrentHp == hpAtBoundary &&
                prompt.Choices.All(choice =>
                    choice.Parameters.GetValueOrDefault("current-hp") == hpAtBoundary.ToString()),
            "Later candidates must use the serialized boundary facts even after an earlier item changes HP.");
        AnswerProgram(game, "skip");
    }

    private static ContentRegistry CombinedRegistry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new CombinedScenarioPackage());

    private static ContentRegistry FrozenFactsRegistry()
    {
        const string rules = """
            {"schemaVersion":13,"skills":[{"id":"fixture:turn-ending-facts","revision":1,
            "minimumRulesVersion":118,"modifiers":[],"viewAs":[],"activations":[],"triggers":[
            {"id":"first-lose-hp","window":"turnEnding","subject":"owner","optional":false,"priority":100,
            "effects":[{"op":"loseHp","target":"owner","amount":1}]},
            {"id":"second-frozen-check","window":"turnEnding","subject":"owner","optional":true,"priority":-100,
            "condition":{"kind":"compare","left":{"kind":"currentHp"},"operator":"equal",
            "right":{"kind":"integerConstant","value":5}},
            "effects":[{"op":"draw","target":"owner","amount":1}]}],
            "contributions":[],"cardIdentities":[]}]}
            """;
        const string presentation =
            "{\"schemaVersion\":1,\"skills\":{\"fixture:turn-ending-facts\":{\"name\":\"冻结事实\",\"description\":\"测试\"}}}";
        var program = SkillProgramCatalog.Load(rules, presentation)
            .Programs[FrozenFactsScenarioPackage.SkillId];
        return ContentRegistry.Build(
            new StandardContentPackage(),
            new FrozenFactsScenarioPackage(program));
    }

    private static GameEngine CreateAndSelect(
        ContentRegistry registry,
        string modeId,
        string generalId)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 118099,
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
        Require(game.Submit(new StartGameCommand()).Accepted, "The TurnEnding fixture failed to start.");
        var selection = game.PendingDecision ?? throw new InvalidOperationException("No general prompt was published.");
        Require(selection.ValidContentIds.Contains(generalId), "The TurnEnding fixture did not offer its owner.");
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat, generalId, game.Revision, selection.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "The TurnEnding owner could not be selected.");
        return game;
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 256; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            Require(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while reaching Play.");
            AdvanceOne(game);
        }
        throw new InvalidOperationException("The TurnEnding fixture did not reach human Play.");
    }

    private static void EndPlayAndReachSkill(GameEngine game, string skillId)
    {
        var play = game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat } prompt
            ? prompt
            : throw new InvalidOperationException("The TurnEnding fixture is not at human Play.");
        var ended = game.Submit(new EndPlayPhaseCommand(
            HumanSeat, game.Revision, play.PromptId));
        Require(ended.Accepted, ended.Error?.Message ?? "The TurnEnding fixture could not end Play.");
        for (var step = 0; step < 32; step++)
        {
            if (game.PendingDecision?.SkillPrompt?.SkillId == skillId) return;
            Require(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} before {skillId}.");
            AdvanceOne(game);
        }
        throw new InvalidOperationException($"The TurnEnding fixture did not reach {skillId}.");
    }

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
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("No program prompt is pending.");
        var choice = prompt.Choices.Single(item =>
            item.Parameters.GetValueOrDefault("program-action") == action);
        Answer(game, choice);
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("No prompt is pending.");
        var answered = game.Submit(new AnswerPromptCommand(
            prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision));
        Require(answered.Accepted, answered.Error?.Message ?? $"The {prompt.Kind} answer was rejected.");
    }

    private static void AdvanceOne(GameEngine game)
    {
        var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(advanced.Accepted, advanced.Error?.Message ?? "The TurnEnding fixture could not advance.");
    }

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static IReadOnlyList<CharacterState> Players(GameEngine game) =>
        (IReadOnlyList<CharacterState>)(typeof(GameEngine)
            .GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(game) ?? throw new InvalidOperationException("The TurnEnding players are unavailable."));

    private static string State(GameEngine game) =>
        SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true));

    private static IReadOnlyList<string> Events(GameEngine game) => game.Events
        .Select(item =>
            $"{item.Sequence}|{item.Payload.GetType().Name}|" +
            JsonSerializer.Serialize(item.Payload, item.Payload.GetType()))
        .ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class CombinedScenarioPackage : IGameContentPackage
    {
        public const string OwnerGeneralId = "fixture:turn-ending-owner";
        public const string ModeId = "identity:classic-turn-ending-test-5";
        private const string DeckId = "fixture:turn-ending-deck";

        public PackageManifest Manifest { get; } = new(
            "turn-ending-scenario",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

        public void Register(IContentRegistryBuilder builder)
        {
            var targetIds = Enumerable.Range(1, 4)
                .Select(index => $"fixture:turn-ending-target-{index}").ToArray();
            builder.AddGeneral(new ContentGeneralDefinition(
                OwnerGeneralId,
                "据守举荐闭月测试武将",
                "supporter",
                "classic:jushou",
                "wei",
                BaseHp: 4,
                AdditionalSkillIds: ["classic:jujian", "classic:biyue"]));
            foreach (var id in targetIds)
                builder.AddGeneral(new ContentGeneralDefinition(
                    id, "结束阶段测试目标", "supporter", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "结束阶段边界测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                [new ContentDeckCardCount("standard:crossbow", 96)]));
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "五人经典身份（结束阶段边界场景）",
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
                GeneralPoolIds: [OwnerGeneralId, .. targetIds]));
        }
    }

    private sealed class FrozenFactsScenarioPackage(SkillProgram program) : IGameContentPackage
    {
        public const string SkillId = "fixture:turn-ending-facts";
        public const string OwnerGeneralId = "fixture:turn-ending-facts-owner";
        public const string ModeId = "identity:classic-turn-ending-facts-test-5";
        private const string DeckId = "fixture:turn-ending-facts-deck";

        public PackageManifest Manifest { get; } = new("turn-ending-facts-scenario", new Version(1, 0, 0));

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddSkill(new ContentSkillDefinition(SkillId, "冻结事实", "测试")
            {
                Program = program,
                ExecutionForms = SkillExecutionForm.Trigger
            });
            var targetIds = Enumerable.Range(1, 4)
                .Select(index => $"fixture:turn-ending-facts-target-{index}").ToArray();
            builder.AddGeneral(new ContentGeneralDefinition(
                OwnerGeneralId, "冻结事实测试武将", "supporter", SkillId, "wei", BaseHp: 4));
            foreach (var id in targetIds)
                builder.AddGeneral(new ContentGeneralDefinition(
                    id, "冻结事实测试目标", "supporter", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "冻结事实测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                [new ContentDeckCardCount("standard:crossbow", 96)]));
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "五人经典身份（冻结事实场景）",
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
                GeneralPoolIds: [OwnerGeneralId, .. targetIds]));
        }
    }
}
