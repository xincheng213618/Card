using CardGame.Content.Standard;
using CardGame.Core;

internal static class SkillProgramSelectedJudgmentChecks
{
    private const string ProgramId = "selected-judgment-test:leiji";
    private const string ObserverProgramId = "selected-judgment-test:observer";
    private const string StartTriggerId = "after-dodge";
    private const string SpadeTriggerId = "spade-damage";
    private const string ClubTriggerId = "club-recover-damage";
    private const string JudgmentReason = "skill.selected-judgment-test.leiji";

    internal static void Definitions()
    {
        var program = SkillProgramCatalog.Load(Rules, Presentation).Programs[ProgramId];
        var start = program.Triggers.Single(trigger => trigger.Id == StartTriggerId);
        var spade = program.Triggers.Single(trigger => trigger.Id == SpadeTriggerId);
        Require(program.MinimumRulesVersion == 171 &&
                start.Effects is
                [
                {
                    Op: SkillProgramEffectOp.SelectTarget,
                    Target: SkillProgramEffectTarget.Owner,
                    TargetKind: SkillProgramTargetKind.OtherLiving
                },
                {
                    Op: SkillProgramEffectOp.StartJudgment,
                    Target: SkillProgramEffectTarget.SelectedTarget,
                    JudgmentReason: JudgmentReason
                },
                { Op: SkillProgramEffectOp.MoveBoundCards, SourceBind: "judgment-card" }
                ] &&
                spade is
                {
                    Subject: SkillProgramTriggerSubject.Any,
                    JudgmentSource: SkillProgramTriggerSubject.Owner
                } &&
                spade.JudgmentReasons.SequenceEqual([JudgmentReason]) &&
                spade.Effects.Single() is
                {
                    Op: SkillProgramEffectOp.Damage,
                    Target: SkillProgramEffectTarget.Owner,
                    TargetReference: { Kind: ProgramParticipantRef.EventTarget },
                    Amount: 2,
                    DamageNature: DamageNature.Thunder
                },
            "The unified program must freeze selected judgment subjects, initiator filters and direct subject damage.");
        var raisedMinimum = SkillProgramCatalog.Load(
            Rules.Replace("\"minimumRulesVersion\": 171", "\"minimumRulesVersion\": 172",
                StringComparison.Ordinal),
            Presentation);
        Require(raisedMinimum.Programs.Values.All(item => item.MinimumRulesVersion == 172) &&
                raisedMinimum.Programs[ProgramId].GameplayHash != program.GameplayHash,
            "Program content must hash its concrete rules floor.");

        AssertReject(Rules.Replace("\"schemaVersion\":62", "\"schemaVersion\":57", StringComparison.Ordinal),
            "expected 62");
        AssertReject(Rules.Replace("\"minimumRulesVersion\": 171", "\"minimumRulesVersion\": 170",
                StringComparison.Ordinal),
            "schema minimum 171");
        var forwardCapability = SkillProgramCatalog.Load(
            Rules.Replace(
                "\"minimumRulesVersion\": 171",
                $"\"minimumRulesVersion\": {GameCheckpoint.CurrentRulesVersion + 1}",
                StringComparison.Ordinal),
            Presentation);
        Require(forwardCapability.Programs.Values.All(item =>
                item.MinimumRulesVersion == GameCheckpoint.CurrentRulesVersion + 1),
            "Program capability metadata must not force an unrelated replay-rules bump.");
        AssertReject(Rules.Replace(
                "{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"},",
                string.Empty,
                StringComparison.Ordinal),
            "selectedTarget must be produced before it is read");
        AssertReject(Rules.Replace("\"excludedReasons\":[]",
                "\"excludedReasons\":[\"skill.selected-judgment-test.leiji\"]", StringComparison.Ordinal),
            "must not overlap");
        AssertReject(Rules.Replace("\"target\":\"owner\",\"targetRef\":{\"kind\":\"eventTarget\"},\"amount\":2",
                "\"target\":\"judgmentSubject\",\"amount\":2", StringComparison.Ordinal),
            "judgmentSubject");
    }

    internal static void SelectedSubjectDamageAndReplay()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new FixturePackage());
        var game = FindActivationBoundary(registry);
        var activationPrompt = game.PendingDecision ??
            throw new InvalidOperationException("The selected-judgment activation prompt was lost.");
        var activate = activationPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate");
        var activated = game.Submit(new AnswerPromptCommand(
            0, activationPrompt.PromptId, activate.Id, game.Revision));
        Require(activated.Accepted &&
                game.PendingDecision is
                {
                    Kind: DecisionKind.ProgramTrigger,
                    PlayerSeat: 0,
                    IsPrivate: true,
                    ValidTargetSeats.Count: 4
                } publishedTargetPrompt &&
                !publishedTargetPrompt.ValidTargetSeats.Contains(0) &&
                publishedTargetPrompt.Choices.All(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "select-target"),
            activated.Error?.Message ?? "Activating the selected judgment did not publish other living targets.");
        var targetPrompt = game.PendingDecision!;

        var paused = RoundTrip(game.CreateCheckpoint());
        var restored = GameReplay.Restore(paused, registry);
        Require(restored.PendingDecision?.PromptId == targetPrompt.PromptId &&
                SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            "A selected-judgment target prompt must restore exactly.");

        var targetSeat = targetPrompt.ValidTargetSeats[0];
        var targetHp = restored.CreateSnapshot(0, revealAll: true).Players[targetSeat].Hp;
        var choice = restored.PendingDecision!.Choices.Single(item => item.Targets.SequenceEqual([targetSeat]));
        var selected = restored.Submit(new AnswerPromptCommand(
            0, restored.PendingDecision.PromptId, choice.Id, restored.Revision));
        Require(selected.Accepted, selected.Error?.Message ?? "The selected judgment target was rejected.");
        FinishResolution(restored);

        var requested = restored.Events.Select(item => item.Payload).OfType<JudgmentRequestedEvent>()
            .Single(item => item.Reason == JudgmentReason);
        var damage = restored.Events.Select(item => item.Payload).OfType<ProgramJudgmentDamageRequestedEvent>()
            .Single(item => item.SkillId == ProgramId && item.TriggerId == SpadeTriggerId);
        var finalBinding = restored.Events.Select(item => item.Payload).OfType<ProgramBindingStartedEvent>()
            .Single(item => item.SkillId == ProgramId && item.BindingId == SpadeTriggerId);
        var players = (IReadOnlyList<CharacterState>)typeof(GameEngine)
            .GetField("_players", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!.GetValue(restored)!;
        Require(requested.TargetSeat == targetSeat &&
                players[finalBinding.OwnerSeat].SkillGrants.Grants.Any(grant =>
                    grant.IsEnabled && grant.SkillId == finalBinding.SkillId &&
                    grant.SkillInstanceId == finalBinding.SkillInstanceId) &&
                damage is { SourceSeat: 0, TargetSeat: var damagedSeat, Amount: 2, Nature: DamageNature.Thunder } &&
                damagedSeat == targetSeat &&
                restored.CreateSnapshot(0, revealAll: true).Players[targetSeat].Hp == targetHp - 2 &&
                restored.Events.Select(item => item.Payload).OfType<ProgramJudgmentTriggerResolvedEvent>()
                    .Count(item => item.SkillId == ProgramId && item.TriggerId == SpadeTriggerId && item.Activated) == 1 &&
                restored.Events.Select(item => item.Payload).OfType<ProgramJudgmentTriggerResolvedEvent>()
                    .All(item => item.TriggerId != ClubTriggerId && item.SkillId != ObserverProgramId),
            "The chosen other character must receive direct thunder damage once, without another owner's source-bound subscription.");

        var replay = GameReplay.Restore(RoundTrip(restored.CreateCheckpoint()), registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) &&
                replay.Events.Select(item => item.Payload).OfType<JudgmentRequestedEvent>()
                    .Count(item => item.Reason == JudgmentReason && item.TargetSeat == targetSeat) == 1 &&
                replay.Events.Select(item => item.Payload).OfType<ProgramJudgmentDamageRequestedEvent>()
                    .Count(item => item.SkillId == ProgramId && item.TargetSeat == targetSeat) == 1,
            "The selected subject, final-result cursor, damage and resumed Dodge response must replay exactly.");
        RequireThrows<InvalidOperationException>(() => GameReplay.Restore(
            RoundTrip(restored.CreateCheckpoint()) with { RulesVersion = 167 }, registry));
    }

    internal static void ReplacementOrderUsesTurnActor()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new OrderingFixturePackage());
        var game = FindActivationBoundary(
            registry,
            OrderingFixturePackage.ModeId,
            OrderingFixturePackage.OwnerGeneralId);
        var activationPrompt = game.PendingDecision ??
            throw new InvalidOperationException("The ordering activation prompt was lost.");
        var activate = activationPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate");
        var activated = game.Submit(new AnswerPromptCommand(
            0, activationPrompt.PromptId, activate.Id, game.Revision));
        Require(activated.Accepted,
            activated.Error?.Message ?? "The ordering fixture rejected its trigger activation.");
        var targetPrompt = game.PendingDecision ??
            throw new InvalidOperationException("The ordering fixture lost its target choice.");
        Require(targetPrompt.Kind == DecisionKind.ProgramTrigger,
            "The ordering fixture did not publish its target choice.");

        var turnActorSeat = game.State.CurrentSeat;
        var targetSeat = targetPrompt.ValidTargetSeats.First(seat => seat != turnActorSeat);
        var beforeTarget = RoundTrip(game.CreateCheckpoint());
        var current = GameReplay.Restore(beforeTarget, registry);
        SubmitTarget(current, targetSeat);

        var currentCandidates = current.ResolutionStack.OfType<JudgmentFrame>().Single()
            .ReplacementCandidateSeats ?? [];
        var expectedCurrent = Enumerable.Range(0, 5)
            .Select(offset => (turnActorSeat + offset) % 5)
            .ToArray();
        Require(targetSeat != turnActorSeat &&
                currentCandidates.SequenceEqual(expectedCurrent),
            "Current rules must order frozen replacement candidates from the current turn actor.");

        var replay = GameReplay.Restore(RoundTrip(current.CreateCheckpoint()), registry);
        Require(replay.ResolutionStack.OfType<JudgmentFrame>().Single()
                    .ReplacementCandidateSeats?.SequenceEqual(expectedCurrent) == true &&
                replay.PendingDecision?.PromptId == current.PendingDecision?.PromptId,
            "A paused turn-actor-ordered replacement cursor must replay exactly.");
    }

    private static GameEngine FindActivationBoundary(
        ContentRegistry registry,
        string modeId = FixturePackage.ModeId,
        string ownerGeneralId = FixturePackage.OwnerGeneralId)
    {
        for (var seed = 1; seed <= 256; seed++)
        {
            var game = StartOwner(registry, seed, modeId, ownerGeneralId);
            for (var step = 0; step < 1_200 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } &&
                    game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single() is { } frame &&
                    frame.Candidates[frame.CandidateIndex].TriggerId == StartTriggerId)
                    return game;

                GameCommand command;
                if (game.PendingDecision is { PlayerSeat: 0 } pending)
                {
                    command = pending.Kind switch
                    {
                        DecisionKind.PlayCard => new EndPlayPhaseCommand(0, game.Revision, pending.PromptId),
                        DecisionKind.RespondDodge when pending.Choices.Any(item =>
                            item.Parameters.GetValueOrDefault("response") == "dodge") =>
                            new AnswerPromptCommand(0, pending.PromptId,
                                pending.Choices.First(item =>
                                    item.Parameters.GetValueOrDefault("response") == "dodge").Id,
                                game.Revision),
                        DecisionKind.RescueDying => new AnswerPromptCommand(0, pending.PromptId,
                            pending.Choices.First(item =>
                                item.Parameters.GetValueOrDefault("response") == "let-die").Id,
                            game.Revision),
                        _ => new AnswerPromptCommand(0, pending.PromptId, pending.Choices.Last().Id, game.Revision)
                    };
                }
                else command = new AdvanceOneStepCommand(game.Revision);
                if (!game.Submit(command).Accepted) break;
            }
        }
        throw new InvalidOperationException("No bounded native Dodge reached the selected-judgment trigger.");
    }

    private static GameEngine StartOwner(
        ContentRegistry registry,
        int seed,
        string modeId,
        string ownerGeneralId)
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
                    0, ownerGeneralId, game.Revision, setup.PromptId)).Accepted,
            "The selected-judgment fixture could not select its owner general.");
        for (var step = 0; step < 64; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return game;
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "The fixture could not reach human play.");
        }
        throw new InvalidOperationException("The selected-judgment fixture did not reach human play.");
    }

    private static void SubmitTarget(GameEngine game, int targetSeat)
    {
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("The selected-judgment target prompt was lost.");
        var choice = prompt.Choices.Single(item => item.Targets.SequenceEqual([targetSeat]));
        var result = game.Submit(new AnswerPromptCommand(
            0, prompt.PromptId, choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "The selected judgment target was rejected.");
    }

    private static void FinishResolution(GameEngine game)
    {
        for (var step = 0; step < 96; step++)
        {
            if (game.ResolutionStack.All(item => item is not
                    (ProgramCardTriggerWindowFrame or JudgmentFrame or ProgramJudgmentTriggerWindowFrame)))
                return;
            Require(game.PendingDecision is null,
                $"The mandatory selected judgment unexpectedly exposed {game.PendingDecision?.Kind}.");
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted,
                advanced.Error?.Message ?? "The selected judgment could not resume its parent action.");
        }
        throw new InvalidOperationException("The selected judgment did not finish within the bounded steps.");
    }

    private sealed class FixturePackage : IGameContentPackage
    {
        internal const string ModeId = "identity:selected-judgment-5";
        internal const string OwnerGeneralId = "selected-judgment-test:owner";
        private static readonly string[] OtherGeneralIds =
            Enumerable.Range(1, 4).Select(index => $"selected-judgment-test:other-{index}").ToArray();

        public PackageManifest Manifest { get; } = new(
            "program-selected-judgment-fixture", new Version(1, 0, 0),
            [new PackageDependency("standard", new Version(1, 11, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(Rules, Presentation);
            foreach (var program in catalog.Programs.Values.OrderBy(item => item.Id, StringComparer.Ordinal))
                builder.AddSkill(new ContentSkillDefinition(program.Id,
                    catalog.Presentations[program.Id].Name,
                    catalog.Presentations[program.Id].Description)
                { Program = program });
            builder.AddGeneral(new ContentGeneralDefinition(
                OwnerGeneralId, "选目标判定测试", "zhang_jiao", ProgramId, "qun", BaseHp: 4));
            foreach (var id in OtherGeneralIds)
                builder.AddGeneral(new ContentGeneralDefinition(
                    id, "选目标判定陪测", "cao_cao", ObserverProgramId, "wei", BaseHp: 4));
            var physicalCards = Enumerable.Range(0, 60).SelectMany(_ => new[]
            {
                new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 7),
                new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 2)
            }).ToArray();
            builder.AddDeck(new ContentDeckRecipe("selected-judgment-test:deck", "选目标判定夹具", 4, 2, [])
            {
                PhysicalCards = physicalCards
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId, "选目标判定测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId: "selected-judgment-test:deck",
                GeneralCandidateCount: 5,
                GeneralPoolIds: [OwnerGeneralId, .. OtherGeneralIds]));
        }
    }

    private sealed class OrderingFixturePackage : IGameContentPackage
    {
        internal const string ModeId = "identity:judgment-ordering-5";
        internal const string OwnerGeneralId = "judgment-ordering-test:owner";
        private const string ReplacementProgramId = "judgment-ordering-test:replace";
        private static readonly string[] OtherGeneralIds =
            Enumerable.Range(1, 4).Select(index => $"judgment-ordering-test:other-{index}").ToArray();

        public PackageManifest Manifest { get; } = new(
            "judgment-replacement-ordering-fixture", new Version(1, 0, 0),
            [new PackageDependency("standard", new Version(1, 11, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(OrderingRules, OrderingPresentation);
            foreach (var program in catalog.Programs.Values.OrderBy(item => item.Id, StringComparer.Ordinal))
                builder.AddSkill(new ContentSkillDefinition(program.Id,
                    catalog.Presentations[program.Id].Name,
                    catalog.Presentations[program.Id].Description)
                { Program = program });
            builder.AddGeneral(new ContentGeneralDefinition(
                OwnerGeneralId, "改判顺序测试", "zhang_jiao", ProgramId, "qun", BaseHp: 4,
                AdditionalSkillIds: [ReplacementProgramId]));
            foreach (var id in OtherGeneralIds)
                builder.AddGeneral(new ContentGeneralDefinition(
                    id, "改判顺序陪测", "cao_cao", ReplacementProgramId, "wei", BaseHp: 4));
            var physicalCards = Enumerable.Range(0, 60).SelectMany(_ => new[]
            {
                new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 7),
                new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 2)
            }).ToArray();
            builder.AddDeck(new ContentDeckRecipe("judgment-ordering-test:deck", "改判顺序夹具", 4, 2, [])
            {
                PhysicalCards = physicalCards
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId, "改判顺序测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId: "judgment-ordering-test:deck",
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

    private static void RequireThrows<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private const string Rules = """
        {"schemaVersion":62,"skills":[
          {"id":"selected-judgment-test:leiji","revision":1,"minimumRulesVersion": 171,"modifiers":[],"viewAs":[],"activations":[],"contributions":[],"triggers":[
            {"id":"after-dodge","window":"cardResponseAccepted","ownerRelation":"actor","cardKinds":["dodge"],
             "optional":true,"effects":[
              {"op":"selectTarget","target":"owner","targetKind":"otherLiving"},
              {"op":"startJudgment","target":"selectedTarget","judgmentReason":"skill.selected-judgment-test.leiji","resultBind":"judgment-card","visibility":"public"},{"op":"moveBoundCards","target":"owner","sourceBind":"judgment-card","destination":"discardPile"}
             ]},
            {"id":"spade-damage","window":"judgmentFinalized","subject":"any","judgmentSource":"owner",
             "judgmentReasons":["skill.selected-judgment-test.leiji"],"suits":["spade"],
             "minimumRank":1,"maximumRank":13,"excludedReasons":[],"optional":false,"effects":[
              {"op":"damage","target":"owner","targetRef":{"kind":"eventTarget"},"amount":2,"nature":"thunder"}
             ]},
            {"id":"club-recover-damage","window":"judgmentFinalized","subject":"any","judgmentSource":"owner",
             "judgmentReasons":["skill.selected-judgment-test.leiji"],"suits":["club"],
             "minimumRank":1,"maximumRank":13,"excludedReasons":[],"optional":false,"effects":[
              {"op":"recover","target":"owner","amount":1},
              {"op":"damage","target":"owner","targetRef":{"kind":"eventTarget"},"amount":1,"nature":"thunder"}
             ]}
          ]},
          {"id":"selected-judgment-test:observer","revision":1,"minimumRulesVersion": 171,"modifiers":[],"viewAs":[],"activations":[],"contributions":[],"triggers":[
            {"id":"observer-spade","window":"judgmentFinalized","subject":"any","judgmentSource":"owner",
             "judgmentReasons":["skill.selected-judgment-test.leiji"],"suits":["spade"],
             "minimumRank":1,"maximumRank":13,"excludedReasons":[],"optional":false,"effects":[
              {"op":"damage","target":"owner","targetRef":{"kind":"eventTarget"},"amount":3,"nature":"thunder"}
             ]}
          ]}
        ]}
        """;

    private const string Presentation = """
        {"schemaVersion":3,"skills":{
          "selected-judgment-test:leiji":{
            "name":"经典雷击配置测试","description":"打出闪后选择另一名角色判定，并按最终花色结算。"
          },
          "selected-judgment-test:observer":{
            "name":"发起者过滤陪测","description":"只有自己发起的指定原因判定才会订阅。"
          }
        }}
        """;

    private const string OrderingRules = """
        {"schemaVersion":62,"skills":[
          {"id":"selected-judgment-test:leiji","revision":1,"minimumRulesVersion": 171,"modifiers":[],"viewAs":[],"activations":[],"contributions":[],"triggers":[
            {"id":"after-dodge","window":"cardResponseAccepted","ownerRelation":"actor","cardKinds":["dodge"],
             "optional":true,"effects":[
              {"op":"selectTarget","target":"owner","targetKind":"otherLiving"},
              {"op":"startJudgment","target":"selectedTarget","judgmentReason":"skill.judgment-ordering-test","resultBind":"judgment-card","visibility":"public"},{"op":"moveBoundCards","target":"owner","sourceBind":"judgment-card","destination":"discardPile"}
             ]}
          ]},
          {"id":"judgment-ordering-test:replace","revision":1,"minimumRulesVersion": 171,"modifiers":[],"viewAs":[],"activations":[],"contributions":[],"triggers":[
            {"id":"replace","window":"judgmentReplacing","subject":"any","excludedReasons":[],
             "optional":true,"effects":[
              {"op":"replaceJudgment","target":"owner","zones":["hand"],"suits":["spade","club"],
               "oldCardDestination":"discardPile"}
             ]}
          ]}
        ]}
        """;

    private const string OrderingPresentation = """
        {"schemaVersion":3,"skills":{
          "selected-judgment-test:leiji":{
            "name":"改判顺序判定发起","description":"打出闪后选择另一名角色判定。"
          },
          "judgment-ordering-test:replace":{
            "name":"改判顺序候选","description":"用黑色手牌替换任意角色的判定牌。"
          }
        }}
        """;
}
