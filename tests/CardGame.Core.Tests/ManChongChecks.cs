using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ManChongChecks
{
    private const int HumanSeat = 0;
    private const string GeneralId = "classic:man-chong";

    public static void JunxingUsesExactCategoriesAndReplaysBothBranches()
    {
        var fixture = FindJunxingGame();
        var game = fixture.Game;
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseProgramSkill &&
            candidate.ProgramSkillId == "classic:junxing" &&
            candidate.ProgramActivationId == "category-punishment");
        var snapshot = game.CreateSnapshot(HumanSeat, revealAll: true);
        var owner = snapshot.Players[HumanSeat];
        var selection = FindJunxingSelection(snapshot, action) ??
            throw new InvalidOperationException("The Junxing fixture has no category-divergent target hand.");
        var play = RequirePrompt(game, DecisionKind.PlayCard);
        var started = game.Submit(new UseProgramSkillCommand(
            HumanSeat,
            "classic:junxing",
            "category-punishment",
            [selection.CostCardId],
            [selection.TargetSeat],
            game.Revision,
            play.PromptId));
        Require(started.Accepted, started.Error?.Message ?? "The exact Junxing activation was rejected.");

        var targetPrompt = RequirePromptForSeat(game, selection.TargetSeat, DecisionKind.ProgramTrigger);
        var privatePrompt = game.CreateSnapshot(selection.TargetSeat).PendingDecision;
        var costCategory = CardCatalog.Get(
            owner.Hand.Single(card => card.Id == selection.CostCardId).Kind).CategoryName;
        Require(targetPrompt is
                {
                    PlayerSeat: var responderSeat,
                    IsPrivate: true,
                    SourceSeat: HumanSeat
                } &&
                responderSeat == selection.TargetSeat &&
                privatePrompt?.PromptId == targetPrompt.PromptId &&
                game.CreateSnapshot(HumanSeat).PendingDecision is null &&
                targetPrompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "different-category-decline") &&
                targetPrompt.Choices
                    .Where(choice => choice.Parameters.GetValueOrDefault("program-action") == "different-category-discard")
                    .All(choice => CardCatalog.Get(
                        snapshot.Players[selection.TargetSeat].Hand.Single(card =>
                            card.Id == choice.Cards.Single()).Kind).CategoryName != costCategory),
            "Junxing must expose its exact private responder, always offer turn-and-draw, and publish only other-category discard cards.");

        var discardedCost = game.CardMovements.Single(move =>
            move.CardId == selection.CostCardId &&
            move.Reason == new CardMoveReason("skill-program.classic:junxing.MoveBoundCards") &&
            move.To == CardLocation.DiscardPile);
        Require(discardedCost.From == CardLocation.Hand(HumanSeat),
            "Junxing must commit the exact captured activation card from the owner's hand into discard.");

        var discardReplay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        AdvanceOne(game);
        AdvanceOne(discardReplay);
        var discardResolved = game.Events.Select(item => item.Payload)
            .OfType<ProgramCategoryDiscardResolvedEvent>()
            .Last(item => item.SkillId == "classic:junxing");
        Require(discardResolved is
                {
                    OwnerSeat: HumanSeat,
                    DiscardedCardId: not null
                } &&
                discardResolved.ChooserSeat == selection.TargetSeat &&
                discardResolved.SourceBind == "cost-cards" &&
                discardResolved.ResultBind == "response" &&
                game.CardMovements.Any(move =>
                    move.CardId == discardResolved.DiscardedCardId &&
                    move.Reason == new CardMoveReason("skill-program.classic:junxing.ChooseDifferentCategoryDiscard") &&
                    move.To == CardLocation.DiscardPile) &&
                game.Events.Select(item => item.Payload).OfType<ProgramSkillResolvedEvent>().Any(item =>
                    item.SkillId == "classic:junxing" && item.ActivationId == "category-punishment" && item.Completed) &&
                game.GetHumanLegalActions().All(candidate => candidate.ProgramSkillId != "classic:junxing"),
            $"Junxing's discard branch must spend one exact legal response card and consume the phase limit " +
            $"(owner={discardResolved.OwnerSeat}, chooser={discardResolved.ChooserSeat}, " +
            $"discard={discardResolved.DiscardedCardId?.ToString() ?? "none"}, " +
            $"move={game.CardMovements.Any(move => move.CardId == discardResolved.DiscardedCardId && move.Reason == new CardMoveReason("skill-program.classic:junxing.ChooseDifferentCategoryDiscard") && move.To == CardLocation.DiscardPile)}, " +
            $"completed={game.Events.Select(item => item.Payload).OfType<ProgramSkillResolvedEvent>().Any(item => item.SkillId == "classic:junxing" && item.ActivationId == "category-punishment" && item.Completed)}, " +
            $"available={game.GetHumanLegalActions().Any(candidate => candidate.ProgramSkillId == "classic:junxing")}).");
        Require(State(discardReplay) == State(game) &&
                Events(discardReplay).SequenceEqual(Events(game)),
            "A paused Junxing discard response must replay exactly.");

        var turnFixture = FindJunxingTurnGame();
        var turnGame = turnFixture.Game;
        var turnAction = turnGame.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseProgramSkill &&
            candidate.ProgramSkillId == "classic:junxing" &&
            candidate.ProgramActivationId == "category-punishment");
        var turnSnapshot = turnGame.CreateSnapshot(HumanSeat, revealAll: true);
        var turnSelection = FindJunxingTurnSelection(turnSnapshot, turnAction) ??
            throw new InvalidOperationException("The Junxing turn fixture lost its category-covering cost.");
        var turnPlay = RequirePrompt(turnGame, DecisionKind.PlayCard);
        var targetBeforeTurn = turnSnapshot.Players[turnSelection.TargetSeat];
        var turnReplay = GameReplay.Restore(RoundTrip(turnGame.CreateCheckpoint()), turnFixture.Registry);
        var turnCommand = new UseProgramSkillCommand(
            HumanSeat,
            "classic:junxing",
            "category-punishment",
            turnSelection.CostCardIds,
            [turnSelection.TargetSeat],
            turnGame.Revision,
            turnPlay.PromptId);
        var turnStarted = turnGame.Submit(turnCommand);
        var replayStarted = turnReplay.Submit(turnCommand);
        Require(turnStarted.Accepted, turnStarted.Error?.Message ?? "The category-covering Junxing activation was rejected.");
        Require(replayStarted.Accepted, replayStarted.Error?.Message ?? "The replayed category-covering Junxing activation was rejected.");
        Require(turnGame.CreateSnapshot(turnSelection.TargetSeat).PendingDecision is null,
            "Covering every target-hand category must auto-resolve Junxing's decline branch without an empty response prompt.");
        var targetAfterTurn = turnGame.CreateSnapshot(HumanSeat, revealAll: true)
            .Players[turnSelection.TargetSeat];
        var turned = turnGame.Events.Select(item => item.Payload)
            .OfType<ProgramCategoryDiscardResolvedEvent>()
            .Last(item => item.SkillId == "classic:junxing");
        var drawnCardIds = turnGame.CardMovements
            .Where(move => move.Reason == new CardMoveReason("skill-program.classic:junxing.Draw") &&
                           move.To == CardLocation.Hand(turnSelection.TargetSeat))
            .Select(move => move.CardId)
            .ToArray();
        Require(turned.DiscardedCardId is null &&
                !targetBeforeTurn.IsFaceDown && targetAfterTurn.IsFaceDown &&
                targetAfterTurn.HandCount == targetBeforeTurn.HandCount + turnSelection.CostCardIds.Count &&
                drawnCardIds.Length == turnSelection.CostCardIds.Count &&
                turnGame.Events.Select(item => item.Payload).OfType<ProgramSkillResolvedEvent>().Any(item =>
                    item.SkillId == "classic:junxing" && item.ActivationId == "category-punishment" && item.Completed) &&
                State(turnReplay) == State(turnGame) && Events(turnReplay).SequenceEqual(Events(turnGame)),
            "Junxing's alternative branch must toggle the target and draw exactly the paid card count.");
    }

    private static Fixture FindJunxingGame()
    {
        var registry = Registry();
        for (var seed = 1; seed <= 256; seed++)
        {
            var game = CreateGame(registry, seed);
            ReachHumanPlay(game);
            var action = game.GetHumanLegalActions().SingleOrDefault(candidate =>
                candidate.Kind == LegalActionKind.UseProgramSkill &&
                candidate.ProgramSkillId == "classic:junxing" &&
                candidate.ProgramActivationId == "category-punishment");
            if (action is not null && FindJunxingSelection(
                    game.CreateSnapshot(HumanSeat, revealAll: true), action) is not null)
            {
                return new Fixture(game, registry, seed);
            }
        }
        throw new InvalidOperationException("No bounded Man Chong fixture exposed a category-divergent Junxing response.");
    }

    private static Fixture FindJunxingTurnGame()
    {
        var registry = Registry();
        for (var seed = 1; seed <= 512; seed++)
        {
            var game = CreateGame(registry, seed);
            ReachHumanPlay(game);
            var action = game.GetHumanLegalActions().SingleOrDefault(candidate =>
                candidate.Kind == LegalActionKind.UseProgramSkill &&
                candidate.ProgramSkillId == "classic:junxing" &&
                candidate.ProgramActivationId == "category-punishment");
            if (action is not null && FindJunxingTurnSelection(
                    game.CreateSnapshot(HumanSeat, revealAll: true), action) is not null)
            {
                return new Fixture(game, registry, seed);
            }
        }
        throw new InvalidOperationException("No bounded Man Chong fixture exposed a category-covering Junxing cost.");
    }

    private static Fixture FindYuceGame() => FindYuceGame(requireCounter: true);

    private static Fixture FindYuceNoCounterGame() => FindYuceGame(requireCounter: false);

    private static Fixture FindYuceGame(bool requireCounter)
    {
        var registry = Registry();
        for (var seed = 1; seed <= 512; seed++)
        {
            var game = CreateGame(registry, seed);
            for (var step = 0; step < 800 && game.State.Winner == Winner.None; step++)
            {
                if (game.PendingDecision is { PlayerSeat: HumanSeat } prompt)
                {
                    if (prompt.Kind == DecisionKind.ProgramTrigger &&
                        prompt.SkillPrompt?.SkillId == "classic:yuce" &&
                        prompt.Choices.Any(choice =>
                            choice.Parameters.GetValueOrDefault("program-action") == "activate"))
                    {
                        var full = game.CreateSnapshot(HumanSeat, revealAll: true);
                        var sourceSeat = prompt.SourceSeat ?? -1;
                        if (sourceSeat >= 0 && full.Players[HumanSeat].Hand.Any(shown =>
                        {
                            var category = CardCatalog.Get(shown.Kind).CategoryName;
                            var sourceHand = full.Players[sourceSeat].Hand;
                            return sourceHand.Count > 0 && (requireCounter
                                ? sourceHand.Any(card => CardCatalog.Get(card.Kind).CategoryName != category)
                                : sourceHand.All(card => CardCatalog.Get(card.Kind).CategoryName == category));
                        }))
                        {
                            return new Fixture(game, registry, seed);
                        }
                        AnswerProgramAction(game, "skip");
                        continue;
                    }
                    if (prompt.Kind == DecisionKind.PlayCard)
                    {
                        var ended = game.Submit(new EndPlayPhaseCommand(
                            HumanSeat,
                            game.Revision,
                            prompt.PromptId));
                        Require(ended.Accepted, ended.Error?.Message ?? "The Man Chong fixture could not end Play.");
                        continue;
                    }
                    if (prompt.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.RescueDying)
                    {
                        var decline = prompt.Choices.FirstOrDefault(choice => choice.Cards.Count == 0) ??
                            throw new InvalidOperationException($"The {prompt.Kind} prompt has no decline choice.");
                        Answer(game, decline);
                        continue;
                    }
                    break;
                }

                var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
                Require(advanced.Accepted, advanced.Error?.Message ?? "The Man Chong fixture could not advance.");
            }
        }
        throw new InvalidOperationException(requireCounter
            ? "No bounded Man Chong fixture reached a category-challenge Yuce prompt."
            : "No bounded Man Chong fixture reached a no-counter Yuce prompt.");
    }

    private static JunxingSelection? FindJunxingSelection(GameSnapshot snapshot, LegalAction action)
    {
        var owner = snapshot.Players[HumanSeat];
        foreach (var cost in owner.Hand.Where(card => action.SelectableCardIds.Contains(card.Id)))
        {
            var category = CardCatalog.Get(cost.Kind).CategoryName;
            foreach (var targetSeat in action.SelectableTargetSeats.Order())
            {
                if (snapshot.Players[targetSeat].Hand.Any(card =>
                        CardCatalog.Get(card.Kind).CategoryName != category))
                {
                    return new JunxingSelection(cost.Id, targetSeat);
                }
            }
        }
        return null;
    }

    private static JunxingTurnSelection? FindJunxingTurnSelection(GameSnapshot snapshot, LegalAction action)
    {
        var owner = snapshot.Players[HumanSeat];
        var ownedByCategory = owner.Hand
            .Where(card => action.SelectableCardIds.Contains(card.Id))
            .GroupBy(card => CardCatalog.Get(card.Kind).CategoryName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(card => card.Id).First(), StringComparer.Ordinal);
        foreach (var targetSeat in action.SelectableTargetSeats.Order())
        {
            var targetCategories = snapshot.Players[targetSeat].Hand
                .Select(card => CardCatalog.Get(card.Kind).CategoryName)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (targetCategories.Length > 0 && targetCategories.All(ownedByCategory.ContainsKey))
            {
                return new JunxingTurnSelection(
                    targetCategories.Select(category => ownedByCategory[category].Id).ToArray(),
                    targetSeat);
            }
        }
        return null;
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
            MaxTurns = 40
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "The Man Chong fixture failed to start.");
        var selection = RequirePrompt(game, DecisionKind.SelectGeneral);
        Require(selection.ValidContentIds.Contains(GeneralId),
            "The Man Chong fixture must offer its only formal general.");
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat,
            GeneralId,
            game.Revision,
            selection.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "The fixture could not select Man Chong.");
        return game;
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 256; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            if (game.PendingDecision is { PlayerSeat: HumanSeat } prompt &&
                prompt.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash)
            {
                Answer(game, prompt.Choices.First(choice => choice.Cards.Count == 0));
                continue;
            }
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "The fixture could not advance to Play.");
        }
        throw new InvalidOperationException("The Man Chong fixture did not reach the human Play prompt.");
    }

    private static void AdvanceOne(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "The pending AI skill choice could not advance.");
    }

    private static void AnswerProgramAction(GameEngine game, string action)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("No prompt is pending.");
        Answer(game, prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == action));
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
        game.PendingDecision is { } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");

    private static PendingDecision RequirePromptForSeat(GameEngine game, int seat, DecisionKind kind) =>
        game.CreateSnapshot(seat).PendingDecision is { } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected {kind} for seat {seat}, found {game.CreateSnapshot(seat).PendingDecision?.Kind.ToString() ?? "no prompt"}.");

    private static PendingDecision RequireProgramPrompt(GameEngine game, string skillId) =>
        game.PendingDecision is { Kind: DecisionKind.ProgramTrigger } prompt &&
        prompt.SkillPrompt?.SkillId == skillId
            ? prompt
            : throw new InvalidOperationException(
                $"Expected ProgramTrigger for {skillId}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"} " +
                $"for {game.PendingDecision?.SkillPrompt?.SkillId ?? "no skill"}.");

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new ScenarioPackage());

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static string State(GameEngine game) =>
        SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true));

    private static IReadOnlyList<string> Events(GameEngine game) => game.Events
        .Select(item =>
            $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record Fixture(GameEngine Game, ContentRegistry Registry, int Seed);
    private sealed record JunxingSelection(int CostCardId, int TargetSeat);
    private sealed record JunxingTurnSelection(IReadOnlyList<int> CostCardIds, int TargetSeat);

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-man-chong-test-6";
        private const string DeckId = "fixture:man-chong-categories";
        private static readonly string[] TargetIds =
        [
            "fixture:man-chong-target-1", "fixture:man-chong-target-2",
            "fixture:man-chong-target-3", "fixture:man-chong-target-4",
            "fixture:man-chong-target-5"
        ];

        public PackageManifest Manifest { get; } = new(
            "man-chong-scenario",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 96, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in TargetIds)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "满宠测试目标",
                    "supporter",
                    "standard:none",
                    "wei",
                    BaseHp: 4));
            }
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "满宠类别选择测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                [
                    new ContentDeckCardCount("standard:slash", 32),
                    new ContentDeckCardCount("standard:dodge", 16),
                    new ContentDeckCardCount("standard:dismantlement", 24),
                    new ContentDeckCardCount("standard:crossbow", 24)
                ]));
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "六人经典身份（满宠场景）",
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
