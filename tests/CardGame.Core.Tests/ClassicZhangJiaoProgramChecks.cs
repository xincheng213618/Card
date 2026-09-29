using CardGame.Content.Standard;
using CardGame.Core;

internal static class ClassicZhangJiaoProgramChecks
{
    private const string LeijiModeId = "identity:classic-zhang-jiao-program-5";
    private const string HuangtianModeId = "identity:classic-huangtian-program-5";
    private const string ZhangJiaoId = "classic:zhang-jiao";
    private const string LeijiReason = "skill.classic.leiji";

    internal static void LeijiGuidaoAndReplay()
    {
        var registry = CreateLeijiRegistry();
        var game = FindLeijiActivation(registry);
        var activation = game.PendingDecision ??
            throw new InvalidOperationException("Formal classic Leiji activation was lost.");
        var activate = activation.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate");
        var activated = game.Submit(new AnswerPromptCommand(
            0, activation.PromptId, activate.Id, game.Revision));
        Require(activated.Accepted,
            activated.Error?.Message ?? "Formal classic Leiji activation was rejected.");
        var targetPrompt = game.PendingDecision ??
            throw new InvalidOperationException("Formal classic Leiji target choice was lost.");
        Require(targetPrompt is
        {
            Kind: DecisionKind.ProgramTrigger,
            PlayerSeat: 0,
            IsPrivate: true
        },
            "Formal classic Leiji did not publish its target choice.");

        var targetSeat = targetPrompt.ValidTargetSeats[0];
        var targetHp = game.CreateSnapshot(0, revealAll: true).Players[targetSeat].Hp;
        var targetChoice = targetPrompt.Choices.Single(choice => choice.Targets.SequenceEqual([targetSeat]));
        var selected = game.Submit(new AnswerPromptCommand(
            0, targetPrompt.PromptId, targetChoice.Id, game.Revision));
        Require(selected.Accepted &&
                game.PendingDecision is
                {
                    Kind: DecisionKind.ProgramJudgmentReplacement,
                    PlayerSeat: 0,
                    IsPrivate: true
                },
            selected.Error?.Message ?? "Formal classic Guidao did not intercept the Leiji judgment.");

        var replacementBoundary = RoundTrip(game.CreateCheckpoint());
        var judgmentFrame = game.ResolutionStack.OfType<JudgmentFrame>().Single();
        var oldJudgmentCardId = judgmentFrame.CardId ??
            throw new InvalidOperationException("Formal classic Leiji judgment has no public card.");

        var skipped = GameReplay.Restore(replacementBoundary, registry);
        AnswerReplacement(skipped, cardId: null);
        FinishLeiji(skipped);
        var requested = skipped.Events.Select(item => item.Payload).OfType<JudgmentRequestedEvent>()
            .Single(item => item.Reason == LeijiReason);
        var damage = skipped.Events.Select(item => item.Payload).OfType<ProgramJudgmentDamageRequestedEvent>()
            .Single(item => item.SkillId == "classic:leiji" && item.TriggerId == "spade-damage");
        Require(requested.TargetSeat == targetSeat &&
                damage is
                {
                    SourceSeat: 0,
                    TargetSeat: var damagedSeat,
                    Amount: 2,
                    Nature: DamageNature.Thunder
                } &&
                damagedSeat == targetSeat &&
                skipped.CreateSnapshot(0, revealAll: true).Players[targetSeat].Hp == targetHp - 2,
            "Formal classic Leiji must damage its selected judgment subject for two thunder on Spade.");
        AssertReplay(skipped, registry, targetSeat, expectReplacement: false);

        var replaced = GameReplay.Restore(replacementBoundary, registry);
        var replacementPrompt = replaced.PendingDecision!;
        var replacementCardId = replacementPrompt.ValidCardIds.First();
        AnswerReplacement(replaced, replacementCardId);
        var exchange = replaced.Events.Select(item => item.Payload)
            .OfType<ProgramJudgmentReplacementResolvedEvent>()
            .Single(item => item.SkillId == "classic:guidao" && item.Activated);
        Require(exchange is
        {
            TriggerId: "replace-judgment",
            OwnerSeat: 0,
            OldCardDestination: SkillProgramOldJudgmentCardDestination.OwnerHand
        } &&
                exchange.OldCardId == oldJudgmentCardId &&
                exchange.ReplacementCardId == replacementCardId &&
                replaced.CreateSnapshot(0, revealAll: true).Players[0].Hand.Any(card =>
                    card.Id == oldJudgmentCardId),
            "Formal classic Guidao must exchange its exact black card for the old public judgment card.");
        FinishLeiji(replaced);
        AssertReplay(replaced, registry, targetSeat, expectReplacement: true);
    }

    internal static GameEngine FindLeijiActivation(
        ContentRegistry registry,
        Role humanRole = Role.Lord,
        string modeId = LeijiModeId,
        int playerCount = 5)
    {
        for (var seed = 1; seed <= 512; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = playerCount,
                ModeId = modeId,
                HumanSeat = 0,
                HumanRole = humanRole,
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2,
                MaxTurns = 80
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted ||
                game.PendingDecision is not { Kind: DecisionKind.SelectGeneral } setup ||
                !game.Submit(new SelectGeneralCommand(
                    0, ZhangJiaoId, game.Revision, setup.PromptId)).Accepted)
                continue;
            for (var step = 0; step < 2_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } &&
                    game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single() is { } frame &&
                    frame.Candidates[frame.CandidateIndex] is
                    {
                        SkillId: "classic:leiji",
                        TriggerId: "after-dodge"
                    })
                    return game;

