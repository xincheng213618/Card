using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class MaDaiChecks
{
    private const string GeneralId = "classic:ma-dai";
    private const string SkillId = "classic:qianxi";

    public static void ContentPromptAndRulesBoundary()
    {
        Require(GameCheckpoint.CurrentRulesVersion >= 104,
            "Formal Qianxi must have an explicit rules-version boundary.");
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        var previous = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 81, 0));
        Require(current.Packages.Any(package =>
                    package.Id == "standard-classic-generals" &&
                    package.Version == new Version(1, 82, 0)) &&
                current.Generals[GeneralId] is
                {
                    FactionId: "shu",
                    BaseHp: 4,
                    PortraitKey: "ma_dai",
                    SkillIds: var skillIds
                } && skillIds.SequenceEqual(["classic:mashu", SkillId]) &&
                current.Skills[SkillId] is
                {
                    LegacyKind: null,
                    Tags: SkillTag.None,
                    ExecutionForms: SkillExecutionForm.State | SkillExecutionForm.Trigger,
                    ActionForms: SkillActionForm.None
                } skill &&
                skill.Description ==
                "准备阶段开始时，你可以摸一张牌然后弃置一张牌。若如此做，你选择距离为1的一名其他角色，然后直到回合结束，该角色不能使用或打出与你以此法弃置的牌颜色相同的手牌。" &&
                !previous.Generals.ContainsKey(GeneralId) &&
                !previous.Skills.ContainsKey(SkillId) &&
                current.ContentHash != previous.ContentHash,
            "Package 1.82.0 must add current classic Ma Dai and Qianxi without mutating 1.81.0.");

        var fixture = FindFixture(CardColor.Red, stopAfterDiscardPrompt: false);
        var game = fixture.Game;
        var prompt = RequirePrompt(game, DecisionKind.Qianxi);
        Require(prompt.IsPrivate &&
                prompt.Choices.Count == 2 &&
                prompt.Choices.Select(choice => choice.Parameters.GetValueOrDefault("action"))
                    .Order(StringComparer.Ordinal)
                    .SequenceEqual(new[] { "qianxi-skip", "qianxi-use" }),
            "Qianxi must begin with one private use-or-skip preparation prompt.");

        var beforeRevision = game.Revision;
        var beforeState = SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
        var forged = game.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            new ChoiceId("qianxi.forged"),
            game.Revision));
        Require(!forged.Accepted &&
                game.Revision == beforeRevision &&
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == beforeState,
            "A forged Qianxi branch must be rejected atomically.");

        var restoredPrompt = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        Require(SnapshotJson.Serialize(restoredPrompt.CreateSnapshot(0, revealAll: true)) == beforeState &&
                restoredPrompt.PendingDecision is { Kind: DecisionKind.Qianxi, IsPrivate: true },
            "A paused Qianxi offer must replay exactly.");

        var handBefore = game.CreateSnapshot(0, revealAll: true).Players[0].HandCount;
        AnswerAction(game, "qianxi-skip");
        ReachHumanPlay(game);
        Require(game.CreateSnapshot(0, revealAll: true).Players[0].HandCount == handBefore + 2 &&
                game.Events.Select(item => item.Payload).OfType<QianxiResolvedEvent>()
                    .Any(item => item is
                        { PlayerSeat: 0, Used: false, DiscardedCardId: null, TargetSeat: null }) &&
                QianxiUsages(game).Count == 0,
            "Skipping Qianxi must continue with the normal draw and create no turn restriction.");

        var legacy = CreateGame(fixture.Registry, fixture.Seed, rulesVersion: 103);
        StartAndSelect(legacy);
        ReachHumanPlay(legacy);
        Require(legacy.Events.Select(item => item.Payload).All(item => item is not QianxiResolvedEvent) &&
                legacy.PendingDecision?.Kind != DecisionKind.Qianxi,
            "Rules v103 must preserve the pre-Qianxi path even with package 1.82.0 content.");
    }

    public static void RedRestrictionFiltersHandResponsesAndReplays() =>
        VerifyColorRestriction(CardColor.Red);

    public static void BlackRestrictionFiltersHandResponsesAndExpires() =>
        VerifyColorRestriction(CardColor.Black);

    private static void VerifyColorRestriction(CardColor restrictedColor)
    {
        var fixture = FindFixture(restrictedColor, stopAfterDiscardPrompt: true);
        var game = fixture.Game;
        var before = game.CreateSnapshot(0, revealAll: true);
        var ownerHandBefore = before.Players[0].HandCount;
        var discard = before.Players[0].Hand.First(card =>
            GetColor(card.Suit) == restrictedColor && card.Kind != CardKind.Duel);
        var discardPrompt = RequirePrompt(game, DecisionKind.Qianxi);
        AnswerChoice(game, discardPrompt.Choices.Single(choice =>
            choice.Cards.Count == 1 && choice.Cards[0] == discard.Id));

        var targetPrompt = RequirePrompt(game, DecisionKind.Qianxi);
        var targetChoice = targetPrompt.Choices.Single(choice =>
            choice.Targets.Count == 1 && choice.Targets[0] == fixture.TargetSeat);
        AnswerChoice(game, targetChoice);
        ReachHumanPlay(game);

        var afterQianxi = game.CreateSnapshot(0, revealAll: true);
        var expectedUsage = $"restriction.target-{fixture.TargetSeat}.{restrictedColor.ToString().ToLowerInvariant()}";
        Require(afterQianxi.Players[0].HandCount == ownerHandBefore + 1 &&
                QianxiUsages(game).SingleOrDefault() is
                { UsageId: var usageId, Scope: SkillUsageScope.Turn, Count: 1 } &&
                usageId == expectedUsage &&
                game.Events.Select(item => item.Payload).OfType<QianxiResolvedEvent>()
                    .Any(item => item is
                    {
                        PlayerSeat: 0,
                        Used: true,
                        TargetSeat: var targetSeat,
                        RestrictedColor: var color
                    } && targetSeat == fixture.TargetSeat && color == restrictedColor),
            "Qianxi must draw one, discard one, then publish one exact target-and-color turn state before normal drawing.");

        var duel = game.GetHumanLegalActions().FirstOrDefault(action =>
            action.Kind == LegalActionKind.Duel &&
            action.TargetSeat == fixture.TargetSeat) ??
            throw new InvalidOperationException("The Qianxi fixture did not expose its Duel action.");
        var played = game.Submit(new PlayCardCommand(
            0,
            duel.CardId!.Value,
            duel.TargetSeats,
            game.Revision,
            RequirePrompt(game, DecisionKind.PlayCard).PromptId,
            duel.PlayedCardKind));
        Require(played.Accepted, played.Error?.Message ?? "The Qianxi Duel was rejected.");

        var response = GetHostPendingDecision(game);
        Require(response is { Kind: DecisionKind.RespondSlash } &&
                response.PlayerSeat == fixture.TargetSeat,
            "The selected Qianxi target must receive the first Duel Slash response window.");
        var targetHand = afterQianxi.Players[fixture.TargetSeat].Hand;
        var restrictedSlashIds = targetHand
            .Where(card => IsSlash(card.Kind) && GetColor(card.Suit) == restrictedColor)
            .Select(card => card.Id)
            .ToHashSet();
        var allowedSlashIds = targetHand
            .Where(card => IsSlash(card.Kind) && GetColor(card.Suit) != restrictedColor)
            .Select(card => card.Id)
            .ToHashSet();
        var publishedSlashIds = response!.Choices
            .Where(choice => choice.Parameters.GetValueOrDefault("response") == "slash")
            .SelectMany(choice => choice.Cards)
            .ToHashSet();
        Require(restrictedSlashIds.Count > 0 &&
                allowedSlashIds.Count > 0 &&
                !publishedSlashIds.Overlaps(restrictedSlashIds) &&
                publishedSlashIds.Overlaps(allowedSlashIds),
            $"Qianxi must filter only {restrictedColor} hand Slashes while preserving opposite-color hand responses.");

        var paused = game.CreateCheckpoint();
        var replay = GameReplay.Restore(RoundTrip(paused), fixture.Registry);
        var replayResponse = GetHostPendingDecision(replay);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                replayResponse?.Choices.SelectMany(choice => choice.Cards).Order()
                    .SequenceEqual(response.Choices.SelectMany(choice => choice.Cards).Order()) == true,
            "A paused Qianxi color-filtered response must replay exactly.");

        if (restrictedColor == CardColor.Black)
        {
            var allowedChoice = response.Choices.First(choice =>
                choice.Parameters.GetValueOrDefault("response") == "slash" &&
                choice.Cards.Count == 1 &&
                allowedSlashIds.Contains(choice.Cards[0]));
            ResolveSyntheticDuelSlash(game, fixture.TargetSeat, allowedChoice.Cards[0]);
            var ownerResponse = RequirePrompt(game, DecisionKind.RespondSlash);
            AnswerChoice(game, ownerResponse.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("response") == "take-damage"));
            ReachHumanPlay(game);
            var ended = game.Submit(new EndPlayPhaseCommand(
                0,
                game.Revision,
                RequirePrompt(game, DecisionKind.PlayCard).PromptId));
            Require(ended.Accepted, ended.Error?.Message ?? "The Qianxi turn could not end.");
            for (var step = 0; step < 256 && QianxiUsages(game).Count != 0; step++)
            {
                var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
                Require(advanced.Accepted, advanced.Error?.Message ?? "The Qianxi expiry fixture could not advance.");
            }
            Require(QianxiUsages(game).Count == 0,
                "Qianxi's hand-color restriction must clear when Ma Dai's turn ends.");
        }
    }

    private static Fixture FindFixture(CardColor color, bool stopAfterDiscardPrompt)
    {
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = CreateGame(registry, seed);
            StartAndSelect(game);
            if (game.PendingDecision?.Kind != DecisionKind.Qianxi) continue;
            if (!stopAfterDiscardPrompt)
            {
                return new Fixture(game, registry, seed, TargetSeat: 1);
            }

            AnswerAction(game, "qianxi-use");
            var snapshot = game.CreateSnapshot(0, revealAll: true);
            if (!snapshot.Players[0].Hand.Any(card =>
                    GetColor(card.Suit) == color && card.Kind != CardKind.Duel) ||
                !snapshot.Players[0].Hand.Any(card => card.Kind == CardKind.Duel))
            {
                continue;
            }

            var target = snapshot.Players
                .Where(player => player.Seat != 0 && game.GetCombatDistance(0, player.Seat) == 1)
                .FirstOrDefault(player =>
                    player.Hand.Any(card => IsSlash(card.Kind) && GetColor(card.Suit) == color) &&
                    player.Hand.Any(card => IsSlash(card.Kind) && GetColor(card.Suit) != color));
            if (target is not null)
            {
                return new Fixture(game, registry, seed, target.Seat);
            }
        }
        throw new InvalidOperationException($"No bounded Ma Dai fixture exposed both Slash colors for {color} Qianxi.");
    }

    private static void StartAndSelect(GameEngine game)
    {
        Require(game.Submit(new StartGameCommand()).Accepted, "The Ma Dai fixture failed to start.");
        var selection = RequirePrompt(game, DecisionKind.SelectGeneral);
        Require(selection.ValidContentIds.Contains(GeneralId, StringComparer.Ordinal),
            "The Ma Dai fixture omitted the formal general.");
        Require(game.Submit(new SelectGeneralCommand(
                0,
                GeneralId,
                game.Revision,
                selection.PromptId)).Accepted,
            "The Ma Dai fixture could not select its formal general.");
        for (var step = 0; step < 256; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.Qianxi or DecisionKind.PlayCard })
            {
                return;
            }
            Require(game.PendingDecision?.PlayerSeat != 0,
                $"Unexpected human prompt {game.PendingDecision?.Kind} before Ma Dai's preparation stage.");
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "The Ma Dai fixture could not reach Qianxi.");
        }
        throw new InvalidOperationException("The Ma Dai fixture did not reach Qianxi or play in bounded steps.");
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 512; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return;
            Require(game.PendingDecision?.PlayerSeat != 0,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while returning to play.");
            var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(result.Accepted, result.Error?.Message ?? "The Ma Dai fixture could not advance.");
        }
        throw new InvalidOperationException("The Ma Dai fixture did not return to play in bounded steps.");
    }

    private static void AnswerAction(GameEngine game, string action)
    {
        var prompt = RequirePrompt(game, DecisionKind.Qianxi);
        AnswerChoice(game, prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == action));
    }

    private static void AnswerChoice(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("There is no human prompt to answer.");
        var result = game.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            choice.Id,
            game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "The prompt choice was rejected.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { PlayerSeat: 0 } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected human {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");

    private static IReadOnlyList<SkillUsageStateSnapshot> QianxiUsages(GameEngine game) =>
        game.CreateSnapshot(0, revealAll: true).Players[0].SkillRuntimeStates!
            .Single(state => state.SkillId == SkillId).Usages;

    private static CardColor GetColor(Suit suit) =>
        suit is Suit.Heart or Suit.Diamond ? CardColor.Red : CardColor.Black;

    private static bool IsSlash(CardKind kind) =>
        kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash;

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

    private static PendingDecision? GetHostPendingDecision(GameEngine game)
    {
        var field = typeof(GameEngine).GetField(
            "_pendingDecision",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine pending-decision store was not found.");
        return (PendingDecision?)field.GetValue(game);
    }

    private static void ResolveSyntheticDuelSlash(GameEngine game, int responderSeat, int slashCardId)
    {
        var duelField = typeof(GameEngine).GetField(
            "_pendingDuel",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine Duel continuation was not found.");
        var duel = duelField.GetValue(game) ??
            throw new InvalidOperationException("The Qianxi Duel lost its continuation.");
        var playersField = typeof(GameEngine).GetField(
            "_players",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine player store was not found.");
        var players = (System.Collections.IList)playersField.GetValue(game)!;
        var responder = players[responderSeat]!;
        var getHand = typeof(GameEngine).GetMethod(
            "GetHand",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine hand accessor was not found.");
        var slash = ((System.Collections.IEnumerable)getHand.Invoke(game, [responder])!)
            .Cast<Card>()
            .Single(card => card.Id == slashCardId);
        var resolutionId = game.ResolutionStack.OfType<CardUseFrame>()
            .Single(frame => frame.CardKind == CardKind.Duel).Id;
        typeof(GameEngine).GetMethod("PopResponseWindow", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(game, [resolutionId]);
        typeof(GameEngine).GetMethod("SetCardUseStep", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(game, [resolutionId, ResolutionFrameStep.ResolvingEffect]);
        typeof(GameEngine).GetMethod("ClearPendingDecision", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(game, null);
        var resolve = typeof(GameEngine).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(method => method.Name == "ResolveDuelResponse" && method.GetParameters().Length == 3);
        resolve.Invoke(game, [duel, responder, slash]);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record Fixture(GameEngine Game, ContentRegistry Registry, int Seed, int TargetSeat);

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-ma-dai-test-4";
        private const string DeckId = "fixture:ma-dai-deck";
        private static readonly string[] BlankGeneralIds =
            ["fixture:ma-dai-blank-1", "fixture:ma-dai-blank-2", "fixture:ma-dai-blank-3"];

        public PackageManifest Manifest { get; } = new(
            "ma-dai-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 82, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var (id, index) in BlankGeneralIds.Select((id, index) => (id, index)))
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    $"潜袭目标{index + 1}",
                    "supporter",
                    "standard:none",
                    "qun",
                    BaseHp: 8));
            }

            var cards = new List<ContentDeckPhysicalCard>();
            Add("standard:slash", 192);
            Add("standard:duel", 96);
            Add("standard:peach", 96);
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "马岱潜袭测试牌堆",
                InitialHandSize: 8,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = cards.ToArray()
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "马岱潜袭测试",
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
