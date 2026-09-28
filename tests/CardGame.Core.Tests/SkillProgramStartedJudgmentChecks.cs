using CardGame.Content.Standard;
using CardGame.Core;

internal static class SkillProgramStartedJudgmentChecks
{
    internal static void Definitions()
    {
        var catalog = SkillProgramCatalog.Load(Rules, Presentation);
        var program = catalog.Programs[ProgramId];
        var dodge = program.Triggers.Single(trigger => trigger.Id == DodgeTriggerId);
        var lightning = program.Triggers.Single(trigger => trigger.Id == LightningTriggerId);
        Require(program.MinimumRulesVersion == 171 &&
                dodge is
                {
                    Window: SkillProgramTriggerWindow.CardResponseAccepted,
                    Optional: true,
                    SourceSkillId: null,
                    SourceViewAsId: null
                } &&
                dodge.CardKinds.SequenceEqual([CardKind.Dodge]) &&
                dodge.Effects[0] is
                {
                    Op: SkillProgramEffectOp.StartJudgment,
                    Target: SkillProgramEffectTarget.Owner,
                    JudgmentReason: JudgmentReason
                } &&
                lightning.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized &&
                lightning.CardKinds.SequenceEqual([CardKind.Lightning]),
            "Card-action triggers must keep direct effective-card filters separate from conversion sources.");

        AssertReject(Rules.Replace("\"schemaVersion\":62", "\"schemaVersion\":57", StringComparison.Ordinal),
            "expected 62");
        AssertReject(Rules.Replace("\"cardKinds\":[\"dodge\"]",
                "\"sourceSkillId\":\"started-judgment-test:source\",\"cardKinds\":[\"dodge\"]",
                StringComparison.Ordinal),
            "cannot be combined");
        AssertReject(Rules.Replace("\"cardKinds\":[\"dodge\"]", "\"cardKinds\":[\"lightning\"]",
                StringComparison.Ordinal),
            "unsupported by cardResponseAccepted");
        AssertReject(Rules.Replace("\"target\":\"owner\",\"judgmentReason\":\"skill.started-judgment-test\"",
                "\"target\":\"opponent\",\"judgmentReason\":\"skill.started-judgment-test\"",
                StringComparison.Ordinal),
            "unsupported SkillProgramEffectTarget");
        AssertReject(Rules.Replace("\"judgmentReason\":\"skill.started-judgment-test\"",
                "\"judgmentReason\":\"\"", StringComparison.Ordinal),
            "must not be empty");
    }

