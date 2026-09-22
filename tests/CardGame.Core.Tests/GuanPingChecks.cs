using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class GuanPingChecks
{
    private const int HumanSeat = 0;
    private const string GeneralId = "classic:guan-ping";

    public static void ContentAndPackageBoundary()
    {
        var previous = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 96, 0));
        var current = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 97, 0));
        Require(!previous.Generals.ContainsKey(GeneralId) &&
                !previous.Skills.ContainsKey("classic:longyin"),
            "Package 1.96.0 must retain the pre-Guan-Ping content boundary.");
        Require(current.Packages.Single(package => package.Id == "standard-classic-generals").Version ==
                    new Version(1, 97, 0) &&
                current.Generals[GeneralId] is
                {
                    BaseHp: 4,
                    FactionId: "shu",
                    Gender: GeneralGender.Male,
                    PortraitKey: "guan_ping"
                } guanPing &&
                guanPing.SkillIds.SequenceEqual(["classic:longyin"]) &&
                current.Skills["classic:longyin"] is
                {
                    LegacyKind: SkillKind.Longyin,
                    ExecutionForms: SkillExecutionForm.Trigger,
                    ActionForms: SkillActionForm.None
                } &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(GeneralId),
            "Package 1.97.0 must publish complete classic Guan Ping with optional Longyin.");
    }

    public static void RedSlashDrawsAndReplays() => CheckPaidSlash(requireRed: true);

    private static void CheckPaidSlash(bool requireRed)
    {
        var fixture = FindSlashGame(requireRed);
        var game = fixture.Game;
        var before = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].HandCount;
        Require(Play(game, fixture.SlashAction, RequirePrompt(game, DecisionKind.PlayCard)).Accepted,
            "The Slash was rejected.");
        var activation = RequireLongyin(game);
        Require(activation.IsPrivate && activation.SourceSeat == HumanSeat &&
                game.CreateSnapshot(1).PendingDecision is null,
            "The committed-card trigger must be private and keep its actual actor.");
        Activate(game);
        var payment = RequireLongyin(game);
        var cost = payment.Choices.Single(choice => choice.Cards.SequenceEqual([fixture.CostCardId]));
        Require(payment.ValidCardIds.Contains(fixture.CostCardId) &&
                game.CreateSnapshot(1).PendingDecision is null,
            "The owned-card payment must expose this owner's cards only to its chooser.");
        var checkpoint = RoundTrip(game.CreateCheckpoint());
        var replay = GameReplay.Restore(checkpoint, fixture.Registry);
        var firstMove = game.CardMovements.Count;
        Answer(game, cost);
        Answer(replay, RequireLongyin(replay).Choices.Single(choice => choice.Id == cost.Id));
        var moves = game.CardMovements.Skip(firstMove).ToArray();
        Require(moves.Count(move => move.CardId == fixture.CostCardId &&
                    move.From == CardLocation.Hand(HumanSeat) && move.To == CardLocation.DiscardPile) == 1 &&
                moves.Count(move => move.To == CardLocation.Hand(HumanSeat) &&
                    move.Reason.Value == "skill-program.classic:longyin.draw") == (requireRed ? 1 : 0),
            "The combination must pay exactly once and draw exactly one card only for a red Slash.");
        Require(game.Events.Select(item => item.Payload).OfType<CardUseDebitRefundedEvent>()
                    .Count(item => item.Debit.ActorSeat == HumanSeat) == 1 &&
                game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].HandCount == before - (requireRed ? 1 : 2) &&
                State(replay) == State(game) && Events(replay).SequenceEqual(Events(game)),
            "The actual debit must be refunded once and the suspended payment must replay exactly.");
        ReachHumanPlay(game);
        Require(game.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.Slash),
            "A refunded Slash must allow another real Slash in the same phase.");
        if (requireRed) return;
        var second = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash);
        Require(Play(game, second, RequirePrompt(game, DecisionKind.PlayCard)).Accepted,
            "The restored second Slash must actually submit.");
        var refundCount = game.Events.Count(item => item.Payload is CardUseDebitRefundedEvent);
        var moveCount = game.CardMovements.Count;
        Answer(game, RequireLongyin(game).Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "skip"));
        Require(game.Events.Count(item => item.Payload is CardUseDebitRefundedEvent) == refundCount &&
                game.CardMovements.Skip(moveCount).All(move =>
                    !move.Reason.Value.StartsWith("skill-program.classic:longyin", StringComparison.Ordinal)),
            "Skipping must neither pay, draw nor refund the second debit.");
        ReachHumanPlay(game);
        Require(game.GetHumanLegalActions().All(action => action.Kind != LegalActionKind.Slash),
            "The second counted Slash must exhaust the ordinary phase allowance.");
    }

    public static void BlackSlashUncountsWithoutDrawingAndSkipPreservesLimit() => CheckPaidSlash(requireRed: false);

    public static void OtherCharactersSlashOffersPrivateChoiceAndReplays()
    {
        var fixture = FindOtherSourceLongyinGame();
        var game = fixture.Game;
        var actor = RequireLongyin(game).SourceSeat!.Value;
        Activate(game);
        var payment = RequireLongyin(game);
        var owner = game.CreateSnapshot(HumanSeat).Players[HumanSeat];
        Require(actor != HumanSeat && game.State.CurrentSeat == actor && game.State.Phase == TurnPhase.Play &&
                payment.ValidCardIds.Count == owner.HandCount + owner.Equipment.Count &&
                game.CreateSnapshot(actor).PendingDecision is null,
            "Another actor's Play Slash must offer this holder a private owned-card payment.");
        var cost = payment.Choices.First();
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        Answer(game, cost);
        Answer(replay, RequireLongyin(replay).Choices.Single(choice => choice.Id == cost.Id));
        Require(game.Events.Select(item => item.Payload).OfType<CardUseDebitRefundedEvent>().Last().Debit.ActorSeat == actor &&
                game.CardMovements.Any(move => move.CardId == cost.Cards.Single() &&
                    move.From == CardLocation.Hand(HumanSeat) && move.To == CardLocation.DiscardPile) &&
                State(replay) == State(game) && Events(replay).SequenceEqual(Events(game)),
            "The observer must refund the actor's debit, pay its own card and replay identically.");
    }

    private static PendingDecision RequireLongyin(GameEngine game) =>
        game.PendingDecision is { PlayerSeat: HumanSeat, SkillPrompt.SkillId: "classic:longyin" } prompt
            ? prompt : throw new InvalidOperationException("Expected the common Longyin program prompt.");

    private static void Activate(GameEngine game) => Answer(game, RequireLongyin(game).Choices.Single(choice =>
        choice.Parameters.GetValueOrDefault("program-action") == "activate"));

    private static SlashFixture FindSlashGame(bool requireRed)
    {
        var registry = Registry();
        for (var seed = 1; seed <= 1_024; seed++)
        {
            var game = CreateGame(registry, seed);
            ReachHumanPlay(game);
            var snapshot = game.CreateSnapshot(HumanSeat, revealAll: true);
            var hand = snapshot.Players[HumanSeat].Hand;
            var cards = hand.ToDictionary(card => card.Id);
            var slashActions = game.GetHumanLegalActions()
                .Where(action => action.Kind == LegalActionKind.Slash && action.CardId is not null)
                .Where(action => IsRed(cards[action.CardId!.Value].Suit) == requireRed)
                .ToArray();
            if (slashActions.Length == 0 ||
                hand.Count(card => card.Kind == CardKind.Slash) < 2)
            {
                continue;
            }

            var action = slashActions[0];
            var cost = hand.FirstOrDefault(card =>
                card.Id != action.CardId && card.Kind != CardKind.Slash);
            if (cost is not null)
            {
                return new SlashFixture(game, registry, action, cost.Id, cards);
            }
        }
        throw new InvalidOperationException(requireRed
            ? "No bounded Guan Ping fixture exposed a red Slash with a retained Slash and separate cost."
            : "No bounded Guan Ping fixture exposed a black Slash with a retained Slash and separate cost.");
    }

    private static Fixture FindOtherSourceLongyinGame()
    {
        var registry = Registry();
        for (var seed = 1; seed <= 1_024; seed++)
        {
            var game = CreateGame(registry, seed);
            ReachHumanPlay(game);
            var play = RequirePrompt(game, DecisionKind.PlayCard);
            var ended = game.Submit(new EndPlayPhaseCommand(HumanSeat, game.Revision, play.PromptId));
            Require(ended.Accepted, ended.Error?.Message ?? "The cross-seat Longyin fixture could not end Play.");
            for (var step = 0; step < 512 && game.State.Winner == Winner.None; step++)
            {
                if (game.PendingDecision is { PlayerSeat: HumanSeat } prompt)
                {
                    if (prompt.SkillPrompt?.SkillId == "classic:longyin" && prompt.SourceSeat != HumanSeat)
                        return new Fixture(game, registry);
                    if (prompt.Kind == DecisionKind.PlayCard) break;
                    var decline = prompt.Choices.FirstOrDefault(choice => choice.Cards.Count == 0);
                    if (decline is null) break;
                    Answer(game, decline);
                    continue;
                }
                var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
                if (!advanced.Accepted) break;
            }
        }
        throw new InvalidOperationException(
            "No bounded Guan Ping fixture observed another character's Play-phase Slash.");
    }

    private static GameEngine CreateGame(ContentRegistry registry, int seed)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 6,
            ModeId = ScenarioPackage.ModeId,
            HumanSeat = HumanSeat,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 20
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "The Guan Ping fixture failed to start.");
        var selection = RequirePrompt(game, DecisionKind.SelectGeneral);
        Require(selection.ValidContentIds.Contains(GeneralId),
            "The Guan Ping fixture must offer its formal general.");
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat,
            GeneralId,
            game.Revision,
            selection.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "The fixture could not select Guan Ping.");
        return game;
    }

    private static CommandResult Play(GameEngine game, LegalAction action, PendingDecision prompt) =>
        game.Submit(new PlayCardCommand(
            HumanSeat,
            action.CardId!.Value,
            action.TargetSeats,
            game.Revision,
            prompt.PromptId,
            action.PlayedCardKind));

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 256; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            if (game.PendingDecision is { PlayerSeat: HumanSeat } prompt)
            {
                var safe = prompt.Choices.FirstOrDefault(choice =>
                               choice.Parameters.Values.Any(value =>
                                   value.Contains("skip", StringComparison.Ordinal) ||
                                   value.Contains("take-damage", StringComparison.Ordinal))) ??
                           prompt.Choices.FirstOrDefault(choice => choice.Cards.Count == 0) ??
                           throw new InvalidOperationException(
                               $"The Guan Ping fixture cannot safely answer {prompt.Kind}.");
                Answer(game, safe);
                continue;
            }
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "The Guan Ping fixture could not advance.");
        }
        throw new InvalidOperationException("The Guan Ping fixture did not reach the human Play prompt.");
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("No prompt is pending.");
        var result = game.Submit(new AnswerPromptCommand(
            prompt.PlayerSeat,
            prompt.PromptId,
            choice.Id,
            game.Revision));
        Require(result.Accepted, result.Error?.Message ?? $"The {prompt.Kind} answer was rejected.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { PlayerSeat: HumanSeat } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected human {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(new Version(1, 97, 0)),
        new ScenarioPackage());

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static string State(GameEngine game) =>
        SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true));

    private static IReadOnlyList<string> Events(GameEngine game) => game.Events
        .Select(item =>
            $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static bool IsRed(Suit suit) => suit is Suit.Heart or Suit.Diamond;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record SlashFixture(
        GameEngine Game,
        ContentRegistry Registry,
        LegalAction SlashAction,
        int CostCardId,
        IReadOnlyDictionary<int, CardSnapshot> InitialHand)
    {
        public bool IsCardRed(int cardId) => GuanPingChecks.IsRed(InitialHand[cardId].Suit);
    }

    private sealed record Fixture(GameEngine Game, ContentRegistry Registry);

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-guan-ping-test-6";
        private const string DeckId = "fixture:guan-ping-slashes";
        private static readonly string[] TargetIds =
        [
            "fixture:guan-ping-target-1", "fixture:guan-ping-target-2",
            "fixture:guan-ping-target-3", "fixture:guan-ping-target-4",
            "fixture:guan-ping-target-5"
        ];

        public PackageManifest Manifest { get; } = new(
            "guan-ping-scenario",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 97, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in TargetIds)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "关平测试目标",
                    "supporter",
                    "standard:none",
                    "wei",
                    BaseHp: 8));
            }

            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "关平龙吟红黑杀测试牌堆",
                InitialHandSize: 10,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = Enumerable.Range(0, 240)
                    .Select(index => index % 3 == 0
                        ? new ContentDeckPhysicalCard(
                            "standard:dodge",
                            (Suit)(index % 4),
                            index % 13 + 1)
                        : new ContentDeckPhysicalCard(
                            "standard:slash",
                            (Suit)(index % 4),
                            index % 13 + 1))
                    .ToArray()
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "六人经典身份（关平场景）",
                MinPlayers: 6,
                MaxPlayers: 6,
                RoleCounts: new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 3,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId,
                GeneralCandidateCount: 6,
                GeneralPoolIds: [GeneralId, .. TargetIds]));
        }
    }
}
