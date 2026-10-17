namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IResumableProgramDrawHost
    {
        public SkillProgramStepOutcome DrawInstruction(ProgramSkillFrame frame, DrawProgramInstruction instruction,
            int targetSeat, CardMoveReason reason) =>
            engine.ExecuteProgramDrawInstruction(frame, instruction, targetSeat, reason);
    }

    private SkillProgramStepOutcome ExecuteProgramDrawInstruction(ProgramSkillFrame frame,
        DrawProgramInstruction instruction, int targetSeat, CardMoveReason reason)
    {
        if (instruction.TargetReference is { } reference) targetSeat = ResolveProgramParticipant(frame, reference);
        var selected = instruction.Target == SkillProgramEffectTarget.SelectedTargets;
        var recipients = selected ? frame.SelectedTargetSeats : new[] { targetSeat };
        if (selected && (instruction.Amount is not FixedProgramAmount { Value: > 0 } || recipients.Count == 0 ||
            recipients.Distinct().Count() != recipients.Count))
            throw new InvalidOperationException("The selected-target draw has invalid participants or amount.");
        var requested = instruction.Amount switch
        {
            BoundCardCountProgramAmount bound => GetProgramCardSet(frame, bound.SourceBind).CardIds.Count,
            PhaseSkillUsageProgramAmount usage => GetProgramPhaseSkillUsage(frame, usage.UsageId),
            FixedProgramAmount fixedAmount => fixedAmount.Value,
            ExpressionProgramAmount expression => ResolveProgramDrawCount(frame, targetSeat, 0, expression.Expression),
            _ => throw new InvalidOperationException("Draw has an unsupported amount instruction.")
        };
        if (requested is null) return SkillProgramStepOutcome.Continue;
        if (!HasNativeDrawAttemptCapability)
        {
            if (selected) DrawProgramSelectedTargets(frame.Id, requested.Value, reason);
            else DrawProgramCards(frame.Id, targetSeat, requested.Value, null,
                instruction.ResultBind, instruction.Visibility, reason);
            return SkillProgramStepOutcome.Continue;
        }
        if (GetActiveProgramFrame(frame.Id).NativeDraw is not null)
            throw new InvalidOperationException("A Draw instruction already owns an unfinished recipient cursor.");
        ReplaceRuntimeTop(frame with { NativeDraw = new NativeDrawProducerReceipt(frame.InstructionIndex,
            requested.Value, selected ? null : instruction.ResultBind, instruction.Visibility, reason)
            { Recipients = recipients } });
        return ContinueProgramNativeDraw(frame.Id)
            ? SkillProgramStepOutcome.AwaitChild : SkillProgramStepOutcome.Continue;
    }

    private SkillProgramStepOutcome BeginOperationProgramDraw(ProgramSkillFrame frame,
        int recipientSeat, int requestedCount, CardMoveReason reason)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame active || active.Id != frame.Id ||
            active.NativeDraw is not null || !IsValidPlayerSeat(recipientSeat) || requestedCount < 0 ||
            active.InstructionIndex < 1)
            throw new InvalidOperationException("A native operation cannot issue an unowned or duplicate draw request.");
        var program = _contentRegistry.GetSkill(active.SkillId).Program!;
        var plan = ProgramInstructionResolver.Default.Resolve(active, program);
        var operation = plan.Instructions[active.InstructionIndex - 1].Op;
        if (!ProgramNativeDrawReturns.Value.ContainsKey(operation))
            throw new InvalidOperationException("The native draw producer has no typed return handler.");
        ReplaceRuntimeTop(active with { NativeDraw = new NativeDrawProducerReceipt(active.InstructionIndex,
            requestedCount, null, SkillProgramCardSetVisibility.Private, reason)
            { Recipients = [recipientSeat], ReturnKind = NativeDrawAttemptReturnKind.ProgramOperation, Operation = operation } });
        ContinueProgramNativeDraw(frame.Id);
        // The operation's exact return handler owns its following stage even
        // when both the request decision and draw complete synchronously.
        return SkillProgramStepOutcome.AwaitChild;
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 0)]
    private bool ResumeProgramNativeDraw(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { NativeDraw: not null } frame || frame.Id != id)
            return false;
        var yielded = ContinueProgramNativeDraw(id);
        // The cursor may finish synchronously. Its following instruction is still
        // dispatched by the ordinary executor, never by re-running this Draw.
        if (!yielded && _resolutionStack.LastOrDefault() is ProgramSkillFrame { NativeDraw: null } active && active.Id == id)
            AdvanceRuntimeProgram(id);
        return true;
    }

    private bool ContinueProgramNativeDraw(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { NativeDraw: { } paid } frame && frame.Id == id)
        {
            if (paid.ActiveAttemptId is not null && !paid.AttemptReturned)
                throw new InvalidOperationException("A draw producer resumed before its exact request child returned.");
            if (paid.RecipientIndex >= paid.Recipients.Count)
            { ReplaceRuntimeTop(frame with { NativeDraw = null }); return false; }
            var seat = paid.Recipients[paid.RecipientIndex];
            if (!paid.RecipientApplied)
            {
                if (!paid.AttemptReturned && TryBeginNativeDrawAttempt(frame)) return true;
                // Ordinary DrawCards still returns only actual DrawPile materials.
                // A replacement's obtained entities are intentionally excluded.
                var before = frame.FixedRecipient is null ? 0 : PairBenefitSequence;
                var materialBefore = OutsidePhaseMovementSequence;
                var drawn = paid.Replacement is null && _players[seat].IsAlive && _winner == Winner.None
                    ? DrawCards(_players[seat], paid.RequestedCount, true, paid.Reason)
                    : Array.AsReadOnly(Array.Empty<int>());
                if (paid.ReturnKind == NativeDrawAttemptReturnKind.ProgramOperation)
                {
                    var outcome = new NativeDrawOutcome(seat, paid.RequestedCount, paid.ActiveAttemptId,
                        materialBefore, OutsidePhaseMovementSequence, paid.Replacement) { ActualDrawIds = drawn };
                    ReplaceRuntimeTop(frame = GetActiveProgramFrame(id) with { NativeDraw = null });
                    ProgramNativeDrawReturns.Value[paid.Operation!.Value](this, frame, outcome);
                    return true;
                }
                CompleteProgramDrawCards(id, seat, paid.ResultBind, paid.Visibility, paid.Reason, before, drawn);
                frame = GetActiveProgramFrame(id);
                ReplaceRuntimeTop(frame = frame with { NativeDraw = paid = paid with { RecipientApplied = true } });
            }
            // A recipient's observers finish before the next actual attempt.
            if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
                TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
                TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
                TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return true;
            frame = GetActiveProgramFrame(id);
            ReplaceRuntimeTop(frame with { NativeDraw = paid with { RecipientIndex = paid.RecipientIndex + 1,
                ActiveAttemptId = null, AttemptReturned = false, Replacement = null, RecipientApplied = false } });
        }
        return true;
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 27580)]
    private void AssertProgramNativeDraw(ProgramSkillFrame frame, ProgramExecutionPlan plan)
    {
        if (frame.NativeDraw is not { } paid) return;
        if (paid.InstructionIndex != frame.InstructionIndex ||
            paid.InstructionIndex < 1 || paid.InstructionIndex > plan.Instructions.Count ||
            !Enum.IsDefined(paid.ReturnKind) ||
            paid.RequestedCount < 0 || paid.Recipients.Count == 0 ||
            paid.Recipients is not System.Collections.IList { IsReadOnly: true } ||
            paid.Recipients.Any(s => !IsValidPlayerSeat(s)) || paid.Recipients.Distinct().Count() != paid.Recipients.Count ||
            paid.RecipientIndex < 0 || paid.RecipientIndex >= paid.Recipients.Count ||
            paid.ActiveAttemptId is null && (paid.AttemptReturned || paid.Replacement is not null) ||
            paid.Replacement is not null && !paid.AttemptReturned)
            throw new InvalidOperationException("A Draw instruction lost its exact paid cursor or result binding.");
        if (paid.ReturnKind == NativeDrawAttemptReturnKind.ProgramInstruction)
        {
            if (!HasNativeDrawAttemptCapability || paid.Operation is not null ||
                plan.Instructions[paid.InstructionIndex - 1].CompiledInstruction is not DrawProgramInstruction instruction ||
                paid.ResultBind != (instruction.Target == SkillProgramEffectTarget.SelectedTargets ? null : instruction.ResultBind) ||
                paid.Visibility != instruction.Visibility ||
                paid.Reason != new CardMoveReason($"skill-program.{frame.SkillId}.{SkillProgramEffectOp.Draw}"))
                throw new InvalidOperationException("A Draw instruction changed its result contract.");
        }
        else if (paid.Operation != plan.Instructions[paid.InstructionIndex - 1].Op || paid.Operation is not { } operation ||
            !ProgramNativeDrawReturns.Value.ContainsKey(operation) || paid.Recipients.Count != 1 || paid.ResultBind is not null)
            throw new InvalidOperationException("A native draw producer changed its paid operation return.");
        if (paid.AttemptReturned && ProgramEventHistory<NativeDrawAttemptDecidedEvent>().Count(e =>
            e.AttemptFrameId == paid.ActiveAttemptId && e.ParentFrameId == frame.Id &&
            e.RecipientSeat == paid.Recipients[paid.RecipientIndex] && e.RequestedCount == paid.RequestedCount &&
            e.Replaced == (paid.Replacement is not null)) != 1)
            throw new InvalidOperationException("A Draw instruction retained an unissued request return.");
    }
}
