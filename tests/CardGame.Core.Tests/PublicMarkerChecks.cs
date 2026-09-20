using CardGame.Content.Standard;
using CardGame.Core;

internal static class PublicMarkerChecks
{
    private const string ModeId = "identity:classic-wuhun-marker-2";
    private const string DeckId = "wuhun-marker:deck";
    private const string AttackerId = "wuhun-marker:attacker";
    private const string OwnerId = "wuhun-marker:owner";
    private const string BystanderOneId = "wuhun-marker:bystander-1";
    private const string BystanderTwoId = "wuhun-marker:bystander-2";

    public static void WuhunDamageOrderAndReplay()
    {
        Require(GameCheckpoint.CurrentRulesVersion >= 91,
            "Source-attributed public markers require rules version 91 or newer.");
        var registry = CreateRegistry(ownerHp: 3, packageId: "wuhun-marker-nonlethal");
        var current = FindFixture(registry, requireAlcohol: true);
        var boundary = RoundTrip(current.CreateCheckpoint());
        var legacy = GameReplay.Restore(boundary with { RulesVersion = 89 }, registry);

        PlayAlcoholAndSlash(current, useAlcohol: true);
        PlayAlcoholAndSlash(legacy, useAlcohol: true);

        var markerEvents = current.Events.Select(item => item.Payload)
            .OfType<PlayerMarkerChangedEvent>()
            .ToArray();
        Require(markerEvents.Length == 2 &&
                markerEvents.Select(item => item.Delta).SequenceEqual([1, 1]) &&
                markerEvents.Select(item => item.Count).SequenceEqual([1, 2]) &&
                markerEvents.All(item =>
                    item.PlayerSeat == 0 &&
                    item.Marker == PlayerMarkerKind.Nightmare &&
                    item.SkillOwnerSeat == 1 &&
                    item.Reason == "skill.wuhun.damage"),
            "Two actual damage points must commit two ordered public Nightmare increments.");
        var publicMarker = current.CreateSnapshot(1).Players[0].Markers?.Single();
        Require(publicMarker is
        {
            Kind: PlayerMarkerKind.Nightmare,
            Name: "梦魇",
            Count: 2
        },
            "An ordinary observer did not receive the exact public Nightmare counter.");
        Require(!legacy.Events.Select(item => item.Payload).OfType<PlayerMarkerChangedEvent>().Any() &&
                legacy.CreateSnapshot(1).Players[0].Markers is null,
            "Rules v89 did not preserve the pre-marker event and snapshot boundary.");
        AssertReplay(current, registry, expectedCount: 2);
        AssertReplay(legacy, registry, expectedCount: 0);

        var lethalRegistry = CreateRegistry(ownerHp: 1, packageId: "wuhun-marker-lethal");
        var lethal = FindFixture(lethalRegistry, requireAlcohol: false);
        PlayAlcoholAndSlash(lethal, useAlcohol: false);
        var events = lethal.Events.Select(item => item.Payload).ToArray();
        var appliedIndex = Array.FindIndex(events, item => item is DamageAppliedEvent damage &&
            damage.SourceSeat == 0 && damage.TargetSeat == 1);
        var markerIndex = Array.FindIndex(events, item => item is PlayerMarkerChangedEvent marker &&
            marker.PlayerSeat == 0 && marker.SkillOwnerSeat == 1);
        var dyingIndex = Array.FindIndex(events, item => item is PlayerDyingEvent dying && dying.VictimSeat == 1);
        Require(appliedIndex >= 0 && markerIndex > appliedIndex && dyingIndex > markerIndex &&
                lethal.CreateSnapshot(0, revealAll: true).Players[0].Markers?.Single().Count == 1,
            "Lethal damage must add Nightmare after damage is applied and before dying begins.");
        AssertReplay(lethal, lethalRegistry, expectedCount: 1);
    }

