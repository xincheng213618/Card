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
    ("shared post-event HP actual amounts and replay", SharedPostEventChecks.HpLossRecoveryAndActualAmountsReplay),
    ("shared post-event dying recovery ordering and replay", SharedPostEventChecks.HpLossWaitsForDyingAndCardRecoveryFinishesFirst),
    ("shared post-event definition context validation", SharedPostEventChecks.DefinitionFiltersRejectWrongContexts),
    ("shared use lifecycle nests rescue inside a suspended card window", CardUseLifecycleChecks.NestedRescueRetainsTheOuterUseWindow),
    ("shared card-use phase-owner movement context validation", CardUseLifecycleChecks.PhaseOwnerDestinationRequiresPhaseContext),
    ("current classic catalogue modes and skill references", CurrentClassicContentChecks.CatalogueAndModes),
    ("turn-ending Xiaoguo Tianxiang game over stops later observers", TurnEndingGameOverChecks.XiaoguoTianxiangVictoryStopsLaterObservers),
    ("2014 boundary Zhou Yu definition version and public gift resource contracts", BoundaryZhouYuChecks.DefinitionAndResourceContracts),
    ("2014 boundary Zhou Yu Yingzi Fanjian transfer choice and replay", BoundaryZhouYuChecks.TransferChoiceAndReplay),
    ("2014 boundary Zhou Yu Fanjian requires a real hand card", BoundaryZhouYuChecks.EmptyHandCannotActivate),
    ("2014 boundary Zhou Yu recipient discard and HP choices replay", BoundaryZhouYuChecks.RecipientCanDiscardOrLoseHp),
    ("2014 boundary Zhou Yu Fanjian uses Hongyan effective suits", BoundaryZhouYuChecks.HongyanChangesRecipientSuitOnly),
    ("2014 boundary Zhou Yu Yingzi respects Luoyi draw replacement", BoundaryZhouYuChecks.YingziRespectsDrawReplacement),
    ("2014 boundary Zhou Yu Yingzi tracks actual lost HP", BoundaryZhouYuChecks.YingziTracksActualLostHp),
    ("2014 boundary Zhou Yu Fanjian discards matching equipment", BoundaryZhouYuChecks.FanjianDiscardsMatchingEquipment),
    ("2017 Cao Ang definition and observer distance schema", CaoAngChecks.DefinitionAndObserverDistanceSchema),
    ("2017 Cao Ang nearby gift reveals and recipient may equip with replay", CaoAngChecks.NearbyTargetGiftRevealsAndRecipientMayEquip),
    ("2017 Cao Ang non-equipment gift stays without use prompt", CaoAngChecks.NonEquipmentGiftKeepsRecipientHandWithoutUsePrompt),
    ("2017 Cao Ang distance beyond one does not trigger", CaoAngChecks.DistanceBeyondOneDoesNotTrigger),
    ("2017 Cao Ang self-target draws only without gift", CaoAngChecks.SelfTargetSlashDrawsOnlyWithoutGift),
    ("Qu Yi definition and shared response and damage rules", QuYiChecks.DefinitionAndSharedRules),
    ("Qu Yi nearby Slash response precedes Jiaozi damage", QuYiChecks.NearbySlashCannotRespondBeforeDamageBonus),
    ("Qu Yi global trick checks each target response range", QuYiChecks.GlobalTrickResponseUsesEachTargetsDistance),
    ("Qu Yi Arrow Barrage checks each target response range", QuYiChecks.ArrowBarrageResponseUsesEachTargetsDistance),
    ("Qu Yi nearby Duel response precedes damage", QuYiChecks.NearbyDuelCannotRequestSlash),
    ("Qu Yi Jiaozi modifies cardless damage", QuYiChecks.CardlessDamageUsesTheSameDamageModifier),
    ("Qu Yi original trick nullification skips nearby target", QuYiChecks.OriginalTrickNullificationSkipsNearbyTarget),
    ("Qu Yi Jiaozi rejects a hand-count tie at damage time", QuYiChecks.TiedHandCountDoesNotIncreaseCardlessDamage),
    ("Qu Yi Jiaozi raises incoming damage for its owner", QuYiChecks.IncomingDamageChecksTheTargetOwner),
    ("response equipment and Chanyuan keep temporary suppression distinct", ResponseAndSkillSuppressionChecks.DefinitionsAndChanyuanRestoresSkills),
    ("Zhang Xiu Xiongluan abolishes areas and blocks hand without armor bypass", ResponseAndSkillSuppressionChecks.XiongluanBlocksHandButDoesNotIgnoreArmor),
    ("Xingtian Axe pays two cards then blocks hand and armor", ResponseAndSkillSuppressionChecks.XingtianPaysTwoAndBlocksOnlyHandCards),
    ("Cai Wenji Duanchang permanently removes killer skills", ResponseAndSkillSuppressionChecks.DuanchangPermanentlyRemovesKillersSkills),
    ("red Slash and Scarlet Blood Sword gate response before damage", ResponseAndSkillSuppressionChecks.SlashResponseRestrictionsRespectWeaponAndSuit),
    ("2026 Shen Sima Yi definition and kill-window schema", ShenSimaYiChecks.DefinitionAndKillWindowSchema),
    ("2026 Shen Sima Yi damage and discard both grant Ren markers", ShenSimaYiChecks.DamageAndDiscardBothGrantRenMarkers),
    ("2026 Shen Sima Yi hand-limit discards grant Ren markers", ShenSimaYiChecks.HandLimitDiscardsGrantRenMarkers),
    ("2026 Shen Sima Yi awakening at four markers grants Lianpo", ShenSimaYiChecks.AwakeningAtFourMarkersGrantsLianpo),
    ("2026 Shen Sima Yi kill grants exactly one extra turn with replay", ShenSimaYiChecks.KillGrantsExactlyOneExtraTurnAndReplays),
    ("2026 Shen Sima Yi declined kill keeps normal rotation", ShenSimaYiChecks.DeclinedKillKeepsNormalRotation),
    ("2026 Shen Sima Yi extra-turn kill chains another extra turn", ShenSimaYiChecks.ExtraTurnKillChainsAnotherExtraTurn),
    ("2010 Cao Pi definition and trigger schema", CaoPiChecks.DefinitionAndTriggerSchema),
    ("2010 Cao Pi Xingshang claims died player cards and replays", CaoPiChecks.XingShangClaimsDiedPlayerCardsAndReplays),
    ("2010 Cao Pi declined Xingshang keeps victim cards in discard", CaoPiChecks.XingShangSkipKeepsVictimCardsInDiscard),
    ("2010 Cao Pi Fangzhu flips target and draws owner lost HP", CaoPiChecks.FangZhuFlipsTargetAndDrawsOwnerLostHp),
    ("2011 Sun Ce definition and trigger schema", SunCeChecks.DefinitionAndTriggerSchema),
    ("2011 Sun Ce Jiang draws when using a duel and replays", SunCeChecks.JiangDrawsWhenUsingDuelAndReplays),
    ("2011 Sun Ce Jiang draws when targeted but not on black slash", SunCeChecks.JiangDrawsWhenTargetedButNotOnBlackSlash),
    ("2011 Sun Ce Hunzi awakens and grants Yingzi and Yinghun", SunCeChecks.HunziAwakensGrantsSkillsAndReplays),
    ("2014 Jie Zhao Yun definition and trigger schema", BoundaryZhaoYunChecks.DefinitionAndTriggerSchema),
    ("2011 Shen Zhao Yun definition and trigger schema", ShenZhaoYunChecks.DefinitionAndTriggerSchema),
    ("2011 Shen Zhao Yun Juejing skips draw refills and caps at four", ShenZhaoYunChecks.JuejingSkipsDrawRefillsAndCapsAtFour),
    ("2011 Shen Zhao Yun Longhun responds with club dodge and plays diamond fire slash", ShenZhaoYunChecks.LonghunRespondsWithClubDodgeAndPlaysDiamondFireSlash),
    ("2011 Shen Zhao Yun Zhanjiang takes Qinggang sword from field", ShenZhaoYunChecks.ZhanjiangTakesQinggangSwordFromField),
    ("Gundam One definition and trigger schema", GaoDaYiHaoChecks.DefinitionAndTriggerSchema),
    ("Gundam One beam rifle discards one card and deals damage once per turn", GaoDaYiHaoChecks.BeamRifleDiscardsOneAndDamagesOncePerTurn),
    ("Gundam One mobile armor extends range and grants second slash", GaoDaYiHaoChecks.MobileArmorExtendsRangeAndGrantsSecondSlash),
    ("Gundam One I-field prevents incoming trick damage", GaoDaYiHaoChecks.IFieldPreventsIncomingTrickDamage),
    ("Gundam One core fighter revives once per game", GaoDaYiHaoChecks.CoreFighterRevivesOncePerGame),
    ("2014 Jie Zhao Yun Longdan converts slash to dodge and fires Yajiao", BoundaryZhaoYunChecks.LongdanConvertsSlashToDodgeAndFiresYajiao),
    ("2014 Jie Zhao Yun Yajiao does not fire on own turn use", BoundaryZhaoYunChecks.YajiaoDoesNotFireOnOwnTurnUse),
    ("2014 Jie Zhao Yun Yajiao mismatch discards from ranged player and replays", BoundaryZhaoYunChecks.YajiaoMismatchDiscardsFromRangedPlayerAndReplays),
    ("2011 Cai Wenji definition and trigger schema", CaiWenJiChecks.DefinitionAndTriggerSchema),
    ("2011 Cai Wenji Beige resolves one branch per judgment and replays", CaiWenJiChecks.BeigeResolvesOneBranchPerJudgmentAndReplays),
    ("2011 Cai Wenji club branch makes the source discard two own cards", CaiWenJiChecks.ClubBranchMakesTheSourceDiscardTwoOwnCards),
    ("2011 Cai Wenji spade branch turns the source over", CaiWenJiChecks.SpadeBranchTurnsTheSourceOver),
    ("2011 Cao Zhi definition and trigger schema", CaoZhiChecks.DefinitionAndTriggerSchema),
    ("2011 Cao Zhi Luoying claims another player's discarded club and replays", CaoZhiChecks.LuoyingClaimsAnotherPlayersDiscardedClubAndReplays),
    ("2011 Cao Zhi Jiushi rescues the dying owner by flipping and replays", CaoZhiChecks.JiushiRescuesDyingOwnerByFlippingAndReplays),
    ("2011 Cao Zhi Jiushi flips back after damage while face down and replays", CaoZhiChecks.JiushiFlipsBackAfterDamageWhileFaceDown),
    ("2011 Jiang Wei definition and trigger schema", JiangWeiChecks.DefinitionAndTriggerSchema),
    ("2011 Jiang Wei Tiaoxin forces a Slash at the owner and replays", JiangWeiChecks.TiaoxinForcesSlashAgainstOwnerAndReplays),
    ("2011 Jiang Wei Tiaoxin discards from a target that cannot respond", JiangWeiChecks.TiaoxinDiscardsWhenTargetCannotRespond),
    ("2011 Jiang Wei Zhiji awakens with recovery and grants Guanxing", JiangWeiChecks.ZhijiAwakensWithRecoveryAndGrantsGuanxing),
    ("2011 Jiang Wei Zhiji awakens with two drawn cards and replays", JiangWeiChecks.ZhijiAwakensWithDrawTwoAndReplays),
    ("2011 Jiang Wei Zhiji stays dormant while hand cards remain", JiangWeiChecks.ZhijiStaysDormantWhileHandCardsRemain),
    ("2011 Dong Zhuo definition and trigger schema", DongZhuoChecks.DefinitionAndTriggerSchema),
    ("2011 Dong Zhuo Jiuchi plays a spade hand card as Alcohol and replays", DongZhuoChecks.JiuchiPlaysSpadeHandCardAsAlcoholAndReplays),
    ("2011 Dong Zhuo Jiuchi rescues a dying owner with a spade hand card", DongZhuoChecks.JiuchiRescuesDyingOwnerWithSpadeHandCard),
    ("2011 Dong Zhuo Roulin demands two dodges from a female target", DongZhuoChecks.RoulinDemandsTwoDodgesFromFemaleTarget),
    ("2011 Dong Zhuo Roulin spares a male target that dodges once", DongZhuoChecks.RoulinSparesMaleTargetWithOneDodge),
    ("2011 Dong Zhuo Roulin demands two dodges when a female slashes the owner", DongZhuoChecks.RoulinDemandsTwoDodgesWhenFemaleSlashesOwner),
    ("2011 Dong Zhuo Benghuai stays silent while nobody has lower HP", DongZhuoChecks.BenghuaiStaysSilentWhileNobodyIsLower),
    ("2011 Dong Zhuo Benghuai loses one HP at the end phase and replays", DongZhuoChecks.BenghuaiLoseHpBranchReplays),
    ("2011 Dong Zhuo Benghuai reduces one maximum HP at the end phase", DongZhuoChecks.BenghuaiReduceMaximumHpBranchReplays),
    ("2011 Dong Zhuo Baonve judges from qun damage and recovers on spade", DongZhuoChecks.BaonveJudgesQunDamageAndRecoversOnSpade),
    ("2011 Dong Zhuo Baonve ignores owner and non-qun damage sources", DongZhuoChecks.BaonveIgnoresOwnerAndNonQunSources),
    ("2011 Liu Shan definition and trigger schema", LiuShanChecks.DefinitionAndTriggerSchema),
    ("2011 Liu Shan Xianle paid basic card lets the Slash resolve", LiuShanChecks.XianglePaidBasicCardLetsSlashResolve),
    ("2011 Liu Shan Xianle nullifies the Slash when the actor cannot pay", LiuShanChecks.XiangleNullifiesSlashWhenActorCannotPay),
    ("2011 Liu Shan Xianle ignores non-Slash cards", LiuShanChecks.XiangleIgnoresNonSlashCards),
    ("2011 Liu Shan Fangquan skip grants an extra turn to the selected target", LiuShanChecks.FangquanSkipGrantsExtraTurnToSelectedTarget),
    ("2011 Liu Shan Fangquan decline keeps the play phase and grants nothing", LiuShanChecks.FangquanDeclineKeepsPlayPhaseAndGrantsNothing),
    ("2011 Liu Shan Ruoyu awakens at minimum HP and gains Jijiang", LiuShanChecks.RuoyuAwakensAtMinimumHpAndGainsJijiang),
    ("2011 Liu Shan Ruoyu stays dormant when not the minimum HP", LiuShanChecks.RuoyuStaysDormantWhenNotMinimumHp),
    ("2011 Liu Shan Ruoyu is not owned by a non-lord", LiuShanChecks.RuoyuNotOwnedByNonLord),

    ("2011 Lu Su definition and trigger schema", LuSuChecks.DefinitionAndTriggerSchema),
    ("2011 Lu Su Haoshi gives half hand after extra draw and replays", LuSuChecks.HaoshiGivesHalfHandAfterExtraDrawAndReplays),
    ("2011 Lu Su declining Haoshi skips the give", LuSuChecks.DecliningHaoshiSkipsTheGive),
    ("2011 Lu Su Dimeng swaps equal hands without discard", LuSuChecks.DimengSwapsEqualHandsWithoutDiscard),
    ("2011 Lu Su Dimeng discards the difference before swapping", LuSuChecks.DimengDiscardsTheDifferenceBeforeSwapping),
    ("Classic Deng Ai definition and trigger schema", DengAiChecks.DefinitionAndTriggerSchema),
    ("Classic Deng Ai Tuntian stores fields, reduces distance and replays", DengAiChecks.TuntianStoresFieldsReducesDistanceAndReplays),
    ("Classic Deng Ai heart judgment stays out and distance unchanged", DengAiChecks.HeartJudgmentStaysOutAndDistanceUnchanged),
    ("Classic Deng Ai Zaoxian awakens and grants Jixi", DengAiChecks.ZaoxianAwakensGrantsJixiAndReplays),
    ("Classic Deng Ai Jixi converts a field into a Snatch", DengAiChecks.JixiConvertsFieldIntoSnatchAndReplays),
    ("Classic Sha Mo Ke definition and trigger schema", ShaMoKeChecks.DefinitionAndTriggerSchema),
    ("Classic Sha Mo Ke Jili draws on the first use only and replays", ShaMoKeChecks.FirstUseDrawsOnceSecondUseDoesNotAndReplays),
    ("Classic Sha Mo Ke Jili draws on the first foreign-turn response", ShaMoKeChecks.FirstResponseDuringForeignTurnDrawsAndReplays),
    ("Classic Zhang He definition and trigger schema", ZhangHeChecks.DefinitionAndTriggerSchema),
    ("Classic Zhang He skip-draw branch takes one card from each target and replays", ZhangHeChecks.SkipDrawBranchTakesOneCardFromEachTargetAndReplays),
    ("Classic Zhang He skip-discard branch keeps hand cards and replays", ZhangHeChecks.SkipDiscardBranchKeepsHandCardsAndReplays),
    ("Classic Zhang He skip-play branch moves equipment to the paired target and replays", ZhangHeChecks.SkipPlayBranchMovesEquipmentToPairedTargetAndReplays),
    ("Classic Zhang Zhao Zhang Hong definition and trigger schema", ZhangZhaoZhangHongChecks.DefinitionAndTriggerSchema),
    ("Classic Zhang Zhao Zhang Hong Zhijian equips a free slot and replays", ZhangZhaoZhangHongChecks.ZhijianEquipsFreeSlotAndDrawsAndReplays),
    ("Classic Zhang Zhao Zhang Hong Guzheng returns one card and takes the rest", ZhangZhaoZhangHongChecks.GuzhengReturnsOneAndTakesRestAndReplays),
    ("Classic Jia Xu definition and policy schema", JiaXuChecks.DefinitionAndPolicySchema),
    ("Classic Jia Xu Wansha spares the victim and blocks other peaches with replay", JiaXuChecks.WanshaSparesTheVictimAndBlocksOthersAndReplays),
    ("Classic Jia Xu Luanwu forces nearest Slashes or HP loss and replays", JiaXuChecks.LuanwuForcesNearestSlashesOrLossAndReplays),
    ("Classic Jia Xu Weimu blocks only black trick targets", JiaXuChecks.WeimuBlocksOnlyBlackTrickTargets),
    ("God Shen Lu Meng definition and skill schema", ShenLuMengChecks.DefinitionAndSkillSchema),
    ("God Shen Lu Meng Shelie replaces the draw with one card per suit and replays", ShenLuMengChecks.ShelieReplacesDrawWithDistinctSuitsAndReplays),
    ("God Shen Lu Meng Shelie decline keeps the normal draw", ShenLuMengChecks.ShelieDeclineKeepsNormalDraw),
    ("God Shen Lu Meng Gongxin reveals a heart, discards it and replays", ShenLuMengChecks.GongxinRevealsHeartDiscardsItAndReplays),
    ("God Shen Lu Meng Gongxin places the heart on the draw pile top and replays", ShenLuMengChecks.GongxinPlacesHeartOnDrawPileTopAndReplays),
    ("God Shen Lu Meng Gongxin declines a heart-less hand privately", ShenLuMengChecks.GongxinDeclineWithoutHeartKeepsHandHidden),
    ("God Shen Cao Cao definition and trigger schema", ShenCaoCaoChecks.DefinitionAndTriggerSchema),
    ("God Shen Cao Cao Guixin claims from every other character and replays", ShenCaoCaoChecks.GuixinClaimsFromEveryOtherCharacterAndReplays),
    ("God Shen Cao Cao Feiying raises incoming distance", ShenCaoCaoChecks.FeiyingRaisesIncomingDistance),
    ("Classic YuJi definition and activation schema", YuJiChecks.DefinitionAndSchema),
    ("Classic YuJi true flip grants Chanyuan and replays", YuJiChecks.GuhuoTrueFlipGrantsChanyuanAndReplays),
    ("Classic YuJi false flip voids the placed card and replays", YuJiChecks.GuhuoFalseFlipVoidsAndReplays),
    ("Classic YuJi Guhuo stays once per turn and reopens next turn", YuJiChecks.GuhuoIsOncePerAnyTurnAndReopensNextTurn),
    ("Classic YuJi Chanyuan holder skips doubt and loses other skills at one HP", YuJiChecks.ChanyuanHolderSkipsDoubtAndSuppressesOtherSkillsAtOneHp),
    ("ZuoCi avatar definition and trigger schema", ZuoCiChecks.DefinitionAndTriggerSchema),
    ("ZuoCi setup declaration is seed reproducible", ZuoCiChecks.SetupDeclarationIsSeedReproducible),
    ("ZuoCi declaration excludes lord skills", ZuoCiChecks.DeclarationExcludesLordSkills),
    ("ZuoCi gender is treated as the revealed avatar", ZuoCiChecks.GenderTreatedAsRevealedAvatar),
    ("ZuoCi faction is treated as the revealed avatar", ZuoCiChecks.FactionTreatedAsRevealedAvatar),
    ("ZuoCi Xinsheng gains an avatar on damage and replays", ZuoCiChecks.XinShengGainsAvatarOnDamageAndReplays),
    ("ZuoCi changes the avatar at turn boundaries", ZuoCiChecks.ChangeAvatarAtTurnBoundaries),
    ("2013 Pan Zhang Ma Zhong definition and generic schema 56", PanZhangMaZhongChecks.DefinitionAndGenericSchema),
    ("2013 Pan Zhang Ma Zhong natural far Slash and replay", PanZhangMaZhongChecks.NaturalSlashReverseRangeAndReplay),
    ("2013 Pan Zhang Ma Zhong Duodao pays and claims weapon with replay", PanZhangMaZhongChecks.NaturalDuodaoPaymentWeaponAndReplay),
    ("generic source armor selection transfers real equipment", PanZhangMaZhongChecks.GenericSourceArmorSelectionActuallyMovesCard),
    ("2013 Pan Zhang Ma Zhong Duodao unarmed source payment and decline", PanZhangMaZhongChecks.DuodaoMayPayWithoutSourceWeaponAndMayDecline),
    ("2013 Pan Zhang Ma Zhong Anjian amount survives Tianxiang transfer", PanZhangMaZhongChecks.AnjianAmountIsFrozenAcrossTianxiangTransfer),
    ("2014 boundary Xu Chu registers and validates general card filter", BoundaryXuChuChecks.DefinitionAndGeneralFilterContract),
    ("Xu Sheng Pojun definition and generic hold contract", XuShengChecks.DefinitionAndContentContract),
    ("Xu Sheng Pojun holds target cards and returns them at turn end", XuShengChecks.ClassicPojunHoldsAndReturnsAtTurnEnd),
    ("Boundary Xu Sheng Pojun damage bonus tracks hand and equipment counts", XuShengChecks.BoundaryPojunDamageBonusTracksCardCounts),
    ("Boundary Xu Sheng Pojun triggers for an off-turn Borrowed Sword Slash", XuShengChecks.BoundaryPojunTriggersOutsideOwnTurn),
    ("Pojun hold cards are discarded when the holder dies", XuShengChecks.PojunHoldsAreDiscardedWhenHolderDies),
    ("Zhang Song equipment use, replacement and replay", ZhangSongChecks.EquipmentUsesReplaceAndResumeExactlyOnce),
    ("Zhang Song category mismatch and phase lifetime", ZhangSongChecks.CategoryMismatchAndPhaseLifetime),
    ("Zhang Song Xiantu original cards and play-end timing", ZhangSongChecks.XiantuSelectsExistingCardsAndPenalizesBeforeDiscard),
    ("Zhang Song Qiangzhi and Xiantu definition and content contract", ZhangSongChecks.DefinitionAndContentContract),
    ("Classic Zhang Song Qiangzhi reveals a category and draws on matching uses", ZhangSongChecks.ClassicQiangzhiRevealsThenDrawsOnMatchingCategory),
    ("Boundary Zhang Song Qiangzhi views the hand and chooses the revealed card", ZhangSongChecks.BoundaryQiangzhiViewsHandAndChoosesReveal),
    ("Classic Zhang Song Xiantu gifts two cards and penalizes a killless phase", ZhangSongChecks.ClassicXiantuGiftsTwoAndPenalizesWithoutKill),
    ("Boundary Zhang Song Xiantu chooses the gift amount and pays the damage penalty", ZhangSongChecks.BoundaryXiantuChoosesGiftAmountAndPenalty),
    ("2014 boundary Xu Chu Luoyi partitions zero to three revealed cards and replays", BoundaryXuChuChecks.RevealedCardsPartitionAndReplay),
    ("2014 boundary Xu Chu damage scope duration and natural Slash replay", BoundaryXuChuChecks.DamageScopeAndDuration),
    ("2014 boundary Xu Chu reverse Duel after Slash and replay", BoundaryXuChuChecks.NaturalReverseDuelAndReplay),
    ("SP Le Jin schema-56 content and version boundary", SpLeJinChecks.DefinitionAndSchemaBoundary),
    ("SP Le Jin Xiaoguo pays basic and replays target choice", SpLeJinChecks.HumanOwnerPaysBasicAndReplaysDamage),
    ("SP Le Jin Xiaoguo target pays equipment and AI choice replays", SpLeJinChecks.EquipmentOptionAndAiPaymentReplay),
    ("SP Le Jin multiple observers let human target pay hand equipment", SpLeJinChecks.HumanTargetPaysHandEquipmentAcrossObservers),
    ("SP Le Jin lethal damage clears other-turn observer boundary", SpLeJinChecks.LethalOtherTurnDamageClearsBoundary),
    ("SP Le Jin with no basic card offers no other-turn trigger", SpLeJinChecks.NoBasicCardDoesNotOfferTrigger),
    ("generic other-turn owned-card category choice executes and replays", SpLeJinChecks.GenericOwnedCategoryChoiceUsesPrivateChooserCards),
    ("classic Zhu Zhi schema-55 content and coverage resource contracts", ZhuZhiChecks.DefinitionAndResourceContracts),
    ("classic Zhu Zhi Anguo returns weapon and replays", ZhuZhiChecks.AnguoReturnsWeaponAndReplays),
    ("classic Zhu Zhi Anguo awaits Xiaoji before coverage draw and replay", ZhuZhiChecks.AnguoAwaitsXiaojiBeforeDrawing),
    ("classic Zhu Zhi Anguo measures after nested public range response", ZhuZhiChecks.AnguoMeasuresAfterNestedRangeResponse),
    ("classic Zhu Zhi Anguo compares living coverage for horses armor and short weapon", ZhuZhiChecks.AnguoCountsLivingCoverageRatherThanPrintedRange),
    ("classic Zhu Zhi Anguo ignores printed range decrease without coverage loss", ZhuZhiChecks.AnguoDoesNotDrawWhenRangeFallsButCoverageStays),
    ("generic equipment coverage binding supports discard and conditional recovery", ZhuZhiChecks.GenericCoverageBindingSupportsDiscardAndRecover),
    ("classic Zhu Zhi Anguo cancels after lethal nested movement response", ZhuZhiChecks.AnguoCancelsWhenNestedMovementKillsSubject),
    ("classic Zhu Zhi Anguo preserves Silver Lion and Wooden Ox immediate hooks", ZhuZhiChecks.AnguoPreservesImmediateEquipmentRemovalHooks),
    ("2014 boundary Gan Ning definition and reusable schema 55", BoundaryGanNingChecks.DefinitionAndReusableSchema),
    ("2014 boundary Gan Ning Fenwei assault subset and replay", BoundaryGanNingChecks.MultiTargetAssaultSubsetAndReplay),
    ("2014 boundary Gan Ning Peach Garden and Nullification coexist", BoundaryGanNingChecks.PeachGardenAndNullificationKeepIndependentEffects),
    ("2014 boundary Gan Ning frozen targets survive earlier observer death", BoundaryGanNingChecks.FrozenDesignationSurvivesEarlierObserverDeath),
    ("2014 boundary Gan Ning Five Grains partial nullification and limited use", BoundaryGanNingChecks.FiveGrainsDeclineUseLimitAndPartialEffects),
    ("2014 boundary Gan Ning Iron Chain and single-target eligibility", BoundaryGanNingChecks.IronChainAndSingleTargetBoundary),
    ("2014 boundary Gan Ning Borrowed Sword counts only its weapon owner", BoundaryGanNingChecks.BorrowedSwordCountsOnlyWeaponOwner),
    ("2014 boundary Gan Ning independent Qixi black hand equipment and red rejection", BoundaryGanNingChecks.IndependentQixiUsesBlackHandAndEquipment),
    ("2014 boundary Gan Ning two Fenwei observers compose and replay", BoundaryGanNingChecks.TwoObserversSeeOnlyRemainingEffects),
    ("boundary Cao Cao Duel damage claims physical card and replays", BoundaryCaoCaoIntegrationChecks.DuelDamageClaimsPhysicalCardAndReplays),
    ("boundary Cao Cao Zhangba two physical cards claim all or remaining", BoundaryCaoCaoIntegrationChecks.ZhangbaTwoPhysicalCardsClaimAllOrRemainingOnly),
    ("classic Zhu Huan schema-54 bound kinds and formal content", ZhuHuanChecks.DefinitionAndReusableKindCondition),
    ("classic Zhu Huan Youdi distinguishes all Slash kinds and transfers non-Slash with replay", ZhuHuanChecks.SlashVariantsStopReturnAndNonSlashTransfersWithReplay),
    ("classic Zhu Huan Youdi equipment empty zones and decline", ZhuHuanChecks.EquipmentEmptySourceAndWholeSkillDecline),
    ("bound card kind condition runs for another skill with public AI and replay", ZhuHuanChecks.BoundKindConditionExecutesForAnotherSkillAndAiUsesPublicEstimate),
    ("existing Juzhan transfer AI uses public target facts", ZhuHuanChecks.ExistingJuzhanTransferAiUsesOnlyPublicTargetState),
    ("2014 boundary Cao Cao content and claimable condition schema gates", BoundaryCaoCaoChecks.ContentAndSchemaBoundary),
    ("2014 boundary Cao Cao physical damage chooses draw or claim and replays", BoundaryCaoCaoChecks.PhysicalDamageDrawClaimAndReplay),
    ("2014 boundary Cao Cao cardless three-point Lightning offers draw once", BoundaryCaoCaoChecks.LightningDamageOnlyOffersDrawOnce),
    ("2014 boundary Cao Cao FactionDefense requires lord and uses Wei response", BoundaryCaoCaoChecks.FactionDefenseRequiresLordAndUsesSharedResponse),
    ("2014 boundary Cao Cao actual two-point damage offers one benefit", BoundaryCaoCaoChecks.TwoPointDamageOffersOneChoice),
    ("after-damage preflight defers non-choice bound-card claim condition", BoundaryCaoCaoChecks.AfterDamagePreflightDefersFrameBoundClaimCondition),
    ("2018 boundary Zhang Liao definition and schema 54 boundary", BoundaryZhangLiaoChecks.DefinitionAndSchemaBoundary),
    ("2018 boundary Zhang Liao dynamic draw plan selection and replay", BoundaryZhangLiaoChecks.DrawPlanSelectionAndReplay),
    ("2018 boundary Zhang Liao respects public hand threshold and prior replacement", BoundaryZhangLiaoChecks.HandThresholdAndPriorReplacement),
    ("2018 boundary Zhang Liao cannot Tuxi after Supply Shortage skips Draw", BoundaryZhangLiaoChecks.SupplyShortageSkipsTheDrawWindow),
    ("boundary Sima Yi Feedback takes source cards per damage point", BoundarySimaYiChecks.FeedbackPerPointAndSourceZones),
    ("boundary Sima Yi Guicai replaces another judgment from hand or equipment", BoundarySimaYiChecks.GuicaiHandEquipmentJudgmentAndReplay),
    ("2019 boundary Guo Jia registers independent Tiandu and immediate Yiji", BoundaryGuoJiaChecks.ContentAndRegistration),
    ("2019 boundary Guo Jia Yiji gives zero one or two current hand cards and replays", BoundaryGuoJiaChecks.YijiGivesZeroOneOrTwoCurrentHandCardsAndReplays),
    ("2019 boundary Guo Jia Tiandu claims own judgment and three-point Yiji replays", BoundaryGuoJiaChecks.TianduClaimsOwnJudgmentAndThreePointYijiReplays),
    ("2019 boundary Guo Jia Yiji handles exhausted draw source without phantom cards", BoundaryGuoJiaChecks.YijiHandlesExhaustedDrawSource),
    ("2019 boundary Diao Chan Lijian validates targets costs and uncounterable Duel", BoundaryDiaoChanChecks.DefinitionAndLijianCommands),
    ("boundary Lijian awaits Xiaoji equipment loss before its virtual Duel and replays", BoundaryDiaoChanChecks.LijianEquipmentLossTriggerPrecedesDuel),
    ("2019 boundary Diao Chan Biyue draws by ending hand state and can be declined", BoundaryDiaoChanChecks.BiyueEmptyNonemptyAndDecline),
    ("classic Li Dian private Xunxun replacement and bottom-order replay", LiDianChecks.ClassicDefinitionAndPrivateReplacementReplay),
    ("classic Li Dian Xunxun takes available card from depleted deck", LiDianChecks.XunxunTakesOnlyAvailableCardFromDepletedDeck),
    ("classic Li Dian Wangxi deals damage and replays", LiDianChecks.WangxiDamageCanBeAcceptedOrDeclinedAndReplayed),
    ("classic Li Dian Wangxi two-point damage gives independent choices", LiDianChecks.WangxiTwoPointDamageOffersTwoIndependentChoices),
    ("classic Li Dian Wangxi taken damage gives private replayable choice", LiDianChecks.WangxiTakenDamageOffersPrivateChoice),
    ("rescued lethal damage offers Wangxi only after dying and replays", DamageAfterDyingChecks.RescuedLethalDamageOffersWangxiAfterRescue),
    ("Kuanggu uses lethal damage distance after target death", DamageAfterDyingChecks.KuangguUsesDistanceAtLethalDamage),
    ("phase exchange definitions and current boundary", ProgramActivationLimitChecks.DefinitionsAndCurrentBoundary),
    ("phase exchange mixed zones atomicity and replay", ProgramActivationLimitChecks.MixedZonesAtomicityAndReplay),
    ("phase exchange extra phase and large selection", ProgramActivationLimitChecks.ExtraPhaseAndLargeSelection),
    ("phase exchange equipment loss trigger and replay", ProgramActivationLimitChecks.EquipmentLossTriggerAndReplay),
    ("phase exchange Qingnang heals once in each play phase", ProgramActivationLimitChecks.HealingRenewsOnlyAtNextPhase),
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
    ("composition AI respects previously granted target restrictions", ProgramCompositionAiIntegrationChecks.ZishouSelfOnlyPreventsWastefulJiangchiAssault),
    ("composition kernel cross-entry compilation", ProgramCompositionDefinitionChecks.EquivalentEntriesCompileSameEffects),
    ("composition kernel resource graph safety", ProgramCompositionDefinitionChecks.ResourceGraphsRejectAliasingLeaksAndMissingInputs),
    ("composition kernel malformed nodes", ProgramCompositionDefinitionChecks.MalformedNodesFailBeforeExecution),
    ("composition kernel AI follows resource partitions and costs", ProgramCompositionDefinitionChecks.AiPoliciesFollowResourcePartitionsAndCosts),
    ("composition kernel cross-entry reveal replay", ProgramCompositionEntryChecks.CrossEntryRevealSubsetReplays),
    ("composition kernel cross-entry gift replay", ProgramCompositionEntryChecks.CrossEntryDrawGiftReplays),
    ("composition kernel boundary capabilities", ProgramCompositionContextChecks.SharedWindowsAcceptCommonNodesAndRejectMissingContexts),
    ("composition kernel target-set consumption", ProgramCompositionContextChecks.TargetSetIsConsumedOnce),
    ("composition kernel active state and judgment replay", ProgramCompositionContextChecks.ActiveStateAndJudgmentReplay),
    ("composition kernel public AI context", ProgramCompositionContextChecks.PublicAiContextAccountsForReplacementAndExpressions),
    ("composition kernel active turn policy replay and expiry", ProgramCompositionContextChecks.ActiveTurnRuleModifierGrantsReplaysAndExpires),
    ("execution plans unify active and trigger instruction identities", ProgramExecutionPlanChecks.ActiveAndTriggerUseStableInstructionPlans),
    ("execution plans freeze instructions and reject ambiguous bindings", ProgramExecutionPlanChecks.PlansFreezeInstructionsAndRejectAmbiguousBindings),
    ("rule query reduction is input-order independent", RuleQueryReducerChecks.IsIndependentOfInputOrderAndRejectsOnlyWinningSetConflicts),
    ("rule query reduction sums before one final clamp", RuleQueryReducerChecks.SumsBeforeClampingAndHandlesIntegerExtremes),
    ("rule query reduction keeps unlimited separate", RuleQueryReducerChecks.KeepsUnlimitedSeparateAndStillValidatesFiniteConflicts),
    ("rule query reduction rejects invalid mutable inputs", RuleQueryReducerChecks.RejectsInvalidInputsAndFreezesOutput),
    ("rule query reducer has no concrete skill dependency", RuleQueryReducerChecks.HasNoConcreteSkillDependency),
    ("rule query base terms precede Set and remain inspectable", RuleQueryReducerChecks.BaseTermsPrecedeSetsAndRemainInspectable),
    ("rule query distance runs both directions before clamping", RuleQueryReducerChecks.DirectionalDistanceRunsBothStagesBeforeClamping),
    ("rule query programs deduplicate instances and evaluate dynamic values", RuleQueryReducerChecks.ProgramContributionsUseInstanceIdentityAndDynamicValues),
    ("rule query registry rejects future Set conflicts", RuleQueryReducerChecks.StaticSetConflictsAreRejectedBeforePlay),
    ("rule query schema requires explicit modifier identity and priority", RuleQueryReducerChecks.SchemaTwelveRequiresExplicitModifierIdentityAndPriority),
    ("rule query content preserves formal and legacy version boundaries", RuleQueryIntegrationChecks.FormalContentPreservesVersionBoundaries),
    ("rule query engine tracks dynamic grants and program instances", RuleQueryIntegrationChecks.EngineTracksDynamicSourcesAndInstanceIdentity),
    ("rule query engine consumes formal unlimited slash programs", RuleQueryIntegrationChecks.EngineConsumesFormalUnlimitedSlashProgram),
    ("character skills retain separate sources and stable ownership", CharacterSkillSetChecks.GrantsRetainSourcesAndStableOwnership),
    ("character skills reject conflicting grants without partial state", CharacterSkillSetChecks.InvalidGrantsAndConflictsLeaveStateUnchanged),
    ("character templates supply defaults without owning current state", CharacterSkillSetChecks.CharacterTemplatesSupplyDefaultsWithoutOwningCurrentState),
    ("match skill binding reads reuse shards and partition numeric buckets", MatchSkillBindingIndexChecks.ReadPathsReuseShardsAndPartitionNumericBuckets),
    ("match skill binding sources preserve instance and unique-program semantics", MatchSkillBindingIndexChecks.SourcesDeduplicateInstancesAndUniqueProgramBuckets),
    ("match skill binding Lord templates do not suppress independent grants", MatchSkillBindingIndexChecks.LordTemplateGateDoesNotSuppressIndependentSources),
    ("match skill binding stamps rebuild only the changed seat", MatchSkillBindingIndexChecks.OnlyBindingStampChangesRebuildOneSeatAndOldShardsStayFrozen),
    ("match skill binding indexes remain isolated per match", MatchSkillBindingIndexChecks.MatchIndexesDoNotShareCaches),
    ("skill executor composes primitive handlers with conditions and committed cursors", SkillProgramExecutorChecks.ExecutesComposedEffectsConditionsAndCursorOrder),
    ("skill executor resumes child resolution without repeating paid effects", SkillProgramExecutorChecks.SuspendedChildResumesWithoutRepeatingPaidEffect),
    ("skill executor cancels remaining effects after an invalid selected cost", SkillProgramExecutorChecks.InvalidSelectedCostCancelsRemainingEffectsAtomically),
    ("skill executor rejects changed programs and missing or duplicate handlers", SkillProgramExecutorChecks.RejectsChangedProgramsUnknownHandlersAndDuplicates),
    ("skill executor discovers reusable primitive handlers by reflection", SkillProgramExecutorChecks.ReflectionDiscoversEveryPrimitiveHandler),
    ("active Program contracts reject invalid selections and preserve target order", SkillProgramExecutorChecks.ActiveActivationContractsRejectInvalidDefinitionsAndPreserveOrder),
    ("active Program primitives dispatch through the explicit shared host", SkillProgramExecutorChecks.ActivePrimitiveHandlersUseTheExplicitSharedHost),
    ("card subset selector enumerates every legal subset once", CardSubsetSelectorChecks.EnumeratesEveryLegalSubsetExactlyOnce),
    ("card subset selector keeps impossible and empty constraints explicit", CardSubsetSelectorChecks.ImpossibleAndEmptySelectionDoNotInventChoices),
    ("card subset selector one-per-suit takes exactly one card of each distinct suit", CardSubsetSelectorChecks.OnePerSuitSelectsExactlyOneCardOfEachDistinctSuit),
    ("card subset selector rejects oversized sources and freezes choices", CardSubsetSelectorChecks.RejectsOversizedOrAmbiguousSourcesAndFreezesChoices),
    ("lifecycle programs reject unsupported event usage scope", ProgramLifecycleChecks.RejectsUnsupportedEventUsageScope),
    ("lifecycle programs use typed frozen comparisons and bounded condition windows", ProgramLifecycleChecks.TypedTriggerComparisonsAndWindowBoundaries),
    ("lifecycle programs reject live card bindings across inserted phases", ProgramLifecycleChecks.RejectsCardBindingsAcrossDetachedPhaseBoundary),
    ("lifecycle programs clean unconsumed temporary cards on success", ProgramLifecycleChecks.SuccessfulProgramsCleanupUnconsumedTemporaryCards),
    ("lifecycle programs cancel missing conditional bindings without leaks", ProgramLifecycleChecks.MissingConditionalBindingsCancelWithoutLeakingCards),
    ("cancelled lifecycle programs preserve parent attack cards", ProgramLifecycleChecks.CancelledDamageProgramsDoNotCleanupParentAttackCards),
    ("AI optional lifecycle programs route and resume subset choices", ProgramLifecycleChecks.AiOptionalProgramsUseTheProgramRouterAndResumeSubsetChoices),
    ("same lifecycle skill instance across sources subscribes once", ProgramLifecycleChecks.SameSkillInstanceAcrossSourcesProducesOneCandidate),
    ("card movement schema and classic content preserve their version boundary", CardMovementProgramChecks.DefinitionsAndVersionBoundary),
    ("card movement programs compose atomic per-card and per-batch triggers with replay", CardMovementProgramChecks.AtomicBatchRunsPerCardAndPerBatchAndReplays),
    ("nested card movement batches retain immediate parent identity", CardMovementProgramChecks.NestedBatchesRetainImmediateParentIdentity),
    ("damage programs preserve schema and package version boundaries", DamageProgramChecks.DefinitionsAndVersionBoundaries),
    ("damage programs claim select gift draw and replay through generic choices", DamageProgramChecks.GenericDamageChoicesClaimSelectGiftDrawAndReplay),
    ("draw-phase programs preserve schema and package version boundaries", DrawPhaseProgramChecks.DefinitionsAndVersionBoundaries),
    ("standard Yingzi runs a mandatory extra draw before one normal draw", DrawPhaseProgramChecks.StandardMandatoryExtraDrawRunsBeforeNormalDraw),
    ("classic Yingzi uses a generic optional choice and replays", DrawPhaseProgramChecks.ClassicOptionalChoiceSkipsActivatesAndReplays),
    ("multiple additive draw-phase programs compose before one normal draw", DrawPhaseProgramChecks.MultipleAdditiveProgramsComposeBeforeOneNormalDraw),
    ("draw-phase replacement suppresses additive programs while skip falls through", DrawPhaseProgramChecks.ReplacementSuppressesAdditiveWhileSkipFallsThrough),
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
    ("Lord tags filter generic runtime skill discovery", SkillOwnershipChecks.LordTagFiltersGenericRuntimeDiscovery),
    ("skill program v2 trigger definitions validate and freeze", SkillProgramTriggerDefinitionChecks.Run),
    ("skill program card triggers require exact conversion sources and replay", SkillProgramCardTriggerChecks.Run),
    ("skill program multi-target triggers wait for every Liuli redirection", SkillProgramTargetOrderChecks.Run),
    ("skill program v3 final judgment definitions validate and freeze", SkillProgramJudgmentTriggerChecks.Definitions),
    ("skill program final judgment triggers resume recovery, drawing and replay", SkillProgramJudgmentTriggerChecks.WindowAndReplay),
    ("skill program frozen judgment instance does not transfer to another grant", SkillProgramJudgmentTriggerChecks.FrozenInstanceCannotTransferToAnotherGrant),
    ("skill program v4 judgment replacement definitions validate and freeze", SkillProgramJudgmentReplacementChecks.Definitions),
    ("skill program judgment replacement commits both old-card destinations and replays", SkillProgramJudgmentReplacementChecks.WindowDestinationsAndReplay),
    ("judgment replacement excludes the equipment producing a Bagua judgment", SkillProgramJudgmentReplacementChecks.BaguaSourceEquipmentIsExcluded),
    ("skill program v5 judgment target and damage definitions validate and freeze", SkillProgramJudgmentDamageChecks.Definitions),
    ("skill program judgment damage resumes through dying, death and replay", SkillProgramJudgmentDamageChecks.TargetDamageDyingAndReplay),
    ("skill program v6 direct card judgments validate and freeze", SkillProgramStartedJudgmentChecks.Definitions),
    ("skill program direct Dodge and Lightning judgments resume and replay", SkillProgramStartedJudgmentChecks.DirectDodgeAndLightningReplay),
    ("skill program v7 cross-owner contributions validate and freeze", SkillProgramContributionChecks.Definitions),
    ("skill program contributions bind owner filters, phase ledger and replay", SkillProgramContributionChecks.CrossOwnerFiltersLedgerAndReplay),
    ("skill program v8 selected judgment subjects validate and freeze", SkillProgramSelectedJudgmentChecks.Definitions),
    ("skill program selected judgment subjects damage and replay", SkillProgramSelectedJudgmentChecks.SelectedSubjectDamageAndReplay),
    ("judgment replacement candidates start from the current turn actor", SkillProgramSelectedJudgmentChecks.ReplacementOrderUsesTurnActor),
    ("skill program v10 card identities and action modifiers validate and freeze", SkillProgramCardIdentityChecks.Definitions),
    ("schema-46 viewAs source zones validate and isolate hand from equipment", SkillProgramViewAsZoneChecks.DefinitionAndZoneIsolation),
    ("mandatory card identity suppresses native use, ignores Slash distance and replays", SkillProgramCardIdentityChecks.MandatoryIdentityDistanceAndReplay),
    ("card-use effect categories use effective card kinds", CardUseModuleEffectChecks.DefinitionsValidateAndClassifyEffectiveCards),
    ("turn card-use effects union order consume and expire by action semantics", CardUseModuleEffectChecks.TurnStateUsesActionSemanticsStableOrderAndExpiration),
    ("active programs suspend into Pindian and branch from the frozen result", PindianModuleChecks.ActiveProgramPindianSuspendsConditionsAndReplays),
    ("god generals choose a private effective faction before reveal and replay", GodFactionSelectionChecks.PromptPrivacyEffectiveFactionAndReplay),
    ("chosen god factions feed existing configured lord-skill checks", GodFactionSelectionChecks.EffectiveFactionFeedsConfiguredLordSkill),
    ("formal Wushen treats a real heart Peach as a distance-free counted Slash", ClassicShenGuanYuChecks.WushenRealDeckIdentityAndReplay),
    ("formal Wushen excludes a retained real heart Peach from dying rescue", ClassicShenGuanYuChecks.WushenHeartPeachCannotRescue),
    ("formal Wuhun completes a real-deck direct-death chain and replays", ClassicShenGuanYuChecks.WuhunRealDeckDeathChainAndReplay),
    ("schema-25 awakening state primitives and frozen conditions validate", SpGuanYuChecks.ProgramPrimitivesAndConditionsValidate),
    ("formal SP Guan Yu awakens through a generic mandatory lifecycle binding", SpGuanYuChecks.ContentAndDanjiReplayBoundary),
    ("formal Nuzhan uses the exact SP Wusheng conversion source", SpGuanYuChecks.NuzhanUsesExactConversionSource),
    ("current Yan Yan registers tagged Juzhan and its initial polarity", YanYanChecks.ContentPolarityAndRulesBoundary),
    ("formal Juzhan uses per-target turn and per-card-use event ledgers", YanYanChecks.YangYinLedgerAndReplay),
    ("formal Mou Lu Meng versions Hengye and Yingbo by content package", MouLuMengChecks.ContentAndRulesBoundary),
    ("formal Hengye grows on damage and resets after a kill", MouLuMengChecks.HengyeGrowthAndKillReset),
    ("formal Yingbo uses first and repeated same-name round branches", MouLuMengChecks.YingboRoundLedgerAndReplay),
    ("formal Cao Zhang versions original Jiangchi with a three-branch prompt", CaoZhangChecks.ContentPromptAndRulesBoundary),
    ("Jiangchi extra draw blocks Slash use and play", CaoZhangChecks.DrawMoreBlocksSlashUseAndResponse),
    ("Jiangchi assault adds no-distance and exactly one Slash", CaoZhangChecks.AssaultAddsDistanceAndOneSlash),
    ("formal Ma Dai versions current Qianxi with staged private choices", MaDaiChecks.ContentPromptAndRulesBoundary),
    ("Qianxi red restriction filters same-color hand responses", MaDaiChecks.RedRestrictionFiltersHandResponsesAndReplays),
    ("Qianxi black restriction filters responses and expires", MaDaiChecks.BlackRestrictionFiltersHandResponsesAndExpires),
    ("formal Gao Shun versions Xianzhen and mandatory Jinjiu identity", GaoShunChecks.ContentIdentityAndRulesBoundary),
    ("winning Xianzhen scopes distance count and armor to one target", GaoShunChecks.XianzhenWinTargetsDistanceCountArmorAndReplays),
    ("losing Xianzhen blocks Slash use only", GaoShunChecks.XianzhenLossBlocksSlashOnly),
    ("formal Liu Biao versions Zishou and Zongshi with a private draw choice", LiuBiaoChecks.ContentPromptAndRulesBoundary),
    ("Zishou restricts card targets while Zongshi follows living factions", LiuBiaoChecks.ZishouTargetsAndZongshiHandLimit),
    ("formal Wang Yi versions public Zhenlie and Miji programs", WangYiChecks.ContentPromptAndRulesBoundary),
    ("public Zhenlie nullifies Slash while Miji draws and distributes exactly", WangYiChecks.ZhenlieSlashAndMijiDistributionReplay),
    ("public Zhenlie nullifies only Wang Yi during a group trick", WangYiChecks.ZhenlieNullifiesOnlyItsGroupEffect),
    ("formal Zhong Hui versions Quanji Zili and Paiyi", ZhongHuiChecks.ContentQuanjiAndBoundary),
    ("Quanji awakens Zili and acquired Paiyi replays", ZhongHuiChecks.ZiliAndPaiyiReplay),
    ("schema-26 Zili validates owned-zone conditions", ZhongHuiChecks.ZiliProgramValidationAndCurrentConditions),
    ("formal Xun You versions active Qice and optional Zhiyu", XunYouChecks.ContentAndRulesBoundary),
    ("Qice converts every hand card once and replays", XunYouChecks.QiceUsesAllHandCardsAndReplays),
    ("Zhiyu draws reveals and makes the source discard", XunYouChecks.ZhiyuDrawRevealDiscardAndReplay),
    ("formal Liao Hua versions locked Dangxian and limited Fuli", LiaoHuaChecks.ContentAndRulesBoundary),
    ("Dangxian runs a pre-draw Play phase with fresh phase limits", LiaoHuaChecks.DangxianExtraPhaseResetsPhaseLimitsAndReplays),
    ("Fuli recovers by living factions flips and remains limited", LiaoHuaChecks.FuliRecoversFlipsConsumesAndReplays),
    ("formal Guan Xing and Zhang Bao version continuous active Fuhun", GuanXingZhangBaoChecks.ContentAndRulesBoundary),
    ("Fuhun damage grants Wusheng and Paoxiao for one turn", GuanXingZhangBaoChecks.ActiveSlashGrantsParentSkillsForOneTurnAndReplays),
    ("Fuhun responds with two exact hand cards without granting parent skills", GuanXingZhangBaoChecks.SlashResponseUsesExactPairWithoutGrantAndReplays),
    ("formal Bu Lian Shi versions active Anxu and optional Zhuiyi", BuLianShiChecks.ContentAndRulesBoundary),
    ("Anxu lets the lower-hand receiver choose an opaque card and applies effective suit", BuLianShiChecks.AnxuUsesOpaqueReceiverChoiceAndEffectiveSuit),
    ("Zhuiyi excludes the killer and may benefit a full-health target", BuLianShiChecks.ZhuiyiExcludesKillerAndAllowsFullHealthTarget),
    ("formal Cheng Pu Lihuo is a versioned state rule without publishing an incomplete general", ChengPuLihuoChecks.ContentAndRulesBoundary),
    ("formal Lihuo versions its completed-use penalty program", ChengPuLihuoChecks.CompletionPenaltyProgramContentBoundary),
    ("Lihuo converts Slash adds one target and loses HP once after the use", ChengPuLihuoChecks.ConvertedFireSlashAddsTargetAndLosesHpOnce),
    ("native and Zhuque Fire Slash use Lihuo target extension without conversion penalty", ChengPuLihuoChecks.NativeAndZhuqueFireSlashDoNotPayConversionPenalty),
    ("a fully dodged Lihuo conversion does not lose HP", ChengPuLihuoChecks.FullyDodgedConversionDoesNotLoseHp),
    ("configured Lihuo penalty enters dying and replays", ChengPuLihuoChecks.CompletedPenaltyCanEnterDyingAndReplay),
    ("Wusheng to Lihuo chained conversion preserves both sources", ChengPuLihuoChecks.ChainedWushengLihuoConversionKeepsBothSources),
    ("current Cheng Pu publishes Chunlao storage and rescue triggers", ChengPuLihuoChecks.ChunlaoContentAndRulesBoundary),
    ("Chunlao stores exact Slash cards publicly and replays a paused selection", ChengPuLihuoChecks.ChunlaoStoresExactSlashesAndReplays),
    ("Chunlao spends one public Chun as virtual Alcohol in a dying response", ChengPuLihuoChecks.ChunlaoRescuesWithVirtualAlcoholAndReplays),
    ("Chunlao AI stores one explained reserve instead of its whole Slash hand", ChengPuLihuoChecks.ChunlaoAiStoresOneExplainedReserve),
    ("formal Han Dang migrates Gongqi and limited Jiefan through package 1.136", HanDangChecks.ContentAndPackageBoundary),
    ("Gongqi equipment cost grants unlimited range and uses opaque optional discard", HanDangChecks.GongqiEquipmentCostAndOpaqueDiscardReplay),
    ("Jiefan freezes attackers consumes its limited use and replays", HanDangChecks.JiefanFreezesRespondersConsumesLimitedUseAndReplays),
    ("formal Cao Chong publishes Chengxiang and Renxin behind package 1.94", CaoChongChecks.ContentAndPackageBoundary),
    ("Chengxiang reveals four cards selects a legal subset and replays", CaoChongChecks.ChengxiangRevealsLegalSubsetAndReplays),
    ("Renxin discards equipment turns over prevents damage and replays", CaoChongChecks.RenxinDiscardsEquipmentTurnsOverPreventsAndReplays),
    ("Gu Yong registers original Shenxing Bingyi and reusable target-set capability", GuYongChecks.ContentAndCapability),
    ("Shenxing repeats with hand and equipment and rejects shortfall", GuYongChecks.ShenxingRepeatsWithEquipmentAndRejectsShortfall),
    ("Bingyi reveals then shares to self and multiple targets with replay", GuYongChecks.BingyiRevealsThenSharesAndReplays),
    ("Bingyi mixed or empty hand draws nothing", GuYongChecks.BingyiMixedOrEmptyDrawsNothing),
    ("Bingyi caps target sets uses public AI and rejects stale seats", GuYongChecks.BingyiCapsTargetsAndRejectsStaleChoice),
    ("formal Guo Huai migrates Jingce at package 1.99", GuoHuaiChecks.ContentAndPackageBoundary),
    ("Jingce counts turn card uses draws two cards and replays", GuoHuaiChecks.JingceCountsTurnUsesDrawsAndReplays),
    ("Jingce requires card uses at least current HP", GuoHuaiChecks.JingceRequiresUseCountAtLeastCurrentHp),
    ("Jingce deduplicates grants composes instances and rechecks ownership", GuoHuaiChecks.JingceDeduplicatesSourcesComposesInstancesAndRechecksOwnership),
    ("Jingce uses independent Dangxian extra and normal Play windows", GuoHuaiChecks.JingceUsesIndependentDangxianPlayWindows),
    ("Jingce does not open a second PlayEnding when normal Play is skipped", GuoHuaiChecks.JingceDoesNotOpenForSkippedNormalPlay),
    ("turn-ending content preserves legacy definitions and publishes programs", TurnEndingBoundaryChecks.ContentPreservesLegacyBoundaryAndPublishesPrograms),
    ("turn-ending boundary orders Jushou Jujian Biyue and replays", TurnEndingBoundaryChecks.OrdersJushouJujianBiyueAndReplays),
    ("turn-ending programs deduplicate sources and recheck ownership", TurnEndingBoundaryChecks.DeduplicatesSourcesAndRechecksOwnership),
    ("turn-ending boundary freezes facts across earlier effects", TurnEndingBoundaryChecks.FreezesFactsAcrossEarlierTurnEndingEffects),
    ("formal Man Chong migrates Junxing and Yuce at package 1.118", ManChongChecks.ContentAndPackageBoundary),
    ("Junxing enforces exact card categories and replays both target branches", ManChongChecks.JunxingUsesExactCategoriesAndReplaysBothBranches),
    ("Yuce reveals one card challenges the source recovers and replays", ManChongChecks.YuceRevealsChallengesRecoversAndReplays),
    ("formal Guan Ping publishes optional Longyin behind package 1.97", GuanPingChecks.ContentAndPackageBoundary),
    ("Longyin discards exactly one card uncounts a red Slash draws and replays", GuanPingChecks.RedSlashDrawsAndReplays),
    ("Longyin uncounts a black Slash without drawing and a later skip preserves the limit", GuanPingChecks.BlackSlashUncountsWithoutDrawingAndSkipPreservesLimit),
    ("Longyin privately answers another character's Play-phase Slash and replays", GuanPingChecks.OtherCharactersSlashOffersPrivateChoiceAndReplays),
    ("formal SP Zhao Yun triggers Chongzhen after a configured Longdan Slash", SpZhaoYunChecks.ConvertedSlashUseTriggersChongzhenAndReplays),
    ("formal SP Zhao Yun targets the attacker after a configured Longdan Dodge", SpZhaoYunChecks.ConvertedDodgeResponseTargetsTheAttackerAndReplays),
    ("formal classic Zhang Jiao resolves configured Leiji and Guidao with replay", ClassicZhangJiaoProgramChecks.LeijiGuidaoAndReplay),
    ("formal classic Huangtian transfers one provider card per play phase", ClassicZhangJiaoProgramChecks.HuangtianContributionAndReplay),
    ("formal boundary Zhang Jiao resolves current Leiji and Guidao with replay", BoundaryZhangJiaoProgramChecks.DodgeLeijiGuidaoAndReplay),
    ("formal boundary Huangtian transfers Dodge or Spade cards once per provider phase", BoundaryZhangJiaoProgramChecks.HuangtianContributionAndReplay),
    ("formal national Zhang Jiao package preserves exact skills, mode and reveal boundaries", NationalZhangJiaoProgramChecks.ContentContractAndRevealBoundary),
    ("formal national Zhang Jiao resolves Spade-only Leiji and owner-hand Guidao", NationalZhangJiaoProgramChecks.LeijiGuidaoAndReplay),
    ("public Nightmare markers count each Wuhun damage point before dying and replay", PublicMarkerChecks.WuhunDamageOrderAndReplay),
    ("Wuhun marker candidates keep living positive maximum ties", PublicMarkerChecks.WuhunCandidateRules),
    ("Wuhun death judgment directly kills without a dying window and replays", PublicMarkerChecks.WuhunDeathJudgmentAndReplay),
    ("dead human Wuhun owners privately choose a frozen target and replay", PublicMarkerChecks.WuhunHumanTargetPromptAndReplay),
    ("Wuhun skips its death trigger after the game is already won", PublicMarkerChecks.WuhunSkipsAfterGameEnd),
    ("Guicai can replace Wuhun judgment with a harmless Peach", PublicMarkerChecks.WuhunJudgmentCanBeReplacedWithPeach),
    ("nested Wuhun deaths resolve inner-first and resume the outer stack", PublicMarkerChecks.NestedWuhunDeathsResumeInStackOrder),
    ("composed AI matches complete and replay with configured actions and responses", SkillProgramMatchChecks.ComposedMatchesCompleteAndReplay),
    ("Wusheng responds to Duel and Barbarian Assault with exact physical costs and replay", WushengResponseChecks.CommandsAndReplay),
    ("Wusheng response conversion respects national reveal slots and requested card kinds", WushengResponseChecks.NationalAndScope),
    ("national Wusheng can reveal during a Slash response and refresh privately", WushengResponseChecks.NationalRevealDuringResponse),
    ("AI Wusheng responses complete and replay with physical card costs", WushengResponseChecks.AiResponseAndReplay),
    ("dual-general health previews, setup and legacy replay agree", NationalHealthChecks.SetupAndReplay),
    ("general health validates content and preserves shipped fingerprints", NationalHealthChecks.ContentIntegrity),
    ("national dual-general setup stays private and each reveal replays", NationalWarChecks.PrivateSetupAndReveal),
    ("national revealed slots independently enable skills while rules 6 replay unchanged", NationalWarChecks.SkillGatingAndLegacy),
    ("national reveal enables every skill in a general's collection", NationalWarChecks.MultiSkillRevealAndLegacy),
    ("national AI chooses general reveals from private opportunities", NationalWarChecks.AiRevealPolicy),
    ("six-player national mode preserves solo faction privacy, victory and replay", NationalWarChecks.AmbitiousFactionMode),
    ("national public-evidence checkpoints replay AI knowledge at a paused prompt", NationalWarChecks.PublicEvidenceCheckpointReplay),
    ("national AI matches finish with public faction outcomes and exact replay", NationalWarChecks.CompleteAiMatches),
    ("recast is an independent validated card movement and replays exactly", RecastChecks.CommandAndReplay),
    ("Iron Chain current rules and AI avoid repeated recasts", RecastChecks.CurrentRulesAndAi),
    ("claimed group cards continue after the claimant dies and replay exactly", GroupClaimChecks.ClaimantDeathContinues),
    ("only corresponding claimed group cards may finish after entering a reshuffled draw pile", GroupClaimChecks.OnlyClaimedCardsMayFinishFromTheDrawPile),
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
    ("manual discards preserve the chosen cards and private prompt", ManualDiscardChecks.ExactSelection),
    ("manual discard checkpoints and commands replay exactly", ManualDiscardChecks.Replay),
    ("manual discard handles large subsets and hands below the limit", ManualDiscardChecks.HandSizeBoundaries),
    ("journaled host steps preserve single-step pacing", PacedCommandChecks.SingleStepParity),
    ("paced human commands checkpoint and replay a full game", PacedCommandChecks.FullPacedReplay),
    ("manual discard rounds finish across identities and table sizes", ManualDiscardChecks.MatchMatrix),
    ("implemented card content is registered and described", CardCatalogDefinitions),
    ("standard deck content is deterministic and balanced", StandardDeckContent),
    ("standard package builds an immutable isolated registry", StandardContentRegistryBuilds),
    ("skill program loader separates canonical gameplay from presentation", SkillProgramChecks.LoaderCanonicalizationAndPresentationIsolation),
    ("skill program loader rejects malformed and unsupported definitions", SkillProgramChecks.LoaderRejectsMalformedUnsupportedDefinitions),
    ("loaded skill programs are defensively immutable", SkillProgramChecks.LoadedProgramsAreDefensivelyImmutable),
    ("configured active sequences reject forged input and replay exact moves", SkillProgramChecks.ConfiguredActiveSequenceIsAtomicAndReplayable),
    ("configured modifiers and view-as rules reach real legal actions", SkillProgramChecks.ConfiguredModifiersAndViewAsReachRealLegalActions),
    ("program gameplay hashes control checkpoint compatibility independently of presentation", SkillProgramChecks.ProgramHashControlsCheckpointCompatibility),
    ("program LoseHp resumes exactly once after rescue", SkillProgramChecks.ProgramLoseHpResumesOnceAfterRescue),
    ("program dying checkpoints cancel a selected card spent on rescue", SkillProgramChecks.ProgramPausedDyingAndConsumedSelection),
    ("program GiveSelected anyLiving excludes and rejects its owner", SkillProgramChecks.ProgramAnyLivingGiftExcludesOwner),
    ("physical deck recipes preserve exact suit, rank and content hashing", PhysicalDeckRecipeChecks.ExactSuitRankValidationAndHashing),
    ("classic standard and military decks match their official physical tables", PhysicalDeckRecipeChecks.ClassicPhysicalDecksMatchOfficialTables),
    ("classic identity applies base HP, multiple skills and legacy replay boundaries", ClassicGeneralChecks.SetupHealthAndReplay),
    ("classic Liu Bei repeats formal Rende with cumulative self-recovery and replay", ClassicGeneralChecks.FormalRendeFlow),
    ("current classic Huang Gai repeats configured Kujin and replays", ClassicGeneralChecks.ConfiguredKujinFlow),
    ("configured Huang Gai Kujin suspends at dying and replays", ClassicGeneralChecks.ConfiguredKujinDyingContinuation),
    ("classic Gan Ning converts black hand and equipped cards through formal Qixi", ClassicGeneralChecks.FormalQixiFlow),
    ("classic Lu Meng optionally skips discard through formal Keji", ClassicGeneralChecks.FormalKejiFlow),
    ("classic Zhang Liao replaces drawing through formal Tuxi", ClassicGeneralChecks.FormalTuxiFlow),
    ("current classic Luoyi uses generic draw adjustment and card-damage state", ClassicGeneralChecks.ProgramLuoyiFlow),
    ("classic Dian Wei pays HP or a weapon for formal Qiangxi damage", ClassicGeneralChecks.FormalQiangxiFlow),
    ("classic Xu Huang converts black cards into persistent Supply Shortage", ClassicGeneralChecks.FormalDuanliangFlow),
    ("classic Zhen Ji repeats black Luoshen judgments and replays", ClassicGeneralChecks.FormalLuoshenAndQingguoFlow),
    ("classic Huang Yueying draws through Jizhi and ignores trick distance through Qicai", ClassicGeneralChecks.FormalJizhiAndQicaiFlow),
    ("classic Ma Chao judges through Tieqi and reduces distance through Mashu", ClassicGeneralChecks.FormalTieqiAndMashuFlow),
    ("classic Huang Zhong prohibits Dodge through eligible Liegong", ClassicGeneralChecks.FormalLiegongFlow),
    ("classic Wei Yan recovers through distance-one Kuanggu damage", ClassicGeneralChecks.FormalKuangguFlow),
    ("classic Lu Bu requires sequential Wushuang responses", ClassicGeneralChecks.FormalWushuangFlow),
    ("current classic Zhao Yun uses configured Longdan with historical replay boundary", ClassicGeneralChecks.ConfiguredLongdanFlow),
    ("current classic Zhen Ji uses configured Qingguo responses with historical boundary", ClassicGeneralChecks.ConfiguredQingguoResponses),
    ("current classic Guan Yu uses configured Wusheng from hand and equipment", ClassicGeneralChecks.ConfiguredWushengSources),
    ("current classic Hua Tuo heals with configured Qingnang once per play phase", ClassicGeneralChecks.ConfiguredQingnangHealing),
    ("classic Da Qiao converts diamonds through Guose and redirects Slash through Liuli", ClassicGeneralChecks.FormalGuoseAndLiuliFlow),
    ("classic Diao Chan starts a virtual Duel through Lijian and draws through Biyue", ClassicGeneralChecks.FormalLijianAndBiyueFlow),
    ("classic Sun Shangxiang recovers through Jieyin and draws through Xiaoji", ClassicGeneralChecks.FormalJieyinAndXiaojiFlow),
    ("classic Lu Xun rejects key trick targets and draws through Lianying", ClassicGeneralChecks.FormalQianxunAndLianyingFlow),
    ("classic Borrowed Sword transfers weapons or nests a real Slash and replays", BorrowedSwordChecks.TransferSlashAndReplay),
    ("classic Borrowed Sword lets FactionSlash provide its nested Slash", BorrowedSwordChecks.FactionSlashProvidesForcedSlash),
    ("classic Stone Axe pays an exact two-card cost and resumes Slash damage", StoneAxeChecks.ExactCostDamageAndReplay),
    ("AI Stone Axe decisions use private published choices and replay", StoneAxeChecks.AiUsesPrivatePublishedChoices),
    ("classic Zhangba converts exactly two hand cards into one replayable Slash", ZhangbaChecks.ActiveUseAndReplay),
    ("classic Zhangba publishes exact two-card Slash responses and replays", ZhangbaChecks.SlashResponseAndReplay),
    ("classic Cixiong stages private opposite-gender choices and replays", CixiongDoubleSwordsChecks.StagedChoiceAndReplay),
    ("Cixiong AI chooses relation-aware discard and draw branches", CixiongDoubleSwordsChecks.AiRelationBranches),
    ("classic Qinglong opens a same-target follow-up Slash and replays", QinglongCrescentBladeChecks.SameTargetFollowupAndReplay),
    ("classic Qinglong can request FactionSlash for its same-target follow-up", QinglongCrescentBladeChecks.FactionSlashProviderOpensFollowupSlash),
    ("Qinglong AI uses private exact Slash choices and replays", QinglongCrescentBladeChecks.AiUsesPrivatePublishedChoice),
    ("classic Ice Sword sequentially discards target cards and prevents Slash damage", IceSwordChecks.SequentialDiscardPreventsDamageAndReplays),
    ("Ice Sword AI uses private opaque target-card choices and replays", IceSwordChecks.AiUsesPrivateOpaqueChoices),
    ("classic Qilin Bow discards an exact public mount before Slash damage", QilinBowChecks.ExactMountChoiceAndReplay),
    ("Qilin Bow AI uses public mount choices and replays", QilinBowChecks.AiUsesPublicMountChoices),
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
    ("classic Zhu Rong claims Barbarian Assault and wins cards through Lieren", ZhuRongChecks.JuxiangAndLierenReplay),
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
    ("classic Tiandu can claim a resolved judgment and preserves legacy rules", ClassicGeneralChecks.FormalTianduJudgment),
    ("classic Fanjian transfers a random card after a private suit choice and replays", ClassicGeneralChecks.FormalFanjianFlow),
    ("classic Guanxing privately orders the draw-pile top and preserves legacy rules", ClassicGeneralChecks.FormalGuanxingFlow),
    ("classic FactionDefense privately asks Wei allies for an exact Dodge and replays", ClassicGeneralChecks.FormalFactionDefenseFlow),
    ("classic FactionDefense lets a Wei ally use Bagua and continues after a failed judgment", ClassicGeneralChecks.FormalFactionDefenseBaguaFallback),
    ("classic FactionSlash uses a Shu ally's exact Slash and consumes a failed request", ClassicGeneralChecks.FormalFactionSlashActiveFlow),
    ("classic FactionSlash privately supplies Duel and Barbarian Slash responses and replays", ClassicGeneralChecks.FormalFactionSlashResponseFlow),
    ("classic Jiuyuan doubles another Wu character's dying Peach and preserves legacy rules", ClassicGeneralChecks.FormalJiuyuanRecoveryBonus),
    ("public-team mode preserves privacy and registered distribution", TeamModeChecks.ContentAndPrivacy),
    ("public-team AI matches terminate and replay deterministically", TeamModeChecks.AiMatchIsDeterministic),
    ("public-team winner rules distinguish living teams", TeamModeChecks.WinningTeamRules),
    ("standard active programs use typed selections and deterministic replay", ActiveSkillChecks.KujinFlow),
    ("active-skill dying resumes through the shared rescue window", ActiveSkillChecks.KujinDyingContinuation),
    ("Qingnang discards privately and recovers a legal wounded target", ActiveSkillChecks.QingnangFlow),
    ("Huichun discards privately and recovers multiple legal wounded targets", ActiveSkillChecks.HuichunFlow),
    ("Mashu changes public distance and distance-gated legal actions", DistanceSkillChecks.MashuDistanceAndLegality),
    ("Qicai removes trick distance restrictions through legal actions", DistanceSkillChecks.QicaiRemovesTrickDistance),
    ("content registry rejects duplicate ids and bad references", ContentRegistryValidation),
    ("engine can consume the standard registry through a compatibility projection", EngineConsumesStandardRegistry),
    ("interactive setup exposes private deterministic general choices", InteractiveSetupPipeline),
    ("interactive AI setup consumes the shared pool and terminates", InteractiveAiSetup),
    ("five-player identity mode reuses the shared engine", FivePlayerIdentityMode),
    ("five-player AI matches terminate for fixed seeds", FivePlayerAiSmoke),
    ("standard generals reference registered skills", GeneralContent),
    ("AI uses the card content policy values", AiCardContentPolicy),
    ("tactical AI weighs friendly fire, recovery, alcohol and conversions", TacticalAiChecks.PlayDecisions),
    ("tactical AI protects the Lord and orders rescue choices", TacticalAiChecks.RescueDecisions),
    ("tactical AI compares configured draw replacements with normal drawing", TacticalAiChecks.DrawReplacementActivation),
    ("tactical AI values current draw programs through the common estimator", TacticalAiChecks.DrawPhaseProgramActivation),
    ("tactical AI changes camp inference only after public evidence", TacticalAiChecks.PublicEvidence),
    ("national AI updates hidden-faction hostility only from public attacks", TacticalAiChecks.NationalPublicEvidence),
    ("tactical AI resolves hidden endgames and avoids canceling beneficial effects", TacticalAiChecks.EndgameAndNullification),
    ("Guicai scores formal judgment suits for allies, enemies and legacy rules", TacticalAiChecks.GuicaiJudgments),
    ("AI policy versions validate and replay deterministically", TacticalAiChecks.PolicyReplay),
    ("targeted tricks finish when the target spends its last card on Nullification", TargetLossChecks.LastNullification),
    ("lethal Ganglie closes remaining triggers before publishing game over", TargetLossChecks.LethalGanglie),
    ("hand guidance is private, read-only and agrees with legal actions", HandGuidanceChecks.ReadOnlyAndPrivate),
    ("hand guidance explains spent Slash allowance and active wine", HandGuidanceChecks.AfterUsingCards),
    ("hand guidance follows real response and discard boundaries", HandGuidanceChecks.PendingDecisions),
    ("passive skill hooks stay small and deterministic", PassiveSkills),
    ("formal Kongcheng rejects empty-hand Duel targets atomically", KongchengChecks.DuelTargeting),
    ("damage trigger candidates use a stable ordering", DamageTriggerOrdering),
    ("AI suspicion changes only from public actions", AiPublicEvidence),
    ("same seed creates the same initial state", FixedSeed),
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
    ("DrawTwo resolves an immediate effect without a target", DrawTwoFlow),
    ("Nullification resolves a private multi-layer trick window", NullificationFlow),
    ("IronChain toggles exact targets and propagates elemental damage", IronChainFlow),
    ("BarbarianAssault resolves each target through private Slash windows", BarbarianAssaultFlow),
    ("ArrowBarrage reuses group resolution through private Dodge windows", ArrowBarrageFlow),
    ("PeachGarden resolves a paused multi-target recovery", PeachGardenFlow),
    ("FiveGrains reveals public cards with private draft prompts", FiveGrainsFlow),
    ("Dismantlement discards a hidden target card deterministically", DismantlementFlow),
    ("Snatch transfers a hidden target card across a distance-one edge", SnatchFlow),
    ("target-card prompts expose opaque slots and replay without hidden identities", TargetCardChecks.OpaqueSlotFlow),
    ("AI target-card choices use only redacted slot candidates", TargetCardChecks.AiUsesOpaqueSlots),
    ("Dismantlement and Snatch can target public equipment and judgment cards", PublicTargetCardFlow),
    ("equipment replaces slots and versions formal weapon ranges", EquipmentFlow),
    ("Bagua uses a deterministic public judgment to defend against Slash", BaguaJudgmentFlow),
    ("Bagua can answer ArrowBarrage Dodge windows in formal rules", BaguaDefendsArrowBarrage),
    ("Qinggang bypasses Bagua armor in a typed Slash resolution", QinggangBypassesBagua),
    ("Renwang Shield nullifies black Slash after target confirmation", RenwangShieldFlow),
    ("FireAttack reveals privately then resolves typed fire damage", FireAttackFlow),
    ("FireAttack can skip the same-suit discard without damage", FireAttackSkipFlow),
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
    ("multiple delayed cards accumulate independent turn effects", MultipleDelayedCardsFlow),
    ("Wusheng converts one red card into a typed Slash", WushengFlow),
    ("Longdan converts Dodge into a typed Slash", LongdanFlow),
    ("Longdan converts Slash into Dodge in a response window", LongdanResponseFlow),
    ("dying response can use Alcohol for self rescue", DyingAlcoholRescueFlow),
    ("current Alcohol legality and AI allow only holder self rescue", DyingAlcoholOnlySelfRule),
    ("dying response can pause and recover with a private Peach", DyingResponseFlow),
    ("Jijiu is an opt-in content package with a typed rescue contract", JijiuChecks.ContentContract),
    ("Jijiu converts a red card through the private dying window", JijiuChecks.DyingFlow),
    ("classic Jijiu converts red equipment with versioned payment and replay", JijiuChecks.EquipmentFlow),
    ("throwing observers are isolated after commit", ObserverFailuresAreIsolated),
    ("observer failures do not change deterministic outcomes", ObserverFailuresDoNotChangeOutcome),
    ("uncaught observer reentry cannot interrupt the engine", UncaughtObserverReentryIsIsolated),
    ("human commands reach play and accept a legal card", HumanPlayApi),
    ("human command answers an incoming Slash with Dodge", HumanDodgeApi),
    ("declining lethal Dodge leaves a completed game completed", LethalHumanResponseKeepsCompletedStatus),
    ("single-step command exposes one AI decision at a time", AdvanceOneStepApi),
    ("AI ending play publishes its committed Discard state", AiEndPlayPublishesState),
    ("synchronous observers cannot advance the engine reentrantly", ReentrantAdvanceIsRejected),
    ("an unknown phase fails fast", UnknownPhaseFailsFast),
    ("AI-only match terminates and records explainable thoughts", AiMatchSmoke),
    ("long AI runs finish without leaving an active resolution", StepGuardFinishesResponse),
    ("snapshot is JSON serializable", SnapshotSerialization)
};

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

