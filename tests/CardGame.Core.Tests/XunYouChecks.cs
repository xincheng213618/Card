using CardGame.Content.Standard;
using CardGame.Core;

internal static class XunYouChecks
{
    private const int HumanSeat = 0;
    private const string GeneralId = "classic:xun-you";
    private const string QiceSkillId = "classic:qice";
    private const string ZhiyuSkillId = "classic:zhiyu";

    public static void ContentAndRulesBoundary()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 87, 0));
        var previous = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 86, 0));
        Require(current.Packages.Any(package =>
                package.Id == "standard-classic-generals" &&
                package.Version == new Version(1, 87, 0)),
            "The current registry must load classic-general package 1.87.0.");

        var general = current.Generals[GeneralId];
        Require(general.FactionId == "wei" && general.BaseHp == 3 &&
                general.Gender == GeneralGender.Male && general.PortraitKey == "xun_you" &&
                general.SkillIds.SequenceEqual([QiceSkillId, ZhiyuSkillId]),
            $"Classic Xun You metadata drifted: faction={general.FactionId}, hp={general.BaseHp}, " +
            $"gender={general.Gender}, portrait={general.PortraitKey}, skills={string.Join(',', general.SkillIds)}.");

        var qice = current.Skills[QiceSkillId];
        Require(qice.LegacyKind == SkillKind.Qice && qice.Tags == SkillTag.None &&
                qice.ExecutionForms == SkillExecutionForm.None &&
                qice.ActionForms == SkillActionForm.Active,
            $"Qice metadata drifted: kind={qice.LegacyKind}, tags={qice.Tags}, " +
            $"execution={qice.ExecutionForms}, actions={qice.ActionForms}.");
        var zhiyu = current.Skills[ZhiyuSkillId];
        Require(zhiyu.LegacyKind == SkillKind.Zhiyu && zhiyu.Tags == SkillTag.None &&
                zhiyu.ExecutionForms == SkillExecutionForm.Trigger &&
                zhiyu.ActionForms == SkillActionForm.None,
            $"Zhiyu metadata drifted: kind={zhiyu.LegacyKind}, tags={zhiyu.Tags}, " +
            $"execution={zhiyu.ExecutionForms}, actions={zhiyu.ActionForms}.");
        Require(!previous.Generals.ContainsKey(GeneralId) &&
                !previous.Skills.ContainsKey(QiceSkillId) &&
                !previous.Skills.ContainsKey(ZhiyuSkillId) &&
                current.ContentHash != previous.ContentHash,
            "Package 1.87.0 must add Xun You without mutating the 1.86.0 registry boundary.");

    }

    public static void QiceUsesAllHandCardsAndReplays()
    {
        var registry = CreateRegistry();
        var game = CreateGame(registry, 1);
        StartAndSelect(game);
        ReachHumanPlay(game, skipZhiyu: true);

        var play = RequirePrompt(game, DecisionKind.PlayCard);
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseSkill && candidate.Skill == SkillKind.Qice);
        var handIds = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand
            .Select(card => card.Id).Order().ToArray();
        Require(handIds.Length > 1 && action.SelectableCardIds.Order().SequenceEqual(handIds) &&
                action.MinCardCount == handIds.Length && action.MaxCardCount == handIds.Length,
            "Qice must publish the current complete non-empty hand as its exact physical cost.");

        var beforeForgery = SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true));
        var revision = game.Revision;
        var forged = game.Submit(new UseSkillCommand(
            HumanSeat,
            SkillKind.Qice,
            handIds.Take(handIds.Length - 1).ToArray(),
            [],
            revision,
            play.PromptId));
        Require(!forged.Accepted && game.Revision == revision &&
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)) == beforeForgery,
            "A Qice subset must be rejected without consuming usage or moving cards.");

        var accepted = game.Submit(new UseSkillCommand(
            HumanSeat,
            SkillKind.Qice,
            handIds,
            [],
            game.Revision,
            play.PromptId));
        Require(accepted.Accepted, accepted.Error?.Message ?? "Qice rejected the exact complete hand.");
        var qice = RequirePrompt(game, DecisionKind.Qice);
        var offeredKinds = qice.Choices
            .Select(choice => choice.Parameters.GetValueOrDefault("card-kind"))
            .ToHashSet(StringComparer.Ordinal);
        Require(qice.IsPrivate && qice.ValidCardIds.Order().SequenceEqual(handIds) &&
                offeredKinds.IsSupersetOf([
                    nameof(CardKind.DrawTwo), nameof(CardKind.Duel), nameof(CardKind.BarbarianAssault),
                    nameof(CardKind.ArrowBarrage), nameof(CardKind.PeachGarden), nameof(CardKind.FiveGrains),
                    nameof(CardKind.Dismantlement), nameof(CardKind.Snatch), nameof(CardKind.FireAttack),
                    nameof(CardKind.IronChain)]) &&
                qice.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("card-kind") == nameof(CardKind.FireAttack) &&
                    choice.Targets.SequenceEqual([HumanSeat])),
            $"Qice must publish the legal proactive ordinary-trick families for the current board " +
            $"(offered={string.Join(',', offeredKinds.Order())}).");

        var pausedReplay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(pausedReplay.PendingDecision is { Kind: DecisionKind.Qice, PlayerSeat: HumanSeat, IsPrivate: true } &&
                SnapshotJson.Serialize(pausedReplay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)),
            "A paused Qice ordinary-trick choice must replay exactly.");

        var drawTwo = qice.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("card-kind") == nameof(CardKind.DrawTwo));
        var priorRuleDraws = game.CardMovements.Count(move =>
            move.To == CardLocation.Hand(HumanSeat) && move.Reason == CardMoveReasons.Draw);
        Answer(game, drawTwo);
        ReachHumanPlay(game, skipZhiyu: true);
        var converted = game.Events.Select(item => item.Payload).OfType<QiceConvertedEvent>().Single();
        Require(converted.SourceSeat == HumanSeat && converted.EffectiveCardKind == CardKind.DrawTwo &&
                converted.PhysicalCardIds.Order().SequenceEqual(handIds) &&
                game.CardMovements.Count(move => handIds.Contains(move.CardId) &&
                    move.From == CardLocation.Hand(HumanSeat) && move.To == CardLocation.Processing &&
                    move.Reason == CardMoveReasons.Use) == handIds.Length &&
                game.CardMovements.Count(move => handIds.Contains(move.CardId) &&
                    move.To == CardLocation.DiscardPile) == handIds.Length &&
                game.CardMovements.Count(move => move.To == CardLocation.Hand(HumanSeat) &&
                    move.Reason == CardMoveReasons.Draw) - priorRuleDraws == 2 &&
                game.GetHumanLegalActions().All(candidate => candidate.Skill != SkillKind.Qice),
            "Qice Draw Two must consume every physical hand card, draw exactly two and remain once per play phase.");

        var completedReplay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(SnapshotJson.Serialize(completedReplay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)),
            "A completed Qice command prefix must replay exactly.");
    }

    public static void ZhiyuDrawRevealDiscardAndReplay()
    {
        var fixture = FindZhiyuFixture();
        var game = fixture.Game;
        var registry = fixture.Registry;
        var prompt = RequirePrompt(game, DecisionKind.Zhiyu);
        var sourceSeat = prompt.SourceSeat ?? throw new InvalidOperationException("Zhiyu did not identify its damage source.");
        var ownerBefore = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat];
        var sourceBefore = game.CreateSnapshot(HumanSeat, revealAll: true).Players[sourceSeat];
        Require(prompt.IsPrivate &&
                prompt.Choices.Select(choice => choice.Parameters.GetValueOrDefault("action"))
                    .Order(StringComparer.Ordinal).SequenceEqual(["zhiyu-skip", "zhiyu-use"]),
            "After damage, Xun You must receive one optional private Zhiyu trigger choice.");

        var paused = RoundTrip(game.CreateCheckpoint());
        var skipped = GameReplay.Restore(paused, registry);
        Answer(skipped, DecisionKind.Zhiyu, "zhiyu-skip");
        Require(skipped.Events.Select(item => item.Payload).OfType<ZhiyuResolvedEvent>().Last() is
                { OwnerSeat: HumanSeat, Used: false, DrawnCardId: null, RevealedCards.Count: 0,
                    AllSameColor: false, DiscardedCardId: null },
            "Skipping Zhiyu must neither draw nor reveal nor force a discard.");

        Answer(game, DecisionKind.Zhiyu, "zhiyu-use");
        Require(game.PendingDecision is null,
            "An AI source's private Zhiyu discard prompt must not leak into the human view.");
        var discard = game.CreateSnapshot(sourceSeat, revealAll: true).PendingDecision ??
                      throw new InvalidOperationException("Zhiyu did not publish the source's private discard prompt.");
        Require(discard is { Kind: DecisionKind.Zhiyu, PlayerSeat: var discardSeat, IsPrivate: true } &&
                discardSeat == sourceSeat && discard.ValidCardIds.Count == sourceBefore.HandCount &&
                discard.Choices.All(choice => choice.Cards.Count == 1 &&
                    discard.ValidCardIds.Contains(choice.Cards[0])),
            "Same-color Zhiyu must let the damage source privately choose one exact hand card to discard.");

        var discardReplay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        AdvanceAiPrompt(game, sourceSeat);
        AdvanceAiPrompt(discardReplay, sourceSeat);
        var result = game.Events.Select(item => item.Payload).OfType<ZhiyuResolvedEvent>().Last();
        Require(result is { OwnerSeat: HumanSeat, Used: true, DrawnCardId: not null,
                    AllSameColor: true, DiscardedCardId: not null } &&
                result.SourceSeat == sourceSeat && result.RevealedCards.Count == ownerBefore.HandCount + 1 &&
                result.RevealedCards.All(card => card.Suit is Suit.Heart or Suit.Diamond) &&
                game.CreateSnapshot(HumanSeat, revealAll: true).Players[sourceSeat].HandCount ==
                    sourceBefore.HandCount - 1 &&
                game.CardMovements.Any(move => move.CardId == result.DrawnCardId &&
                    move.To == CardLocation.Hand(HumanSeat) && move.Reason == CardMoveReasons.ZhiyuDraw) &&
                game.CardMovements.Any(move => move.CardId == result.DiscardedCardId &&
                    move.From == CardLocation.Hand(sourceSeat) && move.To == CardLocation.DiscardPile &&
                    move.Reason == CardMoveReasons.ZhiyuDiscard),
            "Zhiyu must draw, reveal the full same-color hand and discard the source's selected physical card.");
        Require(SnapshotJson.Serialize(discardReplay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)),
            "A paused Zhiyu source-discard prompt must replay deterministically.");

        var completedReplay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(SnapshotJson.Serialize(completedReplay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)),
            "A completed Zhiyu command prefix must replay exactly.");
    }

    private static Fixture FindZhiyuFixture()
    {
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 2_048; seed++)
        {
            var game = CreateGame(registry, seed);
            StartAndSelect(game);
            for (var step = 0; step < 1_024; step++)
            {
                if (game.PendingDecision is
                    { Kind: DecisionKind.Zhiyu, PlayerSeat: HumanSeat, SourceSeat: var sourceSeat } &&
                    sourceSeat is { } source &&
                    game.CreateSnapshot(HumanSeat, revealAll: true).Players[source].HandCount > 0)
                {
                    return new Fixture(game, registry, seed);
                }
                if (game.PendingDecision is { PlayerSeat: HumanSeat }) break;
                Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                    "The Xun You damage fixture could not advance.");
            }
        }
        throw new InvalidOperationException("No bounded all-red Slash fixture reached a human Zhiyu prompt.");
    }

    private static void ReachHumanPlay(GameEngine game, bool skipZhiyu)
    {
        for (var step = 0; step < 2_048; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            if (game.PendingDecision is { Kind: DecisionKind.Zhiyu, PlayerSeat: HumanSeat })
            {
                Require(skipZhiyu, "Unexpected Zhiyu prompt while reaching play.");
                Answer(game, DecisionKind.Zhiyu, "zhiyu-skip");
                continue;
            }
            Require(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while reaching play.");
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "The Xun You fixture could not reach human play.");
        }
        throw new InvalidOperationException("The Xun You fixture did not reach human play in bounded steps.");
    }

    private static void StartAndSelect(GameEngine game)
    {
        Require(game.Submit(new StartGameCommand()).Accepted, "The Xun You fixture failed to start.");
        var selection = RequirePrompt(game, DecisionKind.SelectGeneral);
        Require(selection.ValidContentIds.Contains(GeneralId, StringComparer.Ordinal),
            "The Xun You fixture did not offer its formal general.");
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat, GeneralId, game.Revision, selection.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "The Xun You fixture could not select its general.");
    }

    private static void AdvanceAiPrompt(GameEngine game, int sourceSeat)
    {
        var prompt = game.CreateSnapshot(sourceSeat, revealAll: true).PendingDecision ??
                     throw new InvalidOperationException("There is no AI Zhiyu prompt.");
        Require(prompt.PlayerSeat == sourceSeat && prompt.Kind == DecisionKind.Zhiyu,
            $"Expected AI Zhiyu discard, found {prompt.Kind} at seat {prompt.PlayerSeat}.");
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "The AI Zhiyu discard could not advance.");
    }

    private static void Answer(GameEngine game, DecisionKind kind, string action)
    {
        var prompt = RequirePrompt(game, kind);
        var choice = prompt.Choices.Single(candidate =>
            candidate.Parameters.GetValueOrDefault("action") == action);
        Answer(game, choice);
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("There is no Xun You prompt.");
        var result = game.Submit(new AnswerPromptCommand(
            prompt.PlayerSeat,
            prompt.PromptId,
            choice.Id,
            game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "The Xun You prompt answer was rejected.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { PlayerSeat: HumanSeat } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected human {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");

    private static GameEngine CreateGame(ContentRegistry registry, int seed, int rulesVersion = GameCheckpoint.CurrentRulesVersion)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 4,
            ModeId = ScenarioPackage.ModeId,
            HumanSeat = HumanSeat,
            HumanRole = Role.Rebel,
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

    private sealed record Fixture(GameEngine Game, ContentRegistry Registry, int Seed);

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-xun-you-test-4";
        private const string DeckId = "fixture:xun-you-red-slash-deck";
        private static readonly string[] BlankGeneralIds =
        [
            "fixture:xun-you-lord",
            "fixture:xun-you-supporter-a",
            "fixture:xun-you-supporter-b"
        ];

        public PackageManifest Manifest { get; } = new(
            "xun-you-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 87, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in BlankGeneralIds)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "智愚测试目标",
                    "supporter",
                    "standard:none",
                    "shu",
                    BaseHp: 8));
            }

            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "荀攸奇策智愚测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = Enumerable.Range(0, 192)
                    .Select(index => new ContentDeckPhysicalCard("standard:slash", Suit.Heart, index % 13 + 1))
                    .ToArray()
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "荀攸奇策智愚测试",
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
