namespace CardGame.Core;

public sealed partial class GameEngine
{
    // The order here is the existing event-reaction order. These are rules steps,
    // so they run before the event is queued for committed observers.
    private void AdvanceEventRulesAndQueueFact(IGameEvent payload)
    {
        if (payload is CardActionAcceptedEvent accepted && accepted.Action.ConversionChain.Count != 0)
        {
            PayMeiYingConversion(accepted.Action);
            PayZhenyiHoutu(accepted.Action);
        }
        SynchronizePrivateGeneralLibraries();
        SynchronizeLordSkillProjections();
        SynchronizeHpConditionalSkillGrants();
        SynchronizeEquipmentSuitSkillGrants();
        CleanupLostNamedAmbushPiles();
        CleanupLostPrivateWhirlPiles();
        CaptureQirangDeclaredUse(payload);
        ObserveZhugeGuoTurnAndDeath(payload);
        if (payload is DamageAppliedEvent wangYunDamage) ObserveWangYunLianjiDamage(wangYunDamage);
        if (payload is DamageAppliedEvent requestedDamage) ObserveRoundOnceCardlessRequestedOwnerDamage(requestedDamage);
        ObserveXianfuDamageQualification(payload);
        ObserveDianhuDamageQualification(payload);
        CaptureChouceFrozenJudgment(payload);
        ObserveProgramHealthChange(payload);
        ObserveAdvancedLifecycleEvent(payload);
        ObserveMouZhugeEvent(payload);
        ObserveIssuedCardRecoveryModifiers(payload);
        ObserveBeneficiarySuitShield(payload);
        ObserveNextCardTargetAdjustmentEvent(payload);
        ObserveNextActualUseAdjustment(payload);
        ObservePlayPhaseColorRestriction(payload);
        ObserveFirstTurnCategoryUse(payload);
        ObserveCurrentTurnUseKinds(payload);
        ObserveFirstSlashDuelDeclaration(payload);
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
        ObserveChosenAssaultDamage(payload);
        ObserveCaoYingDamage(payload);
        ObserveCaoYingExpiry(payload);
        ObserveQimeiExpiry(payload);
        ObserveKuanshiExpiry(payload);
        ObserveLiuQiEvent(payload);
        ObserveXuRongDyingQualification(payload);
        ObserveXuRongTurnExpiry(payload);
        ObserveLiJueNativeEvent(payload);
        ObserveSuFeiTurnOrDeath(payload);
        ObserveGuoSiAcceptedUse(payload);
        ObserveGuoSiTurnExpiry(payload);
        ObserveLvQianTurnBoundary(payload);
        ObserveNeifaTurnBoundary(payload);
        ObserveXinPiEvent(payload);
        if (payload is CardActionAcceptedEvent huaManAccepted) ObserveHuaManAcceptedUse(huaManAccepted.Action);
        ObserveJianjiAlcoholExpiry(payload);
        ObserveQingzhongDebtTermination(payload);
        if (payload is PlayerDiedEvent dongBaiDeath) CleanupDongBaiRecipientDeath(dongBaiDeath.VictimSeat);
        ObserveZhuijiDebtTermination(payload);
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
        SynchronizeHpConditionalSkillGrants();
        SynchronizeEquipmentSuitSkillGrants();
        CleanupIssuedPlayPhaseUseBans();
        CleanupLostDeferredPileSources();
        CleanupLostPublicPersistentPiles();
        CleanupLostNamedAmbushPiles();
        CleanupLostPrivateWhirlPiles();
        if (!TryBeginCharacterStateProgramWindow() &&
            !TryBeginHpChangedProgramWindow())
        {
            TryBeginCardsMovedProgramWindow();
        }

        PublishState();
    }
}