                GameCommand command = game.PendingDecision is { PlayerSeat: 0 } pending
                    ? pending.Kind switch
                    {
                        DecisionKind.PlayCard => new EndPlayPhaseCommand(0, game.Revision, pending.PromptId),
                        DecisionKind.RespondDodge when pending.Choices.Any(choice =>
                            choice.Parameters.GetValueOrDefault("response") == "dodge") =>
                            new AnswerPromptCommand(
                                0, pending.PromptId,
                                pending.Choices.First(choice =>
                                    choice.Parameters.GetValueOrDefault("response") == "dodge").Id,
                                game.Revision),
                        DecisionKind.RescueDying => new AnswerPromptCommand(
                            0, pending.PromptId,
                            pending.Choices.First(choice =>
                                choice.Parameters.GetValueOrDefault("response") == "let-die").Id,
                            game.Revision),
                        _ => new AnswerPromptCommand(
                            0, pending.PromptId, pending.Choices.Last().Id, game.Revision)
                    }
                    : new AdvanceOneStepCommand(game.Revision);
                if (!game.Submit(command).Accepted) break;
            }
        }
        throw new InvalidOperationException("No bounded formal classic Dodge reached Leiji.");
    }

    private static void AnswerReplacement(GameEngine game, int? cardId)
    {
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Formal classic Guidao prompt was lost.");
        var choice = cardId is { } selectedCardId
            ? prompt.Choices.Single(item => item.Cards.SequenceEqual([selectedCardId]))
            : prompt.Choices.Single(item =>
                item.Parameters.GetValueOrDefault("action") == "program-judgment-replace-skip");
        var answered = game.Submit(new AnswerPromptCommand(
            0, prompt.PromptId, choice.Id, game.Revision));
        Require(answered.Accepted, answered.Error?.Message ?? "Formal classic Guidao answer was rejected.");
    }

    private static void FinishLeiji(GameEngine game)
    {
        for (var step = 0; step < 128; step++)
        {
            if (game.ResolutionStack.All(frame => frame is not
                    (ProgramCardTriggerWindowFrame or JudgmentFrame or ProgramJudgmentTriggerWindowFrame)))
                return;
            Require(game.PendingDecision is null,
                $"Formal classic Leiji exposed unexpected {game.PendingDecision?.Kind}.");
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted,
                advanced.Error?.Message ?? "Formal classic Leiji did not finish.");
        }
        throw new InvalidOperationException("Formal classic Leiji did not finish within the bounded steps.");
    }

    private static void AssertReplay(
        GameEngine game,
        ContentRegistry registry,
        int targetSeat,
        bool expectReplacement)
    {
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                replay.Events.Select(item => item.Payload).OfType<JudgmentRequestedEvent>()
                    .Count(item => item.Reason == LeijiReason && item.TargetSeat == targetSeat) == 1 &&
                replay.Events.Select(item => item.Payload).OfType<ProgramJudgmentReplacementResolvedEvent>()
                    .Count(item => item.SkillId == "classic:guidao" && item.Activated) ==
                    (expectReplacement ? 1 : 0),
            "Formal classic Leiji and Guidao must replay exactly once.");
    }

    internal static ContentRegistry CreateLeijiRegistry() =>
        ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new SyntheticPackage(
                "classic-zhang-jiao-program-fixture",
                builder =>
                {
                    var cards = Enumerable.Range(0, 60).SelectMany(_ => new[]
                    {
                        new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 7),
                        new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 2)
                    }).ToArray();
                    builder.AddDeck(new ContentDeckRecipe(
                        "classic-zhang-jiao-program:deck", "经典张角配置夹具", 4, 2, [])
                    {
                        PhysicalCards = cards
                    });
                    builder.AddMode(new ContentModeDefinition(
                        LeijiModeId, "经典张角配置测试", 5, 5,
                        IdentityRoles,
                        "classic-zhang-jiao-program:deck",
                        5,
                        [ZhangJiaoId, "classic:zhang-fei", "classic:huang-zhong", "classic:ma-chao", "classic:lu-bu"]));
                },
                new PackageDependency("standard-classic-generals", new Version(1, 65, 0))));

    private static ContentRegistry CreateHuangtianRegistry() =>
        ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new SyntheticPackage(
                "classic-huangtian-program-fixture",
                builder =>
                {
                    var cards = Enumerable.Range(0, 40).SelectMany(_ => new[]
                    {
                        new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, 2),
                        new ContentDeckPhysicalCard("standard:lightning", Suit.Spade, 1),
                        new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)
                    }).ToArray();
                    builder.AddDeck(new ContentDeckRecipe(
                        "classic-huangtian-program:deck", "经典黄天配置夹具", 4, 2, [])
                    {
                        PhysicalCards = cards
                    });
                    builder.AddMode(new ContentModeDefinition(
                        HuangtianModeId, "经典黄天配置测试", 5, 5,
                        IdentityRoles,
                        "classic-huangtian-program:deck",
                        5,
                        [ZhangJiaoId, "classic:lu-bu", "classic:hua-xiong", "classic:pang-de", "sp:zhao-yun"]));
                },
                new PackageDependency("standard-classic-generals", new Version(1, 65, 0))));

    private static IReadOnlyDictionary<string, int> IdentityRoles { get; } =
        new Dictionary<string, int>
        {
            [nameof(Role.Lord)] = 1,
            [nameof(Role.Loyalist)] = 1,
            [nameof(Role.Rebel)] = 2,
            [nameof(Role.Renegade)] = 1
        };

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

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
}
