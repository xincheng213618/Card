using CardGame.Content.Standard;
using CardGame.Core;
using System.Reflection;

internal static class SkillProgramJudgmentDamageChecks
{

    internal static void TargetDamageDyingAndReplay()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new FixturePackage());
        var (boundary, damageTargetSeat) = FindBoundary(registry);
        var activation = boundary.PendingDecision ??
            throw new InvalidOperationException("Judgment damage activation prompt was lost.");
        var frame = boundary.ResolutionStack.OfType<ProgramJudgmentTriggerWindowFrame>().Single();
        var beforeTarget = boundary.CreateSnapshot(0, revealAll: true).Players[damageTargetSeat];
        Require(activation is
        {
            Kind: DecisionKind.ProgramJudgmentTrigger,
            PlayerSeat: 0,
            IsPrivate: true
        } &&
                frame.Candidates[frame.CandidateIndex].TriggerId == StrikeTriggerId &&
                boundary.Events.Select(item => item.Payload)
                    .OfType<ProgramJudgmentTriggerResolvedEvent>()
                    .Any(item => item.TriggerId == RecoveryTriggerId && item.Activated) &&
                boundary.Events.Select(item => item.Payload)
                    .OfType<RecoveryAppliedEvent>()
                    .Any(item => item.SourceSeat == 0 && item.TargetSeat == 0 && item.Amount == 1) &&
                beforeTarget.IsAlive && beforeTarget.Role != Role.Lord,
            "The mandatory Club recovery must finish before the separately optional damage binding.");

        Answer(boundary, activation.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "program-judgment-trigger-activate"));
        var targetPrompt = boundary.PendingDecision ??
            throw new InvalidOperationException("Judgment effect target prompt was lost.");
        Require(targetPrompt is
        {
            Kind: DecisionKind.ProgramTrigger,
            PlayerSeat: 0,
            IsPrivate: true
        } &&
                boundary.ResolutionStack.OfType<ProgramSkillFrame>().Single(item =>
                    item.WindowContext?.Window == SkillProgramTriggerWindow.JudgmentFinalized) is
                { InstructionIndex: 1,
                  WindowContext: { Window: SkillProgramTriggerWindow.JudgmentFinalized } } &&
                targetPrompt.ValidTargetSeats.Contains(damageTargetSeat) &&
                targetPrompt.Choices.Any(choice => choice.Targets.SequenceEqual([damageTargetSeat])) &&
                boundary.CreateSnapshot(1, revealAll: false).PendingDecision is null,
            "Target selection must publish exact living seats only to the skill owner.");
        var paused = boundary.CreateCheckpoint();
        var restoredTarget = GameReplay.Restore(paused, registry);
        Require(restoredTarget.PendingDecision is { Kind: DecisionKind.ProgramTrigger } restoredPrompt &&
                restoredPrompt.PromptId == targetPrompt.PromptId &&
                restoredPrompt.ValidTargetSeats.SequenceEqual(targetPrompt.ValidTargetSeats),
            "A paused selected-target judgment effect must restore its exact private prompt.");

        var invalidParent = GameReplay.Restore(paused, registry);
        var stack = (FrameStore)typeof(GameEngine)
            .GetField("_resolutionStack", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(invalidParent)!;
        var parentIndex = stack.FindIndex(item => item is ProgramSkillFrame
            { TriggerId: "opening-judgment" });
        Require(parentIndex >= 0 && stack[parentIndex] is ProgramSkillFrame
            { InstructionIndex: >= 2 },
            "The opening judgment must retain its committed parent instruction.");
        var parent = (ProgramSkillFrame)stack[parentIndex];
        stack.Replace(parent with { InstructionIndex = 1 });
        var rejectedParent = false;
        try
        {
            typeof(GameEngine).GetMethod("AssertCoreInvariants",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(invalidParent, null);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is
            InvalidOperationException { Message: var message } &&
            message.Contains("judgment continuation", StringComparison.OrdinalIgnoreCase))
        {
            rejectedParent = true;
        }
        Require(rejectedParent,
            "A judgment continuation with a different paid parent instruction must be rejected.");

        var invalidBefore = SnapshotJson.Serialize(boundary.CreateSnapshot(0, revealAll: true));
        var invalid = boundary.Submit(new AnswerPromptCommand(
            0,
            targetPrompt.PromptId,
            new ChoiceId("program-judgment-target.seat-99"),
            boundary.Revision));
        Require(!invalid.Accepted && invalid.Error?.Code == CommandErrorCode.InvalidChoice &&
                SnapshotJson.Serialize(boundary.CreateSnapshot(0, revealAll: true)) == invalidBefore,
            "A forged judgment damage target must be rejected without mutation.");

        Answer(boundary, targetPrompt.Choices.Single(choice =>
            choice.Targets.SequenceEqual([damageTargetSeat])));
        var requested = boundary.Events.Select(item => item.Payload)
            .OfType<ProgramJudgmentDamageRequestedEvent>()
            .Single(item => item.TriggerId == StrikeTriggerId);
        var applied = boundary.Events.Select(item => item.Payload)
            .OfType<DamageAppliedEvent>()
            .Last(item => item.SourceSeat == 0 && item.TargetSeat == damageTargetSeat);
        Require(requested is
        {
            SkillId: ProgramId,
            SourceSeat: 0,
            Amount: 4,
            Nature: DamageNature.Thunder
        } &&
                requested.TargetSeat == damageTargetSeat &&
                applied is { Amount: 4, Nature: DamageNature.Thunder, RemainingHp: 0 } &&
                boundary.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault() is
                { SelectedTargetSeats: [var selectedSeat] } && selectedSeat == damageTargetSeat &&
                boundary.ResolutionStack.Any(item => item is DyingFrame),
            "Selected-target thunder damage must enter the shared typed damage and dying stack.");

        var dyingCheckpoint = boundary.CreateCheckpoint();
        var restoredDying = GameReplay.Restore(dyingCheckpoint, registry);
        Require(SnapshotJson.Serialize(restoredDying.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(boundary.CreateSnapshot(0, revealAll: true)) &&
                restoredDying.ResolutionStack.Any(item => item is DyingFrame),
            "The nested dying continuation must restore without repeating judgment damage.");

        FinishNestedDamage(boundary, damageTargetSeat);
        FinishNestedDamage(restoredDying, damageTargetSeat);
        var finalSnapshot = SnapshotJson.Serialize(boundary.CreateSnapshot(0, revealAll: true));
        Require(finalSnapshot == SnapshotJson.Serialize(restoredDying.CreateSnapshot(0, revealAll: true)) &&
                !boundary.CreateSnapshot(0, revealAll: true).Players[damageTargetSeat].IsAlive &&
                boundary.Events.Select(item => item.Payload).OfType<DyingResolvedEvent>()
                    .Any(item => item.VictimSeat == damageTargetSeat && !item.Survived) &&
                boundary.Events.Select(item => item.Payload).OfType<ProgramJudgmentTriggerResolvedEvent>()
                    .Count(item => item.TriggerId == StrikeTriggerId && item.Activated) == 1 &&
                boundary.ResolutionStack.All(item => item is not
                    (ProgramJudgmentTriggerWindowFrame or JudgmentFrame or DamageFrame or DyingFrame)),
            "Death must return through the program cursor, finish judgment cleanup and resume its parent exactly once.");

        var completedReplay = GameReplay.Restore(boundary.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(completedReplay.CreateSnapshot(0, revealAll: true)) == finalSnapshot &&
                completedReplay.Events.Select(item => item.Payload)
                    .OfType<ProgramJudgmentDamageRequestedEvent>().Count() == 1,
            "A completed judgment damage effect must replay without repeating its child damage.");
    }

    private static (GameEngine Game, int DamageTargetSeat) FindBoundary(ContentRegistry registry)
    {
        var setupReached = 0;
        var selectedOwner = 0;
        var judgmentsStarted = 0;
        string? lastRejection = null;
        string? candidateSample = null;
        for (var seed = 1; seed <= 512; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                ModeId = FixturePackage.ModeId,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2,
                MaxTurns = 30
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted ||
                game.PendingDecision is not { Kind: DecisionKind.SelectGeneral } setup)
                continue;
            setupReached++;
            candidateSample ??= string.Join(',', setup.Choices.SelectMany(choice => choice.ContentIds));
            var selected = game.Submit(new SelectGeneralCommand(
                0, FixturePackage.OwnerGeneralId, game.Revision, setup.PromptId));
            if (!selected.Accepted)
            {
                lastRejection = selected.Error?.Message;
                continue;
            }
            selectedOwner++;

            for (var step = 0; step < 1_200 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is
                    {
                        Kind: DecisionKind.ProgramJudgmentTrigger,
                        PlayerSeat: 0
                    } &&
                    game.ResolutionStack.OfType<ProgramJudgmentTriggerWindowFrame>().Single() is { } frame &&
                    frame.Candidates[frame.CandidateIndex].TriggerId == StrikeTriggerId)
                {
                    var target = game.CreateSnapshot(0, revealAll: true).Players
                        .FirstOrDefault(player => player.Seat != 0 && player is
                            { IsAlive: true, Role: not Role.Lord });
                    if (target is not null)
                        return (game, target.Seat);
                    Answer(game, game.PendingDecision.Choices.Single(choice =>
                        choice.Parameters.GetValueOrDefault("action") == "program-judgment-trigger-skip"));
                    continue;
                }

                GameCommand command = game.PendingDecision is { PlayerSeat: 0 } pending
                    ? pending.Kind switch
                    {
                        DecisionKind.PlayCard =>
                            new EndPlayPhaseCommand(0, game.Revision, pending.PromptId),
                        DecisionKind.ProgramTrigger =>
                            AnswerCommand(game, pending, "program-action", "activate"),
                        DecisionKind.RespondDodge =>
                            AnswerCommand(game, pending, "response", "take-damage"),
                        DecisionKind.RescueDying =>
                            AnswerCommand(game, pending, "response", "let-die"),
                        _ => new AnswerPromptCommand(
                            0, pending.PromptId, pending.Choices.Last().Id, game.Revision)
                    }
                    : new AdvanceOneStepCommand(game.Revision);
                var result = game.Submit(command);
                if (!result.Accepted) { lastRejection = result.Error?.Message; break; }
            }
            judgmentsStarted += game.Events.Select(item => item.Payload)
                .OfType<JudgmentRequestedEvent>().Count();
        }
        throw new InvalidOperationException(
            $"No bounded selected-target judgment damage boundary was found: setup={setupReached}, selected={selectedOwner}, judgments={judgmentsStarted}, candidates={candidateSample}, rejection={lastRejection}.");
    }

    private static AnswerPromptCommand AnswerCommand(
        GameEngine game,
        PendingDecision pending,
        string key,
        string value) =>
        new(0, pending.PromptId,
            pending.Choices.Single(choice => choice.Parameters.GetValueOrDefault(key) == value).Id,
            game.Revision);

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("Program prompt was lost.");
        var accepted = game.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision));
        Require(accepted.Accepted, accepted.Error?.Message ?? "Program judgment answer was rejected.");
    }

    private static void FinishNestedDamage(GameEngine game, int damageTargetSeat)
    {
        for (var step = 0; step < 256; step++)
        {
            var snapshot = game.CreateSnapshot(0, revealAll: true);
            if (!snapshot.Players[damageTargetSeat].IsAlive &&
                game.ResolutionStack.All(item => item is not
                    (ProgramJudgmentTriggerWindowFrame or JudgmentFrame or DamageFrame or DyingFrame)))
                return;
            GameCommand command = game.PendingDecision is { PlayerSeat: 0 } pending
                ? pending.Kind switch
                {
                    DecisionKind.RescueDying =>
                        AnswerCommand(game, pending, "response", "let-die"),
                    DecisionKind.PlayCard =>
                        new EndPlayPhaseCommand(0, game.Revision, pending.PromptId),
                    _ => new AnswerPromptCommand(
                        0, pending.PromptId, pending.Choices.Last().Id, game.Revision)
                }
                : new AdvanceOneStepCommand(game.Revision);
            var result = game.Submit(command);
            Require(result.Accepted, result.Error?.Message ?? "Nested judgment damage could not resume.");
        }
        throw new InvalidOperationException("Nested judgment damage did not complete within the bounded steps.");
    }

    private sealed class FixturePackage : IGameContentPackage
    {
        internal const string ModeId = "identity:classic-program-judgment-damage-5";
        internal const string OwnerGeneralId = "judgment-damage-test:owner";
        private static readonly string[] OtherGeneralIds =
            Enumerable.Range(1, 4).Select(index => $"judgment-damage-test:other-{index}").ToArray();

        public PackageManifest Manifest { get; } = new(
            "program-judgment-damage-fixture", new Version(1, 0, 0),
            [new PackageDependency("standard", new Version(1, 11, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(ValidV5, Presentation);
            var program = catalog.Programs[ProgramId];
            var text = catalog.Presentations[ProgramId];
            builder.AddSkill(new ContentSkillDefinition(ProgramId, text.Name, text.Description)
            { Program = program });
            builder.AddGeneral(new ContentGeneralDefinition(
                OwnerGeneralId, "判定伤害测试", "zhang_jiao", ProgramId, "qun", BaseHp: 4,
                AdditionalSkillIds: ["standard:ganglie"]));
            foreach (var id in OtherGeneralIds)
                builder.AddGeneral(new ContentGeneralDefinition(
                    id, "判定伤害陪测", "cao_cao", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe(
                "judgment-damage-test:club-deck", "梅花伤害夹具", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 80)
                    .Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 5))
                    .ToArray()
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId, "可配置判定伤害场景", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId: "judgment-damage-test:club-deck",
                GeneralCandidateCount: 5,
                GeneralPoolIds: [OwnerGeneralId, .. OtherGeneralIds]));
        }
    }

    private static void AssertReject(string rules, string expected)
    {
        try { _ = SkillProgramCatalog.Load(rules, Presentation); }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains(expected, StringComparison.OrdinalIgnoreCase))
        { return; }
        throw new InvalidOperationException($"Expected rejection containing '{expected}'.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private const string ProgramId = "judgment-damage-test:effects";
    private const string RecoveryTriggerId = "a-club-recover";
    private const string StrikeTriggerId = "b-club-strike";

    private const string ValidV5 = """
        {"schemaVersion":62,"skills":[
          {"id":"judgment-damage-test:effects","revision":1,"triggers":[
            {"id":"opening-judgment","window":"turnStartBeforeNormalFlow","subject":"owner",
             "optional":false,"effects":[
              {"op":"loseHp","target":"owner","amount":1},
              {"op":"startJudgment","target":"owner","judgmentReason":"judgment-damage-test.opening","resultBind":"opening","visibility":"public"},
              {"op":"moveBoundCards","target":"owner","sourceBind":"opening","destination":"discardPile"}
             ]},
            {"id":"a-club-recover","window":"judgmentFinalized","subject":"owner",
             "suits":["club"],"minimumRank":1,"maximumRank":13,"excludedReasons":[],
             "optional":false,"effects":[
              {"op":"recover","target":"owner","amount":1}
             ]},
            {"id":"b-club-strike","window":"judgmentFinalized","subject":"owner",
             "suits":["club"],"minimumRank":1,"maximumRank":13,"excludedReasons":[],
             "optional":true,"effects":[
              {"op":"selectTarget","target":"owner","targetKind":"anyLiving"},
              {"op":"damage","target":"selectedTarget","amount":4,"nature":"thunder"}
             ]}
          ]}
        ]}
        """;

    private const string Presentation = """
        {"schemaVersion":3,"skills":{
          "judgment-damage-test:effects":{
            "name":"判定效果测试","description":"梅花判定后先强制回复，再可选目标造成雷电伤害。"
          }
        }}
        """;
}
