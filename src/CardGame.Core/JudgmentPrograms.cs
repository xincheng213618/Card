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
    bool Succeeded);

public sealed record ProgramJudgmentTriggerCandidate(
    int OwnerSeat, string SkillId, string TriggerId, string GameplayHash);

public sealed record ProgramJudgmentTriggerWindowFrame(
    long Id,
    long ParentFrameId,
    JudgmentFinalizedContext Judgment,
    IReadOnlyList<ProgramJudgmentTriggerCandidate> Candidates,
    int CandidateIndex = 0,
    int InstructionIndex = 0,
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
