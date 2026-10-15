using System.Text.Json;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

Console.OutputEncoding = new System.Text.UTF8Encoding(false);

if (args.Contains("--filter", StringComparer.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Use --filter=<name>; an incomplete filter must not start the full suite.");
    return 2;
}

if (ProgramToolCommands.TryRun(args, out var programToolExitCode))
    return programToolExitCode;

if (args.FirstOrDefault() == "--ai-inspect")
{
    TacticalAiSimulation.Inspect(int.Parse(args[1]), int.Parse(args[2]), args[3]);
    return 0;
}

if (args.FirstOrDefault() == "--ai-batch")
{
    TacticalAiSimulation.Run(args[1]);
    return 0;
}

var unknownArguments = args.Where(argument =>
    !argument.StartsWith("--filter=", StringComparison.OrdinalIgnoreCase) &&
    !argument.Equals("--verbose", StringComparison.OrdinalIgnoreCase)).ToArray();
if (unknownArguments.Length > 0)
{
    Console.Error.WriteLine("Unknown Core check argument; use one or more --filter=<name> and optional --verbose.");
    return 2;
}

// Existing card-effect fixtures retain automatic discards to isolate their scenarios.
// Default/manual discard validation and complete-match coverage live in ManualDiscardChecks.
var tests = new (string Name, Action Body)[]
{
    ("program selection transforms requery facts without mutating sources", ProgramSelectionTransformChecks.CandidateTransformsRequeryFactsAndPreserveReadOnlySources),
    ("program selection relations preserve ordered pairs and card targets", ProgramSelectionTransformChecks.CorrelatedSelectionsPreserveOrderedPairsAndCardTargets),
    ("program selection ordering reads current ranks and preserves source order", ProgramSelectionTransformChecks.PindianOrderingUsesCurrentFactsWithoutMutatingInputs),
    ("program selection validation and target resolvers preserve ordered errors and isolated queries", ProgramSelectionValidationChecks.RegisteredValidationAndTargetResolversPreserveBoundaries),
    ("program selection registration preserves ordered stateless modules and failures", ProgramSelectionRegistrationChecks.CatalogRegistrationPreservesOrderFilteringAndFailures),
    ("program selection claimed sources preserve card and target stages", ProgramSelectionRegistrationChecks.ClaimedSourcesKeepCardAndTargetStagesSeparate),
    ("program selection sources requery private facts and freeze inputs", ProgramSelectionRegistrationChecks.ConvertedAndAllHandSourcesRequeryPrivateFacts),
    ("program action registration preserves source and command boundaries", ProgramActionRegistrationChecks.RegistrationAndDispatchPreserveBoundaries),
    ("character manifests preserve ordered registration and package isolation", CharacterContentCatalogChecks.OrderedRegistrationAndPackageIsolation),
    ("program capability registration isolates adapters and preserves typed host compatibility", ProgramCapabilityChecks.RegistrationAndIsolationPreserveBoundaries),
    ("program rule observer registration preserves ordered recursive boundaries", ProgramRuleObserverChecks.RegistrationPreservesOrderReentrancyAndIsolation),
    ("program rule observer registration rejects invalid metadata", ProgramRuleObserverChecks.InvalidRegistrationsFailBeforeDispatch),
    ("program legality registration preserves ordered capability gates", ProgramLegalityChecks.RegistrationAndDispatchPreserveBoundaries),
    ("program interaction registration preserves dispatch boundaries", ProgramInteractionChecks.RegistrationAndDispatchPreserveBoundaries),
    ("program interaction AI preserves published choices and conditional policies", ProgramInteractionChecks.AiSelectionPreservesPublishedOrderAndConditionalPolicies),
    ("SP Pang Tong: ActualOpaqueRevealOptionalExchangeAndEqualRanksCold", SpPangTongChecks.ActualOpaqueRevealOptionalExchangeAndEqualRanksCold),
    ("SP Pang Tong: ActualAtomicExchangeLowerOriginalRankDrawsOnceCold", SpPangTongChecks.ActualAtomicExchangeLowerOriginalRankDrawsOnceCold),
    ("SP Pang Tong: ActualPaidExchangeSourceLossDrainsBeforeRewardCold", SpPangTongChecks.ActualPaidExchangeSourceLossDrainsBeforeRewardCold),
    ("SP Pang Tong: ActualOtherDeathHealthBranchesAndPersistentGrantCold", SpPangTongChecks.ActualOtherDeathHealthBranchesAndPersistentGrantCold),
    ("SP Pang Tong: ActualWholeDrawInvocationBonusExcludesGiftAndOwnProducerCold", SpPangTongChecks.ActualWholeDrawInvocationBonusExcludesGiftAndOwnProducerCold),
    ("SP Pang Tong: ActualAtomicEquipmentExchangeRecoveryChildBeforeRewardCold", SpPangTongChecks.ActualAtomicEquipmentExchangeRecoveryChildBeforeRewardCold),
    ("Yuan Tan Yuan Shang: BasicDiscardDynamicSlashQuotaTwoTargetsAndTurnExpiryCold", YuanTanYuanShangChecks.BasicDiscardDynamicSlashQuotaTwoTargetsAndTurnExpiryCold),
    ("Yuan Tan Yuan Shang: NonBasicDiscardFirstTwoActualEquipmentPaymentsCold", YuanTanYuanShangChecks.NonBasicDiscardFirstTwoActualEquipmentPaymentsCold),
    ("Yuan Tan Yuan Shang: OrdinaryTrickImplicitSelfAndBorrowedCompletePairsCold", YuanTanYuanShangChecks.OrdinaryTrickImplicitSelfAndBorrowedCompletePairsCold),
    ("Yuan Tan Yuan Shang: PaidGainSourceLossAndOwnerDeathDrainOnceCold", YuanTanYuanShangChecks.PaidGainSourceLossAndOwnerDeathDrainOnceCold),
    ("Yuan Tan Yuan Shang: RealDiscardFaluChildAndOrdinaryDuelResponseCold", YuanTanYuanShangChecks.RealDiscardFaluChildAndOrdinaryDuelResponseCold),
    ("Yuan Tan Yuan Shang: FieldSilverLionRecoveryBeforePrivateDiscardCold", YuanTanYuanShangChecks.FieldSilverLionRecoveryBeforePrivateDiscardCold),
    ("Yuan Tan Yuan Shang: InsertedPlayRearmUnionsBansAndPreservesEquipmentOrdinalCold", YuanTanYuanShangChecks.InsertedPlayRearmUnionsBansAndPreservesEquipmentOrdinalCold),
    ("Lv Qian: StrictActualPhaseAndGrantedDamageContracts", LvQianChecks.StrictActualPhaseAndGrantedDamageContracts),
    ("Lv Qian: ActualWeiluRepeatedSourceNextPlayAndExactRestoreCold", LvQianChecks.ActualWeiluRepeatedSourceNextPlayAndExactRestoreCold),
    ("Lv Qian: ActualPaidWeiluSourceLossAndTargetDeathCold", LvQianChecks.ActualPaidWeiluSourceLossAndTargetDeathCold),
    ("Lv Qian: ActualZengdaoNativeLionDepositPermanentSourceDamageCold", LvQianChecks.ActualZengdaoNativeLionDepositPermanentSourceDamageCold),
    ("Lv Qian: ActualZengdaoDonorDeathPersistsAndRecipientDeathCleansCold", LvQianChecks.ActualZengdaoDonorDeathPersistsAndRecipientDeathCleansCold),
    ("Lv Kuang Lv Xiang: ActualSoleDodgeForeignNativeConversionAndSourceLossCold", LvKuangLvXiangChecks.ActualSoleDodgeForeignNativeConversionAndSourceLossCold),
    ("Lv Kuang Lv Xiang: ActualSelfNominationUsesSecondSlashWithoutNormalQuotaCold", LvKuangLvXiangChecks.ActualSelfNominationUsesSecondSlashWithoutNormalQuotaCold),
    ("Lv Kuang Lv Xiang: ActualNominatedActorMayDeclineWithoutInventedUseCold", LvKuangLvXiangChecks.ActualNominatedActorMayDeclineWithoutInventedUseCold),
    ("Lv Kuang Lv Xiang: ActualLiehouForeignHandThenDifferentHandRecipientSourceLossCold", LvKuangLvXiangChecks.ActualLiehouForeignHandThenDifferentHandRecipientSourceLossCold),
    ("Lv Kuang Lv Xiang: ActualLiehouRangeRecheckedAfterNativeEquipmentLossChildCold", LvKuangLvXiangChecks.ActualLiehouRangeRecheckedAfterNativeEquipmentLossChildCold),
    ("Lv Kuang Lv Xiang: ActualQigongDeclarationSuccessAndFalseChallengeCold", LvKuangLvXiangChecks.ActualQigongDeclarationSuccessAndFalseChallengeCold),
    ("Lv Kuang Lv Xiang: ActualRequestedNativeCostFamiliesAndActorProviderReturnsCold", LvKuangLvXiangChecks.ActualRequestedNativeCostFamiliesAndActorProviderReturnsCold),
    ("Xin Pi: ChijieAppliedGroupDamageStopsRemainingEffectsCold", XinPiChecks.ChijieAppliedGroupDamageStopsRemainingEffectsCold),
    ("Xin Pi: ChijieWholeUndamagedClaimAndSharedAllowanceCold", XinPiChecks.ChijieWholeUndamagedClaimAndSharedAllowanceCold),
    ("Xin Pi: ChijieRejectsNativeClaimedEntityReturnedToDiscardCold", XinPiChecks.ChijieRejectsNativeClaimedEntityReturnedToDiscardCold),
    ("Xin Pi: YinjuNativeDamageRecoveryTargetDrawLimitedAndExpiryCold", XinPiChecks.YinjuNativeDamageRecoveryTargetDrawLimitedAndExpiryCold),
    ("Xin Pi: YinjuPaidDrawSourceLossAndDeathNativeReturnCold", XinPiChecks.YinjuPaidDrawSourceLossAndDeathNativeReturnCold),
    ("Hua Man: StrictActualBarbarianDirectionPreparationAndXiliContracts", HuaManChecks.StrictActualBarbarianDirectionPreparationAndXiliContracts),
    ("Hua Man: ActualMansiAllHandBarbarianAndManyiNativeImmunityCold", HuaManChecks.ActualMansiAllHandBarbarianAndManyiNativeImmunityCold),
    ("Hua Man: ActualMansiEightActualGainsAwakensMaleStableXiliCold", HuaManChecks.ActualMansiEightActualGainsAwakensMaleStableXiliCold),
    ("Hua Man: ActualZhanyuanDeclinedMaleStillLosesMansiCold", HuaManChecks.ActualZhanyuanDeclinedMaleStillLosesMansiCold),
    ("Hua Man: ActualSouyingOutgoingClaimsExactUseAndPaidLossDeathCold", HuaManChecks.ActualSouyingOutgoingClaimsExactUseAndPaidLossDeathCold),
    ("Hua Man: ActualSouyingIncomingDirectionIndependentCancellationCold", HuaManChecks.ActualSouyingIncomingDirectionIndependentCancellationCold),
    ("Hua Man: ActualXiliNativeDamageBothDrawsAndPaidLossDeathCold", HuaManChecks.ActualXiliNativeDamageBothDrawsAndPaidLossDeathCold),
    ("Zhang Qi Ying: ActualFourTokensDiscardSuitsAndSourceLifetimeCold", ZhangQiYingChecks.ActualFourTokensDiscardSuitsAndSourceLifetimeCold),
    ("Zhang Qi Ying: ActualPrivatePrepareAndEndingArbitraryTopBottomCold", ZhangQiYingChecks.ActualPrivatePrepareAndEndingArbitraryTopBottomCold),
    ("Zhang Qi Ying: ActualJudgmentFiveKeepsPhysicalEntityCold", ZhangQiYingChecks.ActualJudgmentFiveKeepsPhysicalEntityCold),
    ("Zhang Qi Ying: ActualElementalThreeCategoryGainPaidChildCold", ZhangQiYingChecks.ActualElementalThreeCategoryGainPaidChildCold),
    ("Zhang Qi Ying: ActualOutOfTurnOtherDyingPeachCostCold", ZhangQiYingChecks.ActualOutOfTurnOtherDyingPeachCostCold),
    ("Cao Chun: InitialMarksPrintedEquipmentNativeLossAndCold", CaoChunChecks.InitialMarksPrintedEquipmentNativeLossAndCold),
    ("Cao Chun: DrawInitialRealSlashThenFreezeNextHandUseDebtCold", CaoChunChecks.DrawInitialRealSlashThenFreezeNextHandUseDebtCold),
    ("Cao Chun: EquipmentOnlyDebtPermitsNativeDistanceUnlimitedEndingSlashCold", CaoChunChecks.EquipmentOnlyDebtPermitsNativeDistanceUnlimitedEndingSlashCold),
    ("Cao Chun: PaidDebtDiscardSourceLossAndOwnerDeathCold", CaoChunChecks.PaidDebtDiscardSourceLossAndOwnerDeathCold),
    ("Wang Can: StrictActualGainDyingAndEndingContracts", WangCanChecks.StrictActualGainDyingAndEndingContracts),
    ("Wang Can: ActualSanwenPriorWitnessBatchAndTurnQuotaCold", WangCanChecks.ActualSanwenPriorWitnessBatchAndTurnQuotaCold),
    ("Wang Can: ActualQiaiClockwiseContributionsNoFictionalRecoveryCold", WangCanChecks.ActualQiaiClockwiseContributionsNoFictionalRecoveryCold),
    ("Wang Can: ActualQiaiNonzeroOwnerHumanPayerAuthorityCold", WangCanChecks.ActualQiaiNonzeroOwnerHumanPayerAuthorityCold),
    ("Wang Can: ActualDenglouPrivateFourNativeBasicsAndUnusableDiscardCold", WangCanChecks.ActualDenglouPrivateFourNativeBasicsAndUnusableDiscardCold),
    ("Wang Can: ActualDenglouGainBeforeBasicsSourceLossAndDeathCold", WangCanChecks.ActualDenglouGainBeforeBasicsSourceLossAndDeathCold),
    ("Zhang Qi Ying: ActualApprovedGrainDiscardVersusNativeCleanupCold", ZhangQiYingChecks.ActualApprovedGrainDiscardVersusNativeCleanupCold),
    ("Cao Chun: TrueCounterspellUsePaysDebtButOrdinaryResponseAndZeroMaterialDoNotCold", CaoChunChecks.TrueCounterspellUsePaysDebtButOrdinaryResponseAndZeroMaterialDoNotCold),
    ("Zhang Qi Ying: ActualBaguaConsumesEffectiveFiveAndNativeDefenseCold", ZhangQiYingChecks.ActualBaguaConsumesEffectiveFiveAndNativeDefenseCold),
    ("Cao Chun: InitialRequestedDrawFundedSlashPaysNativeSourceAndCold", CaoChunChecks.InitialRequestedDrawFundedSlashPaysNativeSourceAndCold),
    ("Cao Chun: InitialRequestedChainedFactionAndElementalSourcesUseNativePaymentsCold", CaoChunChecks.InitialRequestedChainedFactionAndElementalSourcesUseNativePaymentsCold),
    ("Fan Chou: ActualSilverLionFieldGainRecoveryChildCold", FanChouChecks.ActualSilverLionFieldGainRecoveryChildCold),
    ("Fan Chou: ActualWoodenOxFieldGainGrainDamageChildCold", FanChouChecks.ActualWoodenOxFieldGainGrainDamageChildCold),
    ("Fan Chou: ActualCompletedUseFieldRankSixAndPhaseQuotaCold", FanChouChecks.ActualCompletedUseFieldRankSixAndPhaseQuotaCold),
    ("Fan Chou: DeckPrivateCandidatesAndNativeFallbackCold", FanChouChecks.DeckPrivateCandidatesAndNativeFallbackCold),
    ("Fan Chou: OtherRecipientAutonomousDiscardOrGiftCold", FanChouChecks.OtherRecipientAutonomousDiscardOrGiftCold),
    ("Fan Chou: PaidGainSourceLossAndContinuedOwnerDeathCold", FanChouChecks.PaidGainSourceLossAndContinuedOwnerDeathCold),
    ("Fan Chou: ZeroMaterialNativeUseStillOffersCold", FanChouChecks.ZeroMaterialNativeUseStillOffersCold),
    ("Guo Si: ActualCompletedParentRejectsForeignWindowProofCold", GuoSiChecks.ActualCompletedParentRejectsForeignWindowProofCold),
    ("Guo Si: StrictTargetPactAndCompletedPairContracts", GuoSiChecks.StrictTargetPactAndCompletedPairContracts),
    ("Guo Si: ActualTanbeiRandomPrivateGainAndTargetBanCold", GuoSiChecks.ActualTanbeiRandomPrivateGainAndTargetBanCold),
    ("Guo Si: ActualTargetChosenUnlimitedSlashKeepsNativeDebitCold", GuoSiChecks.ActualTargetChosenUnlimitedSlashKeepsNativeDebitCold),
    ("Guo Si: ActualCompletedPairNativeSnatchAndPhaseQuotaCold", GuoSiChecks.ActualCompletedPairNativeSnatchAndPhaseQuotaCold),
    ("Guo Si: ActualEmptyTargetBreakAndNestedIssuanceOrderCold", GuoSiChecks.ActualEmptyTargetBreakAndNestedIssuanceOrderCold),
    ("Guo Si: ActualBorrowedPrimaryAndCompletedExtraTargetCold", GuoSiChecks.ActualBorrowedPrimaryAndCompletedExtraTargetCold),
    ("Guo Si: ActualPaidRandomGainSourceLossAndDeathCold", GuoSiChecks.ActualPaidRandomGainSourceLossAndDeathCold),
    ("Tang Zi: StrictPopulationGrantAndActualThresholdSourceLifetimeCold", TangZiChecks.StrictPopulationGrantAndActualThresholdSourceLifetimeCold),
    ("Tang Zi: ActualPrivateExactXunxunReorderAndNormalDrawCold", TangZiChecks.ActualPrivateExactXunxunReorderAndNormalDrawCold),
    ("Tang Zi: ActualEquipmentUseRewardsBeforeSilverLionRecoveryCold", TangZiChecks.ActualEquipmentUseRewardsBeforeSilverLionRecoveryCold),
    ("Tang Zi: ActualDiscardThresholdAndUndefinedHpPopulationCold", TangZiChecks.ActualDiscardThresholdAndUndefinedHpPopulationCold),
    ("Huang Quan: ActualRecordedDamageAndForeignRecoveryCold", HuangQuanChecks.ActualRecordedDamageAndForeignRecoveryCold),
    ("Huang Quan: ActualJianjiDrawTwoEquipmentAndDelayedNativeUsesCold", HuangQuanChecks.ActualJianjiDrawTwoEquipmentAndDelayedNativeUsesCold),
    ("Huang Quan: ActualRequestedDeclarationSuccessAndChallengedFailureCold", HuangQuanDeclarationChecks.ActualRequestedDeclarationSuccessAndChallengedFailureCold),
    ("Zhang Ji: ActualTargetRankChooserJudgmentAndTwoDamageCold", ZhangJiChecks.ActualTargetRankChooserJudgmentAndTwoDamageCold),
    ("Zhang Ji: ActualHistoricalActivationsIssueDistinctNativeEquipmentCold", ZhangJiChecks.ActualHistoricalActivationsIssueDistinctNativeEquipmentCold),
    ("Su Fei: ConsecutiveNativeTargetsExactDrawGiftColdAndTurnQuota", SuFeiChecks.ConsecutiveNativeTargetsExactDrawGiftColdAndTurnQuota),
    ("Su Fei: NativeBorrowedSwordUsesOnlyOrderedPrimaryTargets", SuFeiChecks.NativeBorrowedSwordUsesOnlyOrderedPrimaryTargets),
    ("Sima Hui: ActualFirstTurnOrderedSealsAndStableGrantedInstancesCold", SimaHuiChecks.ActualFirstTurnOrderedSealsAndStableGrantedInstancesCold),
    ("Sima Hui: ActualElementalOriginPrivateDistributionAndPaidChildrenCold", SimaHuiChecks.ActualElementalOriginPrivateDistributionAndPaidChildrenCold),
    ("Sima Hui: ActualYinshiSealAndArmorGatesAndNativeYeyanCostTail", SimaHuiChecks.ActualYinshiSealAndArmorGatesAndNativeYeyanCostTail),
    ("Xu Rong: ActualForeignPlayRandomPaymentsAndTurnRestrictionsCold", XuRongChecks.ActualForeignPlayRandomPaymentsAndTurnRestrictionsCold),
    ("Xu Rong: ActualFrozenDyingNeedAndPhysicalSlashClaimCold", XuRongChecks.ActualFrozenDyingNeedAndPhysicalSlashClaimCold),
    ("Li Jue: LangxiActualPrepareRandomDamageCold", LiJueChecks.LangxiActualPrepareRandomDamageCold),
    ("Li Jue: YisuanActualNullificationDeferredAndMultiMaterialCold", LiJueChecks.YisuanActualNullificationDeferredAndMultiMaterialCold),
    ("Bei Mi Hu: ActualMarkedDamageOneDrawAndSevenObtainedAwakening", BeiMiHuChecks.ActualMarkedDamageOneDrawAndSevenObtainedAwakening),
    ("Bei Mi Hu: ActualCanshiMultipleLegalDrawTwoTargetsAndMarkerPayments", BeiMiHuChecks.ActualCanshiMultipleLegalDrawTwoTargetsAndMarkerPayments),
    ("Liu Qi: WenjiActualGiftMatchingSlashAndDuelResponsesCold", LiuQiChecks.WenjiActualGiftMatchingSlashAndDuelResponsesCold),
    ("Liu Qi: TunjiangActualUnskippedEmptyPlayAndOtherNativeUse", LiuQiChecks.TunjiangActualUnskippedEmptyPlayAndOtherNativeUse),
    ("Lu Zhi Wei: ActualQingzhongCurrentMinimumAtomicExchangeCold", LuZhiWeiChecks.ActualQingzhongCurrentMinimumAtomicExchangeCold),
    ("Lu Zhi Wei: ActualWeijingNativeSlashQuotaRoundAndCold", LuZhiWeiChecks.ActualWeijingNativeSlashQuotaRoundAndCold),
    ("Wang Yun: ActualRandomWeaponAndIssuedSlashDamageAwakening", WangYunChecks.ActualRandomWeaponAndIssuedSlashDamageAwakening),
    ("Wang Yun: ActualRandomTrickSamplesColdAndNativeLightningJudgment", WangYunChecks.ActualRandomTrickSamplesColdAndNativeLightningJudgment),
    ("Zhuge Guo: ActualEquipmentAcquisitionExactPhysicalExtraTargetCold", ZhugeGuoChecks.ActualEquipmentAcquisitionExactPhysicalExtraTargetCold),
    ("Zhuge Guo: PrivateYuhuaPeekPartitionAndNonbasicNativeDiscard", ZhugeGuoChecks.PrivateYuhuaPeekPartitionAndNonbasicNativeDiscard),
    ("Xi Zhi Cai: ActualGameStartRelationDamageAndClampedRecoveryCold", XiZhiCaiChecks.ActualGameStartRelationDamageAndClampedRecoveryCold),
    ("Xi Zhi Cai: ActualPerPointRedJudgmentTianduClaimAndRecordedDoubleDrawCold", XiZhiCaiChecks.ActualPerPointRedJudgmentTianduClaimAndRecordedDoubleDrawCold),
    ("He Qi: JointBoundPrivacyRealDiscardAndOpaqueUnrevealedGain", HeQiChecks.JointBoundPrivacyRealDiscardAndOpaqueUnrevealedGain),
    ("He Qi: EquipmentSuitThresholdsStableInstancesAndNativeLinkedModifiers", HeQiChecks.EquipmentSuitThresholdsStableInstancesAndNativeLinkedModifiers),
    ("Kan Ze: ActualWholeHandRecipientPrivateRevealAndBothComplementsCold", KanZeChecks.ActualWholeHandRecipientPrivateRevealAndBothComplementsCold),
    ("Kan Ze: ActualRepeatedKuanshiAndDeferredDrawSkipCold", KanZeChecks.ActualRepeatedKuanshiAndDeferredDrawSkipCold),
    ("Zhao Xiang: ActualPaidPlayConversionDrawAndCumulativeRemovalCold", ZhaoXiangChecks.ActualPaidPlayConversionDrawAndCumulativeRemovalCold),
    ("Zhao Xiang: ActualZeroMarkerFiveShuPrivateReplacementAndLowerClamp", ZhaoXiangChecks.ActualZeroMarkerFiveShuPrivateReplacementAndLowerClamp),
    ("Bu Zhi: ActualNativeHandGainAndWholeHandLossOfferOnce", BuZhiChecks.ActualNativeHandGainAndWholeHandLossOfferOnce),
    ("Bu Zhi: ActualEquipmentTargetChooserAndDynamicRebelLimit", BuZhiChecks.ActualEquipmentTargetChooserAndDynamicRebelLimit),
    ("Dong Bai: BlackGiftRecipientAuthorityColdAndExactDraw", DongBaiChecks.BlackGiftRecipientAuthorityColdAndExactDraw),
    ("Dong Bai: ActualHpLossReleasesAllEntitiesAndForeignForcedDiscardIsAllowed", DongBaiChecks.ActualHpLossReleasesAllEntitiesAndForeignForcedDiscardIsAllowed),
    ("Jiqiao and Linglong: ActualPaidEquipmentDoubleRevealAllNonEquipmentCold", JiqiaoAndLinglongChecks.ActualPaidEquipmentDoubleRevealAllNonEquipmentCold),
    ("Jiqiao and Linglong: ActualVirtualBaguaAndQinggangNativeArmorIgnoreCold", JiqiaoAndLinglongChecks.ActualVirtualBaguaAndQinggangNativeArmorIgnoreCold),
    ("Old hand disposition: Actual Old Hand Discard Then Opaque Capped Foreign Cards", OldHandDispositionChecks.ActualOldHandDiscardThenOpaqueCappedForeignCards),
    ("Old hand disposition: Actual Participant Prevention Source Draw And Shared Turn Usage", OldHandDispositionChecks.ActualParticipantPreventionSourceDrawAndSharedTurnUsage),
    ("Xuan Gong Zhu: ActualQimeiForeignHandChangesSharedQuotaAndDamageHpCold", XuanGongZhuChecks.ActualQimeiForeignHandChangesSharedQuotaAndDamageHpCold),
    ("Xuan Gong Zhu: ActualZhuijiBothChoicesSamePlayDebtCold", XuanGongZhuChecks.ActualZhuijiBothChoicesSamePlayDebtCold),
    ("Identity concealment: UndefinedHpRejectsNativeRecoveryAndMaxHpNumberCalculation", IdentityConcealmentChecks.UndefinedHpRejectsNativeRecoveryAndMaxHpNumberCalculation),
    ("Identity concealment: GameStartingHpLossAppearanceDoesNotOfferOffTurnGaoling", IdentityConcealmentChecks.GameStartingHpLossAppearanceDoesNotOfferOffTurnGaoling),
    ("Identity concealment: NativeGroupRecoveryAndSilverLionRemovalRejectUndefinedHp", IdentityConcealmentChecks.NativeGroupRecoveryAndSilverLionRemovalRejectUndefinedHp),
    ("Identity concealment: SetupEveryViewerHasUndefinedHpAndForeignTemplatePrivacy", IdentityConcealmentChecks.SetupEveryViewerHasUndefinedHpAndForeignTemplatePrivacy),
    ("Identity concealment: ActualOwnTurnAppearanceRestoresLordInitialBonusWithoutOffTurnGaoling", IdentityConcealmentChecks.ActualOwnTurnAppearanceRestoresLordInitialBonusWithoutOffTurnGaoling),
    ("Identity concealment: NativeSlashAppearanceGaolingRecoveryChildColdAndLaterDamage", IdentityConcealmentChecks.NativeSlashAppearanceGaolingRecoveryChildColdAndLaterDamage),
    ("Identity concealment: NativeProgramHpLossAndDamageReplacementRespectUndefinedHp", IdentityConcealmentChecks.NativeProgramHpLossAndDamageReplacementRespectUndefinedHp),
    ("Identity concealment: PaidGaolingRecoveryChildCanKillAppearingOwnerAndReturnOnce", IdentityConcealmentChecks.PaidGaolingRecoveryChildCanKillAppearingOwnerAndReturnOnce),
    ("Frozen hand roster: ActualFrozenRosterChooserOrderPrivacyAndMandatoryReward", FrozenHandRosterChecks.ActualFrozenRosterChooserOrderPrivacyAndMandatoryReward),
    ("Frozen hand roster: ActualNativeDamageDeathThenReevaluatedOriginalReward", FrozenHandRosterChecks.ActualNativeDamageDeathThenReevaluatedOriginalReward),
    ("Private whirl: PrivateFiveDepositsColdViewsAndResidualDrawCount", PrivateWhirlChecks.PrivateFiveDepositsColdViewsAndResidualDrawCount),
    ("Private whirl: ActualNextPlayEndingExpiresPrivateEntities", PrivateWhirlChecks.ActualNextPlayEndingExpiresPrivateEntities),
    ("Cao Ying: Granted Current Jianxiong Draw Child Then Claims Native Damage Card", CaoYingChecks.GrantedCurrentJianxiongDrawChildThenClaimsNativeDamageCard),
    ("Cao Ying: Native Guess Privacy Refund Draw And Single Damage", CaoYingChecks.NativeGuessPrivacyRefundDrawAndSingleDamage),
    ("Dong Zhao: actual other lord turn record and own nonlord boundary cold", DongZhaoChecks.ActualForeignLordTurnRecordAndOwnNonLordBoundaryCold),
    ("Dong Zhao: Actual Zaowang Private Grant And Loyalist Succession Cold", DongZhaoChecks.ActualZaowangPrivateGrantAndLoyalistSuccessionCold),
    ("Dong Zhao: Actual Zaowang Rebel Killer Victory And Unarmed Control Cold", DongZhaoChecks.ActualZaowangRebelKillerVictoryAndUnarmedControlCold),
    ("Bao 2025: claimed provenance and nonbasic policies coexist cold", Bao2025Checks.ActualClaimedMaterialProvenanceAndNonBasicPoliciesCoexistCold),
    ("Bao 2025: explicit assault targets damage random and empty cold", Bao2025Checks.ActualAssaultExplicitTargetsDamageRandomDiscardAndEmptyCold),
    ("Bao 2025: recipient replacement learns current Zhengnan cold", Bao2025Checks.ActualDyingReplaceAllGrantsAndLearnZhengnanCold),
    ("Simultaneous Pindian: typed claims and common material ownership cold", SimultaneousPindianChecks.ActualTypedResultClaimsCommonMaterialOwnershipCold),
    ("Simultaneous Pindian: three opponents private commit ties payments cold", SimultaneousPindianChecks.ActualThreeWayPrivateCommitTieAndPaymentsCold),
    ("Simultaneous Pindian: current JiCi equal decline and exact phase refund", SimultaneousPindianChecks.ActualCurrentJiCiLessEqualDeclineAndPhaseRefundCold),
    ("Other death growth: actual deaths choose all skills and extra Play cold", DeathGrowthSkillsChecks.ActualOtherDeathsDrawChooseAllSkillsAndExtraPlayCold),
    ("Other death growth: current living female distance and directions", DeathGrowthSkillsChecks.DynamicLivingFemaleDistanceSelfAndDirectionsCold),
    ("First Slash Duel benefit: private first draw and combined ordinal", FirstSlashDuelBenefitChecks.ActualFirstSlashPrivateDrawAndCombinedOrdinal),
    ("First Slash Duel benefit: converted Duel reverse damage and source loss", FirstSlashDuelBenefitChecks.ActualConvertedDuelReverseDamageSurvivesSourceLoss),
    ("Damage target budget: actual self source-less and two-point occurrences", DamageTargetBudgetChecks.ActualDamageOccurrencesSelfSourceLessAndTwoPoints),
    ("Damage target budget: Borrowed Sword ordered pairs and Iron Chain self", DamageTargetBudgetChecks.ActualBorrowedSwordOrderedPairsAndIronChainSelf),
    ("Damage zones and adjacent red target: each zone privacy once and turn expiry", DamageZoneAndAdjacentTargetChecks.ActualEachZonePrivateSelectionOnceAndTurnExpiry),
    ("Damage zones and adjacent red target: actual dying Peach joins original rescue", DamageZoneAndAdjacentTargetChecks.ActualRedDyingPeachUseJoinsAndReturnsOriginalRescue),
    ("Extra card effect and rank guess: Peach and stacked Alcohol cold replay", ExtraEffectsAndRankGuessChecks.ActualPeachAndDoubleAlcoholBonusColdReplay),
    ("Extra card effect and rank guess: second Snatch public choice retains material", ExtraEffectsAndRankGuessChecks.ActualSecondSnatchRechoosesPublicCardAndRetainsMaterial),
    ("Extra card effect and rank guess: rank equality public reveal and paid child cold", ExtraEffectsAndRankGuessChecks.ActualRankEqualityPublicRevealGainAndPaidChildColdReturn),
    ("Extra card effect and rank guess: death recipient owns permanent skill choice", ExtraEffectsAndRankGuessChecks.ActualDeathRecipientOwnChoicePermanentGrantAndOwnerDecline),
    ("Extra card effect and rank guess: paid source loss death and original use once", ExtraCardEffectPaidSourceChecks.PaidHandCostSourceLossAndOwnerDeathKeepOriginalUseOnce),
    ("Extra card effect and rank guess: legitimately claimed global material and cold second counterspell", ExtraEffectsAndRankGuessChecks.ActualGlobalExtraEffectRetainsLegitimatelyClaimedMaterialAndColdCounterspell),
    ("Rice pile: actual ending hand equipment storage and cold children", RicePileChecks.ActualEndingDrawHEStoreAndColdMovementChildren),
    ("Rice pile: any judgment last rice recovery and multiple pile identity", RicePileChecks.AnyJudgmentLastRiceRecoveryAndMultiplePileIdentity),
    ("Incoming distance courtesy: actual faction cost permanent direction and extra turns", IncomingDistanceCourtesyChecks.ActualFactionCostAndPermanentDirectionAcrossTurns),
    ("Incoming distance courtesy: distinct benefits foreign privacy and recovery", IncomingDistanceCourtesyChecks.CourtesyDistinctBenefitsForeignPrivacyAndRecovery),
    ("Sole target protection: actual Slash and black trick substitution", SoleTargetProtectionChecks.SubstitutionChangesActualSlashAndBlackTrickTarget),
    ("Sole target protection: black delayed quarantine committed use and actual end", SoleTargetProtectionChecks.BlackDelayedQuarantineFinishesCommittedUseAndReturnsAfterActualTurn),
    ("Sole target protection: legacy Benxi duplicate pair tail actual cursor", SoleTargetProtectionChecks.LegacyCurrentEnhancementDuplicateTailUsesActualCursor),
    ("Targeted trick hand HP: actual ending adjustment reward gift and cold", TargetedTrickHandHpChecks.ActualEndingDrawDiscardSelfAndOptionalGiftColdReturn),
    ("Targeted trick hand HP: actual trick targets ineffective and physical completion", TargetedTrickHandHpChecks.TargetedTrickAdditionIneffectivenessAndCompletePhysicalUse),
    ("End refill next turn: capped actual refill and tied greatest hand", EndRefillNextTurnChecks.ActualEndRefillCappedAndNextTurnMostIncludesTie),
    ("End refill next turn: bidirectional HP replacement self source-less and chain", EndRefillNextTurnChecks.ActualBidirectionalHpReplacementSelfSourceLessAndChain),
    ("Phase start card control: payment privacy directed distance and actual expiry", PhaseStartCardControlChecks.MeibuPaymentPrivacyDirectedDistanceAndActualTurnExpiry),
    ("Phase start card control: protected discard self armor and Slash ban", PhaseStartCardControlChecks.MumuProtectedDiscardSelfArmorAndSlashBan),
    ("Phase start card control: trick delayed and counterspell actual uses", PhaseStartCardControlChecks.ZhixiTrickDelayedAndCounterspellActualUses),
    ("Named ambush and source hand: private names all targets and cold recovery", NamedAmbushAndSourceHandChecks.PrivateNamesAndAllTargetUseStateSurviveColdRecovery),
    ("Named ambush and source hand: current branches and zero draw", NamedAmbushAndSourceHandChecks.SourceHandChoicesKeepCurrentTwoBranchesAndZeroDraw),
    ("Forced phase Wine: actual actor commit quota and cold returns", ForcedPhaseAlcoholChecks.RealActorWineCommitQuotaAndColdReturns),
    ("Forced phase Wine: mixed deaths and dead turn owner", ForcedPhaseAlcoholChecks.DeathDrawFreezesMixedKillCountsAndDeadTurnOwner),
    ("Population health kinds: actual maximum HP once and cold children", PopulationHealthKindChecks.ActualMaximumHpChangesDrawOnceAndColdReplayPaidChildren),
    ("Population health kinds: preparation awakening draw and game quota", PopulationHealthKindChecks.PreparationPopulationAwakeningGrantsAfterDrawAndKeepsGameUsage),
    ("Card response completion cost: typed health kind actual recovery", CardResponseCompletionCostChecks.TypedHealthKindMatchesActualResponseCostRecovery),
    ("HP conditional grants: actual HP thresholds phase quota and cold replay", HpConditionalSkillGrantChecks.ActualHpThresholdsPhaseQuotaAndColdReplay),
    ("HP conditional grants: exact source lifetime stable shared instance and privacy", HpConditionalSkillGrantChecks.ExactSourceLifetimeStableSharedInstanceAndReadPrivacy),
    ("Zhanyi benefits: recovery rule expiration and legacy defaults", ZhuLingBenefitChecks.SharedRecoveryAndRuleExpirationKeepLegacyDefaults),
    ("Zhanyi benefits: physical converted zero Peach and cold children", ZhuLingBenefitChecks.PhysicalConvertedAndZeroPeachesFreezeRecoveryAcrossColdChildren),
    ("Triggered card conversion: real hand equipment materials and cold replay", TriggeredCardConversionChecks.BlackHandAndEquipmentUseRealMaterialsAndColdReplay),
    ("Triggered card conversion: first round opportunity decline and extra turn", TriggeredCardConversionChecks.RoundFirstOpportunityDoesNotMoveAfterDeclineOrExtraTurn),
    ("program continuation registration preserves ordered typed returns", ProgramContinuationChecks.OrderedRegistrationPreservesReturnBoundaries),
    ("native return protocol preserves ordered preparation and isolation", ProgramNativeReturnChecks.OrderedNativeBoundariesPreservePreparationAndIsolation),
    ("native return protocol rejects invalid metadata", ProgramNativeReturnChecks.InvalidNativeReturnMetadataFailsBeforeDispatch),
    ("program window returns preserve typed parent guards and registration", ProgramWindowReturnChecks.RegistrationAndTypedParentGuardsPreserveBoundaries),
    ("typed trigger window driver preserves candidate and closing boundaries", ProgramTriggerWindowDriverChecks.TypedWindowDriverPreservesCandidateAndClosingBoundaries),
    ("trigger window protocol preserves ordered skip and typed returns", TriggerWindowProtocolChecks.OrderedSkipAndTypedReturnsPreserveBoundaries),
    ("First strategic damage: committed cursor survives real child windows and replay", FrameOwnedBatchChecks.StrategicFirstDamageInstructionKeepsCommittedCursorAcrossChildren),
    ("Luochong discard: accumulated payments remain frozen across replay", LuochongDiscardChecks.AccumulatedDiscardsRemainFrozenAcrossReplay),
    ("Luochong lethal loss: committed dying continuation replays once", LuochongDiscardChecks.LethalLossKeepsCommittedCursorAcrossDyingReplay),
    ("Observer subtree: exact paid edges, pending choice and once-only cold return", PairedHandRecastChecks.ObserverSubtreeRejectsUnrelatedEdgesAndResumesExactlyOnce),
    ("frame-owned choices preserve multi-step bindings privacy and replay", ProgramChoiceDraftChecks.MultiStepBindingsPrivacyAndReplay),
    ("Damage nature: fire chain normal thunder HP loss and cold replay", DamageNaturePreventionChecks.ActualFireAndChainPreventedNormalThunderAndHpLossPreserved),
    ("Damage nature: preparation Slash distance and play quota", DamageNaturePreventionChecks.PreparationSlashHasDistanceFreedomAndNormalPlayQuota),
    ("Mobile Chen Zhen: same color hand payment shared phase quota and replay", MobileChenZhenChecks.SameColorHandPaymentSharedPhaseQuotaAndReplay),
    ("Zhanyi equipment entry: placed equipment triggers once and cold replays", ZhuLingEquipmentEntryChecks.PlacedEquipmentTriggersOnceAndColdReplays),
    ("Zhanyi equipment entry: shared destination preserves hand gain contracts", ZhuLingEquipmentEntryChecks.EquipmentEntryContractsPreserveHandGainBoundaries),
    ("Friend trio prediction privacy full reward and cold replay", FriendTrioChecks.PredictionPrivacyFullRewardAndColdReplay),
    ("Friend trio Xuanjian whole suit Qihui and reclaim", FriendTrioChecks.XuanjianWholeSuitQihuiAndReclaim),
    ("Friend events recast Duel shared discard budget and cold replay", FriendEventChecks.RecastDuelSharesDiscardBudgetAndColdReplay),
    ("Friend events minimum suit private hold and actual return", FriendEventChecks.MinimumSuitHoldStaysPrivateAndReturnsAtActualTurnEnd),
    ("Mou Zhuge Liang private top yang payment and cold replay", MouZhugeLiangChecks.PrivateTopYangPaymentAndColdReplay),
    ("Friend Pang Tong manjuan ordered current gain cold replay", FriendPangTongChecks.ManjuanOrdersOnlyCurrentGainAndReplays),
    ("Friend Pang Tong yangming real held use unique suits", FriendPangTongChecks.YangmingUsesRealHeldCardsAndUniqueSuits),
    ("Mou Zhuge Liang undamaged copy draw and color limit", MouZhugeLiangChecks.UndamagedCopyDrawAndColorLimit),
    ("Recipient category mark: face-up gift issues one public token and survives source loss", OrdinaryZuoFenChecks.FaceUpGiftIssuesOnePublicTokenAndRetainsItAfterSourceLoss),
    ("Recipient category mark: Lei recovers native dying to one and draws once", OrdinaryZuoFenChecks.LeiRecoversNativeDyingToOneAndDrawsOnce),
    ("Recipient category mark: Fu blindly discards up to two from one area", OrdinaryZuoFenChecks.FuBlindlyDiscardsUpToTwoFromOneArea),
    ("Recipient category mark: Song adds legal targets to the original Slash", OrdinaryZuoFenChecks.SongAddsLegalTargetsToOriginalSlash),
    ("Chained state basic: own play pays state and returns once", ChainedStateBasicConversionChecks.OwnPlayStateCostUsesNativeQuotaAndReturnsOnce),
    ("Chained state basic: requested uses exclude pure provision", ChainedStateBasicConversionChecks.RequestedUsesExcludePureProvisionAndKeepNativeParents),
    ("Foreign selected card Slash: hand use and exact outcome", ForeignSelectedCardSlashChecks.HandSlotsFreezeNativeUseAndDamageOutcome),
    ("Foreign selected card Slash: equipment Judgment and native children", ForeignSelectedCardSlashChecks.EquipmentAndJudgmentMaterialsKeepNativeChildren),
    ("Actor hand limit penalty: real uses stack and change actual discard", ActorHandLimitPenaltyChecks.RealUsesStackAndChangeActualDiscard),
    ("Actor hand limit penalty: added redirected and Borrowed Sword targets", ActorHandLimitPenaltyChecks.AddedRedirectedAndBorrowedSwordTargets),
    ("Play phase temporary skills: independent sources share state and preserve permanent grants", PlayPhaseTemporarySkillChecks.MultiplePhaseGrantSourcesShareInstanceStateQuotaAndPreservePermanentSource),
    ("Play phase temporary skills: ending child and exact boundary cold", PlayPhaseTemporarySkillChecks.PlayEndingChildrenKeepPhaseGrantsUntilExactBoundaryCloses),
    ("Round gained source uses: qualified basic materials distance and quota", OrdinaryShiBaoChecks.BasicUsesRequireEveryCurrentRoundMaterialAndRespectSourceQualification),
    ("Round gained source uses: trick plus minus and implicit self", OrdinaryShiBaoChecks.OrdinaryTrickTargetAdditionRemovalAndImplicitSelfFinishOnce),
    ("Unique maximum trick targets: actual hand leader and tied counts", OrdinaryXinChangChecks.LargestHandTrickAdditionUsesActualActorAndRejectsTies),
    ("Unique maximum trick targets: same use damage reward and paid source loss", OrdinaryXinChangChecks.JoinedTrickDamageRewardOwnsExactUseAndSurvivesSourceLoss),
    ("Paired color responses: same color actual hand payment and duplicate grants", OrdinarySimaZhouChecks.SameColorPairDiscardsOneRealHandCardAndDeduplicatesGrants),
    ("Paired color responses: both directions and original Duel use stay exact", OrdinarySimaZhouChecks.ReverseDodgeAndDuelPairsRetainTheRealUsedCard),
    ("Last zone conversions: equipment Nullification Silver Lion and movement children", LastZoneConversionChecks.LastEquipmentNullificationOwnsSilverLionRecoveryAndMovementChildren),
    ("Last zone conversions: judgment Slash use and pure Duel response retain printed material", LastZoneConversionChecks.LastJudgmentSlashUsesAndPureDuelResponsesRetainPrintedDelayedMaterial),
    ("Overflow target cancellation: unique physical and pair materials return through native gain", OrdinaryZhangHuaChecks.OverflowUniquePhysicalAndPairMaterialsReturnThroughNativeGain),
    ("Matching recast: canonical names self quota and extra Play phase", OrdinaryZhangHuaChecks.MatchingRecastCanonicalNamesSelfQuotaAndExtraPhase),
    ("Ordered printed skill loss: frozen draw and exact turn restoration survive source loss", OrdinaryZhangHuaChecks.FrozenPrintedLossDrawAndExactTurnRestoreSurviveSourceLoss),
    ("Turn default allocation: phase defaults preserve modifiers and repeated Play", OrdinaryZhouChuChecks.PhaseDefaultsRemainSeparateFromNormalModifiersAndRepeatedPlay),
    ("Round distinct basic use: real pair pays once and shares round name across regrant", OrdinaryZhouChuChecks.RealPairUsePaysOnceAndSharesRoundNameAcrossRegrant),
    ("Card response completion cost: Silver Lion equipment Dodge drains health before completion", CardResponseCompletionCostChecks.SilverLionEquipmentDodgeDrainsHealthBeforeCompletion),
    ("Completed undamaged target reveal: public pile payment stays separate from native materials", CardResponseCompletionCostChecks.PublicPileSlashPaymentRemainsSeparateFromNativeMaterials),
    ("Card response completion: native Zhangba pair and cold return", CardResponseCompletionNativeChecks.NativeZhangbaDuelResponseCompletesTwoEntitiesOnceAndColdReturns),
    ("Same name hand demand: actual faction providers own completion exactly once", OrdinaryGuoHuaiChecks.FactionProvidersOwnTheirCompletedResponseExactlyOnce),
    ("Completed undamaged target reveal: whole-use group target same-color discard and cold return", OrdinaryGuoHuaiChecks.UndamagedGroupTargetRevealSameColorAndColdReturn),
    ("Original hand entities: initial physical loss, returned card and real paired recast", OrdinaryYangHuChecks.OriginalInitialEntitiesDepartOnceAndChongxinReallyRecasts),
    ("Original hand entities: optional death transfers only personally received permanent bonuses", OrdinaryYangHuChecks.DeathInheritanceTransfersOnlyOwnReceivedPermanentBonuses),
    ("Outside phase draw discard: actual category discard excludes recast and Discard phase", OrdinaryYangHuChecks.OutsideDiscardUsesRealSanchenPaymentAndExcludesRecastAndDiscardPhase),
    ("Paired hand recast: real Hand private selection, phase quota and cold native return", PairedHandRecastChecks.RealHandPrivateChoiceQuotaAndColdReturn),
    ("Native discard classification: exact paid producers exclude adjacent gain and cleanup", NativeDiscardClassificationChecks.ExactPaidProducersExcludeNeighborReasons),
    ("Phase name prediction: secret actual phase, escalating round cost and native settlement", PhaseNamePredictionChecks.SecretExactPhaseAndSettlement),
    ("Phase name prediction: stolen armor native owner recovery and collateral return", PhaseNamePredictionChecks.StolenArmorNativeOwnerRecoveryAndCollateralReturn),
    ("Last damage source reciprocity: latest actual source, native HE payment and physical regrant memory", LastDamageSourceReciprocityChecks.LatestSourceAndPhysicalRegrantKeepExactNativeReciprocity),
    ("Unexpected Assault: opaque reveal native damage counterspell and conversion lifetime", UnexpectedAssaultChecks.OpaqueRevealDamageNullificationAndConversionLock),
    ("Activation-count awakening: actual refunded activations preparation quota and source loss", GameActivationAwakeningChecks.ActualRefundedActivationsQualifyPreparationOnceAndSurviveSourceLoss),
    ("Category discard refund: distinct payment phase refund and actual turn target ban", DrawDiscardCategoryRefundChecks.DistinctPaymentRefundsPhaseOnlyAndBansOriginalActualTurnTarget),
    ("Category discard refund: equipment child and lost source", DrawDiscardCategoryRefundChecks.EquipmentBatchSettlesAfterAcquiredSourceQualificationLoss),
    ("Draw-funded distinct basic: qualification ledger and paid children", DrawFundedDistinctBasicUseChecks.QualificationLedgerAndPaidChildren),
    ("Draw-funded distinct basic: native Dodge Dying and program returns", DrawFundedDistinctBasicResponseChecks.NativeDodgeDyingAndProgramReturns),
    ("Liejie source discard: effective red quota mixed HE and native children", LiejieSourceDiscardChecks.EffectiveRedQuotaKeepsMixedHePaymentAndNativeChildren),
    ("Liejie source discard: zero decline protected and general weapon payments", LiejieSourceDiscardChecks.ZeroDeclineProtectionAndGeneralWeaponsKeepPhysicalBoundaries),
    ("Choice target prerequisite: missing targets retain mandatory draw recovery and consume group once", ChoiceTargetPrerequisiteChecks.MissingTargetsRetainMandatoryDrawRecoveryAndConsumeGroupOnce),
    ("Choice target prerequisite: available targets keep exact choice through damage and private discard children", ChoiceTargetPrerequisiteChecks.AvailableTargetsKeepExactChoiceThroughDamageAndPrivateDiscardChildren),
    ("Different color judgment replacement: effective owner and subject colors pay hand equipment and replay once", DifferentColorJudgmentReplacementChecks.EffectiveOwnerAndSubjectColorsPayHandEquipmentAndReplayOnce),
    ("Different color judgment replacement: same color none and legacy defaults keep private native choices", DifferentColorJudgmentReplacementChecks.SameColorNoneAndLegacyDefaultsKeepPrivateNativeChoices),
    ("Ending damage marker: frozen actual damage settles once", TurnEndDamageMarkerChecks.FrozenActualDamageAddsOnceAcrossChildrenAndColdReplay),
    ("Liu Hong activation: decline and paid children return once", LiuHongActivationChecks.YujueDeclineAndPlayActivationKeepRealPaidChildrenOnce),
    ("Liu Hong activation: native AI available slots and source-less damage", LiuHongActivationChecks.YujueNativeAiUsesAvailableSlotsAndTuxingExcludesSourcelessDamage),
    ("Tang Ji paid continuation: marked death discards and returns once", TangJiPaidContinuationChecks.MarkedDeathDiscardsOriginalHandEquipmentAndReturnsOnce),
    ("Tang Ji paid continuation: lethal prevention rescues and gifts once", TangJiPaidContinuationChecks.LethalPreventionRescuesThenGiftsWithoutRepaying),
    ("Tongxie use provenance: live child and returned entity use response", TongxieUseProvenanceChecks.LiveFollowUpDoesNotReenterAndReturnedEntityHasIndependentUseOrResponse),
    ("Zhuihuan retaliation: cumulative payments survive damage children and replay", ZhuihuanRetaliationChecks.AccumulatedPunishmentsSurviveDamageChildrenAndReplay),
    ("Draw advice and designated extra target: frozen draw qualifications and HE payment", DrawAdviceAndDesignatedExtraTargetChecks.DrawAdviceFreezesBothQualificationsAfterRealDrawAndPaysHeOnce),
    ("Draw advice and designated extra target: Borrowed Sword compound pair", DrawAdviceAndDesignatedExtraTargetChecks.DesignationBorrowedSwordFreezesOneNewPairAndResolvesBothOwners),
    ("Gifted Slash hand policy: actual use damage reward and cold children", GiftedSlashHandPolicyChecks.GiftedNonSlashUsesFreezeDamageRewardAndColdChildren),
    ("Gifted Slash hand policy: target phase quota and continuous hand lifetime", GiftedSlashHandPolicyChecks.GiftTargetPhaseQuotaAndContinuousHandLifetimeUseRealPhasesAndTurns),
    ("Public pile cash out: damage points hand equipment and decline", PublicPileCashOutChecks.DamagePointsStoreHandOrEquipmentAndDeclineWithColdReturn),
    ("Public pile cash out: frozen payment draw and nested deposit", PublicPileCashOutChecks.PreparationCashOutFreezesAllCardsThroughDiscardDrawAndNestedNewDeposit),
    ("Current turn hand exchange: frozen foreign quantity and native HE returns", CurrentTurnHandExchangeChecks.ForeignGiftFreezesQuantityAndReturnsThroughNativeChildren),
    ("Current turn hand exchange: gift child resource exhaustion", CurrentTurnHandExchangeChecks.GiftChildExhaustionRetainsTheOriginalGivenCount),
    ("Exact owned card count and named targets: frozen payment recovery children", ExactOwnedCardCountAndNamedTargetsChecks.ExactOwnedCountFreezesPaidCostThroughRecoveryAndNamedTargets),
    ("Exact owned card count and named targets: insufficient empty effective suits and native AI", ExactOwnedCardCountAndNamedTargetsChecks.ExactOwnedCountRejectsInsufficientPaymentAndAllowsEmptyTargetsAndNativeAi),
    ("Actual target prompt retains the AI-owned attack", ActualUseTargetPauseChecks.PacedAiTargetPromptCommitsColdAndReturnsOnce),
    ("Owned hand exact rank sum actual gift and recovery return", OwnedHandRankSumChecks.ExactOwnedHandRankSumGiftsAllThirteenAndReturnsThroughChildrenOnce),
    ("Owned hand exact rank sum no solution and native AI", OwnedHandRankSumChecks.ExactOwnedHandRankSumRejectsImpossibleHandsAndNativeAiCompletesAllCards),
    ("Initial hand modifiers: owner maximum HP controls the real deal and dynamic hand limit", InitialHandModifierChecks.ActualDealAndDynamicLimitPreserveRoleAndReplay),
    ("Owner dying awakening paid HP tail returns once", OwnerLifecycleReplacementChecks.DyingAwakeningReplacesOwnerSkillsAndResumesPaidHpTailOnce),
    ("Owner dying replacement permanent source and optional ending replay", OwnerLifecycleReplacementChecks.DyingAwakeningPreservesPermanentSkillAndOptionalEndingAcrossReplay),
    ("Virtual ordinary trick composition frozen branches and public AI", VirtualOrdinaryTrickCompositionChecks.FrozenBranchesAndParticipantResourcesAreValidated),
    ("Ending Pindian win loss tie actual virtual tricks and cold return", PhaseEndingVirtualTrickChecks.EndingPindianWinLossTieUseExactVirtualTricksAndColdReturn),
    ("Trick target arbitrary hand gift empty Dismantlement and true counterspell", PhaseEndingVirtualTrickChecks.QianyaGiftEmptyDismantleAndRealNullificationReturn),
    ("Actual turn use ordinal: frozen declaration order and true uses", ActualTurnUseOrdinalChecks.FrozenDeclarationOrderCountsOnlyActualUsesAndColdRestores),
    ("Actual second use foreign grants share state and expire on end", ActualTurnTemporarySkillChecks.ActualSecondUseForeignGrantsShareStateAndExpireOnEnd),
    ("Actual legacy Wine synchronous HP and completion child return", ActualTurnLegacyAlcoholChecks.SynchronousLegacyWineWaitsForHpAndCompletionChildren),
    ("foreign ending owner and equipment costs wait once", ForeignEndingPaymentChecks.ForeignOwnerCostAndEquipmentResponseWaitOnce),
    ("Short range Slash and directed ending debt: FixedOneActualTurnZeroCostEndingPhysicalOnce", DirectedDistanceDebtChecks.FixedOneActualTurnZeroCostEndingPhysicalOnce),
    ("Short range Slash and directed ending debt: ShortRangeOneTailActualSlashAndNativeDoubleDodge", DirectedDistanceDebtChecks.ShortRangeOneTailActualSlashAndNativeDoubleDodge),
    ("Short range Slash and directed ending debt: WholeActualTurnPriorDamageAndIssuedSourceLossHostBoundary", DirectedDistanceDebtChecks.WholeActualTurnPriorDamageAndIssuedSourceLossHostBoundary),
    ("Short range Slash and directed ending debt: ShortRangeNativeRedirectCursorAndFireOrdering", DirectedDistanceDebtChecks.ShortRangeNativeRedirectCursorAndFireOrdering),
    ("Equipment Slash ownership: KuangfuOwnEquipmentRecoveryAndWholeUseCompletionReturnOnce", KuangfuChecks.KuangfuOwnEquipmentRecoveryAndWholeUseCompletionReturnOnce),
    ("Equipment Slash ownership: KuangfuOwnershipDamageMatrixAndPartialHandPenalty", KuangfuChecks.KuangfuOwnershipDamageMatrixAndPartialHandPenalty),
    ("Actual hand gains and category gifts: ActualHandGainsPreserveBatchIdentityAndExcludeSelfRecursion", ActualHandGainAndCategoryGiftChecks.ActualHandGainsPreserveBatchIdentityAndExcludeSelfRecursion),
    ("Actual hand gains and category gifts: CategoryDeckGiftsUseTrueCompletionQuotaAndPrivateTargets", ActualHandGainAndCategoryGiftChecks.CategoryDeckGiftsUseTrueCompletionQuotaAndPrivateTargets),
    ("Delegated judgment and gain gifts: actual private judgment and paid HE children", DelegatedJudgmentAndGainGiftChecks.DelegatedJudgmentPrivateChoosersAndRealHePaymentColdReplay),
    ("Delegated judgment and gain gifts: sequential red materials and frozen collections", DelegatedJudgmentAndGainGiftChecks.RedBatchRevealsOnlyEachPaidLossAndKeepsFrozenLists),
    ("JudgedRankSlashPolicy.QiangwuActualJudgmentFrozenRankDistanceAndSeparateQuota", JudgedRankSlashPolicyChecks.QiangwuActualJudgmentFrozenRankDistanceAndSeparateQuota),
    ("JudgedRankSlashPolicy.ShenxianOriginalMovedEntityResetAndActualTurnPredicateHostBoundary", JudgedRankSlashPolicyChecks.ShenxianOriginalMovedEntityResetAndActualTurnPredicateHostBoundary),
    ("skill program public pile preparation whole fill paid child", PublicPilePreparationChecks.WholePileFillRetainsPaidChild),
    ("skill program public pile preparation gift recovery original quantity", PublicPilePreparationChecks.GiftChildDrainsBeforeRequestedRecoveryAndOriginalQuantityDraw),
    ("Phase hand seizure: LihunRealCostFlipTakeAndFrozenHpReturnColdReplay", PhaseHandSeizureChecks.LihunRealCostFlipTakeAndFrozenHpReturnColdReplay),
    ("Phase hand seizure: BiyueCurrentHandBranchesSingleDrawInstruction", PhaseHandSeizureChecks.BiyueCurrentHandBranchesSingleDrawInstruction),
    ("RecipientContestConsequencesActualBlackGiftSelfSecondAndTie", RecipientContestConsequencesChecks.ActualBlackGiftSelfSecondAndTie),
    ("RecipientContestConsequencesActualPrintedLordQualificationNativeAndPersistent", RecipientContestConsequencesChecks.ActualPrintedLordQualificationNativeAndPersistent),
    ("RecipientContestConsequencesActualOrdinaryTrickProtectionAndDelayedBoundary", RecipientContestConsequencesChecks.ActualOrdinaryTrickProtectionAndDelayedBoundary),
    ("RecipientContestConsequencesReceiptCollectionsAndLegacyGrantAbi", RecipientContestConsequencesChecks.ReceiptCollectionsAndLegacyGrantAbi),
    ("skill program source curse physical initial judgment replacement and actual turn loss", OrdinaryZhangBaoChecks.CurseIsThePhysicalInitialJudgmentBeforeGuidao),
    ("skill program source curse HP Dying parent and physical death cleanup", OrdinaryZhangBaoChecks.ActualHpDyingAndPhysicalDeathCleanupRetainTypedParents),
    ("skill program activation BooleanState nested gates and current instance isolation", ProgramActivationBooleanStateChecks.NestedGatesUseTheCurrentOwnerSkillInstance),
    ("Damage judgment suit payment four suits and exact claims", BoundaryCaiWenJiChecks.DamageJudgmentBeforePaymentFourSuitsAndExactClaims),
    ("Damage judgment suit payment independent matches and foreign claim", BoundaryCaiWenJiChecks.IndependentSuitRankMatchesAndPreviouslyClaimedJudgment),
    ("Random reveal color Fire Attack HE payment and children cold", BoundaryWolongZhugeLiangChecks.RandomRevealColorHePaymentAndChildrenCold),
    ("Actual turn trick history Cangzhuo discard cold", BoundaryWolongZhugeLiangChecks.ActualTurnTrickHistoryAndCangzhuoDiscardCold),
    ("Actual turn use kinds per Play draw child and actual turn expiry", BoundaryGuoHuaiChecks.EachPlayEndingDrawUsesActualTurnHistoryAndReturnsFromGainChild),
    ("Actual turn use kinds conversion virtual use and qualified sources", BoundaryGuoHuaiChecks.EffectiveConversionsLegacyVirtualUsesAndLostSourcesKeepExactBoundaries),
    ("Discarded provenance virtual Alcohol face children and zero entity", BoundaryCaoZhiChecks.VirtualAlcoholWaitsForFaceChildrenAndUsesNoPhysicalEntity),
    ("Discarded provenance real turn all materials no response", BoundaryCaoZhiChecks.ProvenanceSurvivesRealTurnsAndAllMaterialUsesFreezeNoResponse),
    ("Granted entity extra phase distance claim children and cold", BoundaryLiaoHuaChecks.ExtraPhaseEntityDistanceAndClaimChildrenAreExact),
    ("Frozen faction recovery game damage points benefit children", BoundaryLiaoHuaChecks.FuliFrozenFactionsDamagePointsAndBenefitChildren),
    ("Next actual-use paid wins refresh one target token and original claims", BoundaryJianYongChecks.NextActualUsePaidWinsRefreshSingleTargetTokenAndClaimOriginalCards),
    ("Next actual-use failure preserves Zongshi and prohibits tricks before payment", BoundaryJianYongChecks.NextActualUseFailureKeepsZongshiAndBlocksTricksBeforePayment),
    ("Original target addition rights quota armor and cold", BoundaryGaoShunChecks.OriginalTargetAdditionQuotaArmorAndCold),
    ("Foreign turn Wine actual actor physical virtual and pile dying", BoundaryGaoShunChecks.ForeignDyingAlcoholPhysicalVirtualAndPileBoundaries),
    ("Neighbor discard own first previous actual entities order privacy", BoundaryYuFanChecks.ActualOwnAndFirstPreviousDiscardKeepEntitiesOrderAndPrivacy),
    ("Neighbor Ending revealed equipment non-equipment HP typed children", BoundaryYuFanChecks.RevealedEquipmentAndNonEquipmentHpChildrenResumeOnce),
    ("boundary cheng pu completion payment recovery and dying", BoundaryChengPuChecks.CompletionPaymentWaitsForSilverLionAndFatalHpChildren),
    ("boundary cheng pu round priced actual alcohol", BoundaryChengPuChecks.RoundPricedAlcoholKeepsAtomicMaterialsAndVictimUseReturns),
    ("boundary wu guo tai paid equipment pair cost and typed return", BoundaryWuGuoTaiChecks.PaidEquipmentPairFreezesCostAndSwapsAfterChildren),
    ("boundary wu guo tai opaque dying basic selection", BoundaryWuGuoTaiChecks.DyingOwnedSelectionStaysOpaqueAndBasicHasNoPayment),
    ("Paid actual target HP and original use return", BoundaryWangYiChecks.PaidOtherUseNullificationOpaqueObtainAndActualSlash),
    ("Earned actual Ending and lost HP gift budget", BoundaryWangYiChecks.IndependentActualEndingPromisesFrozenXAndOptionalHeGifts),
    ("public equipment half count draw resumes once", EquipmentHalfDrawChecks.PublicEquipmentCountDrawsOnceAndResumes),
    ("boundary li ru sequential discard top cost and typed child return", BoundaryLiRuChecks.SequentialAnyDiscardKeepsTopPaymentAndChildReturns),
    ("boundary li ru chosen start actual discard fire and dying return", BoundaryLiRuChecks.ChosenStartEscalationUsesActualDiscardAndFireReturn),
    ("Whole equipment donation actual X and recovery", BoundaryCaiFuRenChecks.WholeEquipmentDonationActualXRecoveryAndColdChildren),
    ("Original actual ended turn equipment and draw tiers", BoundaryCaiFuRenChecks.OriginalActualTurnTiersDistinctEquipmentAndDrawOptions),
    ("Actual use target history response exclusion and native", BoundaryCaiFuRenChecks.ActualUseResponseExclusionLegacyNativeAndStrictComposition),
    ("boundary xiao qiao suit prevention equipment cost and capped draw", BoundaryXiaoQiaoChecks.TianxiangEquipmentPaymentWaitsForRecoveryBeforeNewDamageAndCappedDraw),
    ("boundary xiao qiao heart judgment exact placement and native prevention", BoundaryXiaoQiaoChecks.PiaolingOriginalHeartJudgmentTopGiftSelfCostAndNativePrevention),
    ("boundary lu su extra draw half hand gift and cold children", BoundaryLuSuChecks.ExtraDrawAndHalfGiftOwnTheirGainChildren),
    ("boundary lu su exchange first frozen ending silver lion debt", BoundaryLuSuChecks.ExchangeFirstThenFrozenDebtPaysAfterSilverLionChildren),
    ("Real color payment Duel back-damage and mandatory Ending claim", BoundaryYanLiangWenChouChecks.RealColorPaymentDuelBackDamageAndMandatoryEndingClaim),
    ("Multiple original materials already moved and empty virtual responses", BoundaryYanLiangWenChouChecks.MultipleOriginalMaterialsAlreadyMovedAndEmptyVirtualResponses),
    ("boundary two zhangs equipment replacement paid children and cold draw", BoundaryZhangZhaoZhangHongChecks.CapturedEquipmentReplacementDrainsSourceAndRecipientChildrenBeforeDraw),
    ("boundary two zhangs return claim phase quota and damage dying cold", BoundaryZhangZhaoZhangHongChecks.GuzhengReturnsOneThenOptionallyClaimsExactBatchAndKeepsPhaseQuota),
    ("boundary bu lian shi ordered opaque pair gift and reward", BoundaryBuLianShiChecks.OrderedOpaquePairPaysBeforePublicGiftAndReward),
    ("boundary bu lian shi ending issued recipient death repeat and cold", BoundaryBuLianShiChecks.EndingIssuanceAndOwnerDeathRepeatOnlyOriginalRecipient),
    ("Foreign actual turn contest decline win expiry", BoundaryFuHuangHouChecks.ForeignActualTurnWinDeclineAndExpiry),
    ("Actual use aid damage trick legacy native contracts", BoundaryFuHuangHouChecks.DamageTrickGroupPrivateGiftLegacyVirtualAndNativeContracts),
    ("boundary ma dai shown color hand ban and equipment response", BoundaryMaDaiChecks.ShownColorBansHandMaterialsButKeepsEquipmentResponses),
    ("boundary ma dai full materials damage benefit and cold return", BoundaryMaDaiChecks.ShownEntityDamageFreezesFullMultiMaterialUseAndColdReturn),
    ("Ending pair two draws own extra round and cold", BoundaryXiahouShiChecks.OwnAndExtraEndingKeepTwoSeparateDrawsAndColdChildren),
    ("Actual play Slash use recast loss and cleanup", BoundaryXiahouShiChecks.UseAndRecastCountRealLossAndIgnoreProcessingCleanup),
    ("Preparation discard actual cost", BoundaryYuJinChecks.ActualPreparationOpaqueDiscardCostAndZeroEquipmentCount),
    ("Compared black Slash actual target", BoundaryYuJinChecks.FrozenBlackSlashHpHandArmorAndMixedPhysicalPayments),
    ("Yang Xiu multi-target trick owns draw children and unique Borrowed Sword is excluded", OrdinaryYangXiuChecks.ActualMultiTargetTrickWaitsForDrawAndUniqueBorrowedHolderDoesNotOffer),
    ("Yang Xiu categories block own hand uses responses and costs without protecting foreign discards", OrdinaryYangXiuChecks.DeclaredCategoryBlocksOwnHandMaterialsButForeignDiscardAndEquipmentRemainLegal),
    ("SP Ma Chao HP ordered distance and lost HP extra targets pay each target once", OrdinarySpMaChaoChecks.HpOrderedDistanceAndLostHpExtraTargetsPayEachTargetOnce),
    ("SP Ma Chao equipment recast drains Silver Lion and paid source loss before draw", OrdinarySpMaChaoChecks.EquipmentRecastDrainsSilverLionAndPaidSourceLossBeforeRealDraw),
    ("SP Ma Chao legacy Slash and target windows keep one visit per target", OrdinarySpMaChaoChecks.LegacySlashAndExistingTargetWindowsKeepOneVisitPerOriginalTarget),
    ("Ol Huang Zu wangong unlocks after each basic play", OrdinaryHuangZuChecks.WangongUnlocksAfterEachBasicPlay),
    ("Ol Cao Xing liushi damages and zhanwan drains", OrdinaryCaoXingChecks.LiushiDamagesAndZhanwanDrains),
    ("Ol Xun Chen fenglve contests and moushi marks", OrdinaryXunChenChecks.FenglveContestsAndMoushiMarks),
    ("Ol Xun Chen moushi gift marks drains and expires", OrdinaryXunChenChecks.MoushiGiftMarksDrainsAndExpires),
    ("Ol Deng Zhong kanpo takes the matching suit card", OrdinaryDengZhongChecks.KanpoTakesMatchingSuitCard),
    ("Ol Deng Zhong gengzhan tally grants the slash limit", OrdinaryDengZhongChecks.GengzhanTallyGrantsSlashLimit),
    ("Ol Liang Xing luelve choice and zhuanxi amplification", OrdinaryLiangXingChecks.LuelveChoiceResolvesAndZhuanxiAmplifies),
    ("Ol Gao Lan xizhen duel damages and marks", OrdinaryGaoLanChecks.XizhenDuelDamagesAndMarks),
    ("Ol Gao Lan xizhen slash benefit pays after settlement", OrdinaryGaoLanChecks.XizhenSlashBenefitPaysAfterSettlement),
    ("Ol Zhang Changpu yanjiao splits equal rank sums", OrdinaryZhangChangPuChecks.YanjiaoSplitsEqualRankSums),
    ("Ol Zhang Changpu shengshen feeds and consumes yanjiao reveal", OrdinaryZhangChangPuChecks.ShengshenFeedsAndConsumesYanjiaoReveal),
    ("Ol Lü Kai tunan converts dodge into slash", OrdinaryLvKaiChecks.TunanConvertsDodgeIntoSlash),
    ("Ol Lü Kai bijing punishes lost marked card", OrdinaryLvKaiChecks.BijingPunishesLostMarkedCard),
    ("Ol Zhou Fang duanfa recycles black cards in phase ledger steps", OrdinaryZhouFangChecks.DuanfaRecyclesBlackCardsInPhaseLedgerSteps),
    ("Ol Zhou Fang youdi baits black non-slash and pays both benefits", OrdinaryZhouFangChecks.YoudiBaitsBlackNonSlashAndPaysBothBenefits),
    ("Ol Pan Jun guanwei observes another play phase", OrdinaryPanJunChecks.GuanweiObservesAnotherPlayPhase),
    ("Ol Pan Jun gongqing boosts above three range", OrdinaryPanJunChecks.GongqingBoostsAboveThreeRange),
    ("Ol Yi Ji jijie hands deck bottom to selected participant", OrdinaryYiJiChecks.JijieHandsDeckBottomToSelectedParticipant),
    ("Ol Yi Ji jiyuan covers dying entries and own gifts", OrdinaryYiJiChecks.JiyuanCoversDyingEntriesAndOwnGifts),
    ("Ordinary Cao Hong real placement replacement and armor draw", OrdinaryCaoHongChecks.RealPlacementReplacementAndArmorDraw),
    ("Ordinary Cao Hong directed recipient distance and opaque HEJ", OrdinaryCaoHongChecks.DirectedRecipientDistanceOpaqueHejAndEquipmentSource),
    ("Ordinary Guan Yin Ping red equipment cost before lost HP targets and real fire draw", OrdinaryGuanYinPingChecks.RedEquipmentCostFinishesBeforeLostHpTargetsAndRealFireDraw),
    ("Ordinary Guan Yin Ping awakening original weapon and paid gain source loss return", OrdinaryGuanYinPingChecks.AwakeningGetsOriginalPublicWeaponAndDrainsPaidGainAfterSourceLoss),
    ("ordinary OL Zhuge Ke empty-hand Dodge private top request and once payment", OrdinaryZhugeKeChecks.EmptyHandDodgeViewsFourPrivatelyAndPaysOriginalResponseOnce),
    ("ordinary OL Zhuge Ke frozen SilverLion discard damage and exact survivor penalty", OrdinaryZhugeKeChecks.FrozenSilverLionCostDrainsChildrenAndSurvivorPenaltyDisablesCurrentTurn),
    ("Ordinary SP Cao Ren private whole hand after HP children and real discard", OrdinarySpCaoRenChecks.PrivateWholeHandAfterHpChildrenAndRealDiscard),
    ("Ordinary SP Cao Ren Lizhan current wounded draw native and strict composition", OrdinarySpCaoRenChecks.LizhanCurrentWoundedDrawNativeAndStrictComposition),
    ("Ordinary Chen Lin deferred private offer next actual start exchange", OrdinaryChenLinChecks.DeferredPrivateOfferTargetsNextActualStartAndExchangeReturnsOnce),
    ("Ordinary Chen Lin equipment discard recovery paid source loss", OrdinaryChenLinChecks.GameTargetHandHpEquipmentDiscardDrainsRecoveryAfterSourceLoss),
    ("Ordinary SP Cai Wen Ji original Dying four suits private cost cold replay", DyingSuitsAndEndingHistoryChecks.ChenqingOriginalDyingFourSuitsPrivateCostAndColdReplay),
    ("Ordinary SP Cai Wen Ji historical first completion second multi Slash skip", DyingSuitsAndEndingHistoryChecks.MoshiFirstUseCompletionBeforeSecondMultiSlashAndSkip),
    ("Ordinary SP Pang De two real costs and issued Duel typed return", OrdinarySpPangDeChecks.TwoRealCostsAndIssuedDuelTypedReturn),
    ("Ordinary SP Pang De Slash identity and HP after payment children", OrdinarySpPangDeChecks.PrintedSlashIdentityAndHpAfterPaymentChildren),
    ("Ordinary Fu Wan same-use benefit and real Dodge settlement", OrdinaryFuWanChecks.RealBenefitAndDodgeCancellationPayOriginalEntitiesOnce),
    ("Ordinary Fu Wan legacy virtual Slash and Bagua cancellation", OrdinaryFuWanChecks.LegacyVirtualSlashAndBaguaUseExactCancellationProducer),
    ("Ordinary Liu Xie Tianming SilverLion cost and unique current HP peer", OrdinaryLiuXieChecks.TianmingSilverLionPaymentAndUniqueCurrentHpPeerOwnColdChildren),
    ("Ordinary Liu Xie Mizhao atomic gift foreign Pindian winner Slash", OrdinaryLiuXieChecks.MizhaoAtomicGiftForeignPindianWinnerSlashAndColdReturn),
    ("OL signed damage actual material effective color and amount", OrdinaryLingJuChecks.PaidDamageAdjustmentUsesEffectiveOwnedColorAndActualAmount),
    ("OL public dead role costs and prior death acquisition", OrdinaryLingJuChecks.PublicDeadRoleRelaxationsIncludeDeathsBeforeSkillAcquisition),
    ("Tiered Round true uses physical conversion and private cold", BoundaryGuoHuangHouChecks.TrueRoundNamesPhysicalConversionAndPrivateCold),
    ("Tiered Round zero counterspell Dodge and dying returns", BoundaryGuoHuangHouChecks.ZeroCounterspellDodgeAndDyingTypedReturns),
    ("Owner batch loss equipment OR two cards once", BoundaryLingTongChecks.OwnerBatchEquipmentOrTwoCardsCreatesOneOpportunity),
    ("Owner batch loss private paid discards children source loss", BoundaryLingTongChecks.PrivateSequentialChoicesRecoveryChildrenDeclinesAndSourceLoss),
    ("Shared Slash gift equipment recovery both gain children cold", BoundaryChenGongChecks.SharedGiftEquipmentRecoveryAndBothGainChildrenCold),
    ("Shared Slash actual damage rescue Completed source loss cold", BoundaryChenGongChecks.VirtualSlashActualDamageRescueCompletedAndCommittedSourceLossCold),
    ("Turn damage-use draw debt actual gain and frozen ending cost", BoundaryLiuBiaoChecks.ExtraDrawOwnsGainChildAndEndingDebtFreezesNewFactionCount),
    ("Source faction prevention quota opaque HEJ equipment children", BoundaryLiuBiaoChecks.SourceFactionQuotaOpaqueHeJAndEquipmentChildrenAreExact),
    ("Dual private color commitments atomic payment real Duel and cold", BoundaryZhangChunHuaChecks.PrivateCommitmentsAtomicPaymentAndRealDuelCold),
    ("Dual private color opponent actor and paid source death typed return cold", BoundaryZhangChunHuaChecks.OpponentActorAndPaidSourceDeathReturnCold),
    ("Actual turn target commitment damage weapon shared quota", BoundaryDianWeiChecks.RealDamageWeaponCostSharedQuotaAndActualTurnTargets),
    ("Applied damage occurrence frozen victim and real source", BoundaryDianWeiChecks.SecondVictimOccurrenceCountsDamageEventsAndActualParticipants),
    ("Owned damage point judgment actual alcohol suppression expiry", BoundaryDongZhuoChecks.UnlimitedAlcoholActualConsumptionAndTurnExpiry),
    ("Owned damage point judgment per point recovery claim children native", BoundaryDongZhuoChecks.PerPointOwnedJudgmentRecoveryClaimChildrenAndNativeAi),
    ("Owned damage point judgment claimed gain bound Alcohol rescue and cold replay", OwnedJudgmentBoundAlcoholChecks.ClaimedJudgmentGainDyingBoundAlcoholReturnsAndColdReplays),
    ("Black trick target policy actual color and lightning", BoundaryJiaXuChecks.BlackTricksUseActualColorAndLightningTransferContext),
    ("Nearest legal Slash paid conversion provider optional finisher", BoundaryJiaXuChecks.ChaoticAmbitionPaysConvertedAndProviderCardsBeforeOptionalFinisher),
    ("Actual draw obligation distance whole turn paid equipment", BoundaryXuHuangChecks.DuanliangDistanceTracksActualWholeTurnDamageAndPaidEquipment),
    ("Actual draw obligation least hand marker end observers", BoundaryXuHuangChecks.LeastHandMarkerWaitsForEndObserversThenRunsRealExtraDraw),
    ("Phase hand extra turn native public declaration paid selected beneficiary", PhaseHandExtraTurnAiChecks.NativePublicDeclarationPaysAndExecutesSelectedBeneficiaryExtraTurn),
    ("Phase hand extra turn owner only native legacy value", PhaseHandExtraTurnAiChecks.OwnerOnlyNativeExtraTurnKeepsLegacyTwentyValue),
    ("Next-use rank bonus per point real consumption public pool and cold replay", BoundaryCaoChongChecks.PerPointBonusConsumptionPublicPoolAndColdReplay),
    ("Other dying recovery equipment payment turnover exact victim and cold replay", BoundaryCaoChongChecks.EquipmentPaymentBeforeTurnOverAndExactDyingRecovery),
    ("Alternative phase cost actual Judgment Draw zero to two targets cold", BoundaryZhangHeChecks.JudgmentAndDrawSubstitutionPayAtTheirActualBoundaries),
    ("Ending hand count history Extra turns face down native AI", BoundaryZhangHeChecks.EndingHandCountHistoryComparesBeforeInsertAcrossExtraTurnsAndNativeAi),
    ("Participant hand actual requested Slash native AI paid gift", BoundaryFaZhengChecks.XuanhuoRealSlashAndNativeAiRetainPaidGift),
    ("Participant hand per point effective red and HP loss damage cursor", BoundaryFaZhengChecks.EnyuanPerPointEffectiveRedAndHpLossKeepDamageCursor),
    ("Participant hand program bound alcohol rescue typed return and cold", ParticipantHandProgramRescueChecks.BoundAlcoholPaidDamageReturnAndColdReplay),
    ("Current Slash entity claim basic payment original damage and multiple materials", BoundaryPangDeChecks.BasicDiscardClaimsExactEntitiesAndStillResolvesOriginalSlash),
    ("Current Slash entity claim nonbasic receipt real allowance and armor boundaries", BoundaryPangDeChecks.NonBasicDiscardUsesOneTargetReceiptAndAddsRealTurnAllowance),
    ("Bound discard Slash frozen suit quota prepared privacy and expiry", BoundaryHanDangChecks.FrozenSuitQuotaPreparedPrivacyAndExpiry),
    ("First round limited refund exact usage and second round consumption", BoundaryHanDangChecks.FirstRoundExactUsageRefundAndSecondRoundConsumption),
    ("BoundaryXuShu current rules awakening cold replay", BoundaryXuShuChecks.RunCurrentRulesAndAwakening),
    ("Recovery replacement Zhiheng selection and phase quota", BoundarySunQuanChecks.ZhihengSelectionAndPhaseQuota),
    ("Character turned over Shensu three branches and hand equipment payment", BoundaryXiahouYuanChecks.ThreeShensuBranchesAndHandEquipmentCost),
    ("Character turned over Shebian both directions movement children cold replay", BoundaryXiahouYuanChecks.ShebianBothDirectionsMovementChildrenAndReplay),
    ("Character turned over natural skipped turn and old window compatibility", BoundaryXiahouYuanChecks.NaturalFaceUpSkippedTurnAndOldWindowCompatibility),
    ("Turn kind hand exemption survives source suppression actual turn expiry", TurnHandLimitKindExemptionChecks.IssuedKindPolicySurvivesSourceSuppressionUntilActualTurnEnd),
    ("Signed Slash modifier parser and prepared collection boundaries", TurnHandLimitKindExemptionChecks.SignedSlashParserAndPreparedCollectionBoundaries),
    ("Random Pindian both strategies exact RNG tied Slash claims cold replay", BoundaryTaiShiCiChecks.BothRandomStrategiesFreezeReceiptsAndClaimTiedSlashes),
    ("Maximum Slash Pindian claim effective ranks deferred top cold replay", BoundaryTaiShiCiChecks.MaximumSlashClaimUsesEffectiveRanksAndPreservesDeferredTop),
    ("Bound card recast draw mode real Slash response cold replay", BoundaryCaoZhangChecks.DrawMoreReducesUseLimitButKeepsRealSlashResponse),
    ("Bound card recast hand cost and reward children two distant Slashes", BoundaryCaoZhangChecks.HandRecastWaitsForCostAndDrawChildrenBeforeTwoDistantSlashes),
    ("Capped hand refresh real damage private discard and cold replay", BoundaryXunYuChecks.RunCappedHandRefreshAndColdReplay),
    ("Capped hand refresh dead owner exact parent and cold replay", BoundaryXunYuChecks.RunDeathHandRefreshAndColdReplay),
    ("HP paid shield source death full damage prevention and once payment", BoundarySunJianChecks.WuliePaysOnceAndPreventsWholeDamageAfterSourceDeath),
    ("HP paid shield lethal payment exact dying return cold replay", BoundarySunJianChecks.WulieLethalPaymentReturnsAfterDying),
    ("Recovery replacement program amount and recover-to cold replay", BoundarySunQuanChecks.ProgramRecoveryAndRecoverToReplacement),
    ("Recovery replacement paid equipment tail Qixi and replaced ID", BoundarySunQuanChecks.SilverLionPaidQixiAndEquipmentReplacement),
    ("Recovery replacement paid Qixi stops after observer victory", RecoveryPaidWinnerChecks.SilverLionPaidQixiStopsAfterObserverVictory),
    ("Recovery replacement faction request Silver Lion cost before providers", RecoveryPaidWinnerChecks.QinwangSilverLionCostPrecedesProviders),
    ("BoundaryXuShu declared deck criterion male give", BoundaryXuShuChecks.RunDeclaredDeckCriterion),
    ("Program Iron Chain third target owned recast cold replay", BoundaryPangTongChecks.LianhuanAddsThirdTargetAndRecastsOwnedEquipment),
    ("Program self dying restore chosen skill limited nested replay", BoundaryPangTongChecks.NiepanRestoresStateChoosesSkillAndStaysLimited),
    ("boundary Ma Chao Tieqi Paoxiao exact cancellation integration", BoundaryMaChaoChecks.TieqiPaoxiaoTrueDodgeAndNextUseReplay),
    ("boundary Ma Chao suit payment decline privacy replay", BoundaryMaChaoChecks.SuitsPaymentDeclinePrivacyReplay),
    ("boundary Ma Chao judgment dying target loss and legacy", BoundaryMaChaoChecks.JudgmentDyingTargetLossAndLegacy),
    ("program conversion polarity shared state and replay", ProgramConversionPolarityChecks.SharedStateAndReplay),
    ("Feng Lin Wang Ji actual targets private nested payment", FengLinWangJiChecks.ActualTargetsPrivateAndNested),
    ("Feng Lin Wang Ji named count ending and reset", FengLinWangJiChecks.NamedCountEndingAndReset),
    ("Population reorder actual partition and short pile", BoundaryZhugeLiangChecks.PopulationPartitionAndShortPile),
    ("Population reorder parser legacy formal AI", BoundaryZhugeLiangChecks.ParserLegacyAndNativeAi),
    ("Exact top count definition legacy null", BoundaryLiDianChecks.DefinitionAndLegacy),
    ("Unconverted Trick actual use qualification", BoundaryHuangYueyingChecks.ActualUseQualification),
    ("Foreign equipment discard program selections", BoundaryHuangYueyingChecks.ForeignProgramSelections),
    ("Damage target obtain HEJ printed category privacy", BoundaryLuBuChecks.HejAndPrintedCategory),
    ("Damage target obtain victim chooser legality", BoundaryLuBuChecks.VictimChooserAndLegality),
    ("Damage target mount real HEJ privacy inputs", BoundaryGongsunZanChecks.RealZonesAndInputs),
    ("Damage target mount real damage provenance", BoundaryGongsunZanChecks.RealDamageProvenance),
    ("Recorded suit Slash: recording reveal damage cold replay", RecordedSuitSlashChecks.RecordingRevealDamageAndReplay),
    ("Recorded suit Slash: target and use-only response suits", RecordedSuitSlashChecks.TargetAndResponseRecording),
    ("Final target Slash frozen comparisons actual paid Dodge", BoundaryHuangZhongChecks.ComparisonsAndActualDodge),
    ("Final target Slash escalation: judgment quota and Silver Lion", FinalTargetSlashEscalationChecks.ExactTargetJudgmentQuotaAndSilverLion),
    ("Final target Slash multiple targets damage boundaries", BoundaryHuangZhongChecks.MultipleTargetsAndDamageBoundaries),

    ("Boundary Yuan Shao population start role marker limit", BoundaryYuanShaoChecks.StartingPopulationAndQualification),
    ("Boundary Yuan Shao zero targets native compatibility", BoundaryYuanShaoChecks.ZeroTargetsNativeAndCompatibility),
    ("Selected actor Duel actual Use and Nullification", BoundaryHuaXiongChecks.ActualDuelAndNullification),
    ("Selected actor Duel responses and target legality", BoundaryHuaXiongChecks.ResponsesAndLegality),
    ("Paid HP loss quantity and native", BoundaryWeiYanChecks.QuantityAndNative),
    ("Paid HP loss source and legacy", BoundaryWeiYanChecks.SourceAndLegacy),
    ("Boundary Deng Ai own turn slash discard judges authority", BoundaryDengAiChecks.OwnTurnSlashDiscardJudgesIntoAuthority),
    ("Boundary Deng Ai awakening grants snatch and extra turn", BoundaryDengAiChecks.AwakeningGrantsSnatchAndExtraTurn),
    ("Boundary Jiang Wei damaging slash suppresses discard", BoundaryJiangWeiChecks.TauntDamagingSlashSuppressesDiscard),
    ("Boundary Jiang Wei awakening wakes once across phases", BoundaryJiangWeiChecks.ZhijiWakesOnceAcrossPrepareAndEndPhases),
    ("Boundary Sun Ce reclaim once per turn", BoundarySunCeChecks.JiangReclaimsDiscardOncePerTurn),
    ("Boundary Sun Ce hunzi wakes draws and stays silent", BoundarySunCeChecks.HunziWakesDrawsThenStaysSilent),
    ("Boundary Sun Ce zhiba targets wu and claims both on tie", BoundarySunCeChecks.ZhibaTargetsWuOnlyAndClaimsBothOnTie),
    ("Boundary Zhu Rong juxiang immunity claims resolved barbarian", BoundaryZhuRongChecks.JuxiangImmuneAndClaimsResolvedBarbarian),
    ("Boundary Zhu Rong winning pindian takes target card", BoundaryZhuRongChecks.LierenWinningPindianTakesTargetCard),
    ("Boundary Zhu Rong unlimited slash damage draws at phase end", BoundaryZhuRongChecks.ChangbiaoUnlimitedSlashDamageDrawsAtPhaseEnd),
    ("Boundary Meng Huo barbarian immunity and attribution", BoundaryMengHuoChecks.HuoshouImmuneAndAttributesBarbarianDamage),
    ("Boundary Meng Huo zaiqi asks chosen participants in order", BoundaryMengHuoChecks.ZaiqiAsksChosenParticipantsInOrder),
    ("Boundary Guan Xing Zhang Bao converted slash response follows pair color", BoundaryGuanXingZhangBaoChecks.ConvertedSlashResponseFollowsPairColor),
    ("Boundary Guan Xing Zhang Bao granted boundary distance and next Slash damage", BoundaryGuanXingZhangBaoChecks.GrantedBoundarySkillsApplyDistanceAndNextSlashDamage),
    ("Boundary Zhou Tai buqu rescue wound hand limit and duplicate rank", BoundaryZhouTaiChecks.BuquRescueWoundHandLimitAndDuplicateRank),
    ("Boundary Zhou Tai fenji covers own discards and third party gains", BoundaryZhouTaiChecks.FenjiCoversOwnDiscardsAndThirdPartyGains),
    ("Boundary Cao Ren jushou flips draws and discards hand card", BoundaryCaoRenChecks.JushouFlipsDrawsAndDiscardsHandCard),
    ("Boundary Cao Ren jiewei moves field equipment after turning face up", BoundaryCaoRenChecks.JieweiMovesFieldEquipmentAfterTurningFaceUp),
    ("Equipment placement actual replacement Xiaoji transfer", BoundarySunShangxiangChecks.EquipmentAndReplacement),
    ("HP pair freezes after payment children before draw children", BoundarySunShangxiangChecks.PairFreezesAcrossChildren),
    ("Bound draw receipt per point nested gain", BoundaryLiDianChecks.ReceiptAndNestedGain),
    ("Boundary Zhang Fei cancellation next real Use freeze", BoundaryZhangFeiChecks.CancellationAndUseFreeze),
    ("Boundary Zhang Fei actual recovery receipt limited child", BoundaryZhangFeiChecks.RecoveryReceiptAndLimited),
    ("Feng Lin Kuai Jianxiang actual finalized minimum tie", FengLinKuaiChecks.JianxiangActualFinalTargetMinimumTie),
    ("Feng Lin Kuai Yang gift private Yin replay", FengLinKuaiChecks.YangGiftAndPrivateYinReplay),
    ("participant discard mandatory cost cancellation preserves legal composition", BoundaryHuaTuoChecks.MandatoryPaymentCancellationPreservesLegalComposition),
    ("boundary Hua Tuo actual Jijiu HE outside turn own turn negative", BoundaryHuaTuoChecks.JijiuEquipmentRealOutsideTurnAndOwnTurnNegative),
    ("boundary Hua Tuo ordered actual discard effective suit receipts", BoundaryHuaTuoChecks.OrderedActualDiscardsAndEffectiveSuitReceipts),
    ("boundary Lu Xun HE payment exact Hand batch count", BoundaryLuXunChecks.OwnedHePaymentCountsActualHandBatch),
    ("boundary Lu Xun actual trick hold lianying and return", BoundaryLuXunChecks.ActualTrickHoldLianyingAndReturn),
    ("Feng Lin Zuo Ci old registry no library shape", FengLinZuoCiChecks.OldRegistryHasNoLibraryShape),
    ("Feng Lin Zuo Ci initial privacy and replay", FengLinZuoCiChecks.InitialPrivacyAndReplay),
    ("Feng Lin Zuo Ci layered suppression and local disable", FengLinZuoCiChecks.LayeredSuppressionAndLocalDisable),
    ("Feng Lin Zuo Ci same avatar program and actual end", FengLinZuoCiChecks.SameAvatarProgramAndActualEnd),
    ("Feng Lin Zhou Fei first domains and true equipment sequence", FengLinZhouFeiChecks.DomainFirstAndEquipmentSequence),
    ("Feng Lin Zhou Fei same-source association freeze", FengLinZhouFeiChecks.SameSourceMultiplePilesFreezeAssociation),
    ("Feng Lin Lu Zhi strict resources legacy color ABI",FengLinLuZhiChecks.StrictResourcesAndLegacyColorAbi),
    ("Feng Lin Lu Zhi actual discard entry boundaries",FengLinLuZhiChecks.ActualDiscardEntryBoundaries),
    ("Feng Lin Xu You bottom draw and alternating replay", FengLinXuYouChecks.BottomDrawAndAlternatingReplay),
    ("Feng Lin Xu You phase suit distance and expiry", FengLinXuYouChecks.PhaseSuitDistanceAndExpiry),
    ("shared public pile independent source thresholds", FengLinGuanqiuJianChecks.IndependentSourceThresholds),
    ("Feng Lin Guanqiu Jian awakening zero equal hand", FengLinGuanqiuJianChecks.AwakeningZeroAndEqualHandExchange),
    ("Feng Lin Zhuge Zhan private quota and top replay", FengLinZhugeZhanChecks.PrivateQuotaAndTopReplay),
    ("Feng Lin Zhuge Zhan first shared target and actual AI", FengLinZhugeZhanChecks.FirstSharedTargetAndAi),
    ("FengLin Lu Kang live chain delayed Pindian policies", FengLinLuKangChecks.ChainAndTargetPolicies),
    ("FengLin Lu Kang slot cost nested movement", FengLinLuKangChecks.SlotCostNestedMovement),
    ("FengLin Lu Kang preparation awakening turn grant", FengLinLuKangChecks.PreparationAwakeningAndTurnGrant),
    ("FengLin Chen Dao first use issued phase ban", FengLinChenDaoChecks.FirstUseAndIssuedPhaseBan),
    ("FengLin Chen Dao genuine response families", FengLinChenDaoChecks.TrueResponseFamilies),
    ("Feng Lin Sun Liang discard budget draw damage", FengLinSunLiangChecks.DiscardBudgetDrawAndDamage),
    ("Feng Lin Sun Liang completed cost provider lord", FengLinSunLiangChecks.CompletedCostProviderAndLordReward),
    ("Feng Lin Lu Ji actual marker payment and damage", FengLinLuJiChecks.PhysicalStartGiftPrevention),
    ("Feng Lin Lu Ji replacement draw and dying payment", FengLinLuJiChecks.ReplacementDrawAndDyingPayment),
    ("boundary Liu Bei virtual Peach HP child", BoundaryLiuBeiChecks.VirtualPeachHpChild),
    ("boundary Liu Bei origin legacy mechanism", BoundaryLiuBeiChecks.OriginAndLegacyMechanism),
    ("boundary Liu Bei gift threshold recipient", BoundaryLiuBeiChecks.GiftThresholdAndRecipient),
    ("boundary Da Qiao placement refill before delayed Nullification legacy JSON", BoundaryDaQiaoChecks.CompletedPlacementDrawsBeforeDelayedNullification),
    ("boundary Da Qiao atomic mixed discard replay", BoundaryDaQiaoChecks.AtomicMixedDiscardReplay),
    ("boundary Lu Meng all table printed suits replay", BoundaryLuMengChecks.AllTablePrintedSuitsAndReplay),
    ("boundary Lu Meng cross owner skip chain resume death", BoundaryLuMengChecks.CrossOwnerSkipChainAndResumeDeath),
    ("boundary Lu Meng Qinxue windows shared usage", BoundaryLuMengChecks.QinxueWindowsAndSharedUsage),
    ("boundary Lu Meng opt in usage legacy shape", BoundaryLuMengChecks.OptInUsageCatalogAndLegacyShape),
    ("after turn ended return movement prelude ordering", AfterTurnEndedChecks.ReturnMovementPreludeBeforeWindow),
    ("after turn ended due before window nested replay", AfterTurnEndedChecks.DueBeforeWindowAndNestedReplay),
    ("after turn ended old registry exact parent", AfterTurnEndedChecks.OldRegistryShapeAndExactParent),
    ("FengLin Hao Zhao two dynamic actual ends native discard", FengLinHaoZhaoChecks.DynamicTwoEndsAndNativeDiscard),
    ("FengLin Hao Zhao ordered nested native owner", FengLinHaoZhaoChecks.OrderedNestedAndNativeOwner),
    ("Feng Lin Wang Ping paid hand frozen qualification", FengLinWangPingChecks.PaidHandDemandAndFrozenQualification),
    ("Feng Lin Wang Ping first target history independent reward", FengLinWangPingChecks.FirstTargetHistoryAcrossSkillLossAndIndependentReward),
    ("Fame 2017 Xue Zong real response entity exchange", Fame2017XueZongChecks.SlashDuelAndCounterspellEntityExchange),
    ("Fame 2017 Xue Zong escalating discard upgrade", Fame2017XueZongChecks.EscalatingDiscardZeroAndUpgrade),
    ("Fame 2017 Ji Kang real equipment nested replay", Fame2017JiKangChecks.RealRandomEquipReplacementNestedReplay),
    ("Fame 2017 Ji Kang native death bequest shield expiry", Fame2017JiKangChecks.NativeDeathBequestShieldAndExpiry),
    ("Fame 2017 Cao Jie real named actor cost privacy nested replay", Fame2017CaoJieChecks.NamedDefenseActorPaymentAndPrivateTake),
    ("Fame 2017 Cao Jie frozen population public draft AI replay", Fame2017CaoJieChecks.FrozenPopulationPublicDraftAndAiReplay),
    ("Fame 2017 Qin Mi real top private tied replay", Fame2017QinMiChecks.PindianRealTopPrivateAndTiedReplay),
    ("Fame 2017 Qin Mi intercept all targets payment AI", Fame2017QinMiChecks.InterceptAllTargetsActualPaymentAndAi),
    ("committed events see the accepted command", EngineFaultChecks.CommittedObserversSeeTheAcceptedCommand),
    ("command projection preparation failure preserves prior commit", CommandSessionChecks.ProjectionFailurePreservesPriorCommit),
    ("command delivery failure preserves visible commit", CommandSessionChecks.DeliveryFailurePreservesVisibleCommit),
    ("command observer exception preserves commit and result", CommandSessionChecks.ObserverExceptionPreservesCommitAndResult),
    ("prepared snapshot rejects observer collection mutation", CommandSessionChecks.ObserverMutationCannotChangePreparedSnapshot),
    ("committed event collections reject observer mutation", CommandSessionChecks.EventCollectionObserversCannotRewriteCommittedHistory),
    ("round-start programs include foreign owners and cold returns", RoundStartingParticipantChecks.ForeignOwnerReceivesRoundStartAndColdReturns),
    ("selected gift continues after recipient death", CardMovementProgramChecks.SelectedGiftContinuesAfterRecipientDeathAndReplays),
    ("internal failure stops the session and preserves its trusted prefix", EngineFaultChecks.InternalFailureStopsTheSessionAndPreservesTheLastPrefix),
    ("draw and recover compile typed instructions", TypedProgramInstructionChecks.DrawAndRecoverCompileDistinctAmountSources),
    ("commands share prepared projection across result and reentry", PreparedCommandProjectionChecks.PublishedResultAndReentrantRejectionSharePreparedView),
    ("content registry compiles whole catalog dependencies", CompiledSkillMetadataChecks.CatalogDependenciesPreserveWholeRegistryAnswersAndIsolation),
    ("execution plans reuse source bindings without order collisions", CompiledSkillMetadataChecks.SourcePlansReuseBindingsWithoutIdentityOrOrderCollisions),
    ("active Program contracts share legality with public AI", CompiledSkillMetadataChecks.SharedLegalityProtectsActionsSubmittedInputAndPublicAi),
    ("skill execution reads hand context only for dependent conditions", CompiledSkillMetadataChecks.ExecutionConditionsReadHandContextOnlyWhenRequired),
    ("ordinary trick queries preserve exact options when narrowed", CompiledSkillMetadataChecks.OrdinaryTrickQueriesPreserveExactOptionsWhenNarrowed),
    ("legal action queries preserve exact physical entity options", CompiledSkillMetadataChecks.PhysicalCardQueriesPreserveWholePoolFactsAndExactActions),
    ("invariant history index retains issued receipts after source loss", InvariantHistoryIndexChecks.EmptyAndIssuedLedgersKeepHistoricalValidationAfterSourceLoss),
    ("content registry freezes selection weights before publication", RegistrySkillWeightFreezeChecks.RegisteredWeightsDetachInputsAndRejectOutputMutation),
    ("declaration payment lookups preserve exact receipt precedence", DeclarationPaymentLookupChecks.ReverseLookupsPreserveOriginalReceiptPrecedence),
    ("empty binding queries preserve exact states and issued obligations", EmptyBindingQueryChecks.EmptySlotsPreserveExactQueriesAndIssuedObligations),
    ("ordinary card snapshot reuse preserves appearance privacy and replay", SnapshotProjectionReuseChecks.OrdinaryCardReusePreservesAppearancePrivacyAndReplay),
    ("program state projection preserves empty and live policy JSON", ProgramStateProjectionChecks.EmptyAndLivePoliciesMatchOriginalSnapshotJson),
    ("public pile projection preserves empty and live queries", PublicPileProjectionChecks.EmptyAndLiveSourcesPreserveOriginalQueriesAndFreeze),
    ("zero material uses preserve appearance targets and replay", ZeroMaterialTargetAdjustmentChecks.ZeroMaterialUsesPreserveAppearanceTargetsAndReplay),
    ("legacy and borrowed sword zero material continuations", ZeroMaterialTargetAdjustmentChecks.LegacyAndBorrowedSwordZeroMaterialContinuations),
    ("Guan variants OL opening uses and target identity", GuanYuVariantChecks.OlOpeningUsesAndIdentity),
    ("Guan variants demon round return and foreign Dodge reward", GuanYuVariantChecks.DemonRoundReturnAndDodgeAid),
    ("character manifests preserve program bindings and reject invalid input", CharacterContentCatalogChecks.ProgramBindingsAndInvalidInput),
    ("skill executor retires strategic damage batches", FrameOwnedBatchChecks.StrategicDamageCompletesAndCancelsWithItsFrame),
    ("response use completion Dodge timing parent and replay", ResponseUseCompletionChecks.DodgeCompletesAfterCostAndResumesSlashOnce),
    ("response use completion counterspell chain and replay", ResponseUseCompletionChecks.CounterspellCompletesAfterCostAndResumesChainOnce),
    ("response use completion counterspell dying rescue exact parent", ResponseUseCompletionChecks.CounterspellCompletionDyingRescueRetainsExactParent),
    ("response use completion Dodge dying rescue exact parent", ResponseUseCompletionChecks.DodgeCompletionDyingRescueRetainsExactParent),
    ("response use completion excludes group response and wrong loader window", ResponseUseCompletionChecks.GroupDodgeStaysResponseOnlyAndLoaderRejectsWrongWindow),
    ("Fame 2017 Xu Shi foreign equipment AI replay", Fame2017XuShiChecks.ForeignEquipmentGiftAndAiReplay),
    ("Fame 2017 Xu Shi deck slashes responses cap shuffle", Fame2017XuShiChecks.DeckSlashesResponsesCapAndShuffle),
    ("Fame 2017 public pile store exchange distribute replay", Fame2017PublicPileChecks.StoreExchangeDistributionAndReplay),
    ("Fame 2017 public pile compound targets frozen response suit", Fame2017PublicPileChecks.CompoundTargetsAndFrozenResponseSuit),
    ("Fame 2017 Xin Xianying independent comparison quota replay", Fame2017XinXianyingChecks.ComparisonIndependentBranchesQuotaAndReplay),
    ("Fame 2017 Xin Xianying persistent limit self prohibition", Fame2017XinXianyingChecks.PersistentStateSelfTargetAndInsufficientHand),
    ("Fame 2016 deferred pile death loss face down skip replay", Fame2016DeferredChecks.DeferredPileDeathLossAndFaceDownSkip),
    ("Fame 2016 deferred private top gain order replay", Fame2016DeferredChecks.PrivateTopGainOrderAndReplay),
    ("Fame 2016 deferred provider pile zero three replay", Fame2016DeferredChecks.ProviderPileZeroThreeAndNextTurnReplay),
    ("Fame 2016 real ordered Slash and replay", Fame2016StateChecks.RealOrderedSlashAndReplay),
    ("Fame 2016 private damage offer and replay", Fame2016StateChecks.PrivateDamageOfferAndReplay),
    ("Fame 2016 range actor equipment payment privacy replay", Fame2016StateChecks.RangeActorEquipmentPaymentAndReplay),
    ("Fame 2016 population signed limit chain and replay", Fame2016StateChecks.GrowthLimitChainAndReplay),
    ("shared post-event recipient batch counts and reason filters", SharedPostEventChecks.TransferFiltersAndBatchCountsUseTheRecipient),
    ("shared post-event gained cards pause and replay", SharedPostEventChecks.GainsPauseBeforeTheNextInstructionAndReplay),
    ("shared post-event dying recovery ordering and replay", SharedPostEventChecks.HpLossWaitsForDyingAndCardRecoveryFinishesFirst),
    ("shared post-event definition context validation", SharedPostEventChecks.DefinitionFiltersRejectWrongContexts),
    ("shared use lifecycle nests rescue inside a suspended card window", CardUseLifecycleChecks.NestedRescueRetainsTheOuterUseWindow),
    ("shared card-use phase-owner movement context validation", CardUseLifecycleChecks.PhaseOwnerDestinationRequiresPhaseContext),
    ("current classic catalogue modes and skill references", CurrentClassicContentChecks.CatalogueAndModes),
    ("fuhuanghou rescue dodge payment and extra target replay", FuHuanghouChecks.QiuyuanDodgePaymentAndExtraTargetBothReplay),
    ("fuhuanghou fear pins the contestant and replay", FuHuanghouChecks.ZhuikongPinsTheContestantNotItsOwnerAndReplay),
    ("Fame 2011 equipment atomic exchange and replay", Fame2011EquipmentChecks.GanluAtomicEquipmentAndReplay),
    ("Fame 2011 equipment dying reveal privacy and replay", Fame2011EquipmentChecks.BuyiSelfChoiceBlindOtherPrivacyAndReplay),
    ("Fame 2017 Wu Xian Borrowed Sword pairs actual use replay", Fame2017WuXianChecks.BorrowedSwordPairsActualUseAndReplay),
    ("Fame 2017 Wu Xian alternating cycle draw replay", Fame2017WuXianChecks.AlternatingCycleDrawAndReplay),
    ("Fame 2017 Wu Xian red targets actual cost atomicity", Fame2017WuXianChecks.RedTargetsActualCostAndAtomicity),
    ("Fame 2016 Taoluan equipment global nullification", Fame2016TaoluanChecks.EquipmentGlobalAndNullification),
    ("Fame 2016 Taoluan names gift privacy replay", Fame2016TaoluanChecks.NamesGiftPrivacyReplay),
    ("Fame 2016 Taoluan response use completion", Fame2016TaoluanChecks.ResponseUseCompletion),
    ("Fame 2016 conversion declaration entity privacy replay", Fame2016ConversionChecks.DeclarationEntityPrivacyAndReplay),
    ("Fame 2016 conversion tier native tricks replay", Fame2016ConversionChecks.TierModificationNativeTricksAndReplay),
    ("Fame 2015 Wei top deck basic use name ledger replay", Fame2015WeiChecks.TopDeckBasicUseNameLedgerAndReplay),
    ("Fame 2015 Wei weapon entity costs privacy replay", Fame2015WeiChecks.WeaponDamageEntityCostsPrivacyAndReplay),
    ("Fame 2015 Wei faction recovery debt replay", Fame2015WeiChecks.FactionRecoveryDebtAndReplay),
    ("Fame 2015 Wei judgment color damage benefit replay", Fame2015WeiChecks.JudgmentColorDamageBenefitAndReplay),
    ("Fame 2015 Shu all hand Duel draw limit replay", Fame2015ShuChecks.AllHandDuelDrawLimitPaymentAndReplay),
    ("Fame 2015 Shu paired reveal privacy replay", Fame2015ShuChecks.PairedRevealAllFourBranchesPrivacyAndReplay),
    ("Fame 2015 Shu faction request cost reward replay", Fame2015ShuChecks.FactionRequestCostDeclineRewardAndReplay),
    ("Fame 2015 Shu alternative basic cost source dying boundaries", Fame2015ShuChecks.AlternativeBasicCostSourceUseAndDyingBoundaries),
    ("Fame 2015 hand control color real cost opaque gain replay", Fame2015HandControlChecks.RevealColorRealCostOpaqueGainAndHpLossReplay),
    ("Fame 2015 hand control participant draw top order replay", Fame2015HandControlChecks.ParticipantDrawTopOrderEquipmentAndReplay),
    ("Fame 2015 hand control damage limit permanent range replay", Fame2015HandControlChecks.RealPlayDamageHandLimitAndPermanentFactionTargetsReplay),
    ("Program trigger resource contracts", ProgramTriggerResourceChecks.TriggerWindowAndFrozenSuitContracts),
    ("Fame 2014 control Pindi categories targets physical cost and replay", Fame2014ControlChecks.PindiPhysicalCategoriesTargetLimitsAndReplay),
    ("Fame 2013 Li Ru fire chain dying resume and replay", Fame2013LiRuChecks.FireChainAndDyingResumeReplay),
    ("Fame 2013 sequential trick targets physical use and replay", SequentialTrickTargetChecks.AllFiveTricksResolveEveryTargetAndReplay),
    ("Fame 2013 discard movement origin native Dismantlement and replay", DiscardMovementOriginChecks.NativeDismantlementDiscardPreservesOriginAndReplay),
    ("turn-ending Xiaoguo Tianxiang game over stops later observers", TurnEndingGameOverChecks.XiaoguoTianxiangVictoryStopsLaterObservers),
    ("Qu Yi nearby Slash response precedes Jiaozi damage", QuYiChecks.NearbySlashCannotRespondBeforeDamageBonus),
    ("Feng Lin Congjian death multiple grants single target boundaries", FengLinCongjianChecks.OwnerDeathAndMultiGrantBoundaries),
    ("Feng Lin Congjian Equipment gift nested replay", FengLinCongjianChecks.EquipmentGiftNestedReplay),
    ("Zhang Xiu Xiongluan abolishes areas and blocks hand without armor bypass", ResponseAndSkillSuppressionChecks.XiongluanBlocksHandButDoesNotIgnoreArmor),
    ("Cai Wenji Duanchang permanently removes killer skills", ResponseAndSkillSuppressionChecks.DuanchangPermanentlyRemovesKillersSkills),
    ("2026 Shen Sima Yi kill grants exactly one extra turn with replay", ShenSimaYiChecks.KillGrantsExactlyOneExtraTurnAndReplays),
    ("Gundam One beam rifle discards one card and deals damage once per turn", GaoDaYiHaoChecks.BeamRifleDiscardsOneAndDamagesOncePerTurn),
    ("Gundam One core fighter revives once per game", GaoDaYiHaoChecks.CoreFighterRevivesOncePerGame),

    ("2013 Pan Zhang Ma Zhong natural far Slash and replay", PanZhangMaZhongChecks.NaturalSlashReverseRangeAndReplay),
    ("Xu Sheng Pojun holds target cards and returns them at turn end", XuShengChecks.ClassicPojunHoldsAndReturnsAtTurnEnd),
    ("Zhang Song equipment use, replacement and replay", ZhangSongChecks.EquipmentUsesReplaceAndResumeExactlyOnce),
    ("boundary Sima Yi Feedback takes source cards per damage point", BoundarySimaYiChecks.FeedbackPerPointAndSourceZones),
    ("phase exchange mixed zones atomicity and replay", ProgramActivationLimitChecks.MixedZonesAtomicityAndReplay),
    ("owned-card set private draft movement and replay", ProgramOwnedCardsChecks.PrivateDraftBatchMovementAndReplay),
    ("owned-card set shortfall empty sources and invalidation", ProgramOwnedCardsChecks.ShortfallEmptyAndInvalidatedDraft),
    ("Program choice definitions validate choices, payments and presentation hashes", ProgramChoiceChecks.DefinitionsValidateChoicesPaymentsAndPresentationHash),
    ("Program choice selected target conditional effects and replay", ProgramChoiceChecks.SelectedTargetChoosesConditionalEffectsAndReplays),
    ("Program choice revalidates options and exact skill instance", ProgramChoiceChecks.RevalidatesChoiceAndExactInstanceBeforeResolving),
    ("shared use lifecycle preserves dying rescue and replay", CardUseLifecycleChecks.DyingBasicUsesResumeTheirRescueParent),
    ("completed Slash program window follows finished card use and replays", CardUseCompletedChecks.FinishedSlashOpensReplayableProgramWindow),
    ("card-action judgment windows resume and replay", ProgramCardJudgmentWindowChecks.AllCardActionWindowsStartPublicJudgmentsAndReplay),
    ("completed Slash freezes actual damage for conditional programs", CardUseCompletedChecks.CompletedUseFreezesActualDamageFact),
    ("composition kernel resource graph safety", ProgramCompositionDefinitionChecks.ResourceGraphsRejectAliasingLeaksAndMissingInputs),
    ("composition kernel malformed nodes", ProgramCompositionDefinitionChecks.MalformedNodesFailBeforeExecution),
    ("composition kernel boundary capabilities", ProgramCompositionContextChecks.SharedWindowsAcceptCommonNodesAndRejectMissingContexts),
    ("composition kernel target-set consumption", ProgramCompositionContextChecks.TargetSetIsConsumedOnce),
    ("execution plans freeze instructions and reject ambiguous bindings", ProgramExecutionPlanChecks.PlansFreezeInstructionsAndRejectAmbiguousBindings),
    ("rule query reduction is input-order independent", RuleQueryReducerChecks.IsIndependentOfInputOrderAndRejectsOnlyWinningSetConflicts),
    ("rule query reduction rejects invalid mutable inputs", RuleQueryReducerChecks.RejectsInvalidInputsAndFreezesOutput),
    ("rule query distance runs both directions before clamping", RuleQueryReducerChecks.DirectionalDistanceRunsBothStagesBeforeClamping),
    ("rule query registry rejects future Set conflicts", RuleQueryReducerChecks.StaticSetConflictsAreRejectedBeforePlay),
    ("character skills retain separate sources and stable ownership", CharacterSkillSetChecks.GrantsRetainSourcesAndStableOwnership),
    ("character skills reject conflicting grants without partial state", CharacterSkillSetChecks.InvalidGrantsAndConflictsLeaveStateUnchanged),
    ("boundary Zhen Ji frozen final outcome replacement claim", BoundaryZhenJiChecks.FrozenFinalOutcomeReplacementAndClaim),
    ("boundary Zhen Ji exact identity actual turn expiry privacy", BoundaryZhenJiChecks.ExactIdentityReturnActualTurnExpiryAndPrivacy),
    ("boundary Zhen Ji narrow parser legacy null invariants", BoundaryZhenJiChecks.NarrowParserLegacyNullAndOwningInvariants),
    ("boundary Xiahou Dun each damage point real judgment branches", BoundaryXiahouDunChecks.GanglieEachPointRealJudgmentBranches),
    ("boundary Xiahou Dun actual receipt input privacy replay", BoundaryXiahouDunChecks.QingjianActualReceiptInputPrivacyAndReplay),
    ("boundary Xiahou Dun narrow parser legacy null boundary", BoundaryXiahouDunChecks.QingjianNarrowParserAndLegacyNullBoundary),
    ("boundary Guan Yu extra Play quota named Heart source lifecycle", BoundaryGuanYuChecks.ExtraPlayQuotaAndNamedHeartBenefit),
    ("boundary Guan Yu actual Diamond provider Use", BoundaryGuanYuChecks.ActualDiamondProviderUse),
    ("boundary Guan Yu black actual payment chooser privacy replay", BoundaryGuanYuChecks.BlackActualPaymentChooserPrivacyAndReplay),
    ("current turn nonlocked suppression host lifecycle audit", CurrentTurnSkillSuppressionChecks.HostLifecycleQualificationAudit),
    ("match skill binding sources preserve instance and unique-program semantics", MatchSkillBindingIndexChecks.SourcesDeduplicateInstancesAndUniqueProgramBuckets),
    ("skill executor resumes child resolution without repeating paid effects", SkillProgramExecutorChecks.SuspendedChildResumesWithoutRepeatingPaidEffect),
    ("skill executor separates owner-death and lost-skill continuation rights", SkillProgramExecutorChecks.ContinuationPermissionsKeepDeathAndSkillLossSeparate),
    ("skill executor preserves continuation metadata and immutable definition checks", SkillProgramExecutorChecks.ContinuationMetadataAndDefinitionValidationRemainAuthoritative),
    ("skill executor keeps lost-skill continuation behind its paid child", SkillProgramExecutorChecks.SourceLossContinuationWaitsForPaidChildExactlyOnce),
    ("skill executor cancels remaining effects after an invalid selected cost", SkillProgramExecutorChecks.InvalidSelectedCostCancelsRemainingEffectsAtomically),
    ("skill executor rejects changed programs and missing or duplicate handlers", SkillProgramExecutorChecks.RejectsChangedProgramsUnknownHandlersAndDuplicates),
    ("active Program contracts reject invalid selections and preserve target order", SkillProgramExecutorChecks.ActiveActivationContractsRejectInvalidDefinitionsAndPreserveOrder),
    ("card subset selector enumerates every legal subset once", CardSubsetSelectorChecks.EnumeratesEveryLegalSubsetExactlyOnce),
    ("card subset selector rejects oversized sources and freezes choices", CardSubsetSelectorChecks.RejectsOversizedOrAmbiguousSourcesAndFreezesChoices),
    ("lifecycle programs reject live card bindings across inserted phases", ProgramLifecycleChecks.RejectsCardBindingsAcrossDetachedPhaseBoundary),
    ("lifecycle programs cancel missing conditional bindings without leaks", ProgramLifecycleChecks.MissingConditionalBindingsCancelWithoutLeakingCards),
    ("cancelled lifecycle programs preserve parent attack cards", ProgramLifecycleChecks.CancelledDamageProgramsDoNotCleanupParentAttackCards),
    ("card movement programs compose atomic per-card and per-batch triggers with replay", CardMovementProgramChecks.AtomicBatchRunsPerCardAndPerBatchAndReplays),
    ("nested card movement batches retain immediate parent identity", CardMovementProgramChecks.NestedBatchesRetainImmediateParentIdentity),
    ("damage programs claim select gift draw and replay through generic choices", DamageProgramChecks.GenericDamageChoicesClaimSelectGiftDrawAndReplay),
    ("damage cursor typed observers require frozen candidate and parent chain", DamageCursorProjectionChecks.TypedObserversRequireExactFrozenCandidateAndParentChain),
    ("schema-19 additive draw adjustments, explicit draws and damage grants compose and replay", DrawPhaseCompositionChecks.AdditiveAdjustmentDrawAndDamageGrantComposeAndReplay),
    ("schema-19 reveal partitions, recovery and explicit draws conserve cards and replay", DrawPhaseCompositionChecks.RevealPartitionRecoveryAndExtraDrawConserveCardsAndReplay),
    ("schema-19 draw-phase validation rejects unsafe data-flow graphs", DrawPhaseCompositionChecks.ValidatorRejectsUnsafeGraphs),
    ("schema-20 turn policies are typed idempotent and expire together", DrawPolicyProgramChecks.TurnPoliciesAreTypedIdempotentAndExpireTogether),
    ("schema-21 judgment results bind turn conversion and replay", JudgmentDrawProgramChecks.JudgmentBindingConversionAndReplay),
    ("schema-22 Niepan clears owned state and replays", SelfDyingStateProgramChecks.ClearsOwnedStateAndReplays),
    ("structured skill metadata normalizes explicitly and fingerprints content", SkillMetadataChecks.TagsNormalizeAndFingerprint),
    ("skill runtime usage and conversion states reset by declared scope", SkillMetadataChecks.RuntimeUsageAndReset),
    ("Boundary Huang Gai multi card color source lifecycle", FengLinBoundaryHuangGaiChecks.MultiCardColorAndSourceLifecycle),
    ("Boundary Huang Gai dying extra Play legacy", FengLinBoundaryHuangGaiChecks.DyingExtraPlayAndLegacy),
    ("Feng Lin Yu Ji actual dodge duel rescue parents", FengLinYuJiChecks.ActualDodgeDuelRescueParents),
    ("Feng Lin Yu Ji actual counterspell provider parents", FengLinYuJiChecks.ActualCounterspellAndProviderParents),
    ("Feng Lin Yu Ji old registry virtual duel native continuation", FengLinYuJiChecks.NativeVirtualDuelOldRegistry),
    ("Feng Lin Yuan Shu qualified awakening persistent local loss", FengLinYuanShuChecks.QualifiedAwakeningAndPersistentLocalLoss),
    ("Feng Lin Yuan Shu actual response provider chain", FengLinYuanShuChecks.ActualResponseProviderChain),
    ("Feng Lin Yuan Shu command loss regrant source suppression", FengLinYuanShuChecks.CommandLossRegrantAndSuppression),
    ("printed Lord skills follow identity and rules 96 replay ownership boundaries", SkillOwnershipChecks.PrintedLordSkillsFollowIdentityAndReplayBoundary),
    ("skill program v2 trigger definitions validate and freeze", SkillProgramTriggerDefinitionChecks.Run),
    ("skill program card triggers require exact conversion sources and replay", SkillProgramCardTriggerChecks.Run),
    ("skill program multi-target triggers wait for every Liuli redirection", SkillProgramTargetOrderChecks.Run),
    ("skill program final judgment triggers resume recovery, drawing and replay", SkillProgramJudgmentTriggerChecks.WindowAndReplay),
    ("skill program judgment replacement commits both old-card destinations and replays", SkillProgramJudgmentReplacementChecks.WindowDestinationsAndReplay),
    ("skill program judgment damage resumes through dying, death and replay", SkillProgramJudgmentDamageChecks.TargetDamageDyingAndReplay),
    ("skill program direct Dodge and Lightning judgments resume and replay", SkillProgramStartedJudgmentChecks.DirectDodgeAndLightningReplay),
    ("skill program contributions bind owner filters, phase ledger and replay", SkillProgramContributionChecks.CrossOwnerFiltersLedgerAndReplay),
    ("skill program selected judgment subjects damage and replay", SkillProgramSelectedJudgmentChecks.SelectedSubjectDamageAndReplay),
    ("schema-46 viewAs source zones validate and isolate hand from equipment", SkillProgramViewAsZoneChecks.DefinitionAndZoneIsolation),
    ("mandatory card identity suppresses native use, ignores Slash distance and replays", SkillProgramCardIdentityChecks.MandatoryIdentityDistanceAndReplay),
    ("turn card-use effects union order consume and expire by action semantics", CardUseModuleEffectChecks.TurnStateUsesActionSemanticsStableOrderAndExpiration),
    ("active programs suspend into Pindian and branch from the frozen result", PindianModuleChecks.ActiveProgramPindianSuspendsConditionsAndReplays),
    ("god generals choose effective factions with public Lord and private non-Lords and replay", GodFactionSelectionChecks.PromptPrivacyEffectiveFactionAndReplay),
    ("formal Hengye grows on damage and resets after a kill", MouLuMengChecks.HengyeGrowthAndKillReset),
    ("Jiangchi extra draw blocks Slash use and play", CaoZhangChecks.DrawMoreBlocksSlashUseAndResponse),
    ("Qianxi red restriction filters same-color hand responses", MaDaiChecks.RedRestrictionFiltersHandResponsesAndReplays),
    ("Qice converts every hand card once and replays", XunYouChecks.QiceUsesAllHandCardsAndReplays),
    ("configured Lihuo penalty enters dying and replays", ChengPuLihuoChecks.CompletedPenaltyCanEnterDyingAndReplay),
    ("Chunlao spends one public Chun as virtual Alcohol in a dying response", ChengPuLihuoChecks.ChunlaoRescuesWithVirtualAlcoholAndReplays),
    ("turn-ending boundary orders Jushou Jujian Biyue and replays", TurnEndingBoundaryChecks.OrdersJushouJujianBiyueAndReplays),
    ("public Nightmare markers count each Wuhun damage point before dying and replay", PublicMarkerChecks.WuhunDamageOrderAndReplay),
    ("Xingshang claims nested direct death cleanup cards and replays", PublicMarkerChecks.XingshangClaimsNestedDeathCleanupAndReplay),
    ("recast is an independent validated card movement and replays exactly", RecastChecks.CommandAndReplay),
    ("claimed group cards continue after the claimant dies and replay exactly", GroupClaimChecks.ClaimantDeathContinues),
    ("viewer snapshot hides private roles and hands", SnapshotHidesSecrets),
    ("viewer snapshot hides seed and other players' decisions", SnapshotHidesEngineSecrets),
    ("viewer cannot mutate the engine pending decision", SnapshotDecisionIsDefensive),
    ("commands publish a revision and reject stale input atomically", CommandRevisionBoundary),
    ("play commands use one exact published card and target choice", CommandPlayUsesExactChoice),
    ("prompt answers validate prompt, choice, actor and revision", CommandPromptAnswerBoundary),
    ("command reentry is a typed rejection", CommandReentryIsTyped),
    ("winner rules cover all three camps", WinnerRules),
    ("manual discards reject invalid subsets without mutation", ManualDiscardChecks.Rejections),
    ("manual discard checkpoints and commands replay exactly", ManualDiscardChecks.Replay),
    ("standard deck content is deterministic and balanced", StandardDeckContent),
    ("standard package builds an immutable isolated registry", StandardContentRegistryBuilds),
    ("skill program loader separates canonical gameplay from presentation", SkillProgramChecks.LoaderCanonicalizationAndPresentationIsolation),
    ("skill program loader rejects malformed and unsupported definitions", SkillProgramChecks.LoaderRejectsMalformedUnsupportedDefinitions),
    ("configured active sequences reject forged input and replay exact moves", SkillProgramChecks.ConfiguredActiveSequenceIsAtomicAndReplayable),
    ("program gameplay hashes control checkpoint compatibility independently of presentation", SkillProgramChecks.ProgramHashControlsCheckpointCompatibility),
    ("program dying checkpoints cancel a selected card spent on rescue", SkillProgramChecks.ProgramPausedDyingAndConsumedSelection),
    ("physical deck recipes preserve exact suit, rank and content hashing", PhysicalDeckRecipeChecks.ExactSuitRankValidationAndHashing),
    ("classic identity applies base HP, multiple skills and legacy replay boundaries", ClassicGeneralChecks.SetupHealthAndReplay),
    ("classic Borrowed Sword transfers weapons or nests a real Slash and replays", BorrowedSwordChecks.TransferSlashAndReplay),
    ("classic Stone Axe pays an exact two-card cost and resumes Slash damage", StoneAxeChecks.ExactCostDamageAndReplay),
    ("classic Qinglong opens a same-target follow-up Slash and replays", QinglongCrescentBladeChecks.SameTargetFollowupAndReplay),
    ("classic Ice Sword sequentially discards target cards and prevents Slash damage", IceSwordChecks.SequentialDiscardPreventsDamageAndReplays),
    ("classic Pang De discards an opaque hand card or public equipment through Mengjin", MengjinChecks.HiddenHandPublicEquipmentChoiceAndReplay),
    ("classic Xiao Qiao converts Spades and transfers damage through Tianxiang", XiaoQiaoChecks.HongyanTianxiangTransferAndReplay),
    ("classic Zhuque Fan converts ordinary Slash to Fire Slash and preserves owner choice", ZhuqueFanChecks.FireConversionChainFactionSlashAndLegacyBoundary),
    ("classic Silver Lion caps damage and recovers after leaving equipment", SilverLionChecks.DamageCapRemovalRecoveryAndLegacyBoundary),
    ("classic Wooden Ox stores private playable grain and replays", WoodenOxChecks.StoresPrivatePlayableGrainAndReplays),
    ("classic FactionSlash privately supplies Duel and Barbarian Slash responses and replays", ClassicGeneralChecks.FormalFactionSlashResponseFlow),
    ("standard active programs use typed selections and deterministic replay", ActiveSkillChecks.KujinFlow),
    ("Mashu changes public distance and distance-gated legal actions", DistanceSkillChecks.MashuDistanceAndLegality),
    ("content registry rejects duplicate ids and bad references", ContentRegistryValidation),
    ("interactive setup exposes private deterministic general choices", InteractiveSetupPipeline),
    ("Opening customization: private roster and initial hand rerolls replay", OpeningCustomizationChecks.PrivateRosterAndOpeningHandReplay),
    ("Opening customization: public Lord then nonzero human and private AI", OpeningCustomizationChecks.NonLordSelectsAfterPublicLordBeforePrivateAi),
    ("Opening customization: nonzero team and national human before private AI", OpeningCustomizationChecks.TeamAndNationalHumanChooseBeforePrivateAi),
    ("identity role general candidates freeze private choices and replay", IdentitySetupChecks.RoleCandidatesPrivacyAndReplay),
    ("identity general variants keep candidate families private and replay exact editions", IdentitySetupChecks.GeneralVariantsPrivacyAndReplay),
    ("identity Rebel kill reward draws exactly three cards and replays once", IdentitySetupChecks.RebelKillDrawsThree),
    ("AI policy versions validate and replay deterministically", TacticalAiChecks.PolicyReplay),
    ("targeted tricks finish when the target spends its last card on Nullification", TargetLossChecks.LastNullification),
    ("hand guidance is private, read-only and agrees with legal actions", HandGuidanceChecks.ReadOnlyAndPrivate),
    ("formal Kongcheng rejects empty-hand Duel targets atomically", KongchengChecks.DuelTargeting),
    ("accepted command journals serialize and replay deterministically", CommandJournalReplay),
    ("command checkpoints restore a paused private prompt deterministically", CheckpointRestore),
    ("command checkpoints reject same-version content drift", CheckpointRejectsSameVersionContentDrift),
    ("initial deal registers every physical card in one zone", InitialDealCardZones),
    ("invalid single and batch moves are atomic", InvalidCardMovesAreAtomic),
    ("Slash and Dodge pass through the Processing zone", SlashAndDodgeProcessing),
    ("card inventory is conserved at every public boundary", CardInventoryConservation),
    ("observers run only after the public operation commits", ObserversRunPostCommit),
    ("typed host events are committed, ordered and deterministic", TypedEventStream),
    ("slash resolution exposes a serializable frame stack", ResolutionFrameStack),
    ("duel alternates Slash responses through a typed window", DuelResponseFlow),
    ("Delayed Nullification: placement judgment chain and cold replay", DelayedNullificationChecks.PlacementAndJudgmentWindows),
    ("Nullification resolves a private multi-layer trick window", NullificationFlow),
    ("PeachGarden resolves a paused multi-target recovery", PeachGardenFlow),
    ("FiveGrains reveals public cards with private draft prompts", FiveGrainsFlow),
    ("target-card prompts expose opaque slots and replay without hidden identities", TargetCardChecks.OpaqueSlotFlow),
    ("FireAttack reveals privately then resolves typed fire damage", FireAttackFlow),
    ("dying response can pause and recover with a private Peach", DyingResponseFlow),
    ("throwing observers are isolated after commit", ObserverFailuresAreIsolated),
    ("synchronous observers cannot advance the engine reentrantly", ReentrantAdvanceIsRejected),
    ("an unknown phase fails fast", UnknownPhaseFailsFast),
    ("snapshot is JSON serializable", SnapshotSerialization)
};

var nameFilters = args.Where(argument => argument.StartsWith("--filter=", StringComparison.OrdinalIgnoreCase))
    .Select(argument => argument["--filter=".Length..].Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
if (nameFilters.Length > 0)
{
    if (nameFilters.Any(value => value.Length == 0))
    {
        Console.Error.WriteLine("A Core check filter cannot be empty.");
        return 2;
    }
    foreach (var value in nameFilters)
    {
        if (!tests.Any(test => test.Name.Contains(value, StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine($"No Core checks matched filter '{value}'.");
            return 2;
        }
    }
    tests = tests.Where(test => nameFilters.Any(value => test.Name.Contains(value, StringComparison.OrdinalIgnoreCase))).ToArray();
}

var passed = 0;
var failed = 0;
const int skipped = 0;
foreach (var (name, body) in tests)
{
    var started = System.Diagnostics.Stopwatch.GetTimestamp();
    try
    {
        body();
        passed++;
        Console.WriteLine($"[PASS] {name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.WriteLine($"[FAIL] {name}");
        Console.WriteLine($"       {exception.GetType().Name}: {exception.Message}");
        Console.WriteLine(exception.StackTrace);
        if (args.Contains("--verbose", StringComparer.OrdinalIgnoreCase)) Console.WriteLine(exception);
    }
    finally
    {
        if (args.Contains("--verbose", StringComparer.OrdinalIgnoreCase))
            Console.WriteLine($"[TIME] {name}: {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)} ms");
    }
}

Console.WriteLine();
Console.WriteLine($"{passed} passed, {failed} failed, {skipped} skipped ({tests.Length} total).");
return failed == 0 ? 0 : 1;


static void SnapshotHidesSecrets()
{
    var game = GameEngine.CreateStandard(new GameOptions { UseInteractiveDiscard = false, Seed = 13, HumanSeat = 0, HumanRole = Role.Lord }, StandardContentRegistry.Create());
    var snapshot = game.State;
    var self = snapshot.Players.Single(player => player.Seat == 0);
    var hiddenOpponent = snapshot.Players.First(player => player.Seat != 0 && player.Role is null);

    Equal(Role.Lord, self.Role);
    Equal(4, self.Hand.Count);
    Equal(0, hiddenOpponent.Hand.Count);
    Equal(4, hiddenOpponent.HandCount);
}

static void SnapshotHidesEngineSecrets()
{
    var game = GameEngine.CreateStandard(new GameOptions { UseInteractiveDiscard = false, Seed = 413, HumanSeat = 0, HumanRole = Role.Lord }, StandardContentRegistry.Create());
    game.DriveStart();

    Equal(413, game.Seed);
    Equal<int?>(null, game.State.Seed);
    Equal<int?>(413, game.CreateSnapshot(0, revealAll: true).Seed);
    NotNull(game.State.PendingDecision);
    Equal<PendingDecision?>(null, game.CreateSnapshot(1).PendingDecision);
    Equal<PendingDecision?>(null, game.CreateSnapshot(1, revealAll: true).PendingDecision);
    True(game.Log.All(entry => !entry.Message.Contains("413", StringComparison.Ordinal)));
}

static void SnapshotDecisionIsDefensive()
{
    GameEngine? selectedGame = null;
    for (var seed = 1; seed <= 64 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            UseInteractiveDiscard = false,
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord
        }, StandardContentRegistry.Create());
        game.DriveStart();
        if (game.PendingDecision is { ValidCardIds.Count: > 0, ValidTargetSeats.Count: > 0 })
        {
            selectedGame = game;
        }
    }

    NotNull(selectedGame);
    var exposed = selectedGame!.State.PendingDecision!;
    var originalCard = exposed.ValidCardIds[0];
    var originalTarget = exposed.ValidTargetSeats[0];
    True(exposed.ValidCardIds is not int[]);
    True(exposed.ValidTargetSeats is not int[]);

    var copiedCards = exposed.ValidCardIds.ToArray();
    var copiedTargets = exposed.ValidTargetSeats.ToArray();
    copiedCards[0] = int.MaxValue;
    copiedTargets[0] = int.MaxValue;

    Equal(originalCard, selectedGame.PendingDecision!.ValidCardIds[0]);
    Equal(originalTarget, selectedGame.PendingDecision!.ValidTargetSeats[0]);
}

static void CommandRevisionBoundary()
{
    var game = GameEngine.CreateStandard(new GameOptions
    {
        UseInteractiveDiscard = false,
        Seed = 901,
        HumanSeat = 0,
        HumanRole = Role.Lord
    }, StandardContentRegistry.Create());

    Equal(0L, game.Revision);
    Equal(0L, game.State.Revision);

    var started = game.Submit(new StartGameCommand(ExpectedRevision: 0));
    True(started.Accepted);
    Equal<CommandError?>(null, started.Error);
    Equal(1L, started.Revision);
    Equal(started.Revision, started.State.Revision);
    NotNull(started.PendingDecision);
    True(started.PendingDecision!.PromptId.IsValid);
    Equal(started.Revision, started.PendingDecision.Revision);
    True(started.PendingDecision.Choices.Count > 0);
    Equal(
        started.PendingDecision.Choices.Count,
        started.PendingDecision.Choices.Select(choice => choice.Id).Distinct().Count());

    var stateBeforeStale = SnapshotJson.Serialize(game.State);
    var logCountBeforeStale = game.Log.Count;
    var movementCountBeforeStale = game.CardMovements.Count;
    var stale = game.Submit(new EndPlayPhaseCommand(
        ActorSeat: 0,
        ExpectedRevision: 0,
        PromptId: started.PendingDecision.PromptId));

    False(stale.Accepted);
    Equal(CommandErrorCode.StaleRevision, stale.Error!.Code);
    Equal(1L, game.Revision);
    Equal(stateBeforeStale, SnapshotJson.Serialize(game.State));
    Equal(logCountBeforeStale, game.Log.Count);
    Equal(movementCountBeforeStale, game.CardMovements.Count);
}

static void CommandPlayUsesExactChoice()
{
    GameEngine? selectedGame = null;
    PromptChoice? selectedChoice = null;
    LegalAction? selectedAction = null;
    for (var seed = 1; seed <= 64 && selectedChoice is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            UseInteractiveDiscard = false,
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord
        }, StandardContentRegistry.Create());
        var started = game.Submit(new StartGameCommand());
        var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
            candidate.CardId is not null && candidate.TargetSeats.Count == 1 &&
            candidate.ConversionSource is null && candidate.AdditionalConversionSources is null);
        var choice = action is null ? null : started.PendingDecision?.Choices.FirstOrDefault(candidate =>
            candidate.Cards.SequenceEqual([action.CardId!.Value]) &&
            candidate.Targets.SequenceEqual(action.TargetSeats) &&
            !candidate.Parameters.ContainsKey("conversion-skill-id") &&
            candidate.Parameters.GetValueOrDefault("played-card-kind") == action.PlayedCardKind?.ToString());
        if (started.Accepted && choice is not null && action is not null)
        {
            selectedGame = game;
            selectedChoice = choice;
            selectedAction = action;
        }
    }

    NotNull(selectedGame);
    NotNull(selectedChoice);
    NotNull(selectedAction);
    var pending = selectedGame!.PendingDecision!;
    var before = SnapshotJson.Serialize(selectedGame.State);
    var revisionBefore = selectedGame.Revision;
    var card = selectedChoice!.Cards.Single();
    var target = selectedChoice.Targets.Single();

    var malformed = selectedGame.Submit(new PlayCardCommand(
        ActorSeat: 0,
        CardId: card,
        TargetSeats: [int.MaxValue],
        ExpectedRevision: revisionBefore,
        PromptId: pending.PromptId,
        PlayedCardKind: selectedAction!.PlayedCardKind,
        TargetCardId: selectedAction.TargetCardId));
    if (malformed.Accepted) throw new InvalidOperationException("An unpublished target was accepted.");
    Equal(CommandErrorCode.InvalidTarget, malformed.Error!.Code);
    Equal(revisionBefore, selectedGame.Revision);
    Equal(before, SnapshotJson.Serialize(selectedGame.State));

    var accepted = selectedGame.Submit(new PlayCardCommand(
        ActorSeat: 0,
        CardId: card,
        TargetSeats: [target],
        ExpectedRevision: revisionBefore,
        PromptId: pending.PromptId,
        PlayedCardKind: selectedAction!.PlayedCardKind,
        TargetCardId: selectedAction.TargetCardId));
    if (!accepted.Accepted) throw new InvalidOperationException(
        accepted.Error?.Message ?? "The exact published play action was rejected.");
    Equal(revisionBefore + 1, selectedGame.Revision);
    ResolveNullificationWindowForTest(selectedGame);
    if (!selectedGame!.Events.Any(item => item.Payload is CardUseDeclaredEvent declared &&
            declared.CardId == card))
        throw new InvalidOperationException("The accepted exact choice did not declare its physical card use.");
}

