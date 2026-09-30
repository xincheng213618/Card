using CardGame.Content.Standard;
using CardGame.Core;

internal static class TargetLossChecks
{
    public static void LastNullification()
    {
        foreach (var kind in new[] { CardKind.Dismantlement, CardKind.Snatch, CardKind.FireAttack })
        {
            var standard = StandardContentRegistry.Create();
            string Id(CardKind cardKind) => standard.Cards.Values.Single(card => card.LegacyKind == cardKind).Id;
            var registry = ContentRegistry.Build(new StandardContentPackage(), new SyntheticPackage("target-loss", builder =>
                builder.AddDeck(new ContentDeckRecipe("target-loss:deck", "响应后空手牌场景", 1, 2,
                    [new(Id(kind), 2), new(Id(CardKind.Nullification), 2), new(Id(CardKind.Dodge), 20)]))));
            GameEngine? selected = null;
            LegalAction? chosen = null;
            for (var seed = 1; seed <= 4096 && selected is null; seed++)
            {
                var game = GameEngine.CreateStandard(new GameOptions
                {
                    Seed = seed,
                    PlayerCount = 5,
                    HumanSeat = 0,
                    HumanRole = Role.Lord,
                    AiPolicyVersion = 2,
                    DeckId = "target-loss:deck",
                    AdvanceAfterHumanCommands = false
                }, registry);
                Require(game.Submit(new StartGameCommand()).Accepted, "Fixture failed to start.");
                var full = game.CreateSnapshot(0, true);
                if (!full.Players.Single(player => player.Seat == 0).Hand.Any(card => card.Kind == CardKind.Nullification)) continue;
                var action = game.GetHumanLegalActions().FirstOrDefault(action => action.CardId is { } cardId &&
                    full.Players[0].Hand.Any(card => card.Id == cardId && card.Kind == kind) && action.TargetCardId is null &&
                    action.TargetSeat is { } target && full.Players.Single(player => player.Seat == target).Hand is { Count: 1 } hand && hand[0].Kind == CardKind.Nullification);
                if (action is not null) { selected = game; chosen = action; }
            }
            Require(selected is not null && chosen is not null, $"Could not deal a legal {kind} + counter-Nullification fixture.");
            var match = selected!;
            var play = chosen!;
            Require(match.Submit(new PlayCardCommand(0, play.CardId!.Value, play.TargetSeats, match.Revision, match.PendingDecision!.PromptId)).Accepted, "Targeted play failed.");
            for (var step = 0; step < 40 && match.ResolutionStack.Count > 0; step++)
            {
                GameCommand command;
                if (match.PendingDecision is { } prompt)
                {
                    Require(prompt.Kind == DecisionKind.Nullification, "Only a Nullification prompt should be needed.");
                    var window = match.ResolutionStack.OfType<NullificationWindowFrame>().Single();
                    var choice = prompt.Choices.Single(choice => choice.Parameters.GetValueOrDefault("response") == (window.EffectNullified ? "nullification" : "pass"));
                    command = new AnswerPromptCommand(0, prompt.PromptId, choice.Id, match.Revision);
                }
                else command = new AdvanceOneStepCommand(match.Revision);
                Require(match.Submit(command).Accepted, "Nullification continuation failed.");
            }
            var skipped = match.Events.Select(item => item.Payload).OfType<CardEffectSkippedEvent>().Single();
            Require(skipped.CardKind == kind && skipped.TargetSeat == play.TargetSeat && skipped.Reason == CardEffectSkipReason.TargetHandEmpty, "Expected explicit empty-hand completion.");
            var responses = match.Events.Select(item => item.Payload).OfType<NullificationRespondedEvent>().ToArray();
            Require(responses.Length == 2 && responses[0].ResponderSeat == play.TargetSeat && responses[1].ResponderSeat == 0, "Target's last card must be countered by the human source.");
            Require(match.ResolutionStack.Count == 0 && match.State.ProcessingCardCount == 0 && match.PendingDecision is null, "Effect left a stranded frame or prompt.");
            Require(match.State.Players.Single(player => player.Seat == play.TargetSeat).HandCount == 0, "Effect must not silently switch to another zone.");
            Require(match.Events.All(item => item.Payload is not TargetCardDiscardedEvent && item.Payload is not TargetCardTakenEvent && item.Payload is not FireAttackCardRevealedEvent), "Skipped effect claimed a nonexistent card.");
            Require(match.CreateCardZoneDiagnostics().Count == 24, "Fixture lost a physical card.");
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(match.CreateCheckpoint())), registry);
            Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, true)) == SnapshotJson.Serialize(match.CreateSnapshot(0, true)), "Countered last-card checkpoint did not replay.");
            Console.WriteLine($"  {kind}: target used its final card, human countered, effect skipped and replayed (seed {match.CreateCheckpoint().Options.Seed}).");
        }
    }


    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
