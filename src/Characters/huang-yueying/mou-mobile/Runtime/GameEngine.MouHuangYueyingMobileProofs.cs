namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidMouHy(ProgramSkillFrame f)
    {
        if (f.MouHuangYueyingMobile is not { } r || r.Source != MouHySource(f) || r.GameplayHash != f.GameplayHash ||
            f.InstructionIndex != 1 || r.After < r.Before || !Enum.IsDefined(r.Stage)) return false;
        if (r.Operation == SkillProgramEffectOp.ResolveMouHuangYueyingJizhi)
            return r.Source.SkillId == MouHuangYueyingMobileComposition.Jizhi && r.RecipientSeat == f.OwnerSeat &&
                ProgramEventHistory<MouHuangYueyingJizhiStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash &&
                    e.WindowId == r.WindowId && e.ActualTurn == r.ActualTurn && e.TurnOwnerSeat == r.TurnOwnerSeat) == 1;
        if (r.Operation == SkillProgramEffectOp.PlaceMouHuangYueyingQicaiEquipment)
            return r.Source.SkillId == MouHuangYueyingMobileComposition.Qicai && r.Phase is { } p &&
                ProgramEventHistory<MouHuangYueyingQicaiReservedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash &&
                    e.RecipientSeat == r.RecipientSeat && e.Phase == p) == 1 && (!r.Paid || r.CardId is not null && r.From is not null);
        return r.Operation == SkillProgramEffectOp.TransferMouHuangYueyingQicaiGains && r.Source.SkillId == MouHuangYueyingMobileComposition.Qicai && r.Source.BindingId == "receive-three" &&
            ProgramEventHistory<MouHuangYueyingQiGainClaimedEvent>().Count(e => e.GrantId == r.GrantId && e.GainSequence == r.ClaimSequence && e.Source == r.Source &&
                e.GameplayHash == r.GameplayHash && e.CardId == r.CardId && e.GainingSeat == r.RecipientSeat && e.BatchId == r.WindowId) == 1;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 31500)]
    private void AssertMouHy(ProgramSkillFrame f)
    { if (f.MouHuangYueyingMobile is not null && !ValidMouHy(f)) throw new InvalidOperationException("Mobile Yueying lost her exact native Draw, placement or original gain debt."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 31500)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 31500)]
    private bool ContinuePaidMouHy(ProgramSkillFrame f) => f.MouHuangYueyingMobile is { Paid: true } && ValidMouHy(f) || f.WindowContext is { } context &&
        IsIssuedMouHyQiCandidate(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, 0, context.OccurrenceIndex), context);
    [ProgramContinuation(ProgramContinuationKind.Resume, 31500)]
    private bool ResumeMouHy(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouHuangYueyingMobile: not null } f || f.Id != id) return false; ContinueMouHy(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 31500)]
    private bool ReturnMouHyMovement(ProgramSkillFrame f) => f.MouHuangYueyingMobile is not null && ResumeMouHy(f.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 31500)]
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 31500)]
    private bool MouHyMovementProof(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation m) => ValidMouHy(f) &&
        e?.Op == f.MouHuangYueyingMobile!.Operation && m.SubjectSeat == f.OwnerSeat && m.BeforeCount == 0 && m.CoverageResultBind is null;
    private bool MouHyFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouHy(f)) return false; var r = f.MouHuangYueyingMobile!;
        if (f.NativeDraw is not null && NativeDrawAttemptRidesOn(child, f)) return true;
        if (RecoveryReplacementFrameRidesOn(child, f)) return true;
        if (child is HpChangedTriggerWindowFrame hp) return ProgramHpReturnMatches(f, hp);
        if (child is ProgramLifecycleTriggerWindowFrame state) return state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
        return child is CardsMovedTriggerWindowFrame moved && ProgramMovementReturnMatches(f, moved) && moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After);
    }
    private bool MouHyObserverSuffix(int i) => HasObserverSuffix(i, static (g, n) => g.IsMovementObserverEdge(n) || g.IsSameNameHandObserverEdge(n) ||
        g.IsYanPuDyingExitEdge(n) || g.TypedDamagePostEventEdge(g._resolutionStack[n - 1], g._resolutionStack[n]) ||
        g._resolutionStack[n - 1] is ProgramSkillFrame f && g.MouHyFirstChild(f, g._resolutionStack[n]) || NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) ||
        g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d));
    private ProgramSkillFrame? MouHyObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.MouHyFirstChild(f, child), static (g, i) => g.MouHyObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 31500)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 31500)]
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 31500)]
    private bool MouHyDamageObserver(long id) => MouHyObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 31500)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 31500)]
    private bool MouHyDying() => ActiveDying is { } d && MouHyObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 31500)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 31500)]
    private bool MouHyRecovery(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (!ValidMouHy(f)) return false;
        if (RecoveryReplacementFrameRidesOn(recovery, f)) return true;
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id); var n = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return i >= 0 && n > i + 1 && MouHyFirstChild(f, _resolutionStack[i + 1]) && MouHyObserverSuffix(i) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[n - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 31500)]
    private bool AdvanceMouHyChildren() => _pendingDecision is null && MouHyObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
