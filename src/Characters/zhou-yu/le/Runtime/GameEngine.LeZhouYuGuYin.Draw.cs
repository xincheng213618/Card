namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome BeginGuYinDraw(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.GuYin is not null || f.InstructionIndex != 1 || f.TriggerId != "initial-entity-discard-draw" ||
            f.ActivationId != f.TriggerId || f.WindowContext is not { } context ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0)
            throw new InvalidOperationException("GuYin requires its original native discard opportunity.");
        var c = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId,
            f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex);
        if (!IsIssuedGuYinCandidate(c, context)) throw new InvalidOperationException("GuYin lost its frozen source and original-entity invoice.");
        var facts = ExactGuYinQualifications(context.MovementBatch!, GuYinSource(f), f.GameplayHash);
        var r = new GuYinReceipt(GuYinSource(f), f.GameplayHash, context.ParentFrameId, facts[0].ActualTurn, facts[0].TurnOwner)
        { Stage = GuYinStage.Drawing, Entities = facts.Select(e => new GuYinDiscardEntity(e.OriginalOwnerSeat,
            e.CardId, e.PrintedKind, e.MovementSequence)).ToArray() };
        ReplaceRuntimeTop(f = f with { GuYin = r });
        AdvanceEventRulesAndQueueFact(new GuYinDrawStartedEvent(f.Id, r.Source, r.GameplayHash, r.BatchId, r.Entities.Count));
        ContinueGuYinDraw(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private bool DrainGuYinDraw(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);

    private void ContinueGuYinDraw(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { GuYin: { } r } f || f.Id != id || _pendingDecision is not null) return;
        AssertGuYin(f);
        if (r.Stage == GuYinStage.DrawChildren)
        {
            if (DrainGuYinDraw(f)) return;
            f = GetActiveProgramFrame(id);
            r = f.GuYin! with { Index = r.Index + 1, Stage = GuYinStage.Drawing, DrawOutcome = null };
            ReplaceRuntimeTop(f = f with { GuYin = r, PendingMovementContinuation = null });
        }
        if (r.Index >= r.Entities.Count || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None)
        {
            AdvanceEventRulesAndQueueFact(new GuYinCompletedEvent(id, r.BatchId,
                ProgramEventHistory<GuYinDrawReturnedEvent>().Count(e => e.FrameId == id)));
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
            FinishProgramSkill(f, true);
            return;
        }
        if (f.NativeDraw is not null) return;
        var e = r.Entities[r.Index];
        AdvanceEventRulesAndQueueFact(new GuYinDrawRequestedEvent(id, r.Index, e.CardId, e.MovementSequence));
        BeginOperationProgramDraw(f, f.OwnerSeat, 1, new(GuYinDrawReason));
    }

    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveGuYinInitialEntityDiscardDraw)]
    private void ReturnGuYinDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.GuYin is not { Stage: GuYinStage.Drawing } r || r.Index >= r.Entities.Count ||
            outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != 1 ||
            ProgramEventHistory<GuYinDrawReturnedEvent>().Any(e => e.FrameId == f.Id && e.Index == r.Index))
            throw new InvalidOperationException("GuYin cannot repeat or substitute a native draw return.");
        ReplaceRuntimeTop(f = f with { GuYin = r with { Stage = GuYinStage.DrawChildren, DrawOutcome = outcome },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new GuYinDrawReturnedEvent(f.Id, r.Index, r.Entities[r.Index].CardId,
            outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.SequenceBefore,
            outcome.SequenceAfter, outcome.AttemptFrameId));
        ContinueGuYinDraw(f.Id);
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 28811)]
    private bool ResumeGuYin(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { GuYin: not null } f || f.Id != id) return false; ContinueGuYinDraw(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 28811)]
    private bool ReturnGuYinMovement(ProgramSkillFrame f)
    { if (f.GuYin is null) return false; ContinueGuYinDraw(f.Id); return true; }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 28811)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 28811)]
    private bool CanContinueIssuedGuYin(ProgramSkillFrame f)
    {
        if (f.SkillId != "le:guyin" || f.TriggerId is null || f.ActivationId != f.TriggerId || f.WindowContext is not { } context ||
            f.InstructionIndex is < 0 or > 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash) return false;
        var c = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId,
            f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex);
        if (f.TriggerId == "mark-initial-entities")
            return ExactOriginalHandLifecycleParent(f, SkillProgramTriggerWindow.GameStarting) &&
                (IsIssuedGuYinInitialCandidate(c, context) || f.InstructionIndex == 1 &&
                    ProgramEventHistory<GuYinInitializedEvent>().Where(e => e.FrameId == f.Id).ToArray() is [var initialized] &&
                    initialized.Source == GuYinSource(f) && initialized.GameplayHash == f.GameplayHash);
        return f.GuYin is not null ? ValidGuYinReceipt(f) : IsIssuedGuYinCandidate(c, context);
    }

    private bool ValidGuYinReceipt(ProgramSkillFrame f)
    {
        if (f.GuYin is not { } r || f.SkillId != "le:guyin" || f.TriggerId != "initial-entity-discard-draw" ||
            f.ActivationId != f.TriggerId || f.InstructionIndex != 1 || f.WindowContext is not { } context ||
            r.Source != GuYinSource(f) || r.GameplayHash != f.GameplayHash || r.BatchId != context.ParentFrameId ||
            !Enum.IsDefined(r.Stage) || r.Index < 0 || r.Index > r.Entities.Count || r.Entities.Count == 0 ||
            r.Entities is not System.Collections.IList { IsReadOnly: true } ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != r.GameplayHash ||
            ProgramEventHistory<GuYinDrawStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var start] ||
            start != new GuYinDrawStartedEvent(f.Id, r.Source, r.GameplayHash, r.BatchId, r.Entities.Count) ||
            context.MovementBatch is not { } batch ||
            !ExactGuYinQualifications(batch, r.Source, r.GameplayHash).Select(e =>
                new GuYinDiscardEntity(e.OriginalOwnerSeat, e.CardId, e.PrintedKind, e.MovementSequence)).SequenceEqual(r.Entities)) return false;
        var c = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId,
            f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex);
        if (!GuYinMotherMatches(c, context)) return false;
        var facts = ExactGuYinQualifications(batch, r.Source, r.GameplayHash);
        if (facts.Any(e => e.ActualTurn != r.ActualTurn || e.TurnOwner != r.TurnOwner)) return false;
        var requests = ProgramEventHistory<GuYinDrawRequestedEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
        var returns = ProgramEventHistory<GuYinDrawReturnedEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
        var returnedCount = r.Index + (r.Stage == GuYinStage.DrawChildren ? 1 : 0);
        return returns.Length == returnedCount && requests.Length >= returnedCount && requests.Length <= r.Index + 1 &&
            requests.Where((e, i) => i >= r.Entities.Count || e.Index != i || e.CardId != r.Entities[i].CardId ||
                e.MovementSequence != r.Entities[i].MovementSequence).Any() == false &&
            returns.Where((e, i) => e.Index != i || e.CardId != r.Entities[i].CardId || e.ActualCount < 0 || e.ActualCount > 1 ||
                e.ReplacementCount < 0 || e.Before < 0 || e.After < e.Before || e.After > _movementSequence).Any() == false &&
            (r.Stage == GuYinStage.Drawing ? r.DrawOutcome is null && f.PendingMovementContinuation is null :
                r.Index < r.Entities.Count && r.DrawOutcome is { } o && o.RecipientSeat == f.OwnerSeat && o.RequestedCount == 1 &&
                returns[^1] == new GuYinDrawReturnedEvent(f.Id, r.Index, r.Entities[r.Index].CardId,
                    o.ActualDrawIds.Count, o.ReplacementGainedIds.Count, o.SequenceBefore, o.SequenceAfter, o.AttemptFrameId) &&
                f.NativeDraw is null && f.PendingMovementContinuation is { } p && IsGuYinMovement(f, GetPausedPrivateOfferEffect(f), p));
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 28811)]
    private void AssertGuYin(ProgramSkillFrame f)
    { if (f.GuYin is not null && !ValidGuYinReceipt(f)) throw new InvalidOperationException("GuYin lost its original arrival, paid recipient cursor or exact native draw return."); }
}
