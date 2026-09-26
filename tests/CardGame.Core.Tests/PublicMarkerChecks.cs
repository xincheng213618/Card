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
    private const string WuhunSkillId = "wuhun-marker:wuhun";
    private const string WuhunRules = """
    {"schemaVersion":61,"skills":[{"id":"wuhun-marker:wuhun","revision":2,"minimumRulesVersion":170,
    "modifiers":[],"viewAs":[],"activations":[],"triggers":[
      {"id":"damage-nightmare","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamagePoint","optional":false,"priority":0,
       "effects":[{"op":"changeAttributedMarker","target":"owner","targetRef":{"kind":"eventSource"},"marker":"nightmare","amount":1}]},
      {"id":"death-judgment","window":"ownerDied","subject":"owner","optional":false,"priority":0,
       "effects":[{"op":"selectTarget","target":"owner","targetKind":"maximumAttributedMarker","marker":"nightmare"},
                  {"op":"startJudgment","target":"selectedTarget","judgmentReason":"skill.wuhun.death","resultBind":"judgment","visibility":"public"},
                  {"op":"causeDeathUnlessBoundCardKind","target":"selectedTarget","sourceBind":"judgment","excludedCardKinds":["peach","peachGarden"]}]}
    ],"contributions":[],"cardIdentities":[],"states":[]}]}
    """;
    private const string WuhunPresentation = """
    {"schemaVersion":3,"skills":{"wuhun-marker:wuhun":{"name":"武魂","description":"归属梦魇与死亡判定测试。"}}}
    """;

    public static void WuhunDamageOrderAndReplay()
    {
        Require(GameCheckpoint.CurrentRulesVersion >= 91,
            "Source-attributed public markers require rules version 91 or newer.");
        var registry = CreateRegistry(ownerHp: 3, packageId: "wuhun-marker-nonlethal");
        var current = FindFixture(registry, requireAlcohol: true);

        PlayAlcoholAndSlash(current, useAlcohol: true);

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
                    item.Reason.Contains("damage-nightmare", StringComparison.Ordinal)),
            "Two actual damage points must commit two ordered public Nightmare increments.");
        var publicMarker = current.CreateSnapshot(1).Players[0].Markers?.Single();
        Require(publicMarker is
        {
            Kind: PlayerMarkerKind.Nightmare,
            Name: "梦魇",
            Count: 2
        },
            "An ordinary observer did not receive the exact public Nightmare counter.");
        AssertReplay(current, registry, expectedCount: 2);

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
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        var wuhun = current.Skills["classic:wuhun"];
        Require(wuhun is
                {
                    Tags: SkillTag.Locked,
                    ExecutionForms: SkillExecutionForm.State,
                    Program.RuntimeVersion: "skill-program-v61",
                    Program.MinimumRulesVersion: 171
                } &&
                wuhun.Program.Triggers.Select(trigger => trigger.Window)
                    .SequenceEqual([
                        SkillProgramTriggerWindow.DamageAppliedBeforeDying,
                        SkillProgramTriggerWindow.OwnerDied
                    ]),
            "Current Wuhun must bind the pre-dying Nightmare record and death judgment program.");
        Exception? schemaFailure = null;
        try
        {
            _ = SkillProgramCatalog.Load(
                WuhunRules.Replace("\"schemaVersion\":61", "\"schemaVersion\":57", StringComparison.Ordinal),
                WuhunPresentation);
        }
        catch (Exception exception)
        {
            schemaFailure = exception;
        }
        Require(schemaFailure is InvalidOperationException &&
                schemaFailure.Message.Contains("expected 60", StringComparison.Ordinal),
            "Unsupported schema versions must be rejected before running the Wuhun graph.");

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

        PlayAlcoholAndSlash(current, useAlcohol: false);

        var events = current.Events.Select(item => item.Payload).ToArray();
        var ownerDeath = Array.FindIndex(events, item => item is PlayerDiedEvent died && died.VictimSeat == 1);
        var skillStarted = Array.FindIndex(events, item => item is ProgramBindingStartedEvent started &&
            started.OwnerSeat == 1 && started.SkillId == WuhunSkillId &&
            started.Window == SkillProgramTriggerWindow.OwnerDied);
        var judgment = Array.FindIndex(events, item => item is JudgmentResolvedEvent resolved &&
            resolved.Reason == JudgmentReasons.Wuhun && resolved.TargetSeat == 0);
        var directDeath = Array.FindIndex(events, item => item is ProgramSkillCauseDeathDeclaredEvent direct &&
            direct.SourceSeat == 1 && direct.TargetSeat == 0 && direct.SkillId == WuhunSkillId);
        var targetDeath = Array.FindIndex(events, item => item is PlayerDiedEvent died &&
            died.VictimSeat == 0 && died.KillerSeat is null);
        var targetDying = events.OfType<PlayerDyingEvent>().Any(item => item.VictimSeat == 0);
        var cleared = events.OfType<PlayerMarkerChangedEvent>().SingleOrDefault(item =>
            item.PlayerSeat == 0 && item.SkillOwnerSeat == 1 && item.Delta == -1);
        Require(ownerDeath >= 0 && skillStarted > ownerDeath &&
                judgment > skillStarted && directDeath > judgment && targetDeath > directDeath &&
                !targetDying && cleared is { Count: 0, Reason: "program.attributed-marker.death-clear" } &&
                current.State.Status == EngineStatus.Completed &&
                !current.CreateSnapshot(0, revealAll: true).Players[0].IsAlive,
            $"Wuhun must select the positive maximum after owner death, judge, then directly kill without dying rescue. " +
            $"indices={ownerDeath}/{skillStarted}/{judgment}/{directDeath}/{targetDeath}; dying={targetDying}; " +
            $"cleared={cleared?.Delta}/{cleared?.Count}/{cleared?.Reason}; " +
            $"state={current.State.Winner}/{current.State.Status}; targetAlive={current.CreateSnapshot(0, revealAll: true).Players[0].IsAlive}.");

        AssertReplayStateAndDeathEvents(current, registry);
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
                if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 1 } targetPrompt &&
                    targetPrompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "select-target"))
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
                current.CreateSnapshot(1).PendingDecision?.Kind == DecisionKind.ProgramTrigger &&
                current.ResolutionStack.TakeLast(2).Select(frame => frame.Kind)
                    .SequenceEqual([ResolutionFrameKind.ProgramDeathTriggerWindow, ResolutionFrameKind.ProgramSkill]),
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
                !events.OfType<ProgramBindingStartedEvent>().Any(item =>
                    item.SkillId == WuhunSkillId && item.Window == SkillProgramTriggerWindow.OwnerDied) &&
                !events.OfType<ProgramSkillCauseDeathDeclaredEvent>().Any() &&
                events.OfType<PlayerMarkerChangedEvent>().Any(item =>
                    item.PlayerSeat == 0 && item.SkillOwnerSeat == 1 && item.Delta == -1) &&
                game.CreateSnapshot(0, revealAll: true).Players[0].IsAlive,
            $"Wuhun must not start after its owner's death has already determined the winner. " +
            $"state={game.State.Winner}/{game.State.Status}; ownerDied={events.OfType<PlayerDiedEvent>().Any(item => item.VictimSeat == 1)}; " +
            $"started={events.OfType<ProgramBindingStartedEvent>().Any(item => item.SkillId == WuhunSkillId && item.Window == SkillProgramTriggerWindow.OwnerDied)}; " +
            $"direct={events.OfType<ProgramSkillCauseDeathDeclaredEvent>().Any()}; " +
            $"cleared={events.OfType<PlayerMarkerChangedEvent>().Any(item => item.PlayerSeat == 0 && item.SkillOwnerSeat == 1 && item.Delta == -1)}; " +
            $"attackerAlive={game.CreateSnapshot(0, revealAll: true).Players[0].IsAlive}.");
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
            attackerSkill: "classic:guicai");
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
                if (game.PendingDecision is { Kind: DecisionKind.ProgramJudgmentReplacement, PlayerSeat: 0 })
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
        Require(resolved.CardKind == CardKind.Peach &&
                events.OfType<JudgmentReplacementResolvedEvent>().Any(item =>
                    item.Reason == JudgmentReasons.Wuhun && item.Used &&
                    item.NewCardKind == CardKind.Peach) &&
                !events.OfType<ProgramSkillCauseDeathDeclaredEvent>().Any() &&
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
            attackerSkill: "classic:quhu");
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
                action.Kind == LegalActionKind.UseProgramSkill &&
                action.ProgramSkillId == "classic:quhu" &&
                action.SelectableTargetSeats.Contains(0));
            if (quhu is null)
                continue;
            var lordMaxRank = ready.Players[0].Hand.Max(card => card.Rank);
            var sourceCard = ready.Players[2].Hand
                .Where(card => quhu.SelectableCardIds.Contains(card.Id) && card.Rank <= lordMaxRank)
                .OrderBy(card => card.Rank)
                .FirstOrDefault();
            if (sourceCard is null ||
                !game.Submit(new UseProgramSkillCommand(
                    2,
                    "classic:quhu",
                    quhu.ProgramActivationId!,
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
                !game.Events.Select(item => item.Payload).OfType<PindianResultDeterminedEvent>().Any(item =>
                    item.Result.SourceSeat == 2 && item.Result.OpponentSeat == 0 && !item.Result.SourceWon) ||
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
                        Kind: DecisionKind.ProgramTrigger,
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
        Require(current.ResolutionStack.OfType<ProgramDeathTriggerWindowFrame>().Select(frame => frame.OwnerSeat)
                    .SequenceEqual([3, 2]),
            "Nested Wuhun must retain both outer and inner owner-death program windows.");

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
        Require(events.OfType<ProgramBindingStartedEvent>().Where(item =>
                        item.SkillId == WuhunSkillId && item.Window == SkillProgramTriggerWindow.OwnerDied)
                    .Select(item => item.OwnerSeat)
                    .TakeLast(2).SequenceEqual([3, 2]) &&
                events.OfType<ProgramSkillCauseDeathDeclaredEvent>().Select(item => item.TargetSeat)
                    .TakeLast(2).SequenceEqual([2, nestedTarget]) &&
                events.OfType<ProgramBindingResolvedEvent>().Where(item =>
                        item.SkillId == WuhunSkillId && item.Window == SkillProgramTriggerWindow.OwnerDied)
                    .Select(item => item.OwnerSeat)
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
        string? attackerSkill = null,
        IReadOnlyDictionary<string, int>? roleCounts = null) =>
        ContentRegistry.Build(
            new StandardContentPackage(),
            new SyntheticPackage(
                packageId,
                builder =>
                {
                    var currentRules = WuhunRules
                        .Replace("\"revision\":2", "\"revision\":3", StringComparison.Ordinal)
                        .Replace("\"window\":\"afterDamageApplied\"",
                            "\"window\":\"damageAppliedBeforeDying\"", StringComparison.Ordinal);
                    var wuhun = SkillProgramCatalog.Load(currentRules, WuhunPresentation).Programs[WuhunSkillId];
                    builder.AddSkill(new ContentSkillDefinition(
                        WuhunSkillId,
                        "武魂",
                        "受到每点伤害后，伤害来源获得一枚梦魇标记。")
                    {
                        Program = wuhun,
                        Tags = SkillTag.Locked,
                        ExecutionForms = SkillExecutionForm.State
                    });
                    if (attackerSkill == "classic:guicai")
                    {
                        builder.AddSkill(new ContentSkillDefinition(
                            "wuhun-marker:guicai",
                            "鬼才",
                            "一名角色的判定牌生效前，你可以打出一张手牌替换之。"));
                    }
                    if (attackerSkill == "classic:quhu")
                    {
                        builder.AddSkill(StandardContentRegistry.CreateWithClassicGenerals()
                            .Skills["classic:quhu"]);
                    }
                    builder.AddGeneral(new ContentGeneralDefinition(
                        AttackerId,
                        "攻击者",
                        "guan_yu",
                        attackerSkill switch
                        {
                            "classic:guicai" => "wuhun-marker:guicai",
                            "classic:wuhun" => WuhunSkillId,
                            "classic:quhu" => WuhunSkillId,
                            _ => "standard:none"
                        },
                        "wei",
                        BaseHp: 4,
                        AdditionalSkillIds: attackerSkill == "classic:quhu"
                            ? ["classic:quhu"]
                            : null));
                    builder.AddGeneral(new ContentGeneralDefinition(
                        OwnerId, "武魂测试者", "shen-guan-yu", WuhunSkillId, "god", BaseHp: ownerHp));
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
