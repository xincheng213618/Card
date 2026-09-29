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
    CardUseDistanceLimit
}
public enum SkillRuleOperation { Add, Set, Unlimited }
public enum SkillRuleValueExpression { LivingFactionCount, OwnedZoneCount, OwnerLostHp = 2, NegatedOwnedZoneCount = 3 }
public enum SkillProgramConditionKind { Always, OwnTurn, NotOwnTurn, Wounded, HpAtLeast, HandCountAtLeast, CardUseIsRed, SelectedTargetIsOther, SelectedTargetHandGreaterThanOwner, PindianWon, PindianNotWon, BooleanState, All, Any, Not, FaceDown, Chained, ChoiceIs, BoundCardsSameColor, BoundCardsMatchCategories, BoundCardsMatchKinds = 20, HasClaimableDamageCards = 21, AttackRangeCoverageDecreased = 22, HasOwnedCardCategory = 23, BoundCardCountAtLeast = 24, ActivationCardCountAtLeast = 25, ClassicIdentityMode = 26, BoundCardSuitMatchesChoice = 27, BoundCardsMatchSuits = 28, BoundCardCategoryMatchesCardAction = 29, EventTargetGenderIs = 30, EventSourceGenderIs = 31 }
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
    DamageSourceFactionIs = 26
}
public enum SkillProgramTriggerValueKind
{
    IntegerConstant,
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
    TurnOwnerDiscardPhaseHandDiscardCount = 26
}
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
    OtherLiving,
    OtherLivingWithHand,
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
    LivingPairDistinct = 27
}
public enum SkillProgramCardCategory { Basic, Trick, Equipment }
public enum SkillProgramTurnOwnerScope { Own = 0, OtherLiving = 1 }
public enum SkillProgramDamageModifierExpiration { CurrentTurnEnd = 0, NextOwnerTurnStart = 1 }
public enum SkillProgramDamageModifierSourceScope { OwnerUsed = 0, DamageSource = 1, DamageParticipant = 2 }
public enum SkillProgramDamageModifierCondition
{
    Always = 0,
    SourceOutsideTargetAttackRange = 1,
    SourceNotFewerHandAndEquipmentThanTarget = 2,
    OwnerUniqueMaximumHand = 3
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
    Draw,
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
    TakeRandomCardFromEveryOtherCharacter
}
public enum SkillProgramEffectTarget { Owner, Actor, SelectedTarget, SelectedTargets }
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
    AfterHpLost,
    AfterHpRecovered,
    CharacterDied
}
public enum SkillProgramTriggerSubject { Owner, Any, Source, DamageSource, DamageTarget }
public enum SkillProgramMovementOccurrence { PerBatch, PerCard }
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
    HandLimitMinusHandCount = 15
}
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
public enum SkillProgramCardTargetRestriction { SelfOnly, DistanceUnlimitedAgainstTarget, SlashCountUnlimitedAgainstTarget, IgnoreArmorAgainstTarget }
public enum SkillProgramCardColorRelation { OppositeBoundCard }
public enum SkillProgramStateVisibility { Public, Private }
public enum SkillProgramStateResetScope { Game }
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
        Func<IReadOnlyList<CardZoneKind>, IReadOnlyList<SkillProgramCardCategory>, bool>? hasOwnedCardCategory = null,
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
                hasOwnedCardCategory?.Invoke(Zones, CardCategories) == true,
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
            SkillProgramConditionKind.ClassicIdentityMode => true,
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
            SkillProgramConditionKind.HpAtLeast => context.Hp >= Value,
            SkillProgramConditionKind.HandCountAtLeast => context.HandCount >= Value,
            SkillProgramConditionKind.CardUseIsRed => cardUseIsRed == true,
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
                SkillProgramConditionKind.BoundCardCategoryMatchesCardAction =>
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
        bool? eventSourceIsFemale = null) => Kind switch
        {
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
            SkillProgramConditionKind.SelectedTargetIsOther =>
                selectedTarget is not null && selectedTarget.Seat != context.Seat,
            SkillProgramConditionKind.SelectedTargetHandGreaterThanOwner =>
                selectedTarget is not null && selectedTarget.HandCount > context.HandCount,
            SkillProgramConditionKind.All => Children.All(child => child.Evaluate(context, selectedTarget, pindianWon, booleanState, cardUseIsRed, choiceResult, boundCardsSameColor, boundCardsMatchCategories, boundCardsMatchKinds, attackRangeCoverageDecreased, boundCardCount, activationCardCount, boundCardSuitMatchesChoice, boundCardsMatchSuits, boundCardCategoryMatchesCardAction, eventTargetIsFemale, eventSourceIsFemale)),
            SkillProgramConditionKind.Any => Children.Any(child => child.Evaluate(context, selectedTarget, pindianWon, booleanState, cardUseIsRed, choiceResult, boundCardsSameColor, boundCardsMatchCategories, boundCardsMatchKinds, attackRangeCoverageDecreased, boundCardCount, activationCardCount, boundCardSuitMatchesChoice, boundCardsMatchSuits, boundCardCategoryMatchesCardAction, eventTargetIsFemale, eventSourceIsFemale)),
            SkillProgramConditionKind.Not => !Children[0].Evaluate(context, selectedTarget, pindianWon, booleanState, cardUseIsRed, choiceResult, boundCardsSameColor, boundCardsMatchCategories, boundCardsMatchKinds, attackRangeCoverageDecreased, boundCardCount, activationCardCount, boundCardSuitMatchesChoice, boundCardsMatchSuits, boundCardCategoryMatchesCardAction, eventTargetIsFemale, eventSourceIsFemale),
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
    int TurnOwnerDiscardPhaseHandDiscardCount = 0)
{
    public bool GetBooleanState(string skillId, string skillInstanceId, string stateId) =>
        BooleanStates?.GetValueOrDefault(BooleanStateKey(skillId, skillInstanceId, stateId)) ??
        throw new InvalidOperationException("The frozen trigger facts do not contain the requested boolean state.");

    public static string BooleanStateKey(string skillId, string skillInstanceId, string stateId) =>
        $"{skillId}\u001f{skillInstanceId}\u001f{stateId}";
}

public readonly record struct SkillProgramOwnedZoneCounts(
    int WoodenOxGrain, int BuquWound, int Authority, int Chunlao)
{
    public int Get(CardZoneKind zone) => zone switch
    {
        CardZoneKind.WoodenOxGrain => WoodenOxGrain,
        CardZoneKind.BuquWound => BuquWound,
        CardZoneKind.Authority => Authority,
        CardZoneKind.Chunlao => Chunlao,
        _ => 0
    };
}

