using System.Text.Json.Serialization;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CardGame.Core;

public enum SkillRuleQuery
{
    DrawCount,
    HandLimit,
    SlashLimit,
    OutgoingDistance,
    IncomingDistance,
    SlashDistanceLimit,
    AttackRange,
    CardTargetCount,
    CardUseDistanceLimit,
    CardEffectImmunity = 630
}
public enum SkillRuleOperation { Add, Set, Unlimited }
public enum SkillRuleValueExpression { LivingFactionCount, OwnedZoneCount, OwnerLostHp = 2, NegatedOwnedZoneCount = 3, NegatedOwnerLostHp = 900, OwnerMarkerCount = 2600, CurrentTurnUsedHandSuitCount = 4600, PublicLivingFactionCount = 5200 }
public enum SkillRuleQueryDependency { MarkerState }
public enum SkillProgramConditionKind { Always, OwnTurn, NotOwnTurn, Wounded, HpAtLeast, HandCountAtLeast, CardUseIsRed, SelectedTargetIsOther, SelectedTargetHandGreaterThanOwner, PindianWon, PindianNotWon, BooleanState, All, Any, Not, FaceDown, Chained, ChoiceIs, BoundCardsSameColor, BoundCardsMatchCategories, BoundCardsMatchKinds = 20, HasClaimableDamageCards = 21, AttackRangeCoverageDecreased = 22, HasOwnedCardCategory = 23, BoundCardCountAtLeast = 24, ActivationCardCountAtLeast = 25, ClassicIdentityMode = 26, BoundCardSuitMatchesChoice = 27, BoundCardsMatchSuits = 28, BoundCardCategoryMatchesCardAction = 29, EventTargetGenderIs = 30, EventSourceGenderIs = 31, SelectedTargetWounded = 820, RuntimeBooleanState = 450, PublicCounterAtLeast = 451, PublicCounterOdd = 452, HandCountGreaterThanHp = 1020, PositiveHandLimit = 1021, HasUsableHandCard = 1022, RequestedSlashDamagedOwner = 1023, PreviousPlayCardIsBasic = 1024 }
public enum SkillProgramTriggerConditionKind
{
    Always,
    Compare,
    ClassicIdentityMode,
    BooleanState,
    CardActionActorIsCurrentTurn,
    CardActionActorIsOwner,
    CardActionPhaseIsPlay,
    CardUseCausedDamage,
    CardUseConversionSkillIs,
    LordGeneralNotIn,
    All,
    Any,
    Not,
    OtherDamageParticipantAlive,
    DirectCardUseDamage = 14,
    DamageCardIsRed = 15,
    DamageTargetIsOther = 16,
    CardActionCategoryIs = 17,
    CardActionTargetIsOwner = 18,
    DeathKillerIsOwner = 19,
    DeathVictimHasCards = 20,
    CardActionCardIsRed = 21,
    DamageCardIsSlash = 22,
    OwnerIsTurnPlayer = 23,
    CardActionFromOwnerHand = 24,
    DamageSourceIsOwner = 25,
    DamageSourceFactionIs = 26,
    FaceDown = 27,
    CardActionMatchesPreviousPlayCard = 28,
    CardActionCardIsBlack = 800,
    DamageSourceGenderIs = 801,
    HasUnfulfilledPlayPhaseColorRestriction = 803,
    CardActionSuitIs = 901,
    OwnerKilledThisTurn = 550,
    TurnDiscardIncludesAllSuits = 1660,
    DeathExtinguishedFaction = 1020, DiscardPhaseSuitsAllDistinct = 1021, OtherDamageSourceAlive = 1022, DamageSourcePairUnused = 1023,
    PreviousPlayCardIsBasic = 1024,
    PhaseIsPlay = 1025,
    CardActionOpponentIsOwner = 1026
}
public enum SkillProgramTriggerValueKind
{
    CurrentActualPlayPhysicalSlashLossCount = 6700,
    OwnerTrickUsesThisActualTurn = 4400,
    TurnOwnerDamageDealtThisTurn = 2800,
    CurrentAvailableEquipmentSlotCount = 1400,
    CurrentHandCountMinusHp = 1660,
    IntegerConstant = 0,
    CardsUsedThisTurn,
    CurrentHp,
    CurrentMaxHp,
    CurrentHandCount,
    CurrentOwnedZoneCount,
    MovedCardCount,
    SourceZoneCountBefore,
    SourceZoneCountAfter,
    EventTargetHp,
    CardUseDesignatedTargetCount = 10,
    SourceToTargetDistanceAtDamage = 11,
    EventTargetMaxHp = 12,
    EventTargetHandCount = 13,
    CurrentAttackRange = 14,
    PlayPhaseKillCountByTurnOwner = 15,
    PlayPhaseDamageDealtByTurnOwner = 16,
    OwnerEventTargetDistance = 17,
    DestinationZoneCountBefore = 18,
    DestinationZoneCountAfter = 19,
    HpChangeAmount = 20,
    HpBeforeChange = 21,
    HpAfterChange = 22,
    OwnerAttributedMarkerCount = 23,
    CardsUsedOrRespondedThisTurn = 24,
    LivingPlayersMinHp = 25,
    TurnOwnerDiscardPhaseHandDiscardCount = 26,
    DamageInstancesTakenThisTurn = 27,
    EventTargetDamageInstancesTakenThisTurn = 4100,
    CardActionPhysicalCardCount = 400,
    MarkerParity = 450,
    LivingWoundedCount = 451,
    GlobalMarkerCount = 452,
    OwnerLostHp = 600, CardActionHandCardCount = 601, PlayPhaseDamageTakenByAny = 900,
    EventTargetMarkerCount = 1280, EventSourceMarkerCount = 3101,
    TurnOwnerSlashUseCount = 3102,
    MovedEquipmentCardCount = 5000
}
public enum SkillProgramSuitSource { DamageCard }
public enum SkillProgramComparisonOperator
{
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual
}
public enum SkillProgramTargetKind
{
    AnyLivingLeastHandCount = 1460, OtherLivingHighestHand = 1461,
    AnyLivingWithHand = 1020, LivingWhoseAttackRangeIncludesLord = 1021,
    OtherLiving = 0,
    OtherLivingWithHand,
    OtherLivingWithFewerHandCards = 1462,
    OtherLivingAtDistanceOne,
    AnyLiving,
    OtherWounded,
    AnyWounded,
    AnyLivingHandBelowMaxHp,
    EventTarget,
    OtherLivingExceptSource,
    MaximumAttributedMarker,
    OtherLivingUnequalHandPair,
    OtherLivingHandAtLeastOwner,
    CurrentCardUseTargets = 13,
    OtherLivingInBoundParticipantAttackRange = 14,
    OtherLivingWithHandHpGreaterThanOwner = 15,
    OtherLivingMale = 16,
    OtherWoundedMale = 17,
    OtherLivingInAttackRange = 18,
    OtherLivingSlashable = 19,
    OtherLivingVirtualSlashTarget = 20,
    SlashRedirectable = 21,
    OtherLivingPair = 22,
    OtherLivingLeastHandCount = 23,
    OtherLivingWhoseAttackRangeIncludesOwner = 24,
    EventSource = 25,
    OtherLivingWithQinggangSword = 26,
    LivingPairDistinct = 27,
    CurrentTurnPlayer = 400,
    EquipmentExchangePair = 660,
    EventTargetWithHand = 661,
    AnyLivingHighestHp = 730,
    AnyLivingHighestHand = 731,
    OtherLivingEmptyHand = 742,
    OtherLivingHandAtMostOwner = 5700,
    OtherLivingDelayedTrickTarget = 800,
    OtherLegalCurrentCardTarget = 820,
    OtherLivingWithHandOrEquipment = 940,
    AnyLivingMale = 2000,
    OtherLivingWuFactionWithHand = 2001, CurrentArrowBarrageTargets = 2601}
public enum SkillProgramCardCategory { Basic, Trick, Equipment, InstantTrick }
public enum SkillProgramGainPhaseQualification { OutsideOwnerDraw }
public enum SkillProgramTurnOwnerScope { Own = 0, OtherLiving = 1, OwnOrPreviousLiving = 5300, EarnedActualEnding = 5601,
    PaidPrepDiscardEnding = 6600, IssuedFixedDistanceEnding = 8600
}
public enum SkillProgramDiscardOwnerScope { Other = 0, Own = 1 }
public enum SkillProgramDamageModifierExpiration { CurrentTurnEnd = 0, NextOwnerTurnStart = 1 }
public enum SkillProgramDamageModifierSourceScope { OwnerUsed = 0, DamageSource = 1, DamageParticipant = 2 }
public enum SkillProgramDamageModifierCondition
{
    Always = 0,
    SourceOutsideTargetAttackRange = 1,
    SourceNotFewerHandAndEquipmentThanTarget = 2,
    OwnerUniqueMaximumHand = 3,
    FaceStatesDiffer = 1021,
    ChainedFirePropagationOrigin = 1020
}
public sealed record SkillProgramDamageModifier(
    string Id, IReadOnlyList<CardKind> CardKinds, int Amount,
    SkillProgramDamageModifierCondition Condition,
    SkillProgramDamageModifierSourceScope SourceScope);
public sealed record SkillProgramChoiceOption(string Id, SkillProgramCondition Condition)
{
    public string Label { get; internal set; } = "";
}
public enum SkillProgramEffectOp
{
    StoreNonBasicOwnedPublicPile = 8500, RemovePublicPileAfterAttackDamage = 8501, ResolvePreparationPublicPile = 8502,
    DepositSelectedSourceCurse = 8200, DrawForSourceCurseUse = 8201, LoseHpForLostSourceCurses = 8202,
    DepositBoundPrivateCardOffer = 7700, ResolveDeferredPrivateCardOffer = 7701, ResolveGameTargetHandHpChoice = 7702,
    DiscardTargetHpCardsAndDamage = 7601,
    DiscardOwnedCardToAdjustCurrentDamage = 7100,
    DiscardHandOrUseEquipment = 7150, MoveFieldEquipment = 7151,
    GiveDrawPileBottomCard = 7160, GivePindianCard = 7161,
    GiveSelectedTargetHand = 7162,
    XiZhenResponseBenefit = 7163,
    SelectedTargetVirtualSlashAgainstOwner = 7164,
    YanjiaoRevealTopCards = 7165, YanjiaoSplitRevealedCards = 7166, ShenShenDrawAndArmBonus = 7167,
    TunanUseRevealedCard = 7168, BijingMarkHandCards = 7169, BijingRecastMarkedCards = 7170,
    BijingPunishDiscardPhase = 7171,
    DuanfaDiscardAndDraw = 7180, YoudiBaitDiscard = 7181,
    GuanchaoChoosePattern = 7196, GuanchaoRankDraw = 7197, XunxianGiftUsedCard = 7198,
    ChangjiDesignationDraw = 7252, ZengouGiftMarkedCards = 7253, ZengouPunishRecipient = 7254,
    IssueShownEntityTurnPolicy = 6500,
    PlaceCapturedEquipmentAndDraw = 6200, RestoreActualDiscardBatch = 6201,
    DiscardSuitPreventDamageAndBenefit = 5900, PlaceMatchedJudgmentCard = 5901,
    ClaimDiscardedEntityWithProvenance = 4500, UseVirtualAlcohol = 4501, OfferFaceUpForOutsideClaims = 4502,
    AwaitBoundCardMovements = 1484, UseVirtualDuel = 1486,
    InsertGrantedEntityPlayPhase = 4800, ClaimGrantedPhaseSlash = 4801,
    FreezeLivingFactionRecovery = 4802, DrawToFrozenFactionCount = 4803, TurnOverIfFrozenFactionCountExceedsGameDamage = 4804,
    RevealTopCardsWithNextBooleanBonus = 3600, ObtainBoundCardsAndArmNextRevealBonus = 3601,
    RecoverOtherDyingVictimTo = 3602,
    SuppressOwnSkillAfterAlcoholSlashDamage = 3900,
    StartOwnedDamagePointJudgment = 3901,
    JudgeDamageTargetThenOfferSuitDiscard = 4200,
    ChoosePrivateColorsDiscardAndDuel = 4300,
    GrantLeastHandMarkerOrDraw = 3800,
    ViewAndTakeSelectedTargetHand = 3300, RequestHandBySuitsOrLoseHp = 3301,
    RecastBoundCard = 3100,
    PayHpToGrantOneUseDamageShield = 3000,
    DrawThenDiscardHandToMaximumHp = 2900,
    UseOwnerSlashAgainstTurnOwner = 2800,
    DeclareDeckCriterionAndGiveMatchingCard = 2801,
    RecoverToMaximum = 1900, DrawRecoveryReceipt = 1901, ReserveNextSlashDamage = 1902,
    SuppressCurrentSlashTargetAndJudgeSuitDiscard = 1940,
    AccumulatePaidPhaseGift = 1740, OfferVirtualBasicCard = 1741, RewardOutOfTurnFactionSlash = 1742,
    GiveOwnedCardToOtherFinalTargetAndDraw = 1580,
    DiscardNonFinalTargetCardThenDraw = 1440, DiscardHandToNamedTurnCount = 1441,
    CollectFinalTargetCardInPublicPile = 1380, ExchangePublicPileHand = 1381, ObtainPublicPileCard = 1382, DiscardPublicZoneAfterHandPayment = 1383,
    CommitConversionPolarity = 1460, GiveSelectedOwnedCardAndDamage = 1461, ObserveDamageSourceHandAndGive = 1462, DrawToHandCount = 1463,
    AlternatingSuitDrawDiscard = 1420, FirstCategoryCompletedTop = 1421,
    DiscardDistinctFactionParticipants = 1820,
    HoldOwnerHandUntilTurnEnd = 1700,
    InitializePrivateGeneralLibrary = 1640, AcquirePrivateGeneralAvatar = 1641, ChoosePrivateGeneralAvatar = 1642,
    StoreArbitraryOwnedPublicPile = 1541, ResolveFirstGameDomainCrossing = 1540, UsePublicPileEquipmentSequence = 1542,
    StoreBoundHandInPublicPile = 1480, PublicPileColorDamage = 1481, RewardDiscardedActionColor = 1482, AwaitOwnedCardMovement = 1483,
    PeekTurnQuotaTop = 1320,
    NullifyFirstTurnTargetByHand = 1321,
    IssueCardNoResponseAndPlayUseBan = 1340,
    ResolveDiscardBudgetParticipants = 1360, PreventOwnPlayOutsideTargetRangeDamage = 1361, DiscardOutsideRangeAfterInsufficientUses = 1362, OfferCompletedFactionCostGift = 1363,
    ChangeParticipantMarker = 1280, ConsumeMarkerPreventDamage = 1281, AddMarkerSubjectNormalDraw = 1282,
    ScheduleDeferredHandAlignment = 1300,
    ResolveDeferredHandAlignment = 1301,
    ExchangeRespondedCardEntities = 1200, DrawPublicSuitThenEscalatingDiscard = 1201,
    UseRandomDeckEquipment = 1240, GrantRandomSkillAndSuitShield = 1241,
    DeclareNameForTargetDefense = 1120, DrawAndDraftLowHandPopulation = 1121,
    ApplyAlternatingChoiceBenefit = 1100,
    ObtainDeckCardWithConsecutiveTarget = 1101,
    AbolishEquipmentSlotGroup = 1400, RecastSelectedEquipment = 1401, ReplaceSkillsOnPreparation = 1402,
    CompareSelectedHandWithHpHand = 1160,
    AdjustPersistentHandLimit = 1161,
    ProhibitSelfCardTargetsForTurn = 1162,
    ChooseDifferentActionCategoryGift = 1080,
    DeclareBoundCardNameUntilTurnEnd = 1040,
    UpgradeConversionTier = 1041,
    ReplaceAllSlashTargets = 1180,
    StartCardActionPindian = 1181,
    Draw = 0,
    Recover,
    LoseHp,
    Damage,
    Pindian,
    GiveSelected,
    DiscardSelected,
    InsertPhase,
    RecoverTo,
    TurnOver,
    RevealTopCards,
    SelectCardSubset,
    MoveBoundCards,
    SetFaceState,
    SelectTarget,
    SelectTargets,
    SelectSourceCard,
    GiveBoundCard,
    ClaimDamageCards,
    TakeRandomHandCardFromSelectedTargets,
    FilterBoundCards,
    AdjustNormalDraw,
    GrantTurnCardDamageModifier,
    GrantTurnCardActionProhibition,
    GrantTurnRuleModifier,
    GrantTurnCardTargetRestriction,
    StartJudgment,
    GrantTurnCardConversion,
    DiscardOwnedZoneCards,
    SetChainedState,
    SelectAndMoveOwnedCard,
    RefundCardUseDebit,
    StartPindian,
    SetBooleanState,
    ToggleBooleanState,
    GrantDirectedTurnCardPolicy,
    ChangeMaximumHp,
    GrantSkills,
    ChooseOption,
    SelectOwnedCards,
    GrantTurnHandColorRestriction,
    PreventCurrentDamage,
    CaptureSelectedCards,
    RevealBoundCards,
    ChooseDifferentCategoryDiscard,
    UseSelectedCardsAs,
    UseAllHandCardsAsOrdinaryTrick,
    GrantTurnSkills,
    ChangeAttributedMarker,
    CauseDeathUnlessBoundCardKind,
    UseBoundCardAsDyingAlcohol,
    ChooseOtherOwnedCardDiscard,
    DistributeOwnedCards,
    RequestAttackRangeAid,
    NullifyCurrentCardEffect,
    NullifySelectedCardEffects = 55,
    ReplaceJudgment,
    StartVirtualDuel,
    RequestFactionCard,
    TransferRandomOwnedCard,
    AccumulateSelectedCardCount,
    ClaimJudgmentCard,
    ReorderTopCards,
    RepeatJudgment,
    SkipTurnPhases,
    UseVirtualCard,
    RevealUniqueRankForDying = 70,
    ProhibitCurrentResponse,
    RedirectCurrentAttack,
    RedirectCurrentDamage = 75,
    HoldTargetCards,
    RevealTargetHandCard,
    UseBoundCardByTarget,
    PendExtraTurn,
    ClaimDeathCleanupCards,
    GrantTurnHandCardProhibition,
    AbolishOwnerAreas,
    LoseDeathSourceSkills,
    ChooseOwnCardDiscard,
    ExchangeSelectedTargetHands,
    RequestSlashByTarget,
    BindDiscardPhaseDiscards,
    RestorePhaseHandDiscards,
    ClaimMovedCards,
    TakeRandomCardFromEveryOtherCharacter,
    UseVirtualDyingAlcohol,
    DamageParticipants = 400,
    LoseHpParticipants,
    DiscardParticipantCards,
    InitializePrivatePile,
    ExchangePrivatePile,
    GrantAttributedNatureEffect,
    RecoverAllLiving,
    SpendMarkerOrLoseHp,
    LoseHpUnclamped,
    RequestSlashByNearest,
    SelectDistinctSuitHandDiscards = 450,
    ApplyHandDiscardShare = 451,
    SuppressGeneralSkill = 452,
    SetMarkerAmount = 453,
    MoveUniqueMarker = 454,
    ClaimMarkedHand = 455,
    DamageOtherLiving = 456,
    SelectChainedByMarker = 457,
    DiscardTargetEquipment = 458,
    EndCurrentPlay = 459,
    SelectOneSelectedTarget = 460,
    AlterEquipmentSlots = 500,
    AbolishRandomEquipmentSlot,
    SampleFactionSkills,
    ExpireSampledSkills,
    ReplaceSkillsOnAwakening,
    AccumulateCardRank,
    ObtainDeckRankSum,
    DamageAfterDeckShuffle,
    EquipSampledGenerals,
    DamageFarthestCharacter,
    PlaceNamedWeapon,
    ReclaimNamedWeapon,
    InheritWeapon,
    BalanceHandAttackTricks,
    ReplaceJudgmentPhase,
    DrawAllHandSelectedBonus = 550,
    OfferVirtualSlashOrDraw = 630,
    GrantTurnCardEffectImmunity = 631,
    ExchangeSelectedTargetEquipment = 660,
    RequestSlashAgainstChosenTarget = 701,
    TakeSelectedTargetCards = 702,
    ChooseCategoryAlternativeDiscard = 740,
    EscalatingDiscardOrDamage = 741,
    PutDiscardedCardsOnDrawPileTop = 760,
    PutOwnOrPreviousFirstDiscardOnTop = 5300, LoseHpIfRevealedNonEquipmentDiffers = 5301,
    StoreAdjacentDiscardedSlash = 5400, PayCompletedUseDiscardOrLoseHp = 5401,
    UseRoundPricedPileDyingAlcohol = 5402, OfferCurrentSlashFireAndExtraTarget = 5403,
    CollectPublicPile = 780,
    GrantNextCardTargetAdjustment = 782,
    GrantNextActualUseTargetAdjustment = 4700,
    UseDiscardedCardAsDelayedTrick = 800,
    UseVirtualSlash = 801,
    PayEquipmentColorDiscard = 802,
    GrantPlayPhaseColorRestriction = 803,
    OfferCompletedCardGift = 810,
    ApplyCurrentCardEnhancements = 811,
    ConsumeCategoryTargetLedger = 820,
    ReplaceCurrentCardUseActor = 821,
    AddCurrentCardUseTarget = 822,
    ReduceCurrentDamage = 823,
    RequestFactionRecovery = 900, SetNextTurnRuleModifier = 901,
    WeaponDiscardOrDamageBonus = 902, ResolveJudgmentColorBenefit = 903,
    RecastSelectedCards = 920, DrawCompletedCardParticipants = 921,
    RevealSelectedHandAgainstTarget = 922,
    ChooseHandCountIntervention = 940, RevealHandColorDiscardAndTake = 941,
    DrawThenPutOwnedCardOnTopParticipants = 942, LoseOwnerSkillsAndGrant = 943,
    ViewTopCardsAndObtainMatchingCards = 1000, DepositBoundCardsUntilNextTurn = 1001, ObtainDeferredPile = 1002, RewardDeferredProviders = 1003,
    GrowMaximumHpAndHp = 1020, ConsumeTargetPhaseLedger = 1021, GrantTurnBoundSuitUseProhibition = 1022, DiscardSelectedParticipantCards = 1023, OfferBoundCardsForDamagePrevention = 1024,
    SetTurnHandLimitFromPlayDamage = 944, GrantGameFactionAttackRangeTargets = 945,
    DrawTurnOwnerThenDiscardMaximumHandForDodge = 946,
    StoreTopCardInPublicPile = 1140, ExchangePublicPile = 1141, DistributePublicPileIfAllSuits = 1142,
    ExchangeOwnedCardThroughDeckEnd = 1220, UseDeckSlashesThenShuffle = 1221,
    SelectRelativeZoneDemandTarget = 1260, DrawOnFirstProgramTargetEncounter = 1261,
    GrantTurnRedSlashBenefits = 1680, UseDiamondDelayedOrDiscard = 1780, RevealOwnedBoundCardAppearance = 1860, IssueCurrentTurnNonLockedSkillSuppression = 1861, GrantCurrentTurnDirectedHeartSlashBonus = 1862,
    GiveShownBoundCardsAndGrantTurnHandLimit = 1960,
    PlaceSelectedEquipment = 2000, FreezeSelectedHpPair = 2001,
    PreventCurrentTargetSlashCancellation = 2100, AddCurrentTargetSlashDamage = 2101,
    ChooseOwnerHpLoss = 2200, DrawPaidHpLoss = 2201, GrantPaidHpLossDistance = 2202, GrantPaidHpLossSlashLimit = 2203,
    DiscardDamageTargetAndClaimMount = 2300, OfferRedDiscardRecoveryChoice = 2340,
    DrawByDamageCardColor = 2400, UseSelectedActorDuel = 2401,
    ObtainDamageTargetCardAndResolveCategory = 2500,
    GrantFactionPopulationMarker = 2600, RemoveSelectedCurrentArrowBarrageTarget = 2601,
    GrantTurnOriginalTargetAddition = 4900,
    OfferOriginalTargetAddition = 4901,
    OfferShortRangeSlashTarget = 8600, GrantFixedDistanceOneTurnPolicy = 8601, SettleFixedDistanceOneEndingDebt = 8602,
    GrantTurnHandLimitCardKindExemption = 3101,
    PayOwnedCardOrMarker = 3500, RecordEndHandCountAndGrantMarker = 3501,
    ClaimCurrentUsePhysicalCards = 3200, PreventCurrentTargetSlashCancellationByRule = 3201,
    DiscardBoundCardForTurnSlashBenefits = 3400, ScheduleFirstRoundGameUsageRefund = 3401,
    PreventCurrentDamageAndDrawMultiple = 4000, RequestLegalSlashByNearest = 4002, OfferUnlimitedVirtualSlash = 4003,
    SelectEquipmentPairAndPayment = 5500, SelectDyingOwnedCard = 5501,
    DrawExtraAndArmHalfHandSupport = 6000, GiveHalfHandAndIssueTargetSupport = 6001,
    ExchangeHandsAndArmPhaseDebt = 6002, SelectFrozenHandExchangeDebtPayment = 6003, OfferHalfHandRecipientSupport = 6004,
    DrawThenDiscardSuitsForDyingPeach = 7900, UseOwnPlayHistoryAtEnding = 7901,
    GrantJudgedRankSplitSlashTurnPolicy = 8400, DrawFromOtherActualBasicDiscard = 8401,
    DiscardTurnOverAndTakeHand = 8000, ReturnIssuedPhaseHandDebt = 8001,
    DrawEndingPairThenBlockRoundIfUnequal = 6700, RecastSelectedPhysicalSlash = 6701,
    ObtainOneFromEachSelectedTarget = 6300, GiveShownCardToLeastOriginalTarget = 6301,
    IssueFixedRecipientBenefit = 6302, SelectIssuedFixedRecipient = 6303,
    SelectIssuedFixedRecipientWithDeathReturn = 6800,
    DrawBeforeCappedConversionTierUpgrade = 6900,
    ChooseCategoryOrSequentialDiscard = 5700, EscalatingDiscardOrDamageFromSelected = 5701,
    DrawExtraAndArmTurnDamageUseDebt = 5200, SelectTurnDamageUseDebtPayment = 5201, PreventDamageAndConsumeSourceFaction = 5202,
    DrawFireTargetAndGrantTurnUseQuota = 7401, LoseSkillsAndObtainNamedCard = 7402,
    ReceiveOwnerDamage = 4100, ConsumeDistinctTurnTarget = 4101, DrawOwnerAtAppliedDamage = 4102,
    GiveBoundCardThenOfferVirtualSlashOrSharedDraw = 5100,
    PayHpThenNullifyOwnActualUseTarget = 5600, ScheduleEarnedActualEndingBenefit = 5601, DrawLostHpThenOfferOwnedCardsUpTo = 5602,
    DonateAllEquipmentAndOfferRecipientBenefits = 5800, ChooseEquipmentOrDrawAfterOtherActualTurn = 5801,
    DiscardBoundCardForOppositeTurnDuel = 6100, ClaimActualTurnDamageEntities = 6101,
    ResolveForeignTurnPindian = 6400, OfferSameTypeDifferentNameOrExtraTarget = 6401,
    ResolvePrepDiscardOrEnding = 6600, DrawPrepDiscardEnding = 6601,
    RequireTargetDiscardOrEquipmentRecast = 7340,
    OfferSlashTargetBenefit = 7000, SettleDodgeCancelledSlashBenefit = 7001,
    DiscardDrawAndOfferUniqueHpPeer = 7200, GiveAllHandAndStartRecipientPindian = 7201, UsePindianWinnerSlash = 7202,
    DiscardSlashThenOtherCardAndUseDuel = 7320,
    DrawThenNullifyOwnMultiTargetTrick = 7300, RestrictDamageSourceHandCategory = 7301,
    PlaceOwnedEquipmentThenResolveSlotBenefit = 7500,
    GiveBlackHandAndResolveRecipientContest = 8100, RaiseMaximumRecoverAndQualifyPrintedLord = 8101,
    PayHpInspectHandThenDiscardOrSlash = 7800,
    DelegateJudgmentReplacement = 8300, GiveAfterBatchGain = 8301, RevealRedLossAndDraw = 8302,
    DiscardEquipmentThenSlashAndOwnershipOutcome = 8700,
    DrawAfterActualOwnHandGain = 8800, DiscardForeignTurnHandGains = 8801, GiveSameCategoryFromDeck = 8802
}
public enum SkillProgramEffectTarget { Owner, Actor, SelectedTarget, SelectedTargets, HpPairHigher = 2000, HpPairLower = 2001 }
public enum SkillProgramTurnPhase { Judgment, Draw, Play, Discard }
public enum SkillProgramTriggerWindow
{
    CardUseTargetsFinalized,
    CardResponseAccepted,
    CardUseBeforeTargetEffects,
    CardUseCommitted,
    JudgmentReplacing,
    JudgmentFinalized,
    TurnStartBeforeNormalFlow,
    DrawPhaseStarting,
    AfterNormalDraw,
    SelfDyingResponse,
    BeforeDamageApplied,
    AfterDamageApplied,
    PlayEnding,
    DiscardPhaseStarting,
    DiscardPhaseEnded,
    TurnEnding,
    CardsMoved,
    OwnerDied,
    DyingResponse,
    CardUseCompleted,
    DamageAppliedBeforeDying,
    SlashTargetRedirecting,
    SlashBeforeResponse,
    SlashFullyDodged,
    PlayPhaseStarting,
    CardsGained,
    DiscardPileReceived,
    AfterHpLost,
    AfterHpRecovered,
    CharacterDied,
    FirstGameDomainCrossing = 1540,
    CardEffectBeforeApply = 1700,
    AfterTurnEnded = 1643,
    GameStarting = 400,
    DyingEntered,
    DyingExited,
    SkillsChanged = 500,
    AfterHealthChanged = 600, DyingEntering = 660,
    JudgmentPhaseStarting = 800,
    CharacterTurnedFaceUp = 820,
    CharacterTurnedOver = 2900,
    DrawPhaseSkipped = 3800,
    CharacterEnteredChain = 821, DrawPhaseEnded = 1140, ProgramTargetCommitted = 1260,
    OtherActualUseTargeted = 5600,
    OtherActualTurnStarted = 6400,
    ActualSlashTargetBenefit = 7000, SlashDodgeCancelledBenefit = 7001, ActualSlashTargetPenalty = 7340
}
public enum SkillProgramTriggerSubject { Owner, Any, Source, DamageSource, DamageTarget }
public enum SkillProgramMovementOccurrence { PerBatch, PerCard, PerSourceOwner = 700, PerOwnerBatch = 761, PerThirdPartyHandGain = 762, PerOwnerSourceHandGain = 763 }
public enum SkillProgramCardCountExpression { NextPhaseActivationOrdinal = 760 }
public enum SkillProgramHpChangeOccurrence { PerEvent, PerPoint }
public enum SkillProgramDamageOccurrence { PerDamage, PerDamagePoint }
public enum SkillProgramDrawPhaseMode { Additive, Replacement }
public enum SkillProgramOldJudgmentCardDestination { DiscardPile, OwnerHand }
public enum SkillProgramNumberExpression
{
    LivingFactionCount,
    TargetMaxHpMinusHandCount,
    OwnerLostHp,
    BoundCardCount,
    AllOwnedZoneCards,
    CurrentHandCount,
    IntegerConstant,
    PlannedNormalDrawCount,
    SelectedTargetCount,
    LivingPlayerCount,
    EventTargetHp,
    CurrentAttackRange = 11,
    HandHalfFloor = 12,
    SelectedPairHandDifference = 13,
    LivingPlayersMinHp = 14,
    HandLimitMinusHandCount = 15,
    LostHpMinusHandCount = 600,
    CategoryTargetTurnUsage = 820, CurrentHp = 1020, SelectedTargetsHandGreaterThanLord = 1021,
    PhaseSkillUsage = 1022, EventMovedCardCount = 1700, CurrentTurnUsedCardCategoryCount = 4600, OwnerLostHpAtLeastOne = 7400,
    TurnOwnerDiscardPhaseHandDiscardCount = 3100, CurrentHandEmptyTwoOtherwiseOne = 8008, OwnerMaxHp = 8600}
public enum SkillProgramCardSetVisibility { Private, Public }
public enum SkillProgramCardDestination
{
    OwnerHand, DiscardPile, SelectedTargetHand, OwnerPersistentZone, DrawPileBottom, PhaseOwnerHand,
    SelectedTargetEquipment,
    SelectedTargetCorrespondingZone,
    DrawPileTop
}
public enum SkillProgramCardSource { DamageSource, Owner, EventTarget = 2 }
public enum SkillProgramSubsetAiOrder { MostCardsThenRankSum }
public enum SkillProgramTargetAiOrder { Stable, HostileThenHandCount, SupportFirstThenOpposeSecond, SupportDraw, CardEffectIntervention = 4 }
public enum SkillProgramPhaseContinuation { BeforeNormalPreparation }
public enum SkillProgramCardTargetRestriction { SelfOnly, DistanceUnlimitedAgainstTarget, SlashCountUnlimitedAgainstTarget, IgnoreArmorAgainstTarget, ArmorIneffectiveForTurn = 400, NormalSlashTarget = 500 }
public enum SkillProgramCardColorRelation { OppositeBoundCard }
public enum SkillProgramStateVisibility { Public, Private }
public enum SkillProgramStateResetScope { Game, PlayPhase = 450, Turn = 1081 }
public enum SkillProgramStateReacquirePolicy { PreserveUntilGameEnd }
[Flags]
public enum DirectedTurnCardPolicyEffect
{
    None = 0,
    ForbidTarget = 1,
    IgnoreDistance = 2,
    BypassSlashLimit = 4,
    IgnoreArmor = 8
}

public sealed record SkillProgramBooleanStateDefinition(
    string Id,
    bool InitialValue,
    SkillProgramStateVisibility Visibility,
    SkillProgramStateResetScope ResetScope,
    SkillProgramStateReacquirePolicy ReacquirePolicy);

public sealed class SkillProgramCondition
{
    internal SkillProgramCondition(SkillProgramConditionKind kind, int value, IReadOnlyList<SkillProgramCondition> children,
        string? sourceBind = null, string? stateId = null, bool expectedValue = true, string? optionId = null,
        IReadOnlyList<SkillProgramCardCategory>? cardCategories = null,
        IReadOnlyList<CardKind>? cardKinds = null,
        IReadOnlyList<Suit>? suits = null,
        IReadOnlyList<CardZoneKind>? zones = null,
        string? choiceBind = null,
        GeneralGender? gender = null)
    {
        Kind = kind;
        Value = value;
        Children = children;
        SourceBind = sourceBind;
        StateId = stateId;
        ExpectedValue = expectedValue;
        OptionId = optionId;
        CardCategories = cardCategories ?? [];
        CardKinds = cardKinds ?? [];
        Suits = suits ?? [];
        Zones = zones ?? [];
        ChoiceBind = choiceBind;
        Gender = gender;
    }

    public SkillProgramConditionKind Kind { get; }
    public int Value { get; }
    public IReadOnlyList<SkillProgramCondition> Children { get; }
    public string? SourceBind { get; }
    public string? StateId { get; }
    public bool ExpectedValue { get; }

    public string? OptionId { get; }
    public IReadOnlyList<SkillProgramCardCategory> CardCategories { get; }
    public IReadOnlyList<CardKind> CardKinds { get; }
    public IReadOnlyList<Suit> Suits { get; }
    public IReadOnlyList<CardZoneKind> Zones { get; }
    public string? ChoiceBind { get; }
    public GeneralGender? Gender { get; }

    internal bool EvaluateOption(PlayerSkillContext context, Func<bool> hasClaimableDamageCards,
        Func<string, bool>? attackRangeCoverageDecreased = null,
        Func<string, int>? boundCardCount = null,
        Func<IReadOnlyList<CardZoneKind>, IReadOnlyList<SkillProgramCardCategory>, IReadOnlyList<CardKind>, bool>? hasOwnedCardCategory = null,
        int? activationCardCount = null,
        Func<string, string, bool>? boundCardSuitMatchesChoice = null,
        Func<string, IReadOnlyList<SkillProgramCardCategory>, bool>? boundCardsMatchCategories = null) => Kind switch
        {
            SkillProgramConditionKind.ActivationCardCountAtLeast => activationCardCount >= Value,
            SkillProgramConditionKind.BoundCardSuitMatchesChoice =>
                boundCardSuitMatchesChoice?.Invoke(SourceBind!, ChoiceBind!) == ExpectedValue,
            SkillProgramConditionKind.BoundCardsMatchCategories =>
                boundCardsMatchCategories?.Invoke(SourceBind!, CardCategories) == true,
            SkillProgramConditionKind.HasClaimableDamageCards => hasClaimableDamageCards(),
            SkillProgramConditionKind.BoundCardCountAtLeast => boundCardCount?.Invoke(SourceBind!) >= Value,
            SkillProgramConditionKind.AttackRangeCoverageDecreased =>
                attackRangeCoverageDecreased?.Invoke(SourceBind!) == true,
            SkillProgramConditionKind.HasOwnedCardCategory =>
                hasOwnedCardCategory?.Invoke(Zones, CardCategories, CardKinds) == true,
            SkillProgramConditionKind.All => Children.All(child => child.EvaluateOption(context, hasClaimableDamageCards,
                attackRangeCoverageDecreased, boundCardCount: boundCardCount, hasOwnedCardCategory: hasOwnedCardCategory,
                activationCardCount: activationCardCount,
                boundCardSuitMatchesChoice: boundCardSuitMatchesChoice,
                boundCardsMatchCategories: boundCardsMatchCategories)),
            SkillProgramConditionKind.Any => Children.Any(child => child.EvaluateOption(context, hasClaimableDamageCards,
                attackRangeCoverageDecreased, boundCardCount: boundCardCount, hasOwnedCardCategory: hasOwnedCardCategory,
                activationCardCount: activationCardCount,
                boundCardSuitMatchesChoice: boundCardSuitMatchesChoice,
                boundCardsMatchCategories: boundCardsMatchCategories)),
            SkillProgramConditionKind.Not => !Children[0].EvaluateOption(context, hasClaimableDamageCards,
                attackRangeCoverageDecreased, boundCardCount: boundCardCount, hasOwnedCardCategory: hasOwnedCardCategory,
                activationCardCount: activationCardCount,
                boundCardSuitMatchesChoice: boundCardSuitMatchesChoice,
                boundCardsMatchCategories: boundCardsMatchCategories),
            _ => Evaluate(context)
        };

    internal bool ContainsHasClaimableDamageCards() => Kind == SkillProgramConditionKind.HasClaimableDamageCards ||
        Children.Any(child => child.ContainsHasClaimableDamageCards());
    internal bool ContainsPreviousPlayCardIsBasic() => Kind == SkillProgramConditionKind.PreviousPlayCardIsBasic ||
        Children.Any(child => child.ContainsPreviousPlayCardIsBasic());

    internal bool ContainsBoundCardCountAtLeast() => Kind == SkillProgramConditionKind.BoundCardCountAtLeast ||
        Children.Any(child => child.ContainsBoundCardCountAtLeast());

