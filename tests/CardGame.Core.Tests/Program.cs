using System.Text.Json;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

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

// Existing card-effect fixtures retain automatic discards to isolate their scenarios.
// Default/manual discard validation and complete-match coverage live in ManualDiscardChecks.
var tests = new (string Name, Action Body)[]
{
    ("shared post-event recipient batch counts and reason filters", SharedPostEventChecks.TransferFiltersAndBatchCountsUseTheRecipient),
    ("shared post-event damage separation and group recovery", SharedPostEventChecks.DamageDoesNotBecomeHpLossAndGroupRecoveryWaitsPerTarget),
    ("shared post-event gained cards pause and replay", SharedPostEventChecks.GainsPauseBeforeTheNextInstructionAndReplay),
    ("shared post-event dying recovery ordering and replay", SharedPostEventChecks.HpLossWaitsForDyingAndCardRecoveryFinishesFirst),
    ("shared post-event definition context validation", SharedPostEventChecks.DefinitionFiltersRejectWrongContexts),
    ("shared use lifecycle nests rescue inside a suspended card window", CardUseLifecycleChecks.NestedRescueRetainsTheOuterUseWindow),
    ("shared card-use phase-owner movement context validation", CardUseLifecycleChecks.PhaseOwnerDestinationRequiresPhaseContext),
    ("current classic catalogue modes and skill references", CurrentClassicContentChecks.CatalogueAndModes),
    ("turn-ending Xiaoguo Tianxiang game over stops later observers", TurnEndingGameOverChecks.XiaoguoTianxiangVictoryStopsLaterObservers),
    ("2014 boundary Zhou Yu definition version and public gift resource contracts", BoundaryZhouYuChecks.DefinitionAndResourceContracts),
    ("2014 boundary Zhou Yu Yingzi Fanjian transfer choice and replay", BoundaryZhouYuChecks.TransferChoiceAndReplay),
    ("2017 Cao Ang definition and observer distance schema", CaoAngChecks.DefinitionAndObserverDistanceSchema),
    ("2017 Cao Ang nearby gift reveals and recipient may equip with replay", CaoAngChecks.NearbyTargetGiftRevealsAndRecipientMayEquip),
    ("response equipment and Chanyuan keep temporary suppression distinct", ResponseAndSkillSuppressionChecks.DefinitionsAndChanyuanRestoresSkills),
    ("Zhang Xiu Xiongluan abolishes areas and blocks hand without armor bypass", ResponseAndSkillSuppressionChecks.XiongluanBlocksHandButDoesNotIgnoreArmor),
    ("Xingtian Axe pays two cards then blocks hand and armor", ResponseAndSkillSuppressionChecks.XingtianPaysTwoAndBlocksOnlyHandCards),
    ("Cai Wenji Duanchang permanently removes killer skills", ResponseAndSkillSuppressionChecks.DuanchangPermanentlyRemovesKillersSkills),
    ("red Slash and Scarlet Blood Sword gate response before damage", ResponseAndSkillSuppressionChecks.SlashResponseRestrictionsRespectWeaponAndSuit),
    ("2026 Shen Sima Yi definition and kill-window schema", ShenSimaYiChecks.DefinitionAndKillWindowSchema),
    ("2026 Shen Sima Yi kill grants exactly one extra turn with replay", ShenSimaYiChecks.KillGrantsExactlyOneExtraTurnAndReplays),
    ("2010 Cao Pi definition and trigger schema", CaoPiChecks.DefinitionAndTriggerSchema),
    ("2010 Cao Pi Xingshang claims died player cards and replays", CaoPiChecks.XingShangClaimsDiedPlayerCardsAndReplays),
    ("2011 Sun Ce definition and trigger schema", SunCeChecks.DefinitionAndTriggerSchema),
    ("2011 Sun Ce Jiang draws when using a duel and replays", SunCeChecks.JiangDrawsWhenUsingDuelAndReplays),
    ("2014 Jie Zhao Yun definition and trigger schema", BoundaryZhaoYunChecks.DefinitionAndTriggerSchema),
    ("2011 Shen Zhao Yun definition and trigger schema", ShenZhaoYunChecks.DefinitionAndTriggerSchema),
    ("2011 Shen Zhao Yun Juejing skips draw refills and caps at four", ShenZhaoYunChecks.JuejingSkipsDrawRefillsAndCapsAtFour),
    ("Gundam One beam rifle discards one card and deals damage once per turn", GaoDaYiHaoChecks.BeamRifleDiscardsOneAndDamagesOncePerTurn),
    ("2014 Jie Zhao Yun Yajiao mismatch discards from ranged player and replays", BoundaryZhaoYunChecks.YajiaoMismatchDiscardsFromRangedPlayerAndReplays),
    ("2011 Cai Wenji definition and trigger schema", CaiWenJiChecks.DefinitionAndTriggerSchema),
    ("2011 Cai Wenji Beige resolves one branch per judgment and replays", CaiWenJiChecks.BeigeResolvesOneBranchPerJudgmentAndReplays),
    ("2011 Jiang Wei definition and trigger schema", JiangWeiChecks.DefinitionAndTriggerSchema),
    ("2011 Jiang Wei Tiaoxin forces a Slash at the owner and replays", JiangWeiChecks.TiaoxinForcesSlashAgainstOwnerAndReplays),
    ("2011 Lu Su definition and trigger schema", LuSuChecks.DefinitionAndTriggerSchema),
    ("2011 Lu Su Haoshi gives half hand after extra draw and replays", LuSuChecks.HaoshiGivesHalfHandAfterExtraDrawAndReplays),
    ("Classic Deng Ai definition and trigger schema", DengAiChecks.DefinitionAndTriggerSchema),
    ("Classic Deng Ai Tuntian stores fields, reduces distance and replays", DengAiChecks.TuntianStoresFieldsReducesDistanceAndReplays),
    ("Classic Sha Mo Ke definition and trigger schema", ShaMoKeChecks.DefinitionAndTriggerSchema),
    ("Classic Sha Mo Ke Jili draws on the first use only and replays", ShaMoKeChecks.FirstUseDrawsOnceSecondUseDoesNotAndReplays),
    ("Classic Zhang He definition and trigger schema", ZhangHeChecks.DefinitionAndTriggerSchema),
    ("Classic Zhang He skip-discard branch keeps hand cards and replays", ZhangHeChecks.SkipDiscardBranchKeepsHandCardsAndReplays),
    ("Classic Zhang Zhao Zhang Hong definition and trigger schema", ZhangZhaoZhangHongChecks.DefinitionAndTriggerSchema),
    ("Classic Zhang Zhao Zhang Hong Zhijian equips a free slot and replays", ZhangZhaoZhangHongChecks.ZhijianEquipsFreeSlotAndDrawsAndReplays),
    ("Classic Zhang Zhao Zhang Hong Guzheng returns one card and takes the rest", ZhangZhaoZhangHongChecks.GuzhengReturnsOneAndTakesRestAndReplays),

    ("2013 Pan Zhang Ma Zhong definition and generic schema 56", PanZhangMaZhongChecks.DefinitionAndGenericSchema),
    ("2013 Pan Zhang Ma Zhong natural far Slash and replay", PanZhangMaZhongChecks.NaturalSlashReverseRangeAndReplay),
    ("2014 boundary Xu Chu registers and validates general card filter", BoundaryXuChuChecks.DefinitionAndGeneralFilterContract),
    ("Xu Sheng Pojun holds target cards and returns them at turn end", XuShengChecks.ClassicPojunHoldsAndReturnsAtTurnEnd),
    ("Zhang Song equipment use, replacement and replay", ZhangSongChecks.EquipmentUsesReplaceAndResumeExactlyOnce),
    ("2014 boundary Xu Chu damage scope duration and natural Slash replay", BoundaryXuChuChecks.DamageScopeAndDuration),
    ("SP Le Jin schema-56 content and version boundary", SpLeJinChecks.DefinitionAndSchemaBoundary),
    ("SP Le Jin Xiaoguo pays basic and replays target choice", SpLeJinChecks.HumanOwnerPaysBasicAndReplaysDamage),
    ("classic Zhu Zhi schema-55 content and coverage resource contracts", ZhuZhiChecks.DefinitionAndResourceContracts),
    ("classic Zhu Zhi Anguo returns weapon and replays", ZhuZhiChecks.AnguoReturnsWeaponAndReplays),
    ("2014 boundary Gan Ning definition and reusable schema 55", BoundaryGanNingChecks.DefinitionAndReusableSchema),
    ("2014 boundary Gan Ning Fenwei assault subset and replay", BoundaryGanNingChecks.MultiTargetAssaultSubsetAndReplay),
    ("boundary Cao Cao Duel damage claims physical card and replays", BoundaryCaoCaoIntegrationChecks.DuelDamageClaimsPhysicalCardAndReplays),
    ("classic Zhu Huan schema-54 bound kinds and formal content", ZhuHuanChecks.DefinitionAndReusableKindCondition),
    ("classic Zhu Huan Youdi distinguishes all Slash kinds and transfers non-Slash with replay", ZhuHuanChecks.SlashVariantsStopReturnAndNonSlashTransfersWithReplay),
    ("2014 boundary Cao Cao content and claimable condition schema gates", BoundaryCaoCaoChecks.ContentAndSchemaBoundary),
    ("2014 boundary Cao Cao physical damage chooses draw or claim and replays", BoundaryCaoCaoChecks.PhysicalDamageDrawClaimAndReplay),
    ("2018 boundary Zhang Liao definition and schema 54 boundary", BoundaryZhangLiaoChecks.DefinitionAndSchemaBoundary),
    ("2018 boundary Zhang Liao dynamic draw plan selection and replay", BoundaryZhangLiaoChecks.DrawPlanSelectionAndReplay),
    ("boundary Sima Yi Feedback takes source cards per damage point", BoundarySimaYiChecks.FeedbackPerPointAndSourceZones),
    ("2019 boundary Guo Jia Yiji gives zero one or two current hand cards and replays", BoundaryGuoJiaChecks.YijiGivesZeroOneOrTwoCurrentHandCardsAndReplays),
    ("boundary Lijian awaits Xiaoji equipment loss before its virtual Duel and replays", BoundaryDiaoChanChecks.LijianEquipmentLossTriggerPrecedesDuel),
    ("classic Li Dian Wangxi taken damage gives private replayable choice", LiDianChecks.WangxiTakenDamageOffersPrivateChoice),
    ("rescued lethal damage offers Wangxi only after dying and replays", DamageAfterDyingChecks.RescuedLethalDamageOffersWangxiAfterRescue),
    ("phase exchange definitions and current boundary", ProgramActivationLimitChecks.DefinitionsAndCurrentBoundary),
    ("phase exchange mixed zones atomicity and replay", ProgramActivationLimitChecks.MixedZonesAtomicityAndReplay),
    ("phase exchange extra phase and large selection", ProgramActivationLimitChecks.ExtraPhaseAndLargeSelection),
    ("phase exchange equipment loss trigger and replay", ProgramActivationLimitChecks.EquipmentLossTriggerAndReplay),
    ("owned-card set definitions and public AI", ProgramOwnedCardsChecks.DefinitionsAndPublicAi),
    ("owned-card set private draft movement and replay", ProgramOwnedCardsChecks.PrivateDraftBatchMovementAndReplay),
    ("owned-card set shortfall empty sources and invalidation", ProgramOwnedCardsChecks.ShortfallEmptyAndInvalidatedDraft),
    ("Program choice definitions validate choices, payments and presentation hashes", ProgramChoiceChecks.DefinitionsValidateChoicesPaymentsAndPresentationHash),
    ("Program choice selected target conditional effects and replay", ProgramChoiceChecks.SelectedTargetChoosesConditionalEffectsAndReplays),
    ("Program choice AI predicts one branch from public state", ProgramChoiceAiChecks.PublicStatePredictsExactlyOneOption),
    ("Program choice revalidates options and exact skill instance", ProgramChoiceChecks.RevalidatesChoiceAndExactInstanceBeforeResolving),
    ("composition kernel descriptor contracts", ProgramCompositionDefinitionChecks.CatalogDiscoversCompleteOperations),
    ("shared use lifecycle preserves dying rescue and replay", CardUseLifecycleChecks.DyingBasicUsesResumeTheirRescueParent),
    ("shared use lifecycle covers basic equipment trick and multiple targets", CardUseLifecycleChecks.BasicEquipmentAndTricksShareReplayableUseWindows),
    ("completed Slash program window follows finished card use and replays", CardUseCompletedChecks.FinishedSlashOpensReplayableProgramWindow),
    ("card-action judgment windows resume and replay", ProgramCardJudgmentWindowChecks.AllCardActionWindowsStartPublicJudgmentsAndReplay),
    ("completed Slash freezes actual damage for conditional programs", CardUseCompletedChecks.CompletedUseFreezesActualDamageFact),
    ("completed Slash filters frozen conversion provenance", CardUseCompletedChecks.CompletedUseFiltersFrozenConversionSource),
    ("configured Slash converts to Fire Slash without Zhuque Fan", CardUseCompletedChecks.ConfiguredSlashCanBecomeFireSlashWithoutZhuqueFan),
    ("composition kernel current formal content", ProgramCompositionDefinitionChecks.CurrentExecutableContentHasOneExecutionPlan),
    ("composition AI activates target benefits in a real match", ProgramCompositionAiIntegrationChecks.JiemingAiActivatesAndDrawsForFriendlyTarget),
    ("composition kernel resource graph safety", ProgramCompositionDefinitionChecks.ResourceGraphsRejectAliasingLeaksAndMissingInputs),
    ("composition kernel malformed nodes", ProgramCompositionDefinitionChecks.MalformedNodesFailBeforeExecution),
    ("composition kernel cross-entry reveal replay", ProgramCompositionEntryChecks.CrossEntryRevealSubsetReplays),
    ("composition kernel boundary capabilities", ProgramCompositionContextChecks.SharedWindowsAcceptCommonNodesAndRejectMissingContexts),
    ("composition kernel target-set consumption", ProgramCompositionContextChecks.TargetSetIsConsumedOnce),
    ("composition kernel active state and judgment replay", ProgramCompositionContextChecks.ActiveStateAndJudgmentReplay),
    ("composition kernel active turn policy replay and expiry", ProgramCompositionContextChecks.ActiveTurnRuleModifierGrantsReplaysAndExpires),
    ("execution plans freeze instructions and reject ambiguous bindings", ProgramExecutionPlanChecks.PlansFreezeInstructionsAndRejectAmbiguousBindings),
    ("rule query reduction is input-order independent", RuleQueryReducerChecks.IsIndependentOfInputOrderAndRejectsOnlyWinningSetConflicts),
    ("rule query reduction keeps unlimited separate", RuleQueryReducerChecks.KeepsUnlimitedSeparateAndStillValidatesFiniteConflicts),
    ("rule query reduction rejects invalid mutable inputs", RuleQueryReducerChecks.RejectsInvalidInputsAndFreezesOutput),
    ("rule query distance runs both directions before clamping", RuleQueryReducerChecks.DirectionalDistanceRunsBothStagesBeforeClamping),
    ("rule query programs deduplicate instances and evaluate dynamic values", RuleQueryReducerChecks.ProgramContributionsUseInstanceIdentityAndDynamicValues),
    ("rule query registry rejects future Set conflicts", RuleQueryReducerChecks.StaticSetConflictsAreRejectedBeforePlay),
    ("rule query engine tracks dynamic grants and program instances", RuleQueryIntegrationChecks.EngineTracksDynamicSourcesAndInstanceIdentity),
    ("character skills retain separate sources and stable ownership", CharacterSkillSetChecks.GrantsRetainSourcesAndStableOwnership),
    ("character skills reject conflicting grants without partial state", CharacterSkillSetChecks.InvalidGrantsAndConflictsLeaveStateUnchanged),
    ("character templates supply defaults without owning current state", CharacterSkillSetChecks.CharacterTemplatesSupplyDefaultsWithoutOwningCurrentState),
    ("match skill binding sources preserve instance and unique-program semantics", MatchSkillBindingIndexChecks.SourcesDeduplicateInstancesAndUniqueProgramBuckets),
    ("match skill binding stamps rebuild only the changed seat", MatchSkillBindingIndexChecks.OnlyBindingStampChangesRebuildOneSeatAndOldShardsStayFrozen),
    ("skill executor composes primitive handlers with conditions and committed cursors", SkillProgramExecutorChecks.ExecutesComposedEffectsConditionsAndCursorOrder),
    ("skill executor resumes child resolution without repeating paid effects", SkillProgramExecutorChecks.SuspendedChildResumesWithoutRepeatingPaidEffect),
    ("skill executor cancels remaining effects after an invalid selected cost", SkillProgramExecutorChecks.InvalidSelectedCostCancelsRemainingEffectsAtomically),
    ("skill executor rejects changed programs and missing or duplicate handlers", SkillProgramExecutorChecks.RejectsChangedProgramsUnknownHandlersAndDuplicates),
    ("active Program contracts reject invalid selections and preserve target order", SkillProgramExecutorChecks.ActiveActivationContractsRejectInvalidDefinitionsAndPreserveOrder),
    ("card subset selector enumerates every legal subset once", CardSubsetSelectorChecks.EnumeratesEveryLegalSubsetExactlyOnce),
    ("card subset selector keeps impossible and empty constraints explicit", CardSubsetSelectorChecks.ImpossibleAndEmptySelectionDoNotInventChoices),
    ("card subset selector rejects oversized sources and freezes choices", CardSubsetSelectorChecks.RejectsOversizedOrAmbiguousSourcesAndFreezesChoices),
    ("lifecycle programs reject unsupported event usage scope", ProgramLifecycleChecks.RejectsUnsupportedEventUsageScope),
    ("lifecycle programs reject live card bindings across inserted phases", ProgramLifecycleChecks.RejectsCardBindingsAcrossDetachedPhaseBoundary),
    ("lifecycle programs cancel missing conditional bindings without leaks", ProgramLifecycleChecks.MissingConditionalBindingsCancelWithoutLeakingCards),
    ("cancelled lifecycle programs preserve parent attack cards", ProgramLifecycleChecks.CancelledDamageProgramsDoNotCleanupParentAttackCards),
    ("card movement schema and classic content preserve their version boundary", CardMovementProgramChecks.DefinitionsAndVersionBoundary),
    ("card movement programs compose atomic per-card and per-batch triggers with replay", CardMovementProgramChecks.AtomicBatchRunsPerCardAndPerBatchAndReplays),
    ("nested card movement batches retain immediate parent identity", CardMovementProgramChecks.NestedBatchesRetainImmediateParentIdentity),
    ("damage programs preserve schema and package version boundaries", DamageProgramChecks.DefinitionsAndVersionBoundaries),
    ("damage programs claim select gift draw and replay through generic choices", DamageProgramChecks.GenericDamageChoicesClaimSelectGiftDrawAndReplay),
    ("draw-phase programs preserve schema and package version boundaries", DrawPhaseProgramChecks.DefinitionsAndVersionBoundaries),
    ("classic Yingzi uses a generic optional choice and replays", DrawPhaseProgramChecks.ClassicOptionalChoiceSkipsActivatesAndReplays),
    ("schema-19 additive draw adjustments, explicit draws and damage grants compose and replay", DrawPhaseCompositionChecks.AdditiveAdjustmentDrawAndDamageGrantComposeAndReplay),
    ("schema-19 reveal partitions, recovery and explicit draws conserve cards and replay", DrawPhaseCompositionChecks.RevealPartitionRecoveryAndExtraDrawConserveCardsAndReplay),
    ("schema-19 target-hand replacement composes with an explicit draw and replays", DrawPhaseCompositionChecks.TargetHandReplacementAndExplicitDrawPauseAndReplay),
    ("schema-19 draw-phase validation rejects unsafe data-flow graphs", DrawPhaseCompositionChecks.ValidatorRejectsUnsafeGraphs),
    ("schema-20 draw policies preserve grouped definitions and active-operation boundaries", DrawPolicyProgramChecks.DefinitionsGroupsAndActivationBoundary),
    ("schema-20 turn policies are typed idempotent and expire together", DrawPolicyProgramChecks.TurnPoliciesAreTypedIdempotentAndExpireTogether),
    ("schema-21 judgment draw programs preserve definitions and package boundary", JudgmentDrawProgramChecks.DefinitionsAndVersionBoundary),
    ("schema-21 judgment results bind turn conversion and replay", JudgmentDrawProgramChecks.JudgmentBindingConversionAndReplay),
    ("schema-22 self-dying state programs preserve definitions and package boundary", SelfDyingStateProgramChecks.DefinitionsAndVersionBoundary),
    ("schema-22 Niepan clears owned state and replays", SelfDyingStateProgramChecks.ClearsOwnedStateAndReplays),
    ("structured skill metadata normalizes explicitly and fingerprints content", SkillMetadataChecks.TagsNormalizeAndFingerprint),
    ("skill runtime usage and conversion states reset by declared scope", SkillMetadataChecks.RuntimeUsageAndReset),
    ("program Niepan usage restores its instance-scoped game record", SkillMetadataChecks.StructuredNiepanUsageReplays),
    ("printed Lord skills follow identity and rules 96 replay ownership boundaries", SkillOwnershipChecks.PrintedLordSkillsFollowIdentityAndReplayBoundary),
    ("skill program v2 trigger definitions validate and freeze", SkillProgramTriggerDefinitionChecks.Run),
    ("skill program card triggers require exact conversion sources and replay", SkillProgramCardTriggerChecks.Run),
    ("skill program multi-target triggers wait for every Liuli redirection", SkillProgramTargetOrderChecks.Run),
    ("skill program v3 final judgment definitions validate and freeze", SkillProgramJudgmentTriggerChecks.Definitions),
    ("skill program final judgment triggers resume recovery, drawing and replay", SkillProgramJudgmentTriggerChecks.WindowAndReplay),
    ("skill program v4 judgment replacement definitions validate and freeze", SkillProgramJudgmentReplacementChecks.Definitions),
    ("skill program judgment replacement commits both old-card destinations and replays", SkillProgramJudgmentReplacementChecks.WindowDestinationsAndReplay),
    ("skill program v5 judgment target and damage definitions validate and freeze", SkillProgramJudgmentDamageChecks.Definitions),
    ("skill program judgment damage resumes through dying, death and replay", SkillProgramJudgmentDamageChecks.TargetDamageDyingAndReplay),
    ("skill program v6 direct card judgments validate and freeze", SkillProgramStartedJudgmentChecks.Definitions),
    ("skill program direct Dodge and Lightning judgments resume and replay", SkillProgramStartedJudgmentChecks.DirectDodgeAndLightningReplay),
    ("skill program v7 cross-owner contributions validate and freeze", SkillProgramContributionChecks.Definitions),
    ("skill program contributions bind owner filters, phase ledger and replay", SkillProgramContributionChecks.CrossOwnerFiltersLedgerAndReplay),
    ("skill program v8 selected judgment subjects validate and freeze", SkillProgramSelectedJudgmentChecks.Definitions),
    ("skill program selected judgment subjects damage and replay", SkillProgramSelectedJudgmentChecks.SelectedSubjectDamageAndReplay),
    ("skill program v10 card identities and action modifiers validate and freeze", SkillProgramCardIdentityChecks.Definitions),
    ("schema-46 viewAs source zones validate and isolate hand from equipment", SkillProgramViewAsZoneChecks.DefinitionAndZoneIsolation),
    ("mandatory card identity suppresses native use, ignores Slash distance and replays", SkillProgramCardIdentityChecks.MandatoryIdentityDistanceAndReplay),
    ("turn card-use effects union order consume and expire by action semantics", CardUseModuleEffectChecks.TurnStateUsesActionSemanticsStableOrderAndExpiration),
    ("active programs suspend into Pindian and branch from the frozen result", PindianModuleChecks.ActiveProgramPindianSuspendsConditionsAndReplays),
    ("god generals choose a private effective faction before reveal and replay", GodFactionSelectionChecks.PromptPrivacyEffectiveFactionAndReplay),
    ("formal Wuhun completes a real-deck direct-death chain and replays", ClassicShenGuanYuChecks.WuhunRealDeckDeathChainAndReplay),
    ("schema-25 awakening state primitives and frozen conditions validate", SpGuanYuChecks.ProgramPrimitivesAndConditionsValidate),
    ("formal Juzhan uses per-target turn and per-card-use event ledgers", YanYanChecks.YangYinLedgerAndReplay),
    ("formal Hengye grows on damage and resets after a kill", MouLuMengChecks.HengyeGrowthAndKillReset),
    ("Jiangchi extra draw blocks Slash use and play", CaoZhangChecks.DrawMoreBlocksSlashUseAndResponse),
    ("Qianxi red restriction filters same-color hand responses", MaDaiChecks.RedRestrictionFiltersHandResponsesAndReplays),
    ("winning Xianzhen scopes distance count and armor to one target", GaoShunChecks.XianzhenWinTargetsDistanceCountArmorAndReplays),
    ("Zishou restricts card targets while Zongshi follows living factions", LiuBiaoChecks.ZishouTargetsAndZongshiHandLimit),
    ("public Zhenlie nullifies Slash while Miji draws and distributes exactly", WangYiChecks.ZhenlieSlashAndMijiDistributionReplay),
    ("Quanji awakens Zili and acquired Paiyi replays", ZhongHuiChecks.ZiliAndPaiyiReplay),
    ("Qice converts every hand card once and replays", XunYouChecks.QiceUsesAllHandCardsAndReplays),
    ("Dangxian runs a pre-draw Play phase with fresh phase limits", LiaoHuaChecks.DangxianExtraPhaseResetsPhaseLimitsAndReplays),
    ("Fuhun damage grants Wusheng and Paoxiao for one turn", GuanXingZhangBaoChecks.ActiveSlashGrantsParentSkillsForOneTurnAndReplays),
    ("formal Bu Lian Shi versions active Anxu and optional Zhuiyi", BuLianShiChecks.ContentAndRulesBoundary),
    ("Anxu lets the lower-hand receiver choose an opaque card and applies effective suit", BuLianShiChecks.AnxuUsesOpaqueReceiverChoiceAndEffectiveSuit),
    ("configured Lihuo penalty enters dying and replays", ChengPuLihuoChecks.CompletedPenaltyCanEnterDyingAndReplay),
    ("Jiefan freezes attackers consumes its limited use and replays", HanDangChecks.JiefanFreezesRespondersConsumesLimitedUseAndReplays),
    ("Renxin discards equipment turns over prevents damage and replays", CaoChongChecks.RenxinDiscardsEquipmentTurnsOverPreventsAndReplays),
    ("Bingyi reveals then shares to self and multiple targets with replay", GuYongChecks.BingyiRevealsThenSharesAndReplays),
    ("Jingce counts turn card uses draws two cards and replays", GuoHuaiChecks.JingceCountsTurnUsesDrawsAndReplays),
    ("turn-ending boundary orders Jushou Jujian Biyue and replays", TurnEndingBoundaryChecks.OrdersJushouJujianBiyueAndReplays),
    ("Junxing enforces exact card categories and replays both target branches", ManChongChecks.JunxingUsesExactCategoriesAndReplaysBothBranches),
    ("Longyin privately answers another character's Play-phase Slash and replays", GuanPingChecks.OtherCharactersSlashOffersPrivateChoiceAndReplays),
    ("formal SP Zhao Yun triggers Chongzhen after a configured Longdan Slash", SpZhaoYunChecks.ConvertedSlashUseTriggersChongzhenAndReplays),
    ("formal classic Zhang Jiao resolves configured Leiji and Guidao with replay", ClassicZhangJiaoProgramChecks.LeijiGuidaoAndReplay),
    ("formal boundary Zhang Jiao resolves current Leiji and Guidao with replay", BoundaryZhangJiaoProgramChecks.DodgeLeijiGuidaoAndReplay),
    ("formal national Zhang Jiao resolves Spade-only Leiji and owner-hand Guidao", NationalZhangJiaoProgramChecks.LeijiGuidaoAndReplay),
    ("public Nightmare markers count each Wuhun damage point before dying and replay", PublicMarkerChecks.WuhunDamageOrderAndReplay),
    ("Wuhun death judgment directly kills without a dying window and replays", PublicMarkerChecks.WuhunDeathJudgmentAndReplay),
    ("composed AI matches complete and replay with configured actions and responses", SkillProgramMatchChecks.ComposedMatchesCompleteAndReplay),
    ("Wusheng responds to Duel and Barbarian Assault with exact physical costs and replay", WushengResponseChecks.CommandsAndReplay),
    ("Wusheng response conversion respects national reveal slots and requested card kinds", WushengResponseChecks.NationalAndScope),
    ("dual-general health previews, setup and legacy replay agree", NationalHealthChecks.SetupAndReplay),
    ("national dual-general setup stays private and each reveal replays", NationalWarChecks.PrivateSetupAndReveal),
    ("national reveal enables every skill in a general's collection", NationalWarChecks.MultiSkillRevealAndLegacy),
    ("national public-evidence checkpoints replay AI knowledge at a paused prompt", NationalWarChecks.PublicEvidenceCheckpointReplay),
    ("recast is an independent validated card movement and replays exactly", RecastChecks.CommandAndReplay),
    ("claimed group cards continue after the claimant dies and replay exactly", GroupClaimChecks.ClaimantDeathContinues),
    ("standard setup has the 1/2/4/1 identity distribution", IdentityDistribution),
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
    ("paced human commands checkpoint and replay a full game", PacedCommandChecks.FullPacedReplay),
    ("manual discard rounds finish across identities and table sizes", ManualDiscardChecks.MatchMatrix),
    ("implemented card content is registered and described", CardCatalogDefinitions),
    ("standard deck content is deterministic and balanced", StandardDeckContent),
    ("standard package builds an immutable isolated registry", StandardContentRegistryBuilds),
    ("skill program loader separates canonical gameplay from presentation", SkillProgramChecks.LoaderCanonicalizationAndPresentationIsolation),
    ("skill program loader rejects malformed and unsupported definitions", SkillProgramChecks.LoaderRejectsMalformedUnsupportedDefinitions),
    ("configured active sequences reject forged input and replay exact moves", SkillProgramChecks.ConfiguredActiveSequenceIsAtomicAndReplayable),
    ("program gameplay hashes control checkpoint compatibility independently of presentation", SkillProgramChecks.ProgramHashControlsCheckpointCompatibility),
    ("program dying checkpoints cancel a selected card spent on rescue", SkillProgramChecks.ProgramPausedDyingAndConsumedSelection),
    ("physical deck recipes preserve exact suit, rank and content hashing", PhysicalDeckRecipeChecks.ExactSuitRankValidationAndHashing),
    ("classic identity applies base HP, multiple skills and legacy replay boundaries", ClassicGeneralChecks.SetupHealthAndReplay),
    ("current classic Guan Yu uses configured Wusheng from hand and equipment", ClassicGeneralChecks.ConfiguredWushengSources),
    ("classic Borrowed Sword transfers weapons or nests a real Slash and replays", BorrowedSwordChecks.TransferSlashAndReplay),
    ("classic Zhangba converts exactly two hand cards into one replayable Slash", ZhangbaChecks.ActiveUseAndReplay),
    ("classic Cixiong stages private opposite-gender choices and replays", CixiongDoubleSwordsChecks.StagedChoiceAndReplay),
    ("classic Pang De discards an opaque hand card or public equipment through Mengjin", MengjinChecks.HiddenHandPublicEquipmentChoiceAndReplay),
    ("classic Xun Yu resolves private Pindian and attributed Quhu damage", QuhuChecks.PindianWinLossDamageAndReplay),
    ("classic Wolong converts Huoji and Kanpo and provides virtual Bazhen", WolongChecks.ConversionsBazhenAndReplay),
    ("classic Pang Tong converts Lianhuan for recast and use", PangTongChecks.LianhuanNiepanAndReplay),
    ("classic Taishi Ci resolves Tianyi win and loss Slash rules", TianyiChecks.WinLossSlashRulesAndReplay),
    ("classic Cao Ren draws and skips a flipped turn through Jushou", CaoRenChecks.JushouDrawFlipSkipAndReplay),
    ("classic Xiao Qiao converts Spades and transfers damage through Tianxiang", XiaoQiaoChecks.HongyanTianxiangTransferAndReplay),
    ("classic Zhou Tai survives with public unique-rank Buqu wounds", ZhouTaiChecks.BuquWoundsHandLimitAndReplay),
    ("classic Yuan Shao converts same-suit Luanji and expands Xueyi hand limit", YuanShaoChecks.LuanjiAndXueyiReplay),
    ("classic Xiahou Yuan skips phases for virtual no-distance Shensu Slash", XiahouYuanChecks.ShensuPhaseSkipsAndReplay),
    ("classic Hua Xiong rewards red Slash sources through locked Yaowu", HuaXiongChecks.RedSlashBenefitAndReplay),
    ("classic Gongsun Zan switches Yicong's outgoing and incoming distance", GongsunZanChecks.YicongDistanceAndReplay),
    ("classic Sun Jian draws and discards exact cards through Yinghun", SunJianChecks.YinghunChoiceAndReplay),
    ("classic Meng Huo redirects Barbarian Assault and replaces drawing through Zaiqi", MengHuoChecks.HuoshouAndZaiqiReplay),
    ("winning Leiji damage cleans its parent Slash", ZhuRongChecks.LeijiWinningDamageCleansParentSlash),
    ("classic Yu Jin nullifies black Slash through locked Yizhong", YuJinChecks.YizhongBlackSlashAndReplay),
    ("classic Xu Shu prevents trick damage through locked Wuyan", WuyanChecks.PreventsTrickDamageWithVersionBoundary),
    ("classic Xu Shu discards a non-basic card for all Jujian benefits", XuShuChecks.JujianBenefitsAndReplay),
    ("classic Fangtian Halberd resolves exact last-hand Slash targets and replays", FangtianHalberdChecks.LastHandTargetsResolveSequentiallyAndReplay),
    ("classic Guding Blade increases direct Slash damage against empty hands", GudingBladeChecks.EmptyHandDamageAndLegacyBoundary),
    ("classic Zhuque Fan converts ordinary Slash to Fire Slash and preserves owner choice", ZhuqueFanChecks.FireConversionChainFactionSlashAndLegacyBoundary),
    ("classic Tengjia makes ordinary Slash ineffective before response", TengjiaChecks.OrdinarySlashImmunityAndLegacyBoundary),
    ("classic Silver Lion caps damage and recovers after leaving equipment", SilverLionChecks.DamageCapRemovalRecoveryAndLegacyBoundary),
    ("classic Wooden Ox stores private playable grain and replays", WoodenOxChecks.StoresPrivatePlayableGrainAndReplays),
    ("classic FactionSlash privately supplies Duel and Barbarian Slash responses and replays", ClassicGeneralChecks.FormalFactionSlashResponseFlow),
    ("public-team winner rules distinguish living teams", TeamModeChecks.WinningTeamRules),
    ("standard active programs use typed selections and deterministic replay", ActiveSkillChecks.KujinFlow),
    ("Mashu changes public distance and distance-gated legal actions", DistanceSkillChecks.MashuDistanceAndLegality),
    ("content registry rejects duplicate ids and bad references", ContentRegistryValidation),
    ("interactive setup exposes private deterministic general choices", InteractiveSetupPipeline),
    ("five-player identity mode reuses the shared engine", FivePlayerIdentityMode),
    ("AI policy versions validate and replay deterministically", TacticalAiChecks.PolicyReplay),
    ("targeted tricks finish when the target spends its last card on Nullification", TargetLossChecks.LastNullification),
    ("hand guidance is private, read-only and agrees with legal actions", HandGuidanceChecks.ReadOnlyAndPrivate),
    ("formal Kongcheng rejects empty-hand Duel targets atomically", KongchengChecks.DuelTargeting),
    ("damage trigger candidates use a stable ordering", DamageTriggerOrdering),
    ("AI suspicion changes only from public actions", AiPublicEvidence),
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
    ("Nullification resolves a private multi-layer trick window", NullificationFlow),
    ("IronChain toggles exact targets and propagates elemental damage", IronChainFlow),
    ("BarbarianAssault resolves each target through private Slash windows", BarbarianAssaultFlow),
    ("PeachGarden resolves a paused multi-target recovery", PeachGardenFlow),
    ("FiveGrains reveals public cards with private draft prompts", FiveGrainsFlow),
    ("Dismantlement discards a hidden target card deterministically", DismantlementFlow),
    ("Snatch transfers a hidden target card across a distance-one edge", SnatchFlow),
    ("target-card prompts expose opaque slots and replay without hidden identities", TargetCardChecks.OpaqueSlotFlow),
    ("Dismantlement and Snatch can target public equipment and judgment cards", PublicTargetCardFlow),
    ("equipment replaces slots and versions formal weapon ranges", EquipmentFlow),
    ("Bagua uses a deterministic public judgment to defend against Slash", BaguaJudgmentFlow),
    ("Qinggang bypasses Bagua armor in a typed Slash resolution", QinggangBypassesBagua),
    ("Renwang Shield nullifies black Slash after target confirmation", RenwangShieldFlow),
    ("FireAttack reveals privately then resolves typed fire damage", FireAttackFlow),
    ("formal FireAttack can target self, keep the revealed card in hand and replay", FireAttackFormalChecks.SelfTargetAndReplay),
    ("FireSlash and ThunderSlash preserve typed damage nature", AttributeSlashFlow),
    ("Alcohol arms a one-shot Slash damage boost", AlcoholFlow),
    ("formal play-phase Alcohol is limited once per turn and replays", AlcoholLimitChecks.OncePerTurnAndReplay),
    ("Yuanhu can trigger from another seat and recover the damaged player", YuanhuCrossSeatFlow),
    ("Ganglie opens a public judgment and a private source punishment", GanglieFlow),
    ("Guicai privately replaces a public judgment with a hand card", GuicaiFlow),
    ("Guidao privately replaces judgments with exact black hand or equipment cards", GuidaoChecks.BlackHandAndEquipmentReplacement),
    ("Indulgence delays a target play phase through public judgment", IndulgenceFlow),
    ("SupplyShortage delays a target draw phase through public judgment", SupplyShortageFlow),
    ("formal SupplyShortage uses distance and preserves legacy empty-hand behavior", SupplyShortageChecks.TargetingAndResolution),
    ("Lightning hits or transfers through a public delayed judgment", LightningFlow),
    ("Longdan converts Slash into Dodge in a response window", LongdanResponseFlow),
    ("dying response can use Alcohol for self rescue", DyingAlcoholRescueFlow),
    ("current Alcohol legality and AI allow only holder self rescue", DyingAlcoholOnlySelfRule),
    ("dying response can pause and recover with a private Peach", DyingResponseFlow),
    ("classic Jijiu converts red equipment with versioned payment and replay", JijiuChecks.EquipmentFlow),
    ("throwing observers are isolated after commit", ObserverFailuresAreIsolated),
    ("uncaught observer reentry cannot interrupt the engine", UncaughtObserverReentryIsIsolated),
    ("human commands reach play and accept a legal card", HumanPlayApi),
    ("declining lethal Dodge leaves a completed game completed", LethalHumanResponseKeepsCompletedStatus),
    ("single-step command exposes one AI decision at a time", AdvanceOneStepApi),
    ("AI ending play publishes its committed Discard state", AiEndPlayPublishesState),
    ("synchronous observers cannot advance the engine reentrantly", ReentrantAdvanceIsRejected),
    ("an unknown phase fails fast", UnknownPhaseFailsFast),
    ("AI-only match terminates and records explainable thoughts", AiMatchSmoke),
    ("classic Ice Sword sequentially discards target cards and prevents Slash damage", IceSwordChecks.SequentialDiscardPreventsDamageAndReplays),
    ("classic Qilin Bow discards an exact public mount before Slash damage", QilinBowChecks.ExactMountChoiceAndReplay),
    ("classic Qinglong opens a same-target follow-up Slash and replays", QinglongCrescentBladeChecks.SameTargetFollowupAndReplay),
    ("classic Stone Axe pays an exact two-card cost and resumes Slash damage", StoneAxeChecks.ExactCostDamageAndReplay),
    ("Qu Yi nearby Slash response precedes Jiaozi damage", QuYiChecks.NearbySlashCannotRespondBeforeDamageBonus),
    ("Chunlao stores exact Slash cards publicly and replays a paused selection", ChengPuLihuoChecks.ChunlaoStoresExactSlashesAndReplays),
    ("Chunlao spends one public Chun as virtual Alcohol in a dying response", ChengPuLihuoChecks.ChunlaoRescuesWithVirtualAlcoholAndReplays),
    ("Gongqi equipment cost grants unlimited range and uses opaque optional discard", HanDangChecks.GongqiEquipmentCostAndOpaqueDiscardReplay),
    ("Gundam One core fighter revives once per game", GaoDaYiHaoChecks.CoreFighterRevivesOncePerGame),
    ("formal Wushen treats a real heart Peach as a distance-free counted Slash", ClassicShenGuanYuChecks.WushenRealDeckIdentityAndReplay),
    ("snapshot is JSON serializable", SnapshotSerialization)
};

