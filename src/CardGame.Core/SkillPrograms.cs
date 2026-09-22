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
    AttackRange
}
public enum SkillRuleOperation { Add, Set, Unlimited }
public enum SkillRuleValueExpression { LivingFactionCount }
public enum SkillProgramConditionKind { Always, OwnTurn, NotOwnTurn, Wounded, HpAtLeast, HandCountAtLeast, CardUseIsRed, PindianWon, BooleanState, All, Any, Not }
public enum SkillProgramTriggerConditionKind
{
    Always,
    Compare,
    ClassicIdentityMode,
    BooleanState,
    CardActionActorIsCurrentTurn,
    CardActionPhaseIsPlay,
    All,
    Any,
    Not
}
public enum SkillProgramTriggerValueKind
{
    IntegerConstant,
    CardsUsedThisTurn,
    CurrentHp,
    CurrentMaxHp,
    MovedCardCount,
    SourceZoneCountBefore,
    SourceZoneCountAfter
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
    AnyLiving,
    OtherWounded,
    AnyWounded,
    AnyLivingHandBelowMaxHp,
    EventTarget
}
public enum SkillProgramEffectOp
{
    Draw,
    Recover,
    LoseHp,
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
    GrantDirectedTurnCardPolicy
}
public enum SkillProgramEffectTarget { Owner, SelectedTarget }
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
    SelfDyingResponse,
    AfterDamageApplied,
    PlayEnding,
    TurnEnding,
    CardsMoved
}
public enum SkillProgramTriggerEffectOp
{
    Draw,
    Recover,
    LoseHp,
    ObtainOpponentHandCard,
    ReplaceJudgment,
    SelectTarget,
    Damage,
    StartJudgment,
    CauseDeath,
    InsertPhase,
    RecoverTo,
    TurnOver,
    RevealTopCards,
    SelectCardSubset,
    MoveBoundCards,
    SetFaceState,
    SelectSourceCard,
    GiveBoundCard,
    ClaimDamageCards,
    SelectTargets,
    TakeRandomHandCardFromSelectedTargets,
    FilterBoundCards,
    AdjustNormalDraw,
    GrantTurnCardDamageModifier,
    GrantTurnCardActionProhibition,
    GrantTurnRuleModifier,
    GrantTurnCardTargetRestriction,
    GrantTurnCardConversion,
    DiscardOwnedZoneCards,
    SetChainedState,
    SelectAndMoveOwnedCard,
    RefundCardUseDebit,
    StartPindian,
    SetBooleanState,
    ToggleBooleanState,
    GrantDirectedTurnCardPolicy
}
public enum SkillProgramTriggerEffectTarget { Owner, Opponent, SelectedTarget, JudgmentSubject }
public enum SkillProgramTriggerSubject { Owner, Any }
public enum SkillProgramMovementOccurrence { PerBatch, PerCard }
public enum SkillProgramDamageOccurrence { PerDamage, PerDamagePoint }
public enum SkillProgramDrawPhaseMode { Additive, Replacement }
public enum SkillProgramOldJudgmentCardDestination { DiscardPile, OwnerHand }
public enum SkillProgramNumberExpression
{
    LivingFactionCount,
    TargetMaxHpMinusHandCount,
    OwnerLostHp,
    BoundCardCount,
    IntegerConstant
}
public enum SkillProgramCardSetVisibility { Private, Public }
public enum SkillProgramCardDestination { OwnerHand, DiscardPile, SelectedTargetHand }
public enum SkillProgramSubsetAiOrder { MostCardsThenRankSum }
public enum SkillProgramTargetAiOrder { Stable, HostileThenHandCount }
public enum SkillProgramPhaseContinuation { BeforeNormalPreparation }
public enum SkillProgramCardTargetRestriction { SelfOnly }
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
        string? sourceBind = null, string? stateId = null, bool expectedValue = true)
    {
        Kind = kind;
        Value = value;
        Children = children;
        SourceBind = sourceBind;
        StateId = stateId;
        ExpectedValue = expectedValue;
    }

    public SkillProgramConditionKind Kind { get; }
    public int Value { get; }
    public IReadOnlyList<SkillProgramCondition> Children { get; }
    public string? SourceBind { get; }
    public string? StateId { get; }
    public bool ExpectedValue { get; }

    public bool Evaluate(PlayerSkillContext context) => Evaluate(context, null);

    internal bool Evaluate(PlayerSkillContext context, bool? cardUseIsRed) => Kind switch
    {
        SkillProgramConditionKind.Always => true,
        SkillProgramConditionKind.OwnTurn => context.IsOwnTurn,
        SkillProgramConditionKind.NotOwnTurn => !context.IsOwnTurn,
        SkillProgramConditionKind.Wounded => context.Hp < context.MaxHp,
        SkillProgramConditionKind.HpAtLeast => context.Hp >= Value,
        SkillProgramConditionKind.HandCountAtLeast => context.HandCount >= Value,
        SkillProgramConditionKind.CardUseIsRed => cardUseIsRed == true,
        SkillProgramConditionKind.PindianWon or SkillProgramConditionKind.BooleanState =>
            throw new InvalidOperationException($"Condition '{Kind}' requires a running program frame."),
        SkillProgramConditionKind.All => Children.All(child => child.Evaluate(context, cardUseIsRed)),
        SkillProgramConditionKind.Any => Children.Any(child => child.Evaluate(context, cardUseIsRed)),
        SkillProgramConditionKind.Not => !Children[0].Evaluate(context, cardUseIsRed),
        _ => throw new InvalidOperationException($"Unsupported condition kind '{Kind}'.")
    };

    internal bool Evaluate(
        PlayerSkillContext context,
        Func<string, bool> pindianWon,
        Func<string, bool> booleanState,
        bool? cardUseIsRed = null) => Kind switch
    {
        SkillProgramConditionKind.PindianWon => pindianWon(SourceBind!),
        SkillProgramConditionKind.BooleanState => booleanState(StateId!) == ExpectedValue,
        SkillProgramConditionKind.All => Children.All(child => child.Evaluate(context, pindianWon, booleanState, cardUseIsRed)),
        SkillProgramConditionKind.Any => Children.Any(child => child.Evaluate(context, pindianWon, booleanState, cardUseIsRed)),
        SkillProgramConditionKind.Not => !Children[0].Evaluate(context, pindianWon, booleanState, cardUseIsRed),
        _ => Evaluate(context, cardUseIsRed)
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
    bool? CardActionPhaseIsPlay = null)
{
    public bool GetBooleanState(string skillId, string skillInstanceId, string stateId) =>
        BooleanStates?.GetValueOrDefault(BooleanStateKey(skillId, skillInstanceId, stateId)) ??
        throw new InvalidOperationException("The frozen trigger facts do not contain the requested boolean state.");

    public static string BooleanStateKey(string skillId, string skillInstanceId, string stateId) =>
        $"{skillId}\u001f{skillInstanceId}\u001f{stateId}";
}

public sealed record SkillProgramTriggerValue(SkillProgramTriggerValueKind Kind, int Value)
{
    public int Resolve(SkillProgramTriggerFacts facts) => Kind switch
    {
        SkillProgramTriggerValueKind.IntegerConstant => Value,
        SkillProgramTriggerValueKind.CardsUsedThisTurn => facts.CardsUsedThisTurn,
        SkillProgramTriggerValueKind.CurrentHp => facts.CurrentHp,
        SkillProgramTriggerValueKind.CurrentMaxHp => facts.CurrentMaxHp,
        SkillProgramTriggerValueKind.MovedCardCount => facts.MovedCardCount,
        SkillProgramTriggerValueKind.SourceZoneCountBefore => facts.SourceZoneCountBefore,
        SkillProgramTriggerValueKind.SourceZoneCountAfter => facts.SourceZoneCountAfter,
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
        bool expectedValue = true) =>
        (Kind, Children, Left, Comparison, Right, StateId, ExpectedValue) =
        (kind, children, left, comparison, right, stateId, expectedValue);

    public SkillProgramTriggerConditionKind Kind { get; }
    public IReadOnlyList<SkillProgramTriggerCondition> Children { get; }
    public SkillProgramTriggerValue? Left { get; }
    public SkillProgramComparisonOperator? Comparison { get; }
    public SkillProgramTriggerValue? Right { get; }
    public string? StateId { get; }
    public bool ExpectedValue { get; }

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
        SkillProgramTriggerConditionKind.CardActionPhaseIsPlay => facts.CardActionPhaseIsPlay == true,
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
        int priority,
        string? sourceCardIdentityId,
        SkillProgramCondition condition) =>
        (Id, Query, Operation, Value, ValueExpression, Priority, SourceCardIdentityId, Condition) =
        (id, query, operation, value, valueExpression, priority, sourceCardIdentityId, condition);
    public string Id { get; }
    public SkillRuleQuery Query { get; }
    public SkillRuleOperation Operation { get; }
    public int Value { get; }
    public SkillRuleValueExpression? ValueExpression { get; }
    public int Priority { get; }
    public string? SourceCardIdentityId { get; }
    public SkillProgramCondition Condition { get; }

    public int EvaluateValue(int livingFactionCount) => ValueExpression switch
    {
        null => Value,
        SkillRuleValueExpression.LivingFactionCount when livingFactionCount >= 0 => livingFactionCount,
        SkillRuleValueExpression.LivingFactionCount => throw new ArgumentOutOfRangeException(
            nameof(livingFactionCount), livingFactionCount, "Living faction count cannot be negative."),
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
        CardKind outputKind, bool forPlay, bool forResponse, SkillProgramCondition condition) =>
        (Id, InputKinds, InputSuits, OutputKind, ForPlay, ForResponse, Condition) =
        (id, inputKinds, inputSuits, outputKind, forPlay, forResponse, condition);
    public string Id { get; }
    public IReadOnlyList<CardKind> InputKinds { get; }
    public IReadOnlyList<Suit> InputSuits { get; }
    public CardKind OutputKind { get; }
    public bool ForPlay { get; }
    public bool ForResponse { get; }
    public SkillProgramCondition Condition { get; }
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
        DirectedTurnCardPolicyEffect directedPolicyEffects = DirectedTurnCardPolicyEffect.None) =>
        (Op, Target, Amount, Condition, Phase, PhaseContinuation, NumberExpression, MinimumValue,
            ClampToMaxHp, SourceBind, ResultBind, ExceptBind, Visibility, MinimumCards, MaximumCards,
            MaximumRankSum, AiOrder, Destination, FaceDown, Zones, TargetKind,
            MinimumTargets, MaximumTargets, TargetAiOrder, Suits, CardKinds, ActionTypes,
            RuleQuery, RuleOperation, TargetRestriction, JudgmentReason, ColorRelation, OutputKind,
            Chained, ChooserRef, CardOwnerRef, StateId, BooleanValue, OpponentReference, ActorReference, TargetReference,
            DirectedPolicyEffects) =
        (op, target, amount, condition, phase, phaseContinuation, numberExpression, minimumValue,
            clampToMaxHp, sourceBind, resultBind, exceptBind, visibility, minimumCards, maximumCards,
            maximumRankSum, aiOrder, destination, faceDown,
            zones ?? Array.Empty<CardZoneKind>(), targetKind, minimumTargets, maximumTargets, targetAiOrder,
            suits ?? Array.Empty<Suit>(), cardKinds ?? Array.Empty<CardKind>(),
            actionTypes ?? Array.Empty<CardActionType>(), ruleQuery, ruleOperation, targetRestriction,
            judgmentReason, colorRelation, outputKind, chained, chooserRef, cardOwnerRef,
            stateId, booleanValue, opponentReference,
            actorReference, targetReference, directedPolicyEffects);
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
    public int MaximumRankSum { get; }
    public SkillProgramSubsetAiOrder? AiOrder { get; }
    public SkillProgramCardDestination? Destination { get; }
    public bool? FaceDown { get; }
    public IReadOnlyList<CardZoneKind> Zones { get; }
    public SkillProgramTargetKind? TargetKind { get; }
    public int MinimumTargets { get; }
    public int MaximumTargets { get; }
    public SkillProgramTargetAiOrder? TargetAiOrder { get; }
    public IReadOnlyList<Suit> Suits { get; }
    public IReadOnlyList<CardKind> CardKinds { get; }
    public IReadOnlyList<CardActionType> ActionTypes { get; }
    public SkillRuleQuery? RuleQuery { get; }
    public SkillRuleOperation? RuleOperation { get; }
    public SkillProgramCardTargetRestriction? TargetRestriction { get; }
    public string? JudgmentReason { get; }
    public SkillProgramCardColorRelation? ColorRelation { get; }
    public CardKind? OutputKind { get; }
    public bool? Chained { get; }
    public ProgramParticipantReference? ChooserRef { get; }
    public ProgramParticipantReference? CardOwnerRef { get; }
    public string? StateId { get; }
    public bool? BooleanValue { get; }
    public ProgramParticipantReference? OpponentReference { get; }
    public ProgramParticipantReference? ActorReference { get; }
    public ProgramParticipantReference? TargetReference { get; }
    public DirectedTurnCardPolicyEffect DirectedPolicyEffects { get; }
}