static void CommandPromptAnswerBoundary()
{
    GameEngine? selectedGame = null;
    for (var seed = 1; seed <= 4_096 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            UseInteractiveDiscard = false,
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 150
        }, StandardContentRegistry.Create());
        var result = game.Submit(new StartGameCommand());
        var steps = 0;
        while (result.Accepted && result.State.Status != EngineStatus.Completed && steps++ < 500)
        {
            if (result.State.Status == EngineStatus.AwaitingHumanResponse)
            {
                if (result.PendingDecision?.Kind == DecisionKind.RespondDodge)
                {
                    selectedGame = game;
                    break;
                }

                var responsePrompt = result.PendingDecision!;
                var declineResponse = responsePrompt.Choices.Last();
                result = game.Submit(new AnswerPromptCommand(
                    0,
                    responsePrompt.PromptId,
                    declineResponse.Id,
                    game.Revision));
                continue;
            }

            if (result.State.Status == EngineStatus.AwaitingHumanDying)
            {
                var dyingPrompt = result.PendingDecision!;
                var letDieChoice = dyingPrompt.Choices.Single(choice =>
                    choice.Parameters["response"] == "let-die");
                result = game.Submit(new AnswerPromptCommand(
                    0,
                    dyingPrompt.PromptId,
                    letDieChoice.Id,
                    game.Revision));
                continue;
            }

            if (result.State.Status == EngineStatus.AwaitingHumanCardSelection)
            {
                var harvestPrompt = result.PendingDecision!;
                var harvestChoice = harvestPrompt.Choices.First();
                result = game.Submit(new AnswerPromptCommand(
                    0,
                    harvestPrompt.PromptId,
                    harvestChoice.Id,
                    game.Revision));
                continue;
            }

            if (result.State.Status == EngineStatus.AwaitingHumanPlay)
            {
                var playPrompt = result.PendingDecision!;
                result = game.Submit(new EndPlayPhaseCommand(
                    ActorSeat: 0,
                    ExpectedRevision: game.Revision,
                    PromptId: playPrompt.PromptId));
            }
            else
            {
                result = game.Submit(new AdvanceCommand(game.Revision));
            }
        }
    }

    NotNull(selectedGame);
    var gameWithPrompt = selectedGame!;
    var prompt = gameWithPrompt.PendingDecision!;
    True(prompt.PromptId.IsValid);
    Equal(gameWithPrompt.Revision, prompt.Revision);
    True(prompt.Choices.Any(choice => choice.Parameters["response"] == "dodge"));
    var decline = prompt.Choices.Single(choice => choice.Parameters["response"] == "take-damage");
    var before = SnapshotJson.Serialize(gameWithPrompt.State);
    var revisionBefore = gameWithPrompt.Revision;

    var stale = gameWithPrompt.Submit(new AnswerPromptCommand(
        ActorSeat: 0,
        Prompt: prompt.PromptId,
        Choice: decline.Id,
        ExpectedRevision: revisionBefore - 1));
    False(stale.Accepted);
    Equal(CommandErrorCode.StaleRevision, stale.Error!.Code);
    Equal(revisionBefore, gameWithPrompt.Revision);
    Equal(before, SnapshotJson.Serialize(gameWithPrompt.State));

    var wrongPrompt = gameWithPrompt.Submit(new AnswerPromptCommand(
        ActorSeat: 0,
        Prompt: new PromptId(prompt.PromptId.Value + 1),
        Choice: decline.Id,
        ExpectedRevision: revisionBefore));
    False(wrongPrompt.Accepted);
    Equal(CommandErrorCode.InvalidPrompt, wrongPrompt.Error!.Code);
    Equal(before, SnapshotJson.Serialize(gameWithPrompt.State));

    var wrongChoice = gameWithPrompt.Submit(new AnswerPromptCommand(
        ActorSeat: 0,
        Prompt: prompt.PromptId,
        Choice: new ChoiceId("response.not-published"),
        ExpectedRevision: revisionBefore));
    False(wrongChoice.Accepted);
    Equal(CommandErrorCode.InvalidChoice, wrongChoice.Error!.Code);
    Equal(before, SnapshotJson.Serialize(gameWithPrompt.State));

    var accepted = gameWithPrompt.Submit(new AnswerPromptCommand(
        ActorSeat: 0,
        Prompt: prompt.PromptId,
        Choice: decline.Id,
        ExpectedRevision: revisionBefore));
    True(accepted.Accepted);
    Equal(revisionBefore + 1, gameWithPrompt.Revision);
    True(gameWithPrompt.PendingDecision is null ||
         gameWithPrompt.PendingDecision.PromptId != prompt.PromptId);
}