if (args.Any(argument => argument != "--verbose" &&
    !argument.StartsWith("--filter=", StringComparison.OrdinalIgnoreCase)))
{
    Console.Error.WriteLine("Unknown Core check option. Use --filter=<name>.");
    return 2;
}
var nameFilter = args.FirstOrDefault(argument =>
    argument.StartsWith("--filter=", StringComparison.OrdinalIgnoreCase));
if (nameFilter is not null)
{
    var value = nameFilter["--filter=".Length..].Trim();
    if (value.Length == 0)
    {
        Console.Error.WriteLine("A Core check filter cannot be empty.");
        return 2;
    }
    tests = tests.Where(test => test.Name.Contains(value, StringComparison.OrdinalIgnoreCase)).ToArray();
    if (tests.Length == 0)
    {
        Console.Error.WriteLine($"No Core checks matched filter '{value}'.");
        return 2;
    }
}

var passed = 0;
var failed = 0;
const int skipped = 0;
foreach (var (name, body) in tests)
{
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
        if (args.Contains("--verbose", StringComparer.OrdinalIgnoreCase)) Console.WriteLine(exception);
    }
}

Console.WriteLine();
Console.WriteLine($"{passed} passed, {failed} failed, {skipped} skipped ({tests.Length} total).");
return failed == 0 ? 0 : 1;

static void IdentityDistribution()
{
    var game = GameEngine.CreateStandard(new GameOptions { UseInteractiveDiscard = false, Seed = 7 }, StandardContentRegistry.Create());
    var roles = game.CreateSnapshot(0, revealAll: true).Players
        .GroupBy(player => player.Role!.Value)
        .ToDictionary(group => group.Key, group => group.Count());

    Equal(1, roles[Role.Lord]);
    Equal(2, roles[Role.Loyalist]);
    Equal(4, roles[Role.Rebel]);
    Equal(1, roles[Role.Renegade]);
}

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

