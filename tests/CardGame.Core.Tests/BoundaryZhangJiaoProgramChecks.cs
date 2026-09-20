using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryZhangJiaoProgramChecks
{
    private const string LeijiModeId = "identity:boundary-zhang-jiao-program-5";
    private const string HuangtianModeId = "identity:boundary-huangtian-program-5";
    private const string ZhangJiaoId = "boundary:zhang-jiao";
    private const string LeijiReason = "skill.boundary.leiji";

    internal static void DodgeLeijiGuidaoAndReplay()
    {
        var registry = CreateLeijiRegistry();
        var game = FindLeijiActivation(registry);
        var activation = game.PendingDecision ??
            throw new InvalidOperationException("Formal boundary Leiji activation was lost.");
        Answer(game, activation.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "program-trigger-activate"));
        Require(game.PendingDecision is
        {
            Kind: DecisionKind.ProgramJudgmentReplacement,
            PlayerSeat: 0,
            IsPrivate: true
        }, "Formal boundary Leiji must judge its owner before Guidao can replace the result.");

        var replacementBoundary = RoundTrip(game.CreateCheckpoint());
        var judgmentFrame = game.ResolutionStack.OfType<JudgmentFrame>().Single();
        var oldJudgmentCardId = judgmentFrame.CardId ??
            throw new InvalidOperationException("Formal boundary Leiji judgment has no public card.");

        var skipped = GameReplay.Restore(replacementBoundary, registry);
        AnswerReplacement(skipped, cardId: null);
        var damageActivation = AdvanceToJudgmentTrigger(skipped);
        Require(damageActivation is
        {
            Kind: DecisionKind.ProgramJudgmentTrigger,
            PlayerSeat: 0,
            IsPrivate: true
        } && skipped.ResolutionStack.OfType<ProgramJudgmentTriggerWindowFrame>().Single() is { } triggerFrame &&
            triggerFrame.Judgment.SubjectSeat == 0 &&
            triggerFrame.Judgment.Reason == LeijiReason &&
            triggerFrame.Candidates[triggerFrame.CandidateIndex] is
            {
                SkillId: "boundary:leiji",
                TriggerId: "spade-damage"
            },
            "Formal boundary Leiji must subscribe to its own finalized Spade judgment.");
        Answer(skipped, damageActivation.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "program-judgment-trigger-activate"));
        var targetPrompt = skipped.PendingDecision ??
            throw new InvalidOperationException("Formal boundary Leiji target choice was lost.");
        var targetSeat = targetPrompt.ValidTargetSeats.First(seat => seat != 0);
        var targetHp = skipped.CreateSnapshot(0, revealAll: true).Players[targetSeat].Hp;
        Answer(skipped, targetPrompt.Choices.Single(choice => choice.Targets.SequenceEqual([targetSeat])));
        FinishLeiji(skipped);
        var damage = skipped.Events.Select(item => item.Payload).OfType<ProgramJudgmentDamageRequestedEvent>()
            .Single(item => item.SkillId == "boundary:leiji" && item.TriggerId == "spade-damage");
        Require(damage is
        {
            SourceSeat: 0,
            TargetSeat: var damagedSeat,
            Amount: 2,
            Nature: DamageNature.Thunder
        } && damagedSeat == targetSeat &&
            skipped.CreateSnapshot(0, revealAll: true).Players[targetSeat].Hp == targetHp - 2,
            "Formal boundary Leiji must deal two thunder damage to the selected living target on Spade.");
        AssertReplay(skipped, registry, expectReplacement: false);

        var replaced = GameReplay.Restore(replacementBoundary, registry);
        var replacementPrompt = replaced.PendingDecision!;
        var ownerBefore = replaced.CreateSnapshot(0, revealAll: true).Players[0];
        var replacementCard = ownerBefore.Hand.First(card =>
            replacementPrompt.ValidCardIds.Contains(card.Id) &&
            card.Suit == Suit.Spade && card.Rank is >= 2 and <= 9);
        AnswerReplacement(replaced, replacementCard.Id);
        var exchange = replaced.Events.Select(item => item.Payload)
            .OfType<ProgramJudgmentReplacementResolvedEvent>()
            .Single(item => item.SkillId == "boundary:guidao" && item.Activated);
        Require(exchange is
        {
            TriggerId: "replace-judgment",
            OwnerSeat: 0,
            SubjectSeat: 0,
            OldCardDestination: SkillProgramOldJudgmentCardDestination.DiscardPile,
            DrawnCards: 1
        } && exchange.OldCardId == oldJudgmentCardId &&
            exchange.ReplacementCardId == replacementCard.Id &&
            replaced.CreateCardZoneDiagnostics().Single(card => card.CardId == oldJudgmentCardId).Location ==
            CardLocation.DiscardPile &&
            replaced.CreateSnapshot(0, revealAll: true).Players[0].HandCount == ownerBefore.HandCount,
            "Formal boundary Guidao must discard the old judgment and draw once for a committed Spade 2-9 replacement.");
        var skipDamage = AdvanceToJudgmentTrigger(replaced);
        Answer(replaced, skipDamage.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "program-judgment-trigger-skip"));
        FinishLeiji(replaced);
        AssertReplay(replaced, registry, expectReplacement: true);
        RequireThrows<InvalidOperationException>(() =>
            GameReplay.Restore(replacementBoundary with { RulesVersion = 87 }, registry));
    }

    internal static void HuangtianContributionAndReplay()
    {
        var registry = CreateHuangtianRegistry();
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                ModeId = HuangtianModeId,
                HumanSeat = 0,
                HumanRole = Role.Rebel,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2,
                MaxTurns = 80
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted) continue;
            var full = game.CreateSnapshot(0, revealAll: true);
            var lord = full.Players.Single(player => player.Role == Role.Lord);
            if (lord.GeneralId != ZhangJiaoId || full.Players[0].GeneralId == ZhangJiaoId) continue;
            for (var step = 0; step < 512 &&
                 game.State.Status != EngineStatus.Completed &&
                 game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; step++)
            {
                GameCommand command = game.PendingDecision is { PlayerSeat: 0 } pending
                    ? new AnswerPromptCommand(
                        0, pending.PromptId, pending.Choices.Last().Id, game.Revision)
                    : new AdvanceOneStepCommand(game.Revision);
                if (!game.Submit(command).Accepted) break;
            }
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } play) continue;
            var action = game.GetHumanLegalActions().FirstOrDefault(item =>
                item.ProgramSkillId == "boundary:huangtian" &&
                item.ProgramActivationId == "contribute" &&
                item.ProgramSkillOwnerSeat == lord.Seat);
            if (action is null || action.SelectableCardIds.Count == 0) continue;
            var provider = game.CreateSnapshot(0, revealAll: true).Players[0];
            var expectedCards = provider.Hand
                .Where(card => card.Kind == CardKind.Dodge || card.Suit == Suit.Spade)
                .Select(card => card.Id)
                .Order()
                .ToArray();
            Require(action.SelectableCardIds.Order().SequenceEqual(expectedCards),
                "Formal boundary Huangtian must publish the union of Dodge and Spade hand cards.");
            var cardId = action.SelectableCardIds[0];
            var accepted = game.Submit(new UseProgramSkillCommand(
                0,
                "boundary:huangtian",
                "contribute",
                [cardId],
                [lord.Seat],
                game.Revision,
                play.PromptId)
            {
                SkillOwnerSeat = lord.Seat
            });
            Require(accepted.Accepted, accepted.Error?.Message ?? "Formal boundary Huangtian was rejected.");
            var resolved = game.Events.Select(item => item.Payload)
                .OfType<ProgramSkillContributionResolvedEvent>()
                .Single(item => item.SkillId == "boundary:huangtian" && item.CardId == cardId);
            Require(resolved.SkillOwnerSeat == lord.Seat &&
                    game.CreateSnapshot(0, revealAll: true).Players[lord.Seat].Hand.Any(item => item.Id == cardId),
                "Formal boundary Huangtian must transfer the exact provider card to the living Lord owner.");
            for (var step = 0; step < 8 &&
                 game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; step++)
                Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                    "Formal boundary Huangtian did not resume the provider's play phase.");
            Require(game.GetHumanLegalActions().All(item => item.ProgramSkillId != "boundary:huangtian"),
                "Formal boundary Huangtian must be limited once for this provider in the play phase.");

            var checkpoint = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(checkpoint, registry);
            Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                    SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                    replay.Events.Select(item => item.Payload)
                        .OfType<ProgramSkillContributionResolvedEvent>()
                        .Count(item => item.SkillId == "boundary:huangtian" && item.CardId == cardId) == 1,
                "Formal boundary Huangtian transfer and phase ledger must replay exactly.");
            RequireThrows<InvalidOperationException>(() =>
                GameReplay.Restore(checkpoint with { RulesVersion = 87 }, registry));
            return;
        }
        throw new InvalidOperationException("No bounded formal boundary Huangtian provider reached its play phase.");
    }

    private static GameEngine FindLeijiActivation(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 512; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                ModeId = LeijiModeId,
                HumanSeat = 0,
                HumanRole = Role.Lord,
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
                if (game.PendingDecision is { Kind: DecisionKind.ProgramCardTrigger, PlayerSeat: 0 } &&
                    game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single() is { } frame &&
                    frame.Candidates[frame.CandidateIndex] is
                    {
                        SkillId: "boundary:leiji",
                        TriggerId: "after-dodge-response"
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
        throw new InvalidOperationException("No bounded formal boundary Dodge reached Leiji.");
    }

    private static void AnswerReplacement(GameEngine game, int? cardId)
    {
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Formal boundary Guidao prompt was lost.");
        var choice = cardId is { } selectedCardId
            ? prompt.Choices.Single(item => item.Cards.SequenceEqual([selectedCardId]))
            : prompt.Choices.Single(item =>
                item.Parameters.GetValueOrDefault("action") == "program-judgment-replace-skip");
        Answer(game, choice);
    }

    private static void FinishLeiji(GameEngine game)
    {
        for (var step = 0; step < 128; step++)
        {
            if (game.ResolutionStack.All(frame => frame is not
                    (ProgramCardTriggerWindowFrame or JudgmentFrame or ProgramJudgmentTriggerWindowFrame or DamageFrame)))
                return;
            Require(game.PendingDecision is null,
                $"Formal boundary Leiji exposed unexpected {game.PendingDecision?.Kind}.");
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted,
                advanced.Error?.Message ?? "Formal boundary Leiji did not finish.");
        }
        throw new InvalidOperationException("Formal boundary Leiji did not finish within the bounded steps.");
    }

    private static PendingDecision AdvanceToJudgmentTrigger(GameEngine game)
    {
        for (var step = 0; step < 16; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.ProgramJudgmentTrigger } prompt)
                return prompt;
            Require(game.PendingDecision is null,
                $"Formal boundary Leiji exposed unexpected {game.PendingDecision?.Kind} before its result branch.");
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted,
                advanced.Error?.Message ?? "Formal boundary Leiji could not reach its result branch.");
        }
        throw new InvalidOperationException("Formal boundary Leiji result branch was not reached.");
    }

    private static void AssertReplay(GameEngine game, ContentRegistry registry, bool expectReplacement)
    {
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                replay.Events.Select(item => item.Payload).OfType<JudgmentRequestedEvent>()
                    .Count(item => item.Reason == LeijiReason && item.TargetSeat == 0) == 1 &&
                replay.Events.Select(item => item.Payload).OfType<ProgramJudgmentReplacementResolvedEvent>()
                    .Count(item => item.SkillId == "boundary:guidao" && item.Activated) ==
                    (expectReplacement ? 1 : 0),
            "Formal boundary Leiji and Guidao must replay exactly once.");
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("Prompt was lost.");
        var answered = game.Submit(new AnswerPromptCommand(
            0, prompt.PromptId, choice.Id, game.Revision));
        Require(answered.Accepted, answered.Error?.Message ?? "Formal boundary Zhang Jiao answer was rejected.");
    }

    private static ContentRegistry CreateLeijiRegistry() =>
        ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(new Version(1, 66, 0)),
            new SyntheticPackage(
                "boundary-zhang-jiao-program-fixture",
                builder =>
                {
                    var cards = Enumerable.Range(0, 60).SelectMany(_ => new[]
                    {
                        new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 7),
                        new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 2)
                    }).ToArray();
                    builder.AddDeck(new ContentDeckRecipe(
                        "boundary-zhang-jiao-program:deck", "界张角配置夹具", 4, 2, [])
                    {
                        PhysicalCards = cards
                    });
                    builder.AddMode(new ContentModeDefinition(
                        LeijiModeId, "界张角配置测试", 5, 5,
                        IdentityRoles,
                        "boundary-zhang-jiao-program:deck",
                        5,
                        [ZhangJiaoId, "classic:zhang-fei", "classic:huang-zhong", "classic:ma-chao", "classic:lu-bu"]));
                },
                new PackageDependency("standard-classic-generals", new Version(1, 66, 0))));

    private static ContentRegistry CreateHuangtianRegistry() =>
        ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(new Version(1, 66, 0)),
            new SyntheticPackage(
                "boundary-huangtian-program-fixture",
                builder =>
                {
                    var cards = Enumerable.Range(0, 40).SelectMany(_ => new[]
                    {
                        new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, 2),
                        new ContentDeckPhysicalCard("standard:lightning", Suit.Spade, 1),
                        new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 7),
                        new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)
                    }).ToArray();
                    builder.AddDeck(new ContentDeckRecipe(
                        "boundary-huangtian-program:deck", "界黄天配置夹具", 4, 2, [])
                    {
                        PhysicalCards = cards
                    });
                    builder.AddMode(new ContentModeDefinition(
                        HuangtianModeId, "界黄天配置测试", 5, 5,
                        IdentityRoles,
                        "boundary-huangtian-program:deck",
                        5,
                        [ZhangJiaoId, "classic:lu-bu", "classic:hua-xiong", "classic:pang-de", "sp:zhao-yun"]));
                },
                new PackageDependency("standard-classic-generals", new Version(1, 66, 0))));

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
