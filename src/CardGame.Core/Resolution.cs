using System.Text.Json.Serialization;

namespace CardGame.Core;

public static class JudgmentReasons
{
    public const string BaguaDefense = "equipment.bagua-defense";
    public const string Ganglie = "skill.ganglie";
    public const string Leiji = "skill.leiji";
    public const string Luoshen = "skill.luoshen";
    public const string Tieqi = "skill.tieqi";
    public const string Shuangxiong = "skill.shuangxiong";
    public const string Wuhun = "skill.wuhun.death";
    public const string Indulgence = "trick.indulgence";
    public const string SupplyShortage = "trick.supply-shortage";
    public const string Lightning = "trick.lightning";
}

public enum ResolutionFrameKind
{
    CardEffectBeforeApply = 1700,
    CardDeclaration = 1600,
    CardDeclarationChallenge = 1601,
    DeferredTurnEnd = 1300,
    CardUse = 0,
    ResponseWindow,
    Judgment,
    Damage,
    DamageTriggerWindow,
    Recovery,
    Dying,
    Death,
    NullificationWindow,
    TargetCardSelection,
    ProgramSkill,
    ProgramCardTriggerWindow,
    ProgramJudgmentTriggerWindow,
    Pindian,
    ProgramLifecycleTriggerWindow,
    TurnEndingBoundary,
    PlayPhaseStartingBoundary,
    CardsMovedTriggerWindow,
    BeforeDamageProgramWindow,
    ProgramDeathTriggerWindow,
    HpChangedTriggerWindow
}

public enum ResolutionFrameStep
{
    Declared,
    AwaitingResponse,
    ResolvingEffect,
    Completed
}

/// <summary>
/// Serializable data describing one in-flight rules operation. Frames contain
/// no delegates, WPF objects, or mutable content instances, so a trusted host
/// can inspect and later persist the stack without coupling it to the UI.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(DeferredTurnEndFrame), "deferred-turn-end")]
[JsonDerivedType(typeof(CardEffectBeforeApplyFrame), "card-effect-before-apply")]
[JsonDerivedType(typeof(CardDeclarationFrame), "card-declaration")]
[JsonDerivedType(typeof(CardDeclarationChallengeFrame), "card-declaration-challenge")]
[JsonDerivedType(typeof(CardUseFrame), "card-use")]
[JsonDerivedType(typeof(ResponseWindowFrame), "response-window")]
[JsonDerivedType(typeof(JudgmentFrame), "judgment")]
[JsonDerivedType(typeof(DamageFrame), "damage")]
[JsonDerivedType(typeof(DamageTriggerWindowFrame), "damage-trigger-window")]
[JsonDerivedType(typeof(RecoveryFrame), "recovery")]
[JsonDerivedType(typeof(HpChangedTriggerWindowFrame), "hp-changed-trigger-window")]
[JsonDerivedType(typeof(DyingFrame), "dying")]
[JsonDerivedType(typeof(DeathFrame), "death")]
[JsonDerivedType(typeof(NullificationWindowFrame), "nullification-window")]
[JsonDerivedType(typeof(TargetCardSelectionFrame), "target-card-selection")]
[JsonDerivedType(typeof(ProgramSkillFrame), "program-skill")]
[JsonDerivedType(typeof(ProgramCardTriggerWindowFrame), "program-card-trigger-window")]
[JsonDerivedType(typeof(ProgramJudgmentTriggerWindowFrame), "program-judgment-trigger-window")]
[JsonDerivedType(typeof(PindianFrame), "pindian")]
[JsonDerivedType(typeof(ProgramLifecycleTriggerWindowFrame), "program-lifecycle-trigger-window")]
[JsonDerivedType(typeof(TurnEndingBoundaryFrame), "turn-ending-boundary")]
[JsonDerivedType(typeof(PlayPhaseStartingBoundaryFrame), "play-phase-starting-boundary")]
[JsonDerivedType(typeof(ProgramDeathTriggerWindowFrame), "program-death-trigger-window")]
[JsonDerivedType(typeof(CardsMovedTriggerWindowFrame), "cards-moved-trigger-window")]
[JsonDerivedType(typeof(BeforeDamageProgramWindowFrame), "before-damage-program-window")]
public abstract record ResolutionFrame(
    long Id,
    ResolutionFrameKind Kind,
    ResolutionFrameStep Step)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DeclaredCardPayment? AcceptedDeclarationPayment { get; init; }
}

