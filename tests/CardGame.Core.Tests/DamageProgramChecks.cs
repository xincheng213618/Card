using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DamageProgramChecks
{
    private const int HumanSeat = 0;


    public static void GenericDamageChoicesClaimSelectGiftDrawAndReplay()
    {
        VerifyDamageCardClaim();
        VerifyDamageCardSkip();
        VerifyClassicSourceCardSelection();
        VerifyYijiGift();
        VerifyJiemingDraw();
        VerifyNestedDamageCursorReplay();
    }

    private static void VerifyNestedDamageCursorReplay()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new NestedDamagePackage());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 1,
            PlayerCount = 4,
            ModeId = NestedDamagePackage.ModeId,
            HumanSeat = HumanSeat,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 8
        }, registry);
        SubmitAccepted(game, new StartGameCommand());
        var setup = game.PendingDecision is { Kind: DecisionKind.SelectGeneral } selectedSetup &&
                    selectedSetup.ValidContentIds.Contains(NestedDamagePackage.SourceGeneralId)
            ? selectedSetup
            : throw new InvalidOperationException(
                "The nested-damage setup did not offer the source general.");
        SubmitAccepted(game, new SelectGeneralCommand(HumanSeat,
            NestedDamagePackage.SourceGeneralId, game.Revision, setup.PromptId));
        for (var step = 0; step < 100 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
        {
            Require(game.PendingDecision is null,
                "The nested-damage fixture reached an unexpected setup prompt.");
            SubmitAccepted(game, new AdvanceOneStepCommand(game.Revision));
        }
        var play = game.PendingDecision is { Kind: DecisionKind.PlayCard } playPrompt
            ? playPrompt
            : throw new InvalidOperationException(
                "The nested-damage fixture did not reach the human play phase.");
        var slash = game.GetHumanLegalActions().FirstOrDefault(action =>
            action.Kind == LegalActionKind.Slash && action.CardId is not null &&
            action.TargetSeats.SequenceEqual([1])) ??
            throw new InvalidOperationException(
                "The nested-damage fixture has no Slash against seat 1.");
        SubmitAccepted(game, new PlayCardCommand(HumanSeat, slash.CardId!.Value,
            slash.TargetSeats, game.Revision, play.PromptId));
        for (var step = 0; step < 100 &&
             !game.ResolutionStack.OfType<DamageTriggerWindowFrame>()
                 .Any(frame => frame.TargetSeat == 1); step++)
        {
            Require(game.PendingDecision is null,
                "The nested-damage fixture reached an unexpected response prompt.");
            SubmitAccepted(game, new AdvanceOneStepCommand(game.Revision));
        }
        var outer = game.ResolutionStack.OfType<DamageTriggerWindowFrame>()
            .Single(frame => frame.TargetSeat == 1);
        Require(outer.CandidateIndex == 0 && outer.Candidates.Count == 2,
            "The outer damage window did not freeze both ordered candidates.");

        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(State(replay) == State(game) && Events(replay).SequenceEqual(Events(game)),
            "The nested-damage checkpoint did not restore its initial frame.");
        DriveNestedDamage(game, outer.Id, registry);
        DriveNestedDamage(replay, outer.Id, registry);
        Require(State(replay) == State(game) && Events(replay).SequenceEqual(Events(game)),
            "Nested after-damage candidates did not replay exactly.");
        var applied = game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
            .ToArray();
        Require(applied.Count(item => item.TargetSeat == 1) == 1 &&
                applied.Count(item => item.TargetSeat == 0) == 1,
            "The nested damage did not apply exactly once in each direction.");
        Require(game.CardMovements.Count(move =>
                    move.From == CardLocation.DrawPile &&
                    move.To == CardLocation.Hand(1) &&
                    move.Reason.Value == "skill-program.damage-cursor-test:skill.Draw") == 1,
            "The resumed second candidate did not draw exactly one physical card.");
        var advances = game.Events.Select(item => item.Payload)
            .OfType<DamageTriggerWindowAdvancedEvent>()
            .Where(item => item.ResolutionId == outer.Id)
            .Select(item => item.CandidateIndex).ToArray();
        Require(advances.SequenceEqual([1, 2]),
            "The outer damage cursor did not resume at its second candidate.");
        foreach (var binding in new[] { "nested", "follow" })
            Require(game.Events.Select(item => item.Payload)
                    .OfType<ProgramBindingResolvedEvent>()
                    .Count(item => item.BindingId == binding && item.Completed) == 1,
                $"The {binding} after-damage binding did not complete exactly once.");
    }

    private static void DriveNestedDamage(GameEngine game, long outerFrameId, ContentRegistry registry)
    {
        var checkedChildCheckpoint = false;
        for (var step = 0; step < 100; step++)
        {
            if (!game.ResolutionStack.Any(frame => frame.Id == outerFrameId) &&
                !game.ResolutionStack.OfType<DamageFrame>().Any())
            {
                Require(checkedChildCheckpoint, "The nested damage fixture never checkpointed its paused child.");
                return;
            }
            if (!checkedChildCheckpoint && game.ResolutionStack.OfType<ProgramSkillFrame>()
                    .Any(frame => frame.AttackAttempt is not null))
            {
                var childReplay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
                Require(State(childReplay) == State(game) && Events(childReplay).SequenceEqual(Events(game)),
                    "A paused nested program damage attempt did not checkpoint and replay exactly.");
                checkedChildCheckpoint = true;
            }
            Require(game.PendingDecision is null,
                "Nested after-damage resolution unexpectedly required a human response.");
            SubmitAccepted(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Nested after-damage resolution exceeded 100 steps.");
    }

    private sealed class NestedDamagePackage : IGameContentPackage
    {
        internal const string ModeId = "damage-cursor-test:mode";
        internal const string SourceGeneralId = "damage-cursor-test:source";
        private static readonly string[] TargetGeneralIds =
            Enumerable.Range(1, 3).Select(index => $"damage-cursor-test:target-{index}").ToArray();
        private const string SkillId = "damage-cursor-test:skill";
        private const string DeckId = "damage-cursor-test:deck";

        public PackageManifest Manifest { get; } = new(
            "damage-cursor-test", new Version(1, 0, 0),
            [new PackageDependency("standard", new Version(1, 11, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"damage-cursor-test:skill","revision":1,
                 "minimumRulesVersion":{{GameCheckpoint.CurrentRulesVersion}},"triggers":[
                 {"id":"nested","window":"afterDamageApplied","subject":"owner",
                  "damageOccurrence":"perDamage","optional":false,"priority":0,
                  "effects":[{"op":"damage","target":"owner",
                    "targetRef":{"kind":"eventSource"},"sourceRef":{"kind":"owner"},"amount":1}]},
                 {"id":"follow","window":"afterDamageApplied","subject":"owner",
                  "damageOccurrence":"perDamage","optional":false,"priority":1,
                  "effects":[{"op":"draw","target":"owner","amount":1}]}]},
                {"id":"damage-cursor-test:child","revision":1,
                 "minimumRulesVersion":{{GameCheckpoint.CurrentRulesVersion}},"triggers":[
                 {"id":"child-follow","window":"afterDamageApplied","subject":"owner",
                  "damageOccurrence":"perDamage","optional":false,
                  "effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
                """;
            const string presentation = """
                {"schemaVersion":3,"skills":{"damage-cursor-test:skill":
                {"name":"Damage cursor","description":"Nested damage fixture"},
                "damage-cursor-test:child":{"name":"Child cursor","description":"Paused child fixture"}}}
                """;
            var catalog = SkillProgramCatalog.Load(rules, presentation);
            var text = catalog.Presentations[SkillId];
            builder.AddSkill(new ContentSkillDefinition(SkillId, text.Name, text.Description)
            { Program = catalog.Programs[SkillId] });
            var childText = catalog.Presentations["damage-cursor-test:child"];
            builder.AddSkill(new ContentSkillDefinition("damage-cursor-test:child", childText.Name, childText.Description)
            { Program = catalog.Programs["damage-cursor-test:child"] });
            builder.AddGeneral(new ContentGeneralDefinition(
                SourceGeneralId, "Source", "cao_cao", "damage-cursor-test:child", "wei", BaseHp: 4));
            foreach (var targetGeneralId in TargetGeneralIds)
                builder.AddGeneral(new ContentGeneralDefinition(
                    targetGeneralId, "Target", "cao_cao", SkillId, "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe(DeckId, "Slash deck", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 40)
                    .Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 5))
                    .ToArray()
            });
            builder.AddMode(new ContentModeDefinition(ModeId, "Damage cursor", 4, 4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1,
                    [nameof(Role.Renegade)] = 1
                }, DeckId: DeckId, GeneralCandidateCount: 4,
                GeneralPoolIds: [SourceGeneralId, .. TargetGeneralIds]));
        }
    }

    private static void VerifyDamageCardClaim()
    {
        var registry = StandardContentRegistry.Create();
        var game = FindProgramBoundary(registry, "standard:hua-tuo", "standard:feedback");
        var damage = game.ResolutionStack.OfType<DamageTriggerWindowFrame>().Single();
        var cardId = damage.SourceCardId ??
            throw new InvalidOperationException("The Feedback fixture has no physical damage card.");
        Require(game.PendingDecision is
                {
                    Kind: DecisionKind.ProgramTrigger,
                    SkillPrompt.SkillId: "standard:feedback"
                },
            "Current Feedback must pause only in the generic program route.");

        var checkpoint = RoundTrip(game.CreateCheckpoint());
        var replay = GameReplay.Restore(checkpoint, registry);
        AnswerProgram(game, "activate");
        AnswerProgram(replay, "activate");

        var claimed = game.Events.Select(item => item.Payload)
            .OfType<ProgramDamageCardsClaimedEvent>()
            .Single(item => item.SkillId == "standard:feedback");
        Require(claimed.OwnerSeat == HumanSeat && claimed.CardIds.Contains(cardId) &&
                game.CreateSnapshot(HumanSeat).Players[HumanSeat].Hand.Any(card => card.Id == cardId) &&
                game.CardMovements.Any(move =>
                    move.CardId == cardId &&
                    move.From == CardLocation.Processing &&
                    move.To == CardLocation.Hand(HumanSeat) &&
                    move.Reason.Value == "skill-program.standard:feedback.ClaimDamageCards"),
            "Generic Feedback did not claim the exact physical damage card.");
        Require(State(replay) == State(game) && Events(replay).SequenceEqual(Events(game)),
            "A pending generic Feedback claim did not replay exactly.");
    }

    private static void VerifyClassicSourceCardSelection()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var game = FindProgramBoundary(registry, "classic:sima-yi", "classic:feedback");
        AnswerProgram(game, "activate");
        var prompt = RequireProgramPrompt(game, "classic:feedback", "select-source-card");
        Require(prompt.IsPrivate && prompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("source-zone") == CardZoneKind.Hand.ToString() &&
                    choice.Cards.Count == 0),
            "Classic Feedback must represent hidden hand cards as opaque slots.");

        var choice = prompt.Choices[0];
        var sourceSeat = prompt.TargetSeat ??
            throw new InvalidOperationException("Classic Feedback lost its source seat.");
        var sourceBefore = game.CreateSnapshot(HumanSeat, revealAll: true).Players[sourceSeat];
        var expectedCardId = choice.Parameters.GetValueOrDefault("source-zone") == CardZoneKind.Hand.ToString()
            ? sourceBefore.Hand[int.Parse(choice.Parameters["slot-index"],
                System.Globalization.CultureInfo.InvariantCulture)].Id
            : choice.Cards.Single();
        var expectedFrom = choice.Parameters.GetValueOrDefault("source-zone") == CardZoneKind.Hand.ToString()
            ? CardLocation.Hand(sourceSeat)
            : CardLocation.Equipment(sourceSeat);
        var checkpoint = RoundTrip(game.CreateCheckpoint());
        var replay = GameReplay.Restore(checkpoint, registry);
        Answer(game, choice);
        Answer(replay, replay.PendingDecision!.Choices.Single(item => item.Id == choice.Id));

        Require(game.CardMovements.Any(move =>
                    move.CardId == expectedCardId &&
                    move.From == expectedFrom &&
                    move.To == CardLocation.Hand(HumanSeat) &&
                    move.Reason.Value == "skill-program.classic:feedback.MoveBoundCards") &&
                game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Any(item => item.SkillId == "classic:feedback"),
            "Classic Feedback did not move the exact selected source card through the generic binding.");
        Require(State(replay) == State(game) && Events(replay).SequenceEqual(Events(game)),
            "A pending classic Feedback source-card choice did not replay exactly.");
    }

    private static void VerifyDamageCardSkip()
    {
        var registry = StandardContentRegistry.Create();
        var game = FindProgramBoundary(registry, "standard:hua-tuo", "standard:feedback");
        var prompt = game.PendingDecision!;
        var damage = game.ResolutionStack.OfType<DamageTriggerWindowFrame>().Single();
        var cardId = damage.SourceCardId ??
            throw new InvalidOperationException("The Feedback skip fixture has no physical damage card.");
        var ownerHandBefore = game.CreateSnapshot(HumanSeat, revealAll: true)
            .Players[HumanSeat].HandCount;
        Require(prompt.IsPrivate && game.CreateSnapshot(1).PendingDecision is null,
            "A generic Feedback choice must remain private to its owner.");

        AnswerProgram(game, "skip");

        Require(!game.Events.Select(item => item.Payload).OfType<ProgramDamageCardsClaimedEvent>()
                    .Any(item => item.CardIds.Contains(cardId)) &&
                game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].HandCount == ownerHandBefore &&
                game.CardMovements.Any(move =>
                    move.CardId == cardId &&
                    move.From == CardLocation.Processing &&
                    move.To == CardLocation.DiscardPile &&
                    move.Reason == CardMoveReasons.UseFinished),
            "Skipping generic Feedback must leave the damage card to normal card-use cleanup.");
    }

    private static void VerifyYijiGift()
    {
        var registry = StandardContentRegistry.Create();
        var game = FindProgramBoundary(registry, "standard:guo-jia", "standard:yiji");
        var damage = game.ResolutionStack.OfType<DamageTriggerWindowFrame>().Single();
        var candidateCount = damage.Candidates.Count(candidate =>
            candidate.ProgramId == "standard:yiji");
        var damageAmount = game.ResolutionStack.OfType<DamageFrame>().Single().Amount;
        Require(candidateCount == damageAmount && damage.Candidates
                .Where(candidate => candidate.ProgramId == "standard:yiji")
                .Select(candidate => candidate.OccurrenceIndex)
                .SequenceEqual(Enumerable.Range(0, damageAmount)),
            "Yiji must freeze one ordered candidate per applied damage point.");

        AnswerProgram(game, "activate");
        var prompt = RequireProgramPrompt(game, "standard:yiji", "give-bound-card");
        var frame = game.ResolutionStack.OfType<ProgramSkillFrame>().Single();
        var binding = frame.CardSetBindings.Single(item => item.Name == "drawn");
        Require(binding.CardIds.Count == 2 && binding.SourceLocations.All(location =>
                    location == CardLocation.Hand(HumanSeat)) &&
                prompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "keep-bound-cards"),
            "Yiji must retain its two drawn cards in a private frozen-location binding.");

        var gift = prompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "give-bound-card");
        var checkpoint = RoundTrip(game.CreateCheckpoint());
        var replay = GameReplay.Restore(checkpoint, registry);
        Answer(game, gift);
        Answer(replay, replay.PendingDecision!.Choices.Single(item => item.Id == gift.Id));
        var given = game.Events.Select(item => item.Payload).OfType<ProgramBoundCardGivenEvent>()
            .Single(item => item.SkillId == "standard:yiji");
        Require(given.CardId == gift.Cards.Single() && given.TargetSeat == gift.Targets.Single() &&
                game.CardMovements.Any(move =>
                    move.CardId == given.CardId &&
                    move.From == CardLocation.Hand(HumanSeat) &&
                    move.To == CardLocation.Hand(given.TargetSeat) &&
                    move.Reason.Value == "skill-program.standard:yiji.GiveBoundCard"),
            "Yiji did not give exactly one drawn bound card through the generic prompt.");
        Require(State(replay) == State(game) && Events(replay).SequenceEqual(Events(game)),
            "A pending generic Yiji gift did not replay exactly.");
    }

    private static void VerifyJiemingDraw()
    {
        var registry = StandardContentRegistry.Create();
        var game = FindProgramBoundary(registry, "standard:xun-yu", "standard:jieming");
        var damage = game.ResolutionStack.OfType<DamageTriggerWindowFrame>().Single();
        var damageAmount = game.ResolutionStack.OfType<DamageFrame>().Single().Amount;
        Require(damage.Candidates.Count(candidate => candidate.ProgramId == "standard:jieming") == damageAmount,
            "Jieming must freeze one candidate per applied damage point.");

        AnswerProgram(game, "activate");
        var prompt = RequireProgramPrompt(game, "standard:jieming", "select-target");
        var choice = prompt.Choices[0];
        var targetSeat = choice.Targets.Single();
        var before = game.CreateSnapshot(HumanSeat, revealAll: true).Players[targetSeat];
        var expected = before.MaxHp - before.HandCount;
        Require(expected > 0, "Jieming published a target whose hand was already at maximum HP.");
        var checkpoint = RoundTrip(game.CreateCheckpoint());
        var replay = GameReplay.Restore(checkpoint, registry);
        Answer(game, choice);
        Answer(replay, replay.PendingDecision!.Choices.Single(item => item.Id == choice.Id));
        var after = game.CreateSnapshot(HumanSeat, revealAll: true).Players[targetSeat];
        Require(after.HandCount == before.MaxHp &&
                game.CardMovements.Count(move =>
                    move.From == CardLocation.DrawPile &&
                    move.To == CardLocation.Hand(targetSeat) &&
                    move.Reason.Value == "skill-program.standard:jieming.Draw") == expected,
            "Jieming did not evaluate target max-HP minus current hand count at execution time.");
        Require(State(replay) == State(game) && Events(replay).SequenceEqual(Events(game)),
            "A pending generic Jieming target choice did not replay exactly.");
    }

    private static GameEngine FindProgramBoundary(
        ContentRegistry registry,
        string generalId,
        string skillId)
    {
        var offeredCount = 0;
        var promptKinds = new HashSet<string>(StringComparer.Ordinal);
        string? lastFailure = null;
        for (var seed = 1; seed <= 8_192; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                HumanSeat = HumanSeat,
                HumanRole = Role.Lord,
                ModeId = generalId.StartsWith("classic:", StringComparison.Ordinal)
                    ? "identity:classic-5"
                    : "identity:standard-5",
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 180
            }, registry);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "The damage-program fixture failed to start.");
            if (game.PendingDecision?.ValidContentIds.Contains(generalId, StringComparer.Ordinal) != true)
                continue;
            offeredCount++;
            var selected = game.Submit(new SelectGeneralCommand(
                HumanSeat, generalId, game.Revision, game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "The damage-program general was rejected.");
            var result = SubmitAccepted(game, new AdvanceCommand(game.Revision));

            for (var step = 0; step < 4_000 && result.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { } observed)
                    promptKinds.Add($"{observed.Kind}:{observed.SkillPrompt?.SkillId ?? "-"}");
                if (game.PendingDecision is
                    {
                        Kind: DecisionKind.ProgramTrigger,
                        PlayerSeat: HumanSeat,
                        SkillPrompt.SkillId: var promptedSkill
                    } prompt && promptedSkill == skillId && prompt.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("program-action") == "activate"))
                    return game;
                try
                {
                    result = AdvanceFixture(game);
                }
                catch (InvalidOperationException exception)
                {
                    lastFailure = exception.Message;
                    break;
                }
            }
        }
        throw new InvalidOperationException(
            $"No deterministic damage-program boundary was found for {skillId}; " +
            $"offered={offeredCount}, prompts=[{string.Join(',', promptKinds.Order())}], " +
            $"lastFailure={lastFailure ?? "none"}.");
    }

    private static EngineRunResult AdvanceFixture(GameEngine game)
    {
        if (game.PendingDecision is { PlayerSeat: HumanSeat } prompt)
        {
            if (prompt.Kind == DecisionKind.PlayCard)
                return SubmitAccepted(game, new EndPlayPhaseCommand(
                    HumanSeat, game.Revision, prompt.PromptId));
            if (prompt.Kind == DecisionKind.ProgramTrigger)
            {
                var choice = prompt.Choices.FirstOrDefault(item =>
                    item.Parameters.GetValueOrDefault("program-action") is "skip" or
                        "keep-bound-cards") ?? prompt.Choices[0];
                return SubmitAccepted(game, new AnswerPromptCommand(
                    HumanSeat, prompt.PromptId, choice.Id, game.Revision));
            }
            var choiceIndex = prompt.Kind == DecisionKind.SelectHarvestCard ? 0 : prompt.Choices.Count - 1;
            return SubmitAccepted(game, new AnswerPromptCommand(
                HumanSeat, prompt.PromptId, prompt.Choices[choiceIndex].Id, game.Revision));
        }
        return SubmitAccepted(game, new AdvanceOneStepCommand(game.Revision));
    }

    private static EngineRunResult SubmitAccepted(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.Message ?? $"Fixture command {command.GetType().Name} failed.");
        return result.Result;
    }

    private static PendingDecision RequireProgramPrompt(
        GameEngine game,
        string skillId,
        string action) =>
        game.PendingDecision is
        {
            Kind: DecisionKind.ProgramTrigger,
            PlayerSeat: HumanSeat,
            SkillPrompt.SkillId: var actual
        } prompt && actual == skillId && prompt.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == action)
            ? prompt
            : throw new InvalidOperationException(
                $"Expected {skillId}/{action}, found {game.PendingDecision?.SkillPrompt?.SkillId}/" +
                $"{string.Join(',', game.PendingDecision?.Choices.Select(choice =>
                    choice.Parameters.GetValueOrDefault("program-action")) ?? [])}.");

    private static void AnswerProgram(GameEngine game, string action)
    {
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("The program prompt is unavailable.");
        var choice = prompt.Choices.Single(item =>
            item.Parameters.GetValueOrDefault("program-action") == action);
        Answer(game, choice);
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("The prompt is unavailable.");
        var answered = game.Submit(new AnswerPromptCommand(
            HumanSeat, prompt.PromptId, choice.Id, game.Revision));
        Require(answered.Accepted, answered.Error?.Message ?? "The program answer was rejected.");
    }

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static string State(GameEngine game) =>
        SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true));

    private static IReadOnlyList<string> Events(GameEngine game) => game.Events
        .Select(item => $"{item.Sequence}|{item.Payload.GetType().Name}|" +
                        JsonSerializer.Serialize(item.Payload, item.Payload.GetType()))
        .ToArray();

    private static void Reject(string rules, string? expectedMessage)
    {
        const string presentation =
            "{\"schemaVersion\":3,\"skills\":{\"fixture:damage\":{\"name\":\"Damage\",\"description\":\"Fixture\"}}}";
        try
        {
            _ = SkillProgramCatalog.Load(rules, presentation);
        }
        catch (InvalidOperationException exception) when (
            expectedMessage is null || exception.Message.Contains(
                expectedMessage, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        throw new InvalidOperationException(expectedMessage is null
            ? "The invalid schema-15 damage program was accepted."
            : $"The invalid schema-15 damage program did not mention '{expectedMessage}'.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private const string ValidationRules = """
        {"schemaVersion":62,"skills":[{"id":"fixture:damage","revision":1,
        "minimumRulesVersion": 171,"modifiers":[],"viewAs":[],"activations":[],"triggers":[
        {"id":"per-point","window":"afterDamageApplied","subject":"owner",
        "damageOccurrence":"perDamagePoint","optional":true,"priority":0,
        "effects":[
        {"op":"selectTarget","target":"owner","targetKind":"anyLivingHandBelowMaxHp"},
        {"op":"draw","target":"selectedTarget","numberExpression":"targetMaxHpMinusHandCount"}]}],
        "contributions":[],"cardIdentities":[]}]}
        """;
}