public sealed class SkillProgramActivation
{
    internal SkillProgramActivation(string id, int minCards, int maxCards, int minTargets, int maxTargets,
        SkillProgramTargetKind targetKind, int? usesPerTurn, SkillProgramCondition condition,
        IReadOnlyList<SkillProgramEffect> effects) =>
        (Id, MinCards, MaxCards, MinTargets, MaxTargets, TargetKind, UsesPerTurn, Condition, Effects) =
        (id, minCards, maxCards, minTargets, maxTargets, targetKind, usesPerTurn, condition, effects);
    public string Id { get; }
    public int MinCards { get; }
    public int MaxCards { get; }
    public int MinTargets { get; }
    public int MaxTargets { get; }
    public SkillProgramTargetKind TargetKind { get; }
    public int? UsesPerTurn { get; }
    public SkillProgramCondition Condition { get; }
    public IReadOnlyList<SkillProgramEffect> Effects { get; }
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

public sealed class SkillProgramTriggerEffect
{
    internal SkillProgramTriggerEffect(SkillProgramTriggerEffectOp op, SkillProgramTriggerEffectTarget target,
        int amount, SkillProgramCondition condition, IReadOnlyList<CardZoneKind> zones,
        IReadOnlyList<Suit> suits, SkillProgramOldJudgmentCardDestination? oldCardDestination,
        IReadOnlyList<Suit> replacementSuits, int minimumReplacementRank, int maximumReplacementRank,
        SkillProgramTargetKind? targetKind, DamageNature? damageNature, string? judgmentReason,
        TurnPhase? phase = null, SkillProgramPhaseContinuation? phaseContinuation = null,
        SkillProgramNumberExpression? numberExpression = null, int minimumValue = 0,
        bool clampToMaxHp = false, string? sourceBind = null, string? resultBind = null,
        string? exceptBind = null, SkillProgramCardSetVisibility visibility = SkillProgramCardSetVisibility.Private,
        int minimumCards = 0, int maximumCards = 0, int maximumRankSum = 0,
        SkillProgramSubsetAiOrder? aiOrder = null, SkillProgramCardDestination? destination = null,
        bool? faceDown = null, int minimumTargets = 0, int maximumTargets = 0,
        SkillProgramTargetAiOrder? targetAiOrder = null,
        IReadOnlyList<CardKind>? cardKinds = null,
        IReadOnlyList<CardActionType>? actionTypes = null,
        SkillRuleQuery? ruleQuery = null,
        SkillRuleOperation? ruleOperation = null,
        SkillProgramCardTargetRestriction? targetRestriction = null,
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
        DirectedTurnCardPolicyEffect directedPolicyEffects = DirectedTurnCardPolicyEffect.None) =>
        (Op, Target, Amount, Condition, Zones, Suits, OldCardDestination, ReplacementSuits,
            MinimumReplacementRank, MaximumReplacementRank, TargetKind, DamageNature, JudgmentReason,
            Phase, PhaseContinuation, NumberExpression, MinimumValue, ClampToMaxHp, SourceBind,
            ResultBind, ExceptBind, Visibility, MinimumCards, MaximumCards, MaximumRankSum, AiOrder,
            Destination, FaceDown, MinimumTargets, MaximumTargets, TargetAiOrder, CardKinds,
            ActionTypes, RuleQuery, RuleOperation, TargetRestriction, ColorRelation, OutputKind,
            Chained, ChooserRef, CardOwnerRef, StateId, BooleanValue, OpponentReference,
            ActorReference, TargetReference, DirectedPolicyEffects) =
        (op, target, amount, condition, zones, suits, oldCardDestination, replacementSuits,
            minimumReplacementRank, maximumReplacementRank, targetKind, damageNature, judgmentReason,
            phase, phaseContinuation, numberExpression, minimumValue, clampToMaxHp, sourceBind,
            resultBind, exceptBind, visibility, minimumCards, maximumCards, maximumRankSum, aiOrder,
            destination, faceDown, minimumTargets, maximumTargets, targetAiOrder,
            cardKinds ?? Array.Empty<CardKind>(), actionTypes ?? Array.Empty<CardActionType>(),
            ruleQuery, ruleOperation, targetRestriction, colorRelation, outputKind, chained,
            chooserRef, cardOwnerRef, stateId, booleanValue, opponentReference, actorReference,
            targetReference, directedPolicyEffects);
    public SkillProgramTriggerEffectOp Op { get; }
    public SkillProgramTriggerEffectTarget Target { get; }
    public int Amount { get; }
    public SkillProgramCondition Condition { get; }
    public IReadOnlyList<CardZoneKind> Zones { get; }
    public IReadOnlyList<Suit> Suits { get; }
    public SkillProgramOldJudgmentCardDestination? OldCardDestination { get; }
    public IReadOnlyList<Suit> ReplacementSuits { get; }
    public int MinimumReplacementRank { get; }
    public int MaximumReplacementRank { get; }
    public SkillProgramTargetKind? TargetKind { get; }
    public DamageNature? DamageNature { get; }
    public string? JudgmentReason { get; }
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
    public int MaximumRankSum { get; }
    public SkillProgramSubsetAiOrder? AiOrder { get; }
    public SkillProgramCardDestination? Destination { get; }
    public bool? FaceDown { get; }
    public int MinimumTargets { get; }
    public int MaximumTargets { get; }
    public SkillProgramTargetAiOrder? TargetAiOrder { get; }
    public IReadOnlyList<CardKind> CardKinds { get; }
    public IReadOnlyList<CardActionType> ActionTypes { get; }
    public SkillRuleQuery? RuleQuery { get; }
    public SkillRuleOperation? RuleOperation { get; }
    public SkillProgramCardTargetRestriction? TargetRestriction { get; }
    public SkillProgramCardColorRelation? ColorRelation { get; }
    public CardKind? OutputKind { get; }
    public bool? Chained { get; }
    public ProgramParticipantReference? ChooserRef { get; }
    public ProgramParticipantReference? CardOwnerRef { get; }
    public string? StateId { get; }
    public bool? BooleanValue { get; }
    public ProgramParticipantReference? OpponentReference { get; }
    public ProgramParticipantReference? ActorReference { get; }
    public ProgramParticipantReference? TargetReference { get; }
    public DirectedTurnCardPolicyEffect DirectedPolicyEffects { get; }

    internal SkillProgramEffect ToExecutionEffect()
    {
        if (!Enum.TryParse<SkillProgramEffectOp>(Op.ToString(), out var executionOp))
            throw new InvalidOperationException($"Trigger operation '{Op}' is not executable by the shared program runtime.");
        var executionTarget = Target switch
        {
            SkillProgramTriggerEffectTarget.Owner => SkillProgramEffectTarget.Owner,
            SkillProgramTriggerEffectTarget.SelectedTarget => SkillProgramEffectTarget.SelectedTarget,
            _ => throw new InvalidOperationException(
                $"Trigger target '{Target}' is not executable by the shared program runtime.")
        };
        return new SkillProgramEffect(executionOp, executionTarget, Amount, Condition, Phase,
            PhaseContinuation, NumberExpression, MinimumValue, ClampToMaxHp, SourceBind, ResultBind,
            ExceptBind, Visibility, MinimumCards, MaximumCards, MaximumRankSum, AiOrder, Destination,
            FaceDown, Zones, TargetKind, MinimumTargets, MaximumTargets, TargetAiOrder, Suits, CardKinds,
            ActionTypes, RuleQuery, RuleOperation, TargetRestriction, JudgmentReason, ColorRelation, OutputKind,
            Chained, ChooserRef, CardOwnerRef, StateId, BooleanValue, OpponentReference,
            ActorReference, TargetReference, DirectedPolicyEffects);
    }
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
        IReadOnlyList<SkillProgramTriggerEffect> effects,
        int priority = 0, SkillUsageScope? usageScope = null, int? usageLimit = null,
        string? choiceGroup = null,
        SkillProgramCardActionOwnerRelation? ownerRelation = null,
        bool usesSharedExecutor = false) =>
        (Id, Window, SourceSkillId, SourceViewAsId, Subject, Suits, MinimumRank, MaximumRank,
            ExcludedReasons, JudgmentReasons, JudgmentSource, CardKinds, SourceZones, MovementOccurrence,
            DamageOccurrence, DrawPhaseMode,
            Optional, Condition, Effects, Priority, UsageScope, UsageLimit, ChoiceGroup, OwnerRelation,
            UsesSharedExecutor) =
        (id, window, sourceSkillId, sourceViewAsId, subject, suits, minimumRank, maximumRank,
            excludedReasons, judgmentReasons, judgmentSource, cardKinds, sourceZones, movementOccurrence,
            damageOccurrence, drawPhaseMode,
            optional, condition, effects, priority, usageScope, usageLimit, choiceGroup, ownerRelation,
            usesSharedExecutor);
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
    public IReadOnlyList<CardZoneKind> SourceZones { get; }
    public SkillProgramMovementOccurrence? MovementOccurrence { get; }
    public SkillProgramDamageOccurrence? DamageOccurrence { get; }
    public SkillProgramDrawPhaseMode DrawPhaseMode { get; }
    public bool Optional { get; }
    public SkillProgramTriggerCondition Condition { get; }
    public IReadOnlyList<SkillProgramTriggerEffect> Effects { get; }
    public int Priority { get; }
    public SkillUsageScope? UsageScope { get; }
    public int? UsageLimit { get; }
    public string? ChoiceGroup { get; }
    public SkillProgramCardActionOwnerRelation? OwnerRelation { get; }
    public string? ChoiceLabel { get; internal set; }

    public bool UsesSharedExecutor { get; }
}

