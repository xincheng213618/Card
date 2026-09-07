using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum ResolutionFrameKind
{
    CardUse,
    ResponseWindow,
    Damage,
    DamageTriggerWindow,
    DamageSkill,
    Recovery,
    Dying,
    Death
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
    RecoverDamageTarget
}

/// <summary>
/// Serializable data describing one in-flight rules operation. Frames contain
/// no delegates, WPF objects, or mutable content instances, so a trusted host
/// can inspect and later persist the stack without coupling it to the UI.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CardUseFrame), "card-use")]
[JsonDerivedType(typeof(ResponseWindowFrame), "response-window")]
[JsonDerivedType(typeof(DamageFrame), "damage")]
[JsonDerivedType(typeof(DamageTriggerWindowFrame), "damage-trigger-window")]
[JsonDerivedType(typeof(RecoveryFrame), "recovery")]
[JsonDerivedType(typeof(DyingFrame), "dying")]
[JsonDerivedType(typeof(DeathFrame), "death")]
[JsonDerivedType(typeof(DamageSkillFrame), "damage-skill")]
public abstract record ResolutionFrame(
    long Id,
    ResolutionFrameKind Kind,
    ResolutionFrameStep Step);

public sealed record CardUseFrame(
    long Id,
    int SourceSeat,
    int CardId,
    CardKind CardKind,
    IReadOnlyList<int> TargetSeats,
    ResolutionFrameStep Step = ResolutionFrameStep.Declared,
    int TargetIndex = 0)
    : ResolutionFrame(Id, ResolutionFrameKind.CardUse, Step);

public sealed record ResponseWindowFrame(
    long Id,
    long ParentFrameId,
    int SourceSeat,
    int ResponderSeat,
    CardKind IncomingCard,
    ResolutionFrameStep Step = ResolutionFrameStep.AwaitingResponse,
    CardKind? RequiredCardKind = null)
    : ResolutionFrame(Id, ResolutionFrameKind.ResponseWindow, Step);

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
    int SourceCardId,
    CardKind SourceCard,
    IReadOnlyList<DamageTriggerCandidate> Candidates,
    int CandidateIndex = 0,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.DamageTriggerWindow, Step);

public sealed record DamageSkillFrame(
    long Id,
    long ParentFrameId,
    int OwnerSeat,
    int SourceSeat,
    int CardId,
    CardKind CardKind,
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
