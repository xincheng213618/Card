namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool IsResponseCompletionSilverLionAttempt(IReadOnlyList<CardMovementRecord> costs, RecoveryAttempt a) =>
        a.Completion.Producer == RecoveryAttemptProducer.SilverLion && a.Amount == 1 && a.SourceSeat == a.TargetSeat &&
        costs.Any(m => m.CardKind == CardKind.SilverLion && m.From == CardLocation.Equipment(a.TargetSeat) &&
            m.Reason == a.Completion.MoveReason);
    private static bool IsResponseCompletionSilverLionHealth(IReadOnlyList<CardMovementRecord> costs, HpChangeContext h) =>
        h.Kind == HpChangeKind.Recovery && h.Amount == 1 && h.SourceSeat == h.TargetSeat && h.HpAfter == h.HpBefore + 1 &&
        costs.Any(m => m.CardKind == CardKind.SilverLion && m.From == CardLocation.Equipment(h.TargetSeat));
    private static bool SameResponseCompletionRecovery(RecoveryAttempt a, RecoveryAttempt b) =>
        a.Id == b.Id && a.SourceSeat == b.SourceSeat && a.TargetSeat == b.TargetSeat && a.Amount == b.Amount && a.HpBefore == b.HpBefore &&
        a.ActualTurnNumber == b.ActualTurnNumber && a.ActualTurnOwnerSeat == b.ActualTurnOwnerSeat && a.Completion == b.Completion &&
        a.Candidates.SequenceEqual(b.Candidates);

    private bool TryDrainResponseCompletionHealth(ProgramCardTriggerWindowFrame supplied)
    {
        var window = supplied; var r = window.ResponseCompletion!;
        while (r.CostRecoveryCursor < r.CostRecoveries.Count)
        {
            var request = r.CostRecoveries[r.CostRecoveryCursor];
            var native = _resolutionStack.Single(f => f.Id == r.ParentFrameId);
            var actual = native.PendingRecoveryAttempts?.SingleOrDefault(a => a.Id == request.Id);
            if (actual is null || !SameResponseCompletionRecovery(actual, request))
                throw new InvalidOperationException("A response cost lost its original queued Silver Lion recovery.");
            var remaining = native.PendingRecoveryAttempts!.Where(a => a.Id != request.Id).ToArray();
            ReplaceRuntimeFrame(native.Id, native with { PendingRecoveryAttempts = remaining.Length == 0 ? null : Array.AsReadOnly(remaining) });
            ReplaceRuntimeTop(window with { ResponseCompletion = r with { ActiveHealthChildFrameId = request.Id } });
            PushRuntimeFrame(new RecoveryReplacementFrame(request.Id, window.Id, request,
                new(PostEventContinuation.ResponseCompletion, window.Id)));
            AdvanceRuntimeFrame(request.Id); return true;
        }
        while (r.CostHealthCursor < r.CostHealthChanges.Count)
        {
            var change = r.CostHealthChanges[r.CostHealthCursor];
            if (!_pendingHpChanges.Remove(change)) throw new InvalidOperationException("A response cost lost its original native HP-change invoice.");
            var owner = _players[change.TargetSeat];
            var facts = CaptureProgramTriggerFacts(owner) with { HpChangeAmount = change.Amount,
                HpBeforeChange = change.HpBefore, HpAfterChange = change.HpAfter };
            var candidates = owner.IsAlive ? new[] { SkillProgramTriggerWindow.AfterHpRecovered, SkillProgramTriggerWindow.AfterHealthChanged }
                .SelectMany(w => CollectProgramTriggerCandidates(owner, w)).Where(c => GetProgramTrigger(c).Condition.Evaluate(facts, c.SkillId, c.SkillInstanceId))
                .SelectMany(c => Enumerable.Range(0, GetProgramTrigger(c).HpChangeOccurrence == SkillProgramHpChangeOccurrence.PerPoint ? change.Amount : 1)
                    .Select(i => c with { OccurrenceIndex = i })).ToArray() : [];
            if (candidates.Length == 0)
            { r = r with { CostHealthCursor = r.CostHealthCursor + 1 }; ReplaceRuntimeTop(window = window with { ResponseCompletion = r }); continue; }
            var contexts = candidates.Select(c => new ProgramSkillWindowContext(GetProgramTrigger(c).Window, change.Id, owner.Seat,
                SourceSeat: change.SourceSeat, TargetSeat: owner.Seat, Amount: change.Amount, OccurrenceIndex: c.OccurrenceIndex, Facts: facts, HpChange: change)).ToArray();
            ReplaceRuntimeTop(window with { ResponseCompletion = r with { ActiveHealthChildFrameId = change.Id } });
            PushRuntimeFrame(new HpChangedTriggerWindowFrame(change.Id, change, Array.AsReadOnly(candidates), Array.AsReadOnly(contexts),
                PostEventContinuation.ResponseCompletion, window.Id));
            AdvanceRuntimeTop<HpChangedTriggerWindowFrame>(); return true;
        }
        return false;
    }

    private bool IsResponseCompletionHealthChild(ProgramCardTriggerWindowFrame window, ResolutionFrame child)
    {
        if (window.ResponseCompletion is not { CostsDrained: false, ActiveHealthChildFrameId: { } id } r ||
            child.Id != id || r.ActiveCostChildFrameId is not null) return false;
        if (child is RecoveryReplacementFrame recovery)
            return r.CostRecoveryCursor >= 0 && r.CostRecoveryCursor < r.CostRecoveries.Count &&
                recovery.ParentFrameId == window.Id && recovery.Return == new RecoveryReplacementReturn(PostEventContinuation.ResponseCompletion, window.Id) &&
                SameResponseCompletionRecovery(recovery.Attempt, r.CostRecoveries[r.CostRecoveryCursor]) &&
                IsResponseCompletionSilverLionAttempt(r.NativeCosts, recovery.Attempt);
        return child is HpChangedTriggerWindowFrame hp && r.CostRecoveryCursor == r.CostRecoveries.Count &&
            r.CostHealthCursor >= 0 && r.CostHealthCursor < r.CostHealthChanges.Count &&
            hp.Continuation == PostEventContinuation.ResponseCompletion && hp.ResumeFrameId == window.Id && hp.CardId is null && hp.CardKind is null &&
            hp.Change == r.CostHealthChanges[r.CostHealthCursor] && hp.Change.ParentFrameId == r.ParentFrameId &&
            IsResponseCompletionSilverLionHealth(r.NativeCosts, hp.Change);
    }
    private bool ReturnCardResponseCompletionRecovery(RecoveryReplacementFrame child)
    {
        if (child.Return.Continuation != PostEventContinuation.ResponseCompletion) return false;
        if (_resolutionStack.LastOrDefault() is not ProgramCardTriggerWindowFrame window || !IsResponseCompletionHealthChild(window, child))
            throw new InvalidOperationException("A response cost recovery lost its exact typed return.");
        var r = window.ResponseCompletion!;
        ReplaceRuntimeTop(window with { ResponseCompletion = r with { CostRecoveryCursor = r.CostRecoveryCursor + 1, ActiveHealthChildFrameId = null } });
        AdvanceRuntimeTop<ProgramCardTriggerWindowFrame>(); return true;
    }
    private bool ReturnCardResponseCompletionHpChange(HpChangedTriggerWindowFrame child)
    {
        if (child.Continuation != PostEventContinuation.ResponseCompletion) return false;
        if (_resolutionStack.LastOrDefault() is not ProgramCardTriggerWindowFrame window || !IsResponseCompletionHealthChild(window, child))
            throw new InvalidOperationException("A response cost HP observer lost its exact typed return.");
        var r = window.ResponseCompletion!;
        ReplaceRuntimeTop(window with { ResponseCompletion = r with { CostHealthCursor = r.CostHealthCursor + 1, ActiveHealthChildFrameId = null } });
        AdvanceRuntimeTop<ProgramCardTriggerWindowFrame>(); return true;
    }
}
