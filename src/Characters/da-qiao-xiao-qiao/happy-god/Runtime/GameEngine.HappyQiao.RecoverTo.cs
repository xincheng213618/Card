using System.Text.Json.Serialization;

namespace CardGame.Core;

// This is a real recovery intent. The numerical HP query receipts deliberately
// cannot satisfy it, and the eventual native recovery owns a fresh payment.
public sealed record HappyQiaoRecoverToOrigin(long ProgramFrameId, int InstructionIndex,
    string GameplayHash, int SourceSeat, int TargetSeat, int TargetHp,
    long HealthRequestId, HappyQiaoPool Pool, HappyQiaoDualHealth Before, int Amount);

public sealed record HappyQiaoRecoverToIntent(long ProgramFrameId, int InstructionIndex,
    string GameplayHash, int SourceSeat, int TargetSeat, int TargetHp,
    long HealthRequestId, HappyQiaoDualHealth Before, long? RecoveryAttemptId = null, bool FinishedWithoutRecovery = false);

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HappyQiaoRecoverToIntent? HappyQiaoRecoverTo { get; init; }
}

public sealed partial record RecoveryAttempt
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HappyQiaoRecoverToOrigin? HappyQiaoRecoverToOrigin { get; init; }
}

public sealed record HappyQiaoRecoverToQueuedEvent(long ProgramFrameId, long HealthRequestId,
    long RecoveryAttemptId, int SourceSeat, int TargetSeat, int TargetHp, HappyQiaoPool Pool, int RequestedAmount) : IGameEvent;
public sealed record HappyQiaoRecoverToNoRecoveryEvent(long ProgramFrameId, long HealthRequestId,
    int TargetSeat, int TargetHp, HappyQiaoPool Pool) : IGameEvent;

