namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Called only by the mature implicit-target normalization/extra-target
    // producers, after their real owning use has been updated. Keep the exact
    // original window/candidate/context/return; synchronize its action value.
    private void SyncIssuedTieredRoundZeroTrickTargetWindows(long useId, CardActionContext previousAction)
    {
        if (LifecycleCardUse(useId) is not { TieredRoundConversionUse: { FrozenTier: 2, MaterialCount: 0 },
                Action: { Type: CardActionType.Use, PhysicalCards.Count: 0 } currentAction } use ||
            !IsOrdinaryTrick(use.CardKind) || !IsIssuedTieredRoundUse(use, true)) return;
        if (previousAction.Type != CardActionType.Use || previousAction.ActionId != currentAction.ActionId ||
            previousAction.EffectiveKind != currentAction.EffectiveKind)
            throw new InvalidOperationException("A zero-material trick target update lost its original use action.");
        foreach (var window in _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Where(w =>
            w.ParentFrameId == use.Id && w.Action.Type == CardActionType.Use &&
            w.Action.ActionId == currentAction.ActionId && w.TrickContinuation is { EffectCardId: 0 } &&
            w.Continuation is ProgramCardContinuation.CommittedTrick or ProgramCardContinuation.FinalizedTrick or
                ProgramCardContinuation.BeforeTrickTargetEffects).ToArray())
        {
            // Compare against the actual pre-update producer input, not a
            // reconstructed target subset or an arbitrary same-id action.
            if (!TieredRoundActionsStructurallyMatch(window.Action, previousAction))
                throw new InvalidOperationException("A zero-material trick window lost its exact pre-update action.");
            ReplaceRuntimeFrame(window.Id, window with { Action = currentAction });
        }
    }
}