public sealed record SkillProgramTriggerValue(SkillProgramTriggerValueKind Kind, int Value, CardZoneKind? Zone = null,
    PlayerMarkerKind? Marker = null)
{
    public int Resolve(SkillProgramTriggerFacts facts) => Kind switch
    {
        SkillProgramTriggerValueKind.IntegerConstant => Value,
        SkillProgramTriggerValueKind.CardsUsedThisTurn => facts.CardsUsedThisTurn,
        SkillProgramTriggerValueKind.CardsUsedOrRespondedThisTurn => facts.CardsUsedOrRespondedThisTurn,
        SkillProgramTriggerValueKind.CurrentHp => facts.CurrentHp,
        SkillProgramTriggerValueKind.LivingPlayersMinHp => facts.LivingPlayersMinHp,
        SkillProgramTriggerValueKind.TurnOwnerDiscardPhaseHandDiscardCount =>
            facts.TurnOwnerDiscardPhaseHandDiscardCount,
        SkillProgramTriggerValueKind.CurrentMaxHp => facts.CurrentMaxHp,
        SkillProgramTriggerValueKind.CurrentHandCount => facts.CurrentHandCount,
        SkillProgramTriggerValueKind.CurrentOwnedZoneCount => facts.OwnedZoneCounts.Get(Zone!.Value),
        SkillProgramTriggerValueKind.DestinationZoneCountBefore => facts.DestinationZoneCountBefore,
        SkillProgramTriggerValueKind.DestinationZoneCountAfter => facts.DestinationZoneCountAfter,
        SkillProgramTriggerValueKind.HpChangeAmount => facts.HpChangeAmount,
        SkillProgramTriggerValueKind.HpBeforeChange => facts.HpBeforeChange,
        SkillProgramTriggerValueKind.HpAfterChange => facts.HpAfterChange,
        SkillProgramTriggerValueKind.MovedCardCount => facts.MovedCardCount,
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
        SkillProgramTriggerValueKind.OwnerEventTargetDistance => facts.OwnerEventTargetDistance,
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
        IReadOnlyList<string>? factions = null) =>
        (Kind, Children, Left, Comparison, Right, StateId, ExpectedValue, GeneralIds, ConversionSkillId,
            CardCategories, Factions) =
        (kind, children, left, comparison, right, stateId, expectedValue,
            generalIds ?? Array.Empty<string>(), conversionSkillId,
            cardCategories ?? Array.Empty<SkillProgramCardCategory>(),
            factions ?? Array.Empty<string>());

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

    public bool Evaluate(SkillProgramTriggerFacts facts, string? skillId = null, string? skillInstanceId = null) => Kind switch
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
        SkillProgramTriggerConditionKind.DamageCardIsSlash => facts.DamageCardIsSlash == true,
        SkillProgramTriggerConditionKind.DeathKillerIsOwner => facts.DeathKillerIsOwner == true,
        SkillProgramTriggerConditionKind.DeathVictimHasCards => facts.DeathVictimCleanupCardCount > 0,
        SkillProgramTriggerConditionKind.OwnerIsTurnPlayer => facts.OwnerIsTurnPlayer == true,
        SkillProgramTriggerConditionKind.CardActionFromOwnerHand => facts.CardActionFromOwnerHand == true,
        SkillProgramTriggerConditionKind.DamageSourceIsOwner => facts.DamageSourceIsOwner == true,
        SkillProgramTriggerConditionKind.DamageSourceFactionIs => facts.DamageSourceFactionId is { } faction &&
            Factions.Contains(faction, StringComparer.Ordinal),
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
    public int Priority { get; }
    public string? SourceCardIdentityId { get; }
    public IReadOnlyList<CardKind> CardKinds { get; }
    public SkillProgramCondition Condition { get; }

    public int EvaluateValue(SkillProgramRuleContext context) => ValueExpression switch
    {
        null => Value,
        SkillRuleValueExpression.LivingFactionCount when context.LivingFactionCount >= 0 => context.LivingFactionCount,
        SkillRuleValueExpression.LivingFactionCount => throw new ArgumentOutOfRangeException(
            nameof(context), context.LivingFactionCount, "Living faction count cannot be negative."),
        SkillRuleValueExpression.OwnerLostHp => Math.Max(0, context.Owner.MaxHp - context.Owner.Hp),
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

public sealed class SkillProgramViewAs
{
    internal SkillProgramViewAs(string id, IReadOnlyList<CardKind> inputKinds, IReadOnlyList<Suit> inputSuits,
        CardKind outputKind, bool forPlay, bool forResponse, SkillProgramCondition condition,
        int inputCount = 1, IReadOnlyList<CardZoneKind>? sourceZones = null,
        bool allowChainedInput = false,
        IReadOnlyList<SkillProgramCardCategory>? inputCategories = null,
        bool sameSuit = false) =>
        (Id, InputKinds, InputSuits, OutputKind, ForPlay, ForResponse, Condition, InputCount, SourceZones,
            AllowChainedInput, InputCategories, SameSuit) =
        (id, inputKinds, inputSuits, outputKind, forPlay, forResponse, condition, inputCount,
            sourceZones ?? [CardZoneKind.Hand], allowChainedInput, inputCategories ?? [], sameSuit);
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
        bool allowDecline = false) =>
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
            OnePerSuit, AllowDecline) =
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
            prohibitReplacingEquipment, onePerSuit, allowDecline);
    public SkillProgramEffectOp Op { get; }
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

