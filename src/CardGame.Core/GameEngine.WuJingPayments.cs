namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasWuJingHejiTrickPaymentProcessing(IReadOnlyList<Card> processing)
    {
        if (_resolutionStack.Count < 3 ||
            _resolutionStack[^1] is not CardsMovedTriggerWindowFrame movement ||
            _resolutionStack[^2] is not CardUseFrame { WuJingHeji: { },
                RecoveryPaidContinuation: { } continuation, PaymentMovementReceipt: { } payment,
                Action: { } action } use ||
            !IsWuJingHejiUse(use) || !HasWuJingHejiTrickPaymentTail(use, continuation) ||
            !IsPaidCardUseMovementReturn(use, movement)) return false;
        var allowed = payment.Batch.Movements.Where(move => move.To == CardLocation.Processing)
            .Select(move => move.CardId).ToHashSet();
        var visited = new HashSet<long>();
        for (var parentId = action.ParentActionId; parentId is { } id;)
        {
            if (!visited.Add(id) || _resolutionStack.OfType<CardUseFrame>()
                    .SingleOrDefault(parent => parent.Action?.ActionId == id) is not { Action: { } parentAction } parent ||
                parent.Id >= use.Id) return false;
            allowed.UnionWith(parent.PhysicalCardIds ?? [parent.CardId]);
            parentId = parentAction.ParentActionId;
        }
        return processing.All(card => allowed.Contains(card.Id));
    }
}
