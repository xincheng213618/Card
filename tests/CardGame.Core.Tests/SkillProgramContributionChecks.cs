using CardGame.Content.Standard;
using CardGame.Core;

internal static class SkillProgramContributionChecks
{
    private const string ClassicSkillId = "scenario:classic-huangtian";
    private const string BoundarySkillId = "scenario:boundary-huangtian";
    private const string BindingId = "contribute";

    public static void Definitions()
    {
        var catalog = SkillProgramCatalog.Load(Rules, Presentation);
        var classic = catalog.Programs[ClassicSkillId];
        var boundary = catalog.Programs[BoundarySkillId];
        var classicContribution = classic.Contributions.Single();
        Require(classic is { RuntimeVersion: "skill-program-v58", MinimumRulesVersion: 168 } &&
                classicContribution is
                {
                    Id: BindingId,
                    OwnerRole: Role.Lord,
                    UsesPerPlayPhase: 1
                } &&
                classicContribution.ProviderFactions.SequenceEqual(["qun"]) &&
                classicContribution.CardKinds.SequenceEqual([CardKind.Dodge, CardKind.Lightning]) &&
                classicContribution.CardSuits.Count == 0,
            "Schema 7 must freeze the classic cross-owner contribution contract.");
        var boundaryContribution = boundary.Contributions.Single();
        Require(boundaryContribution.CardKinds.SequenceEqual([CardKind.Dodge]) &&
                boundaryContribution.CardSuits.SequenceEqual([Suit.Spade]),
            "Card kind and suit filters must remain separate union inputs.");
        RequireThrows<NotSupportedException>(() =>
            ((IList<string>)classicContribution.ProviderFactions).Add("wei"));
        RequireThrows<NotSupportedException>(() =>
            ((IList<CardKind>)classicContribution.CardKinds).Add(CardKind.Peach));

        AssertReject(Rules.Replace("\"schemaVersion\":58", "\"schemaVersion\":57", StringComparison.Ordinal),
            "expected 58");
        AssertReject(Rules.Replace("\"cardKinds\":[\"dodge\",\"lightning\"]",
            "\"cardKinds\":[]", StringComparison.Ordinal), "physical card kind or suit");
        AssertReject(Rules.Replace("\"providerFactions\":[\"qun\"]",
            "\"providerFactions\":[]", StringComparison.Ordinal), "faction id");
        AssertReject(Rules.Replace("\"usesPerPlayPhase\":1",
            "\"usesPerPlayPhase\":0", StringComparison.Ordinal), "positive");
    }