static void CardCatalogDefinitions()
{
    Equal(48, CardCatalog.ImplementedCards.Count);
    Equal("杀", CardCatalog.Get(CardKind.Slash).DisplayName);
    Equal("闪", CardCatalog.Get(CardKind.Dodge).DisplayName);
    Equal("桃", CardCatalog.Get(CardKind.Peach).DisplayName);
    Equal("决斗", CardCatalog.Get(CardKind.Duel).DisplayName);
    Equal("无中生有", CardCatalog.Get(CardKind.DrawTwo).DisplayName);
    Equal("南蛮入侵", CardCatalog.Get(CardKind.BarbarianAssault).DisplayName);
    Equal("万箭齐发", CardCatalog.Get(CardKind.ArrowBarrage).DisplayName);
    Equal("桃园结义", CardCatalog.Get(CardKind.PeachGarden).DisplayName);
    Equal("五谷丰登", CardCatalog.Get(CardKind.FiveGrains).DisplayName);
    Equal("过河拆桥", CardCatalog.Get(CardKind.Dismantlement).DisplayName);
    Equal("顺手牵羊", CardCatalog.Get(CardKind.Snatch).DisplayName);
    Equal("火攻", CardCatalog.Get(CardKind.FireAttack).DisplayName);
    Equal("火杀", CardCatalog.Get(CardKind.FireSlash).DisplayName);
    Equal("雷杀", CardCatalog.Get(CardKind.ThunderSlash).DisplayName);
    Equal("酒", CardCatalog.Get(CardKind.Alcohol).DisplayName);
    Equal("诸葛连弩", CardCatalog.Get(CardKind.Crossbow).DisplayName);
    Equal("八卦阵", CardCatalog.Get(CardKind.BaguaFormation).DisplayName);
    Equal("仁王盾", CardCatalog.Get(CardKind.RenwangShield).DisplayName);
    Equal("赤兔", CardCatalog.Get(CardKind.OffensiveHorse).DisplayName);
    Equal("绝影", CardCatalog.Get(CardKind.DefensiveHorse).DisplayName);
    Equal("大宛", CardCatalog.Get(CardKind.Dawan).DisplayName);
    Equal("紫骍", CardCatalog.Get(CardKind.Zixing).DisplayName);
    Equal("的卢", CardCatalog.Get(CardKind.Dilu).DisplayName);
    Equal("爪黄飞电", CardCatalog.Get(CardKind.Zhaohuangfeidian).DisplayName);
    Equal("骅骝", CardCatalog.Get(CardKind.Hualiu).DisplayName);
    Equal("玉玺", CardCatalog.Get(CardKind.JadeSeal).DisplayName);
    Equal("青釭剑", CardCatalog.Get(CardKind.QinggangSword).DisplayName);
    Equal("无懈可击", CardCatalog.Get(CardKind.Nullification).DisplayName);
    Equal("铁索连环", CardCatalog.Get(CardKind.IronChain).DisplayName);
    Equal("乐不思蜀", CardCatalog.Get(CardKind.Indulgence).DisplayName);
    Equal("兵粮寸断", CardCatalog.Get(CardKind.SupplyShortage).DisplayName);
    True(CardCatalog.Get(CardKind.Indulgence).Description.Contains("不为红桃"));
    True(CardCatalog.Get(CardKind.SupplyShortage).Description.Contains("不为梅花"));
    Equal("闪电", CardCatalog.Get(CardKind.Lightning).DisplayName);
    Equal("借刀杀人", CardCatalog.Get(CardKind.BorrowedSword).DisplayName);
    Equal("贯石斧", CardCatalog.Get(CardKind.StoneAxe).DisplayName);
    Equal("丈八蛇矛", CardCatalog.Get(CardKind.ZhangbaSerpentSpear).DisplayName);
    Equal("雌雄双股剑", CardCatalog.Get(CardKind.CixiongDoubleSwords).DisplayName);
    Equal("青龙偃月刀", CardCatalog.Get(CardKind.QinglongCrescentBlade).DisplayName);
    Equal("寒冰剑", CardCatalog.Get(CardKind.IceSword).DisplayName);
    Equal("麒麟弓", CardCatalog.Get(CardKind.QilinBow).DisplayName);
    Equal("方天画戟", CardCatalog.Get(CardKind.FangtianHalberd).DisplayName);
    Equal("古锭刀", CardCatalog.Get(CardKind.GudingBlade).DisplayName);
    Equal("朱雀羽扇", CardCatalog.Get(CardKind.ZhuqueFan).DisplayName);
    True(CardCatalog.ImplementedCards.All(definition =>
        !string.IsNullOrWhiteSpace(definition.Description)));
    Equal(38, CardCatalog.Get(CardKind.Peach).AiPlayValue);
    Equal(65, CardCatalog.Get(CardKind.Dodge).AiResponseValue);
    Equal(40, CardCatalog.Get(CardKind.Slash).HandKeepValue);
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
    Equal(27, registry.Cards.Count);
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

static void FivePlayerIdentityMode()
{
    var game = GameEngine.CreateStandard(
        new GameOptions
        {
            UseInteractiveDiscard = false,
            Seed = 917,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = Role.Lord
        },
        StandardContentRegistry.Create());
    var view = game.CreateSnapshot(0, revealAll: true);
    var roles = view.Players
        .GroupBy(player => player.Role!.Value)
        .ToDictionary(group => group.Key, group => group.Count());

    Equal(5, game.PlayerCount);
    Equal(5, view.Players.Count);
    Equal(1, roles[Role.Lord]);
    Equal(1, roles[Role.Loyalist]);
    Equal(2, roles[Role.Rebel]);
    Equal(1, roles[Role.Renegade]);
    True(view.Players.All(player => player.HandCount == 4));

    var started = game.Submit(new StartGameCommand());
    True(started.Accepted);
    Equal(EngineStatus.AwaitingHumanPlay, started.State.Status);
    Equal(22, game.CreateCardZoneDiagnostics().Count(card =>
        card.Location.Zone == CardZoneKind.Hand));
}

static void DamageTriggerOrdering()
{
    var ordered = CardGame.Core.DamageTriggerOrdering.Order(
    [
        new DamageTriggerCandidate(3, "seat3:feedback", Priority: 10, ProgramId: "standard:feedback", ProgramTriggerId: "hit", SkillInstanceId: "seat3:feedback", GameplayHash: "fixture"),
        new DamageTriggerCandidate(1, "seat1:jianxiong", Priority: 10, ProgramId: "standard:jianxiong", ProgramTriggerId: "hit", SkillInstanceId: "seat1:jianxiong", GameplayHash: "fixture"),
        new DamageTriggerCandidate(2, "feedback-b", Priority: 10, ProgramId: "standard:feedback", ProgramTriggerId: "hit", SkillInstanceId: "feedback-b", GameplayHash: "fixture"),
        new DamageTriggerCandidate(2, "feedback-a", Priority: 10, ProgramId: "standard:feedback", ProgramTriggerId: "hit", SkillInstanceId: "feedback-a", GameplayHash: "fixture"),
        new DamageTriggerCandidate(2, "seat2:jianxiong", Priority: 10, ProgramId: "standard:jianxiong", ProgramTriggerId: "hit", SkillInstanceId: "seat2:jianxiong", GameplayHash: "fixture"),
        new DamageTriggerCandidate(0, "wusheng", Priority: 20, ProgramId: "standard:wusheng", ProgramTriggerId: "hit", SkillInstanceId: "wusheng", GameplayHash: "fixture")
    ],
    currentActorSeat: 7,
    playerCount: 8);

    True(ordered.Select(candidate => candidate.CandidateId).SequenceEqual(
    [
        "wusheng",
        "seat1:jianxiong",
        "feedback-a",
        "feedback-b",
        "seat2:jianxiong",
        "seat3:feedback",
    ]));
    Equal(1, CardGame.Core.DamageTriggerOrdering.GetRelativeSeatOrder(7, 0, 8));
    Equal(7, CardGame.Core.DamageTriggerOrdering.GetRelativeSeatOrder(7, 6, 8));
}

static void AiPublicEvidence()
{
    var brain = new SimpleAiBrain(seat: 7, seed: 42);

    brain.ObserveSlash(sourceSeat: 1, targetSeat: 0, lordSeat: 0, revealedTargetRole: Role.Lord);
    Equal(3d, brain.RebelSuspicion[1]);

    brain.ObserveSlash(sourceSeat: 2, targetSeat: 1, lordSeat: 0, revealedTargetRole: null);
    True(brain.RebelSuspicion[2] < 0d);

    brain.ObserveDeath(killerSeat: 3, revealedVictimRole: Role.Rebel);
    Equal(-3d, brain.RebelSuspicion[3]);
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
    for (var seed = 1; seed <= 16; seed++)
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

        TrueWithMessage(steps < 1_200, $"seed {seed} exceeded the inventory loop guard at {steps} steps");
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

static void IronChainFlow()
{
    GameEngine? selectedGame = null;
    IronChainResolvedEvent? chainResolved = null;
    ChainedDamagePropagatedEvent? propagated = null;
    GameSnapshot? chainedSnapshot = null;
    var chainCardId = -1;
    var fireSlashCardId = -1;
    var expectedTargets = new[] { 1, 2 };

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
        var started = game.DriveStart();
        if (started.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }

        var revealed = game.CreateSnapshot(0, revealAll: true);
        if (revealed.Players.Any(player => player.Skills?.Any(skill => skill.ContentId == "standard:yuanhu") == true))
        {
            continue;
        }

        var human = revealed.Players.Single(player => player.Seat == 0);
        var ironChain = human.Hand.FirstOrDefault(card => card.Kind == CardKind.IronChain);
        var fireSlash = human.Hand.FirstOrDefault(card => card.Kind == CardKind.FireSlash);
        var chainAction = game.GetHumanLegalActions().FirstOrDefault(action =>
            action.Kind == LegalActionKind.IronChain &&
            action.TargetSeats.SequenceEqual(expectedTargets));
        var fireSlashAction = game.GetHumanLegalActions().FirstOrDefault(action =>
            action.Kind == LegalActionKind.Slash &&
            action.CardId == fireSlash?.Id &&
            action.TargetSeat == expectedTargets[0]);
        var chainChoice = started.PendingDecision?.Choices.FirstOrDefault(choice =>
            choice.Cards.Count == 1 &&
            choice.Cards[0] == ironChain?.Id &&
            choice.Targets.SequenceEqual(expectedTargets));
        var target = revealed.Players.Single(player => player.Seat == expectedTargets[0]);
        if (ironChain is null ||
            fireSlash is null ||
            chainAction is null ||
            fireSlashAction is null ||
            chainChoice is null ||
            target.Skills?.Any(skill => skill.ContentId == "standard:longdan") == true ||
            target.Hand.Any(card => card.Kind == CardKind.Dodge))
        {
            continue;
        }

        var prompt = started.PendingDecision!;
        var beforeInvalid = game.SerializeState();
        var invalid = game.Submit(new PlayCardCommand(
            ActorSeat: 0,
            CardId: ironChain.Id,
            TargetSeats: [expectedTargets[0], expectedTargets[0]],
            ExpectedRevision: game.Revision,
            PromptId: prompt.PromptId));
        False(invalid.Accepted);
        Equal(CommandErrorCode.InvalidTarget, invalid.Error!.Code);
        Equal(beforeInvalid, game.SerializeState());

        var accepted = game.Submit(new PlayCardCommand(
            ActorSeat: 0,
            CardId: ironChain.Id,
            TargetSeats: chainChoice.Targets,
            ExpectedRevision: game.Revision,
            PromptId: prompt.PromptId));
        True(accepted.Accepted);
        ResolveNullificationWindowForTest(game);

        var currentChain = game.Events
            .Select(eventItem => eventItem.Payload)
            .OfType<IronChainResolvedEvent>()
            .SingleOrDefault();
        if (currentChain is null)
        {
            continue;
        }

        if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
        {
            game.DriveAdvanceOneStep();
        }

        chainedSnapshot = game.CreateSnapshot(0, revealAll: true);
        var followUp = game.GetHumanLegalActions().FirstOrDefault(action =>
            action.Kind == LegalActionKind.Slash &&
            action.CardId == fireSlash.Id &&
            action.TargetSeat == expectedTargets[0]);
        if (followUp is null)
        {
            continue;
        }

        var eventCountBeforeFireSlash = game.Events.Count;
        game.DriveHumanPlay(fireSlash.Id, expectedTargets[0], advanceToHumanBoundary: false);
        for (var step = 0; step < 512; step++)
        {
            propagated = game.Events
                .Skip(eventCountBeforeFireSlash)
                .Select(eventItem => eventItem.Payload)
                .OfType<ChainedDamagePropagatedEvent>()
                .SingleOrDefault();
            if (propagated is not null)
            {
                break;
            }

            if (game.PendingDecision is { PlayerSeat: 0 } humanPrompt)
            {
                _ = humanPrompt.Kind switch
                {
                    DecisionKind.RespondDodge => game.DriveHumanRespond(
                        useDodge: false,
                        advanceToHumanBoundary: false),
                    DecisionKind.RespondSlash => game.DriveHumanRespondSlash(
                        useSlash: false,
                        advanceToHumanBoundary: false),
                    DecisionKind.RescueDying => game.DriveHumanRespondDying(
                        usePeach: false,
                        advanceToHumanBoundary: false),
                    DecisionKind.SelectHarvestCard => ResolveFirstHarvestChoice(game),
                    DecisionKind.Nullification => game.DriveHumanRespondNullification(
                        useNullification: false,
                        advanceToHumanBoundary: false),
                    DecisionKind.PlayCard => game.DriveHumanEndPlay(advanceToHumanBoundary: false),
                    _ => throw new InvalidOperationException(
                        $"Unexpected human prompt during IronChain propagation: {humanPrompt.Kind}.")
                };
            }
            else
            {
                game.DriveAdvanceOneStep();
            }
        }

        if (propagated is null)
        {
            continue;
        }

        for (var settleStep = 0;
             settleStep < 512 &&
             !game.Events.Any(eventItem =>
                 eventItem.Payload is CardUseFinishedEvent finished &&
                 finished.CardId == fireSlash.Id) &&
             !game.Events.Any(eventItem =>
                 eventItem.Payload is ProgramDamageCardsClaimedEvent claimed &&
                 claimed.CardIds.Contains(fireSlash.Id));
             settleStep++)
        {
            if (game.PendingDecision is { PlayerSeat: 0 } settlePrompt)
            {
                _ = settlePrompt.Kind switch
                {
                    DecisionKind.RespondDodge => game.DriveHumanRespond(
                        useDodge: false,
                        advanceToHumanBoundary: false),
                    DecisionKind.RespondSlash => game.DriveHumanRespondSlash(
                        useSlash: false,
                        advanceToHumanBoundary: false),
                    DecisionKind.RescueDying => game.DriveHumanRespondDying(
                        usePeach: false,
                        advanceToHumanBoundary: false),
                    DecisionKind.SelectHarvestCard => ResolveFirstHarvestChoice(game),
                    DecisionKind.Nullification => game.DriveHumanRespondNullification(
                        useNullification: false,
                        advanceToHumanBoundary: false),
                    DecisionKind.PlayCard => game.DriveHumanEndPlay(advanceToHumanBoundary: false),
                    _ => throw new InvalidOperationException(
                        $"Unexpected human prompt while settling IronChain: {settlePrompt.Kind}.")
                };
            }
            else
            {
                game.DriveAdvanceOneStep();
            }
        }

        selectedGame = game;
        chainResolved = currentChain;
        chainCardId = ironChain.Id;
        fireSlashCardId = fireSlash.Id;
    }

    NotNull(selectedGame);
    NotNull(chainResolved);
    NotNull(propagated);
    var gameWithChain = selectedGame!;
    var resolved = chainResolved!;
    var propagation = propagated!;

    True(resolved.TargetSeats.SequenceEqual(expectedTargets));
    Equal(0, resolved.SourceSeat);
    True(gameWithChain.Events.Any(eventItem =>
        eventItem.Payload is NullificationResolvedEvent nullification &&
        nullification.ResolutionId == resolved.ResolutionId &&
        !nullification.EffectNullified));
    var chainStateChanges = gameWithChain.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<IronChainStateChangedEvent>()
        .Where(change => change.ResolutionId == resolved.ResolutionId)
        .ToArray();
    True(chainStateChanges.Select(change => change.TargetSeat).SequenceEqual(expectedTargets));
    True(chainStateChanges.All(change => change.IsChained));

    NotNull(chainedSnapshot);
    True(chainedSnapshot!.Players
        .Where(player => expectedTargets.Contains(player.Seat))
        .All(player => player.IsChained));
    True(gameWithChain.Events.Any(eventItem =>
        eventItem.Payload is CardUseFinishedEvent finished &&
        finished.ResolutionId == resolved.ResolutionId &&
        finished.CardId == chainCardId &&
        finished.CardKind == CardKind.IronChain));
    True(gameWithChain.CardMovements.Any(movement =>
        movement.CardId == chainCardId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.IronChainUse));
    True(gameWithChain.CardMovements.Any(movement =>
        movement.CardId == chainCardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.IronChainFinished));

    Equal(0, propagation.SourceSeat);
    Equal(expectedTargets[0], propagation.FromSeat);
    Equal(expectedTargets[1], propagation.TargetSeat);
    Equal(1, propagation.Amount);
    Equal(DamageNature.Fire, propagation.Nature);
    var elementalDamage = gameWithChain.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<DamageAppliedEvent>()
        .Where(damage => damage.SourceSeat == 0 && damage.Nature == DamageNature.Fire)
        .ToArray();
    Equal(2, elementalDamage.Length);
    True(elementalDamage.Select(damage => damage.TargetSeat).SequenceEqual(expectedTargets));
    True(gameWithChain.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<IronChainStateChangedEvent>()
        .Any(change =>
            change.ResolutionId == propagation.ResolutionId &&
            change.TargetSeat == expectedTargets[0] &&
            !change.IsChained));
    True(gameWithChain.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<IronChainStateChangedEvent>()
        .Any(change =>
            change.ResolutionId == propagation.ResolutionId &&
            change.TargetSeat == expectedTargets[1] &&
            !change.IsChained));
    True(gameWithChain.CreateSnapshot(0, revealAll: true).Players
        .Where(player => expectedTargets.Contains(player.Seat))
        .All(player => !player.IsChained));
    True(gameWithChain.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<CardUseFinishedEvent>()
        .Any(finished =>
            finished.CardId == fireSlashCardId &&
            finished.CardKind == CardKind.FireSlash));
    var fireSlashLocation = gameWithChain.CreateCardZoneDiagnostics()
        .Single(card => card.CardId == fireSlashCardId)
        .Location;
    True(fireSlashLocation == CardLocation.DiscardPile ||
         gameWithChain.Events
             .Select(eventItem => eventItem.Payload)
             .OfType<ProgramDamageCardsClaimedEvent>()
             .Any(claim => claim.CardIds.Contains(fireSlashCardId)));
    Equal(0, gameWithChain.ResolutionStack.Count);
    AssertCardInventory(gameWithChain);
}

static void BarbarianAssaultFlow()
{
    GameEngine? selectedGame = null;
    LegalAction? selectedAction = null;
    CardUseFrame? groupFrame = null;
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
            .FirstOrDefault(candidate => candidate.Kind == LegalActionKind.BarbarianAssault);
        if (result.Status != EngineStatus.AwaitingHumanPlay || action is null)
        {
            continue;
        }

        if (!game.CreateSnapshot(0, revealAll: true).Players.All(player =>
                player.Hand.All(card => card.Kind != CardKind.Nullification)))
        {
            continue;
        }
        if (game.CreateSnapshot(0, revealAll: true).Players
            .Single(player => player.Seat == 0).Skills?
                .Any(skill => skill.ContentId == "standard:yuanhu") == true)
        {
            continue;
        }

        result = game.DriveHumanPlay(action.CardId!.Value, null, advanceToHumanBoundary: false);
        result = ResolveNullificationWindowForTest(game, result);
        var currentFrame = game.ResolutionStack.OfType<CardUseFrame>()
            .SingleOrDefault(frame => frame.CardKind == CardKind.BarbarianAssault);
        var response = game.ResolutionStack.OfType<ResponseWindowFrame>()
            .SingleOrDefault(frame => frame.IncomingCard == CardKind.BarbarianAssault);
        if (currentFrame is not null &&
            response is not null &&
            currentFrame.TargetIndex == 0 &&
            response.ResponderSeat == currentFrame.TargetSeats[0])
        {
            selectedGame = game;
            selectedAction = action;
            groupFrame = currentFrame;
        }
    }

    if (selectedGame is null || selectedAction is null || groupFrame is null)
    {
        throw new InvalidOperationException("No deterministic BarbarianAssault response window was found.");
    }

    var gameWithAssault = selectedGame!;
    var frame = groupFrame!;
    Equal(7, frame.TargetSeats.Count);
    Equal(0, frame.TargetIndex);
    Equal(1, gameWithAssault.State.ProcessingCardCount);
    Equal(
        CardLocation.Processing,
        gameWithAssault.CreateCardZoneDiagnostics()
            .Single(card => card.CardId == frame.CardId)
            .Location);

    var firstTarget = frame.TargetSeats[0];
    var privateTargetView = gameWithAssault.CreateSnapshot(firstTarget);
    Equal(DecisionKind.RespondSlash, privateTargetView.PendingDecision!.Kind);
    Equal(CardKind.BarbarianAssault, privateTargetView.PendingDecision.IncomingCard);
    Equal(CardKind.Slash, privateTargetView.PendingDecision.RequiredCardKind);
    True(privateTargetView.PendingDecision.ValidCardIds.Count > 0);
    Equal<PendingDecision?>(null, gameWithAssault.State.PendingDecision);
    Equal<PendingDecision?>(null, gameWithAssault.CreateSnapshot(1 == firstTarget ? 2 : 1).PendingDecision);

    True(gameWithAssault.Events.Any(eventItem =>
        eventItem.Payload is GroupCardUsedEvent used &&
        used.ResolutionId == frame.Id &&
        used.CardKind == CardKind.BarbarianAssault &&
        used.TargetSeats.SequenceEqual(frame.TargetSeats)));

    var steps = 0;
    while (!gameWithAssault.Events.Any(eventItem =>
               eventItem.Payload is CardUseFinishedEvent finished &&
               finished.ResolutionId == frame.Id) &&
           steps++ < 500)
    {
        var advanced = gameWithAssault.DriveAdvanceOneStep();
        if (advanced.Status == EngineStatus.AwaitingHumanResponse)
        {
            gameWithAssault.DriveHumanRespond(useDodge: false, advanceToHumanBoundary: false);
        }
        else if (advanced.Status == EngineStatus.AwaitingHumanDying)
        {
            gameWithAssault.DriveHumanRespondDying(usePeach: false, advanceToHumanBoundary: false);
        }
    }

    TrueWithMessage(
        gameWithAssault.Events.Any(eventItem =>
            eventItem.Payload is CardUseFinishedEvent finished &&
            finished.ResolutionId == frame.Id &&
            finished.CardKind == CardKind.BarbarianAssault),
        "BarbarianAssault finished event");
    var responses = gameWithAssault.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<GroupResponseEvent>()
        .Where(response => response.ResolutionId == frame.Id)
        .ToArray();
    Equal(frame.TargetSeats.Count, responses.Length);
    True(responses.Select(response => response.ResponderSeat)
        .SequenceEqual(frame.TargetSeats));
    True(responses.All(response => response.IncomingCard == CardKind.BarbarianAssault));
    True(gameWithAssault.Events.Any(eventItem =>
        eventItem.Payload is DamageRequestedEvent damage &&
        damage.SourceCard == CardKind.BarbarianAssault));
    Equal(0, gameWithAssault.ResolutionStack.Count);
    var damageCardClaims = gameWithAssault.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<ProgramDamageCardsClaimedEvent>()
        .Where(claim => claim.CardIds.Contains(frame.CardId))
        .ToArray();
    True(damageCardClaims.Length <= 1);
    Equal(
        damageCardClaims.SingleOrDefault() is { } claim
            ? CardLocation.Hand(claim.OwnerSeat)
            : CardLocation.DiscardPile,
        gameWithAssault.CreateCardZoneDiagnostics()
            .Single(card => card.CardId == frame.CardId)
            .Location);
    AssertCardInventory(gameWithAssault);
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

static void DismantlementFlow()
{
    GameEngine? selectedGame = null;
    LegalAction? selectedAction = null;
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
            .FirstOrDefault(candidate => candidate.Kind == LegalActionKind.Dismantlement);
        if (!game.CreateSnapshot(0, revealAll: true).Players.All(player =>
                player.Hand.All(card => card.Kind != CardKind.Nullification)))
        {
            continue;
        }

        if (result.Status == EngineStatus.AwaitingHumanPlay &&
            action is { TargetSeat: not null })
        {
            var targetView = game.CreateSnapshot(action.TargetSeat.Value, revealAll: true)
                .Players.Single(player => player.Seat == action.TargetSeat.Value);
            if (targetView.Hand.Any(card => card.DisplayName == CardCatalog.Get(CardKind.Dismantlement).DisplayName))
            {
                continue;
            }

            selectedGame = game;
            selectedAction = action;
        }
    }

    if (selectedGame is null || selectedAction is null)
    {
        throw new InvalidOperationException("No deterministic Dismantlement target boundary was found.");
    }

    var gameWithDismantlement = selectedGame!;
    var actionToUse = selectedAction!;
    var targetSeat = actionToUse.TargetSeat!.Value;
    var sourceBefore = gameWithDismantlement.State.Players.Single(player => player.Seat == 0);
    var publicTargetBefore = gameWithDismantlement.State.Players.Single(player => player.Seat == targetSeat);
    var privateTargetBefore = gameWithDismantlement.CreateSnapshot(targetSeat)
        .Players.Single(player => player.Seat == targetSeat);
    True(publicTargetBefore.Hand.Count == 0);
    True(publicTargetBefore.HandCount > 0);
    True(privateTargetBefore.Hand.Count == privateTargetBefore.HandCount);

    var beforeTargetCards = privateTargetBefore.Hand.ToArray();
    var resultAfterUse = gameWithDismantlement.DriveHumanPlay(
        actionToUse.CardId!.Value,
        targetSeat,
        advanceToHumanBoundary: false);

    Equal(EngineStatus.AwaitingHumanCardSelection, resultAfterUse.Status);
    var targetPrompt = resultAfterUse.PendingDecision ??
        throw new InvalidOperationException("Dismantlement should expose a private target-card prompt.");
    Equal(DecisionKind.SelectTargetCard, targetPrompt.Kind);
    True(targetPrompt.IsPrivate);
    Equal(targetSeat, targetPrompt.TargetSeat);
    True(targetPrompt.Choices.Count == privateTargetBefore.HandCount);
    True(targetPrompt.Choices.All(choice =>
        choice.Cards.Count == 0 &&
        choice.Targets.SequenceEqual([targetSeat]) &&
        choice.Parameters.ContainsKey("slot-index") &&
        !choice.Parameters.ContainsKey("card-id")));
    resultAfterUse = gameWithDismantlement.DriveHumanSelectTargetCardSlot(0, advanceToHumanBoundary: false);

    Equal(EngineStatus.Running, resultAfterUse.Status);
    Equal<PendingDecision?>(null, resultAfterUse.PendingDecision);
    Equal(0, resultAfterUse.State.ProcessingCardCount);
    Equal(0, gameWithDismantlement.ResolutionStack.Count);

    var privateTargetAfter = gameWithDismantlement.CreateSnapshot(targetSeat)
        .Players.Single(player => player.Seat == targetSeat);
    var removedCard = beforeTargetCards
        .Single(card => privateTargetAfter.Hand.All(remaining => remaining.Id != card.Id));
    Equal(beforeTargetCards.Length - 1, privateTargetAfter.Hand.Count);
    Equal(sourceBefore.HandCount - 1, resultAfterUse.State.Players.Single(player => player.Seat == 0).HandCount);

    var resolutionId = gameWithDismantlement.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<CardUseDeclaredEvent>()
        .Single(cardUse => cardUse.CardId == actionToUse.CardId)
        .ResolutionId;
    var discardedEvent = gameWithDismantlement.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<TargetCardDiscardedEvent>()
        .Single(eventItem => eventItem.ResolutionId == resolutionId);
    Equal(0, discardedEvent.SourceSeat);
    Equal(targetSeat, discardedEvent.TargetSeat);
    Equal(CardZoneKind.Hand, discardedEvent.FromZone);

    True(gameWithDismantlement.Events.Any(eventItem =>
        eventItem.Payload is CardUseFinishedEvent finished &&
        finished.ResolutionId == resolutionId &&
        finished.CardKind == CardKind.Dismantlement));
    True(gameWithDismantlement.CardMovements.Any(movement =>
        movement.CardId == actionToUse.CardId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Use));
    True(gameWithDismantlement.CardMovements.Any(movement =>
        movement.CardId == actionToUse.CardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.UseFinished));
    True(gameWithDismantlement.CardMovements.Any(movement =>
        movement.CardId == removedCard.Id &&
        movement.From == CardLocation.Hand(targetSeat) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Dismantlement));
    True(gameWithDismantlement.CardMovements.Any(movement =>
        movement.CardId == removedCard.Id &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.DismantlementFinished));
    True(resultAfterUse.State.Players
        .SelectMany(player => player.Hand)
        .All(card => card.Id != removedCard.Id));
    var dismantlementLog = gameWithDismantlement.Log.Last(entry =>
        entry.Type == "CardEffect" && entry.Message.Contains("过河拆桥", StringComparison.Ordinal));
    TrueWithMessage(
        !dismantlementLog.Message.Contains(removedCard.DisplayName, StringComparison.Ordinal),
        $"拆桥日志泄露牌面：{dismantlementLog.Message}; removed={removedCard.DisplayName}");
    AssertCardInventory(gameWithDismantlement);
}

static void SnatchFlow()
{
    GameEngine? selectedGame = null;
    LegalAction? selectedAction = null;
    for (var seed = 1; seed <= 1_024 && selectedGame is null; seed++)
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
            .FirstOrDefault(candidate =>
                candidate.Kind == LegalActionKind.Snatch &&
                candidate.TargetSeat is not null);
        if (result.Status == EngineStatus.AwaitingHumanPlay && action is not null)
        {
            var target = game.CreateSnapshot(0, revealAll: true).Players
                .Single(player => player.Seat == action.TargetSeat);
            if (target.Role != Role.Rebel)
            {
                continue;
            }
            selectedGame = game;
            selectedAction = action;
        }
    }

    if (selectedGame is null || selectedAction is null)
    {
        throw new InvalidOperationException("No deterministic Snatch target boundary was found.");
    }

    var gameWithSnatch = selectedGame!;
    var actionToUse = selectedAction!;
    var targetSeat = actionToUse.TargetSeat!.Value;
    Equal(0, gameWithSnatch.GetSeatDistance(0, 0));
    Equal(1, gameWithSnatch.GetSeatDistance(0, 1));
    Equal(1, gameWithSnatch.GetSeatDistance(0, 7));
    Equal(2, gameWithSnatch.GetSeatDistance(0, 2));
    Equal(4, gameWithSnatch.GetSeatDistance(0, 4));
    Equal(1, gameWithSnatch.GetSeatDistance(0, targetSeat));
    True(gameWithSnatch.GetHumanLegalActions()
        .Where(action => action.Kind == LegalActionKind.Snatch)
        .All(action => action.TargetSeat is { } seat && gameWithSnatch.GetCombatDistance(0, seat) == 1));
    Throws<ArgumentOutOfRangeException>(() => gameWithSnatch.GetSeatDistance(-1, 0));
    Throws<ArgumentOutOfRangeException>(() => gameWithSnatch.GetSeatDistance(0, 8));

    var sourceBefore = gameWithSnatch.State.Players.Single(player => player.Seat == 0);
    var publicTargetBefore = gameWithSnatch.State.Players.Single(player => player.Seat == targetSeat);
    var privateTargetBefore = gameWithSnatch.CreateSnapshot(targetSeat)
        .Players.Single(player => player.Seat == targetSeat);
    True(publicTargetBefore.Hand.Count == 0);
    True(publicTargetBefore.HandCount > 0);
    True(privateTargetBefore.Hand.Count == privateTargetBefore.HandCount);

    var beforeTargetCards = privateTargetBefore.Hand.ToArray();
    var resultAfterUse = gameWithSnatch.DriveHumanPlay(
        actionToUse.CardId!.Value,
        targetSeat,
        advanceToHumanBoundary: false);
    resultAfterUse = ResolveNullificationWindowForTest(gameWithSnatch, resultAfterUse);

    Equal(EngineStatus.Running, resultAfterUse.Status);
    Equal<PendingDecision?>(null, resultAfterUse.PendingDecision);
    Equal(0, resultAfterUse.State.ProcessingCardCount);
    Equal(0, gameWithSnatch.ResolutionStack.Count);

    var privateTargetAfter = gameWithSnatch.CreateSnapshot(targetSeat)
        .Players.Single(player => player.Seat == targetSeat);
    var sourceAfter = gameWithSnatch.State.Players.Single(player => player.Seat == 0);
    var takenCard = beforeTargetCards
        .Single(card => privateTargetAfter.Hand.All(remaining => remaining.Id != card.Id));
    Equal(beforeTargetCards.Length - 1, privateTargetAfter.Hand.Count);
    Equal(sourceBefore.HandCount, sourceAfter.HandCount);
    True(sourceAfter.Hand.Any(card => card.Id == takenCard.Id));

    var resolutionId = gameWithSnatch.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<CardUseDeclaredEvent>()
        .Single(cardUse => cardUse.CardId == actionToUse.CardId)
        .ResolutionId;
    var takenEvent = gameWithSnatch.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<TargetCardTakenEvent>()
        .Single(eventItem => eventItem.ResolutionId == resolutionId);
    Equal(0, takenEvent.SourceSeat);
    Equal(targetSeat, takenEvent.TargetSeat);
    Equal(CardZoneKind.Hand, takenEvent.FromZone);

    True(gameWithSnatch.Events.Any(eventItem =>
        eventItem.Payload is CardUseFinishedEvent finished &&
        finished.ResolutionId == resolutionId &&
        finished.CardKind == CardKind.Snatch));
    True(gameWithSnatch.CardMovements.Any(movement =>
        movement.CardId == actionToUse.CardId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Use));
    True(gameWithSnatch.CardMovements.Any(movement =>
        movement.CardId == actionToUse.CardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.UseFinished));
    True(gameWithSnatch.CardMovements.Any(movement =>
        movement.CardId == takenCard.Id &&
        movement.From == CardLocation.Hand(targetSeat) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Snatch));
    True(gameWithSnatch.CardMovements.Any(movement =>
        movement.CardId == takenCard.Id &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.Hand(0) &&
        movement.Reason == CardMoveReasons.SnatchFinished));

    var ordinaryObserverAfter = gameWithSnatch.CreateSnapshot(2);
    True(ordinaryObserverAfter.Players.Single(player => player.Seat == targetSeat).Hand.Count == 0);
    True(ordinaryObserverAfter.Players.Single(player => player.Seat == 0).Hand.Count == 0);
    True(ordinaryObserverAfter.Players
        .SelectMany(player => player.Hand)
        .All(card => card.Id != takenCard.Id));
    True(gameWithSnatch.State.Players
        .Single(player => player.Seat == 0)
        .Hand.Any(card => card.Id == takenCard.Id));
    True(!gameWithSnatch.Log.Last(entry => entry.Type == "CardEffect")
        .Message.Contains(takenCard.DisplayName, StringComparison.Ordinal));
    AssertCardInventory(gameWithSnatch);
}

