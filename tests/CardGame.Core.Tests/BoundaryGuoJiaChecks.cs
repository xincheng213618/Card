using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryGuoJiaChecks
{
    private const string GeneralId = "boundary:guo-jia";
    private const string Yiji = "boundary:yiji";
    private const string Mode = "identity:classic-boundary-guo-jia-check-5";

    public static void ContentAndRegistration()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var general = registry.Generals[GeneralId];
        Require(general.Name == "界郭嘉" && general.FactionId == "wei" && general.BaseHp == 3 &&
                general.PortraitKey == "boundary_guo_jia" &&
                general.SkillIds.SequenceEqual(["boundary:tiandu", Yiji]) &&
                registry.Modes["identity:classic-5"].GeneralPoolIds!.Contains(GeneralId),
            "2019 Guo Jia must be an independent Wei 3-HP identity general in the formal pool.");
        Require(registry.Skills["boundary:tiandu"].Id == "boundary:tiandu" &&
                registry.Skills["boundary:tiandu"].Program?.Triggers.Single().Effects.Single().Op ==
                    SkillProgramEffectOp.ClaimJudgmentCard,
            "Boundary Tiandu must use the shared judgment-card claim operation.");
        var trigger = registry.Skills[Yiji].Program!.Triggers.Single();
        Require(trigger.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
                trigger.DamageOccurrence == SkillProgramDamageOccurrence.PerDamagePoint &&
                trigger.Optional &&
                trigger.Effects.Select(effect => effect.Op).SequenceEqual([
                    SkillProgramEffectOp.Draw, SkillProgramEffectOp.SelectOwnedCards,
                    SkillProgramEffectOp.GiveBoundCard, SkillProgramEffectOp.GiveBoundCard]) &&
                trigger.Effects[1].NumberExpression == SkillProgramNumberExpression.AllOwnedZoneCards &&
                trigger.Effects[1].Zones.SequenceEqual([CardZoneKind.Hand]),
            "2019 Yiji must draw two, bind the whole current hand, then offer two independent optional gifts per damage point.");
    }

    public static void YijiGivesZeroOneOrTwoCurrentHandCardsAndReplays()
    {
        var registry = Registry();
        var game = FindYiji(registry);
        var offer = Prompt(game);
        Require(offer.SkillPrompt?.SkillId == Yiji && offer.IsPrivate &&
                game.CreateSnapshot(1).PendingDecision is null &&
                game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                    .Last() is { TargetSeat: 0, Amount: 1 },
            "Actual one-point damage to Guo Jia must offer a private Yiji decision.");
        var checkpoint = RoundTrip(game.CreateCheckpoint());
        var before = game.CreateSnapshot(0, true).Players;
        var oldCardId = before[0].Hand.First().Id;

        var skipped = GameReplay.Restore(checkpoint, registry);
        AnswerAction(skipped, "skip");
        Require(skipped.CreateSnapshot(0, true).Players[0].HandCount == before[0].HandCount &&
                skipped.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Any(item => item.SkillId == Yiji && !item.Activated),
            "Declining Yiji must draw and give nothing.");

        var kept = GameReplay.Restore(checkpoint, registry);
        AnswerAction(kept, "activate");
        Require(Prompt(kept).IsPrivate && kept.CreateSnapshot(1).PendingDecision is null,
            "The first gift selection must remain private.");
        AnswerAction(kept, "keep-bound-cards");
        AnswerAction(kept, "keep-bound-cards");
        Require(kept.CreateSnapshot(0, true).Players[0].HandCount == before[0].HandCount + 2,
            "Keeping both optional gifts must retain the two drawn cards.");

        var one = GameReplay.Restore(checkpoint, registry);
        AnswerAction(one, "activate");
        var unchanged = State(one);
        var forgedPrompt = Prompt(one);
        Require(!one.Submit(new AnswerPromptCommand(0, forgedPrompt.PromptId,
                    new ChoiceId("boundary.yiji.forged"), one.Revision)).Accepted && State(one) == unchanged,
            "A fabricated gift choice must be rejected without moving cards.");
        var first = Prompt(one).Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "give-bound-card" &&
            choice.Cards.Single() == oldCardId);
        var firstRecipient = first.Targets.Single();
        Answer(one, first);
        AnswerAction(one, "keep-bound-cards");
        Require(one.CreateSnapshot(0, true).Players[0].HandCount == before[0].HandCount + 1 &&
                one.CreateCardZoneDiagnostics().Any(item => item.CardId == oldCardId &&
                    item.Location == CardLocation.Hand(firstRecipient)) &&
                one.Events.Select(item => item.Payload).OfType<ProgramBoundCardGivenEvent>()
                    .Count(item => item.SkillId == Yiji) == 1,
            "Yiji must allow exactly one gift, including a hand card owned before its draw.");

        var two = GameReplay.Restore(checkpoint, registry);
        AnswerAction(two, "activate");
        Answer(two, Prompt(two).Choices.Single(choice => choice.Id == first.Id));
        Require(Prompt(two).Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "give-bound-card" &&
                    choice.Targets.Single() == firstRecipient && choice.Cards.Single() != oldCardId) &&
                Prompt(two).Choices.All(choice => choice.Cards.Count == 0 || choice.Cards.Single() != oldCardId),
            "Second gift must permit the same recipient but never offer the already transferred card.");
        var second = Prompt(two).Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "give-bound-card" &&
            choice.Targets.Single() != firstRecipient && choice.Cards.Single() != oldCardId);
        var secondRecipient = second.Targets.Single();
        Answer(two, second);
        Require(two.CreateSnapshot(0, true).Players[0].HandCount == before[0].HandCount &&
                two.Events.Select(item => item.Payload).OfType<ProgramBoundCardGivenEvent>()
                    .Where(item => item.SkillId == Yiji).Select(item => item.TargetSeat)
                    .SequenceEqual([firstRecipient, secondRecipient]) &&
                two.CreateCardZoneDiagnostics().Any(item => item.CardId == oldCardId &&
                    item.Location == CardLocation.Hand(firstRecipient)),
            "Yiji must transfer two distinct current hand cards to two distinct legal others.");
        var replay = GameReplay.Restore(RoundTrip(two.CreateCheckpoint()), registry);
        Require(State(replay) == State(two) && Events(replay).SequenceEqual(Events(two)),
            "Completed two-target Yiji must replay from a checkpoint exactly.");
    }

    public static void TianduClaimsOwnJudgmentAndThreePointYijiReplays()
    {
        var registry = Registry(lightningOnly: true);
        var game = Start(registry, 142);
        for (var step = 0; step < 30 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "Could not reach boundary Guo Jia Play to place Lightning.");
        var lightning = game.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.Lightning && action.TargetSeats.SequenceEqual([0]));
        var use = game.Submit(new PlayCardCommand(0, lightning.CardId!.Value, lightning.TargetSeats,
            game.Revision, Prompt(game).PromptId, lightning.PlayedCardKind, lightning.TargetCardId));
        Require(use.Accepted, use.Error?.Message ?? "Guo Jia could not play Lightning.");
        for (var step = 0; step < 700 && game.PendingDecision?.Kind != DecisionKind.ProgramJudgmentTrigger; step++)
            Require(AdvanceConservatively(game),
                $"Could not reach Guo Jia's own effective judgment: step={step}, status={game.State.Status}, " +
                $"winner={game.State.Winner}, prompt={game.PendingDecision?.Kind}, " +
                $"judgments={string.Join(',', game.Events.Select(item => item.Payload).OfType<JudgmentResolvedEvent>().Select(item => $"{item.TargetSeat}:{item.Reason}"))}, " +
                $"hp={game.CreateSnapshot(0, true).Players[0].Hp}.");
        var prompt = Prompt(game);
        Require(prompt.Kind == DecisionKind.ProgramJudgmentTrigger && prompt.PlayerSeat == 0 &&
                prompt.Choices.Select(choice => choice.Parameters.GetValueOrDefault("action"))
                    .Order(StringComparer.Ordinal).SequenceEqual([
                        "program-judgment-trigger-activate", "program-judgment-trigger-skip"]),
            "Boundary Tiandu must use the shared own-judgment claim prompt.");
        var judgment = game.Events.Select(item => item.Payload).OfType<JudgmentResolvedEvent>()
            .Last(item => item.TargetSeat == 0 && item.Reason == JudgmentReasons.Lightning);
        var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        var claim = prompt.Choices.Single(choice => choice.Parameters.GetValueOrDefault("action") ==
            "program-judgment-trigger-activate");
        Answer(game, claim);
        Answer(paused, Prompt(paused).Choices.Single(choice => choice.Id == claim.Id));
        Require(game.CreateCardZoneDiagnostics().Any(item => item.CardId == judgment.CardId &&
                    item.Location == CardLocation.Hand(0)) &&
                game.Events.Select(item => item.Payload).OfType<ProgramJudgmentCardClaimedEvent>()
                    .Any(item => item.OwnerSeat == 0 && item.SkillId == "boundary:tiandu") &&
                State(game) == State(paused) && Events(game).SequenceEqual(Events(paused)),
            "Boundary Tiandu must claim the exact own effective card and resume from checkpoint.");
        var offers = 0;
        for (var step = 0; step < 100 && offers < 3; step++)
        {
            if (game.PendingDecision?.SkillPrompt?.SkillId == Yiji)
            {
                offers++;
                AnswerAction(game, "skip");
                AnswerAction(paused, "skip");
            }
            else
            {
                Require(AdvanceConservatively(game) && AdvanceConservatively(paused),
                    "Could not continue three-point Lightning damage.");
            }
        }
        Require(offers == 3 &&
                game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                    .Any(item => item.TargetSeat == 0 && item.Amount == 3) &&
                State(game) == State(paused) && Events(game).SequenceEqual(Events(paused)),
            "Three actual Lightning damage points must yield three independent Yiji offers and replay.");
    }

    public static void YijiHandlesExhaustedDrawSource()
    {
        var registry = Registry(deckSize: 20);
        GameEngine? found = null;
        for (var seed = 1; seed <= 128 && found is null; seed++)
        {
            var candidate = Start(registry, seed);
            for (var step = 0; step < 600 && candidate.State.Winner == Winner.None; step++)
            {
                if (candidate.PendingDecision is { SkillPrompt.SkillId: Yiji } offer &&
                    offer.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate") &&
                    candidate.CreateSnapshot(0, true).Players[0].HandCount > 0 &&
                    candidate.CreateCardZoneDiagnostics().All(item =>
                        item.Location != CardLocation.DrawPile && item.Location != CardLocation.DiscardPile))
                {
                    found = candidate;
                    break;
                }
                if (!AdvanceConservatively(candidate)) break;
            }
        }
        Require(found is not null, "No bounded Yiji fixture exhausted both draw and discard sources.");
        var game = found!;
        var before = game.CreateSnapshot(0, true).Players[0].Hand.Select(card => card.Id).ToHashSet();
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        AnswerAction(game, "activate");
        AnswerAction(replay, "activate");
        Require(game.PendingDecision is { } gift && gift.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "keep-bound-cards"),
            $"Depleted Yiji did not expose the first optional gift: status={game.State.Status}, " +
            $"prompt={game.PendingDecision?.Kind}, hp={game.CreateSnapshot(0, true).Players[0].Hp}.");
        Require(Prompt(game).Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "give-bound-card" &&
                before.Contains(choice.Cards.Single())) &&
                game.CreateSnapshot(0, true).Players[0].Hand.Select(card => card.Id).ToHashSet().SetEquals(before),
            "Empty draw sources must draw zero while preserving optional gifts from the existing hand.");
        for (var index = 0; index < 2; index++)
        {
            Require(Prompt(game).Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "keep-bound-cards"),
                $"Depleted Yiji lost optional gift {index + 1}: status={game.State.Status}, prompt={game.PendingDecision?.Kind}.");
            AnswerAction(game, "keep-bound-cards");
            AnswerAction(replay, "keep-bound-cards");
        }
        Require(game.CreateSnapshot(0, true).Players[0].Hand.Select(card => card.Id).ToHashSet().SetEquals(before) &&
                State(game) == State(replay) && Events(game).SequenceEqual(Events(replay)),
            "Exhausted Yiji must finish without phantom draws or replay divergence.");
    }

    private static GameEngine FindYiji(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 128; seed++)
        {
            var game = Start(registry, seed);
            for (var step = 0; step < 1200 && game.State.Winner == Winner.None; step++)
            {
                if (game.PendingDecision?.SkillPrompt?.SkillId == Yiji &&
                    game.CreateSnapshot(0, true).Players[0].HandCount > 0)
                    return game;
                if (!AdvanceConservatively(game)) break;
            }
        }
        throw new InvalidOperationException("No bounded real-damage Guo Jia Yiji fixture was found.");
    }

    private static ContentRegistry Registry(bool lightningOnly = false, int deckSize = 160) => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new Scenario(lightningOnly, deckSize));

    private static GameEngine Start(ContentRegistry registry, int seed)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, PlayerCount = 5, ModeId = Mode, HumanSeat = 0,
            HumanRole = Role.Lord, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 12
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Guo Jia fixture failed to start.");
        var select = Prompt(game);
        Require(select.ValidContentIds.Contains(GeneralId) &&
                game.Submit(new SelectGeneralCommand(0, GeneralId, game.Revision, select.PromptId)).Accepted,
            "Guo Jia was not selectable.");
        return game;
    }

    private static bool AdvanceConservatively(GameEngine game)
    {
        var prompt = game.PendingDecision;
        if (prompt is null || prompt.PlayerSeat != 0)
            return game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted;
        if (prompt.Kind == DecisionKind.PlayCard)
            return game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)).Accepted;
        if (prompt.Kind == DecisionKind.DiscardCards)
            return game.Submit(new DiscardCardsCommand(0,
                prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                prompt.PromptId, game.Revision)).Accepted;
        var choice = prompt.Choices.FirstOrDefault(candidate =>
            candidate.Parameters.GetValueOrDefault("program-action") == "skip" ||
            candidate.Cards.Count == 0) ?? prompt.Choices.LastOrDefault();
        return choice is not null && game.Submit(new AnswerPromptCommand(0, prompt.PromptId,
            choice.Id, game.Revision)).Accepted;
    }

    private static PendingDecision Prompt(GameEngine game) =>
        game.PendingDecision ?? throw new InvalidOperationException("Guo Jia fixture lost its prompt.");
    private static void AnswerAction(GameEngine game, string action) => Answer(game,
        Prompt(game).Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == action));
    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = Prompt(game);
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Guo Jia prompt answer failed.");
    }
    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));
    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, true));
    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Scenario(bool lightningOnly, int deckSize) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("boundary-guo-jia-scenario", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddDeck(new ContentDeckRecipe("fixture:boundary-guo-jia-deck", "Guo Jia Deck", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, deckSize).Select(index =>
                    lightningOnly
                        ? new ContentDeckPhysicalCard("standard:lightning", Suit.Spade, 2)
                        : new ContentDeckPhysicalCard(index % 7 == 0 ? "standard:alcohol" : "standard:slash",
                            (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            builder.AddMode(new ContentModeDefinition(Mode, "2019 Guo Jia", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, "fixture:boundary-guo-jia-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [GeneralId, "classic:liu-bei", "classic:guan-yu",
                    "classic:zhang-fei", "classic:sun-quan"]));
        }
    }
}