/// <summary>A resumable program cursor; child resolutions cannot repeat paid effects.</summary>
public sealed record ProgramSkillFrame(
    long Id,
    int OwnerSeat,
    string SkillId,
    string ActivationId,
    string GameplayHash,
    int InstructionIndex,
    IReadOnlyList<int> SelectedCardIds,
    IReadOnlyList<int> SelectedTargetSeats,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.ProgramSkill, Step)
{
    /// <summary>The exact current grant selected when this execution was frozen.</summary>
    public string SkillInstanceId { get; init; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramPrivateTurnHoldDraft? PrivateTurnHoldDraft { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SkillPolarity? ConversionPreviousPolarity { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramConvertingGiftDraft? ConvertingGift { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramFinalTargetGiftDraft? FinalTargetGift { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramDomainCrossingDraft? DomainCrossing { get; init; }
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public ProgramGeneralLibraryDraft? GeneralLibraryDraft {get;init;}
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramPileEquipmentDraft? PileEquipment { get; init; }

    /// <summary>Null for a play activation; otherwise the stable trigger/binding id.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TriggerId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramSkillWindowContext? WindowContext { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ProgramNamedTargetDefense? NamedTargetDefense { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ProgramPublicHandDraft? PublicHandDraft { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PendingDecision? ResponseDecision { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ProgramResponseEntityExchangeDraft? ResponseEntityExchange { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ProgramPublicSuitDiscardDraft? PublicSuitDiscard { get; init; }

    public IReadOnlyList<ProgramSkillNumberBinding> NumberBindings { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SelectedAllOwnerHandCards { get; init; }
    public IReadOnlyList<ProgramAttackRangeCoverageBinding> AttackRangeCoverageBindings { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramMovementContinuation? PendingMovementContinuation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramSelectedCardPayment? SelectedCardPayment { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramSelectedCardPaymentResult? SelectedCardPaymentResult { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramStrategicDamageBatch? StrategicDamageBatch { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ProgramDeferredProviderReward>? DeferredProviderRewards { get; init; }
    public IReadOnlyList<ProgramChoiceResultBinding> ChoiceBindings { get; init; } = [];
    public IReadOnlyList<ProgramSkillCardSetBinding> CardSetBindings { get; init; } = [];
    public IReadOnlyList<ProgramPindianResultBinding> PindianResultBindings { get; init; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramOwnedCardSelection? OwnedCardSelection { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DeferredHandAlignmentResolution? DeferredHandAlignmentResolution { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramAssistedSlashRequest? AssistedSlashRequest { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramOtherCardSelection? OtherCardSelection { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramDiscardChallengeDraft? DiscardChallenge { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramFactionRecoveryDraft? FactionRecoveryDraft { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramWeaponDamageDraft? WeaponDamageDraft { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramHandControlDraft? HandControlDraft { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramDiscardTopPlacement? DiscardTopPlacement { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramCompletedCardGiftDraft? CompletedCardGiftDraft { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramDiscardBudgetDraft? DiscardBudgetDraft { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramCompletedFactionGiftDraft? CompletedFactionGiftDraft { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramCardEnhancementDraft? CardEnhancementDraft { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramHoldCardSelection? HoldCardSelection { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramRevealCardSelection? RevealCardSelection { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramOwnedCardDistribution? OwnedCardDistribution { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramAttackRangeAid? AttackRangeAid { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramTopReorder? TopReorder { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ProgramQuotaTopDraft? QuotaTop { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public AlternatingSuitTopDraft? AlternatingSuitTop { get; init; }
    public NamedTurnCountFlowDraft? NamedTurnCountFlow { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramRepeatedJudgment? RepeatedJudgment { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramAdvancedSelection? AdvancedSelection { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramPrivateReserveDraft? PrivateReserveDraft { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramPublicPileDraft? PublicPileDraft { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PublicPileColorPayment? PublicPileColorPayment {get;init;}
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ProgramDeckEndExchange? DeckEndExchange { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ProgramRelativeZoneDemand? RelativeZoneDemand { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ProgramDeckSlashSequence? DeckSlashSequence { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramSelectedParticipantDiscardDraft? SelectedParticipantDiscard { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramDamageCardOffer? DamageCardOffer { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramHandComparisonDraft? HandComparisonDraft { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AttackAttemptState? AttackAttempt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramAttackReturn? AttackReturn { get; init; }
    public CardResolutionContinuations Continuations { get; init; } = new();
    public CardAttackState? CardAttack { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FactionRecoveryDebtReturn? FactionRecoveryDebtReturn { get; init; }
    public bool ReexecuteParticipantInstruction { get; init; }
}

/// <summary>One cardless program damage instruction, owned solely by its program frame.</summary>
public sealed record AttackAttemptState(
    int SourceSeat,
    int TargetSeat,
    int DamageAmount,
    DamageNature Nature,
    bool SourceLess,
    long? ProgramJudgmentWindowId = null,
    bool DamageAmountFinalized = false,
    bool IgnoresArmor = false,
    bool DamageWasApplied = false,
    int SourceToTargetDistanceAtDamage = 0,
    long? ResolvedDyingDamageFrameId = null,
    bool BeforeDamageProgramsResolved = false,
    bool DamageRedirected = false,
    IReadOnlyList<int>? ChainedTargetSeats = null,
    int ChainedTargetIndex = 0,
    bool IsChainPropagation = false,
    string? TransferSkillId = null,
    int? TransferOwnerSeat = null,
    int? TransferTargetSeat = null,
    bool TransferDrawLostHp = false)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<int>? RangePreventionVisitedTargets { get; init; }
}

/// <summary>Identifies a suspended parent using existing frame IDs, without retaining its attack object.</summary>
public sealed record ProgramAttackReturn(long? ParentAttackOwnerFrameId,
    long? ParentDamageWindowFrameId, long? ParentJudgmentFrameId);

public sealed record FactionRecoveryDebtReturn(long DyingFrameId, long DyingParentFrameId,
    int DyingVictimSeat, DyingContinuationKind DyingContinuation, bool Survived,
    long? ParentAttackOwnerFrameId, long? ParentDamageWindowFrameId);

public sealed record CardAttackState
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<int>? RangePreventionVisitedTargets { get; init; }
    public bool Active { get; init; }
    public int SourceSeat { get; init; }
    public int CardUserSeat { get; init; }
    public int TargetSeat { get; init; }
    public int DamageAmount { get; init; }
    public bool DamageAmountFinalized { get; init; }
    public bool IgnoresArmor { get; init; }
    public bool IsSourceLess { get; init; }
    public IReadOnlyList<int> ChainedTargetSeats { get; init; } = [];
    public int ChainedTargetIndex { get; init; }
    public bool IsChainPropagation { get; init; }
    public bool FactionDefenseAttempted { get; init; }
    public bool FactionSlashAttempted { get; init; }
    public bool CixiongDoubleSwordsResolved { get; init; }
    public bool IceSwordAttempted { get; init; }
    public bool QilinBowAttempted { get; init; }
    public bool DamageWasApplied { get; init; }
    public int SourceToTargetDistanceAtDamage { get; init; }
    public long? ResolvedDyingDamageFrameId { get; init; }
    public bool PendingRedBladeDamageBonus { get; init; }
    public bool DamageRedirected { get; init; }
    public bool BeforeDamageProgramsResolved { get; init; }
    public bool ProhibitsDodge { get; init; }
    public bool ProhibitsTargetHandResponses { get; init; }
    public IReadOnlyList<string> ResponseProhibitingSkillNames { get; init; } = [];
    public int RequiredDodgeResponses { get; init; } = 1;
    public int SuccessfulDodgeResponses { get; init; }
    public CardKind? EffectiveCardKind { get; init; }
    public bool IsDelayedJudgmentDamage { get; init; }
    public long? ProgramJudgmentFrameId { get; init; }
    public long? ProgramSkillFrameId { get; init; }
    public long? ProgramSkillCardUseFrameId { get; init; }
    public int? DelayedJudgmentSeat { get; init; }
    public DamageNature? DamageNatureOverride { get; init; }
    public CardConversionSource? ConversionSource { get; init; }
    public int? CardId { get; init; }
    public CardKind? AppearanceKind { get; init; }
    public Suit? AppearanceSuit { get; init; }
    public int? AppearanceRank { get; init; }
    public IReadOnlyList<int> PhysicalCardIds { get; init; } = [];
    public ProgramDamageTransferReceipt? DamageTransferFollowup { get; init; }
}

public sealed record ProgramDamageTransferReceipt(string SkillId, int OwnerSeat, int TargetSeat, bool DrawLostHp);

public sealed record CardUseFrame(
    long Id,
    int SourceSeat,
    int CardId,
    CardKind CardKind,
    IReadOnlyList<int> TargetSeats,
    ResolutionFrameStep Step = ResolutionFrameStep.Declared,
    int TargetIndex = 0,
    bool IgnoresArmor = false,
    IReadOnlyList<int>? PhysicalCardIds = null)
    : ResolutionFrame(Id, ResolutionFrameKind.CardUse, Step)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardActionContext? Action { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool FirstOwnPlayUseDistanceUnlimited { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IssuedCardNoResponse? IssuedNoResponse { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DyingResponseEvent? DyingResponse { get; init; }

    public CardResolutionContinuations Continuations { get; init; } = new();
    public CardAttackState? CardAttack { get; init; }
    public IReadOnlyList<CardAttackState>? PreparedTargetAttacks { get; init; }
    public bool CausedDamage { get; init; }
    public bool ProgramUseAccepted { get; init; }
    public bool ProgramUseCommitted { get; init; }
    public bool FinalizedTrickProgramsStarted { get; init; }
    public bool FinalizedSimpleProgramsStarted { get; init; }
    public bool TargetsAdjusted { get; init; }
    public bool SlashTargetsCancelled { get; init; }
    public bool UnlimitedUse { get; init; }
    public bool YingboUnrespondable { get; init; }
    public bool YingboRepeated { get; init; }
    public int? ProgramAdjustedSlashBaseDamage { get; init; }
    public int? ForeignPublicPileSlashBaseDamage { get; init; }
    public ProgramSimpleCardContinuation? AdjustedSimpleContinuation { get; init; }
    public IReadOnlyList<int> CompletedDamageParticipants { get; init; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<int>? IneffectiveTargetSeats { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FireAttackSelectionState? FireAttackSelection { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SequentialTrickUse? SequentialTrick { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public CurrentCardEnhancement Enhancements { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? EnhancementOwnerSeat { get; init; }
}

public sealed record SequentialTrickUse(LegalActionKind ActionKind, int? FirstTargetCardId, CardKind? RequiredCardKind);

/// <summary>The revealed card becomes public only after the target chooses it.</summary>
public sealed record FireAttackSelectionState(int? RevealedCardId);

public sealed record ResponseWindowFrame(
    long Id,
    long ParentFrameId,
    int SourceSeat,
    int ResponderSeat,
    CardKind IncomingCard,
    ResolutionFrameStep Step = ResolutionFrameStep.AwaitingResponse,
    CardKind? RequiredCardKind = null)
    : ResolutionFrame(Id, ResolutionFrameKind.ResponseWindow, Step);

/// <summary>
/// A short, deterministic judgment operation. The frame is retained while the
/// physical card is in the owner's Judgment zone, which keeps automatic
/// equipment defenses on the same serializable resolution stack as responses.
/// </summary>
public enum JudgmentContinuationKind
{
    Bagua,
    FactionDefenseBagua,
    ProgramSkill,
    Indulgence,
    SupplyShortage,
    Lightning
}

public sealed record JudgmentFrame(
    long Id,
    long ParentFrameId,
    int TargetSeat,
    string Reason,
    int? CardId,
    CardKind? CardKind,
    Suit? Suit,
    bool? Succeeded,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect,
    int ReplacementCandidateIndex = 0)
    : ResolutionFrame(Id, ResolutionFrameKind.Judgment, Step)
{
    public CardResolutionContinuations Continuations { get; init; } = new();
    public CardAttackState? CardAttack { get; init; }
    public int SourceSeat { get; init; }
    public JudgmentContinuationKind Continuation { get; init; }
    public long? ParentAttackId { get; init; }
    public int? DelayedCardId { get; init; }
    public string? ProgramResultBind { get; init; }
    public SkillProgramCardSetVisibility? ProgramResultVisibility { get; init; }

    /// <summary>Frozen, ordered replacement opportunities for this judgment.</summary>
    public IReadOnlyList<JudgmentTriggerCandidate> ReplacementCandidates { get; init; } = [];

    public IReadOnlyList<int> ReplacementCandidateSeats =>
        ReplacementCandidates.Select(candidate => candidate.OwnerSeat).ToArray();
}

public sealed record DamageFrame(
    long Id,
    long ParentFrameId,
    int SourceSeat,
    int TargetSeat,
    int Amount,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect,
    DamageNature Nature = DamageNature.Normal)
    : ResolutionFrame(Id, ResolutionFrameKind.Damage, Step);

/// <summary>
/// A serializable cursor over every eligible after-damage trigger. The cursor
/// is separate from an individual optional skill frame so a trigger may pause,
/// resolve, and then resume the same ordered window without losing later
/// candidates.
/// </summary>
public sealed record DamageTriggerWindowFrame(
    long Id,
    long ParentFrameId,
    int SourceSeat,
    int TargetSeat,
    int? SourceCardId,
    CardKind? SourceCard,
    IReadOnlyList<DamageTriggerCandidate> Candidates,
    SkillProgramTriggerWindow TriggerWindow,
    int CandidateIndex = 0,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.DamageTriggerWindow, Step);

public sealed record RecoveryFrame(
    long Id,
    long ParentFrameId,
    int SourceSeat,
    int TargetSeat,
    int Amount,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect,
    int HpBefore = 0)
    : ResolutionFrame(Id, ResolutionFrameKind.Recovery, Step);

public enum DyingContinuationKind
{
    Damage,
    ProgramSkill,
    AttackHpLoss
}

public sealed record DyingFrame(
    long Id,
    long ParentFrameId,
    int VictimSeat,
    int? KillerSeat,
    IReadOnlyList<int> ResponderSeats,
    int ResponderIndex,
    DyingContinuationKind Continuation,
    ResolutionFrameStep Step = ResolutionFrameStep.AwaitingResponse)
    : ResolutionFrame(Id, ResolutionFrameKind.Dying, Step)
{
    public IReadOnlyList<string> AttemptedSelfDyingBindings { get; init; } = [];
    public int ResponderSeat => ResponderSeats[ResponderIndex];
    public bool ResumesProgramSkill => Continuation == DyingContinuationKind.ProgramSkill;
    public bool ResumesAttackHpLoss => Continuation == DyingContinuationKind.AttackHpLoss;
    public long FrameId => Id;
    public long? DamageFrameId => Continuation == DyingContinuationKind.Damage ? ParentFrameId : null;
}

public enum DeathReturnKind
{
    Dying,
    ProgramSkill
}

public sealed record DeathFrame(
    long Id,
    long ParentFrameId,
    int VictimSeat,
    int? KillerSeat,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.Death, Step)
{
    public DeathReturnKind? ReturnKind { get; init; }
    public bool OwnerDiedProgramsResolved { get; init; }
    public bool KillerProgramsResolved { get; init; }
    public IReadOnlyList<int> CleanedUpCardIds { get; init; } = [];
}

/// <summary>
/// A public trick-effect response cursor. It records only public card/use
/// context and the ordered seat cursor; private nullification-card ids remain
/// in the responder-scoped PendingDecision instead.
/// </summary>
public sealed record NullificationWindowFrame(
    long Id,
    long ParentFrameId,
    int SourceSeat,
    IReadOnlyList<int> TargetSeats,
    int EffectCardId,
    CardKind EffectCardKind,
    LegalActionKind ActionKind,
    int? TargetCardId,
    CardKind? RequiredCardKind,
    IReadOnlyList<int> CandidateSeats,
    int CandidateIndex = 0,
    int ChainDepth = 0,
    bool EffectNullified = false,
    ResolutionFrameStep Step = ResolutionFrameStep.AwaitingResponse)
    : ResolutionFrame(Id, ResolutionFrameKind.NullificationWindow, Step)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IssuedCounterspellNode? IssuedNoResponseNode { get; init; }
}

/// <summary>
/// A private source-side cursor for selecting one card from another player's
/// hidden hand. CandidateSlots contains only opaque ordinal slots, never card
/// ids or card kinds, so the frame is safe to inspect as a public resolution
/// cursor while the actual hand remains private to the trusted host.
/// </summary>
public sealed record TargetCardSelectionFrame(
    long Id,
    long ParentFrameId,
    int SourceSeat,
    int TargetSeat,
    int EffectCardId,
    CardKind EffectCardKind,
    LegalActionKind ActionKind,
    IReadOnlyList<int> CandidateSlots,
    ResolutionFrameStep Step = ResolutionFrameStep.AwaitingResponse)
    : ResolutionFrame(Id, ResolutionFrameKind.TargetCardSelection, Step);