    internal static void DirectDodgeAndLightningReplay()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new FixturePackage());
        VerifyDodgeResponse(registry);
        VerifyLightningUse(registry);
    }

    private static void VerifyDodgeResponse(ContentRegistry registry)
    {
        var game = FindDodgeBoundary(registry);
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("The direct Dodge judgment prompt was lost.");
        var accepted = game.Events.Select(item => item.Payload)
            .OfType<CardActionAcceptedEvent>()
            .Last(item => item.Action is { Type: CardActionType.Response, EffectiveKind: CardKind.Dodge });
        var dodgeCardId = accepted.Action.PhysicalCards.Single().CardId;
        Require(prompt is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0, IsPrivate: true } &&
                game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single() is { } frame &&
                frame.Candidates[frame.CandidateIndex].TriggerId == DodgeTriggerId &&
                game.CreateCardZoneDiagnostics().Single(card => card.CardId == dodgeCardId).Location ==
                    CardLocation.Processing,
            "A native Dodge response must pause after acceptance while retaining its exact paid card.");

        var paused = RoundTrip(game.CreateCheckpoint());
        var restored = GameReplay.Restore(paused, registry);
        Require(restored.PendingDecision?.PromptId == prompt.PromptId &&
                SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            "A paused direct Dodge trigger must restore its private activation boundary exactly.");

        Activate(restored);
        FinishConfiguredJudgment(restored);
        var requested = restored.Events.Select(item => item.Payload)
            .OfType<JudgmentRequestedEvent>()
            .Last(item => item.Reason == JudgmentReason);
        Require(requested.TargetSeat == 0,
            "The configured Dodge judgment must target its skill owner.");
        Require(restored.Events.Select(item => item.Payload).OfType<JudgmentResolvedEvent>()
                .Any(item => item.ResolutionId == requested.ResolutionId && item.Reason == JudgmentReason),
            "The configured Dodge judgment must publish its matching resolved result.");
        Require(restored.Events.Select(item => item.Payload).OfType<ProgramJudgmentTriggerResolvedEvent>()
                .Any(item => item.TriggerId == ResultTriggerId && item.Activated),
            "The configured Dodge judgment must reach its mandatory final-result binding.");
        var finalBinding = restored.Events.Select(item => item.Payload).OfType<ProgramBindingStartedEvent>()
            .Single(item => item.SkillId == ProgramId && item.BindingId == ResultTriggerId);
        var players = (IReadOnlyList<CharacterState>)typeof(GameEngine)
            .GetField("_players", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!.GetValue(restored)!;
        Require(players[finalBinding.OwnerSeat].SkillGrants.Grants.Any(grant =>
                grant.IsEnabled && grant.SkillId == finalBinding.SkillId &&
                grant.SkillInstanceId == finalBinding.SkillInstanceId),
            "The final judgment binding must execute through an active grant instance.");
        Require(restored.Events.Select(item => item.Payload).OfType<ProgramCardTriggerResolvedEvent>()
                .Count(item => item.TriggerId == DodgeTriggerId && item.Activated) == 1,
            "The direct Dodge trigger must complete exactly once.");
        Require(restored.CreateCardZoneDiagnostics().Single(card => card.CardId == dodgeCardId).Location ==
                CardLocation.DiscardPile,
            "The original Dodge must leave Processing after its nested self judgment finishes.");
        Require(restored.ResolutionStack.All(item => item is not
                (ProgramCardTriggerWindowFrame or JudgmentFrame or ProgramJudgmentTriggerWindowFrame)),
            "The direct Dodge judgment must clear every configured judgment frame before resuming its parent.");
        AssertCompletedReplay(restored, registry, DodgeTriggerId);
    }

    private static void VerifyLightningUse(ContentRegistry registry)
    {
        var game = StartOwner(registry, FixturePackage.LightningModeId, seed: 1);
        var action = game.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.Lightning);
        var played = game.Submit(new PlayCardCommand(
            0,
            action.CardId!.Value,
            action.TargetSeats,
            game.Revision,
            game.PendingDecision!.PromptId,
            action.PlayedCardKind));
        Require(played.Accepted &&
                game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } &&
                game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single() is { } frame &&
                frame.Candidates[frame.CandidateIndex].TriggerId == LightningTriggerId &&
                game.CreateCardZoneDiagnostics().Single(card => card.CardId == action.CardId.Value).Location ==
                    CardLocation.Processing,
            played.Error?.Message ?? "A native Lightning use did not reach its direct trigger boundary.");

        var restored = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Activate(restored);
        FinishConfiguredJudgment(restored);
        var requested = restored.Events.Select(item => item.Payload)
            .OfType<JudgmentRequestedEvent>()
            .Last(item => item.Reason == JudgmentReason);
        Require(requested.TargetSeat == 0 &&
                restored.Events.Select(item => item.Payload).OfType<ProgramCardTriggerResolvedEvent>()
                    .Count(item => item.TriggerId == LightningTriggerId && item.Activated) == 1 &&
                restored.Events.Select(item => item.Payload).OfType<DelayedCardPlacedEvent>()
                    .Any(item => item.CardId == action.CardId.Value && item.TargetSeat == 0) &&
                restored.CreateCardZoneDiagnostics().Single(card => card.CardId == action.CardId.Value).Location ==
                    CardLocation.Judgment(0) &&
                restored.ResolutionStack.All(item => item is not
                    (ProgramCardTriggerWindowFrame or JudgmentFrame or ProgramJudgmentTriggerWindowFrame)),
            "The self judgment must return to the original Lightning placement without losing its physical card.");
        AssertCompletedReplay(restored, registry, LightningTriggerId);
    }

    private static GameEngine FindDodgeBoundary(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 256; seed++)
        {
            var game = StartOwner(registry, FixturePackage.ResponseModeId, seed);
            for (var step = 0; step < 1_200 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } &&
                    game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single() is { } frame &&
                    frame.Candidates[frame.CandidateIndex].TriggerId == DodgeTriggerId)
                    return game;

                GameCommand command;
                if (game.PendingDecision is { PlayerSeat: 0 } pending)
                {
                    command = pending.Kind switch
                    {
                        DecisionKind.PlayCard =>
                            new EndPlayPhaseCommand(0, game.Revision, pending.PromptId),
                        DecisionKind.RespondDodge when pending.Choices.Any(choice =>
                            choice.Parameters.GetValueOrDefault("response") == "dodge") =>
                            new AnswerPromptCommand(0, pending.PromptId,
                                pending.Choices.First(choice =>
                                    choice.Parameters.GetValueOrDefault("response") == "dodge").Id,
                                game.Revision),
                        DecisionKind.RescueDying =>
                            new AnswerPromptCommand(0, pending.PromptId,
                                pending.Choices.First(choice =>
                                    choice.Parameters.GetValueOrDefault("response") == "let-die").Id,
                                game.Revision),
                        _ => new AnswerPromptCommand(
                            0, pending.PromptId, pending.Choices.Last().Id, game.Revision)
                    };
                }
                else command = new AdvanceOneStepCommand(game.Revision);

                if (!game.Submit(command).Accepted) break;
            }
        }
        throw new InvalidOperationException("No bounded native Dodge response trigger was found.");
    }

    private static GameEngine StartOwner(ContentRegistry registry, string modeId, int seed)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 5,
            ModeId = modeId,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 80
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted &&
                game.PendingDecision is { Kind: DecisionKind.SelectGeneral } setup &&
                game.Submit(new SelectGeneralCommand(
                    0, FixturePackage.OwnerGeneralId, game.Revision, setup.PromptId)).Accepted,
            "The started-judgment fixture could not select its owner general.");
        for (var step = 0; step < 64; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return game;
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "The fixture could not reach human play.");
        }
        throw new InvalidOperationException("The started-judgment fixture did not reach human play.");
    }

    private static void Activate(GameEngine game)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("Program prompt was lost.");
        var activation = prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate");
        var result = game.Submit(new AnswerPromptCommand(
            0, prompt.PromptId, activation.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "The started judgment was rejected.");
    }

    private static void FinishConfiguredJudgment(GameEngine game)
    {
        for (var step = 0; step < 64; step++)
        {
            if (game.ResolutionStack.All(item => item is not
                    (ProgramCardTriggerWindowFrame or JudgmentFrame or ProgramJudgmentTriggerWindowFrame)))
                return;
            Require(game.PendingDecision is null,
                "The mandatory configured judgment unexpectedly exposed another human prompt.");
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted,
                advanced.Error?.Message ?? "The configured judgment could not resume its parent action.");
        }
        throw new InvalidOperationException("The configured judgment did not finish within the bounded steps.");
    }

    private static void AssertCompletedReplay(GameEngine game, ContentRegistry registry, string triggerId)
    {
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                replay.Events.Select(item => item.Payload).OfType<ProgramCardTriggerResolvedEvent>()
                    .Count(item => item.TriggerId == triggerId && item.Activated) == 1 &&
                replay.Events.Select(item => item.Payload).OfType<JudgmentRequestedEvent>()
                    .Count(item => item.Reason == JudgmentReason) == 1,
            "A completed direct card judgment must replay without repeating either cursor.");
    }

    private sealed class FixturePackage : IGameContentPackage
    {
        internal const string ResponseModeId = "identity:started-judgment-response-5";
        internal const string LightningModeId = "identity:started-judgment-lightning-5";
        internal const string OwnerGeneralId = "started-judgment-test:owner";
        private static readonly string[] OtherGeneralIds =
            Enumerable.Range(1, 4).Select(index => $"started-judgment-test:other-{index}").ToArray();

        public PackageManifest Manifest { get; } = new(
            "program-started-judgment-fixture", new Version(1, 0, 0),
            [new PackageDependency("standard", new Version(1, 11, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(Rules, Presentation);
            var program = catalog.Programs[ProgramId];
            var text = catalog.Presentations[ProgramId];
            builder.AddSkill(new ContentSkillDefinition(ProgramId, text.Name, text.Description)
            { Program = program });
            builder.AddGeneral(new ContentGeneralDefinition(
                OwnerGeneralId, "发起判定测试", "zhang_jiao", ProgramId, "qun", BaseHp: 4));
            foreach (var id in OtherGeneralIds)
                builder.AddGeneral(new ContentGeneralDefinition(
                    id, "发起判定陪测", "cao_cao", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe(
                "started-judgment-test:response-deck", "响应判定夹具", 4, 2,
                [new ContentDeckCardCount("standard:slash", 50),
                 new ContentDeckCardCount("standard:dodge", 50)]));
            builder.AddDeck(new ContentDeckRecipe(
                "started-judgment-test:lightning-deck", "闪电判定夹具", 4, 2,
                [new ContentDeckCardCount("standard:lightning", 80)]));
            var roles = new Dictionary<string, int>
            {
                [nameof(Role.Lord)] = 1,
                [nameof(Role.Loyalist)] = 1,
                [nameof(Role.Rebel)] = 2,
                [nameof(Role.Renegade)] = 1
            };
            builder.AddMode(new ContentModeDefinition(
                ResponseModeId, "打出闪后判定", 5, 5, roles,
                DeckId: "started-judgment-test:response-deck",
                GeneralCandidateCount: 5,
                GeneralPoolIds: [OwnerGeneralId, .. OtherGeneralIds]));
            builder.AddMode(new ContentModeDefinition(
                LightningModeId, "使用闪电后判定", 5, 5, roles,
                DeckId: "started-judgment-test:lightning-deck",
                GeneralCandidateCount: 5,
                GeneralPoolIds: [OwnerGeneralId, .. OtherGeneralIds]));
        }
    }

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

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

    private const string ProgramId = "started-judgment-test:skill";
    private const string DodgeTriggerId = "after-dodge";
    private const string LightningTriggerId = "after-lightning";
    private const string ResultTriggerId = "after-result";
    private const string JudgmentReason = "skill.started-judgment-test";

    private const string Rules = """
        {"schemaVersion":62,"skills":[
          {"id":"started-judgment-test:skill","revision":1,"minimumRulesVersion": 171,"triggers":[
            {"id":"after-dodge","window":"cardResponseAccepted","ownerRelation":"actor","cardKinds":["dodge"],
             "optional":true,"effects":[
              {"op":"startJudgment","target":"owner","judgmentReason":"skill.started-judgment-test","resultBind":"judgment-card","visibility":"public"},{"op":"moveBoundCards","target":"owner","sourceBind":"judgment-card","destination":"discardPile"}
             ]},
            {"id":"after-lightning","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["lightning"],
             "optional":true,"effects":[
              {"op":"startJudgment","target":"owner","judgmentReason":"skill.started-judgment-test","resultBind":"judgment-card","visibility":"public"},{"op":"moveBoundCards","target":"owner","sourceBind":"judgment-card","destination":"discardPile"}
             ]},
            {"id":"after-result","window":"judgmentFinalized","subject":"owner",
             "suits":["spade","heart","club","diamond"],"minimumRank":1,"maximumRank":13,
             "excludedReasons":[],"optional":false,"effects":[
              {"op":"draw","target":"owner","amount":1}
             ]}
          ]}
        ]}
        """;

    private const string Presentation = """
        {"schemaVersion":3,"skills":{
          "started-judgment-test:skill":{
            "name":"发起判定测试","description":"打出闪或使用闪电后可发起一次自己的判定。"
          }
        }}
        """;
}
