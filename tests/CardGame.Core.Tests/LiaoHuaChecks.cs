using CardGame.Content.Standard;
using CardGame.Core;

internal static class LiaoHuaChecks
{
    private const int HumanSeat = 0;
    private const string GeneralId = "classic:liao-hua";
    private const string HarnessGeneralId = "fixture:liao-hua-harness";
    private const string DangxianSkillId = "classic:dangxian";
    private const string FuliSkillId = "classic:fuli";

    public static void ContentAndRulesBoundary()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 88, 0));
        var previous = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 87, 0));
        Require(current.Packages.Any(package =>
                package.Id == "standard-classic-generals" &&
                package.Version == new Version(1, 88, 0)),
            "The current registry must load classic-general package 1.88.0.");

        var general = current.Generals[GeneralId];
        Require(general.FactionId == "shu" && general.BaseHp == 4 &&
                general.Gender == GeneralGender.Male && general.PortraitKey == "liao_hua" &&
                general.SkillIds.SequenceEqual([DangxianSkillId, FuliSkillId]),
            $"Classic Liao Hua metadata drifted: faction={general.FactionId}, hp={general.BaseHp}, " +
            $"gender={general.Gender}, portrait={general.PortraitKey}, skills={string.Join(',', general.SkillIds)}.");

        var dangxian = current.Skills[DangxianSkillId];
        Require(dangxian.LegacyKind is null && dangxian.Program is { } dangxianProgram &&
                dangxianProgram.Triggers.Single() is
                {
                    Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,
                    Optional: false
                } &&
                dangxian.Tags == SkillTag.Locked &&
                dangxian.ExecutionForms == SkillExecutionForm.State &&
                dangxian.ActionForms == SkillActionForm.None,
            $"Dangxian metadata drifted: kind={dangxian.LegacyKind}, tags={dangxian.Tags}, " +
            $"execution={dangxian.ExecutionForms}, actions={dangxian.ActionForms}.");
        var fuli = current.Skills[FuliSkillId];
        Require(fuli.LegacyKind is null && fuli.Program is { } fuliProgram &&
                fuliProgram.Triggers.Single() is
                {
                    Window: SkillProgramTriggerWindow.SelfDyingResponse,
                    Optional: true,
                    UsageScope: SkillUsageScope.Game,
                    UsageLimit: 1
                } &&
                fuli.Tags == SkillTag.Limited &&
                fuli.ExecutionForms == SkillExecutionForm.Trigger &&
                fuli.ActionForms == SkillActionForm.None,
            $"Fuli metadata drifted: kind={fuli.LegacyKind}, tags={fuli.Tags}, " +
            $"execution={fuli.ExecutionForms}, actions={fuli.ActionForms}.");
        Require(!previous.Generals.ContainsKey(GeneralId) &&
                !previous.Skills.ContainsKey(DangxianSkillId) &&
                !previous.Skills.ContainsKey(FuliSkillId) &&
                current.ContentHash != previous.ContentHash,
            "Package 1.88.0 must add Liao Hua without mutating the 1.87.0 registry boundary.");

    }

    public static void DangxianExtraPhaseResetsPhaseLimitsAndReplays()
    {
        var registry = CreateRegistry();
        var game = CreateGame(registry, ScenarioPackage.FormalModeId, seed: 1);
        StartAndSelect(game, GeneralId);
        ReachHumanPlay(game);

        var extraPrompt = RequirePrompt(game, DecisionKind.PlayCard);
        Require(game.Events.Select(item => item.Payload).OfType<ProgramPhaseScheduledEvent>()
                    .Count(item => item.SkillId == DangxianSkillId && item.Started) == 1 &&
                game.CardMovements.All(move =>
                    move.To != CardLocation.Hand(HumanSeat) || move.Reason != CardMoveReasons.Draw),
            "Dangxian must enter an extra Play phase before the normal Draw phase.");

        var pausedReplay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(pausedReplay.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat } &&
                SnapshotJson.Serialize(pausedReplay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)),
            "A paused Dangxian extra Play phase must replay exactly.");

        var slash = game.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.Slash && action.TargetSeats.Count == 1);
        var played = game.Submit(new PlayCardCommand(
            HumanSeat,
            slash.CardId!.Value,
            slash.TargetSeats,
            game.Revision,
            extraPrompt.PromptId,
            slash.PlayedCardKind,
            slash.TargetCardId)
        {
            ConversionSource = slash.ConversionSource
        });
        Require(played.Accepted, played.Error?.Message ?? "Dangxian rejected its first Slash.");
        ReachHumanPlay(game);
        Require(game.GetHumanLegalActions().All(action => action.Kind != LegalActionKind.Slash),
            "The first Dangxian Play phase must still enforce its own one-Slash limit.");

        var endPrompt = RequirePrompt(game, DecisionKind.PlayCard);
        var ended = game.Submit(new EndPlayPhaseCommand(
            HumanSeat,
            game.Revision,
            endPrompt.PromptId));
        Require(ended.Accepted, ended.Error?.Message ?? "Dangxian extra Play phase could not end.");
        ReachHumanPlay(game);

        var dangxianEvents = game.Events.Select(item => item.Payload)
            .OfType<ProgramPhaseScheduledEvent>()
            .Where(item => item.SkillId == DangxianSkillId).ToArray();
        Require(dangxianEvents.Length == 2 && dangxianEvents[0].Started && !dangxianEvents[1].Started &&
                game.Events.Select(item => item.Payload).OfType<PhaseChangedEvent>()
                    .Count(item => item.ActorSeat == HumanSeat && item.Phase == TurnPhase.Play) == 2 &&
                game.CardMovements.Count(move =>
                    move.To == CardLocation.Hand(HumanSeat) && move.Reason == CardMoveReasons.Draw) == 2 &&
                game.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.Slash),
            "Ending Dangxian must resume normal draw, enter a second Play phase and reset phase Slash usage.");

        var completedReplay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(SnapshotJson.Serialize(completedReplay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)),
            "A completed Dangxian command prefix must replay exactly.");
    }

    public static void FuliRecoversFlipsConsumesAndReplays()
    {
        var registry = CreateRegistry();
        var game = CreateGame(registry, ScenarioPackage.FuliModeId, seed: 1);
        StartAndSelect(game, HarnessGeneralId);
        ReachHumanPlay(game);

        var initialHp = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hp;
        for (var expectedHp = initialHp - 1; expectedHp >= 1; expectedHp--)
        {
            UseKujin(game);
            ReachHumanPlay(game);
            Require(game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hp == expectedHp,
                $"Kujin setup did not reduce Liao Hua to {expectedHp} HP.");
        }

        UseKujin(game);
        var fuliPrompt = RequirePrompt(game, DecisionKind.RescueDying);
        Require(fuliPrompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("response") == "program-trigger" &&
                    choice.Parameters.GetValueOrDefault("skill-id") == FuliSkillId) &&
                fuliPrompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("response") == "let-die"),
            "Fuli must appear as an optional private configured dying response.");

        var paused = RoundTrip(game.CreateCheckpoint());
        var replay = GameReplay.Restore(paused, registry);
        AnswerFuli(game);
        AnswerFuli(replay);
        ReachHumanPlay(game);
        ReachHumanPlay(replay);

        var resolved = game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
            .Single(item => item.SkillId == FuliSkillId && item.Activated);
        var owner = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat];
        var fuliState = (owner.SkillRuntimeStates ?? [])
            .Single(state => state.SkillId == FuliSkillId);
        Require(resolved is
                {
                    OwnerSeat: HumanSeat,
                    Window: SkillProgramTriggerWindow.SelfDyingResponse,
                    Completed: true
                } && owner.Hp == 4 && owner.IsFaceDown &&
                fuliState.Usages.Single(usage => usage.Scope == SkillUsageScope.Game).Count == 1,
            "Fuli must recover to the living-faction count, flip the general and consume its game-scoped use.");
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)),
            "A paused Fuli dying choice must replay deterministically through recovery and flip.");

        for (var expectedHp = 3; expectedHp >= 1; expectedHp--)
        {
            UseKujin(game);
            ReachHumanPlay(game);
        }
        UseKujin(game);
        Require(game.PendingDecision is null &&
                game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hp == 0 &&
                game.Events.Select(item => item.Payload).OfType<ProgramBindingStartedEvent>()
                    .Count(item => item.SkillId == FuliSkillId) == 1,
            "Consumed Fuli must not be offered again in a later dying window.");
    }

    private static void UseKujin(GameEngine game)
    {
        var prompt = RequirePrompt(game, DecisionKind.PlayCard);
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseSkill && candidate.Skill == SkillKind.Kujin);
        var result = game.Submit(new UseSkillCommand(
            HumanSeat,
            SkillKind.Kujin,
            [],
            [],
            game.Revision,
            prompt.PromptId));
        Require(result.Accepted, result.Error?.Message ?? "The Fuli setup could not use Kujin.");
    }

    private static void AnswerFuli(GameEngine game)
    {
        var prompt = RequirePrompt(game, DecisionKind.RescueDying);
        var choice = prompt.Choices.Single(candidate =>
            candidate.Parameters.GetValueOrDefault("response") == "program-trigger" &&
            candidate.Parameters.GetValueOrDefault("skill-id") == FuliSkillId);
        var result = game.Submit(new AnswerPromptCommand(
            HumanSeat,
            prompt.PromptId,
            choice.Id,
            game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "The Fuli dying response was rejected.");
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 2_048; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            Require(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while reaching Liao Hua play.");
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "The Liao Hua fixture could not advance.");
        }
        throw new InvalidOperationException("The Liao Hua fixture did not reach human play in bounded steps.");
    }

    private static void StartAndSelect(GameEngine game, string generalId)
    {
        Require(game.Submit(new StartGameCommand()).Accepted, "The Liao Hua fixture failed to start.");
        var selection = RequirePrompt(game, DecisionKind.SelectGeneral);
        Require(selection.ValidContentIds.Contains(generalId, StringComparer.Ordinal),
            $"The Liao Hua fixture did not offer {generalId}.");
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat,
            generalId,
            game.Revision,
            selection.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "The Liao Hua fixture could not select its general.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { PlayerSeat: HumanSeat } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected human {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");

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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string FormalModeId = "identity:classic-liao-hua-test-4";
        public const string FuliModeId = "identity:classic-liao-hua-fuli-test-4";
        private const string DeckId = "fixture:liao-hua-slash-deck";
        private static readonly (string Id, string Faction)[] BlankGenerals =
        [
            ("fixture:liao-hua-wei", "wei"),
            ("fixture:liao-hua-wu", "wu"),
            ("fixture:liao-hua-qun", "qun")
        ];

        public PackageManifest Manifest { get; } = new(
            "liao-hua-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 88, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var (id, faction) in BlankGenerals)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "廖化测试目标",
                    "supporter",
                    "standard:none",
                    faction,
                    BaseHp: 8));
            }
            builder.AddGeneral(new ContentGeneralDefinition(
                HarnessGeneralId,
                "廖化技能测试",
                "liao_hua",
                "classic:kujin",
                "shu",
                BaseHp: 4,
                AdditionalSkillIds: [DangxianSkillId, FuliSkillId]));

            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "廖化当先伏枥测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = Enumerable.Range(0, 192)
                    .Select(index => new ContentDeckPhysicalCard(
                        "standard:slash",
                        (Suit)(index % 4),
                        index % 13 + 1))
                    .ToArray()
            });
            AddMode(builder, FormalModeId, "廖化当先测试", GeneralId);
            AddMode(builder, FuliModeId, "廖化伏枥测试", HarnessGeneralId);
        }

        private static void AddMode(
            IContentRegistryBuilder builder,
            string modeId,
            string name,
            string ownerGeneralId) =>
            builder.AddMode(new ContentModeDefinition(
                modeId,
                name,
                4,
                4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId,
                GeneralCandidateCount: 4,
                GeneralPoolIds: [ownerGeneralId, .. BlankGenerals.Select(item => item.Id)]));
    }
}