if (args.FirstOrDefault() == "--only-cao-zhang")
{
    tests = tests.Where(test => test.Name.Contains("Jiangchi", StringComparison.Ordinal)).ToArray();
}

if (args.FirstOrDefault() == "--only-ma-dai")
{
    tests = tests.Where(test => test.Name.Contains("Qianxi", StringComparison.Ordinal)).ToArray();
}

if (args.FirstOrDefault() == "--only-gao-shun")
{
    tests = tests.Where(test =>
        test.Name.Contains("Gao Shun", StringComparison.Ordinal) ||
        test.Name.Contains("Xianzhen", StringComparison.Ordinal)).ToArray();
}

if (args.FirstOrDefault() == "--only-liu-biao")
{
    tests = tests.Where(test =>
        test.Name.Contains("Liu Biao", StringComparison.Ordinal) ||
        test.Name.Contains("Zishou", StringComparison.Ordinal) ||
        test.Name.Contains("Zongshi", StringComparison.Ordinal)).ToArray();
}

if (args.FirstOrDefault() == "--only-wang-yi")
{
    tests = tests.Where(test =>
        test.Name.Contains("Wang Yi", StringComparison.Ordinal) ||
        test.Name.Contains("Zhenlie", StringComparison.Ordinal) ||
        test.Name.Contains("Miji", StringComparison.Ordinal)).ToArray();
}