    internal bool ContainsHasOwnedCardCategory() => Kind == SkillProgramConditionKind.HasOwnedCardCategory ||
        Children.Any(child => child.ContainsHasOwnedCardCategory());

    internal bool ContainsBoundCardsMatchCategories() => Kind == SkillProgramConditionKind.BoundCardsMatchCategories ||
        Children.Any(child => child.ContainsBoundCardsMatchCategories());

    internal bool CanEvaluateWithoutProgramFrame() => Kind switch
    {
        SkillProgramConditionKind.Always or SkillProgramConditionKind.OwnTurn or
            SkillProgramConditionKind.NotOwnTurn or SkillProgramConditionKind.Wounded or
            SkillProgramConditionKind.HpAtLeast or SkillProgramConditionKind.HandCountAtLeast or
            SkillProgramConditionKind.FaceDown or SkillProgramConditionKind.Chained or
            SkillProgramConditionKind.ClassicIdentityMode or SkillProgramConditionKind.RuntimeBooleanState or
            SkillProgramConditionKind.PublicCounterAtLeast or SkillProgramConditionKind.PublicCounterOdd or SkillProgramConditionKind.HandCountGreaterThanHp or SkillProgramConditionKind.PositiveHandLimit or SkillProgramConditionKind.HasUsableHandCard => true,
        SkillProgramConditionKind.All or SkillProgramConditionKind.Any or SkillProgramConditionKind.Not =>
            Children.All(child => child.CanEvaluateWithoutProgramFrame()),
        _ => false
    };

    public bool Evaluate(PlayerSkillContext context) => Evaluate(context, null);

    internal bool Evaluate(PlayerSkillContext context, bool? cardUseIsRed,
        bool? eventTargetIsFemale = null, bool? eventSourceIsFemale = null) => Kind switch
        {
            SkillProgramConditionKind.Always => true,
            SkillProgramConditionKind.OwnTurn => context.IsOwnTurn,
            SkillProgramConditionKind.NotOwnTurn => !context.IsOwnTurn,
            SkillProgramConditionKind.Wounded => context.Hp < context.MaxHp,
            SkillProgramConditionKind.FaceDown => context.IsFaceDown,
            SkillProgramConditionKind.Chained => context.IsChained,
            SkillProgramConditionKind.ClassicIdentityMode => context.IsClassicIdentityMode,
            SkillProgramConditionKind.RuntimeBooleanState => context.RuntimeBooleanStates?.GetValueOrDefault(StateId!) == ExpectedValue,
            SkillProgramConditionKind.PublicCounterAtLeast => context.PublicCounters?.GetValueOrDefault(StateId!) >= Value,
            SkillProgramConditionKind.PublicCounterOdd => (context.PublicCounters?.GetValueOrDefault(StateId!) % 2 == 1) == ExpectedValue,
            SkillProgramConditionKind.HpAtLeast => context.Hp >= Value,
            SkillProgramConditionKind.HandCountAtLeast => context.HandCount >= Value,
            SkillProgramConditionKind.HandCountGreaterThanHp => context.HandCount > context.Hp,
            SkillProgramConditionKind.PositiveHandLimit => context.HandLimit > 0,
            SkillProgramConditionKind.HasUsableHandCard => context.HasUsableHandCard == true,
            SkillProgramConditionKind.CardUseIsRed => cardUseIsRed == true,
            SkillProgramConditionKind.PreviousPlayCardIsBasic => context.PreviousPlayCardIsBasic == true,
            SkillProgramConditionKind.EventTargetGenderIs => eventTargetIsFemale == (Gender == GeneralGender.Female),
            SkillProgramConditionKind.EventSourceGenderIs => eventSourceIsFemale == (Gender == GeneralGender.Female),
            SkillProgramConditionKind.PindianWon or SkillProgramConditionKind.BooleanState or
                SkillProgramConditionKind.ChoiceIs or SkillProgramConditionKind.BoundCardsSameColor or
                SkillProgramConditionKind.BoundCardsMatchCategories or SkillProgramConditionKind.BoundCardsMatchKinds or
                SkillProgramConditionKind.BoundCardsMatchSuits or
                SkillProgramConditionKind.HasClaimableDamageCards or SkillProgramConditionKind.AttackRangeCoverageDecreased or
                SkillProgramConditionKind.BoundCardCountAtLeast or SkillProgramConditionKind.HasOwnedCardCategory or
                SkillProgramConditionKind.ActivationCardCountAtLeast or
                SkillProgramConditionKind.BoundCardSuitMatchesChoice or
                SkillProgramConditionKind.BoundCardCategoryMatchesCardAction or
                SkillProgramConditionKind.RequestedSlashDamagedOwner =>
                throw new InvalidOperationException($"Condition '{Kind}' requires a running program frame."),
            SkillProgramConditionKind.All => Children.All(child => child.Evaluate(context, cardUseIsRed,
                eventTargetIsFemale, eventSourceIsFemale)),
            SkillProgramConditionKind.Any => Children.Any(child => child.Evaluate(context, cardUseIsRed,
                eventTargetIsFemale, eventSourceIsFemale)),
            SkillProgramConditionKind.Not => !Children[0].Evaluate(context, cardUseIsRed,
                eventTargetIsFemale, eventSourceIsFemale),
            _ => throw new InvalidOperationException($"Unsupported condition kind '{Kind}'.")
        };

    internal bool Evaluate(
        PlayerSkillContext context,
        Func<string, bool> pindianWon,
        Func<string, bool> booleanState,
        bool? cardUseIsRed = null) =>
        Evaluate(context, null, pindianWon, booleanState, cardUseIsRed);

    internal bool Evaluate(
        PlayerSkillContext context,
        PlayerSkillContext? selectedTarget,
        Func<string, bool> pindianWon,
        Func<string, bool> booleanState,
        bool? cardUseIsRed = null,
        Func<string, string?>? choiceResult = null,
        Func<string, bool>? boundCardsSameColor = null,
        Func<string, IReadOnlyList<SkillProgramCardCategory>, bool>? boundCardsMatchCategories = null,
        Func<string, IReadOnlyList<CardKind>, bool>? boundCardsMatchKinds = null,
        Func<string, bool>? attackRangeCoverageDecreased = null,
        Func<string, int>? boundCardCount = null,
        int? activationCardCount = null,
        Func<string, string, bool>? boundCardSuitMatchesChoice = null,
        Func<string, IReadOnlyList<Suit>, bool>? boundCardsMatchSuits = null,
        Func<string, SkillProgramCardCategory?, bool>? boundCardCategoryMatchesCardAction = null,
        bool? eventTargetIsFemale = null,
        bool? eventSourceIsFemale = null,
        bool? requestedSlashDamagedOwner = null) => Kind switch
        {
            SkillProgramConditionKind.RequestedSlashDamagedOwner => requestedSlashDamagedOwner == true,
            SkillProgramConditionKind.ActivationCardCountAtLeast => activationCardCount >= Value,
            SkillProgramConditionKind.BoundCardSuitMatchesChoice =>
                boundCardSuitMatchesChoice?.Invoke(SourceBind!, ChoiceBind!) == ExpectedValue,
            SkillProgramConditionKind.BoundCardCountAtLeast => boundCardCount?.Invoke(SourceBind!) >= Value,
            SkillProgramConditionKind.ChoiceIs => choiceResult?.Invoke(SourceBind!) == OptionId,
            SkillProgramConditionKind.BoundCardsSameColor => boundCardsSameColor?.Invoke(SourceBind!) == true,
            SkillProgramConditionKind.BoundCardsMatchCategories =>
                boundCardsMatchCategories?.Invoke(SourceBind!, CardCategories) == true,
            SkillProgramConditionKind.BoundCardsMatchKinds =>
                boundCardsMatchKinds?.Invoke(SourceBind!, CardKinds) == true,
            SkillProgramConditionKind.BoundCardsMatchSuits =>
                boundCardsMatchSuits?.Invoke(SourceBind!, Suits) == true,
            SkillProgramConditionKind.BoundCardCategoryMatchesCardAction =>
                boundCardCategoryMatchesCardAction?.Invoke(SourceBind!, null) == true,
            SkillProgramConditionKind.AttackRangeCoverageDecreased =>
                attackRangeCoverageDecreased?.Invoke(SourceBind!) == true,
            SkillProgramConditionKind.PindianWon => pindianWon(SourceBind!),
            SkillProgramConditionKind.PindianNotWon => !pindianWon(SourceBind!),
            SkillProgramConditionKind.BooleanState => booleanState(StateId!) == ExpectedValue,
            SkillProgramConditionKind.SelectedTargetWounded => selectedTarget is not null && selectedTarget.Hp < selectedTarget.MaxHp,
            SkillProgramConditionKind.SelectedTargetIsOther =>
                selectedTarget is not null && selectedTarget.Seat != context.Seat,
            SkillProgramConditionKind.SelectedTargetHandGreaterThanOwner =>
                selectedTarget is not null && selectedTarget.HandCount > context.HandCount,
            SkillProgramConditionKind.All => Children.All(child => child.Evaluate(context, selectedTarget, pindianWon, booleanState, cardUseIsRed, choiceResult, boundCardsSameColor, boundCardsMatchCategories, boundCardsMatchKinds, attackRangeCoverageDecreased, boundCardCount, activationCardCount, boundCardSuitMatchesChoice, boundCardsMatchSuits, boundCardCategoryMatchesCardAction, eventTargetIsFemale, eventSourceIsFemale, requestedSlashDamagedOwner)),
            SkillProgramConditionKind.Any => Children.Any(child => child.Evaluate(context, selectedTarget, pindianWon, booleanState, cardUseIsRed, choiceResult, boundCardsSameColor, boundCardsMatchCategories, boundCardsMatchKinds, attackRangeCoverageDecreased, boundCardCount, activationCardCount, boundCardSuitMatchesChoice, boundCardsMatchSuits, boundCardCategoryMatchesCardAction, eventTargetIsFemale, eventSourceIsFemale, requestedSlashDamagedOwner)),
            SkillProgramConditionKind.Not => !Children[0].Evaluate(context, selectedTarget, pindianWon, booleanState, cardUseIsRed, choiceResult, boundCardsSameColor, boundCardsMatchCategories, boundCardsMatchKinds, attackRangeCoverageDecreased, boundCardCount, activationCardCount, boundCardSuitMatchesChoice, boundCardsMatchSuits, boundCardCategoryMatchesCardAction, eventTargetIsFemale, eventSourceIsFemale, requestedSlashDamagedOwner),
            _ => Evaluate(context, cardUseIsRed, eventTargetIsFemale, eventSourceIsFemale)
        };
}

