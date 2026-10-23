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
    ("Multiplayer: human seats prompts privacy and cold replay", MultiplayerChecks.MultipleHumansWaitRespondAndReplay),
    ("LAN rooms: HTTP players AI isolation retries and match completion", LanRoomChecks.HttpPlayersAiAndIsolation),
    ("LAN rooms: HTTP first round permissions retries and eight seats", LanRoomChecks.HttpFirstRound),
    ("Bagua response: failed judgment hand Dodge and cold replay", BaguaResponseChecks.FailedJudgmentReturnsToDodgeAndColdReplay),
    ("Bagua response: decline armor and red judgment retain hand Dodge", BaguaResponseChecks.DeclineAndRedJudgmentKeepHandDodgeOptional),
    ("Wushuang shop: mixed goods no mounts and ephemeral equipment", WushuangModeChecks.MixedShopNoMountsAndEphemeralEquipment),
    ("Wushuang mode: native shop growth acquired skills replacement and replay", WushuangModeChecks.NativeShopGrowthAcquiredSkillsReplacementAndReplay),
    ("Wushuang mode: explicit clock expires paid child and cold replay", WushuangModeChecks.ExplicitClockExpiresPaidChildAndColdReplay),
    ("Wushuang skills B: OfficialFuqiNonTargetResponseAndGuanchaoTrueUse", WushuangSkillsBChecks.OfficialFuqiNonTargetResponseAndGuanchaoTrueUse),
    ("Wushuang skills B: OfficialGuzhengAndDuwuVersionDifferences", WushuangSkillsBChecks.OfficialGuzhengAndDuwuVersionDifferences),
    ("Wushuang skills A: YashiSuppressesItsActualSelfSourceUntilTheNextStart", WushuangOfficialSkillsAChecks.YashiSuppressesItsActualSelfSourceUntilTheNextStart),
    ("Wushuang skills A: TiaoxinExpandsOnlyAfterItsRequestedSlashFailsToDamage", WushuangOfficialSkillsAChecks.TiaoxinExpandsOnlyAfterItsRequestedSlashFailsToDamage),
    ("Wushuang skills A: OfficialAuditKeepsSelfGiftsPublicEquipmentCostsAndPhaseLimits", WushuangOfficialSkillsAChecks.OfficialAuditKeepsSelfGiftsPublicEquipmentCostsAndPhaseLimits),
    ("Wushuang skills A: OriginalYijiAndGuanxuKeepPrivatePhysicalEntitiesAcrossColdReplay", WushuangOfficialSkillsAChecks.OriginalYijiAndGuanxuKeepPrivatePhysicalEntitiesAcrossColdReplay),
    ("Wushuang skills A: ChoufaRewritesOnlyFrozenHandEntitiesWithoutInspectingTheirNames", WushuangOfficialSkillsAChecks.ChoufaRewritesOnlyFrozenHandEntitiesWithoutInspectingTheirNames),
    ("Wushuang skills A: TairanRepaysActualRecoveredHpAndOnlyItsStillHeldDrawnCards", WushuangOfficialSkillsAChecks.TairanRepaysActualRecoveredHpAndOnlyItsStillHeldDrawnCards),
    ("Wushuang skills A: LingrenBindsItsDamageToTheGuessedUseAndCiweiCancelsOnlySecondUse", WushuangOfficialSkillsAChecks.LingrenBindsItsDamageToTheGuessedUseAndCiweiCancelsOnlySecondUse),
    ("Wushuang skills A: JinzhiPaysIncreasingSameColorCountsAndDrawsBeforeNativeBasicEffects", WushuangOfficialSkillsAChecks.JinzhiPaysIncreasingSameColorCountsAndDrawsBeforeNativeBasicEffects),
    ("Wushuang combat: NoArmorFixedDamageAndFiniteReduction", WushuangCombatChecks.NoArmorFixedDamageAndFiniteReduction),
    ("Wushuang combat: DrawTwoAndAdditionalDismantlementKeepNativePaymentsCold", WushuangCombatChecks.DrawTwoAndAdditionalDismantlementKeepNativePaymentsCold),
    ("Wushuang combat: RecastAndActualDrawThresholdResumeTheirPaidInstruction", WushuangCombatChecks.RecastAndActualDrawThresholdResumeTheirPaidInstruction),
    ("Wushuang combat: LightningJudgmentsUseNativeReplacementAndSourcelessDamage", WushuangCombatChecks.LightningJudgmentsUseNativeReplacementAndSourcelessDamage),
    ("Wushuang combat: DyingExitDamageWaitsForItsNativePaidChildCold", WushuangCombatChecks.DyingExitDamageWaitsForItsNativePaidChildCold),
    ("Wushuang audit combat: JieyingClaimsOnlyAfterNativeTurnEndedAndResumesGainChildCold", WushuangAuditCombatChecks.JieyingClaimsOnlyAfterNativeTurnEndedAndResumesGainChildCold),
    ("Wushuang audit combat: WangongKeepsLastActualBasicAcrossTurnsAndCapturesAcceptedSlash", WushuangAuditCombatChecks.WangongKeepsLastActualBasicAcrossTurnsAndCapturesAcceptedSlash),
    ("Wushuang pools: OfficialPoolsPricesVersionsAndAcquisitionRecovery", WushuangModeChecks.OfficialPoolsPricesVersionsAndAcquisitionRecovery),
    ("Wushuang pools: FreeRefreshCapacityAndOriginalReplacement", WushuangModeChecks.FreeRefreshCapacityAndOriginalReplacement),
    ("Wushuang pools: OriginalQimouAndHunZiVersions", WushuangModeChecks.OriginalQimouAndHunZiVersions),
    ("Wushuang pools: TacticQuotaFloorsAndEmptySlotsUseActualRules", WushuangModeChecks.TacticQuotaFloorsAndEmptySlotsUseActualRules),
    ("Wushuang skills B: WeiyiYinglueAndZhendanNativeRules", WushuangSkillsBChecks.WeiyiYinglueAndZhendanNativeRules),
    ("Wushuang skills B: LiantaoAndXiaojieNativeUsesReplay", WushuangSkillsBChecks.LiantaoAndXiaojieNativeUsesReplay),
    ("Wushuang skills B: BianyuIdentityAndChunlaoAnyHpLoss", WushuangSkillsBChecks.BianyuIdentityAndChunlaoAnyHpLoss),
    ("Wushuang skills B: DinganSecondNameAndLargestHandDiscard", WushuangSkillsBChecks.DinganSecondNameAndLargestHandDiscard),
    ("Wushuang skills B: OfficialJiushiActiveAlcoholAndDamageFace", WushuangSkillsBChecks.OfficialJiushiActiveAlcoholAndDamageFace),
    ("Wushuang skills B: OfficialQiangzhiDingcuoAndLonghunRules", WushuangSkillsBChecks.OfficialQiangzhiDingcuoAndLonghunRules),
    ("Wushuang skills B: OfficialZengouFirstDodgeMultiTargetAndDyingCost", WushuangSkillsBChecks.OfficialZengouFirstDodgeMultiTargetAndDyingCost),
    ("Guandu mode: frozen roster private rotating draft tier five military deck", GuanduModeChecks.RealPackageFreezesRosterRotatesPrivateDraftAndDealsTierFiveMilitaryDeck),
    ("Guandu mode: scoped options and historical checkpoint shape", GuanduModeChecks.GuanduOptionsRemainScopedAndOldCheckpointShapeStaysUnchanged),
    ("Guandu mode: enemy hand friendly penalty and native commander victory", GuanduModeChecks.NativeKillsTransferEnemyHandPunishFriendlyKillsAndDeferCommanderVictory),
    ("Guandu mode: real deck exhaustion finishes native draw children", GuanduModeChecks.ActualDeckExhaustionWaitsForDrawChildrenAndAwardsAiArmy),
    ("Guandu events: counterspell Use and played Slash keep distinct global count", GuanduEventChecks.TenthUseCountsNativeCounterspellButNotPlayedSlash),
    ("Guandu events: tenth global use native replay and target legality", GuanduEventChecks.TenthUsesGlobalNativeCounterAndRevalidatesOriginalTarget),
    ("Guandu events: tenth BorrowedSword preserves owner and paired source victim", GuanduEventChecks.TenthBorrowedSwordRechecksOnlyWeaponOwnersAndKeepsThePairedSourceVictim),
    ("Guandu events: Wuchao fire underdog and recipient damage base", GuanduEventChecks.WuchaoAndUnderdogUseActualDamageAndRecipientScopes),
    ("Guandu events: grain shortage normal draw and once per use reward", GuanduEventChecks.GrainShortageReplacesNormalCountAndRewardsUseOnce),
    ("Guandu events: real turn start duel or health loss and cold resume", GuanduEventChecks.TurnStartDuelAndHpLossRetainRealSourceAndCold),
    ("Guandu events: hold line round allowance next discard debt extra turn", GuanduEventChecks.HoldLineTracksRoundDebtAcrossGenuineExtraTurn),
    ("Guandu events: standoff actual round five first slash damage", GuanduEventChecks.StandoffSwitchesAtRoundFiveAndOnlyFirstSlash),
    ("Guandu events: slow advance responses skipped and next actual draw", GuanduEventChecks.SlowAdvanceTracksResponsesSkippedDrawAndNextActualDraw),
    ("Guandu events: arrogance independent health and hand maxima", GuanduEventChecks.ArroganceChecksHandMaximumAfterItsRealDiscard),
    ("Guandu chariot: Wusheng response destroyed invoice and cold native completion", GuanduChariotChecks.RedChariotWushengResponseKeepsSingleDestroyedInvoiceAndTypedCompletion),
    ("Guandu chariot: Wusheng use and actual discard destroy one entity", GuanduChariotChecks.RedChariotWushengUseAndRealDiscardPhaseDestroyOnce),
    ("Guandu cards: enemy intoxication actual turn quota and damage", GuanduCardChecks.IntoxicationKeepsEnemyTurnQuotaAndEveryRealDamageBonus),
    ("Guandu cards: three recipients counterspells and repeated native draw", GuanduCardChecks.StoreGrainUsesThreeTargetsSeparateCounterspellsAndRepeatedNativeDraws),
    ("Guandu cards: recovery chains and exact recast", GuanduCardChecks.ReinforcementsAndUnitedEffortKeepNativeRecoveryChainsAndRecast),
    ("Guandu cards: complete basic families one and zero materials", GuanduCardChecks.CompleteBasicFamiliesProjectIntoxicationWithOneAndZeroMaterials),
    ("Guandu generals: private Shicai and live faction qualification cold", GuanduGeneralChecks.ShicaiPrivateTopAndFushiLivingFactionQualificationSurviveColdReplay),
    ("Guandu generals: foreign normal draw interception and exact payment cold", GuanduGeneralChecks.JieliangPaysBeforeForeignNormalDrawAndUsesActualTurnHandLimit),
    ("Guandu generals: grain collapse and incoming Gangzhi native boundaries", GuanduGeneralChecks.GrainCollapseAndIncomingOnlyGangzhiUseNativeDrawAndHpReplacement),
    ("Guandu generals: generated chariot both slots and departure destruction", GuanduGeneralChecks.ThunderChariotGeneratesRealEquipmentDiscardsBothSlotsAndDestroysOnDeparture),
    ("Guandu generals: Choulue native Duel FireAttack and cold children", GuanduGeneralChecks.ChoulueGiftIssuesRealVirtualDuelAndFireAttackAcrossColdChildren),
    ("Guandu generals: Yuanlve entity and Moushi actual next play receipts", GuanduGeneralChecks.YuanlveExactGiftAndMoushiNextActualPlayUseNativeDrawReceipts),
    ("Guandu generals: off turn Fuyuan and owner death Zhongjie native returns", GuanduGeneralChecks.FuyuanOffTurnNativeUseAndZhongjieOwnerDeathKeepTheirPaidChildren),
    ("Guandu generals: Suishi real dying and faction death boundaries", GuanduGeneralChecks.SuishiUsesTrueDyingEntryAndFactionDeathWithoutConfusingTheArmy),
    ("Guandu generals: Xiying exact turn paid refusal blocks use and response", GuanduGeneralChecks.XiyingPaidRefusalBlocksTrueUseAndResponseForOnlyTheActualTurn),
    ("Dragon boat activity: opt in team only fixed draft and cold restore", DragonBoatGeneralChecks.ActualPackageDraftIsOptInAndSharedWithOnlyTheTeammate),
    ("Dragon boat activity: treasure cap friendly damage loss and extra turn boundary", DragonBoatGeneralChecks.TreasurePaymentsCapAndTwoActualRoundsIncludeExtraTurns),
    ("Dragon boat generals: real consecutive targets without ordinary quota", DragonBoatGeneralChecks.LianpianUsesRealConsecutiveTargetsWithoutTheOrdinaryOlQuota),
    ("Dragon boat generals: actual damage points and exact Jianji card cold", DragonBoatGeneralChecks.DianhuCountsRealDamagePointsAndJianjiKeepsItsNativeExactCard),
    ("Dragon boat generals: threshold grants and team Wenji Tunjiang cold", DragonBoatGeneralChecks.XingzhaoSharesThresholdBenefitsAndWenjiTunjiangUseTeamRules),
    ("Dragon boat cards: allied recovery and shared wine allowance", DragonBoatCardChecks.AlliedBasicsKeepNativeRecoveryAndOneSharedWineAllowance),
    ("Dragon boat cards: real draw recipients and counterspell windows", DragonBoatCardChecks.AlliedAndGlobalDrawsUseExactRecipientsRequestsAndCounterspellWindows),
    ("Dragon boat cards: post damage tied ranking discard then draw", DragonBoatCardChecks.AgainstCurrentFreezesPostDamageTiesThenDiscardsBeforeDrawing),
    ("Dragon boat cards: full basic families retain arity and named Peach", DragonBoatCardChecks.FullBasicFamiliesKeepMaterialArityWhileNamedPeachStaysPeach),
    ("Wenhe activity: real eight seats private draft and cold restore", WenhePackageChecks.RealOptInOfficialRosterPrivateDraftAndColdRestore),
    ("Wenhe mode grants: template replacement and ordinary same-name instances", WenheRuleGrantChecks.ModeInstancesSurviveTemplateReplacementWithoutProtectingOrdinarySameName),
    ("Wenhe mode grants: native suppression death curse and cold rules", WenheRuleGrantChecks.NativeSuppressionAndDeathCursePreserveModeRulesNirvanaAndChampionMark),
    ("Wenhe rules: first round all actors nearest Slash and cold prompt", WenheRuleChecks.FirstRoundChaosIncludesOwnerUsesNearestPhysicalSlashAndColdPrompt),
    ("Wenhe rules: private champion mark native draw once and cold replay", WenheRuleChecks.ChampionMarkNativeDrawPrivateUntilUseOnceAndColdReplay),
    ("Wenhe rules: native death children reward then sole survivor", WenheRuleChecks.KillRewardRaisesOnlyMaximumDrawsThreeAndFinalDeathChildPrecedesWinner),
    ("Wenhe rounds: famine host dies without issuing the rule twice", WenheRoundRuleChecks.FamineIssuesOnceWhenItsHostActuallyDiesAtOldRoundEnd),
    ("Wenhe rounds: equipment replacement finishes children before next tied actor", WenheRoundRuleChecks.EquipmentReplacementCompletesOneParticipantChildBeforeTheNextTie),
    ("Wenhe rounds: dead actual owner retains only mode equipment ending", WenheRoundRuleChecks.DeadActualTurnOwnerStillRunsOnlyTheModeEquipmentEnding),
    ("Wenhe rounds: bounty doubles real reward without healing", WenheRoundRuleChecks.BountyKillUsesDoubleRealRewardWithoutHealing),
    ("Wenhe rounds: last stand rescue draw and extra turn paid once", WenheRoundRuleChecks.LastStandRescueDrawAndExtraActualTurnArePaidOnceEach),
    ("Wenhe rounds: sweep source damage chain propagation and ordinary loss", WenheRoundRuleChecks.SweepAddsAtTheSourceOnceWhileChainAndHpLossStayExact),
    ("Wenhe rounds: all dead famine waits for frozen children before draw", WenheRoundRuleChecks.FamineAllDeadCompletesAsDrawAfterItsFrozenDeathChildren),
    ("Wenhe cards: star shift native health children and counterspell", WenheCardChecks.StarShiftUsesRealSettingsChildrenAndOneCounterspell),
    ("Wenhe cards: hidden hand redistribution and atomic cold return", WenheCardChecks.HandRedistributionKeepsHiddenEntitiesAndAtomicColdReturn),
    ("Wenhe cards: equipment capacity and real Silver Lion children", WenheCardChecks.BeamSwapPreservesEquipmentCapacityAndSilverLionChildren),
    ("Wenhe cards: physical chaos retains parent through nearest Slash cold return", WenheCardChecks.ChaosCardRetainsPaidParentThroughRealNearestSlashAndColdReturn),
    ("Wenhe cards: real additional targets retire receipts and pay once cold", WenheCardChecks.RealAdditionalTargetRetiresEachRandomReceiptAndPaysOnceAcrossColdReturn),
    ("Wenhe poison: serial public loss hidden transfers and cold return", WenhePoisonChecks.PublicDeparturesAreSerialNativeLossesAndHiddenTransfersStayOpaque),
    ("Wenhe poison: physical conversion pays loss before Slash and dying", WenhePoisonChecks.ConvertedPhysicalPoisonPaysBeforeItsRealSlashAndCanEnterDying),
    ("Wenhe poison: fire attack waits for public poison cost before damage", WenhePoisonChecks.FireAttackWaitsForItsPublicPoisonCostBeforeDamage),
    ("Wenhe AI: four published tricks score survival views without self target crash", WenheAiChecks.FourPublishedTricksScorePublicSurvivalViewsWithoutSelfTargetCrash),
    ("Hell activity: five tiers dormant auxiliary private draft checkpoint", HellActivityChecks.RealPackageFiveTiersDormantAuxiliaryRotatedDraftAndCheckpoint),
    ("Hell activity: four stages await deaths and unlock hidden boss", HellActivityChecks.FourStagesWaitForDeathChildrenAndRealMengpoUnlocksHiddenBoss),
    ("Hell activity: living Mengpo keeps ordinary final boss and victory", HellActivityChecks.LivingMengpoDoesNotUnlockHiddenBossOrBlockFinalVictory),
    ("Hell Mengpo: real robe blocks all soul targets before payment", HellMengpoChecks.RealRobeExcludesEverySoulTargetWithoutPublishingAnEmptyUse),
    ("Hell ordinary tricks: Qice permission preserves other names and materials", HellOrdinaryTrickChecks.QicePermissionFilterKeepsOtherNamesAndUnpaidMaterials),
    ("Hell ordinary tricks: added soul all hand one zero materials native restoration", HellOrdinaryTrickChecks.AddedSoulNamePreservesAllHandOneZeroMaterialsAndNativeSkillRestoration),
    ("Hell tier: Chongsheng keeps owned zones limited ledger and cold draw", HellTierPrivilegeChecks.ChongshengPreservesHandEquipmentAndLimitedLedgerAcrossColdDraw),
    ("Hell tier: actual equipment late fifth tier assault native cold", HellTierPrivilegeChecks.TierEquipmentAndLateFifthTierAssaultUseRealNativeCardsOnce),
    ("Hell kings: preparation draw random assaults and repeated damage", HellKingChecks.NativePreparationDrawRandomAssaultAndRepeatedDamage),
    ("Hell kings: actual gain batches outside draw phase", HellKingChecks.ZhiwangUsesRealGainBatchesAndExcludesDrawPhases),
    ("Hell kings: bitter wine poison and recovery source", HellKingChecks.BitterWinePeachPoisonAndRecoverySourceStayNative),
    ("Hell kings: death children recalculate highest health", HellKingChecks.DeathEffectsRecalculateExtremeHpAndPreserveNativeSubtrees),
    ("Hell kings: armor native Slash and current health exchange", HellKingChecks.VirtualArmorNativeSlashAndReincarnationSetting),
    ("Hell Dizang: actual distance fire thresholds draw children cold", HellDizangChecks.ActualDistanceFireDamageThresholdAndWuliangDrawChildrenCold),
    ("Hell Dizang: native judgment any suit rank choices cold", HellDizangChecks.FinalJudgmentAnySuitRankChoiceAndColdReplay),
    ("Hell Dizang: abolished mounts native recast draw cold", HellDizangChecks.AbolishedMountSlotsNativeRecastPaymentDrawAndCold),
    ("Hell Mengpo: pre-discard distinct suits native movement", HellMengpoChecks.ShiyouRedirectsHandLimitAndProgramDiscardsBeforeEntry),
    ("Hell Mengpo: exact lost skill restoration native soul card", HellMengpoChecks.WanghunRestoresOnlyItsOwnSkillsAndDestroysSoulWithColdChildren),
    ("Hell Mengpo: actual enemy turn category use response ban", HellMengpoChecks.WangshiBansUseAndResponseCategoryForTheActualEnemyTurn),
    ("Shanhe campaign: frozen content routes and nested views", ShanheCampaignChecks.ContentRoutesAndPublishedNestedListsAreFrozen),
    ("Shanhe chapters: three real boss bundles frozen choices explicit confirmation cold", ShanheChapterRewardChecks.ThreeBossBundlesFreezeBothChoicesAndRequireExplicitChapterConfirmation),
    ("Shanhe chapters: metadata nested pools exact legacy hashes bounded shops", ShanheChapterRewardChecks.ChapterMetadataNestedPoolsLegacyHashesAndLargeShopsKeepTheirBoundaries),
    ("Shanhe market: random stock category prices refresh and cold replay", ShanheMarketChecks.RandomStockCategoryPricesRefreshAndColdReplay),
    ("Shanhe inheritance: frozen initial rare skill once and cold replay", ShanheOpeningSkillChecks.InitialThreeChoiceSkillIsFrozenUnlockedOnceAndColdReplayed),
    ("Shanhe inheritance: verified rare pool and old definition fingerprints", ShanheOpeningSkillChecks.VerifiedRareCatalogAndOldDefinitionFingerprintsRemainValid),
    ("Shanhe market: insufficient funds small pools and legacy fixed stock", ShanheMarketChecks.InsufficientFundsSmallPoolsAndLegacyFixedStock),
    ("Shanhe roster: ordinary expanded general real first battle cold replay", ShanhePlayerRosterChecks.OrdinaryRosterStartsRealExpandedGeneralBattleAndRestoresCold),
    ("Shanhe roster: frozen pools exact eight legacy and forged pool rejection", ShanhePlayerRosterChecks.SavedRostersFreezeLegacyCompatibilityAndRejectForgedPools),
    ("Shanhe campaign: story shop skill replacement atomic cold journal", ShanheCampaignChecks.StoryShopSkillReplacementAndColdJournalStayAtomic),
    ("Shanhe campaign: real completed battles gate rewards and full story replay", ShanheCampaignChecks.RealCompletedBattlesGateRewardsAndReplayTheWholeStory),
    ("Shanhe campaign: seeded entry encounter and content fingerprint", ShanheCampaignChecks.SeededEntryEncounterAndContentFingerprintRemainStable),
    ("Shanhe campaign: independent tactic grants alias rejection and cold setup", ShanheCampaignChecks.TacticsUseIndependentGrantsAndRejectAliases),
    ("Shanhe campaign: repeated acquisition identities hashes boss groups and all instances cold", ShanheRepeatableTacticChecks.CampaignRepeatedAcquisitionsRespectIdentityHashAndKeepAllInstancesCold),
    ("Shanhe campaign: decline frozen rewards exact gold once and cold journal", ShanheCampaignChecks.DecliningPublishedRewardsPaysOnceAndReplaysWithoutReroll),
    ("Shanhe codex: initial tactics frozen grants validation and cold replay", ShanheCodexLoadoutChecks.InitialTacticsValidateFreezeGrantAndReplayWithoutChangingContent),
    ("Shanhe codex: locked skill equipment pools and forged commands", ShanheCodexLoadoutChecks.LockedSkillsAndEquipmentStayOutOfAllRewardPathsAndForgedCommands),
    ("Shanhe codex: old options unrestricted cold checkpoint replay", ShanheCodexLoadoutChecks.OldOptionsRemainUnrestrictedThroughColdCheckpointReplay),
    ("Shanhe codex content: editions checkpoint pool and native equipment", ShanheCodexContentChecks.EditionsCheckpointChoiceAndSpecialEquipmentStayPlayable),
    ("Shanhe tactic: effective pindian bonus instances caps claims cold replay", ShanheTacticPindianChecks.EffectiveBonusInstancesCapsPhysicalClaimsAndColdReplay),
    ("Shanhe tactic: simultaneous ranks and opponent AI use same bonus cold", ShanheTacticPindianChecks.SimultaneousRanksAndOpponentAiUseTheSameBonusCold),
    ("Shanhe repeatable tactic: paid Slash hand boundary converted alcohol replacement and chain cold", ShanheRepeatableTacticChecks.JingyuTwoInstancesUsePaidSlashHandBoundaryConversionsAndCold),
    ("Shanhe Armor: repeated team opening paid grants skill loss and fresh battle cold", ShanheArmorChecks.RepeatedTeamArmorStartsOnlyAlliesSurvivesSkillLossAndRestartsCold),
    ("Shanhe S5 Zhangliao: planned draw smaller hands native Pojun and cold return", ShanheS5ZhangliaoChecks.PlannedDrawTakesThreeSmallerHandsThenRealPojunReturnsCold),
    ("Shanhe Lingdong: final native judgment Armor aftermath and typed outcomes cold", ShanheLingdongChecks.FinalNativeGuicaiColorControlsPreventionArmorAndAfterDamageCold),
    ("Shanhe Lingdong: real elemental chain per recipient prevention cold", ShanheLingdongChecks.ElementalChainJudgesEachRecipientAndMiddlePreventionKeepsFullOriginDamageCold),
    ("Shanhe Lingdong: Tiandu claim movement frozen suit and replacement bypass cold", ShanheLingdongChecks.NativeTianduKeepsClaimedEntityWhileReplacementsBypassAndRealDamageJudgesCold),
    ("Shanhe Huangkong: planned draw Yingzi team aura real Tuxi and Hongde cold", ShanheHuangkongChecks.PlannedDrawDoublesAfterYingziAndTeamAuraBeforeRealTuxiCold),
    ("Shanhe Huangkong: doubled discard quota actual payment and child return cold", ShanheHuangkongChecks.DoubledDiscardQuotaPaysFourWithoutChangingHandLimitAndResumesChildrenCold),
    ("Shanhe Huangkong: counted exemptions protected cards skipped phases and NPC quota", ShanheHuangkongChecks.DiscardExemptionsProtectedCardsSkippedPhaseAndNpcUseSameQuota),
    ("Shanhe S5 Dong Zhuo: explicit printed Lord recipe frozen validation and cache", ShanheS5DongZhuoChecks.ExplicitPrintedLordRecipeFreezesValidatesHashesAndNeverChangesTeamRole),
    ("Shanhe S5 Dong Zhuo: two scenario owners per point Armor recovery and claim cold", ShanheS5DongZhuoChecks.TwoQualifiedScenarioBaonueOwnersJudgeEachDamagePointThroughFullArmorCold),
    ("Shanhe S5 Dong Zhuo: suppression own source source-less and Jueqing boundaries", ShanheS5DongZhuoChecks.DisabledPrintedSourceOwnDamageSourceLessAndJueqingDoNotInventBaonue),
    ("Shanhe god faction: actual god general fallback public views native override and Baonue cold", ShanheGodFactionChecks.RealGodGeneralUsesScenarioFallbackForPublicFactionAndNativeBaonueCold),
    ("Shanhe god faction: null input hash session propagation and tampered battle rejection", ShanheGodFactionChecks.DefaultGodFactionPolicyFreezesHashesPassesSessionAndRejectsTamperedBattle),
    ("Shanhe team Slash: independent allies shared native quota inserted Play and cold", ShanheTeamSlashChecks.OtherPublicAlliesAddNativeSharedSlashQuotaAndInsertedPlayResetsCold),
    ("Shanhe team Slash: paid use holder death loss and future quota recheck cold", ShanheTeamSlashChecks.PaidSlashSurvivesHolderDeathOrLossWhileFutureQuotaRechecksCold),
    ("Shanhe Dong Min: fixed cap before Armor public prediction and replacements cold", ShanheS5DongMinChecks.NativeFixedCapPrecedesArmorAndPublicPredictionCold),
    ("Shanhe Dong Min: elemental recipients source-less and live caps cold", ShanheS5DongMinChecks.ElementalChainAndLiveCapSourcesRecheckWithoutInflatingLowerDamageCold),
    ("Shanhe Cuanchao: native polarity accepted and declined whole-use state cold", ShanheDongMinCuanchaoChecks.WholeUseCancellationCommitsNativePolarityOnlyWhenAcceptedCold),
    ("Shanhe Cuanchao: all recipients extra rounds and delayed placement cold", ShanheDongMinCuanchaoChecks.WholeUseStateCancelsAllRecipientsRoundsAndDelayedPlacementCold),
    ("Shanhe Cuanchao: self uses live skill changes and native damage cap cold", ShanheDongMinCuanchaoChecks.SelfTargetedUsesAndLiveSkillChangesPreserveNativeReturnsAndCapCold),
    ("Shanhe Far Strike: native program final distance armor public prediction and cold", ShanheFarStrikeChecks.NativeAndProgramDamageUseFinalDirectedDistanceArmorAndPublicPredictionCold),
    ("Shanhe Far Strike: chained recipient distance source loss and cold", ShanheFarStrikeChecks.ElementalChainRechecksRecipientDistanceAndLiveSkillLossCold),
    ("Shanhe Liyu: whole regions actual gain receipts printed categories and cold", ShanheLiyuChecks.WholeRegionsUseActualObtainReceiptsAndPrintedCategoryCold),
    ("Shanhe Liyu: acquired count native Wushuang Armor and nullification cold", ShanheLiyuChecks.AcquiredCountDuelUsesNativeWushuangArmorAndNullificationCold),
    ("Shanhe Liyu: paid gain children source lifetimes and destroyed equipment cold", ShanheLiyuChecks.PaidRegionSourceLifetimesAndDestroyedEquipmentCannotInflateDuelCold),
    ("Shanhe Biyue: empty Hand live limit native gain children and cold", ShanheBiyueChecks.EmptyHandRefillsLiveLimitAndNativeGainChildrenCold),
    ("Shanhe Biyue: real Liuli payment range and next actual turn expiry cold", ShanheBiyueChecks.NonemptyGrantRedirectsPhysicalSlashAndExpiresAtNextActualTurnCold),
    ("Shanhe Biyue: independent sources extra turns and source loss cold", ShanheBiyueChecks.IndependentSourcesExtraTurnsAndOriginalSourceLossCold),
    ("Shanhe Lijian: real ordered Duel use windows and physical receipts cold", ShanheLijianChecks.RealOrderedDuelRunsUseWindowsAndPaysOriginalEntitiesCold),
    ("Shanhe Lijian: paid source lifetimes and dead participant returns cold", ShanheLijianChecks.PaidDiscardSurvivesOwnerLossOrDeathAndDeadParticipantsCold),
    ("Shanhe S5 Roulin: any gender physical double responses and maximum policy cold", ShanheS5RoulinChecks.AnyGenderBidirectionalSlashResponsesUseTwoDistinctDodgesAndMaximumPolicyCold),
    ("Shanhe S5 Roulin: paid response source loss new uses and Armor cold", ShanheS5RoulinChecks.FrozenIssuedResponseSurvivesSourceLossButNewSlashRechecksArmorCold),
    ("Shanhe S5 Zaiqi: lost HP plus one real reveal recovery and gain children cold", ShanheS5ZaiqiChecks.LostHpPlusOneRevealsRealCardsAndReturnsGainAndRecoveryChildrenCold),
    ("Shanhe S5 Zaiqi: replacement eligibility codex purchase and conditional recipes cold", ShanheS5ZaiqiChecks.FullHpAndDeclinedReplacementKeepNormalDrawAndVerifiedCodexReward),
    ("Shanhe S5 Kanpo: purchased HE native second layer and fresh target windows cold", ShanheS5KanpoChecks.PurchasedHeAndNativeCounterspellsKeepSecondLayerAndFreshTargetWindowsCold),
    ("Shanhe S5 Kanpo: paid children source loss and death cold", ShanheS5KanpoChecks.PaidCounterspellChildrenRetainPolicyAfterSourceLossOrDeathCold),
    ("Shanhe Jinnang Ji: actual plans extra draws and late active sources cold", ShanheJinnangJiChecks.ActualDrawsIncludePlansAndExtraDrawsWithLateActiveSourcesCold),
    ("Shanhe Jinnang Ji: replacement gains versus real draw phase skill draws cold", ShanheJinnangJiChecks.ReplacementGainsDoNotCountButRealDrawPhaseSkillDrawsDoCold),
    ("Shanhe Jinnang Ji: multiple real phases turn end and scoped schema cold", ShanheJinnangJiChecks.MultipleRealDrawPhasesAccumulateAndTurnEndClearsWithScopedSchemaCold),
    ("Shanhe Fengyi: original three enemies initial health conditional package and cold", ShanheFengyiEncounterChecks.ActualFiveSeatRecipeStartsOriginalThreeEnemiesAndFreezesInitialHealthCold),
    ("Shanhe Gaichao: real single enemy source health draw and conditional packages cold", ShanheFengyiEncounterChecks.ActualGaichaoSingleEnemyRecipeKeepsSourceHealthDrawAndConditionalPackagesCold),
    ("Shanhe Duoduo Yishan: native invocation ordinals three versions and real loadout cold", ShanheDuoduoYishanChecks.ActualDrawOrdinalsStackThreeVersionsAndFreezeProductionLoadoutCold),
    ("Shanhe Duoduo Yishan: captured eligibility late sources and paid children cold", ShanheDuoduoYishanChecks.CapturedDrawEligibilityRejectsLateSourcesAndPreservesPaidChildrenCold),
    ("Shanhe Duoduo Yishan: Jinnang Ji replacements and other actual turns cold", ShanheDuoduoYishanChecks.DrawPhaseBonusesFeedJinnangJiButReplacementGainsAndOtherTurnsStayScopedCold),
    ("Shanhe Yaoli: six versions real Peach recovery ordinals and actual turns cold", ShanheYaoliChecks.RecoveryCallsStackSixVersionsAndRealPeachesResetAtActualTurnsCold),
    ("Shanhe Yaoli: live sources paid children and zero recovery cold", ShanheYaoliChecks.LiveSourcesAndPaidChildrenPreserveEligibilityAndZeroRecoveryCold),
    ("Shanhe Yaoli: Zaiqi Kuanggu blood blade and recovery draw one payment cold", ShanheYaoliChecks.ZaiqiKuangguBloodBladeAndRecoveryDrawUseOneModifiedPaymentCold),
    ("Shanhe Yaoli: redirected recovery and Yizheng final recipient once cold", ShanheYaoliChecks.RedirectedRecoveryAndYizhengSupportApplyFinalRecipientPoliciesOnceCold),
    ("Shanhe Dong Ji: black locks exact dynamic discard or draw cold", ShanheDongJiChecks.BlackGiftLocksExcludePaymentAndExactDynamicDiscardOrDrawCold),
    ("Shanhe Dong Ji: frozen HP equipment children and paid source invalidation cold", ShanheDongJiChecks.FrozenRecipientHpSurvivesEquipmentChildrenAndPaidSourceInvalidationCold),
    ("Shanhe Dong Ji: actual source pair encounter and edition isolation cold", ShanheDongJiChecks.ActualDongshiEncounterUsesSourcePairAndIsolatesEditionsCold),
    ("Shanhe first Slash: declarations misses responses conversions and virtual uses cold", ShanheFirstSlashChecks.DeclarationsCountMissResponsesConversionsAndVirtualUsesCold),
    ("Shanhe first Slash: round and actor turn native boundaries cold", ShanheFirstSlashChecks.RoundAndActorTurnOrdinalsRemainStableAcrossNativeBoundariesCold),
    ("Shanhe first Slash: card layer replacements and chain propagation cold", ShanheFirstSlashChecks.CardLayerBonusesPrecedeReplacementsAndDoNotRepeatOnChainsCold),
    ("Shanhe first Slash: eight production tactics and native Slash quota cold", ShanheFirstSlashChecks.EightProductionTacticsGrantIndependentBonusesAndNativeSlashQuotaCold),
    ("Shanhe Chengxiong: real rounds extra turns zero damage and source lifetimes cold", ShanheChengxiongChecks.RealRoundParityExtraTurnsProgramDamageAndZeroPaymentsCold),
    ("Shanhe Chengxiong: native elemental propagation and live source loss cold", ShanheChengxiongChecks.NativeElementalPropagationRechecksEachRecipientAndLiveSourceLossCold),
    ("Shanhe Chengxiong: actual source form conditional recipes and replacements cold", ShanheChengxiongChecks.ActualChengxiongGaoshunRecipesAndEarlyReplacementsRemainIsolatedCold),
    ("Shanhe Caoxing: continuous HE topdeck native Slash responses and damage markers cold", ShanheCaoxingChecks.ContinuousHeartHeTopdeckUsesRealSlashCostsResponsesAndDamageMarkersCold),
    ("Shanhe Caoxing: stacked effects actual discard exact draw and all-layer clearing cold", ShanheCaoxingChecks.StackedLiushiClearsAllAtNativeDiscardEndAndDrawsExactCountCold),
    ("Shanhe Caoxing: paid draw source loss tails and isolated actual recipes cold", ShanheCaoxingChecks.PaidZhanwanDrawRetainsSourceLossTailAndActualS5RecipesStayIsolatedCold),
    ("Liushi effect contract: rejects unpaid foreign and wrong-window definitions", LiushiEffectValidationChecks.RejectsUnpaidForeignAndWrongWindowDefinitions),
    ("Shanhe Shidu: Liushi top Heart native Pindian live half and discard rewards cold", ShanheShiduChecks.LiushiTopHeartPindianObtainsWholeHandThenCapturesLiveHalfCold),
    ("Shanhe Shidu: loss ties and emptied native contests cold", ShanheShiduChecks.LosingTiedAndEmptiedContestsDoNotInventTransfersCold),
    ("Shanhe Shidu: paid source loss target death and isolated actual recipes cold", ShanheShiduChecks.PaidObtainSourceLossTargetDeathAndActualRecipesStayIsolatedCold),
    ("Shanhe Shihun: direct native Slash current skill pool distinct grants and cold", ShanheShihunChecks.DirectSlashAcquiresCurrentEligibleSkillOncePerDamageAndReplaysCold),
    ("Shanhe Shihun: chain prevention replacements source loss and persistent grants cold", ShanheShihunChecks.DodgedChainedAndReplacedDamageDoNotAcquireSkillsAndPaidSourceLossKeepsGrant),
    ("Shanhe Jiaozi: Shidu live hand leadership damage Armor and half return cold", ShanheShihunChecks.ShiduHandLeadershipChangesJiaoziDamageBeforeAndAfterHalfReturnCold),
    ("Shanhe Huaxiong: colored card health whole recovery before Armor cold", ShanheHuaxiongChecks.ColoredCardDamageChangesMaximumHpOrRecoversWholeAmountBeforeArmorCold),
    ("Shanhe Huaxiong: round limited native Duel responses and paid source loss cold", ShanheHuaxiongChecks.RoundLimitedNativeDuelUsesResponsesNullificationAndPaidSourceLossCold),
    ("Shanhe Huaxiong: paired health Duel and actual isolated recipes cold", ShanheHuaxiongChecks.PairedYaowuDuelInteractionsAndActualRecipesStayIsolatedCold),
    ("Shanhe Taishici: sole incoming repeats before live range draw cold", ShanheTaishiciChecks.IncomingSoleCardRepeatsBeforeLiveRangeDrawAndReplaysCold),
    ("Shanhe Taishici: sequential preparation Duels persistent shortfall cold", ShanheTaishiciChecks.PreparationSequentialDuelsGrantPersistentShortfallAndReplaysCold),
    ("Shanhe Taishici: Douchan range Dulie draw and isolated recipes cold", ShanheTaishiciChecks.DouchanRangeFeedsDulieAndConditionalRecipesStayIsolatedCold),
    ("Mou Taishi Ci OL: fixed one native Duel initial population capped permanent growth", CardGame.Core.Tests.MouTaishiCiOlChecks.OneActualDuelOrInitialPopulationCappedPermanentGrowth),
    ("Mou Lu Xun Mobile: actual trick hold return private loss distribution and recorded native use", CardGame.Core.Tests.MouLuXunMobileChecks.ActualTrickHoldReturnPrivateLossDistributionAndRecordedNativeUse),
    ("Mou Lu Xun Mobile: physical Wenhe Chaos participant effect and exact parent cold return", CardGame.Core.Tests.MouLuXunMobileChecks.RealWenheChaosParticipantRecordsAndReturnsItsPhysicalParent),
    ("Mobile Shen Cao Cao: Guixin chosen areas per point and paid child cold", MobileShenCaoCaoChecks.GuixinSelectsAreasPerDamagePointAndColdReturnsOnce),
    ("Mobile Shen Lu Bu: printed trick targets and whole use Wuqian across turns", CardGame.Core.Tests.MobileShenLuBuChecks.PrintedTrickTargetsAndWholeUseWuqianAcrossTurns),
    ("Mobile Shen Lu Bu: ending damage card gain original paid frame cold", CardGame.Core.Tests.MobileShenLuBuChecks.EndingDamageCardGainKeepsOriginalPaidFrameCold),
    ("Mobile Shen Sima Yi: unanswered native windows round cap and faction awakening cold", CardGame.Core.Tests.MobileShenSimaYiChecks.UnansweredNativeWindowsRoundCapAndFactionAwakeningCold),
    ("Mobile Shen Sima Yi: escalating permanent learning kill choice and paid draw cold", CardGame.Core.Tests.MobileShenSimaYiChecks.EscalatingPermanentLearningKillChoiceAndPaidDrawCold),
    ("OL Shen Zhen Ji: even native draw opaque discard repeating branch cold", CardGame.Core.Tests.OlShenZhenJiChecks.EvenDrawAndOpaqueDiscardRepeatFromOriginalFrameCold),
    ("OL Shen Zhen Ji: odd native dying death repeats and survivor stops cold", CardGame.Core.Tests.OlShenZhenJiChecks.OddNativeDyingDeathRepeatsAndSurvivorStopsCold),
    ("OL Shen Cao Pi: native draw foreign deposit capacity and cold", CardGame.Core.Tests.OlShenCaoPiChecks.ChuyuanNativeDrawForeignDepositCapacityAndCold),
    ("OL Shen Cao Pi: two actual awakenings obtain Chu permanent skill and cold", CardGame.Core.Tests.OlShenCaoPiChecks.TwoActualAwakeningsObtainChuAndGrantPermanentSkillCold),
    ("anniversary Sun Quan private Ming native copy and random surge cold returns once", AnniversaryShenSunQuanChecks.PrivateMingNativeCopyAndRandomSurgeColdReturnsOnce),
    ("OL2016 Shen Zhao Yun: live HP costs native equipment use response cold recovery", CardGame.Core.Tests.Ol2016ShenZhaoYunChecks.LiveHpCostsNativeEquipmentUseResponseAndColdRecovery),
    ("OL2016 Shen Sima Yi: actual Discard phase original Jilue paid draw and Lianpo cold", CardGame.Core.Tests.Ol2016ShenSimaYiChecks.ActualPhaseDiscardsOriginalJiluePaidDrawAndLianpoCold),
    ("OL Shen Cao Cao: priority regions fallback random damage point cold", CardGame.Core.Tests.OlShenCaoCaoChecks.PriorityRegionsFallbackRandomAndDamagePointCold),
    ("Anniversary Shen Zhang Liao Cuixi paid cold and temporary equipment phases", AnniversaryShenZhangLiaoChecks.CuixiPaidColdAndTemporarySlotsUseActualPhases),
    ("Mobile Shen Ma Chao: real nature markers Jimie both Yuli recharge cold", CardGame.Core.Tests.MobileShenMaChaoChecks.RealNatureMarkerJimieAndBothYuliRechargeCold),
    ("Mobile Shen Ma Chao: frozen Tingwei subset gift damage next target turn expiry cold", CardGame.Core.Tests.MobileShenMaChaoChecks.FrozenTingweiSubsetGiftDamageAndNextTargetTurnExpiryCold),
    ("Mobile Shen Jiang Wei: physical exchange ordered native Slashes cold", MobileShenJiangWeiChecks.XinghunPhysicalExchangeAndOrderedSlashesCold),
    ("Mobile Shen Jiang Wei: Tiantao Shenpei Huitian real native returns cold", MobileShenJiangWeiChecks.TiantaoShenpeiAndHuitianUseRealNativeReturns),
    ("Shanhe Caocao Guixin: independent damage points random areas and turnover cold", ShanheCaocaoGuixinChecks.DamagePointsChooseIndependentNonemptyAreasAndTurnOverCold),
    ("Shanhe Caocao Guixin: sequential live zones paid source loss and children cold", ShanheCaocaoGuixinChecks.SequentialAreaChildrenUseCurrentZonesAndPreservePaidLossCold),
    ("Shanhe Caocao Guixin: paid owner death and isolated recipes cold", ShanheCaocaoGuixinChecks.PaidOwnerDeathAndConditionalRecipesStayIsolatedCold),
    ("Shanhe Yujin: frozen preparation printed types and exact owner payment cold", ShanheYujinChecks.FrozenPreparationDiscardsUsePrintedNonEquipmentAndExactOwnerPaymentCold),
    ("Shanhe Yujin: immediate native draw gain children and paid source loss cold", ShanheYujinChecks.ImmediateTargetDrawUsesNativeAttemptsGainChildrenAndPaidSourceLossCold),
    ("Shanhe Yujin: legacy ending timing and isolated actual recipes cold", ShanheYujinChecks.LegacyEndingTimingAndActualRecipesStayIsolatedCold),
    ("Shanhe Yudu: actual enemy damage single poison frozen round Armor and chain cold", ShanheYuduChecks.ActualEnemyDamagePoisonsOnceAndDrawsFrozenRoundThroughArmorChainCold),
    ("Shanhe Yudu: final preparation judgment poison loss before native Niepan cold", ShanheYuduChecks.FinalPreparationJudgmentRemovesOrPaysPoisonBeforeNativeNiepanCold),
    ("Shanhe Yudu: paid poison sources and isolated actual recipes cold", ShanheYuduChecks.PaidPoisonSourcesAndActualRecipesStayIsolatedCold),
    ("Shanhe Liru: actual single enemy recipe conditional registry and recovery", ShanheLiruEncounterChecks.ActualSingleEnemyRecipeAndConditionalRegistryRecover),
    ("Shanhe Liru: exact previous campaign definitions recover", ShanheLiruEncounterChecks.PreLiruCampaignsRestoreOnlyTheirExactDefinitions),
    ("Shanhe rank: native optional rebirth recovery then five gained cards cold", ShanheRankRebirthChecks.NativeOptionalRebirthRecoversThenGainsFiveWithoutClearingStateCold),
    ("Shanhe resources: flag flows across battles and legacy JSON stays unchanged", ShanheCampaignChecks.StartingResourceFlagFlowsAcrossBattlesAndKeepsLegacyJson),
    ("Shanhe resources: catalog restores exact legacy and pre Liru definitions", ShanheCampaignChecks.StartingResourceCatalogRestoresExactLegacyAndPreLiru),
    ("Shanhe resources: native use conversion response equipment and delayed exit cold", ShanheStartingResourceChecks.NewResourcesUseConversionDodgeAndEquipmentExitCold),
    ("Shanhe resources: claimed originals and legacy physical cards stay in circulation", ShanheStartingResourceChecks.ClaimedResourcesAndLegacyRecipeRetainTheirPhysicalCards),
    ("Shanhe resources: supplied Guhuo binds new use owner and failed declarations discard cold", ShanheStartingResourceDeclarationChecks.SuppliedGuhuoTracksItsNewUseOwnerAndFailedDeclarationStaysDiscardedCold),
    ("Shanhe rank: bonuses freeze once and legacy recipes stay unchanged", ShanheCampaignChecks.GeneralRankBonusesFreezeOnceAndLegacyRecipesStayUnchanged),
    ("Shanhe rank: rebirth usage survives two battles victory defeat and cold", ShanheCampaignChecks.FifthRankRebirthUsageSurvivesTwoBattlesAndColdVictoryOrDefeat),
    ("Signed HP: real Slash Armor debt same last responder Peaches cold", SignedHpRescueChecks.PhysicalSlashDebtNeedsSameLastResponderPeachesAndReplaysCold),
    ("Signed HP: native loss bypasses Armor and declined rescue advances cold", SignedHpRescueChecks.NativeLoseHpBypassesArmorAndPassAdvancesTheRescueCursorCold),
    ("Signed HP: paid program Alcohol repeats same responder cold", SignedHpRescueChecks.PaidProgramAlcoholCanContinueTheSameResponderAndReplaysCold),
    ("Shanhe S5 Jiuchi: black hand unlimited Wine real Armor permanent exchange cold", ShanheS5JiuchiChecks.BlackHandWineUnlimitedUsesAndRealArmorDamagePermanentlySwapCold),
    ("Shanhe S5 Jiuchi: ordinary prevented and HP loss damage boundaries cold", ShanheS5JiuchiChecks.OrdinaryPreventedAndHpLossSlashesCannotCreatePermanentExchangeCold),
    ("Shanhe S5 Jiuchi: paid source lifetimes and temporary printed restoration cold", ShanheS5JiuchiChecks.PaidSourceLifetimesAndTemporaryPrintedReturnPreservePermanentReplacementCold),
    ("Permanent Alcohol exchange: strict parser and registry reference boundaries", AlcoholSlashSkillReplacementValidationChecks.LoaderAndRegistryRejectInvalidPermanentAlcoholReplacementContracts),
    ("Shanhe Armor: full partial damage counter health delta and native Kuanggu cold", ShanheArmorChecks.FullAndPartialArmorKeepDamageCounterAndRealHealthDeltaCold),
    ("Shanhe Armor: real Iron Chain full damage HP loss and maximum replacement cold", ShanheArmorChecks.IronChainCarriesFullDamageWhileHpLossAndMaximumReplacementBypassArmorCold),
    ("Armor AI: published program target real lethality and pure queries", ArmorAiChecks.PublishedProgramAiChoosesRealKillInsteadOfArmoredLowHpAndQueriesStayPure),
    ("Armor AI: ordered program damage remaining Armor and direct self loss", ArmorAiChecks.ProgramHealthEstimatesRespectRemainingArmorAndInstructionOrder),
    ("Armor AI: public payment kinds and native replacement witnesses", ArmorAiChecks.PurePaymentsSeparateDamageHpLossAndMaximumLossWithNativeWitnesses),
    ("Shanhe tactics: first damage nested counters and cold children", ShanheTacticReactionChecks.FirstActualDamageConsumesBeforeNestedEnemyCountersAndColdChildren),
    ("Shanhe tactics: empty enemy set consumes quota cold", ShanheTacticReactionChecks.EmptyEnemySetConsumesTheFirstDamageQuotaAndColdReplay),
    ("Shanhe tactics: actual recovery draw and empty maximum each battle", ShanheTacticReactionChecks.RecoveryDrawUsesOneActualEventAndEmptyMaximumRestartsEachBattle),
    ("Shanhe tactics: random recovery damage native Kuanggu children cold", ShanheTacticReactionChecks.RandomRecoveryDamageFreezesItsRecipientAndComposesWithNativeRecovery),
    ("Shanhe original skills: Qiangxi damage cost nested counter cold", ShanheOriginalSkillChecks.QiangxiDamageCostRunsCounterBeforeTargetAndDoesNotRepeatCold),
    ("Shanhe original skills: Qiangxi source less cost Jueqing counter cold", ShanheOriginalSkillChecks.QiangxiSourceLessCostSurvivesJueqingWhileSourcedCounterAndHitBecomeHpLossCold),
    ("Shanhe original skills: Shizhan real Duel nested counter cold", ShanheOriginalSkillChecks.ShizhanUsesRealOtherActorDuelAndOnlyDamageTriggersCounterCold),
    ("Shanhe original skills: Shenxing Lianying last hand batch cold", ShanheOriginalSkillChecks.ShenxingLastHandBatchCompletesLianyingBeforeNativeDrawCold),
    ("Shanhe replacements: absolute hp loss alcohol Luoyi card layer cold", ShanheDamageReplacementChecks.AbsoluteHpLossKeepsAlcoholLuoyiAndExplicitCardLayerCold),
    ("Shanhe replacements: separate round quotas armor cold", ShanheDamageReplacementChecks.RoundQuotaSeparatesSlashAndTrickAndIgnoresArmorCold),
    ("Shanhe replacements: zero maximum direct death paid costs cold", ShanheDamageReplacementChecks.ZeroMaximumDiesWithoutDyingAndNativeCostsStayPaidCold),
    ("Shanhe recovery: paid dying Peach blood blade original dying cold", ShanheDamageReplacementChecks.PaidDyingPeachRecoveryStartsBloodBladeAndReturnsToOriginalDyingCold),
    ("Shanhe recovery: native Shenqu Peach blood blade typed return cold", ShanheDamageReplacementChecks.NativeShenquPeachInsideDamageStartsBloodBladeAndReturnsCold),
    ("Shanhe recovery: Longhun two heart paid group one event cold", ShanheDamageReplacementChecks.RealLonghunTwoHeartDyingRecoveryPaysWholeGroupAndDrawsOnceCold),
    ("Shanhe recovery: Huituo real red judgment generic invoice cold", ShanheDamageReplacementChecks.RealHuituoRedJudgmentRecoveryFeedsBloodBladeThroughGenericReceiptCold),
    ("Shanhe recovery: selected targets independent frozen invoices cold", ShanheDamageReplacementChecks.SelectedTargetsKeepIndependentFrozenRecoveryInvoicesCold),
    ("Shanhe boss: battle Slash responses conversions virtual uses turns cold Duel", ShanheBossChecks.BattleSlashResponsesCountCurrentConvertedVirtualUsesAcrossTurnsAndColdDuel),
    ("Shanhe boss: Shenqu maximum draw frozen request real Peach cold", ShanheBossChecks.ShenquDrawsMaximumRatherThanRefillingFreezesNativeRequestAndUsesRealPeach),
    ("Shanhe companion: fixed opening frozen faces native Fudao cold", ShanheBossChecks.FixedCompanionOpeningReplacesRandomBaseFreezesFacesAndReplaysBeforeNativeFudao),
    ("Shanhe observer: third party real Duel exclusions lost and stacked instances cold", ShanheObservedTacticChecks.ThirdPartyNeihongObservesActualDuelsExcludesOtherDamageAndLosesNativeInstancesCold),
    ("Shanhe observer: fire chain frozen base and Silver Lion cold", ShanheObservedTacticChecks.ObservedFireChainRechecksFrozenOriginBaseAndSilverLionCold),
    ("Shanhe observer: unknown public teams preserve identity privacy cold", ShanheObservedTacticChecks.UnknownPublicTeamsDoNotInferHiddenIdentityOrFactionCold),
    ("Shanhe trick ordinal: nondamaging first use and actual turn reset cold", ShanheTrickOrdinalTacticChecks.NonDamagingFirstTrickConsumesSlotAndNextActualTurnResetsCold),
    ("Shanhe trick ordinal: paid Nullification virtual Duel actor isolation cold", ShanheTrickOrdinalTacticChecks.PaidNullificationPrecedesOwnVirtualDuelAndKeepsActorIsolationCold),
    ("Shanhe trick ordinal: converted repeated AOE original receipt cold", ShanheTrickOrdinalTacticChecks.ConvertedRepeatedAoeKeepsOriginalOrdinalAcrossOwnCounterspellCold),
    ("Shanhe team tactics: living public allies native Slash debits and skill loss cold", ShanheTeamTacticChecks.JibanRechecksLivingPublicAlliesNativeSlashDebitsAndSkillLossCold),
    ("Shanhe team draw aura: independent copies other public allies phase-only cold", ShanheTeamTacticChecks.TeamDrawAuraStacksForOtherPublicAlliesOnlyDuringActualDrawPhasesCold),
    ("Shanhe team draw aura: source death loss before issue and frozen paid request cold", ShanheTeamTacticChecks.TeamDrawAuraRechecksBeforeIssueAndKeepsIssuedRequestsFrozenCold),
    ("Shanhe team tactics: real opening Draw2 native Hongde gain child cold", ShanheTeamTacticChecks.TeamOpeningUsesRealDrawBatchesWithNativeHongdeCold),
    ("Shanhe team tactics: fixed Fenghuang faces actual draws native Fudao cold", ShanheTeamTacticChecks.FixedFenghuangOpeningKeepsFacesBeforeTeamDrawAndNativeFudaoCold),
    ("Shanhe passives: dynamic source damage programs chains and skill loss cold", ShanhePassiveTacticChecks.LowHandDamageRechecksProgramsChainsAndSkillLossCold),
    ("Shanhe passives: final outgoing distance horses reverse and Slash quota cold", ShanhePassiveTacticChecks.FinalOutgoingDistanceIgnoresHorsesButKeepsReverseAndSlashLimitCold),
    ("Shanhe passives: delayed outcome inverts once without rewriting cards cold", ShanhePassiveTacticChecks.DelayedOutcomeInvertsOnceAcrossRealSourcesWithoutRewritingCardsCold),
    ("Shanhe battle: dynamic rosters private views team AI cold replay", ShanheBattleChecks.DynamicRostersPrivateViewsAndTeamAiColdReplay),
    ("Shanhe battle: bonuses physical opening cards granted skills cold replay", ShanheBattleChecks.BonusesPhysicalOpeningCardsAndGrantedSkillColdReplay),
    ("Shanhe battle: invalid recipes external collection isolation", ShanheBattleChecks.RejectInvalidRecipesAndFreezeExternalCollections),
    ("Boss activity: real ghost setup private roster nonzero human and replay", BossActivityChecks.RealGhostSetupPrivateRosterNonzeroHumanAndColdReplay),
    ("Boss activity: four stages wait for death children preserve players and replay", BossActivityChecks.FourStagesWaitForDeathChildrenPreservePlayersAndColdReplay),
    ("Boss activity: registry freezes nested metadata fingerprints and rejects invalid tier", BossActivityChecks.RegistryFreezesNestedMetadataFingerprintsAndRejectsBadTier),
    ("Boss activity: one advance sees pending stage facts and closes dead boss turn", BossActivityChecks.OneAdvanceSeesPendingStageFactsAndClosesDeadBossTurn),
    ("Hulao alliance feast: native per-target nullification and cold choice", HulaoCardChecks.AllianceFeastRetainsNativePerTargetNullificationAndColdChoice),
    ("Shenwu activity: real shared draft private rotated seats and replay", ShenwuActivityChecks.RealSharedDraftPrivacyRotatedSeatsEquipmentAndColdReplay),
    ("Shenwu activity: initial recipe permanent half health and frozen metadata", ShenwuActivityChecks.ExactInitialRecipePermanentHalfHealthAndFrozenMetadata),
    ("Shenwu activity: leader victory waits for death children and extinction", ShenwuActivityChecks.LeaderVictoryWaitsForDeathChildrenAndAlliedExtinctionLoses),
    ("Shenwu NPC: native damage policies replacement draw and turnover", ShenwuNpcChecks.NativeDamagePoliciesReplacementDrawAndTurnover),
    ("Shenwu NPC: damage source and recovery participants draw once", ShenwuNpcChecks.DamageSourceAndRecoveryParticipantsDrawExactlyOnce),
    ("Shenwu NPC: Mingwan later targets and actual ended turn Yandu", ShenwuNpcChecks.MingwanRestrictsLaterUsesAndYanduUsesActualEndedTurn),
    ("Shenwu NPC: Luanchang actual turn start end native AOE parents", ShenwuNpcChecks.LuanchangOwnStartAndActualEndKeepNativeAoeParents),
    ("Shenwu NPC: Futai Peach policy and native coalition death supply", ShenwuNpcChecks.FutaiPeachPolicyAndNativeCoalitionDeathBenefits),
    ("Shenwu expanded: isolated eight beasts five tiers and one dying awakening", ShenwuNpcChecks.ExpandedRecipesAndDyingAwakening),
    ("Shenwu expanded: native fire nine heads and locked freeze", ShenwuNpcChecks.ExpandedFireNineHeadsAndFreeze),
    ("Shenwu expanded: poison exact payments and repeated native judgments cold", ShenwuNpcChecks.ExpandedPoisonPaymentsAndYanyuJudgments),
    ("Shenwu expanded: random damage Xunyou and ended turn Slash cold", ShenwuNpcChecks.ExpandedRandomReturnsXunyouAndBingxian),
    ("Shenwu expanded: Sipu first two responses and real enemy tiers cold", ShenwuNpcChecks.ExpandedSipuAndRealTierInitialization),
    ("Shenwu expanded: frozen burst waits for each dying awakening child cold", ShenwuNpcChecks.ExpandedBurstWaitsForEachDyingChild),
    ("Shenwu expanded: continuous freeze red discard Juyuan and forced identity cold", ShenwuNpcChecks.ExpandedContinuousPoliciesAndPoisonIdentity),
    ("Shenwu expanded: black judgment stops and Echou native Peach use cold", ShenwuNpcChecks.ExpandedBlackJudgmentAndEchouActualUse),
    ("Shenwu cards: native requests payments and cold returns", ShenwuCardChecks.OrdinaryTricksRetainNativeRequestsPaymentsAndColdReturns),
    ("Shenwu equipment: original entities and native effects", ShenwuCardChecks.ExclusiveEquipmentUsesOriginalEntitiesAndNativeEffects),
    ("Nian NPC: real draw replacement dormant targets and Slash payments", NianNpcChecks.BaseDrawRandomReplacementDormancyAndNativeSlash),
    ("Nian activity: real package rotated draft tier five death children and replay", NianPackageChecks.RealPackageRotatedDraftTierFiveDeathChildrenAndColdReplay),
    ("Nian activity: round bags preserve state across extras death skips and replay", NianActivityChecks.RoundBagsPreserveNativeStateAcrossExtrasDeathsAndCold),
    ("Nian clock: expiry retires paid physical Slash children and replay", NianActivityChecks.NativeClockExpiresPaidSlashChildWithoutRepaymentAndCold),
    ("Nian NPC: private wise keeps and actual loss batches", NianNpcChecks.RuizhiPrivateKeepsAndJingjueRealMixedLossBatches),
    ("Nian NPC: actual skipped phases and outside turn per point draws", NianNpcChecks.ActualSkipLedgerAndOutsideTurnPerPointDamageRecovery),
    ("Nian revival: shared three uses and real dead ally restoration", NianRevivalChecks.SharedRevivalRestoresRealDeadAllyThreeTimes),
    ("Nian revival: AI restores human with cold draw and limited usage", NianRevivalChecks.AiRevivesHumanWithColdDrawAndPreservesLimitedUsage),
    ("Nian clock: expiry retires paid revival children without refund", NianRevivalChecks.TimeoutCancelsPaidRevivalDrawWithoutRefundOrReplay),
    ("Nian clock: expiry disposes only the current judgment entity", NianRevivalChecks.TimeoutDisposesOnlyActiveJudgmentEntity),
    ("Nian revival: native draw attempt returns before physical cards", NianRevivalChecks.RevivalWaitsForNativeDrawAttemptBeforePhysicalCards),
    ("Activity ordinary tricks: Qice mode names native materials and source isolation", ActivityOrdinaryTrickChecks.QiceUsesModeNamesWithAllRealMaterialsAndUnchangedSource),
    ("Activity ordinary tricks: tiered one zero materials native receipts and quota", ActivityOrdinaryTrickChecks.TieredOneAndZeroMaterialUsesKeepNativeReceiptsAndQuota),
    ("Activity ordinary tricks: Zhenlie static filter actual activity target", ActivityOrdinaryTrickChecks.ZhenlieStaticOrdinaryFilterCancelsActualActivityTarget),
    ("Activity ordinary tricks: declared entity private declarer native cost", ActivityOrdinaryTrickChecks.ClassicDeclaredEntityKeepsActivityNamePrivateDeclarerAndNativeCost),
    ("Cixiong multi target: staged choices current Slash cursor private cold", CixiongMultiTargetChecks.StagedChoicesKeepTheCurrentNativeSlashTargetAndColdReplay),
    ("Hulao special equipment: native effects and setup entities", HulaoCardChecks.EquipmentGrantsRespectNativeEffectsAndSetupEntities),
    ("Hulao rules: armor prevents whole large damage and coalition death supply", HulaoRuleChecks.AozhanPreventsWholeLargeDamageAndUnitedSuppliesSurvivors),
    ("Hulao rules: real tier five compatible setup and ordinary skill replay", HulaoRuleChecks.RealHulaoSetupTierFiveCompatibleRosterAndKurouColdReplay),
    ("Hulao rules: Xiuluo pays owned discard before real judgment", HulaoRuleChecks.XiuluoPaysNativeOwnedDiscardBeforeRemovingRealJudgment),
    ("Hulao rules: Shenqu physical Peach and Jiwu turn expiry", HulaoRuleChecks.ShenquUsesPhysicalPeachAndJiwuSkillsExpireAtActualTurnEnd),
    ("Hulao transition: paid damage interrupts preserves zones and replay", HulaoTransitionChecks.PaidDamageInterruptsAtFourOrZeroPreservesZonesAndColdReplay),
    ("Hulao transition: real reshuffle interrupts and second waits for turn end", HulaoTransitionChecks.RealDrawShuffleInterruptsThenSecondShuffleWaitsForActualTurnEnd),
    ("Hulao transition: allied and final boss deaths finish children", HulaoTransitionChecks.AlliedAndFinalBossDeathsFinishChildrenBeforeVictory),
    ("Ghost rules: Enyuan hearts per damage and actual recovery points", GhostRuleChecks.EnyuanRequiresHeartsPerDamageAndRewardsActualRecoveryPoints),
    ("Ghost rules: death source discard preserves native zones and replay", GhostRuleChecks.GhostDeathDiscardsTheKillerThroughNativeZones),
    ("Ghost rules: judgment discard and red black consequences use actual cards", GhostRuleChecks.GhostJudgmentDiscardAndRedBlackConsequencesUseActualCards),
    ("Ghost virtual trick: native responses nullification and cold return", GhostVirtualTrickChecks.GroupUsesRetainNativeResponsesNullificationAndColdReturn),
    ("Jiange activity: five-tier players scaled NPCs private faction roster and replay", JiangeActivityChecks.SetupTiersTeamsPrivateRosterAndReplay),
    ("Jiange activity: Fuyou repeats one red trick use and replays", JiangeActivityChecks.RedTrickRepeatsOnePhysicalUseAndReplays),
    ("Jiange activity: native NPC enemy effects and limited rescue replay", JiangeActivityChecks.NpcEffectsAndLimitedRescueUseRealCommands),
    ("native logical discard invocations preserve whole mixed and cross-prompt costs", NativeLogicalDiscardChecks.WholeLogicalDiscardsKeepCrossPromptAndMixedMaterialInvoicesCold),
    ("native effective faction changes preserve avatar declarations and setup boundaries", NativeFactionChecks.AvatarAcquisitionRedeclarationSourceLossAndSetupCold),
    ("Meng Da: GoudeWholeInvocationHistoryAndOptionMasksCold", MengDaChecks.GoudeWholeInvocationHistoryAndOptionMasksCold),
    ("Cai Mao: whole reveal decline HP privacy and Miuyan replay", CaiMaoChecks.WholeRevealDeclineHpLimitPrivacyAndMiuyanCold),
    ("actual phase end: six actual phases typed cold returns skip and restoration", ActualPhaseEndChecks.SixActualPhaseEndsKeepTypedColdReturnsAndSkipRestorationBoundaries),
    ("Ya Dan: shortest living arc tie opaque and native discards replay", YaDanChecks.QingyaShortestLivingArcTieOpaqueAndNativeDiscardsCold),
    ("Ya Dan: next actual phase original card native uses and expiry replay", YaDanChecks.QingyaNextActualPhaseOriginalCardNativeUsesAndExpiryCold),
    ("Ya Dan: paid children loss death and recursive Slash replay", YaDanChecks.QingyaPaidChildrenLossDeathAndRecursiveSlashCold),
    ("Ya Dan: whole round true uses actor replacement and reset replay", YaDanChecks.TielunWholeRoundTrueUsesActorReplacementAndResetCold),
    ("Cao Teng: setup and first actual dying rescue faction replay", CaoTengChecks.SetupAndFirstActualDyingRescueFactionCold),
    ("Cao Teng: ordered private distinct benefits recovery and restore replay", CaoTengChecks.OrderedPrivateDistinctBenefitsRecoveryAndRestoreCold),
    ("Cao Teng: permanent hand growth linked skills and own turn expiry replay", CaoTengChecks.PermanentHandGrowthLinkedAndGrantOwnTurnExpiryCold),
    ("Cao Teng: paid native children source loss death and exact claim replay", CaoTengChecks.PaidNativeChildrenSourceLossDeathAndExactClaimCold),
    ("Yang Fu: whole actual turn ordinal dynamic targets and replay", YangFuChecks.BingzhongWholeActualTurnOrdinalDynamicTargetsAndCold),
    ("Yang Fu: normal target policies and typed tail replay", YangFuChecks.BingzhongNormalTargetPoliciesAndTypedTailCold),
    ("Yang Fu: native stock equipment children and Slash replay", YangFuChecks.SuyiNativeStockEquipmentChildrenAndSlashCold),
    ("Yang Fu: damage draw and paid loss death replay", YangFuChecks.BingzhongDamageDrawAndSuyiPaidLossDeathCold),
    ("Da Qiao Xiao Qiao: public pile derived sources zero transitions and replay", DaQiaoXiaoQiaoChecks.PublicPileDerivedSourcesZeroTransitionsAndCold),
    ("Ma Yuan Yi: actual draw threshold native pile and privacy replay", MaYuanYiChecks.JibingActualDrawThresholdNativePileAndPrivacyCold),
    ("Ma Yuan Yi: actual use played Dodge Duel and forced Slash replay", MaYuanYiChecks.WangjingActualUsePlayedDodgeDuelAndForcedSlashCold),
    ("Ma Yuan Yi: all factions awakening maximum loss grant and death replay", MaYuanYiChecks.MoucuanAllFactionsThresholdMaximumLossGrantAndDeathCold),
    ("Ma Yuan Yi: historical Bing judgment replacement thunder children replay", MaYuanYiChecks.BinghuoHistoricalBingJudgmentReplacementThunderChildrenCold),
    ("Liu Ba: Tongdu original entity and genuine PlayEnding", LiuBaChecks.TongduOriginalEntityTruePlayEndDebtAndCold),
    ("Liu Ba: dynamic maximum recast and exact coins", LiuBaChecks.ZhubiDynamicMaximumRecastAndMintChildrenCold),
    ("Liu Ba: private bottom slots atomic exchange", LiuBaChecks.CoinEndingPrivateBottomSlotsAtomicExchangeCold),
    ("Liu Ba: transferred coins and paid native children", LiuBaChecks.PermanentTransferredCoinsPaidLossDeathAndRecoveryCold),
    ("Bao Xin: real effective Hand pile random ring and cold", BaoXinChecks.MutaoRealEffectiveHandPileRandomRingAndCold),
    ("Bao Xin: foreign damage current Slash count and issued source loss", BaoXinChecks.MutaoForeignDamageCurrentSlashCountAndIssuedSourceLoss),
    ("Bao Xin: actual damage frozen directional distance and deck obtain", BaoXinChecks.YimouActualDamageFrozenDirectionalDistanceAndDeckObtain),
    ("Bao Xin: victim private gift then native draw once", BaoXinChecks.YimouVictimPrivateGiftThenNativeDrawOnce),
    ("Cui Yan: self equipment multi-material and next use cold", CuiYanChecks.ShutongSelfEquipmentMultiMaterialAndNextUseCold),
    ("Cui Yan: true responses played and turn expiry cold", CuiYanChecks.ShutongTrueResponsesPlayedAndTurnExpiryCold),
    ("Cui Yan: pre-skill history actual end and range cold", CuiYanChecks.ZhijiePreSkillHistoryActualEndAndRangeCold),
    ("Cui Yan: issued loss death and legacy Alcohol cold", CuiYanChecks.ShutongIssuedLossDeathAndLegacyAlcoholCold),
    ("Cui Yan faction supply: requester suit and native Liubing projection cold", CuiYanFactionSupplyChecks.RequesterSuitRestrictionAndNativeLiubingProjectionCold),
    ("Native recast invocation: single and whole group are distinct", NativeRecastInvocationChecks.NativeSingleAndWholeGroupAreDistinct),
    ("Native recast invocation: two participants independent and cold", NativeRecastInvocationChecks.TwoParticipantsOwnIndependentFrozenInvocationsCold),
    ("Native recast invocation: paid loss death and zero draw cold", NativeRecastInvocationChecks.PaidLossAndDeathKeepZeroDrawCompletionCold),
    ("Native recast invocation: equipment recovery and Ox spill cold", NativeRecastInvocationChecks.EquipmentRecoveryAndOxSpillAreNotExtraRecastsCold),
    ("Li Yi: virtual Stone Axe real weapon and paid children cold", LiYiChecks.LiYiVirtualStoneAxeRealWeaponAndPaidChildrenCold),
    ("Li Yi: private same choices recast Lion and Ox cold", LiYiChecks.ChanshuangPrivateSameChoicesRecastLionOxCold),
    ("Li Yi: native Slash quota zero supply and faction cold", LiYiChecks.ChanshuangNativeSlashQuotaZeroSupplyAndFactionCold),
    ("Li Yi: whole actual turn history and first X ending cold", LiYiChecks.ChanshuangWholeActualTurnHistoryAndFirstXEndingCold),
    ("Sun Yuan: native obtain reply and round range cold", SunYuanChecks.SayingNativeObtainReplyAndRoundRangeCold),
    ("Sun Yuan: independent directions recursive damage and zero cost cold", SunYuanChecks.DongxinIndependentDirectionsRecursiveDamageAndZeroCostCold),
    ("Sun Yuan: exact entity quota reacquisition and identity cold", SunYuanChecks.DongxinExactEntityQuotaReacquisitionAndIdentityCold),
    ("Sun Yuan: paid loss death claims and equipment children cold", SunYuanChecks.DongxinPaidLossDeathClaimsAndEquipmentChildrenCold),
    ("Liu Zhang: round draw original entities and actual skip cold", LiuZhangChecks.FengweiRoundDrawOriginalEntitiesAndActualSkipCold),
    ("Liu Zhang: native card damage chain and prevention cold", LiuZhangChecks.FengweiNativeCardDamageChainAndPreventionCold),
    ("Liu Zhang: true use request families and played exclusions cold", LiuZhangChecks.ZonghuTrueUseRequestFamiliesAndPlayedExclusionsCold),
    ("Liu Zhang: exact gifts Lion Ox source loss death and quota cold", LiuZhangChecks.ZonghuExactGiftsLionOxSourceLossDeathAndQuotaCold),
    ("Saying native supply: zero suppliers preserve foreign actor and native return cold", Batch49NativeSupplyChecks.SayingZeroSuppliersKeepForeignActorAndNativeReturnsCold),
    ("Batch51 native boundary: dynamic Leiluan admission and legacy fingerprints", Batch51NativeBoundaryChecks.LeiluanPolicyKeepsDynamicAdmissionAndLegacyFingerprints),
    ("Kong Shu: dynamic count last hand and native use cold", KongShuChecks.LeiluanDynamicCountLastHandRewardAndNativeUseCold),
    ("Kong Shu: true responses played and pre skill round names cold", KongShuChecks.LeiluanTrueResponsePlayedAndPreSkillRoundNamesCold),
    ("Kong Shu: true basic completion and whole round ending cold", KongShuChecks.FuchaoTrueBasicCompletionAndRoundEndingWholeQueuesCold),
    ("Kong Shu: equipment payments source loss death privacy cold", KongShuChecks.LeiluanEquipmentPaymentsSourceLossDeathAndPrivacyCold),
    ("Ma Cheng: native extra targets and whole Slash cold", MaChengChecks.ChenglieTargetsNativeMultiAndZeroUsesCold),
    ("Ma Cheng: secret original entities and deck edges cold", MaChengChecks.ChenglieSecretOriginalEntitiesPrivacyAndDeckEdgesCold),
    ("Ma Cheng: responses versus recovery cold", MaChengChecks.ChenglieResponsesRecoveryAndTrueNativeDirectionCold),
    ("Ma Cheng: paid Lion Ox source loss death cold", MaChengChecks.ChengliePaidLionOxSourceLossAndDeathCold),
    ("Sun Hong: frozen adjustment and random original returns cold", SunHongChecks.XianbiFrozenAdjustmentAndRandomOriginalReturnsCold),
    ("Sun Hong: private whole discard children source loss death cold", SunHongChecks.XianbiPrivateWholeDiscardChildrenLossDeathCold),
    ("Sun Hong: opaque replacement actual phase and victim choice cold", SunHongChecks.ZenrunOpaqueReplacementActualPhaseAndVictimChoiceCold),
    ("Sun Hong: Lion Ox and nested paid loss death cold", SunHongChecks.ZenrunLionOxAndNestedXianbiPaidLossDeathCold),
    ("Sun Hong: actual normal draw phase replacement cold", SunHongChecks.ZenrunActualNormalDrawPhaseReplacementCold),
    ("Native draw attempt: private decision and actual result cold", NativeDrawAttemptChecks.ProgramDrawWaitsForPrivateDecisionAndBindsOnlyActualCardsCold),
    ("Native draw attempt: sequential recipients and actual gain observers cold", NativeDrawAttemptChecks.SelectedRecipientsWaitForEachActualDrawAndObserverCold),
    ("Native draw attempt: zero request and absent capability", NativeDrawAttemptChecks.ZeroRequestAndAbsentCapabilityKeepSynchronousDrawContract),
    ("Native draw attempt: actual normal extra and skipped phases cold", NativeDrawAttemptChecks.ActualNormalAndExtraDrawWaitForExactAttemptCold),
    ("Native card draw: DrawTwo attempts actual gain children and repeated effects cold", NativeDrawCardEffectChecks.NativeDrawTwoWaitsForAttemptAndActualGainChildrenCold),
    ("Native card draw: Zenrun replaces DrawTwo without forged draws cold", NativeDrawCardEffectChecks.ZenrunReplacesNativeDrawTwoWithoutForgingDrawMaterialsCold),
    ("Guan Hai: two real Hand baits true Use and played boundaries cold", GuanHaiChecks.XieweiAtomicHandBaitsTrueUsesAndPlayedBoundariesCold),
    ("Guan Hai: minimum entity native Pindian ties and empty boundaries cold", GuanHaiChecks.YouqueMinimumEntityNativePindianTieAndEmptyBoundariesCold),
    ("Guan Hai: whole discard foreign Slash and latest Spade reward cold", GuanHaiChecks.YouqueWholeDiscardForeignSlashAndLatestSpadeRewardCold),
    ("Guan Hai: paid children source loss death Lion Ox and rejection cold", GuanHaiChecks.GuanHaiPaidChildrenSourceLossDeathLionOxAndInputRejectionCold),
    ("Xie Ling Yu: first complete discard group and permanent deletion cold", XieLingYuChecks.YuandiFirstCompleteDiscardGroupAndPermanentDeletionCold),
    ("Xie Ling Yu: actual turn reset native uses and played boundaries cold", XieLingYuChecks.YuandiActualTurnResetNativeUsesAndPlayedBoundariesCold),
    ("Xie Ling Yu: actual draw names five limit and exempt hand cards cold", XieLingYuChecks.XinyouActualDrawNamesFiveLimitAndExemptHandCardsCold),
    ("Xie Ling Yu: paid children replacement source loss death and privacy cold", XieLingYuChecks.XinyouPaidChildrenReplacementSourceLossDeathAndInputPrivacyCold),
    ("Li Feng: initial categories preparation leases and ending distribution cold", LiFengChecks.JiyunInitialCategoriesPreparationLeasesAndEndingDistributionCold),
    ("Li Feng: original play kinds and whole use collection cold", LiFengChecks.ShuliangOriginalPlayKindsAndWholeUseCollectionCold),
    ("Li Feng: native responses played quota and faction supply cold", LiFengChecks.ShuliangNativeResponsesPlayedQuotaAndFactionSupplyCold),
    ("Li Feng: paid children source loss death and privacy cold", LiFengChecks.LiFengPaidChildrenSourceLossDeathAndInputPrivacyCold),
    ("Ordinary Cai Fu Ren: actual turn use targets and single Qieting choice cold", CardGame.Core.Tests.OrdinaryCaiFuRenChecks.QietingActualTurnUseTargetsAndSingleChoiceCold),
    ("Ordinary Cai Fu Ren: empty slot equipment movement and native children cold", CardGame.Core.Tests.OrdinaryCaiFuRenChecks.QietingEmptySlotEquipmentMovementAndNativeChildrenCold),
    ("Ordinary Cai Fu Ren: whole equipment gift recipient choices and quota cold", CardGame.Core.Tests.OrdinaryCaiFuRenChecks.XianzhouWholeEquipmentGiftRecipientChoicesAndQuotaCold),
    ("Ordinary Cai Fu Ren: paid source loss deaths privacy and replay cold", CardGame.Core.Tests.OrdinaryCaiFuRenChecks.XianzhouPaidSourceLossDeathsPrivacyAndReplayCold),
    ("Huan Shu Jun: round adjustment true basic uses and native responses cold", CardGame.Core.Tests.HuanShuJunChecks.HuanpeiRoundAdjustmentTrueBasicUsesAndNativeResponsesCold),
    ("Huan Shu Jun: Peach and gifted equipment use restoration children cold", CardGame.Core.Tests.HuanShuJunChecks.WenyiPeachAndGiftedEquipmentUseRestoreChildrenCold),
    ("Huan Shu Jun: exact damage refresh and native draw cold", CardGame.Core.Tests.HuanShuJunChecks.MiaoxinExactDamageReplenishesGameAllowanceAndNativeDrawCold),
    ("Huan Shu Jun: paid source loss death privacy and rejected input cold", CardGame.Core.Tests.HuanShuJunChecks.PaidSourceLossDeathPrivacyAndRejectedInputCold),
    ("Zhao Zhong: first actual turn permanent grants and regrant cold", CardGame.Core.Tests.ZhaoZhongChecks.PengbiFirstActualTurnPermanentGrantsAndRegrantCold),
    ("Zhao Zhong: original Hand gift independent next chain leases cold", CardGame.Core.Tests.ZhaoZhongChecks.DiciOriginalHandGiftIndependentNextChainLeasesCold),
    ("Zhao Zhong: first positive recovery nonstacking native damage cold", CardGame.Core.Tests.ZhaoZhongChecks.YintianFirstPositiveRecoveryNonstackingNativeDamageCold),
    ("Zhao Zhong: actual non-draw gains and native draw children cold", CardGame.Core.Tests.ZhaoZhongChecks.BiriActualNonDrawGainsAndNativeDrawChildrenCold),
    ("Tao Qian: whole virtual Slash and unique damage payment cold", CardGame.Core.Tests.TaoQianChecks.ZongluanWholeVirtualSlashAndUniqueDamagePaymentCold),
    ("Tao Qian: first victim damage original Hand and turn timing cold", CardGame.Core.Tests.TaoQianChecks.ZhaohuoFirstVictimDamageOriginalHandAndTurnTimingCold),
    ("Tao Qian: marked Use Played and hand limit expiry cold", CardGame.Core.Tests.TaoQianChecks.ZhaohuoMarkedUsePlayedAndHandLimitExpiryCold),
    ("Tao Qian: per recipient native draw replacement paid rights cold", CardGame.Core.Tests.TaoQianChecks.WenrenPerRecipientNativeDrawReplacementPaidRightsCold),
    ("Le Da Qiao Zixi: original Qin subslots placement judgment and movement cold", CardGame.Core.Tests.LeDaQiaoZixiChecks.ZixiOriginalQinSubslotsPlacementJudgmentAndMovementCold),
    ("Le Da Qiao Zixi: sole target Basic trick repeat and native draw cold", CardGame.Core.Tests.LeDaQiaoZixiChecks.ZixiSoleTargetBasicTrickRepeatAndNativeDrawCold),
    ("Le Da Qiao Zixi: three Judgment Equipment obtain damage and paid children cold", CardGame.Core.Tests.LeDaQiaoZixiChecks.ZixiThreeJudgmentEquipmentObtainDamageAndPaidChildrenCold),
    ("Le Da Qiao Zixi: final targets responses source rights and input privacy cold", CardGame.Core.Tests.LeDaQiaoZixiChecks.ZixiFinalTargetsResponsesSourceRightsAndInputPrivacyCold),
    ("Le Xiao Qiao Qiqin: opening permanent global entities and hand limit cold", CardGame.Core.Tests.LeXiaoQiaoQiqinChecks.QiqinOpeningPermanentGlobalEntitiesAndHandLimitCold),
    ("Le Xiao Qiao Qiqin: preparation whole shared discard pile cold", CardGame.Core.Tests.LeXiaoQiaoQiqinChecks.QiqinPreparationObtainsWholeSharedDiscardPileCold),
    ("Le Xiao Qiao Qiqin: paid children source rights death and replay cold", CardGame.Core.Tests.LeXiaoQiaoQiqinChecks.QiqinPaidObtainChildrenSourceLossDeathAndReplayCold),
    ("Le Xiao Qiao Weiwan: random region suits and actual gain branches cold", CardGame.Core.Tests.LeXiaoQiaoWeiwanChecks.WeiwanRandomRegionSuitCohortAndActualGainBranchesCold),
    ("Le Xiao Qiao Weiwan: equipment Qin Lion Ox and effective suit cold", CardGame.Core.Tests.LeXiaoQiaoWeiwanChecks.WeiwanEquipmentQinCostsLionOxAndEffectiveSuitCold),
    ("Le Xiao Qiao Weiwan: directed all Use distance quota bans and expiry cold", CardGame.Core.Tests.LeXiaoQiaoWeiwanChecks.WeiwanDirectedAllUseDistanceCountBanAndTurnExpiryCold),
    ("Le Xiao Qiao Weiwan: paid source loss death and input privacy cold", CardGame.Core.Tests.LeXiaoQiaoWeiwanChecks.WeiwanPaidSourceLossDeathAndInputPrivacyCold),
    ("Le Cai Wen Ji: opening entities hand limit and incoming distance cold", CardGame.Core.Tests.LeCaiWenJiChecks.ShuangjiaOpeningGlobalEntitiesHandLimitAndIncomingDistanceCold),
    ("Le Cai Wen Ji: whole marked loss missing suits and native children cold", CardGame.Core.Tests.LeCaiWenJiChecks.BeifenWholeMarkedLossMissingSuitObtainAndNativeChildrenCold),
    ("Le Cai Wen Ji: all Use range quota and other bans cold", CardGame.Core.Tests.LeCaiWenJiChecks.BeifenAllCardUseDistanceAndNaturalLimitsKeepOtherBansCold),
    ("Le Cai Wen Ji: issued loss source rights privacy and replay cold", CardGame.Core.Tests.LeCaiWenJiChecks.BeifenIssuedLossSourceRightsPrivacyAndReplayCold),
    ("Wu Guan Yu Juewu: all eight damage names original materials and native quota cold", CardGame.Core.Tests.WuGuanYuJuewuChecks.JuewuAllDamageNamesUseOriginalEntitiesAndNativeQuotaCold),
    ("Wu Guan Yu Juewu: foreign hand rank residency appearance and pindian cold", CardGame.Core.Tests.WuGuanYuJuewuChecks.JuewuForeignHandRankResidencyAppearanceAndPindianCold),
    ("Wu Guan Yu Juewu: true forced uses reject played and paid child rights cold", CardGame.Core.Tests.WuGuanYuJuewuChecks.JuewuTrueForcedUsesRejectPlayedAndPaidChildrenRightsCold),
    ("Wu Guan Yu Juewu: flood ordered nullification and native children cold", CardGame.Core.Tests.WuGuanYuJuewuChecks.WaterOriginalOrderNullificationAndNativeChildrenCold),
    ("Wu Guan Yu Wuyou: private random contribution self and phase rights cold", CardGame.Core.Tests.WuGuanYuWuyouChecks.WuyouContributionRandomPrivateSelfAndPhaseRightsCold),
    ("Wu Guan Yu Wuyou: named original use residency and paid children cold", CardGame.Core.Tests.WuGuanYuWuyouChecks.WuyouNamedOriginalUseResidencyAndPaidChildrenCold),
    ("Wu Guan Yu Wuyou: later phase names native responses and supplied use cold", CardGame.Core.Tests.WuGuanYuWuyouChecks.WuyouLaterPhaseNamesNativeResponsesAndProvidedUseCold),
    ("Wu Guan Yu Yixian: all field equipment native children and original owners cold", CardGame.Core.Tests.WuGuanYuYixianChecks.FieldEquipmentAtomicOriginalOwnersLionOxAndCompensationCold),
    ("Wu Guan Yu Yixian: discard equipment limited and empty branches cold", CardGame.Core.Tests.WuGuanYuYixianChecks.DiscardEquipmentObtainLimitedAndEmptyBranchesCold),
    ("Wu Guan Yu Yixian: paid source loss deaths privacy and replay cold", CardGame.Core.Tests.WuGuanYuYixianChecks.PaidSourceLossDeathNativeChildrenAndInputPrivacyCold),
    ("Wu Guan Yu Juewu: foreign Hand rank sum original entities and paid source rights cold", CardGame.Core.Tests.WuGuanYuRankConsumerChecks.ForeignHandExactRankGiftKeepsPrintedEntitiesAndPaidSourceRightsCold),
    ("Wu Guan Yu Juewu: foreign Hand maximum ascending history and hand comparison cold", CardGame.Core.Tests.WuGuanYuRankConsumerChecks.ForeignHandMaximumAscendingAndHandComparisonUseEffectiveRankCold),
    ("Le Zou Shi: opening entities dynamic non-locked suppression restore cold", CardGame.Core.Tests.LeZouShiChecks.YunzhengOpeningEntitiesDynamicNonLockedSuppressionAndRestoreCold),
    ("Le Zou Shi: private slots previous marks effective suit obtain cold", CardGame.Core.Tests.LeZouShiChecks.HuoxinPrivateSlotsOriginalMarkAndEffectiveSuitObtainCold),
    ("Le Zou Shi: true response forced targetless uses Played exclusions cold", CardGame.Core.Tests.LeZouShiChecks.HuoxinTrueResponseForcedAndNoTargetUsesExcludePurePlayedCold),
    ("Le Zou Shi: native equipment payment paid source children cold", CardGame.Core.Tests.LeZouShiChecks.HuoxinNativeEquipmentPaymentAndPaidSourceChildrenCold),
    ("Le Diao Chan: frozen opening whole Hand inversion actual limit cold", CardGame.Core.Tests.LeDiaoChanChecks.TanbanFrozenOpeningWholeHandInversionAndActualLimitCold),
    ("Le Diao Chan: retained show native basic trick delayed equipment cold", CardGame.Core.Tests.LeDiaoChanChecks.DiouRetainedShowNativeBasicTrickDelayedAndEquipmentCold),
    ("Le Diao Chan: true response Played exclusion actual turn show OR name cold", CardGame.Core.Tests.LeDiaoChanChecks.DiouTrueResponsePlayedExclusionAndActualTurnShowOrSameNameCold),
    ("Le Diao Chan: paid native children source loss death carrier retirement cold", CardGame.Core.Tests.LeDiaoChanChecks.DiouPaidNativeChildrenSourceLossDeathAndCarrierRetirementCold),
    ("Mou Huang Gai: real PlayStart gifts per point Loss Armor privacy cold", CardGame.Core.Tests.MouHuangGaiChecks.RealPlayStartGiftsPerPointLossArmorPaymentPrivacyCold),
    ("Mou Huang Gai: actual Draw timing dynamic own turn Use privileges cold", CardGame.Core.Tests.MouHuangGaiChecks.ActualDrawTimingDynamicOwnTurnUsePrivilegesCold),
    ("Mou Huang Gai: native responses Played foreign turn boundaries cold", CardGame.Core.Tests.MouHuangGaiChecks.NativeResponsesPlayedAndForeignTurnBoundariesCold),
    ("Mou Huang Gai: paid gift source loss dying rescue frozen Use cold", CardGame.Core.Tests.MouHuangGaiChecks.PaidGiftSourceLossDyingRescueAndFrozenUseCold),
    ("Wu Zhang Fei: true Use material ranks private sampling cold", CardGame.Core.Tests.WuZhangFeiChecks.TrueUseMaterialRanksPrivateSamplingAndCold),
    ("Wu Zhang Fei: causal discard obtain victim damage Ox spill cold", CardGame.Core.Tests.WuZhangFeiChecks.CausalDiscardObtainVictimDamageAndOxSpillCold),
    ("Wu Zhang Fei: private actual turn rank cycles phase budgets reset cold", CardGame.Core.Tests.WuZhangFeiChecks.PrivateActualTurnRankCyclesPhaseBudgetsAndResetCold),
    ("Wu Zhang Fei: Haoxian real shuffle multi owner gain native dying cold", CardGame.Core.Tests.WuZhangFeiChecks.HaoxianRealShuffleMultiOwnerGainNativeDyingAndCold),
    ("Wu Lu Kang: Shenduan real pair global group payments and cold", CardGame.Core.Tests.WuLuKangPindianChecks.ShenduanRealPairGlobalGroupPaymentsAndCold),
    ("Wu Lu Kang: Kegou actual boundaries distinct minima repeat native Slash cold", CardGame.Core.Tests.WuLuKangPindianChecks.KegouActualBoundariesDistinctMinimaRepeatAndNativeSlashCold),
    ("Wu Lu Kang: Dixian successful guess effective rank native quota lifetime cold", CardGame.Core.Tests.WuLuKangDixianChecks.SuccessfulGuessEffectiveRankNativeQuotaAndLifetimeCold),
    ("Wu Lu Kang: Dixian failed guess exact K regions native children cold", CardGame.Core.Tests.WuLuKangDixianChecks.FailedGuessExactKRegionsNativeChildrenAndCold),
    ("Mou Xiahou Shi Mobile: Yanyu real discard actual Play quota whole turn late inserted support cold", CardGame.Core.Tests.MouXiahouShiYanyuChecks.ActualDiscardPhaseQuotaWholeTurnInsertedSupportCold),
    ("Mou Xiahou Shi Mobile: Yanyu paid discard children source loss regrant rescue cold", CardGame.Core.Tests.MouXiahouShiYanyuChecks.PaidDiscardChildrenSourceLossRegrantAndNativeRescueCold),
    ("Mou Xiahou Shi Mobile: Qiaoshi foreign source native damage armor and turn budget cold", CardGame.Core.Tests.MouXiahouShiQiaoshiChecks.ForeignSourceChoiceActualDamageArmorAndTurnBudgetCold),
    ("Mou Xiahou Shi Mobile: Qiaoshi native recovery replacement paid children source loss cold", CardGame.Core.Tests.MouXiahouShiQiaoshiChecks.NativeRecoveryReplacementPaidChildrenSourceLossAndCold),
    ("Le Zhuge Guo: native reorder and same Use effects", CardGame.Core.Tests.LeZhugeGuoChecks.NativeReorderAndSameUseEffects),
    ("Mou Zhen Ji Mobile: seated reveal color movement hand limit cold", CardGame.Core.Tests.MouZhenJiMobileChecks.SeatedRevealColorMovementHandLimitCold),
    ("Mou Yuan Shao Mobile: any pair native Arrow responses independent actual turn caps and Lord targets", CardGame.Core.Tests.MouYuanShaoMobileChecks.AnyPairNativeArrowResponsesIndependentTurnCapsAndLordTargets),
    ("Mou Zhu Rong Mobile: native designation contest deferred damage and outside Assault gift", CardGame.Core.Tests.MouZhuRongMobileChecks.NativeDesignationContestDeferredDamageAndOutsideAssaultGift),
    ("Mou Tian Yu Mobile: native Backwater discard field move and permanent range or Slash", CardGame.Core.Tests.MouTianYuMobileChecks.NativeBackwaterDiscardFieldMoveAndPermanentRangeOrSlash),
    ("OL Cheng Ji Cheng Cui: native bottom cost multi Dodge and whole turn material promise", CardGame.Core.Tests.OlChengJiChengCuiChecks.NativeBottomCostMultiDodgeAndOriginalWholeTurnMaterialPromise),
    ("Mou Da Qiao Mobile: native Guose mode budget removal and Heart Liuli issued Play", CardGame.Core.Tests.MouDaQiaoMobileChecks.NativeGuoseModeBudgetRemovalAndHeartLiuliIssuedPlay),
    ("Mou Da Qiao Mobile: Identity Guose shares four native uses", CardGame.Core.Tests.MouDaQiaoMobileChecks.IdentityGuoseSharesFourNativeUses),
    ("Mou Huang Yueying Mobile: native Jizhi exact exemption and Qicai three received entities", CardGame.Core.Tests.MouHuangYueyingMobileChecks.NativeJizhiExactExemptionAndQicaiThreeReceivedEntities),
    ("OL Mo Lu Bu: native opening Qi Gangquan reciprocal Kuang and death return", CardGame.Core.Tests.OlMoLuBuChecks.NativeOpeningQiGangquanReciprocalKuangAndDeathReturn),
    ("OL Mo Sima Yi: actual damage records marked aliases learning and round Slash return", CardGame.Core.Tests.OlMoSimaYiChecks.ActualDamageRecordsMarkedAliasesLearningAndRoundSlashReturn),
    ("Mou Cheng Yu OL: stacked targets and actual next-Use native rewards Cold", CardGame.Core.Tests.MouChengYuOlChecks.StackedTargetsAndActualNextUseNativeRewardsCold),
    ("OL Mo Zhang Fei: mandatory identity mixed native targets and counterspell Cold", CardGame.Core.Tests.OlMoZhangFeiChecks.MandatoryIdentityMixedNativeTargetsAndCounterspellCold),
    ("OL Mo Zhang Fei: native three damage modification colors and issued green Cold", CardGame.Core.Tests.OlMoZhangFeiChecks.NativeThreeDamageModificationColorsAndIssuedGreenCold),
    ("OL Mo Lu Bu: Wushuang opening before either normal first seat", CardGame.Core.Tests.OlMoLuBuChecks.WushuangOpeningBeforeEitherNormalFirstSeat),
    ("Mou Han Dang Mobile: native color responses and range aid", CardGame.Core.Tests.MouHanDangMobileChecks.NativeColorResponsesAndRangeAid),
    ("Wu Huangfu Song: Chaozhen real minimum first turn Dying recovery cold", CardGame.Core.Tests.WuHuangFuSongChecks.ChaozhenRealMinimumFirstTurnDyingRecoveryAndCold),
    ("Wu Zhuge Liang: complete opening HP ordering Qingshi branches copied Use cold", CardGame.Core.Tests.WuZhugeLiangChecks.CompleteOpeningHpOrderingQingshiBranchesAndCopiedUseCold),
    ("Wu Zhuge Liang: copied pure Played native Duel return turn expiry cold", CardGame.Core.Tests.WuZhugeLiangChecks.CopiedPurePlayedNativeDuelReturnAndTurnExpiryCold),
    ("Mou Pang Tong Mobile: level one native HP targets phase use unlimited recast", CardGame.Core.Tests.MouPangTongMobileChecks.LevelOneNativeCostTargetsPhaseUseAndUnlimitedRecast),
    ("Mou Pang Tong Mobile: ordered native Niepan upgrade all target discard cold", CardGame.Core.Tests.MouPangTongMobileChecks.OrderedNativeNiepanUpgradeAndAllTargetDiscardCold),
    ("Mou Meng Huo Mobile: native attribution charge phase and paid recipient cold", CardGame.Core.Tests.MouMengHuoMobileChecks.NativeAttributionChargePhaseAndPaidRecipientCold),
    ("Mou Zhou Yu Mobile: frozen Yingzi and actual covered guess cold", CardGame.Core.Tests.MouZhouYuMobileChecks.FrozenYingziAndActualCoveredGuessCold),
    ("Mou Lu Zhi: private Ren and completed categories", CardGame.Core.Tests.MouLuZhiMobileChecks.PrivateRenVariableCostAndCompletedCategories),
    ("Mou Xiahou Dun: native retaliation and non-Use distribution", CardGame.Core.Tests.MouXiahouDunMobileChecks.NativeRetaliationAndNonUseDistribution),
    ("Mo Diao Chan: native cohorts and affected Use completion", CardGame.Core.Tests.OlMoDiaoChanChecks.NativeCohortAndAffectedUseCompletion),
    ("Mou Diao Chan: native ring and actual Ending draw", CardGame.Core.Tests.MouDiaoChanMobileChecks.NativeRingAndEndingDraw),
    ("Mou Fa Zheng: native marks random gain and Enyuan", CardGame.Core.Tests.MouFaZhengMobileChecks.NativeMarksRandomGainAndEnyuan),
    ("SP Sun Shangxiang OL: actual recovery help and Fanxiang", CardGame.Core.Tests.SpSunShangxiangOlChecks.ActualRecoveryHelpAndFanxiang),
    ("Mou Sun Jian OL: first HP and independent Hulie", CardGame.Core.Tests.MouSunJianOlChecks.FirstHpAndIndependentHulie),
    ("Mou Zhang Xiu OL: actual phases and previous true Use", CardGame.Core.Tests.MouZhangXiuOlChecks.ActualPhasesAndPreviousTrueUse),
    ("Mou Wen Chou OL: native counts and first all-Hand", CardGame.Core.Tests.MouWenChouOlChecks.NativeCountsAndFirstAllHand),
    ("Mou Deng Ai OL: native Gu exchange and Jiewan", CardGame.Core.Tests.MouDengAiOlChecks.NativeGuExchangeAndJiewan),
    ("OL Mo Cao Cao: native equipment and Bachao", CardGame.Core.Tests.OlMoCaoCaoChecks.NativeEquipmentAndBachao),
    ("Mou Zhang He Mobile: actual skipped phases and Liaoshi", CardGame.Core.Tests.MouZhangHeMobileChecks.NativeSkippedPhasesAndLiaoshi),
    ("Mou Zhu Ran Mobile: sealed payments and Heyuan", CardGame.Core.Tests.MouZhuRanMobileChecks.NativeSealedPaymentsAndHeyuan),
    ("Mou Gongsun Zan OL: equal range native Slash and next actual Play expiry Cold", CardGame.Core.Tests.MouGongsunZanOlChecks.EqualRangeNativeSlashAndNextActualPlayExpiryCold),
    ("Mou Jiang Wei Mobile: real requested Slash gift charge awakening and Beifa Cold", CardGame.Core.Tests.MouJiangWeiMobileChecks.RealRequestedSlashGiftChargeAwakeningAndBeifaCold),
    ("Mou Lu Zhi OL: real red pressure counter and round draw Cold", CardGame.Core.Tests.MouLuZhiOlChecks.RealRedPressureCounterAndRoundDrawCold),
    ("Mou Hua Xiong OL: native committed pair and persistent directional tickets Cold", CardGame.Core.Tests.MouHuaXiongOlChecks.NativeCommittedPairMutualUseAndPersistentDirectionalTicketsCold),
    ("Mou Dong Zhao OL: real damage gift and native suit-loss obligation Cold", CardGame.Core.Tests.MouDongZhaoOlChecks.RealDamageGiftAndNativeSuitLossObligationCold),
    ("Mou Gongsun Zan Mobile: real Hu original Played and Hand conversions Round charge Cold", CardGame.Core.Tests.MouGongsunZanMobileChecks.RealHuOriginalPlayedAndHandConversionsWithRoundChargeCold),
    ("Mou Xiao Qiao OL: real public Hand reveal returns and retained native uses Cold", CardGame.Core.Tests.MouXiaoQiaoOlChecks.RealMiluoRetainedUsesAndExternalRevealReturnCold),
    ("Mou Wang Ling: real Ending gifts five-name awakening and duplicate retention Cold", CardGame.Core.Tests.MouWangLingChecks.ActualEndingGiftsFiveNamesAwakenAndRetainDuplicatesCold),
    ("Mou Liu Xie: native all-Hand adjustments and ordered rewards Cold", CardGame.Core.Tests.MouLiuXieChecks.NativeAllHandAdjustmentsAndOrderedRewardsCold),
    ("Mou Xu Sheng: native color-run benefits and mutual opposing-Hand choices Cold", CardGame.Core.Tests.MouXuShengChecks.NativeColorRunBenefitsAndMutualOpposingHandChoicesCold),
    ("Mou Dian Wei: native three contests multi-target Slash and actual recovery debt Cold", CardGame.Core.Tests.MouDianWeiChecks.NativeThreeContestsMultiTargetSlashAndActualRecoveryDebtCold),
    ("Mou Zhuge Jin anniversary: private gift finite damage and derived true Use with Houde Cold", CardGame.Core.Tests.MouZhugeJinAnniversaryChecks.PrivateGiftFiniteDamageAndDerivedTrueUseWithHoudeCold),
    ("Mou Zhou Yu anniversary: additive fire targets and both native Yingmou branches Cold", CardGame.Core.Tests.MouZhouYuAnniversaryChecks.AdditiveFireTargetsAndBothNativeYingmouBranchesCold),
    ("Mou Jiang Ji anniversary: per-Play gifts and selected-target native effects Cold", CardGame.Core.Tests.MouJiangJiAnniversaryChecks.PerPlayGiftsAndSelectedTargetNativeEffectsCold),
    ("Mou Jia Xu anniversary: private Fumou native queue and recorded Rushi Cold", CardGame.Core.Tests.MouJiaXuAnniversaryChecks.PrivateFumouNativeQueueAndRecordedRushiCold),
    ("Mou Cheng Yu anniversary: actual health secret names native responses and Gaojian Cold", CardGame.Core.Tests.MouChengYuAnniversaryChecks.ActualHealthSecretNamesNativeResponsesAndGaojianCold),
    ("Mou Xun Yu anniversary: actual foreign actor adjustments same Use and Bizuo Cold", CardGame.Core.Tests.MouXunYuAnniversaryChecks.ActualForeignActorAdjustmentsSameUseAndBizuoCold),
    ("Mou Cao Hong anniversary: native range debt and bilateral distance Hand exchange Cold", CardGame.Core.Tests.MouCaoHongAnniversaryChecks.NativeRangeDebtAndBilateralDistanceHandExchangeCold),
    ("Mou Dong Zhuo OL: actual Xiongni Fengshang Wine Cold and repeated Preparing", CardGame.Core.Tests.MouDongZhuoOlChecks.ActualXiongniFengshangWineColdAndRepeatedPreparing),
    ("Numeric rules: marker-count directional modifiers lose actual source", CardGame.Core.Tests.MouDongZhuoOlChecks.MarkerCountDirectionalModifierSourceRemoval),
    ("Mou Guan Ping: native whole-color Slash after ordinary Slash repeated options and Cold", CardGame.Core.Tests.MouGuanPingChecks.NativeWholeColorAfterOrdinarySlashOptionsRepeatAndCold),
    ("Mou Jiang Wei: true Trick Yin empty HE causal Dying Fuzhan Cold and modified boundaries", CardGame.Core.Tests.MouJiangWeiAnniversaryChecks.TrueTrickYinEmptyHeCausalDyingFuzhanColdAndModifiedBoundaries),
    ("Mou Cao Shuang: actual Use items recast Cold deletion awakening and historical target", CardGame.Core.Tests.MouCaoShuangAnniversaryChecks.ActualUseItemsRecastColdDeletionAwakeningAndHistoricalTarget),
    ("Mou Hu Che Er: actual reveal Slash chain Cold phase-tail equipment and next turn", CardGame.Core.Tests.MouHuCheErChecks.ActualRevealSlashChainColdPhaseTailEquipmentAndNextTurn),
    ("Mou Lu Su Anniversary: actual Draw gift Yang damage equipment gain Yin Played and Cold", CardGame.Core.Tests.MouLuSuAnniversaryChecks.ActualDrawGiftYangDamageEquipmentGainYinPlayedAndCold),
    ("Mou Sima Shi: actual marks uses discard claim phase tail death shuffle and Cold", CardGame.Core.Tests.MouSimaShiChecks.ActualMarksUsesDiscardClaimPhaseTailDeathShuffleAndCold),
    ("Mou Zhang Xiu Anniversary: actual Fuxi branches Haoyi entities Lightning and Cold", CardGame.Core.Tests.MouZhangXiuAnniversaryChecks.ActualFuxiBranchesHaoyiEntitiesLightningAndCold),
    ("Cao Xian: actual wings completed Uses removal adjustment options and Cold", CardGame.Core.Tests.CaoXianChecks.ActualWingsCompletedUsesRemovalAdjustmentOptionsAndCold),
    ("Mou Hu Lie: actual self Yu relay Yitou round Slashes and Cold", CardGame.Core.Tests.MouHuLieChecks.ActualSelfYuRelayYitouRoundSlashesAndCold),
    ("Mou Chen Lin: actual participant gifts written Uses return top and Cold", CardGame.Core.Tests.MouChenLinChecks.ActualParticipantGiftsWrittenUsesReturnTopAndCold),
    ("Mou Sima Yi Anniversary: actual Give polarity private original Slash Played and Cold", CardGame.Core.Tests.MouSimaYiAnniversaryChecks.ActualGivePolarityPrivateOriginalSlashPlayedAndCold),
    ("Mou Sima Yi Anniversary: human Give distinct physical choices and Cold", CardGame.Core.Tests.MouSimaYiAnniversaryChecks.HumanGiveUsesDistinctPhysicalChoicesAndCold),
    ("Mou Cao Ang Anniversary: global equipment loss and original foreign damage Use source-less Cold", CardGame.Core.Tests.MouCaoAngAnniversaryChecks.GlobalEquipmentLossDiscardOriginalForeignUseSourceLessAndCold),
    ("Mou Ju Shou Anniversary: actual Draw choice Use-only entity expiry and paid Cold", CardGame.Core.Tests.MouJuShouAnniversaryChecks.ActualDrawChoiceUseOnlyEntityExpiryAndPaidCold),
    ("Mou Guo Jia Anniversary: private conversion current Yiji native Lightning Lunshi and Cold", CardGame.Core.Tests.MouGuoJiaAnniversaryChecks.PrivateConversionCurrentYijiNativeLightningLunshiAndCold),
    ("Mou Dong Cheng Anniversary: paid protection original claim virtual multi-target once and Cold", CardGame.Core.Tests.MouDongChengAnniversaryChecks.PaidProtectionOriginalClaimVirtualMultiTargetOnceAndCold),
    ("Mou Kong Rong OL: real turn-end zero Use sealed tied HP payments Cold", CardGame.Core.Tests.MouKongRongOlChecks.ActualTurnEndZeroUseAndSealedTiedHpPaymentsCold),
    ("Mou Sun Shangxiang Mobile: actual gift Zhuang repeated-holder failure and Wu Xiaoji Cold", CardGame.Core.Tests.MouSunShangxiangMobileChecks.ActualGiftZhuangRepeatedHolderFailureAndWuXiaojiCold),
    ("Mou Huang Gai Anniversary: completed Trick entity stacks actual top Slash and expiry Cold", CardGame.Core.Tests.MouHuangGaiAnniversaryChecks.CompletedTrickEntityStacksActualTopSlashAndExpiryCold),
    ("Mou Huang Quan Anniversary: native gifts equal reply original effect whole Use and Cold", CardGame.Core.Tests.MouHuangQuanAnniversaryChecks.NativeGiftsEqualReplyOriginalEffectWholeUseAndCold),
    ("Mou Lu Bu Mobile: actual double response multi-obtain and victim branches Cold", CardGame.Core.Tests.MouLuBuMobileChecks.ActualDoubleResponseMultiObtainAndVictimBranchesCold),
    ("Mou Gan Ning Mobile: real repeated guesses and independent Wei targets Cold", CardGame.Core.Tests.MouGanNingMobileChecks.RealRepeatedGuessesAndIndependentWeiTargetsCold),
    ("Mou Guo Huai Mobile: ordered real predictions and next actual start none Cold", CardGame.Core.Tests.MouGuoHuaiMobileChecks.OrderedRealPredictionsAndNextActualStartNoneCold),
    ("Mou Guo You Zhi Mobile: real private gift hand adjustment and exact discard reuse Cold", CardGame.Core.Tests.MouGuoYouZhiMobileChecks.RealPrivateGiftHandAdjustmentAndExactDiscardReuseCold),
    ("Wu Huangfu Song: Lianjie actual draw privileges Jiangxian damage end loss cold", CardGame.Core.Tests.WuHuangFuSongChecks.LianjieActualDrawPrivilegesJiangxianDamageAndEndLossCold),
    ("Le Cai Yong: initial Xian actual loss binary protection Armor expiry cold", CardGame.Core.Tests.LeCaiYongChecks.InitialXianActualLossBinaryProtectionArmorAndExpiryCold),
    ("Le Cai Yong: effective names previous Use private samples cold", CardGame.Core.Tests.LeCaiYongChecks.FeibaiEffectiveNamesPreviousUsePrivateSamplesAndCold),
    ("Le Cai Yong: true Dodge Null parents Played exclusion cold", CardGame.Core.Tests.LeCaiYongChecks.FeibaiTrueDodgeNullParentsAndPlayedExclusionCold),
    ("Le Cai Yong: actual fallback short Draw Xian disable source loss cold", CardGame.Core.Tests.LeCaiYongChecks.FeibaiActualFallbackShortDrawMarksDisableAndSourceLossCold),
    ("Le Cao Zhi: opening Fu entities mode aliases hand limit privacy cold", CardGame.Core.Tests.LeCaoZhiChecks.OpeningFuEntitiesModeAliasesHandLimitAndPrivacyCold),
    ("Le Cao Zhi: alternate native names real materials delayed responses cold", CardGame.Core.Tests.LeCaoZhiChecks.AlternateNativeNamesRealMaterialsDelayedAndResponsesCold),
    ("Le Cao Zhi: fixed accepted pairs nested Played cross turn source cold", CardGame.Core.Tests.LeCaoZhiChecks.FixedAcceptedPairsNestedPlayedCrossTurnAndSourceCold),
    ("Le Cao Zhi: full name union actual obtains shortage gain source cold", CardGame.Core.Tests.LeCaoZhiChecks.FullNameUnionActualObtainsShortageAndGainSourceCold),
    ("Le Cao Zhi: private Hand mark replacement zero selection return cold", CardGame.Core.Tests.LeCaoZhiChecks.PrivateHandMarkReplacementZeroSelectionAndReturnCold),
    ("Mou Hua Xiong Mobile: native Slash colors benefit before Armor HP cold", CardGame.Core.Tests.MouHuaXiongMobileChecks.YaowuNativeSlashColorsBeforeArmorAndHpCold),
    ("Mou Hua Xiong Mobile: paid benefit children source loss native return cold", CardGame.Core.Tests.MouHuaXiongMobileChecks.YaowuPaidBenefitChildrenSourceLossAndNativeReturnCold),
    ("Mou Hua Xiong Mobile: actual Draw paid source loss Slash privileges cold", CardGame.Core.Tests.MouHuaXiongMobileChecks.YangweiActualDrawPaidSourceLossSlashPrivilegesCold),
    ("Mou Hua Xiong Mobile: phase expiry next own Ending extra turn skip regrant cold", CardGame.Core.Tests.MouHuaXiongMobileChecks.YangweiPhaseExpiryNextOwnEndingExtraTurnSkipAndRegrantCold),
    ("Mou Lu Meng Mobile: Keji real costs independent branches Armor Hand limit rescue cold", CardGame.Core.Tests.MouLuMengMobileChecks.KejiRealCostsIndependentBranchesArmorHandLimitAndRescueCold),
    ("Mou Lu Meng Mobile: effective Peach restrictions conversion native Dying cold", CardGame.Core.Tests.MouLuMengMobileChecks.KejiEffectivePeachRestrictionsConversionAndNativeDyingCold),
    ("Mou Lu Meng Mobile: Dujiang real Preparation once shared Keji regrant cold", CardGame.Core.Tests.MouLuMengMobileChecks.DujiangRealPreparationOnceGrantSharedKejiQuotaAndRegrantCold),
    ("Mou Lu Meng Mobile: Duojing final targets Armor opaque gain exact phase children cold", CardGame.Core.Tests.MouLuMengMobileChecks.DuojingFinalTargetsArmorOpaqueGainExactPhaseAndNativeChildrenCold),
    ("Mou Yu Jin Mobile: exact damage invoice Armor only unbounded historical restore cold", CardGame.Core.Tests.MouYuJinMobileChecks.XiayuanExactDamageInvoiceArmorOnlyAndUnboundedHistoricalRestoreCold),
    ("Mou Yu Jin Mobile: friendly owners shared invoice Round extra turn regrant cold", CardGame.Core.Tests.MouYuJinMobileChecks.XiayuanFriendlyOwnersSharedInvoiceRoundExtraTurnAndRegrantCold),
    ("Mou Yu Jin Mobile: paid discard source loss self rescue death native return cold", CardGame.Core.Tests.MouYuJinMobileChecks.XiayuanPaidDiscardSourceLossSelfRescueDeathAndOriginalDamageReturnCold),
    ("Mou Yu Jin Mobile: Jieyue actual Ending Armor first recipient optional Hand Equipment gift cold", CardGame.Core.Tests.MouYuJinJieyueChecks.JieyueActualEndingArmorFirstRecipientOptionalHandEquipmentGiftCold),
    ("Mou Liu Cheng Mobile: mandatory completion debit draw shortfall true responses paid children cold", CardGame.Core.Tests.MouLiuChengCompletionChecks.CompletionMandatoryDebitDrawShortfallResponsesAndPaidChildrenCold),
    ("Mou Liu Cheng Mobile: native alternating Dismantlement Slash parents privacy legality cold", CardGame.Core.Tests.MouLiuChengCompletionChecks.CompletionNativeAlternatingDismantlementSlashParentsPrivacyAndLegalityCold),
    ("Mou Liu Cheng Mobile: actual target notifications independent skill quotas and card families cold", CardGame.Core.Tests.MouLiuChengAccrualChecks.ActualTargetNotificationsIndependentQuotasAndCardFamiliesCold),
    ("Mou Liu Cheng Mobile: exact Play phases source lifetime and permanent Chui cold", CardGame.Core.Tests.MouLiuChengAccrualChecks.ExactPlayPhasesSourceLifetimeAndPermanentChuiCold),
    ("Mou Xu Huang Mobile: sealed four cell strategy private actual Play quota cold", CardGame.Core.Tests.MouXuHuangDuanliangChecks.SealedFourCellStrategyPrivacyAndExactPlayQuotaCold),
    ("Mou Xu Huang Mobile: real top SupplyShortage obtain native zero Duel legality children cold", CardGame.Core.Tests.MouXuHuangDuanliangChecks.RealTopSupplyObtainAndNativeZeroDuelLegalityChildrenCold),
    ("Mou Xu Huang Mobile: actual Ending frozen full Shipo roster Hand or native damage cold", CardGame.Core.Tests.MouXuHuangShipoChecks.ActualEndingFrozenCompleteRosterAndRecipientHandOrDamageCold),
    ("Mou Xu Huang Mobile: exact obtained subset one recipient paid source loss death cold", CardGame.Core.Tests.MouXuHuangShipoChecks.ExactObtainedSubsetSingleRecipientAndAcceptedSourceLifetimeCold),
    ("Mou Chen Gong Mobile: real HE gift choices exact Play quota paid children cold", CardGame.Core.Tests.MouChenGongMingceChecks.RealHandEquipmentGiftChoicesExactPlayQuotaAndPaidChildrenCold),
    ("Mou Chen Gong Mobile: actual Play start Ce damage then clear source lifetime cold", CardGame.Core.Tests.MouChenGongMingceChecks.ActualPlayStartCeAccumulationFrozenDamageThenClearAndSourceLifetimeCold),
    ("Mou Chen Gong Mobile: Zhichi native damage natures chain Armor Loss boundaries cold", CardGame.Core.Tests.MouChenGongZhichiChecks.NativeDamageNaturesSourceLessChainsArmorAndHpLossBoundariesCold),
    ("Mou Chen Gong Mobile: Zhichi acquisition source lifetime actual turn expiry cold", CardGame.Core.Tests.MouChenGongZhichiChecks.AcquisitionIssuedProtectionSourceLossAndActualTurnExpiryCold),
    ("OL Mou Huang Yueying: first Basic true Use whole recast and actual turn cold", CardGame.Core.Tests.OlMouHuangYueyingChecks.BingcaiGlobalFirstBasicTrueUseAndRecastClassificationCold),
    ("OL Mou Huang Yueying: basic native effects independent additive rounds cold", CardGame.Core.Tests.OlMouHuangYueyingChecks.BingcaiBasicEffectsAndIndependentExtraRoundsCold),
    ("OL Mou Huang Yueying: Lixian original material names separate phase unlocks cold", CardGame.Core.Tests.OlMouHuangYueyingChecks.LixianOriginalMaterialNamesAndSeparatePhaseUnlocksCold),
    ("OL Mou Huang Yueying: foreign paid recast equipment source loss death cold", CardGame.Core.Tests.OlMouHuangYueyingChecks.BingcaiPaidForeignUseEquipmentLossAndDeathCold),
    ("OL Mou Zhou Yu: unique Hand suit private view fire and actual phase quota cold", CardGame.Core.Tests.OlMouZhouYuChecks.ZuifengUniqueHandSuitPrivateViewFireAndPhaseQuotaCold),
    ("OL Mou Zhou Yu: foreign Alcohol original material and paid lifetime cold", CardGame.Core.Tests.OlMouZhouYuChecks.ZuifengForeignAlcoholOriginalMaterialAndPaidLifetimeCold),
    ("OL Mou Zhou Yu: every public Hand reveal true Use whitelist and actual turn cold", CardGame.Core.Tests.OlMouZhouYuChecks.GuquEveryShownEntityTrueUseWhitelistAndActualTurnCold),
    ("OL Mou Zhou Yu: conversion declaration and native draw paid rights cold", CardGame.Core.Tests.OlMouZhouYuChecks.GuquConversionDeclarationAndNativeDrawPaidRightsCold),
    ("Wu Lu Xun: round random insertion original eights native children cold", CardGame.Core.Tests.WuLuXunChecks.XiongmuRoundRandomInsertionOriginalRankEightAndNativeChildrenCold),
    ("Wu Lu Xun: first actual turn damage history and threshold cold", CardGame.Core.Tests.WuLuXunChecks.XiongmuFirstActualTurnDamageHistoryAndThresholdCold),
    ("Wu Lu Xun: true Use Played effective ranks and paid draw cold", CardGame.Core.Tests.WuLuXunChecks.ZhangcaiTrueUsePlayedEffectiveRanksAndPaidDrawCold),
    ("Wu Lu Xun: limited expiry source rights and extra phase cold", CardGame.Core.Tests.WuLuXunChecks.RuxianLimitedExpirySourceRightsAndExtraPhaseCold),
    ("Le Zhou Yu: original opening entities and multiple owners cold", CardGame.Core.Tests.LeZhouYuChecks.GuYinOriginalOpeningEntitiesAndMultiOwnerInitializationCold),
    ("Le Zhou Yu: true Use discard Played and native draw children cold", CardGame.Core.Tests.LeZhouYuChecks.GuYinTrueUseDiscardPlayedAndNativeDrawChildrenCold),
    ("Le Zhou Yu: current range whole batch and same Play custody latch cold", CardGame.Core.Tests.LeZhouYuChecks.PingluRealRangeWholeBatchAndSamePlayResidencyLatchCold),
    ("Le Zhou Yu: departure reobtain new phase and paid source rights cold", CardGame.Core.Tests.LeZhouYuChecks.PingluDepartureReobtainNewPhaseAndPaidSourceRightsCold),
    ("OL Mo Sun Quan: simultaneous private round lifetime history and exhausted None cold", CardGame.Core.Tests.OlMoSunQuanChecks.QuanyuSimultaneousPrivateRoundLifetimeHistoryAndNoneCold),
    ("OL Mo Sun Quan: true Use Tianen branches native source rights cold", CardGame.Core.Tests.OlMoSunQuanChecks.TianenTrueUseTwoBranchesAndNativeSourceRightsCold),
    ("OL Mo Sun Quan: Qiangang permanent Rumo old round damage HP children cold", CardGame.Core.Tests.OlMoSunQuanChecks.QiangangPermanentRumoOldRoundDamageAndHpChildrenCold),
    ("OL Mo Sun Quan: native Slash six effects quota extra rounds cold", CardGame.Core.Tests.OlMoSunQuanChecks.NativeSlashSixEffectsQuotaAndExtraRoundsCold),
    ("OL Mo Sun Quan: Meteor prepaid declaration source rights and false admission cold", CardGame.Core.Tests.OlMoSunQuanDeclarationChecks.MeteorPrepaidDeclarationKeepsInitialSingletonQuotaAndFalseAdmissionCold),
    ("OL Mo Sun Quan: Meteor native Spear complete initial target quota cold", CardGame.Core.Tests.OlMoSunQuanMeteorChecks.MeteorZhangbaRequiresCompleteInitialSingletonAfterOrdinaryQuotaCold),
    ("Le Zhou Fei: original and transferred entities hand limit cold", CardGame.Core.Tests.LeZhouFeiChecks.LingkongOriginalAndTransferredEntitiesHandLimitCold),
    ("Le Zhou Fei: first whole gain outside Draw and actual turns cold", CardGame.Core.Tests.LeZhouFeiChecks.LingkongFirstWholeGainOutsideDrawPhaseActualTurnsCold),
    ("Le Zhou Fei: Xianshu colors fresh health draw bounds and neutral cold", CardGame.Core.Tests.LeZhouFeiChecks.XianshuColorsFreshHealthDrawBoundsAndNeutralEntityCold),
    ("Le Zhou Fei: Xianshu paid children source rights privacy and stale inputs cold", CardGame.Core.Tests.LeZhouFeiChecks.XianshuPaidDrawHealthChildrenSourceRightsAndPrivateInputsCold),
    ("Le Mi Heng: original physical marks equipment slots and hand limit cold", CardGame.Core.Tests.LeMiHengChecks.JiguOriginalEntitiesEquipmentSlotsAndHandLimitCold),
    ("Le Mi Heng: historical started seats round latch and extra turns cold", CardGame.Core.Tests.LeMiHengChecks.JiguHistoricalStartedSeatsRoundLatchAndExtraTurnsCold),
    ("Le Mi Heng: native Draw replacements paid rights and privacy cold", CardGame.Core.Tests.LeMiHengChecks.JiguNativeDrawReplacementPaidRightsAndPrivacyCold),
    ("Le Mi Heng: same name HE outputs distance and ordinary quotas cold", CardGame.Core.Tests.LeMiHengChecks.SiruiExactNamesHeOutputsDistanceAndOrdinaryQuotasCold),
    ("Le Mi Heng: true response request Use and Played exclusions cold", CardGame.Core.Tests.LeMiHengChecks.SiruiTrueUseResponseRequestAndPlayedExclusionsCold),
    ("Le Mi Heng: paid children regained phase quota and rejected sources cold", CardGame.Core.Tests.LeMiHengChecks.SiruiPaidChildrenRegainPhaseQuotaAndRejectedSourcesCold),
    ("Pang Ji: actual Draw discard category obtain and target lease cold", CardGame.Core.Tests.PangJiChecks.BiguoActualDrawDiscardCategoryObtainAndTargetLeaseCold),
    ("Pang Ji: native targeted Pindian nonwinner type bans and expiry cold", CardGame.Core.Tests.PangJiChecks.BiguoNativeTargetPindianNotWinTypeBanAndExpiryCold),
    ("Pang Ji: blind original entity swap rank claims and win draw cold", CardGame.Core.Tests.PangJiChecks.DouyuBlindOriginalEntitySwapRankClaimsAndWinDrawCold),
    ("Pang Ji: simultaneous Pindian paid children and source rights cold", CardGame.Core.Tests.PangJiChecks.PangJiSimultaneousPindianPaidChildrenAndSourceRightsCold),
    ("Ordinary Gu Yong: exact mixed Shenxing costs and native children cold", CardGame.Core.Tests.OrdinaryGuYongChecks.ShenxingExactMixedCostsAndNativeChildrenCold),
    ("Ordinary Gu Yong: actual phase quota foreign discard and restoration cold", CardGame.Core.Tests.OrdinaryGuYongChecks.BingyiActualPhaseQuotaForeignDiscardAndRestorationCold),
    ("Ordinary Gu Yong: frozen Hand colors self and distinct recipients cold", CardGame.Core.Tests.OrdinaryGuYongChecks.BingyiFrozenHandColorsSelfAndDistinctRecipientsCold),
    ("Ordinary Gu Yong: native origins negatives and paid loss death cold", CardGame.Core.Tests.OrdinaryGuYongChecks.BingyiNativeOriginNegativesAndPaidSourceLossDeathCold),
    ("Zhou Qun: repeated Hand and top pairs with native gain cold", ZhouQunChecks.ChenshuoCompleteRepeatedPairsAndNativeGainCold),
    ("Zhou Qun: private exchange and exact next invocation cold", ZhouQunChecks.TianhouPrivateExchangeSourceLossAndExactNextInvocationCold),
    ("Zhou Qun: extrema fire and thunder native children cold", ZhouQunChecks.WeatherExtremaFireAndThunderNativeChildrenCold),
    ("Zhou Qun: final actor judgment and whole use nullification cold", ZhouQunChecks.NingwuFinalActorNativeJudgmentAndWholeUseNullificationCold),
    ("Round ending: old round movement children and actual turn identity cold", RoundEndingChecks.OldRoundMovementChildrenAndColdKeepActualTurnIdentity),
    ("Round ending: pending closing extras skipped turns and population cold", RoundEndingChecks.PendingAndClosingExtraTurnsSkippedTurnsAndPopulationCold),
    ("Round ending: BeiMiHu prelude follows old round cold", RoundEndingChecks.BeiMiHuPreludeStartsOnlyAfterOldRoundEndingCold),
    ("Round ending: legacy round holds return before old round closes cold", RoundEndingChecks.LegacyRoundHoldsReturnBeforeOldRoundEndsCold),
    ("Yuan Ji: atomic hand pair and independent opposite ends self cold", YuanJiChecks.JieyanAtomicHandPairAndTwoEndsSelfCold),
    ("Yuan Ji: suit lock reshuffle and paid loss death cold", YuanJiChecks.JieyanSuitLockReshuffleAndPaidLossDeathCold),
    ("Yuan Ji: round marks actual turn bottom and residency cold", YuanJiChecks.JinghuaRoundMarksTurnBottomAndResidencyCold),
    ("Yuan Ji: actual turn draw old round top and children cold", YuanJiChecks.ShuiyueActualTurnDrawOldRoundTopAndChildrenCold),
    ("Tang Tang: private take gift and whole native children cold", TangTangChecks.ZexingPrivateTakeGiftAndWholeNativeChildrenCold),
    ("Tang Tang: hp loss repeated peers and actual play scopes cold", TangTangChecks.ZexingHpLossRepeatedPeersAndActualPlayScopesCold),
    ("Tang Tang: whole gain directions colors and native transfer cold", TangTangChecks.ZhiyiWholeGainDirectionsColorsAndNativeTransferCold),
    ("Tang Tang: paid source loss death and equipment removal cold", TangTangChecks.ZexingPaidSourceLossDeathAndEquipmentRemovalCold),
    ("Guo Tu Weijie: specialized foreign Qigong Heji and own turn Shanjia", GuoTuWeijieSpecializedChecks.SpecializedForeignQigongHejiAndOwnTurnShanjiaBoundaryCold),
    ("Guo Tu Weijie: six basic names native dying and failure cold", GuoTuWeijieChecks.WeijieSixBasicNamesNativeDyingAndFailureCold),
    ("Guo Tu Weijie: played Duel AOE true Dodge and supply cold", GuoTuWeijieChecks.WeijiePlayedDuelAoeAndTrueDodgeSupplyCold),
    ("Guo Tu Weijie: forced and program request families cold", GuoTuWeijieChecks.WeijieForcedAndProgramRequestFamiliesCold),
    ("Guo Tu Weijie: opaque cost source loss death and turn quota cold", GuoTuWeijieChecks.WeijieOpaqueCostSourceLossDeathAndTurnQuotaCold),
    ("Guo Tu Qushi: private seals native draw privacy once per Play and cold", GuoTuQushiChecks.PrivateSealsNativeDrawPrivacyOncePerPlayAndCold),
    ("Guo Tu Qushi: same type and all use maximum targets remain independent", GuoTuQushiChecks.SameTypeAndAllUseMaximumTargetsRemainIndependent),
    ("Guo Tu Qushi: true responses played and legacy uses exact turn history", GuoTuQushiChecks.TrueResponsesPlayedAndLegacyUsesKeepExactTurnHistory),
    ("Guo Tu Qushi: issued seal source loss and native death children", GuoTuQushiChecks.IssuedSealSurvivesSourceLossAndNativeDeathChildren),
    ("Ruan Yu: private full bottom order zero and short deck", RuanYuChecks.XingzuoPrivateFullBottomOrderZeroAndShortDeckCold),
    ("Ruan Yu: ending all Hand random bottom debt loss and death", RuanYuChecks.XingzuoEndingAllHandRandomBottomDebtLossAndDeathCold),
    ("Ruan Yu: black all ordinary native use and foreign Nullification", RuanYuChecks.MiaoxianBlackAllOrdinaryNativeUseAndForeignNullificationCold),
    ("Ruan Yu: red true use responses Wooden Ox and paid children", RuanYuChecks.MiaoxianRedUseResponsesWoodenOxAndPaidChildrenCold),
    ("Cao Yu: true round opaque choices and suit draw", CaoYuChecks.GongjieTrueRoundOpaqueChoicesAndSuitDrawCold),
    ("Cao Yu: causal minimum edges and issued actual end", CaoYuChecks.XiangxuCausalMinimumEdgesAndIssuedActualEndCold),
    ("Cao Yu: true dying history and permanent limited gift", CaoYuChecks.XiangzuTrueDyingHistoryAndPermanentLimitedCold),
    ("Cao Yu: native Lion Ox paid loss death and privacy", CaoYuChecks.CaoYuNativeLionOxPaidLossDeathAndPrivacyCold),
    ("Qu Huang: true last Hand loss self foreign round and refill", QuHuangChecks.QiejianActualLastHandSelfForeignRoundAndRefillCold),
    ("Qu Huang: original Lightning equipment order and legality", QuHuangChecks.NishouOriginalLightningEquipmentOrderAndLegalityCold),
    ("Qu Huang: genuine phase debt source loss and timing", QuHuangChecks.NishouGenuinePhaseDebtSourceLossAndTimingCold),
    ("Qu Huang: staged original hands privacy and paid children", QuHuangChecks.NishouStagedOriginalHandsPrivacyAndPaidChildrenCold),
    ("Liu Pan: native add remove zero and random gifts", LiuPanChecks.PijingNativeAddRemoveZeroAndRandomGiftCold),
    ("Liu Pan: BorrowedSword pairs and reciprocal supportive targets", LiuPanChecks.PijingBorrowedPairsAndReciprocalSupportTargetsCold),
    ("Liu Pan: long leases source loss and true designation", LiuPanChecks.PijingLongLeaseSourceLossAndTrueDesignationCold),
    ("Liu Pan: equipment gift children privacy and paid death", LiuPanChecks.PijingEquipmentGiftsChildrenPrivacyAndPaidDeathCold),
    ("Hu Ban: whole FireAttack round options and repeated effects", HuBanChecks.HuiyunWholeFireAttackRoundOptionsAndRepeatedEffectsCold),
    ("Hu Ban: natural Hand use quotas and native kinds", HuBanChecks.HuiyunNaturalHandUseQuotaAndNativeKindsCold),
    ("Hu Ban: original shown entity consent and privacy", HuBanChecks.HuiyunOriginalShownEntityConsentAndPrivacyCold),
    ("Hu Ban: equipment cost recast children and paid loss death", HuBanChecks.HuiyunEquipmentCostRecastChildrenAndPaidLossDeathCold),
    ("Wen Yang: actual damage numeric choices and turn budgets replay", CardGame.Core.Tests.WenYangChecks.LvliActualDamageNumericChoicesAndTurnBudgetsCold),
    ("Wen Yang: both awakenings actual boundaries and shared quota replay", CardGame.Core.Tests.WenYangChecks.BothAwakeningsActualBoundariesAndSharedDamageQuotaCold),
    ("Wen Yang: physical inventory subtype scarcity and issued ending replay", CardGame.Core.Tests.WenYangChecks.QingjiaoPhysicalInventorySubtypeScarcityAndIssuedEndingCold),
    ("Wen Yang: paid children source loss death and native Lion Ox replay", CardGame.Core.Tests.WenYangChecks.PaidChildrenSourceLossDeathAndNativeLionOxEndingCold),
    ("Da Qiao Xiao Qiao: both costs native turnover gender damage and replay", DaQiaoXiaoQiaoChecks.BothCostsNativeTurnoverGenderDamageAndCold),
    ("Da Qiao Xiao Qiao: equipment strip Lion Ox and paid children replay", DaQiaoXiaoQiaoChecks.EquipmentStripLionOxAndPaidChildrenCold),
    ("Da Qiao Xiao Qiao: linked Tianxiang Liuli and paid source loss death replay", DaQiaoXiaoQiaoChecks.LinkedTianxiangLiuliAndPaidSourceLossDeathCold),
    ("Fan Jiang Zhang Da: effective black native armor and privacy replay", FanJiangZhangDaChecks.YuanchouEffectiveBlackNativeArmorAndPrivacyCold),
    ("Fan Jiang Zhang Da: lifetime Slash and both Duel directions replay", FanJiangZhangDaChecks.JueshengLifetimeSlashAndBothDuelDirectionsCold),
    ("Fan Jiang Zhang Da: whole multi target extra effects and temporary expiry replay", FanJiangZhangDaChecks.JueshengWholeMultiTargetExtraEffectsAndTemporaryExpiryCold),
    ("Fan Jiang Zhang Da: paid armor children source loss death and permanent spend replay", FanJiangZhangDaChecks.JueshengPaidArmorChildrenSourceLossDeathAndPermanentSpendCold),
    ("Wu Tu Gu: actual fire points prevention chain and persistent marks replay", WuTuGuChecks.ActualFirePointsPreventionChainAndPersistentMarksCold),
    ("Wu Tu Gu: actual round and declaration freeze replay", WuTuGuChecks.HanyongActualRoundAndDeclarationFreezeCold),
    ("Wu Tu Gu: native groups Huoshou zero use and paid source loss replay", WuTuGuChecks.NativeGroupHuoshouZeroUseAndPaidSourceLossCold),
    ("Wu Tu Gu: ending single unclamped loss dying children and source loss replay", WuTuGuChecks.EndingSingleUnclampedLossDyingChildrenAndSourceLossCold),
    ("Sun Hao: normal draw additive independent grants and actual uses replay", SunHaoChecks.CanshiNormalDrawAdditiveIndependentGrantsAndActualUsesCold),
    ("Sun Hao: native equipment children distance and paid loss death replay", SunHaoChecks.CanshiNativeEquipmentChildrenDistanceAndPaidLossDeathCold),
    ("Sun Hao: recipient damage finalization chain lightning and armor replay", SunHaoChecks.ChouhaiRecipientFinalizationChainLightningAndArmorCold),
    ("Sun Hao: rule wounded queries roles faction and actual turn replay", SunHaoChecks.GuimingRuleWoundedQueriesRolesFactionAndActualTurnCold),
    ("Pei Yuan Shao: FulueSingleFinalTargetOptionsWholeDamageAndCold", PeiYuanShaoChecks.FulueSingleFinalTargetOptionsWholeDamageAndCold),
    ("Wu Jing: HejiSelfForeignRecursivePhysicalConversionAndCold", WuJingChecks.HejiSelfForeignRecursivePhysicalConversionAndCold),
    ("Xiang Chong: GuyingAtomicLossChoiceDebtAndCold", XiangChongChecks.GuyingAtomicLossChoiceDebtAndCold),
    ("Yang Feng: JiaweiCompletedTurnQuotaPartitionAndDistanceCold", YangFengChecks.JiaweiCompletedTurnQuotaPartitionAndDistanceCold),
    ("Yang Yi: Juanxia virtual Duel damage both directions and cold return", YangYiChecks.VirtualDuelDamageBothDirectionsAndColdReturn),
    ("A Hui Nan: FirstTwoUsersDrawAndActualTurnLedgerCold", AHuiNanChecks.FirstTwoUsersDrawAndActualTurnLedgerCold),
    ("Wang Guan: MiuyanNativeBlackMaterialsPolarityAndWholeDamageCold", WangGuanChecks.MiuyanNativeBlackMaterialsPolarityAndWholeDamageCold),
    ("Qin Lang: DrawDiscardCountsRoundResetAndPreparationCold", QinLangChecks.DrawDiscardCountsRoundResetAndPreparationCold),
    ("Ma Xiu Ma Tie: KenshangWholeTargetReplacementActualDamageAndCold", MaXiuMaTieChecks.KenshangWholeTargetReplacementActualDamageAndCold),
    ("Cheng Gong Ying: PreparationAdjustmentsPrivateDiscardExactGainAndCold", ChengGongYingChecks.PreparationAdjustmentsPrivateDiscardExactGainAndCold),
    ("Tian Chou: SequentialTopFiveGrainsComplementAndExhaustionCold", TianChouChecks.SequentialTopFiveGrainsComplementAndExhaustionCold),
    ("Ruan Hui: MingchaPublicRevealNormalReplacementZeroAndReshuffleCold", RuanHuiChecks.MingchaPublicRevealNormalReplacementZeroAndReshuffleCold),
    ("Ma Mi Di: BingjieNativeTargetsPrivatePaymentsBorrowedSwordAndRepeatCold", MaMiDiChecks.BingjieNativeTargetsPrivatePaymentsBorrowedSwordAndRepeatCold),
    ("Duan Jiong: YangYinOriginalSlashesForcedQuotaAndCold", DuanJiongChecks.YangYinOriginalSlashesForcedQuotaAndCold),
    ("Ke Bi Neng: OrderedItemsAndPerItemDrawsRecastEquipmentCold", KeBiNengChecks.OrderedItemsAndPerItemDrawsRecastEquipmentCold),
    ("Niu Fu: ShisuanSourceBranchesNullDeadAndEmptyCold", NiuFuChecks.ShisuanSourceBranchesNullDeadAndEmptyCold),
    ("Zhu Jun: CuipoNativeSlashEffectiveNamesDuelAndChainCold", ZhuJunChecks.CuipoNativeSlashEffectiveNamesDuelAndChainCold),
    ("Zhang Yan: SujiAnyTargetOwnForeignNormalRangeAndDamageOwnerCold", ZhangYanChecks.SujiAnyTargetOwnForeignNormalRangeAndDamageOwnerCold),
    ("Han Fu: ShuziHandExchangePrivateChooserAndNamesCold", HanFuChecks.ShuziHandExchangePrivateChooserAndNamesCold),
    ("Wu An Guo: FourSuitsRealYangAndYinNativeResponsesCold", WuAnGuoChecks.FourSuitsRealYangAndYinNativeResponsesCold),
    ("Yuan Huan: DeruPresenceSetsAllNoneAndOpaqueRandomPrivacyCold", YuanHuanChecks.DeruPresenceSetsAllNoneAndOpaqueRandomPrivacyCold),
    ("Lu Yu Sheng: BottomRevealNativeDrawAndZeroHeartsCold", LuYuShengChecks.BottomRevealNativeDrawAndZeroHeartsCold),
    ("Wang Kuang: BothDoWhileOrdersPrivateDiscardAndNativeExhaustionCold", WangKuangChecks.BothDoWhileOrdersPrivateDiscardAndNativeExhaustionCold),
    ("Cao Xi: NonbasicValuesCapWeaponsAndNativeSlashQuotaCold", CaoXiChecks.NonbasicValuesCapWeaponsAndNativeSlashQuotaCold),
    ("Hu Jin Ding: QingyuanOpeningFirstDamageRandomMarkedHandsAndQuotaCold", HuJinDingChecks.QingyuanOpeningFirstDamageRandomMarkedHandsAndQuotaCold),
    ("Yan Rou: PaidOwnSlashRefundAndUnpaidQuotaRemainExactCold", YanRouChecks.PaidOwnSlashRefundAndUnpaidQuotaRemainExactCold),
    ("Li Wan: LianjuActualUsesSameColorGiftAndZeroReturnCold", LiWanChecks.LianjuActualUsesSameColorGiftAndZeroReturnCold),
    ("Ding Shang Wan: OpeningZeroGiftArbitraryDiscardFixedRecordActualEndsAndOverlapCold", DingShangWanChecks.OpeningZeroGiftArbitraryDiscardFixedRecordActualEndsAndOverlapCold),
    ("He Jin: DonorPrivateGiftActualComparisonAndPhaseQuotaCold", HeJinChecks.DonorPrivateGiftActualComparisonAndPhaseQuotaCold),
    ("Qian Zhao: WeifuNativeJudgmentCategoryStacksAndExpiryCold", QianZhaoChecks.WeifuNativeJudgmentCategoryStacksAndExpiryCold),
    ("Fei Yi: YanruIndependentParityMinimumAndZeroHandCold", FeiYiChecks.YanruIndependentParityMinimumAndZeroHandCold),
    ("Zhang Shi Ping: TiedSelfDrawForeignPlayTwoRoundBranchesActualPhasesAndCold", ZhangShiPingChecks.TiedSelfDrawForeignPlayTwoRoundBranchesActualPhasesAndCold),
    ("Du Fu Ren: MixedOwnedGainsStackBlackActualSlashSourceLossDodgePreventionAndSnatchCold", DuFuRenChecks.MixedOwnedGainsStackBlackActualSlashSourceLossDodgePreventionAndSnatchCold),
    ("Hao Pu: SealedFixedAnswerPrivacyAdjustmentAndPhaseQuotaCold", HaoPuChecks.SealedFixedAnswerPrivacyAdjustmentAndPhaseQuotaCold),
    ("Ma Wan: SimultaneousPrivateCommitmentsUnanimousSupplementActualDrawAndCold", MaWanChecks.SimultaneousPrivateCommitmentsUnanimousSupplementActualDrawAndCold),
    ("Zhang Yi: ActualEndDamageDrawRecoveryExtraPlayQuotasAndPaidSourceLossCold", ZhangYiChecks.ActualEndDamageDrawRecoveryExtraPlayQuotasAndPaidSourceLossCold),
    ("Luo Xian: ZeroEvenOwnOtherEndsAndSkippedFaceDownTurnCold", LuoXianChecks.ZeroEvenOwnOtherEndsAndSkippedFaceDownTurnCold),
    ("Hand reveal privacy: FireAttack and Gongxin close before opaque Dismantlement cold", LuoXianChecks.TemporaryRevealsReturnToOpaqueTargetSlotsCold),
    ("Yan Pu: ActualForeignDrawGiftEndingBranchesRoundQuotaAndSourceLossCold", YanPuChecks.ActualForeignDrawGiftEndingBranchesRoundQuotaAndSourceLossCold),
    ("Du Xi: ActualUnequalPairDrawModifiersRoundTransferAndSameHolderCold", DuXiChecks.ActualUnequalPairDrawModifiersRoundTransferAndSameHolderCold),
    ("Fu Rong: RealDiscardOrderNativeQuotaAndColdReturns", FuRongChecks.RealDiscardOrderNativeQuotaAndColdReturns),
    ("Wang Fu Zhao Lei: ActualDamagePointsSourceLessSelfAndMutualExclusionCold", WangFuZhaoLeiChecks.ActualDamagePointsSourceLessSelfAndMutualExclusionCold),
    ("Qiao Zhou: ZhimingActualPreparationDiscardEndAndPublicForecastCold", QiaoZhouChecks.ZhimingActualPreparationDiscardEndAndPublicForecastCold),
    ("Gao Gan: NativeUseVictimsAndSourceLossRetainOneDeferredDraw", GaoGanChecks.NativeUseVictimsAndSourceLossRetainOneDeferredDraw),
    ("Huo Jun: ActualInitialSlotsHandLimitPaidSourceLossAndNativeEquipmentCold", HuoJunChecks.ActualInitialSlotsHandLimitPaidSourceLossAndNativeEquipmentCold),
    ("committed use contracts preserve immutable registration and response-use validation", ProgramCommittedUseContractChecks.RegistrationRejectsAmbiguousAndMutableContracts),
    ("Zhang Ling: ShoufuActualHandPlacementAndIssuerLossKeepPublicTalismanUntilDamage", ZhangLingChecks.ShoufuActualHandPlacementAndIssuerLossKeepPublicTalismanUntilDamage),
    ("Sun Ru OL: ActualCurrentActorLastZoneLossOncePerTurnAndColdReturn", SunRuOlChecks.ActualCurrentActorLastZoneLossOncePerTurnAndColdReturn),
    ("Liu Pi: CancelledEqualAndLowerExchangesPreserveDeckSlotsAndCold", LiuPiChecks.CancelledEqualAndLowerExchangesPreserveDeckSlotsAndCold),
    ("Wo Long Feng Chu: YoulongPaidEquipmentMovementSurvivesColdSourceLossAndRoundFaces", WoLongFengChuChecks.YoulongPaidEquipmentMovementSurvivesColdSourceLossAndRoundFaces),
    ("Wo Long Feng Chu: zero counterspell retains frozen response admission cold", WoLongFengChuChecks.YoulongCounterspellPreservesFrozenResponseAdmissionCold),
    ("Cao Shuang: ActualDeceasedChoiceReplacesOnlyInheritedGrantAndCleansOldPileCold", CaoShuangChecks.ActualDeceasedChoiceReplacesOnlyInheritedGrantAndCleansOldPileCold),
    ("SP Zhang Liao: NativeEqualAndZeroRankExchangesKeepOnePhysicalPayment", SpZhangLiaoChecks.NativeEqualAndZeroRankExchangesKeepOnePhysicalPayment),
    ("Bian Fu Ren: NativeForeignLossFreezesActorAndUsesOneActualTurnQuota", BianFuRenChecks.NativeForeignLossFreezesActorAndUsesOneActualTurnQuota),
    ("Cao Song: ActualGiftRemembersEveryPreviousCountAndReturnsCold", CaoSongChecks.ActualGiftRemembersEveryPreviousCountAndReturnsCold),
    ("Ding Yuan: RelationshipTransferPaysOnceAndMarkerGrantSurvivesCixiaoLoss", DingYuanChecks.RelationshipTransferPaysOnceAndMarkerGrantSurvivesCixiaoLoss),
    ("Niu Jin: OpeningDrawsToEightWithoutDrawingOrReducingAtFour", NiuJinChecks.OpeningDrawsToEightWithoutDrawingOrReducingAtFour),
    ("Duan Wei: ActualHandExtremaAlternateAtPaidUseAndColdReplay", DuanWeiChecks.ActualHandExtremaAlternateAtPaidUseAndColdReplay),
    ("Mobile Liu Zan: ActualColorPairsDrawOnceThroughGainChildAndResetOnExtraTurn", MobileLiuZanChecks.ActualColorPairsDrawOnceThroughGainChildAndResetOnExtraTurn),
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
    ("program runtime modules share match instances and reject engine dependencies", ProgramRuntimeModuleChecks.IndependentFactoriesShareMatchInstancesAndRejectEngineDependencies),
    ("program rule observer registration preserves ordered recursive boundaries", ProgramRuleObserverChecks.RegistrationPreservesOrderReentrancyAndIsolation),
    ("program rule observer registration rejects invalid metadata", ProgramRuleObserverChecks.InvalidRegistrationsFailBeforeDispatch),
    ("program legality registration preserves ordered capability gates", ProgramLegalityChecks.RegistrationAndDispatchPreserveBoundaries),
    ("shen sun ce native yingba fuhai death growth", ShenSunCeChecks.NativeYingbaFuhaiAndDeathGrowth),
    ("Table order: moved participant ring preserves damage and judgment trigger priorities", TableOrderChecks.TriggerPrioritiesUseMovedParticipantOrder),
    ("Table order: native move changes distance cold replay and next normal turn", TableOrderChecks.NativeMoveChangesDistanceAndNextNormalTurn),
    ("shen jiang wei native tianren discards and maximum threshold", ShenJiangWeiChecks.NativeTianrenDiscardsAndMaximumThreshold),
    ("shen jiang wei native jiufa ranks and pingxiang fire slashes", ShenJiangWeiChecks.NativeJiufaRanksAndPingxiangFireSlashes),
    ("Shen Xun Yu Tianzuo eight entities Dinghan and Lingce cold", ShenXunYuChecks.TianzuoEightEntitiesDinghanAndLingceCold),
    ("Qizheng wrong response pays then obtains cold", ShenXunYuChecks.QizhengWrongResponsePaysThenObtainsCold),
    ("Qizheng ambush native damage and replay", ShenXunYuChecks.QizhengAmbushUsesRealDamageAndReplay),
    ("shen deng ai develop allocate awaken and use issued domains", CardGame.Core.Tests.ShenDengAiChecks.DevelopAllocateAwakenAndUseIssuedDomains),
    ("shen deng ai cuixin completed use native child and delayed carrier", CardGame.Core.Tests.ShenDengAiChecks.CuixinRestoresCompletedUseAfterNativeChildAndDelayedCarrier),
    ("Happy Shen Deng Ai: optional reallocation developed draw and nonhand Cuixin cold", CardGame.Core.Tests.HappyShenDengAiChecks.OptionalReallocationDevelopedDrawAndNonHandCuixinResumeCold),
    ("Mobile Shen Hua Tuo: Wuling ordered entries equipment and native draw cold", CardGame.Core.Tests.MobileShenHuaTuoChecks.WulingOrdersEntriesEquipmentAndNativeDrawCold),
    ("Mobile Shen Hua Tuo: Youyi shared Ren payment recovery cold", CardGame.Core.Tests.MobileShenHuaTuoChecks.YouyiSharedRenPaysWholeAreaBeforeRecoveryCold),
    ("Shen Ma Chao starting mounts foreign Slash debt and Hengwu cold", ShenMaChaoChecks.StartingMountsForeignSlashDebtAndHengwuCold),
    ("Shen Ma Chao own mount Dodge true response and zero Hengwu cold", ShenMaChaoChecks.OwnMountDodgeTrueResponseAndZeroHengwuCold),
    ("Shen Pang Tong: native Luansuo and Fengliao", ShenPangTongChecks.NativeLuansuoAndFengliao),
    ("Shen Pang Tong: native Kunyu death payment cold", ShenPangTongChecks.NativeKunyuDeathPaymentCold),
    ("Happy Shen Pang Tong: independent plans native Slash theft lower rewards and failure growth cold", CardGame.Core.Tests.HappyShenPangTongChecks.IndependentPlansNativeSlashTheftLowerActualTurnRewardsAndFailureGrowthCold),
    ("Happy Shen Diao Chan: native tie recipient suit expiry and private whole-suit payments", CardGame.Core.Tests.HappyShenDiaoChanChecks.NativeTiedContestSuitBanOwnEndingAndWholeSuitPrivateTakeCold),
    ("Shen Zhong Hui: Linjie Duzhang awakening cold", ShenZhongHuiChecks.LinjieDuzhangAndAwakeningCold),
    ("Shen Zhong Hui: Lishi future phases judgment and discard cold", ShenZhongHuiChecks.LishiFuturePhasesJudgmentAndDiscardCold),
    ("Happy Shen Zhong Hui: twice actual turn paid draw cold and all-card Lishi native use", HappyShenZhongHuiChecks.TwiceActualTurnDrawColdAndAllCardLishiNativeUse),
    ("Anniversary Shen Zhang Jiao: owned discards random thirty-six shuffle thunder cold", CardGame.Core.Tests.AnniversaryShenZhangJiaoChecks.OwnedDiscardRandomThirtySixShuffleAndThunderCold),
    ("Anniversary Shen Zhang Jiao: Sanshou whole-turn actual types and cleanup", CardGame.Core.Tests.AnniversaryShenZhangJiaoChecks.SanshouTracksWholeTurnRealUseTypesAndCleansReveals),
    ("Anniversary Shen Huang Zhong: Lieqiong discard amplification Tianchong cold", CardGame.Core.Tests.AnniversaryShenHuangZhongChecks.LieqiongPhysicalDiscardAmplificationAndTianchongCold),
    ("Anniversary Shen Huang Zhong: Zhanjue native draw phase Slash recovery controls", CardGame.Core.Tests.AnniversaryShenHuangZhongChecks.ZhanjueNativeDrawPhaseSlashRecoveryAndWoundControls),
    ("Shen Cao Pi: Yinzhi native hand response and actual-turn restriction", CardGame.Core.Tests.ShenCaoPiChecks.YinzhiNativeHandResponseAndActualTurnRestriction),
    ("Shen Cao Pi: Keming native damage borrow and same-name echo cold", CardGame.Core.Tests.ShenCaoPiChecks.KemingBorrowsNativeDamageAndEchoesSameNamesCold),
    ("Mobile Gan Ning: ended Camp removal precedes gain and resumes cold", CardGame.Core.Tests.MobileShenGanNingChecks.EndedCampRemovalPrecedesGainAndResumesCold),
    ("Mobile Shen Lu Xun: Cuike chains before discard and cold returns once", MobileShenLuXunChecks.CuikeChainsBeforeDiscardAndColdReturnsOnce),
    ("Anniversary Shen Guan Yu: respondable heart Slashes bidirectional Nightmare and native draw death cold", CardGame.Core.Tests.AnniversaryShenGuanYuChecks.RespondableHeartSlashesDrawOnceBidirectionalNightmareAndDeathCold),
    ("Anniversary Shen Liu Bei: live HP formulas and chained recipient subset cold", CardGame.Core.Tests.AnniversaryShenLiuBeiChecks.LiveHpDrawAndOptionalChainedRecipientsCold),
    ("Happy Shen Zhang Liao: viewed color, abolished thresholds and sourced restoration cold", CardGame.Core.Tests.HappyShenZhangLiaoChecks.ViewedColorAbolitionThresholdsAndSourcedRestorationResumeCold),
    ("Happy Shen Guo Jia: five-suit reveal strict gift and turn-named native uses cold", CardGame.Core.Tests.HappyShenGuoJiaChecks.FiveSuitRevealStrictGiftAndTurnNamedNativeUsesCold),
    ("Happy Sun Quan Shehu paid cold and native learning Dingli", HappyShenSunQuanChecks.ShehuPaidColdAndLearningDingliUseNativeReturns),
    ("Happy Sun Quan source death and exact Play restriction", HappyShenSunQuanChecks.SourceDeathKeepsHolderDutyAndOnlyPlayRestrictsForeignUses),
    ("Happy Shen Lu Bu: actual-start first Slash Duel native targets cold", HappyShenLuBuChecks.WuqianActualStartAndFirstSlashDuelUseNativeTargetsCold),
    ("Happy Shen Lu Bu: paid HP groups foreign-turn full-HP reset cold", HappyShenLuBuChecks.ShenfenPaidHpGroupsAndForeignTurnFullHpResetCold),
    ("Happy Shen Zhen Ji: three branches actual-turn targets refunded reward cold", CardGame.Core.Tests.HappyShenZhenJiChecks.ThreeBranchesActualTurnTargetsAndRefundedRewardCold),
    ("Happy Shen Zhen Ji: black equipment response seventh card and native return cold", CardGame.Core.Tests.HappyShenZhenJiChecks.BlackEquipmentResponseStoresSeventhAndTurnEndReturnsAllCold),
    ("Happy Shen Cao Cao: random actual gains conditional turnover cold", HappyShenCaoCaoChecks.RandomOnePerCharacterActualThresholdAndFaceDownCold),
    ("Happy Shen Zhuge Liang: real stars wind and self mist cold", CardGame.Core.Tests.HappyShenZhugeLiangChecks.SevenRealHandStarsWindDamageAndSelfMistActualExpiryCold),
    ("Happy Shen Zhou Yu: whole-turn discard three melodies recurring fire cold", CardGame.Core.Tests.HappyShenZhouYuChecks.WholeTurnDiscardThreeMelodiesAndRecurringStartFireResumeCold),
    ("Happy Shen Lu Meng: forced Shelie sole red hand shared actual turn cold", HappyShenLuMengChecks.ForcedShelieSoleRedHandAndSharedActualTurnCold),
    ("Happy Shen Liu Bei: free choices extra Play colors and actual damage draw cold", HappyShenLiuBeiChecks.FreeChoiceExtraPlayColorsAndActualDamageDrawCold),
    ("Happy Shen Lu Xun: population threshold chosen subset native damage cold", HappyShenLuXunChecks.PopulationThresholdChosenSubsetAndNativeDamageCold),
    ("Happy Shen Xu Chu: record draws marked Duel and equal record cold", CardGame.Core.Tests.HappyShenXuChuChecks.RecordDrawsMarkedDuelAndEqualRecordResumeCold),
    ("Happy Shen Dian Wei: protected chooser paid Elai native damage and death queue cold", CardGame.Core.Tests.HappyShenDianWeiChecks.ProtectedChooserPaidElaiNativeDamageAndDeathQueueCold),
    ("Happy Shen Sima Yi: four Jilue paths strict Peach and first actual-turn draw cold", CardGame.Core.Tests.HappyShenSimaYiChecks.FourJiluePathsStrictPeachAndFirstActualTurnDrawResumeCold),
    ("Happy Shen Guan Yu: voluntary heart Slash survived and death five HP judgments cold", CardGame.Core.Tests.HappyShenGuanYuChecks.VoluntaryHeartSlashAndSurvivedDeathFiveHpJudgmentsCold),
    ("Happy Shen Zhang Jiao: use-only Huang deck-only thirty-six and four skills cold", HappyShenZhangJiaoChecks.UseOnlyHuangDeckOnlyThirtySixAndFourSkillCold),
    ("Happy Shen Taishi Ci: first dying equal HP and foreign ending round awakening cold", CardGame.Core.Tests.HappyShenTaishiCiChecks.FirstDyingEqualHpJudgmentAndForeignEndingRoundAwakeningResumeCold),
    ("Happy Shen Hua Tuo: medicine dying heart equipment and paid needle targets cold", CardGame.Core.Tests.HappyShenHuaTuoChecks.MedicineDyingHeartEquipmentAndPaidNeedleTargetsCold),
    ("Happy Shen Gan Ning: three-suit shares and native recovery draw cold", CardGame.Core.Tests.HappyShenGanNingChecks.ThreeSuitSharesAndRecoveryBeforeNativeDrawCold),
    ("Happy Shen Zhao Yun: two-red single use black current-turn obtains cold", CardGame.Core.Tests.HappyShenZhaoYunChecks.TwoRedSingleUseAndBlackCurrentTurnObtainsResumeCold),
    ("Mobile Shen Zhang Liao: Duorui paid slot native skill loan expiry cold", CardGame.Core.Tests.MobileShenZhangLiaoChecks.DuoruiPaysRealSlotAndBorrowsUntilTargetTurnEndsCold),
    ("Mobile Shen Zhang Liao: Zhiti native Pindian Duel and damage restore cold", CardGame.Core.Tests.MobileShenZhangLiaoChecks.ZhitiRestoresAtTruePindianDuelAndDamageBoundaries),
    ("Shen Lu Su native table order Dingzhou paid cold", ShenLuSuChecks.NativeTableOrderAndDingzhouPaidCold),
    ("Happy Shen Lu Su: both Dingzhou branches and unrestricted Team Zhimeng cold", CardGame.Core.Tests.HappyShenLuSuChecks.BothDingzhouBranchesAndUnrestrictedTeamZhimengCold),
    ("Shen Lu Su native Zhimeng random division cold", ShenLuSuChecks.NativeZhimengRandomDivisionCold),
    ("Shen Xu Chu actual turn maximum marked Duel and cold", ShenXuChuChecks.ZhengqingActualTurnsAndMarkedDuelCold),
    ("Shen Xu Chu self draw cap paid Zhuangpo discard cold", ShenXuChuChecks.ZhengqingSelfCapAndZhuangpoPaidDiscardCold),
    ("shen zhang fei native judgment overwrite and persistent punishments", ShenZhangFeiChecks.NativeJudgmentOverwriteAndPersistentPunishments),
    ("shen zhang fei native colorless targets quota and direct death", ShenZhangFeiChecks.NativeColorlessTargetsQuotaAndDirectDeath),
    ("Shen Hua Tuo: Luxin true invocation HP children cold and Jingyu per name", CardGame.Core.Tests.ShenHuaTuoChecks.LuxinInterruptsNextTrueInvocationAndJingyuCountsEachNameOnce),
    ("Shen Hua Tuo: Huandao restore two-stage random recipient loss cold", CardGame.Core.Tests.ShenHuaTuoChecks.HuandaoRestoresThenSamplesGeneralAndSkillBeforeRecipientLoss),
    ("Skill invocation: actual converted materials and native equipment cold", CardGame.Core.Tests.ShenHuaTuoChecks.ActualConvertedMaterialsAndNativeEquipmentCold),
    ("Shen Guo Jia Huishi native repeated judgment gift and cold restore", ShenGuoJiaChecks.HuishiNativeRepeatedJudgmentGiftAndColdRestore),
    ("Shen Guo Jia Hui Shi fallback four native draws", ShenGuoJiaChecks.HuiShiFourDrawFallbackUsesNativeAttempt),
    ("Shen Guo Jia Hui Shi Tianyi and native Zuoxing cold", ShenGuoJiaChecks.HuiShiAwakensAtPreparationAndZuoxingUsesRealTrickCold),
    ("Shen Guo Jia Hui Shi forced Dujiang actual zero Armor cold", ShenGuoJiaChecks.HuiShiForcesActualDujiangWithoutInventingArmorCold),
    ("shen guo jia real huishi baijia paid maximum cold", ShenGuoJiaCrossAwakeningChecks.RealHuiShiWaivesBaijiaHistoryAndKeepsPaidMaximumCold),
    ("shen sun ce native fenghe paid gift cold restore", ShenSunCeChecks.NativeFenghePaidGiftColdRestore),
    ("ShenTaishiCi: paid discard opaque take original turn and rotate cold", CardGame.Core.Tests.ShenTaishiCiChecks.PaidDiscardAndOpaqueTakeResumeOriginalTurnAndRotate),
    ("ShenTaishiCi: mission success physical Slash choice and dying failure cold", CardGame.Core.Tests.ShenTaishiCiChecks.MissionSuccessPhysicalSlashChoiceAndFailedDyingRecovery),
    ("ShenTaishiCi: higher HP Slash final Heart judgment cancels target cold", CardGame.Core.Tests.ShenTaishiCiChecks.HigherHpSlashDulieUsesFinalHeartJudgment),
    ("program interaction registration preserves dispatch boundaries", ProgramInteractionChecks.RegistrationAndDispatchPreserveBoundaries),
    ("program interaction AI preserves published choices and conditional policies", ProgramInteractionChecks.AiSelectionPreservesPublishedOrderAndConditionalPolicies),
    ("SP Pang Tong: ActualOpaqueRevealOptionalExchangeAndEqualRanksCold", SpPangTongChecks.ActualOpaqueRevealOptionalExchangeAndEqualRanksCold),
    ("Yuan Tan Yuan Shang: BasicDiscardDynamicSlashQuotaTwoTargetsAndTurnExpiryCold", YuanTanYuanShangChecks.BasicDiscardDynamicSlashQuotaTwoTargetsAndTurnExpiryCold),
    ("Lv Qian: ActualWeiluRepeatedSourceNextPlayAndExactRestoreCold", LvQianChecks.ActualWeiluRepeatedSourceNextPlayAndExactRestoreCold),
    ("Lv Kuang Lv Xiang: ActualSoleDodgeForeignNativeConversionAndSourceLossCold", LvKuangLvXiangChecks.ActualSoleDodgeForeignNativeConversionAndSourceLossCold),
    ("Xin Pi: ChijieAppliedGroupDamageStopsRemainingEffectsCold", XinPiChecks.ChijieAppliedGroupDamageStopsRemainingEffectsCold),
    ("Hua Man: ActualMansiAllHandBarbarianAndManyiNativeImmunityCold", HuaManChecks.ActualMansiAllHandBarbarianAndManyiNativeImmunityCold),
    ("Zhang Qi Ying: ActualFourTokensDiscardSuitsAndSourceLifetimeCold", ZhangQiYingChecks.ActualFourTokensDiscardSuitsAndSourceLifetimeCold),
    ("Cao Chun: InitialMarksPrintedEquipmentNativeLossAndCold", CaoChunChecks.InitialMarksPrintedEquipmentNativeLossAndCold),
    ("Cao Chun: acquired three marks Slash return and ending conditions cold", CaoChunChecks.AcquiredAfterStartGrantsThreeMarksAndPreservesEndingConditionCold),
    ("Wang Can: ActualSanwenPriorWitnessBatchAndTurnQuotaCold", WangCanChecks.ActualSanwenPriorWitnessBatchAndTurnQuotaCold),
    ("Fan Chou: ActualSilverLionFieldGainRecoveryChildCold", FanChouChecks.ActualSilverLionFieldGainRecoveryChildCold),
    ("Guo Si: ActualCompletedParentRejectsForeignWindowProofCold", GuoSiChecks.ActualCompletedParentRejectsForeignWindowProofCold),
    ("Tang Zi: ActualPrivateExactXunxunReorderAndNormalDrawCold", TangZiChecks.ActualPrivateExactXunxunReorderAndNormalDrawCold),
    ("Huang Quan: ActualRecordedDamageAndForeignRecoveryCold", HuangQuanChecks.ActualRecordedDamageAndForeignRecoveryCold),
    ("Huang Quan: ActualRequestedDeclarationSuccessAndChallengedFailureCold", HuangQuanDeclarationChecks.ActualRequestedDeclarationSuccessAndChallengedFailureCold),
    ("Zhang Ji: ActualTargetRankChooserJudgmentAndTwoDamageCold", ZhangJiChecks.ActualTargetRankChooserJudgmentAndTwoDamageCold),
    ("Su Fei: ConsecutiveNativeTargetsExactDrawGiftColdAndTurnQuota", SuFeiChecks.ConsecutiveNativeTargetsExactDrawGiftColdAndTurnQuota),
    ("Sima Hui: ActualFirstTurnOrderedSealsAndStableGrantedInstancesCold", SimaHuiChecks.ActualFirstTurnOrderedSealsAndStableGrantedInstancesCold),
    ("Xu Rong: ActualForeignPlayRandomPaymentsAndTurnRestrictionsCold", XuRongChecks.ActualForeignPlayRandomPaymentsAndTurnRestrictionsCold),
    ("Li Jue: LangxiActualPrepareRandomDamageCold", LiJueChecks.LangxiActualPrepareRandomDamageCold),
    ("Bei Mi Hu: ActualMarkedDamageOneDrawAndSevenObtainedAwakening", BeiMiHuChecks.ActualMarkedDamageOneDrawAndSevenObtainedAwakening),
    ("Liu Qi: WenjiActualGiftMatchingSlashAndDuelResponsesCold", LiuQiChecks.WenjiActualGiftMatchingSlashAndDuelResponsesCold),
    ("Lu Zhi Wei: ActualQingzhongCurrentMinimumAtomicExchangeCold", LuZhiWeiChecks.ActualQingzhongCurrentMinimumAtomicExchangeCold),
    ("Wang Yun: ActualRandomWeaponAndIssuedSlashDamageAwakening", WangYunChecks.ActualRandomWeaponAndIssuedSlashDamageAwakening),
    ("Zhuge Guo: ActualEquipmentAcquisitionExactPhysicalExtraTargetCold", ZhugeGuoChecks.ActualEquipmentAcquisitionExactPhysicalExtraTargetCold),
    ("Xi Zhi Cai: ActualGameStartRelationDamageAndClampedRecoveryCold", XiZhiCaiChecks.ActualGameStartRelationDamageAndClampedRecoveryCold),
    ("He Qi: JointBoundPrivacyRealDiscardAndOpaqueUnrevealedGain", HeQiChecks.JointBoundPrivacyRealDiscardAndOpaqueUnrevealedGain),
    ("Kan Ze: ActualWholeHandRecipientPrivateRevealAndBothComplementsCold", KanZeChecks.ActualWholeHandRecipientPrivateRevealAndBothComplementsCold),
    ("Zhao Xiang: ActualPaidPlayConversionDrawAndCumulativeRemovalCold", ZhaoXiangChecks.ActualPaidPlayConversionDrawAndCumulativeRemovalCold),
    ("Bu Zhi: ActualNativeHandGainAndWholeHandLossOfferOnce", BuZhiChecks.ActualNativeHandGainAndWholeHandLossOfferOnce),
    ("Dong Bai: BlackGiftRecipientAuthorityColdAndExactDraw", DongBaiChecks.BlackGiftRecipientAuthorityColdAndExactDraw),
    ("Jiqiao and Linglong: ActualPaidEquipmentDoubleRevealAllNonEquipmentCold", JiqiaoAndLinglongChecks.ActualPaidEquipmentDoubleRevealAllNonEquipmentCold),
    ("Old hand disposition: Actual Old Hand Discard Then Opaque Capped Foreign Cards", OldHandDispositionChecks.ActualOldHandDiscardThenOpaqueCappedForeignCards),
    ("Xuan Gong Zhu: ActualQimeiForeignHandChangesSharedQuotaAndDamageHpCold", XuanGongZhuChecks.ActualQimeiForeignHandChangesSharedQuotaAndDamageHpCold),
    ("Identity concealment: UndefinedHpRejectsNativeRecoveryAndMaxHpNumberCalculation", IdentityConcealmentChecks.UndefinedHpRejectsNativeRecoveryAndMaxHpNumberCalculation),
    ("Identity concealment: GameStartingHpLossAppearanceDoesNotOfferOffTurnGaoling", IdentityConcealmentChecks.GameStartingHpLossAppearanceDoesNotOfferOffTurnGaoling),
    ("Identity concealment: NativeGroupRecoveryAndSilverLionRemovalRejectUndefinedHp", IdentityConcealmentChecks.NativeGroupRecoveryAndSilverLionRemovalRejectUndefinedHp),
    ("Identity concealment: SetupEveryViewerHasUndefinedHpAndForeignTemplatePrivacy", IdentityConcealmentChecks.SetupEveryViewerHasUndefinedHpAndForeignTemplatePrivacy),
    ("Identity concealment: ActualOwnTurnAppearanceRestoresLordInitialBonusWithoutOffTurnGaoling", IdentityConcealmentChecks.ActualOwnTurnAppearanceRestoresLordInitialBonusWithoutOffTurnGaoling),
    ("Identity concealment: NativeSlashAppearanceGaolingRecoveryChildColdAndLaterDamage", IdentityConcealmentChecks.NativeSlashAppearanceGaolingRecoveryChildColdAndLaterDamage),
    ("Identity concealment: NativeProgramHpLossAndDamageReplacementRespectUndefinedHp", IdentityConcealmentChecks.NativeProgramHpLossAndDamageReplacementRespectUndefinedHp),
    ("Identity concealment: PaidGaolingRecoveryChildCanKillAppearingOwnerAndReturnOnce", IdentityConcealmentChecks.PaidGaolingRecoveryChildCanKillAppearingOwnerAndReturnOnce),
    ("Frozen hand roster: ActualFrozenRosterChooserOrderPrivacyAndMandatoryReward", FrozenHandRosterChecks.ActualFrozenRosterChooserOrderPrivacyAndMandatoryReward),
    ("Private whirl: PrivateFiveDepositsColdViewsAndResidualDrawCount", PrivateWhirlChecks.PrivateFiveDepositsColdViewsAndResidualDrawCount),
    ("Cao Ying: Granted Current Jianxiong Draw Child Then Claims Native Damage Card", CaoYingChecks.GrantedCurrentJianxiongDrawChildThenClaimsNativeDamageCard),
    ("Dong Zhao: actual other lord turn record and own nonlord boundary cold", DongZhaoChecks.ActualForeignLordTurnRecordAndOwnNonLordBoundaryCold),
    ("Bao 2025: claimed provenance and nonbasic policies coexist cold", Bao2025Checks.ActualClaimedMaterialProvenanceAndNonBasicPoliciesCoexistCold),
    ("Simultaneous Pindian: typed claims and common material ownership cold", SimultaneousPindianChecks.ActualTypedResultClaimsCommonMaterialOwnershipCold),
    ("Other death growth: actual deaths choose all skills and extra Play cold", DeathGrowthSkillsChecks.ActualOtherDeathsDrawChooseAllSkillsAndExtraPlayCold),
    ("First Slash Duel benefit: private first draw and combined ordinal", FirstSlashDuelBenefitChecks.ActualFirstSlashPrivateDrawAndCombinedOrdinal),
    ("Damage target budget: actual self source-less and two-point occurrences", DamageTargetBudgetChecks.ActualDamageOccurrencesSelfSourceLessAndTwoPoints),
    ("Damage zones and adjacent red target: each zone privacy once and turn expiry", DamageZoneAndAdjacentTargetChecks.ActualEachZonePrivateSelectionOnceAndTurnExpiry),
    ("Extra card effect and rank guess: Peach and stacked Alcohol cold replay", ExtraEffectsAndRankGuessChecks.ActualPeachAndDoubleAlcoholBonusColdReplay),
    ("Extra card effect and rank guess: paid source loss death and original use once", ExtraCardEffectPaidSourceChecks.PaidHandCostSourceLossAndOwnerDeathKeepOriginalUseOnce),
    ("Rice pile: actual ending hand equipment storage and cold children", RicePileChecks.ActualEndingDrawHEStoreAndColdMovementChildren),
    ("Incoming distance courtesy: actual faction cost permanent direction and extra turns", IncomingDistanceCourtesyChecks.ActualFactionCostAndPermanentDirectionAcrossTurns),
    ("Sole target protection: actual Slash and black trick substitution", SoleTargetProtectionChecks.SubstitutionChangesActualSlashAndBlackTrickTarget),
    ("Targeted trick hand HP: actual ending adjustment reward gift and cold", TargetedTrickHandHpChecks.ActualEndingDrawDiscardSelfAndOptionalGiftColdReturn),
    ("End refill next turn: capped actual refill and tied greatest hand", EndRefillNextTurnChecks.ActualEndRefillCappedAndNextTurnMostIncludesTie),
    ("Phase start card control: payment privacy directed distance and actual expiry", PhaseStartCardControlChecks.MeibuPaymentPrivacyDirectedDistanceAndActualTurnExpiry),
    ("Named ambush and source hand: private names all targets and cold recovery", NamedAmbushAndSourceHandChecks.PrivateNamesAndAllTargetUseStateSurviveColdRecovery),
    ("Forced phase Wine: actual actor commit quota and cold returns", ForcedPhaseAlcoholChecks.RealActorWineCommitQuotaAndColdReturns),
    ("Population health kinds: actual maximum HP once and cold children", PopulationHealthKindChecks.ActualMaximumHpChangesDrawOnceAndColdReplayPaidChildren),
    ("Card response completion cost: typed health kind actual recovery", CardResponseCompletionCostChecks.TypedHealthKindMatchesActualResponseCostRecovery),
    ("HP conditional grants: actual HP thresholds phase quota and cold replay", HpConditionalSkillGrantChecks.ActualHpThresholdsPhaseQuotaAndColdReplay),
    ("Zhanyi benefits: recovery rule expiration and legacy defaults", ZhuLingBenefitChecks.SharedRecoveryAndRuleExpirationKeepLegacyDefaults),
    ("Triggered card conversion: real hand equipment materials and cold replay", TriggeredCardConversionChecks.BlackHandAndEquipmentUseRealMaterialsAndColdReplay),
    ("program runtime hook registration preserves phase order and per-game isolation", ProgramRuntimeHookChecks.OrderedPhasesAndMatchIsolation),
    ("program runtime hook registration rejects invalid metadata", ProgramRuntimeHookChecks.InvalidMetadataRejected),
    ("program runtime proofs preserve typed admission order and dying groups", ProgramRuntimeProofChecks.OrderedTypedAdmissionAndDyingGroups),
    ("program continuation registration preserves ordered typed returns", ProgramContinuationChecks.OrderedRegistrationPreservesReturnBoundaries),
    ("native return protocol preserves ordered preparation and isolation", ProgramNativeReturnChecks.OrderedNativeBoundariesPreservePreparationAndIsolation),
    ("native return protocol rejects invalid metadata", ProgramNativeReturnChecks.InvalidNativeReturnMetadataFailsBeforeDispatch),
    ("program window returns preserve typed parent guards and registration", ProgramWindowReturnChecks.RegistrationAndTypedParentGuardsPreserveBoundaries),
    ("typed trigger window driver preserves candidate and closing boundaries", ProgramTriggerWindowDriverChecks.TypedWindowDriverPreservesCandidateAndClosingBoundaries),
    ("trigger window protocol preserves ordered skip and typed returns", TriggerWindowProtocolChecks.OrderedSkipAndTypedReturnsPreserveBoundaries),
    ("First strategic damage: committed cursor survives real child windows and replay", FrameOwnedBatchChecks.StrategicFirstDamageInstructionKeepsCommittedCursorAcrossChildren),
    ("Luochong discard: accumulated payments remain frozen across replay", LuochongDiscardChecks.AccumulatedDiscardsRemainFrozenAcrossReplay),
    ("Observer subtree: exact paid edges, pending choice and once-only cold return", PairedHandRecastChecks.ObserverSubtreeRejectsUnrelatedEdgesAndResumesExactlyOnce),
    ("frame-owned choices preserve multi-step bindings privacy and replay", ProgramChoiceDraftChecks.MultiStepBindingsPrivacyAndReplay),
    ("Damage nature: fire chain normal thunder HP loss and cold replay", DamageNaturePreventionChecks.ActualFireAndChainPreventedNormalThunderAndHpLossPreserved),
    ("Mobile Chen Zhen: same color hand payment shared phase quota and replay", MobileChenZhenChecks.SameColorHandPaymentSharedPhaseQuotaAndReplay),
    ("Zhanyi equipment entry: placed equipment triggers once and cold replays", ZhuLingEquipmentEntryChecks.PlacedEquipmentTriggersOnceAndColdReplays),
    ("Friend trio prediction privacy full reward and cold replay", FriendTrioChecks.PredictionPrivacyFullRewardAndColdReplay),
    ("Friend events recast Duel shared discard budget and cold replay", FriendEventChecks.RecastDuelSharesDiscardBudgetAndColdReplay),
    ("Mou Zhuge Liang private top yang payment and cold replay", MouZhugeLiangChecks.PrivateTopYangPaymentAndColdReplay),
    ("Mou Zhuge Liang copied basic cards finish without physical cleanup", MouZhugeLiangChecks.CopiedBasicCardsFinishWithoutPhysicalCleanup),
    ("Friend Pang Tong manjuan ordered current gain cold replay", FriendPangTongChecks.ManjuanOrdersOnlyCurrentGainAndReplays),
    ("Recipient category mark: face-up gift issues one public token and survives source loss", OrdinaryZuoFenChecks.FaceUpGiftIssuesOnePublicTokenAndRetainsItAfterSourceLoss),
    ("Chained state basic: own play pays state and returns once", ChainedStateBasicConversionChecks.OwnPlayStateCostUsesNativeQuotaAndReturnsOnce),
    ("Foreign selected card Slash: hand use and exact outcome", ForeignSelectedCardSlashChecks.HandSlotsFreezeNativeUseAndDamageOutcome),
    ("Actor hand limit penalty: real uses stack and change actual discard", ActorHandLimitPenaltyChecks.RealUsesStackAndChangeActualDiscard),
    ("Shared phase skills: owner and foreign choices preserve replay and exact expiry", SharedPhaseSkillChecks.OwnAndForeignChoicesReplayAndExpire),
    ("Play phase temporary skills: independent sources share state and preserve permanent grants", PlayPhaseTemporarySkillChecks.MultiplePhaseGrantSourcesShareInstanceStateQuotaAndPreservePermanentSource),
    ("Round gained source uses: qualified basic materials distance and quota", OrdinaryShiBaoChecks.BasicUsesRequireEveryCurrentRoundMaterialAndRespectSourceQualification),
    ("Unique maximum trick targets: actual hand leader and tied counts", OrdinaryXinChangChecks.LargestHandTrickAdditionUsesActualActorAndRejectsTies),
    ("Paired color responses: same color actual hand payment and duplicate grants", OrdinarySimaZhouChecks.SameColorPairDiscardsOneRealHandCardAndDeduplicatesGrants),
    ("Last zone conversions: equipment Nullification Silver Lion and movement children", LastZoneConversionChecks.LastEquipmentNullificationOwnsSilverLionRecoveryAndMovementChildren),
    ("Overflow target cancellation: unique physical and pair materials return through native gain", OrdinaryZhangHuaChecks.OverflowUniquePhysicalAndPairMaterialsReturnThroughNativeGain),
    ("Turn default allocation: phase defaults preserve modifiers and repeated Play", OrdinaryZhouChuChecks.PhaseDefaultsRemainSeparateFromNormalModifiersAndRepeatedPlay),
    ("Card response completion: native Zhangba pair and cold return", CardResponseCompletionNativeChecks.NativeZhangbaDuelResponseCompletesTwoEntitiesOnceAndColdReturns),
    ("Same name hand demand: actual faction providers own completion exactly once", OrdinaryGuoHuaiChecks.FactionProvidersOwnTheirCompletedResponseExactlyOnce),
    ("Original hand entities: initial physical loss, returned card and real paired recast", OrdinaryYangHuChecks.OriginalInitialEntitiesDepartOnceAndChongxinReallyRecasts),
    ("Native discard classification: exact paid producers exclude adjacent gain and cleanup", NativeDiscardClassificationChecks.ExactPaidProducersExcludeNeighborReasons),
    ("Phase name prediction: secret actual phase, escalating round cost and native settlement", PhaseNamePredictionChecks.SecretExactPhaseAndSettlement),
    ("Last damage source reciprocity: latest actual source, native HE payment and physical regrant memory", LastDamageSourceReciprocityChecks.LatestSourceAndPhysicalRegrantKeepExactNativeReciprocity),
    ("Unexpected Assault: opaque reveal native damage counterspell and conversion lifetime", UnexpectedAssaultChecks.OpaqueRevealDamageNullificationAndConversionLock),
    ("Activation-count awakening: actual refunded activations preparation quota and source loss", GameActivationAwakeningChecks.ActualRefundedActivationsQualifyPreparationOnceAndSurviveSourceLoss),
    ("Category discard refund: distinct payment phase refund and actual turn target ban", DrawDiscardCategoryRefundChecks.DistinctPaymentRefundsPhaseOnlyAndBansOriginalActualTurnTarget),
    ("Draw-funded distinct basic: qualification ledger and paid children", DrawFundedDistinctBasicUseChecks.QualificationLedgerAndPaidChildren),
    ("Draw-funded distinct basic: native Dodge Dying and program returns", DrawFundedDistinctBasicResponseChecks.NativeDodgeDyingAndProgramReturns),
    ("Liejie source discard: effective red quota mixed HE and native children", LiejieSourceDiscardChecks.EffectiveRedQuotaKeepsMixedHePaymentAndNativeChildren),
    ("Choice target prerequisite: missing targets retain mandatory draw recovery and consume group once", ChoiceTargetPrerequisiteChecks.MissingTargetsRetainMandatoryDrawRecoveryAndConsumeGroupOnce),
    ("Different color judgment replacement: effective owner and subject colors pay hand equipment and replay once", DifferentColorJudgmentReplacementChecks.EffectiveOwnerAndSubjectColorsPayHandEquipmentAndReplayOnce),
    ("Ending damage marker: frozen actual damage settles once", TurnEndDamageMarkerChecks.FrozenActualDamageAddsOnceAcrossChildrenAndColdReplay),
    ("Liu Hong activation: decline and paid children return once", LiuHongActivationChecks.YujueDeclineAndPlayActivationKeepRealPaidChildrenOnce),
    ("Tang Ji paid continuation: marked death discards and returns once", TangJiPaidContinuationChecks.MarkedDeathDiscardsOriginalHandEquipmentAndReturnsOnce),
    ("Tongxie use provenance: live child and returned entity use response", TongxieUseProvenanceChecks.LiveFollowUpDoesNotReenterAndReturnedEntityHasIndependentUseOrResponse),
    ("Zhuihuan retaliation: cumulative payments survive damage children and replay", ZhuihuanRetaliationChecks.AccumulatedPunishmentsSurviveDamageChildrenAndReplay),
    ("Draw advice and designated extra target: frozen draw qualifications and HE payment", DrawAdviceAndDesignatedExtraTargetChecks.DrawAdviceFreezesBothQualificationsAfterRealDrawAndPaysHeOnce),
    ("Gifted Slash hand policy: actual use damage reward and cold children", GiftedSlashHandPolicyChecks.GiftedNonSlashUsesFreezeDamageRewardAndColdChildren),
    ("Public pile cash out: damage points hand equipment and decline", PublicPileCashOutChecks.DamagePointsStoreHandOrEquipmentAndDeclineWithColdReturn),
    ("Current turn hand exchange: frozen foreign quantity and native HE returns", CurrentTurnHandExchangeChecks.ForeignGiftFreezesQuantityAndReturnsThroughNativeChildren),
    ("Exact owned card count and named targets: frozen payment recovery children", ExactOwnedCardCountAndNamedTargetsChecks.ExactOwnedCountFreezesPaidCostThroughRecoveryAndNamedTargets),
    ("Actual target prompt retains the AI-owned attack", ActualUseTargetPauseChecks.PacedAiTargetPromptCommitsColdAndReturnsOnce),
    ("Owned hand exact rank sum actual gift and recovery return", OwnedHandRankSumChecks.ExactOwnedHandRankSumGiftsAllThirteenAndReturnsThroughChildrenOnce),
    ("Initial hand modifiers: owner maximum HP controls the real deal and dynamic hand limit", InitialHandModifierChecks.ActualDealAndDynamicLimitPreserveRoleAndReplay),
    ("Owner dying awakening paid HP tail returns once", OwnerLifecycleReplacementChecks.DyingAwakeningReplacesOwnerSkillsAndResumesPaidHpTailOnce),
    ("Virtual ordinary trick composition frozen branches and public AI", VirtualOrdinaryTrickCompositionChecks.FrozenBranchesAndParticipantResourcesAreValidated),
    ("Ending Pindian win loss tie actual virtual tricks and cold return", PhaseEndingVirtualTrickChecks.EndingPindianWinLossTieUseExactVirtualTricksAndColdReturn),
    ("Actual turn use ordinal: frozen declaration order and true uses", ActualTurnUseOrdinalChecks.FrozenDeclarationOrderCountsOnlyActualUsesAndColdRestores),
    ("Actual second use foreign grants share state and expire on end", ActualTurnTemporarySkillChecks.ActualSecondUseForeignGrantsShareStateAndExpireOnEnd),
    ("Actual legacy Wine synchronous HP and completion child return", ActualTurnLegacyAlcoholChecks.SynchronousLegacyWineWaitsForHpAndCompletionChildren),
    ("foreign ending owner and equipment costs wait once", ForeignEndingPaymentChecks.ForeignOwnerCostAndEquipmentResponseWaitOnce),
    ("Short range Slash and directed ending debt: ShortRangeNativeRedirectCursorAndFireOrdering", DirectedDistanceDebtChecks.ShortRangeNativeRedirectCursorAndFireOrdering),
    ("Equipment Slash ownership: KuangfuOwnEquipmentRecoveryAndWholeUseCompletionReturnOnce", KuangfuChecks.KuangfuOwnEquipmentRecoveryAndWholeUseCompletionReturnOnce),
    ("Actual hand gains and category gifts: ActualHandGainsPreserveBatchIdentityAndExcludeSelfRecursion", ActualHandGainAndCategoryGiftChecks.ActualHandGainsPreserveBatchIdentityAndExcludeSelfRecursion),
    ("Delegated judgment and gain gifts: actual private judgment and paid HE children", DelegatedJudgmentAndGainGiftChecks.DelegatedJudgmentPrivateChoosersAndRealHePaymentColdReplay),
    ("JudgedRankSlashPolicy.QiangwuActualJudgmentFrozenRankDistanceAndSeparateQuota", JudgedRankSlashPolicyChecks.QiangwuActualJudgmentFrozenRankDistanceAndSeparateQuota),
    ("skill program public pile preparation whole fill paid child", PublicPilePreparationChecks.WholePileFillRetainsPaidChild),
    ("Phase hand seizure: LihunRealCostFlipTakeAndFrozenHpReturnColdReplay", PhaseHandSeizureChecks.LihunRealCostFlipTakeAndFrozenHpReturnColdReplay),
    ("RecipientContestConsequencesActualBlackGiftSelfSecondAndTie", RecipientContestConsequencesChecks.ActualBlackGiftSelfSecondAndTie),
    ("RecipientContestConsequences winner own private discards cold replay", RecipientContestConsequencesChecks.ActualBlackGiftWinnerOwnPrivateDiscardsColdReplay),
    ("skill program source curse physical initial judgment replacement and actual turn loss", OrdinaryZhangBaoChecks.CurseIsThePhysicalInitialJudgmentBeforeGuidao),
    ("skill program activation BooleanState nested gates and current instance isolation", ProgramActivationBooleanStateChecks.NestedGatesUseTheCurrentOwnerSkillInstance),
    ("Damage judgment suit payment four suits and exact claims", BoundaryCaiWenJiChecks.DamageJudgmentBeforePaymentFourSuitsAndExactClaims),
    ("Random reveal color Fire Attack HE payment and children cold", BoundaryWolongZhugeLiangChecks.RandomRevealColorHePaymentAndChildrenCold),
    ("Actual turn use kinds per Play draw child and actual turn expiry", BoundaryGuoHuaiChecks.EachPlayEndingDrawUsesActualTurnHistoryAndReturnsFromGainChild),
    ("Guo Huai Jingce: shared suits and types distinguish own and foreign Play endings", BoundaryGuoHuaiChecks.StandardAndBoundaryShareSuitAndTypeBenefitsButKeepDifferentEndingScopes),
    ("Discarded provenance virtual Alcohol face children and zero entity", BoundaryCaoZhiChecks.VirtualAlcoholWaitsForFaceChildrenAndUsesNoPhysicalEntity),
    ("Granted entity extra phase distance claim children and cold", BoundaryLiaoHuaChecks.ExtraPhaseEntityDistanceAndClaimChildrenAreExact),
    ("Next actual-use paid wins refresh one target token and original claims", BoundaryJianYongChecks.NextActualUsePaidWinsRefreshSingleTargetTokenAndClaimOriginalCards),
    ("Original target addition rights quota armor and cold", BoundaryGaoShunChecks.OriginalTargetAdditionQuotaArmorAndCold),
    ("Neighbor discard own first previous actual entities order privacy", BoundaryYuFanChecks.ActualOwnAndFirstPreviousDiscardKeepEntitiesOrderAndPrivacy),
    ("boundary cheng pu completion payment recovery and dying", BoundaryChengPuChecks.CompletionPaymentWaitsForSilverLionAndFatalHpChildren),
    ("boundary wu guo tai paid equipment pair cost and typed return", BoundaryWuGuoTaiChecks.PaidEquipmentPairFreezesCostAndSwapsAfterChildren),
    ("Paid actual target HP and original use return", BoundaryWangYiChecks.PaidOtherUseNullificationOpaqueObtainAndActualSlash),
    ("public equipment half count draw resumes once", EquipmentHalfDrawChecks.PublicEquipmentCountDrawsOnceAndResumes),
    ("boundary li ru sequential discard top cost and typed child return", BoundaryLiRuChecks.SequentialAnyDiscardKeepsTopPaymentAndChildReturns),
    ("Shanhe Li Ru no-cost trick or all-hand paid child", ShanheLiruSkillChecks.NoCostTrickOrWholeHandKeepsPaidMovementChild),
    ("Shanhe Li Ru chosen-start hand difference fire and two-round quota", ShanheLiruSkillChecks.StartingSeatDifferenceFireEmptyHandAndTwoRoundQuota),
    ("Whole equipment donation actual X and recovery", BoundaryCaiFuRenChecks.WholeEquipmentDonationActualXRecoveryAndColdChildren),
    ("boundary xiao qiao suit prevention equipment cost and capped draw", BoundaryXiaoQiaoChecks.TianxiangEquipmentPaymentWaitsForRecoveryBeforeNewDamageAndCappedDraw),
    ("boundary lu su extra draw half hand gift and cold children", BoundaryLuSuChecks.ExtraDrawAndHalfGiftOwnTheirGainChildren),
    ("Real color payment Duel back-damage and mandatory Ending claim", BoundaryYanLiangWenChouChecks.RealColorPaymentDuelBackDamageAndMandatoryEndingClaim),
    ("boundary two zhangs equipment replacement paid children and cold draw", BoundaryZhangZhaoZhangHongChecks.CapturedEquipmentReplacementDrainsSourceAndRecipientChildrenBeforeDraw),
    ("boundary bu lian shi ordered opaque pair gift and reward", BoundaryBuLianShiChecks.OrderedOpaquePairPaysBeforePublicGiftAndReward),
    ("Foreign actual turn contest decline win expiry", BoundaryFuHuangHouChecks.ForeignActualTurnWinDeclineAndExpiry),
    ("boundary ma dai shown color hand ban and equipment response", BoundaryMaDaiChecks.ShownColorBansHandMaterialsButKeepsEquipmentResponses),
    ("Ending pair two draws own extra round and cold", BoundaryXiahouShiChecks.OwnAndExtraEndingKeepTwoSeparateDrawsAndColdChildren),
    ("Preparation discard actual cost", BoundaryYuJinChecks.ActualPreparationOpaqueDiscardCostAndZeroEquipmentCount),
    ("Yang Xiu multi-target trick owns draw children and unique Borrowed Sword is excluded", OrdinaryYangXiuChecks.ActualMultiTargetTrickWaitsForDrawAndUniqueBorrowedHolderDoesNotOffer),
    ("SP Ma Chao HP ordered distance and lost HP extra targets pay each target once", OrdinarySpMaChaoChecks.HpOrderedDistanceAndLostHpExtraTargetsPayEachTargetOnce),
    ("Ol Huang Zu wangong unlocks after each basic play", OrdinaryHuangZuChecks.WangongUnlocksAfterEachBasicPlay),
    ("Ol Cao Xing liushi damages and zhanwan drains", OrdinaryCaoXingChecks.LiushiDamagesAndZhanwanDrains),
    ("Ol Xun Chen fenglve contests and moushi marks", OrdinaryXunChenChecks.FenglveContestsAndMoushiMarks),
    ("Ol Deng Zhong kanpo takes the matching suit card", OrdinaryDengZhongChecks.KanpoTakesMatchingSuitCard),
    ("Ol Liang Xing luelve choice and zhuanxi amplification", OrdinaryLiangXingChecks.LuelveChoiceResolvesAndZhuanxiAmplifies),
    ("Ol Gao Lan xizhen duel damages and marks", OrdinaryGaoLanChecks.XizhenDuelDamagesAndMarks),
    ("Ol Zhang Changpu yanjiao splits equal rank sums", OrdinaryZhangChangPuChecks.YanjiaoSplitsEqualRankSums),
    ("Ol Lü Kai tunan converts dodge into slash", OrdinaryLvKaiChecks.TunanConvertsDodgeIntoSlash),
    ("Ol Zhou Fang duanfa recycles black cards in phase ledger steps", OrdinaryZhouFangChecks.DuanfaRecyclesBlackCardsInPhaseLedgerSteps),
    ("Ol Pan Jun guanwei observes another play phase", OrdinaryPanJunChecks.GuanweiObservesAnotherPlayPhase),
    ("Ol Yi Ji jijie hands deck bottom to selected participant", OrdinaryYiJiChecks.JijieHandsDeckBottomToSelectedParticipant),
    ("Ordinary Cao Hong real placement replacement and armor draw", OrdinaryCaoHongChecks.RealPlacementReplacementAndArmorDraw),
    ("Ordinary Guan Yin Ping red equipment cost before lost HP targets and real fire draw", OrdinaryGuanYinPingChecks.RedEquipmentCostFinishesBeforeLostHpTargetsAndRealFireDraw),
    ("ordinary OL Zhuge Ke empty-hand Dodge private top request and once payment", OrdinaryZhugeKeChecks.EmptyHandDodgeViewsFourPrivatelyAndPaysOriginalResponseOnce),
    ("Ordinary SP Cao Ren private whole hand after HP children and real discard", OrdinarySpCaoRenChecks.PrivateWholeHandAfterHpChildrenAndRealDiscard),
    ("Ordinary Chen Lin deferred private offer next actual start exchange", OrdinaryChenLinChecks.DeferredPrivateOfferTargetsNextActualStartAndExchangeReturnsOnce),
    ("Ordinary SP Cai Wen Ji original Dying four suits private cost cold replay", DyingSuitsAndEndingHistoryChecks.ChenqingOriginalDyingFourSuitsPrivateCostAndColdReplay),
    ("Ordinary SP Pang De two real costs and issued Duel typed return", OrdinarySpPangDeChecks.TwoRealCostsAndIssuedDuelTypedReturn),
    ("Ordinary Fu Wan same-use benefit and real Dodge settlement", OrdinaryFuWanChecks.RealBenefitAndDodgeCancellationPayOriginalEntitiesOnce),
    ("Ordinary Liu Xie Tianming SilverLion cost and unique current HP peer", OrdinaryLiuXieChecks.TianmingSilverLionPaymentAndUniqueCurrentHpPeerOwnColdChildren),
    ("OL signed damage actual material effective color and amount", OrdinaryLingJuChecks.PaidDamageAdjustmentUsesEffectiveOwnedColorAndActualAmount),
    ("Tiered Round true uses physical conversion and private cold", BoundaryGuoHuangHouChecks.TrueRoundNamesPhysicalConversionAndPrivateCold),
    ("Danxin: opening damage zero round and capped draw children cold", BoundaryGuoHuangHouChecks.OpeningDamageBenefitAndCappedDrawCold),
    ("Skill changes: opening damage dying upgrades replacements and cold", SkillChangeOpeningChecks.DamageAndDyingChangesBeforeFirstRoundCold),
    ("Owner batch loss equipment OR two cards once", BoundaryLingTongChecks.OwnerBatchEquipmentOrTwoCardsCreatesOneOpportunity),
    ("Shared Slash gift equipment recovery both gain children cold", BoundaryChenGongChecks.SharedGiftEquipmentRecoveryAndBothGainChildrenCold),
    ("Turn damage-use draw debt actual gain and frozen ending cost", BoundaryLiuBiaoChecks.ExtraDrawOwnsGainChildAndEndingDebtFreezesNewFactionCount),
    ("Dual private color commitments atomic payment real Duel and cold", BoundaryZhangChunHuaChecks.PrivateCommitmentsAtomicPaymentAndRealDuelCold),
    ("Actual turn target commitment damage weapon shared quota", BoundaryDianWeiChecks.RealDamageWeaponCostSharedQuotaAndActualTurnTargets),
    ("Owned damage point judgment actual alcohol suppression expiry", BoundaryDongZhuoChecks.UnlimitedAlcoholActualConsumptionAndTurnExpiry),
    ("Owned damage point judgment claimed gain bound Alcohol rescue and cold replay", OwnedJudgmentBoundAlcoholChecks.ClaimedJudgmentGainDyingBoundAlcoholReturnsAndColdReplays),
    ("Black trick target policy actual color and lightning", BoundaryJiaXuChecks.BlackTricksUseActualColorAndLightningTransferContext),
    ("Actual draw obligation distance whole turn paid equipment", BoundaryXuHuangChecks.DuanliangDistanceTracksActualWholeTurnDamageAndPaidEquipment),
    ("Phase hand extra turn native public declaration paid selected beneficiary", PhaseHandExtraTurnAiChecks.NativePublicDeclarationPaysAndExecutesSelectedBeneficiaryExtraTurn),
    ("Next-use rank bonus per point real consumption public pool and cold replay", BoundaryCaoChongChecks.PerPointBonusConsumptionPublicPoolAndColdReplay),
    ("Alternative phase cost actual Judgment Draw zero to two targets cold", BoundaryZhangHeChecks.JudgmentAndDrawSubstitutionPayAtTheirActualBoundaries),
    ("Participant hand actual requested Slash native AI paid gift", BoundaryFaZhengChecks.XuanhuoRealSlashAndNativeAiRetainPaidGift),
    ("Participant hand program bound alcohol rescue typed return and cold", ParticipantHandProgramRescueChecks.BoundAlcoholPaidDamageReturnAndColdReplay),
    ("Current Slash entity claim basic payment original damage and multiple materials", BoundaryPangDeChecks.BasicDiscardClaimsExactEntitiesAndStillResolvesOriginalSlash),
    ("Bound discard Slash frozen suit quota prepared privacy and expiry", BoundaryHanDangChecks.FrozenSuitQuotaPreparedPrivacyAndExpiry),
    ("BoundaryXuShu current rules awakening cold replay", BoundaryXuShuChecks.RunCurrentRulesAndAwakening),
    ("Recovery replacement Zhiheng selection and phase quota", BoundarySunQuanChecks.ZhihengSelectionAndPhaseQuota),
    ("Character turned over Shensu three branches and hand equipment payment", BoundaryXiahouYuanChecks.ThreeShensuBranchesAndHandEquipmentCost),
    ("Turn kind hand exemption survives source suppression actual turn expiry", TurnHandLimitKindExemptionChecks.IssuedKindPolicySurvivesSourceSuppressionUntilActualTurnEnd),
    ("Random Pindian both strategies exact RNG tied Slash claims cold replay", BoundaryTaiShiCiChecks.BothRandomStrategiesFreezeReceiptsAndClaimTiedSlashes),
    ("Bound card recast draw mode real Slash response cold replay", BoundaryCaoZhangChecks.DrawMoreReducesUseLimitButKeepsRealSlashResponse),
    ("Capped hand refresh real damage private discard and cold replay", BoundaryXunYuChecks.RunCappedHandRefreshAndColdReplay),
    ("HP paid shield source death full damage prevention and once payment", BoundarySunJianChecks.WuliePaysOnceAndPreventsWholeDamageAfterSourceDeath),
    ("Recovery replacement paid Qixi stops after observer victory", RecoveryPaidWinnerChecks.SilverLionPaidQixiStopsAfterObserverVictory),
    ("Program Iron Chain third target owned recast cold replay", BoundaryPangTongChecks.LianhuanAddsThirdTargetAndRecastsOwnedEquipment),
    ("boundary Ma Chao Tieqi Paoxiao exact cancellation integration", BoundaryMaChaoChecks.TieqiPaoxiaoTrueDodgeAndNextUseReplay),
    ("program conversion polarity shared state and replay", ProgramConversionPolarityChecks.SharedStateAndReplay),
    ("Feng Lin Wang Ji actual targets private nested payment", FengLinWangJiChecks.ActualTargetsPrivateAndNested),
    ("Population reorder actual partition and short pile", BoundaryZhugeLiangChecks.PopulationPartitionAndShortPile),
    ("Exact top count definition legacy null", BoundaryLiDianChecks.DefinitionAndLegacy),
    ("Unconverted Trick actual use qualification", BoundaryHuangYueyingChecks.ActualUseQualification),
    ("Damage target obtain HEJ printed category privacy", BoundaryLuBuChecks.HejAndPrintedCategory),
    ("Damage target mount real HEJ privacy inputs", BoundaryGongsunZanChecks.RealZonesAndInputs),
    ("Recorded suit Slash: recording reveal damage cold replay", RecordedSuitSlashChecks.RecordingRevealDamageAndReplay),
    ("Final target Slash frozen comparisons actual paid Dodge", BoundaryHuangZhongChecks.ComparisonsAndActualDodge),
    ("Final target Slash escalation: judgment quota and Silver Lion", FinalTargetSlashEscalationChecks.ExactTargetJudgmentQuotaAndSilverLion),

    ("Boundary Yuan Shao population start role marker limit", BoundaryYuanShaoChecks.StartingPopulationAndQualification),
    ("Selected actor Duel actual Use and Nullification", BoundaryHuaXiongChecks.ActualDuelAndNullification),
    ("Paid HP loss quantity and native", BoundaryWeiYanChecks.QuantityAndNative),
    ("Boundary Deng Ai own turn slash discard judges authority", BoundaryDengAiChecks.OwnTurnSlashDiscardJudgesIntoAuthority),
    ("Boundary Jiang Wei damaging slash suppresses discard", BoundaryJiangWeiChecks.TauntDamagingSlashSuppressesDiscard),
    ("Boundary Sun Ce reclaim once per turn", BoundarySunCeChecks.JiangReclaimsDiscardOncePerTurn),
    ("Boundary Zhu Rong juxiang immunity claims resolved barbarian", BoundaryZhuRongChecks.JuxiangImmuneAndClaimsResolvedBarbarian),
    ("Boundary Meng Huo barbarian immunity and attribution", BoundaryMengHuoChecks.HuoshouImmuneAndAttributesBarbarianDamage),
    ("Boundary Guan Xing Zhang Bao converted slash response follows pair color", BoundaryGuanXingZhangBaoChecks.ConvertedSlashResponseFollowsPairColor),
    ("Boundary Zhou Tai buqu rescue wound hand limit and duplicate rank", BoundaryZhouTaiChecks.BuquRescueWoundHandLimitAndDuplicateRank),
    ("Boundary Cao Ren jushou flips draws and discards hand card", BoundaryCaoRenChecks.JushouFlipsDrawsAndDiscardsHandCard),
    ("Equipment placement actual replacement Xiaoji transfer", BoundarySunShangxiangChecks.EquipmentAndReplacement),
    ("Boundary Zhang Fei cancellation next real Use freeze", BoundaryZhangFeiChecks.CancellationAndUseFreeze),
    ("Feng Lin Kuai Jianxiang actual finalized minimum tie", FengLinKuaiChecks.JianxiangActualFinalTargetMinimumTie),
    ("participant discard mandatory cost cancellation preserves legal composition", BoundaryHuaTuoChecks.MandatoryPaymentCancellationPreservesLegalComposition),
    ("boundary Lu Xun HE payment exact Hand batch count", BoundaryLuXunChecks.OwnedHePaymentCountsActualHandBatch),
    ("Feng Lin Zuo Ci old registry no library shape", FengLinZuoCiChecks.OldRegistryHasNoLibraryShape),
    ("Feng Lin Zhou Fei first domains and true equipment sequence", FengLinZhouFeiChecks.DomainFirstAndEquipmentSequence),
    ("Feng Lin Lu Zhi actual discard entry boundaries",FengLinLuZhiChecks.ActualDiscardEntryBoundaries),
    ("Feng Lin Xu You bottom draw and alternating replay", FengLinXuYouChecks.BottomDrawAndAlternatingReplay),
    ("Shicai single entity automatic top and conversion eligibility", FengLinXuYouChecks.SingleEntityAutomaticTopAndConversionEligibility),
    ("Shicai completed Dodge retains native qualification and cold replay", ShicaiResponseContextChecks.CompletedDodgeRetainsNativeQualificationAndColdReplay),
    ("shared public pile independent source thresholds", FengLinGuanqiuJianChecks.IndependentSourceThresholds),
    ("Feng Lin Zhuge Zhan private quota and top replay", FengLinZhugeZhanChecks.PrivateQuotaAndTopReplay),
    ("FengLin Lu Kang live chain delayed Pindian policies", FengLinLuKangChecks.ChainAndTargetPolicies),
    ("FengLin Chen Dao first use issued phase ban", FengLinChenDaoChecks.FirstUseAndIssuedPhaseBan),
    ("Feng Lin Sun Liang discard budget draw damage", FengLinSunLiangChecks.DiscardBudgetDrawAndDamage),
    ("Feng Lin Lu Ji actual marker payment and damage", FengLinLuJiChecks.PhysicalStartGiftPrevention),
    ("boundary Liu Bei virtual Peach HP child", BoundaryLiuBeiChecks.VirtualPeachHpChild),
    ("boundary Da Qiao placement refill before delayed Nullification legacy JSON", BoundaryDaQiaoChecks.CompletedPlacementDrawsBeforeDelayedNullification),
    ("boundary Lu Meng all table printed suits replay", BoundaryLuMengChecks.AllTablePrintedSuitsAndReplay),
    ("after turn ended return movement prelude ordering", AfterTurnEndedChecks.ReturnMovementPreludeBeforeWindow),
    ("FengLin Hao Zhao two dynamic actual ends native discard", FengLinHaoZhaoChecks.DynamicTwoEndsAndNativeDiscard),
    ("Feng Lin Wang Ping paid hand frozen qualification", FengLinWangPingChecks.PaidHandDemandAndFrozenQualification),
    ("Fame 2017 Xue Zong real response entity exchange", Fame2017XueZongChecks.SlashDuelAndCounterspellEntityExchange),
    ("Fame 2017 Ji Kang real equipment nested replay", Fame2017JiKangChecks.RealRandomEquipReplacementNestedReplay),
    ("Fame 2017 Cao Jie real named actor cost privacy nested replay", Fame2017CaoJieChecks.NamedDefenseActorPaymentAndPrivateTake),
    ("Fame 2017 Qin Mi real top private tied replay", Fame2017QinMiChecks.PindianRealTopPrivateAndTiedReplay),
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
    ("active effect snapshots preserve real duration ownership freezing and replay", ActivePlayerEffectChecks.LiveDurationOwnershipAndFreezing),
    ("public pile projection preserves empty and live queries", PublicPileProjectionChecks.EmptyAndLiveSourcesPreserveOriginalQueriesAndFreeze),
    ("zero material uses preserve appearance targets and replay", ZeroMaterialTargetAdjustmentChecks.ZeroMaterialUsesPreserveAppearanceTargetsAndReplay),
    ("character manifests preserve program bindings and reject invalid input", CharacterContentCatalogChecks.ProgramBindingsAndInvalidInput),
    ("skill executor retires strategic damage batches", FrameOwnedBatchChecks.StrategicDamageCompletesAndCancelsWithItsFrame),
    ("response use completion Dodge timing parent and replay", ResponseUseCompletionChecks.DodgeCompletesAfterCostAndResumesSlashOnce),
    ("response use completion counterspell chain and replay", ResponseUseCompletionChecks.CounterspellCompletesAfterCostAndResumesChainOnce),
    ("response use completion counterspell dying rescue exact parent", ResponseUseCompletionChecks.CounterspellCompletionDyingRescueRetainsExactParent),
    ("response use completion Dodge dying rescue exact parent", ResponseUseCompletionChecks.DodgeCompletionDyingRescueRetainsExactParent),
    ("response use completion excludes group response and wrong loader window", ResponseUseCompletionChecks.GroupDodgeStaysResponseOnlyAndLoaderRejectsWrongWindow),
    ("Fame 2017 Xu Shi foreign equipment AI replay", Fame2017XuShiChecks.ForeignEquipmentGiftAndAiReplay),
    ("Fame 2017 public pile store exchange distribute replay", Fame2017PublicPileChecks.StoreExchangeDistributionAndReplay),
    ("Fame 2017 Xin Xianying independent comparison quota replay", Fame2017XinXianyingChecks.ComparisonIndependentBranchesQuotaAndReplay),
    ("Fame 2016 deferred pile death loss face down skip replay", Fame2016DeferredChecks.DeferredPileDeathLossAndFaceDownSkip),
    ("Fame 2016 real ordered Slash and replay", Fame2016StateChecks.RealOrderedSlashAndReplay),
    ("shared post-event recipient batch counts and reason filters", SharedPostEventChecks.TransferFiltersAndBatchCountsUseTheRecipient),
    ("shared use lifecycle nests rescue inside a suspended card window", CardUseLifecycleChecks.NestedRescueRetainsTheOuterUseWindow),
    ("Happy Shen Wei Yan: real bone slot first equipment native children cold", HappyShenWeiYanChecks.RealBoneSlotFirstEquipmentAndNativeChildrenCold),
    ("Happy Shen Wei Yan: Ezhi physical use Gujing gain replacement and native bone returns", HappyShenWeiYanChecks.EzhiPhysicalUseGujingDestinationAndDiscardedNativeUses),
    ("Happy Shen Zhang Fei: real take children return before death mark cold", CardGame.Core.Tests.HappyShenZhangFeiChecks.RealTakeChildrenReturnBeforeDeathMarkCold),
    ("Happy Shen Da Xiao Qiao: independent pools native recovery dying cold", CardGame.Core.Tests.HappyShenDaXiaoQiaoChecks.IndependentPoolsNativeRecoveryAndDyingResumeCold),
    ("Happy Shen Zuo Ci: persistent physical faces native gain destruction and overflow cold", CardGame.Core.Tests.HappyShenZuoCiChecks.RealPersistentPhantomsNativeUseForeignGainAndOverflowCold),
    ("OL Lu Shi: ending history lifetime adjustments native thunder pair cold", CardGame.Core.Tests.OlLuShiChecks.EndingHistoryLifetimeAdjustmentsAndNativeThunderPairResumeCold),
    ("Anniversary Ruan Ji: shown entities actual-turn names persistent native Wine cold", AnniversaryRuanJiChecks.ShownEntitiesOrdinaryNamesAndPersistentNativeWineCold),
    ("Anniversary Kan Ze: actual second damage point per turn native recovery cold", AnniversaryKanZeChecks.ActualSecondDamagePointPerTurnAndNativeRecoveryCold),
    ("Anniversary Hu Zhao: native areas, Huomo and distance cold", CardGame.Core.Tests.AnniversaryHuZhaoChecks.NativeAreaCostsJudgmentRestorationHuomoAndDistanceCold),
    ("Anniversary Xu Jing: marked entity circulation actual use debt cold", CardGame.Core.Tests.AnniversaryXuJingChecks.MarkedEntityCirculatesAndActualUsesRepayCaixiaResumeCold),
    ("Anniversary Du Kui: revealed ranks native uses mutual range field moves cold", AnniversaryDuKuiChecks.RevealedRankLoopNativeUseAndMutualRangeMovesCold),
    ("Anniversary Liu Yong: Xi leader native uses phase quota cold", CardGame.Core.Tests.AnniversaryLiuYongChecks.XiLeaderNativeUsesAndPhaseQuotaResumeCold),
    ("Anniversary Liu Li: category payment dynamic cap native round names cold", CardGame.Core.Tests.AnniversaryLiuLiChecks.CategoryDiscardDynamicDrawAndNativeRoundNamesCold),
    ("Anniversary Zhang Fen: real cart upgrades inserted Play destruction native returns cold", AnniversaryZhangFenChecks.RealCartUpgradesInsertedPlayDestructionAndNativeReturnsCold),
    ("Anniversary Bai Ling Yun: private top native use suppression limited recovery cold", CardGame.Core.Tests.AnniversaryBaiLingYunChecks.PrivateTopNativeUseSuppressionAndLimitedRecoveryResumeCold),
    ("Anniversary Yang Biao: normal draw obligation, dying HE payment and whole random Uses cold", CardGame.Core.Tests.AnniversaryYangBiaoChecks.NormalDrawObligationDyingHePaymentAndWholeRandomUsesCold),
    ("Anniversary Le Jiu: paid whole-use bonus reverse Duel undamaged native return cold", AnniversaryLeJiuChecks.PaidWholeUseBonusDuelReverseAndUndamagedNativeReturnCold),
    ("Anniversary He Yan: sourced choices and sealed suit hands cold", CardGame.Core.Tests.AnniversaryHeYanChecks.SourcedChoicesAndSealedSuitHandsResumeCold),
    ("Anniversary Guo Zhao: replacement colors permanent options native equipment cold", CardGame.Core.Tests.AnniversaryGuoZhaoChecks.ReplacementColorLeasePermanentOptionsAndNativeEquipmentCold),
    ("Anniversary Mang Ya Chang: first source damage native Lion cap survivor discard death cold", AnniversaryMangYaChangChecks.FirstSourceChoiceNativeLionCapSurvivorDiscardAndDeathCold),
    ("Anniversary Feng Xi: native refill and black-target branches cold", CardGame.Core.Tests.AnniversaryFengXiChecks.NativeRefillAndBothBlackTargetBranchesResumeCold),
    ("Anniversary Zhao Tong Zhao Guang: real pair basics Wine Played Slash awakening Qingren cold", CardGame.Core.Tests.AnniversaryZhaoTongZhaoGuangChecks.PairBasicRequirementNativeWinePlayedSlashAwakeningAndQingrenCold),
    ("Anniversary Lv Dai: atomic equipment entry and exit recover once cold", CardGame.Core.Tests.AnniversaryLvDaiChecks.AtomicEquipmentExitAndEntryRecoverOnceCold),
    ("Anniversary Gao Xiang: chosen participant private discards, basic acquisition and cold", CardGame.Core.Tests.AnniversaryGaoXiangChecks.ChosenParticipantPrivateDiscardsBasicAcquisitionAndCold),
    ("Anniversary Fu Rong Fu Qian: whole-damage protection post-maximum refill cold", AnniversaryFuRongFuQianChecks.WholeDamageProtectionAndPostMaximumLostHpRefillCold),
    ("Anniversary Xiahou En: actual-turn native Duel permanent sword temporary loss cold", AnniversaryXiahouEnChecks.ActualTurnNativeDuelPermanentSwordAndTemporaryLossCold),
    ("Anniversary Zhang Wen: winning Pindian refund and whole-extrema gift cold", CardGame.Core.Tests.AnniversaryZhangWenChecks.SongshuRefundAndSibianWholeExtremaGiftResumeCold),
    ("Anniversary Zhang Xun: next actual turn Slash benefits native followup and cold", CardGame.Core.Tests.AnniversaryZhangXunChecks.NextActualTurnSlashBenefitsNativeFollowupAndCold),
    ("current classic catalogue modes and skill references", CurrentClassicContentChecks.CatalogueAndModes),
    ("Fame 2011 equipment atomic exchange and replay", Fame2011EquipmentChecks.GanluAtomicEquipmentAndReplay),
    ("Fame 2011 equipment dying reveal privacy and replay", Fame2011EquipmentChecks.BuyiSelfChoiceBlindOtherPrivacyAndReplay),
    ("Fame 2017 Wu Xian Borrowed Sword pairs actual use replay", Fame2017WuXianChecks.BorrowedSwordPairsActualUseAndReplay),
    ("Fame 2016 Taoluan equipment global nullification", Fame2016TaoluanChecks.EquipmentGlobalAndNullification),
    ("Taoluan AI uses public ally inference and private gift choices", TaoluanCooperationAiChecks.PublicInferenceAndPrivateGiftChoices),
    ("Taoluan AI requests teammate and replays recorded policy", Fame2016TaoluanChecks.AiRequestsTeammateAndReplaysRecordedPolicy),
    ("Fame 2016 conversion declaration entity privacy replay", Fame2016ConversionChecks.DeclarationEntityPrivacyAndReplay),
    ("Fame 2015 Wei top deck basic use name ledger replay", Fame2015WeiChecks.TopDeckBasicUseNameLedgerAndReplay),
    ("Fame 2015 Shu all hand Duel draw limit replay", Fame2015ShuChecks.AllHandDuelDrawLimitPaymentAndReplay),
    ("Fame 2015 hand control color real cost opaque gain replay", Fame2015HandControlChecks.RevealColorRealCostOpaqueGainAndHpLossReplay),
    ("Program trigger resource contracts", ProgramTriggerResourceChecks.TriggerWindowAndFrozenSuitContracts),
    ("Fame 2014 control Pindi categories targets physical cost and replay", Fame2014ControlChecks.PindiPhysicalCategoriesTargetLimitsAndReplay),
    ("Fame 2013 Li Ru fire chain dying resume and replay", Fame2013LiRuChecks.FireChainAndDyingResumeReplay),
    ("Fame 2013 sequential trick targets physical use and replay", SequentialTrickTargetChecks.AllFiveTricksResolveEveryTargetAndReplay),
    ("Fame 2013 discard movement origin native Dismantlement and replay", DiscardMovementOriginChecks.NativeDismantlementDiscardPreservesOriginAndReplay),
    ("turn-ending Xiaoguo Tianxiang game over stops later observers", TurnEndingGameOverChecks.XiaoguoTianxiangVictoryStopsLaterObservers),
    ("Feng Lin Congjian death multiple grants single target boundaries", FengLinCongjianChecks.OwnerDeathAndMultiGrantBoundaries),
    ("Zhang Xiu Xiongluan abolishes areas and blocks hand without armor bypass", ResponseAndSkillSuppressionChecks.XiongluanBlocksHandButDoesNotIgnoreArmor),
    ("2026 Shen Sima Yi kill grants exactly one extra turn with replay", ShenSimaYiChecks.KillGrantsExactlyOneExtraTurnAndReplays),
    ("Gundam One beam rifle discards one card and deals damage once per turn", GaoDaYiHaoChecks.BeamRifleDiscardsOneAndDamagesOncePerTurn),
    ("Xu Sheng Pojun holds target cards and returns them at turn end", XuShengChecks.ClassicPojunHoldsAndReturnsAtTurnEnd),
    ("phase exchange mixed zones atomicity and replay", ProgramActivationLimitChecks.MixedZonesAtomicityAndReplay),
    ("owned-card set private draft movement and replay", ProgramOwnedCardsChecks.PrivateDraftBatchMovementAndReplay),
    ("owned-card selection bounds remain private and replay", ProgramOwnedCardsChecks.VariableBoundsStayPrivateAndReplay),
    ("owned-card set shortfall empty sources and invalidation", ProgramOwnedCardsChecks.ShortfallEmptyAndInvalidatedDraft),
    ("Program choice definitions validate choices, payments and presentation hashes", ProgramChoiceChecks.DefinitionsValidateChoicesPaymentsAndPresentationHash),
    ("completed Slash program window follows finished card use and replays", CardUseCompletedChecks.FinishedSlashOpensReplayableProgramWindow),
    ("card-action judgment windows resume and replay", ProgramCardJudgmentWindowChecks.AllCardActionWindowsStartPublicJudgmentsAndReplay),
    ("composition kernel resource graph safety", ProgramCompositionDefinitionChecks.ResourceGraphsRejectAliasingLeaksAndMissingInputs),
    ("composition kernel malformed nodes", ProgramCompositionDefinitionChecks.MalformedNodesFailBeforeExecution),
    ("composition kernel boundary capabilities", ProgramCompositionContextChecks.SharedWindowsAcceptCommonNodesAndRejectMissingContexts),
    ("composition kernel target-set consumption", ProgramCompositionContextChecks.TargetSetIsConsumedOnce),
    ("execution plans freeze instructions and reject ambiguous bindings", ProgramExecutionPlanChecks.PlansFreezeInstructionsAndRejectAmbiguousBindings),
    ("rule query reduction is input-order independent", RuleQueryReducerChecks.IsIndependentOfInputOrderAndRejectsOnlyWinningSetConflicts),
    ("rule query reduction rejects invalid mutable inputs", RuleQueryReducerChecks.RejectsInvalidInputsAndFreezesOutput),
    ("rule query distance runs both directions before clamping", RuleQueryReducerChecks.DirectionalDistanceRunsBothStagesBeforeClamping),
    ("rule query actual-round use distance requires captured outgoing count", RuleQueryReducerChecks.ActualRoundUseDistanceRequiresCapturedCountAndOutgoingAddition),
    ("rule query registry rejects future Set conflicts", RuleQueryReducerChecks.StaticSetConflictsAreRejectedBeforePlay),
    ("character skills retain separate sources and stable ownership", CharacterSkillSetChecks.GrantsRetainSourcesAndStableOwnership),
    ("character skills reject conflicting grants without partial state", CharacterSkillSetChecks.InvalidGrantsAndConflictsLeaveStateUnchanged),
    ("boundary Zhen Ji frozen final outcome replacement claim", BoundaryZhenJiChecks.FrozenFinalOutcomeReplacementAndClaim),
    ("boundary Xiahou Dun each damage point real judgment branches", BoundaryXiahouDunChecks.GanglieEachPointRealJudgmentBranches),
    ("boundary Guan Yu extra Play quota named Heart source lifecycle", BoundaryGuanYuChecks.ExtraPlayQuotaAndNamedHeartBenefit),
    ("current turn nonlocked suppression host lifecycle audit", CurrentTurnSkillSuppressionChecks.HostLifecycleQualificationAudit),
    ("match skill binding sources preserve instance and unique-program semantics", MatchSkillBindingIndexChecks.SourcesDeduplicateInstancesAndUniqueProgramBuckets),
    ("module catalog contracts preserve validation order and failure identity", ProgramModuleContractChecks.CatalogValidationKeepsOrderAndFailureIdentity),
    ("program card legality preserves ordered stages and live queries", ProgramCardLegalityChecks.OrderedStagesAndLiveQueries),
    ("program card-use payments preserve ordered proofs and match isolation", ProgramCardUsePaymentChecks.OrderedPaymentProofsAndMatchIsolation),
    ("program damage hooks preserve actual order side effects and short circuits", ProgramDamageHookChecks.ActualDamagePhasesKeepOrderAndShortCircuit),
    ("module distance rules preserve ordered arithmetic and native query boundaries", ProgramDistanceRuleChecks.OrderedArithmeticAndNativeDistanceQueries),
    ("program target rules preserve ordered boundaries and live queries", ProgramTargetRuleChecks.OrderedBoundariesAndLiveQueries),
    ("program target windows preserve ordered subscriptions and exact target identity", ProgramTargetWindowProtocolChecks.OrderedWindowSubscriptionsAndExactTargetIdentity),
    ("program view contributors preserve ordered visibility and Processing privacy", ProgramViewContributorChecks.OrderedVisibilityContributionsAndProcessingPrivacy),
    ("program phase-used projections preserve ordered fold and match isolation", ProgramPhaseUsedProjectionChecks.OrderedFoldAndMatchIsolation),
    ("program fact requirements preserve entry sources and catalog isolation", ProgramFactRequirementChecks.CatalogDemandPreservesEntrySourcesAndIsolation),
    ("module operation continuation preserves dead-target receipt and host boundaries", ProgramModuleContractChecks.DeadTargetPolicyKeepsReceiptAndHostBoundaries),
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
    ("Boundary Huang Gai multi card color source lifecycle", FengLinBoundaryHuangGaiChecks.MultiCardColorAndSourceLifecycle),
    ("Feng Lin Yu Ji actual dodge duel rescue parents", FengLinYuJiChecks.ActualDodgeDuelRescueParents),
    ("Feng Lin Yuan Shu qualified awakening persistent local loss", FengLinYuanShuChecks.QualifiedAwakeningAndPersistentLocalLoss),
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
    ("Private reserve: ending skill order payments expiry and replay", PrivateReserveOrderChecks.EndSkillsChooseOrderPayOnceAndReplay),
    ("formal Hengye grows on damage and resets after a kill", MouLuMengChecks.HengyeGrowthAndKillReset),
    ("Jiangchi extra draw blocks Slash use and play", CaoZhangChecks.DrawMoreBlocksSlashUseAndResponse),
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
    ("manual discards accept partial payments and restore the remaining quota", ManualDiscardChecks.PartialPaymentsRestoreRemainingQuota),
    ("manual discards preserve doubled quota across partial payments cold", ShanheHuangkongChecks.PartialDiscardKeepsFrozenDoubledQuotaCold),
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
    ("classic Zhuque Fan converts ordinary Slash to Fire Slash and preserves owner choice", ZhuqueFanChecks.FireConversionChainFactionSlashAndLegacyBoundary),
    ("classic Silver Lion caps damage and recovers after leaving equipment", SilverLionChecks.DamageCapRemovalRecoveryAndLegacyBoundary),
    ("classic Wooden Ox stores private playable grain and replays", WoodenOxChecks.StoresPrivatePlayableGrainAndReplays),
    ("classic FactionSlash privately supplies Duel and Barbarian Slash responses and replays", ClassicGeneralChecks.FormalFactionSlashResponseFlow),
    ("content registry rejects duplicate ids and bad references", ContentRegistryValidation),
    ("interactive setup exposes private deterministic general choices", InteractiveSetupPipeline),
    ("Opening customization: private roster and initial hand rerolls replay", OpeningCustomizationChecks.PrivateRosterAndOpeningHandReplay),
    ("Opening customization: public Lord then nonzero human and private AI", OpeningCustomizationChecks.NonLordSelectsAfterPublicLordBeforePrivateAi),
    ("Opening customization: nonzero team and national human before private AI", OpeningCustomizationChecks.TeamAndNationalHumanChooseBeforePrivateAi),
    ("identity dual generals preserve selection health privacy skills and replay", IdentityDualGeneralChecks.SelectionHealthPrivacyAndReplay),
    ("identity role general candidates freeze private choices and replay", IdentitySetupChecks.RoleCandidatesPrivacyAndReplay),
    ("identity general variants keep candidate families private and replay exact editions", IdentitySetupChecks.GeneralVariantsPrivacyAndReplay),
    ("identity draft preserves fixed fair private families restricted AI swaps and exact replay", WeightedGeneralDraftChecks.PrivacyDiversityAndReplay),
    ("identity draft AI matches public Lord defenses roles and faction cooperation", GeneralDraftAiChecks.MatchupsAndPrivacy),
    ("recorded general selection preserves scoring and strict fingerprints", RecordedGeneralSelectionChecks.RecordedScoringAndFingerprintsRemainStrict),
    ("identity draft AI prefers actual ordinary edition upgrades and safer Lords", GeneralDraftAiChecks.OrdinaryEditionStrengthAndLordSafety),
    ("identity draft AI evaluates ordinary roster rules without legacy edition priors", GeneralDraftAiChecks.OrdinaryRosterRuleCoverage),
    ("identity draft AI prices Lord benefit recipients costs and awakening", GeneralDraftAiChecks.LordBenefitRecipientsAndCosts),
    ("identity draft separates five and eight player exact edition selection rosters", IdentityGeneralPoolChecks.ModePoolsAndSelectionBoundaries),
    ("god rules deduplication keeps one roster entry and historical definitions", IdentityGeneralPoolChecks.EquivalentGodRulesUseOneRosterEntry),
    ("Mou Cao Pi song limits paid benefits and exact cold replay", MouCaoPiChecks.SongAndXingshangCostsBenefitsAndColdReplay),
    ("Mou Cao Pi Fangzhu actual target turn expiry and permanent Songwei", MouCaoPiChecks.FangzhuDurationAndPermanentSongweiSkills),
    ("Mou Cao Pi printed corpse inheritance and original skill loss", MouCaoPiChecks.InheritanceLosesOwnThreeSkillsAndKeepsNonLordPrintedSkills),
    ("identity draft keeps excluded editions in native avatar libraries and freezes eligibility", IdentityGeneralPoolChecks.ExcludedEditionsRemainRealAvatarsAndMetadataIsFrozen),
    ("Liu Yan: actual targets post-payment and effective hand replay", LiuYanChecks.ActualTargetsPostPaymentAndEffectiveHandCold),
    ("identity Rebel kill reward draws exactly three cards and replays once", IdentitySetupChecks.RebelKillDrawsThree),
    ("AI policy versions validate and replay deterministically", TacticalAiChecks.PolicyReplay),
    ("targeted tricks finish when the target spends its last card on Nullification", TargetLossChecks.LastNullification),
    ("hand guidance is private, read-only and agrees with legal actions", HandGuidanceChecks.ReadOnlyAndPrivate),
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
    ("Group counterspells: attacks only cancel current recipient and cold chain", GroupTargetNullificationChecks.AttacksOnlyCancelCurrentRecipient),
    ("Group counterspells: harvest preserves shared reveal and canceled pickers cold", GroupTargetNullificationChecks.HarvestKeepsSharedRevealAndSkipsOnlyCanceledPickers),
    ("Group counterspells: recovery only cancels current recipient cold", GroupTargetNullificationChecks.RecoveryOnlyCancelsCurrentRecipient),
    ("PeachGarden resolves a paused multi-target recovery", PeachGardenFlow),
    ("FiveGrains reveals public cards with private draft prompts", FiveGrainsFlow),
    ("target-card prompts expose opaque slots and replay without hidden identities", TargetCardChecks.OpaqueSlotFlow),
    ("target-card preselection survives native nullification and replay without second picker", TargetCardChecks.PreselectedSlotSurvivesNullificationAndReplay),
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
        Console.WriteLine(exception);
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
    Equal(34, registry.Cards.Count);
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
    Equal(CardKind.QizhengXiangsheng, registry.GetCard("mobile:qizheng-xiangsheng").LegacyKind);
    Equal(CardKind.DagongChe, registry.GetCard("anniversary:dagong-che-card").LegacyKind);
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
    TrueWithMessage(
        checkpoint.ContentPackages.SequenceEqual(["standard@" + StandardContentPackage.CurrentVersion]),
        "checkpoint records the content package signature");
    Equal(StandardContentRegistry.Create().ContentHash, checkpoint.ContentHash);

    var json = GameCheckpointJson.Serialize(checkpoint);
    TrueWithMessage(!JsonDocument.Parse(json).RootElement.TryGetProperty("RulesVersion", out _),
        "new checkpoints omit the removed rules epoch");
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
    var legacyJson = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
    legacyJson["RulesVersion"] = int.MaxValue;
    var legacyCheckpoint = GameCheckpointJson.Deserialize(legacyJson.ToJsonString());
    var legacyRestored = GameReplay.Restore(legacyCheckpoint, StandardContentRegistry.Create());
    Equal(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)),
        SnapshotJson.Serialize(legacyRestored.CreateSnapshot(0, revealAll: true)));
    TrueWithMessage(original.Events.Select(EventSignature).SequenceEqual(legacyRestored.Events.Select(EventSignature)) &&
        !JsonDocument.Parse(GameCheckpointJson.Serialize(legacyCheckpoint)).RootElement.TryGetProperty("RulesVersion", out _),
        "legacy JSON fields are ignored on read and omitted on write without changing replay");

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
                 (response.ResolutionId, response.EffectCardId, response.WindowFrameId)))
    {
        var ordered = responseGroup.OrderBy(response => response.ChainDepth).ToArray();
        var resolved = resolvedEffects.Single(effect =>
            effect.ResolutionId == responseGroup.Key.ResolutionId &&
            effect.EffectCardId == responseGroup.Key.EffectCardId && effect.WindowFrameId == responseGroup.Key.WindowFrameId);
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
    firstStep = ResolveNullificationWindowForTest(gameWithGarden, firstStep);
    if (gameWithGarden.ResolutionStack.OfType<CardUseFrame>().Single().TargetIndex == 0)
        firstStep = gameWithGarden.DriveAdvanceOneStep();
    Equal(EngineStatus.Running, firstStep.Status);
    TrueWithMessage(
        gameWithGarden.ResolutionStack.OfType<CardUseFrame>().Single().TargetIndex == 1,
        "PeachGarden advances one target per recovery step after that target's counterspell window");
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
        ResolveNullificationWindowForTest(gameWithGarden);
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
