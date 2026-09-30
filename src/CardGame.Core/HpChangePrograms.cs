namespace CardGame.Core;

public enum HpChangeKind { Loss, Recovery, Damage = 600, MaximumHp = 601 }
public enum PostEventContinuation { Boundary, Program, CardUse, GroupRecovery, AwaitedProgramMovement }

/// <summary>Actual committed HP delta. Damage and setting HP are separate rules operations.</summary>
public sealed record HpChangeContext(
    long Id, long? ParentFrameId, int? SourceSeat, int TargetSeat,
    HpChangeKind Kind, int Amount, int HpBefore, int HpAfter);

public sealed record HpChangedTriggerWindowFrame(
    long Id,
    HpChangeContext Change,
    IReadOnlyList<ProgramTriggerCandidate> Candidates,
    IReadOnlyList<ProgramSkillWindowContext> Contexts,
    PostEventContinuation Continuation,
    long? ResumeFrameId = null,
    int? CardId = null,
    CardKind? CardKind = null,
    int CandidateIndex = 0,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.HpChangedTriggerWindow, Step);
