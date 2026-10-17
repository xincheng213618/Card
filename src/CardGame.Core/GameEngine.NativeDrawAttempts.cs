namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasNativeDrawAttemptCapability => _contentRegistry.ProgramDependencies
        .HasTriggerWindow(SkillProgramTriggerWindow.NativeDrawStarting);

    // The child decides a request before any physical draw. The exact producer
    // performs an ordinary draw only after this typed return, retaining its
    // existing movement parent and DrawPile-only invocation proof.
    private bool TryBeginNativeDrawAttempt(ResolutionFrame parent)
    {
        var paid = parent.NativeDraw ?? throw new InvalidOperationException("A draw attempt has no paid producer.");
        var seat = paid.Recipients[paid.RecipientIndex];
        if (!HasNativeDrawAttemptCapability || paid.RequestedCount <= 0 || !_setupComplete ||
            paid.Reason == CardMoveReasons.InitialDeal || !_players[seat].IsAlive || _winner != Winner.None)
            return false;
        var facts = CaptureProgramTriggerFacts(_players[seat]);
        var candidates = Array.AsReadOnly(CollectEligibleProgramTriggerCandidates(_players[seat],
            SkillProgramTriggerWindow.NativeDrawStarting, facts).ToArray());
        if (candidates.Count == 0) return false;
        var id = ++_resolutionSequence;
        var context = new NativeDrawAttemptContext(id, parent.Id, seat, paid.RequestedCount, paid.Reason,
            _turnNumber, _turnProgression.OwnerSeat, CurrentActualDiscardRecoveryPhase(), OutsidePhaseMovementSequence);
        var returned = new NativeDrawAttemptReturn(paid.ReturnKind,
            paid.InstructionIndex, paid.RecipientIndex);
        var window = new ProgramLifecycleTriggerWindowFrame(++_resolutionSequence, seat,
            SkillProgramTriggerWindow.NativeDrawStarting, candidates,
            ProgramLifecycleContinuation.ResumeNativeDrawAttempt, facts) { NativeDrawAttempt = context };
        ReplaceRuntimeTop(parent with { NativeDraw = paid with { ActiveAttemptId = id } });
        var attempt = new NativeDrawAttemptFrame(id, context, returned, window.Id);
        PushRuntimeFrame(attempt);
        AdvanceEventRulesAndQueueFact(new NativeDrawAttemptStartedEvent(context, returned));
        PushRuntimeFrame(window);
        AdvanceRuntimeFrame(window.Id);
        return true;
    }

    private NativeDrawAttemptFrame? ExactNativeDrawAttempt(ProgramSkillWindowContext context)
    {
        if (context.Window != SkillProgramTriggerWindow.NativeDrawStarting || context.NativeDraw is not { } native)
            return null;
        var index = _resolutionStack.FindLastIndex(f => f.Id == context.ParentFrameId);
        if (index < 1 || _resolutionStack[index] is not ProgramLifecycleTriggerWindowFrame window ||
            window.NativeDrawAttempt != native || window.Window != context.Window ||
            window.Continuation != ProgramLifecycleContinuation.ResumeNativeDrawAttempt ||
            _resolutionStack[index - 1] is not NativeDrawAttemptFrame attempt || attempt.Id != native.AttemptFrameId ||
            attempt.Context != native || attempt.ActiveWindowFrameId != window.Id ||
            attempt.Stage != NativeDrawAttemptStage.Programs || context.OwnerSeat != native.RecipientSeat ||
            context.SourceSeat != native.RecipientSeat || context.TargetSeat != native.RecipientSeat ||
            context.Amount != native.RequestedCount)
            return null;
        return attempt;
    }

    private bool IsExactNativeDrawCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        ExactNativeDrawAttempt(context) is { Replacement: null } attempt &&
        candidate.OwnerSeat == attempt.Context.RecipientSeat && _players[candidate.OwnerSeat].IsAlive &&
        _resolutionStack.FirstOrDefault(f => f.Id == context.ParentFrameId) is ProgramLifecycleTriggerWindowFrame window &&
        window.CandidateIndex >= 0 && window.CandidateIndex < window.Candidates.Count &&
        window.Candidates[window.CandidateIndex] == candidate;

    private ProgramSkillWindowContext AttachNativeDrawContext(ProgramLifecycleTriggerWindowFrame window,
        ProgramSkillWindowContext context) => window.NativeDrawAttempt is { } native
            ? context with { NativeDraw = native, Amount = native.RequestedCount,
                SourceSeat = native.RecipientSeat, TargetSeat = native.RecipientSeat }
            : context;

    // Called immediately after the replacement's real HE -> Hand movements.
    // Compensation and other children may follow, but cannot unpay this result.
    private void CompleteNativeDrawReplacement(ProgramSkillFrame frame, int sourceSeat,
        IReadOnlyList<int> gainedIds, CardMoveReason reason, long sequenceBefore)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame active || active.Id != frame.Id ||
            frame.WindowContext is not { } context || ExactNativeDrawAttempt(context) is not { Replacement: null } attempt ||
            _resolutionStack[^2] is not ProgramLifecycleTriggerWindowFrame window ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !MountObserverCandidateMatches(frame, window.Candidates[window.CandidateIndex]) ||
            context.NativeDraw is not { } native || frame.OwnerSeat != native.RecipientSeat ||
            !IsValidPlayerSeat(sourceSeat) || sourceSeat == native.RecipientSeat ||
            sequenceBefore < native.SequenceBefore || sequenceBefore > OutsidePhaseMovementSequence ||
            gainedIds.Count > native.RequestedCount || gainedIds.Distinct().Count() != gainedIds.Count)
            throw new InvalidOperationException("A draw replacement lost its exact live request or paid materials.");
        var after = OutsidePhaseMovementSequence;
        var actual = _cardMovements.Where(m => m.Sequence > sequenceBefore && m.Sequence <= after &&
            m.To == CardLocation.Hand(native.RecipientSeat) && m.Reason == reason).OrderBy(m => m.Sequence).ToArray();
        if (!actual.Select(m => m.CardId).SequenceEqual(gainedIds) || actual.Any(m =>
                m.From.OwnerSeat != sourceSeat || m.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)))
            throw new InvalidOperationException("Replacement gains must be the exact original HE materials, never drawn cards.");
        var replacement = new NativeDrawReplacementResult(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId,
            frame.ActivationId, sourceSeat, reason, sequenceBefore, after) { GainedIds = gainedIds };
        ReplaceRuntimeFrame(attempt.Id, attempt with { Replacement = replacement });
        CompleteLingkongNativeDrawReplacement(frame, attempt, replacement);
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.NativeDrawStarting)]
    private void ReturnNativeDrawStartingProgram(ProgramSkillFrame frame, bool completed)
    {
        if (frame.WindowContext is not { } context || ExactNativeDrawAttempt(context) is null ||
            _resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame window ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !MountObserverCandidateMatches(frame, window.Candidates[window.CandidateIndex]))
            throw new InvalidOperationException("A pre-draw binding lost its exact candidate return.");
        AdvanceProgramLifecycleCursor(window);
        AdvanceRuntimeFrame(window.Id);
    }

    private void ReturnNativeDrawAttemptWindow(ProgramLifecycleTriggerWindowFrame window)
    {
        if (_resolutionStack.LastOrDefault() is not NativeDrawAttemptFrame attempt || window.NativeDrawAttempt != attempt.Context ||
            window.Id != attempt.ActiveWindowFrameId || window.CandidateIndex != window.Candidates.Count ||
            attempt.Stage != NativeDrawAttemptStage.Programs)
            throw new InvalidOperationException("A pre-draw window lost its exact request return.");
        ReplaceRuntimeTop(attempt with { Stage = NativeDrawAttemptStage.Returning });
        AdvanceRuntimeFrame(attempt.Id);
    }

    private void ContinueNativeDrawAttempt(long id)
    {
        if (_resolutionStack.LastOrDefault() is not NativeDrawAttemptFrame attempt || attempt.Id != id ||
            attempt.Stage != NativeDrawAttemptStage.Returning || _pendingDecision is not null ||
            !Enum.IsDefined(attempt.Return.Kind) ||
            _resolutionStack.Count < 2 || _resolutionStack[^2] is not { } parent ||
            parent.Id != attempt.Context.ParentFrameId || parent.NativeDraw is not { } paid ||
            paid.ActiveAttemptId != id || paid.AttemptReturned || paid.RecipientApplied ||
            paid.ReturnKind != attempt.Return.Kind || paid.InstructionIndex != attempt.Return.InstructionIndex || paid.RecipientIndex != attempt.Return.RecipientIndex ||
            paid.Recipients[paid.RecipientIndex] != attempt.Context.RecipientSeat || paid.RequestedCount != attempt.Context.RequestedCount)
            throw new InvalidOperationException("A draw request cannot return to an unissued producer or cursor.");
        AdvanceEventRulesAndQueueFact(new NativeDrawAttemptDecidedEvent(id, parent.Id,
            attempt.Context.RecipientSeat, attempt.Context.RequestedCount, attempt.Replacement is not null));
        PopResolutionFrame(id, ResolutionFrameKind.NativeDrawAttempt);
        ReplaceRuntimeTop(parent with { NativeDraw = paid with
            { AttemptReturned = true, Replacement = attempt.Replacement } });
        AdvanceRuntimeFrame(parent.Id);
    }

    private static bool NativeDrawAttemptRidesOn(ResolutionFrame child, ResolutionFrame parent) =>
        child is NativeDrawAttemptFrame attempt &&
            attempt.Context.ParentFrameId == parent.Id && parent.NativeDraw is { } paid &&
            paid.ActiveAttemptId == attempt.Id && paid.InstructionIndex == attempt.Return.InstructionIndex &&
            paid.RecipientIndex == attempt.Return.RecipientIndex && paid.ReturnKind == attempt.Return.Kind ||
        parent is NativeDrawAttemptFrame native && child is ProgramLifecycleTriggerWindowFrame window &&
            window.Id == native.ActiveWindowFrameId && window.NativeDrawAttempt == native.Context &&
            window.Continuation == ProgramLifecycleContinuation.ResumeNativeDrawAttempt ||
        child is ProgramSkillFrame programChild && parent is ProgramLifecycleTriggerWindowFrame lifecycle &&
            lifecycle.NativeDrawAttempt is { } context && programChild.WindowContext?.NativeDraw == context &&
            programChild.WindowContext.ParentFrameId == lifecycle.Id &&
            lifecycle.CandidateIndex >= 0 && lifecycle.CandidateIndex < lifecycle.Candidates.Count &&
            MountObserverCandidateMatches(programChild, lifecycle.Candidates[lifecycle.CandidateIndex]);

    private void AssertNativeDrawAttempts()
    {
        for (var index = 0; index < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not NativeDrawAttemptFrame attempt) continue;
            var context = attempt.Context;
            if (!HasNativeDrawAttemptCapability || attempt.Id != context.AttemptFrameId || index == 0 ||
                _resolutionStack[index - 1] is not { } parent || parent.Id != context.ParentFrameId ||
                parent.NativeDraw is not { } paid || paid.ActiveAttemptId != attempt.Id || paid.AttemptReturned ||
                paid.RecipientApplied || paid.InstructionIndex != attempt.Return.InstructionIndex ||
                paid.RecipientIndex != attempt.Return.RecipientIndex ||
                paid.Recipients[paid.RecipientIndex] != context.RecipientSeat || paid.RequestedCount != context.RequestedCount ||
                paid.Reason != context.Reason || context.RequestedCount <= 0 || context.Reason == CardMoveReasons.InitialDeal ||
                context.ActualTurnNumber != _turnNumber || context.ActualTurnOwnerSeat != _turnProgression.OwnerSeat ||
                context.ActualPhase != CurrentActualDiscardRecoveryPhase() || context.SequenceBefore > OutsidePhaseMovementSequence ||
                !Enum.IsDefined(attempt.Stage) || !Enum.IsDefined(attempt.Return.Kind) || attempt.Return.Kind != paid.ReturnKind ||
                (attempt.Return.Kind switch
                {
                    NativeDrawAttemptReturnKind.DrawPhase => parent is not DrawPhaseObligationFrame { Stage: DrawPhaseObligationStage.RequestingDraw },
                    NativeDrawAttemptReturnKind.CardUseDrawTwo => parent is not CardUseFrame { NativeCardEffectDraw.Stage: NativeCardEffectDrawStage.Requesting },
                    NativeDrawAttemptReturnKind.GuanduCardEffect => parent is not CardUseFrame { GuanduCardEffect.Stage: GuanduCardEffectStage.Drawing },
                    NativeDrawAttemptReturnKind.DragonBoatCardEffect => parent is not CardUseFrame { DragonBoatCardEffect.Stage: DragonBoatCardEffectStage.Drawing },
                    NativeDrawAttemptReturnKind.EquipmentRecast => parent is not EquipmentRecastFrame { DrawApplied: false },
                    _ => parent is not ProgramSkillFrame
                }) ||
                ProgramEventHistory<NativeDrawAttemptStartedEvent>().Count(e => e.Context == context && e.Return == attempt.Return) != 1 ||
                ProgramEventHistory<NativeDrawAttemptDecidedEvent>().Any(e => e.AttemptFrameId == attempt.Id))
                throw new InvalidOperationException("A pre-draw request lost its exact producer, phase or once-only issuance.");
            if (attempt.Stage == NativeDrawAttemptStage.Programs &&
                (index + 1 >= _resolutionStack.Count || _resolutionStack[index + 1] is not ProgramLifecycleTriggerWindowFrame window ||
                 !NativeDrawAttemptRidesOn(window, attempt)))
                throw new InvalidOperationException("A pre-draw request lost its active candidate window.");
            if (attempt.Replacement is { } replacement &&
                (replacement.OwnerSeat != context.RecipientSeat || !IsValidPlayerSeat(replacement.SourceSeat) ||
                 replacement.SourceSeat == context.RecipientSeat || replacement.SequenceBefore < context.SequenceBefore ||
                 replacement.SequenceAfter < replacement.SequenceBefore || replacement.SequenceAfter > OutsidePhaseMovementSequence ||
                 replacement.GainedIds.Count > context.RequestedCount ||
                 replacement.GainedIds is not System.Collections.IList { IsReadOnly: true } ||
                 replacement.GainedIds.Distinct().Count() != replacement.GainedIds.Count ||
                 !_cardMovements.Where(m => m.Sequence > replacement.SequenceBefore && m.Sequence <= replacement.SequenceAfter &&
                     m.To == CardLocation.Hand(context.RecipientSeat) && m.Reason == replacement.Reason)
                     .OrderBy(m => m.Sequence).Select(m => m.CardId).SequenceEqual(replacement.GainedIds) ||
                 _cardMovements.Any(m => m.Sequence > replacement.SequenceBefore && m.Sequence <= replacement.SequenceAfter &&
                     m.To == CardLocation.Hand(context.RecipientSeat) && m.Reason == replacement.Reason &&
                     (m.From.OwnerSeat != replacement.SourceSeat || m.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)))))
                throw new InvalidOperationException("A draw replacement lost its frozen actual obtained materials.");
        }
    }
}