static void CommandReentryIsTyped()
{
    var game = GameEngine.CreateStandard(new GameOptions
    {
        UseInteractiveDiscard = false,
        Seed = 907,
        HumanSeat = 0,
        HumanRole = Role.Lord
    }, StandardContentRegistry.Create());
    CommandResult? reentry = null;
    game.LogAdded += _ => reentry ??= game.Submit(new AdvanceCommand(game.Revision));

    var result = game.Submit(new StartGameCommand());

    True(result.Accepted);
    NotNull(reentry);
    False(reentry!.Accepted);
    Equal(CommandErrorCode.ReentrantOperation, reentry.Error!.Code);
    Equal(result.Revision, game.Revision);
}

static void WinnerRules()
{
    Equal(Winner.None, GameRules.EvaluateWinner(
    [
        new(Role.Lord, true),
        new(Role.Loyalist, true),
        new(Role.Rebel, true),
        new(Role.Renegade, true)
    ]));

    Equal(Winner.LordAndLoyalists, GameRules.EvaluateWinner(
    [
        new(Role.Lord, true),
        new(Role.Loyalist, true),
        new(Role.Rebel, false),
        new(Role.Renegade, false)
    ]));

    Equal(Winner.Rebels, GameRules.EvaluateWinner(
    [
        new(Role.Lord, false),
        new(Role.Loyalist, false),
        new(Role.Rebel, true),
        new(Role.Renegade, true)
    ]));

    Equal(Winner.Renegade, GameRules.EvaluateWinner(
    [
        new(Role.Lord, false),
        new(Role.Loyalist, false),
        new(Role.Rebel, false),
        new(Role.Renegade, true)
    ]));
}