if (args.FirstOrDefault() == "--only-zhong-hui")
{
    tests = tests.Where(test =>
        test.Name.Contains("Zhong Hui", StringComparison.Ordinal) ||
        test.Name.Contains("Quanji", StringComparison.Ordinal) ||
        test.Name.Contains("Zili", StringComparison.Ordinal) ||
        test.Name.Contains("Paiyi", StringComparison.Ordinal)).ToArray();
}

if (args.FirstOrDefault() == "--only-xun-you")
{
    tests = tests.Where(test =>
        test.Name.Contains("Xun You", StringComparison.Ordinal) ||
        test.Name.Contains("Qice", StringComparison.Ordinal) ||
        test.Name.Contains("Zhiyu", StringComparison.Ordinal)).ToArray();
}

if (args.FirstOrDefault() == "--only-liao-hua")
{
    tests = tests.Where(test =>
        test.Name.Contains("Liao Hua", StringComparison.Ordinal) ||
        test.Name.Contains("Dangxian", StringComparison.Ordinal) ||
        test.Name.Contains("Fuli", StringComparison.Ordinal)).ToArray();
}

if (args.FirstOrDefault() == "--only-guan-xing-zhang-bao")
{
    tests = tests.Where(test =>
        test.Name.Contains("Guan Xing", StringComparison.Ordinal) ||
        test.Name.Contains("Fuhun", StringComparison.Ordinal)).ToArray();
}