static void PublicTargetCardFlow()
{
    static GameEngine? FindGameWithPublicTarget(
        LegalActionKind actionKind,
        CardZoneKind targetZone)
    {
        for (var seed = 1; seed <= 16_384; seed++)
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

            for (var round = 0;
                 round < 12 && result.Status == EngineStatus.AwaitingHumanPlay;
                 round++)
            {
                var full = game.CreateSnapshot(0, revealAll: true);
                if (game.GetHumanLegalActions().Any(action =>
                        action.Kind == actionKind &&
                        action.TargetSeat is not null &&
                        action.TargetCardId is not null &&
                        full.Players.Single(player => player.Seat == action.TargetSeat).Role == Role.Rebel &&
                        full.Players.Where(player => player.Seat != 0).All(player =>
                            player.Hand.All(card => card.Kind != CardKind.Nullification)) &&
                        HasPublicTarget(game, action, targetZone)))
                {
                    return game;
                }

                result = game.DriveHumanEndPlay(advanceToHumanBoundary: true);
            }
        }

        return null;
    }

    static bool HasPublicTarget(
        GameEngine game,
        LegalAction action,
        CardZoneKind targetZone)
    {
        var target = game.State.Players.Single(player => player.Seat == action.TargetSeat);
        var cards = targetZone switch
        {
            CardZoneKind.Equipment => target.Equipment,
            CardZoneKind.Judgment => target.Judgment,
            _ => throw new ArgumentOutOfRangeException(nameof(targetZone), targetZone, null)
        };
        return cards.Any(card => card.Id == action.TargetCardId);
    }

    static (LegalAction Action, CardSnapshot TargetCard) GetPublicTarget(
        GameEngine game,
        LegalActionKind actionKind,
        CardZoneKind targetZone)
    {
        var action = game.GetHumanLegalActions().First(candidate =>
            candidate.Kind == actionKind &&
            candidate.TargetSeat is not null &&
            candidate.TargetCardId is not null &&
            HasPublicTarget(game, candidate, targetZone));
        var target = game.State.Players.Single(player => player.Seat == action.TargetSeat);
        var targetCard = targetZone switch
        {
            CardZoneKind.Equipment => target.Equipment.Single(card => card.Id == action.TargetCardId),
            CardZoneKind.Judgment => target.Judgment.Single(card => card.Id == action.TargetCardId),
            _ => throw new ArgumentOutOfRangeException(nameof(targetZone), targetZone, null)
        };
        return (action, targetCard);
    }

    static void AssertPublicTargetResolution(
        GameEngine game,
        LegalActionKind actionKind,
        int effectCardId,
        CardSnapshot targetCard,
        CardZoneKind targetZone)
    {
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == actionKind &&
            candidate.CardId == effectCardId &&
            candidate.TargetCardId == targetCard.Id);
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Public target test has no play prompt.");
        var choice = prompt.Choices.Single(candidate =>
            candidate.Cards.SequenceEqual([action.CardId!.Value]) &&
            candidate.Targets.SequenceEqual([action.TargetSeat!.Value]) &&
            candidate.Parameters.GetValueOrDefault("target-card-id") == targetCard.Id.ToString());
        Equal(action.Description, choice.Description);

        var targetSeat = action.TargetSeat ??
            throw new InvalidOperationException("A public target action must declare a target seat.");
        var sourceBefore = game.State.Players.Single(player => player.Seat == 0);
        var targetBefore = game.State.Players.Single(player => player.Seat == targetSeat);
        var targetCardsBefore = targetZone switch
        {
            CardZoneKind.Equipment => targetBefore.Equipment,
            CardZoneKind.Judgment => targetBefore.Judgment,
            _ => throw new ArgumentOutOfRangeException(nameof(targetZone), targetZone, null)
        };
        TrueWithMessage(targetCardsBefore.Any(card => card.Id == targetCard.Id), "target has selected public card before use");
        var ordinaryTargetBefore = game.CreateSnapshot(2).Players.Single(player => player.Seat == targetSeat);
        var ordinaryTargetCardsBefore = targetZone switch
        {
            CardZoneKind.Equipment => ordinaryTargetBefore.Equipment,
            CardZoneKind.Judgment => ordinaryTargetBefore.Judgment,
            _ => throw new ArgumentOutOfRangeException(nameof(targetZone), targetZone, null)
        };
        TrueWithMessage(ordinaryTargetCardsBefore.Any(card => card.Id == targetCard.Id), "public viewer sees selected target card");

        var result = game.DriveHumanPlay(
            action.CardId!.Value,
            targetSeat,
            advanceToHumanBoundary: false,
            targetCardId: targetCard.Id);
        result = ResolveNullificationWindowForTest(game, result);
        Equal(EngineStatus.Running, result.Status);
        Equal<PendingDecision?>(null, result.PendingDecision);
        Equal(0, result.State.ProcessingCardCount);
        Equal(0, game.ResolutionStack.Count);

        var resolutionId = game.Events
            .Select(eventItem => eventItem.Payload)
            .OfType<CardUseDeclaredEvent>()
            .Single(cardUse => cardUse.CardId == action.CardId)
            .ResolutionId;
        var sourceAfter = game.State.Players.Single(player => player.Seat == 0);
        var targetAfter = game.State.Players.Single(player => player.Seat == targetSeat);
        var targetCardsAfter = targetZone switch
        {
            CardZoneKind.Equipment => targetAfter.Equipment,
            CardZoneKind.Judgment => targetAfter.Judgment,
            _ => throw new ArgumentOutOfRangeException(nameof(targetZone), targetZone, null)
        };
        TrueWithMessage(targetCardsAfter.All(card => card.Id != targetCard.Id),
            $"target public zone no longer contains selected card: action={actionKind}, zone={targetZone}, " +
            $"target={targetSeat}, card={targetCard.Id}, remaining=[{string.Join(',', targetCardsAfter.Select(card => card.Id))}], " +
            $"status={game.State.Status}, stack={game.ResolutionStack.Count}, " +
            $"moves=[{string.Join(" | ", game.CardMovements.Where(move => move.CardId == targetCard.Id).Select(move => $"{move.From}->{move.To}:{move.Reason}"))}]");

        if (actionKind == LegalActionKind.Dismantlement)
        {
            Equal(sourceBefore.HandCount - 1, sourceAfter.HandCount);
            var discarded = game.Events
                .Select(eventItem => eventItem.Payload)
                .OfType<TargetCardDiscardedEvent>()
                .Single(eventItem => eventItem.ResolutionId == resolutionId);
            Equal(targetZone, discarded.FromZone);
            Equal<int?>(targetCard.Id, discarded.PublicCardId);
            Equal<CardKind?>(targetCard.Kind, discarded.PublicCardKind);
            var targetMoveReason = targetZone == CardZoneKind.Judgment
                ? CardMoveReasons.DismantlementJudgment
                : CardMoveReasons.Dismantlement;
            var targetFinishReason = targetZone == CardZoneKind.Judgment
                ? CardMoveReasons.DismantlementJudgmentFinished
                : CardMoveReasons.DismantlementFinished;
            TrueWithMessage(game.CardMovements.Any(movement =>
                movement.CardId == targetCard.Id &&
                movement.From == new CardLocation(targetZone, targetSeat) &&
                movement.To == CardLocation.Processing &&
                movement.Reason == targetMoveReason), "Dismantlement target card enters Processing");
            TrueWithMessage(game.CardMovements.Any(movement =>
                movement.CardId == targetCard.Id &&
                movement.From == CardLocation.Processing &&
                movement.To == CardLocation.DiscardPile &&
                movement.Reason == targetFinishReason), "Dismantlement target card reaches discard pile");
            TrueWithMessage(game.CreateCardZoneDiagnostics().Any(card =>
                card.CardId == targetCard.Id && card.Location == CardLocation.DiscardPile),
                "Dismantlement target card final location is discard pile");
        }
        else
        {
            Equal(sourceBefore.HandCount, sourceAfter.HandCount);
            TrueWithMessage(sourceAfter.Hand.Any(card => card.Id == targetCard.Id), "Snatch source receives target card");
            var taken = game.Events
                .Select(eventItem => eventItem.Payload)
                .OfType<TargetCardTakenEvent>()
                .Single(eventItem => eventItem.ResolutionId == resolutionId);
            Equal(targetZone, taken.FromZone);
            Equal<int?>(targetCard.Id, taken.PublicCardId);
            Equal<CardKind?>(targetCard.Kind, taken.PublicCardKind);
            var targetMoveReason = targetZone == CardZoneKind.Judgment
                ? CardMoveReasons.SnatchJudgment
                : CardMoveReasons.Snatch;
            var targetFinishReason = targetZone == CardZoneKind.Judgment
                ? CardMoveReasons.SnatchJudgmentFinished
                : CardMoveReasons.SnatchFinished;
            TrueWithMessage(game.CardMovements.Any(movement =>
                movement.CardId == targetCard.Id &&
                movement.From == new CardLocation(targetZone, targetSeat) &&
                movement.To == CardLocation.Processing &&
                movement.Reason == targetMoveReason), "Snatch target card enters Processing");
            TrueWithMessage(game.CardMovements.Any(movement =>
                movement.CardId == targetCard.Id &&
                movement.From == CardLocation.Processing &&
                movement.To == CardLocation.Hand(0) &&
                movement.Reason == targetFinishReason), "Snatch target card reaches source hand");
        }

        TrueWithMessage(game.Log.Last(entry => entry.Type == "CardEffect")
            .Message.Contains(targetCard.DisplayName, StringComparison.Ordinal), "public target appears in effect log");
        TrueWithMessage(game.Events.Any(eventItem =>
            eventItem.Payload is CardUseFinishedEvent finished &&
            finished.ResolutionId == resolutionId &&
            finished.CardKind == (actionKind == LegalActionKind.Dismantlement
                ? CardKind.Dismantlement
                : CardKind.Snatch)), "public target card use finishes");
        AssertCardInventory(game);
    }

    var dismantlementEquipmentGame = FindGameWithPublicTarget(
        LegalActionKind.Dismantlement,
        CardZoneKind.Equipment) ??
        throw new InvalidOperationException("No deterministic Dismantlement public-equipment boundary was found.");
    var (dismantlementEquipmentAction, dismantlementEquipment) = GetPublicTarget(
        dismantlementEquipmentGame,
        LegalActionKind.Dismantlement,
        CardZoneKind.Equipment);
    True(dismantlementEquipmentAction.Description.Contains(dismantlementEquipment.DisplayName, StringComparison.Ordinal));
    AssertPublicTargetResolution(
        dismantlementEquipmentGame,
        LegalActionKind.Dismantlement,
        dismantlementEquipmentAction.CardId!.Value,
        dismantlementEquipment,
        CardZoneKind.Equipment);

    var dismantlementJudgmentGame = FindGameWithPublicTarget(
        LegalActionKind.Dismantlement,
        CardZoneKind.Judgment) ??
        throw new InvalidOperationException("No deterministic Dismantlement public-judgment boundary was found.");
    var (dismantlementJudgmentAction, dismantlementJudgment) = GetPublicTarget(
        dismantlementJudgmentGame,
        LegalActionKind.Dismantlement,
        CardZoneKind.Judgment);
    True(dismantlementJudgmentAction.Description.Contains(dismantlementJudgment.DisplayName, StringComparison.Ordinal));
    AssertPublicTargetResolution(
        dismantlementJudgmentGame,
        LegalActionKind.Dismantlement,
        dismantlementJudgmentAction.CardId!.Value,
        dismantlementJudgment,
        CardZoneKind.Judgment);

    var snatchEquipmentGame = FindGameWithPublicTarget(
        LegalActionKind.Snatch,
        CardZoneKind.Equipment) ??
        throw new InvalidOperationException("No deterministic Snatch public-equipment boundary was found.");
    var (snatchEquipmentAction, snatchEquipment) = GetPublicTarget(
        snatchEquipmentGame,
        LegalActionKind.Snatch,
        CardZoneKind.Equipment);
    AssertPublicTargetResolution(
        snatchEquipmentGame,
        LegalActionKind.Snatch,
        snatchEquipmentAction.CardId!.Value,
        snatchEquipment,
        CardZoneKind.Equipment);

    var snatchJudgmentGame = FindGameWithPublicTarget(
        LegalActionKind.Snatch,
        CardZoneKind.Judgment) ??
        throw new InvalidOperationException("No deterministic Snatch public-judgment boundary was found.");
    var (snatchJudgmentAction, snatchJudgment) = GetPublicTarget(
        snatchJudgmentGame,
        LegalActionKind.Snatch,
        CardZoneKind.Judgment);
    AssertPublicTargetResolution(
        snatchJudgmentGame,
        LegalActionKind.Snatch,
        snatchJudgmentAction.CardId!.Value,
        snatchJudgment,
        CardZoneKind.Judgment);
}

static void EquipmentFlow()
{
    Equal(27, EquipmentCatalog.Implemented.Count);
    Equal(EquipmentSlot.Weapon, EquipmentCatalog.Get(CardKind.Crossbow).Slot);
    Equal(1, EquipmentCatalog.Get(CardKind.Crossbow).WeaponAttackRange);
    Equal(int.MaxValue, EquipmentCatalog.Get(CardKind.Crossbow).SlashLimitBonus);
    True(EquipmentCatalog.Get(CardKind.QinggangSword).IgnoresArmor);
    Equal(EquipmentSlot.Weapon, EquipmentCatalog.Get(CardKind.QinggangSword).Slot);
    Equal(2, EquipmentCatalog.Get(CardKind.QinggangSword).WeaponAttackRange);
    Equal(EquipmentSlot.Weapon, EquipmentCatalog.Get(CardKind.StoneAxe).Slot);
    Equal(3, EquipmentCatalog.Get(CardKind.StoneAxe).WeaponAttackRange);
    Equal(EquipmentSlot.Weapon, EquipmentCatalog.Get(CardKind.ZhangbaSerpentSpear).Slot);
    Equal(3, EquipmentCatalog.Get(CardKind.ZhangbaSerpentSpear).WeaponAttackRange);
    Equal(EquipmentSlot.Weapon, EquipmentCatalog.Get(CardKind.CixiongDoubleSwords).Slot);
    Equal(2, EquipmentCatalog.Get(CardKind.CixiongDoubleSwords).WeaponAttackRange);
    Equal(EquipmentSlot.Weapon, EquipmentCatalog.Get(CardKind.QinglongCrescentBlade).Slot);
    Equal(3, EquipmentCatalog.Get(CardKind.QinglongCrescentBlade).WeaponAttackRange);
    Equal(EquipmentSlot.Weapon, EquipmentCatalog.Get(CardKind.IceSword).Slot);
    Equal(2, EquipmentCatalog.Get(CardKind.IceSword).WeaponAttackRange);
    Equal(EquipmentSlot.Weapon, EquipmentCatalog.Get(CardKind.QilinBow).Slot);
    Equal(5, EquipmentCatalog.Get(CardKind.QilinBow).WeaponAttackRange);
    Equal(EquipmentSlot.Weapon, EquipmentCatalog.Get(CardKind.FangtianHalberd).Slot);
    Equal(4, EquipmentCatalog.Get(CardKind.FangtianHalberd).WeaponAttackRange);
    Equal(EquipmentSlot.Weapon, EquipmentCatalog.Get(CardKind.GudingBlade).Slot);
    Equal(2, EquipmentCatalog.Get(CardKind.GudingBlade).WeaponAttackRange);
    Equal(EquipmentSlot.Weapon, EquipmentCatalog.Get(CardKind.ZhuqueFan).Slot);
    Equal(4, EquipmentCatalog.Get(CardKind.ZhuqueFan).WeaponAttackRange);
    Equal(EquipmentSlot.Armor, EquipmentCatalog.Get(CardKind.BaguaFormation).Slot);
    True(EquipmentCatalog.Get(CardKind.RenwangShield).BlocksBlackSlash);
    Equal(EquipmentSlot.Armor, EquipmentCatalog.Get(CardKind.RenwangShield).Slot);
    Equal(EquipmentSlot.OffensiveHorse, EquipmentCatalog.Get(CardKind.OffensiveHorse).Slot);
    Equal(EquipmentSlot.DefensiveHorse, EquipmentCatalog.Get(CardKind.DefensiveHorse).Slot);
    Equal(EquipmentSlot.OffensiveHorse, EquipmentCatalog.Get(CardKind.Dawan).Slot);
    Equal(-1, EquipmentCatalog.Get(CardKind.Dawan).OutgoingDistanceModifier);
    Equal(EquipmentSlot.OffensiveHorse, EquipmentCatalog.Get(CardKind.Zixing).Slot);
    Equal(-1, EquipmentCatalog.Get(CardKind.Zixing).OutgoingDistanceModifier);
    Equal(EquipmentSlot.DefensiveHorse, EquipmentCatalog.Get(CardKind.Dilu).Slot);
    Equal(1, EquipmentCatalog.Get(CardKind.Dilu).IncomingDistanceModifier);
    Equal(EquipmentSlot.DefensiveHorse, EquipmentCatalog.Get(CardKind.Zhaohuangfeidian).Slot);
    Equal(1, EquipmentCatalog.Get(CardKind.Zhaohuangfeidian).IncomingDistanceModifier);
    Equal(EquipmentSlot.DefensiveHorse, EquipmentCatalog.Get(CardKind.Hualiu).Slot);
    Equal(1, EquipmentCatalog.Get(CardKind.Hualiu).IncomingDistanceModifier);
    Equal(EquipmentSlot.Treasure, EquipmentCatalog.Get(CardKind.JadeSeal).Slot);
    Equal(EquipmentSlot.Treasure, EquipmentCatalog.Get(CardKind.WoodenOx).Slot);

    GameEngine? selectedGame = null;
    int? selectedSeed = null;
    CardSnapshot? firstCrossbow = null;
    CardSnapshot? secondCrossbow = null;
    CardSnapshot? slash = null;
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
        if (result.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }

        var hand = game.State.Players.Single(player => player.Seat == 0).Hand;
        var crossbows = hand.Where(card => card.Kind == CardKind.Crossbow).ToArray();
        var slashCard = hand.FirstOrDefault(card => card.Kind == CardKind.Slash);
        if (crossbows.Length >= 2 && slashCard is not null)
        {
            selectedGame = game;
            selectedSeed = seed;
            firstCrossbow = crossbows[0];
            secondCrossbow = crossbows[1];
            slash = slashCard;
        }
    }

    if (selectedGame is null || selectedSeed is null || firstCrossbow is null || secondCrossbow is null || slash is null)
    {
        throw new InvalidOperationException("No deterministic equipment replacement boundary was found.");
    }

    var gameWithEquipment = selectedGame;
    var firstEquipAction = gameWithEquipment.GetHumanLegalActions().Single(action =>
        action.Kind == LegalActionKind.Equip && action.CardId == firstCrossbow.Id);
    True(firstEquipAction.TargetSeat is null);
    var firstResult = gameWithEquipment.DriveHumanPlay(
        firstCrossbow.Id,
        targetSeat: null,
        advanceToHumanBoundary: true);
    Equal(EngineStatus.AwaitingHumanPlay, firstResult.Status);

    var afterFirst = gameWithEquipment.CreateSnapshot(0, revealAll: true);
    var firstEquipment = afterFirst.Players.Single(player => player.Seat == 0).Equipment;
    Equal(1, firstEquipment.Count);
    Equal(firstCrossbow.Id, firstEquipment.Single().Id);
    Equal(1, gameWithEquipment.GetAttackRange(0));
    Equal(2, gameWithEquipment.GetCombatDistance(0, 2));
    False(gameWithEquipment.GetHumanLegalActions().Any(action =>
        action.Kind == LegalActionKind.Slash &&
        action.CardId == slash.Id &&
        action.TargetSeat == 2));
    var slashCountField = typeof(GameEngine).GetField(
        "_slashCountThisTurn",
        BindingFlags.NonPublic | BindingFlags.Instance) ??
        throw new InvalidOperationException("Slash count field not found.");
    slashCountField.SetValue(gameWithEquipment, 1);
    TrueWithMessage(
        gameWithEquipment.GetHumanLegalActions().Any(action =>
            action.Kind == LegalActionKind.Slash && action.CardId == slash.Id),
        "Crossbow keeps Slash legal after the ordinary per-turn limit is reached");
    True(gameWithEquipment.CreateSnapshot(2).Players.Single(player => player.Seat == 0)
        .Equipment.Any(card => card.Id == firstCrossbow.Id));

    var changedEvents = gameWithEquipment.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<EquipmentChangedEvent>()
        .ToArray();
    var firstChanged = changedEvents.Single();
    Equal(0, firstChanged.PlayerSeat);
    Equal(EquipmentSlot.Weapon, firstChanged.Slot);
    Equal(firstCrossbow.Id, firstChanged.CardId);
    Equal<int?>(null, firstChanged.ReplacedCardId);
    True(gameWithEquipment.CardMovements.Any(movement =>
        movement.CardId == firstCrossbow.Id &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.EquipmentUse));
    True(gameWithEquipment.CardMovements.Any(movement =>
        movement.CardId == firstCrossbow.Id &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.Equipment(0) &&
        movement.Reason == CardMoveReasons.EquipmentEnter));

    var secondResult = gameWithEquipment.DriveHumanPlay(
        secondCrossbow.Id,
        targetSeat: null,
        advanceToHumanBoundary: true);
    Equal(EngineStatus.AwaitingHumanPlay, secondResult.Status);
    var afterReplacement = gameWithEquipment.CreateSnapshot(0, revealAll: true);
    var finalEquipment = afterReplacement.Players.Single(player => player.Seat == 0).Equipment;
    Equal(1, finalEquipment.Count);
    Equal(secondCrossbow.Id, finalEquipment.Single().Id);
    Equal(1, gameWithEquipment.GetAttackRange(0));
    var allChangedEvents = gameWithEquipment.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<EquipmentChangedEvent>()
        .ToArray();
    Equal(2, allChangedEvents.Length);
    var replacementEvent = gameWithEquipment.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<EquipmentChangedEvent>()
        .Single(eventItem => eventItem.CardId == secondCrossbow.Id);
    Equal(firstCrossbow.Id, replacementEvent.ReplacedCardId);
    True(gameWithEquipment.CreateCardZoneDiagnostics().Any(card =>
        card.CardId == firstCrossbow.Id && card.Location == CardLocation.DiscardPile));
    True(gameWithEquipment.CardMovements.Any(movement =>
        movement.CardId == firstCrossbow.Id &&
        movement.From == CardLocation.Equipment(0) &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.EquipmentReplace));

    GameEngine? horseGame = null;
    CardSnapshot? horse = null;
    for (var seed = 1; seed <= 2_048 && horseGame is null; seed++)
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

        horse = game.State.Players.Single(player => player.Seat == 0).Hand
            .FirstOrDefault(card => card.Kind == CardKind.OffensiveHorse);
        if (horse is not null)
        {
            horseGame = game;
        }
    }

    if (horseGame is null || horse is null)
    {
        throw new InvalidOperationException("No deterministic offensive-horse boundary was found.");
    }

    Equal(2, horseGame.GetCombatDistance(0, 2));
    horseGame.DriveHumanPlay(horse.Id, targetSeat: null, advanceToHumanBoundary: true);
    Equal(1, horseGame.GetCombatDistance(0, 2));
    Equal(2, horseGame.GetSeatDistance(0, 2));
    True(horseGame.GetCombatDistance(0, 2) <= horseGame.GetSeatDistance(0, 2));

    AssertCardInventory(gameWithEquipment);
    AssertCardInventory(horseGame);
}

