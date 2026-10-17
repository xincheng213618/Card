namespace CardGame.Core;

public sealed record ProgramRecoveryApplicationReceipt(long HpChangeId, long RecoveryFrameId,
    int InstructionIndex, string GameplayHash, int SourceSeat, int TargetSeat,
    int RequestedAmount, int HpBefore, int HpAfter);
public sealed record ProgramRecoveryAppliedEvent(long ProgramFrameId, ProgramRecoveryApplicationReceipt Receipt) : IGameEvent;
public sealed record ProgramRecoveryRequestedEvent(long ProgramFrameId, int InstructionIndex, string GameplayHash,
    long RecoveryFrameId, int SourceSeat, int TargetSeat, int RequestedAmount, int HpBefore) : IGameEvent;

public sealed partial class GameEngine
{
    private void RecordProgramRecoveryRequest(ResolutionFrame? parent, RecoveryAttempt attempt)
    {
        if (parent is not ProgramSkillFrame owner || attempt.Completion.Producer != RecoveryAttemptProducer.Program) return;
        if (attempt.Completion != new RecoveryAttemptCompletion(RecoveryAttemptProducer.Program, owner.InstructionIndex) ||
            TypedRecoveryPausedEffect(owner) is null)
            throw new InvalidOperationException("Program recovery request lost its exact instruction owner.");
        AdvanceEventRulesAndQueueFact(new ProgramRecoveryRequestedEvent(owner.Id, owner.InstructionIndex,
            owner.GameplayHash, attempt.Id, attempt.SourceSeat, attempt.TargetSeat, attempt.Amount, attempt.HpBefore));
    }

    private bool ProgramRecoveryReplacementReceiptMatches(ProgramSkillFrame owner, RecoveryReplacementFrame recovery,
        IReadOnlyList<IGameEvent> history)
    {
        var attempt = recovery.Attempt;
        if (recovery.Id != attempt.Id || recovery.ParentFrameId != owner.Id || recovery.Id <= owner.Id ||
            recovery.Return.ResumeFrameId != owner.Id || recovery.Return.CardId is not null || recovery.Return.CardKind is not null ||
            !(recovery.Return.Continuation == PostEventContinuation.Program ||
              recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && owner.PendingMovementContinuation is not null) ||
            attempt.Completion != new RecoveryAttemptCompletion(RecoveryAttemptProducer.Program, owner.InstructionIndex) ||
            TypedRecoveryPausedEffect(owner) is null || attempt.Amount <= 0 ||
            !IsValidPlayerSeat(attempt.SourceSeat) || !IsValidPlayerSeat(attempt.TargetSeat) ||
            !TypedRecoveryApplicationBoundsMatch(recovery, history)) return false;
        var issued = new ProgramRecoveryRequestedEvent(owner.Id, owner.InstructionIndex, owner.GameplayHash,
            attempt.Id, attempt.SourceSeat, attempt.TargetSeat, attempt.Amount, attempt.HpBefore);
        return history.OfType<ProgramRecoveryRequestedEvent>().Count(e => e == issued) == 1;
    }

    // One actual recovery owns one HP invoice. Native operations and primitive
    // Recover use the same RecoveryFrame, including synchronous target batches.
    private void RecordProgramRecoveryApplication(RecoveryFrame recovery, HpChangeContext change)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == recovery.ParentFrameId);
        if (index < 0 || _resolutionStack[index] is not ProgramSkillFrame owner) return;
        if (change.ParentFrameId != owner.Id || change.Kind != HpChangeKind.Recovery ||
            change.SourceSeat != recovery.SourceSeat || change.TargetSeat != recovery.TargetSeat ||
            change.HpBefore != recovery.HpBefore || change.Amount <= 0 || change.Amount > recovery.Amount)
            throw new InvalidOperationException("Program recovery lost its real child and HP invoice.");
        var receipt = new ProgramRecoveryApplicationReceipt(change.Id, recovery.Id, owner.InstructionIndex,
            owner.GameplayHash, recovery.SourceSeat, recovery.TargetSeat, recovery.Amount, change.HpBefore, change.HpAfter);
        ReplaceRuntimeFrame(owner.Id, owner with
        { RecoveryApplications = Array.AsReadOnly((owner.RecoveryApplications ?? []).Append(receipt).ToArray()) });
        AdvanceEventRulesAndQueueFact(new ProgramRecoveryAppliedEvent(owner.Id, receipt));
    }

    private bool ProgramRecoveryReceiptMatches(ProgramSkillFrame owner, HpChangedTriggerWindowFrame hp,
        IReadOnlyList<IGameEvent> history)
    {
        if (!ProgramHpReturnMatches(owner, hp) || hp.Change.Kind != HpChangeKind.Recovery ||
            (owner.RecoveryApplications ?? []).Where(r => r.HpChangeId == hp.Change.Id).ToArray() is not [var receipt] ||
            receipt.InstructionIndex != owner.InstructionIndex || receipt.GameplayHash != owner.GameplayHash ||
            TypedRecoveryPausedEffect(owner) is null || receipt.SourceSeat != hp.Change.SourceSeat ||
            receipt.TargetSeat != hp.Change.TargetSeat || receipt.HpBefore != hp.Change.HpBefore || receipt.HpAfter != hp.Change.HpAfter ||
            hp.Change.Amount != receipt.HpAfter - receipt.HpBefore || hp.Change.Amount <= 0 ||
            hp.Change.Amount > receipt.RequestedAmount || receipt.RecoveryFrameId <= owner.Id)
            return false;
        return history.OfType<ProgramRecoveryAppliedEvent>().Count(e => e.ProgramFrameId == owner.Id && e.Receipt == receipt) == 1;
    }

    private void ClearProgramRecoveryApplication(HpChangeContext change)
    {
        if (change.Kind != HpChangeKind.Recovery || change.ParentFrameId is not { } parent) return;
        var index = _resolutionStack.FindIndex(f => f.Id == parent);
        if (index < 0 || _resolutionStack[index] is not ProgramSkillFrame { RecoveryApplications: { } receipts } owner) return;
        var remaining = receipts.Where(r => r.HpChangeId != change.Id).ToArray();
        ReplaceRuntimeFrame(owner.Id, owner with { RecoveryApplications = remaining.Length == 0 ? null : remaining });
    }
}
