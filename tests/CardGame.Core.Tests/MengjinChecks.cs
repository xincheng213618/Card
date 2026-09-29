using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class MengjinChecks
{
    public static void HiddenHandPublicEquipmentChoiceAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var boundary = MengjinScenario.FindHumanTrigger();
        var game = boundary.Game;
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Mengjin fixture lost its private activation prompt.");
        Require(prompt.Kind == DecisionKind.ProgramTrigger &&
                prompt.SkillPrompt?.SkillId == "classic:mengjin" &&
                prompt.Choices.Count == 2 &&
                prompt.PlayerSeat == boundary.SourceSeat &&
                prompt.TargetSeat == boundary.TargetSeat &&
                prompt.IsPrivate &&
                game.CreateSnapshot(boundary.TargetSeat).PendingDecision is null,
            "A fully dodged Slash must privately offer Mengjin before its payment choice.");

        var checkpoint = RoundTrip(game.CreateCheckpoint());
        var restored = GameReplay.Restore(checkpoint, registry);
        Require(State(restored) == State(game) && Events(restored).SequenceEqual(Events(game)),
            "An in-flight Mengjin prompt must restore exactly.");

        var invalid = game.Submit(new AnswerPromptCommand(
            boundary.SourceSeat,
            prompt.PromptId,
            new ChoiceId("forged-mengjin-choice"),
            game.Revision));
        Require(!invalid.Accepted && State(game) == State(restored),
            "A forged Mengjin choice must be rejected without mutation.");

        var skipped = GameReplay.Restore(checkpoint, registry);
        var skippedPrompt = skipped.PendingDecision!;
        var skip = skippedPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "skip");
        var targetBeforeSkip = skipped.CreateSnapshot(boundary.SourceSeat, revealAll: true)
            .Players[boundary.TargetSeat];
        var skippedResult = skipped.Submit(new AnswerPromptCommand(
            boundary.SourceSeat, skippedPrompt.PromptId, skip.Id, skipped.Revision));
        var targetAfterSkip = skipped.CreateSnapshot(boundary.SourceSeat, revealAll: true)
            .Players[boundary.TargetSeat];
        Require(skippedResult.Accepted &&
                skipped.Events.Select(item => item.Payload).OfType<ProgramCardTriggerResolvedEvent>()
                    .Any(item => item.SkillId == "classic:mengjin" && !item.Activated) &&
                targetAfterSkip.Hp == targetBeforeSkip.Hp &&
                targetAfterSkip.Hand.Count == targetBeforeSkip.Hand.Count &&
                targetAfterSkip.Equipment.Count == targetBeforeSkip.Equipment.Count,
            skippedResult.Error?.Message ?? "Skipping Mengjin must end the canceled Slash without discarding.");

        var used = GameReplay.Restore(checkpoint, registry);
        var usedPrompt = used.PendingDecision!;
        var activate = usedPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate");
        var activated = used.Submit(new AnswerPromptCommand(
            boundary.SourceSeat, usedPrompt.PromptId, activate.Id, used.Revision));
        Require(activated.Accepted && used.PendingDecision is
        {
            Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0,
            SkillPrompt.SkillId: "classic:mengjin"
        },
            activated.Error?.Message ?? "Activating Mengjin must publish its card-payment choice.");
        var payment = used.PendingDecision!;
        var before = used.CreateSnapshot(boundary.SourceSeat, revealAll: true)
            .Players[boundary.TargetSeat];
        var handChoices = payment.Choices.Where(choice =>
            choice.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)).ToArray();
        var equipmentChoices = payment.Choices.Where(choice =>
            choice.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Equipment)).ToArray();
        Require(payment.IsPrivate && payment.TargetSeat == boundary.TargetSeat &&
                handChoices.Length == before.Hand.Count &&
                handChoices.All(choice => choice.Cards.Count == 0) &&
                equipmentChoices.SelectMany(choice => choice.Cards)
                    .SequenceEqual(before.Equipment.Select(card => card.Id)) &&
                before.Judgment.All(card => !payment.ValidCardIds.Contains(card.Id)) &&
                used.CreateSnapshot(boundary.TargetSeat).PendingDecision is null,
            "Mengjin payment must reveal only opaque hand slots and public equipment.");
        var paymentCheckpoint = RoundTrip(used.CreateCheckpoint());
        Require(State(GameReplay.Restore(paymentCheckpoint, registry)) == State(used),
            "An in-flight Mengjin payment must restore exactly.");
        var use = payment.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card");
        var expectedCardId = use.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)
            ? before.Hand[int.Parse(use.Parameters["slot-index"], System.Globalization.CultureInfo.InvariantCulture)].Id
            : use.Cards.Single();
        var usedResult = used.Submit(new AnswerPromptCommand(
            boundary.SourceSeat, payment.PromptId, use.Id, used.Revision));
        // A new-table target can open an out-of-turn loss trigger (e.g. Tuntian's
        // cardsMoved judgment window) on the Mengjin discard; the binding completes
        // only after that nested window resolves, so drive the accepted payment
        // forward instead of assuming synchronous completion.
        for (var step = 0; step < 64; step++)
        {
            if (used.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Any(item => item.SkillId == "classic:mengjin" && item.Activated && item.Completed) ||
                used.PendingDecision is { } pendingDecision &&
                pendingDecision.PlayerSeat == boundary.SourceSeat)
            {
                break;
            }

            Require(used.Submit(new AdvanceOneStepCommand(used.Revision)).Accepted,
                "Mengjin fixture could not advance past the target's out-of-turn trigger.");
        }

        var movement = used.CardMovements.LastOrDefault(move =>
            move.CardId == expectedCardId && move.Reason.Value == "skill-program.classic:mengjin.SelectAndMoveOwnedCard");
        var after = used.CreateSnapshot(boundary.SourceSeat, revealAll: true)
            .Players[boundary.TargetSeat];
        Require(usedResult.Accepted &&
                used.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Any(item => item.SkillId == "classic:mengjin" && item.Activated && item.Completed) &&
                movement?.To == CardLocation.DiscardPile &&
                after.Hp == before.Hp &&
                after.Hand.Count + after.Equipment.Count == before.Hand.Count + before.Equipment.Count - 1,
            usedResult.Error?.Message ?? "Using Mengjin must discard the selected target card after Dodge canceled Slash.");

        var replayed = GameReplay.Restore(RoundTrip(used.CreateCheckpoint()), registry);
        Require(State(replayed) == State(used) && Events(replayed).SequenceEqual(Events(used)),
            "A completed Mengjin use must replay exactly.");

    }

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static string State(GameEngine game) =>
        SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));

    private static IReadOnlyList<string> Events(GameEngine game) => game.Events
        .Select(item => $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