static void BaguaJudgmentFlow()
{
    static (GameEngine Game, PendingDecision Prompt, PromptChoice Choice, JudgmentResolvedEvent Judgment)? FindBoundary(
        bool desiredSuccess)
    {
        for (var seed = 1; seed <= 8_192; seed++)
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
            var bagua = game.State.Players.Single(player => player.Seat == 0).Hand
                .FirstOrDefault(card => card.Kind == CardKind.BaguaFormation);
            if (result.Status != EngineStatus.AwaitingHumanPlay || bagua is null)
            {
                continue;
            }

            result = game.DriveHumanPlay(bagua.Id, targetSeat: null, advanceToHumanBoundary: true);
            result = game.DriveHumanEndPlay(advanceToHumanBoundary: false);
            var steps = 0;
            while (result.Status != EngineStatus.Completed && steps++ < 1_500)
            {
                if (result.Status == EngineStatus.AwaitingHumanResponse)
                {
                    var prompt = game.PendingDecision ??
                        throw new InvalidOperationException("The response status has no prompt.");
                    var baguaChoice = prompt.Kind == DecisionKind.RespondDodge &&
                                      prompt.IncomingCard is
                                          CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash
                        ? prompt.Choices.FirstOrDefault(choice =>
                            choice.Parameters.GetValueOrDefault("response") == "bagua")
                        : null;
                    if (baguaChoice is not null)
                    {
                        var eventCount = game.Events.Count;
                        var accepted = game.Submit(new AnswerPromptCommand(
                            ActorSeat: 0,
                            Prompt: prompt.PromptId,
                            Choice: baguaChoice.Id,
                            ExpectedRevision: game.Revision));
                        TrueWithMessage(accepted.Accepted, "Bagua prompt choice accepted");
                        for (var judgmentStep = 0; judgmentStep < 64 &&
                             !game.Events.Skip(eventCount).Any(eventItem =>
                                 eventItem.Payload is JudgmentResolvedEvent resolved &&
                                 resolved.Reason == JudgmentReasons.BaguaDefense); judgmentStep++)
                        {
                            if (game.PendingDecision?.Kind == DecisionKind.ProgramTrigger)
                            {
                                _ = ResolveIncidentalProgramTrigger(game);
                            }
                            else if (game.PendingDecision is { Kind: DecisionKind.ProgramJudgmentReplacement or DecisionKind.ProgramJudgmentReplacement })
                            {
                                _ = game.DriveHumanRespond(useDodge: false, advanceToHumanBoundary: false);
                            }
                            else
                            {
                                _ = game.DriveAdvanceOneStep();
                            }
                        }
                        var judgment = game.Events
                            .Skip(eventCount)
                            .Select(eventItem => eventItem.Payload)
                            .OfType<JudgmentResolvedEvent>()
                            .Single(candidate => candidate.Reason == JudgmentReasons.BaguaDefense);
                        if (judgment.Succeeded == desiredSuccess)
                        {
                            return (game, prompt, baguaChoice, judgment);
                        }

                        break;
                    }

                    result = prompt.Kind switch
                    {
                        DecisionKind.ProgramTrigger => ResolveIncidentalProgramTrigger(game),
                        DecisionKind.RespondSlash => game.DriveHumanRespondSlash(
                            useSlash: false,
                            advanceToHumanBoundary: false),
                        DecisionKind.RespondDodge => game.DriveHumanRespond(
                            useDodge: false,
                            advanceToHumanBoundary: false),
                        _ => game.DriveHumanRespond(
                            useDodge: false,
                            advanceToHumanBoundary: false)
                    };
                    continue;
                }

                result = result.Status switch
                {
                    EngineStatus.AwaitingHumanPlay => game.DriveHumanEndPlay(
                        advanceToHumanBoundary: false),
                    EngineStatus.AwaitingHumanDying => game.DriveHumanRespondDying(
                        usePeach: false,
                        advanceToHumanBoundary: false),
                    EngineStatus.AwaitingHumanCardSelection => ResolveFirstHarvestChoice(game),
                    _ => game.DriveAdvanceOneStep()
                };
            }
        }

        return null;
    }

    var successful = FindBoundary(desiredSuccess: true) ??
        throw new InvalidOperationException("No deterministic successful Bagua judgment boundary was found.");
    var failed = FindBoundary(desiredSuccess: false) ??
        throw new InvalidOperationException("No deterministic failed Bagua judgment boundary was found.");

    foreach (var outcome in new[] { successful, failed })
    {
        Equal(DecisionKind.RespondDodge, outcome.Prompt.Kind);
        Equal(CardKind.Dodge, outcome.Prompt.RequiredCardKind);
        Equal("bagua", outcome.Choice.Parameters["response"]);
        Equal(0, outcome.Choice.Cards.Count);
        Equal(JudgmentReasons.BaguaDefense, outcome.Judgment.Reason);
        NotNull(outcome.Judgment.CardId);
        NotNull(outcome.Judgment.CardKind);
        NotNull(outcome.Judgment.Suit);
        TrueWithMessage(outcome.Judgment.CardId > 0, "judgment has a physical card id");
        TrueWithMessage(
            outcome.Judgment.Suit is Suit.Heart or Suit.Diamond or Suit.Spade or Suit.Club,
            "judgment has a valid suit");
        TrueWithMessage(
            outcome.Game.ResolutionStack.All(frame => frame is not JudgmentFrame),
            "Bagua leaves no open judgment frame");

        var cardId = outcome.Judgment.CardId!.Value;
        var cardKind = outcome.Judgment.CardKind!.Value;
        var judgmentMovement = outcome.Game.CardMovements.Single(movement =>
            movement.CardId == cardId &&
            movement.Reason == CardMoveReasons.JudgmentReveal);
        Equal(CardLocation.DrawPile, judgmentMovement.From);
        Equal(CardLocation.Judgment(0), judgmentMovement.To);
        True(outcome.Game.CardMovements.Any(movement =>
            movement.CardId == cardId &&
            movement.Reason == CardMoveReasons.JudgmentFinish &&
            movement.From == CardLocation.Judgment(0) &&
            movement.To == CardLocation.DiscardPile));
        Equal(CardLocation.DiscardPile, outcome.Game.CreateCardZoneDiagnostics()
            .Single(card => card.CardId == cardId).Location);

        var requestedEvent = outcome.Game.Events
            .Select(eventItem => eventItem.Payload)
            .OfType<JudgmentRequestedEvent>()
            .Single(requested => requested.ResolutionId == outcome.Judgment.ResolutionId);
        Equal(outcome.Judgment.ParentResolutionId, requestedEvent.ParentResolutionId);
        TrueWithMessage(
            requestedEvent.SourceCard is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash,
            "Bagua judgment source is a Slash");
        TrueWithMessage(outcome.Game.Events.Any(eventItem =>
            eventItem.Payload is JudgmentResolvedEvent resolved &&
            resolved.ResolutionId == outcome.Judgment.ResolutionId &&
            resolved.CardId == cardId &&
            resolved.CardKind == cardKind &&
            resolved.Succeeded == outcome.Judgment.Succeeded),
            "judgment result is committed as a typed event");
        TrueWithMessage(outcome.Game.Events.Any(eventItem =>
            eventItem.Payload is CardMovedEvent moved &&
            moved.CardId == cardId &&
            moved.From == CardLocation.DrawPile &&
            moved.To == CardLocation.Judgment(0) &&
            moved.Reason == CardMoveReasons.JudgmentReveal),
            "judgment reveal is committed as a typed movement event");

        var judgmentEventIndex = outcome.Game.Events
            .Select((eventItem, index) => (eventItem, index))
            .Single(entry =>
                entry.eventItem.Payload is JudgmentResolvedEvent resolved &&
                resolved.ResolutionId == outcome.Judgment.ResolutionId)
            .index;
        var nextEvents = outcome.Game.Events
            .Skip(judgmentEventIndex + 1)
            .Select(eventItem => eventItem.Payload)
            .ToArray();
        TrueWithMessage(
            nextEvents.FirstOrDefault() is CardMovedEvent moved &&
            moved.CardId == cardId &&
            moved.Reason == CardMoveReasons.JudgmentFinish,
            "judgment card is finished before the Slash outcome");
        var nextResolutionEvent = nextEvents.Skip(1).FirstOrDefault();
        Equal(!outcome.Judgment.Succeeded, nextResolutionEvent is DamageRequestedEvent);
        AssertCardInventory(outcome.Game);
    }

    var serializedFrame = JsonSerializer.Serialize(new ResolutionFrame[]
    {
        new JudgmentFrame(
            Id: 9,
            ParentFrameId: 3,
            TargetSeat: 0,
            Reason: JudgmentReasons.BaguaDefense,
            CardId: 4,
            CardKind: CardKind.Dodge,
            Suit: Suit.Heart,
            Succeeded: true)
    });
    TrueWithMessage(
        serializedFrame.Contains("\"judgment\"", StringComparison.Ordinal),
        "JudgmentFrame uses the polymorphic judgment discriminator");
}

static void QinggangBypassesBagua()
{
    var controlledKinds = Enumerable.Repeat(CardKind.Dodge, 81).ToArray();
    controlledKinds[0] = CardKind.QinggangSword;
    controlledKinds[1] = CardKind.Slash;
    for (var index = 2; index < 32; index++)
    {
        controlledKinds[index] = CardKind.BaguaFormation;
    }

    var controlledCardIds = Enumerable.Range(0, controlledKinds.Length)
        .Select(index => $"test-controlled:card-{index + 1}")
        .ToArray();
    var registry = ContentRegistry.Build(
        new StandardContentPackage(),
        new SyntheticPackage(
            "test-controlled",
            builder =>
            {
                for (var index = 0; index < controlledKinds.Length; index++)
                {
                    var definition = CardCatalog.Get(controlledKinds[index]);
                    builder.AddCard(new ContentCardDefinition(
                        controlledCardIds[index],
                        definition.DisplayName,
                        definition.CategoryName,
                        definition.Description,
                        controlledKinds[index]));
                }

                builder.AddDeck(new ContentDeckRecipe(
                    "test-controlled:deck",
                    "青釭剑判定绕过测试牌堆",
                    InitialHandSize: 4,
                    DrawPerTurn: 2,
                    controlledCardIds
                        .Select(cardId => new ContentDeckCardCount(cardId, 1))
                        .ToArray()));
            }));
    GameEngine? game = null;
    int? selectedSeed = null;
    CardSnapshot? qinggang = null;
    CardSnapshot? slash = null;
    LegalAction? slashAction = null;
    var targetSeat = -1;
    for (var seed = 1; seed <= 4_096 && game is null; seed++)
    {
        var candidate = GameEngine.CreateStandard(
            new GameOptions
            {
                AdvanceAfterHumanCommands = false,
                UseInteractiveDiscard = false,
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                DeckId = "test-controlled:deck",
                MaxTurns = 60
            },
            registry);
        var result = candidate.DriveStart();
        if (result.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }

        var humanHand = candidate.CreateSnapshot(0, revealAll: true).Players
            .Single(player => player.Seat == 0).Hand;
        var candidateQinggang = humanHand.FirstOrDefault(card => card.Kind == CardKind.QinggangSword);
        var candidateSlash = humanHand.FirstOrDefault(card => card.Kind == CardKind.Slash);
        if (candidateQinggang is null || candidateSlash is null)
        {
            continue;
        }

        candidate.DriveHumanPlay(candidateQinggang.Id, targetSeat: null, advanceToHumanBoundary: false);
        candidate.DriveAdvance();
        result = candidate.DriveHumanEndPlay(advanceToHumanBoundary: true);
        if (result.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }

        var afterAiTurns = candidate.CreateSnapshot(0, revealAll: true);
        var candidateAction = candidate.GetHumanLegalActions().FirstOrDefault(action =>
            action.Kind == LegalActionKind.Slash &&
            action.CardId == candidateSlash.Id &&
            action.TargetSeat is { } seat &&
            afterAiTurns.Players.Single(player => player.Seat == seat).Equipment
                .Any(card => card.Kind == CardKind.BaguaFormation) &&
            afterAiTurns.Players.Single(player => player.Seat == seat).Hand
                .Any(card => card.Kind == CardKind.Dodge));
        if (candidateAction is null)
        {
            continue;
        }

        game = candidate;
        selectedSeed = seed;
        qinggang = candidateQinggang;
        slash = candidateSlash;
        slashAction = candidateAction;
        targetSeat = candidateAction.TargetSeat!.Value;
    }

    if (game is null || selectedSeed is null || qinggang is null || slash is null || slashAction is null)
    {
        throw new InvalidOperationException("No deterministic Qinggang and Bagua bypass boundary was found.");
    }

    TrueWithMessage(targetSeat >= 0, "an AI target has equipped Bagua before the human turn");
    TrueWithMessage(
        game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0)
            .Equipment.Any(card => card.Kind == CardKind.QinggangSword),
        "Qinggang enters the public weapon slot");
    Equal(2, game.GetAttackRange(0));
    var eventCount = game.Events.Count;
    var slashResult = game.DriveHumanPlay(
        slashAction.CardId!.Value,
        slashAction.TargetSeat,
        advanceToHumanBoundary: false);
    Equal(EngineStatus.Running, slashResult.Status);

    var frame = game.ResolutionStack.OfType<CardUseFrame>().Single(cardUse =>
        cardUse.CardId == slash.Id);
    TrueWithMessage(frame.IgnoresArmor, "the in-flight CardUseFrame records Qinggang's modifier");
    var targetView = game.CreateSnapshot(targetSeat);
    Equal(DecisionKind.RespondDodge, targetView.PendingDecision!.Kind);
    TrueWithMessage(
        targetView.PendingDecision.Choices.All(choice =>
            choice.Parameters.GetValueOrDefault("response") != "bagua"),
        "Qinggang removes Bagua from the target response choices");

    var declared = game.Events
        .Skip(eventCount)
        .Select(eventItem => eventItem.Payload)
        .OfType<CardUseDeclaredEvent>()
        .Single(eventItem => eventItem.CardId == slash.Id);
    TrueWithMessage(declared.IgnoresArmor, "CardUseDeclaredEvent records Qinggang's modifier");
    var used = game.Events
        .Skip(eventCount)
        .Select(eventItem => eventItem.Payload)
        .OfType<CardUsedEvent>()
        .Single(eventItem => eventItem.CardId == slash.Id);
    TrueWithMessage(used.IgnoresArmor, "CardUsedEvent records Qinggang's modifier");
    TrueWithMessage(
        !game.Events.Skip(eventCount).Any(eventItem =>
            eventItem.Payload is JudgmentRequestedEvent or JudgmentResolvedEvent),
        "Qinggang does not open a Bagua judgment");
    TrueWithMessage(
        JsonSerializer.Serialize(frame).Contains("\"IgnoresArmor\":true", StringComparison.Ordinal),
        "Qinggang modifier survives frame serialization");

    var targetResponse = game.DriveAdvanceOneStep();
    Equal(EngineStatus.Running, targetResponse.Status);
    Equal(0, game.ResolutionStack.Count);
    TrueWithMessage(
        game.Events.Skip(eventCount).Any(eventItem =>
            eventItem.Payload is CardRespondedEvent responded &&
            responded.ResponderSeat == targetSeat),
        "the target may still use a physical Dodge, but never Bagua");
    AssertCardInventory(game);
}

static void RenwangShieldFlow()
{
    var controlledKinds = Enumerable.Repeat(CardKind.Dodge, 81).ToArray();
    controlledKinds[0] = CardKind.RenwangShield;
    for (var index = 1; index < 25; index++)
    {
        controlledKinds[index] = CardKind.Slash;
    }

    var controlledCardIds = Enumerable.Range(0, controlledKinds.Length)
        .Select(index => $"test-renwang:card-{index + 1}")
        .ToArray();
    var registry = ContentRegistry.Build(
        new StandardContentPackage(),
        new SyntheticPackage(
            "test-renwang",
            builder =>
            {
                for (var index = 0; index < controlledKinds.Length; index++)
                {
                    var definition = CardCatalog.Get(controlledKinds[index]);
                    builder.AddCard(new ContentCardDefinition(
                        controlledCardIds[index],
                        definition.DisplayName,
                        definition.CategoryName,
                        definition.Description,
                        controlledKinds[index]));
                }

                builder.AddDeck(new ContentDeckRecipe(
                    "test-renwang:deck",
                    "仁王盾黑色杀阻挡测试牌堆",
                    InitialHandSize: 4,
                    DrawPerTurn: 2,
                    controlledCardIds
                        .Select(cardId => new ContentDeckCardCount(cardId, 1))
                        .ToArray()));
            }));

    static (GameEngine Game, CardSnapshot BlackSlash, CardSnapshot RedSlash, int ShieldSeat)?
        FindBoundary(ContentRegistry registry, int rulesVersion, bool expectBlackLegal)
    {
        for (var seed = 1; seed <= 8_192; seed++)
        {
            var candidate = GameEngine.CreateStandard(
                new GameOptions
                {
                    AdvanceAfterHumanCommands = false,
                    UseInteractiveDiscard = false,
                    Seed = seed,
                    HumanSeat = 0,
                    HumanRole = Role.Lord,
                    DeckId = "test-renwang:deck",
                    MaxTurns = 60
                },
                registry);
            if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
            {
                candidate = GameReplay.Restore(
                    candidate.CreateCheckpoint() with { RulesVersion = rulesVersion },
                    registry);
            }

            var started = candidate.DriveStart();
            if (started.Status != EngineStatus.AwaitingHumanPlay)
            {
                continue;
            }

            var initial = candidate.CreateSnapshot(0, revealAll: true);
            var human = initial.Players.Single(player => player.Seat == 0);
            var shieldOwner = initial.Players.SingleOrDefault(player =>
                player.Seat != 0 &&
                player.IsAlive &&
                player.Hand.Any(card => card.Kind == CardKind.RenwangShield) &&
                player.Hand.All(card => card.Kind != CardKind.Slash));
            var candidateBlackSlash = human.Hand.FirstOrDefault(card =>
                card.Kind == CardKind.Slash &&
                card.Suit is Suit.Spade or Suit.Club);
            var candidateRedSlash = human.Hand.FirstOrDefault(card =>
                card.Kind == CardKind.Slash &&
                card.Suit is Suit.Heart or Suit.Diamond);
            if (shieldOwner is null || candidateBlackSlash is null || candidateRedSlash is null)
            {
                continue;
            }

            var nextHumanTurn = candidate.DriveHumanEndPlay(advanceToHumanBoundary: true);
            if (nextHumanTurn.Status != EngineStatus.AwaitingHumanPlay)
            {
                continue;
            }

            var afterAiTurns = candidate.CreateSnapshot(0, revealAll: true);
            var target = afterAiTurns.Players.Single(player => player.Seat == shieldOwner.Seat);
            if (!target.IsAlive ||
                !target.Equipment.Any(card => card.Kind == CardKind.RenwangShield))
            {
                continue;
            }

            var humanAfterAiTurns = afterAiTurns.Players.Single(player => player.Seat == 0);
            var blackAfterAiTurns = humanAfterAiTurns.Hand.FirstOrDefault(card =>
                card.Id == candidateBlackSlash.Id);
            var redAfterAiTurns = humanAfterAiTurns.Hand.FirstOrDefault(card =>
                card.Id == candidateRedSlash.Id);
            if (blackAfterAiTurns is null || redAfterAiTurns is null)
            {
                continue;
            }

            var legal = candidate.GetHumanLegalActions();
            var hasBlackAction = legal.Any(action =>
                action.Kind == LegalActionKind.Slash &&
                action.CardId == blackAfterAiTurns.Id &&
                action.TargetSeat == shieldOwner.Seat);
            var hasRedAction = legal.Any(action =>
                action.Kind == LegalActionKind.Slash &&
                action.CardId == redAfterAiTurns.Id &&
                action.TargetSeat == shieldOwner.Seat);
            if (hasBlackAction != expectBlackLegal || !hasRedAction)
            {
                continue;
            }

            return (candidate, blackAfterAiTurns, redAfterAiTurns, shieldOwner.Seat);
        }

        return null;
    }

    var formal = FindBoundary(
        registry,
        GameCheckpoint.CurrentRulesVersion,
        expectBlackLegal: true) ??
        throw new InvalidOperationException("No deterministic formal Renwang Shield boundary was found.");
    var game = formal.Game;
    var blackSlash = formal.BlackSlash;
    var redSlash = formal.RedSlash;
    var shieldSeat = formal.ShieldSeat;

    var targetView = game.CreateSnapshot(0).Players.Single(player => player.Seat == shieldSeat);
    TrueWithMessage(
        targetView.Equipment.Any(card => card.Kind == CardKind.RenwangShield),
        "Renwang Shield remains visible in the ordinary player snapshot");
    TrueWithMessage(
        game.GetHumanLegalActions().Any(action =>
            action.Kind == LegalActionKind.Slash &&
            action.CardId == blackSlash.Id &&
            action.TargetSeat == shieldSeat),
        "formal rules keep black Slash in the published legal actions");
    TrueWithMessage(
        game.GetHumanLegalActions().Any(action =>
            action.Kind == LegalActionKind.Slash &&
            action.CardId == redSlash.Id &&
            action.TargetSeat == shieldSeat),
        "red Slash remains a legal target against Renwang Shield");

    var targetHp = targetView.Hp;
    var eventCount = game.Events.Count;
    var accepted = game.DriveHumanPlay(
        blackSlash.Id,
        shieldSeat,
        advanceToHumanBoundary: false);
    Equal(EngineStatus.Running, accepted.Status);
    TrueWithMessage(
        game.Events.Skip(eventCount).Any(eventItem =>
            eventItem.Payload is ArmorEffectAppliedEvent armor &&
            armor.ArmorCard == CardKind.RenwangShield &&
            armor.SourceSeat == 0 &&
            armor.TargetSeat == shieldSeat &&
            armor.IncomingCard == CardKind.Slash),
        "formal Renwang resolution publishes a typed armor event");
    TrueWithMessage(
        !game.Events.Skip(eventCount).Any(eventItem =>
            eventItem.Payload is ResponseRequestedEvent or DamageRequestedEvent),
        "an ineffective black Slash opens neither Dodge nor damage");
    TrueWithMessage(
        game.Events.Skip(eventCount).Any(eventItem =>
            eventItem.Payload is CardUseFinishedEvent finished &&
            finished.CardId == blackSlash.Id),
        "the ineffective Slash still finishes its ordinary card-use lifecycle");
    Equal(targetHp, game.CreateSnapshot(0).Players.Single(player => player.Seat == shieldSeat).Hp);
    Equal(0, game.ResolutionStack.Count);
    Equal(
        CardLocation.DiscardPile,
        game.CreateCardZoneDiagnostics().Single(card => card.CardId == blackSlash.Id).Location);

    AssertCardInventory(game);
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

static void AttributeSlashFlow()
{
    GameEngine? selectedGame = null;
    LegalAction? selectedAction = null;
    CardSnapshot? selectedCard = null;
    for (var seed = 1; seed <= 2_048 && selectedGame is null; seed++)
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

        var self = game.CreateSnapshot(0).Players.Single(player => player.Seat == 0);
        foreach (var action in game.GetHumanLegalActions()
                     .Where(candidate =>
                         candidate.Kind == LegalActionKind.Slash &&
                         candidate.TargetSeat is not null))
        {
            var card = self.Hand.Single(candidate => candidate.Id == action.CardId);
            if (card.Kind is not (CardKind.FireSlash or CardKind.ThunderSlash))
            {
                continue;
            }

            var target = game.CreateSnapshot(action.TargetSeat!.Value)
                .Players.Single(player => player.Seat == action.TargetSeat.Value);
            if (target.Hand.Any(candidate => candidate.Kind == CardKind.Dodge) ||
                target.Skills?.Any(skill => IsDamageTriggerSkill(skill.ContentId)) == true ||
                target.Skills?.Any(skill => skill.ContentId == "standard:longdan") == true)
            {
                continue;
            }

            selectedGame = game;
            selectedAction = action;
            selectedCard = card;
            break;
        }
    }

    if (selectedGame is null || selectedAction is null || selectedCard is null)
    {
        throw new InvalidOperationException("No deterministic elemental Slash boundary was found.");
    }

    var gameWithAttributeSlash = selectedGame!;
    var actionToUse = selectedAction!;
    var attackCard = selectedCard!;
    var targetSeat = actionToUse.TargetSeat!.Value;
    var expectedNature = attackCard.Kind == CardKind.FireSlash
        ? DamageNature.Fire
        : DamageNature.Thunder;
    var expectedLabel = expectedNature == DamageNature.Fire ? "火焰" : "雷电";

    var resultAfterUse = gameWithAttributeSlash.DriveHumanPlay(
        actionToUse.CardId!.Value,
        targetSeat,
        advanceToHumanBoundary: false);

    Equal(EngineStatus.Running, resultAfterUse.Status);
    Equal(0, resultAfterUse.State.ProcessingCardCount);
    Equal(0, gameWithAttributeSlash.ResolutionStack.Count);

    var events = gameWithAttributeSlash.Events.Select(eventItem => eventItem.Payload).ToArray();
    var declared = events
        .OfType<CardUseDeclaredEvent>()
        .Single(eventItem => eventItem.CardId == attackCard.Id);
    Equal(attackCard.Kind, declared.CardKind);

    var requested = events
        .OfType<DamageRequestedEvent>()
        .Single(eventItem => eventItem.SourceCard == attackCard.Kind);
    Equal(expectedNature, requested.Nature);
    Equal(expectedNature, events
        .OfType<DamageAppliedEvent>()
        .Single(eventItem => eventItem.SourceSeat == 0 && eventItem.TargetSeat == targetSeat)
        .Nature);
    Equal(expectedNature, events
        .OfType<AfterDamageEvent>()
        .Single(eventItem => eventItem.ResolutionId == requested.ResolutionId)
        .Nature);
    True(events.Any(eventItem =>
        eventItem is CardUseFinishedEvent finished &&
        finished.CardId == attackCard.Id &&
        finished.CardKind == attackCard.Kind));
    True(gameWithAttributeSlash.Log.Any(entry =>
        entry.Type == "Damage" &&
        entry.Message.Contains($"1 点{expectedLabel}伤害", StringComparison.Ordinal)));

    AssertCardInventory(gameWithAttributeSlash);
}

