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
    ProgramCardUseContext? CardUse = null,
    JudgmentFinalizedContext? Judgment = null,
    ProgramJudgmentReplacementContext? JudgmentReplacement = null,
    HpChangeContext? HpChange = null);

public sealed record ProgramSkillNumberBinding(string Name, int Value);
public sealed record ProgramTopReorder(
    IReadOnlyList<int> ViewedCardIds,
    IReadOnlyList<int> TopCardIds,
    IReadOnlyList<int> BottomCardIds,
    bool ChoosingBottom);
public sealed record ProgramRepeatedJudgment(
    string Reason, string ResultBind, IReadOnlyList<Suit> SuccessSuits, int CompletedCount,
    bool? LastMatched = null);
public sealed record ProgramAttackRangeCoverageBinding(string Name, int SubjectSeat, int BeforeCount, int AfterCount);
public sealed record ProgramMovementContinuation(int SubjectSeat, int BeforeCount, string? CoverageResultBind);

/// <summary>Private draft: no cards move until selection completes and a later node consumes the binding.</summary>
public sealed record ProgramOwnedCardSelection(
    int CardOwnerSeat,
    string ResultBind,
    int RequiredCount,
    IReadOnlyList<int> CandidateCardIds,
    IReadOnlyList<CardLocation> CandidateLocations,
    IReadOnlyList<int> SelectedCardIds,
    int MinimumCount = 0);

/// <summary>
/// Private draft for holding another character's cards on their own general card.
/// The skill owner chooses among opaque hand slots and visible equipment; no card
/// moves until the hold completes, then all selected cards transfer together.
/// </summary>
public sealed record ProgramHoldCardSelection(
    int HolderSeat,
    int ChooserSeat,
    string ResultBind,
    int RequiredCount,
    IReadOnlyList<int> CandidateCardIds,
    IReadOnlyList<CardLocation> CandidateLocations,
    IReadOnlyList<int> SelectedCardIds,
    int MinimumCount = 0);

/// <summary>
/// Private draft for revealing one of another character's hand cards after the
/// chooser has viewed the whole hand. Unlike the hold draft, choices carry the
/// real card identities because the viewing is the skill's own effect. An
/// optional suit filter and decline let 攻心-style reveals pick among eligible
/// cards or walk away without revealing anything.
/// </summary>
public sealed record ProgramRevealCardSelection(
    int HolderSeat,
    int ChooserSeat,
    string ResultBind,
    IReadOnlyList<int> CandidateCardIds,
    IReadOnlyList<int> EligibleCardIds,
    bool AllowDecline);

/// <summary>
/// Private, committed distribution progress. A decline is legal only before the first transfer;
/// after that, the frozen required count is an all-or-nothing continuation.
/// </summary>
public sealed record ProgramOwnedCardDistribution(
    int CardOwnerSeat,
    string SourceBind,
    int RequiredCount,
    IReadOnlyList<CardZoneKind> Zones,
    SkillProgramTargetKind TargetKind,
    bool AllowDeclineBeforeFirst,
    IReadOnlyList<int> GivenCardIds,
    IReadOnlyList<int> TargetSeats);

/// <summary>Frozen sequential responders for one attack-range aid instruction.</summary>
public sealed record ProgramAttackRangeAid(
    int TargetSeat,
    IReadOnlyList<int> ResponderSeats,
    int ResponderIndex);

public sealed record ProgramChoiceResultBinding(string Name, string OptionId, int ChooserSeat);

public sealed record ProgramOptionChosenEvent(
    long FrameId, string SkillId, string BindingId, int OwnerSeat,
    string ResultBind, string OptionId, int ChooserSeat, string OptionLabel = "") : IGameEvent;

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
    IReadOnlyList<CardLocation> SourceLocations)
{
    /// <summary>Public suit frozen before a single-card transfer; remains readable if the card moves again.</summary>
    public Suit? FrozenRevealedSuit { get; init; }
}

public sealed record ProgramBoundCardGivenEvent(
    long FrameId,
    string SkillId,
    string BindingId,
    int OwnerSeat,
    int TargetSeat,
    int CardId) : IGameEvent;

public sealed record ProgramOwnedCardDistributedEvent(
    long FrameId,
    string SkillId,
    string BindingId,
    int OwnerSeat,
    int TargetSeat,
    int CardId,
    int DistributionIndex,
    int RequiredCount) : IGameEvent;

public sealed record ProgramAttackRangeAidStartedEvent(
    long FrameId,
    string SkillId,
    string BindingId,
    int OwnerSeat,
    int TargetSeat,
    IReadOnlyList<int> ResponderSeats) : IGameEvent;

public sealed record ProgramAttackRangeAidChoiceResolvedEvent(
    long FrameId,
    string SkillId,
    string BindingId,
    int OwnerSeat,
    int ResponderSeat,
    int TargetSeat,
    int? DiscardedWeaponCardId,
    IReadOnlyList<int> DrawnCardIds) : IGameEvent;

public sealed record ProgramDamageCardsClaimedEvent(
    long FrameId,
    string SkillId,
    string BindingId,
    int OwnerSeat,
    IReadOnlyList<int> CardIds) : IGameEvent;

public sealed record ProgramMovedCardsClaimedEvent(
    long FrameId,
    string SkillId,
    string BindingId,
    int OwnerSeat,
    int SourceSeat,
    int CardId) : IGameEvent;

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

