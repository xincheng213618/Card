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
    CardUse,
    ResponseWindow,
    Judgment,
    Damage,
    DamageTriggerWindow,
    DamageSkill,
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
    CardsMovedTriggerWindow,
    BeforeDamageProgramWindow,
    ProgramDeathTriggerWindow
}

public enum ResolutionFrameStep
{
    Declared,
    AwaitingResponse,
    ResolvingEffect,
    Completed
}

public enum DamageSkillEffectKind
{
    None,
    ClaimDamageCard,
    GiftDrawnCard,
    DrawToMaxHand,
    RecoverDamageTarget,
    GanglieJudgment,
    TakeSourceCard,
    RecoverDamageSource,
    BenefitDamageSource,
    RevealHandAndPunishSource,
    SelectRevealedCardsByRank,
    RevealCardAndChallengeSource
}

/// <summary>
/// Serializable data describing one in-flight rules operation. Frames contain
/// no delegates, WPF objects, or mutable content instances, so a trusted host
/// can inspect and later persist the stack without coupling it to the UI.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CardUseFrame), "card-use")]
[JsonDerivedType(typeof(ResponseWindowFrame), "response-window")]
[JsonDerivedType(typeof(JudgmentFrame), "judgment")]
[JsonDerivedType(typeof(DamageFrame), "damage")]
[JsonDerivedType(typeof(DamageTriggerWindowFrame), "damage-trigger-window")]
[JsonDerivedType(typeof(RecoveryFrame), "recovery")]
[JsonDerivedType(typeof(DyingFrame), "dying")]
[JsonDerivedType(typeof(DeathFrame), "death")]
[JsonDerivedType(typeof(DamageSkillFrame), "damage-skill")]
[JsonDerivedType(typeof(NullificationWindowFrame), "nullification-window")]
[JsonDerivedType(typeof(TargetCardSelectionFrame), "target-card-selection")]
[JsonDerivedType(typeof(ProgramSkillFrame), "program-skill")]
[JsonDerivedType(typeof(ProgramCardTriggerWindowFrame), "program-card-trigger-window")]
[JsonDerivedType(typeof(ProgramJudgmentTriggerWindowFrame), "program-judgment-trigger-window")]
[JsonDerivedType(typeof(PindianFrame), "pindian")]
[JsonDerivedType(typeof(ProgramLifecycleTriggerWindowFrame), "program-lifecycle-trigger-window")]
[JsonDerivedType(typeof(TurnEndingBoundaryFrame), "turn-ending-boundary")]
[JsonDerivedType(typeof(ProgramDeathTriggerWindowFrame), "program-death-trigger-window")]
[JsonDerivedType(typeof(CardsMovedTriggerWindowFrame), "cards-moved-trigger-window")]
[JsonDerivedType(typeof(BeforeDamageProgramWindowFrame), "before-damage-program-window")]
public abstract record ResolutionFrame(
    long Id,
    ResolutionFrameKind Kind,
    ResolutionFrameStep Step);

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

    /// <summary>Null for a play activation; otherwise the stable trigger/binding id.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TriggerId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramSkillWindowContext? WindowContext { get; init; }

    public IReadOnlyList<ProgramSkillNumberBinding> NumberBindings { get; init; } = [];
    public IReadOnlyList<ProgramAttackRangeCoverageBinding> AttackRangeCoverageBindings { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramMovementContinuation? PendingMovementContinuation { get; init; }
    public IReadOnlyList<ProgramChoiceResultBinding> ChoiceBindings { get; init; } = [];
    public IReadOnlyList<ProgramSkillCardSetBinding> CardSetBindings { get; init; } = [];
    public IReadOnlyList<ProgramPindianResultBinding> PindianResultBindings { get; init; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramOwnedCardSelection? OwnedCardSelection { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramOwnedCardDistribution? OwnedCardDistribution { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramAttackRangeAid? AttackRangeAid { get; init; }
}

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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<int>? IneffectiveTargetSeats { get; init; }
}

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
    IReadOnlyList<int>? ReplacementCandidateSeats = null,
    int ReplacementCandidateIndex = 0)
    : ResolutionFrame(Id, ResolutionFrameKind.Judgment, Step);

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
    int CandidateIndex = 0,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.DamageTriggerWindow, Step);

public sealed record DamageSkillFrame(
    long Id,
    long ParentFrameId,
    int OwnerSeat,
    int SourceSeat,
    int? CardId,
    CardKind? CardKind,
    SkillKind Skill,
    ResolutionFrameStep Step = ResolutionFrameStep.AwaitingResponse,
    string CandidateId = "",
    int Priority = 0,
    DamageSkillEffectKind Effect = DamageSkillEffectKind.None,
    IReadOnlyList<int>? EffectCardIds = null)
    : ResolutionFrame(Id, ResolutionFrameKind.DamageSkill, Step);

public sealed record RecoveryFrame(
    long Id,
    long ParentFrameId,
    int SourceSeat,
    int TargetSeat,
    int Amount,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.Recovery, Step);

public sealed record DyingFrame(
    long Id,
    long ParentFrameId,
    int VictimSeat,
    int? KillerSeat,
    IReadOnlyList<int> ResponderSeats,
    int ResponderIndex,
    ResolutionFrameStep Step = ResolutionFrameStep.AwaitingResponse)
    : ResolutionFrame(Id, ResolutionFrameKind.Dying, Step);

public sealed record DeathFrame(
    long Id,
    long ParentFrameId,
    int VictimSeat,
    int? KillerSeat,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.Death, Step);

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
    : ResolutionFrame(Id, ResolutionFrameKind.NullificationWindow, Step);

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
