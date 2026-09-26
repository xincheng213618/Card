namespace CardGame.Core;

/// <summary>Frozen, trusted-host facts for one final judgment result.</summary>
public sealed record JudgmentFinalizedContext(
    long JudgmentFrameId,
    int SubjectSeat,
    string Reason,
    int CardId,
    CardKind CardKind,
    Suit Suit,
    int Rank,
    bool Succeeded,
    int? SourceSeat = null);

/// <summary>Frozen replacement card choice and parent judgment identity.</summary>
public sealed record ProgramJudgmentReplacementContext(
    long JudgmentFrameId,
    int SubjectSeat,
    string Reason,
    int OldCardId,
    int ReplacementCardId,
    int DrawnCards = 0,
    int RecoveredHp = 0,
    Suit? ReplacementSuit = null,
    int? ReplacementRank = null);

public sealed record ProgramJudgmentTriggerCandidate(
    int OwnerSeat, string SkillId, string TriggerId, string SkillInstanceId, string GameplayHash);

public sealed record ProgramJudgmentTriggerWindowFrame(
    long Id,
    long ParentFrameId,
    JudgmentFinalizedContext Judgment,
    IReadOnlyList<ProgramJudgmentTriggerCandidate> Candidates,
    int CandidateIndex = 0,
    bool Activated = false)
    : ResolutionFrame(Id, ResolutionFrameKind.ProgramJudgmentTriggerWindow, ResolutionFrameStep.ResolvingEffect);

public sealed record ProgramJudgmentTriggerResolvedEvent(
    long FrameId,
    long JudgmentFrameId,
    string SkillId,
    string TriggerId,
    int OwnerSeat,
    bool Activated) : IGameEvent;

public sealed record ProgramJudgmentReplacementResolvedEvent(
    long JudgmentFrameId,
    string SkillId,
    string TriggerId,
    int OwnerSeat,
    int SubjectSeat,
    bool Activated,
    int OldCardId,
    int? ReplacementCardId,
    SkillProgramOldJudgmentCardDestination OldCardDestination,
    int DrawnCards,
    int RecoveredHp) : IGameEvent;

public sealed record ProgramJudgmentTargetSelectedEvent(
    long FrameId,
    long JudgmentFrameId,
    string SkillId,
    string TriggerId,
    int OwnerSeat,
    int TargetSeat) : IGameEvent;

public sealed record ProgramJudgmentDamageRequestedEvent(
    long FrameId,
    long JudgmentFrameId,
    string SkillId,
    string TriggerId,
    int SourceSeat,
    int TargetSeat,
    int Amount,
    DamageNature Nature) : IGameEvent;