static void StandardDeckContent()
{
    var registry = StandardContentRegistry.Create();
    var definition = registry.GetDeck("standard:basic-demo");
    var game = GameEngine.CreateStandard(new GameOptions
    {
        Seed = 1,
        PlayerCount = 5,
        HumanSeat = 0,
        HumanRole = Role.Lord,
        UseInteractiveSetup = false
    }, registry);
    var physical = game.CreateCardZoneDiagnostics();
    var expected = definition.Cards.ToDictionary(
        entry => registry.GetCard(entry.CardDefinitionId).LegacyKind!.Value,
        entry => entry.Count);
    var actual = physical.GroupBy(card => card.CardKind)
        .ToDictionary(group => group.Key, group => group.Count());

    Equal(4, definition.InitialHandSize);
    Equal(2, definition.DrawPerTurn);
    Equal(definition.Cards.Sum(entry => entry.Count), physical.Count);
    Equal(expected.Count, actual.Count);
    foreach (var (kind, count) in expected) Equal(count, actual[kind]);
    True(physical.Select(card => card.CardId).Order().SequenceEqual(
        Enumerable.Range(1, physical.Count)));
    True(game.CreateSnapshot(0, true).Players.All(player =>
        player.HandCount == definition.InitialHandSize));
}

static void StandardContentRegistryBuilds()
{
    var registry = StandardContentRegistry.Create();
    var secondRegistry = StandardContentRegistry.Create();

    Equal(1, registry.Packages.Count);
    Equal("standard", registry.Packages[0].Id);
    TrueWithMessage(!string.IsNullOrWhiteSpace(registry.ContentHash), "registry exposes a content hash");
    Equal(registry.ContentHash, secondRegistry.ContentHash);
    Equal(31, registry.Cards.Count);
    Equal(13, registry.Skills.Count);
    Equal(
        "装备至防具槽；成为普通/火/雷杀的直接目标时可选择公开判定，红色判定牌视为闪。",
        registry.GetCard("standard:bagua").Description);
    Equal("public-judgment-dodge", registry.GetCard("standard:bagua").AiTags!["response"]);
    Equal(
        "装备至武器槽；你使用杀时无视目标的防具。",
        registry.GetCard("standard:qinggang_sword").Description);
    Equal("ignore-armor", registry.GetCard("standard:qinggang_sword").AiTags!["modifier"]);
    Equal(14, registry.Generals.Count);
    Equal(1, registry.Decks.Count);
    Equal(2, registry.Modes.Count);
    Equal(CardKind.Slash, registry.GetCard("standard:slash").LegacyKind);
    Equal("standard:jianxiong", registry.Generals["standard:cao-cao"].SkillId);
    Equal("standard:wusheng", registry.Generals["standard:guan-yu"].SkillId);
    Equal("standard:longdan", registry.Generals["standard:zhao-yun"].SkillId);
    TrueWithMessage(registry.GetSkill("standard:yingzi") is
    { Program: not null },
        "current Yingzi uses the shared composition kernel");
    TrueWithMessage(registry.GetSkill("standard:yiji") is
    { Program: not null },
        "current Yiji uses the shared composition kernel");
    Equal("standard:yiji", registry.Generals["standard:guo-jia"].SkillId);
    TrueWithMessage(registry.GetSkill("standard:jieming") is
    { Program: not null },
        "current Jieming uses the shared composition kernel");
    Equal("standard:jieming", registry.Generals["standard:xun-yu"].SkillId);
    Equal("standard:yuanhu", registry.GetSkill("standard:yuanhu").Id);
    Equal("standard:yuanhu", registry.Generals["standard:demo-yuanhu"].SkillId);
    Equal("standard:ganglie", registry.GetSkill("standard:ganglie").Id);
    Equal("standard:ganglie", registry.Generals["standard:demo-ganglie"].SkillId);
    Equal("standard:guicai", registry.GetSkill("standard:guicai").Id);
    Equal("standard:guicai", registry.Generals["standard:demo-guicai"].SkillId);
    Equal(18, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:slash").Count);
    Equal(4, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:duel").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:draw_two").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:barbarian_assault").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:arrow_barrage").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:fire_attack").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:peach_garden").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:five_grains").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:dismantlement").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:snatch").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:fire_slash").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:thunder_slash").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:alcohol").Count);
    Equal(1, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:qinggang_sword").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:nullification").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:iron_chain").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:supply_shortage").Count);

    True(!ReferenceEquals(registry.Cards, secondRegistry.Cards));
    Throws<NotSupportedException>(() =>
        ((IDictionary<string, ContentCardDefinition>)registry.Cards).Add(
            "test:extra",
            new ContentCardDefinition("test:extra", "额外", "测试", "测试")));
}