public sealed partial class GameEngine
{
    private bool TryHandleHappyQiaoRecoverTo(long frameId, int ownerSeat, int targetSeat, int value)
    {
        if (!HasHappyQiaoHealth(targetSeat)) return false;
        var frame = GetActiveProgramFrame(frameId);
        if (frame.OwnerSeat != ownerSeat || _resolutionStack.LastOrDefault()?.Id != frame.Id ||
            value < 0 || value > _players[targetSeat].MaxHp)
            throw new InvalidOperationException("Qiao RecoverTo requires its exact native Program instruction and target HP.");
        if (!_players[targetSeat].IsAlive || _winner != Winner.None) return true;
        var key = $"recover-to:{frame.Id}:{frame.InstructionIndex}:{targetSeat}";
        if (frame.HappyQiaoHealth?.OccurrenceKey != key)
        {
            if (_pendingDecision is not null || frame.HappyQiaoHealth is { Applied: false, Released: false })
                throw new InvalidOperationException("RecoverTo cannot replace an unsettled health operation.");
            var initialRequest = new HappyQiaoHealthReceipt(++_resolutionSequence, frame.Id, frame.Kind, targetSeat, ownerSeat,
                HpChangeKind.Recovery, value, HappyQiaoHealthReturnKind.RecoverTo, key, frame.InstructionIndex,
                _players[targetSeat].HappyQiaoHealth!);
            ReplaceRuntimeTop(frame with { HappyQiaoHealth = initialRequest, HappyQiaoRecoverTo = new(frame.Id, frame.InstructionIndex,
                frame.GameplayHash, ownerSeat, targetSeat, value, initialRequest.Id, initialRequest.Before) });
            AdvanceEventRulesAndQueueFact(new HappyQiaoHealthRequestedEvent(initialRequest.Id, frame.Id, targetSeat,
                initialRequest.Kind, initialRequest.Amount, initialRequest.ReturnKind, initialRequest.OccurrenceKey));
            var child = new HappyQiaoHealthChoiceFrame(++_resolutionSequence, frame.Id, initialRequest.Id, targetSeat);
            PushRuntimeFrame(child); AdvanceRuntimeFrame(child.Id);
            return true;
        }
        _ = TryBeginHappyQiaoHealthChoice(frame.Id, targetSeat, HpChangeKind.Recovery, value,
            HappyQiaoHealthReturnKind.RecoverTo, key, ownerSeat);
        frame = GetActiveProgramFrame(frame.Id);
        var receipt = frame.HappyQiaoHealth!;
        var intent = frame.HappyQiaoRecoverTo ?? throw new InvalidOperationException("RecoverTo lost its original target-HP intent.");
        if (intent.ProgramFrameId != frame.Id || intent.InstructionIndex != frame.InstructionIndex || intent.GameplayHash != frame.GameplayHash ||
            intent.SourceSeat != ownerSeat || intent.TargetSeat != targetSeat || intent.TargetHp != value ||
            intent.HealthRequestId != receipt.Id || intent.Before != receipt.Before || intent.RecoveryAttemptId is not null ||
            intent.FinishedWithoutRecovery || receipt.ReturnKind != HappyQiaoHealthReturnKind.RecoverTo || receipt.Pool is not { } pool)
            throw new InvalidOperationException("RecoverTo cannot change its target, chosen sister, instruction or settled intent.");
        RequireHappyQiaoSelectedReceipt(frame, receipt);
        var amount = Math.Max(0, checked(value - intent.Before.Hp(pool)));
        if (amount == 0)
        {
            ReleaseHappyQiaoHealthSelection(frame.Id);
            ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { HappyQiaoRecoverTo = intent with { FinishedWithoutRecovery = true } });
            AdvanceEventRulesAndQueueFact(new HappyQiaoRecoverToNoRecoveryEvent(frame.Id, receipt.Id, targetSeat, value, pool));
            return true;
        }
        var origin = new HappyQiaoRecoverToOrigin(frame.Id, frame.InstructionIndex, frame.GameplayHash, ownerSeat,
            targetSeat, value, receipt.Id, pool, intent.Before, amount);
        if (!TryQueueRecoveryReplacement(frame.Id, ownerSeat, targetSeat, amount,
            new(RecoveryAttemptProducer.Program, frame.InstructionIndex), origin))
            throw new InvalidOperationException("A positive Qiao RecoverTo must queue its real native recovery.");
        frame = GetActiveProgramFrame(frame.Id);
        var request = frame.PendingRecoveryAttempts!.Single(a => a.HappyQiaoRecoverToOrigin == origin);
        ReplaceRuntimeTop(frame with { HappyQiaoRecoverTo = intent with { RecoveryAttemptId = request.Id } });
        ReleaseHappyQiaoHealthSelection(frame.Id);
        AdvanceEventRulesAndQueueFact(new HappyQiaoRecoverToQueuedEvent(frame.Id, receipt.Id, request.Id, ownerSeat,
            targetSeat, value, pool, amount));
        return true;
    }

    private bool TryBindHappyQiaoRecoverToRequest(RecoveryReplacementFrame frame)
    {
        var origin = frame.Attempt.HappyQiaoRecoverToOrigin!;
        RequireHappyQiaoRecoverToOrigin(frame, origin);
        if (frame.HappyQiaoHealth is not null) return false;
        if (_resolutionStack.LastOrDefault()?.Id != frame.Id || _players[origin.TargetSeat].HappyQiaoHealth != origin.Before)
            throw new InvalidOperationException("RecoverTo's native recovery lost the unchanged original selected pool.");
        var request = new HappyQiaoHealthReceipt(++_resolutionSequence, frame.Id, frame.Kind, origin.TargetSeat,
            origin.SourceSeat, HpChangeKind.Recovery, origin.Amount, HappyQiaoHealthReturnKind.RecoveryRequest,
            $"recovery-request:{frame.Id}:{origin.TargetSeat}", 0, origin.Before, Pool: origin.Pool);
        ReplaceRuntimeTop(frame with { HappyQiaoHealth = request });
        AdvanceEventRulesAndQueueFact(new HappyQiaoHealthRequestedEvent(request.Id, frame.Id, origin.TargetSeat,
            request.Kind, request.Amount, request.ReturnKind, request.OccurrenceKey));
        AdvanceEventRulesAndQueueFact(new HappyQiaoHealthPoolSelectedEvent(request.Id, frame.Id, origin.TargetSeat,
            origin.Pool, origin.Before.Hp(origin.Pool), origin.Before.MaximumHp));
        // Resume with the newly owned receipt, never keep evaluating the stale
        // pre-binding RecoveryReplacementFrame supplied by the caller.
        AdvanceRuntimeFrame(frame.Id);
        return true;
    }

    private void RequireHappyQiaoRecoverToOrigin(RecoveryReplacementFrame frame, HappyQiaoRecoverToOrigin origin)
    {
        var attempt = frame.Attempt;
        var parent = _resolutionStack.SingleOrDefault(f => f.Id == frame.ParentFrameId) as ProgramSkillFrame;
        if (parent is null || parent.Id != origin.ProgramFrameId || parent.GameplayHash != origin.GameplayHash ||
            parent.InstructionIndex != origin.InstructionIndex || parent.OwnerSeat != origin.SourceSeat ||
            parent.HappyQiaoRecoverTo is not { } intent || intent.ProgramFrameId != parent.Id ||
            intent.InstructionIndex != origin.InstructionIndex || intent.GameplayHash != origin.GameplayHash ||
            intent.SourceSeat != origin.SourceSeat || intent.TargetSeat != origin.TargetSeat || intent.TargetHp != origin.TargetHp ||
            intent.Before != origin.Before || intent.HealthRequestId != origin.HealthRequestId || intent.RecoveryAttemptId != frame.Id || intent.FinishedWithoutRecovery ||
            parent.HappyQiaoHealth is not { ReturnKind: HappyQiaoHealthReturnKind.RecoverTo, Released: true, Applied: false } selected ||
            selected.Id != origin.HealthRequestId || selected.ParentFrameId != parent.Id || selected.ParentKind != parent.Kind ||
            selected.InstructionIndex != origin.InstructionIndex || selected.TargetSeat != origin.TargetSeat || selected.SourceSeat != origin.SourceSeat ||
            selected.Kind != HpChangeKind.Recovery || selected.Amount != origin.TargetHp || selected.Pool != origin.Pool || selected.Before != origin.Before ||
            selected.OccurrenceKey != $"recover-to:{parent.Id}:{parent.InstructionIndex}:{origin.TargetSeat}" ||
            attempt.Id != frame.Id || attempt.SourceSeat != origin.SourceSeat || attempt.TargetSeat != origin.TargetSeat ||
            attempt.HpBefore is not null || attempt.HappyQiaoBefore != origin.Before || attempt.Amount != origin.Amount || origin.Amount <= 0 ||
            origin.TargetHp > origin.Before.MaximumHp || origin.Amount != Math.Max(0, checked(origin.TargetHp - origin.Before.Hp(origin.Pool))) ||
            attempt.Completion != new RecoveryAttemptCompletion(RecoveryAttemptProducer.Program, origin.InstructionIndex) ||
            ProgramEventHistory<HappyQiaoHealthPoolSelectedEvent>().Count(e => e == new HappyQiaoHealthPoolSelectedEvent(origin.HealthRequestId,
                parent.Id, origin.TargetSeat, origin.Pool, origin.Before.Hp(origin.Pool), origin.Before.MaximumHp)) != 1 ||
            ProgramEventHistory<HappyQiaoHealthRequestedEvent>().Count(e => e == new HappyQiaoHealthRequestedEvent(selected.Id, parent.Id,
                origin.TargetSeat, selected.Kind, selected.Amount, selected.ReturnKind, selected.OccurrenceKey)) != 1 ||
            ProgramEventHistory<HappyQiaoHealthSelectionReleasedEvent>().Count(e => e == new HappyQiaoHealthSelectionReleasedEvent(selected.Id, parent.Id)) != 1 ||
            ProgramEventHistory<HappyQiaoRecoverToQueuedEvent>().Count(e => e == new HappyQiaoRecoverToQueuedEvent(parent.Id, selected.Id,
                frame.Id, origin.SourceSeat, origin.TargetSeat, origin.TargetHp, origin.Pool, origin.Amount)) != 1)
            throw new InvalidOperationException("A native RecoverTo cannot borrow a query, a different pool, or a replayed recovery intent.");
    }
}