if (args.FirstOrDefault() == "--only-bu-lian-shi")
{
    tests = tests.Where(test =>
        test.Name.Contains("Bu Lian Shi", StringComparison.Ordinal) ||
        test.Name.Contains("Anxu", StringComparison.Ordinal) ||
        test.Name.Contains("Zhuiyi", StringComparison.Ordinal)).ToArray();
}

if (args.FirstOrDefault() == "--only-cheng-pu-lihuo")
{
    tests = tests.Where(test => test.Name.Contains("Lihuo", StringComparison.Ordinal)).ToArray();
}

if (args.FirstOrDefault() == "--only-cheng-pu")
{
    tests = tests.Where(test =>
        test.Name.Contains("Cheng Pu", StringComparison.Ordinal) ||
        test.Name.Contains("Lihuo", StringComparison.Ordinal) ||
        test.Name.Contains("Chunlao", StringComparison.Ordinal)).ToArray();
}

if (args.FirstOrDefault() == "--only-han-dang")
{
    tests = tests.Where(test =>
        test.Name.Contains("Han Dang", StringComparison.Ordinal) ||
        test.Name.Contains("Gongqi", StringComparison.Ordinal) ||
        test.Name.Contains("Jiefan", StringComparison.Ordinal)).ToArray();
}

if (args.FirstOrDefault() == "--only-cao-chong")
{
    tests = tests.Where(test =>
        test.Name.Contains("Cao Chong", StringComparison.Ordinal) ||
        test.Name.Contains("Chengxiang", StringComparison.Ordinal) ||
        test.Name.Contains("Renxin", StringComparison.Ordinal)).ToArray();
}

