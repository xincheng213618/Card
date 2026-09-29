using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class MouLuMengChecks
{
    private const string GeneralId = "mou:lu-meng";
    private const string HengyeSkillId = "mou:hengye";
    private const string YingboSkillId = "mou:yingbo";

    public static void HengyeGrowthAndKillReset()
    {
        var fixture = CreateFixture();
        var game = fixture.Game;
        var recipientBefore = game.CreateSnapshot(0, revealAll: true).Players[fixture.RecipientSeat].HandCount;

        var firstCardId = PlaySlash(game, fixture.TargetSeat);
        var firstPrompt = RequirePrompt(game, DecisionKind.Yingbo);
        var pendingCheckpoint = RoundTrip(game.CreateCheckpoint());
        var pendingReplay = GameReplay.Restore(pendingCheckpoint, fixture.Registry);
        Require(SnapshotJson.Serialize(pendingReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                pendingReplay.PendingDecision is { Kind: DecisionKind.Yingbo, IsPrivate: true },
            "A paused first-use Yingbo gift must replay with the same private card continuation.");

        var gift = firstPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "yingbo-gift" &&
            choice.Targets is [var seat] && seat == fixture.RecipientSeat);
        Require(Answer(game, firstPrompt, gift), "The first Yingbo gift choice was rejected.");
        ReachHumanPlay(game);
        var afterGift = game.CreateSnapshot(0, revealAll: true);
        Require(afterGift.Players[fixture.RecipientSeat].HandCount == recipientBefore + 1 &&
                afterGift.Players[fixture.RecipientSeat].Hand.Any(card => card.Id == firstCardId) &&
                Growth(game) == 1 && game.GetAttackRange(0) == 2,
            "The first damage must grow Hengye once and transfer the resolved physical Slash.");

        PlaySlash(game, fixture.TargetSeat);
        ReachHumanPlay(game);
        Require(Growth(game) == 2 && game.GetAttackRange(0) == 3,
            "The repeated Slash damage must grow all Hengye values to 2/3.");

        PlaySlash(game, fixture.TargetSeat);
        ReachHumanPlay(game);
        Require(Growth(game) == 3 && game.GetAttackRange(0) == 4,
            "The third damage must cap Hengye at 3/3 and expose its full attack-range bonus.");

        PlaySlash(game, fixture.TargetSeat);
        ReachHumanPlay(game);
        var final = game.CreateSnapshot(0, revealAll: true);
        var growthEvents = game.Events.Select(item => item.Payload)
            .OfType<HengyeGrowthChangedEvent>()
            .Select(item => item.CurrentGrowth)
            .ToArray();
        var reset = game.Events.Select(item => item.Payload)
            .OfType<SkillResetEvent>()
            .LastOrDefault(item => item.SkillOwnerSeat == 0 && item.SkillId == HengyeSkillId);
        Require(!final.Players[fixture.TargetSeat].IsAlive &&
                growthEvents.SequenceEqual([1, 2, 3]) &&
                reset is { PreviousUsageCount: 3 } &&
                Growth(game) == 0 &&
                game.GetAttackRange(0) == 1,
            $"The third damage must reach 3/3, kill the target, then reset Hengye to its game-start state " +
            $"(alive={final.Players[fixture.TargetSeat].IsAlive}, hp={final.Players[fixture.TargetSeat].Hp}, " +
            $"growthEvents={string.Join(',', growthEvents)}, reset={reset?.PreviousUsageCount.ToString() ?? "none"}, " +
            $"growth={Growth(game)}, range={game.GetAttackRange(0)})." );

        var restored = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                Growth(restored) == 0,
            "The completed growth, kill and reset sequence must replay exactly.");

        var recoveryFixture = CreateFixture();
        var recoveryGame = recoveryFixture.Game;
        PlaySlash(recoveryGame, recoveryFixture.TargetSeat);
        var recoveryGift = RequirePrompt(recoveryGame, DecisionKind.Yingbo);
        Require(Answer(recoveryGame, recoveryGift, recoveryGift.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "yingbo-skip")),
            "The recovery fixture could not skip its first Yingbo gift.");
        ReachHumanPlay(recoveryGame);
        PlaySlash(recoveryGame, recoveryFixture.TargetSeat);
        ReachHumanPlay(recoveryGame);
        PlaySlash(recoveryGame, recoveryFixture.TargetSeat);
        ReachHumanPlay(recoveryGame);
        Require(Growth(recoveryGame) == 3, "The recovery fixture did not reach full Hengye growth.");
        var recoveryBefore = recoveryGame.Events.Select(item => item.Payload)
            .OfType<RecoveryAppliedEvent>()
            .Count(item => item.SourceSeat == 0 && item.TargetSeat == 0);
        var owner = recoveryGame.CreateSnapshot(0, revealAll: true).Players[0];
        SetRuntimeHp(recoveryGame, 0, owner.MaxHp - 1);
        var recoveryPlay = RequirePrompt(recoveryGame, DecisionKind.PlayCard);
        Require(recoveryGame.Submit(new EndPlayPhaseCommand(
                0,
                recoveryGame.Revision,
                recoveryPlay.PromptId)).Accepted,
            "The recovery fixture could not end its play phase.");
        ReachNextHumanPlay(recoveryGame);
        var recoveryAfter = recoveryGame.Events.Select(item => item.Payload)
            .OfType<RecoveryAppliedEvent>()
            .Count(item => item.SourceSeat == 0 && item.TargetSeat == 0);
        Require(Growth(recoveryGame) == 3 && recoveryAfter == recoveryBefore + 1,
            "Full Hengye growth must persist across rounds and recover its wounded owner at turn start.");
    }

    private static int PlaySlash(GameEngine game, int targetSeat)
    {
        var prompt = RequirePrompt(game, DecisionKind.PlayCard);
        var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
            candidate.Kind == LegalActionKind.Slash &&
            candidate.CardId is not null &&
            candidate.TargetSeats.SequenceEqual([targetSeat])) ??
            throw new InvalidOperationException($"No legal Slash targets seat {targetSeat}.");
        var result = game.Submit(new PlayCardCommand(
            0,
            action.CardId!.Value,
            action.TargetSeats,
            game.Revision,
            prompt.PromptId,
            action.PlayedCardKind));
        Require(result.Accepted, result.Error?.Message ?? "The Mou Lu Meng Slash was rejected.");
        return action.CardId.Value;
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { PlayerSeat: 0 } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected human {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");

    private static bool Answer(GameEngine game, PendingDecision prompt, PromptChoice choice) =>
        game.Submit(new AnswerPromptCommand(
            prompt.PlayerSeat,
            prompt.PromptId,
            choice.Id,
            game.Revision)).Accepted;

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 128; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return;
            Require(game.PendingDecision?.PlayerSeat != 0,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while returning to play.");
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "The Mou Lu Meng fixture could not return to the play boundary.");
        }
        throw new InvalidOperationException("The Mou Lu Meng fixture did not return to play in bounded steps.");
    }

    private static void ReachNextHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 4_096; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } &&
                game.Events.Select(item => item.Payload).OfType<RoundStartedEvent>().Count() >= 2)
                return;
            Require(game.PendingDecision?.PlayerSeat != 0,
                $"Unexpected human prompt {game.PendingDecision?.Kind} before the next round.");
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "The Mou Lu Meng fixture could not advance to the next round.");
        }
        throw new InvalidOperationException("The Mou Lu Meng fixture did not reach the next round in bounded steps.");
    }

    private static void SetRuntimeHp(GameEngine game, int seat, int hp)
    {
        var playersField = typeof(GameEngine).GetField(
            "_players",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The runtime player list was not found.");
        var players = (System.Collections.IList)(playersField.GetValue(game) ??
            throw new InvalidOperationException("The runtime player list is unavailable."));
        var player = players[seat] ??
            throw new InvalidOperationException("The runtime player is unavailable.");
        var hpProperty = player.GetType().GetProperty("Hp") ??
            throw new InvalidOperationException("The runtime HP property was not found.");
        hpProperty.SetValue(player, hp);
    }

    private static int Growth(GameEngine game) =>
        game.CreateSnapshot(0, revealAll: true).Players[0].SkillRuntimeStates!
            .Single(state => state.SkillId == HengyeSkillId).Usages
            .SingleOrDefault(usage =>
                usage.UsageId == "growth" &&
                usage.Scope == SkillUsageScope.Game)?.Count ?? 0;

    private static Fixture CreateFixture()
    {
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 1_024; seed++)
        {
            var game = CreateGame(registry, seed);
            ReachFirstHumanPlay(game);
            var snapshot = game.CreateSnapshot(0, revealAll: true);
            var eligible = game.GetHumanLegalActions()
                .Where(action => action.Kind == LegalActionKind.Slash && action.TargetSeats.Count == 1)
                .Select(action => action.TargetSeats[0])
                .Distinct()
                .Where(seat => snapshot.Players[seat].Role is Role.Rebel or Role.Renegade)
                .ToArray();
            if (eligible.Length == 0) continue;
            var targetSeat = eligible[0];
            var recipientSeat = snapshot.Players
                .Where(player => player.IsAlive && player.Seat != 0 && player.Seat != targetSeat)
                .Select(player => player.Seat)
                .First();
            return new Fixture(game, registry, targetSeat, recipientSeat);
        }
        throw new InvalidOperationException("No bounded Mou Lu Meng fixture exposed a safe adjacent target.");
    }

    private static void ReachFirstHumanPlay(GameEngine game)
    {
        Require(game.Submit(new StartGameCommand()).Accepted, "The Mou Lu Meng fixture failed to start.");
        for (var step = 0; step < 2_048; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return;
            if (game.PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } selection)
            {
                Require(selection.ValidContentIds.Contains(GeneralId, StringComparer.Ordinal),
                    $"The Mou Lu Meng candidate list omitted {GeneralId}.");
                Require(game.Submit(new SelectGeneralCommand(
                        0,
                        GeneralId,
                        game.Revision,
                        selection.PromptId)).Accepted,
                    "The Mou Lu Meng fixture could not select its formal general.");
                continue;
            }
            Require(game.PendingDecision?.PlayerSeat != 0,
                $"Unexpected human prompt {game.PendingDecision?.Kind} before Mou Lu Meng's first play phase.");
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "The Mou Lu Meng fixture could not reach its first play phase.");
        }
        throw new InvalidOperationException("The Mou Lu Meng fixture did not reach play in bounded steps.");
    }

    private static GameEngine CreateGame(ContentRegistry registry, int seed) =>
        GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 4,
            ModeId = ScenarioPackage.ModeId,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 60
        }, registry);

    private static ContentRegistry CreateRegistry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new ScenarioPackage());

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record Fixture(
        GameEngine Game,
        ContentRegistry Registry,
        int TargetSeat,
        int RecipientSeat);

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-mou-lu-meng-test-4";
        private const string DeckId = "fixture:mou-lu-meng-deck";
        private static readonly string[] BlankGeneralIds =
            ["fixture:mou-lu-meng-blank-1", "fixture:mou-lu-meng-blank-2", "fixture:mou-lu-meng-blank-3"];

        public PackageManifest Manifest { get; } = new(
            "mou-lu-meng-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 80, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var (id, index) in BlankGeneralIds.Select((id, index) => (id, index)))
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    $"英博目标{index + 1}",
                    "supporter",
                    "standard:none",
                    "qun",
                    BaseHp: 6));
            }

            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "谋吕蒙横野与英博测试牌堆",
                InitialHandSize: 12,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = Enumerable.Range(0, 256)
                    .Select(index => new ContentDeckPhysicalCard(
                        "standard:slash",
                        (Suit)(index % 4),
                        index % 13 + 1))
                    .ToArray()
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "谋吕蒙横野与英博测试",
                4,
                4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId,
                GeneralCandidateCount: 4,
                GeneralPoolIds: [GeneralId, .. BlankGeneralIds]));
        }
    }
}
