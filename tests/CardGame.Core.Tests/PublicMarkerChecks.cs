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
        Require(GameCheckpoint.CurrentRulesVersion >= 90,
            "Public per-damage-point markers require rules version 90 or newer.");

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

    private static GameEngine FindFixture(ContentRegistry registry, bool requireAlcohol)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 4,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                ModeId = ModeId,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2,
                MaxTurns = 20
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted || !AdvanceToHumanPlay(game))
                continue;
            var full = game.CreateSnapshot(0, revealAll: true);
            if (full.Players[0].GeneralId != AttackerId || full.Players[1].GeneralId != OwnerId)
                continue;
            var actions = game.GetHumanLegalActions();
            if (actions.All(action => action.Kind != LegalActionKind.Slash || action.TargetSeat != 1) ||
                requireAlcohol && actions.All(action => action.Kind != LegalActionKind.Alcohol))
                continue;
            return game;
        }

        throw new InvalidOperationException("Could not find a bounded Wuhun marker fixture.");
    }

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

    private static ContentRegistry CreateRegistry(int ownerHp, string packageId) =>
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
                    builder.AddGeneral(new ContentGeneralDefinition(
                        AttackerId, "攻击者", "guan_yu", "standard:none", "wei", BaseHp: 4));
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
                        [
                            new ContentDeckCardCount("standard:slash", 40),
                            new ContentDeckCardCount("standard:alcohol", 20)
                        ]));
                    builder.AddMode(new ContentModeDefinition(
                        ModeId,
                        "武魂公开标记测试",
                        MinPlayers: 4,
                        MaxPlayers: 4,
                        RoleCounts: new Dictionary<string, int>
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
