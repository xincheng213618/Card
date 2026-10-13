namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Issued receipts can survive different invalidations. Keep each permission
    // explicit and leave the receipt, stage and ownership checks in its mechanism.
    private bool CanContinueProgramAfterOwnerDeath(ProgramSkillFrame frame) =>
        CanContinueDamageZoneAcquisition(frame) ||
        CanContinueFirstSlashDuelBenefit(frame) ||
        CanContinueExtraEffectsAndRankGuess(frame) ||
        CanContinueRicePile(frame) ||
        CanContinueNamedAmbush(frame) || CanContinueDamageSourceHandChoice(frame) ||
        CanContinuePhaseStartCardControl(frame) ||
        CanContinueEndRefillNextTurn(frame) ||
        CanContinueIncomingDistanceCourtesy(frame) ||
        CanContinueUnequalHandHp(frame) ||
        CanContinueSelectedForeignCardSlash(frame) ||
        CanContinueIssuedRoundGainedUse(frame) ||
        CanContinueIssuedJoinedTrickDamageReward(frame) ||
        CanContinuePairedColorDisposition(frame) ||
        CanContinueSameNameHandDiscardOrDamage(frame) ||
        CanContinueCompletedUndamagedTargetReveal(frame) ||
        CanContinueRecipientCategoryMark(frame) ||
        CanContinueOffTurnUsedCardGift(frame) ||
        CanContinueOrderedPrintedSkillLoss(frame) ||
        CanContinueOverflowTargetCancellation(frame) ||
        CanContinueOriginalHandEntityProgram(frame) ||
        CanContinueOutsidePhaseDrawDiscard(frame) ||
        CanContinueMatchingRecast(frame) ||
        CanContinuePairedHandRecast(frame) ||
        CanContinuePhaseNamePrediction(frame) ||
        CanContinueLastDamageSourceReciprocity(frame) ||
        CanContinuePaidHpDamageShield(frame) ||
        CanContinuePaidDamageTargetObtain(frame);

    private bool CanContinueProgramAfterSkillLoss(ProgramSkillFrame frame) =>
        CanContinueDamageZoneAcquisition(frame) ||
        CanContinueFirstSlashDuelBenefit(frame) ||
        CanContinueExtraEffectsAndRankGuess(frame) ||
        CanContinueRicePile(frame) ||
        CanContinueNamedAmbush(frame) || CanContinueDamageSourceHandChoice(frame) ||
        CanContinueEndRefillNextTurn(frame) ||
        CanContinueIncomingDistanceCourtesy(frame) ||
        CanContinueSoleTargetProtection(frame) ||
        CanContinuePhaseStartCardControl(frame) ||
        CanContinueUnequalHandHp(frame) ||
        CanContinuePhaseAlcohol(frame) ||
        CanContinueHpConditionalProgram(frame) ||
        CanContinueSelectedForeignCardSlash(frame) ||
        CanContinuePairedColorDisposition(frame) ||
        CanContinueSameNameHandDiscardOrDamage(frame) ||
        CanContinueCompletedUndamagedTargetReveal(frame) ||
        CanContinueRecipientCategoryMark(frame) ||
        CanContinueOffTurnUsedCardGift(frame) ||
        CanContinueOrderedPrintedSkillLoss(frame) ||
        CanContinueOverflowTargetCancellation(frame) ||
        CanContinueOriginalHandEntityProgram(frame) ||
        CanContinueOutsidePhaseDrawDiscard(frame) ||
        CanContinueMatchingRecast(frame) ||
        CanContinuePairedHandRecast(frame) ||
        CanContinuePhaseNamePrediction(frame) ||
        CanContinueLastDamageSourceReciprocity(frame) ||
        CanContinueGameActivationAwakening(frame) ||
        CanContinueDrawDiscardCategoryRefund(frame) ||
        CanContinueIssuedFixedDistanceEnding(frame) ||
        CanContinuePhaseHandSeizure(frame) ||
        CanContinueIssuedPrivateOffer(frame) ||
        CanContinueIssuedOriginalTargetAddition(frame) ||
        CanContinueIssuedGiftedSlashReward(frame) ||
        CanContinueIssuedJoinedTrickDamageReward(frame) ||
        CanContinueIssuedRoundGainedUse(frame) ||
        CanContinueIssuedDrawAdvice(frame) ||
        CanContinueIssuedHpLoss(frame) ||
        CanContinueReplacedPaidHpContinuation(frame) ||
        CanContinuePaidHpDamageShield(frame) ||
        CanContinuePaidDamageTargetMount(frame) ||
        CanContinuePaidDamageTargetObtain(frame) ||
        CanContinuePaidLostHpChain(frame);

    private sealed partial class ProgramSkillHost
    {
        public bool CanContinueAfterOwnerDeath(ProgramSkillFrame frame) =>
            engine.CanContinueProgramAfterOwnerDeath(frame);

        public bool CanContinueAfterSkillLoss(ProgramSkillFrame frame) =>
            engine.CanContinueProgramAfterSkillLoss(frame);
    }
}