public sealed class SkillProgram
{
    internal SkillProgram(string id, int revision, string gameplayHash, string runtimeVersion, int minimumRulesVersion,
        IReadOnlyList<SkillProgramModifier> modifiers, IReadOnlyList<SkillProgramViewAs> viewAs,
        IReadOnlyList<SkillProgramActivation> activations, IReadOnlyList<SkillProgramTrigger> triggers,
        IReadOnlyList<SkillProgramContribution> contributions,
        IReadOnlyList<SkillProgramCardIdentity> cardIdentities,
        bool usesCompositionKernel = false,
        IReadOnlyList<SkillProgramBooleanStateDefinition>? booleanStates = null) =>
        (Id, Revision, GameplayHash, RuntimeVersion, MinimumRulesVersion, Modifiers, ViewAs, Activations, Triggers,
            Contributions, CardIdentities, UsesCompositionKernel, BooleanStates) =
        (id, revision, gameplayHash, runtimeVersion, minimumRulesVersion, modifiers, viewAs, activations, triggers,
            contributions, cardIdentities, usesCompositionKernel, booleanStates ?? []);
    public string Id { get; }
    public int Revision { get; }
    public string GameplayHash { get; }
    public string RuntimeVersion { get; }
    public int MinimumRulesVersion { get; }
    internal bool UsesCompositionKernel { get; }
    public IReadOnlyList<SkillProgramModifier> Modifiers { get; }
    public IReadOnlyList<SkillProgramViewAs> ViewAs { get; }
    public IReadOnlyList<SkillProgramActivation> Activations { get; }
    public IReadOnlyList<SkillProgramTrigger> Triggers { get; }
    public IReadOnlyList<SkillProgramContribution> Contributions { get; }
    public IReadOnlyList<SkillProgramCardIdentity> CardIdentities { get; }
    public IReadOnlyList<SkillProgramBooleanStateDefinition> BooleanStates { get; }
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
    public const string RuntimeVersion = "skill-program-v1";
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
        var schemaVersion = RequireVersion(root, "rules", 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24);
        var runtimeVersion = schemaVersion switch
        {
            1 => RuntimeVersion,
            2 => "skill-program-v2",
            3 => "skill-program-v3",
            4 => "skill-program-v4",
            5 => "skill-program-v5",
            6 => "skill-program-v6",
            7 => "skill-program-v7",
            8 => "skill-program-v8",
            9 => "skill-program-v9",
            10 => "skill-program-v10",
            11 => "skill-program-v11",
            12 => "skill-program-v12",
            13 => "skill-program-v13",
            14 => "skill-program-v14",
            15 => "skill-program-v15",
            16 => "skill-program-v16",
            17 => "skill-program-v17",
            18 => "skill-program-v18",
            19 => "skill-program-v19",
            20 => "skill-program-v20",
            21 => "skill-program-v21",
            22 => "skill-program-v22",
            23 => "skill-program-v23",
            _ => "skill-program-v24"
        };
        var minimumRulesVersion = schemaVersion switch
        {
            1 => 79,
            2 => 80,
            3 => 81,
            4 => 82,
            5 => 83,
            6 => 84,
            7 => 85,
            8 => 86,
            9 => 93,
            10 => 94,
            11 => 116,
            12 => 117,
            13 => 118,
            14 => 119,
            15 => 120,
            16 => 121,
            17 => 122,
            18 => 123,
            19 => 124,
            20 => 125,
            21 => 126,
            22 => 127,
            23 => 128,
            _ => 129
        };
        var skills = Required(root, "skills", JsonValueKind.Array, "rules");
        CheckCount(skills.GetArrayLength(), "rules.skills");
        var result = new Dictionary<string, SkillProgram>(StringComparer.Ordinal);
        var index = 0;
        foreach (var skill in skills.EnumerateArray())
        {
            var path = $"rules.skills[{index++}]";
            RequireObject(skill, path);
            CheckProperties(skill, path, schemaVersion switch
            {
                1 => ["id", "revision", "modifiers", "viewAs", "activations"],
                < 7 => ["id", "revision", "modifiers", "viewAs", "activations", "triggers"],
                8 => ["id", "revision", "minimumRulesVersion", "modifiers", "viewAs", "activations", "triggers", "contributions"],
                9 => ["id", "revision", "minimumRulesVersion", "modifiers", "viewAs", "activations", "triggers", "contributions"],
                < 24 => ["id", "revision", "minimumRulesVersion", "modifiers", "viewAs", "activations", "triggers", "contributions", "cardIdentities"],
                _ => ["id", "revision", "minimumRulesVersion", "modifiers", "viewAs", "activations", "triggers", "contributions", "cardIdentities", "states"]
            });
            var id = Identifier(skill, "id", path);
            var skillPath = $"skill '{id}' ({path})";
            if (result.ContainsKey(id)) Fail(skillPath, $"duplicate skill id '{id}'");
            var revision = PositiveInt(skill, "revision", skillPath);
            var skillMinimumRulesVersion = minimumRulesVersion;
            if (schemaVersion >= 8 && skill.TryGetProperty("minimumRulesVersion", out _))
            {
                skillMinimumRulesVersion = RequiredInt(skill, "minimumRulesVersion", skillPath);
                if (skillMinimumRulesVersion < minimumRulesVersion)
                    Fail(skillPath + ".minimumRulesVersion",
                        $"must be at least the schema minimum {minimumRulesVersion}");
            }
            var modifiers = ReadArray(skill, "modifiers", skillPath,
                (node, modifierPath) => ParseModifier(node, modifierPath, schemaVersion), schemaVersion >= 2);
            var viewAs = ReadArray(skill, "viewAs", skillPath, ParseViewAs, schemaVersion >= 2);
            var activations = ReadArray(skill, "activations", skillPath,
                (node, activationPath) => ParseActivation(node, activationPath, schemaVersion), schemaVersion >= 2);
            var triggers = schemaVersion >= 2
                ? ReadArray(skill, "triggers", skillPath,
                    (node, triggerPath) => ParseTrigger(node, triggerPath, schemaVersion), optional: true)
                : Array.Empty<SkillProgramTrigger>();
            var contributions = schemaVersion >= 7
                ? ReadArray(skill, "contributions", skillPath, ParseContribution, optional: true)
                : Array.Empty<SkillProgramContribution>();
            var cardIdentities = schemaVersion >= 10
                ? ReadArray(skill, "cardIdentities", skillPath, ParseCardIdentity, optional: true)
                : Array.Empty<SkillProgramCardIdentity>();
            var booleanStates = schemaVersion >= 24
                ? ReadArray(skill, "states", skillPath, ParseBooleanState, optional: true)
                : Array.Empty<SkillProgramBooleanStateDefinition>();
            if (modifiers.Count == 0 && viewAs.Count == 0 && activations.Count == 0 && triggers.Count == 0 &&
                contributions.Count == 0 && cardIdentities.Count == 0)
                Fail(skillPath, "must define at least one modifier, viewAs rule, activation, trigger, contribution, or card identity");
            EnsureUniqueIds(modifiers.Select(item => item.Id), skillPath + ".modifiers");
            EnsureUniqueIds(viewAs.Select(item => item.Id), skillPath + ".viewAs");
            EnsureUniqueIds(activations.Select(item => item.Id), skillPath + ".activations");
            EnsureUniqueIds(triggers.Select(item => item.Id), skillPath + ".triggers");
            ValidateTriggerChoiceGroups(skillPath, triggers, schemaVersion);
            EnsureUniqueIds(contributions.Select(item => item.Id), skillPath + ".contributions");
            EnsureUniqueIds(cardIdentities.Select(item => item.Id), skillPath + ".cardIdentities");
            EnsureUniqueIds(booleanStates.Select(item => item.Id), skillPath + ".states");
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
                if (effect.Op is SkillProgramTriggerEffectOp.SetBooleanState or SkillProgramTriggerEffectOp.ToggleBooleanState &&
                    !declaredStateIds.Contains(effect.StateId!))
                    Fail(skillPath, $"trigger effect references undeclared state '{effect.StateId}'");
            EnsureUniqueIds(activations.Select(item => item.Id).Concat(contributions.Select(item => item.Id)),
                skillPath + ".playBindings");
            ValidateCardIdentityModifiers(skillPath, modifiers, cardIdentities);
            var hashInput = runtimeVersion + "\n" + Canonicalize(skill);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput))).ToLowerInvariant();
            result.Add(id, new SkillProgram(id, revision, hash, runtimeVersion, skillMinimumRulesVersion,
                modifiers, viewAs, activations, triggers, contributions, cardIdentities,
                usesCompositionKernel: schemaVersion >= 23, booleanStates: booleanStates));
        }
        if (schemaVersion >= 2) ValidateTriggerSources(result);
        return result;
    }

    private static Dictionary<string, SkillPresentation> LoadPresentations(JsonElement root,
        IReadOnlyDictionary<string, SkillProgram> programs)
    {
        RequireObject(root, "presentation");
        CheckProperties(root, "presentation", "schemaVersion", "skills");
        var schemaVersion = RequireVersion(root, "presentation", 1, 2);
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
            CheckProperties(property.Value, path, schemaVersion == 1
                ? ["name", "description"]
                : ["name", "description", "triggerChoices", "booleanStates"]);
            var triggerChoices = new Dictionary<string, string>(StringComparer.Ordinal);
            var booleanStates = new Dictionary<string, ProgramBooleanStatePresentation>(StringComparer.Ordinal);
            if (schemaVersion >= 2 && property.Value.TryGetProperty("triggerChoices", out var choices))
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
            if (schemaVersion >= 2 && property.Value.TryGetProperty("booleanStates", out var states))
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
            result.Add(id, new SkillPresentation(NonEmptyString(property.Value, "name", path),
                NonEmptyString(property.Value, "description", path),
                new ReadOnlyDictionary<string, string>(triggerChoices),
                new ReadOnlyDictionary<string, ProgramBooleanStatePresentation>(booleanStates)));
        }
        foreach (var id in programs.Keys)
            if (!result.ContainsKey(id)) Fail("presentation.skills", $"missing presentation for skill '{id}'");
        return result;
    }

    private static SkillProgramModifier ParseModifier(JsonElement node, string path, int schemaVersion)
    {
        RequireObject(node, path);
        CheckProperties(node, path, schemaVersion switch
        {
            >= 12 => ["id", "query", "operation", "value", "valueExpression", "priority",
                "sourceCardIdentityId", "condition"],
            >= 10 => ["query", "operation", "value", "sourceCardIdentityId", "condition"],
            _ => ["query", "operation", "value", "condition"]
        });
        var id = schemaVersion >= 12
            ? Identifier(node, "id", path)
            : "legacy-" + path[(path.LastIndexOf('[') + 1)..^1];
        var query = EnumValue<SkillRuleQuery>(node, "query", path);
        if (schemaVersion < 10 && query == SkillRuleQuery.SlashDistanceLimit)
            Fail(path + ".query", "slashDistanceLimit requires schema version 10");
        if (schemaVersion < 12 && query == SkillRuleQuery.AttackRange)
            Fail(path + ".query", "attackRange requires schema version 12");
        var operation = EnumValue<SkillRuleOperation>(node, "operation", path);
        var hasValue = node.TryGetProperty("value", out _);
        var hasValueExpression = node.TryGetProperty("valueExpression", out _);
        if (schemaVersion < 12 && hasValueExpression)
            Fail(path + ".valueExpression", "requires schema version 12");
        if (hasValue == hasValueExpression)
            Fail(path, "exactly one of value or valueExpression is required");
        var value = hasValue ? RequiredInt(node, "value", path) : 0;
        SkillRuleValueExpression? valueExpression = hasValueExpression
            ? EnumValue<SkillRuleValueExpression>(node, "valueExpression", path)
            : null;
        var priority = schemaVersion >= 12 ? RequiredInt(node, "priority", path) : 0;
        if (priority is < -1000 or > 1000)
            Fail(path + ".priority", "must be between -1000 and 1000");
        var sourceCardIdentityId = node.TryGetProperty("sourceCardIdentityId", out _)
            ? Identifier(node, "sourceCardIdentityId", path)
            : null;
        if (value is < -1024 or > 1024)
            Fail(path + ".value", "modifier value must be between -1024 and 1024");
        if (operation == SkillRuleOperation.Add && value == 0)
        {
            if (valueExpression is null) Fail(path + ".value", "add requires a non-zero value");
        }
        if (operation is SkillRuleOperation.Add or SkillRuleOperation.Unlimited && priority != 0)
            Fail(path + ".priority", "add and unlimited modifiers require priority 0");
        if (valueExpression is not null &&
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
        return new SkillProgramModifier(id, query, operation, value, valueExpression, priority,
            sourceCardIdentityId, OptionalCondition(node, path));
    }

    private static SkillProgramCardIdentity ParseCardIdentity(JsonElement node, string path)
    {
        RequireObject(node, path);
        CheckProperties(node, path, "id", "zones", "inputKinds", "inputSuits", "outputKind", "condition");
        var id = Identifier(node, "id", path);
        var zones = EnumArray<CardZoneKind>(node, "zones", path);
        if (zones.Count != 1 || zones[0] != CardZoneKind.Hand)
            Fail(path + ".zones", "schema 10 supports exactly the owner hand zone");
        var inputs = EnumArray<CardKind>(node, "inputKinds", path);
        var suits = EnumArray<Suit>(node, "inputSuits", path);
        if (inputs.Count == 0 && suits.Count == 0)
            Fail(path, "must filter at least one physical card kind or suit");
        var output = EnumValue<CardKind>(node, "outputKind", path);
        if (output is not (CardKind.Slash or CardKind.Dodge))
            Fail(path + ".outputKind", "schema 10 supports only slash or dodge identities");
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
        CheckProperties(node, path, "id", "inputKinds", "inputSuits", "outputKind", "forPlay", "forResponse", "condition");
        var id = Identifier(node, "id", path);
        var inputs = EnumArray<CardKind>(node, "inputKinds", path);
        var suits = EnumArray<Suit>(node, "inputSuits", path);
        var output = EnumValue<CardKind>(node, "outputKind", path);
        if (output is not (CardKind.Slash or CardKind.Dodge)) Fail(path + ".outputKind", "only slash or dodge is supported");
        var forPlay = RequiredBool(node, "forPlay", path);
        var forResponse = RequiredBool(node, "forResponse", path);
        if (!forPlay && !forResponse) Fail(path, "at least one of forPlay or forResponse must be true");
        if (output == CardKind.Dodge && forPlay)
            Fail(path + ".forPlay", "dodge is response-only and cannot be played proactively");
        if (inputs.Count > 0 && inputs.All(kind => kind == output))
            Fail(path + ".inputKinds", "viewAs must change at least one accepted input kind");
        return new SkillProgramViewAs(id, inputs, suits, output, forPlay, forResponse, OptionalCondition(node, path));
    }

    private static SkillProgramActivation ParseActivation(JsonElement node, string path, int schemaVersion)
    {
        RequireObject(node, path);
        CheckProperties(node, path, "id", "minCards", "maxCards", "minTargets", "maxTargets", "targetKind", "usesPerTurn", "condition", "effects");
        var id = Identifier(node, "id", path);
        var minCards = NonNegativeInt(node, "minCards", path);
        var maxCards = NonNegativeInt(node, "maxCards", path);
        var minTargets = NonNegativeInt(node, "minTargets", path);
        var maxTargets = NonNegativeInt(node, "maxTargets", path);
        if (minCards > maxCards) Fail(path, "minCards cannot exceed maxCards");
        if (maxCards > 64) Fail(path + ".maxCards", "must not exceed 64");
        if (minTargets > maxTargets) Fail(path, "minTargets cannot exceed maxTargets");
        if (maxTargets > 1) Fail(path + ".maxTargets", "must not exceed 1");
        var targetKind = EnumValue<SkillProgramTargetKind>(node, "targetKind", path);
        int? uses = null;
        if (node.TryGetProperty("usesPerTurn", out var usesNode))
        {
            if (usesNode.ValueKind == JsonValueKind.Null) uses = null;
            else { uses = GetInt(usesNode, path + ".usesPerTurn"); if (uses <= 0) Fail(path + ".usesPerTurn", "must be positive or null"); }
        }
        else Fail(path, "missing required property 'usesPerTurn'");
        var effects = ReadArray<SkillProgramEffect>(node, "effects", path, schemaVersion >= 23
            ? ParseCompositionEffect : ParseEffect);
        if (effects.Any(effect => ContainsCardUseColorCondition(effect.Condition)))
            Fail(path + ".effects", "card-use color conditions require a schema 24 card-action trigger");
        if (effects.Count == 0) Fail(path + ".effects", "must contain at least one effect");
        if (schemaVersion >= 23)
        {
            if (minTargets != maxTargets || minCards != maxCards)
                Fail(path, "composed activations require exact initial card and target counts");
            ProgramCompositionValidator.Validate(path, effects, minTargets == 1, minCards);
        }
        else ValidateActivation(path, minCards, maxCards, minTargets, maxTargets, effects);
        return new SkillProgramActivation(id, minCards, maxCards, minTargets, maxTargets, targetKind, uses,
            OptionalCondition(node, path), effects);
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

    private static SkillProgramEffect ParseEffect(JsonElement node, string path)
    {
        RequireObject(node, path);
        CheckProperties(node, path, "op", "target", "amount", "condition");
        var op = EnumValue<SkillProgramEffectOp>(node, "op", path);
        if (op is not (SkillProgramEffectOp.Draw or SkillProgramEffectOp.Recover or
            SkillProgramEffectOp.LoseHp or SkillProgramEffectOp.GiveSelected or
            SkillProgramEffectOp.DiscardSelected))
            Fail(path + ".op", "this operation is not supported by play activations");
        var target = EnumValue<SkillProgramEffectTarget>(node, "target", path);
        var amount = PositiveInt(node, "amount", path);
        if (amount > 1024) Fail(path + ".amount", "must not exceed 1024");
        if (op == SkillProgramEffectOp.GiveSelected && target != SkillProgramEffectTarget.SelectedTarget)
            Fail(path + ".target", "giveSelected requires selectedTarget");
        if (op == SkillProgramEffectOp.DiscardSelected && target != SkillProgramEffectTarget.Owner)
            Fail(path + ".target", "discardSelected requires owner");
        var condition = OptionalCondition(node, path);
        if ((op is SkillProgramEffectOp.GiveSelected or SkillProgramEffectOp.DiscardSelected) &&
            condition.Kind != SkillProgramConditionKind.Always)
            Fail(path + ".condition", "selected-card consumption must be unconditional after activation validation");
        return new SkillProgramEffect(op, target, amount, condition);
    }

    private static SkillProgramEffect ParseCompositionEffect(JsonElement node, string path) =>
        ProgramOperationCatalog.Default.Parse(node, path,
            (condition, conditionPath) => ParseCondition(condition, conditionPath, 0));

    private static SkillProgramTriggerEffect ToCompositionTriggerEffect(SkillProgramEffect effect, string path)
    {
        if (!Enum.TryParse<SkillProgramTriggerEffectOp>(effect.Op.ToString(), out var triggerOp))
            Fail(path + ".op", "this operation consumes activation input and cannot be used by a trigger");
        var target = effect.Target == SkillProgramEffectTarget.Owner
            ? SkillProgramTriggerEffectTarget.Owner : SkillProgramTriggerEffectTarget.SelectedTarget;
        return new(triggerOp, target, effect.Amount, effect.Condition, effect.Zones, effect.Suits,
            null, [], 0, 0, effect.TargetKind, null, effect.JudgmentReason,
            phase: effect.Phase, phaseContinuation: effect.PhaseContinuation,
            numberExpression: effect.NumberExpression, minimumValue: effect.MinimumValue,
            clampToMaxHp: effect.ClampToMaxHp, sourceBind: effect.SourceBind,
            resultBind: effect.ResultBind, exceptBind: effect.ExceptBind, visibility: effect.Visibility,
            minimumCards: effect.MinimumCards, maximumCards: effect.MaximumCards,
            maximumRankSum: effect.MaximumRankSum, aiOrder: effect.AiOrder,
            destination: effect.Destination, faceDown: effect.FaceDown,
            minimumTargets: effect.MinimumTargets, maximumTargets: effect.MaximumTargets,
            targetAiOrder: effect.TargetAiOrder, cardKinds: effect.CardKinds,
            actionTypes: effect.ActionTypes, ruleQuery: effect.RuleQuery,
            ruleOperation: effect.RuleOperation, targetRestriction: effect.TargetRestriction,
            colorRelation: effect.ColorRelation, outputKind: effect.OutputKind, chained: effect.Chained,
            chooserRef: effect.ChooserRef, cardOwnerRef: effect.CardOwnerRef,
            stateId: effect.StateId, booleanValue: effect.BooleanValue,
            opponentReference: effect.OpponentReference, actorReference: effect.ActorReference,
            targetReference: effect.TargetReference, directedPolicyEffects: effect.DirectedPolicyEffects);
    }

    private static SkillProgramTrigger ParseTrigger(JsonElement node, string path, int schemaVersion)
    {
        RequireObject(node, path);
        CheckProperties(node, path, schemaVersion switch
        {
            2 => ["id", "window", "sourceSkillId", "sourceViewAsId", "optional", "effects"],
            < 6 => ["id",
                "window",
                "sourceSkillId",
                "sourceViewAsId",
                "subject",
                "suits",
                "minimumRank",
                "maximumRank",
                "excludedReasons",
                "optional",
                "effects"],
            < 8 => ["id",
                "window",
                "sourceSkillId",
                "sourceViewAsId",
                "cardKinds",
                "subject",
                "suits",
                "minimumRank",
                "maximumRank",
                "excludedReasons",
                "optional",
                "effects"],
            < 11 => ["id",
                "window",
                "sourceSkillId",
                "sourceViewAsId",
                "cardKinds",
                "subject",
                "suits",
                "minimumRank",
                "maximumRank",
                "excludedReasons",
                "judgmentReasons",
                "judgmentSource",
                "optional",
                "effects"],
            11 or 12 => ["id",
                "window",
                "sourceSkillId",
                "sourceViewAsId",
                "cardKinds",
                "subject",
                "suits",
                "minimumRank",
                "maximumRank",
                "excludedReasons",
                "judgmentReasons",
                "judgmentSource",
                "optional",
                "priority",
                "usageScope",
                "usageLimit",
                "effects"],
            13 or 14 or 15 or 16 => ["id",
                "window",
                "sourceSkillId",
                "sourceViewAsId",
                "cardKinds",
                "sourceZones",
                "movementOccurrence",
                "damageOccurrence",
                "subject",
                "suits",
                "minimumRank",
                "maximumRank",
                "excludedReasons",
                "judgmentReasons",
                "judgmentSource",
                "optional",
                "condition",
                "priority",
                "usageScope",
                "usageLimit",
                "effects"],
            _ => ["id",
                "window",
                "sourceSkillId",
                "sourceViewAsId",
                "cardKinds",
                "sourceZones",
                "movementOccurrence",
                "damageOccurrence",
                "subject",
                "suits",
                "minimumRank",
                "maximumRank",
                "excludedReasons",
                "judgmentReasons",
                "judgmentSource",
                "optional",
                "condition",
                "priority",
                "usageScope",
                "usageLimit",
                "drawPhaseMode",
                "choiceGroup",
                "ownerRelation",
                "effects"]
        });
        var id = Identifier(node, "id", path);
        var window = EnumValue<SkillProgramTriggerWindow>(node, "window", path);
        if (window == SkillProgramTriggerWindow.CardUseCommitted && schemaVersion < 24)
            Fail(path + ".window", "cardUseCommitted requires schema version 24");
        string? sourceSkillId = null;
        string? sourceViewAsId = null;
        SkillProgramTriggerSubject? subject = null;
        IReadOnlyList<Suit> suits = Array.Empty<Suit>();
        IReadOnlyList<string> excludedReasons = Array.Empty<string>();
        IReadOnlyList<string> judgmentReasons = Array.Empty<string>();
        SkillProgramTriggerSubject? judgmentSource = null;
        IReadOnlyList<CardKind> cardKinds = Array.Empty<CardKind>();
        IReadOnlyList<CardZoneKind> sourceZones = Array.Empty<CardZoneKind>();
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
        var isSharedCardWindow = schemaVersion >= 24 && window is (
            SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardUseBeforeTargetEffects or
            SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardResponseAccepted);
        var isLifecycleWindow = window is SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or
            SkillProgramTriggerWindow.DrawPhaseStarting or
            SkillProgramTriggerWindow.SelfDyingResponse or
            SkillProgramTriggerWindow.AfterDamageApplied or
            SkillProgramTriggerWindow.PlayEnding or
            SkillProgramTriggerWindow.TurnEnding or
            SkillProgramTriggerWindow.CardsMoved;
        var supportsTriggerCondition = isSharedCardWindow || window is SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or
            SkillProgramTriggerWindow.DrawPhaseStarting or
            SkillProgramTriggerWindow.PlayEnding or
            SkillProgramTriggerWindow.TurnEnding or
            SkillProgramTriggerWindow.CardsMoved;
        if (window != SkillProgramTriggerWindow.CardsMoved &&
            (node.TryGetProperty("sourceZones", out _) ||
             node.TryGetProperty("movementOccurrence", out _)))
            Fail(path, "sourceZones and movementOccurrence are supported only by cardsMoved");
        if (window != SkillProgramTriggerWindow.AfterDamageApplied &&
            node.TryGetProperty("damageOccurrence", out _))
            Fail(path, "damageOccurrence is supported only by afterDamageApplied");
        if (window != SkillProgramTriggerWindow.DrawPhaseStarting &&
            (node.TryGetProperty("drawPhaseMode", out _) || node.TryGetProperty("choiceGroup", out _)))
            Fail(path, "drawPhaseMode and choiceGroup are supported only by drawPhaseStarting");
        if (node.TryGetProperty("ownerRelation", out _) &&
            (schemaVersion < 24 || window is not (SkillProgramTriggerWindow.CardUseCommitted or
                SkillProgramTriggerWindow.CardUseBeforeTargetEffects or
                SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardResponseAccepted)))
            Fail(path + ".ownerRelation", "requires a schema 24 card-action window");
        if (node.TryGetProperty("drawPhaseMode", out _) && schemaVersion < 17)
            Fail(path + ".drawPhaseMode", "requires schema version 17");
        if (window == SkillProgramTriggerWindow.AfterDamageApplied && schemaVersion < 15 &&
            node.TryGetProperty("damageOccurrence", out _))
            Fail(path + ".damageOccurrence", "requires schema version 15");
        if (node.TryGetProperty("condition", out _) &&
            (schemaVersion < (window == SkillProgramTriggerWindow.CardsMoved ? 14 : 13) ||
             !supportsTriggerCondition))
            Fail(path + ".condition",
                "trigger conditions require a supported lifecycle or card-movement boundary");
        if (!isLifecycleWindow && !isSharedCardWindow &&
            (node.TryGetProperty("priority", out _) || node.TryGetProperty("usageScope", out _) ||
             node.TryGetProperty("usageLimit", out _)))
            Fail(path, "priority and usage fields are supported only by schema 11 lifecycle windows");
        if (isLifecycleWindow)
        {
            if (window == SkillProgramTriggerWindow.DrawPhaseStarting && schemaVersion < 16)
                Fail(path + ".window", "drawPhaseStarting requires schema version 16");
            if (window == SkillProgramTriggerWindow.CardsMoved && schemaVersion < 14)
                Fail(path + ".window", "cardsMoved requires schema version 14");
            if (schemaVersion < 11)
                Fail(path + ".window", $"{Camel(window)} requires schema version 11");
            if (node.TryGetProperty("sourceSkillId", out _) || node.TryGetProperty("sourceViewAsId", out _) ||
                node.TryGetProperty("cardKinds", out _) || node.TryGetProperty("suits", out _) ||
                node.TryGetProperty("minimumRank", out _) || node.TryGetProperty("maximumRank", out _) ||
                node.TryGetProperty("excludedReasons", out _) || node.TryGetProperty("judgmentReasons", out _) ||
                node.TryGetProperty("judgmentSource", out _))
                Fail(path, "lifecycle trigger windows accept subject, ordering, usage and effects only");
            subject = EnumValue<SkillProgramTriggerSubject>(node, "subject", path);
            if (subject != SkillProgramTriggerSubject.Owner)
                Fail(path + ".subject", "the initial lifecycle runtime supports only owner subjects");
            if (window == SkillProgramTriggerWindow.CardsMoved)
            {
                sourceZones = EnumArray<CardZoneKind>(node, "sourceZones", path);
                if (sourceZones.Count != 1 || sourceZones.Any(zone => zone is not
                        (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment or
                         CardZoneKind.WoodenOxGrain or CardZoneKind.BuquWound or CardZoneKind.Authority or
                         CardZoneKind.Chunlao)))
                    Fail(path + ".sourceZones",
                        "cardsMoved schema 14 requires exactly one owner-scoped source zone");
                movementOccurrence = EnumValue<SkillProgramMovementOccurrence>(
                    node, "movementOccurrence", path);
            }
            if (window == SkillProgramTriggerWindow.AfterDamageApplied && schemaVersion >= 15)
            {
                damageOccurrence = EnumValue<SkillProgramDamageOccurrence>(
                    node, "damageOccurrence", path);
            }
            if (window == SkillProgramTriggerWindow.DrawPhaseStarting && schemaVersion >= 17 &&
                node.TryGetProperty("drawPhaseMode", out _))
            {
                drawPhaseMode = EnumValue<SkillProgramDrawPhaseMode>(node, "drawPhaseMode", path);
            }
            if (node.TryGetProperty("choiceGroup", out _))
            {
                if (schemaVersion < 20)
                    Fail(path + ".choiceGroup", "requires schema version 20");
                choiceGroup = Identifier(node, "choiceGroup", path);
            }

        }
        else if (window == SkillProgramTriggerWindow.JudgmentFinalized)
        {
            if (schemaVersion < 3) Fail(path + ".window", "judgmentFinalized requires schema version 3");
            if (node.TryGetProperty("sourceSkillId", out _) || node.TryGetProperty("sourceViewAsId", out _) ||
                node.TryGetProperty("cardKinds", out _))
                Fail(path, "judgmentFinalized does not accept card-conversion source fields");
            subject = EnumValue<SkillProgramTriggerSubject>(node, "subject", path);
            if (schemaVersion < 8 && subject != SkillProgramTriggerSubject.Owner)
                Fail(path + ".subject", "judgmentFinalized currently supports only owner judgments");
            suits = EnumArray<Suit>(node, "suits", path);
            if (suits.Count == 0) Fail(path + ".suits", "must contain at least one final suit");
            minimumRank = RequiredInt(node, "minimumRank", path);
            maximumRank = RequiredInt(node, "maximumRank", path);
            if (minimumRank is < 1 or > 13 || maximumRank is < 1 or > 13 || minimumRank > maximumRank)
                Fail(path, "judgment rank bounds must satisfy 1 <= minimumRank <= maximumRank <= 13");
            excludedReasons = StringArray(node, "excludedReasons", path);
            if (schemaVersion >= 8)
            {
                judgmentReasons = node.TryGetProperty("judgmentReasons", out _)
                    ? StringArray(node, "judgmentReasons", path)
                    : Array.Empty<string>();
                judgmentSource = node.TryGetProperty("judgmentSource", out _)
                    ? EnumValue<SkillProgramTriggerSubject>(node, "judgmentSource", path)
                    : null;
                if (judgmentReasons.Intersect(excludedReasons, StringComparer.Ordinal).Any())
                    Fail(path, "judgmentReasons and excludedReasons must not overlap");
            }
        }
        else if (window == SkillProgramTriggerWindow.JudgmentReplacing)
        {
            if (schemaVersion < 4) Fail(path + ".window", "judgmentReplacing requires schema version 4");
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
            if (schemaVersion >= 24)
            {
                ownerRelation = EnumValue<SkillProgramCardActionOwnerRelation>(node, "ownerRelation", path);
                if (node.TryGetProperty("sourceSkillId", out _) || node.TryGetProperty("sourceViewAsId", out _))
                    Fail(path, "schema 24 card-action composition selects owners by ownerRelation");
            }
            if (node.TryGetProperty("subject", out _) || node.TryGetProperty("suits", out _) ||
                node.TryGetProperty("minimumRank", out _) || node.TryGetProperty("maximumRank", out _) ||
                node.TryGetProperty("excludedReasons", out _) || node.TryGetProperty("judgmentReasons", out _) ||
                node.TryGetProperty("judgmentSource", out _))
                Fail(path, "card-action trigger windows do not accept judgment fields");
            if (schemaVersion >= 6 && node.TryGetProperty("cardKinds", out _))
            {
                if (node.TryGetProperty("sourceSkillId", out _) || node.TryGetProperty("sourceViewAsId", out _))
                    Fail(path, "cardKinds cannot be combined with card-conversion source fields");
                cardKinds = EnumArray<CardKind>(node, "cardKinds", path);
                if (cardKinds.Count == 0) Fail(path + ".cardKinds", "must contain at least one effective card kind");
                var supported = window is SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardUseBeforeTargetEffects or SkillProgramTriggerWindow.CardUseTargetsFinalized
                    ? cardKinds.All(kind => kind is CardKind.Slash or CardKind.FireSlash or
                        CardKind.ThunderSlash or CardKind.Lightning)
                    : cardKinds.All(kind => kind is CardKind.Slash or CardKind.FireSlash or
                        CardKind.ThunderSlash or CardKind.Dodge);
                if (!supported)
                    Fail(path + ".cardKinds", $"contains a card kind unsupported by {Camel(window)}");
            }
            else
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
        }
        if (isLifecycleWindow || isSharedCardWindow)
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
                        "event usage scope is not supported by schema 11 lifecycle triggers");
                usageLimit = PositiveInt(node, "usageLimit", path);
                if (usageLimit > 1024) Fail(path + ".usageLimit", "must not exceed 1024");
            }
        }
        var optional = RequiredBool(node, "optional", path);
        var condition = schemaVersion >= 13
            ? OptionalTriggerCondition(node, path)
            : AlwaysTrigger;
        if (EnumerateTriggerConditions(condition).Any(item => item.Kind is
                SkillProgramTriggerConditionKind.CardActionActorIsCurrentTurn or
                SkillProgramTriggerConditionKind.CardActionPhaseIsPlay) &&
            (schemaVersion < 24 || window is not (SkillProgramTriggerWindow.CardUseCommitted or
                SkillProgramTriggerWindow.CardUseBeforeTargetEffects or
                SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardResponseAccepted)))
            Fail(path + ".condition", "card-action facts require a schema 24 card-action trigger");
        if (window != SkillProgramTriggerWindow.CardsMoved &&
            EnumerateTriggerValues(condition).Any(value => value.Kind is
                SkillProgramTriggerValueKind.MovedCardCount or
                SkillProgramTriggerValueKind.SourceZoneCountBefore or
                SkillProgramTriggerValueKind.SourceZoneCountAfter))
            Fail(path + ".condition", "card-movement values are supported only by cardsMoved");
        if (schemaVersion < 18 && EnumerateTriggerValues(condition).Any(value =>
                value.Kind == SkillProgramTriggerValueKind.CurrentMaxHp))
            Fail(path + ".condition", "currentMaxHp requires schema version 18");
        var effects = ReadArray(node, "effects", path,
            (effect, effectPath) => ParseTriggerEffect(effect, effectPath, window, schemaVersion));
        if (effects.Count == 0) Fail(path + ".effects", "must contain at least one effect");
        var cardActionWindow = window is SkillProgramTriggerWindow.CardUseCommitted or
            SkillProgramTriggerWindow.CardUseBeforeTargetEffects or
            SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardResponseAccepted;
        if (effects.Any(effect => ContainsCardUseColorCondition(effect.Condition)) &&
            (schemaVersion < 24 || !cardActionWindow))
            Fail(path + ".effects", "cardUseIsRed requires a schema 24 card-action trigger");
        if (schemaVersion >= 24 && ownerRelation is not SkillProgramCardActionOwnerRelation.Target &&
            effects.Select(effect => effect.ToExecutionEffect()).SelectMany(EnumerateParticipantReferences)
                .Any(reference => reference.Kind == ProgramParticipantRef.EventTarget))
            Fail(path + ".effects", "eventTarget requires an ownerRelation target card-action trigger");
        if (schemaVersion >= 23)
            ProgramCompositionValidator.Validate(path, effects.Select(effect => effect.ToExecutionEffect()).ToArray(), window: window,
                drawPhaseMode: drawPhaseMode);
        else if (window == SkillProgramTriggerWindow.JudgmentReplacing &&
            (effects[0].Op != SkillProgramTriggerEffectOp.ReplaceJudgment ||
             effects.Skip(1).Any(effect => effect.Op is not
                 (SkillProgramTriggerEffectOp.Draw or SkillProgramTriggerEffectOp.Recover))))
            Fail(path + ".effects",
                "judgmentReplacing requires replaceJudgment first, followed only by draw or recover effects");
        if (schemaVersion < 23 && window == SkillProgramTriggerWindow.JudgmentFinalized)
            ValidateFinalJudgmentEffects(path, schemaVersion, effects);
        else if (schemaVersion < 23 && window is (SkillProgramTriggerWindow.CardUseTargetsFinalized or
                 SkillProgramTriggerWindow.CardResponseAccepted))
            ValidateCardActionEffects(path, schemaVersion, effects);
        else if (schemaVersion < 23 && window is (SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or
                 SkillProgramTriggerWindow.DrawPhaseStarting or
                 SkillProgramTriggerWindow.SelfDyingResponse or
                 SkillProgramTriggerWindow.AfterDamageApplied or
                 SkillProgramTriggerWindow.PlayEnding or
                 SkillProgramTriggerWindow.TurnEnding or
                 SkillProgramTriggerWindow.CardsMoved))
            ValidateLifecycleEffects(path, window, effects);
        if (schemaVersion < 23 && window == SkillProgramTriggerWindow.CardsMoved &&
            effects.Any(effect => effect.Op != SkillProgramTriggerEffectOp.Draw ||
                                  effect.Target != SkillProgramTriggerEffectTarget.Owner))
            Fail(path + ".effects", "the initial cardsMoved window supports only owner draw effects");
        if (schemaVersion < 23 && window == SkillProgramTriggerWindow.DrawPhaseStarting)
        {
            if (schemaVersion >= 19)
            {
                DrawPhaseProgramValidator.Validate(path, drawPhaseMode, effects);
            }
            else
            {
                if (drawPhaseMode == SkillProgramDrawPhaseMode.Additive &&
                    effects.Any(effect => effect is not
                        {
                            Op: SkillProgramTriggerEffectOp.Draw,
                            Target: SkillProgramTriggerEffectTarget.Owner,
                            NumberExpression: null,
                            ResultBind: null
                        }))
                    Fail(path + ".effects",
                        "additive drawPhaseStarting supports only fixed owner draws before schema 19");
                if (drawPhaseMode == SkillProgramDrawPhaseMode.Replacement &&
                    (schemaVersion < 17 ||
                     !IsRandomHandReplacementPlan(effects) &&
                     !(schemaVersion >= 18 && IsBoundCardPartitionReplacementPlan(effects))))
                    Fail(path + ".effects",
                        "replacement draw plans require either target-hand transfer or a bounded reveal/filter/move/recover partition before schema 19");
            }
        }
        return new SkillProgramTrigger(id, window, sourceSkillId, sourceViewAsId, subject, suits,
            minimumRank, maximumRank, excludedReasons, judgmentReasons, judgmentSource,
            cardKinds, sourceZones, movementOccurrence, damageOccurrence, drawPhaseMode, optional, condition, effects, priority,
            usageScope, usageLimit, choiceGroup, ownerRelation,
            usesSharedExecutor: ProgramEntryCapabilities.UsesSharedExecutor(window) &&
                (window is not (SkillProgramTriggerWindow.CardUseCommitted or
                    SkillProgramTriggerWindow.CardUseBeforeTargetEffects or
                    SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardResponseAccepted) ||
                 schemaVersion >= 24));
    }

    private static void ValidateTriggerChoiceGroups(
        string path,
        IReadOnlyList<SkillProgramTrigger> triggers,
        int schemaVersion)
    {
        foreach (var group in triggers
                     .Where(trigger => trigger.ChoiceGroup is not null)
                     .GroupBy(trigger => trigger.ChoiceGroup!, StringComparer.Ordinal))
        {
            if (schemaVersion < 20)
                Fail(path + ".triggers", "choice groups require schema version 20");
            var members = group.ToArray();
            if (members.Length < 2)
                Fail(path + ".triggers", $"choice group '{group.Key}' requires at least two branches");
            if (members.Any(trigger => trigger.Window != SkillProgramTriggerWindow.DrawPhaseStarting ||
                                       trigger.DrawPhaseMode != SkillProgramDrawPhaseMode.Additive ||
                                       !trigger.Optional || trigger.UsageScope is not null ||
                                       trigger.UsageLimit is not null ||
                                       trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always))
            {
                Fail(path + ".triggers",
                    $"choice group '{group.Key}' requires optional unconditional additive draw-phase branches without usage fields");
            }
            if (members.Select(trigger => trigger.Priority).Distinct().Count() != 1)
                Fail(path + ".triggers", $"choice group '{group.Key}' requires one shared priority");
        }
    }

    private static SkillProgramTriggerEffect ParseTriggerEffect(
        JsonElement node, string path, SkillProgramTriggerWindow window, int schemaVersion)
    {
        if (schemaVersion >= 23)
        {
            if (schemaVersion < 24 && window is (SkillProgramTriggerWindow.CardUseCommitted or
                    SkillProgramTriggerWindow.CardUseBeforeTargetEffects or
                SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardResponseAccepted))
                Fail(path, "card-action composition requires schema version 24");
            if (!ProgramEntryCapabilities.UsesSharedExecutor(window))
                Fail(path, "this window requires a typed adapter to the shared program frame before composing effects");
            return ToCompositionTriggerEffect(ParseCompositionEffect(node, path), path);
        }
        RequireObject(node, path);
        CheckProperties(node, path, schemaVersion switch
        {
            < 4 => ["op", "target", "amount", "condition"],
            4 => ["op",
                "target",
                "amount",
                "condition",
                "zones",
                "suits",
                "oldCardDestination",
                "replacementSuits",
                "minimumReplacementRank",
                "maximumReplacementRank"],
            5 => ["op",
                "target",
                "amount",
                "condition",
                "zones",
                "suits",
                "oldCardDestination",
                "replacementSuits",
                "minimumReplacementRank",
                "maximumReplacementRank",
                "targetKind",
                "nature"],
            < 11 => ["op",
                "target",
                "amount",
                "condition",
                "zones",
                "suits",
                "oldCardDestination",
                "replacementSuits",
                "minimumReplacementRank",
                "maximumReplacementRank",
                "targetKind",
                "nature",
                "judgmentReason"],
            11 or 12 => ["op",
                "target",
                "amount",
                "condition",
                "zones",
                "suits",
                "oldCardDestination",
                "replacementSuits",
                "minimumReplacementRank",
                "maximumReplacementRank",
                "targetKind",
                "nature",
                "judgmentReason",
                "phase",
                "phaseContinuation",
                "numberExpression",
                "minimumValue",
                "clampToMaxHp",
                "sourceBind",
                "resultBind",
                "exceptBind",
                "visibility",
                "minimumCards",
                "maximumCards",
                "maximumRankSum",
                "aiOrder",
                "destination"],
            13 or 14 or 15 or 16 => ["op",
                "target",
                "amount",
                "condition",
                "zones",
                "suits",
                "oldCardDestination",
                "replacementSuits",
                "minimumReplacementRank",
                "maximumReplacementRank",
                "targetKind",
                "nature",
                "judgmentReason",
                "phase",
                "phaseContinuation",
                "numberExpression",
                "minimumValue",
                "clampToMaxHp",
                "sourceBind",
                "resultBind",
                "exceptBind",
                "visibility",
                "minimumCards",
                "maximumCards",
                "maximumRankSum",
                "aiOrder",
                "destination",
                "faceDown"],
            17 or 18 => ["op",
                "target",
                "amount",
                "condition",
                "zones",
                "suits",
                "oldCardDestination",
                "replacementSuits",
                "minimumReplacementRank",
                "maximumReplacementRank",
                "targetKind",
                "nature",
                "judgmentReason",
                "phase",
                "phaseContinuation",
                "numberExpression",
                "minimumValue",
                "clampToMaxHp",
                "sourceBind",
                "resultBind",
                "exceptBind",
                "visibility",
                "minimumCards",
                "maximumCards",
                "maximumRankSum",
                "aiOrder",
                "destination",
                "faceDown",
                "minimumTargets",
                "maximumTargets",
                "targetAiOrder"],
            _ => ["op",
                "target",
                "amount",
                "condition",
                "zones",
                "suits",
                "cardKinds",
                "oldCardDestination",
                "replacementSuits",
                "minimumReplacementRank",
                "maximumReplacementRank",
                "targetKind",
                "nature",
                "judgmentReason",
                "phase",
                "phaseContinuation",
                "numberExpression",
                "minimumValue",
                "clampToMaxHp",
                "sourceBind",
                "resultBind",
                "exceptBind",
                "visibility",
                "minimumCards",
                "maximumCards",
                "maximumRankSum",
                "aiOrder",
                "destination",
                "faceDown",
                "minimumTargets",
                "maximumTargets",
                "targetAiOrder",
                "actionTypes",
                "ruleQuery",
                "ruleOperation",
                "targetRestriction",
                "colorRelation",
                "outputKind",
                "chained"]
        });
        var op = EnumValue<SkillProgramTriggerEffectOp>(node, "op", path);
        var target = EnumValue<SkillProgramTriggerEffectTarget>(node, "target", path);
        var zones = (IReadOnlyList<CardZoneKind>)Array.Empty<CardZoneKind>();
        var suits = (IReadOnlyList<Suit>)Array.Empty<Suit>();
        var effectCardKinds = (IReadOnlyList<CardKind>)Array.Empty<CardKind>();
        SkillProgramOldJudgmentCardDestination? oldCardDestination = null;
        var replacementSuits = (IReadOnlyList<Suit>)Array.Empty<Suit>();
        var minimumReplacementRank = 1;
        var maximumReplacementRank = 13;
        SkillProgramTargetKind? targetKind = null;
        DamageNature? damageNature = null;
        string? judgmentReason = null;
        TurnPhase? phase = null;
        SkillProgramPhaseContinuation? phaseContinuation = null;
        SkillProgramNumberExpression? numberExpression = null;
        var minimumValue = 0;
        var clampToMaxHp = false;
        string? sourceBind = null;
        string? resultBind = null;
        string? exceptBind = null;
        var visibility = SkillProgramCardSetVisibility.Private;
        var minimumCards = 0;
        var maximumCards = 0;
        var maximumRankSum = 0;
        SkillProgramSubsetAiOrder? aiOrder = null;
        SkillProgramCardDestination? destination = null;
        bool? faceDown = null;
        var minimumTargets = 0;
        var maximumTargets = 0;
        SkillProgramTargetAiOrder? targetAiOrder = null;
        IReadOnlyList<CardActionType> actionTypes = Array.Empty<CardActionType>();
        SkillRuleQuery? ruleQuery = null;
        SkillRuleOperation? ruleOperation = null;
        SkillProgramCardTargetRestriction? targetRestriction = null;
        SkillProgramCardColorRelation? colorRelation = null;
        CardKind? outputKind = null;
        bool? chained = null;
        var amount = 0;
        var isLifecycleWindow = window is SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or
            SkillProgramTriggerWindow.DrawPhaseStarting or
            SkillProgramTriggerWindow.SelfDyingResponse or SkillProgramTriggerWindow.AfterDamageApplied or
            SkillProgramTriggerWindow.PlayEnding or SkillProgramTriggerWindow.TurnEnding;
        if (!isLifecycleWindow && new[]
            {
                "phase", "phaseContinuation", "numberExpression", "minimumValue", "clampToMaxHp",
                "sourceBind", "resultBind", "exceptBind", "visibility", "minimumCards", "maximumCards",
                "maximumRankSum", "aiOrder", "destination", "faceDown", "minimumTargets",
                "maximumTargets", "targetAiOrder", "cardKinds", "actionTypes", "ruleQuery",
                "ruleOperation", "targetRestriction", "colorRelation", "outputKind", "chained"
            }.Any(name => node.TryGetProperty(name, out _)))
            Fail(path, "lifecycle operation fields are not supported by this trigger window");
        if (isLifecycleWindow)
        {
            if (target != SkillProgramTriggerEffectTarget.Owner &&
                !(schemaVersion >= 15 && window == SkillProgramTriggerWindow.AfterDamageApplied &&
                  target == SkillProgramTriggerEffectTarget.SelectedTarget))
                Fail(path + ".target",
                    "lifecycle effects require owner, except schema 15 afterDamageApplied selected targets");
            switch (op)
            {
                case SkillProgramTriggerEffectOp.Draw:
                    if (schemaVersion >= 20 && window == SkillProgramTriggerWindow.DrawPhaseStarting &&
                        node.TryGetProperty("numberExpression", out _))
                    {
                        if (node.TryGetProperty("amount", out _))
                            Fail(path, "draw accepts amount or numberExpression, not both");
                        numberExpression = EnumValue<SkillProgramNumberExpression>(node, "numberExpression", path);
                        if (numberExpression != SkillProgramNumberExpression.LivingFactionCount)
                            Fail(path + ".numberExpression",
                                "schema 20 drawPhaseStarting draw supports livingFactionCount");
                    }
                    else if (schemaVersion >= 15 && window == SkillProgramTriggerWindow.AfterDamageApplied &&
                        node.TryGetProperty("numberExpression", out _))
                    {
                        if (node.TryGetProperty("amount", out _))
                            Fail(path, "draw accepts amount or numberExpression, not both");
                        numberExpression = EnumValue<SkillProgramNumberExpression>(node, "numberExpression", path);
                        if (numberExpression != SkillProgramNumberExpression.TargetMaxHpMinusHandCount)
                            Fail(path + ".numberExpression",
                                "afterDamageApplied draw supports targetMaxHpMinusHandCount");
                        if (target != SkillProgramTriggerEffectTarget.SelectedTarget)
                            Fail(path + ".target",
                                "targetMaxHpMinusHandCount requires the previously selected target");
                    }
                    else
                    {
                        amount = PositiveInt(node, "amount", path);
                        if (amount > 20) Fail(path + ".amount", "must be between 1 and 20");
                    }
                    resultBind = node.TryGetProperty("resultBind", out _)
                        ? Identifier(node, "resultBind", path)
                        : null;
                    if (resultBind is not null &&
                        (schemaVersion < 15 || window != SkillProgramTriggerWindow.AfterDamageApplied ||
                         target != SkillProgramTriggerEffectTarget.Owner || numberExpression is not null))
                        Fail(path + ".resultBind",
                            "only fixed owner draws in schema 15 afterDamageApplied may bind drawn cards");
                    RejectLifecycleFields(node, path,
                        node.TryGetProperty("numberExpression", out _)
                            ? ["numberExpression", "resultBind"]
                            : ["amount", "resultBind"]);
                    break;
                case SkillProgramTriggerEffectOp.Recover:
                    if (schemaVersion >= 18 && window == SkillProgramTriggerWindow.DrawPhaseStarting &&
                        node.TryGetProperty("numberExpression", out _))
                    {
                        if (node.TryGetProperty("amount", out _))
                            Fail(path, "recover accepts amount or numberExpression, not both");
                        numberExpression = EnumValue<SkillProgramNumberExpression>(node, "numberExpression", path);
                        if (numberExpression != SkillProgramNumberExpression.BoundCardCount)
                            Fail(path + ".numberExpression",
                                "drawPhaseStarting recover supports boundCardCount");
                        sourceBind = Identifier(node, "sourceBind", path);
                        RejectLifecycleFields(node, path, "numberExpression", "sourceBind");
                    }
                    else
                    {
                        amount = PositiveInt(node, "amount", path);
                        if (amount > 20) Fail(path + ".amount", "must be between 1 and 20");
                        RejectLifecycleFields(node, path, "amount");
                    }
                    break;
                case SkillProgramTriggerEffectOp.LoseHp:
                    amount = PositiveInt(node, "amount", path);
                    if (amount > 20) Fail(path + ".amount", "must be between 1 and 20");
                    RejectLifecycleFields(node, path, "amount");
                    break;
                case SkillProgramTriggerEffectOp.InsertPhase:
                    if (window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)
                        Fail(path + ".op",
                            "insertPhase currently requires the clean turnStartBeforeNormalFlow scheduler boundary");
                    phase = EnumValue<TurnPhase>(node, "phase", path);
                    if (phase != TurnPhase.Play) Fail(path + ".phase", "the initial phase scheduler supports only play");
                    phaseContinuation = EnumValue<SkillProgramPhaseContinuation>(node, "phaseContinuation", path);
                    RejectLifecycleFields(node, path, "phase", "phaseContinuation");
                    break;
                case SkillProgramTriggerEffectOp.RecoverTo:
                    numberExpression = EnumValue<SkillProgramNumberExpression>(node, "numberExpression", path);
                    minimumValue = RequiredInt(node, "minimumValue", path);
                    if (minimumValue < 0) Fail(path + ".minimumValue", "must be non-negative");
                    if (numberExpression == SkillProgramNumberExpression.IntegerConstant &&
                        (schemaVersion < 22 || window != SkillProgramTriggerWindow.SelfDyingResponse ||
                         minimumValue is < 1 or > 20))
                        Fail(path,
                            "integerConstant recoverTo requires schema 22 selfDyingResponse and a value between 1 and 20");
                    clampToMaxHp = RequiredBool(node, "clampToMaxHp", path);
                    RejectLifecycleFields(node, path, "numberExpression", "minimumValue", "clampToMaxHp");
                    break;
                case SkillProgramTriggerEffectOp.TurnOver:
                    RejectLifecycleFields(node, path);
                    break;
                case SkillProgramTriggerEffectOp.SetFaceState:
                    if (window != SkillProgramTriggerWindow.TurnEnding)
                        Fail(path + ".op", "setFaceState is supported only at the turnEnding boundary");
                    faceDown = RequiredBool(node, "faceDown", path);
                    RejectLifecycleFields(node, path, "faceDown");
                    break;
                case SkillProgramTriggerEffectOp.RevealTopCards:
                    if (schemaVersion >= 18 && window == SkillProgramTriggerWindow.DrawPhaseStarting &&
                        node.TryGetProperty("numberExpression", out _))
                    {
                        if (node.TryGetProperty("amount", out _))
                            Fail(path, "revealTopCards accepts amount or numberExpression, not both");
                        numberExpression = EnumValue<SkillProgramNumberExpression>(node, "numberExpression", path);
                        if (numberExpression != SkillProgramNumberExpression.OwnerLostHp)
                            Fail(path + ".numberExpression",
                                "drawPhaseStarting revealTopCards supports ownerLostHp");
                    }
                    else
                    {
                        amount = PositiveInt(node, "amount", path);
                        if (amount > 16) Fail(path + ".amount", "must not exceed 16");
                    }
                    resultBind = Identifier(node, "resultBind", path);
                    visibility = EnumValue<SkillProgramCardSetVisibility>(node, "visibility", path);
                    RejectLifecycleFields(node, path,
                        node.TryGetProperty("numberExpression", out _)
                            ? ["numberExpression", "resultBind", "visibility"]
                            : ["amount", "resultBind", "visibility"]);
                    break;
                case SkillProgramTriggerEffectOp.FilterBoundCards:
                    if (schemaVersion < 18 || window != SkillProgramTriggerWindow.DrawPhaseStarting ||
                        target != SkillProgramTriggerEffectTarget.Owner)
                        Fail(path + ".op",
                            "filterBoundCards requires schema 18 drawPhaseStarting and owner");
                    sourceBind = Identifier(node, "sourceBind", path);
                    resultBind = Identifier(node, "resultBind", path);
                    if (sourceBind == resultBind) Fail(path, "sourceBind and resultBind must differ");
                    suits = EnumArray<Suit>(node, "suits", path);
                    if (suits.Count == 0 || suits.Distinct().Count() != suits.Count)
                        Fail(path + ".suits", "must contain distinct suits");
                    RejectLifecycleFields(node, path, "sourceBind", "resultBind", "suits");
                    break;
                case SkillProgramTriggerEffectOp.SelectCardSubset:
                    sourceBind = Identifier(node, "sourceBind", path);
                    resultBind = Identifier(node, "resultBind", path);
                    if (sourceBind == resultBind) Fail(path, "sourceBind and resultBind must differ");
                    minimumCards = RequiredInt(node, "minimumCards", path);
                    maximumCards = RequiredInt(node, "maximumCards", path);
                    maximumRankSum = RequiredInt(node, "maximumRankSum", path);
                    if (minimumCards < 0 || maximumCards < minimumCards ||
                        maximumCards > CardSubsetSelector.MaximumCandidateCount)
                        Fail(path,
                            $"card-count bounds must satisfy 0 <= minimumCards <= maximumCards <= {CardSubsetSelector.MaximumCandidateCount}");
                    if (maximumRankSum is < 1 or > 208)
                        Fail(path + ".maximumRankSum", "must be between 1 and 208");
                    aiOrder = EnumValue<SkillProgramSubsetAiOrder>(node, "aiOrder", path);
                    RejectLifecycleFields(node, path, "sourceBind", "resultBind", "minimumCards",
                        "maximumCards", "maximumRankSum", "aiOrder");
                    break;
                case SkillProgramTriggerEffectOp.MoveBoundCards:
                    sourceBind = Identifier(node, "sourceBind", path);
                    exceptBind = node.TryGetProperty("exceptBind", out _)
                        ? Identifier(node, "exceptBind", path)
                        : null;
                    if (sourceBind == exceptBind) Fail(path, "exceptBind must differ from sourceBind");
                    destination = EnumValue<SkillProgramCardDestination>(node, "destination", path);
                    RejectLifecycleFields(node, path, "sourceBind", "exceptBind", "destination");
                    break;
                case SkillProgramTriggerEffectOp.SelectTarget:
                    if (schemaVersion < 15 || window != SkillProgramTriggerWindow.AfterDamageApplied ||
                        target != SkillProgramTriggerEffectTarget.SelectedTarget)
                        Fail(path + ".op",
                            "lifecycle selectTarget requires schema 15 afterDamageApplied and selectedTarget");
                    targetKind = EnumValue<SkillProgramTargetKind>(node, "targetKind", path);
                    if (OptionalCondition(node, path).Kind != SkillProgramConditionKind.Always)
                        Fail(path + ".condition", "selectTarget must remain unconditional after trigger activation");
                    RejectLifecycleFields(node, path, "targetKind");
                    break;
                case SkillProgramTriggerEffectOp.SelectTargets:
                    if (schemaVersion < 17 || window != SkillProgramTriggerWindow.DrawPhaseStarting ||
                        target != SkillProgramTriggerEffectTarget.Owner)
                        Fail(path + ".op",
                            "selectTargets requires schema 17 drawPhaseStarting and owner");
                    targetKind = EnumValue<SkillProgramTargetKind>(node, "targetKind", path);
                    if (targetKind != SkillProgramTargetKind.OtherLivingWithHand)
                        Fail(path + ".targetKind",
                            "the initial draw replacement selector requires otherLivingWithHand");
                    minimumTargets = RequiredInt(node, "minimumTargets", path);
                    maximumTargets = RequiredInt(node, "maximumTargets", path);
                    if (minimumTargets < 1 || maximumTargets < minimumTargets || maximumTargets > 2)
                        Fail(path,
                            "target-count bounds must satisfy 1 <= minimumTargets <= maximumTargets <= 2");
                    targetAiOrder = EnumValue<SkillProgramTargetAiOrder>(node, "targetAiOrder", path);
                    if (OptionalCondition(node, path).Kind != SkillProgramConditionKind.Always)
                        Fail(path + ".condition", "selectTargets must remain unconditional after trigger activation");
                    RejectLifecycleFields(node, path, "targetKind", "minimumTargets", "maximumTargets",
                        "targetAiOrder");
                    break;
                case SkillProgramTriggerEffectOp.SelectSourceCard:
                    if (schemaVersion < 15 || window != SkillProgramTriggerWindow.AfterDamageApplied ||
                        target != SkillProgramTriggerEffectTarget.Owner)
                        Fail(path + ".op", "selectSourceCard requires schema 15 afterDamageApplied and owner");
                    zones = EnumArray<CardZoneKind>(node, "zones", path);
                    if (zones.Count == 0 || zones.Any(zone => zone is not
                            (CardZoneKind.Hand or CardZoneKind.Equipment)))
                        Fail(path + ".zones", "selectSourceCard requires hand and/or equipment zones");
                    resultBind = Identifier(node, "resultBind", path);
                    RejectLifecycleFields(node, path, "zones", "resultBind");
                    break;
                case SkillProgramTriggerEffectOp.GiveBoundCard:
                    if (schemaVersion < 15 || window != SkillProgramTriggerWindow.AfterDamageApplied ||
                        target != SkillProgramTriggerEffectTarget.Owner)
                        Fail(path + ".op", "giveBoundCard requires schema 15 afterDamageApplied and owner");
                    sourceBind = Identifier(node, "sourceBind", path);
                    targetKind = EnumValue<SkillProgramTargetKind>(node, "targetKind", path);
                    if (targetKind is not (SkillProgramTargetKind.OtherLiving or SkillProgramTargetKind.AnyLiving))
                        Fail(path + ".targetKind", "giveBoundCard supports otherLiving or anyLiving targets");
                    RejectLifecycleFields(node, path, "sourceBind", "targetKind");
                    break;
                case SkillProgramTriggerEffectOp.ClaimDamageCards:
                    if (schemaVersion < 15 || window != SkillProgramTriggerWindow.AfterDamageApplied ||
                        target != SkillProgramTriggerEffectTarget.Owner)
                        Fail(path + ".op", "claimDamageCards requires schema 15 afterDamageApplied and owner");
                    RejectLifecycleFields(node, path);
                    break;
                case SkillProgramTriggerEffectOp.TakeRandomHandCardFromSelectedTargets:
                    if (schemaVersion < 17 || window != SkillProgramTriggerWindow.DrawPhaseStarting ||
                        target != SkillProgramTriggerEffectTarget.Owner)
                        Fail(path + ".op",
                            "takeRandomHandCardFromSelectedTargets requires schema 17 drawPhaseStarting and owner");
                    amount = PositiveInt(node, "amount", path);
                    if (amount != 1)
                        Fail(path + ".amount", "the initial random-hand transfer supports exactly one card per target");
                    RejectLifecycleFields(node, path, "amount");
                    break;
                case SkillProgramTriggerEffectOp.AdjustNormalDraw:
                    if (schemaVersion < 19 || window != SkillProgramTriggerWindow.DrawPhaseStarting ||
                        target != SkillProgramTriggerEffectTarget.Owner)
                        Fail(path + ".op", "adjustNormalDraw requires schema 19 drawPhaseStarting and owner");
                    amount = RequiredInt(node, "amount", path);
                    if (amount is < -20 or > 20 || amount == 0)
                        Fail(path + ".amount", "must be a non-zero value between -20 and 20");
                    RejectLifecycleFields(node, path, "amount");
                    break;
                case SkillProgramTriggerEffectOp.GrantTurnCardDamageModifier:
                    if (schemaVersion < 19 || window != SkillProgramTriggerWindow.DrawPhaseStarting ||
                        target != SkillProgramTriggerEffectTarget.Owner)
                        Fail(path + ".op",
                            "grantTurnCardDamageModifier requires schema 19 drawPhaseStarting and owner");
                    amount = PositiveInt(node, "amount", path);
                    if (amount > 20) Fail(path + ".amount", "must be between 1 and 20");
                    effectCardKinds = EnumArray<CardKind>(node, "cardKinds", path);
                    if (effectCardKinds.Count == 0 ||
                        effectCardKinds.Distinct().Count() != effectCardKinds.Count)
                        Fail(path + ".cardKinds", "must contain distinct effective card kinds");
                    if (effectCardKinds.Any(kind => kind is not
                            (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or
                             CardKind.Duel or CardKind.BarbarianAssault or CardKind.ArrowBarrage or
                             CardKind.FireAttack)))
                        Fail(path + ".cardKinds", "contains a card kind that cannot directly cause card-use damage");
                    RejectLifecycleFields(node, path, "amount", "cardKinds");
                    break;
                case SkillProgramTriggerEffectOp.GrantTurnCardActionProhibition:
                    if (schemaVersion < 20 || window != SkillProgramTriggerWindow.DrawPhaseStarting ||
                        target != SkillProgramTriggerEffectTarget.Owner)
                        Fail(path + ".op",
                            "grantTurnCardActionProhibition requires schema 20 drawPhaseStarting and owner");
                    effectCardKinds = EnumArray<CardKind>(node, "cardKinds", path);
                    actionTypes = EnumArray<CardActionType>(node, "actionTypes", path);
                    if (effectCardKinds.Count == 0 ||
                        effectCardKinds.Distinct().Count() != effectCardKinds.Count)
                        Fail(path + ".cardKinds", "must contain distinct effective card kinds");
                    if (actionTypes.Count == 0 || actionTypes.Distinct().Count() != actionTypes.Count ||
                        actionTypes.Any(action => action is not (CardActionType.Use or CardActionType.Response)))
                        Fail(path + ".actionTypes", "must contain distinct use and/or response actions");
                    RejectLifecycleFields(node, path, "cardKinds", "actionTypes");
                    break;
                case SkillProgramTriggerEffectOp.GrantTurnRuleModifier:
                    if (schemaVersion < 20 || window != SkillProgramTriggerWindow.DrawPhaseStarting ||
                        target != SkillProgramTriggerEffectTarget.Owner)
                        Fail(path + ".op",
                            "grantTurnRuleModifier requires schema 20 drawPhaseStarting and owner");
                    ruleQuery = EnumValue<SkillRuleQuery>(node, "ruleQuery", path);
                    ruleOperation = EnumValue<SkillRuleOperation>(node, "ruleOperation", path);
                    if (ruleQuery == SkillRuleQuery.SlashLimit && ruleOperation == SkillRuleOperation.Add)
                    {
                        amount = PositiveInt(node, "amount", path);
                        if (amount > 20) Fail(path + ".amount", "must be between 1 and 20");
                        RejectLifecycleFields(node, path, "ruleQuery", "ruleOperation", "amount");
                    }
                    else if (ruleQuery == SkillRuleQuery.SlashDistanceLimit &&
                             ruleOperation == SkillRuleOperation.Unlimited)
                    {
                        RejectLifecycleFields(node, path, "ruleQuery", "ruleOperation");
                    }
                    else
                    {
                        Fail(path, "turn rule modifiers support slashLimit add or slashDistanceLimit unlimited");
                    }
                    break;
                case SkillProgramTriggerEffectOp.GrantTurnCardTargetRestriction:
                    if (schemaVersion < 20 || window != SkillProgramTriggerWindow.DrawPhaseStarting ||
                        target != SkillProgramTriggerEffectTarget.Owner)
                        Fail(path + ".op",
                            "grantTurnCardTargetRestriction requires schema 20 drawPhaseStarting and owner");
                    targetRestriction = EnumValue<SkillProgramCardTargetRestriction>(
                        node, "targetRestriction", path);
                    RejectLifecycleFields(node, path, "targetRestriction");
                    break;
                case SkillProgramTriggerEffectOp.StartJudgment:
                    if (schemaVersion < 21 || window != SkillProgramTriggerWindow.DrawPhaseStarting ||
                        target != SkillProgramTriggerEffectTarget.Owner)
                        Fail(path + ".op", "startJudgment lifecycle binding requires schema 21 drawPhaseStarting and owner");
                    judgmentReason = NonEmptyString(node, "judgmentReason", path);
                    if (judgmentReason.Length > 128)
                        Fail(path + ".judgmentReason", "must not exceed 128 characters");
                    resultBind = Identifier(node, "resultBind", path);
                    visibility = EnumValue<SkillProgramCardSetVisibility>(node, "visibility", path);
                    if (visibility != SkillProgramCardSetVisibility.Public)
                        Fail(path + ".visibility", "a lifecycle judgment result must remain public");
                    RejectLifecycleFields(node, path, "judgmentReason", "resultBind", "visibility");
                    break;
                case SkillProgramTriggerEffectOp.GrantTurnCardConversion:
                    if (schemaVersion < 21 || window != SkillProgramTriggerWindow.DrawPhaseStarting ||
                        target != SkillProgramTriggerEffectTarget.Owner)
                        Fail(path + ".op", "grantTurnCardConversion requires schema 21 drawPhaseStarting and owner");
                    sourceBind = Identifier(node, "sourceBind", path);
                    colorRelation = EnumValue<SkillProgramCardColorRelation>(node, "colorRelation", path);
                    outputKind = EnumValue<CardKind>(node, "outputKind", path);
                    if (colorRelation != SkillProgramCardColorRelation.OppositeBoundCard ||
                        outputKind != CardKind.Duel)
                        Fail(path, "the initial turn conversion supports oppositeBoundCard as duel only");
                    RejectLifecycleFields(node, path, "sourceBind", "colorRelation", "outputKind");
                    break;
                case SkillProgramTriggerEffectOp.DiscardOwnedZoneCards:
                    if (schemaVersion < 22 || window != SkillProgramTriggerWindow.SelfDyingResponse ||
                        target != SkillProgramTriggerEffectTarget.Owner)
                        Fail(path + ".op",
                            "discardOwnedZoneCards requires schema 22 selfDyingResponse and owner");
                    zones = EnumArray<CardZoneKind>(node, "zones", path);
                    if (zones.Count == 0 || zones.Distinct().Count() != zones.Count ||
                        zones.Any(zone => zone is not
                            (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment)))
                        Fail(path + ".zones",
                            "must contain distinct hand, equipment and/or judgment zones");
                    RejectLifecycleFields(node, path, "zones");
                    break;
                case SkillProgramTriggerEffectOp.SetChainedState:
                    if (schemaVersion < 22 || window != SkillProgramTriggerWindow.SelfDyingResponse ||
                        target != SkillProgramTriggerEffectTarget.Owner)
                        Fail(path + ".op", "setChainedState requires schema 22 selfDyingResponse and owner");
                    chained = RequiredBool(node, "chained", path);
                    RejectLifecycleFields(node, path, "chained");
                    break;
                default:
                    Fail(path + ".op", $"operation '{Camel(op)}' is not supported by lifecycle bindings");
                    break;
            }
        }
        else if (op == SkillProgramTriggerEffectOp.ReplaceJudgment)
        {
            if (window != SkillProgramTriggerWindow.JudgmentReplacing)
                Fail(path + ".op", "replaceJudgment is supported only in judgmentReplacing");
            if (node.TryGetProperty("amount", out _) || node.TryGetProperty("replacementSuits", out _) ||
                node.TryGetProperty("minimumReplacementRank", out _) ||
                node.TryGetProperty("maximumReplacementRank", out _))
                Fail(path, "replaceJudgment does not accept amount or replacement-result filters");
            if (target != SkillProgramTriggerEffectTarget.Owner)
                Fail(path + ".target", "replaceJudgment requires target owner");
            zones = EnumArray<CardZoneKind>(node, "zones", path);
            if (zones.Count == 0 || zones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)))
                Fail(path + ".zones", "replaceJudgment requires hand and/or equipment zones");
            suits = EnumArray<Suit>(node, "suits", path);
            if (suits.Count == 0) Fail(path + ".suits", "replaceJudgment requires at least one effective suit");
            oldCardDestination = EnumValue<SkillProgramOldJudgmentCardDestination>(
                node, "oldCardDestination", path);
        }
        else if (op == SkillProgramTriggerEffectOp.SelectTarget)
        {
            var supportedWindow = window == SkillProgramTriggerWindow.JudgmentFinalized ||
                schemaVersion >= 8 && window is
                    (SkillProgramTriggerWindow.CardUseTargetsFinalized or
                     SkillProgramTriggerWindow.CardResponseAccepted);
            if (schemaVersion < 5 || !supportedWindow)
                Fail(path + ".op", "selectTarget requires judgmentFinalized, or a schema version 8 card-action window");
            if (target != SkillProgramTriggerEffectTarget.SelectedTarget)
                Fail(path + ".target", "selectTarget requires selectedTarget");
            if (node.TryGetProperty("amount", out _) || node.TryGetProperty("nature", out _))
                Fail(path, "selectTarget accepts targetKind, not amount or nature");
            targetKind = EnumValue<SkillProgramTargetKind>(node, "targetKind", path);
            if (OptionalCondition(node, path).Kind != SkillProgramConditionKind.Always)
                Fail(path + ".condition", "selectTarget must remain unconditional after trigger activation");
        }
        else if (op == SkillProgramTriggerEffectOp.Damage)
        {
            if (schemaVersion < 5 || window != SkillProgramTriggerWindow.JudgmentFinalized)
                Fail(path + ".op", "damage requires schema version 5 and judgmentFinalized");
            if (target != SkillProgramTriggerEffectTarget.SelectedTarget &&
                (schemaVersion < 8 || target != SkillProgramTriggerEffectTarget.JudgmentSubject))
                Fail(path + ".target", "judgment damage requires selectedTarget, or schema version 8 judgmentSubject");
            if (node.TryGetProperty("targetKind", out _))
                Fail(path, "damage uses the previously selected target and does not accept targetKind");
            amount = PositiveInt(node, "amount", path);
            if (amount > 20) Fail(path + ".amount", "judgment damage amount must be between 1 and 20");
            damageNature = EnumValue<DamageNature>(node, "nature", path);
        }
        else if (op == SkillProgramTriggerEffectOp.CauseDeath)
        {
            if (schemaVersion < 9 || window != SkillProgramTriggerWindow.JudgmentFinalized)
                Fail(path + ".op", "causeDeath requires schema version 9 and judgmentFinalized");
            if (target is not (SkillProgramTriggerEffectTarget.SelectedTarget or
                SkillProgramTriggerEffectTarget.JudgmentSubject))
                Fail(path + ".target", "judgment causeDeath requires selectedTarget or judgmentSubject");
            if (node.TryGetProperty("amount", out _) || node.TryGetProperty("targetKind", out _) ||
                node.TryGetProperty("nature", out _) || node.TryGetProperty("zones", out _) ||
                node.TryGetProperty("suits", out _) || node.TryGetProperty("oldCardDestination", out _) ||
                node.TryGetProperty("replacementSuits", out _) ||
                node.TryGetProperty("minimumReplacementRank", out _) ||
                node.TryGetProperty("maximumReplacementRank", out _) ||
                node.TryGetProperty("judgmentReason", out _))
                Fail(path, "causeDeath accepts only target and condition");
        }
        else if (op == SkillProgramTriggerEffectOp.StartJudgment)
        {
            if (schemaVersion < 6 || window is not
                (SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardResponseAccepted))
                Fail(path + ".op", "startJudgment requires schema version 6 and a card-action window");
            if (target != SkillProgramTriggerEffectTarget.Owner &&
                (schemaVersion < 8 || target != SkillProgramTriggerEffectTarget.SelectedTarget))
                Fail(path + ".target", "startJudgment requires owner, or schema version 8 selectedTarget");
            if (node.TryGetProperty("amount", out _) || node.TryGetProperty("targetKind", out _) ||
                node.TryGetProperty("nature", out _) || node.TryGetProperty("zones", out _) ||
                node.TryGetProperty("suits", out _) || node.TryGetProperty("oldCardDestination", out _) ||
                node.TryGetProperty("replacementSuits", out _) ||
                node.TryGetProperty("minimumReplacementRank", out _) ||
                node.TryGetProperty("maximumReplacementRank", out _))
                Fail(path, "startJudgment accepts only target, condition and judgmentReason");
            judgmentReason = NonEmptyString(node, "judgmentReason", path);
            if (judgmentReason.Length > 128)
                Fail(path + ".judgmentReason", "must not exceed 128 characters");
        }
        else
        {
            amount = PositiveInt(node, "amount", path);
            if (node.TryGetProperty("zones", out _) || node.TryGetProperty("suits", out _) ||
                node.TryGetProperty("oldCardDestination", out _))
                Fail(path, "only replaceJudgment accepts card-selection and old-card fields");
            if (window == SkillProgramTriggerWindow.JudgmentReplacing)
            {
                if (op is not (SkillProgramTriggerEffectOp.Draw or SkillProgramTriggerEffectOp.Recover) ||
                    target != SkillProgramTriggerEffectTarget.Owner)
                    Fail(path, "judgmentReplacing follow-up effects support only owner draw or recover");
                replacementSuits = EnumArray<Suit>(node, "replacementSuits", path);
                if (replacementSuits.Count == 0)
                    Fail(path + ".replacementSuits", "must contain at least one committed replacement suit");
                minimumReplacementRank = RequiredInt(node, "minimumReplacementRank", path);
                maximumReplacementRank = RequiredInt(node, "maximumReplacementRank", path);
                if (minimumReplacementRank is < 1 or > 13 || maximumReplacementRank is < 1 or > 13 ||
                    minimumReplacementRank > maximumReplacementRank)
                    Fail(path, "replacement rank bounds must satisfy 1 <= minimum <= maximum <= 13");
            }
            else if (node.TryGetProperty("replacementSuits", out _) ||
                     node.TryGetProperty("minimumReplacementRank", out _) ||
                     node.TryGetProperty("maximumReplacementRank", out _))
                Fail(path, "replacement-result filters are supported only in judgmentReplacing");
            if (node.TryGetProperty("targetKind", out _) || node.TryGetProperty("nature", out _))
                Fail(path, "targetKind and nature are supported only by selectTarget and damage");
        }
        if (op != SkillProgramTriggerEffectOp.StartJudgment && node.TryGetProperty("judgmentReason", out _))
            Fail(path, "judgmentReason is supported only by startJudgment");
        if (target == SkillProgramTriggerEffectTarget.JudgmentSubject &&
            (op is not (SkillProgramTriggerEffectOp.Damage or SkillProgramTriggerEffectOp.CauseDeath) ||
             window != SkillProgramTriggerWindow.JudgmentFinalized))
            Fail(path + ".target", "judgmentSubject is supported only by judgmentFinalized damage or causeDeath");
        if (schemaVersion >= 8 &&
            window is SkillProgramTriggerWindow.CardUseTargetsFinalized or
                SkillProgramTriggerWindow.CardResponseAccepted &&
            target == SkillProgramTriggerEffectTarget.SelectedTarget &&
            op is not (SkillProgramTriggerEffectOp.SelectTarget or SkillProgramTriggerEffectOp.StartJudgment))
            Fail(path + ".target", "card-action selectedTarget is supported only by selectTarget and startJudgment");
        if (window == SkillProgramTriggerWindow.JudgmentFinalized &&
            op is not (SkillProgramTriggerEffectOp.Draw or SkillProgramTriggerEffectOp.Recover or
                SkillProgramTriggerEffectOp.SelectTarget or SkillProgramTriggerEffectOp.Damage or
                SkillProgramTriggerEffectOp.CauseDeath))
            Fail(path,
                "judgmentFinalized currently supports only owner draw or recover effects, or a selected-target damage sequence; schema 9 also permits causeDeath");
        if (window == SkillProgramTriggerWindow.JudgmentFinalized &&
            (op is SkillProgramTriggerEffectOp.Draw or SkillProgramTriggerEffectOp.Recover) &&
            target != SkillProgramTriggerEffectTarget.Owner)
            Fail(path, "judgmentFinalized currently supports only owner draw or recover effects");
        if (op is SkillProgramTriggerEffectOp.Draw or SkillProgramTriggerEffectOp.Recover)
        {
            if (amount > 20) Fail(path + ".amount", "draw and recover amount must be between 1 and 20");
        }
        else if (op == SkillProgramTriggerEffectOp.ObtainOpponentHandCard &&
                 (amount != 1 || target != SkillProgramTriggerEffectTarget.Owner))
            Fail(path, "obtainOpponentHandCard requires amount 1 and target owner");
        return new SkillProgramTriggerEffect(op, target, amount, OptionalCondition(node, path),
            zones, suits, oldCardDestination, replacementSuits,
            minimumReplacementRank, maximumReplacementRank, targetKind, damageNature, judgmentReason,
            phase, phaseContinuation, numberExpression, minimumValue, clampToMaxHp, sourceBind, resultBind,
            exceptBind, visibility, minimumCards, maximumCards, maximumRankSum, aiOrder, destination,
            faceDown, minimumTargets, maximumTargets, targetAiOrder, effectCardKinds, actionTypes,
            ruleQuery, ruleOperation, targetRestriction, colorRelation, outputKind, chained);
    }

    private static void RejectLifecycleFields(JsonElement node, string path, params string[] allowed)
    {
        var common = new HashSet<string>(["op", "target", "condition"], StringComparer.Ordinal);
        common.UnionWith(allowed);
        foreach (var property in node.EnumerateObject())
            if (!common.Contains(property.Name))
                Fail(path + "." + property.Name,
                    $"is not supported by lifecycle operation '{node.GetProperty("op").GetString()}'");
    }

    private static void ValidateLifecycleEffects(
        string path,
        SkillProgramTriggerWindow window,
        IReadOnlyList<SkillProgramTriggerEffect> effects)
    {
        var binds = new Dictionary<string, int>(StringComparer.Ordinal);
        var selectedTargetAvailable = false;
        foreach (var effect in effects)
        {
            if (effect.Op == SkillProgramTriggerEffectOp.InsertPhase && binds.Count > 0)
                Fail(path + ".effects",
                    "insertPhase requires a clean boundary with no previously created card-set bindings");
            if (effect.Op == SkillProgramTriggerEffectOp.RevealTopCards)
            {
                var maximumRevealCount = effect.NumberExpression == SkillProgramNumberExpression.OwnerLostHp
                    ? 16
                    : effect.Amount;
                if (!binds.TryAdd(effect.ResultBind!, maximumRevealCount))
                    Fail(path + ".effects", $"duplicate card-set binding '{effect.ResultBind}'");
                continue;
            }
            if (effect.Op == SkillProgramTriggerEffectOp.StartJudgment)
            {
                if (!binds.TryAdd(effect.ResultBind!, 1))
                    Fail(path + ".effects", $"duplicate card-set binding '{effect.ResultBind}'");
                continue;
            }
            if (effect.Op == SkillProgramTriggerEffectOp.FilterBoundCards)
            {
                if (!binds.TryGetValue(effect.SourceBind!, out var maximumSourceCount))
                    Fail(path + ".effects", $"unknown source card-set binding '{effect.SourceBind}'");
                if (!binds.TryAdd(effect.ResultBind!, maximumSourceCount))
                    Fail(path + ".effects", $"duplicate card-set binding '{effect.ResultBind}'");
                continue;
            }
            if (effect.Op == SkillProgramTriggerEffectOp.Draw && effect.ResultBind is { } drawBind)
            {
                if (!binds.TryAdd(drawBind, effect.Amount))
                    Fail(path + ".effects", $"duplicate card-set binding '{drawBind}'");
                continue;
            }
            if (effect.Op == SkillProgramTriggerEffectOp.SelectSourceCard)
            {
                if (!binds.TryAdd(effect.ResultBind!, 1))
                    Fail(path + ".effects", $"duplicate card-set binding '{effect.ResultBind}'");
                continue;
            }
            if (effect.Op == SkillProgramTriggerEffectOp.SelectCardSubset)
            {
                if (!binds.TryGetValue(effect.SourceBind!, out var maximumSourceCount))
                    Fail(path + ".effects", $"unknown source card-set binding '{effect.SourceBind}'");
                try
                {
                    CardSubsetSelector.ValidateDefinition(
                        maximumSourceCount,
                        new CardSubsetConstraint(
                            effect.MinimumCards,
                            effect.MaximumCards,
                            effect.MaximumRankSum));
                }
                catch (ArgumentException exception)
                {
                    Fail(path + ".effects", exception.Message);
                }
                if (!binds.TryAdd(effect.ResultBind!, Math.Min(maximumSourceCount, effect.MaximumCards)))
                    Fail(path + ".effects", $"duplicate card-set binding '{effect.ResultBind}'");
                continue;
            }
            if (effect.Op == SkillProgramTriggerEffectOp.MoveBoundCards)
            {
                if (!binds.ContainsKey(effect.SourceBind!))
                    Fail(path + ".effects", $"unknown source card-set binding '{effect.SourceBind}'");
                if (effect.ExceptBind is { } except && !binds.ContainsKey(except))
                    Fail(path + ".effects", $"unknown excluded card-set binding '{except}'");
            }
            if (effect.Op == SkillProgramTriggerEffectOp.GiveBoundCard &&
                !binds.ContainsKey(effect.SourceBind!))
                Fail(path + ".effects", $"unknown source card-set binding '{effect.SourceBind}'");
            if (effect.Op == SkillProgramTriggerEffectOp.GrantTurnCardConversion &&
                !binds.ContainsKey(effect.SourceBind!))
                Fail(path + ".effects", $"unknown conversion card-set binding '{effect.SourceBind}'");
            if (effect.Op == SkillProgramTriggerEffectOp.Recover &&
                effect.NumberExpression == SkillProgramNumberExpression.BoundCardCount &&
                !binds.ContainsKey(effect.SourceBind!))
                Fail(path + ".effects", $"unknown recovery card-set binding '{effect.SourceBind}'");
            if (effect.Op is SkillProgramTriggerEffectOp.SelectTarget or SkillProgramTriggerEffectOp.SelectTargets)
            {
                if (selectedTargetAvailable)
                    Fail(path + ".effects", "only one lifecycle target selection is supported");
                selectedTargetAvailable = true;
                continue;
            }
            if (effect.Op == SkillProgramTriggerEffectOp.TakeRandomHandCardFromSelectedTargets &&
                !selectedTargetAvailable)
                Fail(path + ".effects",
                    "random hand-card transfer requires selectTargets first");
            if (effect.Target == SkillProgramTriggerEffectTarget.SelectedTarget && !selectedTargetAvailable)
                Fail(path + ".effects", "selectedTarget effects require selectTarget first");
        }
    }

    private static bool IsRandomHandReplacementPlan(IReadOnlyList<SkillProgramTriggerEffect> effects) =>
        effects.Count == 2 &&
        effects[0] is
        {
            Op: SkillProgramTriggerEffectOp.SelectTargets,
            Target: SkillProgramTriggerEffectTarget.Owner,
            TargetKind: SkillProgramTargetKind.OtherLivingWithHand
        } &&
        effects[1] is
        {
            Op: SkillProgramTriggerEffectOp.TakeRandomHandCardFromSelectedTargets,
            Target: SkillProgramTriggerEffectTarget.Owner,
            Amount: 1
        };

    private static bool IsBoundCardPartitionReplacementPlan(
        IReadOnlyList<SkillProgramTriggerEffect> effects)
    {
        if (effects.Count != 5 ||
            effects[0] is not
            {
                Op: SkillProgramTriggerEffectOp.RevealTopCards,
                Target: SkillProgramTriggerEffectTarget.Owner,
                NumberExpression: SkillProgramNumberExpression.OwnerLostHp,
                Visibility: SkillProgramCardSetVisibility.Public,
                ResultBind: { } revealedBind
            } ||
            effects[1] is not
            {
                Op: SkillProgramTriggerEffectOp.FilterBoundCards,
                Target: SkillProgramTriggerEffectTarget.Owner,
                SourceBind: { } filterSource,
                ResultBind: { } filteredBind
            } ||
            effects[2] is not
            {
                Op: SkillProgramTriggerEffectOp.MoveBoundCards,
                Target: SkillProgramTriggerEffectTarget.Owner,
                SourceBind: { } discardedBind,
                ExceptBind: null,
                Destination: SkillProgramCardDestination.DiscardPile
            } ||
            effects[3] is not
            {
                Op: SkillProgramTriggerEffectOp.MoveBoundCards,
                Target: SkillProgramTriggerEffectTarget.Owner,
                SourceBind: { } gainedSource,
                ExceptBind: { } gainedExcept,
                Destination: SkillProgramCardDestination.OwnerHand
            } ||
            effects[4] is not
            {
                Op: SkillProgramTriggerEffectOp.Recover,
                Target: SkillProgramTriggerEffectTarget.Owner,
                NumberExpression: SkillProgramNumberExpression.BoundCardCount,
                SourceBind: { } recoveryBind
            })
            return false;
        return filterSource == revealedBind && discardedBind == filteredBind &&
               gainedSource == revealedBind && gainedExcept == filteredBind && recoveryBind == filteredBind;
    }

    private static void ValidateFinalJudgmentEffects(
        string path,
        int schemaVersion,
        IReadOnlyList<SkillProgramTriggerEffect> effects)
    {
        var selectionIndexes = effects
            .Select((effect, index) => (effect, index))
            .Where(item => item.effect.Op == SkillProgramTriggerEffectOp.SelectTarget)
            .Select(item => item.index)
            .ToArray();
        var selectedDamageIndexes = effects
            .Select((effect, index) => (effect, index))
            .Where(item => item.effect is
                { Op: SkillProgramTriggerEffectOp.Damage, Target: SkillProgramTriggerEffectTarget.SelectedTarget })
            .Select(item => item.index)
            .ToArray();
        var selectedDeathIndexes = effects
            .Select((effect, index) => (effect, index))
            .Where(item => item.effect is
                { Op: SkillProgramTriggerEffectOp.CauseDeath, Target: SkillProgramTriggerEffectTarget.SelectedTarget })
            .Select(item => item.index)
            .ToArray();
        var subjectDamageIndexes = effects
            .Select((effect, index) => (effect, index))
            .Where(item => item.effect is
                { Op: SkillProgramTriggerEffectOp.Damage, Target: SkillProgramTriggerEffectTarget.JudgmentSubject })
            .Select(item => item.index)
            .ToArray();
        var subjectDeathIndexes = effects
            .Select((effect, index) => (effect, index))
            .Where(item => item.effect is
                { Op: SkillProgramTriggerEffectOp.CauseDeath, Target: SkillProgramTriggerEffectTarget.JudgmentSubject })
            .Select(item => item.index)
            .ToArray();
        if (selectionIndexes.Length == 0 && selectedDamageIndexes.Length == 0 && selectedDeathIndexes.Length == 0 &&
            subjectDamageIndexes.Length == 0 && subjectDeathIndexes.Length == 0)
            return;
        if (schemaVersion < 5)
            Fail(path + ".effects", "selected-target judgment damage requires schema version 5");
        if (subjectDamageIndexes.Length > 0 && schemaVersion < 8)
            Fail(path + ".effects", "judgment-subject damage requires schema version 8");
        if ((selectedDeathIndexes.Length > 0 || subjectDeathIndexes.Length > 0) && schemaVersion < 9)
            Fail(path + ".effects", "judgment causeDeath requires schema version 9");
        if (selectionIndexes.Length == 0)
        {
            if (selectedDamageIndexes.Length > 0 || selectedDeathIndexes.Length > 0)
                Fail(path + ".effects", "selected-target judgment effects require selectTarget first");
            return;
        }
        var selectedEffectIndexes = selectedDamageIndexes.Concat(selectedDeathIndexes).ToArray();
        if (selectionIndexes.Length != 1 || selectionIndexes[0] != 0 || selectedEffectIndexes.Length == 0 ||
            selectedEffectIndexes.Any(index => index <= selectionIndexes[0]))
            Fail(path + ".effects",
                "selected-target judgment effects require exactly one selectTarget first and at least one later effect");
    }

    private static void ValidateCardActionEffects(
        string path,
        int schemaVersion,
        IReadOnlyList<SkillProgramTriggerEffect> effects)
    {
        var selectionIndexes = effects
            .Select((effect, index) => (effect, index))
            .Where(item => item.effect.Op == SkillProgramTriggerEffectOp.SelectTarget)
            .Select(item => item.index)
            .ToArray();
        var selectedJudgmentIndexes = effects
            .Select((effect, index) => (effect, index))
            .Where(item => item.effect is
                { Op: SkillProgramTriggerEffectOp.StartJudgment, Target: SkillProgramTriggerEffectTarget.SelectedTarget })
            .Select(item => item.index)
            .ToArray();
        if (selectionIndexes.Length == 0 && selectedJudgmentIndexes.Length == 0) return;
        if (schemaVersion < 8)
            Fail(path + ".effects", "selected-subject card judgments require schema version 8");
        if (selectionIndexes.Length != 1 || selectionIndexes[0] != 0 || selectedJudgmentIndexes.Length == 0 ||
            selectedJudgmentIndexes.Any(index => index <= selectionIndexes[0]))
            Fail(path + ".effects",
                "selected-subject card judgments require exactly one selectTarget first and a later startJudgment");
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
                    SkillProgramTriggerWindow.SelfDyingResponse or
                    SkillProgramTriggerWindow.AfterDamageApplied or
                    SkillProgramTriggerWindow.PlayEnding or
                    SkillProgramTriggerWindow.TurnEnding or
                    SkillProgramTriggerWindow.CardsMoved) continue;
                if (trigger.CardKinds.Count > 0) continue;
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

    private static void ValidateActivation(string path, int minCards, int maxCards, int minTargets, int maxTargets,
        IReadOnlyList<SkillProgramEffect> effects)
    {
        if (effects.Any(effect => effect.Target == SkillProgramEffectTarget.SelectedTarget) && minTargets != 1)
            Fail(path, "selectedTarget effects require exactly one required target");
        if (maxTargets == 0 && effects.Any(effect => effect.Target == SkillProgramEffectTarget.SelectedTarget))
            Fail(path, "an effect uses selectedTarget but the activation selects no target");
        var consumers = effects.Where(effect => effect.Op is SkillProgramEffectOp.GiveSelected or SkillProgramEffectOp.DiscardSelected).ToArray();
        if (consumers.Length > 1) Fail(path + ".effects", "selected cards may be consumed only once");
        if (maxCards == 0 && consumers.Length != 0) Fail(path, "selected-card effect requires selected cards");
        if (maxCards > 0 && consumers.Length != 1) Fail(path, "selected cards require exactly one giveSelected or discardSelected effect");
        if (consumers.Length == 1 && (minCards != maxCards || consumers[0].Amount != minCards))
            Fail(path, "selected-card consumption amount must equal the exact selected card count");
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
        CheckProperties(node, path, "kind", "children", "left", "operator", "right", "stateId", "expectedValue");
        var kind = EnumValue<SkillProgramTriggerConditionKind>(node, "kind", path);
        var hasChildren = node.TryGetProperty("children", out var childrenNode);
        var hasLeft = node.TryGetProperty("left", out var leftNode);
        var hasOperator = node.TryGetProperty("operator", out _);
        var hasRight = node.TryGetProperty("right", out var rightNode);
        var hasStateId = node.TryGetProperty("stateId", out _);
        var hasExpectedValue = node.TryGetProperty("expectedValue", out _);
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
            booleanState && (!hasExpectedValue || RequiredBool(node, "expectedValue", path)));
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
    }

    private static SkillProgramTriggerValue ParseTriggerValue(JsonElement node, string path)
    {
        RequireObject(node, path);
        CheckProperties(node, path, "kind", "value");
        var kind = EnumValue<SkillProgramTriggerValueKind>(node, "kind", path);
        var hasValue = node.TryGetProperty("value", out _);
        if ((kind == SkillProgramTriggerValueKind.IntegerConstant) != hasValue)
            Fail(path, kind == SkillProgramTriggerValueKind.IntegerConstant
                ? "integerConstant requires value"
                : "this trigger value does not accept value");
        return new SkillProgramTriggerValue(
            kind,
            hasValue ? RequiredInt(node, "value", path) : 0);
    }

    private static SkillProgramCondition ParseCondition(JsonElement node, string path, int depth)
    {
        if (depth >= MaximumDepth) Fail(path, $"condition nesting exceeds {MaximumDepth}");
        RequireObject(node, path);
        CheckProperties(node, path, "kind", "value", "children", "sourceBind", "stateId", "expectedValue");
        var kind = EnumValue<SkillProgramConditionKind>(node, "kind", path);
        var hasValue = node.TryGetProperty("value", out var valueNode);
        var hasChildren = node.TryGetProperty("children", out var childrenNode);
        var value = hasValue ? GetInt(valueNode, path + ".value") : 0;
        var sourceBind = node.TryGetProperty("sourceBind", out _) ? Identifier(node, "sourceBind", path) : null;
        var stateId = node.TryGetProperty("stateId", out _) ? Identifier(node, "stateId", path) : null;
        var expectedValue = node.TryGetProperty("expectedValue", out _) ? RequiredBool(node, "expectedValue", path) : true;
        var children = new List<SkillProgramCondition>();
        if (hasChildren)
        {
            if (childrenNode.ValueKind != JsonValueKind.Array) Fail(path + ".children", "must be an array");
            CheckCount(childrenNode.GetArrayLength(), path + ".children");
            var index = 0;
            foreach (var child in childrenNode.EnumerateArray()) children.Add(ParseCondition(child, $"{path}.children[{index++}]", depth + 1));
        }
        var needsValue = kind is SkillProgramConditionKind.HpAtLeast or SkillProgramConditionKind.HandCountAtLeast;
        if (needsValue != hasValue) Fail(path, needsValue ? "this condition requires value" : "this condition does not accept value");
        if (needsValue && value < 0) Fail(path + ".value", "must be non-negative");
        var composite = kind is SkillProgramConditionKind.All or SkillProgramConditionKind.Any or SkillProgramConditionKind.Not;
        if (composite != hasChildren) Fail(path, composite ? "this condition requires children" : "this condition does not accept children");
        if (kind == SkillProgramConditionKind.Not && children.Count != 1) Fail(path + ".children", "not requires exactly one child");
        if (kind is SkillProgramConditionKind.All or SkillProgramConditionKind.Any && children.Count == 0)
            Fail(path + ".children", "all and any require at least one child");
        if ((kind == SkillProgramConditionKind.PindianWon) != (sourceBind is not null))
            Fail(path, kind == SkillProgramConditionKind.PindianWon
                ? "pindianWon requires sourceBind" : "sourceBind is accepted only by pindianWon");
        if ((kind == SkillProgramConditionKind.BooleanState) != (stateId is not null))
            Fail(path, kind == SkillProgramConditionKind.BooleanState
                ? "booleanState requires stateId" : "stateId is accepted only by booleanState");
        if (kind != SkillProgramConditionKind.BooleanState && node.TryGetProperty("expectedValue", out _))
            Fail(path + ".expectedValue", "is accepted only by booleanState");
        return new SkillProgramCondition(kind, value, new ReadOnlyCollection<SkillProgramCondition>(children),
            sourceBind, stateId, expectedValue);
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
