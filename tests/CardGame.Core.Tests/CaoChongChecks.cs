using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CaoChongChecks
{
    private const int HumanSeat = 0;
    private const string GeneralId = "classic:cao-chong";

    public static void ContentAndPackageBoundary()
    {
        var previous = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 93, 0));
        var current = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 94, 0));
        Require(!previous.Generals.ContainsKey(GeneralId) &&
                !previous.Skills.ContainsKey("classic:chengxiang") &&
                !previous.Skills.ContainsKey("classic:renxin"),
            "Package 1.93.0 must retain the pre-Cao-Chong content boundary.");
        Require(current.Packages.Single(package => package.Id == "standard-classic-generals").Version ==
                    new Version(1, 94, 0) &&
                current.Generals[GeneralId] is
                {
                    BaseHp: 3,
                    FactionId: "wei",
                    Gender: GeneralGender.Male,
                    PortraitKey: "cao_chong"
                } caoChong &&
                caoChong.SkillIds.SequenceEqual(["classic:chengxiang", "classic:renxin"]) &&
                current.Skills["classic:chengxiang"] is
                {
                    LegacyKind: SkillKind.Chengxiang,
                    ExecutionForms: SkillExecutionForm.Trigger,
                    ActionForms: SkillActionForm.None
                } &&
                current.Skills["classic:renxin"] is
                {
                    LegacyKind: SkillKind.Renxin,
                    ExecutionForms: SkillExecutionForm.Trigger,
                    ActionForms: SkillActionForm.None
                } &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(GeneralId),
            "Package 1.94.0 must publish complete classic Cao Chong and both optional triggers.");
        Require(GameCheckpoint.CurrentRulesVersion == 101,
            "Adding package-owned Cao Chong content must not create another global replay-rules gate.");
    }

    public static void ChengxiangRevealsLegalSubsetAndReplays()
    {
        var fixture = FindHumanPrompt(DecisionKind.Chengxiang);
        var game = fixture.Game;
        var registry = fixture.Registry;
        var firstPrompt = RequirePrompt(game, DecisionKind.Chengxiang);
        Require(firstPrompt.IsPrivate &&
                firstPrompt.Choices.Select(choice => choice.Parameters.GetValueOrDefault("action"))
                    .Order(StringComparer.Ordinal)
                    .SequenceEqual(["chengxiang-skip", "chengxiang-use"]),
            "Chengxiang must first publish a private optional use/skip decision.");

        var firstPaused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        AnswerAction(game, "chengxiang-use");
        AnswerAction(firstPaused, "chengxiang-use");
        var subsetPrompt = RequirePrompt(game, DecisionKind.Chengxiang);
        var publicCards = game.CreateSnapshot(HumanSeat).PublicRevealedCards;
        Require(publicCards.Count == 4 && subsetPrompt.ValidCardIds.Order().SequenceEqual(
                    publicCards.Select(card => card.Id).Order()) &&
                subsetPrompt.Choices.Count > 0 &&
                subsetPrompt.Choices.All(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "chengxiang-obtain" &&
                    choice.Cards.Sum(cardId => publicCards.Single(card => card.Id == cardId).Rank) <= 13),
            "Chengxiang must publicly reveal four physical cards and publish only subsets with rank sum at most 13.");

        var forged = new ChoiceId("chengxiang.obtain.forged-over-thirteen");
        var revision = game.Revision;
        var rejected = game.Submit(new AnswerPromptCommand(
            HumanSeat,
            subsetPrompt.PromptId,
            forged,
            revision));
        Require(!rejected.Accepted && game.Revision == revision,
            "An unpublished Chengxiang subset must be rejected without changing the state.");

        var subsetPaused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        var selected = subsetPrompt.Choices
            .Where(choice => choice.Cards.Count > 0)
            .OrderByDescending(choice => choice.Cards.Count)
            .ThenByDescending(choice => choice.Cards.Sum(cardId =>
                publicCards.Single(card => card.Id == cardId).Rank))
            .First();
        Answer(game, selected);
        Answer(firstPaused, RequirePrompt(firstPaused, DecisionKind.Chengxiang).Choices.Single(choice => choice.Id == selected.Id));
        Answer(subsetPaused, RequirePrompt(subsetPaused, DecisionKind.Chengxiang).Choices.Single(choice => choice.Id == selected.Id));

        var resolved = game.Events.Select(item => item.Payload).OfType<ChengxiangResolvedEvent>().Last();
        Require(resolved is { OwnerSeat: HumanSeat, Used: true } &&
                resolved.RevealedCardIds.Count == 4 &&
                resolved.ObtainedCardIds.Order().SequenceEqual(selected.Cards.Order()) &&
                resolved.ObtainedRankSum <= 13 &&
                resolved.DiscardedCardIds.Count + resolved.ObtainedCardIds.Count == 4 &&
                game.CardMovements.Count(move => resolved.ObtainedCardIds.Contains(move.CardId) &&
                    move.Reason == CardMoveReasons.ChengxiangObtain &&
                    move.To == CardLocation.Hand(HumanSeat)) == resolved.ObtainedCardIds.Count &&
                game.CardMovements.Count(move => resolved.DiscardedCardIds.Contains(move.CardId) &&
                    move.Reason == CardMoveReasons.ChengxiangDiscard &&
                    move.To == CardLocation.DiscardPile) == resolved.DiscardedCardIds.Count,
            "Chengxiang must move the exact chosen subset to hand and every remainder to discard.");
        Require(State(firstPaused) == State(game) && Events(firstPaused).SequenceEqual(Events(game)) &&
                State(subsetPaused) == State(game) && Events(subsetPaused).SequenceEqual(Events(game)),
            "Both paused Chengxiang decision stages must replay to the same state and event stream.");
    }

    public static void RenxinDiscardsEquipmentTurnsOverPreventsAndReplays()
    {
        var fixture = FindHumanPrompt(DecisionKind.Renxin);
        var game = fixture.Game;
        var registry = fixture.Registry;
        var prompt = RequirePrompt(game, DecisionKind.Renxin);
        var targetSeat = prompt.TargetSeat ?? throw new InvalidOperationException("Renxin did not identify its target.");
        var before = game.CreateSnapshot(HumanSeat, revealAll: true);
        var ownerBefore = before.Players[HumanSeat];
        var targetBefore = before.Players[targetSeat];
        var use = prompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("action") == "renxin-use");
        var costId = use.Cards.Single();
        Require(prompt.IsPrivate && targetSeat != HumanSeat && targetBefore.Hp == 1 &&
                prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("action") == "renxin-skip") &&
                ownerBefore.Hand.Concat(ownerBefore.Equipment)
                    .Any(card => card.Id == costId && EquipmentCatalog.IsEquipment(card.Kind)),
            "Renxin must be offered only for another 1-HP target and name exact owned equipment-category costs.");

        var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Answer(game, use);
        Answer(paused, RequirePrompt(paused, DecisionKind.Renxin).Choices.Single(choice => choice.Id == use.Id));
        var after = game.CreateSnapshot(HumanSeat, revealAll: true);
        var resolved = game.Events.Select(item => item.Payload).OfType<RenxinResolvedEvent>().Last();
        Require(resolved is
                {
                    OwnerSeat: HumanSeat,
                    Used: true,
                    PreventedAmount: > 0,
                    DiscardedCardId: not null
                } &&
                resolved.TargetSeat == targetSeat && resolved.DiscardedCardId == costId &&
                after.Players[targetSeat].Hp == targetBefore.Hp &&
                after.Players[HumanSeat].IsFaceDown != ownerBefore.IsFaceDown &&
                game.CardMovements.Any(move => move.CardId == costId &&
                    move.Reason == CardMoveReasons.RenxinDiscard &&
                    move.To == CardLocation.DiscardPile),
            "Renxin must discard the selected equipment card, toggle the owner and prevent all pending damage.");
        Require(State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "A paused Renxin prevention must replay exactly.");
    }

    private static Fixture FindHumanPrompt(DecisionKind sought)
    {
        var registry = Registry();
        for (var seed = 1; seed <= 1024; seed++)
        {
            var game = CreateGame(registry, seed);
            if (game is null)
            {
                continue;
            }
            for (var step = 0; step < 1200 && game.State.Winner == Winner.None; step++)
            {
                if (game.PendingDecision is { PlayerSeat: HumanSeat } prompt)
                {
                    if (prompt.Kind == sought)
                    {
                        return new Fixture(game, registry, seed);
                    }

                    if (prompt.Kind == DecisionKind.PlayCard)
                    {
                        var ended = game.Submit(new EndPlayPhaseCommand(
                            HumanSeat,
                            game.Revision,
                            prompt.PromptId));
                        Require(ended.Accepted, ended.Error?.Message ?? "The Cao Chong fixture could not end Play.");
                        continue;
                    }

                    if (prompt.Kind == DecisionKind.Chengxiang)
                    {
                        AnswerAction(game, "chengxiang-skip");
                        continue;
                    }

                    if (prompt.Kind == DecisionKind.Renxin)
                    {
                        AnswerAction(game, "renxin-skip");
                        continue;
                    }

                    if (prompt.Kind is DecisionKind.RespondDodge or
                        DecisionKind.RespondSlash or
                        DecisionKind.RescueDying)
                    {
                        var decline = prompt.Choices.FirstOrDefault(choice => choice.Cards.Count == 0) ??
                            throw new InvalidOperationException($"The {prompt.Kind} fixture prompt has no decline choice.");
                        Answer(game, decline);
                        continue;
                    }

                    break;
                }

                var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
                Require(advanced.Accepted, advanced.Error?.Message ?? "The Cao Chong fixture could not advance.");
            }
        }

        throw new InvalidOperationException($"No bounded Cao Chong fixture reached {sought}.");
    }

    private static GameEngine? CreateGame(ContentRegistry registry, int seed)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 6,
            ModeId = ScenarioPackage.ModeId,
            HumanSeat = HumanSeat,
            HumanRole = Role.Rebel,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 40
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "The Cao Chong fixture failed to start.");
        var prompt = RequirePrompt(game, DecisionKind.SelectGeneral);
        if (!prompt.ValidContentIds.Contains(GeneralId))
        {
            return null;
        }
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat,
            GeneralId,
            game.Revision,
            prompt.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "The Cao Chong fixture could not select Cao Chong.");
        return game;
    }

    private static void AnswerAction(GameEngine game, string action)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("No prompt is pending.");
        Answer(game, prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == action));
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
        .Select(item => $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record Fixture(GameEngine Game, ContentRegistry Registry, int Seed);

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-cao-chong-test-6";
        private const string DeckId = "fixture:cao-chong-combat";
        private static readonly string[] TargetIds =
        [
            "fixture:cao-chong-target-1",
            "fixture:cao-chong-target-2",
            "fixture:cao-chong-target-3",
            "fixture:cao-chong-target-4",
            "fixture:cao-chong-target-5"
        ];

        public PackageManifest Manifest { get; } = new(
            "cao-chong-scenario",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 94, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in TargetIds)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "曹冲测试目标",
                    "supporter",
                    "standard:none",
                    "wei",
                    BaseHp: 4));
            }
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "曹冲伤害测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                [
                    new ContentDeckCardCount("standard:slash", 64),
                    new ContentDeckCardCount("standard:crossbow", 24),
                    new ContentDeckCardCount("standard:peach", 16)
                ]));
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "六人经典身份（曹冲场景）",
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