    public static void WuhunCandidateRules()
    {
        Require(GameRules.WuhunJudgmentCausesDeath(CardKind.Slash) &&
                !GameRules.WuhunJudgmentCausesDeath(CardKind.Peach) &&
                !GameRules.WuhunJudgmentCausesDeath(CardKind.PeachGarden) &&
                !GameRules.WuhunJudgmentCausesDeath(null),
            "Wuhun direct death must exempt only Peach, Peach Garden and an exhausted judgment.");
        var tiedCandidates = GameRules.GetMaximumMarkerCandidates(
        [
            new PlayerMarkerCandidateState(3, IsAlive: true, Count: 2),
            new PlayerMarkerCandidateState(1, IsAlive: true, Count: 2),
            new PlayerMarkerCandidateState(2, IsAlive: false, Count: 5),
            new PlayerMarkerCandidateState(0, IsAlive: true, Count: 0)
        ]);
        Require(tiedCandidates.SequenceEqual([1, 3]) &&
                GameRules.GetMaximumMarkerCandidates(
                [
                    new PlayerMarkerCandidateState(0, IsAlive: true, Count: 0),
                    new PlayerMarkerCandidateState(1, IsAlive: false, Count: 4)
                ]).Count == 0,
            "Maximum marker candidates must keep every living positive tie and reject zero/dead entries.");
    }

    public static void WuhunDeathJudgmentAndReplay()
    {
        Require(GameCheckpoint.CurrentRulesVersion >= 92,
            "Wuhun death target selection requires rules version 92 or newer.");
        var registry = CreateRegistry(
            ownerHp: 1,
            packageId: "wuhun-death-direct",
            deckCards: [new ContentDeckCardCount("standard:slash", 60)]);
        var current = FindFixture(registry, requireAlcohol: false);
        var boundary = RoundTrip(current.CreateCheckpoint());
        var legacy = GameReplay.Restore(boundary with { RulesVersion = 91 }, registry);

        PlayAlcoholAndSlash(current, useAlcohol: false);
        PlayAlcoholAndSlash(legacy, useAlcohol: false);

        var events = current.Events.Select(item => item.Payload).ToArray();
        var ownerDeath = Array.FindIndex(events, item => item is PlayerDiedEvent died && died.VictimSeat == 1);
        var skillStarted = Array.FindIndex(events, item => item is DeathSkillStartedEvent started &&
            started.OwnerSeat == 1 && started.CandidateSeats.SequenceEqual([0]));
        var targetSelected = Array.FindIndex(events, item => item is DeathSkillTargetSelectedEvent selected &&
            selected.OwnerSeat == 1 && selected.TargetSeat == 0);
        var judgment = Array.FindIndex(events, item => item is JudgmentResolvedEvent resolved &&
            resolved.Reason == JudgmentReasons.Wuhun && resolved.TargetSeat == 0 && resolved.Succeeded);
        var directDeath = Array.FindIndex(events, item => item is DirectDeathDeclaredEvent direct &&
            direct.SourceSeat == 1 && direct.TargetSeat == 0 && direct.Skill == SkillKind.Wuhun);
        var targetDeath = Array.FindIndex(events, item => item is PlayerDiedEvent died &&
            died.VictimSeat == 0 && died.KillerSeat is null);
        var targetDying = events.OfType<PlayerDyingEvent>().Any(item => item.VictimSeat == 0);
        var cleared = events.OfType<PlayerMarkerChangedEvent>().SingleOrDefault(item =>
            item.PlayerSeat == 0 && item.SkillOwnerSeat == 1 && item.Delta == -1);
        Require(ownerDeath >= 0 && skillStarted > ownerDeath && targetSelected > skillStarted &&
                judgment > targetSelected && directDeath > judgment && targetDeath > directDeath &&
                !targetDying && cleared is { Count: 0, Reason: "skill.wuhun.death-clear" } &&
                current.State.Status == EngineStatus.Completed &&
                !current.CreateSnapshot(0, revealAll: true).Players[0].IsAlive,
            "Wuhun must select the positive maximum after owner death, judge, then directly kill without dying rescue.");

        Require(!legacy.Events.Select(item => item.Payload).OfType<DeathSkillStartedEvent>().Any() &&
                !legacy.Events.Select(item => item.Payload).OfType<DirectDeathDeclaredEvent>().Any() &&
                legacy.CreateSnapshot(0, revealAll: true).Players[0].IsAlive &&
                legacy.CreateSnapshot(0, revealAll: true).Players[0].Markers?.Single().Count == 1,
            "Rules v91 must preserve source attribution without activating or clearing the death skill.");

        AssertReplayStateAndDeathEvents(current, registry);
        AssertReplayStateAndDeathEvents(legacy, registry);
    }

