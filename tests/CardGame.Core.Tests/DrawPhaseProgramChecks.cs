using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DrawPhaseProgramChecks
{
    private const int HumanSeat = 0;

    public static void DefinitionsAndVersionBoundaries()
    {
        Require(GameCheckpoint.CurrentRulesVersion >= 125,
            "Schema-20 draw-policy programs require at least rules version 125.");
        Require(StandardContentPackage.CurrentVersion == new Version(1, 14, 0) &&
                StandardClassicGeneralPackage.CurrentVersion >= new Version(1, 106, 0),
            "The current standard and classic packages must expose their draw-phase-program boundaries.");

        var currentStandard = StandardContentRegistry.Create();
        var currentClassic = StandardContentRegistry.CreateWithClassicGenerals();
        var standard = currentStandard.Skills["standard:yingzi"];
        var classic = currentClassic.Skills["classic:yingzi"];
        var tuxi = currentClassic.Skills["classic:tuxi"];
        var zaiqi = currentClassic.Skills["classic:zaiqi"];
        var luoyi = currentClassic.Skills["classic:luoyi"];

        Require(standard is { LegacyKind: null, Program: not null } &&
                standard.Program!.MinimumRulesVersion == 170,
            "Standard Yingzi must use its configured program.");
        Require(classic is { LegacyKind: null, Program: not null } &&
                classic.Program!.MinimumRulesVersion == 170,
            "Classic Yingzi must use its configured program.");
        Require(tuxi is { LegacyKind: null, Program: not null } &&
                tuxi.Program!.MinimumRulesVersion == 170 &&
                tuxi.Program.Triggers.Single() is
                {
                    Window: SkillProgramTriggerWindow.DrawPhaseStarting,
                    Optional: true,
                    Priority: 100,
                    DrawPhaseMode: SkillProgramDrawPhaseMode.Replacement,
                    Effects:
                    [
                        { Op: SkillProgramEffectOp.SelectTargets,
                          TargetKind: SkillProgramTargetKind.OtherLivingWithHand,
                          MinimumTargets: 1,
                          MaximumTargets: 2,
                          TargetAiOrder: SkillProgramTargetAiOrder.HostileThenHandCount },
                        { Op: SkillProgramEffectOp.TakeRandomHandCardFromSelectedTargets, Amount: 1 }
                    ]
                },
            "Classic Tuxi did not preserve the classic@1.102/schema-17 replacement boundary.");
        Require(zaiqi is { LegacyKind: null, Program: not null } &&
                zaiqi.Program!.MinimumRulesVersion == 170 &&
                zaiqi.Program.Triggers.Single() is
                {
                    Window: SkillProgramTriggerWindow.DrawPhaseStarting,
                    Optional: true,
                    Priority: 90,
                    DrawPhaseMode: SkillProgramDrawPhaseMode.Replacement,
                    Effects:
                    [
                        { Op: SkillProgramEffectOp.RevealTopCards,
                          NumberExpression: SkillProgramNumberExpression.OwnerLostHp,
                          ResultBind: "revealed",
                          Visibility: SkillProgramCardSetVisibility.Public },
                        { Op: SkillProgramEffectOp.FilterBoundCards,
                          SourceBind: "revealed", ResultBind: "hearts", Suits: [Suit.Heart] },
                        { Op: SkillProgramEffectOp.MoveBoundCards,
                          SourceBind: "hearts", Destination: SkillProgramCardDestination.DiscardPile },
                        { Op: SkillProgramEffectOp.MoveBoundCards,
                          SourceBind: "revealed", ExceptBind: "hearts",
                          Destination: SkillProgramCardDestination.OwnerHand },
                        { Op: SkillProgramEffectOp.Recover,
                          NumberExpression: SkillProgramNumberExpression.BoundCardCount,
                          SourceBind: "hearts" }
                    ]
                },
            "Classic Zaiqi did not preserve the classic@1.103/schema-18 reveal replacement boundary.");
        Require(luoyi is { LegacyKind: null, Program: not null } &&
                luoyi.Program!.MinimumRulesVersion == 170 &&
                luoyi.Program.Triggers.Single() is
                {
                    Window: SkillProgramTriggerWindow.DrawPhaseStarting,
                    Optional: true,
                    Priority: 80,
                    DrawPhaseMode: SkillProgramDrawPhaseMode.Additive,
                    Effects:
                    [
                        { Op: SkillProgramEffectOp.AdjustNormalDraw, Amount: -1 },
                        { Op: SkillProgramEffectOp.GrantTurnCardDamageModifier,
                          Amount: 1,
                          CardKinds: [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Duel] }
                    ]
                },
            "Classic Luoyi did not preserve the classic@1.104/schema-19 draw-adjustment boundary.");

        var standardTrigger = standard.Program!.Triggers.Single();
        var classicTrigger = classic.Program!.Triggers.Single();
        Require(standardTrigger is
                {
                    Window: SkillProgramTriggerWindow.DrawPhaseStarting,
                    Optional: false,
                    Effects: [{ Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.Owner, Amount: 1 }]
                } &&
                classicTrigger is
                {
                    Window: SkillProgramTriggerWindow.DrawPhaseStarting,
                    Optional: true,
                    Effects: [{ Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.Owner, Amount: 1 }]
                },
            "Both Yingzi definitions must use the bounded fixed owner-draw primitive with their printed optionality.");

        var recovered = SkillProgramCatalog.Load(ValidationRules.Replace(
            "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}",
            "{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}",
            StringComparison.Ordinal), ValidationPresentation).Programs["fixture:draw-validation"];
        Require(recovered.Triggers.Single().Effects.Single().Op == SkillProgramEffectOp.Recover,
            "The current draw-phase window supports the shared recovery operation.");
        Reject(ReplacementValidationRules.Replace("\"maximumTargets\":2", "\"maximumTargets\":3",
            StringComparison.Ordinal), "target bounds exceed");
        Reject(ReplacementValidationRules.Replace(
            "\"op\":\"takeRandomHandCardFromSelectedTargets\",\"target\":\"owner\",\"amount\":1",
            "\"op\":\"draw\",\"target\":\"selectedTarget\",\"amount\":1",
            StringComparison.Ordinal), "selectedTarget must be produced");
        Reject(RevealReplacementValidationRules.Replace(
            "\"sourceBind\":\"revealed\",\"resultBind\":\"hearts\",\"suits\":[\"heart\"]",
            "\"sourceBind\":\"missing\",\"resultBind\":\"hearts\",\"suits\":[\"heart\"]",
            StringComparison.Ordinal), "unknown card binding 'missing'");
        var positiveAdjustment = SkillProgramCatalog.Load(
            AdjustmentValidationRules.Replace("\"amount\":-1", "\"amount\":1", StringComparison.Ordinal),
            ValidationPresentation.Replace(
                "fixture:draw-validation", "fixture:draw-adjustment-validation", StringComparison.Ordinal));
        Require(positiveAdjustment.Programs["fixture:draw-adjustment-validation"].Triggers.Single()
                .Effects.First().Amount == 1,
            "Current programs must accept positive normal-draw adjustments without a fixed recipe shape.");
        Reject(AdjustmentValidationRules.Replace("\"amount\":-1", "\"amount\":0",
            StringComparison.Ordinal), "non-zero fixed amount");
        Reject(AdjustmentValidationRules.Replace("\"amount\":-1", "\"amount\":-21",
            StringComparison.Ordinal), "specify a non-zero fixed amount");
        Reject(AdjustmentValidationRules.Replace(
            "[\"slash\",\"fireSlash\",\"thunderSlash\",\"duel\"]",
            "[\"slash\",\"slash\"]",
            StringComparison.Ordinal), "duplicate values");
    }

    public static void StandardMandatoryExtraDrawRunsBeforeNormalDraw()
    {
        var registry = StandardContentRegistry.Create();
        var game = SelectGeneral(registry, "identity:standard-5", "standard:zhou-yu");

        var advanced = game.Submit(new AdvanceCommand(game.Revision));
        Require(advanced.Accepted && game.State.Phase == TurnPhase.Play &&
                game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat },
            advanced.Error?.Message ??
            $"Standard Yingzi did not reach the Play boundary (phase={game.State.Phase}, prompt={game.PendingDecision?.Kind.ToString() ?? "none"}, hand={HandCount(game)})." );
        Require(HandCount(game) == 7 &&
                game.CardMovements.Count(move =>
                    move.Reason.Value == "skill-program.standard:yingzi.Draw" &&
                    move.To == CardLocation.Hand(HumanSeat)) == 1 &&
                game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(item =>
                    item.SkillId == "standard:yingzi" &&
                    item.Window == SkillProgramTriggerWindow.DrawPhaseStarting &&
                    item.Activated && item.Completed) &&
                game.Events.Select(item => item.Payload).All(item =>
                    item is not DrawSkillResolvedEvent { Skill: SkillKind.Yingzi }),
            "Standard Yingzi must draw one configured card plus exactly two normal cards without using its retired event route.");
    }

    public static void ClassicOptionalChoiceSkipsActivatesAndReplays()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var skipped = SelectGeneral(registry, "identity:classic-5", "classic:zhou-yu");
        ReachClassicYingzi(skipped);
        var handBeforeSkip = HandCount(skipped);
        var skipPrompt = RequireProgramPrompt(skipped, "classic:yingzi");
        Require(skipPrompt.IsPrivate &&
                skipPrompt.Choices.Select(choice => choice.Parameters["program-action"])
                    .OrderBy(action => action, StringComparer.Ordinal)
                    .SequenceEqual(["activate", "skip"]),
            "Classic Yingzi must publish one private generic activate/skip choice.");
        var serializedFrames = JsonSerializer.Serialize(skipped.ResolutionStack);
        var roundTrippedFrames = JsonSerializer.Deserialize<ResolutionFrame[]>(serializedFrames) ?? [];
        Require(roundTrippedFrames.Single() is ProgramLifecycleTriggerWindowFrame
                {
                    Window: SkillProgramTriggerWindow.DrawPhaseStarting,
                    Continuation: ProgramLifecycleContinuation.CompleteDrawPhase,
                    SkipPlayPhaseAfterDraw: false,
                    CandidateIndex: 0
                },
            "A paused draw-phase choice must serialize its parent window and continuation.");
        AnswerProgram(skipped, "skip");
        Require(skipped.State.Phase == TurnPhase.Play && skipped.PendingDecision is null &&
                HandCount(skipped) == handBeforeSkip + 2 &&
                skipped.CardMovements.All(move =>
                    move.Reason.Value != "skill-program.classic:yingzi.Draw") &&
                skipped.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(item =>
                    item.SkillId == "classic:yingzi" && !item.Activated && !item.Completed),
            $"Skipping classic Yingzi must continue through exactly one normal two-card draw " +
            $"(phase={skipped.State.Phase}, prompt={skipped.PendingDecision?.Kind.ToString() ?? "none"}, " +
            $"handDelta={HandCount(skipped) - handBeforeSkip}, skillMoves=" +
            $"{skipped.CardMovements.Count(move => move.Reason.Value == "skill-program.classic:yingzi.Draw")}).");

        var activated = SelectGeneral(registry, "identity:classic-5", "classic:zhou-yu");
        ReachClassicYingzi(activated);
        var handBeforeUse = HandCount(activated);
        var replay = GameReplay.Restore(RoundTrip(activated.CreateCheckpoint()), registry);
        AnswerProgram(activated, "activate");
        AnswerProgram(replay, "activate");
        Require(HandCount(activated) == handBeforeUse + 3 &&
                activated.CardMovements.Count(move =>
                    move.Reason.Value == "skill-program.classic:yingzi.Draw" &&
                    move.To == CardLocation.Hand(HumanSeat)) == 1 &&
                activated.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(item =>
                    item.SkillId == "classic:yingzi" && item.Activated && item.Completed) &&
                State(replay) == State(activated) && Events(replay).SequenceEqual(Events(activated)),
            "Activated classic Yingzi must draw one configured card, then two normal cards, and replay exactly.");
    }

    public static void MultipleAdditiveProgramsComposeBeforeOneNormalDraw()
    {
        var catalog = SkillProgramCatalog.Load(CompositionRules, CompositionPresentation);
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new CompositionPackage(catalog.Programs[CompositionPackage.AlphaSkillId],
                catalog.Programs[CompositionPackage.BetaSkillId]));
        var game = SelectGeneral(registry, CompositionPackage.ModeId, CompositionPackage.OwnerGeneralId);

        var advanced = game.Submit(new AdvanceCommand(game.Revision));
        var starts = game.Events.Select(item => item.Payload).OfType<ProgramBindingStartedEvent>()
            .Where(item => item.Window == SkillProgramTriggerWindow.DrawPhaseStarting)
            .Select(item => item.SkillId).ToArray();
        Require(advanced.Accepted && game.State.Phase == TurnPhase.Play &&
                game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat } &&
                HandCount(game) == 8 &&
                starts.SequenceEqual([CompositionPackage.AlphaSkillId, CompositionPackage.BetaSkillId]) &&
                game.CardMovements.Count(move =>
                    move.Reason.Value.StartsWith("skill-program.fixture:draw-", StringComparison.Ordinal) &&
                    move.To == CardLocation.Hand(HumanSeat)) == 2,
            $"Two additive draw programs must run in stable order before one, and only one, normal two-card draw " +
            $"(phase={game.State.Phase}, prompt={game.PendingDecision?.Kind.ToString() ?? "none"}, " +
            $"hand={HandCount(game)}, starts={string.Join(",", starts)})." );
    }

    public static void ReplacementSuppressesAdditiveWhileSkipFallsThrough()
    {
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new ReplacementCompositionPackage());
        var game = SelectGeneral(
            registry,
            ReplacementCompositionPackage.ModeId,
            ReplacementCompositionPackage.OwnerGeneralId);

        var advanced = game.Submit(new AdvanceCommand(game.Revision));
        Require(advanced.Accepted &&
                game.PendingDecision is
                    { Kind: DecisionKind.ProgramTrigger, SkillPrompt.SkillId: "classic:tuxi" },
            advanced.Error?.Message ?? "The replacement/additive fixture did not begin with Tuxi.");
        var handBefore = HandCount(game);
        var paused = RoundTrip(game.CreateCheckpoint());

        AnswerProgram(game, "activate");
        var targets = RequireProgramPrompt(game, "classic:tuxi").Choices
            .First(choice => choice.Targets.Count == 2);
        var used = game.Submit(new AnswerPromptCommand(
            HumanSeat, game.PendingDecision!.PromptId, targets.Id, game.Revision));
        var usedStarts = game.Events.Select(item => item.Payload).OfType<ProgramBindingStartedEvent>()
            .Where(item => item.Window == SkillProgramTriggerWindow.DrawPhaseStarting)
            .Select(item => item.SkillId).ToArray();
        Require(used.Accepted && game.State.Phase == TurnPhase.Play &&
                game.PendingDecision is null &&
                HandCount(game) == handBefore + 2 &&
                usedStarts.SequenceEqual(["classic:tuxi"]) &&
                game.CardMovements.All(move =>
                    move.Reason.Value != "skill-program.classic:yingzi.Draw"),
            used.Error?.Message ??
            $"A completed replacement must suppress Yingzi and the normal draw " +
            $"(phase={game.State.Phase}, handDelta={HandCount(game) - handBefore}, " +
            $"starts={string.Join(",", usedStarts)})." );

        var skipped = GameReplay.Restore(paused, registry);
        AnswerProgram(skipped, "skip");
        Require(skipped.PendingDecision is
                { Kind: DecisionKind.ProgramTrigger, SkillPrompt.SkillId: "classic:yingzi" },
            "Skipping Tuxi must continue to the later additive Yingzi candidate.");
        AnswerProgram(skipped, "activate");
        var skippedStarts = skipped.Events.Select(item => item.Payload).OfType<ProgramBindingStartedEvent>()
            .Where(item => item.Window == SkillProgramTriggerWindow.DrawPhaseStarting)
            .Select(item => item.SkillId).ToArray();
        Require(skipped.State.Phase == TurnPhase.Play &&
                skipped.PendingDecision is null &&
                HandCount(skipped) == handBefore + 3 &&
                skippedStarts.SequenceEqual(["classic:yingzi"]) &&
                skipped.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(item =>
                    item.SkillId == "classic:tuxi" && !item.Activated && !item.Completed) &&
                skipped.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(item =>
                    item.SkillId == "classic:yingzi" && item.Activated && item.Completed),
            $"A skipped replacement must fall through to Yingzi plus one normal draw " +
            $"(phase={skipped.State.Phase}, handDelta={HandCount(skipped) - handBefore}, " +
            $"starts={string.Join(",", skippedStarts)})." );
    }

    private static void ReachClassicYingzi(GameEngine game)
    {
        var advanced = game.Submit(new AdvanceCommand(game.Revision));
        Require(advanced.Accepted && game.PendingDecision is
                { Kind: DecisionKind.ProgramTrigger, PlayerSeat: HumanSeat, SkillPrompt.SkillId: "classic:yingzi" },
            advanced.Error?.Message ?? "Classic Yingzi did not publish its generic program choice.");
    }

    private static PendingDecision RequireProgramPrompt(GameEngine game, string skillId) =>
        game.PendingDecision is
            { Kind: DecisionKind.ProgramTrigger, PlayerSeat: HumanSeat, SkillPrompt.SkillId: var actual } prompt &&
        actual == skillId
            ? prompt
            : throw new InvalidOperationException($"Expected ProgramTrigger for {skillId}.");

    private static void AnswerProgram(GameEngine game, string action)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("No program prompt is pending.");
        var choice = prompt.Choices.Single(item =>
            item.Parameters.GetValueOrDefault("program-action") == action);
        var answered = game.Submit(new AnswerPromptCommand(
            HumanSeat, prompt.PromptId, choice.Id, game.Revision));
        Require(answered.Accepted, answered.Error?.Message ?? $"The program {action} choice was rejected.");
    }

    private static GameEngine SelectGeneral(ContentRegistry registry, string modeId, string generalId)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                HumanSeat = HumanSeat,
                HumanRole = Role.Lord,
                ModeId = modeId,
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2,
                MaxTurns = 20
            }, registry);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "The draw-phase fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice => choice.ContentIds.SequenceEqual([generalId])) != true)
                continue;
            var selected = game.Submit(new SelectGeneralCommand(
                HumanSeat, generalId, game.Revision, game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? $"Could not select {generalId}.");
            return game;
        }
        throw new InvalidOperationException($"No deterministic selection fixture exposed {generalId}.");
    }

    private static int HandCount(GameEngine game) => game.CreateSnapshot(HumanSeat, revealAll: true)
        .Players.Single(player => player.Seat == HumanSeat).HandCount;

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static string State(GameEngine game) =>
        SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true));

    private static IReadOnlyList<string> Events(GameEngine game) => game.Events
        .Select(item => $"{item.Sequence}|{item.Payload.GetType().Name}|" +
                        JsonSerializer.Serialize(item.Payload, item.Payload.GetType()))
        .ToArray();

    private static void Reject(string rules, string expectedMessage)
    {
        try
        {
            _ = SkillProgramCatalog.Load(rules, ValidationPresentation);
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        throw new InvalidOperationException($"Expected validation failure containing '{expectedMessage}'.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private const string ValidationRules =
        "{\"schemaVersion\":60,\"skills\":[{\"id\":\"fixture:draw-validation\",\"revision\":1," +
        "\"minimumRulesVersion\":170,\"modifiers\":[],\"viewAs\":[],\"activations\":[]," +
        "\"triggers\":[{\"id\":\"draw\",\"window\":\"drawPhaseStarting\",\"subject\":\"owner\"," +
        "\"optional\":false,\"priority\":0,\"effects\":[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}]}]," +
        "\"contributions\":[],\"cardIdentities\":[]}]}";

    private const string ValidationPresentation =
        "{\"schemaVersion\":3,\"skills\":{\"fixture:draw-validation\":{\"name\":\"Draw\",\"description\":\"Fixture\"}}}";

    private const string ReplacementValidationRules =
        "{\"schemaVersion\":60,\"skills\":[{\"id\":\"fixture:draw-replacement-validation\",\"revision\":1," +
        "\"minimumRulesVersion\":170,\"modifiers\":[],\"viewAs\":[],\"activations\":[]," +
        "\"triggers\":[{\"id\":\"replace\",\"window\":\"drawPhaseStarting\",\"subject\":\"owner\"," +
        "\"optional\":true,\"priority\":100,\"drawPhaseMode\":\"replacement\",\"effects\":[" +
        "{\"op\":\"selectTargets\",\"target\":\"owner\",\"targetKind\":\"otherLivingWithHand\"," +
        "\"minimumTargets\":1,\"maximumTargets\":2,\"targetAiOrder\":\"hostileThenHandCount\"}," +
        "{\"op\":\"takeRandomHandCardFromSelectedTargets\",\"target\":\"owner\",\"amount\":1}]}]," +
        "\"contributions\":[],\"cardIdentities\":[]}]}";

    private const string RevealReplacementValidationRules =
        "{\"schemaVersion\":60,\"skills\":[{\"id\":\"fixture:draw-reveal-validation\",\"revision\":1," +
        "\"minimumRulesVersion\":170,\"modifiers\":[],\"viewAs\":[],\"activations\":[]," +
        "\"triggers\":[{\"id\":\"replace\",\"window\":\"drawPhaseStarting\",\"subject\":\"owner\"," +
        "\"optional\":true,\"priority\":90,\"drawPhaseMode\":\"replacement\",\"effects\":[" +
        "{\"op\":\"revealTopCards\",\"target\":\"owner\",\"numberExpression\":\"ownerLostHp\"," +
        "\"resultBind\":\"revealed\",\"visibility\":\"public\"}," +
        "{\"op\":\"filterBoundCards\",\"target\":\"owner\",\"sourceBind\":\"revealed\",\"resultBind\":\"hearts\",\"suits\":[\"heart\"]}," +
        "{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"hearts\",\"destination\":\"discardPile\"}," +
        "{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"revealed\",\"exceptBind\":\"hearts\",\"destination\":\"ownerHand\"}," +
        "{\"op\":\"recover\",\"target\":\"owner\",\"numberExpression\":\"boundCardCount\",\"sourceBind\":\"hearts\"}]}]," +
        "\"contributions\":[],\"cardIdentities\":[]}]}";

    private const string AdjustmentValidationRules =
        "{\"schemaVersion\":60,\"skills\":[{\"id\":\"fixture:draw-adjustment-validation\",\"revision\":1," +
        "\"minimumRulesVersion\":170,\"modifiers\":[],\"viewAs\":[],\"activations\":[]," +
        "\"triggers\":[{\"id\":\"adjust\",\"window\":\"drawPhaseStarting\",\"subject\":\"owner\"," +
        "\"optional\":true,\"priority\":80,\"effects\":[" +
        "{\"op\":\"adjustNormalDraw\",\"target\":\"owner\",\"amount\":-1}," +
        "{\"op\":\"grantTurnCardDamageModifier\",\"target\":\"owner\",\"amount\":1," +
        "\"cardKinds\":[\"slash\",\"fireSlash\",\"thunderSlash\",\"duel\"]}]}]," +
        "\"contributions\":[],\"cardIdentities\":[]}]}";

    private const string CompositionRules = """
        {"schemaVersion":60,"skills":[
        {"id":"fixture:draw-alpha","revision":1,"minimumRulesVersion":170,
        "modifiers":[],"viewAs":[],"activations":[],"triggers":[
        {"id":"extra","window":"drawPhaseStarting","subject":"owner","optional":false,"priority":0,
        "effects":[{"op":"draw","target":"owner","amount":1}]}],"contributions":[],"cardIdentities":[]},
        {"id":"fixture:draw-beta","revision":1,"minimumRulesVersion":170,
        "modifiers":[],"viewAs":[],"activations":[],"triggers":[
        {"id":"extra","window":"drawPhaseStarting","subject":"owner","optional":false,"priority":0,
        "effects":[{"op":"draw","target":"owner","amount":1}]}],"contributions":[],"cardIdentities":[]}
        ]}
        """;

    private const string CompositionPresentation = """
        {"schemaVersion":3,"skills":{
        "fixture:draw-alpha":{"name":"额外摸牌甲","description":"测试"},
        "fixture:draw-beta":{"name":"额外摸牌乙","description":"测试"}
        }}
        """;

    private sealed class CompositionPackage(SkillProgram alpha, SkillProgram beta) : IGameContentPackage
    {
        public const string AlphaSkillId = "fixture:draw-alpha";
        public const string BetaSkillId = "fixture:draw-beta";
        public const string OwnerGeneralId = "fixture:draw-owner";
        public const string ModeId = "identity:draw-program-test-5";

        public PackageManifest Manifest { get; } = new(
            "draw-program-scenario",
            new Version(1, 0, 0),
            [new PackageDependency("standard", StandardContentPackage.CurrentVersion)]);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddSkill(new ContentSkillDefinition(AlphaSkillId, "额外摸牌甲", "测试")
            {
                Program = alpha,
                ExecutionForms = SkillExecutionForm.Trigger
            });
            builder.AddSkill(new ContentSkillDefinition(BetaSkillId, "额外摸牌乙", "测试")
            {
                Program = beta,
                ExecutionForms = SkillExecutionForm.Trigger
            });
            builder.AddGeneral(new ContentGeneralDefinition(
                OwnerGeneralId, "摸牌计划测试武将", "supporter", AlphaSkillId, "wei", BaseHp: 4,
                AdditionalSkillIds: [BetaSkillId]));
            var targets = Enumerable.Range(1, 4).Select(index => $"fixture:draw-target-{index}").ToArray();
            foreach (var target in targets)
                builder.AddGeneral(new ContentGeneralDefinition(
                    target, "摸牌计划测试目标", "supporter", "standard:none", "wei", BaseHp: 4));
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "五人身份（摸牌计划场景）",
                5,
                5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                },
                "standard:basic-demo",
                GeneralCandidateCount: 5,
                GeneralPoolIds: [OwnerGeneralId, .. targets]));
        }
    }

    private sealed class ReplacementCompositionPackage : IGameContentPackage
    {
        public const string OwnerGeneralId = "fixture:draw-replacement-owner";
        public const string ModeId = "identity:draw-replacement-test-5";

        public PackageManifest Manifest { get; } = new(
            "draw-replacement-scenario",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 103, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                OwnerGeneralId,
                "摸牌替换测试武将",
                "supporter",
                "classic:tuxi",
                "wei",
                BaseHp: 4,
                AdditionalSkillIds: ["classic:yingzi"]));
            var targets = Enumerable.Range(1, 4)
                .Select(index => $"fixture:draw-replacement-target-{index}")
                .ToArray();
            foreach (var target in targets)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    target,
                    "摸牌替换测试目标",
                    "supporter",
                    "standard:none",
                    "wei",
                    BaseHp: 4));
            }

            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "五人身份（摸牌替换场景）",
                5,
                5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                },
                "classic:standard-deck",
                GeneralCandidateCount: 5,
                GeneralPoolIds: [OwnerGeneralId, .. targets]));
        }
    }
}