if (args.FirstOrDefault() == "--only-program-lifecycle")
{
    tests = tests.Where(test => test.Name.Contains("lifecycle program", StringComparison.Ordinal) ||
        test.Name.Contains("lifecycle skill instance", StringComparison.Ordinal)).ToArray();
}

if (args.FirstOrDefault() == "--only-draw-phase")
{
    tests = tests.Where(test => test.Name.Contains("draw-phase", StringComparison.OrdinalIgnoreCase) ||
        test.Name.Contains("Yingzi", StringComparison.Ordinal)).ToArray();
}

if (args.FirstOrDefault() == "--only-rule-query")
{
    tests = tests.Where(test => test.Name.Contains("rule query", StringComparison.Ordinal)).ToArray();
}

if (args.FirstOrDefault() == "--only-guo-huai")
{
    tests = tests.Where(test =>
        test.Name.Contains("Guo Huai", StringComparison.Ordinal) ||
        test.Name.Contains("Jingce", StringComparison.Ordinal)).ToArray();
}

if (args.FirstOrDefault() == "--only-turn-ending")
{
    tests = tests.Where(test =>
        test.Name.Contains("turn-ending", StringComparison.OrdinalIgnoreCase) ||
        test.Name.Contains("Jushou", StringComparison.Ordinal) ||
        test.Name.Contains("Biyue", StringComparison.Ordinal)).ToArray();
}

if (args.FirstOrDefault() == "--only-man-chong")
{
    tests = tests.Where(test =>
        test.Name.Contains("Man Chong", StringComparison.Ordinal) ||
        test.Name.Contains("Junxing", StringComparison.Ordinal) ||
        test.Name.Contains("Yuce", StringComparison.Ordinal)).ToArray();
}

if (args.FirstOrDefault() == "--only-guan-ping")
{
    tests = tests.Where(test =>
        test.Name.Contains("Guan Ping", StringComparison.Ordinal) ||
        test.Name.Contains("Longyin", StringComparison.Ordinal)).ToArray();
}

if (args.FirstOrDefault() == "--only-checkpoint-restore")
{
    tests = tests.Where(test =>
        test.Name == "command checkpoints restore a paused private prompt deterministically").ToArray();
}

if (args.FirstOrDefault() == "--only-wuhun")
{
    tests = tests.Where(test =>
        test.Name.Contains("Wuhun", StringComparison.Ordinal) ||
        test.Name.Contains("causeDeath", StringComparison.Ordinal)).ToArray();
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

static void EngineConsumesStandardRegistry()
{
    var registry = StandardContentRegistry.Create();
    var game = GameEngine.CreateStandard(
        new GameOptions
        {
            UseInteractiveDiscard = false,
            Seed = 913,
            HumanSeat = 0,
            HumanRole = Role.Lord
        },
        registry);

    True(ReferenceEquals(registry, game.ContentRegistry));
    Equal(90, game.CreateCardZoneDiagnostics().Count);
    Equal(4, game.State.Players.Single(player => player.Seat == 0).HandCount);
    Equal(CardKind.Slash, game.CreateCardZoneDiagnostics()
        .Single(card => card.CardId == 1).CardKind);
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

static void InteractiveAiSetup()
{
    for (var seed = 1; seed <= 8; seed++)
    {
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
                UseInteractiveDiscard = false,
                Seed = seed,
                PlayerCount = 5,
                HumanSeat = -1,
                HumanRole = null,
                UseInteractiveSetup = true,
                MaxTurns = 250
            },
            StandardContentRegistry.Create());

        var result = game.Submit(new StartGameCommand());
        True(result.Accepted);
        Equal(EngineStatus.Completed, result.Status);
        Equal(5, game.AiGeneralThoughts.Count);
        Equal(
            5,
            result.State.Players
                .Select(player => player.GeneralId)
                .Distinct(StringComparer.Ordinal)
                .Count());
        True(result.State.Players.All(player => player.IsGeneralPublic));
        True(game.Events.Any(eventItem => eventItem.Payload is SetupCompletedEvent));
        AssertCardInventory(game);
    }
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

static void FivePlayerAiSmoke()
{
    for (var seed = 1; seed <= 16; seed++)
    {
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
                UseInteractiveDiscard = false,
                Seed = seed,
                PlayerCount = 5,
                HumanSeat = -1,
                HumanRole = null,
                MaxTurns = 250
            },
            StandardContentRegistry.Create());
        var result = game.Submit(new StartGameCommand());

        True(result.Accepted);
        Equal(EngineStatus.Completed, result.State.Status);
        True(result.Winner != Winner.None);
        AssertCardInventory(game);
    }
}

