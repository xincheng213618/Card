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

        var lethalRegistry = CreateRegistry(ownerHp: 1, packageId: "wuhun-marker-lethal",
            deckCards: [new ContentDeckCardCount("standard:slash", 60)]);
        var lethal = FindFixture(lethalRegistry, requireAlcohol: false);
        PlayAlcoholAndSlash(lethal, useAlcohol: false);
        var events = lethal.Events.Select(item => item.Payload).ToArray();
        var appliedIndex = Array.FindIndex(events, item => item is DamageAppliedEvent damage &&
            damage.SourceSeat == 0 && damage.TargetSeat == 1);
        var markerIndex = Array.FindIndex(events, item => item is PlayerMarkerChangedEvent marker &&
            marker.PlayerSeat == 0 && marker.SkillOwnerSeat == 1);
        var dyingIndex = Array.FindIndex(events, item => item is PlayerDyingEvent dying && dying.VictimSeat == 1);
        Require(appliedIndex >= 0 && markerIndex > appliedIndex && dyingIndex > markerIndex,
            "Lethal damage must add Nightmare after damage is applied and before dying begins.");

        ResolveLethalWuhun(lethal);
        var settled = lethal.Events.Select(item => item.Payload).ToArray();
        var ownerDeathIndex = Array.FindIndex(settled, item =>
            item is PlayerDiedEvent died && died.VictimSeat == 1);
        var directIndex = Array.FindIndex(settled, item =>
            item is ProgramSkillCauseDeathDeclaredEvent direct && direct.TargetSeat == 0);
        var attackerDeathIndex = Array.FindIndex(settled, item =>
            item is PlayerDiedEvent died && died.VictimSeat == 0);
        var cleanupIndex = Array.FindIndex(settled, item =>
            item is PlayerMarkerChangedEvent marker && marker.PlayerSeat == 0 && marker.Delta == -1);
        Require(ownerDeathIndex >= 0 && directIndex > ownerDeathIndex &&
                attackerDeathIndex > directIndex && cleanupIndex > attackerDeathIndex &&
                lethal.ResolutionStack.All(frame => frame is not DeathFrame),
            "Nested Wuhun direct death must return to the outer owner-death frame before marker cleanup.");
        var settledReplay = GameReplay.Restore(RoundTrip(lethal.CreateCheckpoint()), lethalRegistry);
        Require(SnapshotJson.Serialize(settledReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(lethal.CreateSnapshot(0, revealAll: true)) &&
                settledReplay.Events.Select(item => item.Payload.GetType()).SequenceEqual(
                    lethal.Events.Select(item => item.Payload.GetType())),
            "Nested direct death and outer marker cleanup did not replay in the same order.");
    }

    public static void XingshangClaimsNestedDeathCleanupAndReplay()
    {
        var registry = CreateRegistry(ownerHp: 1, packageId: "wuhun-xingshang-nested-deaths",
            deckCards: [new ContentDeckCardCount("standard:slash", 60)], withXingshang: true);
        var game = FindFixture(registry, requireAlcohol: false,
            requiredOwnerRole: Role.Loyalist, humanRole: Role.Rebel, fixedSeed: 512);
        var xingshangSeat = game.CreateSnapshot(0, revealAll: true).Players
            .Single(player => player.GeneralId == BystanderOneId).Seat;
        var slash = game.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.Slash && action.TargetSeat == 1);
        Require(Play(game, slash).Accepted, "The nested Xingshang fixture Slash was rejected.");
        for (var step = 0; step < 128; step++)
        {
            var deaths = game.Events.Select(item => item.Payload).OfType<PlayerDiedEvent>().ToArray();
            if (deaths.Any(item => item.VictimSeat == 1) &&
                deaths.Any(item => item.VictimSeat == 0) &&
                game.ResolutionStack.All(frame => frame is not DeathFrame))
                break;
            var result = game.PendingDecision is { PlayerSeat: 0, Kind: not DecisionKind.PlayCard } prompt
                ? game.Submit(new AnswerPromptCommand(0, prompt.PromptId,
                    (prompt.Choices.FirstOrDefault(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "let-die") ??
                     prompt.Choices.First()).Id, game.Revision))
                : game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(result.Accepted, result.Error?.Message ?? "Nested Xingshang death did not advance.");
        }

        var events = game.Events.Select(item => item.Payload).ToArray();
        var outerDeath = Array.FindIndex(events, item => item is PlayerDiedEvent { VictimSeat: 1 });
        var innerDeath = Array.FindIndex(events, item => item is PlayerDiedEvent { VictimSeat: 0 });
        var outerCleanupIds = events.Take(outerDeath).OfType<CardMovedEvent>()
            .Where(item => item.From == CardLocation.Hand(1) && item.To == CardLocation.DiscardPile)
            .Select(item => item.CardId).ToHashSet();
        var innerCleanupIds = events.Skip(outerDeath + 1).Take(innerDeath - outerDeath - 1)
            .OfType<CardMovedEvent>()
            .Where(item => item.From == CardLocation.Hand(0) && item.To == CardLocation.DiscardPile)
            .Select(item => item.CardId).ToHashSet();
        var innerClaim = Array.FindIndex(events, innerDeath + 1, item => item is CardMovedEvent move &&
            move.From == CardLocation.DiscardPile && move.To == CardLocation.Hand(xingshangSeat) &&
            move.Reason.Value.Contains("claim-death-cleanup", StringComparison.Ordinal) &&
            innerCleanupIds.Contains(move.CardId));
        var outerClaim = Array.FindIndex(events, innerClaim + 1, item => item is CardMovedEvent move &&
            move.From == CardLocation.DiscardPile && move.To == CardLocation.Hand(xingshangSeat) &&
            move.Reason.Value.Contains("claim-death-cleanup", StringComparison.Ordinal) &&
            outerCleanupIds.Contains(move.CardId));
        Require(outerDeath >= 0 && innerDeath > outerDeath && outerCleanupIds.Count > 0 &&
                innerCleanupIds.Count > 0 && innerClaim > innerDeath && outerClaim > innerClaim &&
                game.ResolutionStack.All(frame => frame is not DeathFrame),
            "Xingshang must claim inner cleanup before returning to claim the outer death's cleanup cards.");
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                replay.Events.Select(item => item.Payload.GetType()).SequenceEqual(
                    game.Events.Select(item => item.Payload.GetType())),
            "Nested Xingshang cleanup claims did not replay in order.");
    }

    private static void ResolveLethalWuhun(GameEngine game)
    {
        for (var step = 0; step < 128; step++)
        {
            if (game.Events.Any(item => item.Payload is PlayerDiedEvent { VictimSeat: 0 }) &&
                game.ResolutionStack.All(frame => frame is not DeathFrame))
                return;
            var result = game.PendingDecision is { PlayerSeat: 0, Kind: not DecisionKind.PlayCard } prompt
                ? game.Submit(new AnswerPromptCommand(0, prompt.PromptId,
                    (prompt.Choices.FirstOrDefault(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "let-die") ??
                     prompt.Choices.FirstOrDefault(choice =>
                        choice.Parameters.GetValueOrDefault("program-action") == "skip") ??
                     prompt.Choices.First()).Id, game.Revision))
                : game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(result.Accepted,
                $"{result.Error?.Message ?? "The nested Wuhun death continuation was rejected."} " +
                $"Prompt={game.PendingDecision?.Kind}; Deaths={string.Join(',', game.Events.Select(item => item.Payload).OfType<PlayerDiedEvent>().Select(item => item.VictimSeat))}");
        }
        throw new InvalidOperationException(
            $"Nested Wuhun direct death did not settle within 128 steps. " +
            $"Prompt={game.PendingDecision?.Kind}; Stack={string.Join(',', game.ResolutionStack.Select(frame => frame.Kind))}; " +
            $"Deaths={string.Join(',', game.Events.Select(item => item.Payload).OfType<PlayerDiedEvent>().Select(item => item.VictimSeat))}; " +
            $"Direct={game.Events.Count(item => item.Payload is ProgramSkillCauseDeathDeclaredEvent)}");
    }







    private static GameEngine FindFixture(
        ContentRegistry registry,
        bool requireAlcohol,
        Role? requiredOwnerRole = null,
        Role humanRole = Role.Lord,
        int? fixedSeed = null)
    {
        for (var seed = fixedSeed ?? 1; seed <= (fixedSeed ?? 4_096); seed++)
        {
            var game = CreateGame(registry, seed, humanSeat: 0, humanRole);
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


    private static ContentRegistry CreateRegistry(
        int ownerHp,
        string packageId,
        IReadOnlyList<ContentDeckCardCount>? deckCards = null,
        string? attackerSkill = null,
        IReadOnlyDictionary<string, int>? roleCounts = null,
        bool withXingshang = false) =>
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
                    if (withXingshang)
                    {
                        builder.AddSkill(StandardContentRegistry.CreateWithClassicGenerals()
                            .Skills["classic:xingshang"]);
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
                        BystanderOneId, "旁观者一", "liu_bei",
                        withXingshang ? "classic:xingshang" : "standard:none", "shu", BaseHp: 4));
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


    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
