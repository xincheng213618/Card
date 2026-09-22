namespace CardGame.Core;

/// <summary>Frozen public facts and parent identity for one configured lifecycle binding.</summary>
public sealed record ProgramSkillWindowContext(
    SkillProgramTriggerWindow Window,
    long ParentFrameId,
    int OwnerSeat,
    int? SourceSeat = null,
    int? TargetSeat = null,
    long? DamageFrameId = null,
    int Amount = 0,
    int OccurrenceIndex = 0,
    SkillProgramTriggerFacts? Facts = null,
    CardMovementBatchContext? MovementBatch = null,
    int? MovementIndex = null,
    int? ResumeCandidateIndex = null,
    ProgramCardUseContext? CardUse = null);

public sealed record ProgramSkillNumberBinding(string Name, int Value);

public sealed record ProgramChoiceResultBinding(string Name, string OptionId, int ChooserSeat);

public sealed record ProgramOptionChosenEvent(
    long FrameId, string SkillId, string BindingId, int OwnerSeat,
    string ResultBind, string OptionId, int ChooserSeat) : IGameEvent;

public sealed record ProgramPindianResultBinding(
    string Name,
    int SourceSeat,
    int OpponentSeat,
    int SourceRank,
    int OpponentRank,
    bool SourceWon,
    SkillProgramCardSetVisibility Visibility);

public sealed record ProgramSkillCardSetBinding(
    string Name,
    IReadOnlyList<int> CardIds,
    SkillProgramCardSetVisibility Visibility,
    IReadOnlyList<CardLocation> SourceLocations);

public sealed record ProgramBoundCardGivenEvent(
    long FrameId,
    string SkillId,
    string BindingId,
    int OwnerSeat,
    int TargetSeat,
    int CardId) : IGameEvent;

public sealed record ProgramDamageCardsClaimedEvent(
    long FrameId,
    string SkillId,
    string BindingId,
    int OwnerSeat,
    IReadOnlyList<int> CardIds) : IGameEvent;

public sealed record ProgramRandomHandCardsTakenEvent(
    long FrameId,
    string SkillId,
    string BindingId,
    int OwnerSeat,
    IReadOnlyList<int> TargetSeats,
    int CardCount) : IGameEvent;

public sealed record ProgramOwnedZoneCardsDiscardedEvent(
    long FrameId,
    string SkillId,
    string BindingId,
    int OwnerSeat,
    IReadOnlyList<CardZoneKind> Zones,
    int CardCount) : IGameEvent;

public sealed record ProgramChainedStateSetEvent(
    long FrameId,
    string SkillId,
    string BindingId,
    int OwnerSeat,
    bool IsChained,
    int? TargetSeat = null) : IGameEvent;

/// <summary>A configured trigger opportunity frozen independently of reflection and legacy enums.</summary>
public sealed record ProgramTriggerCandidate(
    int OwnerSeat,
    string SkillId,
    string BindingId,
    string SkillInstanceId,
    string GameplayHash,
    int Priority,
    int OccurrenceIndex = 0);

/// <summary>
/// A detached program continuation while a freely interactive phase runs.
/// Keeping it outside ResolutionStack preserves the existing clean phase boundary.
/// </summary>
public sealed record ProgramPhaseSchedule(
    ProgramSkillFrame Frame,
    ProgramLifecycleTriggerWindowFrame ParentFrame,
    TurnPhase Phase,
    SkillProgramPhaseContinuation Continuation);

public enum ProgramLifecycleContinuation { NormalTurnStart, CompleteDrawPhase, CompletePlayPhase }

public enum TurnEndingBoundaryItemKind { Program, LegacyJujian }

/// <summary>One frozen, ordered item in the end-of-turn coordinator.</summary>
public sealed record TurnEndingBoundaryItem(
    TurnEndingBoundaryItemKind Kind,
    int Priority,
    string StableIdentity,
    ProgramTriggerCandidate? Candidate = null);

/// <summary>
/// Serializable end-of-turn cursor. Program bindings and the restricted legacy
/// Jujian bridge resume this one frame without replaying earlier boundaries.
/// </summary>
public sealed record TurnEndingBoundaryFrame(
    long Id,
    int OwnerSeat,
    int TurnNumber,
    IReadOnlyList<TurnEndingBoundaryItem> Items,
    SkillProgramTriggerFacts Facts,
    int ItemIndex = 0,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.TurnEndingBoundary, Step);

public sealed record ProgramLifecycleTriggerWindowFrame(
    long Id,
    int OwnerSeat,
    SkillProgramTriggerWindow Window,
    IReadOnlyList<ProgramTriggerCandidate> Candidates,
    ProgramLifecycleContinuation Continuation,
    SkillProgramTriggerFacts Facts,
    bool SkipPlayPhaseAfterDraw = false,
    bool NormalDrawReplaced = false,
    int NormalDrawAdjustment = 0,
    int CandidateIndex = 0,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.ProgramLifecycleTriggerWindow, Step);

public sealed record ProgramBindingStartedEvent(
    long FrameId,
    string SkillId,
    string BindingId,
    string SkillInstanceId,
    int OwnerSeat,
    SkillProgramTriggerWindow Window) : IGameEvent;

public sealed record ProgramBindingResolvedEvent(
    long FrameId,
    string SkillId,
    string BindingId,
    string SkillInstanceId,
    int OwnerSeat,
    SkillProgramTriggerWindow Window,
    bool Activated,
    bool Completed) : IGameEvent;

public sealed record ProgramCardsRevealedEvent(
    long FrameId,
    string SkillId,
    string BindingId,
    string Bind,
    IReadOnlyList<CardSnapshot> Cards) : IGameEvent;

public sealed record ProgramCardSubsetSelectedEvent(
    long FrameId,
    string SkillId,
    string BindingId,
    string SourceBind,
    string ResultBind,
    IReadOnlyList<int> CardIds,
    int RankSum) : IGameEvent;

public sealed record ProgramPhaseScheduledEvent(
    long FrameId,
    string SkillId,
    string BindingId,
    int OwnerSeat,
    TurnPhase Phase,
    bool Started) : IGameEvent;

public sealed record ProgramNormalDrawAdjustedEvent(
    long FrameId,
    string SkillId,
    string BindingId,
    int OwnerSeat,
    int Adjustment,
    int TotalAdjustment) : IGameEvent;
