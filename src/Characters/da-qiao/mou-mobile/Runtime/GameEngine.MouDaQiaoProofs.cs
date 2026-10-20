namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidMouDaQiaoGuose(ProgramSkillFrame f) => f.MouDaQiaoGuose is { } r && f.SkillId == MouDaQiaoComposition.Guose && f.InstructionIndex == 1 &&
        r.Source == MouDaQiaoSource(f) && r.GameplayHash == f.GameplayHash && Enum.IsDefined(r.Stage) &&
        ProgramEventHistory<MouDaQiaoGuoseReservedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.Phase == r.Phase && e.Branch == r.Branch && e.TargetSeat == r.TargetSeat) == 1 &&
        (!r.Paid || ProgramEventHistory<MouDaQiaoGuosePaidEvent>().Count(e => e.FrameId == f.Id && e.Branch == r.Branch && e.CardId == r.CardId && e.From == r.From) == 1);
    private bool ValidMouDaQiaoPhase(ProgramSkillFrame f) => f.MouDaQiaoLiuliPhase is { } r && f.SkillId == MouDaQiaoComposition.Liuli && f.TriggerId == "marked-turn-start" && f.InstructionIndex == 1 &&
        r.RecipientSeat == f.OwnerSeat && f.GameplayHash == r.GameplayHash && f.WindowContext is { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } c && c.ParentFrameId == r.WindowId &&
        ProgramEventHistory<MouDaQiaoLiuliMarkGrantedEvent>().Count(e => e.GrantId == r.GrantId && e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.RecipientSeat == r.RecipientSeat) == 1 &&
        ProgramEventHistory<MouDaQiaoLiuliPhaseIssuedEvent>().Count(e => e.FrameId == f.Id && e.GrantId == r.GrantId && e.ActualTurn == r.ActualTurn && e.WindowId == r.WindowId) == 1;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 31300)]
    private void AssertMouDaQiao(ProgramSkillFrame f)
    {
        if (f.MouDaQiaoGuose is not null && !ValidMouDaQiaoGuose(f) || f.MouDaQiaoLiuliPhase is not null && !ValidMouDaQiaoPhase(f) ||
            f.MouDaQiaoLiuli is { } r && (!PaidMouDaQiaoLiuli(f) || r.Source != MouDaQiaoSource(f) || r.GameplayHash != f.GameplayHash ||
                f.SelectedTargetSeats is not [var target] || target != r.RedirectTargetSeat || ProgramEventHistory<MouDaQiaoLiuliCostFrozenEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.CardId == r.PaidCardId && e.Suit == r.PaidSuit) != 1))
            throw new InvalidOperationException("Mou Da Qiao lost its issued native payment, redirect target or inserted-phase owner.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 31300)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 31300)]
    private bool ContinuePaidMouDaQiao(ProgramSkillFrame f) => f.MouDaQiaoGuose is { Paid: true } && ValidMouDaQiaoGuose(f) ||
        PaidMouDaQiaoLiuli(f) || ValidMouDaQiaoPhase(f) || f.WindowContext is { } c &&
        IsIssuedMouDaQiaoPhaseCandidate(new(f.OwnerSeat, f.SkillId, f.TriggerId ?? "", f.SkillInstanceId, f.GameplayHash, 0), c);
    [ProgramContinuation(ProgramContinuationKind.Resume, 31300)]
    private bool ResumeMouDaQiao(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id) return false;
        if (f.MouDaQiaoLiuliPhase is not null && IsReturningMouDaQiaoLiuliPhase(f)) { FinishMouDaQiaoLiuliPhase(f); return true; }
        if (f.MouDaQiaoGuose is not null) { ContinueMouDaQiaoGuose(id); return true; }
        return false;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 31300)]
    private bool ReturnMouDaQiaoMovement(ProgramSkillFrame f) => f.MouDaQiaoGuose is not null && ResumeMouDaQiao(f.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 31300)]
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 31300)]
    private bool MouDaQiaoMovementProof(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation m) => f.MouDaQiaoGuose is not null && ValidMouDaQiaoGuose(f) &&
        e?.Op is (SkillProgramEffectOp.ResolveMouDaQiaoGuose or SkillProgramEffectOp.ResolveMouDaQiaoGuoseRemove) && m.SubjectSeat == f.OwnerSeat && m.BeforeCount == 0 && m.CoverageResultBind is null;
    private bool MouDaQiaoFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouDaQiaoGuose(f)) return false;
        var r = f.MouDaQiaoGuose!;
        if (child is CardUseFrame use) return r.Stage == MouDaQiaoGuoseStage.Using && use.Id == r.UseId && use.SourceSeat == f.OwnerSeat && use.CardKind == CardKind.Indulgence && use.CardId == r.CardId &&
            use.Action is { PhysicalCards: [var physical] } a && physical.CardId == r.CardId && physical.From == r.From && a.ConversionChain.SequenceEqual([r.Source]);
        if (f.NativeDraw is not null && NativeDrawAttemptRidesOn(child, f)) return true;
        if (RecoveryReplacementFrameRidesOn(child, f)) return true;
        if (child is HpChangedTriggerWindowFrame hp) return ProgramHpReturnMatches(f, hp);
        if (child is ProgramLifecycleTriggerWindowFrame state) return state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
            state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
        return child is CardsMovedTriggerWindowFrame moved && ProgramMovementReturnMatches(f, moved) && moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After);
    }
    private bool MouDaQiaoObserverSuffix(int index) => HasObserverSuffix(index, static (g, i) => g.IsMovementObserverEdge(i) || g.IsSameNameHandObserverEdge(i) ||
        g.IsYanPuDyingExitEdge(i) || g.TypedDamagePostEventEdge(g._resolutionStack[i - 1], g._resolutionStack[i]) ||
        g._resolutionStack[i - 1] is ProgramSkillFrame f && g.MouDaQiaoFirstChild(f, g._resolutionStack[i]) ||
        NativeDrawAttemptRidesOn(g._resolutionStack[i], g._resolutionStack[i - 1]) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[i], g._resolutionStack[i - 1]),
        static (g, i, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(i, dying));
    private ProgramSkillFrame? MouDaQiaoObserverRoot() => FindProgramObserverRoot(static (g, f, c) => g.MouDaQiaoFirstChild(f, c), static (g, i) => g.MouDaQiaoObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 31300)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 31300)]
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 31300)]
    private bool MouDaQiaoDamageObserver(long id) => MouDaQiaoObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 31300)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 31300)]
    private bool MouDaQiaoDying() => ActiveDying is { } dying && MouDaQiaoObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 31300)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 31300)]
    private bool MouDaQiaoRecovery(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (!ValidMouDaQiaoGuose(f)) return false;
        if (RecoveryReplacementFrameRidesOn(recovery, f)) return true;
        var parent = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return parent >= 0 && child > parent + 1 && MouDaQiaoFirstChild(f, _resolutionStack[parent + 1]) && MouDaQiaoObserverSuffix(parent) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 31300)]
    private bool AdvanceMouDaQiaoChildren() => _pendingDecision is null && MouDaQiaoObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
