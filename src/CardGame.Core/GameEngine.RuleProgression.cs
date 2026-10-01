namespace CardGame.Core;

public sealed partial class GameEngine
{
    // The order here is the existing event-reaction order. These are rules steps,
    // so they run before the event is queued for committed observers.
    private void AdvanceEventRulesAndQueueFact(IGameEvent payload)
    {
        ObserveProgramHealthChange(payload);
        ObserveAdvancedLifecycleEvent(payload);
        ObserveBeneficiarySuitShield(payload);
        ObserveNextCardTargetAdjustmentEvent(payload);
        ObservePlayPhaseColorRestriction(payload);
        QueueGameEvent(payload);
        if (_started)
        {
            ResolveAutomaticDyingTransitionPrograms(payload);
        }
    }

    // A publication request first settles the same windows it previously
    // opened implicitly, then captures the resulting state for notification.
    private void AdvanceRulesAndPublishState()
    {
        CleanupIssuedPlayPhaseUseBans();
        CleanupLostDeferredPileSources();
        CleanupLostPublicPersistentPiles();
        if (!TryBeginCharacterStateProgramWindow() &&
            !TryBeginHpChangedProgramWindow())
        {
            TryBeginCardsMovedProgramWindow();
        }

        PublishState();
    }
}