static void ContentRegistryValidation()
{
    var duplicateId = new ContentCardDefinition(
        "test:duplicate", "重复", "测试", "测试");
    Throws<InvalidOperationException>(() => ContentRegistry.Build(
        new SyntheticPackage("one", builder => builder.AddCard(duplicateId)),
        new SyntheticPackage("two", builder => builder.AddCard(duplicateId))));

    Throws<InvalidOperationException>(() => ContentRegistry.Build(
        new SyntheticPackage("deck", builder => builder.AddDeck(new ContentDeckRecipe(
            "test:deck",
            "坏牌堆",
            1,
            1,
            [new ContentDeckCardCount("missing:card", 1)])))));

    Throws<InvalidOperationException>(() => ContentRegistry.Build(
        new SyntheticPackage(
            "consumer",
            _ => { },
            new PackageDependency("missing", new Version(1, 0, 0)))));

    Throws<InvalidOperationException>(() => ContentRegistry.Build(
        new SyntheticPackage(
            "cycle-a",
            _ => { },
            new PackageDependency("cycle-b", new Version(1, 0, 0))),
            new SyntheticPackage(
            "cycle-b",
            _ => { },
            new PackageDependency("cycle-a", new Version(1, 0, 0)))));

    Throws<InvalidOperationException>(() => ContentRegistry.Build(new SyntheticPackage(
        "identity-team-role",
        builder => builder.AddMode(new ContentModeDefinition(
            "identity:team-role",
            "错误身份局",
            2,
            2,
            new Dictionary<string, int>
            {
                [nameof(Role.Lord)] = 1,
                [nameof(Role.TeamA)] = 1
            })))));

    Throws<InvalidOperationException>(() => ContentRegistry.Build(new SyntheticPackage(
        "national-missing-pool",
        builder => builder.AddMode(new ContentModeDefinition(
            "national:missing-pool",
            "缺少专属武将池的国战",
            4,
            4,
            new Dictionary<string, int>(),
            GeneralCandidateCount: 3,
            ModeKind: ContentModeKind.NationalWarLite,
            FactionCounts: new Dictionary<string, int>
            {
                ["wei"] = 2,
                ["shu"] = 2
            })))));

    var firstContent = ContentRegistry.Build(new SyntheticPackage(
        "hash",
        builder => builder.AddCard(new ContentCardDefinition(
            "hash:card",
            "甲",
            "测试",
            "定义一"))));
    var secondContent = ContentRegistry.Build(new SyntheticPackage(
        "hash",
        builder => builder.AddCard(new ContentCardDefinition(
            "hash:card",
            "乙",
            "测试",
            "定义二"))));
    TrueWithMessage(
        !string.Equals(firstContent.ContentHash, secondContent.ContentHash, StringComparison.Ordinal),
        "same-version package changes produce different content hashes");
}