static void AlcoholFlow()
{
    GameEngine? selectedGame = null;
    LegalAction? alcoholAction = null;
    LegalAction? slashAction = null;
    CardSnapshot? slashCard = null;
    for (var seed = 1; seed <= 4_096 && selectedGame is null; seed++)
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

        var self = game.CreateSnapshot(0).Players.Single(player => player.Seat == 0);
        var candidateAlcohol = game.GetHumanLegalActions()
            .FirstOrDefault(action => action.Kind == LegalActionKind.Alcohol);
        if (candidateAlcohol is null)
        {
            continue;
        }

        foreach (var candidateSlash in game.GetHumanLegalActions()
                     .Where(action =>
                         action.Kind == LegalActionKind.Slash &&
                         action.TargetSeat is not null))
        {
            var candidateCard = self.Hand.Single(card => card.Id == candidateSlash.CardId);
            var targetSeat = candidateSlash.TargetSeat!.Value;
            var target = game.CreateSnapshot(targetSeat).Players
                .Single(player => player.Seat == targetSeat);
            if (!IsSlashCard(candidateCard.Kind) ||
                target.Hand.Any(card => card.Kind == CardKind.Dodge) ||
                target.Skills?.Any(skill => IsDamageTriggerSkill(skill.ContentId)) == true ||
                target.Skills?.Any(skill => skill.ContentId == "standard:longdan") == true)
            {
                continue;
            }

            selectedGame = game;
            alcoholAction = candidateAlcohol;
            slashAction = candidateSlash;
            slashCard = candidateCard;
            break;
        }
    }

    if (selectedGame is null || alcoholAction is null || slashAction is null || slashCard is null)
    {
        throw new InvalidOperationException("No deterministic Alcohol and direct Slash boundary was found.");
    }

    var gameWithAlcohol = selectedGame!;
    var alcoholCardId = alcoholAction!.CardId!.Value;
    var slashCardSnapshot = slashCard!;
    var targetSeatForSlash = slashAction!.TargetSeat!.Value;
    var afterAlcohol = gameWithAlcohol.DriveHumanPlay(
        alcoholCardId,
        targetSeat: null,
        advanceToHumanBoundary: false);
    Equal(EngineStatus.Running, afterAlcohol.Status);
    True(afterAlcohol.State.Players.Single(player => player.Seat == 0).HasAlcoholEffect);
    Equal(0, afterAlcohol.State.ProcessingCardCount);
    Equal(1, gameWithAlcohol.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<AlcoholAppliedEvent>()
        .Count(eventItem => eventItem.SourceSeat == 0 && eventItem.DamageBonus == 1));
    True(gameWithAlcohol.CardMovements.Any(movement =>
        movement.CardId == alcoholCardId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Use));
    True(gameWithAlcohol.CardMovements.Any(movement =>
        movement.CardId == alcoholCardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.UseFinished));
    True(gameWithAlcohol.GetHumanLegalActions().All(action =>
        action.Kind != LegalActionKind.Alcohol));

    var alcoholPlayBoundary = gameWithAlcohol.DriveAdvance();
    Equal(EngineStatus.AwaitingHumanPlay, alcoholPlayBoundary.Status);
    var afterSlash = gameWithAlcohol.DriveHumanPlay(
        slashAction.CardId!.Value,
        targetSeatForSlash,
        advanceToHumanBoundary: false);
    False(afterSlash.State.Players.Single(player => player.Seat == 0).HasAlcoholEffect);
    Equal(0, afterSlash.State.ProcessingCardCount);

    var slashEvents = gameWithAlcohol.Events.Select(eventItem => eventItem.Payload).ToArray();
    var requested = slashEvents
        .OfType<DamageRequestedEvent>()
        .Single(eventItem => eventItem.SourceSeat == 0 && eventItem.TargetSeat == targetSeatForSlash);
    var applied = slashEvents
        .OfType<DamageAppliedEvent>()
        .Single(eventItem => eventItem.SourceSeat == 0 && eventItem.TargetSeat == targetSeatForSlash);
    var afterDamage = slashEvents
        .OfType<AfterDamageEvent>()
        .Single(eventItem => eventItem.ResolutionId == requested.ResolutionId);
    Equal(slashCardSnapshot.Kind, requested.SourceCard);
    Equal(2, requested.Amount);
    Equal(2, applied.Amount);
    Equal(2, afterDamage.Amount);
    True(gameWithAlcohol.Events.Any(eventItem =>
        eventItem.Payload is CardUseFinishedEvent finished &&
        finished.CardId == slashAction.CardId &&
        finished.CardKind == slashCardSnapshot.Kind));
    True(gameWithAlcohol.Log.Any(entry =>
        entry.Type == "Damage" && entry.Message.Contains("2 点", StringComparison.Ordinal)));
    AssertCardInventory(gameWithAlcohol);

    GameEngine? expiringGame = null;
    for (var seed = 1; seed <= 4_096 && expiringGame is null; seed++)
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

        var action = result.Status == EngineStatus.AwaitingHumanPlay
            ? game.GetHumanLegalActions().FirstOrDefault(candidate =>
                candidate.Kind == LegalActionKind.Alcohol)
            : null;
        if (action is null)
        {
            continue;
        }

        var openingTurn = result.State.TurnNumber;
        game.DriveHumanPlay(action.CardId!.Value, advanceToHumanBoundary: false);
        game.DriveAdvance();
        game.DriveHumanEndPlay(advanceToHumanBoundary: false);
        var nextBoundary = game.DriveAdvance();
        var boundarySteps = 0;
        while (nextBoundary.Status != EngineStatus.Completed &&
               !(nextBoundary.Status == EngineStatus.AwaitingHumanPlay &&
                 nextBoundary.State.CurrentSeat == 0 &&
                 nextBoundary.State.TurnNumber > openingTurn) &&
               boundarySteps++ < 2_000)
        {
            nextBoundary = nextBoundary.Status switch
            {
                EngineStatus.AwaitingHumanResponse => game.PendingDecision?.Kind == DecisionKind.RespondSlash
                    ? game.DriveHumanRespondSlash(useSlash: false, advanceToHumanBoundary: false)
                    : game.DriveHumanRespond(useDodge: false, advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanDying => game.DriveHumanRespondDying(
                    usePeach: false,
                    advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanCardSelection => ResolveFirstHarvestChoice(game),
                _ => game.DriveAdvance()
            };
        }
        if (nextBoundary.Status == EngineStatus.AwaitingHumanPlay &&
            nextBoundary.State.CurrentSeat == 0 &&
            nextBoundary.State.TurnNumber > openingTurn)
        {
            expiringGame = game;
        }
    }

    NotNull(expiringGame);
    var expiredState = expiringGame!.State;
    False(expiredState.Players.Single(player => player.Seat == 0).HasAlcoholEffect);
    True(expiringGame.Events.Any(eventItem =>
        eventItem.Payload is AlcoholExpiredEvent expired && expired.PlayerSeat == 0));
    True(expiringGame.Log.Any(entry =>
        entry.Type == "EffectExpired" && entry.Message.Contains("酒效", StringComparison.Ordinal)));
    AssertCardInventory(expiringGame);
}

static void YuanhuCrossSeatFlow()
{
    GameEngine? selectedGame = null;
    PendingDecision? selectedPrompt = null;
    CardSnapshot? selectedAttackCard = null;
    var targetSeat = -1;
    var offered = 0;
    var readyCount = 0;
    var attackCount = 0;
    var resultStates = new Dictionary<string, int>();
    var damageCount = 0;
    var sample = "";
    for (var seed = 1; seed <= 4_096 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
                AdvanceAfterHumanCommands = false,
                UseInteractiveDiscard = false,
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                UseInteractiveSetup = true,
                MaxTurns = 180
            },
            StandardContentRegistry.Create());
        var started = game.DriveStart();
        if (started.Status != EngineStatus.AwaitingHumanGeneralSelection ||
            !started.PendingDecision!.ValidContentIds.Contains("standard:demo-yuanhu", StringComparer.Ordinal))
        {
            continue;
        }
        offered++;

        _ = game.DriveHumanSelectGeneral("standard:demo-yuanhu", advanceToHumanBoundary: false);
        var ready = game.DriveAdvance();
        if (ready.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }
        readyCount++;

        var revealed = game.CreateSnapshot(0, revealAll: true);
        var human = revealed.Players.Single(player => player.Seat == 0);
        if (human.Skills?.Any(skill => skill.ContentId == "standard:yuanhu") != true)
        {
            continue;
        }

        var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
        {
            if (candidate.Kind != LegalActionKind.Slash ||
                candidate.CardId is not { } cardId ||
                candidate.TargetSeat is not { } candidateTarget ||
                candidateTarget == 0)
            {
                return false;
            }

            var target = revealed.Players.Single(player => player.Seat == candidateTarget);
            return target.IsAlive &&
                   target.Hp > 1 &&
                   target.Hand.All(card => card.Kind != CardKind.Dodge);
        });
        if (action is null)
        {
            continue;
        }
        attackCount++;

        var played = game.Submit(new PlayCardCommand(0, action.CardId!.Value,
            action.TargetSeats, game.Revision, game.PendingDecision!.PromptId,
            action.PlayedCardKind));
        if (!played.Accepted) continue;
        for (var step = 0; step < 48 &&
             game.PendingDecision?.SkillPrompt?.SkillId != "standard:yuanhu" &&
             game.ResolutionStack.Count > 0 &&
             game.PendingDecision is null; step++)
        {
            if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted) break;
        }
        var resultKey = $"{game.State.Status}/{game.PendingDecision?.Kind}/{game.PendingDecision?.SkillPrompt?.SkillId}";
        resultStates[resultKey] = resultStates.GetValueOrDefault(resultKey) + 1;
        if (game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
            .Any(item => item.TargetSeat == action.TargetSeat)) damageCount++;
        if (sample.Length == 0) sample = $"seed={seed}, target={action.TargetSeat}, beforeHp={revealed.Players.Single(player => player.Seat == action.TargetSeat).Hp}, afterHp={game.CreateSnapshot(0, true).Players.Single(player => player.Seat == action.TargetSeat).Hp}, ownerHand={game.CreateSnapshot(0, true).Players[0].HandCount}, candidates={string.Join(',', game.Events.Select(item => item.Payload).OfType<DamageTriggerWindowOpenedEvent>().LastOrDefault()?.Candidates.Select(item => item.ProgramId) ?? [])}, status={resultKey}, stack={string.Join(',', game.ResolutionStack.Select(frame => frame.GetType().Name + '/' + frame.Step))}, events={string.Join(',', game.Events.TakeLast(12).Select(item => item.Payload.GetType().Name))}";
        if (game.State.Status == EngineStatus.AwaitingHumanResponse &&
            game.PendingDecision is
            {
                Kind: DecisionKind.ProgramTrigger,
                SkillPrompt.SkillId: "standard:yuanhu"
            })
        {
            selectedGame = game;
            selectedPrompt = game.PendingDecision;
            selectedAttackCard = human.Hand.Single(card => card.Id == action.CardId);
            targetSeat = action.TargetSeat!.Value;
        }
    }

    if (selectedGame is null || selectedPrompt is null || selectedAttackCard is null || targetSeat < 0)
    {
        throw new InvalidOperationException($"No deterministic cross-seat Yuanhu trigger was found: offered={offered}, ready={readyCount}, attack={attackCount}, damage={damageCount}, results={string.Join(';', resultStates.Select(item => item.Key + '=' + item.Value))}, sample={sample}.");
    }

    var gameWithYuanhu = selectedGame!;
    var prompt = selectedPrompt!;
    var targetBefore = gameWithYuanhu.CreateSnapshot(0, revealAll: true).Players
        .Single(player => player.Seat == targetSeat);
    var ownerBefore = gameWithYuanhu.CreateSnapshot(0).Players.Single(player => player.Seat == 0);
    Equal(DecisionKind.ProgramTrigger, prompt.Kind);
    Equal(0, prompt.PlayerSeat);
    Equal("standard:yuanhu", prompt.SkillPrompt?.SkillId);
    True(prompt.IsPrivate);
    True(prompt.Choices.Any(choice =>
        choice.Parameters.GetValueOrDefault("program-action") == "activate"));
    True(prompt.Choices.Any(choice =>
        choice.Parameters.GetValueOrDefault("program-action") == "skip"));
    Equal<PendingDecision?>(null, gameWithYuanhu.CreateSnapshot(targetSeat).PendingDecision);

    var opened = gameWithYuanhu.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<DamageTriggerWindowOpenedEvent>()
        .Last();
    True(opened.Candidates.Any(candidate =>
        candidate.OwnerSeat == 0 &&
        candidate.OwnerSeat != opened.TargetSeat &&
        candidate.ProgramId == "standard:yuanhu"));

    var beforeInvalid = gameWithYuanhu.SerializeState();
    var invalid = gameWithYuanhu.Submit(new AnswerPromptCommand(
        0,
        prompt.PromptId,
        new ChoiceId("yuanhu.fake"),
        gameWithYuanhu.Revision));
    False(invalid.Accepted);
    Equal(CommandErrorCode.InvalidChoice, invalid.Error!.Code);
    Equal(beforeInvalid, gameWithYuanhu.SerializeState());

    var useChoice = prompt.Choices.Single(choice =>
        choice.Parameters.GetValueOrDefault("program-action") == "activate");
    var accepted = gameWithYuanhu.Submit(new AnswerPromptCommand(0, prompt.PromptId,
        useChoice.Id, gameWithYuanhu.Revision));
    TrueWithMessage(accepted.Accepted, "Yuanhu activation accepted");
    var cardPrompt = gameWithYuanhu.PendingDecision!;
    Equal("standard:yuanhu", cardPrompt.SkillPrompt?.SkillId);
    var cardChoice = cardPrompt.Choices.First(choice =>
        choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards");
    var discardCardId = cardChoice.Cards.Single();
    accepted = gameWithYuanhu.Submit(new AnswerPromptCommand(0, cardPrompt.PromptId,
        cardChoice.Id, gameWithYuanhu.Revision));
    TrueWithMessage(accepted.Accepted, "Yuanhu card payment accepted");

    var targetAfter = gameWithYuanhu.CreateSnapshot(0, revealAll: true).Players
        .Single(player => player.Seat == targetSeat);
    var ownerAfter = gameWithYuanhu.CreateSnapshot(0).Players.Single(player => player.Seat == 0);
    Equal(targetBefore.Hp + 1, targetAfter.Hp);
    Equal(ownerBefore.HandCount - 1, ownerAfter.HandCount);
    True(gameWithYuanhu.Events.Any(eventItem =>
        eventItem.Payload is RecoveryAppliedEvent recovery &&
        recovery.SourceSeat == 0 &&
        recovery.TargetSeat == targetSeat &&
        recovery.Amount == 1));
    var resolved = gameWithYuanhu.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<ProgramBindingResolvedEvent>()
        .Single(eventItem => eventItem.OwnerSeat == 0 && eventItem.SkillId == "standard:yuanhu");
    True(resolved.Activated && resolved.Completed);
    True(gameWithYuanhu.CardMovements.Any(movement =>
        movement.CardId == discardCardId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason.Value == "skill-program.standard:yuanhu.MoveBoundCards"));

    var ordinaryViewer = Enumerable.Range(0, gameWithYuanhu.PlayerCount)
        .First(seat => seat != 0 && seat != targetSeat);
    Equal<PendingDecision?>(null, gameWithYuanhu.CreateSnapshot(ordinaryViewer).PendingDecision);
    False(SnapshotJson.Serialize(gameWithYuanhu.CreateSnapshot(ordinaryViewer))
        .Contains($"\"Id\": {discardCardId},", StringComparison.Ordinal));
    AssertCardInventory(gameWithYuanhu);
}

static void GanglieFlow()
{
    GameEngine? selectedGame = null;
    PendingDecision? triggerPrompt = null;
    PendingDecision? punishmentPrompt = null;
    GameSnapshot? triggerOwnerView = null;
    GameSnapshot? triggerHumanView = null;
    CardSnapshot? attackCard = null;
    var ganglieSeat = -1;

    for (var seed = 1; seed <= 4_096 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
                AdvanceAfterHumanCommands = false,
                UseInteractiveDiscard = false,
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                UseInteractiveSetup = true,
                MaxTurns = 180
            },
            StandardContentRegistry.Create());
        var started = game.DriveStart();
        if (started.Status != EngineStatus.AwaitingHumanGeneralSelection)
        {
            continue;
        }

        var humanChoice = started.PendingDecision!.Choices.FirstOrDefault(choice =>
            choice.ContentIds.Count == 1 &&
            choice.ContentIds[0] != "standard:demo-ganglie");
        if (humanChoice is null)
        {
            continue;
        }

        _ = game.DriveHumanSelectGeneral(
            humanChoice.ContentIds.Single(),
            advanceToHumanBoundary: false);
        var ready = game.DriveAdvance();
        if (ready.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }

        var revealed = game.CreateSnapshot(0, revealAll: true);
        var target = revealed.Players.FirstOrDefault(player =>
            player.Seat != 0 &&
            player.IsAlive &&
            player.Skills?.Any(skill => skill.ContentId == "standard:ganglie") == true &&
            player.Hand.All(card => card.Kind != CardKind.Dodge));
        if (target is null)
        {
            continue;
        }

        var human = revealed.Players.Single(player => player.Seat == 0);
        var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
            candidate.Kind == LegalActionKind.Slash &&
            candidate.TargetSeat == target.Seat &&
            candidate.CardId is { } cardId &&
            human.Hand.Single(card => card.Id == cardId).Kind == CardKind.Slash);
        if (action is null)
        {
            continue;
        }

        var resultAfterAttack = game.DriveHumanPlay(
            action.CardId!.Value,
            target.Seat,
            advanceToHumanBoundary: false);
        var ownerViewBeforeTrigger = game.CreateSnapshot(target.Seat);
        var humanViewBeforeTrigger = game.CreateSnapshot(0);
        var privateTrigger = ownerViewBeforeTrigger.PendingDecision;
        if (resultAfterAttack.Status != EngineStatus.Running ||
            privateTrigger is not
            {
                Kind: DecisionKind.ProgramTrigger,
                SkillPrompt.SkillId: "standard:ganglie"
            })
        {
            continue;
        }

        for (var step = 0; step < 20 && game.PendingDecision?.PlayerSeat != 0; step++)
            _ = game.DriveAdvanceOneStep();
        if (game.PendingDecision is not
            {
                Kind: DecisionKind.ProgramTrigger,
                SkillPrompt.SkillId: "standard:ganglie"
            } sourcePrompt ||
            !sourcePrompt.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "choose-option"))
        {
            continue;
        }

        selectedGame = game;
        triggerPrompt = privateTrigger;
        punishmentPrompt = sourcePrompt;
        triggerOwnerView = ownerViewBeforeTrigger;
        triggerHumanView = humanViewBeforeTrigger;
        attackCard = human.Hand.Single(card => card.Id == action.CardId);
        ganglieSeat = target.Seat;
    }

    if (selectedGame is null ||
        triggerPrompt is null ||
        punishmentPrompt is null ||
        triggerOwnerView is null ||
        triggerHumanView is null ||
        attackCard is null ||
        ganglieSeat < 0)
    {
        throw new InvalidOperationException("No deterministic human-source Ganglie punishment boundary was found.");
    }

    var gameWithGanglie = selectedGame!;
    var trigger = triggerPrompt!;
    var prompt = punishmentPrompt!;
    var usedAttackCard = attackCard!;
    Equal(DecisionKind.ProgramTrigger, trigger.Kind);
    Equal(ganglieSeat, trigger.PlayerSeat);
    Equal(0, trigger.SourceSeat);
    Equal(ganglieSeat, trigger.TargetSeat);
    True(trigger.IsPrivate);
    True(trigger.Choices.Any(choice =>
        choice.Parameters.GetValueOrDefault("program-action") == "activate"));
    True(trigger.Choices.Any(choice =>
        choice.Parameters.GetValueOrDefault("program-action") == "skip"));
    True(trigger.Choices.All(choice => choice.Cards.Count == 0 && choice.Targets.Count == 0));
    Equal(DecisionKind.ProgramTrigger, triggerOwnerView!.PendingDecision!.Kind);
    Equal<PendingDecision?>(null, triggerHumanView!.PendingDecision);
    Equal(DecisionKind.ProgramTrigger, gameWithGanglie.State.PendingDecision!.Kind);
    Equal<PendingDecision?>(null, gameWithGanglie.CreateSnapshot(ganglieSeat).PendingDecision);
    Equal<PendingDecision?>(null, gameWithGanglie.CreateSnapshot(1).PendingDecision);

    Equal(DecisionKind.ProgramTrigger, prompt.Kind);
    Equal(0, prompt.PlayerSeat);
    Equal(ganglieSeat, prompt.SourceSeat);
    Equal(0, prompt.TargetSeat);
    True(prompt.IsPrivate);
    var sourceView = gameWithGanglie.CreateSnapshot(0);
    var source = sourceView.Players.Single(player => player.Seat == 0);
    True(source.HandCount >= 2);
    True(prompt.Choices.Any(choice =>
        choice.Parameters.GetValueOrDefault("option-id") == "damage" &&
        choice.Cards.Count == 0 &&
        choice.Targets.Count == 0));
    var discardChoice = prompt.Choices.Single(choice =>
        choice.Parameters.GetValueOrDefault("option-id") == "discard");

    var ordinaryViewer = gameWithGanglie.CreateSnapshot(1);
    Equal<PendingDecision?>(null, ordinaryViewer.PendingDecision);
    var privateCardId = source.Hand.First().Id;
    False(SnapshotJson.Serialize(ordinaryViewer).Contains(
        $"\"Id\": {privateCardId},",
        StringComparison.Ordinal));

    var beforeInvalid = gameWithGanglie.SerializeState();
    var invalid = gameWithGanglie.Submit(new AnswerPromptCommand(
        0,
        prompt.PromptId,
        new ChoiceId("ganglie.punishment.fake"),
        gameWithGanglie.Revision));
    False(invalid.Accepted);
    Equal(CommandErrorCode.InvalidChoice, invalid.Error!.Code);
    Equal(beforeInvalid, gameWithGanglie.SerializeState());

    var accepted = gameWithGanglie.Submit(new AnswerPromptCommand(
        0,
        prompt.PromptId,
        discardChoice.Id,
        gameWithGanglie.Revision));
    TrueWithMessage(accepted.Accepted, "Ganglie punishment choice accepted");
    var discardedCardIds = new List<int>();
    for (var index = 0; index < 2; index++)
    {
        var payment = gameWithGanglie.PendingDecision!;
        Equal("standard:ganglie", payment.SkillPrompt?.SkillId);
        var pick = payment.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards");
        discardedCardIds.Add(pick.Cards.Single());
        accepted = gameWithGanglie.Submit(new AnswerPromptCommand(0, payment.PromptId,
            pick.Id, gameWithGanglie.Revision));
        TrueWithMessage(accepted.Accepted, "Ganglie card payment accepted");
    }
    accepted = gameWithGanglie.Submit(new AdvanceCommand(gameWithGanglie.Revision));
    TrueWithMessage(accepted.Accepted, "Ganglie attack continuation accepted");

    var judgment = gameWithGanglie.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<JudgmentResolvedEvent>()
        .Single(eventItem => eventItem.Reason == JudgmentReasons.Ganglie);
    True(judgment.Succeeded);
    True(judgment.Suit is Suit.Heart or Suit.Diamond);
    TrueWithMessage(gameWithGanglie.Events.Any(eventItem =>
        eventItem.Payload is JudgmentRequestedEvent requested &&
        requested.Reason == JudgmentReasons.Ganglie), "Ganglie judgment request event");

    foreach (var cardId in discardedCardIds)
    {
        TrueWithMessage(gameWithGanglie.CardMovements.Any(movement =>
            movement.CardId == cardId &&
            movement.From == CardLocation.Hand(0) &&
            movement.To == CardLocation.DiscardPile &&
            movement.Reason.Value == "skill-program.standard:ganglie.MoveBoundCards"),
            "Ganglie discard movement");
    }

    var resolved = gameWithGanglie.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<ProgramBindingResolvedEvent>()
        .Single(eventItem =>
            eventItem.SkillId == "standard:ganglie" &&
            eventItem.OwnerSeat == ganglieSeat);
    True(resolved.Activated && resolved.Completed);
    TrueWithMessage(gameWithGanglie.Events.Any(eventItem =>
        eventItem.Payload is CardUseFinishedEvent finished &&
        finished.CardId == usedAttackCard.Id), "Ganglie source attack finished");
    TrueWithMessage(gameWithGanglie.AiThoughts.Any(thought =>
        thought.Candidates.Any(candidate => candidate.Action.Kind == LegalActionKind.UseProgramSkill)),
        "Ganglie AI trigger thought");
    Equal(0, gameWithGanglie.ResolutionStack.Count);
    Equal(0, accepted.State.ProcessingCardCount);
    AssertCardInventory(gameWithGanglie);
}

