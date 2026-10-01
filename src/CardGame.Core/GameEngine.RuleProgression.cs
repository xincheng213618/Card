namespace CardGame.Core;

public sealed partial class GameEngine
{
    // The order here is the existing event-reaction order. These are rules steps,
    // so they run before the event is queued for committed observers.
    private void AdvanceEventRulesAndQueueFact(IGameEvent payload)
    {
        SynchronizeLordSkillProjections();
        ObserveProgramHealthChange(payload);
        ObserveAdvancedLifecycleEvent(payload);
        ObserveBeneficiarySuitShield(payload);
        ObserveNextCardTargetAdjustmentEvent(payload);
        ObservePlayPhaseColorRestriction(payload);
        ObserveFirstTurnCategoryUse(payload);
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
        SynchronizeLordSkillProjections();
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
