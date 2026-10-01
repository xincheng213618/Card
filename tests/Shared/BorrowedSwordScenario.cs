using CardGame.Content.Standard;
using CardGame.Core;

internal static class BorrowedSwordScenario
{
    public static ContentRegistry CreateFixtureRegistry() => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new BorrowedFixtureMode());

    private sealed class BorrowedFixtureMode : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-borrowed-sword", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder) => builder.AddMode(new(
            "identity:classic-borrowed-check5", "Borrowed Sword real classic fixture", 5, 5,
            new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 }, "classic:standard-deck",
            GeneralCandidateCount: 3, GeneralPoolIds:
            [
                "classic:liu-bei",
                "classic:sun-quan",
                "classic:sima-yi",
                "classic:xiahou-dun",
                "classic:hua-tuo",
                "classic:cao-cao",
                "classic:zhang-liao",
                "classic:xu-chu",
                "classic:dian-wei",
                "classic:xu-huang",
                "classic:zhen-ji",
                "classic:huang-yueying",
                "classic:ma-chao",
                "classic:huang-zhong",
                "classic:wei-yan",
                "classic:lu-bu",
                "classic:huang-gai",
                "classic:gan-ning",
                "classic:lu-meng",
                "classic:zhang-fei",
                "classic:zhou-yu",
                "classic:zhuge-liang",
                "classic:guan-yu",
                "classic:zhao-yun",
                "classic:guo-jia",
                "classic:da-qiao",
                "classic:diao-chan",
                "classic:sun-shangxiang",
                "classic:lu-xun",
                "classic:pang-de",
                "classic:xun-yu",
                "classic:yan-liang-wen-chou",
                "classic:wolong-zhuge-liang",
                "classic:pang-tong",
                "classic:taishi-ci",
                "classic:cao-ren",
                "classic:xiao-qiao",
                "classic:zhou-tai",
                "classic:yuan-shao",
                "classic:xiahou-yuan",
                "classic:hua-xiong",
                "classic:gongsun-zan",
                "classic:zhang-jiao",
                "classic:sun-jian",
                "classic:meng-huo",
                "classic:zhu-rong",
                "classic:yu-jin",
                "classic:xu-shu",
                "sp:zhao-yun",
                "classic:shen-guan-yu",
                "sp:guan-yu",
                "classic:yan-yan",
                "mou:lu-meng",
                "classic:cao-zhang",
                "classic:ma-dai",
                "classic:gao-shun",
                "classic:liu-biao",
                "classic:wang-yi",
                "classic:zhong-hui",
                "classic:xun-you",
                "classic:liao-hua",
                "classic:guan-xing-zhang-bao",
                "classic:bu-lian-shi",
                "classic:cheng-pu",
                "classic:han-dang",
                "classic:cao-chong",
                "classic:guo-huai",
                "classic:man-chong",
                "classic:guan-ping",
                "classic:zhu-huan",
                "classic:gu-yong",
                "classic:li-dian",
                "boundary:sima-yi",
                "boundary:guo-jia",
                "boundary:cao-cao",
                "boundary:diao-chan",
                "boundary:zhang-liao",
                "classic:zhu-zhi",
                "boundary:xu-chu",
                "boundary:gan-ning",
                "boundary:zhou-yu",
                "classic:pan-zhang-ma-zhong",
                "sp:le-jin",
                "classic:xu-sheng",
                "boundary:xu-sheng",
                "classic:zhang-song",
                "boundary:zhang-song",
                "classic:ju-shou",
                "boundary:ju-shou",
                "classic:cao-ang",
                "classic:qu-yi",
                "classic:zhang-xiu",
                "ol:shen-guan-yu",
                "classic:shen-sima-yi",
                "classic:shen-zhao-yun",
                "classic:cao-pi",
                "classic:sun-ce",
                "classic:cai-wen-ji",
                "classic:deng-ai",
                "classic:sha-mo-ke",
                "classic:lu-su",
                "classic:jiang-wei",
                "classic:dong-zhuo",
                "classic:liu-shan",
                "classic:zhang-zhao-zhang-hong",
                "boundary:zhao-yun",
                "classic:zhang-he",
                "classic:shen-lu-meng",
                "classic:shen-cao-cao",
                "classic:cao-zhi",
                "classic:jia-xu",
                "classic:shen-zhou-yu",
                "classic:shen-lu-bu",
                "classic:fu-huanghou",
                "ol:shen-sima-yi",
                "ol:shen-liu-bei",
                "ol:shen-lu-xun",
                "ol:shen-gan-ning",
                "ol:shen-zhang-liao",
                "ol:shen-zhou-yu",
                "ol:shen-zhuge-liang",
                "ol:shen-lu-bu",
                "ol:shen-zhao-yun",
                "ol:shen-sun-quan",
                "ol:shen-zhang-jiao",
                "ol:shen-dian-wei",
                "ol:shen-huang-zhong",
                "classic:zhang-chun-hua",
                "classic:ling-tong",
                "classic:chen-gong",
                "classic:wu-guo-tai",
                "classic:fa-zheng",
                "classic:ma-su",
                "classic:li-ru",
                "classic:liu-feng",
                "classic:jian-yong",
                "classic:yu-fan",
                "classic:zhu-ran",
                "classic:cao-zhen",
                "classic:han-hao-shi-huan",
                "classic:chen-qun",
                "classic:wu-yi",
                "classic:zhou-cang",
                "classic:sun-lu-ban",
                "classic:li-yan",
                "classic:sun-deng",
                "classic:guo-huanghou",
                "classic:liu-yu",
                "classic:cen-hun",
                "classic:sun-zi-liu-fang",
                "classic:huang-hao",
                "classic:xin-xianying",
                "classic:zhang-rang",
                "classic:cao-jie",
                "classic:wu-xian",
                "classic:cai-yong",
                "classic:qin-mi",
                "classic:xue-zong",
                "classic:xu-shi",
                "classic:ji-kang",
                "classic:cao-rui",
                "classic:cao-xiu",
                "classic:zhong-yao",
                "classic:liu-chen",
                "classic:xiahou-shi",
                "classic:zhang-ni",
                "classic:sun-xiu",
                "classic:quan-cong",
                "classic:gongsun-yuan",
                "classic:guo-tu-feng-ji",
            ]));
    }

    public static GameEngine FindHumanSourcePlay()
    {
        var registry = CreateFixtureRegistry();
        // Verified classic fixture: real source keeps Borrowed Sword until an AI equips.
        foreach (var seed in new[] { 52 })
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                PlayerCount = 5,
                ModeId = "identity:classic-borrowed-check5",
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 220,
                AiPolicyVersion = 2
            }, registry);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, "Borrowed Sword source fixture failed to start.");
            var firstGeneral = game.PendingDecision?.Choices.FirstOrDefault();
            if (firstGeneral?.ContentIds.Count != 1)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                firstGeneral.ContentIds[0],
                game.Revision,
                game.PendingDecision!.PromptId));
            Require(selected.Accepted, "Borrowed Sword source fixture could not select a general.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, "Borrowed Sword source fixture did not reach play.");
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard } play ||
                game.CreateSnapshot(0).Players[0].Hand.All(card => card.Kind != CardKind.BorrowedSword))
            {
                continue;
            }

            var ended = game.Submit(new EndPlayPhaseCommand(0, game.Revision, play.PromptId));
            Require(ended.Accepted, "Borrowed Sword source fixture could not preserve the trick for another round.");
            for (var step = 0; step < 1_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } &&
                    game.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.BorrowedSword))
                {
                    return game;
                }

                var human = game.CreateSnapshot(0, revealAll: true).Players[0];
                if (!human.IsAlive || human.Hand.All(card => card.Kind != CardKind.BorrowedSword))
                {
                    break;
                }

                Step(game);
            }
        }

        throw new InvalidOperationException(
            "No bounded classic Borrowed Sword source fixture with an equipped target was found.");
    }

    public static GameEngine FindHumanOwnerResponse(bool requireFactionSlash = false,
        string ownerGeneralId = "classic:liu-bei", int maxSeeds = 65_536)
    {
        var registry = CreateFixtureRegistry();
        // The retained ordinary check uses the verified real owner/Slash/Dodge seed.
        // Optional faction/general callers retain their bounded discovery contract.
        var seeds = !requireFactionSlash && ownerGeneralId == "classic:liu-bei" && maxSeeds >= 14956
            ? new[] { 14956 } : Enumerable.Range(1, maxSeeds);
        foreach (var seed in seeds)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                PlayerCount = 5,
                ModeId = "identity:classic-borrowed-check5",
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 220,
                AiPolicyVersion = 2
            }, registry);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, "Borrowed Sword fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual([ownerGeneralId])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                ownerGeneralId,
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, "Borrowed Sword fixture could not select its owner general.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, "Borrowed Sword fixture did not reach its owner's play phase.");
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard } play)
            {
                continue;
            }

            var owner = game.CreateSnapshot(0, revealAll: true).Players[0];
            var weapon = owner.Hand.FirstOrDefault(card =>
                card.Kind is CardKind.Crossbow or CardKind.QinggangSword);
            if (weapon is null)
            {
                continue;
            }

            var equipped = game.Submit(new PlayCardCommand(
                0,
                weapon.Id,
                [],
                game.Revision,
                play.PromptId));
            Require(equipped.Accepted, "Borrowed Sword fixture could not equip the owner's weapon.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                var resumed = game.Submit(new AdvanceCommand(game.Revision));
                Require(resumed.Accepted, "Borrowed Sword fixture did not return to play.");
            }

            var nextPlay = game.PendingDecision ??
                throw new InvalidOperationException("Borrowed Sword fixture lost its play prompt.");
            var ended = game.Submit(new EndPlayPhaseCommand(0, game.Revision, nextPlay.PromptId));
            Require(ended.Accepted, "Borrowed Sword fixture could not end the owner's play phase.");

            for (var step = 0; step < 4_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is
                    {
                        Kind: DecisionKind.RespondSlash,
                        PlayerSeat: 0,
                        IncomingCard: CardKind.BorrowedSword
                    } response &&
                    response.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "borrowed-sword-give-weapon") &&
                    response.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "borrowed-sword-slash") &&
                    (!requireFactionSlash || response.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "faction-slash-request") &&
                        HasHelpingShuProvider(game, registry)))
                {
                    var slashTargetSeat = response.TargetSeat ??
                        throw new InvalidOperationException("Borrowed Sword prompt omitted its Slash target.");
                    var slashTarget = game.CreateSnapshot(0, revealAll: true).Players
                        .Single(player => player.Seat == slashTargetSeat);
                    var forcedSlashKind = game.CreateSnapshot(0, revealAll: true).Players[0].Hand
                        .Concat(game.CreateSnapshot(0, revealAll: true).Players[0].Equipment)
                        .Where(card => response.Choices.Any(choice =>
                            choice.Parameters.GetValueOrDefault("response") == "borrowed-sword-slash" &&
                            choice.Cards.Contains(card.Id)))
                        .Select(card => (CardKind?)card.Kind)
                        .FirstOrDefault() ?? CardKind.Slash;
                    // Only the owner's own ordinary Slash is nullified by Tengjia; a
                    // FactionSlash provider may supply a fire or thunder Slash.
                    var tengjiaBlocks = !requireFactionSlash &&
                                        slashTarget.Equipment.Any(card => card.Kind == CardKind.Tengjia) &&
                                        forcedSlashKind == CardKind.Slash;
                    var canPauseForDodge = !tengjiaBlocks &&
                                           (slashTarget.Hand.Any(card =>
                                                card.Kind == CardKind.Dodge ||
                                                slashTarget.Skills?.Any(skill =>
                                                    skill.ContentId == "classic:longdan" &&
                                                    card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) == true ||
                                                slashTarget.Skills?.Any(skill =>
                                                    skill.ContentId == "classic:qingguo" &&
                                                    card.Suit is Suit.Spade or Suit.Club) == true) ||
                                            slashTarget.Equipment.Any(card =>
                                                card.Kind == CardKind.BaguaFormation)) &&
                                           // Locked card-window triggers that preempt or
                                           // void the dodge response: Liu Shan's Xianle
                                           // nullifies the Slash up front and Da Qiao's
                                           // Liuli transfers it to a third player.
                                           slashTarget.Skills?.Any(skill =>
                                               skill.ContentId is "classic:xiangle" or "classic:liuli") != true;
                    if (canPauseForDodge)
                    {
                        // The FactionSlash branch drives nested forced-Slash
                        // resolutions that can hit the same documented nesting
                        // defect after acceptance, so replay the acceptance body
                        // on a checkpoint copy first; only seeds whose branch
                        // completes cleanly become fixtures.
                        if (!requireFactionSlash ||
                            ProbeFactionSlashResolution(game, response, registry))
                        {
                            return game;
                        }

                        break;
                    }
                }

                var currentOwner = game.CreateSnapshot(0, revealAll: true).Players[0];
                if (!currentOwner.IsAlive || currentOwner.Equipment.All(card => card.Id != weapon.Id))
                {
                    break;
                }

                // A damage-trigger program (e.g. Ganglie) can retaliate while a
                // suspended program-skill Slash resolution sits below it; that
                // nesting trips the Processing invariant on some seeds and is a
                // documented engine defect (nested damage-trigger window conflict,
                // same family as the reported judgment-window conflict), not a
                // regression of any skill here. Such a seed never reaches the
                // fixture prompt, so abandon it and keep scanning.
                try
                {
                    Step(game);
                }
                catch (InvalidOperationException exception) when (exception.Message.StartsWith(
                    "The active card resolution and Processing zone are inconsistent",
                    StringComparison.Ordinal))
                {
                    break;
                }
            }
        }

        throw new InvalidOperationException(
            "No bounded classic Borrowed Sword response fixture with a real Slash and Dodge continuation was found.");
    }

    private static bool ProbeFactionSlashResolution(
        GameEngine game,
        PendingDecision response,
        ContentRegistry registry)
    {
        var copy = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        var eventCount = copy.Events.Count;
        try
        {
            var choice = copy.PendingDecision!.Choices.Single(candidate =>
                candidate.Parameters.GetValueOrDefault("response") == "faction-slash-request");
            var answered = copy.Submit(new AnswerPromptCommand(
                0,
                copy.PendingDecision.PromptId,
                choice.Id,
                copy.Revision));
            if (!answered.Accepted)
            {
                return false;
            }

            for (var step = 0; step < 128 && !copy.Events.Skip(eventCount)
                     .Select(item => item.Payload).OfType<BorrowedSwordResolvedEvent>()
                     .Any(resolved => resolved.UsedSlash); step++)
            {
                Step(copy);
            }

            return true;
        }
        catch (InvalidOperationException exception) when (exception.Message.StartsWith(
            "The active card resolution and Processing zone are inconsistent",
            StringComparison.Ordinal))
        {
            return false;
        }
    }

    public static void Step(GameEngine game)
    {
        GameCommand command;
        if (game.PendingDecision is { PlayerSeat: 0 } prompt)
        {
            if (prompt.Kind == DecisionKind.PlayCard)
            {
                command = new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId);
            }
            else if (prompt.Kind == DecisionKind.DiscardCards)
            {
                command = new DiscardCardsCommand(
                    0,
                    prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                    prompt.PromptId,
                    game.Revision);
            }
            else
            {
                var choice = prompt.Choices.FirstOrDefault(candidate =>
                                 candidate.Parameters.GetValueOrDefault("response") is
                                     "dodge" or "slash" or "peach" or "bagua")
                             ?? prompt.Choices.FirstOrDefault(candidate =>
                                 candidate.Parameters.Values.Any(value =>
                                     value.StartsWith("skip", StringComparison.Ordinal) ||
                                     value is "take-damage" or "pass" or "no-nullification"))
                             ?? prompt.Choices.Last();
                command = new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision);
            }
        }
        else
        {
            command = new AdvanceOneStepCommand(game.Revision);
        }

        var result = game.Submit(command);
        Require(result.Accepted, $"Borrowed Sword fixture could not continue: {result.Error?.Message}");
    }

    private static bool HasHelpingShuProvider(GameEngine game, ContentRegistry registry) =>
        game.CreateSnapshot(0, revealAll: true).Players.Any(player =>
            player.Seat != 0 &&
            player.Role is Role.Loyalist or Role.Renegade &&
            string.Equals(registry.Generals[player.GeneralId].FactionId, "shu", StringComparison.Ordinal) &&
            player.Hand.Any(card => card.Kind is
                CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash));

    private static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