static void InteractiveSetupPipeline()
{
    var options = new GameOptions
    {
        UseInteractiveDiscard = false,
        Seed = 1_931,
        HumanSeat = 0,
        HumanRole = Role.Lord,
        UseInteractiveSetup = true
    };
    var registry = StandardContentRegistry.Create();
    var left = GameEngine.CreateStandard(options, registry);
    var right = GameEngine.CreateStandard(options, StandardContentRegistry.Create());

    Equal(EngineStatus.NotStarted, left.State.Status);
    True(left.State.Players.All(player => player.HandCount == 0));
    True(left.State.Players.All(player => player.GeneralId == string.Empty));

    var started = left.Submit(new StartGameCommand());
    var rightStarted = right.Submit(new StartGameCommand());
    True(started.Accepted);
    True(rightStarted.Accepted);
    Equal(EngineStatus.AwaitingHumanGeneralSelection, started.State.Status);
    Equal(DecisionKind.SelectGeneral, started.PendingDecision!.Kind);
    Equal(3, started.PendingDecision.ValidContentIds.Count);
    True(started.PendingDecision.ValidContentIds.SequenceEqual(
        rightStarted.PendingDecision!.ValidContentIds));
    True(started.PendingDecision.Choices.All(choice => choice.ContentIds.Count == 1));
    True(started.PendingDecision.PromptId.IsValid);

    var otherView = left.CreateSnapshot(1);
    Equal<PendingDecision?>(null, otherView.PendingDecision);
    True(otherView.Players.All(player => player.GeneralId == string.Empty));
    var otherJson = SnapshotJson.Serialize(otherView);
    True(started.PendingDecision.ValidContentIds.All(
        candidate => !otherJson.Contains(candidate, StringComparison.Ordinal)));

    var beforeInvalid = left.Revision;
    var invalid = left.Submit(new SelectGeneralCommand(
        0,
        "standard:not-a-candidate",
        beforeInvalid,
        started.PendingDecision.PromptId));
    False(invalid.Accepted);
    Equal(CommandErrorCode.InvalidGeneral, invalid.Error!.Code);
    Equal(beforeInvalid, left.Revision);

    var choice = started.PendingDecision.Choices[0];
    var selected = left.Submit(new SelectGeneralCommand(
        0,
        choice.ContentIds.Single(),
        left.Revision,
        started.PendingDecision.PromptId));
    var rightSelected = right.Submit(new SelectGeneralCommand(
        0,
        rightStarted.PendingDecision!.Choices[0].ContentIds.Single(),
        right.Revision,
        rightStarted.PendingDecision.PromptId));

    True(selected.Accepted);
    True(rightSelected.Accepted);
    Equal(EngineStatus.AwaitingHumanPlay, selected.State.Status);
    True(selected.State.Players.Single(player => player.Seat == 0).HandCount >= 4);
    True(selected.State.Players.All(player => player.IsGeneralPublic));
    Equal(
        selected.State.Players.Count,
        selected.State.Players.Select(player => player.GeneralId)
            .Distinct(StringComparer.Ordinal)
            .Count());
    Equal(
        SnapshotJson.Serialize(left.CreateSnapshot(0, revealAll: true)),
        SnapshotJson.Serialize(right.CreateSnapshot(0, revealAll: true)));

    True(left.Events.Any(eventItem => eventItem.Payload is GeneralSelectionRequestedEvent));
    True(left.Events.Any(eventItem => eventItem.Payload is GeneralSelectedEvent));
    True(left.Events.Any(eventItem => eventItem.Payload is SetupCompletedEvent));
    var leftEvents = left.Events.Select(eventItem =>
        $"{eventItem.Sequence}|{eventItem.Payload.GetType().Name}|{eventItem.Payload}");
    var rightEvents = right.Events.Select(eventItem =>
        $"{eventItem.Sequence}|{eventItem.Payload.GetType().Name}|{eventItem.Payload}");
    True(leftEvents.SequenceEqual(rightEvents));
}










static string EventSignature(EventEnvelope eventItem) =>
    JsonSerializer.Serialize(new
    {
        eventItem.Id,
        eventItem.ParentId,
        eventItem.Sequence,
        eventItem.Revision,
        eventItem.CorrelationId,
        Payload = JsonSerializer.Serialize(eventItem.Payload, eventItem.Payload.GetType())
    });

static void CommandJournalReplay()
{
    var options = new GameOptions
    {
        UseInteractiveDiscard = false,
        Seed = 808,
        HumanSeat = 0,
        HumanRole = Role.Lord,
        UseInteractiveSetup = false,
        MaxTurns = 120
    };
    var original = GameEngine.CreateStandard(options, StandardContentRegistry.Create());
    var started = original.Submit(new StartGameCommand(original.Revision));
    TrueWithMessage(started.Accepted, "replay start accepted");
    var endedPlay = original.Submit(new EndPlayPhaseCommand(0, original.Revision));
    TrueWithMessage(endedPlay.Accepted, "replay end-play accepted");

    var journal = original.AcceptedCommands;
    Equal(2, journal.Count);
    var json = CommandJson.Serialize(journal);
    TrueWithMessage(json.Contains("\"$type\": \"start\"", StringComparison.Ordinal), "journal has command discriminator");
    var decoded = CommandJson.Deserialize(json);
    Equal(journal.Count, decoded.Count);

    var replayed = GameReplay.Replay(
        options,
        decoded,
        StandardContentRegistry.Create());
    Equal(SnapshotJson.Serialize(original.CreateSnapshot(0, revealAll: true)),
        SnapshotJson.Serialize(replayed.CreateSnapshot(0, revealAll: true)));
    TrueWithMessage(original.CardMovements.SequenceEqual(replayed.CardMovements), "replay preserves card movements");
    TrueWithMessage(
        original.Events.Select(EventSignature).SequenceEqual(replayed.Events.Select(EventSignature)),
        "replay preserves typed events");
    TrueWithMessage(
        original.AcceptedCommands.SequenceEqual(replayed.AcceptedCommands),
        "replay preserves accepted command journal");
}

static void CheckpointRestore()
{
    var options = new GameOptions
    {
        UseInteractiveDiscard = false,
        Seed = 912,
        HumanSeat = 0,
        HumanRole = Role.Lord,
        UseInteractiveSetup = true,
        MaxTurns = 120
    };
    var original = GameEngine.CreateStandard(options, StandardContentRegistry.Create());
    var started = original.Submit(new StartGameCommand(original.Revision));
    TrueWithMessage(started.Accepted, "checkpoint start accepted");
    var pending = started.PendingDecision;
    NotNull(pending);
    Equal(DecisionKind.SelectGeneral, pending!.Kind);

    var checkpoint = original.CreateCheckpoint();
    Equal(original.Revision, checkpoint.Revision);
    Equal(original.ModeId, checkpoint.ModeId);
    Equal(1, checkpoint.Commands.Count);
    Equal(GameCheckpoint.CurrentRulesVersion, checkpoint.RulesVersion);
    TrueWithMessage(
        checkpoint.ContentPackages.SequenceEqual(["standard@" + StandardContentPackage.CurrentVersion]),
        "checkpoint records the content package signature");
    Equal(StandardContentRegistry.Create().ContentHash, checkpoint.ContentHash);

    var json = GameCheckpointJson.Serialize(checkpoint);
    TrueWithMessage(json.Contains("\"$type\": \"start\"", StringComparison.Ordinal),
        "checkpoint JSON retains command discriminators");
    var decoded = GameCheckpointJson.Deserialize(json);
    var restored = GameReplay.Restore(decoded, StandardContentRegistry.Create());

    Equal(
        SnapshotJson.Serialize(original.CreateSnapshot(0)),
        SnapshotJson.Serialize(restored.CreateSnapshot(0)));
    Equal(
        SnapshotJson.Serialize(original.CreateSnapshot(0, revealAll: true)),
        SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)));
    Equal(JsonSerializer.Serialize(original.Log), JsonSerializer.Serialize(restored.Log));
    Equal(JsonSerializer.Serialize(original.AiThoughts), JsonSerializer.Serialize(restored.AiThoughts));
    Equal(JsonSerializer.Serialize(original.AiGeneralThoughts), JsonSerializer.Serialize(restored.AiGeneralThoughts));
    Equal(JsonSerializer.Serialize(original.CardMovements), JsonSerializer.Serialize(restored.CardMovements));
    TrueWithMessage(
        original.Events.Select(EventSignature).SequenceEqual(restored.Events.Select(EventSignature)),
        "checkpoint restore preserves typed events");
    TrueWithMessage(
        original.AcceptedCommands.SequenceEqual(restored.AcceptedCommands),
        "checkpoint restore preserves the command prefix");

    ThrowsFor<InvalidOperationException>(() => GameReplay.Restore(
        decoded with { ContentHash = "00BAD-CONTENT-HASH" },
        StandardContentRegistry.Create()), "a mismatched content hash");
    ThrowsFor<InvalidOperationException>(() => GameReplay.Restore(
        decoded with { RulesVersion = GameCheckpoint.CurrentRulesVersion + 1 },
        StandardContentRegistry.Create()), "a future rules version");
    ThrowsFor<InvalidOperationException>(() => GameReplay.Restore(
        decoded with { RulesVersion = GameCheckpoint.CurrentRulesVersion - 1 },
        StandardContentRegistry.Create()), "an earlier rules version");

    var selectedGeneralId = pending.ValidContentIds.First();
    var originalSelection = original.Submit(new SelectGeneralCommand(
        0,
        selectedGeneralId,
        original.Revision,
        pending.PromptId));
    var restoredSelection = restored.Submit(new SelectGeneralCommand(
        0,
        selectedGeneralId,
        restored.Revision,
        restored.PendingDecision!.PromptId));
    TrueWithMessage(originalSelection.Accepted && restoredSelection.Accepted,
        "the restored private prompt accepts the same command");
    Equal(
        SnapshotJson.Serialize(original.CreateSnapshot(0, revealAll: true)),
        SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)));
    TrueWithMessage(
        original.Events.Select(EventSignature).SequenceEqual(restored.Events.Select(EventSignature)),
        "continued checkpoint state remains deterministic");

    ThrowsFor<ArgumentNullException>(() => GameEngine.CreateStandard(options, null!),
        "a null content registry at game creation");
    ThrowsFor<ArgumentNullException>(() => GameReplay.Replay(options, checkpoint.Commands, null!),
        "a null content registry at replay");
    ThrowsFor<ArgumentNullException>(() => GameReplay.Restore(decoded, null!),
        "a null content registry at restore");

    var commandRegistry = StandardContentRegistry.Create();
    var commandDriven = GameEngine.CreateStandard(
        new GameOptions { UseInteractiveDiscard = false, Seed = 913 },
        commandRegistry);
    _ = commandDriven.DriveStart();
    var commandCheckpoint = commandDriven.CreateCheckpoint();
    Equal(SnapshotJson.Serialize(commandDriven.CreateSnapshot(0, revealAll: true)),
        SnapshotJson.Serialize(GameReplay.Restore(commandCheckpoint, commandRegistry)
            .CreateSnapshot(0, revealAll: true)));
}

static void CheckpointRejectsSameVersionContentDrift()
{
    var registry = StandardContentRegistry.Create();
    var game = GameEngine.CreateStandard(
        new GameOptions
        {
            UseInteractiveDiscard = false,
            Seed = 914,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true
        },
        registry);
    var started = game.Submit(new StartGameCommand(game.Revision));
    TrueWithMessage(started.Accepted, "content-drift checkpoint start accepted");
    var checkpoint = game.CreateCheckpoint();

    var driftedRegistry = ContentRegistry.Build(new ModifiedStandardContentPackage());
    Equal(
        checkpoint.ContentPackages.Single(),
        $"{driftedRegistry.Packages.Single().Id}@{driftedRegistry.Packages.Single().Version}");
    TrueWithMessage(
        !string.Equals(checkpoint.ContentHash, driftedRegistry.ContentHash, StringComparison.Ordinal),
        "same-version standard content drift changes the registry hash");
    Throws<InvalidOperationException>(() => GameReplay.Restore(checkpoint, driftedRegistry));
}

static void InitialDealCardZones()
{
    var game = GameEngine.CreateStandard(new GameOptions { UseInteractiveDiscard = false, Seed = 211 }, StandardContentRegistry.Create());
    var cards = game.CreateCardZoneDiagnostics();

    Equal(90, cards.Count);
    Equal(90, cards.Select(card => card.CardId).Distinct().Count());
    Equal(58, cards.Count(card => card.Location == CardLocation.DrawPile));
    Equal(0, cards.Count(card => card.Location == CardLocation.Processing));
    Equal(0, cards.Count(card => card.Location == CardLocation.DiscardPile));

    for (var seat = 0; seat < 8; seat++)
    {
        Equal(4, cards.Count(card => card.Location == CardLocation.Hand(seat)));
    }

    Equal(32, game.CardMovements.Count);
    for (var index = 0; index < game.CardMovements.Count; index++)
    {
        var movement = game.CardMovements[index];
        Equal(index + 1, movement.Sequence);
        Equal(CardLocation.DrawPile, movement.From);
        Equal(CardLocation.Hand(index % 8), movement.To);
        Equal(CardMoveReasons.InitialDeal, movement.Reason);
    }
}

static void InvalidCardMovesAreAtomic()
{
    var store = new CardZoneStore(playerCount: 2);
    store.LoadInitialDeck(
    [
        new Card(1, CardKind.Slash, Suit.Spade, 7),
        new Card(2, CardKind.Dodge, Suit.Heart, 2),
        new Card(3, CardKind.Peach, Suit.Diamond, 3)
    ]);

    var beforeInvalidTarget = store.CreateDiagnostics();
    Throws<ArgumentOutOfRangeException>(() =>
        store.Move(1, CardLocation.DrawPile, CardLocation.Hand(99)));
    True(beforeInvalidTarget.SequenceEqual(store.CreateDiagnostics()));

    store.Move(1, CardLocation.DrawPile, CardLocation.Hand(0));
    var beforeInvalidBatch = store.CreateDiagnostics();
    Throws<InvalidOperationException>(() =>
        store.MoveMany([2, 1], CardLocation.DrawPile, CardLocation.Hand(1)));
    True(beforeInvalidBatch.SequenceEqual(store.CreateDiagnostics()));
    True(store.CardsAt(CardLocation.DrawPile) is not List<Card>);
    store.AssertInvariants(expectedCardCount: 3);
}

static void SlashAndDodgeProcessing()
{
    GameEngine? selectedGame = null;
    for (var seed = 1; seed <= 128 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            AdvanceAfterHumanCommands = false,
            UseInteractiveDiscard = false,
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 150
        }, StandardContentRegistry.Create());
        var result = game.DriveStart();
        if (HasSkill(game, "standard:yuanhu"))
        {
            continue;
        }

        var decisions = 0;
        while (result.Status != EngineStatus.Completed && decisions++ < 400)
        {
            if (result.Status == EngineStatus.AwaitingHumanResponse)
            {
                var processingCards = game.CreateCardZoneDiagnostics()
                    .Where(card => card.Location == CardLocation.Processing)
                    .ToArray();
                if (result.PendingDecision is
                    { Kind: DecisionKind.RespondDodge, IncomingCard: CardKind.Slash } &&
                    processingCards.Length == 1 &&
                    processingCards[0].CardKind == CardKind.Slash)
                {
                    selectedGame = game;
                    break;
                }

                result = result.PendingDecision?.Kind == DecisionKind.RespondDodge
                    ? game.DriveHumanRespond(useDodge: false)
                    : game.DriveHumanRespondSlash(useSlash: false);
                continue;
            }

            if (result.Status == EngineStatus.AwaitingHumanDying)
            {
                result = game.DriveHumanRespondDying(usePeach: false);
                continue;
            }

            if (result.Status == EngineStatus.AwaitingHumanCardSelection)
            {
                result = ResolveFirstHarvestChoice(game);
                continue;
            }

            result = result.Status == EngineStatus.AwaitingHumanPlay
                ? game.DriveHumanEndPlay()
                : game.DriveAdvanceOneStep();
        }
    }

    NotNull(selectedGame);
    var pendingGame = selectedGame!;
    var before = pendingGame.State;
    Equal(1, before.ProcessingCardCount);
    var slash = pendingGame.CreateCardZoneDiagnostics()
        .Single(card => card.Location == CardLocation.Processing);
    Equal(CardKind.Slash, slash.CardKind);

    var dodgeId = before.Players.Single(player => player.Seat == 0).Hand
        .First(card => card.Kind == CardKind.Dodge).Id;
    var after = pendingGame.DriveHumanRespond(useDodge: true, advanceToHumanBoundary: false);
    Equal(0, after.State.ProcessingCardCount);
    AssertCardInventory(pendingGame);

    True(pendingGame.CardMovements.Any(movement =>
        movement.CardId == slash.CardId &&
        movement.From.Zone == CardZoneKind.Hand &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Use));
    True(pendingGame.CardMovements.Any(movement =>
        movement.CardId == slash.CardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.UseFinished));
    True(pendingGame.CardMovements.Any(movement =>
        movement.CardId == dodgeId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Respond));
    True(pendingGame.CardMovements.Any(movement =>
        movement.CardId == dodgeId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.ResponseFinished));
}

static void CardInventoryConservation()
{
    // Fixed, previously verified matches retain inventory assertions at every
    // boundary. Specific movement and nested-window cases have focused fixtures.
    foreach (var seed in new[] { 1, 7, 13 })
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            AdvanceAfterHumanCommands = false,
            UseInteractiveDiscard = false,
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 100
        }, StandardContentRegistry.Create());
        game.StateChanged += AssertPublishedCardTotal;

        var result = game.DriveStart();
        AssertCardInventory(game);
        var steps = 0;
        while (result.Status != EngineStatus.Completed && steps++ < 800)
        {
            if (game.PendingDecision?.Kind == DecisionKind.ProgramTrigger)
            {
                result = ResolveIncidentalProgramTrigger(game);
            }
            else if (result.Status == EngineStatus.AwaitingHumanPlay)
            {
                var action = game.GetHumanLegalActions()
                    .FirstOrDefault(candidate => candidate.Kind != LegalActionKind.EndPlay &&
                        candidate.CardId is not null);
                result = action is null
                    ? game.DriveHumanEndPlay(advanceToHumanBoundary: false)
                    : action.Kind == LegalActionKind.Recast
                        ? game.Submit(new RecastCardCommand(0, action.CardId!.Value, game.Revision, game.PendingDecision!.PromptId)).Result
                    : DrivePublishedPlayAction(game, action);
            }
            else if (result.Status == EngineStatus.AwaitingHumanResponse)
            {
                result = game.PendingDecision?.Kind == DecisionKind.RespondSlash
                    ? game.DriveHumanRespondSlash(useSlash: true, advanceToHumanBoundary: false)
                    : game.DriveHumanRespond(useDodge: true, advanceToHumanBoundary: false);
            }
            else if (result.Status == EngineStatus.AwaitingHumanDying)
            {
                result = game.DriveHumanRespondDying(usePeach: false, advanceToHumanBoundary: false);
            }
            else if (result.Status == EngineStatus.AwaitingHumanCardSelection)
            {
                result = ResolveFirstHarvestChoice(game);
            }
            else
            {
                result = game.DriveAdvanceOneStep();
            }

            AssertCardInventory(game);
        }

        TrueWithMessage(result.Status == EngineStatus.Completed && steps < 800,
            $"seed {seed} did not complete the inventory match within {steps} steps");
    }
}

static void ObserversRunPostCommit()
{
    var game = GameEngine.CreateStandard(new GameOptions
    {
        UseInteractiveDiscard = false,
        Seed = 331,
        HumanSeat = 0,
        HumanRole = Role.Lord
    }, StandardContentRegistry.Create());
    var observedStatuses = new List<EngineStatus>();
    var publishedSnapshots = new List<GameSnapshot>();
    game.LogAdded += _ => observedStatuses.Add(game.State.Status);
    game.CardMoved += _ => observedStatuses.Add(game.State.Status);
    game.StateChanged += snapshot => publishedSnapshots.Add(snapshot);

    var result = game.DriveStart();

    Equal(EngineStatus.AwaitingHumanPlay, result.Status);
    True(observedStatuses.Count > 0);
    True(observedStatuses.All(status => status == EngineStatus.AwaitingHumanPlay));
    Equal(1, publishedSnapshots.Count);
    Equal(SnapshotJson.Serialize(result.State), SnapshotJson.Serialize(publishedSnapshots[0]));
}

