using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class GaoShunChecks
{
    private const string GeneralId = "classic:gao-shun";
    private const string XianzhenSkillId = "classic:xianzhen";
    private const string JinjiuSkillId = "classic:jinjiu";

    public static void XianzhenWinTargetsDistanceCountArmorAndReplays()
    {
        var fixture = Find(sourceWins: true, requireAlcohol: true);
        var game = fixture.Game;
        var targetSeat = fixture.TargetSeat;
        var targetDistance = game.GetCombatDistance(0, targetSeat);
        var attackRange = game.GetAttackRange(0);
        Require(targetDistance > attackRange,
            "The Xianzhen win fixture must keep its target outside normal attack range.");

        var used = BeginXianzhen(game, fixture.SourceCardId, targetSeat);
        Require(used.Accepted, used.Error?.Message ?? "Xianzhen could not start its Pindian.");

        var privatePrompt = GetHostPendingDecision(game);
        Require(privatePrompt is
                {
                    Kind: DecisionKind.SkillModule,
                    PlayerSeat: var responderSeat,
                    IsPrivate: true
                } &&
                responderSeat == targetSeat &&
                privatePrompt.Choices.All(choice =>
                    choice.Cards.Count == 1 && choice.Targets.Count == 0),
            "Xianzhen must publish one private exact-card Pindian prompt to its target.");
        var beforeForgeryRevision = game.Revision;
        var forged = game.Submit(new AnswerPromptCommand(
            0,
            privatePrompt!.PromptId,
            privatePrompt.Choices[0].Id,
            game.Revision));
        Require(!forged.Accepted && game.Revision == beforeForgeryRevision,
            "A non-owner must not answer the private Xianzhen Pindian prompt.");

        var paused = game.CreateCheckpoint();
        var replay = GameReplay.Restore(RoundTrip(paused), fixture.Registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                GetHostPendingDecision(replay)?.Kind == DecisionKind.SkillModule,
            "A paused Xianzhen opponent-card prompt must replay exactly.");

        ReachHumanPlay(game);
        ReachHumanPlay(replay);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            "The completed winning Xianzhen Pindian must replay exactly.");

        var result = game.Events.Select(item => item.Payload)
            .OfType<PindianResultDeterminedEvent>()
            .Last().Result;
        var runtime = game.CreateSnapshot(0, revealAll: true).Players[0]
            .SkillRuntimeStates!
            .Single(state => state.SkillId == XianzhenSkillId);
        var actions = game.GetHumanLegalActions();
        Require(result is { SourceSeat: 0, SourceWon: true } &&
                result.OpponentSeat == targetSeat &&
                runtime.DirectedPolicies is [var policy] &&
                policy.ActorSeat == 0 && policy.TargetSeat == targetSeat &&
                policy.Effects.HasFlag(DirectedTurnCardPolicyEffect.IgnoreDistance) &&
                policy.Effects.HasFlag(DirectedTurnCardPolicyEffect.BypassSlashLimit) &&
                policy.Effects.HasFlag(DirectedTurnCardPolicyEffect.IgnoreArmor) &&
                actions.Any(action => action.Kind == LegalActionKind.Slash &&
                    action.TargetSeat == targetSeat) &&
                actions.Any(action => action.Kind == LegalActionKind.Snatch &&
                    action.TargetSeat == targetSeat) &&
                actions.Any(action => action.Kind == LegalActionKind.SupplyShortage &&
                    action.TargetSeat == targetSeat),
            "Winning Xianzhen must record its exact target and remove card-use distance only against that target.");

        SetSlashCount(game, 1);
        var exhausted = game.GetHumanLegalActions();
        var slashActions = exhausted.Where(action => action.Kind == LegalActionKind.Slash).ToArray();
        Require(slashActions.Length > 0 &&
                slashActions.All(action => action.TargetSeats.SequenceEqual([targetSeat])),
            "After the normal Slash limit is exhausted, Xianzhen must allow further Slashes only against its Pindian target.");

        var slash = slashActions[0];
        var played = game.Submit(new PlayCardCommand(
            0,
            slash.CardId!.Value,
            slash.TargetSeats,
            game.Revision,
            RequirePrompt(game, DecisionKind.PlayCard).PromptId,
            slash.PlayedCardKind) { ConversionSource = slash.ConversionSource,
                AdditionalConversionSources = slash.AdditionalConversionSources });
        Require(played.Accepted &&
                game.Events.Select(item => item.Payload).OfType<CardUsedEvent>().Last() is
                { SourceSeat: 0, IgnoresArmor: true },
            played.Error?.Message ?? "A winning Xianzhen Slash must ignore the selected target's armor.");

    }

    private static CommandResult BeginXianzhen(GameEngine game, int sourceCardId, int targetSeat)
    {
        var play = RequirePrompt(game, DecisionKind.PlayCard);
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseProgramSkill &&
            candidate.ProgramSkillId == XianzhenSkillId &&
            candidate.ProgramActivationId == "challenge");
        Require(action.SelectableTargetSeats.Contains(targetSeat),
            "The Xianzhen target must be published by the shared activation.");
        var used = game.Submit(new UseProgramSkillCommand(
            0, XianzhenSkillId, "challenge", [], [targetSeat], game.Revision, play.PromptId));
        if (!used.Accepted) return used;
        var sourcePrompt = RequirePrompt(game, DecisionKind.SkillModule);
        var choice = sourcePrompt.Choices.Single(item => item.Cards.SequenceEqual([sourceCardId]));
        return game.Submit(new AnswerPromptCommand(
            0, sourcePrompt.PromptId, choice.Id, game.Revision));
    }

    private static Fixture Find(bool sourceWins, bool requireAlcohol)
    {
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = CreateGame(registry, seed);
            if (!StartAndSelect(game)) continue;
            var play = Reach(game, DecisionKind.PlayCard, 256);
            if (play is null) continue;

            var snapshot = game.CreateSnapshot(0, revealAll: true);
            var sourceHand = snapshot.Players[0].Hand;
            var target = snapshot.Players[2];
            if ((requireAlcohol && sourceHand.All(card => card.Kind != CardKind.Alcohol)) ||
                sourceHand.All(card => card.Kind != CardKind.Snatch) ||
                sourceHand.All(card => card.Kind != CardKind.SupplyShortage) ||
                target.Hand.Count == 0)
            {
                continue;
            }

            var opponentMax = target.Hand.Max(card => card.Rank);
            var sourceCard = sourceWins
                ? sourceHand.Where(card => card.Rank > opponentMax)
                    .OrderByDescending(card => card.Rank)
                    .FirstOrDefault()
                : sourceHand.Where(card => card.Rank <= opponentMax)
                    .OrderBy(card => card.Rank)
                    .FirstOrDefault();
            if (sourceCard is null) continue;
            return new Fixture(game, registry, sourceCard.Id, target.Seat);
        }

        throw new InvalidOperationException(
            $"No bounded Gao Shun fixture exposed a {(sourceWins ? "winning" : "losing")} Xianzhen branch.");
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
            MaxTurns = 40
        }, registry);

    private static bool StartAndSelect(GameEngine game)
    {
        if (!game.Submit(new StartGameCommand()).Accepted) return false;
        var prompt = game.PendingDecision;
        return prompt is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } &&
               prompt.ValidContentIds.Contains(GeneralId, StringComparer.Ordinal) &&
               game.Submit(new SelectGeneralCommand(
                   0,
                   GeneralId,
                   game.Revision,
                   prompt.PromptId)).Accepted;
    }

    private static PendingDecision? Reach(GameEngine game, DecisionKind kind, int limit)
    {
        for (var step = 0; step < limit; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0 } pending && pending.Kind == kind)
                return pending;
            if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted)
                return null;
        }
        return null;
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 256; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return;
            var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(result.Accepted, result.Error?.Message ?? "The Gao Shun fixture could not advance.");
        }
        throw new InvalidOperationException("The Gao Shun fixture did not return to play in bounded steps.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { PlayerSeat: 0 } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected human {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");

    private static PendingDecision? GetHostPendingDecision(GameEngine game)
    {
        var field = typeof(GameEngine).GetField(
            "_pendingDecision",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine pending-decision store was not found.");
        return (PendingDecision?)field.GetValue(game);
    }

    private static void SetSlashCount(GameEngine game, int value) =>
        typeof(GameEngine).GetField(
                "_slashCountThisTurn",
                BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(game, value);

    private static ContentRegistry CreateRegistry() =>
        ContentRegistry.Build(
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
        int SourceCardId,
        int TargetSeat);

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-gao-shun-test-4";
        private const string DeckId = "fixture:gao-shun-deck";
        private static readonly string[] BlankGeneralIds =
            ["fixture:gao-shun-blank-1", "fixture:gao-shun-blank-2", "fixture:gao-shun-blank-3"];

        public PackageManifest Manifest { get; } = new(
            "gao-shun-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 83, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var (id, index) in BlankGeneralIds.Select((id, index) => (id, index)))
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    $"陷阵目标{index + 1}",
                    "supporter",
                    "standard:none",
                    "qun",
                    BaseHp: 8));
            }

            var cards = new List<ContentDeckPhysicalCard>();
            Add("standard:slash", 96);
            Add("standard:alcohol", 96);
            Add("standard:snatch", 64);
            Add("standard:supply_shortage", 64);
            Add("standard:dodge", 64);
            Add("standard:peach", 32);
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "高顺陷阵测试牌堆",
                InitialHandSize: 12,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = cards.ToArray()
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "高顺陷阵测试",
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
            return;

            void Add(string cardId, int count)
            {
                for (var index = 0; index < count; index++)
                {
                    cards.Add(new ContentDeckPhysicalCard(
                        cardId,
                        (Suit)(index % 4),
                        index % 13 + 1));
                }
            }
        }
    }
}
