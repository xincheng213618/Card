namespace CardGame.Core;

public sealed partial class GameEngine
{
    // The order here is the existing event-reaction order. These are rules steps,
    // so they run before the event is queued for committed observers.
    private void AdvanceEventRulesAndQueueFact(IGameEvent payload)
    {
        SynchronizePrivateGeneralLibraries();
        SynchronizeLordSkillProjections();
        ObserveProgramHealthChange(payload);
        ObserveAdvancedLifecycleEvent(payload);
        ObserveBeneficiarySuitShield(payload);
        ObserveNextCardTargetAdjustmentEvent(payload);
        ObserveNextActualUseAdjustment(payload);
        ObservePlayPhaseColorRestriction(payload);
        ObserveFirstTurnCategoryUse(payload);
        ObserveCurrentTurnUseKinds(payload);
        ObserveTurnDrawDebtUse(payload);
        ObserveActualTurnTrickUse(payload);
        ObserveRoundTrickUse(payload);
        ObserveActualForeignUseTargets(payload);
        ObserveActualTurnDamageEntities(payload);
        ObserveTrueRoundCardNames(payload);
        ObserveRoundDistinctBasicUse(payload);
        ObserveRoundGainedRoundBoundary(payload);
        ObserveTurnDefaultStats(payload);
        ObservePhaseHandSeizureDeath(payload);
        ObserveUnnullifiableOrdinaryTrick(payload);
        ObserveJudgedRankSlashUse(payload);
        ObserveKuangfuAppliedDamage(payload);
        ObserveSelectedForeignCardSlashAppliedDamage(payload);
        ObserveLastDamageSourceReciprocity(payload);
        QueueGameEvent(payload);
        if (payload is TurnEndedEvent ended)
        {
            ExpireActorHandLimitPenalties(ended);
            ExpirePlayPhaseSkillGrantsForEndedTurn(ended.TurnNumber);
            ExpireActualTurnSkillGrants(ended.TurnNumber, ended.ActorSeat);
            RestoreOrderedPrintedSkillsAtTurnEnd(ended.TurnNumber, ended.ActorSeat);
        }
        if (payload is GameEndedEvent)
        {
            ExpireAllPlayPhaseSkillGrants("game-ended");
            ExpireAllActorHandLimitPenalties("game-ended");
        }
        if (_started)
        {
            ResolveAutomaticDyingTransitionPrograms(payload);
        }
    }

    // A publication request first settles the same windows it previously
    // opened implicitly, then captures the resulting state for notification.
    private void AdvanceRulesAndPublishState()
    {
        SynchronizePrivateGeneralLibraries();
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
