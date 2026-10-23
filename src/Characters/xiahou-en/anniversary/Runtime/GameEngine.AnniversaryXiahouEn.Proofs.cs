namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 45000)]
    private void RequireAnniversaryXiahouEn(ProgramSkillFrame frame)
    {
        if (frame.AnniversaryXiahouEn is not { } r) return;
        var index = _resolutionStack.FindIndex(p => p.Id == frame.Id);
        if (index < 1 || frame.InstructionIndex != 1 || frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0 ||
            frame.WindowContext is not { } context || context.OwnerSeat != frame.OwnerSeat || r.Source != XiahouEnSource(frame) || r.Hash != frame.GameplayHash ||
            !AnniversaryXiahouEnComposition.Handles(r.Operation) || GetPausedPrivateOfferEffect(frame)?.Op != r.Operation ||
            r.ParentId != context.ParentFrameId || _resolutionStack[index - 1].Id != r.ParentId || r.ActualTurn != _turnNumber || r.TurnOwner != _currentSeat ||
            r.Before < 0 || r.After < r.Before || r.After > _movementSequence || !Enum.IsDefined(r.Stage))
            throw new InvalidOperationException("Shiwu lost its exact owning program, frozen parent, actual turn or typed stage.");
        if (r.Operation == SkillProgramEffectOp.ResolveAnniversaryXiahouEnShiwu)
        {
            if (!IsForeignActualTurnPindian(frame) || r.Origin is not { } origin || origin.FrameId != frame.Id || origin.InstructionIndex != frame.InstructionIndex ||
                origin.Source != r.Source || origin.Hash != r.Hash || origin.ParentWindowId != r.ParentId || origin.ActualTurn != r.ActualTurn || origin.TurnOwner != r.TurnOwner ||
                origin.UseId <= frame.Id || origin.ActionId <= 0 || r.Stage is not (AnniversaryXiahouEnStage.NativeUse or AnniversaryXiahouEnStage.NativeReturned) ||
                ProgramEventHistory<XiahouEnShiwuIssuedEvent>().Count(e => e.Origin == origin) != 1 ||
                r.Stage == AnniversaryXiahouEnStage.NativeReturned && (LifecycleCardUse(origin.UseId) is not null ||
                    ProgramEventHistory<XiahouEnShiwuReturnedEvent>().Count(e => e == new XiahouEnShiwuReturnedEvent(frame.Id, origin.UseId, origin.ActionId)) != 1))
                throw new InvalidOperationException("Shiwu lost its original native Duel issuance or one-time completion.");
            return;
        }
        if (r.Operation == SkillProgramEffectOp.BequeathAnniversaryXiahouEnChijian)
        {
            if (context.Window != SkillProgramTriggerWindow.OwnerDied || r.Stage != AnniversaryXiahouEnStage.SkillChildren || r.Origin is not null ||
                _resolutionStack[index - 1] is not ProgramDeathTriggerWindowFrame window || window.OwnerSeat != frame.OwnerSeat ||
                window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count || !MountObserverCandidateMatches(frame, window.Candidates[window.CandidateIndex]) ||
                _resolutionStack.OfType<DeathFrame>().SingleOrDefault(d => d.Id == window.DeathFrameId) is not { } death ||
                death.VictimSeat != frame.OwnerSeat || death.KillerSeat != context.SourceSeat || context.SourceSeat is not { } killer || killer == frame.OwnerSeat ||
                ProgramEventHistory<XiahouEnChijianGrantedEvent>().Count(e => e == new XiahouEnChijianGrantedEvent(frame.Id, frame.OwnerSeat, killer, true)) != 1)
                throw new InvalidOperationException("Shiwu bequest lost the real death, killer, original candidate or paid permanent grant.");
            return;
        }
        if (r.Origin is not { } damageOrigin || !ExactOrderedPrintedSkillLossParent(frame, out _, out var damage, out var attack) ||
            damage.Id != r.DamageFrameId || attack.IsSourceLess || attack.SourceSeat != r.DamageSource || attack.TargetSeat != r.DamageTarget || damage.Amount != r.DamageAmount ||
            LifecycleCardUse(attack.ResolutionId) is not { SelectedActorDuelOrigin: { } native } use || native.XiahouEnShiwu != damageOrigin || !ValidXiahouEnShiwuUse(use, native) ||
            ProgramEventHistory<XiahouEnShiwuDamageBoundEvent>().Count(e => e == new XiahouEnShiwuDamageBoundEvent(frame.Id, damageOrigin, r.DamageFrameId,
                r.DamageSource, r.DamageTarget, r.DamageAmount)) != 1 ||
            !(r.DamageSource == frame.OwnerSeat && r.DamageTarget == r.TurnOwner || r.DamageTarget == frame.OwnerSeat && r.DamageSource == r.TurnOwner))
            throw new InvalidOperationException("Shiwu lost its suspended original actual Duel damage participant and bound settlement.");
        if (r.DamageSource == frame.OwnerSeat)
        {
            var moves = _cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= r.After && m.Reason.Value == XiahouEnGainReason).ToArray();
            if (r.Stage != AnniversaryXiahouEnStage.GainChildren || r.GainedCardId is { } id && (id <= 0 || moves is not [var movement] ||
                    movement.CardId != id || movement.From != CardLocation.DrawPile) || r.GainedCardId is null && moves.Length != 0 ||
                ProgramEventHistory<XiahouEnShiwuGainPaidEvent>().Count(e => e == new XiahouEnShiwuGainPaidEvent(frame.Id, r.Before, r.After,
                    moves.Count(m => m.To == CardLocation.Hand(frame.OwnerSeat)))) != 1)
                throw new InvalidOperationException("Shiwu lost its original real draw-pile damage-card acquisition and movement invoice.");
        }
        else if (r.Stage != AnniversaryXiahouEnStage.SkillChildren || r.GainedCardId is not null ||
            ProgramEventHistory<XiahouEnChijianGrantedEvent>().Count(e => e == new XiahouEnChijianGrantedEvent(frame.Id, frame.OwnerSeat, r.DamageSource, false)) != 1)
            throw new InvalidOperationException("Shiwu lost the opponent's independent permanent sword grant.");
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 45000)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 45000)]
    private bool KeepIssuedAnniversaryXiahouEn(ProgramSkillFrame f)
    {
        if (f.AnniversaryXiahouEn is not null) { RequireAnniversaryXiahouEn(f); return true; }
        return f.SkillId == AnniversaryXiahouEnComposition.Shiwu && f.TriggerId == AnniversaryXiahouEnComposition.Death && f.InstructionIndex is 0 or 1 &&
            f.WindowContext is { Window: SkillProgramTriggerWindow.OwnerDied, SourceSeat: { } killer } context && killer != f.OwnerSeat &&
            _resolutionStack.OfType<ProgramDeathTriggerWindowFrame>().LastOrDefault() is { } window && window.Id == context.ParentFrameId && window.OwnerSeat == f.OwnerSeat &&
            window.CandidateIndex >= 0 && window.CandidateIndex < window.Candidates.Count && MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex]);
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 45000)]
    private bool IsAnniversaryXiahouEnPaidMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending)
    {
        if (f.AnniversaryXiahouEn is not { Stage: AnniversaryXiahouEnStage.GainChildren, GainedCardId: > 0 } r ||
            effect?.Op != SkillProgramEffectOp.ResolveAnniversaryXiahouEnShiwuDamage || f.PendingMovementContinuation != pending ||
            pending.SubjectSeat != f.OwnerSeat || pending.BeforeCount != 0 || pending.CoverageResultBind is not null) return false;
        RequireAnniversaryXiahouEn(f); return r.After > r.Before;
    }
    private bool XiahouEnFirstObserverChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.AnniversaryXiahouEn is not { } r) return false;
        RequireAnniversaryXiahouEn(f);
        if (child is CardsMovedTriggerWindowFrame moved) return r.Stage == AnniversaryXiahouEnStage.GainChildren && moved.Batch.ParentFrameId == f.Id &&
            moved.Batch.AwaitingProgramFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId &&
            moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m =>
                _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After && m.CardId == r.GainedCardId && m.Reason.Value == XiahouEnGainReason);
        return child is ProgramLifecycleTriggerWindowFrame skills && r.Stage == AnniversaryXiahouEnStage.SkillChildren &&
            skills.Window == SkillProgramTriggerWindow.SkillsChanged && skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? XiahouEnObserverRoot() => FindProgramObserverRoot(
        static (e, f, child) => e.XiahouEnFirstObserverChild(f, child), static (e, index) => e.SameNameHandObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 45000)]
    private bool HasXiahouEnPaidDamageObserver(long id) => XiahouEnObserverRoot() is { AnniversaryXiahouEn.Operation: SkillProgramEffectOp.ResolveAnniversaryXiahouEnShiwuDamage } root &&
        root.WindowContext?.ParentFrameId == id;
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 45000, group: 10)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 45000)]
    private bool HasXiahouEnObserverDying() => ActiveDying is { } dying && XiahouEnObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 45000)]
    private bool AdvanceXiahouEnObserver() => _pendingDecision is null && XiahouEnObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
