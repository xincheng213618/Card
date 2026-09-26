using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundarySimaYiChecks
{
    private const string GeneralId = "boundary:sima-yi";
    private const string FeedbackId = "boundary:feedback";
    private const string GuicaiId = "boundary:guicai";
    private const string FeedbackMode = "identity:boundary-sima-feedback-5";
    private const string JudgmentMode = "identity:boundary-sima-judgment-5";
    private const string EmptySourceMode = "identity:boundary-sima-empty-source-5";

    public static void FeedbackPerPointAndSourceZones()
    {
        var registry = Registry();
        var general = registry.Generals[GeneralId];
        var feedback = registry.Skills[FeedbackId].Program!.Triggers.Single();
        var guicai = registry.Skills[GuicaiId].Program!.Triggers.Single();
        Require(general is { BaseHp: 3, FactionId: "wei", Gender: GeneralGender.Male } &&
                general.SkillIds.SequenceEqual([FeedbackId, GuicaiId]) &&
                registry.Modes["identity:classic-5"].GeneralPoolIds!.Contains(GeneralId) &&
                registry.Modes["identity:classic-8"].GeneralPoolIds!.Contains(GeneralId) &&
                feedback is { Window: SkillProgramTriggerWindow.AfterDamageApplied,
                    DamageOccurrence: SkillProgramDamageOccurrence.PerDamagePoint, Optional: true } &&
                feedback.Effects.Select(effect => effect.Op).SequenceEqual([
                    SkillProgramTriggerEffectOp.SelectSourceCard,
                    SkillProgramTriggerEffectOp.MoveBoundCards]) &&
                feedback.Effects[0].Zones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) &&
                guicai is { Window: SkillProgramTriggerWindow.JudgmentReplacing, Optional: true } &&
                guicai.Effects.Single() is { Op: SkillProgramTriggerEffectOp.ReplaceJudgment,
                    OldCardDestination: SkillProgramOldJudgmentCardDestination.DiscardPile } &&
                guicai.Effects.Single().Zones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) &&
                guicai.Effects.Single().Suits.Order().SequenceEqual(
                    new[] { Suit.Spade, Suit.Club, Suit.Heart, Suit.Diamond }.Order()) &&
                registry.Skills["classic:feedback"].Program!.Triggers.Single().DamageOccurrence ==
                    SkillProgramDamageOccurrence.PerDamage,
            "Boundary Sima Yi must be a distinct current Wei general with per-point Feedback and four-suit owned-zone Guicai.");

        var (multi, sourceSeat) = FindFeedback(registry, amount: 2);
        var first = Prompt(multi);
        Require(first.IsPrivate && multi.CreateSnapshot(sourceSeat).PendingDecision is null &&
                multi.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().Last() is
                { Amount: 2, TargetSeat: 0 },
            "A two-point hit must publish a private first Feedback choice after real damage.");
        var paused = RoundTrip(multi.CreateCheckpoint());
        var declined = GameReplay.Restore(paused, registry);
        var activated = GameReplay.Restore(paused, registry);
        for (var point = 0; point < 2; point++)
        {
            ReachFeedback(activated);
            AnswerAction(activated, "activate");
            var selection = Prompt(activated);
            Require(selection.IsPrivate && selection.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand) &&
                    choice.Cards.Count == 0),
                "Feedback must publish the source hand as opaque slots, never as public card faces.");
            var choice = selection.Choices.First(choice =>
                choice.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
            Answer(activated, choice);
            ReachFeedback(declined);
            AnswerAction(declined, "skip");
        }
        Require(activated.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Count(item => item.SkillId == FeedbackId && item.Activated) == 2 &&
                declined.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Count(item => item.SkillId == FeedbackId && item.Activated) == 0 &&
                activated.CardMovements.Count(move =>
                    move.From == CardLocation.Hand(sourceSeat) && move.To == CardLocation.Hand(0) &&
                    move.Reason.Value == $"skill-program.{FeedbackId}.MoveBoundCards") == 2 &&
                activated.CreateSnapshot(0, true).Players[0].Hp ==
                    declined.CreateSnapshot(0, true).Players[0].Hp,
            "Each point of one hit must independently accept or decline Feedback and transfer exact source cards.");
        AssertReplay(activated, registry);
        AssertReplay(declined, registry);

        var (equipped, equippedSource) = FindFeedback(registry, requireSourceEquipment: true);
        AnswerAction(equipped, "activate");
        var equippedPrompt = Prompt(equipped);
        var equipmentChoice = equippedPrompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Equipment));
        var equipmentId = equipmentChoice.Cards.Single();
        Require(equipped.CreateSnapshot(0, true).Players[equippedSource].Equipment.Any(card => card.Id == equipmentId),
            "Feedback must expose a real source equipment candidate.");
        var equippedPause = RoundTrip(equipped.CreateCheckpoint());
        var equippedReplay = GameReplay.Restore(equippedPause, registry);
        Answer(equipped, equipmentChoice);
        Answer(equippedReplay, Prompt(equippedReplay).Choices.Single(choice => choice.Id == equipmentChoice.Id));
        Require(equipped.CardMovements.Any(move =>
                    move.CardId == equipmentId && move.From == CardLocation.Equipment(equippedSource) &&
                    move.To == CardLocation.Hand(0) &&
                    move.Reason.Value == $"skill-program.{FeedbackId}.MoveBoundCards") &&
                State(equipped) == State(equippedReplay) && Events(equipped).SequenceEqual(Events(equippedReplay)),
            "Feedback must take a selected visible source equipment card and replay the private choice.");
        VerifyNoSourceCardsSkipsFeedback(registry);
    }

    private static void VerifyNoSourceCardsSkipsFeedback(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 512; seed++)
        {
            var game = Start(registry, EmptySourceMode, seed);
            for (var step = 0; step < 250 && game.State.Status != EngineStatus.Completed; step++)
            {
                var eventCount = game.Events.Count;
                if (!Step(game)) break;
                var applied = game.Events.Skip(eventCount).Select(item => item.Payload)
                    .OfType<DamageAppliedEvent>().FirstOrDefault(item => item.TargetSeat == 0);
                if (applied is null) continue;
                var source = game.CreateSnapshot(0, true).Players[applied.SourceSeat];
                if (source.HandCount != 0 || source.Equipment.Count != 0) continue;
                Require(game.PendingDecision?.SkillPrompt?.SkillId != FeedbackId &&
                        !game.Events.Skip(eventCount).Select(item => item.Payload)
                            .OfType<ProgramBindingStartedEvent>().Any(item => item.SkillId == FeedbackId),
                    "Feedback must not offer a source-card choice when the source has no hand or equipment.");
                AssertReplay(game, registry);
                return;
            }
        }
        throw new InvalidOperationException("No bounded real damage from an empty-handed source was found.");
    }

    public static void GuicaiHandEquipmentJudgmentAndReplay()
    {
        var registry = Registry();
        VerifyJudgmentReplacement(registry, useEquipment: false);
        VerifyJudgmentReplacement(registry, useEquipment: true);
    }

    private static void VerifyJudgmentReplacement(ContentRegistry registry, bool useEquipment)
    {
        var (game, cardId, judgedSeat) = FindJudgment(registry, useEquipment);
        var prompt = Prompt(game);
        var frame = game.ResolutionStack.OfType<JudgmentFrame>().Single();
        var oldCardId = frame.CardId ?? throw new InvalidOperationException("Guicai has no old judgment card.");
        var card = game.CreateSnapshot(0, true).Players[0].Hand
            .Concat(game.CreateSnapshot(0, true).Players[0].Equipment).Single(item => item.Id == cardId);
        var originalZone = useEquipment ? CardLocation.Equipment(0) : CardLocation.Hand(0);
        Require(prompt is { Kind: DecisionKind.ProgramJudgmentReplacement, PlayerSeat: 0, IsPrivate: true } &&
                frame.TargetSeat == judgedSeat && judgedSeat != 0 &&
                prompt.ValidCardIds.Contains(cardId) && game.CreateSnapshot(judgedSeat).PendingDecision is null &&
                (!useEquipment || card.Suit is Suit.Heart or Suit.Diamond),
            "Guicai must privately accept the owner's hand or red equipped card for another player's judgment.");
        var choice = prompt.Choices.Single(item => item.Cards.SequenceEqual([cardId]));
        var paused = RoundTrip(game.CreateCheckpoint());
        var replay = GameReplay.Restore(paused, registry);
        Answer(game, choice);
        Answer(replay, Prompt(replay).Choices.Single(item => item.Id == choice.Id));
        var resolved = game.Events.Select(item => item.Payload).OfType<ProgramJudgmentReplacementResolvedEvent>()
            .Single(item => item.SkillId == GuicaiId && item.Activated);
        Require(resolved is { OwnerSeat: 0, OldCardDestination: SkillProgramOldJudgmentCardDestination.DiscardPile } &&
                resolved.SubjectSeat == judgedSeat && resolved.OldCardId == oldCardId &&
                resolved.ReplacementCardId == cardId &&
                game.CardMovements.Any(move => move.CardId == cardId && move.From == originalZone &&
                    move.To == CardLocation.Processing && move.Reason == CardMoveReasons.ProgramJudgmentReplace) &&
                game.CreateCardZoneDiagnostics().Single(item => item.CardId == oldCardId).Location ==
                    CardLocation.DiscardPile &&
                State(game) == State(replay) && Events(game).SequenceEqual(Events(replay)),
            "Guicai must pay the exact card, discard the old public judgment, and replay the paused replacement.");
    }

    private static (GameEngine Game, int SourceSeat) FindFeedback(
        ContentRegistry registry, int amount = 1, bool requireSourceEquipment = false)
    {
        for (var seed = 1; seed <= 1024; seed++)
        {
            var game = Start(registry, FeedbackMode, seed);
            for (var step = 0; step < 550 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { PlayerSeat: 0, SkillPrompt.SkillId: FeedbackId } &&
                    game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().LastOrDefault() is
                        { SourceSeat: var source, TargetSeat: 0, Amount: var actual } && actual == amount &&
                    game.CreateSnapshot(0, true).Players[source] is { } sourcePlayer &&
                    sourcePlayer.HandCount + sourcePlayer.Equipment.Count >= amount &&
                    (!requireSourceEquipment || sourcePlayer.Equipment.Count > 0))
                    return (game, source);
                if (!Step(game)) break;
            }
        }
        throw new InvalidOperationException($"No bounded Feedback amount={amount}, equipment={requireSourceEquipment} fixture.");
    }

    private static (GameEngine Game, int CardId, int JudgedSeat) FindJudgment(
        ContentRegistry registry, bool useEquipment)
    {
        for (var seed = 1; seed <= 2048; seed++)
        {
            var game = Start(registry, JudgmentMode, seed);
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard }) continue;
            var target = game.CreateSnapshot(0, true).Players.Single(player =>
                player.GeneralId == "classic:xiahou-dun").Seat;
            int? equippedId = null;
            if (useEquipment)
            {
                var hand = game.CreateSnapshot(0, true).Players[0].Hand.ToDictionary(card => card.Id);
                var equip = game.GetHumanLegalActions().FirstOrDefault(action =>
                    action.Kind == LegalActionKind.Equip && action.CardId is { } id &&
                    hand[id].Suit is Suit.Heart or Suit.Diamond);
                if (equip?.CardId is not { } equipmentId) continue;
                equippedId = equipmentId;
                if (!game.Submit(new PlayCardCommand(0, equipmentId, [], game.Revision,
                        Prompt(game).PromptId)).Accepted) continue;
                for (var step = 0; step < 8 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                    if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted) break;
            }
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard }) continue;
            var slash = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Slash && action.TargetSeats.SequenceEqual([target]));
            if (slash?.CardId is not { } slashId) continue;
            if (!game.Submit(new PlayCardCommand(0, slashId, slash.TargetSeats,
                    game.Revision, Prompt(game).PromptId, slash.PlayedCardKind, slash.TargetCardId)).Accepted)
                continue;
            for (var step = 0; step < 85 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.ProgramJudgmentReplacement, PlayerSeat: 0 } prompt &&
                    game.ResolutionStack.OfType<JudgmentFrame>().SingleOrDefault() is { TargetSeat: var judged } &&
                    judged == target)
                {
                    var owner = game.CreateSnapshot(0, true).Players[0];
                    var candidate = useEquipment
                        ? equippedId
                        : owner.Hand.FirstOrDefault(card => card.Kind != CardKind.Slash &&
                            card.Suit is Suit.Heart or Suit.Diamond &&
                            prompt.ValidCardIds.Contains(card.Id))?.Id;
                    if (candidate is { } cardId && prompt.ValidCardIds.Contains(cardId))
                        return (game, cardId, target);
                    break;
                }
                if (!Step(game)) break;
            }
        }
        throw new InvalidOperationException($"No bounded Guicai other-player judgment with equipment={useEquipment}.");
    }

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new Scenario());

    private static GameEngine Start(ContentRegistry registry, string mode, int seed)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, PlayerCount = 5, ModeId = mode, HumanSeat = 0,
            HumanRole = Role.Lord, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2, MaxTurns = 20
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Boundary Sima Yi fixture failed to start.");
        var setup = Prompt(game);
        Require(setup.ValidContentIds.Contains(GeneralId) &&
                game.Submit(new SelectGeneralCommand(0, GeneralId, game.Revision, setup.PromptId)).Accepted,
            "Boundary Sima Yi fixture did not select its independent general.");
        Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
            "Boundary Sima Yi fixture did not reach its first human boundary.");
        return game;
    }

    private static bool Step(GameEngine game)
    {
        var prompt = game.PendingDecision;
        GameCommand command = prompt is { PlayerSeat: 0 }
            ? prompt.Kind == DecisionKind.PlayCard
                ? new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)
                : new AnswerPromptCommand(0, prompt.PromptId,
                    prompt.Choices.FirstOrDefault(choice =>
                        choice.Parameters.GetValueOrDefault("program-action") == "skip" ||
                        choice.Parameters.GetValueOrDefault("response") is "take-damage" or "let-die" ||
                        choice.Parameters.GetValueOrDefault("action") == "program-judgment-replace-skip")?.Id ??
                    prompt.Choices.Last().Id, game.Revision)
            : new AdvanceOneStepCommand(game.Revision);
        return game.Submit(command).Accepted;
    }

    private static void ReachFeedback(GameEngine game)
    {
        for (var step = 0; step < 30 && game.PendingDecision?.SkillPrompt?.SkillId != FeedbackId; step++)
        {
            Require(game.PendingDecision is null && game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "Feedback did not resume once for the next damage point.");
        }
        Require(Prompt(game).SkillPrompt?.SkillId == FeedbackId, "Feedback point choice was not reached.");
    }

    private static void AnswerAction(GameEngine game, string action) => Answer(game,
        Prompt(game).Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == action));

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = Prompt(game);
        var result = game.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Boundary Sima Yi choice was rejected.");
    }

    private static PendingDecision Prompt(GameEngine game) => game.PendingDecision ??
        throw new InvalidOperationException("Boundary Sima Yi fixture lost its prompt.");
    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));
    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, true));
    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();
    private static void AssertReplay(GameEngine game, ContentRegistry registry)
    {
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(State(game) == State(replay) && Events(game).SequenceEqual(Events(replay)),
            "Boundary Sima Yi completed choice did not replay exactly.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Scenario : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("boundary-sima-yi-scenario", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var index in Enumerable.Range(1, 3))
                builder.AddGeneral(new ContentGeneralDefinition(
                    $"boundary-sima-yi:other-{index}", "陪测", "cao_cao", "standard:none", "wei", BaseHp: 4));
            var cards = Enumerable.Range(0, 28).Select(_ =>
                    new ContentDeckPhysicalCard("standard:alcohol", Suit.Heart, 5))
                .Concat(Enumerable.Range(0, 84).Select(index =>
                    new ContentDeckPhysicalCard("standard:slash", (Suit)(index % 4), index % 13 + 1)))
                .Concat(Enumerable.Range(0, 36).Select(index =>
                    new ContentDeckPhysicalCard("standard:offensive_horse", (Suit)(index % 4), 5)))
                .ToArray();
            builder.AddDeck(new ContentDeckRecipe("boundary-sima-yi:deck", "界司马懿差异牌堆", 4, 2, [])
            { PhysicalCards = cards });
            builder.AddGeneral(new ContentGeneralDefinition(
                "boundary-sima-yi:other-4", "空牌陪测", "cao_cao", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe("boundary-sima-yi:empty-deck", "空来源差异牌堆", 1, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 40).Select(index =>
                    new ContentDeckPhysicalCard("standard:slash", (Suit)(index % 4), 7)).ToArray()
            });
            void Mode(string id, string other, string deck = "boundary-sima-yi:deck") =>
                builder.AddMode(new ContentModeDefinition(
                id, "界司马懿差异场景", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, deck, GeneralCandidateCount: 5,
                GeneralPoolIds: [GeneralId, other, "boundary-sima-yi:other-1",
                    "boundary-sima-yi:other-2", "boundary-sima-yi:other-3"]));
            Mode(FeedbackMode, "classic:xu-chu");
            Mode(JudgmentMode, "classic:xiahou-dun");
            Mode(EmptySourceMode, "boundary-sima-yi:other-4", "boundary-sima-yi:empty-deck");
        }
    }
}