static void GeneralContent()
{
    var registry = StandardContentRegistry.Create();
    var generals = registry.Generals.Values.ToArray();
    Equal(generals.Length, generals.Select(general => general.Id).Distinct().Count());
    True(generals.All(general => general.SkillIds.Count > 0 &&
        general.SkillIds.All(id => registry.Skills.ContainsKey(id))));
    True(registry.Generals["standard:guan-yu"].SkillIds.Contains("standard:wusheng"));
    True(registry.Generals["standard:zhao-yun"].SkillIds.Contains("standard:longdan"));
    True(registry.Generals["standard:xun-yu"].SkillIds.Contains("standard:jieming"));
    True(registry.Generals["standard:demo-yuanhu"].SkillIds.Contains("standard:yuanhu"));
    True(registry.Generals["standard:demo-ganglie"].SkillIds.Contains("standard:ganglie"));
}

static void AiCardContentPolicy()
{
    var peach = new CardSnapshot(12, CardKind.Peach, Suit.Heart, 12, "桃", "Q");
    var drawTwo = new CardSnapshot(13, CardKind.DrawTwo, Suit.Spade, 13, "无中生有", "K");
    var barbarianAssault = new CardSnapshot(14, CardKind.BarbarianAssault, Suit.Club, 1, "南蛮入侵", "A");
    var arrowBarrage = new CardSnapshot(15, CardKind.ArrowBarrage, Suit.Diamond, 2, "万箭齐发", "2");
    var peachGarden = new CardSnapshot(16, CardKind.PeachGarden, Suit.Heart, 3, "桃园结义", "3");
    var fiveGrains = new CardSnapshot(17, CardKind.FiveGrains, Suit.Spade, 4, "五谷丰登", "4");
    var dismantlement = new CardSnapshot(18, CardKind.Dismantlement, Suit.Club, 5, "过河拆桥", "5");
    var snatch = new CardSnapshot(19, CardKind.Snatch, Suit.Diamond, 6, "顺手牵羊", "6");
    var fireSlash = new CardSnapshot(20, CardKind.FireSlash, Suit.Heart, 7, "火杀", "7");
    var thunderSlash = new CardSnapshot(21, CardKind.ThunderSlash, Suit.Spade, 8, "雷杀", "8");
    var alcohol = new CardSnapshot(22, CardKind.Alcohol, Suit.Club, 9, "酒", "9");
    var fireAttack = new CardSnapshot(23, CardKind.FireAttack, Suit.Diamond, 10, "火攻", "10");
    var ironChain = new CardSnapshot(24, CardKind.IronChain, Suit.Club, 11, "铁索连环", "11");
    var publicJudgment = new CardSnapshot(25, CardKind.Indulgence, Suit.Heart, 5, "乐不思蜀", "5");
    var self = new PlayerSnapshot(
        Seat: 0,
        Name: "AI",
        IsHuman: false,
        Role: Role.Lord,
        IsRoleRevealed: true,
        GeneralId: "liu-bei",
        GeneralName: "刘备",
        PortraitKey: "liu_bei",
        Hp: 2,
        MaxHp: 4,
        IsAlive: true,
        HandCount: 13,
        Hand: [peach, drawTwo, barbarianAssault, arrowBarrage, peachGarden, fiveGrains, dismantlement, snatch, fireSlash, thunderSlash, alcohol, fireAttack, ironChain])
    {
        Skills = []
    };
    var target = self with
    {
        Seat = 1,
        Name = "Target",
        IsHuman = false,
        Role = null,
        IsRoleRevealed = false,
        GeneralId = "",
        GeneralName = "目标",
        PortraitKey = "",
        Skills = null,
        HandCount = 4,
        Hand = []
    };
    var view = new GameSnapshot(
        Seed: null,
        HumanSeat: -1,
        Status: EngineStatus.Running,
        Winner: Winner.None,
        TurnNumber: 1,
        CurrentSeat: 0,
        Phase: TurnPhase.Play,
        DrawPileCount: 20,
        DiscardPileCount: 0,
        Players: [self, target],
        PendingDecision: null);
    var actions = new LegalAction[]
    {
        new(LegalActionKind.Peach, peach.Id, self.Seat, "对自己使用【桃】"),
        new(LegalActionKind.DrawTwo, drawTwo.Id, null, "使用【无中生有】摸两张牌"),
        new(LegalActionKind.BarbarianAssault, barbarianAssault.Id, null, "使用【南蛮入侵】"),
        new(LegalActionKind.ArrowBarrage, arrowBarrage.Id, null, "使用【万箭齐发】"),
        new(LegalActionKind.PeachGarden, peachGarden.Id, null, "使用【桃园结义】"),
        new(LegalActionKind.FiveGrains, fiveGrains.Id, null, "使用【五谷丰登】"),
        new(LegalActionKind.Dismantlement, dismantlement.Id, 1, "对 目标使用【过河拆桥】"),
        new(LegalActionKind.Snatch, snatch.Id, 1, "对 目标使用【顺手牵羊】"),
        new(LegalActionKind.FireAttack, fireAttack.Id, 1, "对 目标使用【火攻】"),
        new(LegalActionKind.Slash, fireSlash.Id, 1, "对 目标使用【火杀】"),
        new(LegalActionKind.Slash, thunderSlash.Id, 1, "对 目标使用【雷杀】"),
        new(LegalActionKind.Alcohol, alcohol.Id, null, "使用【酒】，本回合下一张杀伤害+1"),
        new(LegalActionKind.IronChain, ironChain.Id, 1, "对 目标使用【铁索连环】"),
        new(LegalActionKind.EndPlay, null, null, "结束出牌")
    };

    var (action, thought) = new SimpleAiBrain(seat: 0, seed: 5)
        .ChoosePlay(view, actions, thoughtSequence: 1);

    Equal(LegalActionKind.Peach, action.Kind);
    True(thought.Candidates.Single(candidate => candidate.Action.Kind == LegalActionKind.Peach).Score > 0d);
    True(thought.Candidates.Single(candidate => candidate.Action.Kind == LegalActionKind.DrawTwo).Score > 0d);
    True(thought.Candidates.Single(candidate => candidate.Action.Kind == LegalActionKind.BarbarianAssault).Score > 0d);
    True(thought.Candidates.Single(candidate => candidate.Action.Kind == LegalActionKind.ArrowBarrage).Score > 0d);
    True(thought.Candidates.Single(candidate => candidate.Action.Kind == LegalActionKind.PeachGarden).Score > 0d);
    True(thought.Candidates.Single(candidate => candidate.Action.Kind == LegalActionKind.FiveGrains).Score > 0d);
    var dismantlementCandidate = thought.Candidates.Single(candidate =>
        candidate.Action.Kind == LegalActionKind.Dismantlement);
    True(dismantlementCandidate.Score > 0d);
    True(dismantlementCandidate.Reason.Contains("不读取目标暗牌", StringComparison.Ordinal));
    var snatchCandidate = thought.Candidates.Single(candidate =>
        candidate.Action.Kind == LegalActionKind.Snatch);
    True(snatchCandidate.Score > 0d);
    True(snatchCandidate.Reason.Contains("距离 1", StringComparison.Ordinal));
    True(snatchCandidate.Reason.Contains("不读取目标暗牌", StringComparison.Ordinal));
    var fireAttackCandidate = thought.Candidates.Single(candidate =>
        candidate.Action.Kind == LegalActionKind.FireAttack);
    True(fireAttackCandidate.Score > 0d);
    True(fireAttackCandidate.Reason.Contains("不读取双方暗牌", StringComparison.Ordinal));
    True(thought.Candidates.Single(candidate => candidate.Action.CardId == fireSlash.Id).Score > 0d);
    True(thought.Candidates.Single(candidate => candidate.Action.CardId == thunderSlash.Id).Score > 0d);
    var alcoholCandidate = thought.Candidates.Single(candidate => candidate.Action.CardId == alcohol.Id);
    True(alcoholCandidate.Score > 0d);
    True(alcoholCandidate.Reason.Contains("+1 伤害", StringComparison.Ordinal));
    var ironChainCandidate = thought.Candidates.Single(candidate => candidate.Action.CardId == ironChain.Id);
    True(ironChainCandidate.Score > 0d);
    True(ironChainCandidate.Reason.Contains("公开连环角色", StringComparison.Ordinal));

    var judgmentView = view with
    {
        Players = [self, target with { Judgment = [publicJudgment] }]
    };
    var judgmentActions = new LegalAction[]
    {
        new(
            LegalActionKind.Dismantlement,
            dismantlement.Id,
            target.Seat,
            "对 目标使用【过河拆桥】，选择其判定区【乐不思蜀】",
            TargetCardId: publicJudgment.Id),
        new(
            LegalActionKind.Snatch,
            snatch.Id,
            target.Seat,
            "对 目标使用【顺手牵羊】，选择其判定区【乐不思蜀】",
            TargetCardId: publicJudgment.Id),
        new(LegalActionKind.EndPlay, null, null, "结束出牌")
    };
    var (_, judgmentThought) = new SimpleAiBrain(seat: 0, seed: 5)
        .ChoosePlay(judgmentView, judgmentActions, thoughtSequence: 6);
    var judgmentDismantlementCandidate = judgmentThought.Candidates.Single(candidate =>
        candidate.Action.Kind == LegalActionKind.Dismantlement);
    True(judgmentDismantlementCandidate.Score > 0d);
    True(judgmentDismantlementCandidate.Reason.Contains("公开判定区【乐不思蜀】", StringComparison.Ordinal));
    var judgmentSnatchCandidate = judgmentThought.Candidates.Single(candidate =>
        candidate.Action.Kind == LegalActionKind.Snatch);
    True(judgmentSnatchCandidate.Score > 0d);
    True(judgmentSnatchCandidate.Reason.Contains("公开判定区【乐不思蜀】", StringComparison.Ordinal));

    var alcoholCard = new Card(alcohol.Id, alcohol.Kind, alcohol.Suit, alcohol.Rank);
    var dyingAlcohol = new SimpleAiBrain(seat: 0, seed: 5).ChooseDyingResponseWithAlcohol(
        view,
        victimSeat: 0,
        peaches: [],
        alcohols: [alcoholCard],
        thoughtSequence: 2);
    True(dyingAlcohol.UseAlcohol);
    Equal(alcohol.Id, dyingAlcohol.AlcoholCardId);
    True(dyingAlcohol.Thought.Candidates.Any(candidate =>
        candidate.Action.Kind == LegalActionKind.Alcohol &&
        candidate.Reason.Contains("使自己回到", StringComparison.Ordinal)));

    var nonVictimAlcohol = new SimpleAiBrain(seat: 0, seed: 5).ChooseDyingResponseWithAlcohol(
        view,
        victimSeat: 1,
        peaches: [],
        alcohols: [alcoholCard],
        thoughtSequence: 3,
        allowCrossSeatAlcoholRescue: true);
    False(nonVictimAlcohol.UseAlcohol);
    var nonVictimAlcoholCandidate = nonVictimAlcohol.Thought.Candidates.Single(candidate =>
        candidate.Action.Kind == LegalActionKind.Alcohol);
    Equal(1, nonVictimAlcoholCandidate.Action.TargetSeat);
    True(nonVictimAlcoholCandidate.Reason.Contains("公开濒死角色", StringComparison.Ordinal));
    var formalNonVictimAlcohol = new SimpleAiBrain(seat: 0, seed: 5).ChooseDyingResponseWithAlcohol(
        view,
        victimSeat: 1,
        peaches: [],
        alcohols: [alcoholCard],
        thoughtSequence: 4,
        allowCrossSeatAlcoholRescue: false);
    False(formalNonVictimAlcohol.UseAlcohol);
    True(formalNonVictimAlcohol.Thought.Candidates.All(candidate =>
        candidate.Action.Kind != LegalActionKind.Alcohol));

    var harvest = new SimpleAiBrain(seat: 0, seed: 5).ChooseHarvestCard(
        view,
        [
            new CardSnapshot(21, CardKind.Slash, Suit.Spade, 1, "杀", "A"),
            new CardSnapshot(22, CardKind.Peach, Suit.Heart, 2, "桃", "2")
        ],
        thoughtSequence: 2);
    Equal(22, harvest.CardId);
    True(harvest.Thought.Candidates.Count == 2);

    var redVirtualSlash = new CardSnapshot(23, CardKind.Peach, Suit.Heart, 10, "桃", "10");
    var wushengSelf = self with
    {
        Skills = [new GeneralSkillDefinition("武圣", "红色牌可当作杀使用。")],
        HandCount = 1,
        Hand = [redVirtualSlash]
    };
    var wushengView = view with { Players = [wushengSelf, target] };
    var (wushengAction, wushengThought) = new SimpleAiBrain(seat: 0, seed: 5).ChoosePlay(
        wushengView,
        [
            new LegalAction(
                LegalActionKind.Slash,
                redVirtualSlash.Id,
                target.Seat,
                "将【桃】当作【杀】使用",
                PlayedCardKind: CardKind.Slash),
            new LegalAction(LegalActionKind.EndPlay, null, null, "结束出牌")
        ],
        thoughtSequence: 6);
    Equal(LegalActionKind.Slash, wushengAction.Kind);
    Equal(redVirtualSlash.Id, wushengAction.CardId);
    True(wushengThought.Candidates.Single(candidate =>
        candidate.Action.CardId == redVirtualSlash.Id).Reason.Contains("当作杀", StringComparison.Ordinal));

}

static void PassiveSkills()
{
    var currentContent = StandardContentRegistry.CreateWithActiveSkills();
    True(currentContent.Skills["standard:paoxiao"].Program!.Modifiers.Single().Operation ==
         SkillRuleOperation.Unlimited);
    var damageContent = StandardContentRegistry.CreateWithClassicGenerals();
    var yuanhu = damageContent.Skills["standard:yuanhu"].Program!.Triggers.Single();
    True(yuanhu.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
         yuanhu.Subject == SkillProgramTriggerSubject.Any && yuanhu.Optional &&
         yuanhu.Effects.Any(effect => effect.Op == SkillProgramEffectOp.Recover));
    var ganglie = damageContent.Skills["standard:ganglie"].Program!.Triggers.Single();
    True(ganglie.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
         ganglie.Subject == SkillProgramTriggerSubject.Owner && ganglie.Optional &&
         ganglie.Effects.Any(effect => effect.Op == SkillProgramEffectOp.StartJudgment));

    var wusheng = currentContent.Skills["standard:wusheng"].Program!.ViewAs.Single();
    True(wusheng.OutputKind == CardKind.Slash &&
         wusheng.InputSuits.SequenceEqual([Suit.Heart, Suit.Diamond]));
    var longdan = currentContent.Skills["standard:longdan"].Program!.ViewAs;
    True(longdan.Any(rule => rule.InputKinds.SequenceEqual([CardKind.Dodge]) &&
                             rule.OutputKind == CardKind.Slash));
    True(longdan.Any(rule => rule.OutputKind == CardKind.Dodge &&
                             rule.InputKinds.Contains(CardKind.Slash)));
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

static void FixedSeed()
{
    var left = GameEngine.CreateStandard(new GameOptions { UseInteractiveDiscard = false, Seed = 99 }, StandardContentRegistry.Create()).SerializeState(revealAll: true);
    var right = GameEngine.CreateStandard(new GameOptions { UseInteractiveDiscard = false, Seed = 99 }, StandardContentRegistry.Create()).SerializeState(revealAll: true);
    Equal(left, right);
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

static void DrawTwoFlow()
{
    GameEngine? selectedGame = null;
    LegalAction? selectedAction = null;
    for (var seed = 1; seed <= 512 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            UseInteractiveDiscard = false,
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord
        }, StandardContentRegistry.Create());
        var result = game.DriveStart();
        var human = game.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == 0);
        var action = game.GetHumanLegalActions()
            .FirstOrDefault(candidate => candidate.Kind == LegalActionKind.DrawTwo);
        if (result.Status == EngineStatus.AwaitingHumanPlay &&
            human.Skills?.Any(skill => skill.ContentId == "standard:wusheng") != true &&
            action is not null &&
            game.CreateSnapshot(0, revealAll: true).Players.All(player =>
                player.Hand.All(card => card.Kind != CardKind.Nullification)))
        {
            selectedGame = game;
            selectedAction = action;
        }
    }

    if (selectedGame is null || selectedAction is null)
    {
        throw new InvalidOperationException("No deterministic DrawTwo opening hand was found.");
    }

    var gameWithDrawTwo = selectedGame!;
    var drawTwoAction = selectedAction!;
    var prompt = gameWithDrawTwo.PendingDecision!;
    var drawTwoChoice = prompt.Choices.Single(choice =>
        choice.Cards.Count == 1 &&
        choice.Cards[0] == drawTwoAction.CardId &&
        choice.Parameters.GetValueOrDefault("action") == "draw-two");
    Equal("draw-two", drawTwoChoice.Parameters["action"]);
    Equal(0, drawTwoChoice.Targets.Count);

    var drawTwoCardId = drawTwoAction.CardId!.Value;
    var before = gameWithDrawTwo.State;
    var drawMovementCountBefore = gameWithDrawTwo.CardMovements.Count(movement =>
        movement.From == CardLocation.DrawPile &&
        movement.To == CardLocation.Hand(0) &&
        movement.Reason == CardMoveReasons.Draw);
    var beforeSerialized = gameWithDrawTwo.SerializeState();
    var invalidTarget = gameWithDrawTwo.Submit(new PlayCardCommand(
        ActorSeat: 0,
        CardId: drawTwoCardId,
        TargetSeats: [1],
        ExpectedRevision: gameWithDrawTwo.Revision,
        PromptId: prompt.PromptId));
    False(invalidTarget.Accepted);
    Equal(CommandErrorCode.InvalidTarget, invalidTarget.Error!.Code);
    Equal(beforeSerialized, gameWithDrawTwo.SerializeState());

    var accepted = gameWithDrawTwo.Submit(new PlayCardCommand(
        ActorSeat: 0,
        CardId: drawTwoCardId,
        TargetSeats: [],
        ExpectedRevision: gameWithDrawTwo.Revision,
        PromptId: prompt.PromptId));
    True(accepted.Accepted);
    Equal(EngineStatus.AwaitingHumanPlay, accepted.Status);
    Equal(before.Players.Single(player => player.Seat == 0).HandCount + 1,
        accepted.State.Players.Single(player => player.Seat == 0).HandCount);
    Equal(before.DrawPileCount - 2, accepted.State.DrawPileCount);
    Equal(before.DiscardPileCount + 1, accepted.State.DiscardPileCount);
    Equal(0, accepted.State.ProcessingCardCount);
    Equal(0, gameWithDrawTwo.ResolutionStack.Count);

    var declared = gameWithDrawTwo.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<CardUseDeclaredEvent>()
        .Single(eventItem => eventItem.CardId == drawTwoCardId);
    Equal(CardKind.DrawTwo, declared.CardKind);
    var targets = gameWithDrawTwo.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<TargetsConfirmedEvent>()
        .Single(eventItem => eventItem.ResolutionId == declared.ResolutionId);
    Equal(0, targets.TargetSeats.Count);
    TrueWithMessage(gameWithDrawTwo.Events.Any(eventItem =>
        eventItem.Payload is CardUseFinishedEvent finished &&
        finished.ResolutionId == declared.ResolutionId &&
        finished.CardId == drawTwoCardId &&
        finished.CardKind == CardKind.DrawTwo), "DrawTwo finished event");

    var drawMovements = gameWithDrawTwo.CardMovements
        .Where(movement =>
            movement.From == CardLocation.DrawPile &&
            movement.To == CardLocation.Hand(0) &&
            movement.Reason == CardMoveReasons.Draw)
        .ToArray();
    Equal(2, drawMovements.Length - drawMovementCountBefore);
    TrueWithMessage(gameWithDrawTwo.CardMovements.Any(movement =>
        movement.CardId == drawTwoCardId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Use), "DrawTwo enters Processing");
    TrueWithMessage(gameWithDrawTwo.CardMovements.Any(movement =>
        movement.CardId == drawTwoCardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.UseFinished), "DrawTwo leaves Processing");
    TrueWithMessage(gameWithDrawTwo.Log.Any(entry =>
        entry.Type == "CardEffect" && entry.Message.Contains("无中生有", StringComparison.Ordinal)),
        "DrawTwo effect log");

    var otherViewer = gameWithDrawTwo.CreateSnapshot(1);
    Equal(0, otherViewer.Players.Single(player => player.Seat == 0).Hand.Count);
    AssertCardInventory(gameWithDrawTwo);
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

