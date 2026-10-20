namespace CardGame.Core;

internal sealed partial class ResponseCompletionRuntime
{
    private static bool IsResponseCompletionSilverLionAttempt(IReadOnlyList<CardMovementRecord> costs, RecoveryAttempt a) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ResponseCompletionHealth.IsResponseCompletionSilverLionAttempt(costs,a);
    private static bool IsResponseCompletionSilverLionHealth(IReadOnlyList<CardMovementRecord> costs, HpChangeContext h) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ResponseCompletionHealth.IsResponseCompletionSilverLionHealth(costs,h);
    private static bool SameResponseCompletionRecovery(RecoveryAttempt a, RecoveryAttempt b) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ResponseCompletionHealth.SameResponseCompletionRecovery(a,b);

    internal bool TryDrainResponseCompletionHealth(ProgramCardTriggerWindowFrame supplied)
    {
        var window = supplied; var r = window.ResponseCompletion!;
        while (r.CostRecoveryCursor < r.CostRecoveries.Count)
        {
            var request = r.CostRecoveries[r.CostRecoveryCursor];
            var native = FrameFacts().Single(f => f.Id == r.ParentFrameId);
            var actual = native.PendingRecoveryAttempts?.SingleOrDefault(a => a.Id == request.Id);
            if (actual is null || !SameResponseCompletionRecovery(actual, request))
                throw new InvalidOperationException("A response cost lost its original queued Silver Lion recovery.");
            var remaining = native.PendingRecoveryAttempts!.Where(a => a.Id != request.Id).ToArray();
            _frames.Replace(native.Id, native with { PendingRecoveryAttempts = remaining.Length == 0 ? null : Array.AsReadOnly(remaining) });
            _frames.ReplaceTop(window with { ResponseCompletion = r with { ActiveHealthChildFrameId = request.Id } });
            _frames.Push(new RecoveryReplacementFrame(request.Id, window.Id, request,
                new(PostEventContinuation.ResponseCompletion, window.Id)));
            _frames.Advance(request.Id); return true;
        }
        while (r.CostHealthCursor < r.CostHealthChanges.Count)
        {
            var change = r.CostHealthChanges[r.CostHealthCursor];
            if (!_native.RemovePendingHpChange(change)) throw new InvalidOperationException("A response cost lost its original native HP-change invoice.");
            var owner = change.TargetSeat;
            var facts = _facts.CaptureProgramTriggerFacts(owner) with { HpChangeAmount = change.Amount,
                HpBeforeChange = change.HpBefore, HpAfterChange = change.HpAfter,
                HealthChangeKind = _facts.UsesTriggerCondition(SkillProgramTriggerConditionKind.HealthChangeKindIs)
                    ? change.Kind : null };
            var candidates = _facts.IsAlive(owner) ? new[] { SkillProgramTriggerWindow.AfterHpRecovered, SkillProgramTriggerWindow.AfterHealthChanged }
                .SelectMany(w => _facts.CollectProgramTriggerCandidates(owner, w)).Where(c => _facts.GetProgramTrigger(c).Condition.Evaluate(facts, c.SkillId, c.SkillInstanceId))
                .SelectMany(c => Enumerable.Range(0, _facts.GetProgramTrigger(c).HpChangeOccurrence == SkillProgramHpChangeOccurrence.PerPoint ? change.Amount : 1)
                    .Select(i => c with { OccurrenceIndex = i })).ToArray() : [];
            if (candidates.Length == 0)
            { r = r with { CostHealthCursor = r.CostHealthCursor + 1 }; _frames.ReplaceTop(window = window with { ResponseCompletion = r }); continue; }
            var contexts = candidates.Select(c => new ProgramSkillWindowContext(_facts.GetProgramTrigger(c).Window, change.Id, _facts.PlayerSeat(owner),
                SourceSeat: change.SourceSeat, TargetSeat: _facts.PlayerSeat(owner), Amount: change.Amount, OccurrenceIndex: c.OccurrenceIndex, Facts: facts, HpChange: change)).ToArray();
            _frames.ReplaceTop(window with { ResponseCompletion = r with { ActiveHealthChildFrameId = change.Id } });
            _frames.Push(new HpChangedTriggerWindowFrame(change.Id, change, Array.AsReadOnly(candidates), Array.AsReadOnly(contexts),
                PostEventContinuation.ResponseCompletion, window.Id));
            _frames.AdvanceTop<HpChangedTriggerWindowFrame>(); return true;
        }
        return false;
    }

    internal bool IsResponseCompletionHealthChild(ProgramCardTriggerWindowFrame window, ResolutionFrame child)
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
    internal bool ReturnCardResponseCompletionRecovery(RecoveryReplacementFrame child)
    {
        if (child.Return.Continuation != PostEventContinuation.ResponseCompletion) return false;
        if (_frames.Top is not ProgramCardTriggerWindowFrame window || !IsResponseCompletionHealthChild(window, child))
            throw new InvalidOperationException("A response cost recovery lost its exact typed return.");
        var r = window.ResponseCompletion!;
        _frames.ReplaceTop(window with { ResponseCompletion = r with { CostRecoveryCursor = r.CostRecoveryCursor + 1, ActiveHealthChildFrameId = null } });
        _frames.AdvanceTop<ProgramCardTriggerWindowFrame>(); return true;
    }
    internal bool ReturnCardResponseCompletionHpChange(HpChangedTriggerWindowFrame child)
    {
        if (child.Continuation != PostEventContinuation.ResponseCompletion) return false;
        if (_frames.Top is not ProgramCardTriggerWindowFrame window || !IsResponseCompletionHealthChild(window, child))
            throw new InvalidOperationException("A response cost HP observer lost its exact typed return.");
        var r = window.ResponseCompletion!;
        _frames.ReplaceTop(window with { ResponseCompletion = r with { CostHealthCursor = r.CostHealthCursor + 1, ActiveHealthChildFrameId = null } });
        _frames.AdvanceTop<ProgramCardTriggerWindowFrame>(); return true;
    }
}
