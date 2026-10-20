namespace CardGame.Core;

public sealed record ProgramRecoveryApplicationReceipt(long HpChangeId, long RecoveryFrameId,
    int InstructionIndex, string GameplayHash, int SourceSeat, int TargetSeat,
    int RequestedAmount, int HpBefore, int HpAfter);
public sealed record ProgramRecoveryAppliedEvent(long ProgramFrameId, ProgramRecoveryApplicationReceipt Receipt) : IGameEvent;
public sealed record ProgramRecoveryRequestedEvent(long ProgramFrameId, int InstructionIndex, string GameplayHash,
    long RecoveryFrameId, int SourceSeat, int TargetSeat, int RequestedAmount, int HpBefore) : IGameEvent;

public sealed partial class GameEngine
{
    private void RecordProgramRecoveryRequest(ResolutionFrame? parent, RecoveryAttempt attempt) =>
        HealthContinuations.RecordProgramRecoveryRequest(parent, attempt);

    private bool ProgramRecoveryReplacementReceiptMatches(ProgramSkillFrame owner, RecoveryReplacementFrame recovery,
        IReadOnlyList<IGameEvent> history) =>
        HealthContinuations.ProgramRecoveryReplacementReceiptMatches(owner, recovery, history);

    // One actual recovery owns one HP invoice. Native operations and primitive
    // Recover use the same RecoveryFrame, including synchronous target batches.
    private void RecordProgramRecoveryApplication(RecoveryFrame recovery, HpChangeContext change) =>
        HealthContinuations.RecordProgramRecoveryApplication(recovery, change);

    private bool ProgramRecoveryReceiptMatches(ProgramSkillFrame owner, HpChangedTriggerWindowFrame hp,
        IReadOnlyList<IGameEvent> history) =>
        HealthContinuations.ProgramRecoveryReceiptMatches(owner, hp, history);

    private void ClearProgramRecoveryApplication(HpChangeContext change) =>
        HealthContinuations.ClearProgramRecoveryApplication(change);
}