static void ArrowBarrageFlow()
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
            .FirstOrDefault(candidate => candidate.Kind == LegalActionKind.ArrowBarrage);
        if (result.Status != EngineStatus.AwaitingHumanPlay || action is null)
        {
            continue;
        }

        if (!game.CreateSnapshot(0, revealAll: true).Players.All(player =>
                player.Hand.All(card => card.Kind != CardKind.Nullification)))
        {
            continue;
        }

        result = game.DriveHumanPlay(action.CardId!.Value, null, advanceToHumanBoundary: false);
        result = ResolveNullificationWindowForTest(game, result);
        var currentFrame = game.ResolutionStack.OfType<CardUseFrame>()
            .SingleOrDefault(frame => frame.CardKind == CardKind.ArrowBarrage);
        var responseFrame = game.ResolutionStack.OfType<ResponseWindowFrame>()
            .SingleOrDefault(frame => frame.IncomingCard == CardKind.ArrowBarrage);
        var hasGuaranteedDamageTarget = currentFrame is not null && currentFrame.TargetSeats.Any(targetSeat =>
        {
            var target = game.CreateSnapshot(targetSeat).Players.Single(player => player.Seat == targetSeat);
            return target.Skills?.Any(skill => skill.ViewAsOpportunities?.Any(opportunity =>
                       opportunity.OutputKind == CardKind.Dodge) == true) != true &&
                   !target.Equipment.Any(card => card.Kind == CardKind.BaguaFormation) &&
                   !target.Hand.Any(card => card.Kind == CardKind.Dodge);
        });
        var hasNoFeedbackTarget = currentFrame is not null && currentFrame.TargetSeats.All(targetSeat =>
            game.CreateSnapshot(targetSeat, revealAll: true).Players
                .Single(player => player.Seat == targetSeat).Skills?
                    .Any(skill => skill.ContentId == "standard:feedback") != true);
        if (currentFrame is not null && responseFrame is not null &&
            hasGuaranteedDamageTarget && hasNoFeedbackTarget)
        {
            selectedGame = game;
            selectedAction = action;
            groupFrame = currentFrame;
        }
    }

    if (selectedGame is null || selectedAction is null || groupFrame is null)
    {
        throw new InvalidOperationException("No deterministic ArrowBarrage response window was found.");
    }

    var gameWithBarrage = selectedGame!;
    var frame = groupFrame!;
    Equal(7, frame.TargetSeats.Count);
    True(frame.TargetIndex >= 0 && frame.TargetIndex < frame.TargetSeats.Count);
    Equal(1, gameWithBarrage.State.ProcessingCardCount);
    Equal(
        CardLocation.Processing,
        gameWithBarrage.CreateCardZoneDiagnostics()
            .Single(card => card.CardId == frame.CardId)
            .Location);

    var firstTarget = frame.TargetSeats[frame.TargetIndex];
    var privateTargetView = gameWithBarrage.CreateSnapshot(firstTarget);
    Equal(DecisionKind.RespondDodge, privateTargetView.PendingDecision!.Kind);
    Equal(CardKind.ArrowBarrage, privateTargetView.PendingDecision.IncomingCard);
    Equal(CardKind.Dodge, privateTargetView.PendingDecision.RequiredCardKind);
    True(privateTargetView.PendingDecision.ValidCardIds.Count > 0);
    Equal<PendingDecision?>(null, gameWithBarrage.State.PendingDecision);
    var otherViewerSeat = Enumerable.Range(0, gameWithBarrage.PlayerCount)
        .First(seat => seat != firstTarget);
    Equal<PendingDecision?>(null, gameWithBarrage.CreateSnapshot(otherViewerSeat).PendingDecision);

    True(gameWithBarrage.Events.Any(eventItem =>
        eventItem.Payload is GroupCardUsedEvent used &&
        used.ResolutionId == frame.Id &&
        used.CardKind == CardKind.ArrowBarrage &&
        used.TargetSeats.SequenceEqual(frame.TargetSeats)));
    TrueWithMessage(
        gameWithBarrage.ResolutionStack[^1] is ResponseWindowFrame response &&
        response.IncomingCard == CardKind.ArrowBarrage &&
        response.RequiredCardKind == CardKind.Dodge,
        "ArrowBarrage Dodge response frame");

    var steps = 0;
    while (!gameWithBarrage.Events.Any(eventItem =>
               eventItem.Payload is CardUseFinishedEvent finished &&
               finished.ResolutionId == frame.Id) &&
           steps++ < 500)
    {
        var advanced = gameWithBarrage.DriveAdvanceOneStep();
        if (advanced.Status is EngineStatus.AwaitingHumanResponse or
            EngineStatus.AwaitingHumanDying or
            EngineStatus.AwaitingHumanCardSelection or
            EngineStatus.AwaitingHumanPlay)
        {
            _ = ResolveDelayedCardHumanBoundary(gameWithBarrage, advanced);
        }
    }

    TrueWithMessage(
        gameWithBarrage.Events.Any(eventItem =>
            eventItem.Payload is CardUseFinishedEvent finished &&
            finished.ResolutionId == frame.Id &&
            finished.CardKind == CardKind.ArrowBarrage),
        "ArrowBarrage finished event");
    var responses = gameWithBarrage.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<GroupResponseEvent>()
        .Where(response => response.ResolutionId == frame.Id)
        .ToArray();
    Equal(frame.TargetSeats.Count, responses.Length);
    True(responses.Select(response => response.ResponderSeat)
        .SequenceEqual(frame.TargetSeats));
    True(responses.All(response =>
        response.IncomingCard == CardKind.ArrowBarrage &&
        response.RequiredCardKind == CardKind.Dodge));
    TrueWithMessage(
        gameWithBarrage.Events.Any(eventItem =>
            eventItem.Payload is DamageRequestedEvent damage &&
            damage.SourceCard == CardKind.ArrowBarrage),
        $"ArrowBarrage damage events: {string.Join(" | ", gameWithBarrage.Events
            .Select(eventItem => eventItem.Payload)
            .OfType<DamageRequestedEvent>()
            .Select(damage => $"{damage.SourceCard}:{damage.SourceSeat}->{damage.TargetSeat}"))}; " +
        $"responses: {string.Join(',', gameWithBarrage.Events
            .Select(eventItem => eventItem.Payload)
            .OfType<GroupResponseEvent>()
            .Select(response => $"{response.ResponderSeat}:{response.UsedResponse}"))}");
    TrueWithMessage(
        gameWithBarrage.ResolutionStack.Count == 0,
        $"ArrowBarrage resolution stack should be empty, got {gameWithBarrage.ResolutionStack.Count}: {string.Join(", ", gameWithBarrage.ResolutionStack.Select(frame => frame.Kind))}");
    Equal(
        CardLocation.DiscardPile,
        gameWithBarrage.CreateCardZoneDiagnostics()
            .Single(card => card.CardId == frame.CardId)
            .Location);
    AssertCardInventory(gameWithBarrage);
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

static void BaguaDefendsArrowBarrage()
{
    static (GameEngine Game, PendingDecision Prompt)? ReachBoundary(int seed, int rulesVersion)
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
        if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
        {
            game = GameReplay.Restore(
                game.CreateCheckpoint() with { RulesVersion = rulesVersion }, StandardContentRegistry.Create());
        }

        var result = game.DriveStart();
        var bagua = game.CreateSnapshot(0, revealAll: true).Players
            .Single(player => player.Seat == 0).Hand
            .FirstOrDefault(card => card.Kind == CardKind.BaguaFormation);
        if (result.Status != EngineStatus.AwaitingHumanPlay || bagua is null)
        {
            return null;
        }

        result = game.DriveHumanPlay(bagua.Id, targetSeat: null, advanceToHumanBoundary: true);
        result = game.DriveHumanEndPlay(advanceToHumanBoundary: false);
        for (var step = 0; result.Status != EngineStatus.Completed && step < 1_500; step++)
        {
            if (result.Status == EngineStatus.AwaitingHumanResponse)
            {
                var prompt = game.PendingDecision ??
                    throw new InvalidOperationException("The response status has no prompt.");
                if (prompt.Kind == DecisionKind.RespondDodge &&
                    prompt.IncomingCard == CardKind.ArrowBarrage &&
                    prompt.ValidCardIds.Count > 0)
                {
                    return (game, prompt);
                }

                result = prompt.Kind switch
                {
                    DecisionKind.ProgramTrigger => ResolveIncidentalProgramTrigger(game),
                    DecisionKind.RespondSlash => game.DriveHumanRespondSlash(
                        useSlash: false,
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

        return null;
    }

    (GameEngine Game, PendingDecision Prompt)? formal = null;
    var selectedSeed = 0;
    for (var seed = 1; seed <= 8_192 && formal is null; seed++)
    {
        var candidate = ReachBoundary(seed, GameCheckpoint.CurrentRulesVersion);
        if (candidate?.Prompt.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("response") == "bagua") == true)
        {
            formal = candidate;
            selectedSeed = seed;
        }
    }

    if (formal is null)
    {
        throw new InvalidOperationException(
            "No deterministic ArrowBarrage Bagua response boundary was found.");
    }

    var game = formal.Value.Game;
    var prompt = formal.Value.Prompt;
    var baguaChoice = prompt.Choices.Single(choice =>
        choice.Parameters.GetValueOrDefault("response") == "bagua");
    var eventCount = game.Events.Count;
    var accepted = game.Submit(new AnswerPromptCommand(
        ActorSeat: 0,
        Prompt: prompt.PromptId,
        Choice: baguaChoice.Id,
        ExpectedRevision: game.Revision));
    TrueWithMessage(accepted.Accepted, "ArrowBarrage Bagua choice accepted");

    var judgment = game.Events
        .Skip(eventCount)
        .Select(eventItem => eventItem.Payload)
        .OfType<JudgmentResolvedEvent>()
        .Single(candidate => candidate.Reason == JudgmentReasons.BaguaDefense);
    var requested = game.Events
        .Skip(eventCount)
        .Select(eventItem => eventItem.Payload)
        .OfType<JudgmentRequestedEvent>()
        .Single(candidate => candidate.ResolutionId == judgment.ResolutionId);
    Equal(CardKind.ArrowBarrage, requested.SourceCard);
    var groupResponse = game.Events
        .Skip(eventCount)
        .Select(eventItem => eventItem.Payload)
        .OfType<GroupResponseEvent>()
        .Single(candidate =>
            candidate.IncomingCard == CardKind.ArrowBarrage &&
            candidate.ResponderSeat == 0);
    Equal(judgment.Succeeded, groupResponse.UsedResponse);
    Equal<int?>(null, groupResponse.ResponseCardId);
    Equal(
        judgment.Succeeded ? CardKind.Dodge : null,
        groupResponse.ResponseCardKind);
    TrueWithMessage(
        !game.Events.Skip(eventCount).Any(eventItem =>
            eventItem.Payload is CardRespondedEvent responded &&
            responded.ResponderSeat == 0),
        "Bagua does not invent a physical Dodge movement event");
    AssertCardInventory(game);
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

static void FireAttackSkipFlow()
{
    GameEngine? selectedGame = null;
    PendingDecision? selectedPrompt = null;
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
        result = game.DriveHumanPlay(action.CardId!.Value, targetSeat, advanceToHumanBoundary: false);
        if (result.Status != EngineStatus.Running || game.PendingDecision is not null)
        {
            continue;
        }

        var targetPrompt = game.CreateSnapshot(targetSeat).PendingDecision;
        if (targetPrompt is not { Kind: DecisionKind.FireAttackReveal })
        {
            continue;
        }

        var frame = game.ResolutionStack.OfType<CardUseFrame>()
            .SingleOrDefault(candidate => candidate.CardKind == CardKind.FireAttack);
        if (frame is null)
        {
            continue;
        }

        result = game.DriveAdvanceOneStep();
        var prompt = game.PendingDecision;
        if (result.Status == EngineStatus.AwaitingHumanCardSelection &&
            prompt is { Kind: DecisionKind.FireAttackDiscard, PlayerSeat: 0 } &&
            prompt.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("response") == "fire-attack-skip"))
        {
            selectedGame = game;
            selectedPrompt = prompt;
            selectedFrame = frame;
        }
    }

    if (selectedGame is null || selectedPrompt is null || selectedFrame is null)
    {
        throw new InvalidOperationException("No deterministic FireAttack skip boundary was found.");
    }

    var gameWithSkip = selectedGame!;
    var promptAtBoundary = selectedPrompt!;
    var frameAtBoundary = selectedFrame!;
    var skipChoice = promptAtBoundary.Choices.Single(choice =>
        choice.Parameters.GetValueOrDefault("response") == "fire-attack-skip");
    var revealed = gameWithSkip.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<FireAttackCardRevealedEvent>()
        .Single(eventItem => eventItem.ResolutionId == frameAtBoundary.Id);
    var targetBefore = gameWithSkip.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == frameAtBoundary.TargetSeats.Single());
    var sourceBefore = gameWithSkip.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == 0);

    var accepted = gameWithSkip.Submit(new AnswerPromptCommand(
        0,
        promptAtBoundary.PromptId,
        skipChoice.Id,
        gameWithSkip.Revision));
    TrueWithMessage(accepted.Accepted, "FireAttack skip choice accepted");
    accepted = gameWithSkip.Submit(new AdvanceCommand(gameWithSkip.Revision));
    TrueWithMessage(accepted.Accepted, "FireAttack skip continuation accepted");
    Equal(EngineStatus.AwaitingHumanPlay, accepted.State.Status);
    Equal(0, accepted.State.ProcessingCardCount);
    Equal(0, accepted.State.PublicRevealedCards.Count);
    Equal(0, gameWithSkip.ResolutionStack.Count);

    var resolved = gameWithSkip.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<FireAttackResolvedEvent>()
        .Single(eventItem => eventItem.ResolutionId == frameAtBoundary.Id);
    False(resolved.CausedDamage);
    Equal<int?>(null, resolved.MatchingDiscardCardId);
    False(gameWithSkip.Events.Any(eventItem =>
        eventItem.Payload is DamageRequestedEvent damage &&
        damage.ResolutionId == frameAtBoundary.Id));
    var targetAfter = gameWithSkip.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == frameAtBoundary.TargetSeats.Single());
    Equal(targetBefore.Hp, targetAfter.Hp);
    Equal(targetBefore.HandCount, targetAfter.HandCount);
    TrueWithMessage(targetAfter.Hand.Any(card => card.Id == revealed.CardId),
        "formal FireAttack skip keeps the revealed card in hand");
    Equal(sourceBefore.HandCount,
        gameWithSkip.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).HandCount);
    False(gameWithSkip.CardMovements.Any(movement =>
        movement.CardId == revealed.CardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.FireAttackFinished));
    TrueWithMessage(gameWithSkip.Events.Any(eventItem =>
        eventItem.Payload is CardUseFinishedEvent finished &&
        finished.ResolutionId == frameAtBoundary.Id &&
        finished.CardKind == CardKind.FireAttack), "FireAttack skip finishes card use");
    AssertCardInventory(gameWithSkip);
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