static void TypedEventStream()
{
    var options = new GameOptions
    {
        UseInteractiveDiscard = false,
        Seed = 929,
        HumanSeat = 0,
        HumanRole = Role.Lord,
        MaxTurns = 80
    };
    var left = GameEngine.CreateStandard(options, StandardContentRegistry.Create());
    var right = GameEngine.CreateStandard(options, StandardContentRegistry.Create());
    var observedStatuses = new List<EngineStatus>();
    left.EventCommitted += _ => observedStatuses.Add(left.State.Status);

    var leftResult = left.DriveStart();
    var rightResult = right.DriveStart();

    True(left.Events.Count > 0);
    True(left.Events[0].Payload is GameStartedEvent started && started.PlayerCount == 8);
    Equal(left.Events.Count, left.Events.Select(eventItem => eventItem.Id).Distinct().Count());
    True(left.Events.Select(eventItem => eventItem.Sequence)
        .SequenceEqual(Enumerable.Range(1, left.Events.Count).Select(value => (long)value)));
    True(left.Events.All(eventItem => eventItem.Revision == left.Revision));
    True(left.Events.Any(eventItem => eventItem.Payload is CardMovedEvent));
    True(left.Events.Any(eventItem => eventItem.Payload is TurnStartedEvent));
    True(observedStatuses.Count > 0);
    True(observedStatuses.All(status => status == leftResult.Status));
    True(!left.SerializeState().Contains("Events", StringComparison.Ordinal));

    var leftSignature = left.Events.Select(eventItem =>
        $"{eventItem.Sequence}|{eventItem.Payload.GetType().Name}|{eventItem.Payload}");
    var rightSignature = right.Events.Select(eventItem =>
        $"{eventItem.Sequence}|{eventItem.Payload.GetType().Name}|{eventItem.Payload}");
    True(leftSignature.SequenceEqual(rightSignature));
    Equal(leftResult.Revision, rightResult.Revision);
}

static void ResolutionFrameStack()
{
    GameEngine? selectedGame = null;
    EngineRunResult result = null!;
    for (var seed = 1; seed <= 128 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            AdvanceAfterHumanCommands = false,
            UseInteractiveDiscard = false,
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 150
        }, StandardContentRegistry.Create());
        result = game.DriveStart();
        if (HasSkill(game, "standard:yuanhu"))
        {
            continue;
        }

        var steps = 0;
        while (result.Status != EngineStatus.Completed && steps++ < 500)
        {
            if (game.PendingDecision?.Kind == DecisionKind.ProgramTrigger)
            {
                result = ResolveIncidentalProgramTrigger(game);
                continue;
            }
            if (result.Status == EngineStatus.AwaitingHumanResponse)
            {
                var processingCards = game.CreateCardZoneDiagnostics()
                    .Where(card => card.Location == CardLocation.Processing)
                    .ToArray();
                if (result.PendingDecision is
                    { Kind: DecisionKind.RespondDodge, IncomingCard: CardKind.Slash } &&
                    processingCards.Length == 1 &&
                    processingCards[0].CardKind == CardKind.Slash)
                {
                    selectedGame = game;
                    break;
                }

                result = result.PendingDecision?.Kind == DecisionKind.RespondDodge
                    ? game.DriveHumanRespond(useDodge: false)
                    : game.DriveHumanRespondSlash(useSlash: false);
                continue;
            }

            result = result.Status switch
            {
                EngineStatus.AwaitingHumanPlay => game.DriveHumanEndPlay(),
                EngineStatus.AwaitingHumanDying => game.DriveHumanRespondDying(usePeach: false),
                EngineStatus.AwaitingHumanCardSelection => ResolveFirstHarvestChoice(game),
                _ => game.DriveAdvanceOneStep()
            };
        }
    }

    NotNull(selectedGame);
    var gameWithResolution = selectedGame!;
    Equal(2, gameWithResolution.ResolutionStack.Count);
    RuntimeFrameStoreChecks.VerifyBoundaries(gameWithResolution);
    var originalResolutionId = gameWithResolution.ResolutionStack[0].Id;
    TrueWithMessage(gameWithResolution.ResolutionStack[0] is CardUseFrame cardUse &&
         cardUse.Step == ResolutionFrameStep.AwaitingResponse,
         $"card frame: {gameWithResolution.ResolutionStack[0]}");
    TrueWithMessage(gameWithResolution.ResolutionStack[1] is ResponseWindowFrame responseWindow &&
         responseWindow.ParentFrameId == gameWithResolution.ResolutionStack[0].Id,
         $"response frame: {gameWithResolution.ResolutionStack[1]}");
    var serializedStack = JsonSerializer.Serialize(gameWithResolution.ResolutionStack);
    TrueWithMessage(serializedStack.Contains("response-window", StringComparison.Ordinal), "response-window serialized");
    TrueWithMessage(!gameWithResolution.SerializeState().Contains("ResolutionStack", StringComparison.Ordinal), "frame stack omitted from public state");

    var prompt = gameWithResolution.PendingDecision!;
    var decline = prompt.Choices.Single(choice => choice.Parameters["response"] == "take-damage");
    var accepted = gameWithResolution.Submit(new AnswerPromptCommand(
        0,
        prompt.PromptId,
        decline.Id,
        gameWithResolution.Revision));
    TrueWithMessage(accepted.Accepted, "decline accepted");
    for (var step = 0; step < 32 &&
         gameWithResolution.ResolutionStack.Any(frame => frame.Id == originalResolutionId); step++)
    {
        if (gameWithResolution.PendingDecision?.Kind == DecisionKind.ProgramTrigger)
        {
            _ = ResolveIncidentalProgramTrigger(gameWithResolution);
        }
        else
        {
            _ = gameWithResolution.DriveAdvanceOneStep();
        }
    }
    TrueWithMessage(gameWithResolution.ResolutionStack.All(frame => frame.Id != originalResolutionId), "original resolution released");
    TrueWithMessage(gameWithResolution.Events.Any(eventItem => eventItem.Payload is CardUseDeclaredEvent), "card use declared");
    TrueWithMessage(gameWithResolution.Events.Any(eventItem => eventItem.Payload is DamageRequestedEvent), "damage requested");
    TrueWithMessage(gameWithResolution.Events.Any(eventItem => eventItem.Payload is AfterDamageEvent), "after damage");
    TrueWithMessage(gameWithResolution.Events.Any(eventItem => eventItem.Payload is CardUseFinishedEvent), "card use finished");
}

static void DuelResponseFlow()
{
    GameEngine? selectedGame = null;
    PendingDecision? duelPrompt = null;
    for (var seed = 1; seed <= 512 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            AdvanceAfterHumanCommands = false,
            UseInteractiveDiscard = false,
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 220
        }, StandardContentRegistry.Create());
        var result = game.DriveStart();
        var steps = 0;
        while (result.Status != EngineStatus.Completed && steps++ < 2_500)
        {
            if (game.PendingDecision?.Kind == DecisionKind.ProgramTrigger)
            {
                result = ResolveIncidentalProgramTrigger(game);
                continue;
            }
            if (result.Status == EngineStatus.AwaitingHumanPlay)
            {
                var duel = game.GetHumanLegalActions()
                    .FirstOrDefault(action => action.Kind == LegalActionKind.Duel);
                result = duel is null
                    ? game.DriveHumanEndPlay(advanceToHumanBoundary: false)
                    : game.DriveHumanPlay(duel.CardId!.Value, duel.TargetSeat, advanceToHumanBoundary: false);
                continue;
            }

            if (result.Status == EngineStatus.AwaitingHumanResponse)
            {
                if (game.PendingDecision is
                    { Kind: DecisionKind.RespondSlash, IncomingCard: CardKind.Duel })
                {
                    selectedGame = game;
                    duelPrompt = game.PendingDecision;
                    break;
                }

                result = game.DriveHumanRespond(useDodge: false, advanceToHumanBoundary: false);
                continue;
            }

            result = result.Status == EngineStatus.AwaitingHumanDying
                ? game.DriveHumanRespondDying(usePeach: false, advanceToHumanBoundary: false)
                : result.Status == EngineStatus.AwaitingHumanCardSelection
                    ? ResolveFirstHarvestChoice(game)
                : game.DriveAdvanceOneStep();
        }
    }

    if (selectedGame is null || duelPrompt is null)
    {
        throw new InvalidOperationException("No deterministic Duel Slash-response prompt was found.");
    }

    var gameWithDuel = selectedGame!;
    var prompt = duelPrompt!;
    Equal(DecisionKind.RespondSlash, prompt.Kind);
    Equal(CardKind.Duel, prompt.IncomingCard);
    Equal(CardKind.Slash, prompt.RequiredCardKind);
    TrueWithMessage(prompt.Choices.Any(choice => choice.Parameters["response"] == "slash"), "duel Slash choice");
    TrueWithMessage(gameWithDuel.ResolutionStack[^1] is ResponseWindowFrame response &&
         response.IncomingCard == CardKind.Duel &&
         response.RequiredCardKind == CardKind.Slash, "duel response frame");
    var duelFrame = gameWithDuel.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.CardKind == CardKind.Duel);
    var otherViewer = gameWithDuel.CreateSnapshot(1);
    Equal<PendingDecision?>(null, otherViewer.PendingDecision);

    var beforeInvalid = gameWithDuel.SerializeState();
    var invalid = gameWithDuel.Submit(new AnswerPromptCommand(
        0,
        prompt.PromptId,
        new ChoiceId("respond.slash.fake"),
        gameWithDuel.Revision));
    TrueWithMessage(!invalid.Accepted, "invalid Duel response rejected");
    Equal(CommandErrorCode.InvalidChoice, invalid.Error!.Code);
    Equal(beforeInvalid, gameWithDuel.SerializeState());

    var slashChoice = prompt.Choices.First(choice => choice.Parameters["response"] == "slash");
    var accepted = gameWithDuel.Submit(new AnswerPromptCommand(
        0,
        prompt.PromptId,
        slashChoice.Id,
        gameWithDuel.Revision));
    TrueWithMessage(accepted.Accepted, "Duel Slash response accepted");
    TrueWithMessage(gameWithDuel.Events.Any(eventItem =>
        eventItem.Payload is DuelResponseEvent response &&
        response.ResolutionId == duelFrame.Id &&
        response.ResponderSeat == 0 &&
        response.UsedSlash), "Duel Slash response event");

    var resultAfterResponse = accepted.Result;
    var stepsAfterResponse = 0;
    while (!gameWithDuel.Events.Any(eventItem =>
               eventItem.Payload is CardUseFinishedEvent finished &&
               finished.ResolutionId == duelFrame.Id) &&
           resultAfterResponse.Status != EngineStatus.Completed &&
           stepsAfterResponse++ < 2_500)
    {
        if (resultAfterResponse.Status == EngineStatus.AwaitingHumanPlay)
        {
            resultAfterResponse = gameWithDuel.DriveHumanEndPlay(advanceToHumanBoundary: false);
        }
        else if (resultAfterResponse.Status == EngineStatus.AwaitingHumanResponse)
        {
            resultAfterResponse = gameWithDuel.PendingDecision?.Kind == DecisionKind.RespondSlash
                ? gameWithDuel.DriveHumanRespondSlash(useSlash: false, advanceToHumanBoundary: false)
                : gameWithDuel.DriveHumanRespond(useDodge: false, advanceToHumanBoundary: false);
        }
        else if (resultAfterResponse.Status == EngineStatus.AwaitingHumanDying)
        {
            resultAfterResponse = gameWithDuel.DriveHumanRespondDying(usePeach: false, advanceToHumanBoundary: false);
        }
        else if (resultAfterResponse.Status == EngineStatus.AwaitingHumanCardSelection)
        {
            resultAfterResponse = ResolveFirstHarvestChoice(gameWithDuel);
        }
        else
        {
            resultAfterResponse = gameWithDuel.DriveAdvanceOneStep();
        }
    }

    TrueWithMessage(gameWithDuel.Events.Any(eventItem =>
        eventItem.Payload is DuelResponseEvent response &&
        response.ResolutionId == duelFrame.Id &&
        !response.UsedSlash), "Duel pass event");
    TrueWithMessage(gameWithDuel.Events.Any(eventItem =>
        eventItem.Payload is DamageRequestedEvent damage &&
        damage.ResolutionId != 0 &&
        damage.SourceCard == CardKind.Duel), "Duel damage event");
    TrueWithMessage(gameWithDuel.Events.Any(eventItem =>
        eventItem.Payload is CardUseFinishedEvent finished &&
        finished.ResolutionId == duelFrame.Id &&
        finished.CardKind == CardKind.Duel), "Duel finished event");
    TrueWithMessage(
        gameWithDuel.ResolutionStack.All(frame => frame.Id != duelFrame.Id),
        $"Duel frame completed (count={gameWithDuel.ResolutionStack.Count}, status={gameWithDuel.State.Status}, pending={gameWithDuel.PendingDecision?.Kind})");
    Equal(
        CardLocation.DiscardPile,
        gameWithDuel.CreateCardZoneDiagnostics()
            .Single(card => card.CardId == duelFrame.CardId)
            .Location);
}


static void NullificationFlow()
{
    GameEngine? aiGame = null;
    for (var seed = 1; seed <= 128 && aiGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            AdvanceAfterHumanCommands = false,
            UseInteractiveDiscard = false,
            Seed = seed,
            HumanSeat = -1,
            HumanRole = null,
            MaxTurns = 250
        }, StandardContentRegistry.Create());
        var result = game.DriveStart();
        if (result.Status == EngineStatus.Completed &&
            game.Events.Any(eventItem => eventItem.Payload is NullificationRespondedEvent))
        {
            aiGame = game;
        }
    }

    NotNull(aiGame);
    var resolvedGame = aiGame!;
    var responses = resolvedGame.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<NullificationRespondedEvent>()
        .ToArray();
    True(responses.Length > 0);
    var resolvedEffects = resolvedGame.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<NullificationResolvedEvent>()
        .ToArray();
    True(resolvedEffects.Any(effect => effect.EffectNullified));
    foreach (var responseGroup in responses.GroupBy(response =>
                 (response.ResolutionId, response.EffectCardId)))
    {
        var ordered = responseGroup.OrderBy(response => response.ChainDepth).ToArray();
        var resolved = resolvedEffects.Single(effect =>
            effect.ResolutionId == responseGroup.Key.ResolutionId &&
            effect.EffectCardId == responseGroup.Key.EffectCardId);
        Equal(ordered.Length, resolved.ChainDepth);
        Equal(ordered.Length % 2 == 1, resolved.EffectNullified);
        True(ordered.Select(response => response.ChainDepth)
            .SequenceEqual(Enumerable.Range(1, ordered.Length)));
        foreach (var response in ordered)
        {
            True(resolvedGame.CardMovements.Any(movement =>
                movement.CardId == response.NullificationCardId &&
                movement.From == CardLocation.Hand(response.ResponderSeat) &&
                movement.To == CardLocation.Processing &&
                movement.Reason == CardMoveReasons.Nullification));
            True(resolvedGame.CardMovements.Any(movement =>
                movement.CardId == response.NullificationCardId &&
                movement.From == CardLocation.Processing &&
                movement.To == CardLocation.DiscardPile &&
                movement.Reason == CardMoveReasons.NullificationFinished));
        }
    }
    AssertCardInventory(resolvedGame);

    GameEngine? humanGame = null;
    PendingDecision? humanPrompt = null;
    for (var seed = 1; seed <= 8_192 && humanGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            AdvanceAfterHumanCommands = false,
            UseInteractiveDiscard = false,
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        }, StandardContentRegistry.Create());
        var result = game.DriveStart();
        if (result.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }

        var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
            candidate.Kind is LegalActionKind.DrawTwo or
                LegalActionKind.BarbarianAssault or
                LegalActionKind.ArrowBarrage or
                LegalActionKind.PeachGarden or
                LegalActionKind.FiveGrains or
                LegalActionKind.Dismantlement or
                LegalActionKind.Snatch or
                LegalActionKind.FireAttack or
                LegalActionKind.Duel);
        if (action is null)
        {
            continue;
        }

        _ = game.DriveHumanPlay(
            action.CardId!.Value,
            action.TargetSeat,
            advanceToHumanBoundary: false,
            targetCardId: action.TargetCardId);
        for (var step = 0; step < 256; step++)
        {
            if (game.PendingDecision?.Kind == DecisionKind.Nullification)
            {
                humanGame = game;
                humanPrompt = game.PendingDecision;
                break;
            }

            if (!game.ResolutionStack.Any(frame => frame is NullificationWindowFrame))
            {
                break;
            }

            game.DriveAdvanceOneStep();
        }
    }

    NotNull(humanGame);
    NotNull(humanPrompt);
    var privateGame = humanGame!;
    var privatePrompt = humanPrompt!;
    Equal(DecisionKind.Nullification, privatePrompt.Kind);
    Equal(CardKind.Nullification, privatePrompt.RequiredCardKind);
    True(privatePrompt.ValidCardIds.Count > 0);
    True(privatePrompt.Choices.Any(choice =>
        choice.Parameters.GetValueOrDefault("response") == "nullification"));
    Equal<PendingDecision?>(null, privateGame.CreateSnapshot(1).PendingDecision);
    var window = privateGame.ResolutionStack.OfType<NullificationWindowFrame>().Single();
    Equal(privatePrompt.IncomingCard, window.EffectCardKind);
    Equal(privatePrompt.SourceSeat, window.SourceSeat);
    True(JsonSerializer.Serialize(privateGame.ResolutionStack)
        .Contains("nullification-window", StringComparison.Ordinal));

    var beforeInvalid = privateGame.SerializeState();
    var invalid = privateGame.Submit(new AnswerPromptCommand(
        0,
        privatePrompt.PromptId,
        new ChoiceId("nullification.fake"),
        privateGame.Revision));
    False(invalid.Accepted);
    Equal(CommandErrorCode.InvalidChoice, invalid.Error!.Code);
    Equal(beforeInvalid, privateGame.SerializeState());

    var useChoice = privatePrompt.Choices.First(choice =>
        choice.Parameters.GetValueOrDefault("response") == "nullification");
    var usedCardId = useChoice.Cards.Single();
    var accepted = privateGame.Submit(new AnswerPromptCommand(
        0,
        privatePrompt.PromptId,
        useChoice.Id,
        privateGame.Revision));
    True(accepted.Accepted);
    ResolveNullificationWindowForTest(privateGame);
    True(privateGame.Events.Any(eventItem =>
        eventItem.Payload is NullificationRespondedEvent response &&
        response.ResponderSeat == 0 &&
        response.NullificationCardId == usedCardId));
    False(SnapshotJson.Serialize(privateGame.CreateSnapshot(1))
        .Contains($"\"Id\": {usedCardId},", StringComparison.Ordinal));
    AssertCardInventory(privateGame);
}




static void PeachGardenFlow()
{
    GameEngine? selectedGame = null;
    LegalAction? selectedGarden = null;
    CardUseFrame? groupFrame = null;
    var injuredSeat = -1;

    for (var seed = 1; seed <= 512 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            AdvanceAfterHumanCommands = false,
            UseInteractiveDiscard = false,
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        }, StandardContentRegistry.Create());
        var result = game.DriveStart();
        var garden = game.GetHumanLegalActions()
            .FirstOrDefault(candidate => candidate.Kind == LegalActionKind.PeachGarden);
        var slash = game.GetHumanLegalActions()
            .FirstOrDefault(candidate =>
                candidate.Kind == LegalActionKind.Slash &&
                candidate.TargetSeat is { } targetSeat &&
                !game.CreateSnapshot(targetSeat).Players.Single(player => player.Seat == targetSeat).Hand
                    .Any(card => card.Kind == CardKind.Dodge));
        if (result.Status != EngineStatus.AwaitingHumanPlay || garden is null || slash is null)
        {
            continue;
        }

        var targetSeatForTest = slash.TargetSeat!.Value;
        game.DriveHumanPlay(slash.CardId!.Value, targetSeatForTest, advanceToHumanBoundary: false);
        ResolveNullificationWindowForTest(game);
        var targetAfterSlash = game.State.Players.Single(player => player.Seat == targetSeatForTest);
        if (targetAfterSlash.Hp != targetAfterSlash.MaxHp - 1)
        {
            continue;
        }

        result = game.DriveAdvanceOneStep();
        garden = game.GetHumanLegalActions()
            .FirstOrDefault(candidate => candidate.Kind == LegalActionKind.PeachGarden);
        if (result.Status != EngineStatus.AwaitingHumanPlay || garden is null)
        {
            continue;
        }

        game.DriveHumanPlay(garden.CardId!.Value, null, advanceToHumanBoundary: false);
        ResolveNullificationWindowForTest(game);
        var currentFrame = game.ResolutionStack.OfType<CardUseFrame>()
            .SingleOrDefault(frame => frame.CardKind == CardKind.PeachGarden);
        if (currentFrame is not null &&
            currentFrame.TargetIndex == 0 &&
            currentFrame.TargetSeats.Count == 8 &&
            game.State.ProcessingCardCount == 1)
        {
            selectedGame = game;
            selectedGarden = garden;
            groupFrame = currentFrame;
            injuredSeat = targetSeatForTest;
        }
    }

    if (selectedGame is null || selectedGarden is null || groupFrame is null || injuredSeat < 0)
    {
        throw new InvalidOperationException("No deterministic PeachGarden recovery boundary was found.");
    }

    var gameWithGarden = selectedGame!;
    var frame = groupFrame!;
    Equal(8, frame.TargetSeats.Count);
    Equal(0, frame.TargetIndex);
    Equal(1, gameWithGarden.State.ProcessingCardCount);
    Equal<PendingDecision?>(null, gameWithGarden.State.PendingDecision);
    Equal<PendingDecision?>(null, gameWithGarden.CreateSnapshot(injuredSeat).PendingDecision);
    True(!gameWithGarden.SerializeState().Contains("ResolutionStack", StringComparison.Ordinal));
    TrueWithMessage(
        gameWithGarden.ResolutionStack[^1] is CardUseFrame cardUse &&
        cardUse.CardKind == CardKind.PeachGarden &&
        cardUse.TargetIndex == 0,
        "PeachGarden card-use frame is paused before the first target");

    var firstStep = gameWithGarden.DriveAdvanceOneStep();
    Equal(EngineStatus.Running, firstStep.Status);
    TrueWithMessage(
        gameWithGarden.ResolutionStack.OfType<CardUseFrame>().Single().TargetIndex == 1,
        "PeachGarden advances one target per engine step");
    Equal(1, gameWithGarden.State.ProcessingCardCount);

    True(gameWithGarden.Events.Any(eventItem =>
        eventItem.Payload is GroupCardUsedEvent used &&
        used.ResolutionId == frame.Id &&
        used.CardKind == CardKind.PeachGarden &&
        used.TargetSeats.SequenceEqual(frame.TargetSeats)));

    var steps = 0;
    while (!gameWithGarden.Events.Any(eventItem =>
               eventItem.Payload is CardUseFinishedEvent finished &&
               finished.ResolutionId == frame.Id) &&
           steps++ < 100)
    {
        gameWithGarden.DriveAdvanceOneStep();
    }

    TrueWithMessage(
        gameWithGarden.Events.Any(eventItem =>
            eventItem.Payload is CardUseFinishedEvent finished &&
            finished.ResolutionId == frame.Id &&
            finished.CardKind == CardKind.PeachGarden),
        "PeachGarden finished event");
    var recovered = gameWithGarden.State.Players.Single(player => player.Seat == injuredSeat);
    Equal(recovered.MaxHp, recovered.Hp);
    TrueWithMessage(
        gameWithGarden.Events.Any(eventItem =>
            eventItem.Payload is RecoveryAppliedEvent recovery &&
            recovery.SourceSeat == 0 &&
            recovery.TargetSeat == injuredSeat &&
            recovery.Amount == 1 &&
            recovery.RemainingHp == recovered.MaxHp),
        "PeachGarden recovery event");
    True(!gameWithGarden.Events.Any(eventItem =>
        eventItem.Payload is DamageRequestedEvent damage &&
        damage.SourceCard == CardKind.PeachGarden));
    True(gameWithGarden.CardMovements.Any(movement =>
        movement.CardId == frame.CardId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Use));
    True(gameWithGarden.CardMovements.Any(movement =>
        movement.CardId == frame.CardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.UseFinished));
    Equal(0, gameWithGarden.ResolutionStack.Count);
    Equal(
        CardLocation.DiscardPile,
        gameWithGarden.CreateCardZoneDiagnostics()
            .Single(card => card.CardId == frame.CardId)
            .Location);
    AssertCardInventory(gameWithGarden);
}

static void FiveGrainsFlow()
{
    GameEngine? selectedGame = null;
    CardUseFrame? draftFrame = null;
    EngineRunResult? afterUse = null;
    for (var seed = 1; seed <= 512 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            AdvanceAfterHumanCommands = false,
            UseInteractiveDiscard = false,
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        }, StandardContentRegistry.Create());
        var result = game.DriveStart();
        var action = game.GetHumanLegalActions()
            .FirstOrDefault(candidate => candidate.Kind == LegalActionKind.FiveGrains);
        if (result.Status != EngineStatus.AwaitingHumanPlay || action is null)
        {
            continue;
        }

        result = game.DriveHumanPlay(action.CardId!.Value, null, advanceToHumanBoundary: false);
        result = ResolveNullificationWindowForTest(game, result);
        var candidateFrame = game.ResolutionStack.OfType<CardUseFrame>()
            .SingleOrDefault(candidate => candidate.CardKind == CardKind.FiveGrains);
        if (result.Status == EngineStatus.AwaitingHumanCardSelection &&
            candidateFrame is not null &&
            candidateFrame.TargetSeats.Count == 8 &&
            candidateFrame.TargetIndex == 0 &&
            result.State.PublicRevealedCards.Count == 8)
        {
            selectedGame = game;
            draftFrame = candidateFrame;
            afterUse = result;
        }
    }

    if (selectedGame is null || draftFrame is null || afterUse is null)
    {
        throw new InvalidOperationException("No deterministic FiveGrains public draft boundary was found.");
    }

    var gameWithDraft = selectedGame!;
    var frame = draftFrame!;
    var startedState = afterUse!.State;
    var prompt = gameWithDraft.PendingDecision!;
    Equal(DecisionKind.SelectHarvestCard, prompt.Kind);
    Equal(EngineStatus.AwaitingHumanCardSelection, startedState.Status);
    Equal(0, frame.TargetIndex);
    Equal(9, startedState.ProcessingCardCount);
    Equal(8, startedState.PublicRevealedCards.Count);
    Equal(8, prompt.ValidCardIds.Count);
    True(prompt.ValidCardIds.OrderBy(id => id)
        .SequenceEqual(startedState.PublicRevealedCards.Select(card => card.Id).OrderBy(id => id)));
    Equal(prompt.ValidCardIds.Count, prompt.Choices.Count);
    True(prompt.Choices.All(choice =>
        choice.Parameters.GetValueOrDefault("action") == "harvest-pick" &&
        choice.Cards.Count == 1 &&
        prompt.ValidCardIds.Contains(choice.Cards[0])));

    var otherViewer = gameWithDraft.CreateSnapshot(1);
    Equal<PendingDecision?>(null, otherViewer.PendingDecision);
    Equal(8, otherViewer.PublicRevealedCards.Count);
    True(otherViewer.Players.Single(player => player.Seat == 0).Hand.Count == 0);
    True(startedState.Players.Single(player => player.Seat == 0).Hand
        .Where(card => !prompt.ValidCardIds.Contains(card.Id))
        .All(card => otherViewer.Players.Single(player => player.Seat == 0).Hand.All(hidden => hidden.Id != card.Id)));
    True(gameWithDraft.SerializeState().Contains("PublicRevealedCards", StringComparison.Ordinal));
    True(!gameWithDraft.SerializeState().Contains("ResolutionStack", StringComparison.Ordinal));
    True(gameWithDraft.ResolutionStack[^1] is CardUseFrame cardUse &&
         cardUse.CardKind == CardKind.FiveGrains &&
         cardUse.TargetIndex == 0);
    True(gameWithDraft.Events.Any(eventItem =>
        eventItem.Payload is CardsRevealedEvent revealed &&
        revealed.ResolutionId == frame.Id &&
        revealed.Cards.Count == 8 &&
        revealed.Cards.Select(card => card.Id).SequenceEqual(startedState.PublicRevealedCards.Select(card => card.Id))));
    True(gameWithDraft.CardMovements.Any(movement =>
        movement.From == CardLocation.DrawPile &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Reveal));

    var beforeInvalid = gameWithDraft.SerializeState();
    var invalid = gameWithDraft.Submit(new AnswerPromptCommand(
        0,
        prompt.PromptId,
        new ChoiceId("harvest.card-fake"),
        gameWithDraft.Revision));
    False(invalid.Accepted);
    Equal(CommandErrorCode.InvalidChoice, invalid.Error!.Code);
    Equal(beforeInvalid, gameWithDraft.SerializeState());

    var selectedCardId = prompt.Choices[0].Cards.Single();
    var afterHumanPick = gameWithDraft.DriveHumanSelectHarvestCard(
        selectedCardId,
        advanceToHumanBoundary: false);
    Equal(EngineStatus.Running, afterHumanPick.Status);
    Equal<PendingDecision?>(null, afterHumanPick.State.PendingDecision);
    Equal(7, afterHumanPick.State.PublicRevealedCards.Count);
    Equal(8, afterHumanPick.State.ProcessingCardCount);
    Equal(1, afterHumanPick.State.Players.Single(player => player.Seat == 0).Hand.Count(card => card.Id == selectedCardId));
    Equal(DecisionKind.SelectHarvestCard, gameWithDraft.CreateSnapshot(1).PendingDecision!.Kind);
    Equal<PendingDecision?>(null, gameWithDraft.CreateSnapshot(2).PendingDecision);

    var guard = 0;
    while (gameWithDraft.ResolutionStack.OfType<CardUseFrame>()
               .Any(cardUse => cardUse.Id == frame.Id) &&
           guard++ < 32)
    {
        True(gameWithDraft.State.Status == EngineStatus.Running);
        gameWithDraft.DriveAdvanceOneStep();
    }

    TrueWithMessage(guard < 32, "FiveGrains draft completes within one step per picker");
    TrueWithMessage(gameWithDraft.Events.Any(eventItem =>
        eventItem.Payload is CardUseFinishedEvent finished &&
        finished.ResolutionId == frame.Id &&
        finished.CardKind == CardKind.FiveGrains), "FiveGrains finished event");
    var selections = gameWithDraft.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<HarvestCardSelectedEvent>()
        .Where(selection => selection.ResolutionId == frame.Id)
        .ToArray();
    Equal(frame.TargetSeats.Count, selections.Length);
    True(selections.Select(selection => selection.PlayerSeat).SequenceEqual(frame.TargetSeats));
    True(selections.All(selection =>
        gameWithDraft.CreateCardZoneDiagnostics()
            .Single(card => card.CardId == selection.CardId)
            .Location == CardLocation.Hand(selection.PlayerSeat)));
    True(gameWithDraft.CardMovements.Any(movement =>
        movement.CardId == selectedCardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.Hand(0) &&
        movement.Reason == CardMoveReasons.HarvestPick));
    True(gameWithDraft.CardMovements.Any(movement =>
        movement.CardId == frame.CardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.UseFinished));
    Equal(0, gameWithDraft.State.PublicRevealedCards.Count);
    Equal(0, gameWithDraft.State.ProcessingCardCount);
    Equal(0, gameWithDraft.ResolutionStack.Count);
    AssertCardInventory(gameWithDraft);
}









