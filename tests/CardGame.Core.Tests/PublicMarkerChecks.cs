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
    {"schemaVersion":62,"skills":[{"id":"wuhun-marker:wuhun","revision":2,"minimumRulesVersion": 171,
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
                        builder.AddSkill(StandardContentRegistry.CreateWithClassicGenerals()
                            .Skills["classic:guicai"]);
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
                            "classic:guicai" => "classic:guicai",
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