public sealed record BeforeDamageProgramCandidate(
    ProgramTriggerCandidate Candidate,
    SkillProgramTriggerFacts Facts);

public enum BeforeDamageProgramContinuation
{
    Attack
}

/// <summary>
/// Frozen, ordered program opportunities before one pending damage application.
/// The continuation names engine mechanisms rather than any character or skill.
/// </summary>
public sealed record BeforeDamageProgramWindowFrame(
    long Id,
    long ParentFrameId,
    int SourceSeat,
    int TargetSeat,
    int Amount,
    DamageNature Nature,
    BeforeDamageProgramContinuation Continuation,
    IReadOnlyList<BeforeDamageProgramCandidate> Candidates,
    int CandidateIndex = 0,
    bool Prevented = false,
    int? RedirectedTargetSeat = null,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.BeforeDamageProgramWindow, Step);

public sealed record ProgramDamagePreventedEvent(
    long FrameId,
    string SkillId,
    string BindingId,
    int OwnerSeat,
    int SourceSeat,
    int TargetSeat,
    int Amount) : IGameEvent;

/// <summary>
/// A detached program continuation while a freely interactive phase runs.
/// Keeping it outside ResolutionStack preserves the existing clean phase boundary.
/// </summary>
public sealed record ProgramPhaseSchedule(
    ProgramSkillFrame Frame,
    ProgramLifecycleTriggerWindowFrame ParentFrame,
    TurnPhase Phase,
    SkillProgramPhaseContinuation Continuation);

public enum ProgramLifecycleContinuation
{
    NormalTurnStart, CompleteDrawPhase, CompletePlayPhase, CompleteAfterNormalDraw,
    CompleteDiscardPhase, EndTurnAfterDiscardPhase
}

public enum TurnEndingBoundaryItemKind { Program }

/// <summary>One frozen, ordered item in the end-of-turn coordinator.</summary>
public sealed record TurnEndingBoundaryItem(
    TurnEndingBoundaryItemKind Kind,
    int Priority,
    string StableIdentity,
    ProgramTriggerCandidate? Candidate = null,
    SkillProgramTriggerFacts? Facts = null);

/// <summary>
/// Serializable end-of-turn cursor. Program bindings resume this one frame
/// without replaying earlier boundaries.
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

/// <summary>
/// Serializable play-phase-start cursor over frozen ordered program
/// opportunities from both the turn owner (own-scope triggers) and observers
/// (other-living-scope triggers).
/// </summary>
public sealed record PlayPhaseStartingBoundaryFrame(
    long Id,
    int OwnerSeat,
    IReadOnlyList<TurnEndingBoundaryItem> Items,
    SkillProgramTriggerFacts Facts,
    int ItemIndex = 0,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.PlayPhaseStartingBoundary, Step);

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
    : ResolutionFrame(Id, ResolutionFrameKind.ProgramLifecycleTriggerWindow, Step)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? FrozenBaseDrawCount { get; init; }

    public IReadOnlyDictionary<int, SkillProgramTriggerFacts>? ParticipantFacts { get; init; }
}

/// <summary>
/// Frozen ordered program opportunities owned by a character who has just died.
/// The killer is retained only as public event context so target policies can
/// exclude that seat without naming any concrete skill.
/// </summary>
public sealed record ProgramDeathTriggerWindowFrame(
    long Id,
    long DeathFrameId,
    int OwnerSeat,
    int? KillerSeat,
    IReadOnlyList<ProgramTriggerCandidate> Candidates,
    SkillProgramTriggerFacts Facts,
    int CandidateIndex = 0,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.ProgramDeathTriggerWindow, Step);

/// <summary>Frozen killer-side death programs observed by living characters.</summary>
public sealed record ProgramKillTriggerWindowFrame(
    long Id,
    long DeathFrameId,
    int VictimSeat,
    int? KillerSeat,
    IReadOnlyList<ProgramTriggerCandidate> Candidates,
    IReadOnlyList<ProgramSkillWindowContext> Contexts,
    int CandidateIndex = 0,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.ProgramDeathTriggerWindow, Step);

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
    int OwnerSeat,
    string Bind,
    IReadOnlyList<CardSnapshot> Cards) : IGameEvent;

public sealed record ProgramCategoryDiscardResolvedEvent(
    long FrameId,
    string SkillId,
    string BindingId,
    int OwnerSeat,
    int ChooserSeat,
    string SourceBind,
    string ResultBind,
    int? DiscardedCardId) : IGameEvent;

public sealed record ProgramTurnSkillsGrantedEvent(
    long FrameId,
    string SkillId,
    string BindingId,
    int OwnerSeat,
    IReadOnlyList<string> GrantedSkillIds) : IGameEvent;

public sealed record ProgramCardEffectNullifiedEvent(
    long FrameId,
    string SkillId,
    string BindingId,
    int OwnerSeat,
    int SourceSeat,
    long CardUseFrameId,
    CardKind CardKind) : IGameEvent;

public sealed record ProgramSelectedCardEffectsNullifiedEvent(
    long FrameId, string SkillId, string BindingId, int OwnerSeat, int SourceSeat,
    long CardUseFrameId, CardKind CardKind, IReadOnlyList<int> TargetSeats) : IGameEvent;

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
