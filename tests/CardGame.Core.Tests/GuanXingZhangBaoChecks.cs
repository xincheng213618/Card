using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class GuanXingZhangBaoChecks
{
    private const int HumanSeat = 0;
    private const string GeneralId = "classic:guan-xing-zhang-bao";
    private const string FuhunSkillId = "classic:fuhun";

    public static void ContentAndRulesBoundary()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        var general = current.Generals[GeneralId];
        var fuhun = current.Skills[FuhunSkillId];

        Require(general is
                {
                    Name: "关兴张苞",
                    PortraitKey: "guan_xing_zhang_bao",
                    FactionId: "shu",
                    BaseHp: 4,
                    Gender: GeneralGender.Male
                } && general.SkillIds.SequenceEqual([FuhunSkillId]),
            "Classic Guan Xing & Zhang Bao metadata drifted.");
        Require(fuhun.LegacyKind is null &&
                fuhun.Tags == SkillTag.None &&
                fuhun.ExecutionForms == (SkillExecutionForm.State | SkillExecutionForm.Trigger) &&
                fuhun.ActionForms == SkillActionForm.Active &&
                fuhun.Program is
                {
                    RuntimeVersion: "skill-program-v35",
                    MinimumRulesVersion: 140,
                    ViewAs.Count: 1,
                    Activations.Count: 1,
                    Triggers.Count: 1
                } program &&
                program.ViewAs.Single().InputCount == 2,
            $"Fuhun metadata drifted: kind={fuhun.LegacyKind}, tags={fuhun.Tags}, " +
            $"execution={fuhun.ExecutionForms}, actions={fuhun.ActionForms}.");

    }

    public static void ActiveSlashGrantsParentSkillsForOneTurnAndReplays()
    {
        var registry = CreateRegistry();
        var game = CreateGame(registry, ScenarioPackage.ActiveModeId, seed: 1);
        StartAndSelect(game);
        ReachHumanPlay(game);

        var prompt = RequirePrompt(game, DecisionKind.PlayCard);
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseProgramSkill &&
            candidate.ProgramSkillId == FuhunSkillId &&
            candidate.ProgramActivationId == "two-hand-cards-as-slash");
        var costIds = action.SelectableCardIds.Take(2).ToArray();
        var targetSeat = action.SelectableTargetSeats.First();
        var targetHp = Player(game, targetSeat).Hp;
        Require(action is { MinCardCount: 2, MaxCardCount: 2, MinTargetCount: 1, MaxTargetCount: 1 } &&
                action.SelectableCardIds.Order().SequenceEqual(Player(game, HumanSeat).Hand.Select(card => card.Id).Order()),
            "Fuhun must publish exactly two hand cards and one legal Slash target.");

        var used = game.Submit(new UseProgramSkillCommand(
            HumanSeat,
            FuhunSkillId,
            "two-hand-cards-as-slash",
            costIds,
            [targetSeat],
            game.Revision,
            prompt.PromptId));
        Require(used.Accepted, used.Error?.Message ?? "The exact Fuhun Slash was rejected.");
        ReachHumanPlay(game);

        var converted = game.Events.Select(item => item.Payload)
            .OfType<ProgramViewAsConvertedEvent>().Single(item => item.IsUse && item.SkillId == FuhunSkillId);
        var granted = game.Events.Select(item => item.Payload)
            .OfType<ProgramTurnSkillsGrantedEvent>().Single(item => item.SkillId == FuhunSkillId);
        var owner = Player(game, HumanSeat);
        var ownerSkills = owner.Skills ?? throw new InvalidOperationException("Fuhun owner skills are hidden.");
        var ownerRuntimeStates = owner.SkillRuntimeStates ??
            throw new InvalidOperationException("Fuhun owner runtime states are hidden.");
        var actionAudit = game.Events.Select(item => item.Payload)
            .OfType<CardActionAcceptedEvent>()
            .Single(item => item.Action.ConversionChain.Any(source => source.SkillId == FuhunSkillId));
        Require(converted.PhysicalCardIds.SequenceEqual(costIds) &&
                actionAudit.Action.PhysicalCards.Select(card => card.CardId).SequenceEqual(costIds) &&
                Player(game, targetSeat).Hp == targetHp - 1 &&
                granted.GrantedSkillIds.SequenceEqual(["classic:wusheng", "classic:paoxiao"]) &&
                ownerSkills.Select(skill => skill.ContentId).Contains("classic:wusheng") &&
                ownerSkills.Select(skill => skill.ContentId).Contains("classic:paoxiao") &&
                ownerRuntimeStates.Single(state => state.SkillId == "classic:wusheng").IsAcquired &&
                ownerRuntimeStates.Single(state => state.SkillId == "classic:paoxiao").IsAcquired &&
                ownerRuntimeStates.Single(state => state.SkillId == FuhunSkillId).Usages
                    .Single(usage => usage.Scope == SkillUsageScope.Turn &&
                                     usage.UsageId.StartsWith("grant-parent-skills@", StringComparison.Ordinal)).Count == 1,
            "A damaging Fuhun Slash must audit both costs and grant acquired Wusheng/Paoxiao for this turn.");

        for (var use = 0; use < 2; use++)
        {
            var slash = game.GetHumanLegalActions().FirstOrDefault(candidate =>
                candidate.Kind == LegalActionKind.Slash &&
                candidate.TargetSeat == targetSeat &&
                candidate.ConversionSource?.SkillId == "classic:wusheng") ??
                throw new InvalidOperationException(
                    $"Fuhun-granted Wusheng/Paoxiao must allow converted Slash {use + 1} after the counted Fuhun Slash.");
            var slashPrompt = RequirePrompt(game, DecisionKind.PlayCard);
            var result = game.Submit(new PlayCardCommand(
                HumanSeat,
                slash.CardId!.Value,
                slash.TargetSeats,
                game.Revision,
                slashPrompt.PromptId,
                slash.PlayedCardKind,
                slash.TargetCardId)
            {
                ConversionSource = slash.ConversionSource
            });
            Require(result.Accepted, result.Error?.Message ?? "Fuhun-granted Wusheng Slash was rejected.");
            ReachHumanPlay(game);
        }

        var replayed = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(State(replayed) == State(game) && Events(replayed).SequenceEqual(Events(game)),
            "A completed Fuhun use and turn-scoped grant must replay exactly.");

        EndHumanPlay(game);
        ReachNextHumanPlay(game);
        owner = Player(game, HumanSeat);
        Require(owner.Skills!.All(skill => skill.ContentId is not ("classic:wusheng" or "classic:paoxiao")) &&
                owner.SkillRuntimeStates!.All(state => !state.IsAcquired),
            "Fuhun-granted Wusheng and Paoxiao must expire at the next turn boundary.");
    }

    public static void SlashResponseUsesExactPairWithoutGrantAndReplays()
    {
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 128; seed++)
        {
            var game = CreateGame(registry, ScenarioPackage.ResponseModeId, seed);
            StartAndSelect(game);
            ReachHumanPlay(game);
            EndHumanPlay(game);

            for (var step = 0; step < 512 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.RespondSlash, PlayerSeat: HumanSeat } response &&
                    response.IncomingCard == CardKind.BarbarianAssault &&
                    response.Choices.FirstOrDefault(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "program-view-as-slash" &&
                        choice.Parameters.GetValueOrDefault("conversion-skill-id") == FuhunSkillId) is { } choice)
                {
                    var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
                    Require(State(paused) == State(game),
                        "A paused private Fuhun response prompt must replay exactly.");
                    var answered = game.Submit(new AnswerPromptCommand(
                        HumanSeat,
                        response.PromptId,
                        choice.Id,
                        game.Revision));
                    Require(answered.Accepted, answered.Error?.Message ?? "The Fuhun response was rejected.");

                    var converted = game.Events.Select(item => item.Payload)
                        .OfType<ProgramViewAsConvertedEvent>().Last(item => item.SkillId == FuhunSkillId);
                    var owner = Player(game, HumanSeat);
                    Require(!converted.IsUse && converted.PhysicalCardIds.SequenceEqual(choice.Cards) &&
                            choice.Cards.All(cardId => game.CardMovements.Any(move =>
                                move.CardId == cardId &&
                                move.From == CardLocation.Hand(HumanSeat) &&
                                move.To == CardLocation.Processing &&
                                move.Reason == CardMoveReasons.Respond)) &&
                            game.Events.Select(item => item.Payload).OfType<ProgramTurnSkillsGrantedEvent>()
                                .All(item => item.SkillId != FuhunSkillId) &&
                            owner.Skills!.All(skill => skill.ContentId is not ("classic:wusheng" or "classic:paoxiao")),
                        "A response Fuhun must pay both exact hand cards without granting parent skills.");

                    var replayed = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
                    Require(State(replayed) == State(game) && Events(replayed).SequenceEqual(Events(game)),
                        "A completed Fuhun Slash response must replay exactly.");
                    return;
                }

                if (game.PendingDecision is { PlayerSeat: HumanSeat } human)
                {
                    if (human.Kind == DecisionKind.PlayCard)
                    {
                        EndHumanPlay(game);
                        continue;
                    }
                    break;
                }
                var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
                if (!advanced.Accepted) break;
            }
        }

        throw new InvalidOperationException("No bounded Barbarian Assault Fuhun response was found.");
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 2_048; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            Require(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while reaching Guan Xing & Zhang Bao play.");
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "The Fuhun fixture could not advance.");
        }
        throw new InvalidOperationException("The Fuhun fixture did not reach human play in bounded steps.");
    }

    private static void ReachNextHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 4_096 && game.State.Status != EngineStatus.Completed; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            if (game.PendingDecision is { PlayerSeat: HumanSeat })
                throw new InvalidOperationException($"Unexpected human prompt {game.PendingDecision.Kind} before next turn.");
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "The Fuhun fixture could not reach the next turn.");
        }
        throw new InvalidOperationException("The Fuhun fixture did not reach the next human Play phase.");
    }

    private static void StartAndSelect(GameEngine game)
    {
        Require(game.Submit(new StartGameCommand()).Accepted, "The Fuhun fixture failed to start.");
        var prompt = RequirePrompt(game, DecisionKind.SelectGeneral);
        Require(prompt.ValidContentIds.Contains(GeneralId, StringComparer.Ordinal),
            "The Fuhun fixture did not offer Guan Xing & Zhang Bao.");
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat,
            GeneralId,
            game.Revision,
            prompt.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "The Fuhun fixture could not select its general.");
    }

    private static void EndHumanPlay(GameEngine game)
    {
        var prompt = RequirePrompt(game, DecisionKind.PlayCard);
        var result = game.Submit(new EndPlayPhaseCommand(HumanSeat, game.Revision, prompt.PromptId));
        Require(result.Accepted, result.Error?.Message ?? "The Fuhun fixture could not end Play.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { PlayerSeat: HumanSeat } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected human {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");

    private static PlayerSnapshot Player(GameEngine game, int seat) =>
        game.CreateSnapshot(HumanSeat, revealAll: true).Players.Single(player => player.Seat == seat);

    private static GameEngine CreateGame(
        ContentRegistry registry,
        string modeId,
        int seed,
        int rulesVersion = GameCheckpoint.CurrentRulesVersion)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 4,
            ModeId = modeId,
            HumanSeat = HumanSeat,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 40
        }, registry);
        return rulesVersion == GameCheckpoint.CurrentRulesVersion
            ? game
            : GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
    }

    private static ContentRegistry CreateRegistry() => ContentRegistry.Build(
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
        .Select(item => $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ActiveModeId = "identity:classic-fuhun-active-test-4";
        public const string ResponseModeId = "identity:classic-fuhun-response-test-4";
        private const string ActiveDeckId = "fixture:fuhun-heart-peach-deck";
        private const string ResponseDeckId = "fixture:fuhun-response-deck";
        private static readonly string[] BlankGenerals =
            ["fixture:fuhun-target-1", "fixture:fuhun-target-2", "fixture:fuhun-target-3"];

        public PackageManifest Manifest { get; } = new(
            "guan-xing-zhang-bao-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 119, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in BlankGenerals)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "父魂测试目标",
                    "supporter",
                    "standard:none",
                    "wei",
                    BaseHp: 8));
            }

            builder.AddDeck(new ContentDeckRecipe(
                ActiveDeckId,
                "父魂主动测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = Enumerable.Range(1, 192)
                    .Select(rank => new ContentDeckPhysicalCard(
                        "standard:peach",
                        Suit.Heart,
                        rank % 13 + 1))
                    .ToArray()
            });
            builder.AddDeck(new ContentDeckRecipe(
                ResponseDeckId,
                "父魂响应测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = Enumerable.Range(1, 192)
                    .Select(index => new ContentDeckPhysicalCard(
                        index % 3 == 0 ? "standard:barbarian_assault" : "standard:peach",
                        index % 2 == 0 ? Suit.Heart : Suit.Club,
                        index % 13 + 1))
                    .ToArray()
            });
            AddMode(builder, ActiveModeId, ActiveDeckId);
            AddMode(builder, ResponseModeId, ResponseDeckId);
        }

        private static void AddMode(IContentRegistryBuilder builder, string modeId, string deckId) =>
            builder.AddMode(new ContentModeDefinition(
                modeId,
                "父魂测试",
                4,
                4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1,
                    [nameof(Role.Renegade)] = 1
                },
                deckId,
                GeneralCandidateCount: 4,
                GeneralPoolIds: [GeneralId, .. BlankGenerals]));
    }
}