    public static void CrossOwnerFiltersLedgerAndReplay()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new FixturePackage());
        var game = FindFixture(registry);
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("Contribution fixture lost play prompt.");
        var before = game.CreateSnapshot(0, revealAll: true);
        var provider = before.Players.Single(player => player.Seat == 0);
        var lord = before.Players.Single(player => player.Role == Role.Lord);
        var actions = game.GetHumanLegalActions();
        var classic = actions.Single(action => action.ProgramSkillId == ClassicSkillId);
        var boundary = actions.Single(action => action.ProgramSkillId == BoundarySkillId);
        var expectedClassic = provider.Hand.Where(card => card.Kind is CardKind.Dodge or CardKind.Lightning)
            .Select(card => card.Id).Order().ToArray();
        var expectedBoundary = provider.Hand.Where(card => card.Kind == CardKind.Dodge || card.Suit == Suit.Spade)
            .Select(card => card.Id).Order().ToArray();
        Require(classic.ProgramSkillOwnerSeat == lord.Seat && boundary.ProgramSkillOwnerSeat == lord.Seat &&
                classic.SelectableTargetSeats.SequenceEqual([lord.Seat]) &&
                boundary.SelectableTargetSeats.SequenceEqual([lord.Seat]) &&
                classic.SelectableCardIds.SequenceEqual(expectedClassic) &&
                boundary.SelectableCardIds.SequenceEqual(expectedBoundary),
            "Each contribution must bind the provider's private physical cards to the living Lord skill owner.");

        var beforeJson = SnapshotJson.Serialize(before);
        var boundaryOnly = provider.Hand.First(card => card.Suit == Suit.Spade && card.Kind != CardKind.Dodge);
        var forgedOwner = game.Submit(new UseProgramSkillCommand(0, BoundarySkillId, BindingId,
            [boundaryOnly.Id], [lord.Seat], game.Revision, prompt.PromptId)
        {
            SkillOwnerSeat = 0
        });
        Require(!forgedOwner.Accepted && SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == beforeJson,
            "A forged skill-owner binding must be rejected atomically.");

        var accepted = game.Submit(new UseProgramSkillCommand(0, BoundarySkillId, BindingId,
            [boundaryOnly.Id], [lord.Seat], game.Revision, prompt.PromptId)
        {
            SkillOwnerSeat = lord.Seat
        });
        Require(accepted.Accepted, accepted.Error?.Message ?? "Boundary contribution was rejected.");
        var after = game.CreateSnapshot(0, revealAll: true);
        var resolved = game.Events.Select(item => item.Payload).OfType<ProgramSkillContributionResolvedEvent>()
            .Single(item => item.ProviderSeat == 0 && item.SkillId == BoundarySkillId);
        Require(after.Players[0].Hand.All(card => card.Id != boundaryOnly.Id) &&
                after.Players[lord.Seat].Hand.Any(card => card.Id == boundaryOnly.Id) &&
                resolved is { ProviderSeat: 0, SkillOwnerSeat: var ownerSeat, SkillId: BoundarySkillId,
                    ContributionId: BindingId, CardId: var movedId, CardSuit: Suit.Spade } &&
                ownerSeat == lord.Seat && movedId == boundaryOnly.Id &&
                game.CardMovements.Count(move => move.CardId == boundaryOnly.Id &&
                    move.Reason.Value == $"skill-program.{BoundarySkillId}.{BindingId}.contribute") == 2,
            "The contribution must publicly move the exact provider hand card through Processing to the skill owner.");

        var resumePlay = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(resumePlay.Accepted && game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
            resumePlay.Error?.Message ?? "Contribution did not return to the provider's play phase.");
        actions = game.GetHumanLegalActions();
        Require(actions.All(action => action.ProgramSkillId != BoundarySkillId) &&
                actions.Any(action => action.ProgramSkillId == ClassicSkillId),
            $"The per-phase ledger must be scoped to provider, owner, skill and contribution binding; remaining: " +
            string.Join(", ", actions.Where(action => action.ProgramSkillId is not null)
                .Select(action => $"{action.ProgramSkillId}/{action.ProgramActivationId}@{action.ProgramSkillOwnerSeat}")));
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                replay.GetHumanLegalActions().All(action => action.ProgramSkillId != BoundarySkillId) &&
                replay.Events.Select(item => item.Payload).OfType<ProgramSkillContributionResolvedEvent>()
                    .Count(item => item.ProviderSeat == 0 && item.SkillId == BoundarySkillId) == 1,
            "A completed contribution and its phase ledger must rebuild exactly from the accepted command journal.");

        RequireThrows<InvalidOperationException>(() => GameReplay.Restore(
            RoundTrip(game.CreateCheckpoint()) with { RulesVersion = 84 }, registry));

        var lordProvider = CreateGame(registry, game.Seed, Role.Lord);
        Require(lordProvider.Submit(new StartGameCommand()).Accepted &&
                lordProvider.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } &&
                lordProvider.GetHumanLegalActions().All(action => action.ProgramSkillOwnerSeat is null),
            "A Lord must not receive another-character contribution entry from their own skill.");
    }

    private static GameEngine FindFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = CreateGame(registry, seed, Role.Rebel);
            if (!game.Submit(new StartGameCommand()).Accepted) continue;
            for (var step = 0; step < 200 && game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; step++)
                if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted) break;
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) continue;
            var hand = game.CreateSnapshot(0, revealAll: true).Players[0].Hand;
            if (hand.Any(card => card.Kind == CardKind.Dodge && card.Suit != Suit.Spade) &&
                hand.Any(card => card.Suit == Suit.Spade && card.Kind != CardKind.Dodge) &&
                hand.Any(card => card.Kind != CardKind.Dodge && card.Suit != Suit.Spade) &&
                game.GetHumanLegalActions().Count(action => action.ProgramSkillOwnerSeat is not null) == 2)
                return game;
        }
        throw new InvalidOperationException("No bounded contribution fixture exposed both union filter arms.");
    }

    private static GameEngine CreateGame(ContentRegistry registry, int seed, Role humanRole) =>
        GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = humanRole,
            PlayerCount = 5,
            ModeId = FixturePackage.ModeId,
            UseInteractiveSetup = false,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 80
        }, registry);

    private sealed class FixturePackage : IGameContentPackage
    {
        internal const string ModeId = "identity:program-contribution-5";
        private static readonly string[] GeneralIds =
            Enumerable.Range(0, 5).Select(index => $"program-contribution:general-{index}").ToArray();

        public PackageManifest Manifest { get; } = new(
            "program-contribution-fixture", new Version(1, 0, 0),
            [new PackageDependency("standard", new Version(1, 11, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(Rules, Presentation);
            foreach (var program in catalog.Programs.Values.OrderBy(program => program.Id, StringComparer.Ordinal))
            {
                var display = catalog.Presentations[program.Id];
                builder.AddSkill(new ContentSkillDefinition(program.Id, display.Name, display.Description)
                {
                    Program = program
                });
            }
            foreach (var id in GeneralIds)
                builder.AddGeneral(new ContentGeneralDefinition(id, "黄天测试", "zhang_jiao", ClassicSkillId,
                    "qun", BaseHp: 4, AdditionalSkillIds: [BoundarySkillId]));
            var physicalCards = Enumerable.Range(0, 20).SelectMany(_ => new[]
            {
                new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, 2),
                new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 7),
                new ContentDeckPhysicalCard("standard:peach", Suit.Club, 3),
                new ContentDeckPhysicalCard("standard:slash", Suit.Diamond, 9)
            }).ToArray();
            builder.AddDeck(new ContentDeckRecipe("program-contribution:deck", "黄天候选牌", 4, 2, [])
            {
                PhysicalCards = physicalCards
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId, "跨拥有者贡献测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId: "program-contribution:deck",
                GeneralCandidateCount: 5,
                GeneralPoolIds: GeneralIds));
        }
    }

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static void AssertReject(string rules, string expected)
    {
        try { _ = SkillProgramCatalog.Load(rules, Presentation); }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains(expected, StringComparison.OrdinalIgnoreCase))
        { return; }
        throw new InvalidOperationException($"Expected rejection containing '{expected}'.");
    }

    private static void RequireThrows<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private const string Rules = """
        {"schemaVersion":58,"skills":[
          {"id":"scenario:classic-huangtian","revision":1,"modifiers":[],"viewAs":[],"activations":[],"triggers":[],
           "contributions":[{"id":"contribute","providerFactions":["qun"],"ownerRole":"lord",
             "cardKinds":["dodge","lightning"],"cardSuits":[],"usesPerPlayPhase":1}]},
          {"id":"scenario:boundary-huangtian","revision":1,"modifiers":[],"viewAs":[],"activations":[],"triggers":[],
           "contributions":[{"id":"contribute","providerFactions":["qun"],"ownerRole":"lord",
             "cardKinds":["dodge"],"cardSuits":["spade"],"usesPerPlayPhase":1}]}
        ]}
        """;

    private const string Presentation = """
        {"schemaVersion":3,"skills":{
          "scenario:classic-huangtian":{"name":"经典黄天","description":"其他群角色交给主公一张闪或闪电。"},
          "scenario:boundary-huangtian":{"name":"界黄天","description":"其他群角色交给主公一张闪或黑桃手牌。"}
        }}
        """;
}