static void MultipleDelayedCardsFlow()
{
    var controlledKinds = Enumerable.Repeat(CardKind.Dodge, 48).ToArray();
    controlledKinds[0] = CardKind.Indulgence;
    controlledKinds[1] = CardKind.SupplyShortage;
    var controlledCardIds = Enumerable.Range(0, controlledKinds.Length)
        .Select(index => $"test-delayed:card-{index + 1}")
        .ToArray();
    var registry = ContentRegistry.Build(
        new StandardContentPackage(),
        new SyntheticPackage(
            "test-delayed",
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
                    "test-delayed:deck",
                    "双延时判定测试牌堆",
                    InitialHandSize: 4,
                    DrawPerTurn: 2,
                    controlledCardIds
                        .Select(cardId => new ContentDeckCardCount(cardId, 1))
                        .ToArray()));
            }));

    GameEngine? game = null;
    LegalAction? indulgence = null;
    LegalAction? supplyShortage = null;
    const int targetSeat = 1;
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
                DeckId = "test-delayed:deck",
                MaxTurns = 32
            },
            registry);
        var result = candidate.DriveStart();
        if (result.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }

        var actions = candidate.GetHumanLegalActions();
        var candidateIndulgence = actions.FirstOrDefault(action =>
            action.Kind == LegalActionKind.Indulgence &&
            action.TargetSeat == targetSeat);
        var candidateSupplyShortage = actions.FirstOrDefault(action =>
            action.Kind == LegalActionKind.SupplyShortage &&
            action.TargetSeat == targetSeat);
        if (candidateIndulgence is not null && candidateSupplyShortage is not null)
        {
            game = candidate;
            indulgence = candidateIndulgence;
            supplyShortage = candidateSupplyShortage;
        }
    }

    NotNull(game);
    NotNull(indulgence);
    NotNull(supplyShortage);
    var selected = game!;
    var firstResult = selected.DriveHumanPlay(
        indulgence!.CardId!.Value,
        targetSeat,
        advanceToHumanBoundary: false);
    ResolveNullificationWindowForTest(selected, firstResult);
    if (selected.GetHumanLegalActions().Count == 0)
    {
        selected.DriveAdvance();
    }

    var secondAction = selected.GetHumanLegalActions().Single(action =>
        action.Kind == LegalActionKind.SupplyShortage &&
        action.TargetSeat == targetSeat);
    var secondResult = selected.DriveHumanPlay(
        secondAction.CardId!.Value,
        targetSeat,
        advanceToHumanBoundary: false);
    ResolveNullificationWindowForTest(selected, secondResult);
    if (selected.GetHumanLegalActions().Count == 0)
    {
        selected.DriveAdvance();
    }

    selected.DriveHumanEndPlay();
    var resolved = selected.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<DelayedCardResolvedEvent>()
        .Where(eventItem => eventItem.TargetSeat == targetSeat)
        .ToArray();
    Equal(2, resolved.Length);
    Equal(CardKind.Indulgence, resolved[0].CardKind);
    Equal(CardKind.SupplyShortage, resolved[1].CardKind);
    Equal(!resolved[0].JudgmentSucceeded, resolved[0].SkippedPlayPhase);
    Equal(!resolved[1].JudgmentSucceeded, resolved[1].SkippedDrawPhase);
    Equal(
        resolved[0].SkippedPlayPhase ? TurnPhase.Discard : TurnPhase.Play,
        selected.Events.First(eventItem =>
            eventItem.Payload is PhaseChangedEvent phase &&
            phase.ActorSeat == targetSeat &&
            phase.Phase is TurnPhase.Play or TurnPhase.Discard).Payload is PhaseChangedEvent finalPhase
            ? finalPhase.Phase
            : throw new InvalidOperationException("The delayed target never reached a post-judgment phase."));
    TrueWithMessage(selected.CardMovements.Any(movement =>
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.DelayedCardFinish), "both delayed cards finish from judgment");
    AssertCardInventory(selected);
}

static void WushengFlow()
{
    GameEngine? selectedGame = null;
    CardSnapshot? selectedCard = null;
    int selectedTargetSeat = -1;
    for (var seed = 1; seed <= 4_096 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
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

        var revealed = game.CreateSnapshot(0, revealAll: true);
        var human = revealed.Players.Single(player => player.Seat == 0);
        if (human.Skills?.Any(skill => skill.ContentId == "standard:wusheng") != true)
        {
            continue;
        }

        var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
        {
            if (candidate.Kind != LegalActionKind.Slash ||
                candidate.PlayedCardKind != CardKind.Slash ||
                candidate.CardId is not { } cardId ||
                candidate.TargetSeat is not { } targetSeat)
            {
                return false;
            }

            var card = human.Hand.Single(handCard => handCard.Id == cardId);
            var target = revealed.Players.Single(player => player.Seat == targetSeat);
            return !IsSlashCard(card.Kind) &&
                   card.Suit is (Suit.Heart or Suit.Diamond) &&
                   target.IsAlive &&
                   target.Hp > 1 &&
                    target.Skills?.Any(skill => skill.ContentId != "standard:none") != true &&
                   target.Skills?.All(skill => skill.ContentId is not
                       ("standard:feedback" or "standard:jianxiong")) != false &&
                   target.Hand.All(handCard => handCard.Kind != CardKind.Dodge) &&
                   target.Equipment.All(equipment => equipment.Kind != CardKind.BaguaFormation);
        });
        if (action is not null &&
            revealed.Players.All(player => player.Skills?.Any(skill => skill.ContentId == "standard:yuanhu") != true))
        {
            selectedGame = game;
            selectedCard = human.Hand.Single(card => card.Id == action.CardId);
            selectedTargetSeat = action.TargetSeat!.Value;
        }
    }

    if (selectedGame is null || selectedCard is null || selectedTargetSeat < 0)
    {
        throw new InvalidOperationException("No deterministic Wusheng red-card Slash boundary was found.");
    }

    var gameWithWusheng = selectedGame!;
    var convertedCard = selectedCard!;
    var targetBefore = gameWithWusheng.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == selectedTargetSeat);
    var sourceBefore = gameWithWusheng.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == 0);
    var actionAfterStart = gameWithWusheng.GetHumanLegalActions().Single(action =>
        action.CardId == convertedCard.Id &&
        action.TargetSeat == selectedTargetSeat &&
        action.PlayedCardKind == CardKind.Slash);

    var prompt = gameWithWusheng.PendingDecision!;
    var accepted = gameWithWusheng.Submit(new PlayCardCommand(
        ActorSeat: 0,
        CardId: convertedCard.Id,
        TargetSeats: [selectedTargetSeat],
        ExpectedRevision: gameWithWusheng.Revision,
        PromptId: prompt.PromptId,
        PlayedCardKind: CardKind.Slash)
    { ConversionSource = actionAfterStart.ConversionSource });
    True(accepted.Accepted);
    var resultAfterUse = accepted.State;
    Equal(EngineStatus.AwaitingHumanPlay, resultAfterUse.Status);
    Equal(0, resultAfterUse.ProcessingCardCount);
    Equal(sourceBefore.HandCount - 1, gameWithWusheng.State.Players.Single(player => player.Seat == 0).HandCount);
    var targetAfter = gameWithWusheng.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == selectedTargetSeat);
    Equal(targetBefore.Hp - 1, targetAfter.Hp);

    var declared = gameWithWusheng.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<CardUseDeclaredEvent>()
        .Single(eventItem => eventItem.CardId == convertedCard.Id);
    Equal(CardKind.Slash, declared.CardKind);
    var used = gameWithWusheng.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<CardUsedEvent>()
        .Single(eventItem => eventItem.CardId == convertedCard.Id);
    Equal(CardKind.Slash, used.CardKind);
    var damage = gameWithWusheng.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<DamageRequestedEvent>()
        .Single(eventItem => eventItem.SourceSeat == 0 && eventItem.TargetSeat == selectedTargetSeat);
    Equal(CardKind.Slash, damage.SourceCard);
    var finished = gameWithWusheng.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<CardUseFinishedEvent>()
        .Single(eventItem => eventItem.CardId == convertedCard.Id);
    Equal(CardKind.Slash, finished.CardKind);
    TrueWithMessage(gameWithWusheng.CardMovements.Any(movement =>
        movement.CardId == convertedCard.Id &&
        movement.CardKind == convertedCard.Kind &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Use), "Wusheng keeps physical card movement");
    TrueWithMessage(gameWithWusheng.CardMovements.Any(movement =>
            movement.CardId == convertedCard.Id &&
            movement.From == CardLocation.Processing &&
            (movement.To == CardLocation.DiscardPile && movement.Reason == CardMoveReasons.UseFinished ||
             movement.To.Zone == CardZoneKind.Hand &&
             movement.Reason.Value.EndsWith(".ClaimDamageCards", StringComparison.Ordinal))),
        "Wusheng must finish or explicitly transfer its physical card out of Processing");
    TrueWithMessage(actionAfterStart.Description.Contains("当作【杀】", StringComparison.Ordinal),
        "Wusheng action exposes card conversion");
    AssertCardInventory(gameWithWusheng);
}

static void LongdanFlow()
{
    GameEngine? selectedGame = null;
    CardSnapshot? selectedCard = null;
    var selectedTargetSeat = -1;
    for (var seed = 1; seed <= 4_096 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
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

        var revealed = game.CreateSnapshot(0, revealAll: true);
        var human = revealed.Players.Single(player => player.Seat == 0);
        if (human.Skills?.Any(skill => skill.ContentId == "standard:longdan") != true || HasSkill(game, "standard:yuanhu"))
        {
            continue;
        }

        var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
        {
            if (candidate.Kind != LegalActionKind.Slash ||
                candidate.PlayedCardKind != CardKind.Slash ||
                candidate.CardId is not { } cardId ||
                candidate.TargetSeat is not { } targetSeat)
            {
                return false;
            }

            var card = human.Hand.Single(handCard => handCard.Id == cardId);
            var target = revealed.Players.Single(player => player.Seat == targetSeat);
            return card.Kind == CardKind.Dodge &&
                   target.IsAlive &&
                   target.Hp > 1 &&
                    target.Skills?.Any(skill => skill.ContentId != "standard:none") != true &&
                   target.Hand.All(handCard => handCard.Kind != CardKind.Dodge);
        });
        if (action is not null)
        {
            selectedGame = game;
            selectedCard = human.Hand.Single(card => card.Id == action.CardId);
            selectedTargetSeat = action.TargetSeat!.Value;
        }
    }

    if (selectedGame is null || selectedCard is null || selectedTargetSeat < 0)
    {
        throw new InvalidOperationException("No deterministic Longdan Dodge-to-Slash boundary was found.");
    }

    var gameWithLongdan = selectedGame!;
    var convertedCard = selectedCard!;
    var targetBefore = gameWithLongdan.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == selectedTargetSeat);
    var sourceBefore = gameWithLongdan.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == 0);
    var actionAfterStart = gameWithLongdan.GetHumanLegalActions().Single(action =>
        action.CardId == convertedCard.Id &&
        action.TargetSeat == selectedTargetSeat &&
        action.PlayedCardKind == CardKind.Slash);

    var prompt = gameWithLongdan.PendingDecision!;
    var accepted = gameWithLongdan.Submit(new PlayCardCommand(
        ActorSeat: 0,
        CardId: convertedCard.Id,
        TargetSeats: [selectedTargetSeat],
        ExpectedRevision: gameWithLongdan.Revision,
        PromptId: prompt.PromptId,
        PlayedCardKind: CardKind.Slash)
    { ConversionSource = actionAfterStart.ConversionSource });
    TrueWithMessage(accepted.Accepted, "Longdan Dodge-to-Slash command accepted");
    Equal(EngineStatus.AwaitingHumanPlay, accepted.State.Status);
    Equal(0, accepted.State.ProcessingCardCount);
    Equal(sourceBefore.HandCount - 1,
        gameWithLongdan.State.Players.Single(player => player.Seat == 0).HandCount);
    var targetAfter = gameWithLongdan.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == selectedTargetSeat);
    Equal(targetBefore.Hp - 1, targetAfter.Hp);

    var declared = gameWithLongdan.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<CardUseDeclaredEvent>()
        .Single(eventItem => eventItem.CardId == convertedCard.Id);
    Equal(CardKind.Slash, declared.CardKind);
    var used = gameWithLongdan.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<CardUsedEvent>()
        .Single(eventItem => eventItem.CardId == convertedCard.Id);
    Equal(CardKind.Slash, used.CardKind);
    var damage = gameWithLongdan.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<DamageRequestedEvent>()
        .Single(eventItem => eventItem.SourceSeat == 0 && eventItem.TargetSeat == selectedTargetSeat);
    Equal(CardKind.Slash, damage.SourceCard);
    var finished = gameWithLongdan.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<CardUseFinishedEvent>()
        .Single(eventItem => eventItem.CardId == convertedCard.Id);
    Equal(CardKind.Slash, finished.CardKind);
    TrueWithMessage(gameWithLongdan.CardMovements.Any(movement =>
        movement.CardId == convertedCard.Id &&
        movement.CardKind == convertedCard.Kind &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Use), "Longdan keeps physical card movement");
    TrueWithMessage(gameWithLongdan.CardMovements.Any(movement =>
        movement.CardId == convertedCard.Id &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.UseFinished), "Longdan finishes physical card");
    TrueWithMessage(actionAfterStart.Description.Contains("当作【杀】", StringComparison.Ordinal),
        "Longdan action exposes card conversion");
    AssertCardInventory(gameWithLongdan);
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

static void ObserverFailuresDoNotChangeOutcome()
{
    var options = new GameOptions
    {
        UseInteractiveDiscard = false,
        Seed = 359,
        HumanSeat = -1,
        HumanRole = null,
        MaxTurns = 200
    };
    var baseline = GameEngine.CreateStandard(options, StandardContentRegistry.Create());
    var faulted = GameEngine.CreateStandard(options, StandardContentRegistry.Create());
    faulted.LogAdded += _ => throw new InvalidOperationException("log observer failed");
    faulted.AiThoughtAdded += _ => throw new InvalidOperationException("thought observer failed");
    faulted.StateChanged += _ => throw new InvalidOperationException("state observer failed");
    faulted.CardMoved += _ => throw new InvalidOperationException("movement observer failed");

    var baselineResult = baseline.DriveStart();
    var faultedResult = faulted.DriveStart();

    Equal(baselineResult.Winner, faultedResult.Winner);
    Equal(baseline.SerializeState(revealAll: true), faulted.SerializeState(revealAll: true));
    Equal(JsonSerializer.Serialize(baseline.Log), JsonSerializer.Serialize(faulted.Log));
    Equal(JsonSerializer.Serialize(baseline.AiThoughts), JsonSerializer.Serialize(faulted.AiThoughts));
    Equal(JsonSerializer.Serialize(baseline.CardMovements), JsonSerializer.Serialize(faulted.CardMovements));
    True(faulted.ObserverFailures.Count > 0);
    AssertCardInventory(faulted);
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

static void HumanDodgeApi()
{
    GameEngine? selectedGame = null;
    for (var seed = 1; seed <= 4_096 && selectedGame is null; seed++)
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
        var decisions = 0;
        while (result.Status != EngineStatus.Completed && decisions++ < 400)
        {
            if (result.Status == EngineStatus.AwaitingHumanResponse)
            {
                if (result.PendingDecision?.Kind == DecisionKind.RespondDodge)
                {
                    selectedGame = game;
                    break;
                }

                result = result.PendingDecision?.Kind == DecisionKind.RespondSlash
                    ? game.DriveHumanRespondSlash(useSlash: false)
                    : game.DriveHumanRespond(useDodge: false);
                continue;
            }

            result = result.Status == EngineStatus.AwaitingHumanPlay
                ? game.DriveHumanEndPlay()
                : game.DriveAdvanceOneStep();
        }
    }

    NotNull(selectedGame);
    var before = selectedGame!.State.Players.Single(player => player.Seat == 0);
    True(before.Hand.Any(card => card.Kind == CardKind.Dodge));
    var resultAfterDodge = selectedGame.DriveHumanRespond(
        useDodge: true,
        advanceToHumanBoundary: false);
    var after = resultAfterDodge.State.Players.Single(player => player.Seat == 0);
    Equal(before.Hp, after.Hp);
    True(selectedGame.Log.Any(entry => entry.Type == "CardResponded" && entry.ActorSeat == 0));
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

static void StepGuardFinishesResponse()
{
    var game = GameEngine.CreateStandard(new GameOptions
    {
        UseInteractiveDiscard = false,
        Seed = 617,
        HumanSeat = -1,
        HumanRole = null,
        MaxTurns = 5_559
    }, StandardContentRegistry.Create());

    var result = game.DriveStart();

    Equal(EngineStatus.Completed, result.Status);
    True(result.Winner != Winner.None);
    Equal(0, result.State.ProcessingCardCount);
    Equal(0, game.ResolutionStack.Count);
    Equal(0, game.CreateCardZoneDiagnostics().Count(card => card.Location == CardLocation.Processing));
    AssertCardInventory(game);
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
