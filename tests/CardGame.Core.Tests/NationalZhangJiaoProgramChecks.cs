using CardGame.Content.Standard;
using CardGame.Core;

internal static class NationalZhangJiaoProgramChecks
{
    private const string FixtureModeId = "national:zhang-jiao-program-fixture-4";
    private const string ZhangJiaoId = "national:zhang-jiao";
    private const string HuaTuoId = "national:hua-tuo";
    private const string LeijiReason = "skill.national.leiji";

    internal static void ContentContractAndRevealBoundary()
    {
        var previous = CreateRegistry(includeNationalZhangJiao: false);
        var registry = CreateRegistry(includeNationalZhangJiao: true);
        var package = registry.Packages.Single(item => item.Id == "standard-national-zhang-jiao");
        Require(package.Version == new Version(1, 0, 0) && package.Dependencies.Select(dependency =>
                $"{dependency.Id}@{dependency.MinimumVersion}").SequenceEqual(
                [
                    "standard-national-war-lite@1.1.0",
                    "standard-active-skills@1.0.0",
                    "standard-rescue-skills@1.0.0"
                ]),
            "The formal national Zhang Jiao package signature or dependencies drifted.");

        Require(registry.Generals[ZhangJiaoId] is
        { Name: "张角", FactionId: "qun", BaseHp: 3 } zhangJiao &&
                zhangJiao.SkillIds.SequenceEqual(["national:leiji", "national:guidao"]) &&
                registry.Generals[HuaTuoId] is
                { Name: "华佗", FactionId: "qun", BaseHp: 3 } huaTuo &&
                huaTuo.SkillIds.SequenceEqual(["standard:jijiu", "standard:qingnang"]),
            "The formal Qun pair must preserve standard-national vitals and ordered skills.");

        Require(registry.Skills["national:leiji"] is { Program: { } leiji } &&
                leiji.RuntimeVersion == "skill-program-v61" &&
                leiji.MinimumRulesVersion == 171 &&
                leiji.Triggers.Count == 2 &&
                leiji.Triggers.Single(trigger => trigger.Id == "spade-damage").Suits
                    .SequenceEqual([Suit.Spade]) &&
                leiji.Triggers.All(trigger => trigger.Suits.All(suit => suit != Suit.Club)) &&
                registry.Skills["national:guidao"] is { Program: { } guidao } &&
                guidao.RuntimeVersion == "skill-program-v61" &&
                guidao.MinimumRulesVersion == 171 &&
                guidao.Triggers.Single().Effects.Single().OldCardDestination ==
                    SkillProgramOldJudgmentCardDestination.OwnerHand &&
                !registry.Skills.ContainsKey("national:huangtian"),
            "National Zhang Jiao must expose only Spade Leiji and owner-hand Guidao in the current program schema.");

        var mode = registry.Modes["national:zhang-jiao-4"];
        Require(mode is
        {
            MinPlayers: 4,
            MaxPlayers: 4,
            GeneralCandidateCount: 2,
            ModeKind: ContentModeKind.NationalWarLite
        } &&
                mode.FactionCounts!.OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .SequenceEqual(new Dictionary<string, int>
                    {
                        ["qun"] = 1,
                        ["shu"] = 2,
                        ["wei"] = 1
                    }.OrderBy(entry => entry.Key, StringComparer.Ordinal)) &&
                mode.GeneralPoolIds!.Count == 8 &&
                mode.GeneralPoolIds.Contains(ZhangJiaoId) &&
                mode.GeneralPoolIds.Contains(HuaTuoId),
            "The formal four-player national mode must declare its exact Wei/Shu/Qun distribution and pair-sized pool.");
        Require(!previous.Generals.ContainsKey(ZhangJiaoId) &&
                !previous.Generals.ContainsKey(HuaTuoId) &&
                !previous.Modes.ContainsKey("national:zhang-jiao-4") &&
                previous.ContentHash != registry.ContentHash,
            "Adding formal national Zhang Jiao must not rewrite the previous national registry.");

        var game = FindHumanQunFixture(CreateFixtureRegistry(), stopAtLeiji: false);
        var privateView = game.CreateSnapshot(0);
        var observerView = game.CreateSnapshot(1);
        Require(privateView.Players[0].Skills is { Count: 2 } &&
                privateView.Players[0].SecondarySkills is { Count: 2 } &&
                observerView.Players[0].Skills is null &&
                observerView.Players[0].SecondarySkills is null &&
                EnabledPrograms(game).Length == 0,
            "The selected national pair leaked or enabled programs while both generals were hidden.");
        RevealPrimary(game);
        Require(EnabledPrograms(game).SequenceEqual(["national:guidao", "national:leiji"]) &&
                game.CreateSnapshot(1).Players[0] is
                {
                    Skills.Count: 2,
                    SecondarySkills: null,
                    IsGeneralPublic: true,
                    IsSecondaryGeneralPublic: false
                },
            "Revealing national Zhang Jiao must enable both ordered programs without leaking Hua Tuo.");
        var restored = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), CreateFixtureRegistry());
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(1, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(1, revealAll: true)),
            "The formal national pair reveal did not replay exactly.");
    }

    internal static void LeijiGuidaoAndReplay()
    {
        var registry = CreateFixtureRegistry();
        var game = FindHumanQunFixture(registry, stopAtLeiji: true);
        var activation = game.PendingDecision ??
            throw new InvalidOperationException("Formal national Leiji activation was lost.");
        var activate = activation.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Require(game.Submit(new AnswerPromptCommand(
                0, activation.PromptId, activate.Id, game.Revision)).Accepted,
            "Formal national Leiji activation was rejected.");

        var targetPrompt = game.PendingDecision ??
            throw new InvalidOperationException("Formal national Leiji target choice was lost.");
        Require(targetPrompt is
        {
            Kind: DecisionKind.ProgramTrigger,
            PlayerSeat: 0,
            IsPrivate: true
        }, "Formal national Leiji did not publish a private other-character target choice.");
        var full = game.CreateSnapshot(0, revealAll: true);
        var targetSeat = targetPrompt.ValidTargetSeats
            .First(seat => full.Players[seat].Hp >= 3);
        var targetHp = full.Players[targetSeat].Hp;
        var targetChoice = targetPrompt.Choices.Single(choice => choice.Targets.SequenceEqual([targetSeat]));
        Require(game.Submit(new AnswerPromptCommand(
                0, targetPrompt.PromptId, targetChoice.Id, game.Revision)).Accepted &&
                game.PendingDecision is
                {
                    Kind: DecisionKind.ProgramJudgmentReplacement,
                    PlayerSeat: 0,
                    IsPrivate: true
                },
            "Formal national Guidao did not intercept the selected subject's Leiji judgment.");

        var replacementBoundary = RoundTrip(game.CreateCheckpoint());
        var oldJudgmentCardId = game.ResolutionStack.OfType<JudgmentFrame>().Single().CardId ??
            throw new InvalidOperationException("Formal national Leiji judgment has no public card.");

        var skipped = GameReplay.Restore(replacementBoundary, registry);
        AnswerReplacement(skipped, cardId: null);
        FinishLeiji(skipped);
        var requested = skipped.Events.Select(item => item.Payload).OfType<JudgmentRequestedEvent>()
            .Single(item => item.Reason == LeijiReason);
        var damage = skipped.Events.Select(item => item.Payload)
            .OfType<ProgramJudgmentDamageRequestedEvent>()
            .Single(item => item.SkillId == "national:leiji" && item.TriggerId == "spade-damage");
        Require(requested.TargetSeat == targetSeat &&
                damage is
                {
                    SourceSeat: 0,
                    TargetSeat: var damagedSeat,
                    Amount: 2,
                    Nature: DamageNature.Thunder
                } &&
                damagedSeat == targetSeat &&
                skipped.CreateSnapshot(0, revealAll: true).Players[targetSeat].Hp == targetHp - 2 &&
                skipped.Events.Select(item => item.Payload).OfType<ProgramJudgmentDamageRequestedEvent>()
                    .All(item => item.TriggerId != "club-recover-damage"),
            "Formal national Leiji must deal exactly two thunder damage only for its Spade branch.");
        AssertReplay(skipped, registry, targetSeat, expectReplacement: false);

        var replaced = GameReplay.Restore(replacementBoundary, registry);
        var replacementPrompt = replaced.PendingDecision!;
        var replacementCardId = replacementPrompt.ValidCardIds.First();
        AnswerReplacement(replaced, replacementCardId);
        var exchange = replaced.Events.Select(item => item.Payload)
            .OfType<ProgramJudgmentReplacementResolvedEvent>()
            .Single(item => item.SkillId == "national:guidao" && item.Activated);
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
            "Formal national Guidao must exchange its exact black card for the old judgment card.");
        FinishLeiji(replaced);
        AssertReplay(replaced, registry, targetSeat, expectReplacement: true);
        RequireThrows<InvalidOperationException>(() =>
            GameReplay.Restore(replacementBoundary with { RulesVersion = 88 }, registry));
    }

    private static GameEngine FindHumanQunFixture(ContentRegistry registry, bool stopAtLeiji)
    {
        for (var seed = 1; seed <= 1_024; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 4,
                ModeId = FixtureModeId,
                HumanSeat = 0,
                HumanRole = null,
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2,
                MaxTurns = 80
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted) continue;
            var selection = 0;
            var usable = true;
            for (var step = 0; step < 96 && game.State.Status != EngineStatus.AwaitingHumanPlay; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.SelectGeneral } prompt)
                {
                    var id = selection++ == 0 ? ZhangJiaoId : HuaTuoId;
                    if (!prompt.ValidContentIds.Contains(id) ||
                        !game.Submit(new SelectGeneralCommand(
                            0, id, game.Revision, prompt.PromptId)).Accepted)
                    {
                        usable = false;
                        break;
                    }
                }
                else if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted)
                {
                    usable = false;
                    break;
                }
            }
            if (!usable || game.State.Status != EngineStatus.AwaitingHumanPlay) continue;
            if (!stopAtLeiji) return game;

            RevealPrimary(game);
            for (var step = 0; step < 2_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } &&
                    game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single() is { } frame &&
                    frame.Candidates[frame.CandidateIndex] is
                    {
                        SkillId: "national:leiji",
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
                                0,
                                pending.PromptId,
                                pending.Choices.First(choice =>
                                    choice.Parameters.GetValueOrDefault("response") == "dodge").Id,
                                game.Revision),
                        DecisionKind.RescueDying => new AnswerPromptCommand(
                            0,
                            pending.PromptId,
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
        throw new InvalidOperationException("No bounded formal national Dodge reached Leiji.");
    }

    private static void RevealPrimary(GameEngine game)
    {
        var play = game.PendingDecision ??
            throw new InvalidOperationException("National reveal fixture lost its play prompt.");
        Require(game.Submit(new RevealGeneralCommand(
                0, GeneralSelectionSlot.Primary, game.Revision, play.PromptId)).Accepted,
            "Formal national Zhang Jiao primary reveal failed.");
        Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted &&
                game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
            "Formal national Zhang Jiao reveal did not resume its play phase.");
    }

    private static void AnswerReplacement(GameEngine game, int? cardId)
    {
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Formal national Guidao prompt was lost.");
        var choice = cardId is { } selectedCardId
            ? prompt.Choices.Single(item => item.Cards.SequenceEqual([selectedCardId]))
            : prompt.Choices.Single(item =>
                item.Parameters.GetValueOrDefault("action") == "program-judgment-replace-skip");
        var answered = game.Submit(new AnswerPromptCommand(
            0, prompt.PromptId, choice.Id, game.Revision));
        Require(answered.Accepted,
            answered.Error?.Message ?? "Formal national Guidao answer was rejected.");
    }

    private static void FinishLeiji(GameEngine game)
    {
        for (var step = 0; step < 128; step++)
        {
            if (game.ResolutionStack.All(frame => frame is not
                    (ProgramCardTriggerWindowFrame or JudgmentFrame or ProgramJudgmentTriggerWindowFrame)))
                return;
            Require(game.PendingDecision is null,
                $"Formal national Leiji exposed unexpected {game.PendingDecision?.Kind}.");
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted,
                advanced.Error?.Message ?? "Formal national Leiji did not finish.");
        }
        throw new InvalidOperationException("Formal national Leiji did not finish within the bounded steps.");
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
                    .Count(item => item.SkillId == "national:guidao" && item.Activated) ==
                    (expectReplacement ? 1 : 0),
            "Formal national Leiji and Guidao must replay exactly once.");
    }

    private static string[] EnabledPrograms(GameEngine game)
    {
        var players = ((System.Collections.IEnumerable)typeof(GameEngine)
            .GetField("_players", System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance)!
            .GetValue(game)!).Cast<object>().ToArray();
        var enabled = (System.Collections.IEnumerable)typeof(GameEngine)
            .GetMethod("EnabledSkillPrograms", System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance)!
            .Invoke(game, [players[0]])!;
        return enabled.Cast<SkillProgram>().Select(program => program.Id).ToArray();
    }

    private static ContentRegistry CreateRegistry(bool includeNationalZhangJiao)
    {
        var packages = new List<IGameContentPackage>
        {
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardNationalWarLitePackage()
        };
        if (includeNationalZhangJiao)
            packages.Add(new StandardNationalZhangJiaoPackage());
        return ContentRegistry.Build(packages.ToArray());
    }

    private static ContentRegistry CreateFixtureRegistry() =>
        ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardNationalWarLitePackage(),
            new StandardNationalZhangJiaoPackage(),
            new SyntheticPackage(
                "national-zhang-jiao-program-fixture",
                builder =>
                {
                    var cards = Enumerable.Range(0, 80).SelectMany(_ => new[]
                    {
                        new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 7),
                        new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 2)
                    }).ToArray();
                    builder.AddDeck(new ContentDeckRecipe(
                        "national:zhang-jiao-program:deck",
                        "国战张角配置夹具",
                        4,
                        2,
                        [])
                    {
                        PhysicalCards = cards
                    });
                    builder.AddMode(new ContentModeDefinition(
                        FixtureModeId,
                        "国战张角配置测试",
                        4,
                        4,
                        new Dictionary<string, int>(),
                        "national:zhang-jiao-program:deck",
                        2,
                        [
                            "national:wei-cao-cao",
                            "national:wei-guo-jia",
                            "national:shu-zhang-fei",
                            "national:shu-guan-yu",
                            "national:shu-zhao-yun",
                            "national:shu-liu-bei",
                            ZhangJiaoId,
                            HuaTuoId
                        ],
                        ContentModeKind.NationalWarLite,
                        FactionCounts: new Dictionary<string, int>
                        {
                            ["wei"] = 1,
                            ["shu"] = 2,
                            ["qun"] = 1
                        }));
                },
                new PackageDependency("standard-national-zhang-jiao", new Version(1, 0, 0))));

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
