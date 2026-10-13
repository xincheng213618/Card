using System.Text.Json;
using CardGame.Core;
using static DelayedTrickScenario;

internal static class DelayedNullificationChecks
{
    public static void PlacementAndJudgmentWindows()
    {
        foreach (var kind in new[] { CardKind.Indulgence, CardKind.SupplyShortage, CardKind.Lightning })
            foreach (var depth in new[] { 0, 1, 2 }) Verify(kind, depth);
        Verify(CardKind.SupplyShortage, 1, unrespondable: true);
        Verify(CardKind.SupplyShortage, 0, converted: true);
        Verify(CardKind.SupplyShortage, 1, converted: true);
    }

    private static void Verify(CardKind kind, int depth, bool unrespondable = false, bool converted = false)
    {
        var (game, registry) = Create(unrespondable);
        var target = kind == CardKind.Lightning ? 0 : 1;
        var action = game.GetHumanLegalActions().First(a => a.CardId is not null &&
            a.TargetSeat == target && (converted
                ? a.ConversionSource?.SkillId == "boundary:duanliang-current" && a.PlayedCardKind == kind
                : a.ConversionSource is null && game.CreateSnapshot(0).Players[0].Hand.Single(c => c.Id == a.CardId).Kind == kind));
        var cardId = action.CardId!.Value;
        Accept(game, new PlayCardCommand(0, cardId, action.TargetSeats, game.Revision, game.PendingDecision!.PromptId, action.PlayedCardKind)
            { ConversionSource = action.ConversionSource });
        Require(game.ResolutionStack.Count == 0 &&
            game.CreateSnapshot(0).Players[target].Judgment.Any(c => c.Id == cardId) &&
            !game.Events.Any(e => e.Payload is NullificationRequestedEvent or NullificationResolvedEvent),
            $"{kind} placement must complete without a counterspell window.");
        Require(game.Events.Count(e => e.Payload is CardActionAcceptedEvent { Action.Type: CardActionType.Use } use &&
            use.Action.PhysicalCards.Any(c => c.CardId == cardId)) == 1, "Placement accepted more than one real use.");
        Cold(game, registry);
        if (game.PendingDecision is null) Accept(game, new AdvanceOneStepCommand(game.Revision));
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
        for (var i = 0; i < 100 && game.ResolutionStack.OfType<NullificationWindowFrame>().LastOrDefault()?.DelayedReturn?.CardId != cardId; i++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard } play)
                Accept(game, new EndPlayPhaseCommand(play.PlayerSeat, game.Revision, play.PromptId));
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        var window = game.ResolutionStack.OfType<NullificationWindowFrame>().Single(w => w.DelayedReturn?.CardId == cardId);
        Require(game.State.CurrentSeat == target && window.SourceSeat == target && window.TargetSeats.SequenceEqual([target]) &&
            game.CardMovements.All(m => m.CardId != cardId || m.Reason != CardMoveReasons.DelayedCardFinish),
            "A delayed counterspell must own the holder's actual turn and held judgment card.");
        var judgmentsBefore = game.Events.Count(e => e.Payload is JudgmentRequestedEvent);
        var remaining = depth;
        for (var i = 0; i < 30 && game.ResolutionStack.OfType<NullificationWindowFrame>().Any(w => w.Id == window.Id); i++)
        {
            Cold(game, registry);
            var currentWindow = game.ResolutionStack.OfType<NullificationWindowFrame>().Single(w => w.Id == window.Id);
            var prompt = currentWindow.CandidateIndex < currentWindow.CandidateSeats.Count
                ? game.CreateSnapshot(currentWindow.CandidateSeats[currentWindow.CandidateIndex]).PendingDecision : null;
            if (prompt is not { Kind: DecisionKind.Nullification })
            {
                Accept(game, new AdvanceOneStepCommand(game.Revision));
                continue;
            }
            var response = prompt.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("response") == "nullification");
            var shouldUse = remaining > 0 && response is not null && (!unrespondable || prompt.PlayerSeat == 0);
            var choice = shouldUse ? response! : prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("response") == "pass");
            Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision));
            if (shouldUse) remaining--;
        }
        Require(remaining == 0 && !game.ResolutionStack.OfType<NullificationWindowFrame>().Any(w => w.Id == window.Id),
            "Delayed counterspell chain did not settle at its exact owner.");
        Require(game.Events.Select(e => e.Payload).OfType<NullificationResolvedEvent>().Single(e => e.EffectCardId == cardId).ChainDepth == depth,
            "Delayed counterspell chain lost its parity.");
        if (depth % 2 == 1)
        {
            Require(game.Events.Count(e => e.Payload is JudgmentRequestedEvent) == judgmentsBefore,
                "A canceled delayed effect must not draw a judgment card.");
            Require(kind == CardKind.Lightning
                ? game.CardMovements.Any(m => m.CardId == cardId && m.Reason == CardMoveReasons.DelayedCardTransfer)
                : game.CardMovements.Any(m => m.CardId == cardId && m.From == CardLocation.Judgment(target) &&
                    m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.DelayedCardFinish),
                "Canceled delayed card did not follow its actual discard/transfer path.");
        }
        else Require(game.Events.Count(e => e.Payload is JudgmentRequestedEvent) == judgmentsBefore + 1,
            "An active delayed effect must begin exactly one judgment after responses.");
        Require(game.Events.Count(e => e.Payload is CardActionAcceptedEvent { Action.Type: CardActionType.Use } use &&
            use.Action.PhysicalCards.Any(c => c.CardId == cardId)) == 1, "Triggering a held card accepted a second use.");
        Cold(game, registry);
    }

    private static void Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, 4).All(seat => JsonSerializer.Serialize(restored.CreateSnapshot(seat)) ==
            JsonSerializer.Serialize(game.CreateSnapshot(seat))), "Delayed counterspell cold replay changed a player view.");
    }
}
