using System.Text.Json;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class GuoHuaiChecks
{
    private const int HumanSeat = 0;
    private const string GeneralId = "classic:guo-huai";

    public static void ContentAndPackageBoundary()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[GeneralId] is
                {
                    BaseHp: 4,
                    FactionId: "wei",
                    Gender: GeneralGender.Male,
                    PortraitKey: "guo_huai"
                } guoHuai &&
                guoHuai.SkillIds.SequenceEqual(["classic:jingce"]) &&
                current.Skills["classic:jingce"] is
                {
                    LegacyKind: null,
                    ExecutionForms: SkillExecutionForm.Trigger,
                    ActionForms: SkillActionForm.None,
                    Program:
                    {
                        MinimumRulesVersion: 170
                    } program
                } && program.Triggers.Single() is
                {
                    Window: SkillProgramTriggerWindow.PlayEnding,
                    UsageScope: SkillUsageScope.Phase,
                    UsageLimit: 1
                } &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(GeneralId),
            "Current Guo Huai must publish PlayEnding Jingce through the shared composition kernel.");
    }

    public static void JingceCountsTurnUsesDrawsAndReplays()
    {
        var (game, registry) = CreateReadyGame();
        PlayCrossbows(game, count: 5);
        var beforeEnd = game.CreateSnapshot(HumanSeat, revealAll: true);
        Require(beforeEnd.Players[HumanSeat].Hp == 5,
            "The Lord fixture must expose Guo Huai's current 5 HP threshold.");

        EndPlay(game);
        var prompt = RequirePrompt(game, DecisionKind.ProgramTrigger);
        var use = prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Require(prompt.IsPrivate && prompt.TargetSeat == HumanSeat &&
                prompt.Choices.Count == 2 &&
                use.Parameters.GetValueOrDefault("cards-used-this-turn") == "5" &&
                use.Parameters.GetValueOrDefault("current-hp") == "5",
            "Jingce must compare the exact turn-wide card-use count with current HP at Play end.");

        var revision = game.Revision;
        var forged = game.Submit(new AnswerPromptCommand(
            HumanSeat,
            prompt.PromptId,
            new ChoiceId("jingce.forged"),
            revision));
        Require(!forged.Accepted && game.Revision == revision,
            "An unpublished Jingce answer must be rejected without changing state.");

        var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        var skipped = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Answer(game, use);
        Answer(paused, RequirePrompt(paused, DecisionKind.ProgramTrigger).Choices.Single(choice => choice.Id == use.Id));
        AnswerAction(skipped, "skip");

        var resolved = game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Last(item =>
            item.SkillId == "classic:jingce");
        Require(resolved is
                {
                    OwnerSeat: HumanSeat,
                    Window: SkillProgramTriggerWindow.PlayEnding,
                    Activated: true,
                    Completed: true
                } &&
                game.CardMovements.Count(move =>
                    move.Reason.Value == "skill-program.classic:jingce.Draw" &&
                    move.To == CardLocation.Hand(HumanSeat)) == 2 &&
                game.State.Phase == TurnPhase.Discard &&
                game.PendingDecision is null,
            "Jingce must draw two exact physical cards and then continue to the Discard phase.");
        Require(skipped.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Last(item => item.SkillId == "classic:jingce") is
                { Activated: false, Completed: false } &&
                skipped.CardMovements.All(move => move.Reason.Value != "skill-program.classic:jingce.Draw"),
            "Skipping Jingce must not draw a card.");
        Require(State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "A paused Jingce choice must replay to the same state and event stream.");
    }

    public static void JingceRequiresUseCountAtLeastCurrentHp()
    {
        var (game, _) = CreateReadyGame();
        PlayCrossbows(game, count: 4);
        EndPlay(game);
        Require(game.PendingDecision is null &&
                game.State.Phase == TurnPhase.Discard &&
                !game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Any(item => item.SkillId == "classic:jingce"),
            "Four uses must not trigger Jingce while the Lord's current HP is five.");
    }

    public static void JingceDeduplicatesSourcesComposesInstancesAndRechecksOwnership()
    {
        var (game, _) = CreateReadyGame();
        PlayCrossbows(game, count: 5);
        var owner = Players(game)[HumanSeat];
        var template = owner.SkillGrants.Grants.Single(grant =>
            grant.SkillId == "classic:jingce" && grant.SourceId == CharacterState.PrimarySkillSource);
        owner.SkillGrants.Grant(new SkillGrant(
            "fixture:duplicate-source",
            template.SkillId,
            template.SkillInstanceId,
            "fixture:duplicate-source"));
        owner.SkillGrants.Grant(new SkillGrant(
            "fixture:distinct-source",
            template.SkillId,
            "fixture:jingce-distinct-instance",
            "fixture:distinct-source"));

        EndPlay(game);
        var first = RequirePrompt(game, DecisionKind.ProgramTrigger);
        AnswerAction(game, "activate");
        var second = RequirePrompt(game, DecisionKind.ProgramTrigger);
        Require(first.Choices[0].Parameters["skill-instance-id"] !=
                second.Choices[0].Parameters["skill-instance-id"],
            "A duplicate grant must collapse while a distinct Jingce instance receives its own candidate.");
        AnswerAction(game, "activate");
        Require(game.PendingDecision is null && game.State.Phase == TurnPhase.Discard &&
                game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Count(item => item.SkillId == "classic:jingce" && item.Activated && item.Completed) == 2 &&
                game.CardMovements.Count(move =>
                    move.Reason.Value == "skill-program.classic:jingce.Draw" &&
                    move.To == CardLocation.Hand(HumanSeat)) == 4,
            "Distinct Jingce instances must compose once each without duplicating the shared instance.");

        var (invalidated, _) = CreateReadyGame();
        PlayCrossbows(invalidated, count: 5);
        EndPlay(invalidated);
        var invalidatedOwner = Players(invalidated)[HumanSeat];
        var source = invalidatedOwner.SkillGrants.Grants.Single(grant =>
            grant.SkillId == "classic:jingce" && grant.SourceId == CharacterState.PrimarySkillSource);
        invalidatedOwner.SkillGrants.SetEnabled(source.GrantId, false);
        AnswerAction(invalidated, "activate");
        Require(invalidated.PendingDecision is null && invalidated.State.Phase == TurnPhase.Discard &&
                invalidated.CardMovements.All(move =>
                    move.Reason.Value != "skill-program.classic:jingce.Draw") &&
                invalidated.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Single(item => item.SkillId == "classic:jingce") is
                    { Activated: false, Completed: false },
            "A Jingce instance lost while its prompt is paused must skip safely and continue the parent once.");
    }

    public static void JingceUsesIndependentDangxianPlayWindows()
    {
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new DangxianJingceScenarioPackage());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 670099,
            PlayerCount = 6,
            ModeId = DangxianJingceScenarioPackage.ModeId,
            HumanSeat = HumanSeat,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 20
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "The Dangxian/Jingce fixture failed to start.");
        var selection = RequirePrompt(game, DecisionKind.SelectGeneral);
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat,
            DangxianJingceScenarioPackage.OwnerGeneralId,
            game.Revision,
            selection.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "The combined fixture could not select its owner.");

        ReachHumanPlay(game);
        PlayCrossbows(game, count: 4);
        EndPlay(game);
        var extraPrompt = RequirePrompt(game, DecisionKind.ProgramTrigger);
        Require(extraPrompt.Choices.All(choice =>
                choice.Parameters.GetValueOrDefault("cards-used-this-turn") == "4" &&
                choice.Parameters.GetValueOrDefault("current-hp") == "4"),
            "The extra Play window must freeze four turn uses against the current four HP.");
        AnswerAction(game, "activate");

        ReachHumanPlay(game);
        Require(game.Events.Select(item => item.Payload).OfType<PhaseChangedEvent>()
                    .Count(item => item.Phase == TurnPhase.Play && item.ActorSeat == HumanSeat) == 2,
            "Dangxian must reach a distinct normal Play phase after its extra Play phase finishes.");
        EndPlay(game);
        var normalPrompt = RequirePrompt(game, DecisionKind.ProgramTrigger);
        Require(normalPrompt.Choices.All(choice =>
                choice.Parameters.GetValueOrDefault("cards-used-this-turn") == "4"),
            "The normal Play window must retain the whole-turn count from the extra Play phase.");
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        AnswerAction(game, "skip");
        AnswerAction(replay, "skip");
        Require(game.State.Phase == TurnPhase.Discard && game.PendingDecision is null &&
                game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Count(item => item.SkillId == "classic:jingce") == 2 &&
                State(replay) == State(game) && Events(replay).SequenceEqual(Events(game)),
            "Extra and normal Play must each offer Jingce once, then replay to one Discard continuation.");
    }

    public static void JingceDoesNotOpenForSkippedNormalPlay()
    {
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new DangxianSkippedPlayScenarioPackage());
        GameEngine? game = null;
        for (var seed = 1; seed <= 256 && game is null; seed++)
        {
            var candidate = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 6,
                ModeId = DangxianSkippedPlayScenarioPackage.ModeId,
                HumanSeat = HumanSeat,
                HumanRole = Role.Lord,
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2,
                MaxTurns = 20
            }, registry);
            if (!candidate.Submit(new StartGameCommand()).Accepted ||
                candidate.PendingDecision is not { } selection ||
                !selection.ValidContentIds.Contains(DangxianSkippedPlayScenarioPackage.OwnerGeneralId))
                continue;
            var selected = candidate.Submit(new SelectGeneralCommand(
                HumanSeat,
                DangxianSkippedPlayScenarioPackage.OwnerGeneralId,
                candidate.Revision,
                selection.PromptId));
            if (!selected.Accepted) continue;
            ReachHumanPlay(candidate);
            var hand = candidate.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand;
            if (hand.Count(card => card.Kind == CardKind.Crossbow) >= 4) game = candidate;
        }
        Require(game is not null, "No deterministic skipped-normal-Play fixture had four usable cards.");
        var active = game!;
        PlaceNativeIndulgenceInOwnerJudgment(active);
        PlayCrossbows(active, count: 4);
        EndPlay(active);
        Require(RequirePrompt(active, DecisionKind.ProgramTrigger).SkillPrompt?.SkillId == "classic:jingce",
            "Dangxian's real extra Play must reach its one Jingce PlayEnding window.");
        AnswerAction(active, "skip");

        for (var step = 0; step < 64 &&
             !active.Events.Select(item => item.Payload).OfType<TurnEndedEvent>()
                 .Any(item => item.ActorSeat == HumanSeat); step++)
        {
            Require(active.PendingDecision?.SkillPrompt?.SkillId != "classic:jingce",
                "A skipped normal Play phase must not publish a second Jingce PlayEnding prompt.");
            var advanced = active.Submit(new AdvanceOneStepCommand(active.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "The skipped normal Play fixture could not advance.");
        }

        Require(active.Events.Select(item => item.Payload).OfType<DelayedCardResolvedEvent>().Any(item =>
                    item.CardKind == CardKind.Indulgence && item.TargetSeat == HumanSeat && item.SkippedPlayPhase) &&
                active.Events.Select(item => item.Payload).OfType<PhaseChangedEvent>()
                    .Count(item => item.ActorSeat == HumanSeat && item.Phase == TurnPhase.Play) == 1 &&
                active.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Count(item => item.SkillId == "classic:jingce") == 1 &&
                active.Events.Select(item => item.Payload).OfType<TurnEndedEvent>()
                    .Count(item => item.ActorSeat == HumanSeat) == 1,
            "Indulgence must skip normal Play without fabricating a second PlayEnding or TurnEnded event.");
    }

    private static void PlaceNativeIndulgenceInOwnerJudgment(GameEngine game)
    {
        var card = game.CreateCardZoneDiagnostics().Single(item => item.CardKind == CardKind.Indulgence);
        var store = typeof(GameEngine).GetField("_cardZones", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(game) ?? throw new InvalidOperationException("The card-zone store is unavailable.");
        var move = store.GetType().GetMethod("Move", BindingFlags.Public | BindingFlags.Instance) ??
            throw new InvalidOperationException("The card-zone move primitive is unavailable.");
        _ = move.Invoke(store, [card.CardId, card.Location, CardLocation.Judgment(HumanSeat)]);
    }

    private static (GameEngine Game, ContentRegistry Registry) CreateReadyGame()
    {
        var registry = Registry();
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 670021,
            PlayerCount = 6,
            ModeId = ScenarioPackage.ModeId,
            HumanSeat = HumanSeat,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 20
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "The Guo Huai fixture failed to start.");
        var selection = RequirePrompt(game, DecisionKind.SelectGeneral);
        Require(selection.ValidContentIds.Contains(GeneralId),
            "The Guo Huai fixture must offer its only formal general.");
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat,
            GeneralId,
            game.Revision,
            selection.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "The fixture could not select Guo Huai.");
        ReachHumanPlay(game);
        return (game, registry);
    }

    private static void PlayCrossbows(GameEngine game, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var prompt = RequirePrompt(game, DecisionKind.PlayCard);
            var hand = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand;
            var action = game.GetHumanLegalActions().First(candidate =>
                candidate.CardId is { } cardId &&
                hand.Single(card => card.Id == cardId).Kind == CardKind.Crossbow);
            var played = game.Submit(new PlayCardCommand(
                HumanSeat,
                action.CardId!.Value,
                action.TargetSeats,
                game.Revision,
                prompt.PromptId,
                action.PlayedCardKind,
                action.TargetCardId)
            {
                ConversionSource = action.ConversionSource,
                CardKindModifierSkill = action.CardKindModifierSkill,
                TargetCountModifierSkill = action.TargetCountModifierSkill
            });
            Require(played.Accepted, played.Error?.Message ?? "The fixture could not use Crossbow.");
            ReachHumanPlay(game);
        }
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 128; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat })
            {
                return;
            }
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "The fixture could not advance to Play.");
        }
        throw new InvalidOperationException("The fixture did not reach the human Play prompt.");
    }

    private static void EndPlay(GameEngine game)
    {
        var prompt = RequirePrompt(game, DecisionKind.PlayCard);
        var ended = game.Submit(new EndPlayPhaseCommand(
            HumanSeat,
            game.Revision,
            prompt.PromptId));
        Require(ended.Accepted, ended.Error?.Message ?? "The fixture could not end Play.");
    }

    private static void AnswerAction(GameEngine game, string action)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("No prompt is pending.");
        Answer(game, prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == action));
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("No prompt is pending.");
        var result = game.Submit(new AnswerPromptCommand(
            prompt.PlayerSeat,
            prompt.PromptId,
            choice.Id,
            game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "The Jingce answer was rejected.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new ScenarioPackage());

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static string State(GameEngine game) =>
        SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true));

    private static IReadOnlyList<string> Events(GameEngine game) => game.Events
        .Select(item =>
            $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static IReadOnlyList<CharacterState> Players(GameEngine game) =>
        (IReadOnlyList<CharacterState>)(typeof(GameEngine)
            .GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(game) ?? throw new InvalidOperationException("The fixture players are unavailable."));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-guo-huai-test-6";
        private const string DeckId = "fixture:guo-huai-card-use";
        private static readonly string[] TargetIds =
        [
            "fixture:guo-huai-target-1", "fixture:guo-huai-target-2",
            "fixture:guo-huai-target-3", "fixture:guo-huai-target-4",
            "fixture:guo-huai-target-5"
        ];

        public PackageManifest Manifest { get; } = new(
            "guo-huai-scenario",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 99, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in TargetIds)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "郭淮测试目标",
                    "supporter",
                    "standard:none",
                    "wei",
                    BaseHp: 4));
            }
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "郭淮用牌计数测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                [new ContentDeckCardCount("standard:crossbow", 96)]));
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "六人经典身份（郭淮场景）",
                MinPlayers: 6,
                MaxPlayers: 6,
                RoleCounts: new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 3,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId,
                GeneralCandidateCount: 6,
                GeneralPoolIds: [GeneralId, .. TargetIds]));
        }
    }

    private sealed class DangxianJingceScenarioPackage : IGameContentPackage
    {
        public const string OwnerGeneralId = "fixture:dangxian-jingce-owner";
        public const string ModeId = "identity:classic-dangxian-jingce-test-6";
        private const string DeckId = "fixture:dangxian-jingce-deck";

        public PackageManifest Manifest { get; } = new(
            "dangxian-jingce-scenario",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 99, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var generalIds = Enumerable.Range(0, 6).Select(index => index == 0
                ? OwnerGeneralId
                : $"fixture:dangxian-jingce-target-{index}").ToArray();
            builder.AddGeneral(new ContentGeneralDefinition(
                OwnerGeneralId,
                "当先精策测试武将",
                "supporter",
                "classic:dangxian",
                "wei",
                BaseHp: 3,
                AdditionalSkillIds: ["classic:jingce"]));
            foreach (var id in generalIds.Skip(1))
                builder.AddGeneral(new ContentGeneralDefinition(
                    id, "当先精策测试目标", "supporter", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "当先精策阶段测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                [new ContentDeckCardCount("standard:crossbow", 96)]));
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "六人经典身份（当先精策场景）",
                6,
                6,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 3,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId,
                GeneralCandidateCount: 6,
                GeneralPoolIds: generalIds));
        }
    }

    private sealed class DangxianSkippedPlayScenarioPackage : IGameContentPackage
    {
        public const string OwnerGeneralId = "fixture:dangxian-jingce-skipped-owner";
        public const string ModeId = "identity:classic-dangxian-jingce-skipped-test-6";
        private const string DeckId = "fixture:dangxian-jingce-skipped-deck";

        public PackageManifest Manifest { get; } = new(
            "dangxian-jingce-skipped-scenario",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 99, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var generalIds = Enumerable.Range(0, 6).Select(index => index == 0
                ? OwnerGeneralId
                : $"fixture:dangxian-jingce-skipped-target-{index}").ToArray();
            builder.AddGeneral(new ContentGeneralDefinition(
                OwnerGeneralId,
                "当先精策跳过阶段测试武将",
                "supporter",
                "classic:dangxian",
                "wei",
                BaseHp: 3,
                AdditionalSkillIds: ["classic:jingce"]));
            foreach (var id in generalIds.Skip(1))
                builder.AddGeneral(new ContentGeneralDefinition(
                    id, "当先精策跳过阶段测试目标", "supporter", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "当先精策跳过阶段测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards =
                [
                    new ContentDeckPhysicalCard("standard:indulgence", Suit.Spade, 6),
                    .. Enumerable.Range(0, 95).Select(index =>
                        new ContentDeckPhysicalCard("standard:crossbow", Suit.Spade, index % 13 + 1))
                ]
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "六人经典身份（正常出牌阶段跳过场景）",
                6,
                6,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 3,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId,
                GeneralCandidateCount: 6,
                GeneralPoolIds: generalIds));
        }
    }
}