static void GuicaiFlow()
{
    GameEngine? selectedGame = null;
    PendingDecision? guicaiPrompt = null;
    JudgmentFrame? judgmentFrame = null;
    PromptChoice? replacementChoice = null;
    IReadOnlyList<int>? validCardIds = null;

    for (var seed = 1; seed <= 8_192 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
                AdvanceAfterHumanCommands = false,
                UseInteractiveDiscard = false,
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                UseInteractiveSetup = true,
                MaxTurns = 180
            },
            StandardContentRegistry.Create());
        var started = game.DriveStart();
        if (started.Status != EngineStatus.AwaitingHumanGeneralSelection ||
            started.PendingDecision is not { } setupPrompt)
        {
            continue;
        }

        var guicaiChoice = setupPrompt.Choices.FirstOrDefault(choice =>
            choice.ContentIds.Contains("standard:demo-guicai", StringComparer.Ordinal));
        if (guicaiChoice is null)
        {
            continue;
        }

        _ = game.DriveHumanSelectGeneral(
            "standard:demo-guicai",
            advanceToHumanBoundary: false);
        var ready = game.DriveAdvance();
        if (ready.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }

        var human = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        var bagua = human.Hand.FirstOrDefault(card => card.Kind == CardKind.BaguaFormation);
        if (bagua is null)
        {
            continue;
        }

        var result = game.DriveHumanPlay(
            bagua.Id,
            targetSeat: null,
            advanceToHumanBoundary: true);
        if (result.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }

        result = game.DriveHumanEndPlay(advanceToHumanBoundary: false);
        for (var step = 0; step < 1_500 && result.Status != EngineStatus.Completed; step++)
        {
            if (result.Status == EngineStatus.AwaitingHumanResponse)
            {
                var prompt = game.PendingDecision ??
                    throw new InvalidOperationException("The response status has no prompt.");
                if (prompt.Kind == DecisionKind.ProgramJudgmentReplacement && prompt.PlayerSeat == 0)
                {
                    var frame = game.ResolutionStack.OfType<JudgmentFrame>().SingleOrDefault();
                    if (frame?.CardId is null || frame.Suit is not { } oldSuit)
                    {
                        break;
                    }

                    var hand = game.CreateSnapshot(0, revealAll: true).Players
                        .Single(player => player.Seat == 0)
                        .Hand;
                    var choice = prompt.Choices.FirstOrDefault(candidate =>
                    {
                        if (candidate.Parameters.GetValueOrDefault("action") != "program-judgment-replace" ||
                            candidate.Cards.Count != 1)
                        {
                            return false;
                        }

                        var card = hand.SingleOrDefault(handCard => handCard.Id == candidate.Cards[0]);
                        return card is not null &&
                               (card.Suit is Suit.Heart or Suit.Diamond) !=
                               (oldSuit is Suit.Heart or Suit.Diamond);
                    });
                    if (choice is null)
                    {
                        break;
                    }

                    var beforeInvalid = game.SerializeState();
                    var invalid = game.Submit(new AnswerPromptCommand(
                        ActorSeat: 0,
                        Prompt: prompt.PromptId,
                        Choice: new ChoiceId("guicai.fake.choice"),
                        ExpectedRevision: game.Revision));
                    False(invalid.Accepted);
                    Equal(CommandErrorCode.InvalidChoice, invalid.Error!.Code);
                    Equal(beforeInvalid, game.SerializeState());

                    var accepted = game.Submit(new AnswerPromptCommand(
                        ActorSeat: 0,
                        Prompt: prompt.PromptId,
                        Choice: choice.Id,
                        ExpectedRevision: game.Revision));
                    TrueWithMessage(accepted.Accepted, "Guicai replacement choice accepted");
                    selectedGame = game;
                    guicaiPrompt = prompt;
                    judgmentFrame = frame;
                    replacementChoice = choice;
                    validCardIds = prompt.ValidCardIds.ToArray();
                    break;
                }

                if (prompt.Kind == DecisionKind.RespondSlash)
                {
                    result = game.DriveHumanRespondSlash(
                        useSlash: false,
                        advanceToHumanBoundary: false);
                }
                else if (prompt.Kind == DecisionKind.RespondDodge)
                {
                    result = game.DriveHumanRespond(
                        useDodge: false,
                        advanceToHumanBoundary: false);
                }
                else if (prompt.Kind == DecisionKind.Nullification)
                {
                    result = game.DriveHumanRespondNullification(
                        useNullification: false,
                        advanceToHumanBoundary: false);
                }
                else
                {
                    var generic = game.Submit(new AnswerPromptCommand(
                        ActorSeat: prompt.PlayerSeat,
                        Prompt: prompt.PromptId,
                        Choice: prompt.Choices.First(choice => choice.Cards.Count == 0).Id,
                        ExpectedRevision: game.Revision));
                    result = generic.Result;
                }
                continue;
            }

            result = result.Status switch
            {
                EngineStatus.AwaitingHumanPlay => game.DriveHumanEndPlay(
                    advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanDying => game.DriveHumanRespondDying(
                    usePeach: false,
                    advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanCardSelection => ResolveFirstHarvestChoice(game),
                _ => game.DriveAdvanceOneStep()
            };
        }
    }

    if (selectedGame is null ||
        guicaiPrompt is null ||
        judgmentFrame is null ||
        replacementChoice is null ||
        validCardIds is null)
    {
        throw new InvalidOperationException("No deterministic human Guicai judgment boundary was found.");
    }

    var gameWithGuicai = selectedGame!;
    var finalPrompt = guicaiPrompt!;
    var finalFrame = judgmentFrame!;
    var replacement = replacementChoice!;
    var publishedCardIds = validCardIds!;
    var oldCardId = finalFrame.CardId!.Value;
    var replacementCardId = replacement.Cards.Single();
    Equal(DecisionKind.ProgramJudgmentReplacement, finalPrompt.Kind);
    Equal(0, finalPrompt.PlayerSeat);
    Equal(finalFrame.TargetSeat, finalPrompt.TargetSeat);
    Equal(finalFrame.TargetSeat, finalPrompt.SourceSeat);
    True(finalPrompt.IsPrivate);
    True(publishedCardIds.SequenceEqual(finalPrompt.ValidCardIds));
    True(publishedCardIds.Contains(replacementCardId));
    Equal<PendingDecision?>(null, gameWithGuicai.CreateSnapshot(1).PendingDecision);

    var replacementEvent = gameWithGuicai.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<ProgramJudgmentReplacementResolvedEvent>()
        .Single(eventItem => eventItem.JudgmentFrameId == finalFrame.Id && eventItem.Activated);
    Equal(0, replacementEvent.OwnerSeat);
    Equal(finalFrame.TargetSeat, replacementEvent.SubjectSeat);
    Equal(oldCardId, replacementEvent.OldCardId);
    Equal(replacementCardId, replacementEvent.ReplacementCardId);
    TrueWithMessage(gameWithGuicai.Events.Any(eventItem =>
        eventItem.Payload is JudgmentReplacementRequestedEvent requested &&
        requested.ResolutionId == finalFrame.Id &&
        requested.OwnerSeat == 0), "Guicai request event");
    TrueWithMessage(gameWithGuicai.Events.Any(eventItem =>
        eventItem.Payload is JudgmentResolvedEvent resolved &&
        resolved.ResolutionId == finalFrame.Id &&
        resolved.CardId == replacementCardId), "replacement judgment is final");

    TrueWithMessage(gameWithGuicai.CardMovements.Any(movement =>
        movement.CardId == oldCardId &&
        movement.From == CardLocation.Judgment(finalFrame.TargetSeat) &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.ProgramJudgmentOldCard), "old judgment is discarded");
    TrueWithMessage(gameWithGuicai.CardMovements.Any(movement =>
        movement.CardId == replacementCardId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.ProgramJudgmentReplace), "replacement enters processing");
    TrueWithMessage(gameWithGuicai.CardMovements.Any(movement =>
        movement.CardId == replacementCardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.Judgment(finalFrame.TargetSeat) &&
        movement.Reason == CardMoveReasons.ProgramJudgmentReplace), "replacement enters judgment");
    TrueWithMessage(gameWithGuicai.CardMovements.Any(movement =>
        movement.CardId == replacementCardId &&
        movement.From == CardLocation.Judgment(finalFrame.TargetSeat) &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.JudgmentFinish), "replacement judgment is finished");
    TrueWithMessage(gameWithGuicai.ResolutionStack.All(openFrame =>
        openFrame.Id != finalFrame.Id),
        "Guicai closes the selected judgment frame");
    AssertCardInventory(gameWithGuicai);
}

static void IndulgenceFlow()
{
    var skipped = FindIndulgenceScenario(skipPlayPhase: true);
    var normal = FindIndulgenceScenario(skipPlayPhase: false);

    NotNull(skipped);
    NotNull(normal);
    AssertIndulgenceScenario(skipped!.Value);
    AssertIndulgenceScenario(normal!.Value);
    True(skipped.Value.Resolved.SkippedPlayPhase);
    False(normal.Value.Resolved.SkippedPlayPhase);
    False(skipped.Value.Resolved.JudgmentSucceeded);
    True(normal.Value.Resolved.JudgmentSucceeded);
}

static (GameEngine Game, int TargetSeat, int CardId, DelayedCardResolvedEvent Resolved)?
    FindIndulgenceScenario(bool skipPlayPhase, int rulesVersion = GameCheckpoint.CurrentRulesVersion)
{
    for (var seed = 1; seed <= 4_096; seed++)
    {
        var registry = StandardContentRegistry.Create();
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
                AdvanceAfterHumanCommands = false,
                UseInteractiveDiscard = false,
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                MaxTurns = 180
            },
            registry);
        if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
        {
            game = GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
        }
        var result = game.DriveStart();
        if (result.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }

        var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
            candidate.Kind == LegalActionKind.Indulgence &&
            candidate.TargetSeat is not null);
        if (action is null)
        {
            continue;
        }

        var targetSeat = action.TargetSeat!.Value;
        result = game.DriveHumanPlay(
            action.CardId!.Value,
            targetSeat,
            advanceToHumanBoundary: false);
        var placed = game.Events
            .Select(eventItem => eventItem.Payload)
            .OfType<DelayedCardPlacedEvent>()
            .LastOrDefault(eventItem => eventItem.TargetSeat == targetSeat);
        if (placed is null)
        {
            continue;
        }

        var publicView = game.CreateSnapshot((targetSeat + 1) % game.PlayerCount);
        var publicTarget = publicView.Players.Single(player => player.Seat == targetSeat);
        TrueWithMessage(
            publicTarget.Judgment.Any(card => card.Id == placed.CardId),
            "delayed card is public in the target judgment zone");
        if (game.GetHumanLegalActions().Any(candidate =>
                candidate.Kind == LegalActionKind.Indulgence &&
                candidate.TargetSeat == targetSeat))
        {
            throw new InvalidOperationException("The same delayed card kind was offered twice to one target.");
        }

        for (var step = 0; step < 2_000; step++)
        {
            var resolved = game.Events
                .Select(eventItem => eventItem.Payload)
                .OfType<DelayedCardResolvedEvent>()
                .LastOrDefault(eventItem =>
                    eventItem.TargetSeat == targetSeat &&
                    eventItem.CardId == placed.CardId);
            if (resolved is not null)
            {
                if (resolved.SkippedPlayPhase == skipPlayPhase)
                {
                    var expectedPhase = skipPlayPhase ? TurnPhase.Discard : TurnPhase.Play;
                    if (game.State.CurrentSeat == targetSeat &&
                        game.State.Phase == expectedPhase)
                    {
                        return (game, targetSeat, placed.CardId, resolved);
                    }
                }

                break;
            }

            if (result.Status == EngineStatus.Completed)
            {
                break;
            }

            if (result.Status == EngineStatus.AwaitingHumanResponse ||
                result.Status == EngineStatus.AwaitingHumanDying ||
                result.Status == EngineStatus.AwaitingHumanCardSelection ||
                result.Status == EngineStatus.AwaitingHumanPlay)
            {
                result = ResolveDelayedCardHumanBoundary(game, result);
            }
            else
            {
                result = game.DriveAdvanceOneStep();
            }
        }
    }

    return null;
}

static EngineRunResult ResolveDelayedCardHumanBoundary(
    GameEngine game,
    EngineRunResult result)
{
    return result.Status switch
    {
        EngineStatus.AwaitingHumanPlay => game.DriveHumanEndPlay(advanceToHumanBoundary: false),
        EngineStatus.AwaitingHumanDying => game.DriveHumanRespondDying(
            usePeach: false,
            advanceToHumanBoundary: false),
        EngineStatus.AwaitingHumanCardSelection => ResolveFirstHarvestChoice(game),
        EngineStatus.AwaitingHumanResponse => ResolveDelayedCardHumanPrompt(game),
        _ => throw new InvalidOperationException(
            $"Unexpected human boundary during delayed card flow: {result.Status}.")
    };
}

static EngineRunResult ResolveDelayedCardHumanPrompt(GameEngine game)
{
    var prompt = game.PendingDecision ??
        throw new InvalidOperationException("The delayed card flow expected a human prompt.");
    var choice = prompt.Choices.FirstOrDefault(candidate =>
                     candidate.Parameters.GetValueOrDefault("program-action") == "skip") ??
                 prompt.Choices.FirstOrDefault(candidate =>
        candidate.Cards.Count == 0 && candidate.Targets.Count == 0) ??
        throw new InvalidOperationException(
            $"The delayed card flow could not find a safe pass choice for {prompt.Kind}.");
    var accepted = game.Submit(new AnswerPromptCommand(
        prompt.PlayerSeat,
        prompt.PromptId,
        choice.Id,
        game.Revision));
    TrueWithMessage(accepted.Accepted, $"resolve human {prompt.Kind} prompt");
    return accepted.Result;
}

static void AssertIndulgenceScenario(
    (GameEngine Game, int TargetSeat, int CardId, DelayedCardResolvedEvent Resolved) scenario)
{
    var game = scenario.Game;
    var target = game.State.Players.Single(player => player.Seat == scenario.TargetSeat);
    Equal(0, target.Judgment.Count);
    Equal(scenario.CardId, scenario.Resolved.CardId);
    Equal(CardKind.Indulgence, scenario.Resolved.CardKind);
    Equal(scenario.TargetSeat, scenario.Resolved.TargetSeat);
    Equal(
        game.RulesVersion >= 11
            ? !scenario.Resolved.JudgmentSucceeded
            : scenario.Resolved.JudgmentSucceeded,
        scenario.Resolved.SkippedPlayPhase);
    var judgment = game.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<JudgmentResolvedEvent>()
        .Single(eventItem => eventItem.ResolutionId == scenario.Resolved.ResolutionId);
    if (game.RulesVersion >= 11)
    {
        Equal(judgment.Suit == Suit.Heart, judgment.Succeeded);
        Equal(judgment.Suit != Suit.Heart, scenario.Resolved.SkippedPlayPhase);
    }
    else
    {
        Equal(judgment.Suit is Suit.Heart or Suit.Diamond, judgment.Succeeded);
        Equal(judgment.Succeeded, scenario.Resolved.SkippedPlayPhase);
    }
    var expectedPhase = scenario.Resolved.SkippedPlayPhase ? TurnPhase.Discard : TurnPhase.Play;
    TrueWithMessage(
        game.State.Phase == expectedPhase,
        $"Indulgence boundary phase; expected={expectedPhase}; actual={game.State.Phase}; target={scenario.TargetSeat}; resolution={scenario.Resolved.ResolutionId}; turn={game.State.TurnNumber}; currentSeat={game.State.CurrentSeat}");
    TrueWithMessage(game.Events.Any(eventItem =>
        eventItem.Payload is DelayedCardPlacedEvent placed &&
        placed.CardId == scenario.CardId &&
        placed.TargetSeat == scenario.TargetSeat), "delayed placement event");
    TrueWithMessage(game.Events.Any(eventItem =>
        eventItem.Payload is CardUseFinishedEvent finished &&
        finished.CardId == scenario.CardId &&
        finished.CardKind == CardKind.Indulgence), "delayed card use finished");
    TrueWithMessage(game.CardMovements.Any(movement =>
        movement.CardId == scenario.CardId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Use), "delayed card enters processing");
    TrueWithMessage(game.CardMovements.Any(movement =>
        movement.CardId == scenario.CardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.Judgment(scenario.TargetSeat) &&
        movement.Reason == CardMoveReasons.DelayedCardPlace), "delayed card enters judgment");
    TrueWithMessage(game.CardMovements.Any(movement =>
        movement.CardId == scenario.CardId &&
        movement.From == CardLocation.Judgment(scenario.TargetSeat) &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.DelayedCardFinish), "delayed card leaves judgment");
    TrueWithMessage(game.Events.Any(eventItem =>
        eventItem.Payload is JudgmentResolvedEvent resolved &&
        resolved.ResolutionId == scenario.Resolved.ResolutionId &&
        resolved.CardId == scenario.Resolved.JudgmentCardId &&
        resolved.Reason == JudgmentReasons.Indulgence), "delayed judgment result");
    AssertCardInventory(game);
}

static void SupplyShortageFlow()
{
    var skipDraw = FindSupplyShortageScenario(skipDrawPhase: true);
    var normalDraw = FindSupplyShortageScenario(skipDrawPhase: false);

    NotNull(skipDraw);
    NotNull(normalDraw);
    AssertSupplyShortageScenario(skipDraw!.Value);
    AssertSupplyShortageScenario(normalDraw!.Value);
    True(skipDraw.Value.Resolved.JudgmentSucceeded == false);
    False(normalDraw.Value.Resolved.JudgmentSucceeded == false);
    True(skipDraw.Value.Resolved.SkippedDrawPhase);
    False(normalDraw.Value.Resolved.SkippedDrawPhase);
    Equal(0, skipDraw.Value.TargetDrawCount);
    True(normalDraw.Value.TargetDrawCount > 0);
}

static (GameEngine Game, int TargetSeat, int CardId, DelayedCardResolvedEvent Resolved, int TargetDrawCount)?
    FindSupplyShortageScenario(bool skipDrawPhase, int rulesVersion = GameCheckpoint.CurrentRulesVersion)
{
    for (var seed = 1; seed <= 4_096; seed++)
    {
        var registry = StandardContentRegistry.Create();
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
                AdvanceAfterHumanCommands = false,
                UseInteractiveDiscard = false,
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                MaxTurns = 180
            },
            registry);
        if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
        {
            game = GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
        }
        var result = game.DriveStart();
        if (result.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }

        var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
            candidate.Kind == LegalActionKind.SupplyShortage &&
            candidate.TargetSeat is not null);
        if (action is null)
        {
            continue;
        }

        var targetSeat = action.TargetSeat!.Value;
        TrueWithMessage(
            game.RulesVersion >= 18
                ? game.GetCombatDistance(0, targetSeat) == 1
                : game.State.Players.Single(player => player.Seat == targetSeat).HandCount > 0,
            game.RulesVersion >= 18
                ? "兵粮寸断 target must be selected at combat distance one"
                : "legacy 兵粮寸断 target must be selected from a publicly non-empty hand");
        result = game.DriveHumanPlay(
            action.CardId!.Value,
            targetSeat,
            advanceToHumanBoundary: false);
        var placed = game.Events
            .Select(eventItem => eventItem.Payload)
            .OfType<DelayedCardPlacedEvent>()
            .LastOrDefault(eventItem =>
                eventItem.CardKind == CardKind.SupplyShortage &&
                eventItem.TargetSeat == targetSeat);
        if (placed is null)
        {
            continue;
        }

        var publicView = game.CreateSnapshot((targetSeat + 1) % game.PlayerCount);
        var publicTarget = publicView.Players.Single(player => player.Seat == targetSeat);
        TrueWithMessage(
            publicTarget.Judgment.Any(card => card.Id == placed.CardId),
            "兵粮寸断 is public in the target judgment zone");
        if (game.GetHumanLegalActions().Any(candidate =>
                candidate.Kind == LegalActionKind.SupplyShortage &&
                candidate.TargetSeat == targetSeat))
        {
            throw new InvalidOperationException("The same delayed card kind was offered twice to one target.");
        }

        for (var step = 0; step < 2_000; step++)
        {
            var resolvedEnvelope = game.Events.LastOrDefault(eventItem =>
                eventItem.Payload is DelayedCardResolvedEvent resolved &&
                resolved.CardId == placed.CardId);
            if (resolvedEnvelope is not null)
            {
                var resolved = (DelayedCardResolvedEvent)resolvedEnvelope.Payload;
                if (resolved.SkippedDrawPhase != skipDrawPhase)
                {
                    break;
                }

                var nextPlayPhase = game.Events.FirstOrDefault(eventItem =>
                    eventItem.Sequence > resolvedEnvelope.Sequence &&
                    eventItem.Payload is PhaseChangedEvent phase &&
                    phase.Phase == TurnPhase.Play &&
                    phase.ActorSeat == targetSeat);
                if (nextPlayPhase is null)
                {
                    break;
                }

                var targetDrawCount = game.Events.Count(eventItem =>
                    eventItem.Sequence > resolvedEnvelope.Sequence &&
                    eventItem.Sequence < nextPlayPhase.Sequence &&
                    eventItem.Payload is CardMovedEvent moved &&
                    moved.From == CardLocation.DrawPile &&
                    moved.To == CardLocation.Hand(targetSeat) &&
                    moved.Reason == CardMoveReasons.Draw);
                return (game, targetSeat, placed.CardId, resolved, targetDrawCount);
            }

            if (result.Status == EngineStatus.Completed)
            {
                break;
            }

            if (result.Status == EngineStatus.AwaitingHumanResponse ||
                result.Status == EngineStatus.AwaitingHumanDying ||
                result.Status == EngineStatus.AwaitingHumanCardSelection ||
                result.Status == EngineStatus.AwaitingHumanPlay)
            {
                result = ResolveDelayedCardHumanBoundary(game, result);
            }
            else
            {
                result = game.DriveAdvanceOneStep();
            }
        }
    }

    return null;
}

static void AssertSupplyShortageScenario(
    (GameEngine Game, int TargetSeat, int CardId, DelayedCardResolvedEvent Resolved, int TargetDrawCount) scenario)
{
    var game = scenario.Game;
    var target = game.State.Players.Single(player => player.Seat == scenario.TargetSeat);
    Equal(0, target.Judgment.Count);
    Equal(scenario.CardId, scenario.Resolved.CardId);
    Equal(CardKind.SupplyShortage, scenario.Resolved.CardKind);
    Equal(scenario.TargetSeat, scenario.Resolved.TargetSeat);
    Equal(!scenario.Resolved.JudgmentSucceeded, scenario.Resolved.SkippedDrawPhase);
    var judgment = game.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<JudgmentResolvedEvent>()
        .Single(eventItem => eventItem.ResolutionId == scenario.Resolved.ResolutionId);
    if (game.RulesVersion >= 11)
    {
        Equal(judgment.Suit == Suit.Club, judgment.Succeeded);
        Equal(judgment.Suit != Suit.Club, scenario.Resolved.SkippedDrawPhase);
    }
    else
    {
        Equal(judgment.Suit is Suit.Heart or Suit.Diamond, judgment.Succeeded);
        Equal(!judgment.Succeeded, scenario.Resolved.SkippedDrawPhase);
    }
    Equal(TurnPhase.Play, game.State.Phase);
    TrueWithMessage(game.Events.Any(eventItem =>
        eventItem.Payload is DelayedCardPlacedEvent placed &&
        placed.CardId == scenario.CardId &&
        placed.CardKind == CardKind.SupplyShortage &&
        placed.TargetSeat == scenario.TargetSeat), "兵粮寸断 placement event");
    TrueWithMessage(game.Events.Any(eventItem =>
        eventItem.Payload is CardUseFinishedEvent finished &&
        finished.CardId == scenario.CardId &&
        finished.CardKind == CardKind.SupplyShortage), "兵粮寸断 use finished");
    TrueWithMessage(game.CardMovements.Any(movement =>
        movement.CardId == scenario.CardId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Use), "兵粮寸断 enters processing");
    TrueWithMessage(game.CardMovements.Any(movement =>
        movement.CardId == scenario.CardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.Judgment(scenario.TargetSeat) &&
        movement.Reason == CardMoveReasons.DelayedCardPlace), "兵粮寸断 enters judgment");
    TrueWithMessage(game.CardMovements.Any(movement =>
        movement.CardId == scenario.CardId &&
        movement.From == CardLocation.Judgment(scenario.TargetSeat) &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.DelayedCardFinish), "兵粮寸断 leaves judgment");
    TrueWithMessage(game.Events.Any(eventItem =>
        eventItem.Payload is JudgmentResolvedEvent resolved &&
        resolved.ResolutionId == scenario.Resolved.ResolutionId &&
        resolved.CardId == scenario.Resolved.JudgmentCardId &&
        resolved.Reason == JudgmentReasons.SupplyShortage), "兵粮寸断 judgment result");
    AssertCardInventory(game);
}

static void LightningFlow()
{
    var miss = FindLightningScenario(hit: false);
    var hit = FindLightningScenario(hit: true);

    NotNull(miss);
    NotNull(hit);
    AssertLightningScenario(miss!.Value);
    AssertLightningScenario(hit!.Value);
    False(miss.Value.Resolved.Hit);
    True(hit.Value.Resolved.Hit);
}

static (GameEngine Game, int CardId, LightningResolvedEvent Resolved, int HpBefore, int HpAfter)?
    FindLightningScenario(bool hit)
{
    for (var seed = 1; seed <= 8_192; seed++)
    {
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
                AdvanceAfterHumanCommands = false,
                UseInteractiveDiscard = false,
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                MaxTurns = 180
            },
            StandardContentRegistry.Create());
        var result = game.DriveStart();
        if (result.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }

        var actions = game.GetHumanLegalActions()
            .Where(action => action.Kind == LegalActionKind.Lightning)
            .ToArray();
        if (actions.Length == 0 || actions.Any(action => action.TargetSeat != 0))
        {
            continue;
        }

        var action = actions[0];
        var hpBefore = game.State.Players.Single(player => player.Seat == 0).Hp;
        result = game.DriveHumanPlay(action.CardId!.Value, 0, advanceToHumanBoundary: false);
        for (var step = 0; step < 2_000; step++)
        {
            var resolved = game.Events
                .Select(eventItem => eventItem.Payload)
                .OfType<LightningResolvedEvent>()
                .LastOrDefault(eventItem => eventItem.CardId == action.CardId.Value);
            if (resolved is not null)
            {
                if (resolved.JudgmentTargetSeat != 0)
                {
                    break;
                }

                if (resolved.Hit != hit)
                {
                    break;
                }

                if (hit && game.CreateCardZoneDiagnostics()
                        .Single(card => card.CardId == action.CardId.Value)
                        .Location != CardLocation.DiscardPile)
                {
                    if (result.Status is EngineStatus.AwaitingHumanPlay or
                        EngineStatus.AwaitingHumanResponse or
                        EngineStatus.AwaitingHumanDying or
                        EngineStatus.AwaitingHumanCardSelection)
                    {
                        result = ResolveDelayedCardHumanBoundary(game, result);
                    }
                    else
                    {
                        result = game.DriveAdvanceOneStep();
                    }

                    continue;
                }

                if (!hit &&
                    (game.ResolutionStack.Count != 0 ||
                     game.State.Status != EngineStatus.AwaitingHumanPlay ||
                     game.State.Phase != TurnPhase.Play))
                {
                    if (result.Status is EngineStatus.AwaitingHumanPlay or
                        EngineStatus.AwaitingHumanResponse or
                        EngineStatus.AwaitingHumanDying or
                        EngineStatus.AwaitingHumanCardSelection)
                    {
                        result = ResolveDelayedCardHumanBoundary(game, result);
                    }
                    else
                    {
                        result = game.DriveAdvanceOneStep();
                    }

                    continue;
                }

                if (hit && game.ResolutionStack.Count != 0)
                {
                    break;
                }

                var hpAfter = game.State.Players.Single(player => player.Seat == 0).Hp;
                return (game, action.CardId.Value, resolved, hpBefore, hpAfter);
            }

            if (result.Status == EngineStatus.Completed)
            {
                break;
            }

            if (result.Status is EngineStatus.AwaitingHumanPlay or
                EngineStatus.AwaitingHumanResponse or
                EngineStatus.AwaitingHumanDying or
                EngineStatus.AwaitingHumanCardSelection)
            {
                result = ResolveDelayedCardHumanBoundary(game, result);
            }
            else
            {
                result = game.DriveAdvanceOneStep();
            }
        }
    }

    return null;
}