static void FireAttackFlow()
{
    GameEngine? selectedGame = null;
    LegalAction? selectedAction = null;
    PendingDecision? revealPrompt = null;
    PendingDecision? discardPrompt = null;
    CardUseFrame? selectedFrame = null;
    for (var seed = 1; seed <= 8_192 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            AdvanceAfterHumanCommands = false,
            UseInteractiveDiscard = false,
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        }, StandardContentRegistry.Create());
        var result = game.DriveStart();
        if (HasSkill(game, "standard:yuanhu"))
        {
            continue;
        }

        if (result.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }

        var action = game.GetHumanLegalActions()
            .FirstOrDefault(candidate =>
                candidate.Kind == LegalActionKind.FireAttack &&
                candidate.TargetSeat is not null and not 0);
        if (action is null)
        {
            continue;
        }

        var targetSeat = action.TargetSeat!.Value;
        var targetSkills = game.CreateSnapshot(0, revealAll: true).Players
            .Single(player => player.Seat == targetSeat).Skills;
        if (targetSkills?.Any(skill => skill.ContentId is "standard:yiji" or "standard:jieming" or "standard:yuanhu") == true)
        {
            continue;
        }

        result = game.DriveHumanPlay(action.CardId!.Value, targetSeat, advanceToHumanBoundary: false);
        if (result.Status != EngineStatus.Running || game.PendingDecision is not null)
        {
            continue;
        }

        var targetView = game.CreateSnapshot(targetSeat);
        var candidateRevealPrompt = targetView.PendingDecision;
        if (candidateRevealPrompt is not { Kind: DecisionKind.FireAttackReveal } ||
            candidateRevealPrompt.ValidCardIds.Count == 0)
        {
            continue;
        }

        var sourceViewBeforeReveal = game.CreateSnapshot(0);
        Equal<PendingDecision?>(null, sourceViewBeforeReveal.PendingDecision);
        foreach (var cardId in candidateRevealPrompt.ValidCardIds)
        {
            False(SnapshotJson.Serialize(sourceViewBeforeReveal).Contains(
                $"\"Id\": {cardId},",
                StringComparison.Ordinal));
        }

        var candidateFrame = game.ResolutionStack.OfType<CardUseFrame>()
            .SingleOrDefault(frame => frame.CardKind == CardKind.FireAttack);
        if (candidateFrame is null)
        {
            continue;
        }

        result = game.DriveAdvanceOneStep();
        var candidateDiscardPrompt = game.PendingDecision;
        if (result.Status != EngineStatus.AwaitingHumanCardSelection ||
            candidateDiscardPrompt is not { Kind: DecisionKind.FireAttackDiscard, PlayerSeat: 0 } ||
            !candidateDiscardPrompt.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("response") == "fire-attack-discard"))
        {
            continue;
        }

        if (game.State.PublicRevealedCards.Count != 1 ||
            game.State.ProcessingCardCount != 1)
        {
            continue;
        }

        selectedGame = game;
        selectedAction = action;
        revealPrompt = candidateRevealPrompt;
        discardPrompt = candidateDiscardPrompt;
        selectedFrame = candidateFrame;
    }

    if (selectedGame is null || selectedAction is null || revealPrompt is null ||
        discardPrompt is null || selectedFrame is null)
    {
        throw new InvalidOperationException("No deterministic FireAttack damage boundary was found.");
    }

    var gameWithFireAttack = selectedGame!;
    var actionToUse = selectedAction!;
    var frame = selectedFrame!;
    var targetSeatAtBoundary = actionToUse.TargetSeat!.Value;
    var revealedEvent = gameWithFireAttack.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<FireAttackCardRevealedEvent>()
        .Single(eventItem => eventItem.ResolutionId == frame.Id);
    var discardChoice = discardPrompt.Choices.First(choice =>
        choice.Parameters.GetValueOrDefault("response") == "fire-attack-discard");
    var matchingDiscardCardId = discardChoice.Cards.Single();
    var sourceBefore = gameWithFireAttack.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == 0);
    var targetBefore = gameWithFireAttack.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == targetSeatAtBoundary);
    var ordinaryViewer = gameWithFireAttack.CreateSnapshot(1);
    TrueWithMessage(ordinaryViewer.PublicRevealedCards.Count == 1, "FireAttack publishes the revealed card");
    TrueWithMessage(targetBefore.Hand.Any(card => card.Id == revealedEvent.CardId),
        "formal FireAttack keeps the revealed card in the target hand");
    TrueWithMessage(
        ordinaryViewer.Players.SelectMany(player => player.Hand).All(card => card.Id != matchingDiscardCardId),
        "source discard remains private in an ordinary viewer");

    var invalid = gameWithFireAttack.Submit(new AnswerPromptCommand(
        0,
        discardPrompt.PromptId,
        new ChoiceId("fire-attack.fake"),
        gameWithFireAttack.Revision));
    False(invalid.Accepted);
    Equal(CommandErrorCode.InvalidChoice, invalid.Error!.Code);

    var accepted = gameWithFireAttack.Submit(new AnswerPromptCommand(
        0,
        discardPrompt.PromptId,
        discardChoice.Id,
        gameWithFireAttack.Revision));
    TrueWithMessage(accepted.Accepted, "FireAttack discard choice accepted");
    accepted = gameWithFireAttack.Submit(new AdvanceCommand(gameWithFireAttack.Revision));
    TrueWithMessage(accepted.Accepted, "FireAttack discard continuation accepted");
    Equal(EngineStatus.AwaitingHumanPlay, accepted.State.Status);
    Equal(0, accepted.State.ProcessingCardCount);
    Equal(0, gameWithFireAttack.ResolutionStack.Count);
    Equal(0, accepted.State.PublicRevealedCards.Count);

    var resolved = gameWithFireAttack.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<FireAttackResolvedEvent>()
        .Single(eventItem => eventItem.ResolutionId == frame.Id);
    Equal(revealedEvent.CardId, resolved.RevealedCardId);
    Equal(revealedEvent.Suit, resolved.RevealedSuit);
    Equal(matchingDiscardCardId, resolved.MatchingDiscardCardId);
    True(resolved.CausedDamage);
    TrueWithMessage(gameWithFireAttack.Events.Any(eventItem =>
        eventItem.Payload is DamageRequestedEvent damage &&
        damage.SourceSeat == 0 &&
        damage.TargetSeat == targetSeatAtBoundary &&
        damage.Amount == 1 &&
        damage.SourceCard == CardKind.FireAttack &&
        damage.Nature == DamageNature.Fire), "FireAttack emits typed fire damage");
    var targetAfter = gameWithFireAttack.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == targetSeatAtBoundary);
    Equal(targetBefore.Hp - 1, targetAfter.Hp);
    Equal(targetBefore.HandCount, targetAfter.HandCount);
    TrueWithMessage(targetAfter.Hand.Any(card => card.Id == revealedEvent.CardId),
        "formal FireAttack reveal remains in hand after damage");
    var sourceAfter = gameWithFireAttack.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == 0);
    TrueWithMessage(
        sourceBefore.HandCount - 1 == sourceAfter.HandCount,
        $"FireAttack source hand expected {sourceBefore.HandCount - 1}, actual {sourceAfter.HandCount}, " +
        $"discard card {matchingDiscardCardId} location " +
        $"{gameWithFireAttack.CreateCardZoneDiagnostics().Single(card => card.CardId == matchingDiscardCardId).Location}");

    TrueWithMessage(gameWithFireAttack.CardMovements.Any(movement =>
        movement.CardId == actionToUse.CardId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Use), "FireAttack effect enters Processing");
    False(gameWithFireAttack.CardMovements.Any(movement =>
        movement.CardId == revealedEvent.CardId &&
        movement.From == CardLocation.Hand(targetSeatAtBoundary) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.FireAttackReveal));
    TrueWithMessage(gameWithFireAttack.CardMovements.Any(movement =>
        movement.CardId == matchingDiscardCardId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.FireAttackDiscard), "FireAttack same-suit discard movement");
    False(gameWithFireAttack.CardMovements.Any(movement =>
        movement.CardId == revealedEvent.CardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.FireAttackFinished));
    TrueWithMessage(gameWithFireAttack.CardMovements.Any(movement =>
        movement.CardId == matchingDiscardCardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.FireAttackDiscardFinished), "FireAttack source discard finishes");
    TrueWithMessage(gameWithFireAttack.Events.Any(eventItem =>
        eventItem.Payload is CardUseFinishedEvent finished &&
        finished.ResolutionId == frame.Id &&
        finished.CardKind == CardKind.FireAttack), "FireAttack finished event");
    TrueWithMessage(gameWithFireAttack.AiThoughts.Any(thought =>
        thought.Candidates.Any(candidate =>
            candidate.Action.Kind == LegalActionKind.FireAttackReveal &&
            candidate.Reason.Contains("自己的私有手牌", StringComparison.Ordinal))),
        "FireAttack reveal AI uses its private snapshot");
    AssertCardInventory(gameWithFireAttack);
}
























static void DyingResponseFlow()
{
    GameEngine? selectedGame = null;
    PendingDecision? dyingPrompt = null;
    EngineRunResult result = null!;
    for (var seed = 1; seed <= 512 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            AdvanceAfterHumanCommands = false,
            UseInteractiveDiscard = false,
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        }, StandardContentRegistry.Create());
        result = game.DriveStart();
        var steps = 0;
        while (result.Status != EngineStatus.Completed && steps++ < 2_000)
        {
            if (result.Status == EngineStatus.AwaitingHumanDying)
            {
                var prompt = game.PendingDecision;
                if (prompt is { Choices.Count: > 1 } &&
                    prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("response") == "peach"))
                {
                    selectedGame = game;
                    dyingPrompt = prompt;
                    break;
                }
            }

            result = result.Status switch
            {
                EngineStatus.AwaitingHumanPlay => game.DriveHumanEndPlay(advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanResponse => game.PendingDecision?.Kind == DecisionKind.RespondSlash
                    ? game.DriveHumanRespondSlash(useSlash: false, advanceToHumanBoundary: false)
                    : game.DriveHumanRespond(useDodge: false, advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanDying => game.DriveHumanRespondDying(usePeach: false, advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanCardSelection => ResolveFirstHarvestChoice(game),
                _ => game.DriveAdvanceOneStep()
            };
        }
    }

    if (selectedGame is null || dyingPrompt is null)
    {
        throw new InvalidOperationException("No human dying prompt with a Peach was found in the deterministic seed search.");
    }
    var gameWithDying = selectedGame!;
    var promptAtBoundary = dyingPrompt!;
    Equal(EngineStatus.AwaitingHumanDying, gameWithDying.State.Status);
    Equal(DecisionKind.RescueDying, promptAtBoundary.Kind);
    Equal(promptAtBoundary.TargetSeat, promptAtBoundary.SourceSeat);
    TrueWithMessage(promptAtBoundary.Choices.Any(choice => choice.Parameters["response"] == "peach"), "peach choice");
    TrueWithMessage(gameWithDying.ResolutionStack[^1] is DyingFrame dyingFrame &&
         gameWithDying.ResolutionStack[^2] is DamageFrame damageFrame &&
         dyingFrame.ParentFrameId == damageFrame.Id, "dying stack");
    var serializedDyingStack = JsonSerializer.Serialize(gameWithDying.ResolutionStack);
    TrueWithMessage(serializedDyingStack.Contains("dying", StringComparison.Ordinal), "dying stack serialization");

    var otherViewer = gameWithDying.CreateSnapshot(1);
    Equal<PendingDecision?>(null, otherViewer.PendingDecision);
    var hiddenPeachId = promptAtBoundary.Choices
        .First(choice => choice.Parameters["response"] == "peach")
        .Cards.Single();
    TrueWithMessage(otherViewer.Players.All(player => player.Hand.All(card => card.Id != hiddenPeachId)), "other viewer hides peach");

    var beforeInvalidChoice = gameWithDying.SerializeState();
    var invalidChoice = gameWithDying.Submit(new AnswerPromptCommand(
        gameWithDying.State.HumanSeat,
        promptAtBoundary.PromptId,
        new ChoiceId("dying.fake"),
        gameWithDying.Revision));
    TrueWithMessage(!invalidChoice.Accepted, "invalid dying choice rejected");
    Equal(CommandErrorCode.InvalidChoice, invalidChoice.Error!.Code);
    Equal(beforeInvalidChoice, gameWithDying.SerializeState());

    var originalRevision = gameWithDying.Revision;
    var peachChoice = promptAtBoundary.Choices
        .First(choice => choice.Parameters["response"] == "peach");
    var rescued = gameWithDying.Submit(new AnswerPromptCommand(
        gameWithDying.State.HumanSeat,
        promptAtBoundary.PromptId,
        peachChoice.Id,
        originalRevision));
    TrueWithMessage(rescued.Accepted, "peach command accepted");
    TrueWithMessage(gameWithDying.Events.Any(eventItem =>
        eventItem.Payload is DyingResponseEvent response &&
        response.UsedPeach && response.PeachCardId == hiddenPeachId), "dying peach event");
    TrueWithMessage(gameWithDying.Events.Any(eventItem =>
        eventItem.Payload is DyingResolvedEvent resolved && resolved.Survived), "dying survived event");
    TrueWithMessage(gameWithDying.ResolutionStack.All(frame =>
        frame is not DyingFrame dying || dying.VictimSeat != promptAtBoundary.TargetSeat),
        "completed dying frame removed");
    TrueWithMessage(gameWithDying.CardMovements.Any(movement =>
        movement.CardId == hiddenPeachId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.UseFinished),
        "rescue Peach discarded through the recovery resolution");
    var recovery = gameWithDying.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<RecoveryAppliedEvent>()
        .LastOrDefault(eventItem =>
            eventItem.TargetSeat == promptAtBoundary.TargetSeat &&
            eventItem.Amount == 1);
    NotNull(recovery);
    TrueWithMessage(recovery!.RemainingHp > 0,
        $"dying Peach recovery applied at hp={recovery.RemainingHp}");
}

static void ObserverFailuresAreIsolated()
{
    var game = GameEngine.CreateStandard(new GameOptions
    {
        AdvanceAfterHumanCommands = false,
        UseInteractiveDiscard = false,
        Seed = 337,
        HumanSeat = 0,
        HumanRole = Role.Lord,
        MaxTurns = 100
    }, StandardContentRegistry.Create());
    var deliveredLogs = 0;
    var deliveredThoughts = 0;
    var deliveredStates = 0;
    var deliveredMovements = 0;

    game.LogAdded += _ => throw new InvalidOperationException("log observer failed");
    game.LogAdded += _ => deliveredLogs++;
    game.AiThoughtAdded += _ => throw new InvalidOperationException("thought observer failed");
    game.AiThoughtAdded += _ => deliveredThoughts++;
    game.StateChanged += _ => throw new InvalidOperationException("state observer failed");
    game.StateChanged += _ => deliveredStates++;
    game.CardMoved += _ => throw new InvalidOperationException("movement observer failed");
    game.CardMoved += _ => deliveredMovements++;

    var result = game.DriveStart();
    Equal(EngineStatus.AwaitingHumanPlay, result.Status);
    True(deliveredLogs > 0);
    True(deliveredStates > 0);
    True(deliveredMovements > 0);

    result = game.DriveHumanEndPlay(advanceToHumanBoundary: false);
    for (var step = 0; step < 8 && deliveredThoughts == 0; step++)
    {
        result = game.DriveAdvanceOneStep();
    }

    True(deliveredThoughts > 0);
    True(game.ObserverFailures.Any(failure => failure.NotificationType == nameof(game.LogAdded)));
    True(game.ObserverFailures.Any(failure => failure.NotificationType == nameof(game.AiThoughtAdded)));
    True(game.ObserverFailures.Any(failure => failure.NotificationType == nameof(game.StateChanged)));
    True(game.ObserverFailures.Any(failure => failure.NotificationType == nameof(game.CardMoved)));
    AssertCardInventory(game);
    True(result.Status != EngineStatus.NotStarted);
}








static void ReentrantAdvanceIsRejected()
{
    var game = GameEngine.CreateStandard(new GameOptions
    {
        UseInteractiveDiscard = false,
        Seed = 71,
        HumanSeat = 0,
        HumanRole = Role.Lord
    }, StandardContentRegistry.Create());
    var rejected = false;
    game.LogAdded += entry =>
    {
        if (entry.Type != "CardsDrawn")
        {
            return;
        }

        rejected = game.Submit(new AdvanceCommand(game.Revision)).Error?.Code ==
            CommandErrorCode.ReentrantOperation;
    };

    var result = game.DriveStart();
    True(rejected);
    Equal(EngineStatus.AwaitingHumanPlay, result.Status);
    Equal(Winner.None, result.Winner);
    Equal(TurnPhase.Play, result.State.Phase);
}

static void UnknownPhaseFailsFast()
{
    var game = GameEngine.CreateStandard(new GameOptions
    {
        AdvanceAfterHumanCommands = false,
        UseInteractiveDiscard = false,
        Seed = 79,
        HumanSeat = 0,
        HumanRole = Role.Lord
    }, StandardContentRegistry.Create());
    game.DriveStart();
    game.DriveHumanEndPlay(advanceToHumanBoundary: false);

    var phaseField = typeof(GameEngine).GetField("_phase", BindingFlags.Instance | BindingFlags.NonPublic);
    NotNull(phaseField);
    phaseField!.SetValue(game, (TurnPhase)999);

    Throws<InvalidOperationException>(() => game.DriveAdvanceOneStep());
}



static void SnapshotSerialization()
{
    var game = GameEngine.CreateStandard(new GameOptions { UseInteractiveDiscard = false, Seed = 123 }, StandardContentRegistry.Create());
    using var json = JsonDocument.Parse(game.SerializeState());
    Equal("Lord", json.RootElement.GetProperty("Players")[0].GetProperty("Role").GetString());
    Equal(8, json.RootElement.GetProperty("Players").GetArrayLength());
}


static bool HasSkill(GameEngine game, string skill) =>
    game.CreateSnapshot(0, revealAll: true).Players.Any(player => player.Skills?.Any(entry => entry.ContentId == skill) == true);


static void AssertCardInventory(GameEngine game)
{
    var cards = game.CreateCardZoneDiagnostics();
    Equal(cards.Count, cards.Select(card => card.CardId).Distinct().Count());

    var snapshot = game.CreateSnapshot(0, revealAll: true);
    Equal(snapshot.DrawPileCount, cards.Count(card => card.Location == CardLocation.DrawPile));
    Equal(snapshot.DiscardPileCount, cards.Count(card => card.Location == CardLocation.DiscardPile));
    Equal(snapshot.ProcessingCardCount, cards.Count(card => card.Location == CardLocation.Processing));
    foreach (var player in snapshot.Players)
    {
        var handCards = cards
            .Where(card => card.Location == CardLocation.Hand(player.Seat))
            .Select(card => card.CardId)
            .OrderBy(id => id)
            .ToArray();
        var snapshotCards = player.Hand.Select(card => card.Id).OrderBy(id => id).ToArray();
        Equal(player.HandCount, handCards.Length);
        TrueWithMessage(
            handCards.SequenceEqual(snapshotCards),
            $"hand snapshot mismatch for seat {player.Seat}: zone=[{string.Join(',', handCards)}], snapshot=[{string.Join(',', snapshotCards)}]");

        var equipmentCards = cards
            .Where(card => card.Location == CardLocation.Equipment(player.Seat))
            .OrderBy(card => card.CardId)
            .ToArray();
        var snapshotEquipment = player.Equipment
            .OrderBy(card => card.Id)
            .ToArray();
        Equal(equipmentCards.Length, player.Equipment.Count);
        TrueWithMessage(
            equipmentCards.Select(card => card.CardId).SequenceEqual(snapshotEquipment.Select(card => card.Id)),
            $"equipment snapshot mismatch for seat {player.Seat}: zone=[{string.Join(',', equipmentCards.Select(card => card.CardId))}], snapshot=[{string.Join(',', snapshotEquipment.Select(card => card.Id))}]");
        TrueWithMessage(
            equipmentCards.All(card => EquipmentCatalog.IsEquipment(card.CardKind)),
            $"non-equipment card in equipment zone for seat {player.Seat}");
        TrueWithMessage(
            equipmentCards.Select(card => EquipmentCatalog.Get(card.CardKind).Slot).Distinct().Count() == equipmentCards.Length,
            $"duplicate equipment slot for seat {player.Seat}");

        var judgmentCards = cards
            .Where(card => card.Location == CardLocation.Judgment(player.Seat))
            .OrderBy(card => card.CardId)
            .ToArray();
        var snapshotJudgment = player.Judgment
            .OrderBy(card => card.Id)
            .ToArray();
        Equal(judgmentCards.Length, snapshotJudgment.Length);
        TrueWithMessage(
            judgmentCards.Select(card => card.CardId).SequenceEqual(snapshotJudgment.Select(card => card.Id)),
            $"judgment snapshot mismatch for seat {player.Seat}: zone=[{string.Join(',', judgmentCards.Select(card => card.CardId))}], snapshot=[{string.Join(',', snapshotJudgment.Select(card => card.Id))}]");
        TrueWithMessage(
            judgmentCards.All(card =>
                card.CardKind is CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning ||
                game.ResolutionStack.OfType<JudgmentFrame>()
                    .Any(frame => frame.CardId == card.CardId)),
            $"unexpected card in judgment zone for seat {player.Seat}");
    }

    if (snapshot.Status == EngineStatus.Completed)
    {
        Equal(0, snapshot.ProcessingCardCount);
    }
}

static EngineRunResult ResolveFirstHarvestChoice(GameEngine game)
{
    var prompt = game.PendingDecision ??
        throw new InvalidOperationException("The test expected a human FiveGrains prompt.");
    var choice = prompt.Choices.FirstOrDefault() ??
        throw new InvalidOperationException("The test expected at least one FiveGrains choice.");
    return prompt.Kind switch
    {
        DecisionKind.SelectTargetCard => game.DriveHumanSelectTargetCardSlot(
            int.Parse(choice.Parameters["slot-index"], System.Globalization.CultureInfo.InvariantCulture),
            advanceToHumanBoundary: false),
        DecisionKind.FireAttackReveal or DecisionKind.FireAttackDiscard => game.DriveHumanSelectFireAttackCard(
            choice.Cards.Single(),
            advanceToHumanBoundary: false),
        _ => game.DriveHumanSelectHarvestCard(choice.Cards.Single(), advanceToHumanBoundary: false)
    };
}

static EngineRunResult ResolveIncidentalProgramTrigger(GameEngine game)
{
    var prompt = game.PendingDecision ??
        throw new InvalidOperationException("The test expected a published skill trigger.");
    Equal(DecisionKind.ProgramTrigger, prompt.Kind);
    var choice = prompt.Choices.FirstOrDefault(candidate =>
            candidate.Parameters.GetValueOrDefault("program-action") == "skip") ??
        prompt.Choices.FirstOrDefault(candidate =>
            candidate.Parameters.GetValueOrDefault("program-action") == "activate") ??
        prompt.Choices.FirstOrDefault() ??
        throw new InvalidOperationException("The published skill trigger has no executable choice.");
    var answer = game.Submit(new AnswerPromptCommand(
        prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision));
    TrueWithMessage(answer.Accepted, "published skill trigger choice accepted");
    return answer.Result;
}

static EngineRunResult DrivePublishedPlayAction(GameEngine game, LegalAction action)
{
    var prompt = game.PendingDecision ??
        throw new InvalidOperationException("The test expected a published play prompt.");
    var accepted = game.Submit(new PlayCardCommand(
        prompt.PlayerSeat, action.CardId!.Value, action.TargetSeats,
        game.Revision, prompt.PromptId, action.PlayedCardKind, action.TargetCardId)
    {
        ConversionSource = action.ConversionSource,
        AdditionalConversionSources = action.AdditionalConversionSources,
    });
    TrueWithMessage(accepted.Accepted, "published exact play action accepted");
    return accepted.Result;
}

static EngineRunResult ResolveNullificationWindowForTest(
    GameEngine game,
    EngineRunResult? initial = null)
{
    var result = initial ?? new EngineRunResult(
        game.State.Status,
        game.State.Winner,
        game.State,
        game.PendingDecision,
        game.Revision);
    for (var step = 0;
         step < 256 && game.ResolutionStack.Any(frame =>
             frame is NullificationWindowFrame or TargetCardSelectionFrame);
         step++)
    {
        result = game.PendingDecision?.Kind switch
        {
            DecisionKind.Nullification => game.DriveHumanRespondNullification(
                useNullification: false,
                advanceToHumanBoundary: false),
            DecisionKind.SelectTargetCard => game.DriveHumanSelectTargetCardSlot(
                int.Parse(
                    game.PendingDecision.Choices.First().Parameters["slot-index"],
                    System.Globalization.CultureInfo.InvariantCulture),
                advanceToHumanBoundary: false),
            _ => game.DriveAdvanceOneStep()
        };
    }

    if (game.ResolutionStack.Any(frame =>
            frame is NullificationWindowFrame or TargetCardSelectionFrame))
    {
        throw new InvalidOperationException("The test Nullification window did not settle.");
    }

    return result;
}

static void AssertPublishedCardTotal(GameSnapshot snapshot)
{
    Equal(
        90,
        snapshot.DrawPileCount +
        snapshot.DiscardPileCount +
        snapshot.ProcessingCardCount +
        snapshot.Players.Sum(player =>
            player.HandCount + player.Equipment.Count + player.Judgment.Count));
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

static void True(bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException("Expected condition to be true.");
    }
}

static void TrueWithMessage(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException($"Expected condition to be true: {message}.");
    }
}

static void False(bool condition)
{
    if (condition)
    {
        throw new InvalidOperationException("Expected condition to be false.");
    }
}

static void NotNull(object? value)
{
    if (value is null)
    {
        throw new InvalidOperationException("Expected a non-null value.");
    }
}

static void Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name} to be thrown.");
}

static void ThrowsFor<TException>(Action action, string expectation)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(
        $"Expected {typeof(TException).Name} to be thrown for {expectation}.");
}

sealed class SyntheticPackage : IGameContentPackage
{
    private readonly Action<IContentRegistryBuilder> _register;

    public SyntheticPackage(
        string id,
        Action<IContentRegistryBuilder> register,
        params PackageDependency[] dependencies)
    {
        _register = register;
        Manifest = new PackageManifest(id, new Version(1, 0, 0), dependencies);
    }

    public PackageManifest Manifest { get; }

    public void Register(IContentRegistryBuilder builder) => _register(builder);
}

sealed class ModifiedStandardContentPackage : IGameContentPackage
{
    private readonly StandardContentPackage _inner = new();

    public PackageManifest Manifest => _inner.Manifest;

    public void Register(IContentRegistryBuilder builder) =>
        _inner.Register(new ModifiedStandardContentBuilder(builder));
}

sealed class ModifiedStandardContentBuilder(IContentRegistryBuilder inner) : IContentRegistryBuilder
{
    public void AddCard(ContentCardDefinition definition) => inner.AddCard(
        definition.Id == "standard:slash"
            ? definition with { Description = $"{definition.Description}（同版本内容漂移）" }
            : definition);

    public void AddSkill(ContentSkillDefinition definition) => inner.AddSkill(definition);

    public void AddGeneral(ContentGeneralDefinition definition) => inner.AddGeneral(definition);

    public void AddDeck(ContentDeckRecipe definition) => inner.AddDeck(definition);

    public void AddMode(ContentModeDefinition definition) => inner.AddMode(definition);
}
