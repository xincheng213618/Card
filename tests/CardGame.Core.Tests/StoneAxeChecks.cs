using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class StoneAxeChecks
{
    public static void ExactCostDamageAndReplay()
    {
        var registry = StoneAxeScenario.CreateRegistry();
        var boundary = StoneAxeScenario.FindHumanTrigger(seed: 1);
        var game = boundary.Game;
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Stone Axe fixture lost its private trigger prompt.");
        var source = game.CreateSnapshot(0, revealAll: true).Players[0];
        var target = game.CreateSnapshot(0, revealAll: true).Players
            .Single(player => player.Seat == boundary.TargetSeat);
        var expectedCandidates = source.Hand.Concat(source.Equipment).Select(card => card.Id).ToArray();
        var useChoices = prompt.Choices.Where(choice =>
            choice.Parameters.GetValueOrDefault("action") == "stone-axe-use").ToArray();

        Require(prompt.IsPrivate &&
                game.CreateSnapshot(boundary.TargetSeat).PendingDecision is null &&
                prompt.ValidCardIds.SequenceEqual(expectedCandidates) &&
                useChoices.Length == expectedCandidates.Length * (expectedCandidates.Length - 1) / 2 &&
                useChoices.All(choice =>
                    choice.Cards.Count == 2 &&
                    choice.Cards.Distinct().Count() == 2 &&
                    choice.Cards.All(expectedCandidates.Contains)),
            "Stone Axe must publish every exact two-card hand/equipment cost only to its source.");
        Require(useChoices.Any(choice => choice.Cards.Contains(boundary.StoneAxeCardId)),
            "Stone Axe itself must remain a legal member of the two-card discard cost.");

        var pausedCheckpoint = RoundTrip(game.CreateCheckpoint());
        var paused = GameReplay.Restore(pausedCheckpoint, registry);
        Require(State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "An in-flight Stone Axe cost prompt must restore exactly.");

        var skipped = GameReplay.Restore(pausedCheckpoint, registry);
        var skippedTargetHp = skipped.CreateSnapshot(0, revealAll: true).Players
            .Single(player => player.Seat == boundary.TargetSeat).Hp;
        var skipPrompt = skipped.PendingDecision!;
        var skipChoice = skipPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "stone-axe-skip");
        var skipResult = skipped.Submit(new AnswerPromptCommand(
            0,
            skipPrompt.PromptId,
            skipChoice.Id,
            skipped.Revision));
        Require(skipResult.Accepted &&
                skipped.CreateSnapshot(0, revealAll: true).Players
                    .Single(player => player.Seat == boundary.TargetSeat).Hp == skippedTargetHp &&
                skipped.Events.Select(item => item.Payload).OfType<StoneAxeResolvedEvent>()
                    .Any(resolved => !resolved.Used && resolved.DiscardedCardIds.Count == 0),
            skipResult.Error?.Message ??
            "Skipping Stone Axe must preserve the successful Dodge without damage.");

        var used = GameReplay.Restore(pausedCheckpoint, registry);
        var usedPrompt = used.PendingDecision!;
        var costChoice = usedPrompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("action") == "stone-axe-use" &&
            choice.Cards.Contains(boundary.StoneAxeCardId));
        var targetHpBefore = used.CreateSnapshot(0, revealAll: true).Players
            .Single(player => player.Seat == boundary.TargetSeat).Hp;
        var usedResult = used.Submit(new AnswerPromptCommand(
            0,
            usedPrompt.PromptId,
            costChoice.Id,
            used.Revision));
        Require(usedResult.Accepted, usedResult.Error?.Message ??
            "The exact Stone Axe two-card cost was rejected.");
        var paidSlash = used.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.CardId == boundary.SlashAction.CardId);
        Require(used.ResolutionStack.OfType<BeforeDamageProgramWindowFrame>().Single().ParentFrameId == paidSlash.Id &&
                used.PendingDecision is { IsPrivate: true, PlayerSeat: 0, SkillPrompt.SkillId: "classic:zhiman" } &&
                Enumerable.Range(1, 4).All(seat => used.CreateSnapshot(seat).PendingDecision is null),
            "The verified witness must retain the paid Slash's exact private pre-damage child.");
        var paidCheckpoint = RoundTrip(used.CreateCheckpoint());
        var paid = GameReplay.Restore(paidCheckpoint, registry);
        Require(AllViews(paid) == AllViews(used) &&
                JsonSerializer.Serialize(paid.ResolutionStack) == JsonSerializer.Serialize(used.ResolutionStack) &&
                Events(paid).SequenceEqual(Events(used)) && paid.CardMovements.SequenceEqual(used.CardMovements),
            "The paid Stone Axe must replay all five private views and the exact pending damage child without paying twice.");
        ContinueBeforeDamageWithoutPrevention(used, paidSlash.Id);
        ContinueBeforeDamageWithoutPrevention(paid, paidSlash.Id);
        Require(AllViews(paid) == AllViews(used) && Events(paid).SequenceEqual(Events(used)) &&
                paid.CardMovements.SequenceEqual(used.CardMovements),
            "Both copies must decline the real optional pre-damage child and resume the same paid Slash.");
        var after = used.CreateSnapshot(0, revealAll: true);
        var resolved = used.Events.Select(item => item.Payload)
            .OfType<StoneAxeResolvedEvent>()
            .LastOrDefault();
        Require(after.Players.Single(player => player.Seat == boundary.TargetSeat).Hp == targetHpBefore - 1 &&
                used.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().Count(damage =>
                    damage.SourceSeat == 0 && damage.TargetSeat == boundary.TargetSeat && damage.Amount == 1) == 1 &&
                after.Players[0].Equipment.All(card => card.Id != boundary.StoneAxeCardId) &&
                costChoice.Cards.All(cardId => used.CardMovements.Count(movement =>
                    movement.CardId == cardId &&
                    movement.To == CardLocation.DiscardPile &&
                    movement.Reason == CardMoveReasons.StoneAxeDiscard) == 1) &&
                resolved is { Used: true } &&
                resolved.DiscardedCardIds.SequenceEqual(costChoice.Cards),
            "Stone Axe must discard the exact published pair and resume the same Slash as damage.");

        var damageApplied = GameReplay.Restore(RoundTrip(used.CreateCheckpoint()), registry);
        Require(State(damageApplied) == State(used) && Events(damageApplied).SequenceEqual(Events(used)),
            "The applied Stone Axe damage and any subsequent damage-trigger child must replay exactly.");

    }

    private static void ContinueBeforeDamageWithoutPrevention(GameEngine game, long slashFrameId)
    {
        for (var step = 0; step < 16 && game.ResolutionStack.OfType<BeforeDamageProgramWindowFrame>().Any(frame => frame.ParentFrameId == slashFrameId); step++)
        {
            var prompt = Enumerable.Range(0, 5).Select(seat => game.CreateSnapshot(seat).PendingDecision)
                .FirstOrDefault(decision => decision is not null);
            if (prompt is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger })
            {
                var skip = prompt.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip");
                Require(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, skip.Id, game.Revision)).Accepted,
                    "A published optional damage prevention child must allow preserving the Stone Axe damage.");
            }
            else
                Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                    "The paid Stone Axe pre-damage child must advance legally.");
        }
        Require(!game.ResolutionStack.OfType<BeforeDamageProgramWindowFrame>().Any(frame => frame.ParentFrameId == slashFrameId),
            "The bounded Stone Axe continuation must finish its pre-damage child before asserting damage.");
    }

    private static string AllViews(GameEngine game) => JsonSerializer.Serialize(
        Enumerable.Range(0, 5).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray());

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
