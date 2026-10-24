namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidShiSj(ProgramSkillFrame f) => f.ShiSunJun is { } r && f.SkillId == ShiSjXiongtu && f.InstructionIndex == 1 &&
        r.Source == ShiSjSource(f) && r.Hash == f.GameplayHash && r.Turn == _turnNumber && Enum.IsDefined(r.Stage) &&
        r.Before >= 0 && r.After >= r.Before && r.After <= _movementSequence && IsValidPlayerSeat(r.Target) &&
        ProgramEventHistory<ShiSunJunStartedEvent>().Count(e => e == new ShiSunJunStartedEvent(f.Id, r.Source, r.Hash, r.Turn, r.Target)) == 1;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 49200)]
    private void AssertShiSj(ProgramSkillFrame f)
    {
        if (f.ShiSunJun is not { } r) return;
        if (!ValidShiSj(f) || r.Required is < 0 or > 4 || r.Payment.Count > r.Required || r.Payment.Distinct().Count() != r.Payment.Count ||
            r.Hand.Distinct().Count() != r.Hand.Count || r.Revealed > 0 && !r.Hand.Contains(r.Revealed) ||
            f.TriggerId is null && (f.ActivationId != "show-hand" || r.Phase is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != f.OwnerSeat || f.SelectedTargetSeats is not [var target] || target != r.Target) ||
            f.TriggerId is not null && (f.TriggerId != "other-damage" || f.WindowContext?.DamageFrameId != r.DamageId || r.Target != f.OwnerSeat))
            throw new InvalidOperationException("Shi Sun Jun lost the original frame source, actual phase, frozen branch or native damage receipt.");
        if (r.Stage == ShiSunJunStage.Hand && !GetHand(_players[r.Target]).Select(c => c.Id).SequenceEqual(r.Hand) ||
            r.Stage == ShiSunJunStage.Payment && r.Payment.Any(id => !ShiSjOwnDiscardChoices(f).Any(c => c.Cards.SequenceEqual([id]))))
            throw new InvalidOperationException("Xiongtu lost its unpaid original Hand slot or owned cost entity.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && r.Stage is ShiSunJunStage.Hand or ShiSunJunStage.Branch or ShiSunJunStage.Payment &&
            (_pendingDecision is not { } p || p.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual(p.Choices, ShiSjChoices(f))))
            throw new InvalidOperationException("Xiongtu lost its originally published private choice set.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 49200)]
    [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments, 49200)]
    private bool ShiSjMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => f.ShiSunJun is { } r && ValidShiSj(f) &&
        r.Stage is ShiSunJunStage.DiscardChildren or ShiSunJunStage.CostChildren or ShiSunJunStage.DrawChildren &&
        p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null && p == f.PendingMovementContinuation &&
        e?.Op is SkillProgramEffectOp.ResolveMobileShiXiongtu or SkillProgramEffectOp.ResolveMobileShiXiongtuDamageBenefit;
    private bool ShiSjFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidShiSj(f) || f.ShiSunJun is not { } r) return false;
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == ShiSunJunStage.Drawing;
        if (child is PublicHandRevealWindowFrame reveal) return r.Stage == ShiSunJunStage.RevealChildren && reveal.ParentFrameId == f.Id &&
            reveal.ReturnKind == PublicHandRevealReturnKind.Registered && reveal.ReturnKey == "mobile-shi-sun-jun-hand";
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } pending && ShiSjMovement(f, GetPausedPrivateOfferEffect(f), pending))
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat &&
                moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
        if (child is RecoveryReplacementFrame rec) return rec.ParentFrameId == f.Id && rec.Return.ResumeFrameId == f.Id && rec.Return.Continuation == PostEventContinuation.AwaitedProgramMovement;
        if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && state.CharacterStateContinuation == CharacterStateContinuation.Program);
    }
    private bool ShiSjEdge(int i) => SkillInvocationStructuralEdge(_resolutionStack[i - 1], _resolutionStack[i]) || IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) ||
        IsYanPuDyingExitEdge(i) || TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) ||
        _resolutionStack[i - 1] is ProgramSkillFrame f && ShiSjFirstChild(f, _resolutionStack[i]) ||
        NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]);
    private bool ShiSjSuffix(int n) => _resolutionStack[n + 1] is DyingFrame d && n + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(n + 1, d) ||
        HasObserverSuffix(n, static (e, i) => e.ShiSjEdge(i), static (e, i, dying) => e.IsSuspendedOrPaidRescueObserverSuffix(i, dying));
    private ProgramSkillFrame? ShiSjObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.ShiSjFirstChild(f, c), static (e, n) => e.ShiSjSuffix(n));
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 49200)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 49200)]
    private bool ShiSjDyingChild() => ActiveDying is { } d && ShiSjObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 49200)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 49200)]
    private bool ShiSjDamageChild(long id) => ShiSjObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 49200)]
    private bool ShiSjBeforeDamageChild(long id) => ShiSjObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 49200)] [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 49200)]
    private bool ShiSjNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess || ShiSjObserverRoot() is not { } root || root.Id == f.Id) return false;
        var effect = GetPausedPrivateOfferEffect(f);
        return effect is { Op: SkillProgramEffectOp.Damage } && amount == effect.Amount && source == effect.ActorReference && nature == effect.DamageNature &&
            target == (effect.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, effect.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 49200)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 49200)]
    private bool ShiSjRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (ShiSjFirstChild(f, recovery)) return true;
        var n = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return n >= 0 && c > n + 1 && ShiSjFirstChild(f, _resolutionStack[n + 1]) && ShiSjSuffix(n) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[c - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 49200)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 49200)]
    private bool KeepShiSjPaid(ProgramSkillFrame f) => f.ShiSunJun is { Stage: not ShiSunJunStage.Hand } && ValidShiSj(f);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 49200)]
    private bool AdvanceShiSjChildren() => _pendingDecision is null && ShiSjObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