public enum SkillProgramRevealMode { Random, Chooser }

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
    public bool SelectedCardsSameSuit { get; }
    public IReadOnlyList<EquipmentSlot> EquipmentSlots { get; }
    public string UsageGroup { get; }

    /// <summary>
    /// Targets must leave the activation's selectable hand equipment at least one
    /// free matching slot (Zhijian: equipment gifts cannot replace an equipped card).
    /// </summary>
    public bool TargetRequiresEmptyEquipmentSlot { get; }
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
    public string? ChoiceGroup { get; }
    public SkillProgramCardActionOwnerRelation? OwnerRelation { get; }
    public SkillProgramTurnOwnerScope TurnOwnerScope { get; }
    public string? ChoiceLabel { get; internal set; }

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
        IReadOnlyList<SkillProgramCardPolicy>? cardPolicies = null) =>
        (Id, Revision, GameplayHash, RuntimeVersion, MinimumRulesVersion, Modifiers, ViewAs, Activations, Triggers,
            Contributions, CardIdentities, BooleanStates, DamageModifiers, CardPolicies) =
        (id, revision, gameplayHash, runtimeVersion, minimumRulesVersion, modifiers, viewAs, activations, triggers,
            contributions, cardIdentities, booleanStates ?? [], damageModifiers ?? [], cardPolicies ?? []);
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
                "cardIdentities", "states", "cardPolicies");
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
                cardPolicies.Count == 0)
                Fail(skillPath, "must define at least one modifier, viewAs rule, activation, trigger, contribution, or card identity");
            EnsureUniqueIds(modifiers.Select(item => item.Id), skillPath + ".modifiers");
            EnsureUniqueIds(damageModifiers.Select(item => item.Id), skillPath + ".damageModifiers");
            EnsureUniqueIds(cardPolicies.Select(item => item.Id), skillPath + ".cardPolicies");
            EnsureUniqueIds(viewAs.Select(item => item.Id), skillPath + ".viewAs");
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
            ValidateTriggerChoiceGroups(skillPath, triggers);
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
                    if (activation.MinCards != conversion.InputCount ||
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
                if (effect.Op is SkillProgramEffectOp.SetBooleanState or SkillProgramEffectOp.ToggleBooleanState &&
                    !declaredStateIds.Contains(effect.StateId!))
                    Fail(skillPath, $"effect references undeclared state '{effect.StateId}'");
            foreach (var effect in triggers.SelectMany(item => item.Effects))
                if (effect.Op is SkillProgramEffectOp.SetBooleanState or SkillProgramEffectOp.ToggleBooleanState &&
                    !declaredStateIds.Contains(effect.StateId!))
                    Fail(skillPath, $"trigger effect references undeclared state '{effect.StateId}'");
            EnsureUniqueIds(activations.Select(item => item.Id).Concat(contributions.Select(item => item.Id)),
                skillPath + ".playBindings");
            ValidateCardIdentityModifiers(skillPath, modifiers, cardIdentities);
            var hashInput = runtimeVersion + "\n" + Canonicalize(skill);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput))).ToLowerInvariant();
            result.Add(id, new SkillProgram(id, revision, hash, runtimeVersion, skillMinimumRulesVersion,
                modifiers, viewAs, activations, triggers, contributions, cardIdentities,
                booleanStates: booleanStates,
                damageModifiers: damageModifiers,
                cardPolicies: cardPolicies));
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
            CheckProperties(property.Value, path, "name", "description", "triggerChoices", "booleanStates", "optionLabels");
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
            result.Add(id, new SkillPresentation(NonEmptyString(property.Value, "name", path),
                NonEmptyString(property.Value, "description", path),
                new ReadOnlyDictionary<string, string>(triggerChoices),
                new ReadOnlyDictionary<string, ProgramBooleanStatePresentation>(booleanStates)));
        }
        foreach (var id in programs.Keys)
            if (!result.ContainsKey(id)) Fail("presentation.skills", $"missing presentation for skill '{id}'");
        return result;
    }

    private static SkillProgramCardPolicy ParseCardPolicy(JsonElement node, string path)
    {
        RequireObject(node, path);
        CheckProperties(node, path, "id", "kind", "cardKinds", "requiredCardKinds", "value",
            "inputSuit", "outputSuit", "condition", "factionId", "ownerRole");
        var id = Identifier(node, "id", path);
        var kind = EnumValue<SkillProgramCardPolicyKind>(node, "kind", path);
        var factionId = node.TryGetProperty("factionId", out _)
            ? Identifier(node, "factionId", path) : null;
        Role? ownerRole = node.TryGetProperty("ownerRole", out _)
            ? EnumValue<Role>(node, "ownerRole", path) : null;
        if (kind is SkillProgramCardPolicyKind.FactionResponseRequest or
            SkillProgramCardPolicyKind.RescueRecoveryBonus or
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
        if (kind == SkillProgramCardPolicyKind.RewriteSuit)
        {
            if (inputSuit is null || outputSuit is null || inputSuit == outputSuit ||
                cardKinds.Count != 0 || requiredKinds.Count != 0 || value != 0)
                Fail(path, "rewriteSuit requires two distinct suits and no card or value filters");
        }
        else if (kind == SkillProgramCardPolicyKind.ProhibitTargetSlashResponseBySuit)
        {
            if (inputSuit is null || outputSuit is not null || value != 0 || requiredKinds.Count != 0 ||
                cardKinds.Count == 0 || cardKinds.Any(card => card is not (
                    CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)))
                Fail(path, "suit response prohibition requires Slash kinds and one input suit");
        }
        else if (inputSuit is not null || outputSuit is not null)
            Fail(path, "suit fields require rewriteSuit or a suit response prohibition");
        if (kind is SkillProgramCardPolicyKind.MinimumResponseCount or
            SkillProgramCardPolicyKind.MinimumResponseCountAsTarget)
        {
            if (cardKinds.Count == 0 || requiredKinds.Count == 0 || value is < 2 or > 20)
                Fail(path, "minimumResponseCount requires incoming and response card kinds and value 2..20");
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
        else if (requiredKinds.Count != 0 || value != 0)
            Fail(path, "requiredCardKinds and value require a minimum response count policy");
        if (kind is SkillProgramCardPolicyKind.OfferSkipDiscard or
            SkillProgramCardPolicyKind.RewriteSuit or
            SkillProgramCardPolicyKind.FactionHandLimitBonus)
        {
            if (cardKinds.Count != 0) Fail(path + ".cardKinds", "this policy does not accept card kinds");
        }
        else if (cardKinds.Count == 0 && kind is not (
                     SkillProgramCardPolicyKind.FactionResponseRequest or
                     SkillProgramCardPolicyKind.ProhibitNearbyTargetResponse))
            Fail(path + ".cardKinds", "this policy requires effective card kinds");
        if (kind == SkillProgramCardPolicyKind.VirtualEquipment &&
            cardKinds.Any(card => !EquipmentCatalog.IsEquipment(card)))
            Fail(path, "virtualEquipment requires equipment card kinds");
        return new SkillProgramCardPolicy(id, kind, cardKinds, requiredKinds, value,
            inputSuit, outputSuit, OptionalCondition(node, path), factionId, ownerRole);
    }

    private static SkillProgramDamageModifier ParseDamageModifier(JsonElement node, string path)
    {
        RequireObject(node, path);
        CheckProperties(node, path, "id", "cardKinds", "amount", "condition", "sourceScope");
        var kinds = EnumArray<CardKind>(node, "cardKinds", path);
        if (kinds.Distinct().Count() != kinds.Count ||
            (kinds.Count == 0 && !(
                EnumValue<SkillProgramDamageModifierCondition>(node, "condition", path) ==
                SkillProgramDamageModifierCondition.OwnerUniqueMaximumHand)))
            Fail(path + ".cardKinds", "must contain distinct effective card kinds");
        var amount = PositiveInt(node, "amount", path);
        if (amount > 20) Fail(path + ".amount", "must not exceed 20");
        var condition = EnumValue<SkillProgramDamageModifierCondition>(node, "condition", path);
        var scope = node.TryGetProperty("sourceScope", out _)
            ? EnumValue<SkillProgramDamageModifierSourceScope>(node, "sourceScope", path)
            : SkillProgramDamageModifierSourceScope.OwnerUsed;
        if (condition == SkillProgramDamageModifierCondition.OwnerUniqueMaximumHand)
        {
            if (scope != SkillProgramDamageModifierSourceScope.DamageParticipant || kinds.Count != 0)
                Fail(path, "unique-maximum-hand damage requires damageParticipant and every damage kind");
        }
        else if (scope != SkillProgramDamageModifierSourceScope.OwnerUsed)
            Fail(path + ".sourceScope", "card damage modifiers require ownerUsed");
        return new SkillProgramDamageModifier(Identifier(node, "id", path), kinds, amount, condition, scope);
    }

    private static SkillProgramModifier ParseModifier(JsonElement node, string path)
    {
        RequireObject(node, path);
        CheckProperties(node, path, "id", "query", "operation", "value", "valueExpression", "valueZone",
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
        if (valueExpression is SkillRuleValueExpression.OwnedZoneCount or SkillRuleValueExpression.NegatedOwnedZoneCount)
        {
            if (valueZone is not (CardZoneKind.WoodenOxGrain or CardZoneKind.BuquWound or
                    CardZoneKind.Authority or CardZoneKind.Chunlao))
                Fail(path + ".valueZone", "ownedZoneCount requires a persistent owner zone");
        }
        else if (valueZone is not null)
            Fail(path + ".valueZone", "is accepted only by ownedZoneCount");
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
        if (valueExpression == SkillRuleValueExpression.NegatedOwnedZoneCount &&
            (operation != SkillRuleOperation.Add || query != SkillRuleQuery.OutgoingDistance))
            Fail(path + ".valueExpression",
                "negatedOwnedZoneCount is currently supported only by additive outgoingDistance modifiers");
        if (valueExpression is not null && valueExpression != SkillRuleValueExpression.NegatedOwnedZoneCount &&
            (operation != SkillRuleOperation.Add || query != SkillRuleQuery.HandLimit))
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
        else if (sourceCardIdentityId is not null)
            Fail(path + ".sourceCardIdentityId", "is supported only for slashDistanceLimit");
        if (query == SkillRuleQuery.CardUseDistanceLimit)
        {
            if (operation != SkillRuleOperation.Add || valueExpression is not null || cardKinds.Count == 0)
                Fail(path, "cardUseDistanceLimit requires fixed additive value and effective card kinds");
        }
        else if (query == SkillRuleQuery.CardTargetCount)
        {
            if (operation != SkillRuleOperation.Add || value <= 0 || valueExpression is not null)
                Fail(path, "cardTargetCount requires a positive fixed additive modifier");
            if (cardKinds.Count == 0)
                Fail(path + ".cardKinds", "cardTargetCount requires at least one effective card kind");
        }
        else if (cardKinds.Count != 0)
            Fail(path + ".cardKinds", "is supported only for cardTargetCount or cardUseDistanceLimit");
        return new SkillProgramModifier(id, query, operation, value, valueExpression, valueZone, priority,
            sourceCardIdentityId, cardKinds, OptionalCondition(node, path));
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
        if (output is not (CardKind.Slash or CardKind.Dodge))
            Fail(path + ".outputKind", "card identities support only slash or dodge");
        return new SkillProgramCardIdentity(id, zones, inputs, suits, output,
            OptionalCondition(node, path));
    }

    private static void ValidateCardIdentityModifiers(
        string path,
        IReadOnlyList<SkillProgramModifier> modifiers,
        IReadOnlyList<SkillProgramCardIdentity> identities)
    {
        foreach (var modifier in modifiers.Where(item => item.Query == SkillRuleQuery.SlashDistanceLimit))
        {
            var identity = identities.SingleOrDefault(item => item.Id == modifier.SourceCardIdentityId);
            if (identity is null)
                Fail(path + ".modifiers", $"references unknown card identity '{modifier.SourceCardIdentityId}'");
            if (identity.OutputKind != CardKind.Slash)
                Fail(path + ".modifiers", "slashDistanceLimit requires a card identity that outputs slash");
        }
    }

    private static SkillProgramViewAs ParseViewAs(JsonElement node, string path)
    {
        RequireObject(node, path);
        CheckProperties(node, path, "id", "inputKinds", "inputSuits", "inputCategories", "inputCount", "sourceZones",
            "outputKind", "forPlay", "forResponse", "allowChainedInput", "sameSuit", "condition");
        var id = Identifier(node, "id", path);
        var inputs = EnumArray<CardKind>(node, "inputKinds", path);
        var inputCategories = node.TryGetProperty("inputCategories", out _)
            ? EnumArray<SkillProgramCardCategory>(node, "inputCategories", path)
            : [];
        var suits = EnumArray<Suit>(node, "inputSuits", path);
        var inputCount = node.TryGetProperty("inputCount", out _)
            ? PositiveInt(node, "inputCount", path)
            : 1;
        if (inputCount > 64) Fail(path + ".inputCount", "must not exceed 64");
        var sourceZones = node.TryGetProperty("sourceZones", out _)
            ? EnumArray<CardZoneKind>(node, "sourceZones", path)
            : [CardZoneKind.Hand];
        if (sourceZones.Count == 0 || sourceZones.Any(zone =>
                zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Authority)))
            Fail(path + ".sourceZones", "viewAs accepts hand, equipment or authority sources only");
        if (inputCount > 1 && !sourceZones.SequenceEqual([CardZoneKind.Hand]))
            Fail(path + ".sourceZones", "multi-card viewAs currently accepts hand cards only");
        var output = EnumValue<CardKind>(node, "outputKind", path);
        if (output is not (CardKind.Slash or CardKind.Dodge or CardKind.FireSlash or
                CardKind.Dismantlement or CardKind.SupplyShortage or CardKind.Indulgence or
                CardKind.IronChain or CardKind.FireAttack or CardKind.Nullification or CardKind.Peach or
                CardKind.ArrowBarrage or CardKind.Alcohol or CardKind.Snatch))
            Fail(path + ".outputKind", "this card kind has no configured viewAs use or response path");
        var forPlay = RequiredBool(node, "forPlay", path);
        var forResponse = RequiredBool(node, "forResponse", path);
        var allowChainedInput = node.TryGetProperty("allowChainedInput", out _) &&
                                RequiredBool(node, "allowChainedInput", path);
        var sameSuit = node.TryGetProperty("sameSuit", out _) && RequiredBool(node, "sameSuit", path);
        if (!forPlay && !forResponse) Fail(path, "at least one of forPlay or forResponse must be true");
        if (output == CardKind.Snatch && forResponse)
            Fail(path + ".forResponse", "snatch viewAs is play-only");
        if (output == CardKind.Snatch && inputCount > 1)
            Fail(path + ".inputCount", "snatch viewAs accepts one physical input only");
        if (output == CardKind.Dodge && forPlay)
            Fail(path + ".forPlay", "dodge is response-only and cannot be played proactively");
        if (inputCount > 1 && output is not (CardKind.Slash or CardKind.Dodge or CardKind.ArrowBarrage))
            Fail(path + ".inputCount", "multi-card viewAs has no use or response executor for this output kind");
        if (sameSuit && inputCount < 2)
            Fail(path + ".sameSuit", "sameSuit requires multiple physical inputs");
        if (output == CardKind.ArrowBarrage &&
            (!sameSuit || inputCount != 2 || !forPlay || forResponse))
            Fail(path, "arrowBarrage viewAs requires two same-suit hand cards for play only");
        if (inputCount > 1 && inputCategories.Count != 0)
            Fail(path + ".inputCategories", "multi-card viewAs does not support category input filters");
        if (output is CardKind.Dismantlement or CardKind.SupplyShortage or CardKind.Indulgence or
                CardKind.IronChain or CardKind.FireAttack && forResponse)
            Fail(path + ".forResponse", "this trick cannot be used as a response");
        if (output == CardKind.Nullification && forPlay)
            Fail(path + ".forPlay", "nullification is only legal in the counterspell response window");
        if (output == CardKind.Peach && forPlay)
            Fail(path + ".forPlay", "proactive peach has no configured viewAs executor");
        if (output == CardKind.FireSlash)
        {
            if (inputCount == 1 && inputs.Count == 1 && suits.Count == 0)
            {
                if (inputs[0] != CardKind.Slash || forResponse)
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
        if (inputs.Count > 0 && inputs.All(kind => kind == output))
            Fail(path + ".inputKinds", "viewAs must change at least one accepted input kind");
        return new SkillProgramViewAs(id, inputs, suits, output, forPlay, forResponse,
            OptionalCondition(node, path), inputCount, sourceZones, allowChainedInput, inputCategories, sameSuit);
    }

    private static SkillProgramActivation ParseActivation(JsonElement node, string path)
    {
        RequireObject(node, path);
        CheckProperties(node, path, "id", "minCards", "maxCards", "sourceZones", "minTargets", "maxTargets",
            "targetKind", "usesPerTurn", "usesPerPhase", "usesPerGame", "condition", "effects",
            "selectedCardsSameSuit", "equipmentSlots", "usageGroup", "targetRequiresEmptyEquipmentSlot");
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
        if (effects.Count == 0) Fail(path + ".effects", "must contain at least one effect");
        if (minCards == 0 &&
            effects.Any(effect => effect.Op == SkillProgramEffectOp.CaptureSelectedCards))
            Fail(path + ".minCards", "captureSelectedCards requires at least one initial card");
        {
            ProgramCompositionValidator.Validate(path, effects, minTargets == 1 && maxTargets == 1,
                maxCards, initialTargetSetCount: maxTargets > 1 ? minTargets : 0,
                initialTargetSetMaximum: maxTargets > 1 ? maxTargets : 0);
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
        return new SkillProgramActivation(id, minCards, maxCards, minTargets, maxTargets, targetKind, uses,
            OptionalCondition(node, path), effects, sourceZones, usesPerPhase, usesPerGame,
            selectedCardsSameSuit, equipmentSlots,
            node.TryGetProperty("usageGroup", out _) ? Identifier(node, "usageGroup", path) : null,
            targetRequiresEmptyEquipmentSlot);
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
                allowBoundCardCount: true, allowOwnedCardCategory: true),
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
            effect.Op is not (SkillProgramEffectOp.Draw or SkillProgramEffectOp.Recover))
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
            "hpChangeOccurrence");
        var id = Identifier(node, "id", path);
        var window = EnumValue<SkillProgramTriggerWindow>(node, "window", path);
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
        var isMovementWindow = window is SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained;
        var isHpWindow = window is SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHpRecovered;
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
            if (window is not (SkillProgramTriggerWindow.TurnEnding or
                SkillProgramTriggerWindow.PlayEnding or SkillProgramTriggerWindow.PlayPhaseStarting or
                SkillProgramTriggerWindow.DiscardPhaseEnded))
                Fail(path + ".turnOwnerScope", "requires a turnEnding, playEnding, playPhaseStarting or discardPhaseEnded trigger");
            turnOwnerScope = EnumValue<SkillProgramTurnOwnerScope>(node, "turnOwnerScope", path);
        }
        var isCardActionWindow = window is (
            SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardUseBeforeTargetEffects or
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
            if (!isCardActionWindow || window == SkillProgramTriggerWindow.CardResponseAccepted)
                Fail(path + ".cardCategories", "requires a card-use window");
            cardCategories = EnumArray<SkillProgramCardCategory>(node, "cardCategories", path);
            if (cardCategories.Count == 0 || cardCategories.Distinct().Count() != cardCategories.Count)
                Fail(path + ".cardCategories", "must contain distinct card categories");
        }
        var isLifecycleWindow = isMovementWindow || isHpWindow || window is SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or
            SkillProgramTriggerWindow.DrawPhaseStarting or
            SkillProgramTriggerWindow.AfterNormalDraw or
            SkillProgramTriggerWindow.SelfDyingResponse or
            SkillProgramTriggerWindow.DyingResponse or
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
            SkillProgramTriggerWindow.PlayPhaseStarting;
        var supportsTriggerCondition = isCardActionWindow || isMovementWindow || isHpWindow || window is SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or
            SkillProgramTriggerWindow.DrawPhaseStarting or
            SkillProgramTriggerWindow.AfterNormalDraw or
            SkillProgramTriggerWindow.DyingResponse or
            SkillProgramTriggerWindow.BeforeDamageApplied or
            SkillProgramTriggerWindow.PlayEnding or
            SkillProgramTriggerWindow.DiscardPhaseStarting or
            SkillProgramTriggerWindow.DiscardPhaseEnded or
            SkillProgramTriggerWindow.TurnEnding or
            SkillProgramTriggerWindow.CardsMoved or
            SkillProgramTriggerWindow.OwnerDied or
            SkillProgramTriggerWindow.CharacterDied or
            SkillProgramTriggerWindow.PlayPhaseStarting ||
            window == SkillProgramTriggerWindow.AfterDamageApplied;
        if (window != SkillProgramTriggerWindow.CardsMoved && node.TryGetProperty("sourceZones", out _))
            Fail(path, "sourceZones and movementOccurrence are supported only by cardsMoved");
        if (window is not (SkillProgramTriggerWindow.AfterDamageApplied or
                SkillProgramTriggerWindow.DamageAppliedBeforeDying) &&
            node.TryGetProperty("damageOccurrence", out _))
            Fail(path, "damageOccurrence requires a damage-applied trigger");
        if (window != SkillProgramTriggerWindow.DrawPhaseStarting && node.TryGetProperty("drawPhaseMode", out _))
            Fail(path, "drawPhaseMode is supported only by drawPhaseStarting");
        if (window is not (SkillProgramTriggerWindow.DrawPhaseStarting or
                SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or
                SkillProgramTriggerWindow.PlayPhaseStarting) &&
            node.TryGetProperty("choiceGroup", out _))
            Fail(path, "choiceGroup is supported only by drawPhaseStarting, turnStartBeforeNormalFlow or playPhaseStarting");
        if (node.TryGetProperty("ownerRelation", out _) &&
            window is not (SkillProgramTriggerWindow.CardUseCommitted or
                SkillProgramTriggerWindow.CardUseBeforeTargetEffects or
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
            if ((!supportsDamageSourceConversion &&
                 (node.TryGetProperty("sourceSkillId", out _) || node.TryGetProperty("sourceViewAsId", out _))) ||
                node.TryGetProperty("cardKinds", out _) || node.TryGetProperty("cardCategories", out _) || node.TryGetProperty("suits", out _) ||
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
                    !(window == SkillProgramTriggerWindow.AfterDamageApplied &&
                      subject is SkillProgramTriggerSubject.DamageSource or SkillProgramTriggerSubject.Any) &&
                    !(window == SkillProgramTriggerWindow.BeforeDamageApplied &&
                      subject == SkillProgramTriggerSubject.DamageTarget))
                    Fail(path + ".subject", "this lifecycle window supports only owner subjects");
                if (supportsDamageSourceConversion &&
                    (node.TryGetProperty("sourceSkillId", out _) || node.TryGetProperty("sourceViewAsId", out _)))
                    Fail(path, "owner after-damage triggers do not accept card-conversion source fields");
            }
            if (window == SkillProgramTriggerWindow.CardsGained)
            {
                destinationZones = EnumArray<CardZoneKind>(node, "destinationZones", path);
                if (!destinationZones.SequenceEqual([CardZoneKind.Hand]))
                    Fail(path + ".destinationZones", "cardsGained requires exactly the hand destination zone");
                movementOccurrence = EnumValue<SkillProgramMovementOccurrence>(node, "movementOccurrence", path);
            }
            if (window == SkillProgramTriggerWindow.CardsMoved)
            {
                sourceZones = EnumArray<CardZoneKind>(node, "sourceZones", path);
                if (sourceZones.Count != 1 || sourceZones.Any(zone => zone is not
                        (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment or
                         CardZoneKind.WoodenOxGrain or CardZoneKind.BuquWound or CardZoneKind.Authority or
                         CardZoneKind.Chunlao)))
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
                var supported = window is SkillProgramTriggerWindow.CardUseCompleted or SkillProgramTriggerWindow.CardUseCommitted
                    ? cardKinds.All(kind => kind is not (CardKind.Dodge or CardKind.Nullification))
                    : window == SkillProgramTriggerWindow.CardUseBeforeTargetEffects
                    ? cardKinds.All(kind => kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash ||
                        CardCatalog.Get(kind).CategoryName == "锦囊牌")
                    : window is SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardUseBeforeTargetEffects or SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.SlashTargetRedirecting or SkillProgramTriggerWindow.SlashBeforeResponse or SkillProgramTriggerWindow.SlashFullyDodged
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
            if (hasUsageScope != hasUsageLimit)
                Fail(path, "usageScope and usageLimit must be provided together");
            if (hasUsageScope)
            {
                usageScope = EnumValue<SkillUsageScope>(node, "usageScope", path);
                if (usageScope == SkillUsageScope.Event)
                    Fail(path + ".usageScope",
                        "event usage scope is not supported by lifecycle triggers");
                usageLimit = PositiveInt(node, "usageLimit", path);
                if (usageLimit > 1024) Fail(path + ".usageLimit", "must not exceed 1024");
            }
        }
        var optional = RequiredBool(node, "optional", path);
        if (window == SkillProgramTriggerWindow.DamageAppliedBeforeDying && optional)
            Fail(path + ".optional", "damageAppliedBeforeDying cannot request a choice");
        var condition = OptionalTriggerCondition(node, path);
        if (EnumerateTriggerConditions(condition).Any(item => item.Kind is
                SkillProgramTriggerConditionKind.CardActionActorIsCurrentTurn or
                SkillProgramTriggerConditionKind.CardActionPhaseIsPlay) &&
            (window is not (SkillProgramTriggerWindow.CardUseCommitted or
                SkillProgramTriggerWindow.CardUseBeforeTargetEffects or
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
                item.Kind == SkillProgramTriggerConditionKind.CardActionCardIsRed) &&
            !isCardActionWindow)
            Fail(path + ".condition", "cardActionCardIsRed requires a card-action trigger");
        if (EnumerateTriggerConditions(condition).Any(item =>
                item.Kind == SkillProgramTriggerConditionKind.DeathKillerIsOwner) &&
            window != SkillProgramTriggerWindow.CharacterDied)
            Fail(path + ".condition", "deathKillerIsOwner requires a characterDied trigger");
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
            window != SkillProgramTriggerWindow.AfterDamageApplied)
            Fail(path + ".condition", "directCardUseDamage requires an afterDamageApplied trigger");
        if (EnumerateTriggerConditions(condition).Any(item =>
                item.Kind == SkillProgramTriggerConditionKind.DamageCardIsRed) &&
            window != SkillProgramTriggerWindow.AfterDamageApplied)
            Fail(path + ".condition", "damageCardIsRed requires an afterDamageApplied trigger");
        if (EnumerateTriggerConditions(condition).Any(item =>
                item.Kind == SkillProgramTriggerConditionKind.DamageCardIsSlash) &&
            window != SkillProgramTriggerWindow.AfterDamageApplied)
            Fail(path + ".condition", "damageCardIsSlash requires an afterDamageApplied trigger");
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
                SkillProgramTriggerWindow.CardUseCompleted or SkillProgramTriggerWindow.CardUseBeforeTargetEffects))
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
        if (EnumerateTriggerValues(condition).Any(value => value.Kind == SkillProgramTriggerValueKind.EventTargetHp) &&
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
            window != SkillProgramTriggerWindow.SlashBeforeResponse)
            Fail(path + ".condition", "target-hand comparison requires a Slash response boundary");
        if (EnumerateTriggerValues(condition).Any(value => value.Kind == SkillProgramTriggerValueKind.CurrentAttackRange) &&
            window is not (SkillProgramTriggerWindow.SlashBeforeResponse or
                SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardResponseAccepted))
            Fail(path + ".condition", "attack-range comparison requires a card-action or Slash response boundary");
        if (EnumerateTriggerValues(condition).Any(value =>
                value.Kind == SkillProgramTriggerValueKind.OwnerEventTargetDistance) &&
            (!isCardActionWindow || window == SkillProgramTriggerWindow.CardResponseAccepted))
            Fail(path + ".condition", "owner-to-event-target distance requires a card-use trigger with a target");
        if (EnumerateTriggerValues(condition).Any(value => value.Kind is
                SkillProgramTriggerValueKind.PlayPhaseKillCountByTurnOwner or
                SkillProgramTriggerValueKind.PlayPhaseDamageDealtByTurnOwner) &&
            window is not (SkillProgramTriggerWindow.TurnEnding or
                SkillProgramTriggerWindow.PlayEnding or
                SkillProgramTriggerWindow.PlayPhaseStarting))
            Fail(path + ".condition", "play-phase kill and damage counters require a phase boundary trigger");
        var effects = ReadArray(node, "effects", path,
            (effect, effectPath) => ParseCompositionEffect(effect, effectPath,
                isAfterDamageTrigger: window == SkillProgramTriggerWindow.AfterDamageApplied,
                allowZeroDraw: drawPhaseMode == SkillProgramDrawPhaseMode.Replacement));
        foreach (var effect in effects.Where(item => item.Op == SkillProgramEffectOp.SkipTurnPhases))
        {
            var valid = window switch
            {
                SkillProgramTriggerWindow.TurnStartBeforeNormalFlow =>
                    effect.SkippedPhases.All(phase => phase is SkillProgramTurnPhase.Judgment or SkillProgramTurnPhase.Draw),
                SkillProgramTriggerWindow.AfterNormalDraw =>
                    effect.SkippedPhases.SequenceEqual([SkillProgramTurnPhase.Play]),
                SkillProgramTriggerWindow.DiscardPhaseStarting =>
                    effect.SkippedPhases.SequenceEqual([SkillProgramTurnPhase.Discard]),
                _ => false
            };
            if (!valid) Fail(path + ".effects", "phase substitution does not match its lifecycle boundary");
        }
        if (effects.Count == 0) Fail(path + ".effects", "must contain at least one effect");
        if (effects.Any(effect => effect.TargetKind == SkillProgramTargetKind.CurrentCardUseTargets ||
                effect.Op == SkillProgramEffectOp.NullifySelectedCardEffects) &&
            window != SkillProgramTriggerWindow.CardUseBeforeTargetEffects)
            Fail(path + ".effects", "current card-use target effects require before-target-effects card-use trigger");
        if (effects.Any(effect => effect.Op == SkillProgramEffectOp.ProhibitCurrentResponse) &&
            window != SkillProgramTriggerWindow.SlashBeforeResponse)
            Fail(path + ".effects", "response prohibition requires the Slash-before-response boundary");
        if (effects.Any(effect => effect.Op == SkillProgramEffectOp.RedirectCurrentAttack ||
                effect.TargetKind == SkillProgramTargetKind.SlashRedirectable) &&
            window != SkillProgramTriggerWindow.SlashTargetRedirecting)
            Fail(path + ".effects", "Slash redirection requires the target-redirection boundary");
        if (window == SkillProgramTriggerWindow.DamageAppliedBeforeDying &&
            effects.Any(effect => effect.Op != SkillProgramEffectOp.ChangeAttributedMarker ||
                                  effect.Target != SkillProgramEffectTarget.Owner))
            Fail(path + ".effects", "damageAppliedBeforeDying supports only owner attributed-marker records");
        if (effects.Any(effect => ContainsCardUseColorCondition(effect.Condition)) && !isCardActionWindow)
            Fail(path + ".effects", "cardUseIsRed requires a card-action trigger");
        if (window != SkillProgramTriggerWindow.AfterDamageApplied &&
            window is not (SkillProgramTriggerWindow.JudgmentReplacing or SkillProgramTriggerWindow.JudgmentFinalized) &&
            ownerRelation is not (SkillProgramCardActionOwnerRelation.Target or
                SkillProgramCardActionOwnerRelation.ConversionSource) &&
            window is not (SkillProgramTriggerWindow.SlashFullyDodged or SkillProgramTriggerWindow.SlashBeforeResponse) &&
            effects.SelectMany(EnumerateParticipantReferences)
                .Any(reference => reference.Kind == ProgramParticipantRef.EventTarget))
            Fail(path + ".effects", "eventTarget requires a target-related card-action owner relation");
        if (effects.SelectMany(EnumerateParticipantReferences)
                .Any(reference => reference.Kind == ProgramParticipantRef.EventSource) &&
            window is not (SkillProgramTriggerWindow.AfterDamageApplied or
                SkillProgramTriggerWindow.DamageAppliedBeforeDying or
                SkillProgramTriggerWindow.JudgmentFinalized or
                SkillProgramTriggerWindow.DiscardPhaseEnded))
            Fail(path + ".effects", "eventSource requires a damage-applied, judgment or discard-phase-ended trigger");
        if (window == SkillProgramTriggerWindow.JudgmentReplacing &&
            (effects[0].Op != SkillProgramEffectOp.ReplaceJudgment ||
             effects.Skip(1).Any(effect => effect.Op is not
                 (SkillProgramEffectOp.Draw or SkillProgramEffectOp.Recover) ||
                 effect.Target != SkillProgramEffectTarget.Owner ||
                 effect.ReplacementSuits.Count == 0)))
            Fail(path + ".effects", "judgmentReplacing requires replaceJudgment first, followed only by draw or recover");
        if (window != SkillProgramTriggerWindow.JudgmentReplacing &&
            effects.Any(effect => effect.Op == SkillProgramEffectOp.ReplaceJudgment ||
                                  effect.ReplacementSuits.Count > 0))
            Fail(path + ".effects", "judgment replacement effects require judgmentReplacing");
        if (window != SkillProgramTriggerWindow.JudgmentFinalized &&
            effects.Any(effect => effect.Op == SkillProgramEffectOp.ClaimJudgmentCard))
            Fail(path + ".effects", "claimJudgmentCard requires judgmentFinalized");
        ProgramCompositionValidator.Validate(path, effects, window: window, drawPhaseMode: drawPhaseMode);
        return new SkillProgramTrigger(id, window, sourceSkillId, sourceViewAsId, subject, suits,
            minimumRank, maximumRank, excludedReasons, judgmentReasons, judgmentSource,
            cardKinds, sourceZones, movementOccurrence, damageOccurrence, drawPhaseMode, optional,
            condition, effects, priority, usageScope, usageLimit, choiceGroup, ownerRelation,
            cardCategories: cardCategories, damageCardKinds: damageCardKinds, turnOwnerScope: turnOwnerScope)
        {
            DestinationZones = destinationZones,
            MovementReasons = movementReasons,
            ExcludedMovementReasons = excludedMovementReasons,
            IgnoreOwnSkillMovements = ignoreOwnSkillMovements,
            HpChangeOccurrence = hpChangeOccurrence
        };
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
                SkillProgramTriggerWindow.PlayPhaseStarting))
                Fail(path + ".triggers", $"choice group '{group.Key}' uses unsupported window '{window}'");
            if (members.Select(trigger => trigger.Priority).Distinct().Count() != 1)
                Fail(path + ".triggers", $"choice group '{group.Key}' requires one shared priority");
        }
    }

    private static void ValidateTriggerSources(IReadOnlyDictionary<string, SkillProgram> programs)
    {
        foreach (var owner in programs.Values)
            foreach (var trigger in owner.Triggers)
            {
                var path = $"skill '{owner.Id}'.triggers.{trigger.Id}";
                if (trigger.Window is SkillProgramTriggerWindow.JudgmentFinalized or
                    SkillProgramTriggerWindow.JudgmentReplacing or
                    SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or
                    SkillProgramTriggerWindow.DrawPhaseStarting or
                    SkillProgramTriggerWindow.AfterNormalDraw or
                    SkillProgramTriggerWindow.SelfDyingResponse or
                    SkillProgramTriggerWindow.DyingResponse or
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
                if (source.ViewAs.Count == 0)
                    Fail(path + ".sourceSkillId", $"skill '{trigger.SourceSkillId}' has no viewAs rules");
                var candidates = trigger.SourceViewAsId is null
                    ? source.ViewAs
                    : source.ViewAs.Where(rule => rule.Id == trigger.SourceViewAsId).ToArray();
                if (trigger.SourceViewAsId is not null && candidates.Count == 0)
                    Fail(path + ".sourceViewAsId", $"references unknown viewAs '{trigger.SourceViewAsId}'");
                var supported = trigger.Window switch
                {
                    SkillProgramTriggerWindow.CardUseTargetsFinalized => candidates.Any(rule => rule.ForPlay),
                    SkillProgramTriggerWindow.CardResponseAccepted => candidates.Any(rule => rule.ForResponse),
                    SkillProgramTriggerWindow.AfterDamageApplied => candidates.Any(rule => rule.ForPlay),
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
        CheckProperties(node, path, "kind", "children", "left", "operator", "right", "stateId", "expectedValue", "generalIds", "conversionSkillId", "categories", "factions");
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
            factions);
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
        var usesMarker = kind == SkillProgramTriggerValueKind.OwnerAttributedMarkerCount;
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
                CardZoneKind.Authority or CardZoneKind.Chunlao))
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
        bool allowAttackRangeCoverage = false, bool allowBoundCardCount = false, bool allowOwnedCardCategory = false)
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
                    allowBoundCardCount, allowOwnedCardCategory));
        }
        var needsValue = kind is SkillProgramConditionKind.HpAtLeast or SkillProgramConditionKind.HandCountAtLeast or
            SkillProgramConditionKind.BoundCardCountAtLeast or SkillProgramConditionKind.ActivationCardCountAtLeast;
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
        if ((kind is SkillProgramConditionKind.BoundCardsMatchCategories or
             SkillProgramConditionKind.HasOwnedCardCategory) != (cardCategories.Count > 0))
            Fail(path, "cardCategories are required for card-category conditions and must not be empty");
        if ((kind == SkillProgramConditionKind.HasOwnedCardCategory) != (zones.Count > 0) ||
            zones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)))
            Fail(path, "zones are required for hasOwnedCardCategory and support hand or equipment only");
        if ((kind == SkillProgramConditionKind.BoundCardsMatchKinds) != (cardKinds.Count > 0) ||
            cardKinds.Distinct().Count() != cardKinds.Count)
            Fail(path, "cardKinds are required only for boundCardsMatchKinds and must be distinct and nonempty");
        if ((kind == SkillProgramConditionKind.BoundCardsMatchSuits) != (suits.Count > 0) ||
            suits.Distinct().Count() != suits.Count)
            Fail(path, "suits are required only for boundCardsMatchSuits and must be distinct and nonempty");
        if ((kind == SkillProgramConditionKind.BooleanState) != (stateId is not null))
            Fail(path, kind == SkillProgramConditionKind.BooleanState
                ? "booleanState requires stateId" : "stateId is accepted only by booleanState");
        if (kind is not (SkillProgramConditionKind.BooleanState or SkillProgramConditionKind.BoundCardSuitMatchesChoice) &&
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
