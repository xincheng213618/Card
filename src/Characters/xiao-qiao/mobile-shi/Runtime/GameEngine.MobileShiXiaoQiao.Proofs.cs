namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidShiXq(ProgramSkillFrame f) => f.ShiXiaoQiao is { } r && f.InstructionIndex == 1 && r.Source == ShiXqSource(f) && r.Hash == f.GameplayHash &&
        _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash == r.Hash && r.Turn == _turnNumber && r.Round == _roundNumber &&
        r.ParentId == (f.WindowContext?.ParentFrameId ?? f.ParentFrameId ?? 0) && Enum.IsDefined(r.Stage) && r.Before >= 0 && r.After >= r.Before && r.After <= _movementSequence &&
        GetPausedPrivateOfferEffect(f)?.Op == r.Operation && ProgramEventHistory<ShiXqStartedEvent>().Count(e => e == new ShiXqStartedEvent(f.Id, r.Source, r.Hash, r.Turn, r.Round, r.ParentId, r.Operation)) == 1;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 50400)] private void RequireShiXq(ProgramSkillFrame f)
    {
        if (f.ShiXiaoQiao is not { } r) return;
        if (!ValidShiXq(f) || r.RevokeIds.Count != r.RevokeIds.Distinct(StringComparer.Ordinal).Count() || r.RevokeIds.Any(id => !ShiXqGrantLedger(f.OwnerSeat).Any(e => e.GrantId == id)) ||
            r.Operation == SkillProgramEffectOp.ResolveMobileShiXiaoQiaoHeyun && (f.SkillId != ShiXqHeyun || f.TriggerId is not null || f.ActivationId != "lose-for-draw" || f.WindowContext is not null ||
                r.Paid && (r.SelectedSkill is null || !IsValidPlayerSeat(r.Target) || ProgramEventHistory<ShiXqHeyunPaidEvent>().Count(e => e == new ShiXqHeyunPaidEvent(f.Id, r.Source, r.Target, r.SelectedSkill)) != 1)) ||
            r.Operation == SkillProgramEffectOp.ResolveMobileShiXiaoQiaoYinhui && (f.SkillId != ShiXqYinhui || f.TriggerId != "copy-current-skill" || f.WindowContext?.Window != SkillProgramTriggerWindow.RoundStarting ||
                r.GrantId is not null && (r.GrantSource != ShiXqGrantSource(f.OwnerSeat, f.Id) || r.GrantId != r.GrantSource + ":" + r.SelectedSkill ||
                    ProgramEventHistory<ShiXqCopiedEvent>().Count(e => e.FrameId == f.Id && e.GrantId == r.GrantId && e.GrantSourceId == r.GrantSource && e.SkillId == r.SelectedSkill && e.SkillInstanceId == r.GrantedInstance) != 1)) ||
            r.Draw is { } draw && (r.Operation != SkillProgramEffectOp.ResolveMobileShiXiaoQiaoHeyun || draw.RecipientSeat != r.Target || draw.RequestedCount != 2 ||
                ProgramEventHistory<ShiXqDrawPaidEvent>().Count(e => e.FrameId == f.Id && e.DrawId == draw.AttemptFrameId && e.Recipient == r.Target && e.ActualCount == draw.ActualDrawIds.Count &&
                    e.Replaced == (draw.Replacement is not null) && e.Before == draw.SequenceBefore && e.After == draw.SequenceAfter) != 1))
            throw new InvalidOperationException("Shi Xiao Qiao lost its original instruction/source, exact issued acquisition, skill payment or once-paid native draw.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && f.Step == ResolutionFrameStep.AwaitingResponse && r.Stage is ShiXqStage.Target or ShiXqStage.LoseSkill or ShiXqStage.CopyTarget or ShiXqStage.CopySkill &&
            (_pendingDecision is not { } p || p.PlayerSeat != f.OwnerSeat || !p.IsPrivate || !AssistedChoicesEqual(p.Choices, ShiXqChoices(f))))
            throw new InvalidOperationException("Shi Xiao Qiao lost its exact current public-skill choices.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 50400)]
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 50400)]
    [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments, 50400)]
    private bool ShiXqMovementProof(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => f.ShiXiaoQiao is { Paid: true } r && ValidShiXq(f) && e?.Op == r.Operation &&
        p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null &&
        r.Stage is ShiXqStage.LossChildren or ShiXqStage.RevokeChildren or ShiXqStage.GrantChildren or ShiXqStage.DrawChildren;
    private bool ShiXqFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidShiXq(f) || f.ShiXiaoQiao is not { Paid: true } r) return false;
        if (child is NativeDrawAttemptFrame draw) return r.Stage == ShiXqStage.Drawing && draw.Context.RecipientSeat == r.Target && draw.Context.RequestedCount == 2 && NativeDrawAttemptRidesOn(draw, f);
        if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is { } p && ShiXqMovementProof(f, GetPausedPrivateOfferEffect(f), p) &&
            ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
            moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After && _cardMovements.Contains(m));
        if (child is RecoveryReplacementFrame recovery) return recovery.ParentFrameId == f.Id && recovery.Return.ResumeFrameId == f.Id && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement;
        if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && state.CharacterStateContinuation == CharacterStateContinuation.Program);
    }
    private bool ShiXqChildEdge(int i) => SkillInvocationStructuralEdge(_resolutionStack[i - 1], _resolutionStack[i]) || IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) ||
        TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) || _resolutionStack[i - 1] is ProgramSkillFrame f && ShiXqFirstChild(f, _resolutionStack[i]) ||
        NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]);
    private bool ShiXqChildSuffix(int i) => HasObserverSuffix(i, static (g, n) => g.ShiXqChildEdge(n),
        static (g, n, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(n, dying) || g.TieredRoundZeroDyingRescueRide(n, dying) || g.DrawFundedDistinctBasicDyingRescueRide(n, dying) || g.HuanpeiDyingRescueRide(n, dying));
    private ProgramSkillFrame? ShiXqObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.ShiXqFirstChild(f, child), static (g, i) => g.ShiXqChildSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 50400)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 50400)]
    private bool ShiXqDamageObserver(long id) => ShiXqObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 50400)] private bool ShiXqBeforeDamageObserver(long id) => ShiXqObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 50400)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 50400)]
    private bool ShiXqDyingObserver() => ActiveDying is { } dying && ShiXqObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 50400)] [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 50400)]
    private bool ShiXqNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || ShiXqObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature && target == (e.TargetReference is { } reference ? ResolveProgramParticipant(f, reference) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 50400)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 50400)]
    private bool ShiXqRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (ShiXqFirstChild(f, recovery)) return true;
        var i = _resolutionStack.FindIndex(p => p.Id == f.Id); var child = _resolutionStack.FindIndex(p => p.Id == recovery.Id);
        return i >= 0 && child > i + 1 && ShiXqFirstChild(f, _resolutionStack[i + 1]) && ShiXqChildSuffix(i) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 50400)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 50400)]
    private bool KeepShiXqPaid(ProgramSkillFrame f) => f.ShiXiaoQiao is { Paid: true } && ValidShiXq(f);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 50400)]
    private bool AdvanceShiXqChildren() => _pendingDecision is null && ShiXqObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