    public static void WuhunHumanTargetPromptAndReplay()
    {
        var registry = CreateRegistry(
            ownerHp: 1,
            packageId: "wuhun-death-human-target",
            deckCards: [new ContentDeckCardCount("standard:slash", 60)]);
        GameEngine? selected = null;
        for (var seed = 1; seed <= 4_096 && selected is null; seed++)
        {
            var game = CreateGame(registry, seed, humanSeat: 1, humanRole: Role.Rebel);
            if (!game.Submit(new StartGameCommand()).Accepted)
                continue;
            var full = game.CreateSnapshot(1, revealAll: true);
            if (full.Players[0].GeneralId != AttackerId ||
                full.Players[1].GeneralId != OwnerId)
                continue;

            for (var step = 0; step < 128; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.WuhunTarget, PlayerSeat: 1 })
                {
                    selected = game;
                    break;
                }

                if (game.State.Status == EngineStatus.Completed ||
                    game.State is { Status: EngineStatus.AwaitingHumanPlay, CurrentSeat: 1 })
                    break;

                GameCommand command = game.PendingDecision is { PlayerSeat: 1 } pendingPrompt
                    ? new AnswerPromptCommand(
                        1,
                        pendingPrompt.PromptId,
                        pendingPrompt.Choices.First(choice => choice.Cards.Count == 0).Id,
                        game.Revision)
                    : new AdvanceOneStepCommand(game.Revision);
                if (!game.Submit(command).Accepted)
                    break;
            }
        }

        var current = selected ??
            throw new InvalidOperationException("Could not find a bounded human Wuhun target fixture.");
        var prompt = current.PendingDecision!;
        Require(prompt.IsPrivate && prompt.ValidTargetSeats.SequenceEqual([0]) &&
                current.CreateSnapshot(0).PendingDecision is null &&
                current.CreateSnapshot(1).PendingDecision?.Kind == DecisionKind.WuhunTarget &&
                current.ResolutionStack.TakeLast(2).Select(frame => frame.Kind)
                    .SequenceEqual([ResolutionFrameKind.Death, ResolutionFrameKind.DeathSkill]),
            "A dead human Wuhun owner must privately choose from the frozen maximum-marker candidates.");

        var restored = GameReplay.Restore(RoundTrip(current.CreateCheckpoint()), registry);
        var beforeInvalid = current.SerializeState();
        var invalid = current.Submit(new AnswerPromptCommand(
            1,
            prompt.PromptId,
            new ChoiceId("wuhun.target-forged"),
            current.Revision));
        Require(!invalid.Accepted && invalid.Error?.Code == CommandErrorCode.InvalidChoice &&
                current.SerializeState() == beforeInvalid,
            "A forged Wuhun target must be rejected atomically.");

        Require(current.Submit(new AnswerPromptCommand(
                    1,
                    prompt.PromptId,
                    prompt.Choices.Single().Id,
                    current.Revision)).Accepted,
            "The frozen human Wuhun target was rejected.");
        var restoredPrompt = restored.PendingDecision!;
        Require(restored.Submit(new AnswerPromptCommand(
                    1,
                    restoredPrompt.PromptId,
                    restoredPrompt.Choices.Single().Id,
                    restored.Revision)).Accepted &&
                SnapshotJson.Serialize(restored.CreateSnapshot(1, revealAll: true)) ==
                SnapshotJson.Serialize(current.CreateSnapshot(1, revealAll: true)) &&
                restored.Events.Select(item => item.Payload.GetType().Name)
                    .SequenceEqual(current.Events.Select(item => item.Payload.GetType().Name)),
            "A paused human Wuhun target choice did not restore and replay exactly.");
    }

    public static void WuhunSkipsAfterGameEnd()
    {
        var registry = CreateRegistry(
            ownerHp: 1,
            packageId: "wuhun-death-game-end",
            deckCards: [new ContentDeckCardCount("standard:slash", 60)],
            roleCounts: new Dictionary<string, int>
            {
                [nameof(Role.Lord)] = 1,
                [nameof(Role.Loyalist)] = 2,
                [nameof(Role.Rebel)] = 1
            });
        var game = FindFixture(
            registry,
            requireAlcohol: false,
            requiredOwnerRole: Role.Rebel);

        PlayAlcoholAndSlash(game, useAlcohol: false);

        var events = game.Events.Select(item => item.Payload).ToArray();
        Require(game.State is { Status: EngineStatus.Completed, Winner: Winner.LordAndLoyalists } &&
                events.OfType<PlayerDiedEvent>().Any(item => item.VictimSeat == 1) &&
                !events.OfType<DeathSkillStartedEvent>().Any() &&
                !events.OfType<DirectDeathDeclaredEvent>().Any() &&
                events.OfType<PlayerMarkerChangedEvent>().Any(item =>
                    item.PlayerSeat == 0 && item.SkillOwnerSeat == 1 && item.Delta == -1) &&
                game.CreateSnapshot(0, revealAll: true).Players[0].IsAlive,
            "Wuhun must not start after its owner's death has already determined the winner.");
        AssertReplayStateAndDeathEvents(game, registry);
    }

    public static void WuhunJudgmentCanBeReplacedWithPeach()
    {
        var registry = CreateRegistry(
            ownerHp: 1,
            packageId: "wuhun-death-guicai",
            deckCards:
            [
                new ContentDeckCardCount("standard:slash", 58),
                new ContentDeckCardCount("standard:peach", 2)
            ],
            attackerSkill: SkillKind.Guicai);
        GameEngine? selected = null;
        for (var seed = 1; seed <= 8_192 && selected is null; seed++)
        {
            var game = CreateGame(registry, seed, humanSeat: 0, humanRole: Role.Lord);
            if (!game.Submit(new StartGameCommand()).Accepted || !AdvanceToHumanPlay(game))
                continue;
            var full = game.CreateSnapshot(0, revealAll: true);
            if (full.Players[0].GeneralId != AttackerId ||
                full.Players[1].GeneralId != OwnerId ||
                game.GetHumanLegalActions().All(action =>
                    action.Kind != LegalActionKind.Slash || action.TargetSeat != 1) ||
                !full.Players[0].Hand.Any(card => card.Kind == CardKind.Peach) ||
                full.Players.Skip(1).Any(player => player.Hand.Any(card => card.Kind == CardKind.Peach)))
                continue;

            var slash = game.GetHumanLegalActions().First(action =>
                action.Kind == LegalActionKind.Slash && action.TargetSeat == 1);
            if (!Play(game, slash).Accepted)
                continue;
            for (var step = 0; step < 128; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.Guicai, PlayerSeat: 0 })
                {
                    selected = game;
                    break;
                }

                GameCommand command = game.PendingDecision is { PlayerSeat: 0 } pendingPrompt
                    ? new AnswerPromptCommand(
                        0,
                        pendingPrompt.PromptId,
                        pendingPrompt.Choices.First(choice => choice.Cards.Count == 0).Id,
                        game.Revision)
                    : new AdvanceOneStepCommand(game.Revision);
                if (!game.Submit(command).Accepted || game.State.Status == EngineStatus.Completed)
                    break;
            }
        }

        var current = selected ??
            throw new InvalidOperationException("Could not find a bounded Wuhun Guicai fixture.");
        var prompt = current.PendingDecision!;
        var hand = current.CreateSnapshot(0, revealAll: true).Players[0].Hand;
        var peachChoice = prompt.Choices.Single(choice =>
            choice.Cards.Count == 1 &&
            hand.Single(card => card.Id == choice.Cards[0]).Kind == CardKind.Peach);
        var restored = GameReplay.Restore(RoundTrip(current.CreateCheckpoint()), registry);

        Require(current.Submit(new AnswerPromptCommand(
                    0,
                    prompt.PromptId,
                    peachChoice.Id,
                    current.Revision)).Accepted,
            "Guicai could not replace the Wuhun judgment with Peach.");
        var restoredPrompt = restored.PendingDecision!;
        var restoredPeach = restoredPrompt.Choices.Single(choice => choice.Cards.SequenceEqual(peachChoice.Cards));
        Require(restored.Submit(new AnswerPromptCommand(
                    0,
                    restoredPrompt.PromptId,
                    restoredPeach.Id,
                    restored.Revision)).Accepted,
            "The restored Guicai replacement was rejected.");

        var events = current.Events.Select(item => item.Payload).ToArray();
        var resolved = events.OfType<JudgmentResolvedEvent>().Last(item =>
            item.Reason == JudgmentReasons.Wuhun);
        Require(resolved.CardKind == CardKind.Peach && !resolved.Succeeded &&
                events.OfType<JudgmentReplacementResolvedEvent>().Any(item =>
                    item.Reason == JudgmentReasons.Wuhun && item.Used &&
                    item.NewCardKind == CardKind.Peach) &&
                !events.OfType<DirectDeathDeclaredEvent>().Any() &&
                current.CreateSnapshot(0, revealAll: true).Players[0].IsAlive &&
                current.CreateSnapshot(0, revealAll: true).Players[0].Markers is null &&
                SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(current.CreateSnapshot(0, revealAll: true)),
            "A Peach replacement must make the final Wuhun judgment harmless and replay exactly.");
    }

    public static void NestedWuhunDeathsResumeInStackOrder()
    {
        var registry = CreateRegistry(
            ownerHp: 1,
            packageId: "wuhun-death-nested",
            deckCards: [new ContentDeckCardCount("standard:slash", 60)],
            attackerSkill: SkillKind.Quhu);
        GameEngine? selected = null;
        for (var seed = 1; seed <= 8_192 && selected is null; seed++)
        {
            var game = CreateGame(registry, seed, humanSeat: 2, humanRole: Role.Rebel);
            if (!game.Submit(new StartGameCommand()).Accepted)
                continue;
            var full = game.CreateSnapshot(2, revealAll: true);
            if (full.Players[3].GeneralId != OwnerId ||
                full.Players[2].GeneralId != AttackerId)
                continue;

            for (var step = 0; step < 256; step++)
            {
                if (game.State is { Status: EngineStatus.AwaitingHumanPlay, CurrentSeat: 2 })
                    break;
                if (game.State.Status == EngineStatus.Completed)
                    break;

                GameCommand command = game.PendingDecision is { PlayerSeat: 2 } pendingPrompt
                    ? new AnswerPromptCommand(
                        2,
                        pendingPrompt.PromptId,
                        pendingPrompt.Choices.First(choice => choice.Cards.Count == 0).Id,
                        game.Revision)
                    : new AdvanceOneStepCommand(game.Revision);
                if (!game.Submit(command).Accepted)
                    break;
            }

            var ready = game.CreateSnapshot(2, revealAll: true);
            if (game.State is not { Status: EngineStatus.AwaitingHumanPlay, CurrentSeat: 2 } ||
                !ready.Players[3].IsAlive ||
                ready.Players[3].Role != Role.Loyalist ||
                !ready.Players[0].IsAlive ||
                ready.Players[0].Hand.Count == 0)
                continue;
            var quhu = game.GetHumanLegalActions().SingleOrDefault(action =>
                action.Kind == LegalActionKind.UseSkill &&
                action.Skill == SkillKind.Quhu &&
                action.SelectableTargetSeats.Contains(0));
            if (quhu is null)
                continue;
            var lordMaxRank = ready.Players[0].Hand.Max(card => card.Rank);
            var sourceCard = ready.Players[2].Hand
                .Where(card => quhu.SelectableCardIds.Contains(card.Id) && card.Rank <= lordMaxRank)
                .OrderBy(card => card.Rank)
                .FirstOrDefault();
            if (sourceCard is null ||
                !game.Submit(new UseSkillCommand(
                    2,
                    SkillKind.Quhu,
                    [sourceCard.Id],
                    [0],
                    game.Revision,
                    game.PendingDecision!.PromptId)).Accepted)
                continue;

            for (var step = 0; step < 128; step++)
            {
                if (game.State is { Status: EngineStatus.AwaitingHumanPlay, CurrentSeat: 2 })
                    break;
                if (game.State.Status == EngineStatus.Completed)
                    break;

                GameCommand command = game.PendingDecision is { PlayerSeat: 2 } pendingPrompt
                    ? new AnswerPromptCommand(
                        2,
                        pendingPrompt.PromptId,
                        pendingPrompt.Choices.First(choice => choice.Cards.Count == 0).Id,
                        game.Revision)
                    : new AdvanceOneStepCommand(game.Revision);
                if (!game.Submit(command).Accepted)
                    break;
            }

            if (game.State is not { Status: EngineStatus.AwaitingHumanPlay, CurrentSeat: 2 } ||
                !game.Events.Select(item => item.Payload).OfType<PindianResolvedEvent>().Any(item =>
                    item.InitiatorSeat == 2 && item.OpponentSeat == 0 && !item.InitiatorWon) ||
                !game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().Any(item =>
                    item.SourceSeat == 0 && item.TargetSeat == 2) ||
                !game.Events.Select(item => item.Payload).OfType<PlayerMarkerChangedEvent>().Any(item =>
                    item.PlayerSeat == 0 && item.SkillOwnerSeat == 2 && item.Delta > 0) ||
                !game.CreateSnapshot(2, revealAll: true).Players[3].IsAlive)
                continue;
            var slash = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Slash && action.TargetSeat == 3);
            if (slash is null || !PlayAtSeat(game, slash, actorSeat: 2).Accepted)
                continue;

            for (var step = 0; step < 256; step++)
            {
                if (game.PendingDecision is
                    {
                        Kind: DecisionKind.WuhunTarget,
                        PlayerSeat: 2,
                        ValidTargetSeats: var candidates
                    } && candidates.Contains(0))
                {
                    selected = game;
                    break;
                }
                if (game.State.Status == EngineStatus.Completed)
                    break;

                GameCommand command = game.PendingDecision is { PlayerSeat: 2 } pendingPrompt
                    ? new AnswerPromptCommand(
                        2,
                        pendingPrompt.PromptId,
                        pendingPrompt.Choices.First(choice => choice.Cards.Count == 0).Id,
                        game.Revision)
                    : new AdvanceOneStepCommand(game.Revision);
                if (!game.Submit(command).Accepted)
                    break;
            }
        }

        var current = selected ??
            throw new InvalidOperationException("Could not find a bounded nested Wuhun fixture.");
        var restored = GameReplay.Restore(RoundTrip(current.CreateCheckpoint()), registry);
        const int nestedTarget = 0;
        Require(current.ResolutionStack.OfType<DeathSkillFrame>().Select(frame => frame.OwnerSeat)
                    .SequenceEqual([3, 2]),
            "Nested Wuhun must retain both outer and inner death-skill frames.");

        var prompt = current.PendingDecision!;
        Require(current.Submit(new AnswerPromptCommand(
                    2,
                    prompt.PromptId,
                    prompt.Choices.Single(choice => choice.Targets.SequenceEqual([nestedTarget])).Id,
                    current.Revision)).Accepted,
            "The nested Wuhun target was rejected.");
        var restoredPrompt = restored.PendingDecision!;
        Require(restored.Submit(new AnswerPromptCommand(
                    2,
                    restoredPrompt.PromptId,
                    restoredPrompt.Choices.Single(choice => choice.Targets.SequenceEqual([nestedTarget])).Id,
                    restored.Revision)).Accepted,
            "The restored nested Wuhun target was rejected.");

        var events = current.Events.Select(item => item.Payload).ToArray();
        Require(events.OfType<DeathSkillStartedEvent>().Select(item => item.OwnerSeat)
                    .TakeLast(2).SequenceEqual([3, 2]) &&
                events.OfType<DirectDeathDeclaredEvent>().Select(item => item.TargetSeat)
                    .TakeLast(2).SequenceEqual([2, nestedTarget]) &&
                events.OfType<DeathSkillResolvedEvent>().Select(item => item.OwnerSeat)
                    .TakeLast(2).SequenceEqual([2, 3]) &&
                !current.CreateSnapshot(2, revealAll: true).Players[nestedTarget].IsAlive &&
                !current.CreateSnapshot(2, revealAll: true).Players[2].IsAlive &&
                current.ResolutionStack.Count == 0 &&
                SnapshotJson.Serialize(restored.CreateSnapshot(2, revealAll: true)) ==
                SnapshotJson.Serialize(current.CreateSnapshot(2, revealAll: true)),
            "Nested Wuhun deaths must resolve inner-first, resume the outer death and replay exactly.");
    }

    private static GameEngine FindFixture(
        ContentRegistry registry,
        bool requireAlcohol,
        Role? requiredOwnerRole = null)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = CreateGame(registry, seed, humanSeat: 0, humanRole: Role.Lord);
            if (!game.Submit(new StartGameCommand()).Accepted || !AdvanceToHumanPlay(game))
                continue;
            var full = game.CreateSnapshot(0, revealAll: true);
            if (full.Players[0].GeneralId != AttackerId ||
                full.Players[1].GeneralId != OwnerId ||
                requiredOwnerRole is { } role && full.Players[1].Role != role)
                continue;
            var actions = game.GetHumanLegalActions();
            if (actions.All(action => action.Kind != LegalActionKind.Slash || action.TargetSeat != 1) ||
                requireAlcohol && actions.All(action => action.Kind != LegalActionKind.Alcohol))
                continue;
            return game;
        }

        throw new InvalidOperationException("Could not find a bounded Wuhun marker fixture.");
    }

    private static GameEngine CreateGame(
        ContentRegistry registry,
        int seed,
        int humanSeat,
        Role humanRole) =>
        GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 4,
            HumanSeat = humanSeat,
            HumanRole = humanRole,
            ModeId = ModeId,
            UseInteractiveSetup = false,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 20
        }, registry);

    private static void PlayAlcoholAndSlash(GameEngine game, bool useAlcohol)
    {
        if (useAlcohol)
        {
            var alcohol = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Alcohol);
            Require(Play(game, alcohol).Accepted && AdvanceToHumanPlay(game),
                "The Wuhun fixture could not apply Alcohol before Slash.");
        }

        var slash = game.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.Slash && action.TargetSeat == 1);
        Require(Play(game, slash).Accepted, "The Wuhun fixture Slash was rejected.");
        for (var step = 0; step < 128; step++)
        {
            if (game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                    .Any(item => item.SourceSeat == 0 && item.TargetSeat == 1) &&
                (game.State.Status == EngineStatus.Completed ||
                 game.State is { Status: EngineStatus.AwaitingHumanPlay, CurrentSeat: 0 }))
                return;

            GameCommand command = game.PendingDecision is { PlayerSeat: 0 } prompt
                ? new AnswerPromptCommand(
                    0,
                    prompt.PromptId,
                    prompt.Choices.First(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "let-die").Id,
                    game.Revision)
                : new AdvanceOneStepCommand(game.Revision);
            var result = game.Submit(command);
            Require(result.Accepted,
                result.Error?.Message ?? "The Wuhun damage continuation was rejected.");
        }

        throw new InvalidOperationException("The Wuhun damage did not settle within the bounded steps.");
    }

    private static bool AdvanceToHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 64; step++)
        {
            if (game.State is { Status: EngineStatus.AwaitingHumanPlay, CurrentSeat: 0 })
                return true;
            if (game.PendingDecision is not null)
                return false;
            if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted)
                return false;
        }
        return false;
    }

    private static CommandResult Play(GameEngine game, LegalAction action) => game.Submit(new PlayCardCommand(
        ActorSeat: 0,
        CardId: action.CardId!.Value,
        TargetSeats: action.TargetSeats,
        ExpectedRevision: game.Revision,
        PromptId: game.PendingDecision!.PromptId));

    private static CommandResult PlayAtSeat(
        GameEngine game,
        LegalAction action,
        int actorSeat) => game.Submit(new PlayCardCommand(
            ActorSeat: actorSeat,
            CardId: action.CardId!.Value,
            TargetSeats: action.TargetSeats,
            ExpectedRevision: game.Revision,
            PromptId: game.PendingDecision!.PromptId));

    private static ContentRegistry CreateRegistry(
        int ownerHp,
        string packageId,
        IReadOnlyList<ContentDeckCardCount>? deckCards = null,
        SkillKind? attackerSkill = null,
        IReadOnlyDictionary<string, int>? roleCounts = null) =>
        ContentRegistry.Build(
            new StandardContentPackage(),
            new SyntheticPackage(
                packageId,
                builder =>
                {
                    builder.AddSkill(new ContentSkillDefinition(
                        "wuhun-marker:wuhun",
                        "武魂",
                        "受到每点伤害后，伤害来源获得一枚梦魇标记。",
                        SkillKind.Wuhun));
                    if (attackerSkill == SkillKind.Guicai)
                    {
                        builder.AddSkill(new ContentSkillDefinition(
                            "wuhun-marker:guicai",
                            "鬼才",
                            "一名角色的判定牌生效前，你可以打出一张手牌替换之。",
                            SkillKind.Guicai));
                    }
                    if (attackerSkill == SkillKind.Quhu)
                    {
                        builder.AddSkill(new ContentSkillDefinition(
                            "wuhun-marker:quhu",
                            "驱虎",
                            "出牌阶段限一次，你可以与一名体力值大于你的角色拼点。",
                            SkillKind.Quhu));
                    }
                    builder.AddGeneral(new ContentGeneralDefinition(
                        AttackerId,
                        "攻击者",
                        "guan_yu",
                        attackerSkill switch
                        {
                            SkillKind.Guicai => "wuhun-marker:guicai",
                            SkillKind.Wuhun => "wuhun-marker:wuhun",
                            SkillKind.Quhu => "wuhun-marker:wuhun",
                            _ => "standard:none"
                        },
                        "wei",
                        BaseHp: 4,
                        AdditionalSkillIds: attackerSkill == SkillKind.Quhu
                            ? ["wuhun-marker:quhu"]
                            : null));
                    builder.AddGeneral(new ContentGeneralDefinition(
                        OwnerId, "武魂测试者", "shen-guan-yu", "wuhun-marker:wuhun", "god", BaseHp: ownerHp));
                    builder.AddGeneral(new ContentGeneralDefinition(
                        BystanderOneId, "旁观者一", "liu_bei", "standard:none", "shu", BaseHp: 4));
                    builder.AddGeneral(new ContentGeneralDefinition(
                        BystanderTwoId, "旁观者二", "sun_quan", "standard:none", "wu", BaseHp: 4));
                    builder.AddDeck(new ContentDeckRecipe(
                        DeckId,
                        "武魂公开标记测试牌堆",
                        InitialHandSize: 4,
                        DrawPerTurn: 0,
                        deckCards ??
                        [
                            new ContentDeckCardCount("standard:slash", 40),
                            new ContentDeckCardCount("standard:alcohol", 20)
                        ]));
                    builder.AddMode(new ContentModeDefinition(
                        ModeId,
                        "武魂公开标记测试",
                        MinPlayers: 4,
                        MaxPlayers: 4,
                        RoleCounts: roleCounts ?? new Dictionary<string, int>
                        {
                            [nameof(Role.Lord)] = 1,
                            [nameof(Role.Loyalist)] = 1,
                            [nameof(Role.Rebel)] = 1,
                            [nameof(Role.Renegade)] = 1
                        },
                        DeckId,
                        GeneralCandidateCount: 1,
                        GeneralPoolIds: [AttackerId, OwnerId, BystanderOneId, BystanderTwoId]));
                },
                new PackageDependency("standard", new Version(1, 11, 0))));

    private static void AssertReplay(GameEngine game, ContentRegistry registry, int expectedCount)
    {
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                replay.Events.Select(item => item.Payload).OfType<PlayerMarkerChangedEvent>().Count() == expectedCount,
            $"Rules v{game.RulesVersion} public markers did not replay exactly.");
    }

    private static void AssertReplayStateAndDeathEvents(GameEngine game, ContentRegistry registry)
    {
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        var eventSignature = game.Events.Select(item => item.Payload.GetType().Name).ToArray();
        var replaySignature = replay.Events.Select(item => item.Payload.GetType().Name).ToArray();
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                replaySignature.SequenceEqual(eventSignature),
            $"Rules v{game.RulesVersion} Wuhun death events did not replay exactly.");
    }

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
