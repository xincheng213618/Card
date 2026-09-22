using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class LiuBiaoChecks
{
    private const string GeneralId = "classic:liu-biao";
    private const string ZishouSkillId = "classic:zishou";
    private const string ZongshiSkillId = "classic:zongshi";

    public static void ContentPromptAndRulesBoundary()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        var previous = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 105, 0));
        var zishou = current.Skills[ZishouSkillId];
        Require(current.Packages.Any(package =>
                    package.Id == "standard-classic-generals" &&
                    package.Version == new Version(1, 107, 0)) &&
                current.Generals[GeneralId] is
                {
                    FactionId: "qun",
                    BaseHp: 3,
                    Gender: GeneralGender.Male,
                    PortraitKey: "liu_biao",
                    SkillIds: var skillIds
                } && skillIds.SequenceEqual([ZishouSkillId, ZongshiSkillId]) &&
                zishou.LegacyKind is null &&
                zishou.Program is { RuntimeVersion: "skill-program-v20", MinimumRulesVersion: 125 } &&
                current.Skills.ContainsKey(ZongshiSkillId) &&
                previous.Skills[ZishouSkillId] is { LegacyKind: SkillKind.Zishou, Program: null } &&
                current.ContentHash != previous.ContentHash,
            "Current package must retain the 1.106 Zishou migration without mutating 1.105.0.");

        var fixture = FindFixture();
        var game = fixture.Game;
        var prompt = RequirePrompt(game, DecisionKind.ProgramTrigger);
        Require(prompt.IsPrivate &&
                prompt.Choices.Count == 2 &&
                prompt.Choices.Select(choice => choice.Parameters.GetValueOrDefault("program-action"))
                    .Order(StringComparer.Ordinal)
                    .SequenceEqual(["activate", "skip"]) &&
                zishou.Program!.Triggers.Single().Effects[0].NumberExpression ==
                    SkillProgramNumberExpression.LivingFactionCount,
            "Zishou must publish one generic private choice backed by livingFactionCount.");

        var before = SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
        var restored = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) == before &&
                restored.PendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true },
            "A paused Zishou draw choice must replay exactly.");

        var revision = game.Revision;
        var forged = game.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            new ChoiceId("zishou.forged"),
            game.Revision));
        Require(!forged.Accepted && game.Revision == revision &&
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == before,
            "A forged Zishou answer must be rejected atomically.");

        var skipped = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        var skippedBefore = skipped.CreateSnapshot(0, revealAll: true).Players[0].HandCount;
        Answer(skipped, "zishou-skip");
        ReachHumanPlay(skipped);
        Require(skipped.CreateSnapshot(0, revealAll: true).Players[0].HandCount == skippedBefore + 2 &&
                skipped.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(item =>
                    item.SkillId == ZishouSkillId && !item.Activated),
            "Skipping Zishou must keep normal drawing and create no turn restriction.");

    }

    public static void ZishouTargetsAndZongshiHandLimit()
    {
        var fixture = FindFixture();
        var game = fixture.Game;
        var beforeHand = game.CreateSnapshot(0, revealAll: true).Players[0].HandCount;
        Answer(game, "zishou-use");
        ReachHumanPlay(game);

        var snapshot = game.CreateSnapshot(0, revealAll: true);
        var actions = game.GetHumanLegalActions();
        Require(snapshot.Players[0].HandCount == beforeHand + 6 &&
                game.Events.Select(item => item.Payload).OfType<CardTargetRestrictionGrantedEvent>().Any(item =>
                    item.Restriction.Source.SkillId == ZishouSkillId),
            "Using Zishou must draw normal two plus four living factions and record one turn state.");
        Require(actions.All(action =>
                    action.Kind is not (LegalActionKind.BarbarianAssault or LegalActionKind.ArrowBarrage) &&
                    action.TargetSeats.All(targetSeat => targetSeat == 0)) &&
                actions.Any(action => action.Kind == LegalActionKind.DrawTwo) &&
                actions.Any(action => action.Kind == LegalActionKind.Lightning && action.TargetSeat == 0) &&
                actions.Any(action => action.Kind == LegalActionKind.IronChain && action.TargetSeat == 0) &&
                actions.Any(action => action.Kind == LegalActionKind.PeachGarden) &&
                actions.Any(action => action.Kind == LegalActionKind.FiveGrains),
            "Zishou must remove every other-target card path while retaining self-use and self-only global benefits.");

        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                replay.GetHumanLegalActions().All(action =>
                    action.Kind is not (LegalActionKind.BarbarianAssault or LegalActionKind.ArrowBarrage) &&
                    action.TargetSeats.All(targetSeat => targetSeat == 0)),
            "The resolved Zishou draw and exact play restriction must replay.");

        var fiveGrains = actions.First(action => action.Kind == LegalActionKind.FiveGrains);
        Play(game, fiveGrains);
        Require(game.Events.Select(item => item.Payload).OfType<TargetsConfirmedEvent>()
                .Last(item => item.TargetSeats.Count > 0).TargetSeats.SequenceEqual([0]),
            "Zishou must prune Five Grains to Liu Biao alone before the public target event.");

        var handLimitFixture = FindFixture();
        var ownerHp = handLimitFixture.Game.CreateSnapshot(0, revealAll: true).Players[0].Hp;
        var fullLimit = GetHandLimit(handLimitFixture.Game, 0);
        SetAlive(handLimitFixture.Game, 1, false);
        var reducedLimit = GetHandLimit(handLimitFixture.Game, 0);
        Require(fullLimit == ownerHp + 4 && reducedLimit == ownerHp + 3,
            "Zongshi must add the live distinct-faction count to Liu Biao's current-HP hand limit.");

    }

    private static void Play(GameEngine game, LegalAction action)
    {
        var prompt = RequirePrompt(game, DecisionKind.PlayCard);
        var result = game.Submit(new PlayCardCommand(
            0,
            action.CardId!.Value,
            action.TargetSeats,
            game.Revision,
            prompt.PromptId,
            action.PlayedCardKind));
        Require(result.Accepted, result.Error?.Message ?? "The Zishou card use was rejected.");
    }

    private static void Answer(GameEngine game, string action)
    {
        var prompt = RequirePrompt(game, DecisionKind.ProgramTrigger);
        var programAction = action == "zishou-use" ? "activate" : "skip";
        var choice = prompt.Choices.Single(candidate =>
            candidate.Parameters.GetValueOrDefault("program-action") == programAction);
        var result = game.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            choice.Id,
            game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "The Zishou choice was rejected.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { PlayerSeat: 0 } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected human {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 512; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return;
            Require(game.PendingDecision?.PlayerSeat != 0,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while returning to play.");
            var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(result.Accepted, result.Error?.Message ?? "The Liu Biao fixture could not advance.");
        }
        throw new InvalidOperationException("The Liu Biao fixture did not return to play in bounded steps.");
    }

    private static Fixture FindFixture()
    {
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 2_048; seed++)
        {
            var game = CreateGame(registry, seed);
            StartAndSelect(game);
            if (game.PendingDecision?.Kind != DecisionKind.ProgramTrigger) continue;
            var handKinds = game.CreateSnapshot(0, revealAll: true).Players[0].Hand
                .Select(card => card.Kind)
                .ToHashSet();
            if (RequiredKinds.All(handKinds.Contains))
            {
                return new Fixture(game, registry, seed);
            }
        }
        throw new InvalidOperationException("No bounded Liu Biao fixture exposed every required self/other/global card path.");
    }

    private static void StartAndSelect(GameEngine game)
    {
        Require(game.Submit(new StartGameCommand()).Accepted, "The Liu Biao fixture failed to start.");
        var selection = RequirePrompt(game, DecisionKind.SelectGeneral);
        Require(selection.ValidContentIds.Contains(GeneralId, StringComparer.Ordinal),
            "The Liu Biao fixture omitted the formal general.");
        Require(game.Submit(new SelectGeneralCommand(
                0,
                GeneralId,
                game.Revision,
                selection.PromptId)).Accepted,
            "The Liu Biao fixture could not select its formal general.");
        for (var step = 0; step < 256; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger or DecisionKind.PlayCard })
            {
                return;
            }
            Require(game.PendingDecision?.PlayerSeat != 0,
                $"Unexpected human prompt {game.PendingDecision?.Kind} before Liu Biao's draw phase.");
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "The Liu Biao fixture could not reach its draw phase.");
        }
        throw new InvalidOperationException("The Liu Biao fixture did not reach Zishou or play in bounded steps.");
    }

    private static int GetHandLimit(GameEngine game, int seat)
    {
        var playersField = typeof(GameEngine).GetField(
            "_players",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine player store was not found.");
        var players = (System.Collections.IList)playersField.GetValue(game)!;
        var method = typeof(GameEngine).GetMethod(
            "GetHandLimit",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine hand-limit query was not found.");
        return (int)method.Invoke(game, [players[seat]])!;
    }

    private static void SetAlive(GameEngine game, int seat, bool value)
    {
        var playersField = typeof(GameEngine).GetField(
            "_players",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine player store was not found.");
        var players = (System.Collections.IList)playersField.GetValue(game)!;
        players[seat]!.GetType().GetProperty("IsAlive")!.SetValue(players[seat], value);
    }

    private static GameEngine CreateGame(
        ContentRegistry registry,
        int seed,
        int rulesVersion = GameCheckpoint.CurrentRulesVersion)
    {
        var game = GameEngine.CreateStandard(new GameOptions
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
        return rulesVersion == GameCheckpoint.CurrentRulesVersion
            ? game
            : GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
    }

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

    private static readonly CardKind[] RequiredKinds =
    [
        CardKind.Slash,
        CardKind.DrawTwo,
        CardKind.BarbarianAssault,
        CardKind.ArrowBarrage,
        CardKind.PeachGarden,
        CardKind.FiveGrains,
        CardKind.Lightning,
        CardKind.IronChain
    ];

    private sealed record Fixture(GameEngine Game, ContentRegistry Registry, int Seed);

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-liu-biao-test-4";
        private const string DeckId = "fixture:liu-biao-deck";
        private static readonly (string Id, string Faction)[] BlankGenerals =
        [
            ("fixture:liu-biao-wei", "wei"),
            ("fixture:liu-biao-shu", "shu"),
            ("fixture:liu-biao-wu", "wu")
        ];

        public PackageManifest Manifest { get; } = new(
            "liu-biao-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 84, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var (id, faction) in BlankGenerals)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    $"宗室目标-{faction}",
                    "supporter",
                    "standard:none",
                    faction,
                    BaseHp: 8));
            }

            var cards = new List<ContentDeckPhysicalCard>();
            Add("standard:slash", 48);
            Add("standard:draw_two", 48);
            Add("standard:barbarian_assault", 48);
            Add("standard:arrow_barrage", 48);
            Add("standard:peach_garden", 48);
            Add("standard:five_grains", 48);
            Add("standard:lightning", 48);
            Add("standard:iron_chain", 48);
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "刘表自守测试牌堆",
                InitialHandSize: 24,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = cards.ToArray()
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "刘表自守测试",
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
                GeneralPoolIds: [GeneralId, .. BlankGenerals.Select(item => item.Id)]));
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
