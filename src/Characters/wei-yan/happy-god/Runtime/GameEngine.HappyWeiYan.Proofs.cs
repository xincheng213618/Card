namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 42300)] private void RequireHappyWeiYan(ProgramSkillFrame frame)
    {
        if (frame.HappyWeiYan is not { } receipt) return;
        var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
        var initialTarget = receipt.Operation switch { SkillProgramEffectOp.ResolveHappyWeiYanZigu => frame.TriggerId == "first-equipment" ? frame.OwnerSeat : HappyZiguFrozenCounterpart(frame.OwnerSeat, frame.WindowContext!), SkillProgramEffectOp.ResolveHappyWeiYanEzhi => receipt.OriginalAction?.ActorSeat ?? -1, SkillProgramEffectOp.ResolveHappyWeiYanGujing => frame.SelectedTargetSeats.SingleOrDefault(-1), _ => frame.OwnerSeat };
        if (frame.InstructionIndex != 1 || receipt.Source != HappyWeiYanSource(frame) || receipt.GameplayHash != frame.GameplayHash || receipt.ActualTurn != _turnNumber || !Enum.IsDefined(receipt.Stage) ||
            receipt.ParentId != (frame.WindowContext?.ParentFrameId ?? 0) || index < 0 || frame.TriggerId is not null && (index < 1 || _resolutionStack[index - 1].Id != receipt.ParentId) || receipt.Before < 0 || receipt.After < receipt.Before || receipt.After > _movementSequence ||
            receipt.Paid.Any(m => m.Sequence <= receipt.Before || m.Sequence > receipt.After || _cardMovements.Count(actual => actual == m) != 1) ||
            ProgramEventHistory<HappyWeiYanStartedEvent>().Count(e => e == new HappyWeiYanStartedEvent(frame.Id, receipt.Source, receipt.GameplayHash, receipt.Operation, receipt.ParentId, receipt.ActualTurn, initialTarget, receipt.UseId, receipt.ActionId)) != 1)
            throw new InvalidOperationException("Happy Wei Yan lost his original native parent, source, actual turn or paid entity movement.");
        if (receipt.Operation == SkillProgramEffectOp.ResolveHappyWeiYanEzhi)
        {
            if (receipt.OriginalAction is not { } action || frame.WindowContext is not { } context || !TryHappyEzhiAction(context, out var actual, out var parentId) || !SameAnniversaryHzAction(action, actual) || receipt.NativeParentId != parentId || receipt.Target != action.ActorSeat || action.ActorSeat == frame.OwnerSeat ||
                _resolutionStack[index - 1] is not ProgramCardTriggerWindowFrame window || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count || !MountObserverCandidateMatches(frame, ToSharedCandidate(window.Candidates[window.CandidateIndex])) ||
                receipt.Draw is { } draw && !ValidHappyWeiYanDraw(draw, frame.OwnerSeat, frame.Id))
                throw new InvalidOperationException("Ezhi lost its physical true Use, bound native candidate or once-paid draw.");
        }
        else if (receipt.Operation == SkillProgramEffectOp.ResolveHappyWeiYanBoneReturn)
        {
            if (_resolutionStack[index - 1] is not DeferredTurnEndFrame ended || !IsActualAfterTurnEndedParent(ended) || !MountObserverCandidateMatches(frame, AfterTurnEndedCandidate(ended)) || receipt.Cursor < 0 || receipt.Cursor > receipt.Discarded.Count ||
                !receipt.Discarded.OrderBy(b => b.Sequence).SequenceEqual(receipt.Discarded) || receipt.Discarded.Select(b => b.Sequence).Distinct().Count() != receipt.Discarded.Count ||
                receipt.Discarded.Any(b => _cardMovements.SingleOrDefault(m => m.Sequence == b.Sequence) is not { } move || move.CardId != b.CardId || move.TurnNumber != receipt.ActualTurn || move.CardKind != b.PrintedKind || move.From != CardLocation.Equipment(b.Holder) || move.To != CardLocation.DiscardPile || HappyBoneAt(b.CardId, move.From, b.Sequence - 1) is not { } original || original.PrintedKind != b.PrintedKind || original.Slot != b.Slot))
                throw new InvalidOperationException("Ezhi lost the actual completed turn's frozen chronological discarded bones.");
        }
        else if (receipt.Operation == SkillProgramEffectOp.ResolveHappyWeiYanGujing)
        {
            if (frame.TriggerId is not null || frame.ActivationId != "bone-path" || frame.SelectedTargetSeats is not [var selected] || selected != receipt.Target || receipt.Target == frame.OwnerSeat || receipt.Cursor < 0 || receipt.Cursor > receipt.SelectedBones.Count ||
                receipt.SelectedBones.Select(b => b.Holder).Distinct().Count() != receipt.SelectedBones.Count || receipt.SelectedBones.Any(b => b.Holder == frame.OwnerSeat))
                throw new InvalidOperationException("Gujing lost its selected other-role placement, unique bone holders or limited activation.");
        }
        else if (frame.TriggerId == "first-equipment")
        {
            if (_resolutionStack[index - 1] is not ProgramCardTriggerWindowFrame parent || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || !MountObserverCandidateMatches(frame, ToSharedCandidate(parent.Candidates[parent.CandidateIndex])) ||
                HappyZiguFirstEquipment(frame.OwnerSeat, frame.WindowContext!) is not { } use || use.Id != receipt.UseId || use.Action?.ActionId != receipt.ActionId)
                throw new InvalidOperationException("Zigu lost its first real equipment Use and exact accepted candidate.");
        }
        else if (_resolutionStack[index - 1] is not DamageTriggerWindowFrame parent || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            parent.SourceSeat != frame.WindowContext!.SourceSeat || parent.TargetSeat != frame.WindowContext.TargetSeat || !MountObserverCandidateMatches(frame, parent.Candidates[parent.CandidateIndex].ToProgramCandidate()))
            throw new InvalidOperationException("Zigu lost its sourced positive damage occurrence.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 42300)] private bool HappyWeiYanMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation pending)
    {
        if (frame.HappyWeiYan is not { } receipt || effect?.Op != receipt.Operation || pending != frame.PendingMovementContinuation || pending.SubjectSeat != frame.OwnerSeat || pending.BeforeCount != 0 || pending.CoverageResultBind is not null ||
            receipt.Stage is not (HappyWeiYanStage.Replacing or HappyWeiYanStage.Placing or HappyWeiYanStage.GainChildren or HappyWeiYanStage.DrawChildren or HappyWeiYanStage.BoneDiscardChildren or HappyWeiYanStage.GujingDiscardChildren)) return false;
        if (receipt.Stage == HappyWeiYanStage.DrawChildren)
        { RequireHappyWeiYan(frame); return receipt.Draw is { } draw && ValidHappyWeiYanDraw(draw, frame.OwnerSeat, frame.Id); }
        if (receipt.Paid.Count == 0 || receipt.Stage != HappyWeiYanStage.GujingDiscardChildren && receipt.Paid.Count != 1 || ProgramEventHistory<HappyWeiYanMovementEvent>().Count(e => e == new HappyWeiYanMovementEvent(frame.Id, receipt.Stage, receipt.Before, receipt.After, receipt.Paid.Count)) != 1) return false;
        RequireHappyWeiYan(frame); return true;
    }
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 42300)] private bool HappyZiguNestedDamage(ProgramSkillFrame frame, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess) =>
        frame.HappyWeiYan is { Stage: HappyWeiYanStage.Damage } receipt && frame.TriggerId == "first-equipment" && receipt.Target == target && amount == 1 && source is null && nature is null && !sourceLess && HappyZiguFirstEquipment(frame.OwnerSeat, frame.WindowContext!) is not null;
    private bool HappyWeiYanFirstChild(ProgramSkillFrame frame, ResolutionFrame child)
    {
        if (frame.HappyWeiYan is not { } receipt) return false;
        if (child is CardUseFrame use) return receipt.Operation == SkillProgramEffectOp.ResolveHappyWeiYanBoneReturn && HappyBoneUseRidesOn(use, frame);
        if (child is CardsMovedTriggerWindowFrame moved) return frame.PendingMovementContinuation is { } pending && HappyWeiYanMovement(frame, GetPausedPrivateOfferEffect(frame), pending) && ProgramMovementReturnMatches(frame, moved) &&
            moved.Batch.ParentFrameId == frame.Id && moved.Batch.OriginOwnerSeat == frame.OwnerSeat && moved.Batch.OriginSkillId == frame.SkillId && moved.Batch.OriginSkillInstanceId == frame.SkillInstanceId && moved.Batch.Movements.All(receipt.Paid.Contains);
        if (child is RecoveryReplacementFrame recovery) return frame.PendingMovementContinuation is not null && receipt.Paid.Any(m => m.From.Zone == CardZoneKind.Equipment && m.CardKind == CardKind.SilverLion && m.From.OwnerSeat == recovery.Attempt.TargetSeat) && RecoveryReplacementFrameRidesOn(recovery, frame) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion;
        if (child is HpChangedTriggerWindowFrame hp) return frame.PendingMovementContinuation is not null && hp.Change.ParentFrameId == frame.Id && hp.ResumeFrameId == frame.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        return receipt.Stage == HappyWeiYanStage.Damage && frame.AttackAttempt is { } attack && !attack.SourceLess && attack.SourceSeat == frame.OwnerSeat && attack.TargetSeat == receipt.Target && frame.AttackReturn is not null && DyingSuitsStructuralEdge(frame, child);
    }
    private ProgramSkillFrame? HappyWeiYanObserverRoot() => FindProgramObserverRoot(static (engine, frame, child) => engine.HappyWeiYanFirstChild(frame, child), static (engine, index) => engine.SameNameHandObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 42300)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 42300)]
    private bool HappyWeiYanDamageObserver(long id) => HappyWeiYanObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 42300)] private bool HappyWeiYanBeforeDamageObserver(long id) => HappyWeiYanObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 42300)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 42300)]
    private bool HappyWeiYanDying() => ActiveDying is { } dying && HappyWeiYanObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 42300)] private bool AdvanceHappyWeiYanObserver() => _pendingDecision is null && HappyWeiYanObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 42300)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 42300)] private bool RetainHappyWeiYanAccepted(ProgramSkillFrame frame)
    { if (frame.HappyWeiYan is null) return false; RequireHappyWeiYan(frame); return true; }
}
