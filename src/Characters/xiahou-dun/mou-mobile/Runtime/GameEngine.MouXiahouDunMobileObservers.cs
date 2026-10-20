namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidMouXiahouDun(ProgramSkillFrame f)
    {
        if (f.MouXiahouDunMobile is not { } r || r.Source != MouXiahouDunSource(f) || r.GameplayHash != f.GameplayHash || f.InstructionIndex != 1 ||
            GetPausedPrivateOfferEffect(f)?.Op != r.Op || !Enum.IsDefined(r.Stage) || r.Before < 0 || r.Before > r.After || r.After > _movementSequence ||
            r.Index < 0 || r.Targets is not System.Collections.IList { IsReadOnly: true } || r.Cards is not System.Collections.IList { IsReadOnly: true } ||
            r.Cards.Distinct().Count() != r.Cards.Count || r.Targets.Distinct().Count() != r.Targets.Count ||
            ProgramEventHistory<MouXiahouDunMobileStartedEvent>().Count(e => e == new MouXiahouDunMobileStartedEvent(f.Id, r.Source, r.GameplayHash, r.Op, r.Phase)) != 1) return false;
        if (r.Op == SkillProgramEffectOp.ResolveMouXiahouDunGanglie)
        {
            if (f.SkillId != MouXiahouDunGanglie || f.ActivationId != "retaliate-all" || f.TriggerId is not null || f.WindowContext is not null ||
                r.Phase is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != f.OwnerSeat || !IsRecordedActualDiscardRecoveryPhase(phase) || r.Cards.Count != 0 || r.Pile is not null || r.Index >= Math.Max(1, r.Targets.Count) ||
                r.Stage is not (MouXiahouDunMobileStage.SelectTargets or MouXiahouDunMobileStage.Damaging)) return false;
            return r.Stage != MouXiahouDunMobileStage.Damaging || r.Targets.Count > 0 && r.Targets.Where((s, i) => !IsValidPlayerSeat(s) ||
                ProgramEventHistory<MouXiahouDunMobileGanglieSpentEvent>().Count(e => e == new MouXiahouDunMobileGanglieSpentEvent(f.Id, f.OwnerSeat, s, i)) != 1).Any() == false;
        }
        if (f.SkillId != MouXiahouDunQingjian || f.ActivationId != f.TriggerId || f.WindowContext is not { } c || c.OwnerSeat != f.OwnerSeat || r.Phase is not null || r.Targets.Count != 0) return false;
        if (r.Op == SkillProgramEffectOp.CollectMouXiahouDunQingjian) return f.TriggerId == "collect-non-use" && c.Window == SkillProgramTriggerWindow.DiscardPileReceived &&
            (r.Pile is null || r.Pile.OwnerSeat == f.OwnerSeat && r.Pile.SkillId == f.SkillId && r.Pile.SkillInstanceId == f.SkillInstanceId) && r.Cards.Count <= 1;
        return r.Op == SkillProgramEffectOp.DistributeMouXiahouDunQingjian && f.TriggerId == "distribute-qingjian" && c.Window == SkillProgramTriggerWindow.PlayEnding &&
            (r.Pile is null && r.Cards.Count == 0 || r.Pile is { } pile && pile.OwnerSeat == f.OwnerSeat && pile.SkillId == f.SkillId && pile.SkillInstanceId == f.SkillInstanceId && r.Index < r.Cards.Count &&
                r.Cards.Where((id, i) => ProgramEventHistory<MouXiahouDunMobileRosterEvent>().Count(e => e == new MouXiahouDunMobileRosterEvent(f.Id, i, id, pile.Location)) != 1).Any() == false);
    }
    private bool IsIssuedMouXiahouDunPile(PublicPersistentPileSource source) => source.SkillId == MouXiahouDunQingjian && _resolutionStack.OfType<ProgramSkillFrame>().Any(f =>
        f.MouXiahouDunMobile is { Op: SkillProgramEffectOp.DistributeMouXiahouDunQingjian } r && r.Pile == source && ValidMouXiahouDun(f));
    private void SealMouXiahouDunMovements(long id, long before)
    {
        var f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f with { MouXiahouDunMobile = f.MouXiahouDunMobile! with { After = _movementSequence } });
        foreach (var m in _cardMovements.Where(m => m.Sequence > before && m.Sequence <= _movementSequence).ToArray())
        {
            var fact = new MouXiahouDunMobileMovementEvent(id, m.CardId, m.From, m.To, m.Reason, m.Sequence);
            if (!ProgramEventHistory<MouXiahouDunMobileMovementEvent>().Contains(fact)) AdvanceEventRulesAndQueueFact(fact);
        }
    }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 31200)]
    private bool IsMouXiahouDunMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => f.MouXiahouDunMobile is { Stage: MouXiahouDunMobileStage.MovementChildren } r &&
        e?.Op == r.Op && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null && ValidMouXiahouDun(f);
    private bool MouXiahouDunFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouXiahouDun(f) || f.MouXiahouDunMobile is not { } r) return false;
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } p && IsMouXiahouDunMovement(f, GetPausedPrivateOfferEffect(f), p))
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId &&
                moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After &&
                    _cardMovements.Contains(m) && ProgramEventHistory<MouXiahouDunMobileMovementEvent>().Contains(new(f.Id, m.CardId, m.From, m.To, m.Reason, m.Sequence)));
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouXiahouDunObserverEdge(int n) => IsMovementObserverEdge(n) || IsSameNameHandObserverEdge(n) || IsYanPuDyingExitEdge(n) || TypedDamagePostEventEdge(_resolutionStack[n - 1], _resolutionStack[n]) ||
        _resolutionStack[n - 1] is ProgramSkillFrame f && MouXiahouDunFirstChild(f, _resolutionStack[n]) || NativeDrawAttemptRidesOn(_resolutionStack[n], _resolutionStack[n - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[n], _resolutionStack[n - 1]);
    private bool MouXiahouDunObserverSuffix(int i) => _resolutionStack[i + 1] is DyingFrame dying && i + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(i + 1, dying) ||
        HasObserverSuffix(i, static (g, n) => g.MouXiahouDunObserverEdge(n), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d));
    private ProgramSkillFrame? MouXiahouDunObserverRoot() => FindProgramObserverRoot(static (g, f, c) => g.MouXiahouDunFirstChild(f, c), static (g, n) => g.MouXiahouDunObserverSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 31200)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 31200)]
    private bool HasMouXiahouDunDamageObserver(long id) => MouXiahouDunObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 31200)]
    private bool HasMouXiahouDunBeforeDamageObserver(long id) => MouXiahouDunObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 31200)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 31200)]
    private bool IsMouXiahouDunDying() => ActiveDying is { } dying && MouXiahouDunObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 31200)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 31200)]
    private bool AllowsMouXiahouDunNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess || MouXiahouDunObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 31200)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 31200)]
    private bool IsMouXiahouDunRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return index >= 0 && child > index + 1 && MouXiahouDunFirstChild(f, _resolutionStack[index + 1]) && MouXiahouDunObserverSuffix(index) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 31200)]
    private bool ResumeMouXiahouDun(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouXiahouDunMobile: { } r } f || f.Id != id) return false;
        if (r.Stage == MouXiahouDunMobileStage.Damaging)
        {
            if (f.AttackAttempt is not null || f.AttackReturn is not null) return true;
            if (!ValidMouXiahouDun(f) || ProgramEventHistory<MouXiahouDunMobileDamageIssuedEvent>().Count(e => e == new MouXiahouDunMobileDamageIssuedEvent(f.Id, r.Index, f.OwnerSeat, r.Targets[r.Index])) != 1)
                throw new InvalidOperationException("Ganglie requires the exact completed native damage before its next target.");
            ReplaceRuntimeTop(f = f with { MouXiahouDunMobile = r with { Index = r.Index + 1 } }); IssueMouXiahouDunDamage(f); return true;
        }
        ContinueMouXiahouDunMovement(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 31200)]
    private bool ReturnMouXiahouDunMovement(ProgramSkillFrame f) => f.PendingMovementContinuation is not null && ResumeMouXiahouDun(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 31200)]
    private bool AdvanceMouXiahouDunChildren() => _pendingDecision is null && MouXiahouDunObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 31200)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 31200)]
    private bool ContinueAcceptedMouXiahouDun(ProgramSkillFrame f) => f.MouXiahouDunMobile is { Stage: not MouXiahouDunMobileStage.SelectTargets } && ValidMouXiahouDun(f);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 31200)]
    private void AssertMouXiahouDunMobile(ProgramSkillFrame f) { if (f.MouXiahouDunMobile is not null && !ValidMouXiahouDun(f)) throw new InvalidOperationException("Mou Xiahou Dun lost its owning source, roster or native cursor."); }
}
