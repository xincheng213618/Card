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

    public static void LethalGanglie()
    {
        GameEngine? game = null;
        for (var seed = 1; seed <= 4096 && game is null; seed++)
        {
            var candidate = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                HumanSeat = -1,
                HumanRole = null,
                UseInteractiveSetup = true,
                AiPolicyVersion = 2
            }, StandardContentRegistry.Create());
            Require(candidate.Submit(new StartGameCommand()).Accepted, "Ganglie match failed.");
            if (candidate.State.Status == EngineStatus.Completed &&
                candidate.State.Winner == Winner.Rebels &&
                candidate.Events.Select(item => item.Payload)
                    .OfType<GangliePunishmentResolvedEvent>()
                    .Any(item => !item.SourceAlive))
            {
                game = candidate;
            }
        }

        var match = game ??
            throw new InvalidOperationException("Could not find a deterministic lethal Ganglie fixture under the current rules.");
        Require(
            match.State.Status == EngineStatus.Completed && match.State.Winner == Winner.Rebels,
            $"Lethal Ganglie should finish with a Rebel victory (status={match.State.Status}, winner={match.State.Winner}, turns={match.State.TurnNumber}, events={match.Events.Count}, stack={match.ResolutionStack.Count}, pending={match.PendingDecision?.Kind}).");
        var events = match.Events.Select(item => item.Payload).ToArray();
        var punishment = events.OfType<GangliePunishmentResolvedEvent>()
            .SingleOrDefault(item => !item.SourceAlive);
        Require(punishment is not null, "Fixture did not reach lethal Ganglie.");
        var lethalPunishment = punishment ??
            throw new InvalidOperationException("Fixture did not reach lethal Ganglie.");
        var punishmentDamage = events.OfType<DamageRequestedEvent>()
            .SingleOrDefault(item =>
                item.ResolutionId == lethalPunishment.ResolutionId &&
                item.SourceSeat == lethalPunishment.OwnerSeat &&
                item.TargetSeat == lethalPunishment.SourceSeat &&
                item.Amount == 1 &&
                item.SourceCard is null);
        Require(punishmentDamage is not null, "Lethal Ganglie must publish a nested typed damage request.");
        Require(events.OfType<DamageAppliedEvent>().Any(item =>
            item.SourceSeat == lethalPunishment.OwnerSeat &&
            item.TargetSeat == lethalPunishment.SourceSeat &&
            item.Amount == 1 &&
            item.RemainingHp == 0), "Lethal Ganglie must publish its applied damage.");
        var lethalPunishmentDamage = punishmentDamage ??
            throw new InvalidOperationException("Lethal Ganglie must publish a nested typed damage request.");
        Require(events.OfType<AfterDamageEvent>().Any(item =>
            item.ResolutionId == lethalPunishmentDamage.ResolutionId &&
            item.SourceSeat == lethalPunishment.OwnerSeat &&
            item.TargetSeat == lethalPunishment.SourceSeat &&
            item.Amount == 1 &&
            item.RemainingHp == 0), "Lethal Ganglie must close its nested damage frame.");
        Require(match.ResolutionStack.Count == 0 && match.State.ProcessingCardCount == 0 && match.PendingDecision is null, "Lethal counterattack left a pending continuation.");
        Require(match.Events.Count(item => item.Payload is GameEndedEvent) == 1, "Game end must be published exactly once.");
        Require(match.Events.Last().Payload is GameEndedEvent, "Nothing should resolve after terminal game over.");
        var replay = GameReplay.Restore(match.CreateCheckpoint(), StandardContentRegistry.Create());
        Require(SnapshotJson.Serialize(match.State) == SnapshotJson.Serialize(replay.State), "Lethal counterattack replay changed the ending.");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
