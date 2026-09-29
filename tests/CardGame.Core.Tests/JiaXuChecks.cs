using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class JiaXuChecks
{
    private const string General = "classic:jia-xu";
    private const string Wansha = "classic:wansha";
    private const string Luanwu = "classic:luanwu";
    private const string Weimu = "classic:weimu";
    private const string WanshaMode = "identity:classic-jia-xu-wansha-check-5";
    private const string LuanwuMode = "identity:classic-jia-xu-luanwu-check-5";
    private const string WeimuMode = "identity:classic-jia-xu-weimu-check-5";

    public static void DefinitionAndPolicySchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 3, FactionId: "qun" } general &&
                general.SkillIds.SequenceEqual([Wansha, Luanwu, Weimu]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "2011 Jia Xu must be in the current Qun roster with three HP.");

        // The batch shared primitives are pinned the same way Dong Zhuo pinned
        // theirs: the numeric values are the cross-batch merge contract.
        Require((int)SkillProgramTargetKind.OtherLivingNearest == 28,
            "The otherLivingNearest target kind must stay 28.");
        Require((int)SkillProgramCardPolicyKind.ProhibitDyingPeachByOthers == 18,
            "prohibitDyingPeachByOthers must stay card policy 18.");

        var wansha = current.Skills[Wansha].Program!;
        var rescue = wansha.CardPolicies.Single();
        Require(rescue.Id == "wansha-no-rescue-peach" &&
                rescue.Kind == SkillProgramCardPolicyKind.ProhibitDyingPeachByOthers &&
                rescue.CardKinds.SequenceEqual([CardKind.Peach]) &&
                rescue.RequiredCardKinds.Count == 0 && rescue.Value == 0 &&
                rescue.CardCategories.Count == 0 && rescue.Suits.Count == 0 &&
                rescue.Condition.Kind == SkillProgramConditionKind.Always,
            "Wansha must be a pure dying-peach prohibition for others.");
        Require(wansha.Activations.Count == 0 && wansha.Triggers.Count == 0 &&
                wansha.ViewAs.Count == 0,
            "Wansha must stay a pure card-policy skill.");

        var luanwu = current.Skills[Luanwu].Program!;
        var activation = luanwu.Activations.Single();
        Require(activation.Id == "luanwu" &&
                activation.MinCards == 0 && activation.MaxCards == 0 &&
                activation.SourceZones.SequenceEqual([CardZoneKind.Hand]) &&
                activation.MinTargets == 0 && activation.MaxTargets == 0 &&
                activation.TargetKind == SkillProgramTargetKind.OtherLiving &&
                activation.UsesPerTurn is null && activation.UsesPerPhase is null &&
                activation.UsesPerGame == 1 &&
                activation.Effects.Select(item => item.Op).SequenceEqual(
                    [SkillProgramEffectOp.RequestNearestSlash]),
            "Luanwu must be a once-per-game targetless active skill.");
        var request = activation.Effects[0];
        Require(request.Target == SkillProgramEffectTarget.Owner &&
                request.Condition.Kind == SkillProgramConditionKind.Always,
            "The Luanwu nearest-slash request must be an unconditional owner-driven loop.");

        var weimu = current.Skills[Weimu].Program!;
        var robe = weimu.CardPolicies.Single();
        Require(robe.Id == "weimu-black-trick-target" &&
                robe.Kind == SkillProgramCardPolicyKind.ProhibitTarget &&
                robe.CardKinds.Count == 0 &&
                robe.CardCategories.SequenceEqual([SkillProgramCardCategory.Trick]) &&
                robe.Suits.SequenceEqual([Suit.Spade, Suit.Club]) &&
                robe.Condition.Kind == SkillProgramConditionKind.Always,
            "Weimu must prohibit black trick targets through the suit and category filters.");
        Require(weimu.Activations.Count == 0 && weimu.Triggers.Count == 0 &&
                weimu.ViewAs.Count == 0,
            "Weimu must stay a pure card-policy skill.");

        Require(current.Skills[Wansha].Name == "完杀" &&
                current.Skills[Wansha].Description ==
                    "锁定技，不处于濒死状态的其他角色于你的回合内不能使用【桃】。" &&
                current.Skills[Luanwu].Name == "乱武" &&
                current.Skills[Luanwu].Description ==
                    "限定技，出牌阶段，你可以选择所有其他角色，这些角色各需对距离最近的另一名角色使用一张【杀】，否则失去1点体力。" &&
                current.Skills[Weimu].Name == "帷幕" &&
                current.Skills[Weimu].Description ==
                    "锁定技，你不能被选择为黑色锦囊牌的目标。",
            "Jia Xu must carry the official skill names and texts verbatim.");

        const string policyTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:policies","revision":1,"minimumRulesVersion":189,
            "cardPolicies":[__POLICY__]}]}
            """;
        const string policyPresentation = """
            {"schemaVersion":3,"skills":{"fixture:policies":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(policyTemplate.Replace("__POLICY__",
                """{"id":"robe","kind":"prohibitTarget","cardCategories":["trick"],"suits":["spade","club"]}"""),
                policyPresentation).Programs["fixture:policies"].CardPolicies.Count == 1,
            "A target prohibition with category and suit filters must load.");
        Reject(policyTemplate.Replace("__POLICY__",
                """{"id":"robe","kind":"prohibitTarget"}"""),
            policyPresentation, "a target prohibition must filter by kinds or categories");
        Reject(policyTemplate.Replace("__POLICY__",
                """{"id":"count","kind":"minimumResponseCount","cardKinds":["slash"],"requiredCardKinds":["dodge"],"value":2,"cardCategories":["trick"]}"""),
            policyPresentation, "category filters require a target prohibition policy");
        Reject(policyTemplate.Replace("__POLICY__",
                """{"id":"count","kind":"minimumResponseCount","cardKinds":["slash"],"requiredCardKinds":["dodge"],"value":2,"suits":["spade"]}"""),
            policyPresentation, "suit filters require a target prohibition policy");
        Reject(policyTemplate.Replace("__POLICY__",
                """{"id":"rescue","kind":"prohibitDyingPeachByOthers","cardKinds":["peach"],"requiredCardKinds":["peach"]}"""),
            policyPresentation, "a dying-peach prohibition must stay a plain peach filter");
        Reject(policyTemplate.Replace("__POLICY__",
                """{"id":"rescue","kind":"prohibitDyingPeachByOthers","cardKinds":["dodge"]}"""),
            policyPresentation, "a dying-peach prohibition requires the peach kind");
    }

    public static void WanshaSparesTheVictimAndBlocksOthersAndReplays()
    {
        var registry = Registry(WanshaMode, WanshaDeck());
        var completed = 0;
        for (var seed = 1; seed <= 400 && completed < 1; seed++)
        {
            var game = Start(registry, seed, WanshaMode);
            if (!DriveToFirstPlay(game)) continue;
            var players = game.CreateSnapshot(0, true).Players;
            var slash = players[0].Hand.FirstOrDefault(card => IsSlash(card.Kind));
            var ownerPeach = players[0].Hand.FirstOrDefault(card => card.Kind == CardKind.Peach);
            var victimPeach = players[1].Hand.FirstOrDefault(card => card.Kind == CardKind.Peach);
            var bystanderPeach = players[2].Hand.FirstOrDefault(card => card.Kind == CardKind.Peach);
            if (slash is null || ownerPeach is null || victimPeach is null ||
                bystanderPeach is null || !players[1].IsAlive)
            {
                continue;
            }
            // The engine never offers ally targeting, so the victim seat must
            // not be the lord's loyalist for the fixture slash to be playable.
            if (!game.GetHumanLegalActions().Any(action =>
                    action.Kind == LegalActionKind.Slash && action.CardId == slash.Id &&
                    Targets(action).Contains(1)))
            {
                continue;
            }

            // The turn owner is a non-victim responder too: their peach must be
            // blocked, so no rescue prompt may ever offer it to the human seat.
            var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            var played = game.Submit(new PlayCardCommand(
                0, slash.Id, [1], game.Revision, game.PendingDecision!.PromptId));
            Require(played.Accepted, played.Error?.Message ?? "The Wansha fixture slash was rejected.");
            var replayed = paused.Submit(new PlayCardCommand(
                0, slash.Id, [1], paused.Revision, paused.PendingDecision!.PromptId));
            Require(replayed.Accepted, "The Wansha replay slash was rejected.");
            DriveUntilSettled(game, ownerPeach.Id);
            DriveUntilSettled(paused, ownerPeach.Id);
            if (game.State.Status == EngineStatus.Completed) continue;

            Require(Events(game).SequenceEqual(Events(paused)) &&
                    State(game) == State(paused),
                "The Wansha dying settlement must replay identically from the paused checkpoint.");
            var after = game.CreateSnapshot(0, true).Players;
            Require(after[1].IsAlive && after[1].Hp == 1,
                "The dying victim must rescue itself with its own peach under Wansha.");
            Require(game.CardMovements.Any(movement =>
                    movement.CardId == victimPeach.Id &&
                    movement.From == CardLocation.Hand(1) &&
                    movement.Reason == CardMoveReasons.Use),
                "The self-rescue must spend the victim's real peach.");
            Require(after[2].Hand.Any(card => card.Id == bystanderPeach.Id) &&
                    !game.CardMovements.Any(movement =>
                        movement.CardId == bystanderPeach.Id &&
                        movement.From == CardLocation.Hand(2)),
                "A bystander's peach must stay untouched while Wansha blocks the rescue.");
            Require(after[0].Hand.Any(card => card.Id == ownerPeach.Id),
                "The turn owner's own peach must stay unusable for the dying neighbor.");
            completed++;
        }
        Require(completed == 1, "No seeded setup resolved the Wansha dying settlement.");
    }

    public static void LuanwuForcesNearestSlashesOrLossAndReplays()
    {
        var registry = Registry(LuanwuMode, LuanwuDeck());
        var completed = 0;
        for (var seed = 1; seed <= 400 && completed < 1; seed++)
        {
            var game = Start(registry, seed, LuanwuMode);
            if (!DriveToFirstPlay(game)) continue;
            var offer = game.GetHumanLegalActions().FirstOrDefault(item =>
                item.Kind == LegalActionKind.UseProgramSkill &&
                item.ProgramSkillId == Luanwu &&
                item.ProgramActivationId == "luanwu");
            if (offer is null) continue;
            var before = game.CreateSnapshot(0, true).Players;
            var responderSeats = before.Select(player => player.Seat).Where(seat => seat != 0).ToArray();
            if (responderSeats.Any(seat => !before[seat].IsAlive)) continue;

            var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            if (!ActivateLuanwu(game) || !ActivateLuanwu(paused)) continue;
            DriveUntilSettled(game);
            DriveUntilSettled(paused);
            if (game.State.Status == EngineStatus.Completed) continue;

            Require(Events(game).SequenceEqual(Events(paused)) &&
                    State(game) == State(paused),
                "The Luanwu loop must replay identically from the paused checkpoint.");
            var answers = game.Events.Select(item => item.Payload)
                .OfType<ProgramNearestSlashAnsweredEvent>()
                .Where(item => item.SkillId == Luanwu)
                .ToArray();
            Require(answers.Select(item => item.ResponderSeat).Distinct().Count() ==
                    responderSeats.Length &&
                    responderSeats.All(seat => answers.Any(item => item.ResponderSeat == seat)),
                "Luanwu must ask every other living character exactly once.");
            var after = game.CreateSnapshot(0, true).Players;
            foreach (var answer in answers)
            {
                var seat = answer.ResponderSeat;
                if (answer.TargetSeat is { } target)
                {
                    Require(!answer.LostHp &&
                            GetNearestOthers(game, seat).Contains(target) &&
                            game.Events.Select(item => item.Payload).OfType<CardUsedEvent>()
                                .Any(item => item.SourceSeat == seat && item.TargetSeat == target &&
                                    IsSlash(item.CardKind)),
                        "Each Luanwu answer must slash one of the responder's nearest others.");
                    Require(after[seat].Hp == before[seat].Hp,
                        "A responder that used its Slash must not lose HP.");
                }
                else
                {
                    Require(answer.LostHp && after[seat].Hp == before[seat].Hp - 1,
                        "A responder without a legal Slash must lose exactly one HP.");
                }
            }
            Require(game.Events.Select(item => item.Payload).OfType<ProgramSkillResolvedEvent>()
                    .Any(item => item.SkillId == Luanwu),
                "Luanwu must resolve as a program skill.");
            Require(!game.GetHumanLegalActions().Any(item =>
                    item.Kind == LegalActionKind.UseProgramSkill && item.ProgramSkillId == Luanwu),
                "Luanwu must be spent after its once-per-game activation.");
            completed++;
        }
        Require(completed == 1, "No seeded setup resolved the Luanwu loop.");
    }

    public static void WeimuBlocksOnlyBlackTrickTargets()
    {
        var registry = Registry(WeimuMode, WeimuDeck());
        var completed = 0;
        var sawWeimuAtSeat1 = 0;
        for (var seed = 1; seed <= 800 && completed < 1; seed++)
        {
            var game = Start(registry, seed, WeimuMode);
            if (!DriveToFirstPlay(game)) continue;
            var players = game.CreateSnapshot(0, true).Players;
            var seat1Skills = players[1].Skills?
                .Select(skill => skill.ContentId ?? skill.Name).ToArray() ?? [];
            var seat2Skills = players[2].Skills?
                .Select(skill => skill.ContentId ?? skill.Name).ToArray() ?? [];
            if (seat1Skills.Any(skill => skill.Contains("weimu", StringComparison.Ordinal)))
            {
                sawWeimuAtSeat1++;
            }
            if (!players[1].IsAlive || !players[2].IsAlive ||
                !seat1Skills.Any(skill => skill.Contains("weimu", StringComparison.Ordinal)) ||
                seat2Skills.Any(skill => skill.Contains("weimu", StringComparison.Ordinal)))
            {
                continue;
            }
            var blackDuel = players[0].Hand.FirstOrDefault(card =>
                card.Kind == CardKind.Duel && card.Suit is Suit.Spade or Suit.Club);
            var redDuel = players[0].Hand.FirstOrDefault(card =>
                card.Kind == CardKind.Duel && card.Suit is Suit.Heart or Suit.Diamond);
            var slash = players[0].Hand.FirstOrDefault(card => IsSlash(card.Kind));
            if (blackDuel is null || redDuel is null || slash is null) continue;
            // The engine never offers ally targeting, so every scanned seat must
            // be an enemy of the lord for the targeting checks to be meaningful.
            if (players.Skip(1).Any(player => player.Role == Role.Loyalist)) continue;

            var actions = game.GetHumanLegalActions();
            Require(!actions.Any(action =>
                    action.Kind == LegalActionKind.Duel && action.CardId == blackDuel.Id &&
                    Targets(action).Contains(1)),
                "A black Duel must not target the Weimu owner.");
            Require(actions.Any(action =>
                    action.Kind == LegalActionKind.Duel && action.CardId == redDuel.Id &&
                    Targets(action).Contains(1)),
                "A red Duel must keep targeting the Weimu owner.");
            Require(actions.Any(action =>
                    action.Kind == LegalActionKind.Duel && action.CardId == blackDuel.Id &&
                    Targets(action).Contains(2)),
                "A black Duel must keep targeting a player without Weimu.");
            Require(actions.Any(action =>
                    action.Kind == LegalActionKind.Slash && action.CardId == slash.Id &&
                    Targets(action).Contains(1)),
                "A Slash must keep targeting the Weimu owner.");

            var beforeState = game.SerializeState();
            var beforeEvents = game.Events.Count;
            var beforeCommands = game.AcceptedCommands.Count;
            var rejected = game.Submit(new PlayCardCommand(
                0, blackDuel.Id, [1], game.Revision, game.PendingDecision!.PromptId));
            Require(!rejected.Accepted && rejected.Error?.Code == CommandErrorCode.InvalidTarget,
                "A forged black Duel against the Weimu owner must be rejected at the command boundary.");
            Require(game.SerializeState() == beforeState &&
                    game.Events.Count == beforeEvents &&
                    game.AcceptedCommands.Count == beforeCommands,
                "Rejected Weimu targeting must be atomic.");

            var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            var played = game.Submit(new PlayCardCommand(
                0, redDuel.Id, [1], game.Revision, game.PendingDecision!.PromptId));
            Require(played.Accepted, played.Error?.Message ?? "The red Duel was rejected.");
            var replayed = paused.Submit(new PlayCardCommand(
                0, redDuel.Id, [1], paused.Revision, paused.PendingDecision!.PromptId));
            Require(replayed.Accepted, "The red Duel replay was rejected.");
            DriveUntilSettled(game);
            DriveUntilSettled(paused);
            if (game.State.Status == EngineStatus.Completed) continue;
            Require(Events(game).SequenceEqual(Events(paused)) &&
                    State(game) == State(paused),
                "The red Duel resolution must replay identically from the paused checkpoint.");
            Require(game.Events.Select(item => item.Payload).OfType<CardUsedEvent>()
                    .Any(item => item.SourceSeat == 0 && item.TargetSeat == 1 &&
                        item.CardKind == CardKind.Duel && item.CardId == redDuel.Id),
                "The red Duel must be used against the Weimu owner.");
            completed++;
        }
        Require(completed == 1,
            $"No seeded setup resolved the Weimu target filter (weimu at seat 1 in {sawWeimuAtSeat1} seeds).");
    }

    private static IReadOnlyList<int> Targets(LegalAction action) =>
        action.TargetSeats.Count > 0
            ? action.TargetSeats
            : action.TargetSeat is { } seat ? [seat] : [];

    private static IReadOnlyList<int> GetNearestOthers(GameEngine game, int seat)
    {
        var candidates = game.CreateSnapshot(0, true).Players
            .Where(player => player.IsAlive && player.Seat != seat)
            .Select(player => (Seat: player.Seat, Distance: game.GetCombatDistance(seat, player.Seat)))
            .ToArray();
        var nearest = candidates.Min(item => item.Distance);
        return candidates.Where(item => item.Distance == nearest)
            .Select(item => item.Seat).Order().ToArray();
    }

    private static bool ActivateLuanwu(GameEngine game)
    {
        var prompt = game.PendingDecision;
        if (prompt is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return false;
        var result = game.Submit(new UseProgramSkillCommand(
            0, Luanwu, "luanwu", [], [], game.Revision, prompt.PromptId));
        Require(result.Accepted, result.Error?.Message ?? "Luanwu activation failed.");
        return true;
    }

    private static bool DriveToFirstPlay(GameEngine game)
    {
        DriveUntil(game, () => false, stopAtDecisions: [DecisionKind.PlayCard]);
        return game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
    }

    private static bool IsSlash(CardKind kind) =>
        kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash;

    private static void DriveUntil(
        GameEngine game,
        Func<bool> done,
        string[]? stopAtSkills = null,
        DecisionKind[]? stopAtDecisions = null,
        int budget = 900)
    {
        for (var step = 0; step < budget && !done() && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (prompt.Kind == DecisionKind.ProgramTrigger &&
                prompt.SkillPrompt?.SkillId is { } skillId &&
                stopAtSkills is not null && stopAtSkills.Contains(skillId))
            {
                return;
            }
            if (stopAtDecisions is not null && stopAtDecisions.Contains(prompt.Kind) &&
                prompt.PlayerSeat == 0)
            {
                return;
            }
            if (prompt.PlayerSeat != 0)
            {
                Advance(game);
                continue;
            }
            switch (prompt.Kind)
            {
                case DecisionKind.PlayCard:
                    Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)));
                    continue;
                case DecisionKind.DiscardCards:
                    Accept(game.Submit(new DiscardCardsCommand(0,
                        prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                        prompt.PromptId, game.Revision)));
                    continue;
                default:
                    var choice = prompt.Choices.FirstOrDefault(item =>
                        item.Parameters.GetValueOrDefault("program-action") == "skip") ??
                        prompt.Choices.First();
                    Answer(game, choice);
                    continue;
            }
        }
    }

    private static void DriveUntilSettled(GameEngine game, int? blockedPeachId = null)
    {
        for (var step = 0; step < 900 && game.ResolutionStack.Count > 0 &&
                 game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (blockedPeachId is { } peach && prompt.PlayerSeat == 0 &&
                prompt.Choices.Any(choice => choice.Cards.Contains(peach)))
            {
                throw new InvalidOperationException(
                    "Wansha must not offer the blocked owner peach to the human seat.");
            }
            if (prompt.PlayerSeat != 0)
            {
                Advance(game);
                continue;
            }
            var choice = prompt.Choices.FirstOrDefault(item =>
                item.Parameters.GetValueOrDefault("program-action") == "skip") ??
                prompt.Choices.First();
            Answer(game, choice);
        }
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision!;
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Jia Xu answer failed.");
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Jia Xu fixture did not advance.");
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Jia Xu command failed.");
    }

    private static ContentRegistry Registry(string mode, ContentDeckRecipe deck) => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new JiaXuScenario(mode, deck));

    private static ContentDeckRecipe WanshaDeck() => DeckCore(index => (index % 5) switch
    {
        1 => "standard:dodge",
        2 => "standard:peach",
        3 => "standard:slash",
        _ => "standard:slash"
    });

    private static ContentDeckRecipe LuanwuDeck() => DeckCore(index => (index % 4) switch
    {
        1 => "standard:dodge",
        _ => "standard:slash"
    });

    private static ContentDeckRecipe WeimuDeck() => DeckCore(index => (index % 5) switch
    {
        0 => "standard:duel",
        1 => "standard:slash",
        2 => "standard:dodge",
        3 => "standard:peach",
        _ => "standard:duel"
    });

    private static ContentDeckRecipe DeckCore(Func<int, string> kindFor) =>
        new("fixture:jia-xu-deck", "贾诩测试牌堆", 5, 2, [])
        {
            PhysicalCards = Enumerable.Range(0, 180).Select(index =>
                new ContentDeckPhysicalCard(kindFor(index), (Suit)(index % 4), index % 13 + 1)).ToArray()
        };

    private static GameEngine Start(ContentRegistry registry, int seed, string mode)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            ModeId = mode,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 40
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Jia Xu fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Jia Xu selection failed.");
        return game;
    }

    private static string State(GameEngine game) =>
        JsonSerializer.Serialize(game.CreateSnapshot(0, true));

    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static void Reject(string rules, string presentation, string because = "")
    {
        try
        {
            _ = SkillProgramCatalog.Load(rules, presentation);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException(
            $"Expected invalid Jia Xu composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class JiaXuScenario(string modeId, ContentDeckRecipe deck) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("jia-xu-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 159, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            // Seat 0 is the human lord; the banks carry no skills except the
            // dedicated Weimu bank so each check sees exactly one live skill.
            builder.AddGeneral(new ContentGeneralDefinition("fixture:jia-xu-bank-a", "测试对手一",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:jia-xu-bank-b", "测试对手二",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:jia-xu-bank-c", "测试对手三",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:jia-xu-bank-d", "测试对手四",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:jia-xu-frail-a", "测试濒死一",
                "frail", "standard:none", "qun", BaseHp: 1));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:jia-xu-frail-b", "测试濒死二",
                "frail", "standard:none", "qun", BaseHp: 1));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:jia-xu-frail-c", "测试濒死三",
                "frail", "standard:none", "qun", BaseHp: 1));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:jia-xu-frail-d", "测试濒死四",
                "frail", "standard:none", "qun", BaseHp: 1));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:jia-xu-weimu-bank", "测试帷幕",
                "weimu", "classic:weimu", "qun", BaseHp: 8));
            builder.AddDeck(deck);
            builder.AddMode(new ContentModeDefinition(modeId, "贾诩测试", 5, 5,
                modeId == WeimuMode
                    // The Weimu fixture needs every AI seat to be a legal enemy
                    // target, so the ally seat must not exist in this mode.
                    ? new Dictionary<string, int>
                    {
                        [nameof(Role.Lord)] = 1,
                        [nameof(Role.Rebel)] = 4
                    }
                    : new Dictionary<string, int>
                    {
                        [nameof(Role.Lord)] = 1,
                        [nameof(Role.Loyalist)] = 1,
                        [nameof(Role.Rebel)] = 2,
                        [nameof(Role.Renegade)] = 1
                    },
                deck.Id, GeneralCandidateCount: 5,
                GeneralPoolIds: modeId switch
                {
                    WanshaMode =>
                    [
                        General,
                        "fixture:jia-xu-frail-a",
                        "fixture:jia-xu-frail-b",
                        "fixture:jia-xu-frail-c",
                        "fixture:jia-xu-frail-d"
                    ],
                    WeimuMode =>
                    [
                        General,
                        "fixture:jia-xu-weimu-bank",
                        "fixture:jia-xu-bank-a",
                        "fixture:jia-xu-bank-b",
                        "fixture:jia-xu-bank-c"
                    ],
                    _ =>
                    [
                        General,
                        "fixture:jia-xu-bank-a",
                        "fixture:jia-xu-bank-b",
                        "fixture:jia-xu-bank-c",
                        "fixture:jia-xu-bank-d"
                    ]
                }));
        }
    }
}
