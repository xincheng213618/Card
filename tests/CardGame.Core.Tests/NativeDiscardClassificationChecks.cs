using CardGame.Core;

internal static class NativeDiscardClassificationChecks
{
    public static void ExactPaidProducersExcludeNeighborReasons()
    {
        foreach (var reason in new[]
        {
            "skill-program.fixture:refund.draw-discard-category.discard",
            "skill-program.fixture:reciprocity.last-damage-source.discard",
            "program.phase-name-prediction.cost",
            "skill-program.fixture:outside.outside-phase-discard"
            , "skill-program.fixture:demand.same-name-hand.discard"
            , "skill-program.fixture:reveal.completed-undamaged-target.discard"
        })
            Require(GameEngine.IsDiscardMovementReason(new(reason)), $"Actual discard producer must qualify: {reason}");
        foreach (var reason in new[]
        {
            "skill-program.fixture:refund.draw-discard-category.draw",
            "skill-program.fixture:refund.draw-discard-category.bonus",
            "skill-program.fixture:reciprocity.last-damage-source.draw",
            "program.phase-name-prediction.reveal",
            "program.phase-name-prediction.cost-finished",
            "skill-program.fixture:outside.outside-phase-draw",
            "skill-program.fixture:demand.same-name-hand.damage",
            "skill-program.fixture:reveal.completed-undamaged-target.reveal",
            "skill-program.fixture:unknown.cost",
            "card.recast.discard",
            "card.use.finished",
            "card.response.finished",
            "equipment.replacement",
            "equipment.wooden-ox.grain-discard",
            "player.death-discard",
            "card.harvest-discard"
        })
            Require(!GameEngine.IsDiscardMovementReason(new(reason)), $"Adjacent gain, recast and cleanup must stay excluded: {reason}");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
