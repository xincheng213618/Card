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
        var current = StandardContentRegistry.CreateWithClassicGenerals();

        var general = current.Generals[GeneralId];
        Require(general.FactionId == "wei" && general.BaseHp == 3 &&
                general.Gender == GeneralGender.Male && general.PortraitKey == "xun_you" &&
                general.SkillIds.SequenceEqual([QiceSkillId, ZhiyuSkillId]),
            $"Classic Xun You metadata drifted: faction={general.FactionId}, hp={general.BaseHp}, " +
            $"gender={general.Gender}, portrait={general.PortraitKey}, skills={string.Join(',', general.SkillIds)}.");

        var qice = current.Skills[QiceSkillId];
        Require(qice.Tags == SkillTag.None &&
                qice.ExecutionForms == SkillExecutionForm.None &&
                qice.ActionForms == SkillActionForm.Active &&
                qice.Program is
                {
                    RuntimeVersion: "skill-program-v62",
                    MinimumRulesVersion: 172,
                    Activations.Count: 1
                } && qice.Program.Activations.Single().Effects.Single().Op ==
                    SkillProgramEffectOp.UseAllHandCardsAsOrdinaryTrick,
            $"Qice metadata drifted: id={qice.Id}, tags={qice.Tags}, " +
            $"execution={qice.ExecutionForms}, actions={qice.ActionForms}.");
        var zhiyu = current.Skills[ZhiyuSkillId];
        Require(zhiyu.Tags == SkillTag.None &&
                zhiyu.ExecutionForms == SkillExecutionForm.Trigger &&
                zhiyu.ActionForms == SkillActionForm.None &&
                zhiyu.Program is
                {
                    RuntimeVersion: "skill-program-v62",
                    MinimumRulesVersion: 172,
                    Triggers.Count: 1
                },
            $"Zhiyu metadata drifted: id={zhiyu.Id}, tags={zhiyu.Tags}, " +
            $"execution={zhiyu.ExecutionForms}, actions={zhiyu.ActionForms}.");

    }

    public static void QiceUsesAllHandCardsAndReplays()
    {
        var registry = CreateRegistry();
        var game = CreateGame(registry, 1);
        StartAndSelect(game);
        ReachHumanPlay(game, skipZhiyu: true);

        var play = RequirePrompt(game, DecisionKind.PlayCard);
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseProgramSkill &&
            candidate.ProgramSkillId == QiceSkillId &&
            candidate.ProgramActivationId == "all-hand-as-ordinary-trick");
        var handIds = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand
            .Select(card => card.Id).Order().ToArray();
        Require(handIds.Length > 1 && action.SelectableCardIds.Order().SequenceEqual(handIds) &&
                action.MinCardCount == handIds.Length && action.MaxCardCount == handIds.Length,
            "Qice must publish the current complete non-empty hand as its exact physical cost.");

        var beforeForgery = SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true));
        var revision = game.Revision;
        var forged = game.Submit(new UseProgramSkillCommand(
            HumanSeat,
            QiceSkillId,
            "all-hand-as-ordinary-trick",
            handIds.Take(handIds.Length - 1).ToArray(),
            [],
            revision,
            play.PromptId));
        Require(!forged.Accepted && game.Revision == revision &&
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)) == beforeForgery,
            "A Qice subset must be rejected without consuming usage or moving cards.");

        var accepted = game.Submit(new UseProgramSkillCommand(
            HumanSeat,
            QiceSkillId,
            "all-hand-as-ordinary-trick",
            handIds,
            [],
            game.Revision,
            play.PromptId));
        Require(accepted.Accepted, accepted.Error?.Message ?? "Qice rejected the exact complete hand.");
        var qice = RequireProgramPrompt(game, QiceSkillId);
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
        Require(pausedReplay.PendingDecision is
                { Kind: DecisionKind.ProgramTrigger, PlayerSeat: HumanSeat, IsPrivate: true,
                    SkillPrompt.SkillId: QiceSkillId } &&
                SnapshotJson.Serialize(pausedReplay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)),
            "A paused Qice ordinary-trick choice must replay exactly.");

        var drawTwo = qice.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("card-kind") == nameof(CardKind.DrawTwo));
        var priorRuleDraws = game.CardMovements.Count(move =>
            move.To == CardLocation.Hand(HumanSeat) && move.Reason == CardMoveReasons.Draw);
        Answer(game, drawTwo);
        ReachHumanPlay(game, skipZhiyu: true);
        var converted = game.Events.Select(item => item.Payload).OfType<ProgramViewAsConvertedEvent>()
            .Single(item => item.SkillId == QiceSkillId);
        Require(converted.OwnerSeat == HumanSeat && converted.OutputKind == CardKind.DrawTwo &&
                converted.PhysicalCardIds.Order().SequenceEqual(handIds) &&
                game.CardMovements.Count(move => handIds.Contains(move.CardId) &&
                    move.From == CardLocation.Hand(HumanSeat) && move.To == CardLocation.Processing &&
                    move.Reason == CardMoveReasons.Use) == handIds.Length &&
                game.CardMovements.Count(move => handIds.Contains(move.CardId) &&
                    move.To == CardLocation.DiscardPile) == handIds.Length &&
                game.CardMovements.Count(move => move.To == CardLocation.Hand(HumanSeat) &&
                    move.Reason == CardMoveReasons.Draw) - priorRuleDraws == 2 &&
                game.GetHumanLegalActions().All(candidate => candidate.ProgramSkillId != QiceSkillId),
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
        var prompt = RequireProgramPrompt(game, ZhiyuSkillId);
        var sourceSeat = prompt.SourceSeat ?? throw new InvalidOperationException("Zhiyu did not identify its damage source.");
        var ownerBefore = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat];
        var sourceBefore = game.CreateSnapshot(HumanSeat, revealAll: true).Players[sourceSeat];
        Require(prompt.IsPrivate &&
                prompt.Choices.Select(choice => choice.Parameters.GetValueOrDefault("program-action"))
                    .Order(StringComparer.Ordinal).SequenceEqual(["activate", "skip"]),
            "After damage, Xun You must receive one optional private Zhiyu trigger choice.");

        var paused = RoundTrip(game.CreateCheckpoint());
        var skipped = GameReplay.Restore(paused, registry);
        AnswerProgram(skipped, ZhiyuSkillId, "skip");
        Require(skipped.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Last(item =>
                    item.SkillId == ZhiyuSkillId) is
                { OwnerSeat: HumanSeat, Activated: false, Completed: false } &&
                skipped.Events.Select(item => item.Payload).OfType<ProgramCardsRevealedEvent>()
                    .All(item => item.SkillId != ZhiyuSkillId),
            "Skipping Zhiyu must neither draw nor reveal nor force a discard.");

        AnswerProgram(game, ZhiyuSkillId, "activate");
        Require(game.PendingDecision is null,
            "An AI source's private Zhiyu discard prompt must not leak into the human view.");
        var discard = game.CreateSnapshot(sourceSeat, revealAll: true).PendingDecision ??
                      throw new InvalidOperationException("Zhiyu did not publish the source's private discard prompt.");
        Require(discard is
                { Kind: DecisionKind.ProgramTrigger, PlayerSeat: var discardSeat, IsPrivate: true,
                    SkillPrompt.SkillId: ZhiyuSkillId } &&
                discardSeat == sourceSeat && discard.ValidCardIds.Count == sourceBefore.HandCount &&
                discard.Choices.All(choice => choice.Cards.Count == 1 &&
                    discard.ValidCardIds.Contains(choice.Cards[0])),
            "Same-color Zhiyu must let the damage source privately choose one exact hand card to discard.");

        var discardReplay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        AdvanceAiPrompt(game, sourceSeat);
        AdvanceAiPrompt(discardReplay, sourceSeat);
        var revealed = game.Events.Select(item => item.Payload).OfType<ProgramCardsRevealedEvent>()
            .Last(item => item.SkillId == ZhiyuSkillId);
        var binding = game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
            .Last(item => item.SkillId == ZhiyuSkillId);
        var zhiyuDraw = game.CardMovements.Last(move =>
            move.To == CardLocation.Hand(HumanSeat) &&
            move.Reason.Value == "skill-program.classic:zhiyu.Draw");
        var zhiyuDiscard = game.CardMovements.Last(move =>
            move.From == CardLocation.Hand(sourceSeat) && move.To == CardLocation.DiscardPile &&
            move.Reason.Value == "skill-program.classic:zhiyu.SelectAndMoveOwnedCard");
        Require(binding is { OwnerSeat: HumanSeat, Activated: true, Completed: true } &&
                revealed.Cards.Count == ownerBefore.HandCount + 1 &&
                revealed.Cards.All(card => card.Suit is Suit.Heart or Suit.Diamond) &&
                game.CreateSnapshot(HumanSeat, revealAll: true).Players[sourceSeat].HandCount ==
                    sourceBefore.HandCount - 1 &&
                revealed.Cards.Any(card => card.Id == zhiyuDraw.CardId) &&
                discard.ValidCardIds.Contains(zhiyuDiscard.CardId),
            "Zhiyu must draw, reveal the full same-color hand and discard the source's selected physical card.");
        Require(SnapshotJson.Serialize(discardReplay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)),
            "A paused Zhiyu source-discard prompt must replay deterministically.");

        var completedReplay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(SnapshotJson.Serialize(completedReplay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)),
            "A completed Zhiyu command prefix must replay exactly.");

        var emptySourceFixture = FindZhiyuFixture(requireSourceCards: false);
        var emptySourceGame = emptySourceFixture.Game;
        var emptySourcePrompt = RequireProgramPrompt(emptySourceGame, ZhiyuSkillId);
        var emptySourceSeat = emptySourcePrompt.SourceSeat ??
                              throw new InvalidOperationException("The empty-source Zhiyu fixture lost its source.");
        Require(emptySourceGame.CreateSnapshot(HumanSeat, revealAll: true).Players[emptySourceSeat].HandCount == 0,
            "The empty-source Zhiyu fixture must start its trigger with no source hand cards.");
        AnswerProgram(emptySourceGame, ZhiyuSkillId, "activate");
        var emptySourceResult = emptySourceGame.Events.Select(item => item.Payload)
            .OfType<ProgramBindingResolvedEvent>().Last(item => item.SkillId == ZhiyuSkillId);
        Require(emptySourceResult is { Activated: true, Completed: true } &&
                emptySourceGame.Events.Select(item => item.Payload).OfType<ProgramCardsRevealedEvent>()
                    .Any(item => item.SkillId == ZhiyuSkillId && item.Cards.Count > 0) &&
                emptySourceGame.CardMovements.All(move =>
                    move.Reason.Value != "skill-program.classic:zhiyu.SelectAndMoveOwnedCard"),
            "Zhiyu must reveal and complete without opening a discard when the source has no hand cards.");
        var emptySourceReplay = GameReplay.Restore(RoundTrip(emptySourceGame.CreateCheckpoint()),
            emptySourceFixture.Registry);
        Require(SnapshotJson.Serialize(emptySourceReplay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(emptySourceGame.CreateSnapshot(HumanSeat, revealAll: true)),
            "Zhiyu's empty-source skip must replay exactly.");
    }

    private static Fixture FindZhiyuFixture(bool requireSourceCards = true)
    {
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 2_048; seed++)
        {
            var game = CreateGame(
                registry,
                seed,
                modeId: requireSourceCards ? ScenarioPackage.ModeId : ScenarioPackage.EmptySourceModeId);
            StartAndSelect(game);
            for (var step = 0; step < 1_024; step++)
            {
                if (game.State.Status == EngineStatus.Completed) break;
                if (game.PendingDecision is
                    { Kind: DecisionKind.ProgramTrigger, PlayerSeat: HumanSeat,
                        SkillPrompt.SkillId: ZhiyuSkillId, SourceSeat: var sourceSeat } &&
                    sourceSeat is { } source &&
                    (game.CreateSnapshot(HumanSeat, revealAll: true).Players[source].HandCount > 0) ==
                    requireSourceCards)
                {
                    return new Fixture(game, registry, seed);
                }
                if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat } play)
                {
                    var ended = game.Submit(new EndPlayPhaseCommand(HumanSeat,
                        game.Revision, play.PromptId));
                    Require(ended.Accepted, ended.Error?.Message ?? "Xun You could not end Play.");
                    continue;
                }
                if (game.PendingDecision is { PlayerSeat: HumanSeat } human)
                {
                    var decline = human.Choices.FirstOrDefault(choice => choice.Cards.Count == 0);
                    if (decline is null) break;
                    var answered = game.Submit(new AnswerPromptCommand(HumanSeat,
                        human.PromptId, decline.Id, game.Revision));
                    Require(answered.Accepted, answered.Error?.Message ??
                        "Xun You could not decline an unrelated human response.");
                    continue;
                }
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
            if (game.PendingDecision is
                { Kind: DecisionKind.ProgramTrigger, PlayerSeat: HumanSeat,
                    SkillPrompt.SkillId: ZhiyuSkillId })
            {
                Require(skipZhiyu, "Unexpected Zhiyu prompt while reaching play.");
                AnswerProgram(game, ZhiyuSkillId, "skip");
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
        Require(prompt.PlayerSeat == sourceSeat && prompt.Kind == DecisionKind.ProgramTrigger &&
                prompt.SkillPrompt?.SkillId == ZhiyuSkillId,
            $"Expected AI Zhiyu discard, found {prompt.Kind} at seat {prompt.PlayerSeat}.");
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "The AI Zhiyu discard could not advance.");
    }

    private static void AnswerProgram(GameEngine game, string skillId, string action)
    {
        var prompt = RequireProgramPrompt(game, skillId);
        var choice = prompt.Choices.Single(candidate =>
            candidate.Parameters.GetValueOrDefault("program-action") == action);
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

    private static PendingDecision RequireProgramPrompt(GameEngine game, string skillId) =>
        game.PendingDecision is
            { Kind: DecisionKind.ProgramTrigger, PlayerSeat: HumanSeat, SkillPrompt: { } skillPrompt } prompt &&
        skillPrompt.SkillId == skillId
            ? prompt
            : throw new InvalidOperationException(
                $"Expected human program prompt for {skillId}, found " +
                $"{game.PendingDecision?.Kind.ToString() ?? "no prompt"}/" +
                $"{game.PendingDecision?.SkillPrompt?.SkillId ?? "no skill"}.");

    private static GameEngine CreateGame(
        ContentRegistry registry,
        int seed,
        int rulesVersion = GameCheckpoint.CurrentRulesVersion,
        string modeId = ScenarioPackage.ModeId)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 4,
            ModeId = modeId,
            HumanSeat = HumanSeat,
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

    private sealed record Fixture(GameEngine Game, ContentRegistry Registry, int Seed);

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-xun-you-test-4";
        public const string EmptySourceModeId = "identity:classic-xun-you-empty-source-test-4";
        private const string DeckId = "fixture:xun-you-red-slash-deck";
        private const string EmptySourceDeckId = "fixture:xun-you-empty-source-red-slash-deck";
        private static readonly string[] BlankGeneralIds =
        [
            "fixture:xun-you-lord",
            "fixture:xun-you-supporter-a",
            "fixture:xun-you-supporter-b"
        ];

        public PackageManifest Manifest { get; } = new(
            "xun-you-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 120, 0))]);

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
            builder.AddDeck(new ContentDeckRecipe(
                EmptySourceDeckId,
                "荀攸智愚空来源测试牌堆",
                InitialHandSize: 0,
                DrawPerTurn: 1,
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
            builder.AddMode(new ContentModeDefinition(
                EmptySourceModeId,
                "荀攸智愚空来源测试",
                4,
                4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1,
                    [nameof(Role.Renegade)] = 1
                },
                EmptySourceDeckId,
                GeneralCandidateCount: 4,
                GeneralPoolIds: [GeneralId, .. BlankGeneralIds]));
        }
    }
}
