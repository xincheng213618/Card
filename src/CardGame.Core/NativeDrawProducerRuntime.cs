namespace CardGame.Core;

// Cursor ownership, observer order and once-only draw payment stay on the
// original ProgramSkillFrame. Dependencies capture ports, never match state.
[ProgramRuntimeModule]
internal sealed class NativeDrawProducerRuntime(IProgramRuntimeFrames frames,
    INativeDrawProducerQueries queries, INativeDrawProducerOperations operations,
    INativeDrawAttemptRuntime attempts)
{
    internal SkillProgramStepOutcome ExecuteProgramDrawInstruction(ProgramSkillFrame frame,
        DrawProgramInstruction instruction, int targetSeat, CardMoveReason reason)
    {
        if (instruction.TargetReference is { } reference) targetSeat = queries.ResolveParticipant(frame, reference);
        var selected = instruction.Target == SkillProgramEffectTarget.SelectedTargets;
        var recipients = selected ? frame.SelectedTargetSeats : new[] { targetSeat };
        if (selected && (instruction.Amount is not FixedProgramAmount { Value: > 0 } || recipients.Count == 0 ||
            recipients.Distinct().Count() != recipients.Count))
            throw new InvalidOperationException("The selected-target draw has invalid participants or amount.");
        var requested = instruction.Amount switch
        {
            BoundCardCountProgramAmount bound => queries.BoundCardCount(frame, bound.SourceBind),
            PhaseSkillUsageProgramAmount usage => queries.PhaseSkillUsage(frame, usage.UsageId),
            FixedProgramAmount fixedAmount => fixedAmount.Value,
            ExpressionProgramAmount expression => operations.ResolveDrawCount(frame, targetSeat, 0, expression.Expression),
            _ => throw new InvalidOperationException("Draw has an unsupported amount instruction.")
        };
        if (requested is null) return SkillProgramStepOutcome.Continue;
        if (!queries.HasAttemptCapability && !queries.IsGameStartingDraw(frame) && !queries.IsGameStartingGainDraw(frame) && !queries.IsLiushiClearDraw(frame))
        {
            if (selected) operations.DrawSelectedTargets(frame.Id, requested.Value, reason);
            else operations.DrawProgramCards(frame.Id, targetSeat, requested.Value,
                instruction.ResultBind, instruction.Visibility, reason);
            return SkillProgramStepOutcome.Continue;
        }
        if (frames.ActiveProgram(frame.Id).NativeDraw is not null)
            throw new InvalidOperationException("A Draw instruction already owns an unfinished recipient cursor.");
        frames.ReplaceTop(frame with { NativeDraw = new NativeDrawProducerReceipt(frame.InstructionIndex,
            requested.Value, selected ? null : instruction.ResultBind, instruction.Visibility, reason)
            { Recipients = recipients } });
        return ContinueProgramNativeDraw(frame.Id)
            ? SkillProgramStepOutcome.AwaitChild : SkillProgramStepOutcome.Continue;
    }

    internal SkillProgramStepOutcome BeginOperationProgramDraw(ProgramSkillFrame frame,
        int recipientSeat, int requestedCount, CardMoveReason reason)
    {
        if (frames.Top is not ProgramSkillFrame active || active.Id != frame.Id ||
            active.NativeDraw is not null || !queries.IsValidSeat(recipientSeat) || requestedCount < 0 ||
            active.InstructionIndex < 1)
            throw new InvalidOperationException("A native operation cannot issue an unowned or duplicate draw request.");
        var plan = queries.ResolvePlan(active);
        var operation = plan.Instructions[active.InstructionIndex - 1].Op;
        if (!queries.HasOperationReturn(operation))
            throw new InvalidOperationException("The native draw producer has no typed return handler.");
        frames.ReplaceTop(active with { NativeDraw = new NativeDrawProducerReceipt(active.InstructionIndex,
            requestedCount, null, SkillProgramCardSetVisibility.Private, reason)
            { Recipients = [recipientSeat], ReturnKind = NativeDrawAttemptReturnKind.ProgramOperation, Operation = operation } });
        ContinueProgramNativeDraw(frame.Id);
        // The operation's exact return handler owns its following stage even
        // when both the request decision and draw complete synchronously.
        return SkillProgramStepOutcome.AwaitChild;
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 0)]
    internal bool ResumeProgramNativeDraw(long id)
    {
        if (frames.Top is not ProgramSkillFrame { NativeDraw: not null } frame || frame.Id != id)
            return false;
        var yielded = ContinueProgramNativeDraw(id);
        // The cursor may finish synchronously. Its following instruction is still
        // dispatched by the ordinary executor, never by re-running this Draw.
        if (!yielded && frames.Top is ProgramSkillFrame { NativeDraw: null } active && active.Id == id)
            frames.AdvanceProgram(id);
        return true;
    }

    internal bool ContinueProgramNativeDraw(long id)
    {
        while (frames.Top is ProgramSkillFrame { NativeDraw: { } paid } frame && frame.Id == id)
        {
            if (paid.ActiveAttemptId is not null && !paid.AttemptReturned)
                throw new InvalidOperationException("A draw producer resumed before its exact request child returned.");
            if (paid.RecipientIndex >= paid.Recipients.Count)
            { frames.ReplaceTop(frame with { NativeDraw = null }); return false; }
            var seat = paid.Recipients[paid.RecipientIndex];
            if (!paid.RecipientApplied)
            {
                if (!paid.AttemptReturned && attempts.TryBegin(frame)) return true;
                if (queries.IsLiushiClearDraw(frame) && (!queries.IsAlive(frame.OwnerSeat) ||
                    !queries.HasSkillInstance(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId)))
                {
                    frames.ReplaceTop(frame with { NativeDraw = null });
                    operations.CancelBinding(frames.ActiveProgram(id), "摸牌付款前斩腕来源已失效。");
                    return true;
                }
                // Ordinary DrawCards still returns only actual DrawPile materials.
                // A replacement's obtained entities are intentionally excluded.
                var before = frame.FixedRecipient is null ? 0 : queries.PairBenefitSequence;
                var materialBefore = queries.MovementSequence;
                var drawn = paid.Replacement is null && queries.IsAlive(seat) && queries.Winner == Winner.None && queries.CanApplyPrepDiscardImmediateDraw(frame)
                    ? operations.DrawCards(seat, paid.RequestedCount, true, paid.Reason)
                    : Array.AsReadOnly(Array.Empty<int>());
                if (paid.ReturnKind == NativeDrawAttemptReturnKind.ProgramOperation)
                {
                    var outcome = new NativeDrawOutcome(seat, paid.RequestedCount, paid.ActiveAttemptId,
                        materialBefore, queries.MovementSequence, paid.Replacement) { ActualDrawIds = drawn };
                    frames.ReplaceTop(frame = frames.ActiveProgram(id) with { NativeDraw = null });
                    operations.ReturnOperation(paid.Operation!.Value, frame, outcome);
                    return true;
                }
                operations.CaptureLiushiPayment(frame, paid.RequestedCount, materialBefore, queries.MovementSequence,
                    drawn.Count, paid.ActiveAttemptId, paid.Replacement is not null);
                operations.CompleteDrawCards(id, seat, paid.ResultBind, paid.Visibility, paid.Reason, before, drawn);
                frame = frames.ActiveProgram(id);
                frames.ReplaceTop(frame = frame with { NativeDraw = paid = paid with { RecipientApplied = true } });
            }
            // A recipient's observers finish before the next actual attempt.
            if (operations.BeginRecoveryReplacement(id) ||
                operations.BeginCharacterStateWindow(id) ||
                operations.BeginHpChangedWindow(id) ||
                operations.BeginCardsMovedWindow(id) || operations.BeginSkillsChangedWindow(id)) return true;
            frame = frames.ActiveProgram(id);
            frames.ReplaceTop(frame with { NativeDraw = paid with { RecipientIndex = paid.RecipientIndex + 1,
                ActiveAttemptId = null, AttemptReturned = false, Replacement = null, RecipientApplied = false } });
        }
        return true;
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 27580)]
    internal void AssertProgramNativeDraw(ProgramSkillFrame frame, ProgramExecutionPlan plan)
    {
        if (frame.NativeDraw is not { } paid) return;
        if (paid.InstructionIndex != frame.InstructionIndex ||
            paid.InstructionIndex < 1 || paid.InstructionIndex > plan.Instructions.Count ||
            !Enum.IsDefined(paid.ReturnKind) ||
            paid.RequestedCount < 0 || paid.Recipients.Count == 0 ||
            paid.Recipients is not System.Collections.IList { IsReadOnly: true } ||
            paid.Recipients.Any(s => !queries.IsValidSeat(s)) || paid.Recipients.Distinct().Count() != paid.Recipients.Count ||
            paid.RecipientIndex < 0 || paid.RecipientIndex >= paid.Recipients.Count ||
            paid.ActiveAttemptId is null && (paid.AttemptReturned || paid.Replacement is not null) ||
            paid.Replacement is not null && !paid.AttemptReturned)
            throw new InvalidOperationException("A Draw instruction lost its exact paid cursor or result binding.");
        if (paid.ReturnKind == NativeDrawAttemptReturnKind.ProgramInstruction)
        {
            if ((!queries.HasAttemptCapability && !queries.IsGameStartingDraw(frame) && !queries.IsGameStartingGainDraw(frame) && !queries.IsLiushiClearDraw(frame)) || paid.Operation is not null ||
                plan.Instructions[paid.InstructionIndex - 1].CompiledInstruction is not DrawProgramInstruction instruction ||
                paid.ResultBind != (instruction.Target == SkillProgramEffectTarget.SelectedTargets ? null : instruction.ResultBind) ||
                paid.Visibility != instruction.Visibility ||
                paid.Reason != new CardMoveReason($"skill-program.{frame.SkillId}.{SkillProgramEffectOp.Draw}"))
                throw new InvalidOperationException("A Draw instruction changed its result contract.");
        }
        else if (paid.Operation != plan.Instructions[paid.InstructionIndex - 1].Op || paid.Operation is not { } operation ||
            !queries.HasOperationReturn(operation) || paid.Recipients.Count != 1 || paid.ResultBind is not null)
            throw new InvalidOperationException("A native draw producer changed its paid operation return.");
        if (paid.AttemptReturned && queries.DecidedEvents().Count(e =>
            e.AttemptFrameId == paid.ActiveAttemptId && e.ParentFrameId == frame.Id &&
            e.RecipientSeat == paid.Recipients[paid.RecipientIndex] && e.RequestedCount == paid.RequestedCount &&
            e.Replaced == (paid.Replacement is not null)) != 1)
            throw new InvalidOperationException("A Draw instruction retained an unissued request return.");
    }
}
