namespace CardGame.Core;

public enum HpChangeKind { Loss, Recovery, Damage = 600, MaximumHp = 601 }
public enum PostEventContinuation { Boundary, Program, CardUse, GroupRecovery, AwaitedProgramMovement, VirtualBasicCardUse }

/// <summary>Actual committed HP delta. Damage and setting HP are separate rules operations.</summary>
public sealed record HpChangeContext(
    long Id, long? ParentFrameId, int? SourceSeat, int TargetSeat,
    HpChangeKind Kind, int Amount, int HpBefore, int HpAfter,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] LossOccurrence? LossOccurrence = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ProgramTriggerCandidate>? FrozenLossCandidates = null);

public sealed record LossOccurrence(int ActualTurnNumber, int TurnOwnerSeat, TurnPhase Phase);

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