static void AssertLightningScenario(
    (GameEngine Game, int CardId, LightningResolvedEvent Resolved, int HpBefore, int HpAfter) scenario)
{
    var game = scenario.Game;
    var resolved = scenario.Resolved;
    Equal(scenario.CardId, resolved.CardId);
    Equal(0, resolved.JudgmentTargetSeat);
    TrueWithMessage(game.Events.Any(eventItem =>
        eventItem.Payload is JudgmentResolvedEvent judgment &&
        judgment.ResolutionId == resolved.ResolutionId &&
        judgment.Reason == JudgmentReasons.Lightning &&
        judgment.CardId == resolved.JudgmentCardId &&
        judgment.Succeeded == resolved.Hit), "闪电公开判定结果");

    if (resolved.Hit)
    {
        Equal(3, resolved.DamageAmount);
        Equal<int?>(null, resolved.NextTargetSeat);
        TrueWithMessage(scenario.HpAfter <= scenario.HpBefore,
            $"闪电命中后生命值不应上升：before={scenario.HpBefore}, after={scenario.HpAfter}");
        TrueWithMessage(game.Events.Any(eventItem =>
            eventItem.Payload is DamageRequestedEvent damage &&
            damage.SourceSeat == 0 &&
            damage.TargetSeat == 0 &&
            damage.Amount == 3 &&
            damage.SourceCard == CardKind.Lightning &&
            damage.Nature == DamageNature.Thunder), "闪电命中造成 3 点雷电伤害");
        TrueWithMessage(game.Events.Any(eventItem =>
            eventItem.Payload is DamageAppliedEvent damage &&
            damage.SourceSeat == 0 &&
            damage.TargetSeat == 0 &&
            damage.Amount == 3 &&
            damage.Nature == DamageNature.Thunder), "闪电雷电伤害已应用");
        TrueWithMessage(game.CardMovements.Any(movement =>
            movement.CardId == scenario.CardId &&
            movement.From == CardLocation.Judgment(0) &&
            movement.To == CardLocation.DiscardPile &&
            (movement.Reason == CardMoveReasons.DelayedCardFinish ||
             movement.Reason == CardMoveReasons.DeathDiscard)), "命中后的闪电离开判定区");
    }
    else
    {
        Equal(0, resolved.DamageAmount);
        Equal(1, resolved.NextTargetSeat);
        TrueWithMessage(game.CardMovements.Any(movement =>
            movement.CardId == scenario.CardId &&
            movement.From == CardLocation.Judgment(0) &&
            movement.To == CardLocation.Judgment(1) &&
            movement.Reason == CardMoveReasons.DelayedCardTransfer), "未命中的闪电转移到下一名存活角色");
        var publicView = game.CreateSnapshot(2);
        TrueWithMessage(publicView.Players.Single(player => player.Seat == 1).Judgment
            .Any(card => card.Id == scenario.CardId), "闪电转移后的公开判定区");
        Equal(TurnPhase.Play, game.State.Phase);
        TrueWithMessage(!game.Events.Any(eventItem =>
            eventItem.Payload is DamageRequestedEvent damage &&
            damage.ResolutionId == resolved.ResolutionId), "未命中不造成伤害");
    }

    AssertCardInventory(game);
}

static void LongdanResponseFlow()
{
    GameEngine? selectedGame = null;
    PendingDecision? selectedPrompt = null;
    PromptChoice? selectedChoice = null;
    CardSnapshot? selectedResponseCard = null;
    for (var seed = 1; seed <= 4_096 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
                AdvanceAfterHumanCommands = false,
                UseInteractiveDiscard = false,
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                MaxTurns = 180
            },
            StandardContentRegistry.Create());
        var result = game.DriveStart();
        var humanAtStart = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        if (result.Status != EngineStatus.AwaitingHumanPlay ||
            humanAtStart.Skills?.Any(skill => skill.ContentId == "standard:longdan") != true)
        {
            continue;
        }

        var steps = 0;
        while (result.Status != EngineStatus.Completed && steps++ < 2_000)
        {
            if (result.Status == EngineStatus.AwaitingHumanResponse)
            {
                var prompt = game.PendingDecision ??
                    throw new InvalidOperationException("The response status has no prompt.");
                if (prompt is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge })
                {
                    var human = game.CreateSnapshot(0, revealAll: true).Players
                        .Single(player => player.Seat == 0);
                    var choice = prompt.Choices.FirstOrDefault(candidate =>
                        candidate.Parameters.GetValueOrDefault("response") == "dodge" &&
                        candidate.Parameters.GetValueOrDefault("response-card-kind") == nameof(CardKind.Dodge) &&
                        candidate.Cards.Count == 1 &&
                        human.Hand.Single(card => card.Id == candidate.Cards[0]).Kind is
                            CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash);
                    if (choice is not null)
                    {
                        selectedGame = game;
                        selectedPrompt = prompt;
                        selectedChoice = choice;
                        selectedResponseCard = human.Hand.Single(card => card.Id == choice.Cards[0]);
                        break;
                    }
                }

                result = prompt.Kind switch
                {
                    DecisionKind.RespondSlash => game.DriveHumanRespondSlash(
                        useSlash: false,
                        advanceToHumanBoundary: false),
                    DecisionKind.RespondDodge => game.DriveHumanRespond(
                        useDodge: false,
                        advanceToHumanBoundary: false),
                    DecisionKind.ProgramTrigger => game.DriveHumanRespondFeedback(
                        useFeedback: false,
                        advanceToHumanBoundary: false),
                    _ => throw new InvalidOperationException(
                        $"Unexpected human response prompt {prompt.Kind} while searching for Longdan.")
                };
                continue;
            }

            result = result.Status switch
            {
                EngineStatus.AwaitingHumanPlay => game.DriveHumanEndPlay(advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanDying => game.DriveHumanRespondDying(
                    usePeach: false,
                    advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanCardSelection => ResolveFirstHarvestChoice(game),
                _ => game.DriveAdvanceOneStep()
            };
        }
    }

    if (selectedGame is null || selectedPrompt is null ||
        selectedChoice is null || selectedResponseCard is null)
    {
        throw new InvalidOperationException("No deterministic Longdan Slash-to-Dodge response boundary was found.");
    }

    var gameWithLongdan = selectedGame!;
    var promptAtBoundary = selectedPrompt!;
    var responseChoice = selectedChoice!;
    var responseCard = selectedResponseCard!;
    Equal(DecisionKind.RespondDodge, promptAtBoundary.Kind);
    Equal(CardKind.Dodge, promptAtBoundary.RequiredCardKind);
    Equal("dodge", responseChoice.Parameters["response"]);
    Equal(nameof(CardKind.Dodge), responseChoice.Parameters["response-card-kind"]);
    TrueWithMessage(responseChoice.Description.Contains("当作【闪】", StringComparison.Ordinal),
        "Longdan response choice exposes card conversion");

    var accepted = gameWithLongdan.Submit(new AnswerPromptCommand(
        ActorSeat: 0,
        Prompt: promptAtBoundary.PromptId,
        Choice: responseChoice.Id,
        ExpectedRevision: gameWithLongdan.Revision));
    TrueWithMessage(accepted.Accepted, "Longdan Slash-to-Dodge response accepted");
    TrueWithMessage(gameWithLongdan.Events.Any(eventItem =>
        eventItem.Payload is CardRespondedEvent responded &&
        responded.CardId == responseCard.Id &&
        responded.ResponderSeat == 0 &&
        responded.EffectiveCardKind == CardKind.Dodge),
        "Longdan response exposes the effective Dodge kind");
    TrueWithMessage(gameWithLongdan.CardMovements.Any(movement =>
        movement.CardId == responseCard.Id &&
        movement.CardKind == responseCard.Kind &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Respond),
        "Longdan response enters Processing with the physical card");
    TrueWithMessage(gameWithLongdan.CardMovements.Any(movement =>
        movement.CardId == responseCard.Id &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.ResponseFinished),
        "Longdan response finishes the physical card");
    Equal(CardLocation.DiscardPile, gameWithLongdan.CreateCardZoneDiagnostics()
        .Single(card => card.CardId == responseCard.Id).Location);
    AssertCardInventory(gameWithLongdan);
}

static void DyingAlcoholRescueFlow()
{
    GameEngine? selectedGame = null;
    PendingDecision? dyingPrompt = null;
    EngineRunResult result = null!;
    for (var seed = 1; seed <= 4_096 && selectedGame is null; seed++)
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
        while (result.Status != EngineStatus.Completed && steps++ < 3_000)
        {
            if (game.PendingDecision?.Kind == DecisionKind.ProgramTrigger)
            {
                result = ResolveIncidentalProgramTrigger(game);
                continue;
            }
            if (result.Status == EngineStatus.AwaitingHumanDying)
            {
                var prompt = game.PendingDecision;
                if (prompt is { TargetSeat: 0 } &&
                    prompt.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "alcohol"))
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
                EngineStatus.AwaitingHumanDying => game.DriveHumanRespondDying(
                    usePeach: false,
                    advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanCardSelection => ResolveFirstHarvestChoice(game),
                _ => game.DriveAdvanceOneStep()
            };
        }
    }

    if (selectedGame is null || dyingPrompt is null)
    {
        throw new InvalidOperationException("No deterministic human dying prompt with a self-rescue Alcohol was found.");
    }

    var gameWithDying = selectedGame!;
    var promptAtBoundary = dyingPrompt!;
    var dyingFrame = gameWithDying.ResolutionStack.OfType<DyingFrame>().Single();
    var alcoholChoice = promptAtBoundary.Choices
        .First(choice => choice.Parameters.GetValueOrDefault("response") == "alcohol");
    var alcoholCardId = alcoholChoice.Cards.Single();
    Equal(EngineStatus.AwaitingHumanDying, gameWithDying.State.Status);
    Equal(0, promptAtBoundary.PlayerSeat);
    Equal(0, promptAtBoundary.TargetSeat);
    TrueWithMessage(alcoholChoice.Description.Contains("自救", StringComparison.Ordinal), "Alcohol self-rescue choice");
    TrueWithMessage(
        promptAtBoundary.Choices.All(choice =>
            choice.Parameters.GetValueOrDefault("response") != "alcohol" ||
            choice.Targets.Count == 0),
        "Alcohol rescue has no other target");

    var otherViewer = gameWithDying.CreateSnapshot(1);
    Equal<PendingDecision?>(null, otherViewer.PendingDecision);
    TrueWithMessage(
        otherViewer.Players.All(player => player.Hand.All(card => card.Id != alcoholCardId)),
        "other viewer hides dying Alcohol");

    var beforeInvalid = gameWithDying.SerializeState();
    var invalid = gameWithDying.Submit(new AnswerPromptCommand(
        0,
        promptAtBoundary.PromptId,
        new ChoiceId("dying.fake-alcohol"),
        gameWithDying.Revision));
    TrueWithMessage(!invalid.Accepted, "invalid Alcohol dying choice rejected");
    Equal(CommandErrorCode.InvalidChoice, invalid.Error!.Code);
    Equal(beforeInvalid, gameWithDying.SerializeState());

    var accepted = gameWithDying.DriveHumanRespondDying(
        usePeach: false,
        requestedPeachCardId: null,
        advanceToHumanBoundary: false,
        useAlcohol: true,
        requestedAlcoholCardId: alcoholCardId);
    TrueWithMessage(accepted.Status != EngineStatus.NotStarted, "Alcohol dying choice accepted");
    var response = gameWithDying.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<DyingResponseEvent>()
        .Single(eventItem =>
            eventItem.ResolutionId == dyingFrame.Id &&
            eventItem.ResponderSeat == 0);
    TrueWithMessage(
        response.UsedAlcohol &&
        response.AlcoholCardId == alcoholCardId &&
        !response.UsedPeach &&
        response.PeachCardId is null,
        "typed Alcohol dying response");
    TrueWithMessage(gameWithDying.Events.Any(eventItem =>
        eventItem.Payload is RecoveryAppliedEvent recovery &&
        recovery.SourceSeat == 0 &&
        recovery.TargetSeat == 0 &&
        recovery.Amount == 1 &&
        recovery.RemainingHp == 1), "Alcohol recovery event");
    TrueWithMessage(gameWithDying.Events.Any(eventItem =>
        eventItem.Payload is CardUseFinishedEvent finished &&
        finished.CardId == alcoholCardId &&
        finished.CardKind == CardKind.Alcohol), "Alcohol recovery card finished");
    TrueWithMessage(gameWithDying.Events.Any(eventItem =>
        eventItem.Payload is DyingResolvedEvent resolved &&
        resolved.ResolutionId == dyingFrame.Id &&
        resolved.Survived), "Alcohol dying resolution survived");
    TrueWithMessage(gameWithDying.CardMovements.Any(movement =>
        movement.CardId == alcoholCardId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Use), "Alcohol rescue entered Processing");
    TrueWithMessage(gameWithDying.CardMovements.Any(movement =>
        movement.CardId == alcoholCardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.UseFinished), "Alcohol rescue discarded");
    Equal(1, accepted.State.Players.Single(player => player.Seat == 0).Hp);
    Equal(
        CardLocation.DiscardPile,
        gameWithDying.CreateCardZoneDiagnostics().Single(card => card.CardId == alcoholCardId).Location);
    TrueWithMessage(
        gameWithDying.ResolutionStack.All(frame => frame.Id != dyingFrame.Id),
        "Alcohol dying frame completed");
    AssertCardInventory(gameWithDying);
}

static void DyingAlcoholOnlySelfRule()
{
    TrueWithMessage(GameCheckpoint.CurrentRulesVersion >= 12, "formal Alcohol rules version");
    GameEngine? current = null;
    CardSnapshot? alcohol = null;
    for (var seed = 1; seed <= 4_096 && current is null; seed++)
    {
        var candidate = GameEngine.CreateStandard(new GameOptions
        {
            UseInteractiveDiscard = false,
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        }, StandardContentRegistry.Create());
        var candidateAlcohol = candidate.CreateSnapshot(0).Players[0].Hand
            .FirstOrDefault(card => card.Kind == CardKind.Alcohol);
        if (candidateAlcohol is not null)
        {
            current = candidate;
            alcohol = candidateAlcohol;
        }
    }

    NotNull(current);
    NotNull(alcohol);
    var currentGame = current!;
    var alcoholCard = alcohol!;
    var playersField = typeof(GameEngine).GetField(
        "_players",
        BindingFlags.NonPublic | BindingFlags.Instance) ??
        throw new InvalidOperationException("Runtime players field not found.");
    var alcoholMethod = typeof(GameEngine).GetMethod(
        "GetDyingAlcohols",
        BindingFlags.NonPublic | BindingFlags.Instance) ??
        throw new InvalidOperationException("Dying Alcohol query not found.");
    static object[] RuntimePlayers(FieldInfo field, GameEngine game) =>
        ((System.Collections.IEnumerable)field.GetValue(game)!).Cast<object>().ToArray();
    static Card[] DyingAlcohols(MethodInfo method, GameEngine game, object responder, int victimSeat) =>
        (Card[])method.Invoke(game, [responder, victimSeat])!;

    var runtimePlayers = RuntimePlayers(playersField, currentGame);
    var selfAlcohols = DyingAlcohols(alcoholMethod, currentGame, runtimePlayers[0], victimSeat: 0);
    var otherAlcohols = DyingAlcohols(alcoholMethod, currentGame, runtimePlayers[0], victimSeat: 1);
    TrueWithMessage(selfAlcohols.Any(card => card.Id == alcoholCard.Id), "current self-rescue Alcohol legality");
    Equal(0, otherAlcohols.Length);

    var view = currentGame.CreateSnapshot(0) with
    {
        Players = currentGame.CreateSnapshot(0).Players
            .Select(player => player.Seat == 1 ? player with { Hp = 0 } : player)
            .ToArray()
    };
    var physicalAlcohol = new Card(alcoholCard.Id, alcoholCard.Kind, alcoholCard.Suit, alcoholCard.Rank);
    var formalOther = new SimpleAiBrain(0, 791, 2).ChooseDyingResponseWithAlcohol(
        view,
        victimSeat: 1,
        peaches: [],
        alcohols: [physicalAlcohol],
        thoughtSequence: 1,
        allowCrossSeatAlcoholRescue: false);
    False(formalOther.UseAlcohol);
    TrueWithMessage(
        formalOther.Thought.Candidates.All(candidate => candidate.Action.Kind != LegalActionKind.Alcohol),
        "current AI omits illegal cross-seat Alcohol");
    var formalSelf = new SimpleAiBrain(0, 791, 2).ChooseDyingResponseWithAlcohol(
        view with
        {
            Players = view.Players
                .Select(player => player.Seat == 0 ? player with { Hp = 0 } : player)
                .ToArray()
        },
        victimSeat: 0,
        peaches: [],
        alcohols: [physicalAlcohol],
        thoughtSequence: 2,
        allowCrossSeatAlcoholRescue: false);
    True(formalSelf.UseAlcohol);
    Equal(alcoholCard.Id, formalSelf.AlcoholCardId);
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

static void UncaughtObserverReentryIsIsolated()
{
    var game = GameEngine.CreateStandard(new GameOptions
    {
        UseInteractiveDiscard = false,
        Seed = 347,
        HumanSeat = 0,
        HumanRole = Role.Lord
    }, StandardContentRegistry.Create());
    var laterObserverCalls = 0;
    CommandErrorCode? reentryError = null;
    game.LogAdded += _ => reentryError = game.Submit(new AdvanceCommand(game.Revision)).Error?.Code;
    game.LogAdded += _ => laterObserverCalls++;

    var result = game.DriveStart();

    Equal(EngineStatus.AwaitingHumanPlay, result.Status);
    True(laterObserverCalls > 0);
    Equal(CommandErrorCode.ReentrantOperation, reentryError);
    Equal(0, game.ObserverFailures.Count);
    AssertCardInventory(game);
}

static void HumanPlayApi()
{
    GameEngine? selectedGame = null;
    LegalAction? selectedAction = null;

    // Search a small deterministic seed range instead of coupling the test to one deck order.
    for (var seed = 1; seed <= 64 && selectedAction is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions { AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false, Seed = seed, HumanSeat = 0, HumanRole = Role.Lord }, StandardContentRegistry.Create());
        var result = game.DriveStart();
        var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
            candidate.Kind is LegalActionKind.Slash or LegalActionKind.Peach);
        if (result.Status == EngineStatus.AwaitingHumanPlay && action is not null)
        {
            selectedGame = game;
            selectedAction = action;
        }
    }

    NotNull(selectedGame);
    NotNull(selectedAction);
    var next = selectedGame!.DriveHumanPlay(selectedAction!.CardId!.Value, selectedAction.TargetSeat);
    True(next.Status is EngineStatus.AwaitingHumanPlay or
        EngineStatus.AwaitingHumanResponse or
        EngineStatus.AwaitingHumanDying or
        EngineStatus.AwaitingHumanCardSelection or
        EngineStatus.Completed);
    True(selectedGame!.Log.Any(entry => entry.Type is "CardUsed" or "Recovered"));
}

static void LethalHumanResponseKeepsCompletedStatus()
{
    var exercised = false;
    for (var seed = 1; seed <= 128 && !exercised; seed++)
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
        var completedCardTotals = new List<int>();
        game.StateChanged += snapshot =>
        {
            if (snapshot.Status == EngineStatus.Completed)
            {
                completedCardTotals.Add(
                    snapshot.DrawPileCount +
                    snapshot.DiscardPileCount +
                    snapshot.ProcessingCardCount +
                    snapshot.Players.Sum(player =>
                        player.HandCount + player.Equipment.Count + player.Judgment.Count));
            }
        };
        var result = game.DriveStart();
        var decisions = 0;
        while (result.Status != EngineStatus.Completed && decisions++ < 500)
        {
            if (game.PendingDecision?.Kind == DecisionKind.ProgramTrigger)
            {
                result = ResolveIncidentalProgramTrigger(game);
                continue;
            }
            if (result.Status == EngineStatus.AwaitingHumanPlay)
            {
                result = game.DriveHumanEndPlay();
                continue;
            }

            if (result.Status == EngineStatus.AwaitingHumanResponse)
            {
                if (game.PendingDecision?.Kind == DecisionKind.RespondSlash)
                {
                    result = game.DriveHumanRespondSlash(useSlash: false);
                    continue;
                }

                var hpBefore = game.State.Players.Single(player => player.Seat == 0).Hp;
                result = game.DriveHumanRespond(useDodge: false);
                if (hpBefore == 1)
                {
                    if (result.Status == EngineStatus.AwaitingHumanDying)
                    {
                        result = game.DriveHumanRespondDying(usePeach: false);
                    }

                    if (result.Status == EngineStatus.Completed)
                    {
                        True(result.Winner is Winner.Rebels or Winner.Renegade);
                        Equal(EngineStatus.Completed, game.State.Status);
                        Equal(1, completedCardTotals.Count);
                        Equal(90, completedCardTotals[0]);
                        exercised = true;
                    }
                }

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
            }
        }
    }

    True(exercised);
}

static void AdvanceOneStepApi()
{
    var game = GameEngine.CreateStandard(new GameOptions
    {
        AdvanceAfterHumanCommands = false,
        UseInteractiveDiscard = false,
        Seed = 31,
        HumanSeat = 0,
        HumanRole = Role.Lord
    }, StandardContentRegistry.Create());

    Equal(EngineStatus.AwaitingHumanPlay, game.DriveStart().Status);
    var afterHuman = game.DriveHumanEndPlay(advanceToHumanBoundary: false);
    Equal(EngineStatus.Running, afterHuman.Status);
    Equal(TurnPhase.Discard, afterHuman.State.Phase);

    var thoughtsBefore = game.AiThoughts.Count;
    var afterDiscard = game.DriveAdvanceOneStep();
    Equal(TurnPhase.NotStarted, afterDiscard.State.Phase);
    Equal(thoughtsBefore, game.AiThoughts.Count);

    var afterAiTurnStart = game.DriveAdvanceOneStep();
    Equal(TurnPhase.Play, afterAiTurnStart.State.Phase);
    Equal(thoughtsBefore, game.AiThoughts.Count);

    game.DriveAdvanceOneStep();
    Equal(thoughtsBefore + 1, game.AiThoughts.Count);

    var observedAiDodge = false;
    var result = game.State.Status == EngineStatus.Completed
        ? new EngineRunResult(game.State.Status, game.State.Winner, game.State, game.PendingDecision)
        : new EngineRunResult(game.State.Status, game.State.Winner, game.State, game.PendingDecision);
    for (var step = 0; step < 1_000 && result.Status != EngineStatus.Completed; step++)
    {
        if (game.PendingDecision?.Kind == DecisionKind.ProgramTrigger)
        {
            result = ResolveIncidentalProgramTrigger(game);
            continue;
        }
        if (result.Status == EngineStatus.AwaitingHumanPlay)
        {
            result = game.DriveHumanEndPlay(advanceToHumanBoundary: false);
            continue;
        }

        if (result.Status == EngineStatus.AwaitingHumanResponse)
        {
            result = game.PendingDecision?.Kind == DecisionKind.RespondSlash
                ? game.DriveHumanRespondSlash(useSlash: false, advanceToHumanBoundary: false)
                : game.DriveHumanRespond(useDodge: false, advanceToHumanBoundary: false);
            continue;
        }

        if (result.Status == EngineStatus.AwaitingHumanDying)
        {
            result = game.DriveHumanRespondDying(usePeach: false, advanceToHumanBoundary: false);
            continue;
        }

        if (result.Status == EngineStatus.AwaitingHumanCardSelection)
        {
            result = ResolveFirstHarvestChoice(game);
            continue;
        }

        var before = game.AiThoughts.Count;
        result = game.DriveAdvanceOneStep();
        var emitted = game.AiThoughts.Count - before;
        True(emitted <= 1);
        if (emitted == 1 && game.AiThoughts[^1].Decision == "打出闪")
        {
            observedAiDodge = true;
            break;
        }
    }

    True(observedAiDodge);
}

static void AiEndPlayPublishesState()
{
    GameEngine? selectedGame = null;
    EngineRunResult? selectedResult = null;
    List<GameSnapshot>? published = null;
    for (var seed = 1; seed <= 128 && selectedGame is null; seed++)
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
        game.DriveStart();
        game.DriveHumanEndPlay(advanceToHumanBoundary: false);
        var snapshots = new List<GameSnapshot>();
        game.StateChanged += snapshot => snapshots.Add(snapshot);
        var result = game.DriveAdvanceOneStep();
        for (var step = 0; step < 400 &&
                          result.Status != EngineStatus.Completed &&
                          result.State.Phase != TurnPhase.Discard; step++)
        {
            if (result.Status == EngineStatus.AwaitingHumanPlay)
            {
                result = game.DriveHumanEndPlay(advanceToHumanBoundary: false);
            }
            else if (result.Status == EngineStatus.AwaitingHumanResponse)
            {
                result = game.PendingDecision?.Kind == DecisionKind.RespondSlash
                    ? game.DriveHumanRespondSlash(useSlash: false, advanceToHumanBoundary: false)
                    : game.DriveHumanRespond(useDodge: false, advanceToHumanBoundary: false);
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
        }

        if (result.Status != EngineStatus.Completed && result.State.Phase == TurnPhase.Discard)
        {
            selectedGame = game;
            selectedResult = result;
            published = snapshots;
        }
    }

    NotNull(selectedGame);
    NotNull(selectedResult);
    NotNull(published);
    Equal(TurnPhase.Discard, selectedResult!.State.Phase);
    True(published!.Count > 0);
    Equal(
        SnapshotJson.Serialize(selectedResult.State),
        SnapshotJson.Serialize(published[^1]));
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

static void AiMatchSmoke()
{
    for (var seed = 1; seed <= 32; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            UseInteractiveDiscard = false,
            Seed = seed,
            HumanSeat = -1,
            HumanRole = null,
            MaxTurns = 250
        }, StandardContentRegistry.Create());

        var result = game.DriveStart();
        Equal(EngineStatus.Completed, result.Status);
        True(result.Winner != Winner.None);
        True(game.AiThoughts.Count > 0);
        True(game.AiThoughts.All(thought => thought.Candidates.Count > 0));
    }
}

static void SnapshotSerialization()
{
    var game = GameEngine.CreateStandard(new GameOptions { UseInteractiveDiscard = false, Seed = 123 }, StandardContentRegistry.Create());
    using var json = JsonDocument.Parse(game.SerializeState());
    Equal("Lord", json.RootElement.GetProperty("Players")[0].GetProperty("Role").GetString());
    Equal(8, json.RootElement.GetProperty("Players").GetArrayLength());
}

static bool IsSlashCard(CardKind kind) =>
    kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash;

static bool HasSkill(GameEngine game, string skill) =>
    game.CreateSnapshot(0, revealAll: true).Players.Any(player => player.Skills?.Any(entry => entry.ContentId == skill) == true);

static bool IsDamageTriggerSkill(string? skill) =>
    skill is "standard:jianxiong" or
        "standard:feedback" or
        "standard:yiji" or
        "standard:jieming" or
        "standard:yuanhu" or
        "standard:ganglie";

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
