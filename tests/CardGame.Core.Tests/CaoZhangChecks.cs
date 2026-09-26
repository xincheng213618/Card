using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CaoZhangChecks
{
    private const string GeneralId = "classic:cao-zhang";
    private const string SkillId = "classic:jiangchi";

    public static void ContentPromptAndRulesBoundary()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Packages.Any(package =>
                    package.Id == "standard-classic-generals" &&
                    package.Version == StandardClassicGeneralPackage.CurrentVersion) &&
                current.Generals[GeneralId] is
                {
                    FactionId: "wei",
                    BaseHp: 4,
                    PortraitKey: "cao_zhang",
                    SkillIds: var skillIds
                } && skillIds.SequenceEqual([SkillId]) &&
                current.Skills[SkillId] is
                {
                    LegacyKind: null,
                    Tags: SkillTag.None,
                    ExecutionForms: SkillExecutionForm.Trigger,
                    ActionForms: SkillActionForm.None
                } skill &&
                skill.Program is { MinimumRulesVersion: 168 },
            "Current Cao Zhang must publish grouped Jiangchi through its program.");

        var fixture = FindFixture();
        var game = fixture.Game;
        var prompt = RequirePrompt(game, DecisionKind.ProgramTrigger);
        Require(prompt.IsPrivate &&
                prompt.Choices.Count == 3 &&
                prompt.Choices.Count(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "skip") == 1 &&
                prompt.Choices.Where(choice =>
                        choice.Parameters.GetValueOrDefault("program-action") == "activate")
                    .Select(choice => choice.Parameters.GetValueOrDefault("binding-id"))
                    .Order(StringComparer.Ordinal)
                    .SequenceEqual(["mode-assault", "mode-draw-more"]),
            "Jiangchi must publish exactly the two official modes plus an explicit normal-draw skip.");

        var beforeRevision = game.Revision;
        var beforeState = SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
        var forged = game.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            new ChoiceId("jiangchi.forged"),
            game.Revision));
        Require(!forged.Accepted &&
                game.Revision == beforeRevision &&
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == beforeState,
            "A forged Jiangchi branch must be rejected atomically.");

        var restoredPrompt = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        Require(SnapshotJson.Serialize(restoredPrompt.CreateSnapshot(0, revealAll: true)) == beforeState &&
                restoredPrompt.PendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true },
            "A paused three-branch Jiangchi prompt must replay exactly.");

        var skipped = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        var skippedBefore = skipped.CreateSnapshot(0, revealAll: true).Players[0].HandCount;
        Answer(skipped, "jiangchi-skip");
        ReachHumanPlay(skipped);
        Require(skipped.CreateSnapshot(0, revealAll: true).Players[0].HandCount == skippedBefore + 2 &&
                skipped.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Count(item => item.SkillId == SkillId && !item.Activated) == 2,
            "Skipping Jiangchi must draw normally and create no turn-scoped mode record.");

    }

    public static void DrawMoreBlocksSlashUseAndResponse()
    {
        var fixture = FindFixture();
        var game = fixture.Game;
        var before = game.CreateSnapshot(0, revealAll: true);
        var handBefore = before.Players[0].HandCount;
        Answer(game, "jiangchi-draw-more");
        ReachHumanPlay(game);

        var afterDraw = game.CreateSnapshot(0, revealAll: true);
        Require(afterDraw.Players[0].HandCount == handBefore + 3 &&
                game.Events.Select(item => item.Payload).OfType<CardActionProhibitionGrantedEvent>()
                    .Any(item => item.Prohibition.Source.SkillId == SkillId) &&
                game.GetHumanLegalActions().All(action => action.Kind != LegalActionKind.Slash),
            "Jiangchi's extra-draw branch must draw three and suppress every direct Slash action.");
        var branchReplay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        Require(SnapshotJson.Serialize(branchReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                branchReplay.Events.Select(item => item.Payload).OfType<CardActionProhibitionGrantedEvent>()
                    .Any(item => item.Prohibition.Source.SkillId == SkillId),
            "Jiangchi's resolved extra-draw branch and Slash prohibition must replay exactly.");

        var duel = game.GetHumanLegalActions()
            .Where(action => action.Kind == LegalActionKind.Duel && action.TargetSeat is not null)
            .FirstOrDefault(action => afterDraw.Players[action.TargetSeat!.Value].Hand.Any(card =>
                card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)) ??
            throw new InvalidOperationException("The Jiangchi fixture did not expose a Duel target with Slash.");
        var hpBefore = afterDraw.Players[0].Hp;
        var played = game.Submit(new PlayCardCommand(
            0,
            duel.CardId!.Value,
            duel.TargetSeats,
            game.Revision,
            RequirePrompt(game, DecisionKind.PlayCard).PromptId,
            duel.PlayedCardKind));
        Require(played.Accepted, played.Error?.Message ?? "The Jiangchi Duel was rejected.");

        var targetPrompt = GetHostPendingDecision(game);
        if (targetPrompt is not { Kind: DecisionKind.RespondSlash } ||
            targetPrompt.PlayerSeat != duel.TargetSeat)
        {
            throw new InvalidOperationException(
                "The target must receive the first Duel Slash response window.");
        }
        var targetSlash = targetPrompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("response") == "slash" && choice.Cards.Count == 1);
        ResolveSyntheticDuelSlash(game, targetPrompt.PlayerSeat, targetSlash.Cards[0]);
        Require(game.PendingDecision is not { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 } &&
                game.CreateSnapshot(0, revealAll: true).Players[0].Hp == hpBefore - 1,
            "The extra-draw branch must also block playing Slash to answer Duel, without opening a forged response prompt.");

        ReachHumanPlay(game);
    }

    public static void AssaultAddsDistanceAndOneSlash()
    {
        var fixture = FindFixture();
        var game = fixture.Game;
        var handBefore = game.CreateSnapshot(0, revealAll: true).Players[0].HandCount;
        Answer(game, "jiangchi-assault");
        ReachHumanPlay(game);

        var afterDraw = game.CreateSnapshot(0, revealAll: true);
        var farSlash = game.GetHumanLegalActions()
            .FirstOrDefault(action =>
                action.Kind == LegalActionKind.Slash &&
                action.CardId is not null &&
                action.TargetSeat is { } targetSeat &&
                game.GetCombatDistance(0, targetSeat) > game.GetAttackRange(0)) ??
            throw new InvalidOperationException("Jiangchi did not expose a beyond-range Slash target.");
        Require(afterDraw.Players[0].HandCount == handBefore + 1 &&
                game.Events.Select(item => item.Payload).OfType<TurnRuleModifierGrantedEvent>()
                    .Count(item => item.Modifier.Source.SkillId == SkillId) == 2,
            "Jiangchi's assault branch must draw one and publish one turn-scoped state record.");

        Play(game, farSlash);
        ReachHumanPlay(game);
        var secondSlash = game.GetHumanLegalActions().FirstOrDefault(action =>
            action.Kind == LegalActionKind.Slash && action.CardId is not null) ??
            throw new InvalidOperationException("Jiangchi did not grant its second Slash use.");
        Play(game, secondSlash);
        ReachHumanPlay(game);
        Require(game.GetHumanLegalActions().All(action => action.Kind != LegalActionKind.Slash),
            "Jiangchi must grant exactly one extra Slash, not unlimited Slash uses.");

        var remainingSlash = game.CreateSnapshot(0, revealAll: true).Players[0].Hand
            .FirstOrDefault(card => card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) ??
            throw new InvalidOperationException("The Jiangchi fixture did not retain a third physical Slash for rejection.");
        var playPrompt = RequirePrompt(game, DecisionKind.PlayCard);
        var revision = game.Revision;
        var forged = game.Submit(new PlayCardCommand(
            0,
            remainingSlash.Id,
            [farSlash.TargetSeat!.Value],
            game.Revision,
            playPrompt.PromptId));
        Require(!forged.Accepted && game.Revision == revision,
            "A third Jiangchi Slash command must be rejected without changing the game revision.");

        var modes = game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().ToArray();
        Require(modes.Count(item => item.SkillId == SkillId && item.BindingId == "mode-assault" &&
                                    item.Activated && item.Completed) == 1,
            "Jiangchi must complete exactly one configured assault branch.");
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                replay.GetHumanLegalActions().All(action => action.Kind != LegalActionKind.Slash) &&
                replay.Events.Select(item => item.Payload).OfType<TurnRuleModifierGrantedEvent>()
                    .Count(item => item.Modifier.Source.SkillId == SkillId) == 2,
            "The assault draw, distance-free first Slash and exact two-use limit must replay exactly.");
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
        Require(result.Accepted, result.Error?.Message ?? "The Jiangchi Slash was rejected.");
    }

    private static void Answer(GameEngine game, string action)
    {
        var prompt = RequirePrompt(game, DecisionKind.ProgramTrigger);
        var binding = action switch
        {
            "jiangchi-draw-more" => "mode-draw-more",
            "jiangchi-assault" => "mode-assault",
            "jiangchi-skip" => null,
            _ => throw new InvalidOperationException("Unknown Jiangchi fixture action.")
        };
        var choice = prompt.Choices.Single(candidate => binding is null
            ? candidate.Parameters.GetValueOrDefault("program-action") == "skip"
            : candidate.Parameters.GetValueOrDefault("binding-id") == binding &&
              candidate.Parameters.GetValueOrDefault("program-action") == "activate");
        var result = game.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            choice.Id,
            game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "The Jiangchi choice was rejected.");
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
            Require(result.Accepted, result.Error?.Message ?? "The Jiangchi fixture could not advance.");
        }
        throw new InvalidOperationException("The Jiangchi fixture did not return to play in bounded steps.");
    }

    private static Fixture FindFixture()
    {
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 2_048; seed++)
        {
            var game = CreateGame(registry, seed);
            StartAndSelect(game);
            if (game.PendingDecision?.Kind != DecisionKind.ProgramTrigger) continue;
            var snapshot = game.CreateSnapshot(0, revealAll: true);
            var hand = snapshot.Players[0].Hand;
            if (hand.Count(card => card.Kind == CardKind.Slash) < 4 ||
                hand.All(card => card.Kind != CardKind.Duel) ||
                snapshot.Players.Where(player => player.Seat != 0)
                    .All(player => player.Hand.All(card => card.Kind != CardKind.Slash)))
            {
                continue;
            }
            return new Fixture(game, registry, seed);
        }
        throw new InvalidOperationException("No bounded Cao Zhang fixture exposed four Slashes, Duel and a responding target.");
    }

    private static void StartAndSelect(GameEngine game)
    {
        Require(game.Submit(new StartGameCommand()).Accepted, "The Cao Zhang fixture failed to start.");
        var selection = RequirePrompt(game, DecisionKind.SelectGeneral);
        Require(selection.ValidContentIds.Contains(GeneralId, StringComparer.Ordinal),
            "The Cao Zhang fixture omitted the formal general.");
        Require(game.Submit(new SelectGeneralCommand(
                0,
                GeneralId,
                game.Revision,
                selection.PromptId)).Accepted,
            "The Cao Zhang fixture could not select its formal general.");
        for (var step = 0; step < 256; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger or DecisionKind.PlayCard })
            {
                return;
            }
            Require(game.PendingDecision?.PlayerSeat != 0,
                $"Unexpected human prompt {game.PendingDecision?.Kind} before Cao Zhang's draw phase.");
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "The Cao Zhang fixture could not reach its draw phase.");
        }
        throw new InvalidOperationException("The Cao Zhang fixture did not reach Jiangchi or play in bounded steps.");
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
            throw new InvalidOperationException("The Jiangchi Duel lost its continuation.");
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

    private sealed record Fixture(GameEngine Game, ContentRegistry Registry, int Seed);

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-cao-zhang-test-4";
        private const string DeckId = "fixture:cao-zhang-deck";
        private static readonly string[] BlankGeneralIds =
            ["fixture:cao-zhang-blank-1", "fixture:cao-zhang-blank-2", "fixture:cao-zhang-blank-3"];

        public PackageManifest Manifest { get; } = new(
            "cao-zhang-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 81, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var (id, index) in BlankGeneralIds.Select((id, index) => (id, index)))
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    $"将驰目标{index + 1}",
                    "supporter",
                    "standard:none",
                    "qun",
                    BaseHp: 8));
            }

            var cards = new List<ContentDeckPhysicalCard>();
            Add("standard:slash", 160);
            Add("standard:duel", 64);
            Add("standard:peach", 96);
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "曹彰将驰测试牌堆",
                InitialHandSize: 8,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = cards.ToArray()
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "曹彰将驰测试",
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
