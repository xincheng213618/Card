namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool XingDaoRongOriginal(ProgramSkillFrame f)
    {
        if (f.SkillId != XingDaoRongComposition.Skill || f.TriggerId != XingDaoRongComposition.Starting || f.ActivationId != f.TriggerId ||
            f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play || f.WindowContext is not { Window: SkillProgramTriggerWindow.PlayPhaseStarting } c ||
            c.OwnerSeat != f.OwnerSeat || c.SourceSeat != f.OwnerSeat || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.SkillId == f.SkillId && e.BindingId == f.TriggerId && e.SkillInstanceId == f.SkillInstanceId && e.Window == c.Window) != 1) return false;
        var index = _resolutionStack.FindIndex(frame => frame.Id == f.Id);
        return index > 0 && _resolutionStack[index - 1] is PlayPhaseStartingBoundaryFrame p && p.Id == c.ParentFrameId && p.OwnerSeat == f.OwnerSeat &&
            p.ItemIndex >= 0 && p.ItemIndex < p.Items.Count && p.Items[p.ItemIndex].Candidate is { } candidate && MountObserverCandidateMatches(f, candidate);
    }
    private bool ValidXingDaoRong(ProgramSkillFrame f)
    {
        if (f.XingDaoRongXuHe is not { } r || !XingDaoRongOriginal(f) || f.InstructionIndex != 2 || r.Source != XingDaoRongSource(f) || r.Hash != f.GameplayHash ||
            r.ParentId != f.WindowContext!.ParentFrameId || r.ActualTurn != _turnNumber || !Enum.IsDefined(r.Stage) || r.PaidMaximum < 1 ||
            r.Index < 0 || r.Index > r.Targets.Count || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            r.Targets is not System.Collections.IList { IsReadOnly: true } || r.Targets.Distinct().Count() != r.Targets.Count || r.Targets.Any(s => !IsValidPlayerSeat(s)) ||
            ProgramEventHistory<XingDaoRongXuHeStartedEvent>().Count(e => e == new XingDaoRongXuHeStartedEvent(f.Id, r.Source, r.Hash, r.ParentId, r.ActualTurn, r.PaidMaximum)) != 1) return false;
        var plans = ProgramEventHistory<XingDaoRongXuHeModeEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var targets = ProgramEventHistory<XingDaoRongXuHeTargetEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Ordinal).ToArray();
        if (r.Stage == XingDaoRongXuHeStage.Mode) return r.Mode == "" && r.Targets.Count == 0 && r.Index == 0 && plans.Length == 0 && targets.Length == 0 && r.Draw is null && r.Discard is null;
        if (r.Mode is not ("draw" or "discard") || plans is not [var plan] || plan != new XingDaoRongXuHeModeEvent(f.Id, r.Mode, r.Targets.Count) ||
            r.Targets.Count == 0 || !r.Targets.Contains(f.OwnerSeat) || targets.Length != r.Targets.Count ||
            targets.Where((e, i) => e != new XingDaoRongXuHeTargetEvent(f.Id, i, r.Targets[i])).Any()) return false;
        if (r.Stage == XingDaoRongXuHeStage.Completed) return f.PendingMovementContinuation is null;
        if (r.Index >= r.Targets.Count) return r.Stage == XingDaoRongXuHeStage.Next && r.Draw is null && r.Discard is null;
        var target = r.Targets[r.Index];
        if (r.Stage == XingDaoRongXuHeStage.DiscardChildren)
        {
            if (r.Mode != "discard" || r.Draw is not null || r.Discard is not { } m || m.From.OwnerSeat != target || m.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
                m.Sequence <= r.Before || m.Sequence > r.After || !_cardMovements.Contains(m) || m.Reason != XingDaoRongDiscardReason ||
                ProgramEventHistory<XingDaoRongXuHeDiscardedEvent>().Count(e => e.FrameId == f.Id && e.Ordinal == r.Index && e.TargetSeat == target && e.CardId == m.CardId && e.From == m.From && e.To == m.To && e.Sequence == m.Sequence && e.BatchId > 0) != 1) return false;
            return m.To == CardLocation.DiscardPile || IsRedirectedPaidDiscardMovement(m, m.From, XingDaoRongDiscardReason) ||
                m.To == CardLocation.OutsideGame && (IsEphemeralCardDestroyedMaterial(m.CardId, m.From, m.To) || GetAdvancedCard(m.CardId).IsGeneralWeapon);
        }
        if (r.Stage == XingDaoRongXuHeStage.DrawChildren)
        {
            return r.Mode == "draw" && r.Discard is null && r.Draw is { } draw && draw.RecipientSeat == target && draw.RequestedCount == 1 && draw.SequenceBefore == r.Before && draw.SequenceAfter == r.After &&
                draw.ActualDrawIds.Distinct().Count() == draw.ActualDrawIds.Count &&
                ProgramEventHistory<XingDaoRongXuHeDrawnEvent>().Count(e => e == new XingDaoRongXuHeDrawnEvent(f.Id, r.Index, target, draw.ActualDrawIds.Count, r.Before, r.After)) == 1 &&
                draw.ActualDrawIds.All(id => _cardMovements.Count(m => m.Sequence > r.Before && m.Sequence <= r.After && m.CardId == id && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(target) && m.Reason == XingDaoRongDrawReason) == 1);
        }
        return r.Draw is null && r.Discard is null && (r.Stage == XingDaoRongXuHeStage.Next || r.Stage == XingDaoRongXuHeStage.Card && r.Mode == "discard" || r.Stage == XingDaoRongXuHeStage.Drawing && r.Mode == "draw");
    }
    private bool HasAnniversaryXingDaoRongPrompt() => _resolutionStack.LastOrDefault() is ProgramSkillFrame { XingDaoRongXuHe: { Stage: XingDaoRongXuHeStage.Mode or XingDaoRongXuHeStage.Card }, Step: ResolutionFrameStep.AwaitingResponse } f &&
        _pendingDecision is { Kind: DecisionKind.ProgramTrigger } p && p.PlayerSeat == f.OwnerSeat && ValidXingDaoRong(f) && AssistedChoicesEqual(p.Choices, XingDaoRongChoices(f));
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 45700)] private void AssertXingDaoRong(ProgramSkillFrame f)
    { if (f.XingDaoRongXuHe is not null && !ValidXingDaoRong(f)) throw new InvalidOperationException("Xuhe lost original source, paid maximum HP, frozen group or once-paid native result."); }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 45700)] private bool XingDaoRongMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        f.XingDaoRongXuHe is { Stage: XingDaoRongXuHeStage.DiscardChildren or XingDaoRongXuHeStage.DrawChildren } && ValidXingDaoRong(f) &&
        e?.Op == SkillProgramEffectOp.ResolveAnniversaryXingDaoRongXuHe && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool XingDaoRongFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.XingDaoRongXuHe is not { } r || !ValidXingDaoRong(f)) return false;
        if (child is NativeDrawAttemptFrame draw) return r.Stage == XingDaoRongXuHeStage.Drawing && r.Index < r.Targets.Count && draw.Context.RecipientSeat == r.Targets[r.Index] &&
            draw.Context.RequestedCount == 1 && NativeDrawAttemptRidesOn(draw, f);
        if (r.Stage is not (XingDaoRongXuHeStage.DiscardChildren or XingDaoRongXuHeStage.DrawChildren)) return false;
        if (child is CardsMovedTriggerWindowFrame moved) return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id &&
            moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
            moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills) return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (r.Discard is not { CardKind: CardKind.SilverLion, From.Zone: CardZoneKind.Equipment } paid) return false;
        var target = paid.From.OwnerSeat!.Value;
        if (child is RecoveryReplacementFrame recovery) return recovery.ParentFrameId == f.Id && recovery.Return == new RecoveryReplacementReturn(PostEventContinuation.AwaitedProgramMovement, f.Id) &&
            recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason == XingDaoRongDiscardReason && recovery.Attempt.SourceSeat == target && recovery.Attempt.TargetSeat == target && recovery.Attempt.Amount == 1;
        return child is HpChangedTriggerWindowFrame hp && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.ParentFrameId == f.Id &&
            hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == target && hp.Change.TargetSeat == target && hp.Change.Amount == 1;
    }
    private bool XingDaoRongObserverSuffix(int i) => HasObserverSuffix(i,
        static (e, j) => e.IsMovementObserverEdge(j) || NativeDrawAttemptRidesOn(e._resolutionStack[j], e._resolutionStack[j - 1]) ||
            e.RecoveryReplacementFrameRidesOn(e._resolutionStack[j], e._resolutionStack[j - 1]), static (e, j, dying) => e.IsSuspendedOrPaidRescueObserverSuffix(j, dying));
    private ProgramSkillFrame? XingDaoRongObserverRoot() => FindProgramObserverRoot(static (e, f, child) => e.XingDaoRongFirstChild(f, child), static (e, i) => e.XingDaoRongObserverSuffix(i));
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 45700)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 45700)] private bool XingDaoRongRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame r) => XingDaoRongFirstChild(f, r);
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 45700)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 45700)] private bool XingDaoRongDamageObserver(long id) =>
        XingDaoRongObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 45700)] private bool XingDaoRongBeforeDamageObserver(long id) =>
        XingDaoRongObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 45700)] private bool XingDaoRongDyingObserver() => ActiveDying is { } dying && XingDaoRongObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 45700)] private bool AdvanceXingDaoRongObserver() => _pendingDecision is null && XingDaoRongObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
