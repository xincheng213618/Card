namespace CardGame.Core;

// The exact paid producer and typed return remain the sole pending state;
// this match-local service retains only dedicated primitive interfaces.
[ProgramRuntimeModule, ProgramCapability]
internal sealed class NativeDrawAttemptRuntime(IProgramRuntimeFrames frames,
    IProgramRuntimeEvents events, IProgramRuntimePrompts prompts,
    INativeDrawAttemptQueries queries, INativeDrawAttemptOperations operations,
    IProgramWindowCursors cursors) : INativeDrawAttemptRuntime
{
    public bool TryBegin(ResolutionFrame parent)
    {
        var paid = parent.NativeDraw ?? throw new InvalidOperationException("A draw attempt has no paid producer.");
        var seat = paid.Recipients[paid.RecipientIndex];
        if (!queries.HasAttemptCapability || paid.RequestedCount <= 0 || !queries.SetupComplete ||
            paid.Reason == CardMoveReasons.InitialDeal || !queries.IsAlive(seat) || queries.Winner != Winner.None)
            return false;
        var facts = queries.CaptureTriggerFacts(seat);
        var candidates = Array.AsReadOnly(queries.CollectCandidates(seat, facts).ToArray());
        if (candidates.Count == 0) return false;
        var id = frames.NextId();
        var context = new NativeDrawAttemptContext(id, parent.Id, seat, paid.RequestedCount, paid.Reason,
            queries.TurnNumber, queries.TurnOwnerSeat, queries.ActualPhase, queries.MovementSequence);
        var returned = new NativeDrawAttemptReturn(paid.ReturnKind,
            paid.InstructionIndex, paid.RecipientIndex);
        var window = new ProgramLifecycleTriggerWindowFrame(frames.NextId(), seat,
            SkillProgramTriggerWindow.NativeDrawStarting, candidates,
            ProgramLifecycleContinuation.ResumeNativeDrawAttempt, facts) { NativeDrawAttempt = context };
        frames.ReplaceTop(parent with { NativeDraw = paid with { ActiveAttemptId = id } });
        var attempt = new NativeDrawAttemptFrame(id, context, returned, window.Id);
        frames.Push(attempt);
        events.AdvanceFact(new NativeDrawAttemptStartedEvent(context, returned));
        frames.Push(window);
        frames.Advance(window.Id);
        return true;
    }

    internal NativeDrawAttemptFrame? ExactNativeDrawAttempt(ProgramSkillWindowContext context)
    {
        if (context.Window != SkillProgramTriggerWindow.NativeDrawStarting || context.NativeDraw is not { } native)
            return null;
        if (frames.Find(context.ParentFrameId) is not ProgramLifecycleTriggerWindowFrame window ||
            window.NativeDrawAttempt != native || window.Window != context.Window ||
            window.Continuation != ProgramLifecycleContinuation.ResumeNativeDrawAttempt ||
            frames.ParentOf(window.Id) is not NativeDrawAttemptFrame attempt || attempt.Id != native.AttemptFrameId ||
            attempt.Context != native || attempt.ActiveWindowFrameId != window.Id ||
            attempt.Stage != NativeDrawAttemptStage.Programs || context.OwnerSeat != native.RecipientSeat ||
            context.SourceSeat != native.RecipientSeat || context.TargetSeat != native.RecipientSeat ||
            context.Amount != native.RequestedCount)
            return null;
        return attempt;
    }

    internal bool IsExactNativeDrawCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        ExactNativeDrawAttempt(context) is { Replacement: null } attempt &&
        candidate.OwnerSeat == attempt.Context.RecipientSeat && queries.IsAlive(candidate.OwnerSeat) &&
        frames.Find(context.ParentFrameId) is ProgramLifecycleTriggerWindowFrame window &&
        window.CandidateIndex >= 0 && window.CandidateIndex < window.Candidates.Count &&
        window.Candidates[window.CandidateIndex] == candidate;

    internal ProgramSkillWindowContext AttachNativeDrawContext(ProgramLifecycleTriggerWindowFrame window,
        ProgramSkillWindowContext context) => window.NativeDrawAttempt is { } native
            ? context with { NativeDraw = native, Amount = native.RequestedCount,
                SourceSeat = native.RecipientSeat, TargetSeat = native.RecipientSeat }
            : context;

    // Called immediately after the replacement's real HE -> Hand movements.
    // Compensation and other children may follow, but cannot unpay this result.
    internal void CompleteNativeDrawReplacement(ProgramSkillFrame frame, int sourceSeat,
        IReadOnlyList<int> gainedIds, CardMoveReason reason, long sequenceBefore)
    {
        if (frames.Top is not ProgramSkillFrame active || active.Id != frame.Id ||
            frame.WindowContext is not { } context || ExactNativeDrawAttempt(context) is not { Replacement: null } attempt ||
            frames.ParentOf(active.Id) is not ProgramLifecycleTriggerWindowFrame window ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_DamageTargetMountObserver.MountObserverCandidateMatches(frame, window.Candidates[window.CandidateIndex]) ||
            context.NativeDraw is not { } native || frame.OwnerSeat != native.RecipientSeat ||
            !queries.IsValidSeat(sourceSeat) || sourceSeat == native.RecipientSeat ||
            sequenceBefore < native.SequenceBefore || sequenceBefore > queries.MovementSequence ||
            gainedIds.Count > native.RequestedCount || gainedIds.Distinct().Count() != gainedIds.Count)
            throw new InvalidOperationException("A draw replacement lost its exact live request or paid materials.");
        var after = queries.MovementSequence;
        var actual = queries.Movements().Where(m => m.Sequence > sequenceBefore && m.Sequence <= after &&
            m.To == CardLocation.Hand(native.RecipientSeat) && m.Reason == reason).OrderBy(m => m.Sequence).ToArray();
        if (!actual.Select(m => m.CardId).SequenceEqual(gainedIds) || actual.Any(m =>
                m.From.OwnerSeat != sourceSeat || m.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)))
            throw new InvalidOperationException("Replacement gains must be the exact original HE materials, never drawn cards.");
        var replacement = new NativeDrawReplacementResult(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId,
            frame.ActivationId, sourceSeat, reason, sequenceBefore, after) { GainedIds = gainedIds };
        frames.Replace(attempt.Id, attempt with { Replacement = replacement });
        operations.CompleteLingkongReplacement(frame, attempt, replacement);
    }

    // A DrawPile replacement has a separate issued entitlement and material
    // contract. It never inherits the HE source-seat exception above.
    internal void CompleteDrawPileSlashReplacement(ProgramSkillFrame frame,
        IReadOnlyList<int> gainedIds, CardMoveReason reason, long sequenceBefore)
    {
        if (frames.Top is not ProgramSkillFrame active || active.Id != frame.Id ||
            frame.WindowContext is not { } context || ExactNativeDrawAttempt(context) is not { Replacement: null } attempt ||
            frames.ParentOf(active.Id) is not ProgramLifecycleTriggerWindowFrame window ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_DamageTargetMountObserver.MountObserverCandidateMatches(frame, window.Candidates[window.CandidateIndex]) ||
            !IsExactNativeDrawCandidate(window.Candidates[window.CandidateIndex], context) ||
            queries.DrawPileSlashReplacementProof(frame, attempt.Context) is not { } proof || proof.ProgramFrameId != frame.Id)
            throw new InvalidOperationException("A DrawPile replacement lost its exact issued request and entitlement.");
        var replacement = new NativeDrawReplacementResult(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId,
            frame.ActivationId, -1, reason, sequenceBefore, queries.MovementSequence)
        {
            SourceKind = NativeDrawReplacementSourceKind.DrawPileSlash,
            DrawPileSlashProof = proof,
            GainedIds = gainedIds
        };
        if (!ValidReplacement(attempt.Context, replacement))
            throw new InvalidOperationException("A DrawPile replacement lost its issued Slash materials.");
        frames.Replace(attempt.Id, attempt with { Replacement = replacement });
    }

    private bool ValidReplacement(NativeDrawAttemptContext context, NativeDrawReplacementResult replacement)
    {
        if (replacement.OwnerSeat != context.RecipientSeat || !Enum.IsDefined(replacement.SourceKind) ||
            replacement.SequenceBefore < context.SequenceBefore || replacement.SequenceAfter < replacement.SequenceBefore ||
            replacement.SequenceAfter > queries.MovementSequence || replacement.GainedIds.Count > context.RequestedCount ||
            replacement.GainedIds is not System.Collections.IList { IsReadOnly: true } ||
            replacement.GainedIds.Distinct().Count() != replacement.GainedIds.Count) return false;
        var material = queries.Movements().Where(m => m.Sequence > replacement.SequenceBefore && m.Sequence <= replacement.SequenceAfter &&
            m.To == CardLocation.Hand(context.RecipientSeat) && m.Reason == replacement.Reason).OrderBy(m => m.Sequence).ToArray();
        if (!material.Select(m => m.CardId).SequenceEqual(replacement.GainedIds)) return false;
        return replacement.SourceKind switch
        {
            NativeDrawReplacementSourceKind.OwnedHandOrEquipment => replacement.DrawPileSlashProof is null &&
                queries.IsValidSeat(replacement.SourceSeat) && replacement.SourceSeat != context.RecipientSeat &&
                material.All(m => m.From.OwnerSeat == replacement.SourceSeat && m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment),
            NativeDrawReplacementSourceKind.DrawPileSlash => replacement.SourceSeat == -1 && replacement.DrawPileSlashProof is not null &&
                material.All(m => m.From == CardLocation.DrawPile && m.CardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) &&
                queries.IsIssuedDrawPileSlashReplacement(context, replacement),
            _ => false
        };
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.NativeDrawStarting)]
    internal void ReturnNativeDrawStartingProgram(ProgramSkillFrame frame, bool completed)
    {
        if (frame.WindowContext is not { } context || ExactNativeDrawAttempt(context) is null ||
            frames.Top is not ProgramLifecycleTriggerWindowFrame window ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_DamageTargetMountObserver.MountObserverCandidateMatches(frame, window.Candidates[window.CandidateIndex]))
            throw new InvalidOperationException("A pre-draw binding lost its exact candidate return.");
        cursors.AdvanceProgramLifecycleCursor(window);
        frames.Advance(window.Id);
    }

    internal void ReturnNativeDrawAttemptWindow(ProgramLifecycleTriggerWindowFrame window)
    {
        if (frames.Top is not NativeDrawAttemptFrame attempt || window.NativeDrawAttempt != attempt.Context ||
            window.Id != attempt.ActiveWindowFrameId || window.CandidateIndex != window.Candidates.Count ||
            attempt.Stage != NativeDrawAttemptStage.Programs)
            throw new InvalidOperationException("A pre-draw window lost its exact request return.");
        frames.ReplaceTop(attempt with { Stage = NativeDrawAttemptStage.Returning });
        frames.Advance(attempt.Id);
    }

    [NativeFrameAutomaticStep(NativeFrameAutomaticPhase.AfterOpening, 20, NativeFrameAutomaticPending.RequireNoneBeforeFrame, NativeFrameAutomaticPublish.AfterAdvance)]
    [NativeFrameRoute(typeof(NativeDrawAttemptFrame), 10)]
    internal void ContinueNativeDrawAttempt(long id)
    {
        if (frames.Top is not NativeDrawAttemptFrame attempt || attempt.Id != id ||
            attempt.Stage != NativeDrawAttemptStage.Returning || prompts.Current is not null ||
            !Enum.IsDefined(attempt.Return.Kind) ||
            frames.ParentOf(attempt.Id) is not { } parent ||
            parent.Id != attempt.Context.ParentFrameId || parent.NativeDraw is not { } paid ||
            paid.ActiveAttemptId != id || paid.AttemptReturned || paid.RecipientApplied ||
            paid.ReturnKind != attempt.Return.Kind || paid.InstructionIndex != attempt.Return.InstructionIndex || paid.RecipientIndex != attempt.Return.RecipientIndex ||
            paid.Recipients[paid.RecipientIndex] != attempt.Context.RecipientSeat || paid.RequestedCount != attempt.Context.RequestedCount)
            throw new InvalidOperationException("A draw request cannot return to an unissued producer or cursor.");
        events.AdvanceFact(new NativeDrawAttemptDecidedEvent(id, parent.Id,
            attempt.Context.RecipientSeat, attempt.Context.RequestedCount, attempt.Replacement is not null));
        frames.Pop(id, ResolutionFrameKind.NativeDrawAttempt);
        frames.ReplaceTop(parent with { NativeDraw = paid with
            { AttemptReturned = true, Replacement = attempt.Replacement } });
        frames.Advance(parent.Id);
    }

    internal void AssertNativeDrawAttempts()
    {
        foreach (var attempt in frames.OfType<NativeDrawAttemptFrame>())
        {
            var context = attempt.Context;
            if (!queries.HasAttemptCapability || attempt.Id != context.AttemptFrameId ||
                frames.ParentOf(attempt.Id) is not { } parent || parent.Id != context.ParentFrameId ||
                parent.NativeDraw is not { } paid || paid.ActiveAttemptId != attempt.Id || paid.AttemptReturned ||
                paid.RecipientApplied || paid.InstructionIndex != attempt.Return.InstructionIndex ||
                paid.RecipientIndex != attempt.Return.RecipientIndex ||
                paid.Recipients[paid.RecipientIndex] != context.RecipientSeat || paid.RequestedCount != context.RequestedCount ||
                paid.Reason != context.Reason || context.RequestedCount <= 0 || context.Reason == CardMoveReasons.InitialDeal ||
                context.ActualTurnNumber != queries.TurnNumber || context.ActualTurnOwnerSeat != queries.TurnOwnerSeat ||
                context.ActualPhase != queries.ActualPhase || context.SequenceBefore > queries.MovementSequence ||
                !Enum.IsDefined(attempt.Stage) || !Enum.IsDefined(attempt.Return.Kind) || attempt.Return.Kind != paid.ReturnKind ||
                (attempt.Return.Kind switch
                {
                    NativeDrawAttemptReturnKind.DrawPhase => parent is not DrawPhaseObligationFrame { Stage: DrawPhaseObligationStage.RequestingDraw },
                    NativeDrawAttemptReturnKind.SoleIncomingRange => parent is not CardUseFrame { SoleIncomingRangeDraw.Stage: SoleIncomingRangeDrawStage.Requesting },
                    NativeDrawAttemptReturnKind.CardUseDrawTwo => parent is not CardUseFrame { NativeCardEffectDraw.Stage: NativeCardEffectDrawStage.Requesting },
                    NativeDrawAttemptReturnKind.GuanduCardEffect => parent is not CardUseFrame { GuanduCardEffect.Stage: GuanduCardEffectStage.Drawing },
                    NativeDrawAttemptReturnKind.WumiaoFlood => parent is not CardUseFrame { WumiaoFlood.Stage: WumiaoFloodStage.Drawing },
                    NativeDrawAttemptReturnKind.DragonBoatCardEffect => parent is not CardUseFrame { DragonBoatCardEffect.Stage: DragonBoatCardEffectStage.Drawing },
                    NativeDrawAttemptReturnKind.EquipmentRecast => parent is not EquipmentRecastFrame { DrawApplied: false },
                    _ => parent is not ProgramSkillFrame
                }) ||
                queries.StartedEvents().Count(e => e.Context == context && e.Return == attempt.Return) != 1 ||
                queries.DecidedEvents().Any(e => e.AttemptFrameId == attempt.Id))
                throw new InvalidOperationException("A pre-draw request lost its exact producer, phase or once-only issuance.");
            if (attempt.Stage == NativeDrawAttemptStage.Programs &&
                (frames.ChildOf(attempt.Id) is not ProgramLifecycleTriggerWindowFrame window ||
                 !global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_NativeDrawAttempts.NativeDrawAttemptRidesOn(window, attempt)))
                throw new InvalidOperationException("A pre-draw request lost its active candidate window.");
            if (attempt.Replacement is { } replacement && !ValidReplacement(context, replacement))
                throw new InvalidOperationException("A draw replacement lost its frozen actual obtained materials.");
        }
        // The attempt child may already have returned while its paid producer
        // is draining movement children. Its typed DrawPile proof stays checked
        // on that original producer as well.
        foreach (var parent in frames.OfType<ResolutionFrame>())
        {
            if (parent.NativeDraw is not { Replacement: { } replacement } paid ||
                replacement.SourceKind == NativeDrawReplacementSourceKind.OwnedHandOrEquipment && replacement.DrawPileSlashProof is null)
                continue;
            var issued = queries.StartedEvents().Where(e => e.Context.AttemptFrameId == paid.ActiveAttemptId &&
                e.Context.ParentFrameId == parent.Id).ToArray();
            if (!paid.AttemptReturned || paid.RecipientIndex < 0 || paid.RecipientIndex >= paid.Recipients.Count ||
                issued is not [var start] || start.Context.RecipientSeat != paid.Recipients[paid.RecipientIndex] ||
                start.Context.RequestedCount != paid.RequestedCount || start.Context.Reason != paid.Reason ||
                start.Return.Kind != paid.ReturnKind || start.Return.InstructionIndex != paid.InstructionIndex ||
                start.Return.RecipientIndex != paid.RecipientIndex || !ValidReplacement(start.Context, replacement))
                throw new InvalidOperationException("A returned DrawPile replacement lost its original producer and material proof.");
        }
    }
}