public sealed record SkillProgramTriggerFacts(
    int CardsUsedThisTurn,
    int CurrentHp,
    bool IsClassicIdentityMode,
    int MovedCardCount = 0,
    int SourceZoneCountBefore = 0,
    int SourceZoneCountAfter = 0,
    int CurrentMaxHp = 0,
    IReadOnlyDictionary<string, bool>? BooleanStates = null,
    bool? CardActionActorIsCurrentTurn = null,
    bool? CardActionPhaseIsPlay = null,
    bool? CardUseCausedDamage = null,
    IReadOnlyList<string>? CardUseConversionSkillIds = null,
    int CurrentHandCount = 0,
    string? LordGeneralId = null,
    SkillProgramOwnedZoneCounts OwnedZoneCounts = default,
    int EventTargetHp = 0,
    bool? CardActionActorIsOwner = null,
    bool? OtherDamageParticipantAlive = null,
    int CardUseDesignatedTargetCount = 0,
    bool? DirectCardUseDamage = null,
    bool? DamageCardIsRed = null,
    int? SourceToTargetDistanceAtDamage = null,
    int EventTargetMaxHp = 0,
    bool? DamageTargetIsOther = null,
    int EventTargetHandCount = 0,
    int CurrentAttackRange = 0,
    SkillProgramCardCategory? CardActionCategory = null,
    bool? CardActionTargetIsOwner = null,
    int PlayPhaseKillCountByTurnOwner = 0,
    int PlayPhaseDamageDealtByTurnOwner = 0,
    int OwnerEventTargetDistance = int.MaxValue,
    int DestinationZoneCountBefore = 0,
    int DestinationZoneCountAfter = 0,
    int HpChangeAmount = 0,
    int HpBeforeChange = 0,
    int HpAfterChange = 0,
    bool? DeathKillerIsOwner = null,
    IReadOnlyDictionary<PlayerMarkerKind, int>? MarkerCounts = null,
    int DeathVictimCleanupCardCount = 0,
    bool? CardActionCardIsRed = null,
    bool? DamageCardIsSlash = null,
    int CardsUsedOrRespondedThisTurn = 0,
    bool? OwnerIsTurnPlayer = null,
    bool? CardActionFromOwnerHand = null,
    int LivingPlayersMinHp = 0,
    bool? DamageSourceIsOwner = null,
    string? DamageSourceFactionId = null,
    int TurnOwnerDiscardPhaseHandDiscardCount = 0,
    bool OwnerIsFaceDown = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    bool? CardActionMatchesPreviousPlayCard = null,
    bool? PreviousPlayCardIsBasic = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    int? DamageInstancesTakenThisTurn = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    int LivingWoundedCount = 0,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<PlayerMarkerKind, int>? GlobalMarkerCounts = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    bool? OwnerKilledThisTurn = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    int? CardActionPhysicalCardCount = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? CardActionHandCardCount = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] bool? CardActionCardIsBlack = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] GeneralGender? DamageSourceGender = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? UnfulfilledPhaseColorRestrictionInstances = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] int PlayPhaseDamageTakenByAny = 0,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] Suit? CardActionSuit = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] bool? DeathExtinguishedFaction = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] bool? DiscardPhaseSuitsAllDistinct = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] bool? OtherDamageSourceAlive = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? BlockedDamageSourceSkills = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<int>? LowHandPopulationSeats = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<PlayerMarkerKind,int>? EventTargetMarkerCounts = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<PlayerMarkerKind,int>? EventSourceMarkerCounts = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? CurrentAvailableEquipmentSlotCount = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, int>? PublicPersistentPileCounts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TurnDiscardSuitMask = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TurnOwnerDamageDealtThisTurn = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TurnOwnerSlashUseCount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Suit? DamageCardSuit = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? PhaseIsPlay = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? CardActionOpponentIsOwner = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CardMovementTiming? MovementTiming { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? EventTargetDamageInstancesTakenThisTurn { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CurrentTurnUsedCardCategoryCount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? OwnerTrickUsesThisActualTurn { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CurrentActualPlayPhysicalSlashLossCount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int MovedEquipmentCardCount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FrozenPreviousLivingSeat { get; init; }

    public bool GetBooleanState(string skillId, string skillInstanceId, string stateId) =>
        BooleanStates?.GetValueOrDefault(BooleanStateKey(skillId, skillInstanceId, stateId)) ??
        throw new InvalidOperationException("The frozen trigger facts do not contain the requested boolean state.");

    public static string BooleanStateKey(string skillId, string skillInstanceId, string stateId) =>
        $"{skillId}\u001f{skillInstanceId}\u001f{stateId}";
}

public readonly record struct SkillProgramOwnedZoneCounts(
    int WoodenOxGrain, int BuquWound, int Authority, int Chunlao,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] int PublicPersistentPile = 0)
{
    public int Get(CardZoneKind zone) => zone switch
    {
        CardZoneKind.WoodenOxGrain => WoodenOxGrain,
        CardZoneKind.BuquWound => BuquWound,
        CardZoneKind.Authority => Authority,
        CardZoneKind.Chunlao => Chunlao,
        CardZoneKind.PublicPersistentPile => PublicPersistentPile,
        _ => 0
    };
}

public sealed record SkillProgramTriggerValue(SkillProgramTriggerValueKind Kind, int Value, CardZoneKind? Zone = null,
    PlayerMarkerKind? Marker = null)
{
    public int Resolve(SkillProgramTriggerFacts facts) => Kind switch
    {
        SkillProgramTriggerValueKind.IntegerConstant => Value,
        SkillProgramTriggerValueKind.CurrentActualPlayPhysicalSlashLossCount => facts.CurrentActualPlayPhysicalSlashLossCount ??
            throw new InvalidOperationException("Actual Play physical Slash loss facts were not captured."),
        SkillProgramTriggerValueKind.TurnOwnerDamageDealtThisTurn => facts.TurnOwnerDamageDealtThisTurn ??
            throw new InvalidOperationException("Ending-turn damage facts were not captured."),
        SkillProgramTriggerValueKind.OwnerTrickUsesThisActualTurn => facts.OwnerTrickUsesThisActualTurn ??
            throw new InvalidOperationException("Actual-turn trick usage was not captured."),
        SkillProgramTriggerValueKind.CardsUsedThisTurn => facts.CardsUsedThisTurn,
        SkillProgramTriggerValueKind.CardsUsedOrRespondedThisTurn => facts.CardsUsedOrRespondedThisTurn,
        SkillProgramTriggerValueKind.DamageInstancesTakenThisTurn => facts.DamageInstancesTakenThisTurn ?? 0,
        SkillProgramTriggerValueKind.EventTargetDamageInstancesTakenThisTurn => facts.EventTargetDamageInstancesTakenThisTurn ??
            throw new InvalidOperationException("Actual damage-target occurrence facts were not captured."),
        SkillProgramTriggerValueKind.CardActionPhysicalCardCount => facts.CardActionPhysicalCardCount ?? 0,
        SkillProgramTriggerValueKind.CardActionHandCardCount => facts.CardActionHandCardCount ?? 0,
        SkillProgramTriggerValueKind.MarkerParity => facts.MarkerCounts?.GetValueOrDefault(Marker!.Value) % 2 ?? 0,
        SkillProgramTriggerValueKind.LivingWoundedCount => facts.LivingWoundedCount,
        SkillProgramTriggerValueKind.GlobalMarkerCount => facts.GlobalMarkerCounts?.GetValueOrDefault(Marker!.Value) ?? 0,
        SkillProgramTriggerValueKind.CurrentAvailableEquipmentSlotCount => facts.CurrentAvailableEquipmentSlotCount ?? throw new InvalidOperationException("Equipment slot facts were not captured."),
        SkillProgramTriggerValueKind.CurrentHp => facts.CurrentHp,
        SkillProgramTriggerValueKind.LivingPlayersMinHp => facts.LivingPlayersMinHp,
        SkillProgramTriggerValueKind.TurnOwnerDiscardPhaseHandDiscardCount =>
            facts.TurnOwnerDiscardPhaseHandDiscardCount,
        SkillProgramTriggerValueKind.CurrentMaxHp => facts.CurrentMaxHp,
        SkillProgramTriggerValueKind.OwnerLostHp => Math.Max(0, facts.CurrentMaxHp - Math.Max(0, facts.CurrentHp)),
        SkillProgramTriggerValueKind.CurrentHandCountMinusHp => facts.CurrentHandCount - facts.CurrentHp,
        SkillProgramTriggerValueKind.CurrentHandCount => facts.CurrentHandCount,
        SkillProgramTriggerValueKind.CurrentOwnedZoneCount => facts.OwnedZoneCounts.Get(Zone!.Value),
        SkillProgramTriggerValueKind.DestinationZoneCountBefore => facts.DestinationZoneCountBefore,
        SkillProgramTriggerValueKind.DestinationZoneCountAfter => facts.DestinationZoneCountAfter,
        SkillProgramTriggerValueKind.HpChangeAmount => facts.HpChangeAmount,
        SkillProgramTriggerValueKind.HpBeforeChange => facts.HpBeforeChange,
        SkillProgramTriggerValueKind.HpAfterChange => facts.HpAfterChange,
        SkillProgramTriggerValueKind.MovedCardCount => facts.MovedCardCount,
        SkillProgramTriggerValueKind.MovedEquipmentCardCount => facts.MovedEquipmentCardCount,
        SkillProgramTriggerValueKind.SourceZoneCountBefore => facts.SourceZoneCountBefore,
        SkillProgramTriggerValueKind.SourceZoneCountAfter => facts.SourceZoneCountAfter,
        SkillProgramTriggerValueKind.EventTargetHp => facts.EventTargetHp,
        SkillProgramTriggerValueKind.EventTargetMaxHp => facts.EventTargetMaxHp,
        SkillProgramTriggerValueKind.SourceToTargetDistanceAtDamage =>
            facts.SourceToTargetDistanceAtDamage ?? int.MaxValue,
        SkillProgramTriggerValueKind.CardUseDesignatedTargetCount => facts.CardUseDesignatedTargetCount,
        SkillProgramTriggerValueKind.EventTargetHandCount => facts.EventTargetHandCount,
        SkillProgramTriggerValueKind.CurrentAttackRange => facts.CurrentAttackRange,
        SkillProgramTriggerValueKind.PlayPhaseKillCountByTurnOwner => facts.PlayPhaseKillCountByTurnOwner,
        SkillProgramTriggerValueKind.PlayPhaseDamageDealtByTurnOwner => facts.PlayPhaseDamageDealtByTurnOwner,
        SkillProgramTriggerValueKind.PlayPhaseDamageTakenByAny => facts.PlayPhaseDamageTakenByAny,
        SkillProgramTriggerValueKind.OwnerEventTargetDistance => facts.OwnerEventTargetDistance,
        SkillProgramTriggerValueKind.EventTargetMarkerCount => facts.EventTargetMarkerCounts?.GetValueOrDefault(Marker!.Value) ?? 0,
        SkillProgramTriggerValueKind.EventSourceMarkerCount => facts.EventSourceMarkerCounts?.GetValueOrDefault(Marker!.Value) ?? 0,
        SkillProgramTriggerValueKind.TurnOwnerSlashUseCount => facts.TurnOwnerSlashUseCount ??
            throw new InvalidOperationException("Turn-owner slash use facts were not captured."),
        SkillProgramTriggerValueKind.OwnerAttributedMarkerCount => Marker is { } marker &&
            facts.MarkerCounts is { } counts && counts.TryGetValue(marker, out var markerCount) ? markerCount : 0,
        _ => throw new InvalidOperationException($"Unsupported trigger value kind '{Kind}'.")
    };
}

public sealed class SkillProgramTriggerCondition
{
    internal SkillProgramTriggerCondition(
        SkillProgramTriggerConditionKind kind,
        IReadOnlyList<SkillProgramTriggerCondition> children,
        SkillProgramTriggerValue? left = null,
        SkillProgramComparisonOperator? comparison = null,
        SkillProgramTriggerValue? right = null,
        string? stateId = null,
        bool expectedValue = true,
        IReadOnlyList<string>? generalIds = null,
        string? conversionSkillId = null,
        IReadOnlyList<SkillProgramCardCategory>? cardCategories = null,
        IReadOnlyList<string>? factions = null, GeneralGender? gender = null, IReadOnlyList<Suit>? suits = null) =>
        (Kind, Children, Left, Comparison, Right, StateId, ExpectedValue, GeneralIds, ConversionSkillId,
            CardCategories, Factions, Gender, Suits) =
        (kind, children, left, comparison, right, stateId, expectedValue,
            generalIds ?? Array.Empty<string>(), conversionSkillId,
            cardCategories ?? Array.Empty<SkillProgramCardCategory>(),
            factions ?? Array.Empty<string>(), gender, suits ?? Array.Empty<Suit>());

    public SkillProgramTriggerConditionKind Kind { get; }
    public IReadOnlyList<SkillProgramTriggerCondition> Children { get; }
    public SkillProgramTriggerValue? Left { get; }
    public SkillProgramComparisonOperator? Comparison { get; }
    public SkillProgramTriggerValue? Right { get; }
    public string? StateId { get; }
    public bool ExpectedValue { get; }
    public IReadOnlyList<string> GeneralIds { get; }
    public string? ConversionSkillId { get; }
    public IReadOnlyList<SkillProgramCardCategory> CardCategories { get; }
    public IReadOnlyList<string> Factions { get; }
    public GeneralGender? Gender { get; }
    public IReadOnlyList<Suit> Suits { get; }

    public bool Evaluate(SkillProgramTriggerFacts facts, string? skillId = null, string? skillInstanceId = null)
    {
        if (skillId is not null && skillInstanceId is not null && facts.PublicPersistentPileCounts is { } counts)
        {
            var key = $"{skillId.Length}:{skillId}{skillInstanceId.Length}:{skillInstanceId}";
            facts = facts with { OwnedZoneCounts = facts.OwnedZoneCounts with { PublicPersistentPile = counts.GetValueOrDefault(key) } };
        }
        return EvaluateCore(facts, skillId, skillInstanceId);
    }
    private bool EvaluateCore(SkillProgramTriggerFacts facts, string? skillId, string? skillInstanceId) => Kind switch
    {
        SkillProgramTriggerConditionKind.Always => true,
        SkillProgramTriggerConditionKind.Compare => Compare(facts),
        SkillProgramTriggerConditionKind.ClassicIdentityMode => facts.IsClassicIdentityMode,
        SkillProgramTriggerConditionKind.BooleanState when skillId is not null && skillInstanceId is not null =>
            facts.GetBooleanState(skillId, skillInstanceId, StateId!) == ExpectedValue,
        SkillProgramTriggerConditionKind.BooleanState =>
            throw new InvalidOperationException("A trigger boolean-state condition requires its skill instance."),
        SkillProgramTriggerConditionKind.CardActionActorIsCurrentTurn =>
            facts.CardActionActorIsCurrentTurn == true,
        SkillProgramTriggerConditionKind.CardActionActorIsOwner =>
            facts.CardActionActorIsOwner == true,
        SkillProgramTriggerConditionKind.CardActionPhaseIsPlay => facts.CardActionPhaseIsPlay == true,
        SkillProgramTriggerConditionKind.CardActionMatchesPreviousPlayCard => facts.CardActionMatchesPreviousPlayCard == true,
        SkillProgramTriggerConditionKind.CardUseCausedDamage => facts.CardUseCausedDamage == true,
        SkillProgramTriggerConditionKind.CardUseConversionSkillIs =>
            facts.CardUseConversionSkillIds?.Contains(ConversionSkillId!, StringComparer.Ordinal) == true,
        SkillProgramTriggerConditionKind.OtherDamageParticipantAlive =>
            facts.OtherDamageParticipantAlive == true,
        SkillProgramTriggerConditionKind.DirectCardUseDamage => facts.DirectCardUseDamage == true,
        SkillProgramTriggerConditionKind.DamageCardIsRed => facts.DamageCardIsRed == true,
        SkillProgramTriggerConditionKind.DamageTargetIsOther => facts.DamageTargetIsOther == true,
        SkillProgramTriggerConditionKind.CardActionCategoryIs =>
            facts.CardActionCategory is { } category && CardCategories.Contains(category),
        SkillProgramTriggerConditionKind.CardActionTargetIsOwner => facts.CardActionTargetIsOwner == true,
        SkillProgramTriggerConditionKind.CardActionCardIsRed => facts.CardActionCardIsRed == true,
        SkillProgramTriggerConditionKind.HasUnfulfilledPlayPhaseColorRestriction => skillInstanceId is not null &&
            facts.UnfulfilledPhaseColorRestrictionInstances?.Contains(skillInstanceId, StringComparer.Ordinal) == true,
        SkillProgramTriggerConditionKind.CardActionCardIsBlack => facts.CardActionCardIsBlack == true,
        SkillProgramTriggerConditionKind.CardActionSuitIs => facts.CardActionSuit is { } suit && Suits.Contains(suit),
        SkillProgramTriggerConditionKind.DamageSourceGenderIs => facts.DamageSourceGender is { } gender && gender == Gender,
        SkillProgramTriggerConditionKind.DamageCardIsSlash => facts.DamageCardIsSlash == true,
        SkillProgramTriggerConditionKind.DeathKillerIsOwner => facts.DeathKillerIsOwner == true,
        SkillProgramTriggerConditionKind.DeathExtinguishedFaction => facts.DeathExtinguishedFaction == true,
        SkillProgramTriggerConditionKind.TurnDiscardIncludesAllSuits => facts.TurnDiscardSuitMask == 15,
        SkillProgramTriggerConditionKind.DiscardPhaseSuitsAllDistinct => facts.DiscardPhaseSuitsAllDistinct == true,
        SkillProgramTriggerConditionKind.OtherDamageSourceAlive => facts.OtherDamageSourceAlive == true,
        SkillProgramTriggerConditionKind.DamageSourcePairUnused => skillId is not null && facts.OtherDamageSourceAlive == true && facts.BlockedDamageSourceSkills?.Contains(skillId) != true,
        SkillProgramTriggerConditionKind.OwnerKilledThisTurn => facts.OwnerKilledThisTurn == true,
        SkillProgramTriggerConditionKind.DeathVictimHasCards => facts.DeathVictimCleanupCardCount > 0,
        SkillProgramTriggerConditionKind.OwnerIsTurnPlayer => facts.OwnerIsTurnPlayer == true,
        SkillProgramTriggerConditionKind.PhaseIsPlay => facts.PhaseIsPlay == true,
        SkillProgramTriggerConditionKind.CardActionOpponentIsOwner => facts.CardActionOpponentIsOwner == true,
        SkillProgramTriggerConditionKind.CardActionFromOwnerHand => facts.CardActionFromOwnerHand == true,
        SkillProgramTriggerConditionKind.DamageSourceIsOwner => facts.DamageSourceIsOwner == true,
        SkillProgramTriggerConditionKind.PreviousPlayCardIsBasic => facts.PreviousPlayCardIsBasic == true,
        SkillProgramTriggerConditionKind.DamageSourceFactionIs => facts.DamageSourceFactionId is { } faction &&
            Factions.Contains(faction, StringComparer.Ordinal),
        SkillProgramTriggerConditionKind.FaceDown => facts.OwnerIsFaceDown,
        SkillProgramTriggerConditionKind.LordGeneralNotIn =>
            facts.LordGeneralId is null || !GeneralIds.Contains(facts.LordGeneralId, StringComparer.Ordinal),
        SkillProgramTriggerConditionKind.All => Children.All(child => child.Evaluate(facts, skillId, skillInstanceId)),
        SkillProgramTriggerConditionKind.Any => Children.Any(child => child.Evaluate(facts, skillId, skillInstanceId)),
        SkillProgramTriggerConditionKind.Not => !Children[0].Evaluate(facts, skillId, skillInstanceId),
        _ => throw new InvalidOperationException($"Unsupported trigger condition kind '{Kind}'.")
    };

    private bool Compare(SkillProgramTriggerFacts facts)
    {
        var left = Left!.Resolve(facts);
        var right = Right!.Resolve(facts);
        return Comparison switch
        {
            SkillProgramComparisonOperator.Equal => left == right,
            SkillProgramComparisonOperator.NotEqual => left != right,
            SkillProgramComparisonOperator.LessThan => left < right,
            SkillProgramComparisonOperator.LessThanOrEqual => left <= right,
            SkillProgramComparisonOperator.GreaterThan => left > right,
            SkillProgramComparisonOperator.GreaterThanOrEqual => left >= right,
            _ => throw new InvalidOperationException("The trigger comparison operator is unavailable.")
        };
    }
}

public sealed class SkillProgramModifier
{
    internal SkillProgramModifier(
        string id,
        SkillRuleQuery query,
        SkillRuleOperation operation,
        int value,
        SkillRuleValueExpression? valueExpression,
        CardZoneKind? valueZone,
        int priority,
        string? sourceCardIdentityId,
        IReadOnlyList<CardKind>? cardKinds,
        SkillProgramCondition condition) =>
        (Id, Query, Operation, Value, ValueExpression, ValueZone, Priority, SourceCardIdentityId, CardKinds, Condition) =
        (id, query, operation, value, valueExpression, valueZone, priority, sourceCardIdentityId,
            cardKinds ?? Array.Empty<CardKind>(), condition);
    public string Id { get; }
    public SkillRuleQuery Query { get; }
    public SkillRuleOperation Operation { get; }
    public int Value { get; }
    public SkillRuleValueExpression? ValueExpression { get; }
    public CardZoneKind? ValueZone { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlayerMarkerKind? ValueMarker { get; internal init; }
    [JsonIgnore]
    public IReadOnlyList<SkillRuleQueryDependency> QueryDependencies => ValueExpression == SkillRuleValueExpression.OwnerMarkerCount
        ? [SkillRuleQueryDependency.MarkerState] : [];
    public int Priority { get; }
    public string? SourceCardIdentityId { get; }
    public IReadOnlyList<CardKind> CardKinds { get; }
    public SkillProgramCondition Condition { get; }

    public int EvaluateValue(SkillProgramRuleContext context) => ValueExpression switch
    {
        null => Value,
        SkillRuleValueExpression.OwnerMarkerCount when ValueMarker is { } marker && context.OwnerMarkerCount is { } count => Math.Max(0, count(marker)),
        SkillRuleValueExpression.LivingFactionCount when context.LivingFactionCount >= 0 => context.LivingFactionCount,
        SkillRuleValueExpression.LivingFactionCount => throw new ArgumentOutOfRangeException(
            nameof(context), context.LivingFactionCount, "Living faction count cannot be negative."),
        SkillRuleValueExpression.PublicLivingFactionCount => context.PublicLivingFactionCount is >= 0 ? context.PublicLivingFactionCount.Value :
            throw new InvalidOperationException("Public living factions were not captured."),
        SkillRuleValueExpression.CurrentTurnUsedHandSuitCount => context.CurrentTurnUsedHandSuitCount ??
            throw new InvalidOperationException("Actual turn hand-suit facts were not captured."),
        SkillRuleValueExpression.OwnerLostHp => Math.Max(0, context.Owner.MaxHp - context.Owner.Hp),
        SkillRuleValueExpression.NegatedOwnerLostHp => -Math.Max(0, context.Owner.MaxHp - context.Owner.Hp),
        SkillRuleValueExpression.OwnedZoneCount when ValueZone is { } zone && context.OwnedZoneCount is not null =>
            context.OwnedZoneCount(zone),
        SkillRuleValueExpression.NegatedOwnedZoneCount when ValueZone is { } zone && context.OwnedZoneCount is not null =>
            -context.OwnedZoneCount(zone),
        _ => throw new InvalidOperationException($"Unsupported rule value expression '{ValueExpression}'.")
    };
}

/// <summary>
/// A mandatory identity projected onto one physical card while it remains in
/// one of the configured owner zones. Unlike viewAs, this is not an optional
/// player action: native uses and responses of the physical card are replaced.
/// </summary>
public sealed class SkillProgramCardIdentity
{
    internal SkillProgramCardIdentity(string id, IReadOnlyList<CardZoneKind> zones,
        IReadOnlyList<CardKind> inputKinds, IReadOnlyList<Suit> inputSuits,
        CardKind outputKind, SkillProgramCondition condition) =>
        (Id, Zones, InputKinds, InputSuits, OutputKind, Condition) =
        (id, zones, inputKinds, inputSuits, outputKind, condition);
    public string Id { get; }
    public IReadOnlyList<CardZoneKind> Zones { get; }
    public IReadOnlyList<CardKind> InputKinds { get; }
    public IReadOnlyList<Suit> InputSuits { get; }
    public CardKind OutputKind { get; }
    public SkillProgramCondition Condition { get; }
}

public sealed record SkillProgramDeclarationValidation(string ChallengeGrantSkillId);

public sealed class SkillProgramViewAs
{
    internal SkillProgramViewAs(string id, IReadOnlyList<CardKind> inputKinds, IReadOnlyList<Suit> inputSuits,
        CardKind outputKind, bool forPlay, bool forResponse, SkillProgramCondition condition,
        int inputCount = 1, IReadOnlyList<CardZoneKind>? sourceZones = null,
        bool allowChainedInput = false,
        IReadOnlyList<SkillProgramCardCategory>? inputCategories = null,
        bool sameSuit = false, int? usesPerPhase = null, string? usageGroup = null,
        bool inheritPreviousPlaySuit = false) =>
        (Id, InputKinds, InputSuits, OutputKind, ForPlay, ForResponse, Condition, InputCount, SourceZones,
            AllowChainedInput, InputCategories, SameSuit, UsesPerPhase, UsageGroup, InheritPreviousPlaySuit) =
        (id, inputKinds, inputSuits, outputKind, forPlay, forResponse, condition, inputCount,
            sourceZones ?? [CardZoneKind.Hand], allowChainedInput, inputCategories ?? [], sameSuit,
            usesPerPhase, usageGroup, inheritPreviousPlaySuit);
    public string Id { get; }
    public IReadOnlyList<CardKind> InputKinds { get; }
    public IReadOnlyList<SkillProgramCardCategory> InputCategories { get; }
    public IReadOnlyList<Suit> InputSuits { get; }
    public CardKind OutputKind { get; }
    public bool ForPlay { get; }
    public bool ForResponse { get; }
    public SkillProgramCondition Condition { get; }
    public int InputCount { get; }
    public IReadOnlyList<CardZoneKind> SourceZones { get; }
    public bool AllowChainedInput { get; }
    public bool SameSuit { get; }
    public int? UsesPerPhase { get; }
    public string? UsageGroup { get; }
    public bool InheritPreviousPlaySuit { get; }
    public bool VariableInputCount { get; internal init; }
    public bool DistanceUnlimited { get; internal init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? UseEffectiveInputSuit { get; internal init; }
    public bool ExtendedUse { get; internal init; }
    public int DamageBonus { get; internal init; }
    public int RecoveryBonus { get; internal init; }
    public SkillProgramCardDestination? CostDestination { get; internal init; }
    public bool UnusedOutputThisTurn { get; internal init; }
    public bool UseOnly { get; internal init; }
    public bool AllowSameKind { get; internal init; }
    public bool NoDying { get; internal init; }
    public bool UnusedOutputNameThisGame { get; internal init; }
    public string? NameLedgerId { get; internal init; }
    public bool SingleCardTrickUse { get; internal init; }
    public bool ExcludeOwnerEffects { get; internal init; }
    public string? ConversionStateId { get; internal init; }
    public int MinimumTier { get; internal init; }
    public int MaximumTier { get; internal init; } = 2;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SkillProgramDeclarationValidation? DeclarationValidation { get; internal init; }
    public bool DeclaredEntity { get; internal init; }
    public string? ActivationUsageGroup { get; internal init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ProgramTieredRoundConversionPolicy? TieredRoundConversion { get; internal init; }
}

public sealed class SkillProgramEffect
{
    internal SkillProgramEffect(
        SkillProgramEffectOp op,
        SkillProgramEffectTarget target,
        int amount,
        SkillProgramCondition condition,
        TurnPhase? phase = null,
        SkillProgramPhaseContinuation? phaseContinuation = null,
        SkillProgramNumberExpression? numberExpression = null,
        int minimumValue = 0,
        bool clampToMaxHp = false,
        string? sourceBind = null,
        string? resultBind = null,
        string? exceptBind = null,
        SkillProgramCardSetVisibility visibility = SkillProgramCardSetVisibility.Private,
        int minimumCards = 0,
        int maximumCards = 0,
        int maximumRankSum = 0,
        SkillProgramSubsetAiOrder? aiOrder = null,
        SkillProgramCardDestination? destination = null,
        CardZoneKind? destinationZone = null,
        SkillProgramCardSource cardSource = SkillProgramCardSource.DamageSource,
        bool? faceDown = null,
        IReadOnlyList<CardZoneKind>? zones = null,
        SkillProgramTargetKind? targetKind = null,
        int minimumTargets = 0,
        int maximumTargets = 0,
        SkillProgramTargetAiOrder? targetAiOrder = null,
        IReadOnlyList<Suit>? suits = null,
        IReadOnlyList<CardKind>? cardKinds = null,
        IReadOnlyList<CardActionType>? actionTypes = null,
        SkillRuleQuery? ruleQuery = null,
        SkillRuleOperation? ruleOperation = null,
        SkillProgramCardTargetRestriction? targetRestriction = null,
        string? judgmentReason = null,
        SkillProgramCardColorRelation? colorRelation = null,
        CardKind? outputKind = null,
        bool? chained = null,
        ProgramParticipantReference? chooserRef = null,
        ProgramParticipantReference? cardOwnerRef = null,
        string? stateId = null,
        bool? booleanValue = null,
        ProgramParticipantReference? opponentReference = null,
        ProgramParticipantReference? actorReference = null,
        ProgramParticipantReference? targetReference = null,
        DirectedTurnCardPolicyEffect directedPolicyEffects = DirectedTurnCardPolicyEffect.None,
        IReadOnlyList<string>? skillIds = null,
        IReadOnlyList<SkillProgramCardCategory>? cardCategories = null,
        IReadOnlyList<SkillProgramChoiceOption>? options = null,
        bool skipIfNoCards = false,
        PlayerMarkerKind? marker = null,
        bool allowDeclineBeforeFirst = false,
        bool allowFewerWhenInsufficient = false,
        IReadOnlyList<EquipmentSlot>? equipmentSlots = null,
        SkillProgramDamageModifierExpiration damageModifierExpiration = SkillProgramDamageModifierExpiration.CurrentTurnEnd,
        SkillProgramDamageModifierSourceScope damageModifierSourceScope = SkillProgramDamageModifierSourceScope.OwnerUsed,
        bool allowSameOwnerHandReturn = false,
        string? coverageResultBind = null,
        bool awaitMovementTriggers = false, bool revealBeforeMove = false,
        string? matchSuitOfBind = null, bool allowSameSource = false,
        bool skipIfNoTarget = false,
        DamageNature? damageNature = null,
        SkillProgramOldJudgmentCardDestination? oldCardDestination = null,
        IReadOnlyList<Suit>? replacementSuits = null,
        int minimumReplacementRank = 0,
        int maximumReplacementRank = 0,
        string? providerFactionId = null,
        IReadOnlyList<SkillProgramTurnPhase>? skippedPhases = null,
        SkillProgramRevealMode? revealMode = null,
        ProgramParticipantReference? sourceRef = null,
        bool prohibitReplacingEquipment = false,
        bool onePerSuit = false,
        bool allowDecline = false,
        bool useCardActionWindows = false, bool freezeMovedCardSuit = false, bool useFrozenSuit = false,
        SkillProgramSuitSource? suitFrom = null, PlayerMarkerKind? amountFromMarker = null, bool clearMarker = false) =>
        (Op, Target, Amount, Condition, Phase, PhaseContinuation, NumberExpression, MinimumValue,
            ClampToMaxHp, SourceBind, ResultBind, ExceptBind, Visibility, MinimumCards, MaximumCards,
            MaximumRankSum, AiOrder, Destination, DestinationZone, CardSource, FaceDown, Zones, TargetKind,
            MinimumTargets, MaximumTargets, TargetAiOrder, Suits, CardKinds, ActionTypes,
            RuleQuery, RuleOperation, TargetRestriction, JudgmentReason, ColorRelation, OutputKind,
            Chained, ChooserRef, CardOwnerRef, StateId, BooleanValue, OpponentReference, ActorReference, TargetReference,
            DirectedPolicyEffects, SkillIds, CardCategories, Options, SkipIfNoCards, Marker,
            AllowDeclineBeforeFirst, AllowFewerWhenInsufficient, EquipmentSlots,
            DamageModifierExpiration, DamageModifierSourceScope, AllowSameOwnerHandReturn, CoverageResultBind, AwaitMovementTriggers,
            RevealBeforeMove, MatchSuitOfBind, AllowSameSource, SkipIfNoTarget,
            DamageNature, OldCardDestination, ReplacementSuits, MinimumReplacementRank, MaximumReplacementRank,
            ProviderFactionId, SkippedPhases, RevealMode, SourceRef, ProhibitReplacingEquipment,
            OnePerSuit, AllowDecline, UseCardActionWindows, FreezeMovedCardSuit, UseFrozenSuit,
            SuitFrom, AmountFromMarker, ClearMarker) =
        (op, target, amount, condition, phase, phaseContinuation, numberExpression, minimumValue,
            clampToMaxHp, sourceBind, resultBind, exceptBind, visibility, minimumCards, maximumCards,
            maximumRankSum, aiOrder, destination, destinationZone, cardSource, faceDown,
            zones ?? Array.Empty<CardZoneKind>(), targetKind, minimumTargets, maximumTargets, targetAiOrder,
            suits ?? Array.Empty<Suit>(), cardKinds ?? Array.Empty<CardKind>(),
            actionTypes ?? Array.Empty<CardActionType>(), ruleQuery, ruleOperation, targetRestriction,
            judgmentReason, colorRelation, outputKind, chained, chooserRef, cardOwnerRef,
            stateId, booleanValue, opponentReference,
            actorReference, targetReference, directedPolicyEffects,
            skillIds ?? Array.Empty<string>(), cardCategories ?? Array.Empty<SkillProgramCardCategory>(),
            options ?? Array.Empty<SkillProgramChoiceOption>(), skipIfNoCards, marker,
            allowDeclineBeforeFirst, allowFewerWhenInsufficient,
            equipmentSlots ?? Array.Empty<EquipmentSlot>(), damageModifierExpiration,
            damageModifierSourceScope, allowSameOwnerHandReturn, coverageResultBind, awaitMovementTriggers,
            revealBeforeMove, matchSuitOfBind, allowSameSource, skipIfNoTarget,
            damageNature, oldCardDestination, replacementSuits ?? Array.Empty<Suit>(),
            minimumReplacementRank, maximumReplacementRank, providerFactionId,
            skippedPhases ?? Array.Empty<SkillProgramTurnPhase>(), revealMode, sourceRef,
            prohibitReplacingEquipment, onePerSuit, allowDecline, useCardActionWindows, freezeMovedCardSuit, useFrozenSuit,
            suitFrom, amountFromMarker, clearMarker);
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PrivateGeneralLibraryPolicy? GeneralLibraryPolicy {get;internal init;}
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PublicDeathDamageCostPolicy? PublicDeathDamageCost { get; internal init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ExactTopCount { get; internal init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramFinalTargetComparison? FinalTargetComparison { get; internal init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramPopulationThresholdCount? PopulationThresholdCount { get; internal init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AllBottomStateId { get; internal init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SkillProgramClaimHandLimitExemption? ClaimHandLimitExemption { get; internal init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AvailableAtSourceOnly { get; internal init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OwnerBind { get; internal init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChooserBind { get; internal init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LeftoverBind { get; internal init; }
    public SkillProgramEffectOp Op { get; }
    // An internal execution view compiled by the operation descriptor after JSON validation.
    // The public definition and its serialized content fingerprint stay unchanged.
    [System.Text.Json.Serialization.JsonIgnore]
    internal ProgramSkillInstruction? CompiledInstruction { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool UseCardActionWindows { get; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool FreezeMovedCardSuit { get; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool UseFrozenSuit { get; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SkillProgramSuitSource? SuitFrom { get; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PlayerMarkerKind? AmountFromMarker { get; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool ClearMarker { get; }
    public SkillProgramEffectTarget Target { get; }
    public int Amount { get; }
    public SkillProgramCondition Condition { get; }
    public TurnPhase? Phase { get; }
    public SkillProgramPhaseContinuation? PhaseContinuation { get; }
    public SkillProgramNumberExpression? NumberExpression { get; }
    public int MinimumValue { get; }
    public bool ClampToMaxHp { get; }
    public string? SourceBind { get; }
    public string? ResultBind { get; }
    public string? ExceptBind { get; }
    public SkillProgramCardSetVisibility Visibility { get; }
    public int MinimumCards { get; }
    public int MaximumCards { get; }
    public bool AllowFewerWhenInsufficient { get; }
    public int MaximumRankSum { get; }
    public SkillProgramSubsetAiOrder? AiOrder { get; }
    public SkillProgramCardDestination? Destination { get; }
    public CardZoneKind? DestinationZone { get; }
    public SkillProgramCardSource CardSource { get; }
    public bool? FaceDown { get; }
    public IReadOnlyList<CardZoneKind> Zones { get; }
    public SkillProgramTargetKind? TargetKind { get; }
    public int MinimumTargets { get; }
    public int MaximumTargets { get; }
    public SkillProgramTargetAiOrder? TargetAiOrder { get; }
    public IReadOnlyList<Suit> Suits { get; }
    public IReadOnlyList<CardKind> CardKinds { get; }
    public IReadOnlyList<EquipmentSlot> EquipmentSlots { get; }
    public SkillProgramDamageModifierExpiration DamageModifierExpiration { get; }
    public SkillProgramDamageModifierSourceScope DamageModifierSourceScope { get; }
    public IReadOnlyList<CardActionType> ActionTypes { get; }
    public SkillRuleQuery? RuleQuery { get; }
    public SkillRuleOperation? RuleOperation { get; }
    public SkillProgramCardTargetRestriction? TargetRestriction { get; }
    public string? JudgmentReason { get; }
    public SkillProgramCardColorRelation? ColorRelation { get; }
    public CardKind? OutputKind { get; }
    public string? ProviderFactionId { get; }
    public IReadOnlyList<SkillProgramTurnPhase> SkippedPhases { get; }
    public bool? Chained { get; }
    public ProgramParticipantReference? ChooserRef { get; }
    public ProgramParticipantReference? CardOwnerRef { get; }
    public string? StateId { get; }
    public bool? BooleanValue { get; }
    public ProgramParticipantReference? OpponentReference { get; }
    public ProgramParticipantReference? ActorReference { get; }
    public ProgramParticipantReference? TargetReference { get; }
    public DirectedTurnCardPolicyEffect DirectedPolicyEffects { get; }
    public IReadOnlyList<string> SkillIds { get; }
    public IReadOnlyList<SkillProgramCardCategory> CardCategories { get; }
    public IReadOnlyList<SkillProgramChoiceOption> Options { get; }
    public bool SkipIfNoCards { get; }
    public PlayerMarkerKind? Marker { get; }
    public bool AllowDeclineBeforeFirst { get; }
    public bool AllowSameOwnerHandReturn { get; }
    public string? CoverageResultBind { get; }
    public bool AwaitMovementTriggers { get; }
    public bool RevealBeforeMove { get; }
    public string? MatchSuitOfBind { get; }
    public bool AllowSameSource { get; }
    public bool SkipIfNoTarget { get; }
    public DamageNature? DamageNature { get; }
    public SkillProgramOldJudgmentCardDestination? OldCardDestination { get; }
    public IReadOnlyList<Suit> ReplacementSuits { get; }
    public int MinimumReplacementRank { get; }
    public int MaximumReplacementRank { get; }
    public SkillProgramRevealMode? RevealMode { get; }
    public ProgramParticipantReference? SourceRef { get; }
    public bool ProhibitReplacingEquipment { get; }
    public bool OnePerSuit { get; }
    public bool AllowDecline { get; }
}

public enum SkillProgramRevealMode { Random, Chooser, SelfChoiceOtherwiseRandom = 660 }

public sealed record SkillProgramMarkerCost(PlayerMarkerKind Marker, int Amount);

public sealed class SkillProgramActivation
{
    internal SkillProgramActivation(string id, int minCards, int maxCards, int minTargets, int maxTargets,
        SkillProgramTargetKind targetKind, int? usesPerTurn, SkillProgramCondition condition,
        IReadOnlyList<SkillProgramEffect> effects, IReadOnlyList<CardZoneKind>? sourceZones = null,
        int? usesPerPhase = null, int? usesPerGame = null,
        bool selectedCardsSameSuit = false,
        IReadOnlyList<EquipmentSlot>? equipmentSlots = null,
        string? usageGroup = null,
        bool targetRequiresEmptyEquipmentSlot = false) =>
        (Id, MinCards, MaxCards, MinTargets, MaxTargets, TargetKind, UsesPerTurn, UsesPerPhase, UsesPerGame,
            Condition, Effects, SourceZones, SelectedCardsSameSuit, EquipmentSlots, UsageGroup,
            TargetRequiresEmptyEquipmentSlot) =
        (id, minCards, maxCards, minTargets, maxTargets, targetKind, usesPerTurn, usesPerPhase, usesPerGame, condition, effects,
            sourceZones ?? Array.AsReadOnly(new[] { CardZoneKind.Hand }), selectedCardsSameSuit,
            equipmentSlots ?? Array.Empty<EquipmentSlot>(), usageGroup ?? id, targetRequiresEmptyEquipmentSlot);
    public string Id { get; }
    public int MinCards { get; }
    public int MaxCards { get; }
    public int MinTargets { get; }
    public int MaxTargets { get; }
    public SkillProgramTargetKind TargetKind { get; }
    public int? UsesPerTurn { get; }
    public int? UsesPerPhase { get; }
    public int? UsesPerGame { get; }
    public SkillProgramCondition Condition { get; }
    public IReadOnlyList<SkillProgramEffect> Effects { get; }
    public IReadOnlyList<CardZoneKind> SourceZones { get; }
    public string? CategoryTargetLedgerId { get; internal init; }
    public string? TargetPhaseLedgerId { get; internal init; }
    public bool SelectedCardsSameSuit { get; }
    public IReadOnlyList<EquipmentSlot> EquipmentSlots { get; }
    public string UsageGroup { get; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SkillProgramMarkerCost? MarkerCost { get; internal init; }

    /// <summary>
    /// Targets must leave the activation's selectable hand equipment at least one
    /// free matching slot (Zhijian: equipment gifts cannot replace an equipped card).
    /// </summary>
    public bool TargetRequiresEmptyEquipmentSlot { get; }
    public bool ContinueAfterOwnerDeath { get; internal init; }
    public bool SelectedCardsDistinctSuits { get; internal init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SkillProgramCardCountExpression? CardCountExpression { get; internal init; }
    public IReadOnlyList<CardKind> CardKinds { get; internal init; } = [];
    public IReadOnlyList<Suit> CardSuits { get; internal init; } = [];
    public IReadOnlyList<SkillProgramCardCategory> CardCategories { get; internal init; } = [];
}

/// <summary>
/// A play-phase entry granted by this skill to another character. The provider
/// contributes one matching physical hand card to the living skill owner; kind
/// and suit filters form a union so definitions can express "Dodge or Spade".
/// </summary>
public sealed class SkillProgramContribution
{
    internal SkillProgramContribution(string id, IReadOnlyList<string> providerFactions, Role ownerRole,
        IReadOnlyList<CardKind> cardKinds, IReadOnlyList<Suit> cardSuits, int usesPerPlayPhase) =>
        (Id, ProviderFactions, OwnerRole, CardKinds, CardSuits, UsesPerPlayPhase) =
        (id, providerFactions, ownerRole, cardKinds, cardSuits, usesPerPlayPhase);
    public string Id { get; }
    public IReadOnlyList<string> ProviderFactions { get; }
    public Role OwnerRole { get; }
    public IReadOnlyList<CardKind> CardKinds { get; }
    public IReadOnlyList<Suit> CardSuits { get; }
    public int UsesPerPlayPhase { get; }
}

public enum SkillProgramDynamicUsageLimitKind { AlivePlayersCapped }
public sealed record SkillProgramDynamicUsageLimit(SkillProgramDynamicUsageLimitKind Kind, int Cap);

public sealed class SkillProgramTrigger
{
    internal SkillProgramTrigger(string id, SkillProgramTriggerWindow window, string? sourceSkillId,
        string? sourceViewAsId, SkillProgramTriggerSubject? subject, IReadOnlyList<Suit> suits,
        int minimumRank, int maximumRank, IReadOnlyList<string> excludedReasons,
        IReadOnlyList<string> judgmentReasons, SkillProgramTriggerSubject? judgmentSource,
        IReadOnlyList<CardKind> cardKinds, IReadOnlyList<CardZoneKind> sourceZones,
        SkillProgramMovementOccurrence? movementOccurrence,
        SkillProgramDamageOccurrence? damageOccurrence,
        SkillProgramDrawPhaseMode drawPhaseMode,
        bool optional, SkillProgramTriggerCondition condition,
        IReadOnlyList<SkillProgramEffect> effects,
        int priority = 0, SkillUsageScope? usageScope = null, int? usageLimit = null,
        string? choiceGroup = null,
        SkillProgramCardActionOwnerRelation? ownerRelation = null,
        IReadOnlyList<SkillProgramCardCategory>? cardCategories = null,
        IReadOnlyList<CardKind>? damageCardKinds = null,
        SkillProgramTurnOwnerScope turnOwnerScope = SkillProgramTurnOwnerScope.Own) =>
        (Id, Window, SourceSkillId, SourceViewAsId, Subject, Suits, MinimumRank, MaximumRank,
            ExcludedReasons, JudgmentReasons, JudgmentSource, CardKinds, SourceZones, MovementOccurrence,
            DamageOccurrence, DrawPhaseMode,
            Optional, Condition, Effects, Priority, UsageScope, UsageLimit, ChoiceGroup, OwnerRelation,
            CardCategories, DamageCardKinds, TurnOwnerScope) =
        (id, window, sourceSkillId, sourceViewAsId, subject, suits, minimumRank, maximumRank,
            excludedReasons, judgmentReasons, judgmentSource, cardKinds, sourceZones, movementOccurrence,
            damageOccurrence, drawPhaseMode,
            optional, condition, effects, priority, usageScope, usageLimit, choiceGroup, ownerRelation,
            cardCategories ?? [], damageCardKinds ?? [], turnOwnerScope);
    public string Id { get; }
    public SkillProgramTriggerWindow Window { get; }
    public string? SourceSkillId { get; }
    public string? SourceViewAsId { get; }
    public SkillProgramTriggerSubject? Subject { get; }
    public IReadOnlyList<Suit> Suits { get; }
    public int MinimumRank { get; }
    public int MaximumRank { get; }
    public IReadOnlyList<string> ExcludedReasons { get; }
    public IReadOnlyList<string> JudgmentReasons { get; }
    public SkillProgramTriggerSubject? JudgmentSource { get; }
    public IReadOnlyList<CardKind> CardKinds { get; }
    public IReadOnlyList<CardKind> DamageCardKinds { get; }
    public IReadOnlyList<SkillProgramCardCategory> CardCategories { get; }
    public IReadOnlyList<CardZoneKind> SourceZones { get; }
    public IReadOnlyList<CardZoneKind> DestinationZones { get; internal init; } = [];
    public IReadOnlyList<string> MovementReasons { get; internal init; } = [];
    public IReadOnlyList<string> ExcludedMovementReasons { get; internal init; } = [];
    public bool IgnoreOwnSkillMovements { get; internal init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool MovementDiscardOnly { get; internal init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public SkillProgramDiscardOwnerScope DiscardOwnerScope { get; internal init; }
    public SkillProgramHpChangeOccurrence HpChangeOccurrence { get; internal init; }
    public SkillProgramMovementOccurrence? MovementOccurrence { get; }
    public SkillProgramDamageOccurrence? DamageOccurrence { get; }
    public SkillProgramDrawPhaseMode DrawPhaseMode { get; }
    public bool Optional { get; }
    public SkillProgramTriggerCondition Condition { get; }
    public IReadOnlyList<SkillProgramEffect> Effects { get; }
    public int Priority { get; }
    public SkillUsageScope? UsageScope { get; }
    public int? UsageLimit { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SkillProgramDynamicUsageLimit? DynamicUsageLimit { get; internal init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NamedUsageGroup { get; internal init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SkillProgramGainPhaseQualification? GainPhaseQualification { get; internal init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RequireDamageSource { get; internal init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RequireNoCardConversion { get; internal init; }
    public string? ChoiceGroup { get; }
    public SkillProgramCardActionOwnerRelation? OwnerRelation { get; }
    public bool AllowNoEventTarget { get; internal init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool IncludeResponseUses { get; internal init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool SingleActionInstance { get; internal init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool NoDyingAtActivation { get; internal init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool DeferredTurnEndOnly { get; internal init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool OnlyDesignatedCardTargets { get; internal init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool AllowOwnDiscardPhaseEnded { get; internal init; }
    public SkillProgramTurnOwnerScope TurnOwnerScope { get; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool EvaluateConditionAtResolution { get; internal init; }
    public string? ChoiceLabel { get; internal set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SkillProgramMarkerCost? MarkerCost { get; internal init; }

}

public sealed class SkillProgram
{
    internal SkillProgram(string id, int revision, string gameplayHash, string runtimeVersion, int minimumRulesVersion,
        IReadOnlyList<SkillProgramModifier> modifiers, IReadOnlyList<SkillProgramViewAs> viewAs,
        IReadOnlyList<SkillProgramActivation> activations, IReadOnlyList<SkillProgramTrigger> triggers,
        IReadOnlyList<SkillProgramContribution> contributions,
        IReadOnlyList<SkillProgramCardIdentity> cardIdentities,
        IReadOnlyList<SkillProgramBooleanStateDefinition>? booleanStates = null,
        IReadOnlyList<SkillProgramDamageModifier>? damageModifiers = null,
        IReadOnlyList<SkillProgramCardPolicy>? cardPolicies = null, bool lordSkillProjection = false, bool cannotChallengeDeclarations = false) =>
        (Id, Revision, GameplayHash, RuntimeVersion, MinimumRulesVersion, Modifiers, ViewAs, Activations, Triggers,
            Contributions, CardIdentities, BooleanStates, DamageModifiers, CardPolicies, LordSkillProjection, CannotChallengeDeclarations) =
        (id, revision, gameplayHash, runtimeVersion, minimumRulesVersion, modifiers, viewAs, activations, triggers,
            contributions, cardIdentities, booleanStates ?? [], damageModifiers ?? [], cardPolicies ?? [], lordSkillProjection, cannotChallengeDeclarations);
    public bool LordSkillProjection { get; }
    public bool CannotChallengeDeclarations { get; }
    public string Id { get; }
    public int Revision { get; }
    public string GameplayHash { get; }
    public string RuntimeVersion { get; }
    public int MinimumRulesVersion { get; }
    public IReadOnlyList<SkillProgramModifier> Modifiers { get; }
    public IReadOnlyList<SkillProgramViewAs> ViewAs { get; }
    public IReadOnlyList<SkillProgramActivation> Activations { get; }
    public IReadOnlyList<SkillProgramTrigger> Triggers { get; }
    public IReadOnlyList<SkillProgramContribution> Contributions { get; }
    public IReadOnlyList<SkillProgramCardIdentity> CardIdentities { get; }
    public IReadOnlyList<SkillProgramBooleanStateDefinition> BooleanStates { get; }
    public IReadOnlyList<SkillProgramDamageModifier> DamageModifiers { get; }
    public IReadOnlyList<SkillProgramCardPolicy> CardPolicies { get; }
}

public sealed class SkillPresentation
{
    internal SkillPresentation(string name, string description,
        IReadOnlyDictionary<string, string>? triggerChoices = null,
        IReadOnlyDictionary<string, ProgramBooleanStatePresentation>? booleanStates = null) =>
        (Name, Description, TriggerChoices, BooleanStates) =
        (name, description, triggerChoices ?? new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal)), booleanStates ??
            new ReadOnlyDictionary<string, ProgramBooleanStatePresentation>(
                new Dictionary<string, ProgramBooleanStatePresentation>(StringComparer.Ordinal)));
    public string Name { get; }
    public string Description { get; }
    public IReadOnlyDictionary<string, string> TriggerChoices { get; }
    public IReadOnlyDictionary<string, ProgramBooleanStatePresentation> BooleanStates { get; }
    public string? AuthorityName { get; internal init; }
    public IReadOnlyDictionary<string, string> TriggerLabels { get; internal init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));
    public IReadOnlyDictionary<string, string> ActivationLabels { get; internal init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));
}

public sealed record ProgramBooleanStatePresentation(string TrueText, string FalseText);

public sealed class SkillProgramCatalog
{
    public const int RulesSchemaVersion = 62;
    public const int PresentationSchemaVersion = 3;
    public const string RuntimeVersion = "skill-program-v62";
    private const int MaximumDepth = 16;
    private const int MaximumItems = 256;
    private static readonly SkillProgramCondition Always = new(
        SkillProgramConditionKind.Always, 0, Array.Empty<SkillProgramCondition>());
    private static readonly SkillProgramTriggerCondition AlwaysTrigger = new(
        SkillProgramTriggerConditionKind.Always, Array.Empty<SkillProgramTriggerCondition>());

    private SkillProgramCatalog(IReadOnlyDictionary<string, SkillProgram> programs,
        IReadOnlyDictionary<string, SkillPresentation> presentations) =>
        (Programs, Presentations) = (programs, presentations);

    public IReadOnlyDictionary<string, SkillProgram> Programs { get; }
    public IReadOnlyDictionary<string, SkillPresentation> Presentations { get; }

    public static SkillProgramCatalog Load(string rulesJson, string presentationJson)
    {
        ArgumentNullException.ThrowIfNull(rulesJson);
        ArgumentNullException.ThrowIfNull(presentationJson);
        try
        {
            using var rules = Parse(rulesJson, "rules");
            using var presentation = Parse(presentationJson, "presentation");
            var programs = LoadPrograms(rules.RootElement);
            var presentations = LoadPresentations(presentation.RootElement, programs);
            return new SkillProgramCatalog(ReadOnly(programs), ReadOnly(presentations));
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception exception) when (exception is JsonException or FormatException or OverflowException)
        {
            throw new InvalidOperationException($"Invalid skill program JSON: {exception.Message}", exception);
        }
    }

    private static JsonDocument Parse(string json, string path)
    {
        try
        {
            var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = MaximumDepth
            });
            RejectDuplicateProperties(document.RootElement, path);
            return document;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Invalid JSON at {path}: {exception.Message}", exception);
        }
    }

    private static Dictionary<string, SkillProgram> LoadPrograms(JsonElement root)
    {
        RequireObject(root, "rules");
        CheckProperties(root, "rules", "schemaVersion", "skills");
        RequireVersion(root, "rules", RulesSchemaVersion);
        var runtimeVersion = RuntimeVersion;
        const int minimumRulesVersion = 171;
        var skills = Required(root, "skills", JsonValueKind.Array, "rules");
        CheckCount(skills.GetArrayLength(), "rules.skills");
        var result = new Dictionary<string, SkillProgram>(StringComparer.Ordinal);
        var index = 0;
        foreach (var skill in skills.EnumerateArray())
        {
            var path = $"rules.skills[{index++}]";
            RequireObject(skill, path);
            CheckProperties(skill, path, "id", "revision", "minimumRulesVersion", "modifiers",
                "damageModifiers", "viewAs", "activations", "triggers", "contributions",
                "cardIdentities", "states", "cardPolicies", "lordSkillProjection", "cannotChallengeDeclarations");
            var id = Identifier(skill, "id", path);
            var skillPath = $"skill '{id}' ({path})";
            if (result.ContainsKey(id)) Fail(skillPath, $"duplicate skill id '{id}'");
            var revision = PositiveInt(skill, "revision", skillPath);
            var skillMinimumRulesVersion = minimumRulesVersion;
            if (skill.TryGetProperty("minimumRulesVersion", out _))
            {
                skillMinimumRulesVersion = RequiredInt(skill, "minimumRulesVersion", skillPath);
                if (skillMinimumRulesVersion < minimumRulesVersion)
                    Fail(skillPath + ".minimumRulesVersion",
                        $"must be at least the schema minimum {minimumRulesVersion}");
            }
            var modifiers = ReadArray(skill, "modifiers", skillPath,
                ParseModifier, optional: true);
            var damageModifiers = ReadArray(skill, "damageModifiers", skillPath, ParseDamageModifier, optional: true);
            var cardPolicies = ReadArray(skill, "cardPolicies", skillPath, ParseCardPolicy, optional: true);
            var viewAs = ReadArray(skill, "viewAs", skillPath,
                ParseViewAs, optional: true);
            var activations = ReadArray(skill, "activations", skillPath,
                ParseActivation, optional: true);
            var triggers = ReadArray(skill, "triggers", skillPath,
                ParseTrigger, optional: true);
            var contributions = ReadArray(skill, "contributions", skillPath, ParseContribution, optional: true);
            var cardIdentities = ReadArray(skill, "cardIdentities", skillPath, ParseCardIdentity, optional: true);
            var booleanStates = ReadArray(skill, "states", skillPath, ParseBooleanState, optional: true);
            if (modifiers.Count == 0 && viewAs.Count == 0 && activations.Count == 0 && triggers.Count == 0 &&
                contributions.Count == 0 && cardIdentities.Count == 0 && damageModifiers.Count == 0 &&
                cardPolicies.Count == 0 && !(skill.TryGetProperty("lordSkillProjection", out _) && RequiredBool(skill, "lordSkillProjection", skillPath)) && !(skill.TryGetProperty("cannotChallengeDeclarations", out _) && RequiredBool(skill, "cannotChallengeDeclarations", skillPath)))
                Fail(skillPath, "must define at least one modifier, viewAs rule, activation, trigger, contribution, or card identity");
            EnsureUniqueIds(modifiers.Select(item => item.Id), skillPath + ".modifiers");
            EnsureUniqueIds(damageModifiers.Select(item => item.Id), skillPath + ".damageModifiers");
            EnsureUniqueIds(cardPolicies.Select(item => item.Id), skillPath + ".cardPolicies");
            EnsureUniqueIds(viewAs.Select(item => item.Id), skillPath + ".viewAs");
            foreach (var group in viewAs.Where(rule => rule.UsageGroup is not null).GroupBy(rule => rule.UsageGroup))
                if (group.Select(rule => rule.UsesPerPhase).Distinct().Count() != 1)
                    Fail(skillPath + ".viewAs", "a shared usageGroup must declare the same phase limit");
            EnsureUniqueIds(activations.Select(item => item.Id), skillPath + ".activations");
            foreach (var group in activations.GroupBy(item => item.UsageGroup, StringComparer.Ordinal))
            {
                var first = group.First();
                if (group.Any(item => item.UsesPerTurn != first.UsesPerTurn ||
                                      item.UsesPerPhase != first.UsesPerPhase ||
                                      item.UsesPerGame != first.UsesPerGame))
                    Fail(skillPath + ".activations",
                        $"usageGroup '{group.Key}' must use identical turn, phase and game limits");
            }
            EnsureUniqueIds(triggers.Select(item => item.Id), skillPath + ".triggers");
            PrivateOfferComposition.ValidateBindings(skillPath, triggers);
            PhaseHandSeizureComposition.ValidateBindings(skillPath, activations, triggers);
            JudgedRankSlashComposition.ValidateBindings(skillPath, activations, triggers);
            ValidateTriggerChoiceGroups(skillPath, triggers);
            foreach (var group in triggers.Where(t => t.NamedUsageGroup is not null).GroupBy(t => t.NamedUsageGroup!, StringComparer.Ordinal))
            {
                var first = group.First();
                if (group.Any(t => t.UsageScope != first.UsageScope || t.UsageLimit != first.UsageLimit || t.DynamicUsageLimit != first.DynamicUsageLimit))
                    Fail(skillPath + ".triggers", $"named usage group '{group.Key}' requires identical scope and limit policies");
            }
            EnsureUniqueIds(contributions.Select(item => item.Id), skillPath + ".contributions");
            EnsureUniqueIds(cardIdentities.Select(item => item.Id), skillPath + ".cardIdentities");
            EnsureUniqueIds(booleanStates.Select(item => item.Id), skillPath + ".states");
            foreach (var activation in activations)
                foreach (var effect in activation.Effects.Where(item =>
                             item.Op == SkillProgramEffectOp.UseSelectedCardsAs))
                {
                    var conversion = viewAs.SingleOrDefault(item => item.Id == effect.SourceBind);
                    if (conversion is null)
                        Fail(skillPath + ".activations",
                            $"selected-card use references unknown viewAs '{effect.SourceBind}'");
                    if (!conversion.ForPlay || conversion.OutputKind != effect.OutputKind)
                        Fail(skillPath + ".activations",
                            $"viewAs '{effect.SourceBind}' does not support this play output");
                    if (conversion.VariableInputCount
                            ? activation.MinCards < 1 || activation.MaxCards != int.MaxValue ||
                              !activation.SourceZones.SequenceEqual([CardZoneKind.Hand])
                            : activation.MinCards != conversion.InputCount ||
                              activation.MaxCards != conversion.InputCount)
                        Fail(skillPath + ".activations",
                            $"viewAs '{effect.SourceBind}' requires exactly {conversion.InputCount} selected cards");
                    if (effect.OutputKind == CardKind.ArrowBarrage &&
                        (!conversion.SameSuit || !activation.SelectedCardsSameSuit ||
                         activation.MinTargets != 0 || activation.MaxTargets != 0 ||
                         activation.SourceZones.Count != 1 || activation.SourceZones[0] != CardZoneKind.Hand))
                        Fail(skillPath + ".activations",
                            "global same-suit card use requires two hand cards and no initial target");
                }
            foreach (var activation in activations.Where(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.ChoosePrivateColorsDiscardAndDuel)))
                if (activation.MinCards != 0 || activation.MaxCards != 0 || activation.MinTargets != 1 || activation.MaxTargets != 1 ||
                    activation.TargetKind != SkillProgramTargetKind.OtherLiving || activation.Effects.Count != 1 ||
                    activation.UsesPerPhase != 1 || activation.UsesPerTurn is not null)
                    Fail(skillPath + ".activations", "Private color Duel requires one other target, zero cards and one use per actual Play phase.");
            foreach (var activation in activations.Where(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.UseSelectedActorDuel)))
                if (activation.MinCards != 0 || activation.MaxCards != 0 || activation.MinTargets != 1 || activation.MaxTargets != 1 ||
                    activation.TargetKind != SkillProgramTargetKind.OtherLiving || activation.Effects.Count != 1 ||
                    activation.UsesPerPhase != 2 || activation.UsesPerTurn is not null)
                    Fail(skillPath + ".activations", "Selected actor Duel requires one other target, zero cards and exactly two uses per actual Play phase.");
            foreach (var activation in activations.Where(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.UseDiamondDelayedOrDiscard)))
                if (activation.MinCards != 1 || activation.MaxCards != 1 || activation.MinTargets != 0 || activation.MaxTargets != 0 ||
                    activation.SourceZones.Any(z => z is not (CardZoneKind.Hand or CardZoneKind.Equipment)) ||
                    !activation.CardSuits.SequenceEqual([Suit.Diamond]) || activation.UsesPerPhase != 1 || activation.Effects.Count != 1)
                    Fail(skillPath + ".activations", "diamond delayed use requires one Diamond HE card, no initial targets, one operation and one use per Play phase");
            if (triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.PayHpInspectHandThenDiscardOrSlash)))
                Fail(skillPath + ".triggers", "HP hand inspection is a standalone activation operation");
            if (triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.DiscardSlashThenOtherCardAndUseDuel)))
                Fail(skillPath + ".triggers", "conditional discard Duel is a standalone activation operation");
            if (triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.UseDiamondDelayedOrDiscard)))
                Fail(skillPath + ".triggers", "diamond delayed use is an activation operation");
            foreach (var activation in activations)
                foreach (var grant in activation.Effects.Where(e => e.Op == SkillProgramEffectOp.GrantNextActualUseTargetAdjustment))
                    if (!booleanStates.Any(s => s.Id == grant.StateId && !s.InitialValue &&
                            s.Visibility == SkillProgramStateVisibility.Public && s.ResetScope == SkillProgramStateResetScope.Turn) ||
                        activation.Condition is not { Kind: SkillProgramConditionKind.BooleanState, ExpectedValue: false } gate || gate.StateId != grant.StateId)
                        Fail(skillPath, "next actual-use adjustment requires its initially-false public Turn state and matching enabled activation condition");
            var declaredStateIds = booleanStates.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var trigger in triggers)
                foreach (var condition in EnumerateTriggerConditions(trigger.Condition))
                    if (condition.Kind == SkillProgramTriggerConditionKind.BooleanState &&
                        !declaredStateIds.Contains(condition.StateId!))
                        Fail(skillPath + ".triggers", $"trigger condition references undeclared state '{condition.StateId}'");
            foreach (var condition in activations.SelectMany(item => item.Effects)
                         .Select(item => item.Condition)
                         .Concat(triggers.SelectMany(item => item.Effects).Select(item => item.Condition))
                         .SelectMany(EnumerateConditions))
                if (condition.Kind == SkillProgramConditionKind.BooleanState &&
                    !declaredStateIds.Contains(condition.StateId!))
                    Fail(skillPath, $"effect condition references undeclared state '{condition.StateId}'");
            foreach (var effect in activations.SelectMany(item => item.Effects))
                if (effect.Op is SkillProgramEffectOp.SetBooleanState or SkillProgramEffectOp.ToggleBooleanState or SkillProgramEffectOp.RecastSelectedCards or SkillProgramEffectOp.DrawCompletedCardParticipants or
                    SkillProgramEffectOp.RevealTopCardsWithNextBooleanBonus or SkillProgramEffectOp.ObtainBoundCardsAndArmNextRevealBonus &&
                    !declaredStateIds.Contains(effect.StateId!))
                    Fail(skillPath, $"effect references undeclared state '{effect.StateId}'");
            foreach (var effect in triggers.SelectMany(item => item.Effects))
                if (effect.Op is SkillProgramEffectOp.SetBooleanState or SkillProgramEffectOp.ToggleBooleanState or SkillProgramEffectOp.RecastSelectedCards or SkillProgramEffectOp.DrawCompletedCardParticipants or
                    SkillProgramEffectOp.RevealTopCardsWithNextBooleanBonus or SkillProgramEffectOp.ObtainBoundCardsAndArmNextRevealBonus &&
                    !declaredStateIds.Contains(effect.StateId!))
                    Fail(skillPath, $"trigger effect references undeclared state '{effect.StateId}'");
            foreach (var effect in activations.SelectMany(item => item.Effects).Concat(triggers.SelectMany(item => item.Effects)))
                if (effect.Op is SkillProgramEffectOp.RevealTopCardsWithNextBooleanBonus or SkillProgramEffectOp.ObtainBoundCardsAndArmNextRevealBonus &&
                    !booleanStates.Any(state => state.Id == effect.StateId && !state.InitialValue &&
                        state.Visibility == SkillProgramStateVisibility.Public && state.ResetScope == SkillProgramStateResetScope.Game))
                    Fail(skillPath, "next-use reveal bonus requires a declared initially-false public Game state");
            foreach (var effect in activations.SelectMany(item => item.Effects).Concat(triggers.SelectMany(item => item.Effects)))
                if (effect.AllBottomStateId is { } completionState &&
                    !booleanStates.Any(state => state.Id == completionState && !state.InitialValue &&
                        state.Visibility == SkillProgramStateVisibility.Private && state.ResetScope == SkillProgramStateResetScope.Turn))
                    Fail(skillPath, "all-bottom completion requires a declared initially-false private Turn state");
            if (activations.Any(item => item.Effects.Any(effect => effect.AllBottomStateId is not null)))
                Fail(skillPath + ".activations", "all-bottom completion state requires an owner preparation trigger");
            EnsureUniqueIds(activations.Select(item => item.Id).Concat(contributions.Select(item => item.Id)),
                skillPath + ".playBindings");
            ValidateCardIdentityModifiers(skillPath, modifiers, cardIdentities);
            var hashInput = runtimeVersion + "\n" + Canonicalize(skill);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput))).ToLowerInvariant();
            result.Add(id, new SkillProgram(id, revision, hash, runtimeVersion, skillMinimumRulesVersion,
                modifiers, viewAs, activations, triggers, contributions, cardIdentities,
                booleanStates: booleanStates,
                damageModifiers: damageModifiers,
                cardPolicies: cardPolicies, lordSkillProjection: (skill.TryGetProperty("lordSkillProjection", out _) && RequiredBool(skill, "lordSkillProjection", skillPath)), cannotChallengeDeclarations: (skill.TryGetProperty("cannotChallengeDeclarations", out _) && RequiredBool(skill, "cannotChallengeDeclarations", skillPath))));
        }
        var conversionStates = result.Values.SelectMany(program => program.ViewAs)
            .Where(rule => rule.ConversionStateId is not null).Select(rule => rule.ConversionStateId!).ToHashSet(StringComparer.Ordinal);
        foreach (var program in result.Values)
        {
            SlashTargetBenefitComposition.ValidateProgram(program);
            SlashTargetPenaltyComposition.Validate(program);
            FireTargetBenefitComposition.Validate(program);
            foreach (var effect in program.Activations.SelectMany(activation => activation.Effects)
                         .Concat(program.Triggers.SelectMany(trigger => trigger.Effects))
                         .Where(effect => effect.Op is SkillProgramEffectOp.DeclareBoundCardNameUntilTurnEnd or SkillProgramEffectOp.UpgradeConversionTier or SkillProgramEffectOp.DrawBeforeCappedConversionTierUpgrade))
                if (!conversionStates.Contains(effect.StateId!)) Fail("skill '" + program.Id + "'", "conversion operation references an unknown conversionStateId");
            foreach (var rule in program.ViewAs.Where(rule => rule.ConversionStateId is not null))
            {
                if (rule.DeclaredEntity && (!rule.SourceZones.SequenceEqual([CardZoneKind.Hand]) || rule.UsesPerPhase is not null ||
                    !program.Activations.Any(activation => activation.Effects.Any(effect => effect.Op == SkillProgramEffectOp.DeclareBoundCardNameUntilTurnEnd && effect.StateId == rule.ConversionStateId))))
                    Fail("skill '" + program.Id + "'.viewAs", "declaredEntity requires an owner-hand declaration producer without a second allowance");
                if (rule.ActivationUsageGroup is { } group && !program.Activations.Any(activation => activation.UsageGroup == group &&
                    activation.UsesPerPhase == rule.UsesPerPhase && activation.Effects.Any(effect => effect.Op == SkillProgramEffectOp.DeclareBoundCardNameUntilTurnEnd && effect.StateId == rule.ConversionStateId)))
                    Fail("skill '" + program.Id + "'.viewAs", "activationUsageGroup requires the matching phase-limited declaration activation");
            }
        }
        ValidateTriggerSources(result);
        return result;
    }

    private static Dictionary<string, SkillPresentation> LoadPresentations(JsonElement root,
        IReadOnlyDictionary<string, SkillProgram> programs)
    {
        RequireObject(root, "presentation");
        CheckProperties(root, "presentation", "schemaVersion", "skills");
        RequireVersion(root, "presentation", PresentationSchemaVersion);
        var skills = Required(root, "skills", JsonValueKind.Object, "presentation");
        CheckCount(skills.EnumerateObject().Count(), "presentation.skills");
        var result = new Dictionary<string, SkillPresentation>(StringComparer.Ordinal);
        foreach (var property in skills.EnumerateObject())
        {
            var id = property.Name;
            var path = $"presentation.skills.{id}";
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128) Fail(path, "skill id must contain 1 to 128 characters");
            if (!programs.ContainsKey(id)) Fail(path, $"presentation references unknown skill '{id}'");
            RequireObject(property.Value, path);
            CheckProperties(property.Value, path, "name", "description", "triggerChoices", "booleanStates", "optionLabels", "activationLabels", "authorityName", "triggerLabels");
            var triggerChoices = new Dictionary<string, string>(StringComparer.Ordinal);
            var booleanStates = new Dictionary<string, ProgramBooleanStatePresentation>(StringComparer.Ordinal);
            if (property.Value.TryGetProperty("triggerChoices", out var choices))
            {
                RequireObject(choices, path + ".triggerChoices");
                CheckCount(choices.EnumerateObject().Count(), path + ".triggerChoices");
                foreach (var choice in choices.EnumerateObject())
                {
                    if (!programs[id].Triggers.Any(trigger => trigger.Id == choice.Name))
                        Fail(path + ".triggerChoices." + choice.Name,
                            $"references unknown trigger '{choice.Name}'");
                    var label = NonEmptyStringValue(choice.Value, path + ".triggerChoices." + choice.Name);
                    if (!triggerChoices.TryAdd(choice.Name, label))
                        Fail(path + ".triggerChoices." + choice.Name, "duplicates a trigger choice label");
                }
            }
            if (property.Value.TryGetProperty("booleanStates", out var states))
            {
                RequireObject(states, path + ".booleanStates");
                CheckCount(states.EnumerateObject().Count(), path + ".booleanStates");
                foreach (var state in states.EnumerateObject())
                {
                    if (!programs[id].BooleanStates.Any(item => item.Id == state.Name))
                        Fail(path + ".booleanStates." + state.Name, $"references unknown state '{state.Name}'");
                    RequireObject(state.Value, path + ".booleanStates." + state.Name);
                    CheckProperties(state.Value, path + ".booleanStates." + state.Name, "trueText", "falseText");
                    booleanStates.Add(state.Name, new(
                        NonEmptyString(state.Value, "trueText", path + ".booleanStates." + state.Name),
                        NonEmptyString(state.Value, "falseText", path + ".booleanStates." + state.Name)));
                }
            }
            foreach (var group in programs[id].Triggers
                         .Where(trigger => trigger.ChoiceGroup is not null)
                         .GroupBy(trigger => trigger.ChoiceGroup, StringComparer.Ordinal))
            {
                foreach (var trigger in group)
                {
                    if (!triggerChoices.TryGetValue(trigger.Id, out var label))
                        Fail(path + ".triggerChoices",
                            $"missing choice label for grouped trigger '{trigger.Id}'");
                    trigger.ChoiceLabel = label;
                }
            }
            var programOptions = programs[id].Activations.SelectMany(item => item.Effects.SelectMany(effect => effect.Options))
                .Concat(programs[id].Triggers.SelectMany(item => item.Effects.SelectMany(effect => effect.Options))).ToArray();
            var optionLabels = new Dictionary<string, string>(StringComparer.Ordinal);
            if (property.Value.TryGetProperty("optionLabels", out var labels))
            {
                RequireObject(labels, path + ".optionLabels");
                CheckCount(labels.EnumerateObject().Count(), path + ".optionLabels");
                foreach (var label in labels.EnumerateObject())
                {
                    if (!programOptions.Any(option => option.Id == label.Name))
                        Fail(path + ".optionLabels", $"unknown option '{label.Name}'");
                    if (!optionLabels.TryAdd(label.Name, NonEmptyStringValue(label.Value, path + ".optionLabels")))
                        Fail(path + ".optionLabels", $"duplicate option label '{label.Name}'");
                }
            }
            foreach (var option in programOptions)
            {
                if (!optionLabels.TryGetValue(option.Id, out var label))
                    Fail(path + ".optionLabels", $"missing label for option '{option.Id}'");
                option.Label = label;
            }
            var activationLabels = new Dictionary<string, string>(StringComparer.Ordinal);
            if (property.Value.TryGetProperty("activationLabels", out var activationLabelNode))
            {
                RequireObject(activationLabelNode, path + ".activationLabels");
                CheckCount(activationLabelNode.EnumerateObject().Count(), path + ".activationLabels");
                foreach (var label in activationLabelNode.EnumerateObject())
                {
                    if (!programs[id].Activations.Any(activation => activation.Id == label.Name))
                        Fail(path + ".activationLabels", $"unknown activation '{label.Name}'");
                    if (!activationLabels.TryAdd(label.Name, NonEmptyStringValue(label.Value, path + ".activationLabels")))
                        Fail(path + ".activationLabels", $"duplicate activation label '{label.Name}'");
                }
            }
            var triggerLabels = new Dictionary<string, string>(StringComparer.Ordinal);
            if (property.Value.TryGetProperty("triggerLabels", out var triggerLabelNode))
            {
                RequireObject(triggerLabelNode, path + ".triggerLabels");
                CheckCount(triggerLabelNode.EnumerateObject().Count(), path + ".triggerLabels");
                foreach (var label in triggerLabelNode.EnumerateObject())
                {
                    if (!programs[id].Triggers.Any(trigger => trigger.Id == label.Name))
                        Fail(path + ".triggerLabels", $"references unknown trigger '{label.Name}'");
                    if (!triggerLabels.TryAdd(label.Name, NonEmptyStringValue(label.Value, path + ".triggerLabels")))
                        Fail(path + ".triggerLabels", $"duplicate trigger label '{label.Name}'");
                }
            }
            result.Add(id, new SkillPresentation(NonEmptyString(property.Value, "name", path),
                NonEmptyString(property.Value, "description", path),
                new ReadOnlyDictionary<string, string>(triggerChoices),
                new ReadOnlyDictionary<string, ProgramBooleanStatePresentation>(booleanStates))
                {
                    ActivationLabels = new ReadOnlyDictionary<string, string>(activationLabels),
                    TriggerLabels = new ReadOnlyDictionary<string, string>(triggerLabels),
                    AuthorityName = property.Value.TryGetProperty("authorityName", out var authorityName)
                        ? NonEmptyStringValue(authorityName, path + ".authorityName") : null
                });
        }
        foreach (var id in programs.Keys)
            if (!result.ContainsKey(id)) Fail("presentation.skills", $"missing presentation for skill '{id}'");
        return result;
    }

    private static SkillProgramCardPolicy ParseCardPolicy(JsonElement node, string path)
    {
        RequireObject(node, path);
        CheckProperties(node, path, "id", "kind", "cardKinds", "requiredCardKinds", "value",
            "inputSuit", "outputSuit", "condition", "factionId", "ownerRole", "discardCost", "providerDrawCount");
        var id = Identifier(node, "id", path);
        var kind = EnumValue<SkillProgramCardPolicyKind>(node, "kind", path);
        var factionId = node.TryGetProperty("factionId", out _)
            ? Identifier(node, "factionId", path) : null;
        Role? ownerRole = node.TryGetProperty("ownerRole", out _)
            ? EnumValue<Role>(node, "ownerRole", path) : null;
        if (kind is SkillProgramCardPolicyKind.FactionResponseRequest or
            SkillProgramCardPolicyKind.RescueRecoveryBonus or
            SkillProgramCardPolicyKind.RedirectOwnTurnFactionRecovery or
            SkillProgramCardPolicyKind.FactionHandLimitBonus)
        {
            if (factionId is null) Fail(path, "a faction policy requires its provider faction id");
        }
        else if (factionId is not null)
            Fail(path, "factionId requires a faction response or recovery policy");
        var cardKinds = node.TryGetProperty("cardKinds", out _)
            ? EnumArray<CardKind>(node, "cardKinds", path) : [];
        var requiredKinds = node.TryGetProperty("requiredCardKinds", out _)
            ? EnumArray<CardKind>(node, "requiredCardKinds", path) : [];
        if (cardKinds.Distinct().Count() != cardKinds.Count ||
            requiredKinds.Distinct().Count() != requiredKinds.Count)
            Fail(path, "card kind filters must contain distinct values");
        var value = node.TryGetProperty("value", out _) ? RequiredInt(node, "value", path) : 0;
        Suit? inputSuit = node.TryGetProperty("inputSuit", out _)
            ? EnumValue<Suit>(node, "inputSuit", path) : null;
        Suit? outputSuit = node.TryGetProperty("outputSuit", out _)
            ? EnumValue<Suit>(node, "outputSuit", path) : null;
        if (kind == SkillProgramCardPolicyKind.PindianRankBySuit)
        {
            if(inputSuit is null || outputSuit is not null || value is < 1 or > 13 || cardKinds.Count != 0 || requiredKinds.Count != 0)
                Fail(path,"Pindian suit rank requires one input suit, rank 1..13 and no card filters.");
        }
        else if (kind == SkillProgramCardPolicyKind.EquipmentSuitHandLimitAtMaxHp)
        {
            if (inputSuit is null or Suit.None || outputSuit is not null || value != 0 || cardKinds.Count != 0 || requiredKinds.Count != 0 ||
                ownerRole is not null || factionId is not null || OptionalCondition(node, path).Kind != SkillProgramConditionKind.Always)
                Fail(path, "Equipment-suit exact MaxHP hand limit requires one ordinary input suit and no other filters.");
        }
        else if (kind == SkillProgramCardPolicyKind.RewriteSuit)
        {
            if (inputSuit is null || outputSuit is null || inputSuit == outputSuit ||
                cardKinds.Count != 0 || requiredKinds.Count != 0 || value != 0)
                Fail(path, "rewriteSuit requires two distinct suits and no card or value filters");
        }
        else if (kind is SkillProgramCardPolicyKind.IgnoreSlashUseDistanceBySuit or SkillProgramCardPolicyKind.ProhibitTargetSlashResponseBySuit or
                 SkillProgramCardPolicyKind.BypassSlashLimitBySuit)
        {
            if (inputSuit is null || outputSuit is not null || value != 0 || requiredKinds.Count != 0 ||
                cardKinds.Count == 0 || cardKinds.Any(card => card is not (
                    CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)))
                Fail(path, "suit response prohibition requires Slash kinds and one input suit");
        }
        else if (kind == SkillProgramCardPolicyKind.ProhibitTargetBySuit)
        {
            if (inputSuit is null || outputSuit is not null || value != 0 || requiredKinds.Count != 0 ||
                cardKinds.Count == 0 || cardKinds.Any(card => card is not (
                    CardKind.Duel or CardKind.Dismantlement or CardKind.Snatch or CardKind.FireAttack or
                    CardKind.IronChain or CardKind.BorrowedSword or CardKind.Indulgence or CardKind.SupplyShortage or
                    CardKind.BarbarianAssault or CardKind.ArrowBarrage or CardKind.PeachGarden or
                    CardKind.FiveGrains)))
                Fail(path, "target prohibition by suit requires targeted trick kinds and one input suit");
        }
        else if (inputSuit is not null || outputSuit is not null)
            Fail(path, "suit fields require rewriteSuit or a suit response prohibition");
        if (kind is SkillProgramCardPolicyKind.MinimumResponseCount or
            SkillProgramCardPolicyKind.MinimumResponseCountAsTarget or SkillProgramCardPolicyKind.MinimumSlashResponseAtDistanceOne)
        {
            if (cardKinds.Count == 0 || requiredKinds.Count == 0 || value is < 2 or > 20)
                Fail(path, "minimumResponseCount requires incoming and response card kinds and value 2..20");
        }
        else if (kind == SkillProgramCardPolicyKind.PrivateTopBasicRequest)
        {
            if (!cardKinds.SequenceEqual([CardKind.Slash, CardKind.Dodge, CardKind.Peach, CardKind.Alcohol]) ||
                requiredKinds.Count != 0 || value != 2 || inputSuit is not null || outputSuit is not null ||
                factionId is not null || ownerRole is not null || OptionalCondition(node, path).Kind != SkillProgramConditionKind.Always)
                Fail(path, "Private top basic requests require exactly four basic needs, two/four viewing and no other qualifiers.");
        }
        else if (kind == SkillProgramCardPolicyKind.FactionResponseRequest)
        {
            if (requiredKinds.Count != 1 || requiredKinds[0] is not (CardKind.Dodge or CardKind.Slash) || value != 0)
                Fail(path, "a faction request requires one Dodge or Slash response kind");
        }
        else if (kind == SkillProgramCardPolicyKind.RescueRecoveryBonus)
        {
            if (requiredKinds.Count != 0 || value is < 1 or > 20)
                Fail(path, "a recovery bonus requires value 1..20 and no response kinds");
        }
        else if (kind == SkillProgramCardPolicyKind.RedirectOwnTurnFactionRecovery)
        {
            if (requiredKinds.Count != 0 || cardKinds.Count != 0 || value != 1 || ownerRole != Role.Lord)
                Fail(path, "Own-turn faction recovery replacement requires a lord policy, one recovery and no card filters.");
        }
        else if (kind == SkillProgramCardPolicyKind.FactionHandLimitBonus)
        {
            if (requiredKinds.Count != 0 || value is < 1 or > 20)
                Fail(path, "a faction hand-limit bonus requires value 1..20 and no response kinds");
        }
        else if (kind == SkillProgramCardPolicyKind.ProhibitNearbyTargetResponse)
        {
            if (requiredKinds.Count != 0 || value is < 1 or > 20 ||
                cardKinds.Any(card => card is not (
                    CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or
                    CardKind.Duel or CardKind.DrawTwo or CardKind.BarbarianAssault or
                    CardKind.ArrowBarrage or CardKind.PeachGarden or CardKind.FiveGrains or
                    CardKind.Dismantlement or CardKind.Snatch or CardKind.FireAttack or
                    CardKind.IronChain or CardKind.BorrowedSword)))
                Fail(path, "nearby response prohibition requires Slash or ordinary trick kinds and distance 1..20");
        }
        else if (kind != SkillProgramCardPolicyKind.PindianRankBySuit && (requiredKinds.Count != 0 || value != 0))
            Fail(path, "requiredCardKinds and value require a minimum response count policy");
        if (kind is SkillProgramCardPolicyKind.DistanceOneToNotHigherHp or SkillProgramCardPolicyKind.SlashExtraTargetsByLostHp or SkillProgramCardPolicyKind.ClaimedEntitiesFaceDownUse or SkillProgramCardPolicyKind.SuppressOthersNonLockedDuringDying or SkillProgramCardPolicyKind.ProhibitBlackTrickTarget or SkillProgramCardPolicyKind.PreventForeignEquipmentDiscard or SkillProgramCardPolicyKind.OfferSkipDiscard or SkillProgramCardPolicyKind.DrawFromBottom or
            SkillProgramCardPolicyKind.PreventEnteringChain or SkillProgramCardPolicyKind.ProhibitDelayedTrickTarget or SkillProgramCardPolicyKind.ProhibitPindianTarget or
            SkillProgramCardPolicyKind.FirstActualPlayUseDistanceUnlimited or SkillProgramCardPolicyKind.PindianTopCardChoice or SkillProgramCardPolicyKind.PindianRankBySuit or
            SkillProgramCardPolicyKind.RewriteSuit or SkillProgramCardPolicyKind.EquipmentSuitHandLimitAtMaxHp or
            SkillProgramCardPolicyKind.FactionHandLimitBonus or SkillProgramCardPolicyKind.RedirectOwnTurnFactionRecovery or
            SkillProgramCardPolicyKind.ForeignPublicPileSlash or SkillProgramCardPolicyKind.PindianClaim or SkillProgramCardPolicyKind.PindianClaimAllWhenSourceWins or SkillProgramCardPolicyKind.IgnoreTurnObtainedHandCardsForDiscard or
            SkillProgramCardPolicyKind.PindianOpponentRandomHand or SkillProgramCardPolicyKind.PindianMaximumSlashClaim)        {
            if (cardKinds.Count != 0) Fail(path + ".cardKinds", "this policy does not accept card kinds");
        }
        else if (cardKinds.Count == 0 && kind is not (
                     SkillProgramCardPolicyKind.FactionResponseRequest or
                     SkillProgramCardPolicyKind.ProhibitNearbyTargetResponse or SkillProgramCardPolicyKind.ForceChained or
                     SkillProgramCardPolicyKind.ChainedHandLimitAura or SkillProgramCardPolicyKind.MarkerTurnBonuses or
                     SkillProgramCardPolicyKind.WoundedPopulationBonuses or SkillProgramCardPolicyKind.WoundedInRangeHandLimitPenalty or
                     SkillProgramCardPolicyKind.NextCardUnlimitedAfterNonLockedSkill or SkillProgramCardPolicyKind.DamageBecomesHpLoss or
                     SkillProgramCardPolicyKind.ForeignPublicPileSlash or SkillProgramCardPolicyKind.PindianClaim or SkillProgramCardPolicyKind.IgnoreTurnObtainedHandCardsForDiscard or SkillProgramCardPolicyKind.PreventForeignEquipmentDiscard))
            Fail(path + ".cardKinds", "this policy requires effective card kinds");
        if (kind == SkillProgramCardPolicyKind.CannotNullifyOwnOrdinaryTrick &&
            (cardKinds.Count == 0 || cardKinds.Any(k => CardCatalog.Get(k).CategoryName != "锦囊牌" ||
                k is CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning or CardKind.Nullification) ||
             requiredKinds.Count != 0 || value != 0 || inputSuit is not null || outputSuit is not null || ownerRole is not null || factionId is not null ||
             OptionalCondition(node, path).Kind != SkillProgramConditionKind.Always))
            Fail(path, "Cannot-nullify ordinary tricks require genuine non-delayed trick kinds and no other qualifiers; counterspells use their paid node policy.");
        if (kind is SkillProgramCardPolicyKind.RandomRevealColorFireAttack or SkillProgramCardPolicyKind.UnrespondableNullification)
        {
            var required = kind == SkillProgramCardPolicyKind.RandomRevealColorFireAttack ? CardKind.FireAttack : CardKind.Nullification;
            if (!cardKinds.SequenceEqual([required]) || requiredKinds.Count != 0 || value != 0 ||
                inputSuit is not null || outputSuit is not null || ownerRole is not null || factionId is not null ||
                OptionalCondition(node, path).Kind != SkillProgramConditionKind.Always)
                Fail(path, "Issued Fire Attack/counterspell policies require their exact kind and no other qualifiers.");
        }
        if (kind == SkillProgramCardPolicyKind.MinimumSlashResponseAtDistanceOne &&
            (cardKinds.Count != 3 || !cardKinds.ToHashSet().SetEquals([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) ||
             !requiredKinds.SequenceEqual([CardKind.Dodge]) || value != 2 || ownerRole is not null || factionId is not null ||
             inputSuit is not null || outputSuit is not null || OptionalCondition(node, path).Kind != SkillProgramConditionKind.Always))
            Fail(path, "A distance-one Slash policy requires exactly Slash-family/Dodge/minimum two without qualifiers.");
        if (kind == SkillProgramCardPolicyKind.ExclusiveTurnPeachUse && !cardKinds.SequenceEqual([CardKind.Peach]))
            Fail(path, "exclusiveTurnPeachUse requires exactly Peach");
        if (kind == SkillProgramCardPolicyKind.SlashRangeFromEffectiveRank &&
            (cardKinds.Count == 0 || cardKinds.Any(card => card is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)) ||
             requiredKinds.Count != 0 || value != 0 || inputSuit is not null || outputSuit is not null))
            Fail(path, "rank Slash range requires only Slash effective kinds and no response, suit or numeric value");
        if (kind == SkillProgramCardPolicyKind.UnlimitedAlcoholUse &&
            (!cardKinds.SequenceEqual([CardKind.Alcohol]) || requiredKinds.Count != 0 || value != 0 ||
             inputSuit is not null || outputSuit is not null))
            Fail(path, "Unlimited Alcohol requires only Alcohol effective kind and no response, suit or numeric value.");
        if (kind is SkillProgramCardPolicyKind.AlcoholKingIdentityRank or SkillProgramCardPolicyKind.ForeignTurnAlcoholUseProhibition &&
            (!cardKinds.SequenceEqual([CardKind.Alcohol]) || requiredKinds.Count != 0 || value != 0 || inputSuit is not null || outputSuit is not null ||
             factionId is not null || ownerRole is not null || OptionalCondition(node, path).Kind != SkillProgramConditionKind.Always))
            Fail(path, "Alcohol identity/rank and foreign-turn use prohibition require exactly Alcohol and no other qualifiers.");
        if (kind == SkillProgramCardPolicyKind.VirtualEquipment &&
            cardKinds.Any(card => !EquipmentCatalog.IsEquipment(card)))
            Fail(path, "virtualEquipment requires equipment card kinds");
        var discardCost = node.TryGetProperty("discardCost", out _) ? RequiredInt(node, "discardCost", path) : 0;
        var providerDrawCount = node.TryGetProperty("providerDrawCount", out _) ? RequiredInt(node, "providerDrawCount", path) : 0;
        if (discardCost is < 0 or > 1 || providerDrawCount is < 0 or > 20 ||
            (node.TryGetProperty("discardCost", out _) || node.TryGetProperty("providerDrawCount", out _)) &&
            (kind != SkillProgramCardPolicyKind.RedirectOwnTurnFactionRecovery &&
             (kind != SkillProgramCardPolicyKind.FactionResponseRequest || !requiredKinds.SequenceEqual([CardKind.Slash]))))
            Fail(path, "Faction request payment and reward require a Slash request and bounded amounts.");
        if (kind == SkillProgramCardPolicyKind.RedirectOwnTurnFactionRecovery &&
            (discardCost != 0 || providerDrawCount != 1))
            Fail(path, "Own-turn faction recovery replacement rewards exactly one card and charges no payment.");
        var policy = new SkillProgramCardPolicy(id, kind, cardKinds, requiredKinds, value,
            inputSuit, outputSuit, OptionalCondition(node, path), factionId, ownerRole)
        { DiscardCost = discardCost, ProviderDrawCount = providerDrawCount };
        GameEngine.ValidateComparedBlackSlashPolicy(path, policy);
        return policy;
    }

    private static SkillProgramDamageModifier ParseDamageModifier(JsonElement node, string path)
    {
        RequireObject(node, path);
        CheckProperties(node, path, "id", "cardKinds", "amount", "condition", "sourceScope");
        var kinds = EnumArray<CardKind>(node, "cardKinds", path);
        if (kinds.Distinct().Count() != kinds.Count ||
            (kinds.Count == 0 && !(
                EnumValue<SkillProgramDamageModifierCondition>(node, "condition", path) is
                SkillProgramDamageModifierCondition.OwnerUniqueMaximumHand or SkillProgramDamageModifierCondition.ChainedFirePropagationOrigin
                    or SkillProgramDamageModifierCondition.FaceStatesDiffer)))
            Fail(path + ".cardKinds", "must contain distinct effective card kinds");
        var amount = PositiveInt(node, "amount", path);
        if (amount > 20) Fail(path + ".amount", "must not exceed 20");
        var condition = EnumValue<SkillProgramDamageModifierCondition>(node, "condition", path);
        var scope = node.TryGetProperty("sourceScope", out _)
            ? EnumValue<SkillProgramDamageModifierSourceScope>(node, "sourceScope", path)
            : SkillProgramDamageModifierSourceScope.OwnerUsed;
        if (condition is SkillProgramDamageModifierCondition.OwnerUniqueMaximumHand or SkillProgramDamageModifierCondition.ChainedFirePropagationOrigin)
        {
            if (scope != SkillProgramDamageModifierSourceScope.DamageParticipant || kinds.Count != 0)
                Fail(path, "unique-maximum-hand damage requires damageParticipant and every damage kind");
        }
        else if (condition is SkillProgramDamageModifierCondition.FaceStatesDiffer)
        {
            if (scope != SkillProgramDamageModifierSourceScope.DamageParticipant)
                Fail(path + ".sourceScope", "face-state damage modifiers require damageParticipant");
        }
        else if (scope != SkillProgramDamageModifierSourceScope.OwnerUsed)
            Fail(path + ".sourceScope", "card damage modifiers require ownerUsed");
        return new SkillProgramDamageModifier(Identifier(node, "id", path), kinds, amount, condition, scope);
    }

    private static SkillProgramModifier ParseModifier(JsonElement node, string path)
    {
        RequireObject(node, path);
        CheckProperties(node, path, "id", "query", "operation", "value", "valueExpression", "valueZone", "valueMarker",
            "priority", "sourceCardIdentityId", "cardKinds", "condition");
        var id = Identifier(node, "id", path);
        var query = EnumValue<SkillRuleQuery>(node, "query", path);
        var operation = EnumValue<SkillRuleOperation>(node, "operation", path);
        var hasValue = node.TryGetProperty("value", out _);
        var hasValueExpression = node.TryGetProperty("valueExpression", out _);
        if (hasValue == hasValueExpression)
            Fail(path, "exactly one of value or valueExpression is required");
        var value = hasValue ? RequiredInt(node, "value", path) : 0;
        SkillRuleValueExpression? valueExpression = hasValueExpression
            ? EnumValue<SkillRuleValueExpression>(node, "valueExpression", path)
            : null;
        var valueZone = node.TryGetProperty("valueZone", out _)
            ? EnumValue<CardZoneKind>(node, "valueZone", path) : (CardZoneKind?)null;
        if (valueExpression == SkillRuleValueExpression.PublicLivingFactionCount &&
            (query != SkillRuleQuery.HandLimit || operation != SkillRuleOperation.Add))
            Fail(path, "public living factions require an additive hand-limit modifier");
        if (valueExpression is SkillRuleValueExpression.OwnedZoneCount or SkillRuleValueExpression.NegatedOwnedZoneCount)
        {
            if (valueZone is not (CardZoneKind.WoodenOxGrain or CardZoneKind.BuquWound or
                    CardZoneKind.Authority or CardZoneKind.Chunlao or CardZoneKind.PublicPersistentPile))
                Fail(path + ".valueZone", "ownedZoneCount requires a persistent owner zone");
        }
        else if (valueZone is not null)
            Fail(path + ".valueZone", "is accepted only by ownedZoneCount");
        var valueMarker = node.TryGetProperty("valueMarker", out _) ? EnumValue<PlayerMarkerKind>(node, "valueMarker", path) : (PlayerMarkerKind?)null;
        if ((valueExpression == SkillRuleValueExpression.OwnerMarkerCount) != (valueMarker is not null))
            Fail(path + ".valueMarker", "ownerMarkerCount requires exactly one explicit marker parameter");
        var priority = RequiredInt(node, "priority", path);
        if (priority is < -1000 or > 1000)
            Fail(path + ".priority", "must be between -1000 and 1000");
        var sourceCardIdentityId = node.TryGetProperty("sourceCardIdentityId", out _)
            ? Identifier(node, "sourceCardIdentityId", path)
            : null;
        var cardKinds = node.TryGetProperty("cardKinds", out _)
            ? EnumArray<CardKind>(node, "cardKinds", path)
            : [];
        if (value is < -1024 or > 1024)
            Fail(path + ".value", "modifier value must be between -1024 and 1024");
        if (operation == SkillRuleOperation.Add && value == 0)
        {
            if (valueExpression is null) Fail(path + ".value", "add requires a non-zero value");
        }
        if (operation is SkillRuleOperation.Add or SkillRuleOperation.Unlimited && priority != 0)
            Fail(path + ".priority", "add and unlimited modifiers require priority 0");
        if (valueExpression is (SkillRuleValueExpression.NegatedOwnedZoneCount or SkillRuleValueExpression.NegatedOwnerLostHp) &&
            (operation != SkillRuleOperation.Add || query != SkillRuleQuery.OutgoingDistance))
            Fail(path + ".valueExpression",
                "negatedOwnedZoneCount is currently supported only by additive outgoingDistance modifiers");
        if (valueExpression is not null && valueExpression is not (SkillRuleValueExpression.NegatedOwnedZoneCount or SkillRuleValueExpression.NegatedOwnerLostHp) &&
            (operation != SkillRuleOperation.Add || query != SkillRuleQuery.HandLimit &&
                !(query == SkillRuleQuery.CardTargetCount && valueExpression == SkillRuleValueExpression.OwnerLostHp)))
            Fail(path + ".valueExpression",
                "livingFactionCount is currently supported only by additive handLimit modifiers");
        if (operation == SkillRuleOperation.Unlimited)
        {
            if (query is not (SkillRuleQuery.SlashLimit or SkillRuleQuery.SlashDistanceLimit or
                    SkillRuleQuery.AttackRange))
                Fail(path, "unlimited is supported only for slashLimit, slashDistanceLimit or attackRange");
            if (value != 0) Fail(path + ".value", "unlimited requires value 0");
        }
        if (query == SkillRuleQuery.SlashDistanceLimit)
        {
            if (operation != SkillRuleOperation.Unlimited)
                Fail(path + ".operation", "slashDistanceLimit currently requires unlimited");
            if (sourceCardIdentityId is null)
                Fail(path, "slashDistanceLimit requires sourceCardIdentityId");
        }
        else if (sourceCardIdentityId is not null && query != SkillRuleQuery.SlashLimit)
            Fail(path + ".sourceCardIdentityId", "is supported only for slashDistanceLimit");
        if (query == SkillRuleQuery.CardUseDistanceLimit)
        {
            if (operation != SkillRuleOperation.Add || valueExpression is not null || cardKinds.Count == 0)
                Fail(path, "cardUseDistanceLimit requires fixed additive value and effective card kinds");
        }
        else if (query == SkillRuleQuery.CardTargetCount)
        {
            var lostHpSlashTargets = valueExpression == SkillRuleValueExpression.OwnerLostHp && value == 0 &&
                cardKinds.Count > 0 && cardKinds.All(kind => kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash);
            if (operation != SkillRuleOperation.Add || !lostHpSlashTargets && (value <= 0 || valueExpression is not null))
                Fail(path, "cardTargetCount requires a positive fixed modifier or an additive ownerLostHp Slash modifier");
            if (cardKinds.Count == 0)
                Fail(path + ".cardKinds", "cardTargetCount requires at least one effective card kind");
        }
        else if (cardKinds.Count != 0)
            Fail(path + ".cardKinds", "is supported only for cardTargetCount or cardUseDistanceLimit");
        return new SkillProgramModifier(id, query, operation, value, valueExpression, valueZone, priority,
            sourceCardIdentityId, cardKinds, OptionalCondition(node, path)) { ValueMarker = valueMarker };
    }

    private static SkillProgramCardIdentity ParseCardIdentity(JsonElement node, string path)
    {
        RequireObject(node, path);
        CheckProperties(node, path, "id", "zones", "inputKinds", "inputSuits", "outputKind", "condition");
        var id = Identifier(node, "id", path);
        var zones = EnumArray<CardZoneKind>(node, "zones", path);
        if (zones.Count != 1 || zones[0] != CardZoneKind.Hand)
            Fail(path + ".zones", "card identities require exactly the owner hand zone");
        var inputs = EnumArray<CardKind>(node, "inputKinds", path);
        var suits = EnumArray<Suit>(node, "inputSuits", path);
        if (inputs.Count == 0 && suits.Count == 0)
            Fail(path, "must filter at least one physical card kind or suit");
        var output = EnumValue<CardKind>(node, "outputKind", path);
        if (output is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.Dodge))
            Fail(path + ".outputKind", "card identities support only slash or dodge");
        return new SkillProgramCardIdentity(id, zones, inputs, suits, output,
            OptionalCondition(node, path));
    }

    private static void ValidateCardIdentityModifiers(
        string path,
        IReadOnlyList<SkillProgramModifier> modifiers,
        IReadOnlyList<SkillProgramCardIdentity> identities)
    {
        foreach (var modifier in modifiers.Where(item => item.SourceCardIdentityId is not null))
        {
            var identity = identities.SingleOrDefault(item => item.Id == modifier.SourceCardIdentityId);
            if (identity is null)
                Fail(path + ".modifiers", $"references unknown card identity '{modifier.SourceCardIdentityId}'");
            if (identity.OutputKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash))
                Fail(path + ".modifiers", "slashDistanceLimit requires a card identity that outputs slash");
        }
    }

    private static SkillProgramViewAs ParseViewAs(JsonElement node, string path)
    {
        RequireObject(node, path);
        CheckProperties(node, path, "id", "inputKinds", "inputSuits", "inputCategories", "inputCount", "sourceZones",
            "outputKind", "forPlay", "forResponse", "allowChainedInput", "sameSuit", "condition",
            "usesPerPhase", "usageGroup", "inheritPreviousPlaySuit", "extendedUse", "damageBonus", "recoveryBonus",
            "costDestination", "unusedOutputThisTurn", "useOnly", "singleCardTrickUse", "excludeOwnerEffects", "conversionStateId", "minimumTier", "maximumTier", "declaredEntity", "activationUsageGroup", "allowSameKind", "noDying", "unusedOutputNameThisGame", "nameLedgerId", "declarationValidation", "useEffectiveInputSuit", "distanceUnlimited", "tieredRoundConversion");
        var tieredRound = node.TryGetProperty("tieredRoundConversion", out var tieredRoundNode)
            ? TieredRoundConversionSchema.Parse(tieredRoundNode, path + ".tieredRoundConversion") : null;
        SkillProgramDeclarationValidation? declaration = null;
        if (node.TryGetProperty("declarationValidation", out var declarationNode))
        {
            RequireObject(declarationNode, path + ".declarationValidation");
            CheckProperties(declarationNode, path + ".declarationValidation", "challengeGrantSkillId");
            declaration = new(Identifier(declarationNode, "challengeGrantSkillId", path + ".declarationValidation"));
        }
        var allowSameKind = node.TryGetProperty("allowSameKind", out _) && RequiredBool(node, "allowSameKind", path);
        var noDying = node.TryGetProperty("noDying", out _) && RequiredBool(node, "noDying", path);
        var unusedName = node.TryGetProperty("unusedOutputNameThisGame", out _) && RequiredBool(node, "unusedOutputNameThisGame", path);
        var nameLedger = node.TryGetProperty("nameLedgerId", out _) ? Identifier(node, "nameLedgerId", path) : null;
        if (unusedName != (nameLedger is not null)) Fail(path, "unusedOutputNameThisGame requires exactly one nameLedgerId");
        var conversionState = node.TryGetProperty("conversionStateId", out _) ? Identifier(node, "conversionStateId", path) : null;
        var minimumTier = node.TryGetProperty("minimumTier", out _) ? NonNegativeInt(node, "minimumTier", path) : 0;
        var maximumTier = node.TryGetProperty("maximumTier", out _) ? NonNegativeInt(node, "maximumTier", path) : 2;
        var declaredEntity = node.TryGetProperty("declaredEntity", out _) && RequiredBool(node, "declaredEntity", path);
        var activationGroup = node.TryGetProperty("activationUsageGroup", out _) ? Identifier(node, "activationUsageGroup", path) : null;
        if (minimumTier > maximumTier || maximumTier > 2 ||
            conversionState is null && (declaredEntity || minimumTier != 0 || maximumTier != 2 || activationGroup is not null))
            Fail(path, "bounded conversion tiers require conversionStateId");
        var singleTrick = node.TryGetProperty("singleCardTrickUse", out _) && RequiredBool(node, "singleCardTrickUse", path);
        var excludeOwner = node.TryGetProperty("excludeOwnerEffects", out _) && RequiredBool(node, "excludeOwnerEffects", path);
        var extended = node.TryGetProperty("extendedUse", out _) && RequiredBool(node, "extendedUse", path);
        var useOnly = node.TryGetProperty("useOnly", out _) && RequiredBool(node, "useOnly", path);
        var unusedOutputThisTurn = node.TryGetProperty("unusedOutputThisTurn", out _) && RequiredBool(node, "unusedOutputThisTurn", path);
        SkillProgramCardDestination? costDestination = node.TryGetProperty("costDestination", out _)
            ? EnumValue<SkillProgramCardDestination>(node, "costDestination", path) : null;
        var id = Identifier(node, "id", path);
        var inputs = EnumArray<CardKind>(node, "inputKinds", path);
        var inputCategories = node.TryGetProperty("inputCategories", out _)
            ? EnumArray<SkillProgramCardCategory>(node, "inputCategories", path)
            : [];
        var suits = EnumArray<Suit>(node, "inputSuits", path);
        var variableInput = node.TryGetProperty("inputCount", out var inputCountNode) &&
                            inputCountNode.ValueKind == JsonValueKind.Null;
        var inputCount = variableInput ? 1
            : node.TryGetProperty("inputCount", out _)
                ? tieredRound is null ? PositiveInt(node, "inputCount", path) : NonNegativeInt(node, "inputCount", path) : 1;
        if (inputCount > 64) Fail(path + ".inputCount", "must not exceed 64");
        var distanceUnlimited = node.TryGetProperty("distanceUnlimited", out _) &&
                                RequiredBool(node, "distanceUnlimited", path);
        var sourceZones = node.TryGetProperty("sourceZones", out _)
            ? EnumArray<CardZoneKind>(node, "sourceZones", path)
            : [CardZoneKind.Hand];
        if (sourceZones.Count == 0 && !(tieredRound is not null && inputCount == 0) || sourceZones.Any(zone =>
                zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Authority or CardZoneKind.WoodenOxGrain)))
            Fail(path + ".sourceZones", "viewAs accepts hand, equipment or authority sources only");
        if (inputCount > 1 && !extended && !sourceZones.SequenceEqual([CardZoneKind.Hand]))
            Fail(path + ".sourceZones", "multi-card viewAs currently accepts hand cards only");
        var output = EnumValue<CardKind>(node, "outputKind", path);
        if (tieredRound is null && !singleTrick && output is not (CardKind.Slash or CardKind.Dodge or CardKind.FireSlash or CardKind.ThunderSlash or
                CardKind.Dismantlement or CardKind.SupplyShortage or CardKind.Indulgence or
                CardKind.IronChain or CardKind.FireAttack or CardKind.Nullification or CardKind.Peach or
                CardKind.ArrowBarrage or CardKind.Alcohol or CardKind.Snatch))
            Fail(path + ".outputKind", "this card kind has no configured viewAs use or response path");
        var forPlay = RequiredBool(node, "forPlay", path);
        var forResponse = RequiredBool(node, "forResponse", path);
        if (tieredRound is null && singleTrick && (inputCount != 1 || !forPlay || forResponse || output is not
            (CardKind.DrawTwo or CardKind.Duel or CardKind.BarbarianAssault or CardKind.ArrowBarrage or
             CardKind.PeachGarden or CardKind.FiveGrains or CardKind.Dismantlement or CardKind.Snatch or
             CardKind.FireAttack or CardKind.IronChain or CardKind.BorrowedSword)))
            Fail(path + ".singleCardTrickUse", "requires one physical source and an ordinary trick for play");
        if (tieredRound is null && useOnly && !singleTrick && !((conversionState is not null || unusedName) && output == CardKind.Nullification && !forPlay && forResponse && inputCount == 1) && (inputCount != 1 ||
            (output == CardKind.Dodge ? forPlay || !forResponse :
             !forPlay || forResponse || output is not
                (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.Peach or CardKind.Alcohol))))
            Fail(path + ".useOnly", "requires one physical cost and a legal basic-card use direction");
        if (unusedName && (!useOnly || inputCount != 1 ||
            CardUseCategoryCatalog.Get(output) is not (CardUseCategories.Basic or CardUseCategories.InstantTrick)))
            Fail(path, "game name ledgers require one physical use-only basic or ordinary trick output");
        if (costDestination is not null && (costDestination != SkillProgramCardDestination.DrawPileTop || !useOnly))
            Fail(path + ".costDestination", "requires drawPileTop and a useOnly basic-card conversion");
        if (costDestination is not null && sourceZones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)))
            Fail(path + ".sourceZones", "alternative-cost viewAs requires a physical hand or equipment source");
        if (unusedOutputThisTurn && !useOnly)
            Fail(path + ".unusedOutputThisTurn", "requires a useOnly basic-card conversion");
        var allowChainedInput = node.TryGetProperty("allowChainedInput", out _) &&
                                RequiredBool(node, "allowChainedInput", path);
        var sameSuit = node.TryGetProperty("sameSuit", out _) && RequiredBool(node, "sameSuit", path);
        int? usesPerPhase = node.TryGetProperty("usesPerPhase", out _) ? PositiveInt(node, "usesPerPhase", path) : null;
        if (usesPerPhase > 1024) Fail(path + ".usesPerPhase", "must not exceed 1024");
        var usageGroup = node.TryGetProperty("usageGroup", out _) ? Identifier(node, "usageGroup", path) : null;
        var inheritPreviousPlaySuit = node.TryGetProperty("inheritPreviousPlaySuit", out _) &&
                                     RequiredBool(node, "inheritPreviousPlaySuit", path);
        if (inheritPreviousPlaySuit && usesPerPhase is null)
            Fail(path + ".inheritPreviousPlaySuit", "requires usesPerPhase");
        if (usageGroup is not null && usesPerPhase is null)
            Fail(path + ".usageGroup", "requires usesPerPhase");
        if (!singleTrick && !(conversionState is not null && useOnly && !forPlay && forResponse && output is CardKind.Dodge or CardKind.Nullification) && (usesPerPhase is not null || inheritPreviousPlaySuit) &&
            (!forPlay || forResponse || inputCount != 1 || output is not
                (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.Peach or CardKind.Alcohol)))
            Fail(path, "phase-limited viewAs requires one basic-card input for play only");
        if (activationGroup is not null && (usesPerPhase is null || declaredEntity)) Fail(path, "activationUsageGroup requires a phase-limited direct conversion");
        if (!forPlay && !forResponse) Fail(path, "at least one of forPlay or forResponse must be true");
        if (output == CardKind.Snatch && forResponse)
            Fail(path + ".forResponse", "snatch viewAs is play-only");
        if (output == CardKind.Snatch && inputCount > 1)
            Fail(path + ".inputCount", "snatch viewAs accepts one physical input only");
        if (output == CardKind.Dodge && forPlay)
            Fail(path + ".forPlay", "dodge is response-only and cannot be played proactively");
        if (inputCount > 1 && !extended && output is not (CardKind.Slash or CardKind.Dodge or CardKind.ArrowBarrage))
            Fail(path + ".inputCount", "multi-card viewAs has no use or response executor for this output kind");
        if (sameSuit && inputCount < 2)
            Fail(path + ".sameSuit", "sameSuit requires multiple physical inputs");
        if (tieredRound is null && !singleTrick && output == CardKind.ArrowBarrage &&
            (!sameSuit || inputCount != 2 || !forPlay || forResponse))
            Fail(path, "arrowBarrage viewAs requires two same-suit hand cards for play only");
        if (inputCount > 1 && inputCategories.Count != 0)
            Fail(path + ".inputCategories", "multi-card viewAs does not support category input filters");
        if (output is CardKind.Dismantlement or CardKind.SupplyShortage or CardKind.Indulgence or
                CardKind.IronChain or CardKind.FireAttack && forResponse)
            Fail(path + ".forResponse", "this trick cannot be used as a response");
        if (output == CardKind.Nullification && forPlay)
            Fail(path + ".forPlay", "nullification is only legal in the counterspell response window");
        if (output == CardKind.Peach && forPlay && usesPerPhase is null && !extended && !useOnly)
            Fail(path + ".forPlay", "proactive peach has no configured viewAs executor");
        if (output == CardKind.ThunderSlash && usesPerPhase is null && !useOnly && declaration is null)
            Fail(path + ".outputKind", "thunderSlash requires phase-limited play viewAs");
        if (output == CardKind.FireSlash && usesPerPhase is null && !extended && !useOnly)
        {
            if (inputCount == 1 && inputs.Count > 0 && suits.Count == 0)
            {
                if (inputs.Any(kind => kind is not (CardKind.Slash or CardKind.ThunderSlash)) || forResponse)
                    Fail(path + ".forResponse",
                        "fireSlash viewAs currently requires one physical slash for play only");
            }
            else if (inputs.Count == 0 && suits.Count == 1)
            {
                if (!forPlay || forResponse)
                    Fail(path + ".forResponse",
                        "fireSlash viewAs suited input is play-only");
            }
            else
            {
                Fail(path,
                    "fireSlash viewAs currently requires one physical slash for play only");
            }
        }
        if (allowChainedInput &&
            (!forPlay || forResponse || inputCount != 1 || output != CardKind.FireSlash))
            Fail(path + ".allowChainedInput", "chained viewAs currently supports one input for fireSlash play only");
        if (!allowSameKind && inputs.Count > 0 && inputs.All(kind => kind == output))
            Fail(path + ".inputKinds", "viewAs must change at least one accepted input kind");
        if (declaration is not null && (inputCount != 1 || !sourceZones.SequenceEqual([CardZoneKind.Hand]) ||
            CardUseCategoryCatalog.Get(output) is not (CardUseCategories.Basic or CardUseCategories.InstantTrick) ||
            costDestination is not null || conversionState is not null || usesPerPhase is not null || unusedName || useOnly ||
            allowChainedInput || sameSuit || inheritPreviousPlaySuit || unusedOutputThisTurn))
            Fail(path + ".declarationValidation", "requires one hand entity and a basic or ordinary-trick output without another cost or usage mechanism");
        if (variableInput && (!forPlay || forResponse || output is not CardKind.Slash ||
                !sourceZones.SequenceEqual([CardZoneKind.Hand]) || sameSuit || allowChainedInput ||
                inputCategories.Count != 0 || inputs.Count != 0 && inputs.All(kind => kind == output) ||
                singleTrick || extended || useOnly || unusedName || excludeOwner || noDying ||
                usesPerPhase is not null || inheritPreviousPlaySuit || conversionState is not null ||
                costDestination is not null || declaration is not null || activationGroup is not null))
            Fail(path, "variable-input viewAs supports plain hand-card slash conversions for play only");
        if (distanceUnlimited && !variableInput)
            Fail(path + ".distanceUnlimited", "requires a variable-input conversion");
        var parsedViewAs = new SkillProgramViewAs(id, inputs, suits, output, forPlay, forResponse,
            OptionalCondition(node, path), inputCount, sourceZones, allowChainedInput, inputCategories, sameSuit,
            usesPerPhase, usageGroup, inheritPreviousPlaySuit)
        { TieredRoundConversion = tieredRound, UseEffectiveInputSuit = node.TryGetProperty("useEffectiveInputSuit", out _) ? RequiredBool(node, "useEffectiveInputSuit", path) : null, DeclarationValidation = declaration, AllowSameKind = allowSameKind, NoDying = noDying, UnusedOutputNameThisGame = unusedName, NameLedgerId = nameLedger, ConversionStateId = conversionState, MinimumTier = minimumTier, MaximumTier = maximumTier, DeclaredEntity = declaredEntity, ActivationUsageGroup = activationGroup, SingleCardTrickUse = singleTrick, ExcludeOwnerEffects = excludeOwner, ExtendedUse = extended, UseOnly = useOnly, UnusedOutputThisTurn = unusedOutputThisTurn, CostDestination = costDestination,
          VariableInputCount = variableInput, DistanceUnlimited = distanceUnlimited,
          DamageBonus = node.TryGetProperty("damageBonus", out _) ? NonNegativeInt(node, "damageBonus", path) : 0,
          RecoveryBonus = node.TryGetProperty("recoveryBonus", out _) ? NonNegativeInt(node, "recoveryBonus", path) : 0 };
        TieredRoundConversionSchema.Validate(parsedViewAs, path);
        return parsedViewAs;
    }

    private static SkillProgramActivation ParseActivation(JsonElement node, string path)
    {
        RequireObject(node, path);
        CheckProperties(node, path, "id", "minCards", "maxCards", "sourceZones", "minTargets", "maxTargets",
            "targetKind", "usesPerTurn", "usesPerPhase", "usesPerGame", "condition", "effects",
            "selectedCardsSameSuit", "equipmentSlots", "usageGroup", "targetRequiresEmptyEquipmentSlot", "markerCost", "continueAfterOwnerDeath", "selectedCardsDistinctSuits", "cardCountExpression", "cardKinds", "cardSuits", "cardCategories", "categoryTargetLedgerId", "targetPhaseLedgerId");
        var id = Identifier(node, "id", path);
        var minCards = NonNegativeInt(node, "minCards", path);
        var allAvailableCards = node.TryGetProperty("maxCards", out var maximumNode) &&
                                maximumNode.ValueKind == JsonValueKind.Null;
        var maxCards = allAvailableCards ? int.MaxValue : NonNegativeInt(node, "maxCards", path);
        var minTargets = NonNegativeInt(node, "minTargets", path);
        var maxTargets = NonNegativeInt(node, "maxTargets", path);
        if (minCards > maxCards) Fail(path, "minCards cannot exceed maxCards");
        if (!allAvailableCards && maxCards > 64) Fail(path + ".maxCards", "must not exceed 64; null accepts all available cards");
        if (minTargets > maxTargets) Fail(path, "minTargets cannot exceed maxTargets");
        var targetKind = EnumValue<SkillProgramTargetKind>(node, "targetKind", path);
        if (maxTargets > 16)
            Fail(path + ".maxTargets", "must not exceed the bounded player selection limit of 16");
        if (maxTargets > 1 && targetKind == SkillProgramTargetKind.EventTarget)
            Fail(path + ".targetKind", "event targets cannot be selected by an active command");
        int? uses = null;
        if (node.TryGetProperty("usesPerTurn", out var usesNode))
        {
            if (usesNode.ValueKind == JsonValueKind.Null) uses = null;
            else { uses = GetInt(usesNode, path + ".usesPerTurn"); if (uses <= 0) Fail(path + ".usesPerTurn", "must be positive or null"); }
        }
        else Fail(path, "missing required property 'usesPerTurn'");
        int? usesPerPhase = null;
        if (node.TryGetProperty("usesPerPhase", out var phaseUsesNode) &&
            phaseUsesNode.ValueKind != JsonValueKind.Null)
        {
            usesPerPhase = GetInt(phaseUsesNode, path + ".usesPerPhase");
            if (usesPerPhase <= 0) Fail(path + ".usesPerPhase", "must be positive or null");
        }
        int? usesPerGame = null;
        if (node.TryGetProperty("usesPerGame", out var gameUsesNode) &&
            gameUsesNode.ValueKind != JsonValueKind.Null)
        {
            usesPerGame = GetInt(gameUsesNode, path + ".usesPerGame");
            if (usesPerGame <= 0) Fail(path + ".usesPerGame", "must be positive or null");
        }
        var sourceZones = node.TryGetProperty("sourceZones", out _)
            ? EnumArray<CardZoneKind>(node, "sourceZones", path)
            : Array.AsReadOnly(new[] { CardZoneKind.Hand });
        if (sourceZones.Count == 0 || sourceZones.Any(zone => zone is not
                (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain or
                 CardZoneKind.BuquWound or CardZoneKind.Authority or CardZoneKind.Chunlao)))
            Fail(path + ".sourceZones", "must contain owner-scoped selectable zones");
        var selectedCardsSameSuit = node.TryGetProperty("selectedCardsSameSuit", out _) &&
                                    RequiredBool(node, "selectedCardsSameSuit", path);
        if (selectedCardsSameSuit && (minCards < 2 || maxCards != minCards))
            Fail(path + ".selectedCardsSameSuit", "requires an exact selection of at least two cards");
        var equipmentSlots = node.TryGetProperty("equipmentSlots", out _)
            ? EnumArray<EquipmentSlot>(node, "equipmentSlots", path) : Array.Empty<EquipmentSlot>();
        if (equipmentSlots.Count > 0 && (maxCards == 0 ||
            sourceZones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment))))
            Fail(path + ".equipmentSlots", "requires selected owner hand or equipment cards");
        var targetRequiresEmptyEquipmentSlot = node.TryGetProperty("targetRequiresEmptyEquipmentSlot", out _) &&
                                                RequiredBool(node, "targetRequiresEmptyEquipmentSlot", path);
        if (targetRequiresEmptyEquipmentSlot && (maxTargets - minTargets is not 0 || maxTargets != 1 ||
                maxCards != 0))
            Fail(path + ".targetRequiresEmptyEquipmentSlot",
                "requires exactly one selected target and effect-level card selection");
        var effects = ReadArray<SkillProgramEffect>(node, "effects", path, (effect, effectPath) =>
            ParseCompositionEffect(effect, effectPath));
        if (effects.SelectMany(EnumerateParticipantReferences).Any(reference =>
                reference.Kind is ProgramParticipantRef.EventTarget or ProgramParticipantRef.EventSource))
            Fail(path + ".effects", "event participants require a trigger window");
        if (effects.Any(effect => ContainsCardUseColorCondition(effect.Condition)))
            Fail(path + ".effects", "card-use color conditions require a card-action trigger");
        if (effects.Any(effect => effect.Op == SkillProgramEffectOp.SkipTurnPhases))
            Fail(path + ".effects", "phase substitution requires a lifecycle trigger");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.DiscardTargetHpCardsAndDamage) &&
            (effects.Count != 1 || minCards != 0 || maxCards != 0 || minTargets != 1 || maxTargets != 1 ||
             targetKind != SkillProgramTargetKind.OtherLivingInAttackRange || uses is not null || usesPerPhase is not null || usesPerGame is not null))
            Fail(path, "Variable target-HP discard damage requires one standalone zero-input, attack-range-target activation.");
        if (effects.Count == 0) Fail(path + ".effects", "must contain at least one effect");
        PrivateOfferComposition.ValidateActivation(path, effects, minCards, maxCards, minTargets, maxTargets, targetKind,
            uses, usesPerPhase, usesPerGame, node.TryGetProperty("continueAfterOwnerDeath", out var privateOfferDeath) && privateOfferDeath.GetBoolean());
        if (effects.Any(e => e.Op == SkillProgramEffectOp.ScheduleFirstRoundGameUsageRefund) && usesPerGame != 1)
            Fail(path, "a first-round refund requires usesPerGame:1");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.SelectEquipmentPairAndPayment) &&
            (minCards != 0 || maxCards != 0 || minTargets != 0 || maxTargets != 0 || usesPerPhase != 1 ||
             uses is not null || usesPerGame is not null || targetKind != SkillProgramTargetKind.AnyLiving ||
             node.TryGetProperty("continueAfterOwnerDeath", out var pairDeath) && pairDeath.GetBoolean()))
            Fail(path, "a paid equipment pair requires one use per phase, zero initial cards/targets and normal source lifetime");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.ChooseCategoryOrSequentialDiscard) &&
            (minCards != 1 || maxCards != 1 || minTargets != 1 || maxTargets != 1 || usesPerPhase != 1 ||
             uses is not null || usesPerGame is not null || targetKind != SkillProgramTargetKind.OtherLivingWithHand ||
             node.TryGetProperty("continueAfterOwnerDeath", out var sequentialDeath) && sequentialDeath.GetBoolean()))
            Fail(path, "a category/sequential challenge requires one real hand-trick cost, one other hand-bearing target and one use per play phase");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.EscalatingDiscardOrDamageFromSelected) &&
            (minCards != 0 || maxCards != 0 || minTargets != 1 || maxTargets != 1 || usesPerGame != 1 ||
             uses is not null || usesPerPhase is not null || targetKind != SkillProgramTargetKind.OtherLiving ||
             node.TryGetProperty("continueAfterOwnerDeath", out var ringDeath) && ringDeath.GetBoolean()))
            Fail(path, "a chosen-start escalating discard requires one standalone other target and one exact original game usage");
        if (targetKind == SkillProgramTargetKind.OtherLivingHandAtMostOwner)
            Fail(path, "at-most-owner hand targeting is only supported by its Ending selection composition");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.ExchangeHandsAndArmPhaseDebt) &&
            (minCards != 0 || maxCards != 0 || minTargets != 2 || maxTargets != 2 || targetKind != SkillProgramTargetKind.OtherLivingPair ||
             usesPerPhase != 1 || uses is not null || usesPerGame is not null ||
             node.TryGetProperty("continueAfterOwnerDeath", out var phaseExchangeDeath) && phaseExchangeDeath.GetBoolean()))
            Fail(path, "a deferred hand exchange requires a zero-card original other pair and one actual Play-phase usage");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.ObtainOneFromEachSelectedTarget) &&
            (minCards != 0 || maxCards != 0 || minTargets != 2 || maxTargets != 2 || targetKind != SkillProgramTargetKind.LivingPairDistinct ||
             usesPerPhase != 1 || uses is not null || usesPerGame is not null ||
             node.TryGetProperty("continueAfterOwnerDeath", out var pairObtainAfterDeath) && pairObtainAfterDeath.GetBoolean()))
            Fail(path, "ordered pair obtain requires two original living targets, zero cards and one actual Play-phase usage");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.RecastSelectedPhysicalSlash) &&
            (minCards != 1 || maxCards != 1 || minTargets != 0 || maxTargets != 0 || uses is not null || usesPerGame is not null || usesPerPhase is not null ||
             !sourceZones.SequenceEqual([CardZoneKind.Hand]) || targetKind != SkillProgramTargetKind.AnyLiving ||
             node.TryGetProperty("continueAfterOwnerDeath", out var slashRecastAfterDeath) && slashRecastAfterDeath.GetBoolean()))
            Fail(path, "physical Slash recast requires an unrestricted one-hand-card zero-target active program");
        if (effects.Any(PaidHpLossProgram.IsOperation) && (usesPerGame != 1 || minCards != 0 || maxCards != 0 || minTargets != 0 || maxTargets != 0))
            Fail(path, "Paid HP loss requires a limited one-use zero-card zero-target activation.");
        SkillProgramCardCountExpression? cardCountExpression = node.TryGetProperty("cardCountExpression", out _)
            ? EnumValue<SkillProgramCardCountExpression>(node, "cardCountExpression", path) : null;
        if (cardCountExpression is not null && (minCards != 1 || maxCards != int.MaxValue ||
            selectedCardsSameSuit || node.TryGetProperty("selectedCardsDistinctSuits", out var distinctNode) && distinctNode.GetBoolean() ||
            node.TryGetProperty("markerCost", out _) || effects.Any(effect => effect.Op is SkillProgramEffectOp.Pindian or
                SkillProgramEffectOp.StartPindian or SkillProgramEffectOp.UseSelectedCardsAs or SkillProgramEffectOp.UseAllHandCardsAsOrdinaryTrick)))
            Fail(path + ".cardCountExpression", "requires open card bounds from one and no fixed suit, marker or Pindian cost");
        if (node.TryGetProperty("categoryTargetLedgerId", out _) &&
            (minCards != 1 || maxCards != 1 || minTargets != 1 || maxTargets != 1 ||
             !sourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) ||
             effects.FirstOrDefault() is not { Op: SkillProgramEffectOp.ConsumeCategoryTargetLedger } ledgerEffect ||
             ledgerEffect.StateId != Identifier(node, "categoryTargetLedgerId", path) ||
             Identifier(node, "categoryTargetLedgerId", path) != id))
            Fail(path + ".categoryTargetLedgerId", "requires one owned card, one other target and a matching first ledger instruction");
        if (node.TryGetProperty("targetPhaseLedgerId", out _) &&
            ((minCards != 0 || maxCards != 0) && !effects.Any(e => e.Op == SkillProgramEffectOp.AccumulatePaidPhaseGift) || minTargets != 1 || maxTargets != 1 ||
             effects.FirstOrDefault() is not { Op: SkillProgramEffectOp.ConsumeTargetPhaseLedger } targetLedgerEffect ||
             targetLedgerEffect.StateId != Identifier(node,"targetPhaseLedgerId",path) || Identifier(node,"targetPhaseLedgerId",path) != id))
            Fail(path + ".targetPhaseLedgerId", "requires one target and a matching first ledger instruction");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.AccumulatePaidPhaseGift))
        {
            var gifts = effects.Select((e,i) => (e,i)).Where(x => x.e.Op == SkillProgramEffectOp.GiveSelected).ToArray();
            var counts = effects.Select((e,i) => (e,i)).Where(x => x.e.Op == SkillProgramEffectOp.AccumulatePaidPhaseGift).ToArray();
            if (gifts.Length != 1 || counts.Length != 1 || gifts[0].i >= counts[0].i || gifts[0].e.Condition.Kind != SkillProgramConditionKind.Always || minCards < 1 || sourceZones.Count != 1 || sourceZones[0] != CardZoneKind.Hand || !node.TryGetProperty("targetPhaseLedgerId", out _))
                Fail(path, "paid phase gift counting requires one unconditional earlier Hand gift and a target phase ledger");
        }
        var cardKinds = node.TryGetProperty("cardKinds", out _) ? EnumArray<CardKind>(node, "cardKinds", path) : [];
        if (effects.Any(e => e.Op == SkillProgramEffectOp.RecastSelectedPhysicalSlash) &&
            !cardKinds.ToHashSet().SetEquals([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]))
            Fail(path + ".cardKinds", "physical Slash recast requires exactly the three printed physical Slash kinds");
        var cardSuits = node.TryGetProperty("cardSuits", out _) ? EnumArray<Suit>(node, "cardSuits", path) : [];
        var cardCategories = node.TryGetProperty("cardCategories", out _) ? EnumArray<SkillProgramCardCategory>(node, "cardCategories", path) : [];
        if ((cardKinds.Count > 0 || cardSuits.Count > 0 || cardCategories.Count > 0) && maxCards == 0)
            Fail(path, "card input filters require selected cards");
        if (minCards == 0 &&
            effects.Any(effect => effect.Op == SkillProgramEffectOp.CaptureSelectedCards))
            Fail(path + ".minCards", "captureSelectedCards requires at least one initial card");
        if (effects.Any(effect => effect.Op == SkillProgramEffectOp.DeclareBoundCardNameUntilTurnEnd) &&
            (minCards != 1 || maxCards != 1 || minTargets != 0 || maxTargets != 0 ||
             !sourceZones.SequenceEqual([CardZoneKind.Hand]) ||
             effects.FirstOrDefault() is not { Op: SkillProgramEffectOp.CaptureSelectedCards } ||
             !effects.Any(effect => effect.Op == SkillProgramEffectOp.RevealBoundCards)))
            Fail(path, "declaring requires one captured and publicly revealed owner hand card without initial targets");
        foreach (var declaration in effects.Where(effect => effect.Op == SkillProgramEffectOp.DeclareBoundCardNameUntilTurnEnd))
        {
            if (effects[0].ResultBind != declaration.SourceBind || effects[^1] != declaration ||
                !effects.TakeWhile(effect => effect != declaration).Any(effect => effect.Op == SkillProgramEffectOp.RevealBoundCards &&
                    effect.SourceBind == declaration.SourceBind && effect.Condition.Kind == SkillProgramConditionKind.Always))
                Fail(path, "declaring requires the unconditional matching hand reveal before its terminal declaration");
        }
        if (effects.Any(effect => effect.Op == SkillProgramEffectOp.RecastSelectedCards) &&
            (minCards != 1 || maxCards != 1 || minTargets != 0 || maxTargets != 0 ||
             !sourceZones.SequenceEqual([CardZoneKind.Hand]) || cardCountExpression is not null))
            Fail(path, "recastSelectedCards requires exactly one owner hand card and no initial targets");
        if (effects.Any(effect => effect.Op == SkillProgramEffectOp.RevealSelectedHandAgainstTarget) &&
            (minCards != 1 || maxCards != 1 || minTargets != 1 || maxTargets != 1 ||
             targetKind != SkillProgramTargetKind.OtherLivingWithHand ||
             !sourceZones.SequenceEqual([CardZoneKind.Hand]) || cardCountExpression is not null))
            Fail(path, "revealSelectedHandAgainstTarget requires exactly one owner hand card and one other living target with hand cards");
        if (effects.Any(effect => effect.Op == SkillProgramEffectOp.DrawTurnOwnerThenDiscardMaximumHandForDodge) &&
            (effects.Count != 1 || minCards != 0 || maxCards != 0 || minTargets != 0 || maxTargets != 0))
            Fail(path, "drawTurnOwnerThenDiscardMaximumHandForDodge requires one standalone effect and no initial cards or targets");
        {
            ProgramCompositionValidator.Validate(path, effects, minTargets == 1 && maxTargets == 1,
                maxCards, initialTargetSetCount: maxTargets > 1 ? minTargets : 0,
                initialTargetSetMaximum: maxTargets > 1 ? maxTargets : 0, activationTargetKind: targetKind, activationSourceZones: sourceZones, activationMinimumCards: minCards, activationCardCategories: cardCategories);
            if (effects.Any(effect => effect.Op == SkillProgramEffectOp.UseAllHandCardsAsOrdinaryTrick) &&
                (minCards != 1 || maxCards != 64 || minTargets != 0 || maxTargets != 0 ||
                 sourceZones.Count != 1 || sourceZones[0] != CardZoneKind.Hand))
                Fail(path,
                    "all-hand ordinary-trick use requires card bounds 1..64, no initial target and hand-only source");
            var pindianIndexes = effects.Select((effect, index) => (effect, index))
                .Where(item => item.effect.Op == SkillProgramEffectOp.Pindian).Select(item => item.index).ToArray();
            if (pindianIndexes.Length > 1 ||
                pindianIndexes.Length == 1 && (minCards != 1 || maxCards != 1 ||
                    minTargets != 1 || maxTargets != 1 || sourceZones.Count != 1 ||
                    sourceZones[0] != CardZoneKind.Hand))
                Fail(path, "Pindian requires one owner hand card and one required target");
        }
        if (effects.Any(effect => effect.Op == SkillProgramEffectOp.ConsumeDistinctTurnTarget) &&
            (effects.Count(effect => effect.Op == SkillProgramEffectOp.ConsumeDistinctTurnTarget) != 1 ||
             effects[0].Op != SkillProgramEffectOp.ConsumeDistinctTurnTarget || minTargets != 1 || maxTargets != 1 ||
             targetKind != SkillProgramTargetKind.OtherLiving))
            Fail(path, "actual-turn target commitment requires a first standalone ledger instruction and one other living target");
        if (effects.Any(effect => effect.Op is SkillProgramEffectOp.RecoverToMaximum or SkillProgramEffectOp.DrawRecoveryReceipt or SkillProgramEffectOp.ReserveNextSlashDamage))
            Fail(path, "recovery receipt and cancellation reserve require their lifecycle trigger boundaries");
        ValidateRelativeZoneDemandActivation(path, effects, minCards, maxCards, minTargets, maxTargets, sourceZones, usesPerPhase);
        if (effects.Any(effect => effect.Op == SkillProgramEffectOp.ExchangeOwnedCardThroughDeckEnd))
        {
            // The other-player entry shares only its provider phase counter. Reject
            // owner-only gates or costs rather than silently bypassing them there.
            if (effects.Count != 1 || minCards != 1 || maxCards != 1 || minTargets != 0 || maxTargets != 0 ||
                !sourceZones.Order().SequenceEqual(new[] { CardZoneKind.Hand, CardZoneKind.Equipment }.Order()) ||
                usesPerPhase != 1 || uses is not null || OptionalCondition(node, path).Kind != SkillProgramConditionKind.Always ||
                node.EnumerateObject().Any(property => property.Name is not
                    ("id" or "minCards" or "maxCards" or "minTargets" or "maxTargets" or "targetKind" or
                     "usesPerTurn" or "usesPerPhase" or "sourceZones" or "effects" or "condition")))
                Fail(path, "deck-end exchange requires one standalone unconditional effect, one HE card, no target/filter or owner-only gate/cost, and one use per provider play phase");
        }
        var activation = new SkillProgramActivation(id, minCards, maxCards, minTargets, maxTargets, targetKind, uses,
            OptionalCondition(node, path), effects, sourceZones, usesPerPhase, usesPerGame,
            selectedCardsSameSuit, equipmentSlots,
            node.TryGetProperty("usageGroup", out _) ? Identifier(node, "usageGroup", path) : null,
            targetRequiresEmptyEquipmentSlot) { TargetPhaseLedgerId = node.TryGetProperty("targetPhaseLedgerId", out _) ? Identifier(node, "targetPhaseLedgerId", path) : null, CategoryTargetLedgerId = node.TryGetProperty("categoryTargetLedgerId", out _) ? Identifier(node, "categoryTargetLedgerId", path) : null, MarkerCost = ParseMarkerCost(node, path),
                ContinueAfterOwnerDeath = node.TryGetProperty("continueAfterOwnerDeath", out _) && RequiredBool(node, "continueAfterOwnerDeath", path),
                SelectedCardsDistinctSuits = node.TryGetProperty("selectedCardsDistinctSuits", out _) && RequiredBool(node, "selectedCardsDistinctSuits", path),
                CardCountExpression = cardCountExpression, CardKinds = cardKinds, CardSuits = cardSuits, CardCategories = cardCategories };
        RecipientContestConsequencesComposition.Activation(path, activation);
        SourceCurseComposition.ValidateActivation(path, activation);
        PublicPilePreparationComposition.ValidateActivation(path, activation);
        ActualHandGainAndCategoryGiftComposition.ValidateActivation(path, activation);
        ActualDiscardRecoveryComposition.ValidateActivation(path, activation);
        EquipmentDonationComposition.ValidateActivation(path, activation);
        DirectedDistanceDebtComposition.ValidateActivation(path, activation);
        ConditionalDiscardDuelComposition.Validate(path, activation);
        InspectedHandSlashComposition.Validate(path, activation);
        KuangfuComposition.Validate(path, activation);
        RecipientContestComposition.Validate(path, effects, null, minCards, minTargets, targetKind, usesPerPhase);
        if (effects.Any(e => e.Op == SkillProgramEffectOp.GiveAllHandAndStartRecipientPindian) &&
            (maxCards != 0 || maxTargets != 1 || uses is not null || usesPerGame is not null ||
             !sourceZones.SequenceEqual([CardZoneKind.Hand]) || activation.Condition.Kind != SkillProgramConditionKind.Always || activation.MarkerCost is not null))
            Fail(path, "recipient contest requires a whole-hand, unconditional one-use-per-actual-Play activation without other quota or marker cost");
        return activation;
    }

    private static SkillProgramContribution ParseContribution(JsonElement node, string path)
    {
        RequireObject(node, path);
        CheckProperties(node, path, "id", "providerFactions", "ownerRole", "cardKinds", "cardSuits",
            "usesPerPlayPhase");
        var id = Identifier(node, "id", path);
        var providerFactions = StringArray(node, "providerFactions", path);
        if (providerFactions.Count == 0)
            Fail(path + ".providerFactions", "must contain at least one faction id");
        var ownerRole = EnumValue<Role>(node, "ownerRole", path);
        var cardKinds = EnumArray<CardKind>(node, "cardKinds", path);
        var cardSuits = EnumArray<Suit>(node, "cardSuits", path);
        if (cardKinds.Count == 0 && cardSuits.Count == 0)
            Fail(path, "must accept at least one physical card kind or suit");
        var usesPerPlayPhase = PositiveInt(node, "usesPerPlayPhase", path);
        if (usesPerPlayPhase > 64)
            Fail(path + ".usesPerPlayPhase", "must not exceed 64");
        return new SkillProgramContribution(id, providerFactions, ownerRole, cardKinds, cardSuits,
            usesPerPlayPhase);
    }

    private static SkillProgramBooleanStateDefinition ParseBooleanState(JsonElement node, string path)
    {
        RequireObject(node, path);
        CheckProperties(node, path, "id", "initialValue", "visibility", "resetScope", "reacquirePolicy");
        return new(
            Identifier(node, "id", path),
            RequiredBool(node, "initialValue", path),
            EnumValue<SkillProgramStateVisibility>(node, "visibility", path),
            EnumValue<SkillProgramStateResetScope>(node, "resetScope", path),
            EnumValue<SkillProgramStateReacquirePolicy>(node, "reacquirePolicy", path));
    }

    private static SkillProgramEffect ParseCompositionEffect(JsonElement node, string path,
        bool isAfterDamageTrigger = false, bool allowZeroDraw = false)
    {
        var effect = ProgramOperationCatalog.Default.Parse(node, path,
            (condition, conditionPath) => ParseCondition(condition, conditionPath, 0,
                defaultPindianBind: true, allowChoice: true, allowBoundCards: true,
                allowBoundCardCategories: true, allowBoundCardKinds: true,
                allowClaimableDamageCards: true, allowAttackRangeCoverage: true,
                allowBoundCardCount: true, allowOwnedCardCategory: true,
                allowRequestedSlashDamage: true),
            allowZeroDraw);
        if ((effect.Condition.ContainsHasClaimableDamageCards() ||
             effect.Options.Any(option => option.Condition.ContainsHasClaimableDamageCards())) &&
            (!isAfterDamageTrigger || effect.Op != SkillProgramEffectOp.ChooseOption ||
             effect.Target != SkillProgramEffectTarget.Owner))
            Fail(path + ".condition", "hasClaimableDamageCards requires an owner choice in afterDamageApplied");
        if ((effect.Condition.ContainsHasOwnedCardCategory() ||
             effect.Options.Any(option => option.Condition.ContainsHasOwnedCardCategory())) &&
            effect.Op != SkillProgramEffectOp.ChooseOption)
            Fail(path + ".condition", "hasOwnedCardCategory requires a chooser-owned card option");
        if (effect.Target == SkillProgramEffectTarget.SelectedTargets &&
            effect.Op is not (SkillProgramEffectOp.Draw or SkillProgramEffectOp.Recover or SkillProgramEffectOp.SetChainedState or SkillProgramEffectOp.DiscardSelectedParticipantCards or SkillProgramEffectOp.DamageParticipants or SkillProgramEffectOp.GrantAttributedNatureEffect or SkillProgramEffectOp.RemoveSelectedCurrentArrowBarrageTarget))
            Fail(path + ".target", "selectedTargets is supported only by draw or recover");
        return effect;
    }
    private static SkillProgramTrigger ParseTrigger(JsonElement node, string path)
    {
        RequireObject(node, path);
        CheckProperties(node, path, "id", "window", "sourceSkillId", "sourceViewAsId", "cardKinds",
            "cardCategories", "damageCardKinds", "sourceZones", "movementOccurrence", "damageOccurrence",
            "subject", "suits", "minimumRank", "maximumRank", "excludedReasons", "judgmentReasons",
            "judgmentSource", "optional", "condition", "priority", "usageScope", "usageLimit",
            "drawPhaseMode", "choiceGroup", "ownerRelation", "turnOwnerScope", "effects",
            "destinationZones", "movementReasons", "excludedMovementReasons", "ignoreOwnSkillMovements",
            "hpChangeOccurrence", "markerCost", "evaluateConditionAtResolution", "allowNoEventTarget", "allowOwnDiscardPhaseEnded", "movementDiscardOnly", "discardOwnerScope", "includeResponseUses", "singleActionInstance", "onlyDesignatedCardTargets", "noDyingAtActivation", "deferredTurnEndOnly", "dynamicUsageLimit", "namedUsageGroup", "gainPhaseQualification", "requireDamageSource", "requireNoCardConversion");
        var id = Identifier(node, "id", path);
        var window = EnumValue<SkillProgramTriggerWindow>(node, "window", path);
        bool? requireNoCardConversion = null;
        if (node.TryGetProperty("requireNoCardConversion", out _))
        {
            if (!RequiredBool(node, "requireNoCardConversion", path) ||
                window is not (SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardResponseAccepted))
                Fail(path + ".requireNoCardConversion", "requires true on an accepted use or response boundary");
            requireNoCardConversion = true;
        }
        var gainPhase = node.TryGetProperty("gainPhaseQualification", out _)
            ? EnumValue<SkillProgramGainPhaseQualification>(node, "gainPhaseQualification", path)
            : (SkillProgramGainPhaseQualification?)null;
        if (gainPhase is not null && window != SkillProgramTriggerWindow.CardsGained)
            Fail(path + ".gainPhaseQualification", "requires a cardsGained trigger");
        bool? requireDamageSource = null;
        if (node.TryGetProperty("requireDamageSource", out _))
        {
            if (!RequiredBool(node, "requireDamageSource", path))
                Fail(path + ".requireDamageSource", "only true is supported");
            requireDamageSource = true;
        }
        var singleActionInstance = node.TryGetProperty("singleActionInstance", out _) && RequiredBool(node, "singleActionInstance", path);
        if (node.TryGetProperty("singleActionInstance", out _) && window != SkillProgramTriggerWindow.CardUseCompleted) Fail(path, "singleActionInstance requires cardUseCompleted");
        var onlyDesignatedCardTargets = node.TryGetProperty("onlyDesignatedCardTargets", out _) && RequiredBool(node, "onlyDesignatedCardTargets", path);
        var includeResponseUses = node.TryGetProperty("includeResponseUses", out _) && RequiredBool(node, "includeResponseUses", path);
        if (node.TryGetProperty("includeResponseUses", out _) && window is not (SkillProgramTriggerWindow.CardUseCompleted or SkillProgramTriggerWindow.CardUseCommitted))
            Fail(path + ".includeResponseUses", "requires a committed or completed card-use trigger");
        string? sourceSkillId = null;
        string? sourceViewAsId = null;
        SkillProgramTriggerSubject? subject = null;
        IReadOnlyList<Suit> suits = Array.Empty<Suit>();
        IReadOnlyList<string> excludedReasons = Array.Empty<string>();
        IReadOnlyList<string> judgmentReasons = Array.Empty<string>();
        SkillProgramTriggerSubject? judgmentSource = null;
        IReadOnlyList<CardKind> cardKinds = Array.Empty<CardKind>();
        IReadOnlyList<CardKind> damageCardKinds = [];
        IReadOnlyList<SkillProgramCardCategory> cardCategories = [];
        IReadOnlyList<CardZoneKind> sourceZones = Array.Empty<CardZoneKind>();
        IReadOnlyList<CardZoneKind> destinationZones = [];
        var isMovementWindow = window is SkillProgramTriggerWindow.CardsMoved or
            SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived;
        var isHpWindow = window is SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged;
        foreach (var field in new[] { "movementOccurrence", "movementReasons", "excludedMovementReasons", "ignoreOwnSkillMovements" })
            if (!isMovementWindow && node.TryGetProperty(field, out _))
                Fail(path + "." + field, "supported only by cardsMoved or cardsGained");
        if (!isHpWindow && node.TryGetProperty("hpChangeOccurrence", out _))
            Fail(path + ".hpChangeOccurrence", "requires afterHpLost or afterHpRecovered");
        if (window != SkillProgramTriggerWindow.CardsGained && node.TryGetProperty("destinationZones", out _))
            Fail(path + ".destinationZones", "requires cardsGained");
        var movementReasons = node.TryGetProperty("movementReasons", out _) ? StringArray(node, "movementReasons", path) : [];
        var excludedMovementReasons = node.TryGetProperty("excludedMovementReasons", out _) ? StringArray(node, "excludedMovementReasons", path) : [];
        if (movementReasons.Concat(excludedMovementReasons).Any(reason => !reason.Contains('.')) ||
            movementReasons.Intersect(excludedMovementReasons, StringComparer.Ordinal).Any())
            Fail(path, "movement reasons must be distinct namespaced ids without overlapping includes and excludes");
        var ignoreOwnSkillMovements = node.TryGetProperty("ignoreOwnSkillMovements", out _) &&
            RequiredBool(node, "ignoreOwnSkillMovements", path);
        var hpChangeOccurrence = node.TryGetProperty("hpChangeOccurrence", out _)
            ? EnumValue<SkillProgramHpChangeOccurrence>(node, "hpChangeOccurrence", path)
            : SkillProgramHpChangeOccurrence.PerEvent;
        SkillProgramMovementOccurrence? movementOccurrence = null;
        SkillProgramDamageOccurrence? damageOccurrence = null;
        var drawPhaseMode = SkillProgramDrawPhaseMode.Additive;
        var minimumRank = 1;
        var maximumRank = 13;
        var priority = 0;
        SkillUsageScope? usageScope = null;
        int? usageLimit = null;
        string? choiceGroup = null;
        SkillProgramCardActionOwnerRelation? ownerRelation = null;
        var turnOwnerScope = SkillProgramTurnOwnerScope.Own;
        if (node.TryGetProperty("turnOwnerScope", out _))
        {
            if (window is not (SkillProgramTriggerWindow.AfterTurnEnded or SkillProgramTriggerWindow.TurnEnding or
                SkillProgramTriggerWindow.PlayEnding or SkillProgramTriggerWindow.PlayPhaseStarting or
                SkillProgramTriggerWindow.DiscardPhaseEnded or SkillProgramTriggerWindow.DiscardPhaseStarting or
                SkillProgramTriggerWindow.JudgmentPhaseStarting))
                Fail(path + ".turnOwnerScope", "requires an afterTurnEnded, turnEnding, playEnding, playPhaseStarting, judgmentPhaseStarting, discardPhaseStarting or discardPhaseEnded trigger");
            turnOwnerScope = EnumValue<SkillProgramTurnOwnerScope>(node, "turnOwnerScope", path);
        }
        var isCardActionWindow = window is (
            SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardEffectBeforeApply or SkillProgramTriggerWindow.CardUseBeforeTargetEffects or
            SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardResponseAccepted or
            SkillProgramTriggerWindow.CardUseCompleted or SkillProgramTriggerWindow.SlashTargetRedirecting or
            SkillProgramTriggerWindow.SlashBeforeResponse or SkillProgramTriggerWindow.SlashFullyDodged);
        if (node.TryGetProperty("damageCardKinds", out _))
        {
            if (window != SkillProgramTriggerWindow.AfterDamageApplied)
                Fail(path + ".damageCardKinds", "requires an afterDamageApplied trigger");
            damageCardKinds = EnumArray<CardKind>(node, "damageCardKinds", path);
            if (damageCardKinds.Count == 0 || damageCardKinds.Distinct().Count() != damageCardKinds.Count)
                Fail(path + ".damageCardKinds", "must contain distinct effective card kinds");
        }
        if (node.TryGetProperty("cardCategories", out _))
        {
            if ((!isCardActionWindow || window == SkillProgramTriggerWindow.CardResponseAccepted) &&
                window != SkillProgramTriggerWindow.DiscardPileReceived)
                Fail(path + ".cardCategories", "requires a card-use window");
            cardCategories = EnumArray<SkillProgramCardCategory>(node, "cardCategories", path);
            if (cardCategories.Count == 0 || cardCategories.Distinct().Count() != cardCategories.Count)
                Fail(path + ".cardCategories", "must contain distinct card categories");
        }
        var isLifecycleWindow = window is SkillProgramTriggerWindow.ActualSlashTargetPenalty or SkillProgramTriggerWindow.ActualSlashTargetBenefit or SkillProgramTriggerWindow.SlashDodgeCancelledBenefit || window == SkillProgramTriggerWindow.OtherActualTurnStarted || window == SkillProgramTriggerWindow.OtherActualUseTargeted || window == SkillProgramTriggerWindow.AfterTurnEnded || window == SkillProgramTriggerWindow.FirstGameDomainCrossing || window == SkillProgramTriggerWindow.ProgramTargetCommitted || isMovementWindow || isHpWindow || window is SkillProgramTriggerWindow.SkillsChanged or SkillProgramTriggerWindow.GameStarting or
            SkillProgramTriggerWindow.DyingEntering or SkillProgramTriggerWindow.DyingEntered or SkillProgramTriggerWindow.DyingExited or SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or
            SkillProgramTriggerWindow.DrawPhaseStarting or
            SkillProgramTriggerWindow.AfterNormalDraw or SkillProgramTriggerWindow.DrawPhaseEnded or SkillProgramTriggerWindow.DrawPhaseSkipped or
            SkillProgramTriggerWindow.SelfDyingResponse or
            SkillProgramTriggerWindow.DyingResponse or SkillProgramTriggerWindow.DyingEntering or
            SkillProgramTriggerWindow.BeforeDamageApplied or
            SkillProgramTriggerWindow.DamageAppliedBeforeDying or
            SkillProgramTriggerWindow.AfterDamageApplied or
            SkillProgramTriggerWindow.PlayEnding or
            SkillProgramTriggerWindow.DiscardPhaseStarting or
            SkillProgramTriggerWindow.DiscardPhaseEnded or
            SkillProgramTriggerWindow.TurnEnding or
            SkillProgramTriggerWindow.CardsMoved or
            SkillProgramTriggerWindow.OwnerDied or
            SkillProgramTriggerWindow.CharacterDied or
            SkillProgramTriggerWindow.PlayPhaseStarting or SkillProgramTriggerWindow.JudgmentPhaseStarting or
            SkillProgramTriggerWindow.CharacterTurnedOver or SkillProgramTriggerWindow.CharacterTurnedFaceUp or SkillProgramTriggerWindow.CharacterEnteredChain;
        var supportsTriggerCondition = window is SkillProgramTriggerWindow.ActualSlashTargetPenalty or SkillProgramTriggerWindow.ActualSlashTargetBenefit or SkillProgramTriggerWindow.SlashDodgeCancelledBenefit || window == SkillProgramTriggerWindow.OtherActualTurnStarted || window == SkillProgramTriggerWindow.OtherActualUseTargeted || window == SkillProgramTriggerWindow.AfterTurnEnded || window == SkillProgramTriggerWindow.FirstGameDomainCrossing || window == SkillProgramTriggerWindow.ProgramTargetCommitted || isCardActionWindow || isMovementWindow || isHpWindow || window is SkillProgramTriggerWindow.SkillsChanged or SkillProgramTriggerWindow.GameStarting or
            SkillProgramTriggerWindow.DyingEntering or SkillProgramTriggerWindow.DyingEntered or SkillProgramTriggerWindow.DyingExited or SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or
            SkillProgramTriggerWindow.DrawPhaseStarting or
            SkillProgramTriggerWindow.AfterNormalDraw or SkillProgramTriggerWindow.DrawPhaseEnded or SkillProgramTriggerWindow.DrawPhaseSkipped or
            SkillProgramTriggerWindow.DyingResponse or SkillProgramTriggerWindow.DyingEntering or
            SkillProgramTriggerWindow.BeforeDamageApplied or
            SkillProgramTriggerWindow.PlayEnding or
            SkillProgramTriggerWindow.DiscardPhaseStarting or
            SkillProgramTriggerWindow.DiscardPhaseEnded or
            SkillProgramTriggerWindow.TurnEnding or
            SkillProgramTriggerWindow.CardsMoved or
            SkillProgramTriggerWindow.OwnerDied or
            SkillProgramTriggerWindow.CharacterDied or
            SkillProgramTriggerWindow.PlayPhaseStarting or SkillProgramTriggerWindow.JudgmentPhaseStarting or
            SkillProgramTriggerWindow.CharacterTurnedOver or SkillProgramTriggerWindow.CharacterTurnedFaceUp or SkillProgramTriggerWindow.CharacterEnteredChain ||
            window == SkillProgramTriggerWindow.AfterDamageApplied;
        if (window is not (SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.DiscardPileReceived) &&
            node.TryGetProperty("sourceZones", out _))
            Fail(path, "sourceZones and movementOccurrence are supported only by cardsMoved or discardPileReceived");
        if (window is not (SkillProgramTriggerWindow.AfterDamageApplied or
                SkillProgramTriggerWindow.DamageAppliedBeforeDying) &&
            node.TryGetProperty("damageOccurrence", out _))
            Fail(path, "damageOccurrence requires a damage-applied trigger");
        if (window != SkillProgramTriggerWindow.DrawPhaseStarting && node.TryGetProperty("drawPhaseMode", out _))
            Fail(path, "drawPhaseMode is supported only by drawPhaseStarting");
        if (window is not (SkillProgramTriggerWindow.DrawPhaseStarting or
                SkillProgramTriggerWindow.DrawPhaseEnded or
                SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or
                SkillProgramTriggerWindow.PlayPhaseStarting) &&
            node.TryGetProperty("choiceGroup", out _))
            Fail(path, "choiceGroup requires drawPhaseStarting, drawPhaseEnded, turnStartBeforeNormalFlow or playPhaseStarting");
        if (node.TryGetProperty("ownerRelation", out _) &&
            window is not (SkillProgramTriggerWindow.CardUseCommitted or
                SkillProgramTriggerWindow.CardEffectBeforeApply or SkillProgramTriggerWindow.CardUseBeforeTargetEffects or
                SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardResponseAccepted or
                SkillProgramTriggerWindow.CardUseCompleted or SkillProgramTriggerWindow.SlashTargetRedirecting or
                SkillProgramTriggerWindow.SlashBeforeResponse or SkillProgramTriggerWindow.SlashFullyDodged))
            Fail(path + ".ownerRelation", "requires a card-action window");
        if (node.TryGetProperty("condition", out _) && !supportsTriggerCondition)
            Fail(path + ".condition",
                "trigger conditions require a supported lifecycle or card-movement boundary");
        if (!isLifecycleWindow && !isCardActionWindow &&
            (node.TryGetProperty("priority", out _) || node.TryGetProperty("usageScope", out _) ||
             node.TryGetProperty("usageLimit", out _)))
            Fail(path, "priority and usage fields require lifecycle or card-action windows");
        if (isLifecycleWindow)
        {
            var supportsDamageSourceConversion = window == SkillProgramTriggerWindow.AfterDamageApplied;
            var supportsDiscardCardFilter = window == SkillProgramTriggerWindow.DiscardPileReceived;
            if ((!supportsDamageSourceConversion &&
                 (node.TryGetProperty("sourceSkillId", out _) || node.TryGetProperty("sourceViewAsId", out _))) ||
                (!supportsDiscardCardFilter && node.TryGetProperty("cardKinds", out _)) ||
                (!supportsDiscardCardFilter && node.TryGetProperty("cardCategories", out _)) ||
                (!supportsDiscardCardFilter && node.TryGetProperty("suits", out _)) ||
                node.TryGetProperty("minimumRank", out _) || node.TryGetProperty("maximumRank", out _) ||
                node.TryGetProperty("excludedReasons", out _) || node.TryGetProperty("judgmentReasons", out _) ||
                node.TryGetProperty("judgmentSource", out _))
                Fail(path, "lifecycle trigger windows accept subject, ordering, usage and effects only");
            subject = EnumValue<SkillProgramTriggerSubject>(node, "subject", path);
            if (supportsDamageSourceConversion && subject == SkillProgramTriggerSubject.Source)
            {
                sourceSkillId = Identifier(node, "sourceSkillId", path);
                if (node.TryGetProperty("sourceViewAsId", out var sourceViewAs))
                {
                    if (sourceViewAs.ValueKind == JsonValueKind.String)
                    {
                        sourceViewAsId = sourceViewAs.GetString();
                        if (string.IsNullOrWhiteSpace(sourceViewAsId) || sourceViewAsId.Length > 128)
                            Fail(path + ".sourceViewAsId", "must contain 1 to 128 characters");
                    }
                    else if (sourceViewAs.ValueKind != JsonValueKind.Null)
                        Fail(path + ".sourceViewAsId", "must be a string or null");
                }
            }
            else
            {
                if (subject != SkillProgramTriggerSubject.Owner &&
                    !(window == SkillProgramTriggerWindow.DyingEntering && subject == SkillProgramTriggerSubject.Any) &&
                    !(window is (SkillProgramTriggerWindow.CharacterTurnedOver or SkillProgramTriggerWindow.CharacterTurnedFaceUp or SkillProgramTriggerWindow.CharacterEnteredChain) && subject == SkillProgramTriggerSubject.Any) &&
                    !(window == SkillProgramTriggerWindow.AfterDamageApplied &&
                      subject is SkillProgramTriggerSubject.DamageSource or SkillProgramTriggerSubject.Any) &&
                    !(window == SkillProgramTriggerWindow.BeforeDamageApplied &&
                      subject is SkillProgramTriggerSubject.DamageTarget or SkillProgramTriggerSubject.DamageSource) &&
                    !(window == SkillProgramTriggerWindow.DamageAppliedBeforeDying && subject == SkillProgramTriggerSubject.DamageTarget &&
                      node.TryGetProperty("effects", out var appearanceEffects) && appearanceEffects.ValueKind == JsonValueKind.Array &&
                      appearanceEffects.EnumerateArray().Any(e => e.TryGetProperty("op", out var op) && op.ValueKind == JsonValueKind.String &&
                          string.Equals(op.GetString(), "drawByDamageCardColor", StringComparison.OrdinalIgnoreCase))))
                    Fail(path + ".subject", "this lifecycle window supports only owner subjects");
                if (supportsDamageSourceConversion &&
                    (node.TryGetProperty("sourceSkillId", out _) || node.TryGetProperty("sourceViewAsId", out _)))
                    Fail(path, "owner after-damage triggers do not accept card-conversion source fields");
            }
            if (supportsDiscardCardFilter && node.TryGetProperty("suits", out _))
            {
                suits = EnumArray<Suit>(node, "suits", path);
                if (suits.Distinct().Count() != suits.Count)
                    Fail(path + ".suits", "must contain distinct suits");
            }
            if (supportsDiscardCardFilter && node.TryGetProperty("cardKinds", out _))
            {
                cardKinds = EnumArray<CardKind>(node, "cardKinds", path);
                if (cardKinds.Distinct().Count() != cardKinds.Count)
                    Fail(path + ".cardKinds", "must contain distinct card kinds");
            }
            if (window == SkillProgramTriggerWindow.CardsGained)
            {
                destinationZones = EnumArray<CardZoneKind>(node, "destinationZones", path);
                if (!destinationZones.SequenceEqual([CardZoneKind.Hand]))
                    Fail(path + ".destinationZones", "cardsGained requires exactly the hand destination zone");
                movementOccurrence = EnumValue<SkillProgramMovementOccurrence>(node, "movementOccurrence", path);
            }
            if (window == SkillProgramTriggerWindow.DiscardPileReceived && node.TryGetProperty("sourceZones", out _))
            {
                sourceZones = EnumArray<CardZoneKind>(node, "sourceZones", path);
                if (sourceZones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment)))
                    Fail(path + ".sourceZones", "discardPileReceived accepts hand, equipment and judgment sources only");
            }
            if (window == SkillProgramTriggerWindow.CardsMoved)
            {
                sourceZones = EnumArray<CardZoneKind>(node, "sourceZones", path);
                movementOccurrence = EnumValue<SkillProgramMovementOccurrence>(node, "movementOccurrence", path);
                if (sourceZones.Count == 0 || sourceZones.Distinct().Count() != sourceZones.Count ||
                    movementOccurrence != SkillProgramMovementOccurrence.PerOwnerBatch && sourceZones.Count != 1 || sourceZones.Any(zone => zone is not
                        (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment or
                         CardZoneKind.WoodenOxGrain or CardZoneKind.BuquWound or CardZoneKind.Authority or
                         CardZoneKind.Chunlao) && !(node.TryGetProperty("effects", out var rawEffects) && rawEffects.ValueKind == JsonValueKind.Array &&
                             rawEffects.EnumerateArray().Any(e => e.ValueKind == JsonValueKind.Object &&
                             e.TryGetProperty("op", out var op) && op.ValueKind == JsonValueKind.String && op.GetString() == "revealRedLossAndDraw") &&
                             zone is CardZoneKind.PojunHold or CardZoneKind.PrivateReserve or CardZoneKind.PublicDeferredPile or
                                 CardZoneKind.PublicPersistentPile or CardZoneKind.PrivateTurnHold)))
                    Fail(path + ".sourceZones",
                        "cardsMoved requires exactly one owner-scoped source zone");
                movementOccurrence = EnumValue<SkillProgramMovementOccurrence>(
                    node, "movementOccurrence", path);
            }
            if (window is
                (SkillProgramTriggerWindow.AfterDamageApplied or
                    SkillProgramTriggerWindow.DamageAppliedBeforeDying))
            {
                damageOccurrence = EnumValue<SkillProgramDamageOccurrence>(
                    node, "damageOccurrence", path);
            }
            if (window == SkillProgramTriggerWindow.DrawPhaseStarting &&
                node.TryGetProperty("drawPhaseMode", out _))
            {
                drawPhaseMode = EnumValue<SkillProgramDrawPhaseMode>(node, "drawPhaseMode", path);
            }
            if (node.TryGetProperty("choiceGroup", out _))
            {
                choiceGroup = Identifier(node, "choiceGroup", path);
            }

        }
        else if (window == SkillProgramTriggerWindow.JudgmentFinalized)
        {
            if (node.TryGetProperty("sourceSkillId", out _) || node.TryGetProperty("sourceViewAsId", out _) ||
                node.TryGetProperty("cardKinds", out _))
                Fail(path, "judgmentFinalized does not accept card-conversion source fields");
            subject = EnumValue<SkillProgramTriggerSubject>(node, "subject", path);
            suits = EnumArray<Suit>(node, "suits", path);
            if (suits.Count == 0) Fail(path + ".suits", "must contain at least one final suit");
            minimumRank = RequiredInt(node, "minimumRank", path);
            maximumRank = RequiredInt(node, "maximumRank", path);
            if (minimumRank is < 1 or > 13 || maximumRank is < 1 or > 13 || minimumRank > maximumRank)
                Fail(path, "judgment rank bounds must satisfy 1 <= minimumRank <= maximumRank <= 13");
            excludedReasons = StringArray(node, "excludedReasons", path);
            judgmentReasons = node.TryGetProperty("judgmentReasons", out _)
                ? StringArray(node, "judgmentReasons", path)
                : Array.Empty<string>();
            judgmentSource = node.TryGetProperty("judgmentSource", out _)
                ? EnumValue<SkillProgramTriggerSubject>(node, "judgmentSource", path)
                : null;
            if (judgmentReasons.Intersect(excludedReasons, StringComparer.Ordinal).Any())
                Fail(path, "judgmentReasons and excludedReasons must not overlap");
        }
        else if (window == SkillProgramTriggerWindow.JudgmentReplacing)
        {
            if (node.TryGetProperty("sourceSkillId", out _) || node.TryGetProperty("sourceViewAsId", out _) ||
                node.TryGetProperty("cardKinds", out _) ||
                node.TryGetProperty("suits", out _) || node.TryGetProperty("minimumRank", out _) ||
                node.TryGetProperty("maximumRank", out _) || node.TryGetProperty("judgmentReasons", out _) ||
                node.TryGetProperty("judgmentSource", out _))
                Fail(path, "judgmentReplacing accepts subject and excludedReasons, not card-action or final-result fields");
            subject = EnumValue<SkillProgramTriggerSubject>(node, "subject", path);
            excludedReasons = StringArray(node, "excludedReasons", path);
        }
        else
        {
            ownerRelation = node.TryGetProperty("ownerRelation", out _)
                ? EnumValue<SkillProgramCardActionOwnerRelation>(node, "ownerRelation", path)
                : SkillProgramCardActionOwnerRelation.Actor;
            if (node.TryGetProperty("subject", out _) || node.TryGetProperty("suits", out _) ||
                node.TryGetProperty("minimumRank", out _) || node.TryGetProperty("maximumRank", out _) ||
                node.TryGetProperty("excludedReasons", out _) || node.TryGetProperty("judgmentReasons", out _) ||
                node.TryGetProperty("judgmentSource", out _))
                Fail(path, "card-action trigger windows do not accept judgment fields");
            if (node.TryGetProperty("cardKinds", out _) || cardCategories.Count > 0)
            {
                if (ownerRelation == SkillProgramCardActionOwnerRelation.ConversionSource)
                    Fail(path + ".ownerRelation", "conversionSource requires a conversion skill, not card filters");
                if (node.TryGetProperty("sourceSkillId", out _) || node.TryGetProperty("sourceViewAsId", out _))
                    Fail(path, "card filters cannot be combined with card-conversion source fields");
                if (node.TryGetProperty("cardKinds", out _))
                {
                    cardKinds = EnumArray<CardKind>(node, "cardKinds", path);
                    if (cardKinds.Count == 0) Fail(path + ".cardKinds", "must contain at least one effective card kind");
                }
                var supported = window is (SkillProgramTriggerWindow.CardUseCompleted or SkillProgramTriggerWindow.CardUseCommitted) && includeResponseUses
                    ? true
                    : window is SkillProgramTriggerWindow.CardUseCompleted or SkillProgramTriggerWindow.CardUseCommitted
                    ? cardKinds.All(kind => kind is not (CardKind.Dodge or CardKind.Nullification))
                    : window is SkillProgramTriggerWindow.CardEffectBeforeApply or SkillProgramTriggerWindow.CardUseBeforeTargetEffects or SkillProgramTriggerWindow.CardUseTargetsFinalized
                    ? cardKinds.All(kind => kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash ||
                        CardCatalog.Get(kind).CategoryName == "锦囊牌")
                    : window is SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardEffectBeforeApply or SkillProgramTriggerWindow.CardUseBeforeTargetEffects or SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.SlashTargetRedirecting or SkillProgramTriggerWindow.SlashBeforeResponse or SkillProgramTriggerWindow.SlashFullyDodged
                    ? cardKinds.All(kind => kind is CardKind.Slash or CardKind.FireSlash or
                        CardKind.ThunderSlash or CardKind.Lightning)
                    : cardKinds.All(kind => kind is CardKind.Slash or CardKind.FireSlash or
                        CardKind.ThunderSlash or CardKind.Dodge or CardKind.Nullification);
                if (!supported)
                    Fail(path + ".cardKinds", $"contains a card kind unsupported by {Camel(window)}");
            }
            else if (ownerRelation == SkillProgramCardActionOwnerRelation.ConversionSource)
            {
                sourceSkillId = Identifier(node, "sourceSkillId", path);
                if (node.TryGetProperty("sourceViewAsId", out var sourceViewAs))
                {
                    if (sourceViewAs.ValueKind == JsonValueKind.String)
                    {
                        sourceViewAsId = sourceViewAs.GetString();
                        if (string.IsNullOrWhiteSpace(sourceViewAsId) || sourceViewAsId.Length > 128)
                            Fail(path + ".sourceViewAsId", "must be null or contain 1 to 128 characters");
                    }
                    else if (sourceViewAs.ValueKind != JsonValueKind.Null)
                        Fail(path + ".sourceViewAsId", "must be a string or null");
                }
            }
            else if (node.TryGetProperty("sourceSkillId", out _) || node.TryGetProperty("sourceViewAsId", out _))
                Fail(path, "card-conversion source fields require conversionSource ownerRelation");
        }
        if (isLifecycleWindow || isCardActionWindow)
        {
            if (node.TryGetProperty("priority", out _))
            {
                priority = RequiredInt(node, "priority", path);
                if (priority is < -1000 or > 1000)
                    Fail(path + ".priority", "must be between -1000 and 1000");
            }
            var hasUsageScope = node.TryGetProperty("usageScope", out _);
            var hasUsageLimit = node.TryGetProperty("usageLimit", out _);
            var hasDynamic = node.TryGetProperty("dynamicUsageLimit", out _);
            if (hasUsageScope != (hasUsageLimit || hasDynamic) || hasUsageLimit && hasDynamic)
                Fail(path, "usageScope requires exactly one static or dynamic usage limit");
            if (hasUsageScope)
            {
                usageScope = EnumValue<SkillUsageScope>(node, "usageScope", path);
                if (usageScope == SkillUsageScope.Event) Fail(path + ".usageScope", "event usage is unsupported");
                if (hasUsageLimit)
                {
                    usageLimit = PositiveInt(node, "usageLimit", path);
                    if (usageLimit > 1024) Fail(path + ".usageLimit", "must not exceed 1024");
                }
            }
        }
        SkillProgramDynamicUsageLimit? dynamicLimit = null;
        if (node.TryGetProperty("dynamicUsageLimit", out var dynamicNode))
        {
            RequireObject(dynamicNode, path + ".dynamicUsageLimit");
            CheckProperties(dynamicNode, path + ".dynamicUsageLimit", "kind", "cap");
            var kind = EnumValue<SkillProgramDynamicUsageLimitKind>(dynamicNode, "kind", path + ".dynamicUsageLimit");
            var cap = PositiveInt(dynamicNode, "cap", path + ".dynamicUsageLimit");
            if (usageScope != SkillUsageScope.Round || cap > 1024) Fail(path, "dynamic usage requires Round scope and cap <=1024");
            dynamicLimit = new(kind, cap);
        }
        var namedUsage = node.TryGetProperty("namedUsageGroup", out _) ? Identifier(node, "namedUsageGroup", path) : null;
        if (namedUsage is not null && usageScope is null) Fail(path, "named usage requires a limited trigger");
        if (namedUsage is not null && (!char.IsAsciiLetter(namedUsage[0]) || namedUsage.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.'))))
            Fail(path + ".namedUsageGroup", "requires an ASCII letter followed by letters, digits, underscore, hyphen or dot");
        var optional = RequiredBool(node, "optional", path);
        if (window == SkillProgramTriggerWindow.DamageAppliedBeforeDying && optional)
            Fail(path + ".optional", "damageAppliedBeforeDying cannot request a choice");
        var condition = OptionalTriggerCondition(node, path);
        if (EnumerateTriggerValues(condition).Any(value => value.Kind == SkillProgramTriggerValueKind.TurnOwnerDamageDealtThisTurn) &&
            window != SkillProgramTriggerWindow.TurnEnding)
            Fail(path + ".condition", "ending-turn damage history requires turnEnding");
        if (EnumerateTriggerConditions(condition).Any(c => c.Kind == SkillProgramTriggerConditionKind.TurnDiscardIncludesAllSuits) &&
            window != SkillProgramTriggerWindow.AfterTurnEnded)
            Fail(path, "turn discard suit history requires AfterTurnEnded");
        if (EnumerateTriggerValues(condition).Any(v=>v.Kind==SkillProgramTriggerValueKind.EventTargetMarkerCount) && window is not (SkillProgramTriggerWindow.BeforeDamageApplied or SkillProgramTriggerWindow.DrawPhaseStarting or SkillProgramTriggerWindow.DiscardPhaseEnded or SkillProgramTriggerWindow.TurnEnding)) Fail(path + ".condition", "eventTargetMarkerCount requires an actual damage, draw, or discard subject");
        if (EnumerateTriggerValues(condition).Any(v=>v.Kind==SkillProgramTriggerValueKind.EventSourceMarkerCount) && window != SkillProgramTriggerWindow.AfterDamageApplied) Fail(path + ".condition", "eventSourceMarkerCount requires the after-damage subject");
        if (EnumerateTriggerConditions(condition).Any(item => item.Kind == SkillProgramTriggerConditionKind.OwnerKilledThisTurn) &&
            window != SkillProgramTriggerWindow.TurnEnding)
            Fail(path + ".condition", "turn kill history requires turnEnding");
        if (EnumerateTriggerValues(condition).Any(item => item.Kind is SkillProgramTriggerValueKind.CardActionPhysicalCardCount or SkillProgramTriggerValueKind.CardActionHandCardCount) &&
            !isCardActionWindow)
            Fail(path + ".condition", "physical card count requires a card-action window");
        if (EnumerateTriggerConditions(condition).Any(item =>
                item.Kind == SkillProgramTriggerConditionKind.CardActionMatchesPreviousPlayCard) &&
            window != SkillProgramTriggerWindow.CardUseCommitted)
            Fail(path + ".condition", "previous play-card comparison requires cardUseCommitted");
        if (EnumerateTriggerValues(condition).Any(item =>
                item.Kind is SkillProgramTriggerValueKind.DamageInstancesTakenThisTurn or SkillProgramTriggerValueKind.EventTargetDamageInstancesTakenThisTurn) &&
            window != SkillProgramTriggerWindow.AfterDamageApplied)
            Fail(path + ".condition", "damage-instance count requires afterDamageApplied");
        if (EnumerateTriggerConditions(condition).Any(item => item.Kind is
                SkillProgramTriggerConditionKind.CardActionActorIsCurrentTurn or
                SkillProgramTriggerConditionKind.CardActionPhaseIsPlay) &&
            (window is not (SkillProgramTriggerWindow.CardUseCommitted or
                SkillProgramTriggerWindow.CardEffectBeforeApply or SkillProgramTriggerWindow.CardUseBeforeTargetEffects or
                SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardResponseAccepted or
                SkillProgramTriggerWindow.CardUseCompleted or SkillProgramTriggerWindow.SlashTargetRedirecting or
                SkillProgramTriggerWindow.SlashBeforeResponse or SkillProgramTriggerWindow.SlashFullyDodged) &&
                window != SkillProgramTriggerWindow.AfterDamageApplied))
            Fail(path + ".condition", "card-action facts require a card-action or after-damage trigger");
        if (EnumerateTriggerConditions(condition).Any(item =>
                item.Kind == SkillProgramTriggerConditionKind.CardActionActorIsOwner) &&
            !isCardActionWindow)
            Fail(path + ".condition", "cardActionActorIsOwner requires a card-action trigger");
        if (EnumerateTriggerConditions(condition).Any(item =>
                item.Kind == SkillProgramTriggerConditionKind.CardActionTargetIsOwner) &&
            (!isCardActionWindow || window == SkillProgramTriggerWindow.CardResponseAccepted))
            Fail(path + ".condition", "cardActionTargetIsOwner requires a card-use trigger with targets");
        if (EnumerateTriggerConditions(condition).Any(item =>
                item.Kind is SkillProgramTriggerConditionKind.CardActionCardIsRed or SkillProgramTriggerConditionKind.CardActionCardIsBlack or SkillProgramTriggerConditionKind.CardActionSuitIs) &&
            !isCardActionWindow)
            Fail(path + ".condition", "cardActionCardIsRed requires a card-action trigger");
        if (EnumerateTriggerConditions(condition).Any(item =>
                item.Kind == SkillProgramTriggerConditionKind.DamageSourceGenderIs) &&
            window != SkillProgramTriggerWindow.BeforeDamageApplied)
            Fail(path + ".condition", "damageSourceGenderIs requires a beforeDamageApplied trigger");
        if (EnumerateTriggerConditions(condition).Any(item =>
                item.Kind == SkillProgramTriggerConditionKind.DeathKillerIsOwner) &&
            window != SkillProgramTriggerWindow.CharacterDied)
            Fail(path + ".condition", "deathKillerIsOwner requires a characterDied trigger");
        if (EnumerateTriggerConditions(condition).Any(item => item.Kind is SkillProgramTriggerConditionKind.OtherDamageSourceAlive or SkillProgramTriggerConditionKind.DamageSourcePairUnused) && window != SkillProgramTriggerWindow.BeforeDamageApplied)
            Fail(path + ".condition", "damage source offer conditions require a beforeDamageApplied trigger");
        if (EnumerateTriggerConditions(condition).Any(item => item.Kind == SkillProgramTriggerConditionKind.DiscardPhaseSuitsAllDistinct) && window != SkillProgramTriggerWindow.DiscardPhaseEnded)
            Fail(path + ".condition", "discardPhaseSuitsAllDistinct requires a discardPhaseEnded trigger");
        if (EnumerateTriggerConditions(condition).Any(item => item.Kind == SkillProgramTriggerConditionKind.DeathExtinguishedFaction) &&
            window != SkillProgramTriggerWindow.CharacterDied)
            Fail(path + ".condition", "deathExtinguishedFaction requires a characterDied trigger");
        if (EnumerateTriggerConditions(condition).Any(item =>
                item.Kind == SkillProgramTriggerConditionKind.DeathVictimHasCards) &&
            window != SkillProgramTriggerWindow.CharacterDied)
            Fail(path + ".condition", "deathVictimHasCards requires a characterDied trigger");
        if (EnumerateTriggerConditions(condition).Any(item =>
                item.Kind == SkillProgramTriggerConditionKind.OtherDamageParticipantAlive) &&
            window != SkillProgramTriggerWindow.AfterDamageApplied)
            Fail(path + ".condition", "otherDamageParticipantAlive requires an afterDamageApplied trigger");
        if (EnumerateTriggerConditions(condition).Any(item =>
                item.Kind == SkillProgramTriggerConditionKind.DirectCardUseDamage) &&
            window is not (SkillProgramTriggerWindow.BeforeDamageApplied or SkillProgramTriggerWindow.AfterDamageApplied))
            Fail(path + ".condition", "directCardUseDamage requires a damage trigger");
        if (EnumerateTriggerConditions(condition).Any(item =>
                item.Kind == SkillProgramTriggerConditionKind.DamageCardIsRed) &&
            window != SkillProgramTriggerWindow.AfterDamageApplied)
            Fail(path + ".condition", "damageCardIsRed requires an afterDamageApplied trigger");
        if (EnumerateTriggerConditions(condition).Any(item =>
                item.Kind == SkillProgramTriggerConditionKind.DamageCardIsSlash) &&
            window is not (SkillProgramTriggerWindow.BeforeDamageApplied or SkillProgramTriggerWindow.AfterDamageApplied))
            Fail(path + ".condition", "damageCardIsSlash requires a damage trigger");
        if (EnumerateTriggerConditions(condition).Any(item =>
                item.Kind == SkillProgramTriggerConditionKind.DamageTargetIsOther) &&
            window != SkillProgramTriggerWindow.AfterDamageApplied)
            Fail(path + ".condition", "damageTargetIsOther requires an afterDamageApplied trigger");
        if (EnumerateTriggerConditions(condition).Any(item =>
                item.Kind is SkillProgramTriggerConditionKind.DamageSourceIsOwner or
                    SkillProgramTriggerConditionKind.DamageSourceFactionIs) &&
            window is not (SkillProgramTriggerWindow.AfterDamageApplied or
                SkillProgramTriggerWindow.DamageAppliedBeforeDying))
            Fail(path + ".condition", "damage-source facts require a damage-applied trigger");
        if (EnumerateTriggerConditions(condition).Any(item =>
                item.Kind == SkillProgramTriggerConditionKind.CardUseCausedDamage) &&
            window != SkillProgramTriggerWindow.CardUseCompleted)
            Fail(path + ".condition", "cardUseCausedDamage requires a cardUseCompleted trigger");
        if (EnumerateTriggerConditions(condition).Any(item =>
                item.Kind == SkillProgramTriggerConditionKind.CardUseConversionSkillIs) &&
            window != SkillProgramTriggerWindow.CardUseCompleted)
            Fail(path + ".condition", "cardUseConversionSkillIs requires a cardUseCompleted trigger");
        if (EnumerateTriggerConditions(condition).Any(item =>
                item.Kind == SkillProgramTriggerConditionKind.CardActionCategoryIs) &&
            window is not (SkillProgramTriggerWindow.CardUseCommitted or
                SkillProgramTriggerWindow.CardUseCompleted or SkillProgramTriggerWindow.CardEffectBeforeApply or SkillProgramTriggerWindow.CardUseBeforeTargetEffects or SkillProgramTriggerWindow.CardUseTargetsFinalized))
            Fail(path + ".condition", "cardActionCategoryIs requires a card-use trigger");
        if (EnumerateTriggerConditions(condition).Any(item =>
                item.Kind == SkillProgramTriggerConditionKind.CardActionFromOwnerHand) &&
            !isCardActionWindow)
            Fail(path + ".condition", "cardActionFromOwnerHand requires a card-action trigger");
        if (!isHpWindow && EnumerateTriggerValues(condition).Any(value => value.Kind is
                SkillProgramTriggerValueKind.HpChangeAmount or SkillProgramTriggerValueKind.HpBeforeChange or
                SkillProgramTriggerValueKind.HpAfterChange))
            Fail(path + ".condition", "HP change values require afterHpLost or afterHpRecovered");
        if (window != SkillProgramTriggerWindow.CardsGained && EnumerateTriggerValues(condition).Any(value => value.Kind is
                SkillProgramTriggerValueKind.DestinationZoneCountBefore or SkillProgramTriggerValueKind.DestinationZoneCountAfter))
            Fail(path + ".condition", "destination-zone values require cardsGained");
        if (!isMovementWindow && EnumerateTriggerValues(condition).Any(value => value.Kind == SkillProgramTriggerValueKind.MovedCardCount))
            Fail(path + ".condition", "card-movement values are supported only by cardsMoved or cardsGained");
        if (window != SkillProgramTriggerWindow.CardsMoved &&
            EnumerateTriggerValues(condition).Any(value => value.Kind is
                SkillProgramTriggerValueKind.SourceZoneCountBefore or
                SkillProgramTriggerValueKind.SourceZoneCountAfter))
            Fail(path + ".condition", "card-movement values are supported only by cardsMoved");
        var finalTargetSlash = node.TryGetProperty("effects", out var finalEffects) && finalEffects.ValueKind == JsonValueKind.Array && finalEffects.EnumerateArray().Any(effect => effect.ValueKind == JsonValueKind.Object && effect.TryGetProperty("op", out var op) && op.ValueKind == JsonValueKind.String && (string.Equals(op.GetString(), "preventCurrentTargetSlashCancellation", StringComparison.OrdinalIgnoreCase) || string.Equals(op.GetString(), "addCurrentTargetSlashDamage", StringComparison.OrdinalIgnoreCase)));
        if (finalTargetSlash && (window != SkillProgramTriggerWindow.CardUseTargetsFinalized ||
            ownerRelation != SkillProgramCardActionOwnerRelation.Actor || cardKinds.Count == 0 ||
            cardKinds.Any(card => card is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash))))
            Fail(path, "final target Slash facts require actual finalized actor Use and explicit Slash family kinds");
        if (EnumerateTriggerValues(condition).Any(value => value.Kind == SkillProgramTriggerValueKind.EventTargetHp) && !finalTargetSlash &&
            window is not (SkillProgramTriggerWindow.BeforeDamageApplied or
                SkillProgramTriggerWindow.AfterDamageApplied))
            Fail(path + ".condition", "eventTargetHp requires a damage trigger");
        if (EnumerateTriggerValues(condition).Any(value => value.Kind is
                SkillProgramTriggerValueKind.SourceToTargetDistanceAtDamage or
                SkillProgramTriggerValueKind.EventTargetMaxHp) &&
            window != SkillProgramTriggerWindow.AfterDamageApplied)
            Fail(path + ".condition", "damage participant values require afterDamageApplied");
        if (EnumerateTriggerValues(condition).Any(value => value.Kind == SkillProgramTriggerValueKind.CardUseDesignatedTargetCount) &&
            (!isCardActionWindow || window == SkillProgramTriggerWindow.CardResponseAccepted))
            Fail(path + ".condition", "cardUseDesignatedTargetCount requires a card-use trigger");
        if (EnumerateTriggerValues(condition).Any(value => value.Kind == SkillProgramTriggerValueKind.EventTargetHandCount) &&
            window is not (SkillProgramTriggerWindow.SlashBeforeResponse or SkillProgramTriggerWindow.TurnEnding) && !finalTargetSlash)
            Fail(path + ".condition", "target-hand comparison requires a Slash response or turn-ending boundary");
        if (EnumerateTriggerValues(condition).Any(value => value.Kind == SkillProgramTriggerValueKind.CurrentAttackRange) &&
            window is not (SkillProgramTriggerWindow.SlashBeforeResponse or
                SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardResponseAccepted or SkillProgramTriggerWindow.JudgmentPhaseStarting))
            Fail(path + ".condition", "attack-range comparison requires a card-action or Slash response boundary");
        if (EnumerateTriggerValues(condition).Any(value =>
                value.Kind == SkillProgramTriggerValueKind.OwnerEventTargetDistance) &&
            (!isCardActionWindow || window == SkillProgramTriggerWindow.CardResponseAccepted) && window != SkillProgramTriggerWindow.JudgmentPhaseStarting)
            Fail(path + ".condition", "owner-to-event-target distance requires a card-use trigger with a target");
        if (EnumerateTriggerValues(condition).Any(value => value.Kind is
                SkillProgramTriggerValueKind.PlayPhaseKillCountByTurnOwner or
                SkillProgramTriggerValueKind.PlayPhaseDamageDealtByTurnOwner) &&
            window is not (SkillProgramTriggerWindow.TurnEnding or
                SkillProgramTriggerWindow.PlayEnding or
                SkillProgramTriggerWindow.PlayPhaseStarting))
            Fail(path + ".condition", "play-phase kill and damage counters require a phase boundary trigger");
        if (EnumerateTriggerValues(condition).Any(value => value.Kind == SkillProgramTriggerValueKind.PlayPhaseDamageTakenByAny) &&
            window is not (SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.PlayEnding or SkillProgramTriggerWindow.PlayPhaseStarting))
            Fail(path + ".condition", "global play damage count requires a play or card-use boundary");
        var effects = ReadArray(node, "effects", path,
            (effect, effectPath) => ParseCompositionEffect(effect, effectPath,
                isAfterDamageTrigger: window == SkillProgramTriggerWindow.AfterDamageApplied,
                allowZeroDraw: drawPhaseMode == SkillProgramDrawPhaseMode.Replacement));
        if (effects.Any(effect => effect.Op is SkillProgramEffectOp.ReceiveOwnerDamage or SkillProgramEffectOp.ConsumeDistinctTurnTarget))
            Fail(path, "received-damage cost and distinct-turn-target commitment require an active Play instruction");
        if (effects.Any(effect => effect.Op == SkillProgramEffectOp.DrawOwnerAtAppliedDamage) &&
            (window != SkillProgramTriggerWindow.AfterDamageApplied || effects.Count != 2 ||
             effects[0].Op != SkillProgramEffectOp.DrawOwnerAtAppliedDamage ||
             effects[1] is not { Op: SkillProgramEffectOp.SelectAndMoveOwnedCard, AwaitMovementTriggers: true,
                 SkipIfNoCards: true, Destination: SkillProgramCardDestination.DiscardPile } appliedFieldDiscard ||
             appliedFieldDiscard.ChooserRef?.Kind != ProgramParticipantRef.Owner || appliedFieldDiscard.CardOwnerRef?.Kind != ProgramParticipantRef.EventTarget ||
             appliedFieldDiscard.Zones.Count == 0 || appliedFieldDiscard.Zones.Any(zone => zone is not (CardZoneKind.Equipment or CardZoneKind.Judgment)) ||
             appliedFieldDiscard.Condition.Kind != SkillProgramConditionKind.Always))
            Fail(path, "applied-damage benefit requires the finite draw then awaited optional-field-discard producer");
        if (window == SkillProgramTriggerWindow.CardUseCommitted && includeResponseUses &&
            (effects.Count != 1 || effects[0].Op != SkillProgramEffectOp.IssueCardNoResponseAndPlayUseBan))
            Fail(path, "committed response uses require the single issued card-policy operation");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.IssueCardNoResponseAndPlayUseBan) &&
            (window != SkillProgramTriggerWindow.CardUseCommitted || ownerRelation != SkillProgramCardActionOwnerRelation.Actor || effects.Count != 1 ||
             !RequiresPositiveTriggerCondition(condition, SkillProgramTriggerConditionKind.CardActionActorIsCurrentTurn) ||
             !RequiresPositiveTriggerCondition(condition, SkillProgramTriggerConditionKind.CardActionPhaseIsPlay)))
            Fail(path, "issued card policy requires a single own Play-use actor operation");
        if (window is SkillProgramTriggerWindow.DyingEntered or SkillProgramTriggerWindow.DyingExited &&
            (optional || effects.Any(effect => effect.Op != SkillProgramEffectOp.Draw || effect.Target != SkillProgramEffectTarget.Owner ||
                effect.NumberExpression is not null || effect.Condition.Kind != SkillProgramConditionKind.Always)))
            Fail(path, "dying transition triggers require mandatory constant owner draws");
        foreach (var effect in effects.Where(item => item.Op == SkillProgramEffectOp.SkipTurnPhases))
        {
            var valid = window switch
            {
                SkillProgramTriggerWindow.TurnStartBeforeNormalFlow =>
                    effect.SkippedPhases.All(phase => phase is SkillProgramTurnPhase.Judgment or SkillProgramTurnPhase.Draw),
                SkillProgramTriggerWindow.JudgmentPhaseStarting =>
                    effect.SkippedPhases.SequenceEqual([SkillProgramTurnPhase.Judgment]),
                SkillProgramTriggerWindow.DrawPhaseStarting =>
                    effect.SkippedPhases.SequenceEqual([SkillProgramTurnPhase.Draw]),
                SkillProgramTriggerWindow.AfterNormalDraw =>
                    effect.SkippedPhases.SequenceEqual([SkillProgramTurnPhase.Play]),
                SkillProgramTriggerWindow.DiscardPhaseStarting =>
                    effect.SkippedPhases.SequenceEqual([SkillProgramTurnPhase.Discard]),
                _ => false
            };
            if (!valid) Fail(path + ".effects", "phase substitution does not match its lifecycle boundary");
        }
        if (effects.Count == 0) Fail(path + ".effects", "must contain at least one effect");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.GrantFactionPopulationMarker) &&
            (window != SkillProgramTriggerWindow.GameStarting || subject != SkillProgramTriggerSubject.Owner || optional ||
             effects.Count != 1 || effects[0].Condition.Kind != SkillProgramConditionKind.Always))
            Fail(path, "population marker grant requires a single mandatory game-start owner operation");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.RemoveSelectedCurrentArrowBarrageTarget || e.TargetKind == SkillProgramTargetKind.CurrentArrowBarrageTargets) &&
            (window != SkillProgramTriggerWindow.CardUseTargetsFinalized || ownerRelation != SkillProgramCardActionOwnerRelation.Actor ||
             !cardKinds.SequenceEqual([CardKind.ArrowBarrage]) || !optional || effects.Count != 2 ||
             effects[0] is not { Op: SkillProgramEffectOp.SelectTargets, TargetKind: SkillProgramTargetKind.CurrentArrowBarrageTargets, MinimumTargets: 1, MaximumTargets: 1 } ||
             effects[1].Op != SkillProgramEffectOp.RemoveSelectedCurrentArrowBarrageTarget || condition.Kind != SkillProgramTriggerConditionKind.Always))
            Fail(path, "ArrowBarrage exclusion requires the exact optional actor finalization selection and single removal");
        if (effects.Any(effect => effect.Op is SkillProgramEffectOp.RecoverToMaximum or SkillProgramEffectOp.DrawRecoveryReceipt) &&
            (window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || subject != SkillProgramTriggerSubject.Owner ||
             effects.Count != 2 || effects[0].Op != SkillProgramEffectOp.RecoverToMaximum || effects[1].Op != SkillProgramEffectOp.DrawRecoveryReceipt))
            Fail(path + ".effects", "maximum recovery requires the preparation owner recovery then receipt draw pair");
        if (effects.Any(effect => effect.Op == SkillProgramEffectOp.ReserveNextSlashDamage) &&
            (window != SkillProgramTriggerWindow.SlashFullyDodged || ownerRelation != SkillProgramCardActionOwnerRelation.Actor || effects.Count != 1 || optional))
            Fail(path + ".effects", "cancellation reserve requires the actual actor's mandatory fully-dodged Slash boundary");
        if (effects.Any(effect => effect.TargetKind == SkillProgramTargetKind.CurrentCardUseTargets) &&
            window is not (SkillProgramTriggerWindow.CardEffectBeforeApply or SkillProgramTriggerWindow.CardUseBeforeTargetEffects or SkillProgramTriggerWindow.CardUseTargetsFinalized) ||
            effects.Any(effect => effect.Op == SkillProgramEffectOp.NullifySelectedCardEffects) &&
            window != SkillProgramTriggerWindow.CardUseBeforeTargetEffects)
            Fail(path + ".effects", "current target selection requires a finalized or before-effect use; nullification requires before-effect use");
        if (effects.Any(effect => effect.Op == SkillProgramEffectOp.ProhibitCurrentResponse) &&
            window != SkillProgramTriggerWindow.SlashBeforeResponse)
            Fail(path + ".effects", "response prohibition requires the Slash-before-response boundary");
        if (effects.Any(effect => effect.Op == SkillProgramEffectOp.RedirectCurrentAttack ||
                effect.TargetKind == SkillProgramTargetKind.SlashRedirectable) &&
            window != SkillProgramTriggerWindow.SlashTargetRedirecting)
            Fail(path + ".effects", "Slash redirection requires the target-redirection boundary");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.DrawByDamageCardColor) &&
            (window != SkillProgramTriggerWindow.DamageAppliedBeforeDying || subject != SkillProgramTriggerSubject.DamageTarget ||
             optional || effects.Count != 1 || damageOccurrence != SkillProgramDamageOccurrence.PerDamage))
            Fail(path + ".effects", "Damage appearance Draw requires its single mandatory damage-target before-dying trigger.");
        if (window == SkillProgramTriggerWindow.DamageAppliedBeforeDying &&
            effects.Any(effect => effect.Op is not (SkillProgramEffectOp.ChangeAttributedMarker or SkillProgramEffectOp.DrawByDamageCardColor) ||
                                  effect.Target != SkillProgramEffectTarget.Owner))
            Fail(path + ".effects", "damageAppliedBeforeDying supports only owner attributed-marker records");
        if (effects.Any(effect => ContainsCardUseColorCondition(effect.Condition)) && !isCardActionWindow)
            Fail(path + ".effects", "cardUseIsRed requires a card-action trigger");
        if (window != SkillProgramTriggerWindow.AfterDamageApplied &&
            !(window == SkillProgramTriggerWindow.BeforeDamageApplied && subject == SkillProgramTriggerSubject.DamageSource) &&
            window is not (SkillProgramTriggerWindow.JudgmentReplacing or SkillProgramTriggerWindow.JudgmentFinalized) &&
            ownerRelation is not (SkillProgramCardActionOwnerRelation.Target or
                SkillProgramCardActionOwnerRelation.ConversionSource) &&
            !(window == SkillProgramTriggerWindow.CardUseTargetsFinalized &&
                effects.Any(effect => effect.Op is SkillProgramEffectOp.ClaimCurrentUsePhysicalCards or SkillProgramEffectOp.PreventCurrentTargetSlashCancellationByRule)) &&
            window is not (SkillProgramTriggerWindow.SlashFullyDodged or SkillProgramTriggerWindow.SlashBeforeResponse or
                SkillProgramTriggerWindow.PlayPhaseStarting or SkillProgramTriggerWindow.PlayEnding or
                SkillProgramTriggerWindow.JudgmentPhaseStarting or SkillProgramTriggerWindow.CharacterTurnedOver or SkillProgramTriggerWindow.CharacterTurnedFaceUp or
                SkillProgramTriggerWindow.CharacterEnteredChain or SkillProgramTriggerWindow.CardUseCompleted or
                SkillProgramTriggerWindow.DiscardPhaseEnded or SkillProgramTriggerWindow.TurnEnding) &&
            effects.SelectMany(EnumerateParticipantReferences)
                .Any(reference => reference.Kind == ProgramParticipantRef.EventTarget))
            Fail(path + ".effects", "eventTarget requires a target-related card-action owner relation");
        if (effects.SelectMany(EnumerateParticipantReferences)
                .Any(reference => reference.Kind == ProgramParticipantRef.EventSource) &&
            window is not (SkillProgramTriggerWindow.AfterDamageApplied or
                SkillProgramTriggerWindow.DamageAppliedBeforeDying or
                SkillProgramTriggerWindow.BeforeDamageApplied or
                SkillProgramTriggerWindow.JudgmentFinalized or
                SkillProgramTriggerWindow.DiscardPhaseEnded or
                SkillProgramTriggerWindow.OtherActualUseTargeted) &&
            !(window == SkillProgramTriggerWindow.CardsGained && movementOccurrence == SkillProgramMovementOccurrence.PerSourceOwner))
            Fail(path + ".effects", "eventSource requires a damage-applied, judgment or discard-phase-ended trigger");
        if (window == SkillProgramTriggerWindow.JudgmentReplacing &&
            (effects[0].Op is not (SkillProgramEffectOp.ReplaceJudgment or SkillProgramEffectOp.DelegateJudgmentReplacement) ||
             effects.Skip(1).Any(effect => effect.Op is not
                 (SkillProgramEffectOp.Draw or SkillProgramEffectOp.Recover) ||
                 effect.Target != SkillProgramEffectTarget.Owner ||
                 effect.ReplacementSuits.Count == 0)))
            Fail(path + ".effects", "judgmentReplacing requires replaceJudgment first, followed only by draw or recover");
        if (window != SkillProgramTriggerWindow.JudgmentReplacing &&
            effects.Any(effect => effect.Op is SkillProgramEffectOp.ReplaceJudgment or SkillProgramEffectOp.DelegateJudgmentReplacement ||
                                  effect.ReplacementSuits.Count > 0))
            Fail(path + ".effects", "judgment replacement effects require judgmentReplacing");
        if (window != SkillProgramTriggerWindow.JudgmentFinalized &&
            effects.Any(effect => effect.Op == SkillProgramEffectOp.ClaimJudgmentCard))
            Fail(path + ".effects", "claimJudgmentCard requires judgmentFinalized");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.DelegateJudgmentReplacement) &&
            (effects.Count != 1 || subject != SkillProgramTriggerSubject.Any || !optional))
            Fail(path, "delegated judgment requires one optional any-subject replacement operation");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.GiveAfterBatchGain) &&
            (effects.Count != 1 || window != SkillProgramTriggerWindow.CardsGained || subject != SkillProgramTriggerSubject.Owner ||
             movementOccurrence != SkillProgramMovementOccurrence.PerBatch || optional || usageScope is not null || usageLimit is not null))
            Fail(path, "batch gain gift requires one owner perBatch cardsGained operation with its own actual-phase quota");
        var movementDiscardOnly = node.TryGetProperty("movementDiscardOnly", out _) && RequiredBool(node, "movementDiscardOnly", path);

        if (effects.Any(e => e.Op == SkillProgramEffectOp.RevealRedLossAndDraw) &&
            (effects.Count != 1 || window != SkillProgramTriggerWindow.CardsMoved || subject != SkillProgramTriggerSubject.Owner ||
             movementOccurrence != SkillProgramMovementOccurrence.PerOwnerBatch || optional || movementDiscardOnly || usageScope is not null || usageLimit is not null))
            Fail(path, "red loss requires one mandatory owner perOwnerBatch loss operation");
        var allowNoEventTarget = node.TryGetProperty("allowNoEventTarget", out _) && RequiredBool(node, "allowNoEventTarget", path);
        if (allowNoEventTarget && (!isCardActionWindow || ownerRelation != SkillProgramCardActionOwnerRelation.ConversionSource ||
            effects.SelectMany(EnumerateParticipantReferences).Any(reference => reference.Kind == ProgramParticipantRef.EventTarget)))
            Fail(path + ".allowNoEventTarget", "requires a conversion-source card action without event-target participants");
        var allowOwnDiscardPhaseEnded = node.TryGetProperty("allowOwnDiscardPhaseEnded", out _) && RequiredBool(node, "allowOwnDiscardPhaseEnded", path);
        if (node.TryGetProperty("allowOwnDiscardPhaseEnded", out _) && (window != SkillProgramTriggerWindow.DiscardPhaseEnded ||
            turnOwnerScope != SkillProgramTurnOwnerScope.Own))
            Fail(path + ".allowOwnDiscardPhaseEnded", "requires an own discardPhaseEnded trigger");
        // Discard observers already produce one candidate per matching entity.
        // The provenance operation makes that existing boundary explicit without
        // changing the occurrence contract of historical discard programs.
        if (window == SkillProgramTriggerWindow.DiscardPileReceived &&
            effects.Any(e => e.Op is SkillProgramEffectOp.ClaimDiscardedEntityWithProvenance or SkillProgramEffectOp.StoreAdjacentDiscardedSlash or SkillProgramEffectOp.DrawFromOtherActualBasicDiscard))
            movementOccurrence = node.TryGetProperty("movementOccurrence", out _)
                ? EnumValue<SkillProgramMovementOccurrence>(node, "movementOccurrence", path)
                : effects.Any(e => e.Op == SkillProgramEffectOp.DrawFromOtherActualBasicDiscard)
                    ? SkillProgramMovementOccurrence.PerBatch : SkillProgramMovementOccurrence.PerCard;
        if (movementOccurrence == SkillProgramMovementOccurrence.PerSourceOwner && window != SkillProgramTriggerWindow.CardsGained)
            Fail(path + ".movementOccurrence", "perSourceOwner requires a cardsGained boundary");
        if (movementOccurrence == SkillProgramMovementOccurrence.PerOwnerBatch && window != SkillProgramTriggerWindow.CardsMoved)
            Fail(path + ".movementOccurrence", "perOwnerBatch requires a cardsMoved boundary");
        if (movementOccurrence == SkillProgramMovementOccurrence.PerThirdPartyHandGain && window != SkillProgramTriggerWindow.CardsGained)
            Fail(path + ".movementOccurrence", "perThirdPartyHandGain requires a cardsGained boundary");
        if (movementOccurrence == SkillProgramMovementOccurrence.PerOwnerSourceHandGain && window != SkillProgramTriggerWindow.CardsMoved)
            Fail(path + ".movementOccurrence", "perOwnerSourceHandGain requires a cardsMoved boundary");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.ClaimDiscardedEntityWithProvenance) &&
            (movementOccurrence != SkillProgramMovementOccurrence.PerCard || subject != SkillProgramTriggerSubject.Owner ||
             node.TryGetProperty("discardOwnerScope", out var provenanceScope) && !string.Equals(provenanceScope.GetString(), "other", StringComparison.OrdinalIgnoreCase) ||
             cardCategories.Count != 0 || movementReasons.Count != 0 || excludedMovementReasons.Count != 0))
            Fail(path, "Provenance claims require unfiltered other-player per-card discard/judgment origins.");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.StoreAdjacentDiscardedSlash) &&
            (window != SkillProgramTriggerWindow.DiscardPileReceived || movementOccurrence != SkillProgramMovementOccurrence.PerCard ||
             subject != SkillProgramTriggerSubject.Owner || optional || cardKinds.Count != 0 || cardCategories.Count != 0 ||
             movementReasons.Count != 0 || excludedMovementReasons.Count != 0 || node.TryGetProperty("discardOwnerScope", out _)))
            Fail(path, "Adjacent Slash storage requires an unfiltered mandatory owner per-card actual discard boundary.");
        if (effects.Any(e => e.Op is SkillProgramEffectOp.OfferCurrentSlashFireAndExtraTarget or SkillProgramEffectOp.PayCompletedUseDiscardOrLoseHp) &&
            ownerRelation != SkillProgramCardActionOwnerRelation.Actor)
            Fail(path, "Current Slash conversion and completed use payment require the actual actor relation.");
        LoseHpIfRevealedNonEquipmentDiffersDescriptor.Validate(path, effects, window, subject,
            movementOccurrence, movementDiscardOnly, sourceZones, turnOwnerScope,
            movementReasons, excludedMovementReasons, ignoreOwnSkillMovements);
        if (EnumerateTriggerValues(condition).Any(value => value.Kind == SkillProgramTriggerValueKind.MovedEquipmentCardCount) &&
            (window != SkillProgramTriggerWindow.CardsMoved || movementOccurrence != SkillProgramMovementOccurrence.PerOwnerBatch ||
             subject != SkillProgramTriggerSubject.Owner || movementDiscardOnly || !sourceZones.Contains(CardZoneKind.Equipment)))
            Fail(path + ".condition", "equipment-loss values require an actual owner-batch cardsMoved boundary including equipment");
        var discardOwnerScope = node.TryGetProperty("discardOwnerScope", out _)
            ? EnumValue<SkillProgramDiscardOwnerScope>(node, "discardOwnerScope", path) : SkillProgramDiscardOwnerScope.Other;
        if (node.TryGetProperty("discardOwnerScope", out _) && window != SkillProgramTriggerWindow.DiscardPileReceived)
            Fail(path + ".discardOwnerScope", "requires discardPileReceived");
        if (node.TryGetProperty("movementDiscardOnly", out _) && window is not (SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.DiscardPileReceived))
            Fail(path + ".movementDiscardOnly", "requires a cardsMoved boundary");
        var deferredOnly = node.TryGetProperty("deferredTurnEndOnly", out _) && RequiredBool(node, "deferredTurnEndOnly", path);
        if (effects.Any(effect => effect.Op == SkillProgramEffectOp.ReplaceSkillsOnPreparation) &&
            (window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow ||
             subject != SkillProgramTriggerSubject.Owner || turnOwnerScope != SkillProgramTurnOwnerScope.Own ||
             optional || usageScope != SkillUsageScope.Game || usageLimit != 1 ||
             effects[^1].Op != SkillProgramEffectOp.ReplaceSkillsOnPreparation))
            Fail(path, "preparation replacement requires a terminal forced once-game own preparation binding");
        if (effects.Any(effect => effect.Op == SkillProgramEffectOp.JudgeDamageTargetThenOfferSuitDiscard) &&
            (window != SkillProgramTriggerWindow.AfterDamageApplied || subject != SkillProgramTriggerSubject.Any ||
             !optional || damageOccurrence != SkillProgramDamageOccurrence.PerDamage || effects.Count != 1))
            Fail(path, "Damage-target suit payment requires a single optional any-subject per-damage actual damage binding.");
        if (effects.Count(effect => effect.Op == SkillProgramEffectOp.StartOwnedDamagePointJudgment) > 1)
            Fail(path, "A binding can own only one damage-point judgment producer.");
        if (effects.Any(effect => effect.Op == SkillProgramEffectOp.SuppressOwnSkillAfterAlcoholSlashDamage) &&
            (window != SkillProgramTriggerWindow.AfterDamageApplied || subject != SkillProgramTriggerSubject.DamageSource ||
             optional || effects.Count != 1))
            Fail(path, "Alcohol Slash suppression requires a single forced damage-source afterDamageApplied binding.");
        if (effects.Any(effect => effect.Op == SkillProgramEffectOp.ScheduleDeferredHandAlignment) &&
            (window != SkillProgramTriggerWindow.TurnEnding || subject != SkillProgramTriggerSubject.Owner ||
                turnOwnerScope != SkillProgramTurnOwnerScope.Own))
            Fail(path, "deferred alignment scheduling requires the source owner's own ending boundary");
        if (deferredOnly && (window != SkillProgramTriggerWindow.TurnEnding || subject != SkillProgramTriggerSubject.Owner ||
                turnOwnerScope != SkillProgramTurnOwnerScope.Own || optional || effects.Count != 1 ||
                effects[0].Op != SkillProgramEffectOp.ResolveDeferredHandAlignment) ||
            !deferredOnly && effects.Any(e => e.Op == SkillProgramEffectOp.ResolveDeferredHandAlignment))
            Fail(path, "a deferred end continuation requires one resolver, mandatory own TurnEnding and deferredTurnEndOnly");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.DiscardDamageTargetAndClaimMount) &&
            (window != SkillProgramTriggerWindow.AfterDamageApplied || subject != SkillProgramTriggerSubject.DamageSource ||
             effects.Count != 1 || !optional))
            Fail(path, "damage-target mount claim requires one optional actual after-damage source operation");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.ObtainDamageTargetCardAndResolveCategory) &&
            (window != SkillProgramTriggerWindow.AfterDamageApplied || subject != SkillProgramTriggerSubject.DamageSource ||
             effects.Count != 1 || !optional || damageOccurrence != SkillProgramDamageOccurrence.PerDamage))
            Fail(path, "damage-target obtain requires one optional per-damage actual after-damage source operation");
        if (effects.Any(e => e.NumberExpression == SkillProgramNumberExpression.CurrentTurnUsedCardCategoryCount) &&
            (window != SkillProgramTriggerWindow.PlayEnding || subject != SkillProgramTriggerSubject.Owner || turnOwnerScope != SkillProgramTurnOwnerScope.Own))
            Fail(path, "actual turn type-count draw requires an own Play-ending owner window");
        SourceCurseComposition.ValidateTrigger(path, effects, window, subject, turnOwnerScope, optional, ownerRelation, includeResponseUses);
        PublicPilePreparationComposition.ValidateTrigger(path, effects, window, subject, turnOwnerScope, optional, damageCardKinds, damageOccurrence);
        PrivateOfferComposition.ValidateTrigger(path, effects, window, subject, turnOwnerScope, optional);
        HalfHandPhaseDebtTriggerContract.Validate(path, effects, subject, optional);
        PhaseHandSeizureComposition.ValidateTrigger(path, effects, subject, optional);
        DyingSuitsAndEndingHistoryComposition.Validate(path, effects, window, subject, optional, usageScope, usageLimit, turnOwnerScope);
        OwnTrickAndHandCategoryComposition.Validate(path, effects, window, subject, optional);
        PaidTargetEndingComposition.Validate(path, effects, window, subject, turnOwnerScope, optional);
        GrantedEntityPhaseComposition.Validate(path, effects, window, subject, turnOwnerScope);
        FrozenFactionRecoveryComposition.Validate(path, effects, window, subject, usageScope, usageLimit);
        AlternativePhaseCostCompositionContract.Validate(path, effects, window, subject, turnOwnerScope);
        TurnDrawDebtComposition.Validate(path, effects, window, subject, optional, drawPhaseMode, turnOwnerScope);
        if (effects.Any(e => e.TargetKind == SkillProgramTargetKind.OtherLivingHandAtMostOwner) &&
            (window != SkillProgramTriggerWindow.TurnEnding || subject != SkillProgramTriggerSubject.Owner ||
             turnOwnerScope != SkillProgramTurnOwnerScope.Own))
            Fail(path, "at-most-owner hand targeting requires the exact owner Ending window");
        SuitPreventionAndJudgmentPlacementComposition.Validate(path, effects, window, subject, turnOwnerScope);
        if (effects.Any(e => e.Op == SkillProgramEffectOp.DiscardTargetHpCardsAndDamage))
            Fail(path, "Variable target-HP discard damage requires its standalone activation.");
        SignedDamagePaymentComposition.Validate(path, effects, window, subject);
        EquipmentPairDyingCardComposition.Validate(path, effects, window, subject);
        ActualDiscardRecoveryComposition.ValidateTrigger(path, effects, window, subject, optional, discardOwnerScope,
            movementDiscardOnly, suits, cardKinds, cardCategories, movementReasons, excludedMovementReasons, movementOccurrence);
        ShownEntityTurnPolicyComposition.Validate(path, effects, window, subject, turnOwnerScope);
        EquipmentDonationComposition.ValidateTrigger(path, effects, window, subject, turnOwnerScope, optional);
        DirectedDistanceDebtComposition.ValidateTrigger(path, effects, window, subject, turnOwnerScope, optional, ownerRelation, cardKinds);
        PlacedEquipmentBenefitComposition.Validate(path, effects, window, subject, turnOwnerScope, optional);
        PaidColorDamageClaimComposition.Validate(path, effects, window, subject, turnOwnerScope, optional);
        ForeignTurnContestAidComposition.Validate(path, effects, window, subject, optional, ownerRelation, cardKinds);
        RecipientContestConsequencesComposition.Trigger(path, effects, window, subject, optional, turnOwnerScope, usageScope, usageLimit);
        RecipientContestComposition.Validate(path, effects, window, 0, 0, SkillProgramTargetKind.AnyLiving, null);
        if (effects.Any(e => e.Op == SkillProgramEffectOp.DiscardDrawAndOfferUniqueHpPeer) &&
            (subject != SkillProgramTriggerSubject.Owner || !optional || cardKinds.Count != 0 || ownerRelation is not null))
            Fail(path, "unique-HP peer is an optional real actual-Slash-target owner operation, with no declaration-only filters");
        PrepDiscardEndingComposition.Validate(path, effects, window, subject, turnOwnerScope, optional);
        SlashTargetBenefitComposition.Validate(path, effects, window, subject, optional);
        ProgramCompositionValidator.Validate(path, effects, initialSelectedTarget: deferredOnly, window: window, drawPhaseMode: drawPhaseMode, cardActionRelation: ownerRelation, cardKinds: cardKinds, turnOwnerScope: turnOwnerScope);
        if (node.TryGetProperty("onlyDesignatedCardTargets", out _) && ownerRelation != SkillProgramCardActionOwnerRelation.Target) Fail(path + ".onlyDesignatedCardTargets", "requires a target-owner card trigger");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.ResolveDiscardBudgetParticipants) &&
            (window != SkillProgramTriggerWindow.DiscardPhaseEnded || subject != SkillProgramTriggerSubject.Owner || !allowOwnDiscardPhaseEnded || turnOwnerScope != SkillProgramTurnOwnerScope.Own))
            Fail(path, "discard budget requires an actual own discard-phase end");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.OfferRedDiscardRecoveryChoice) &&
            (window != SkillProgramTriggerWindow.TurnEnding || subject != SkillProgramTriggerSubject.Owner ||
             optional || turnOwnerScope != SkillProgramTurnOwnerScope.Own || effects.Count != 1))
            Fail(path, "red discard recovery requires one mandatory own turn-ending operation");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.PreventCurrentDamageAndDrawMultiple) &&
            (window != SkillProgramTriggerWindow.BeforeDamageApplied || subject != SkillProgramTriggerSubject.DamageTarget))
            Fail(path, "prevention draw requires an exact before-damage damage-target window");
        if (effects.Any(e => e.Op is SkillProgramEffectOp.RequestLegalSlashByNearest or SkillProgramEffectOp.OfferUnlimitedVirtualSlash))
            Fail(path, "legal nearest Slash and unlimited Slash require an active play program");        if (effects.Any(e => e.Op == SkillProgramEffectOp.PreventOwnPlayOutsideTargetRangeDamage) &&
            (window != SkillProgramTriggerWindow.BeforeDamageApplied || subject != SkillProgramTriggerSubject.DamageSource || optional))
            Fail(path, "own play range prevention requires a mandatory damage-source window");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.DiscardOutsideRangeAfterInsufficientUses) &&
            (window != SkillProgramTriggerWindow.PlayEnding || subject != SkillProgramTriggerSubject.Owner || optional))
            Fail(path, "range discard requires a mandatory own play ending window");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.OfferCompletedFactionCostGift) &&
            (window != SkillProgramTriggerWindow.CardUseCompleted || ownerRelation != SkillProgramCardActionOwnerRelation.Observer || includeResponseUses || optional || cardKinds.Count == 0 || cardKinds.Any(k => k is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash))))
            Fail(path, "faction completed cost gift requires a mandatory observer of true completed Slash-family use");
        if (effects.Any(effect => effect.ClaimHandLimitExemption is not null) &&
            (window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || subject != SkillProgramTriggerSubject.Owner))
            Fail(path, "exact judgment claim exemption requires an owner preparation trigger");
        if (effects.Any(effect => effect.AllBottomStateId is not null) &&
            (window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || subject != SkillProgramTriggerSubject.Owner))
            Fail(path, "all-bottom completion state requires an owner preparation trigger");
        if (requireDamageSource is not null &&
            (window != SkillProgramTriggerWindow.AfterDamageApplied || subject != SkillProgramTriggerSubject.Owner))
            Fail(path + ".requireDamageSource", "requires an afterDamageApplied owner trigger");
        var parsedTrigger = new SkillProgramTrigger(id, window, sourceSkillId, sourceViewAsId, subject, suits,
            minimumRank, maximumRank, excludedReasons, judgmentReasons, judgmentSource,
            cardKinds, sourceZones, movementOccurrence, damageOccurrence, drawPhaseMode, optional,
            condition, effects, priority, usageScope, usageLimit, choiceGroup, ownerRelation,
            cardCategories: cardCategories, damageCardKinds: damageCardKinds, turnOwnerScope: turnOwnerScope)
        {
            DynamicUsageLimit = dynamicLimit, NamedUsageGroup = namedUsage, GainPhaseQualification = gainPhase, RequireDamageSource = requireDamageSource,
            RequireNoCardConversion = requireNoCardConversion,
            DestinationZones = destinationZones,
            AllowNoEventTarget = allowNoEventTarget,
            IncludeResponseUses = includeResponseUses,
            SingleActionInstance = singleActionInstance,
            DeferredTurnEndOnly = deferredOnly,
            NoDyingAtActivation = node.TryGetProperty("noDyingAtActivation", out _) && RequiredBool(node, "noDyingAtActivation", path),
            OnlyDesignatedCardTargets = onlyDesignatedCardTargets,
            AllowOwnDiscardPhaseEnded = allowOwnDiscardPhaseEnded,
            MovementReasons = movementReasons,
            ExcludedMovementReasons = excludedMovementReasons,
            IgnoreOwnSkillMovements = ignoreOwnSkillMovements,
            MovementDiscardOnly = movementDiscardOnly,
            DiscardOwnerScope = discardOwnerScope,
            EvaluateConditionAtResolution = node.TryGetProperty("evaluateConditionAtResolution", out _) && RequiredBool(node, "evaluateConditionAtResolution", path),
            HpChangeOccurrence = hpChangeOccurrence,
            MarkerCost = ParseMarkerCost(node, path)
        };
        PairObtainFixedRecipientComposition.ValidateTrigger(path, parsedTrigger);
        ActualHandGainAndCategoryGiftComposition.ValidateTrigger(path, parsedTrigger);
        SelectIssuedFixedRecipientWithDeathReturnDescriptor.ValidateTrigger(path, parsedTrigger);
        EndingPairSlashLossComposition.ValidateTrigger(path, parsedTrigger);
        CappedConversionBenefitComposition.ValidateTrigger(path, parsedTrigger);
        return parsedTrigger;
    }

    private static SkillProgramMarkerCost? ParseMarkerCost(JsonElement node, string path)
    {
        if (!node.TryGetProperty("markerCost", out var cost)) return null;
        RequireObject(cost, path + ".markerCost");
        CheckProperties(cost, path + ".markerCost", "marker", "amount");
        var amount = PositiveInt(cost, "amount", path + ".markerCost");
        if (amount > 1024) Fail(path + ".markerCost.amount", "must not exceed 1024");
        return new(EnumValue<PlayerMarkerKind>(cost, "marker", path + ".markerCost"), amount);
    }
    private static void ValidateTriggerChoiceGroups(
        string path,
        IReadOnlyList<SkillProgramTrigger> triggers)
    {
        foreach (var group in triggers
                     .Where(trigger => trigger.ChoiceGroup is not null)
                     .GroupBy(trigger => trigger.ChoiceGroup!, StringComparer.Ordinal))
        {
            var members = group.ToArray();
            if (members.Length < 2)
                Fail(path + ".triggers", $"choice group '{group.Key}' requires at least two branches");
            if (members.Select(trigger => trigger.Window).Distinct().Count() != 1 ||
                members.Select(trigger => trigger.Optional).Distinct().Count() != 1)
                Fail(path + ".triggers", $"choice group '{group.Key}' requires one window and optional policy");
            var window = members[0].Window;
            if (window == SkillProgramTriggerWindow.DrawPhaseStarting &&
                members.Any(trigger => trigger.DrawPhaseMode != SkillProgramDrawPhaseMode.Additive ||
                                       !trigger.Optional || trigger.UsageScope is not null ||
                                       trigger.UsageLimit is not null ||
                                       trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always))
            {
                Fail(path + ".triggers",
                    $"choice group '{group.Key}' requires optional unconditional additive draw-phase branches without usage fields");
            }
            if (window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)
            {
                var sharedScope = members[0].UsageScope;
                if (members.Any(trigger => trigger.UsageScope != sharedScope || trigger.UsageLimit != 1) ||
                    sharedScope != SkillUsageScope.Game && sharedScope != SkillUsageScope.Turn)
                    Fail(path + ".triggers",
                        $"turn-start choice group '{group.Key}' requires one shared game or turn usage limit");
            }
            else if (window is not (SkillProgramTriggerWindow.DrawPhaseStarting or
                SkillProgramTriggerWindow.DrawPhaseEnded or
                SkillProgramTriggerWindow.PlayPhaseStarting))
                Fail(path + ".triggers", $"choice group '{group.Key}' uses unsupported window '{window}'");
            if (members.Select(trigger => trigger.Priority).Distinct().Count() != 1)
                Fail(path + ".triggers", $"choice group '{group.Key}' requires one shared priority");
        }
    }

    private static void ValidateRelativeZoneDemandActivation(string path, IReadOnlyList<SkillProgramEffect> effects,
        int minCards, int maxCards, int minTargets, int maxTargets, IReadOnlyList<CardZoneKind> zones, int? usesPerPhase)
    {
        if (!effects.Any(effect => effect.Op == SkillProgramEffectOp.SelectRelativeZoneDemandTarget)) return;
        if (minCards != 1 || maxCards != 1 || minTargets != 0 || maxTargets != 0 || usesPerPhase != 1 ||
            !zones.Order().SequenceEqual(new[] { CardZoneKind.Hand, CardZoneKind.Equipment }.Order()) || effects.Count < 3 ||
            effects[0] is not { Op: SkillProgramEffectOp.CaptureSelectedCards, Condition.Kind: SkillProgramConditionKind.Always } ||
            effects[1] is not { Op: SkillProgramEffectOp.MoveBoundCards, Destination: SkillProgramCardDestination.DiscardPile, AwaitMovementTriggers: true, Condition.Kind: SkillProgramConditionKind.Always } ||
            effects[2] is not { Op: SkillProgramEffectOp.SelectRelativeZoneDemandTarget } ||
            effects[0].ResultBind != effects[1].SourceBind || effects[0].ResultBind != effects[2].SourceBind ||
            effects.Count(effect => effect.Op == SkillProgramEffectOp.SelectRelativeZoneDemandTarget) != 1)
            Fail(path, "relative-zone demand requires one HE cost captured, discarded and awaited before its sole declaration, no initial target, and one use per play phase");
    }

    private static void ValidateTriggerSources(IReadOnlyDictionary<string, SkillProgram> programs)
    {
        SourceCurseComposition.ValidatePrograms(programs);
        DirectedDistanceDebtComposition.ValidatePrograms(programs);
        PublicPilePreparationComposition.ValidatePrograms(programs);
        foreach (var owner in programs.Values)
            foreach (var trigger in owner.Triggers)
            {
                var path = $"skill '{owner.Id}'.triggers.{trigger.Id}";
                foreach (var reward in trigger.Effects.Where(effect => effect.Op == SkillProgramEffectOp.DrawOnFirstProgramTargetEncounter))
                    if (!programs.TryGetValue(reward.SkillIds.Single(), out var declarationProgram) ||
                        declarationProgram.Activations.SingleOrDefault(item => item.Id == reward.StateId)?.Effects.Any(effect => effect.Op == SkillProgramEffectOp.SelectRelativeZoneDemandTarget) != true)
                        Fail(path, "first-target reward requires a real configured relative-zone declaration activation");
                if (trigger.Window is SkillProgramTriggerWindow.JudgmentFinalized or
                    SkillProgramTriggerWindow.JudgmentReplacing or
                    SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or
                    SkillProgramTriggerWindow.DrawPhaseStarting or
                    SkillProgramTriggerWindow.AfterNormalDraw or SkillProgramTriggerWindow.DrawPhaseEnded or SkillProgramTriggerWindow.DrawPhaseSkipped or
                    SkillProgramTriggerWindow.SelfDyingResponse or
                    SkillProgramTriggerWindow.DyingResponse or SkillProgramTriggerWindow.DyingEntering or
                    SkillProgramTriggerWindow.BeforeDamageApplied or
                    SkillProgramTriggerWindow.DamageAppliedBeforeDying or
                    SkillProgramTriggerWindow.PlayEnding or
                    SkillProgramTriggerWindow.TurnEnding or
                    SkillProgramTriggerWindow.CardsMoved or
                    SkillProgramTriggerWindow.OwnerDied) continue;
                if (trigger.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
                    trigger.Subject != SkillProgramTriggerSubject.Source) continue;
                if (trigger.OwnerRelation != SkillProgramCardActionOwnerRelation.ConversionSource &&
                    trigger.Subject != SkillProgramTriggerSubject.Source) continue;
                if (trigger.CardKinds.Count > 0 || trigger.CardCategories.Count > 0) continue;
                var sourceSkillId = trigger.SourceSkillId;
                if (sourceSkillId is null)
                    Fail(path + ".sourceSkillId", $"references unknown skill '{trigger.SourceSkillId}'");
                if (!programs.TryGetValue(sourceSkillId!, out var source))
                    Fail(path + ".sourceSkillId", $"references unknown skill '{sourceSkillId}'");
                var ordinaryTrickSources = source.Activations.SelectMany(activation => activation.Effects)
                    .Where(effect => effect.Op == SkillProgramEffectOp.UseAllHandCardsAsOrdinaryTrick &&
                        (trigger.SourceViewAsId is null || effect.SourceBind == trigger.SourceViewAsId)).ToArray();
                if (source.ViewAs.Count == 0 && ordinaryTrickSources.Length == 0)
                    Fail(path + ".sourceSkillId", $"skill '{trigger.SourceSkillId}' has no viewAs rules");
                var candidates = trigger.SourceViewAsId is null
                    ? source.ViewAs
                    : source.ViewAs.Where(rule => rule.Id == trigger.SourceViewAsId).ToArray();
                if (trigger.SourceViewAsId is not null && candidates.Count == 0 && ordinaryTrickSources.Length == 0)
                    Fail(path + ".sourceViewAsId", $"references unknown viewAs '{trigger.SourceViewAsId}'");
                var supported = trigger.Window switch
                {
                    SkillProgramTriggerWindow.CardUseCompleted when trigger.IncludeResponseUses =>
                        candidates.Any(rule => rule.ForPlay || rule.ForResponse) || ordinaryTrickSources.Length > 0,
                    SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardUseTargetsFinalized or
                        SkillProgramTriggerWindow.CardEffectBeforeApply or SkillProgramTriggerWindow.CardUseBeforeTargetEffects or SkillProgramTriggerWindow.CardUseCompleted =>
                        candidates.Any(rule => rule.ForPlay) || ordinaryTrickSources.Length > 0,
                    SkillProgramTriggerWindow.CardResponseAccepted => candidates.Any(rule => rule.ForResponse),
                    SkillProgramTriggerWindow.AfterDamageApplied => candidates.Any(rule => rule.ForPlay) || ordinaryTrickSources.Length > 0,
                    _ => false
                };
                if (!supported)
                    Fail(path, $"source viewAs does not support window '{Camel(trigger.Window)}'");
            }
    }

    private static string Camel<T>(T value) where T : struct, Enum
    {
        var name = Enum.GetName(value)!;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static SkillProgramCondition OptionalCondition(JsonElement owner, string path) =>
        owner.TryGetProperty("condition", out var condition) ? ParseCondition(condition, path + ".condition", 0) : Always;

    private static SkillProgramTriggerCondition OptionalTriggerCondition(JsonElement owner, string path) =>
        owner.TryGetProperty("condition", out var condition)
            ? ParseTriggerCondition(condition, path + ".condition", 0)
            : AlwaysTrigger;

    private static SkillProgramTriggerCondition ParseTriggerCondition(
        JsonElement node,
        string path,
        int depth)
    {
        if (depth >= MaximumDepth) Fail(path, $"trigger condition nesting exceeds {MaximumDepth}");
        RequireObject(node, path);
        CheckProperties(node, path, "kind", "children", "left", "operator", "right", "stateId", "expectedValue", "generalIds", "conversionSkillId", "categories", "factions", "gender", "suits");
        var kind = EnumValue<SkillProgramTriggerConditionKind>(node, "kind", path);
        var hasChildren = node.TryGetProperty("children", out var childrenNode);
        var hasLeft = node.TryGetProperty("left", out var leftNode);
        var hasOperator = node.TryGetProperty("operator", out _);
        var hasRight = node.TryGetProperty("right", out var rightNode);
        var hasStateId = node.TryGetProperty("stateId", out _);
        var hasExpectedValue = node.TryGetProperty("expectedValue", out _);
        var hasGeneralIds = node.TryGetProperty("generalIds", out _);
        var hasConversionSkillId = node.TryGetProperty("conversionSkillId", out _);
        var hasCategories = node.TryGetProperty("categories", out var categoriesNode);
        var hasFactions = node.TryGetProperty("factions", out _);
        var children = new List<SkillProgramTriggerCondition>();
        if (hasChildren)
        {
            if (childrenNode.ValueKind != JsonValueKind.Array) Fail(path + ".children", "must be an array");
            CheckCount(childrenNode.GetArrayLength(), path + ".children");
            var index = 0;
            foreach (var child in childrenNode.EnumerateArray())
                children.Add(ParseTriggerCondition(child, $"{path}.children[{index++}]", depth + 1));
        }
        var composite = kind is SkillProgramTriggerConditionKind.All or
            SkillProgramTriggerConditionKind.Any or SkillProgramTriggerConditionKind.Not;
        if (composite != hasChildren)
            Fail(path, composite ? "this trigger condition requires children" : "this trigger condition does not accept children");
        var compare = kind == SkillProgramTriggerConditionKind.Compare;
        if (compare && !(hasLeft && hasOperator && hasRight) ||
            !compare && (hasLeft || hasOperator || hasRight))
            Fail(path, compare
                ? "compare requires left, operator and right"
                : "this trigger condition does not accept comparison fields");
        if (kind == SkillProgramTriggerConditionKind.Not && children.Count != 1)
            Fail(path + ".children", "not requires exactly one child");
        if (kind is SkillProgramTriggerConditionKind.All or SkillProgramTriggerConditionKind.Any && children.Count == 0)
            Fail(path + ".children", "all and any require at least one child");
        var booleanState = kind == SkillProgramTriggerConditionKind.BooleanState;
        if (booleanState != hasStateId)
            Fail(path, booleanState ? "booleanState requires stateId" : "this trigger condition does not accept stateId");
        if (!booleanState && hasExpectedValue)
            Fail(path, "this trigger condition does not accept expectedValue");
        var lordGeneralNotIn = kind == SkillProgramTriggerConditionKind.LordGeneralNotIn;
        if (lordGeneralNotIn != hasGeneralIds)
            Fail(path, lordGeneralNotIn ? "lordGeneralNotIn requires generalIds" :
                "this trigger condition does not accept generalIds");
        var generalIds = lordGeneralNotIn ? StringArray(node, "generalIds", path) : Array.Empty<string>();
        if (lordGeneralNotIn && generalIds.Count == 0)
            Fail(path + ".generalIds", "must not be empty");
        var conversionSkillIs = kind == SkillProgramTriggerConditionKind.CardUseConversionSkillIs;
        if (conversionSkillIs != hasConversionSkillId)
            Fail(path, conversionSkillIs ? "cardUseConversionSkillIs requires conversionSkillId" :
                "this trigger condition does not accept conversionSkillId");
        var cardActionCategoryIs = kind == SkillProgramTriggerConditionKind.CardActionCategoryIs;
        if (cardActionCategoryIs != hasCategories)
            Fail(path, cardActionCategoryIs ? "cardActionCategoryIs requires categories" :
                "this trigger condition does not accept categories");
        var cardCategories = cardActionCategoryIs
            ? EnumArray<SkillProgramCardCategory>(node, "categories", path)
            : Array.Empty<SkillProgramCardCategory>();
        if (cardActionCategoryIs && (cardCategories.Count == 0 ||
                cardCategories.Distinct().Count() != cardCategories.Count))
            Fail(path + ".categories", "must contain distinct card categories");
        var genderIs = kind == SkillProgramTriggerConditionKind.DamageSourceGenderIs;
        var suitIs = kind == SkillProgramTriggerConditionKind.CardActionSuitIs;
        if (suitIs != node.TryGetProperty("suits", out _)) Fail(path, "Only cardActionSuitIs requires suits.");
        var suits = suitIs ? EnumArray<Suit>(node, "suits", path) : Array.Empty<Suit>();
        if (suitIs && (suits.Count == 0 || suits.Distinct().Count() != suits.Count))
            Fail(path + ".suits", "must contain distinct suits");
        if (genderIs != node.TryGetProperty("gender", out _)) Fail(path, "Only damageSourceGenderIs requires gender.");
        var damageSourceFactionIs = kind == SkillProgramTriggerConditionKind.DamageSourceFactionIs;
        if (damageSourceFactionIs != hasFactions)
            Fail(path, damageSourceFactionIs ? "damageSourceFactionIs requires factions" :
                "this trigger condition does not accept factions");
        var factions = damageSourceFactionIs ? StringArray(node, "factions", path) : Array.Empty<string>();
        if (damageSourceFactionIs && factions.Count == 0)
            Fail(path + ".factions", "must not be empty");
        var left = compare ? ParseTriggerValue(leftNode, path + ".left") : null;
        SkillProgramComparisonOperator? comparison = compare
            ? EnumValue<SkillProgramComparisonOperator>(node, "operator", path)
            : null;
        var right = compare ? ParseTriggerValue(rightNode, path + ".right") : null;
        return new SkillProgramTriggerCondition(
            kind,
            new ReadOnlyCollection<SkillProgramTriggerCondition>(children),
            left,
            comparison,
            right,
            booleanState ? Identifier(node, "stateId", path) : null,
            booleanState && (!hasExpectedValue || RequiredBool(node, "expectedValue", path)),
            generalIds,
            conversionSkillIs ? Identifier(node, "conversionSkillId", path) : null,
            cardCategories,
            factions, genderIs ? EnumValue<GeneralGender>(node, "gender", path) : null, suits);
    }

    private static IEnumerable<SkillProgramTriggerValue> EnumerateTriggerValues(
        SkillProgramTriggerCondition condition)
    {
        if (condition.Left is not null) yield return condition.Left;
        if (condition.Right is not null) yield return condition.Right;
        foreach (var child in condition.Children)
            foreach (var value in EnumerateTriggerValues(child))
                yield return value;
    }

    private static bool RequiresPositiveTriggerCondition(SkillProgramTriggerCondition c, SkillProgramTriggerConditionKind kind) => c.Kind==kind || c.Kind==SkillProgramTriggerConditionKind.All && c.Children.Any(child=>RequiresPositiveTriggerCondition(child,kind));

    private static IEnumerable<SkillProgramTriggerCondition> EnumerateTriggerConditions(
        SkillProgramTriggerCondition condition)
    {
        yield return condition;
        foreach (var child in condition.Children)
            foreach (var nested in EnumerateTriggerConditions(child)) yield return nested;
    }

    private static IEnumerable<SkillProgramCondition> EnumerateConditions(SkillProgramCondition condition)
    {
        yield return condition;
        foreach (var child in condition.Children)
            foreach (var nested in EnumerateConditions(child)) yield return nested;
    }

    private static IEnumerable<ProgramParticipantReference> EnumerateParticipantReferences(SkillProgramEffect effect)
    {
        if (effect.ChooserRef is not null) yield return effect.ChooserRef;
        if (effect.CardOwnerRef is not null) yield return effect.CardOwnerRef;
        if (effect.OpponentReference is not null) yield return effect.OpponentReference;
        if (effect.ActorReference is not null) yield return effect.ActorReference;
        if (effect.TargetReference is not null) yield return effect.TargetReference;
        if (effect.SourceRef is not null) yield return effect.SourceRef;
    }

    private static SkillProgramTriggerValue ParseTriggerValue(JsonElement node, string path)
    {
        RequireObject(node, path);
        CheckProperties(node, path, "kind", "value", "zone", "marker");
        var kind = EnumValue<SkillProgramTriggerValueKind>(node, "kind", path);
        var hasValue = node.TryGetProperty("value", out _);
        var hasZone = node.TryGetProperty("zone", out _);
        var hasMarker = node.TryGetProperty("marker", out _);
        var usesMarker = kind is SkillProgramTriggerValueKind.EventTargetMarkerCount or SkillProgramTriggerValueKind.EventSourceMarkerCount or SkillProgramTriggerValueKind.OwnerAttributedMarkerCount or SkillProgramTriggerValueKind.MarkerParity or SkillProgramTriggerValueKind.GlobalMarkerCount;
        if (usesMarker != hasMarker)
            Fail(path, usesMarker ? "ownerAttributedMarkerCount requires marker" :
                "this trigger value does not accept marker");
        if ((kind == SkillProgramTriggerValueKind.IntegerConstant) != hasValue)
            Fail(path, kind == SkillProgramTriggerValueKind.IntegerConstant
                ? "integerConstant requires value"
                : "this trigger value does not accept value");
        var usesZone = kind == SkillProgramTriggerValueKind.CurrentOwnedZoneCount;
        if (usesZone != hasZone)
            Fail(path, usesZone ? "currentOwnedZoneCount requires zone" :
                "this trigger value does not accept zone");
        var zone = hasZone ? EnumValue<CardZoneKind>(node, "zone", path) : (CardZoneKind?)null;
        if (zone is not null && zone is not (CardZoneKind.WoodenOxGrain or CardZoneKind.BuquWound or
                CardZoneKind.Authority or CardZoneKind.Chunlao or CardZoneKind.PublicPersistentPile))
            Fail(path + ".zone", "must be an owner-scoped persistent named zone");
        return new SkillProgramTriggerValue(
            kind,
            hasValue ? RequiredInt(node, "value", path) : 0,
            zone,
            hasMarker ? EnumValue<PlayerMarkerKind>(node, "marker", path) : null);
    }

    private static SkillProgramCondition ParseCondition(
        JsonElement node, string path, int depth, bool defaultPindianBind = false, bool allowChoice = false,
        bool allowBoundCards = false, bool allowBoundCardCategories = false,
        bool allowBoundCardKinds = false,
        bool allowClaimableDamageCards = false,
        bool allowAttackRangeCoverage = false, bool allowBoundCardCount = false, bool allowOwnedCardCategory = false,
        bool allowRequestedSlashDamage = false)
    {
        if (depth >= MaximumDepth) Fail(path, $"condition nesting exceeds {MaximumDepth}");
        RequireObject(node, path);
        CheckProperties(node, path, "kind", "value", "children", "sourceBind", "stateId", "expectedValue", "optionId", "cardCategories", "cardKinds", "suits", "zones", "choiceBind", "gender");
        var kind = EnumValue<SkillProgramConditionKind>(node, "kind", path);
        var participantGenderIs = kind is SkillProgramConditionKind.EventTargetGenderIs or
            SkillProgramConditionKind.EventSourceGenderIs;
        if (participantGenderIs != node.TryGetProperty("gender", out var genderNode))
            Fail(path, participantGenderIs ? "participant gender conditions require gender" :
                "this condition does not accept gender");
        if (kind == SkillProgramConditionKind.ChoiceIs && !allowChoice)
            Fail(path, "choiceIs requires a composition instruction and an earlier named choice");
        if (kind == SkillProgramConditionKind.BoundCardsSameColor && !allowBoundCards)
            Fail(path, "boundCardsSameColor requires a composition instruction and an earlier card binding");
        if (kind == SkillProgramConditionKind.BoundCardsMatchCategories && !allowBoundCardCategories)
            Fail(path, "boundCardsMatchCategories requires a composition instruction and an earlier card binding");
        if (kind == SkillProgramConditionKind.BoundCardCategoryMatchesCardAction && !allowBoundCardCategories)
            Fail(path, "boundCardCategoryMatchesCardAction requires a composition instruction and an earlier card binding");
        if (kind == SkillProgramConditionKind.BoundCardsMatchKinds && !allowBoundCardKinds)
            Fail(path, "boundCardsMatchKinds requires a composition instruction and an earlier card binding");
        if (kind == SkillProgramConditionKind.BoundCardsMatchSuits && !allowBoundCards)
            Fail(path, "boundCardsMatchSuits requires a composition instruction and an earlier card binding");
        if (kind == SkillProgramConditionKind.HasClaimableDamageCards && !allowClaimableDamageCards)
            Fail(path, "hasClaimableDamageCards requires an after-damage composition window");
        if (kind == SkillProgramConditionKind.AttackRangeCoverageDecreased && !allowAttackRangeCoverage)
            Fail(path, "attackRangeCoverageDecreased requires a composition instruction with a coverage binding");
        if (kind == SkillProgramConditionKind.BoundCardCountAtLeast && !allowBoundCardCount)
            Fail(path, "boundCardCountAtLeast requires a bound-card option condition");
        if (kind == SkillProgramConditionKind.HasOwnedCardCategory && !allowOwnedCardCategory)
            Fail(path, "hasOwnedCardCategory requires an owner-card option condition");
        if (kind == SkillProgramConditionKind.RequestedSlashDamagedOwner)
        {
            if (!allowRequestedSlashDamage)
                Fail(path, "requestedSlashDamagedOwner requires a composition instruction after a requested slash");
            if (node.TryGetProperty("value", out var slashValue) && (slashValue.ValueKind != JsonValueKind.Number || slashValue.GetInt32() != 0) ||
                node.TryGetProperty("children", out _) || node.TryGetProperty("sourceBind", out _) ||
                node.TryGetProperty("stateId", out _) || node.TryGetProperty("optionId", out _) ||
                node.TryGetProperty("choiceBind", out _) || node.TryGetProperty("gender", out _) ||
                node.TryGetProperty("cardCategories", out _) || node.TryGetProperty("cardKinds", out _) ||
                node.TryGetProperty("suits", out _) || node.TryGetProperty("zones", out _))
                Fail(path, "requestedSlashDamagedOwner accepts no additional fields");
        }
        var hasValue = node.TryGetProperty("value", out var valueNode);
        var hasChildren = node.TryGetProperty("children", out var childrenNode);
        var value = hasValue ? GetInt(valueNode, path + ".value") : 0;
        var sourceBind = node.TryGetProperty("sourceBind", out _) ? Identifier(node, "sourceBind", path) : null;
        var stateId = node.TryGetProperty("stateId", out _) ? Identifier(node, "stateId", path) : null;
        var optionId = node.TryGetProperty("optionId", out _) ? Identifier(node, "optionId", path) : null;
        var choiceBind = node.TryGetProperty("choiceBind", out _) ? Identifier(node, "choiceBind", path) : null;
        var cardCategories = node.TryGetProperty("cardCategories", out _)
            ? EnumArray<SkillProgramCardCategory>(node, "cardCategories", path)
            : [];
        var cardKinds = node.TryGetProperty("cardKinds", out _)
            ? EnumArray<CardKind>(node, "cardKinds", path)
            : [];
        var suits = node.TryGetProperty("suits", out _)
            ? EnumArray<Suit>(node, "suits", path)
            : [];
        var zones = node.TryGetProperty("zones", out _)
            ? EnumArray<CardZoneKind>(node, "zones", path)
            : [];
        var expectedValue = node.TryGetProperty("expectedValue", out _) ? RequiredBool(node, "expectedValue", path) : true;
        var children = new List<SkillProgramCondition>();
        if (hasChildren)
        {
            if (childrenNode.ValueKind != JsonValueKind.Array) Fail(path + ".children", "must be an array");
            CheckCount(childrenNode.GetArrayLength(), path + ".children");
            var index = 0;
            foreach (var child in childrenNode.EnumerateArray())
                children.Add(ParseCondition(child, $"{path}.children[{index++}]", depth + 1,
                    defaultPindianBind, allowChoice, allowBoundCards, allowBoundCardCategories,
                    allowBoundCardKinds, allowClaimableDamageCards, allowAttackRangeCoverage,
                    allowBoundCardCount, allowOwnedCardCategory, allowRequestedSlashDamage));
        }
        var needsValue = kind is SkillProgramConditionKind.HpAtLeast or SkillProgramConditionKind.HandCountAtLeast or
            SkillProgramConditionKind.BoundCardCountAtLeast or SkillProgramConditionKind.ActivationCardCountAtLeast or SkillProgramConditionKind.PublicCounterAtLeast;
        if (needsValue != hasValue) Fail(path, needsValue ? "this condition requires value" : "this condition does not accept value");
        if (needsValue && value < 0) Fail(path + ".value", "must be non-negative");
        var composite = kind is SkillProgramConditionKind.All or SkillProgramConditionKind.Any or SkillProgramConditionKind.Not;
        if (composite != hasChildren) Fail(path, composite ? "this condition requires children" : "this condition does not accept children");
        if (kind == SkillProgramConditionKind.Not && children.Count != 1) Fail(path + ".children", "not requires exactly one child");
        if (kind is SkillProgramConditionKind.All or SkillProgramConditionKind.Any && children.Count == 0)
            Fail(path + ".children", "all and any require at least one child");
        var pindianCondition = kind is SkillProgramConditionKind.PindianWon or SkillProgramConditionKind.PindianNotWon;
        if (pindianCondition && sourceBind is null && defaultPindianBind)
            sourceBind = "__active-pindian-result";
        var choiceCondition = kind == SkillProgramConditionKind.ChoiceIs;
        var boundCardCondition = kind is SkillProgramConditionKind.BoundCardsSameColor or
            SkillProgramConditionKind.BoundCardsMatchCategories or SkillProgramConditionKind.BoundCardsMatchKinds or
            SkillProgramConditionKind.BoundCardsMatchSuits or
            SkillProgramConditionKind.AttackRangeCoverageDecreased or SkillProgramConditionKind.BoundCardCountAtLeast or
            SkillProgramConditionKind.BoundCardSuitMatchesChoice or
            SkillProgramConditionKind.BoundCardCategoryMatchesCardAction;
        if ((pindianCondition || choiceCondition || boundCardCondition) != (sourceBind is not null))
            Fail(path, "sourceBind is required only for named result conditions");
        if (choiceCondition != (optionId is not null))
            Fail(path, "optionId is required only for choiceIs");
        // An owned-card option may filter by broad category, by exact kind, or by both. Category-only
        // conditions keep requiring cardCategories so existing content cannot silently widen a filter.
        // The wording stays aligned with the historical messages because shared definition checks
        // assert on rejection fragments rather than exception types.
        if (kind is SkillProgramConditionKind.BoundCardsMatchCategories && cardCategories.Count == 0)
            Fail(path, "cardCategories are required for card-category conditions and must not be empty");
        if (kind == SkillProgramConditionKind.HasOwnedCardCategory && cardCategories.Count == 0 && cardKinds.Count == 0)
            Fail(path, "cardCategories are required for card-category conditions and must not be empty");
        if (kind is not (SkillProgramConditionKind.BoundCardsMatchCategories or
             SkillProgramConditionKind.HasOwnedCardCategory) && cardCategories.Count > 0)
            Fail(path, "cardCategories are required for card-category conditions and must not be empty");
        if (zones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) ||
            (kind == SkillProgramConditionKind.HasOwnedCardCategory) != (zones.Count > 0))
            Fail(path, "zones are required for hasOwnedCardCategory and support hand or equipment only");
        if (kind == SkillProgramConditionKind.BoundCardsMatchKinds && cardKinds.Count == 0)
            Fail(path, "cardKinds are required only for boundCardsMatchKinds and must be distinct and nonempty");
        if (kind is not (SkillProgramConditionKind.BoundCardsMatchKinds or
             SkillProgramConditionKind.HasOwnedCardCategory) && cardKinds.Count > 0)
            Fail(path, "cardKinds are required only for boundCardsMatchKinds and must be distinct and nonempty");
        if (cardKinds.Distinct().Count() != cardKinds.Count)
            Fail(path, "cardKinds are required only for boundCardsMatchKinds and must be distinct and nonempty");
        if ((kind == SkillProgramConditionKind.BoundCardsMatchSuits) != (suits.Count > 0) ||
            suits.Distinct().Count() != suits.Count)
            Fail(path, "suits are required only for boundCardsMatchSuits and must be distinct and nonempty");
        if ((kind is SkillProgramConditionKind.BooleanState or SkillProgramConditionKind.RuntimeBooleanState or SkillProgramConditionKind.PublicCounterAtLeast or SkillProgramConditionKind.PublicCounterOdd) != (stateId is not null))
            Fail(path, kind == SkillProgramConditionKind.BooleanState
                ? "booleanState requires stateId" : "stateId is accepted only by booleanState");
        if (kind is not (SkillProgramConditionKind.BooleanState or SkillProgramConditionKind.BoundCardSuitMatchesChoice or SkillProgramConditionKind.RuntimeBooleanState or SkillProgramConditionKind.PublicCounterOdd) &&
            node.TryGetProperty("expectedValue", out _))
            Fail(path + ".expectedValue", "is accepted only by booleanState or boundCardSuitMatchesChoice");
        if ((kind == SkillProgramConditionKind.BoundCardSuitMatchesChoice) != (choiceBind is not null))
            Fail(path + ".choiceBind", "choiceBind is required only for boundCardSuitMatchesChoice");
        return new SkillProgramCondition(kind, value, new ReadOnlyCollection<SkillProgramCondition>(children),
             sourceBind, stateId, expectedValue, optionId, cardCategories, cardKinds, suits, zones, choiceBind,
             participantGenderIs ? EnumValue<GeneralGender>(node, "gender", path) : null);
    }

    private static bool ContainsCardUseColorCondition(SkillProgramCondition condition) =>
        condition.Kind == SkillProgramConditionKind.CardUseIsRed ||
        condition.Children.Any(ContainsCardUseColorCondition);

    private static IReadOnlyList<T> ReadArray<T>(JsonElement owner, string name, string path,
        Func<JsonElement, string, T> parser, bool optional = false)
    {
        if (optional && !owner.TryGetProperty(name, out _)) return Array.Empty<T>();
        var array = Required(owner, name, JsonValueKind.Array, path);
        CheckCount(array.GetArrayLength(), path + "." + name);
        var result = new List<T>();
        var index = 0;
        foreach (var node in array.EnumerateArray()) result.Add(parser(node, $"{path}.{name}[{index++}]"));
        return new ReadOnlyCollection<T>(result);
    }

    private static IReadOnlyList<T> EnumArray<T>(JsonElement owner, string name, string path) where T : struct, Enum
    {
        var values = ReadArray(owner, name, path, (node, itemPath) => ParseEnum<T>(node, itemPath));
        if (values.Distinct().Count() != values.Count) Fail(path + "." + name, "contains duplicate values");
        return values;
    }

    private static IReadOnlyList<string> StringArray(JsonElement owner, string name, string path)
    {
        var values = ReadArray(owner, name, path, (node, itemPath) =>
        {
            if (node.ValueKind != JsonValueKind.String) Fail(itemPath, "must be a string");
            var value = node.GetString()!;
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
                Fail(itemPath, "must contain 1 to 128 characters");
            return value;
        });
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Count)
            Fail(path + "." + name, "contains duplicate values");
        return values;
    }

    private static T EnumValue<T>(JsonElement owner, string name, string path) where T : struct, Enum =>
        ParseEnum<T>(Required(owner, name, JsonValueKind.String, path), path + "." + name);

    private static T ParseEnum<T>(JsonElement node, string path) where T : struct, Enum
    {
        if (node.ValueKind != JsonValueKind.String) Fail(path, "must be a camelCase string");
        var text = node.GetString()!;
        foreach (var value in Enum.GetValues<T>())
        {
            var name = Enum.GetName(value)!;
            var camel = char.ToLowerInvariant(name[0]) + name[1..];
            if (text == camel) return value;
        }
        Fail(path, $"unsupported {typeof(T).Name} value '{text}'");
        return default;
    }

    private static string Canonicalize(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, element);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                { writer.WritePropertyName(property.Name); WriteCanonical(writer, property.Value); }
                writer.WriteEndObject(); break;
            case JsonValueKind.Array:
                writer.WriteStartArray(); foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item); writer.WriteEndArray(); break;
            case JsonValueKind.String: writer.WriteStringValue(element.GetString()); break;
            case JsonValueKind.Number: writer.WriteRawValue(element.GetRawText(), skipInputValidation: false); break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
            default: throw new InvalidOperationException("Unsupported JSON token in canonical skill program.");
        }
    }

    private static void RejectDuplicateProperties(JsonElement node, string path)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in node.EnumerateObject())
            {
                if (!names.Add(property.Name)) Fail(path, $"duplicate property '{property.Name}'");
                RejectDuplicateProperties(property.Value, path + "." + property.Name);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in node.EnumerateArray()) RejectDuplicateProperties(item, $"{path}[{index++}]");
        }
    }

    private static void CheckProperties(JsonElement node, string path, params string[] allowed)
    {
        var set = allowed.ToHashSet(StringComparer.Ordinal);
        foreach (var property in node.EnumerateObject())
            if (!set.Contains(property.Name)) Fail(path + "." + property.Name, "unsupported property");
    }

    private static int RequireVersion(JsonElement root, string path, params int[] supported)
    {
        var version = RequiredInt(root, "schemaVersion", path);
        if (!supported.Contains(version))
            Fail(path + ".schemaVersion", $"unsupported schema version {version}; expected {string.Join(" or ", supported)}");
        return version;
    }

    private static JsonElement Required(JsonElement owner, string name, JsonValueKind kind, string path)
    {
        if (!owner.TryGetProperty(name, out var value)) { Fail(path, $"missing required property '{name}'"); return default; }
        if (value.ValueKind != kind) Fail(path + "." + name, $"must be {kind}");
        return value;
    }

    private static string Identifier(JsonElement owner, string name, string path)
    {
        var value = NonEmptyString(owner, name, path);
        if (value.Length > 128) Fail(path + "." + name, "must not exceed 128 characters");
        return value;
    }

    private static string NonEmptyString(JsonElement owner, string name, string path)
    {
        var value = Required(owner, name, JsonValueKind.String, path).GetString()!;
        if (string.IsNullOrWhiteSpace(value)) Fail(path + "." + name, "must not be empty");
        return value;
    }

    private static string NonEmptyStringValue(JsonElement node, string path)
    {
        if (node.ValueKind != JsonValueKind.String) Fail(path, "must be a string");
        var value = node.GetString()!;
        if (string.IsNullOrWhiteSpace(value)) Fail(path, "must not be empty");
        if (value.Length > 512) Fail(path, "must not exceed 512 characters");
        return value;
    }

    private static int RequiredInt(JsonElement owner, string name, string path) => GetInt(Required(owner, name, JsonValueKind.Number, path), path + "." + name);
    private static int PositiveInt(JsonElement owner, string name, string path) { var value = RequiredInt(owner, name, path); if (value <= 0) Fail(path + "." + name, "must be positive"); return value; }
    private static int NonNegativeInt(JsonElement owner, string name, string path) { var value = RequiredInt(owner, name, path); if (value < 0) Fail(path + "." + name, "must be non-negative"); return value; }
    private static int GetInt(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Number) Fail(path, "must be a 32-bit integer");
        if (!value.TryGetInt32(out var result)) Fail(path, "must be a 32-bit integer");
        return result;
    }
    private static bool RequiredBool(JsonElement owner, string name, string path)
    {
        if (!owner.TryGetProperty(name, out var value)) { Fail(path, $"missing required property '{name}'"); return false; }
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) Fail(path + "." + name, "must be a boolean");
        return value.GetBoolean();
    }
    private static void RequireObject(JsonElement node, string path) { if (node.ValueKind != JsonValueKind.Object) Fail(path, "must be an object"); }
    private static void CheckCount(int count, string path) { if (count > MaximumItems) Fail(path, $"contains more than {MaximumItems} items"); }
    private static void EnsureUniqueIds(IEnumerable<string> ids, string path) { var seen = new HashSet<string>(StringComparer.Ordinal); foreach (var id in ids) if (!seen.Add(id)) Fail(path, $"duplicate id '{id}'"); }
    private static IReadOnlyDictionary<string, T> ReadOnly<T>(Dictionary<string, T> source) => new ReadOnlyDictionary<string, T>(source);
    [DoesNotReturn]
    private static void Fail(string path, string message) => throw new InvalidOperationException($"Invalid skill program at {path}: {message}.");
}
