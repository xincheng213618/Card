using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class GuYongChecks
{
    private const string GeneralId = "classic:gu-yong";
    private const string Shenxing = "classic:shenxing";
    private const string Bingyi = "classic:bingyi";

    public static void ContentAndCapability()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        var general = current.Generals[GeneralId];
        Require(general is { Name: "顾雍", FactionId: "wu", BaseHp: 3, PortraitKey: "gu_yong" } &&
                general.SkillIds.SequenceEqual([Shenxing, Bingyi]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(GeneralId) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(GeneralId) &&
                current.Skills.ContainsKey(Shenxing) && current.Skills.ContainsKey(Bingyi),
            "Current Gu Yong roster and skill registration drifted.");
        var action = current.Skills[Shenxing].Program!.Activations.Single();
        var trigger = current.Skills[Bingyi].Program!.Triggers.Single();
        Require(action is { MinCards: 2, MaxCards: 2, UsesPerTurn: null } &&
                action.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) &&
                trigger.Window == SkillProgramTriggerWindow.TurnEnding &&
                trigger.Effects[0].TargetKind == SkillProgramTargetKind.AnyLiving &&
                trigger.Effects[0].NumberExpression == SkillProgramNumberExpression.CurrentHandCount &&
                trigger.Effects[3].Target == SkillProgramEffectTarget.SelectedTargets,
            "Gu Yong must use shared programs with exact payment and dynamic target bounds.");
        var rules = Resource("CardGame.Content.Standard.SkillPrograms.classic-gu-yong.rules.json");
        var presentation = Resource("CardGame.Content.Standard.SkillPrograms.classic-gu-yong.presentation.json");
        RejectDefinition(rules.Replace("\"schemaVersion\": 60", "\"schemaVersion\": 57", StringComparison.Ordinal),
            presentation, "expected 60");
        RejectDefinition(rules.Replace("\"op\": \"revealBoundCards\", \"target\": \"owner\"",
                "\"op\": \"revealBoundCards\", \"target\": \"selectedTargets\"", StringComparison.Ordinal),
            presentation, "requires owner");
    }

    public static void ShenxingRepeatsWithEquipmentAndRejectsShortfall()
    {
        var (game, registry) = Create(initialHand: 4, drawPerTurn: 2, allRed: true);
        var first = Action(game);
        Use(game, [first.SelectableCardIds[0], first.SelectableCardIds[1]]);
        ReachPlay(game);
        var playPrompt = RequirePrompt(game, DecisionKind.PlayCard);
        var crossbow = game.GetHumanLegalActions().First(item => item.CardId is not null &&
            game.CreateSnapshot(0, revealAll: true).Players[0].Hand.Any(card =>
                card.Id == item.CardId && card.Kind == CardKind.Crossbow));
        Accept(game.Submit(new PlayCardCommand(0, crossbow.CardId!.Value, crossbow.TargetSeats,
            game.Revision, playPrompt.PromptId, crossbow.PlayedCardKind, crossbow.TargetCardId)));
        ReachPlay(game);
        var second = Action(game);
        var equipmentId = game.CreateCardZoneDiagnostics().Single(card =>
            card.Location == CardLocation.Equipment(0)).CardId;
        Require(second.SelectableCardIds.Contains(equipmentId), "Shenxing must offer the equipped Crossbow.");
        Use(game, [equipmentId, second.SelectableCardIds.First(id => id != equipmentId)]);
        ReachPlay(game);
        Require(game.CardMovements.Count(item => item.Reason.Value == $"skill-program.{Shenxing}.DiscardSelected") == 4 &&
                game.CardMovements.Count(item => item.Reason.Value == $"skill-program.{Shenxing}.Draw") == 2,
            "Two Shenxing activations must each spend two owned cards then draw one.");
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(State(replay) == State(game), "Shenxing replay diverged.");
        var (shortfall, _) = Create(initialHand: 1, drawPerTurn: 0, allRed: true);
        Require(shortfall.GetHumanLegalActions().All(item => item.ProgramSkillId != Shenxing),
            "Shenxing must be unavailable with fewer than two owned cards.");
    }

    public static void BingyiRevealsThenSharesAndReplays()
    {
        var (game, registry) = Create(initialHand: 3, drawPerTurn: 0, allRed: true);
        ReachBingyi(game);
        var activation = RequirePrompt(game, DecisionKind.ProgramTrigger);
        Require(activation.IsPrivate && activation.SkillPrompt?.SkillId == Bingyi,
            "Bingyi activation must be private.");
        AnswerAction(game, "activate");
        var targets = RequirePrompt(game, DecisionKind.ProgramTrigger);
        Require(targets.IsPrivate && targets.Choices.Any(choice => choice.Targets.SequenceEqual([0, 1, 2])) &&
                targets.Choices.All(choice => choice.Targets.Count <= 3) &&
                !game.Events.Any(item => item.Payload is ProgramCardsRevealedEvent { SkillId: Bingyi }),
            "Bingyi must offer self and multiple targets up to the frozen hand count before revealing cards.");
        var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        ChooseTargets(game, [0, 1, 2]);
        ChooseTargets(paused, [0, 1, 2]);
        Require(State(game) == State(paused), "Target-choice checkpoint diverged.");
        while (game.PendingDecision?.Choices.Any(choice =>
                   choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards") == true)
        {
            var prompt = game.PendingDecision!;
            var select = prompt.Choices.First(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards");
            Require(!game.Events.Any(item => item.Payload is ProgramCardsRevealedEvent { SkillId: Bingyi }),
                "Bingyi must keep hand cards private during selection.");
            Answer(game, select);
            Answer(paused, paused.PendingDecision!.Choices.Single(choice => choice.Cards.SequenceEqual(select.Cards)));
        }
        if (game.PendingDecision?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards") == true)
        {
            AnswerAction(game, "finish-owned-cards");
            AnswerAction(paused, "finish-owned-cards");
        }
        var revealed = game.Events.Select(item => item.Payload).OfType<ProgramCardsRevealedEvent>()
            .SingleOrDefault(item => item.SkillId == Bingyi);
        Require(revealed is not null &&
                game.CardMovements.Count(item => item.Reason.Value == $"skill-program.{Bingyi}.Draw") == 3 &&
                State(game) == State(paused) && Events(game).SequenceEqual(Events(paused)),
            $"Same-color Bingyi mismatch: revealed={revealed?.Cards.Count}, suits={string.Join(',', revealed?.Cards.Select(card => card.Suit) ?? [])}, reasons={string.Join(',', game.CardMovements.TakeLast(6).Select(move => move.Reason.Value))}, resolved={string.Join(',', game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Where(item => item.SkillId == Bingyi).Select(item => $"{item.Activated}/{item.Completed}"))}, draws={game.CardMovements.Count(item => item.Reason.Value == $"skill-program.{Bingyi}.Draw")}, state={State(game) == State(paused)}, events={Events(game).SequenceEqual(Events(paused))}.");
    }

    public static void BingyiCapsTargetsAndRejectsStaleChoice()
    {
        var (single, _) = Create(initialHand: 1, drawPerTurn: 0, allRed: true);
        ReachBingyi(single);
        AnswerAction(single, "activate");
        var singleTargets = RequirePrompt(single, DecisionKind.ProgramTrigger);
        Require(singleTargets.Choices.Count == 4 &&
                singleTargets.Choices.All(choice => choice.Targets.Count == 1) &&
                singleTargets.Choices.Any(choice => choice.Targets.SequenceEqual([0])),
            "One hand card must cap Bingyi at one living target, including self.");

        var (game, _) = Create(initialHand: 3, drawPerTurn: 0, allRed: true);
        ReachBingyi(game);
        AnswerAction(game, "activate");
        var targetPrompt = RequirePrompt(game, DecisionKind.ProgramTrigger);
        var (rankedId, thought) = new SimpleAiBrain(0, 17).ChooseSupportDrawTargets(
            game.CreateSnapshot(0), targetPrompt.Choices, 1);
        Require(targetPrompt.Choices.Any(choice => choice.Id == rankedId) &&
                thought.Candidates.Count == targetPrompt.Choices.Count,
            "Shared support AI must rank only public target-set choices.");
        var stale = targetPrompt.Choices.Single(choice => choice.Targets.SequenceEqual([0, 1]));
        var players = (IReadOnlyList<CharacterState>)(typeof(GameEngine)
            .GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!);
        players[1].IsAlive = false;
        var revision = game.Revision;
        var movements = game.CardMovements.Count;
        try
        {
            _ = game.Submit(new AnswerPromptCommand(0, targetPrompt.PromptId, stale.Id, revision));
            throw new InvalidOperationException("The stale selected seat was accepted.");
        }
        catch (InvalidOperationException error) when (error.Message.Contains("no longer legal", StringComparison.Ordinal))
        {
        }
        Require(game.Revision == revision &&
                game.CardMovements.Count == movements &&
                game.Events.All(item => item.Payload is not ProgramCardsRevealedEvent { SkillId: Bingyi }),
            "A target that died after the choice was published must reject without revealing or drawing.");
    }

    public static void BingyiMixedOrEmptyDrawsNothing()
    {
        GameEngine? mixed = null;
        for (var seed = 1; seed <= 64 && mixed is null; seed++)
        {
            var (candidate, _) = Create(initialHand: 4, drawPerTurn: 0, allRed: false, seed: seed);
            var dealt = candidate.CreateSnapshot(0, revealAll: true).Players[0].Hand;
            if (dealt.Any(card => card.Suit is Suit.Heart or Suit.Diamond) &&
                dealt.Any(card => card.Suit is Suit.Spade or Suit.Club)) mixed = candidate;
        }
        Require(mixed is not null, "No mixed-color deterministic fixture was found.");
        var activeMixed = mixed!;
        var hand = activeMixed.CreateSnapshot(0, revealAll: true).Players[0].Hand;
        Require(hand.Any(card => card.Suit is Suit.Heart or Suit.Diamond) &&
                hand.Any(card => card.Suit is Suit.Spade or Suit.Club),
            "Mixed fixture did not deal both card colors.");
        ReachBingyi(activeMixed);
        AnswerAction(activeMixed, "activate");
        ChooseTargets(activeMixed, [0, 1]);
        while (activeMixed.PendingDecision?.Choices.Any(choice =>
                   choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards") == true)
            AnswerAction(activeMixed, "select-owned-cards");
        if (activeMixed.PendingDecision?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards") == true)
            AnswerAction(activeMixed, "finish-owned-cards");
        Require(activeMixed.Events.Any(item => item.Payload is ProgramCardsRevealedEvent { SkillId: Bingyi }) &&
                activeMixed.CardMovements.All(item => item.Reason.Value != $"skill-program.{Bingyi}.Draw"),
            "Mixed colors must be exposed without awarding a card.");
        var (empty, _) = Create(initialHand: 0, drawPerTurn: 0, allRed: true);
        Accept(empty.Submit(new EndPlayPhaseCommand(0, empty.Revision, empty.PendingDecision!.PromptId)));
        for (var step = 0; step < 32 && empty.State.TurnNumber == 1; step++)
        {
            Require(empty.PendingDecision?.SkillPrompt?.SkillId != Bingyi,
                "An empty hand must not activate Bingyi.");
            Accept(empty.Submit(new AdvanceOneStepCommand(empty.Revision)));
        }
    }

    private static (GameEngine Game, ContentRegistry Registry) Create(int initialHand, int drawPerTurn,
        bool allRed, int seed = 17)
    {
        var scenario = new Scenario(initialHand, drawPerTurn, allRed);
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), scenario);
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4, ModeId = Scenario.ModeId,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, GeneralId, game.Revision, game.PendingDecision!.PromptId)));
        ReachPlay(game);
        return (game, registry);
    }
    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 128 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, "Fixture did not reach Play.");
    }
    private static void ReachBingyi(GameEngine game)
    {
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
        for (var step = 0; step < 64 && game.PendingDecision?.SkillPrompt?.SkillId != Bingyi; step++)
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        Require(game.PendingDecision?.SkillPrompt?.SkillId == Bingyi, "Fixture did not reach Bingyi.");
    }
    private static LegalAction Action(GameEngine game) => game.GetHumanLegalActions().Single(item => item.ProgramSkillId == Shenxing);
    private static void Use(GameEngine game, IReadOnlyList<int> cards) => Accept(game.Submit(
        new UseProgramSkillCommand(0, Shenxing, "discard-two-draw-one", cards, [],
            game.Revision, game.PendingDecision!.PromptId)));
    private static void ChooseTargets(GameEngine game, IReadOnlyList<int> seats) =>
        Answer(game, game.PendingDecision!.Choices.Single(choice => choice.Targets.SequenceEqual(seats)));
    private static void AnswerAction(GameEngine game, string action) =>
        Answer(game, game.PendingDecision!.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == action));
    private static void Answer(GameEngine game, PromptChoice choice) => Accept(game.Submit(
        new AnswerPromptCommand(0, game.PendingDecision!.PromptId, choice.Id, game.Revision)));
    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { } prompt && prompt.Kind == kind ? prompt :
            throw new InvalidOperationException($"Expected {kind}, got {game.PendingDecision?.Kind}.");
    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));
    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
    private static IReadOnlyList<string> Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();
    private static string Resource(string name)
    {
        using var stream = typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing resource {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    private static void RejectDefinition(string rules, string presentation, string fragment)
    {
        try { _ = SkillProgramCatalog.Load(rules, presentation); }
        catch (InvalidOperationException error) when (error.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase))
        { return; }
        throw new InvalidOperationException($"Expected program definition rejection containing {fragment}.");
    }
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Rejected command.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Scenario(int initialHand, int drawPerTurn, bool allRed) : IGameContentPackage
    {
        public const string ModeId = "identity:classic-gu-yong-check-4";
        public PackageManifest Manifest { get; } = new("gu-yong-scenario", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var targets = new[] { "fixture:gu-yong-1", "fixture:gu-yong-2", "fixture:gu-yong-3" };
            foreach (var id in targets)
                builder.AddGeneral(new ContentGeneralDefinition(id, "测试目标", "supporter", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe("fixture:gu-yong-deck", "顾雍测试牌堆", initialHand,
                drawPerTurn, [])
            {
                PhysicalCards = Enumerable.Range(0, 120)
                    .Select(index => new ContentDeckPhysicalCard("standard:crossbow",
                        allRed ? Suit.Heart : index % 2 == 0 ? Suit.Heart : Suit.Spade, index % 13 + 1))
                    .ToArray()
            });
            builder.AddMode(new ContentModeDefinition(ModeId, "顾雍场景", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2 }, "fixture:gu-yong-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [GeneralId, .. targets]));
        }
    }
}
