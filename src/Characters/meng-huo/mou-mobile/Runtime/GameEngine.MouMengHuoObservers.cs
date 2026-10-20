namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool MouMhPhaseParent(ProgramSkillFrame f)
    {
        if (f.WindowContext is not { } c || c.OwnerSeat != f.OwnerSeat || c.SourceSeat != f.OwnerSeat) return false;
        return _resolutionStack.SingleOrDefault(w => w.Id == c.ParentFrameId) switch
        {
            PlayPhaseStartingBoundaryFrame p when c.Window == SkillProgramTriggerWindow.PlayPhaseStarting && p.OwnerSeat == f.OwnerSeat &&
                p.ItemIndex >= 0 && p.ItemIndex < p.Items.Count && p.Items[p.ItemIndex].Candidate is { } candidate => MountObserverCandidateMatches(f, candidate),
            ProgramLifecycleTriggerWindowFrame p when p.Window == c.Window && p.OwnerSeat == f.OwnerSeat && p.CandidateIndex >= 0 && p.CandidateIndex < p.Candidates.Count => MountObserverCandidateMatches(f, p.Candidates[p.CandidateIndex]),
            _ => false
        };
    }
    private bool ValidMouMh(ProgramSkillFrame f)
    {
        if (f.MouMengHuo is not { } r || r.Source != MouMhSource(f) || r.GameplayHash != f.GameplayHash || f.InstructionIndex != 1 || f.WindowContext is not { } c ||
            c.ParentFrameId != r.WindowId || c.OwnerSeat != f.OwnerSeat || c.SourceSeat != f.OwnerSeat || f.ActivationId != f.TriggerId ||
            f.SkillId != (r.Obtain ? MouMengHuoComposition.Huoshou : MouMengHuoComposition.Zaiqi) || f.TriggerId != (r.Obtain ? "obtain-at-play-start" : "spend-at-discard-end") ||
            c.Window != (r.Obtain ? SkillProgramTriggerWindow.PlayPhaseStarting : SkillProgramTriggerWindow.DiscardPhaseEnded) ||
            !MouMhPhaseParent(f) || !Enum.IsDefined(r.Stage) || r.Budget is < 0 or > 7 ||
            r.Cursor < 0 || r.Cursor > r.Targets.Count || r.Targets.Count > r.Budget || r.Targets.Distinct().Count() != r.Targets.Count || r.Targets.Any(t => !IsValidPlayerSeat(t)) ||
            r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            ProgramEventHistory<MouMengHuoStartedEvent>().Count(e => e == new MouMengHuoStartedEvent(f.Id, r.Source, r.GameplayHash, r.WindowId, r.Obtain, r.Budget)) != 1) return false;
        if (r.Obtain) return r.Stage == MouMengHuoStage.ObtainChildren && r.Accepted && r.Budget == 0 && r.Targets.Count == 0 && r.Payment is null && r.Draw is null;
        if (!r.Accepted) return r.Stage == MouMengHuoStage.Selecting && r.Cursor == 0 && r.Payment is null && r.Draw is null && !ProgramEventHistory<MouMengHuoChargeSpentEvent>().Any(e => e.FrameId == f.Id);
        var selected = ProgramEventHistory<MouMengHuoTargetSelectedEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
        if (selected.Length != r.Targets.Count || selected.Where((e, i) => e.Index != i || e.TargetSeat != r.Targets[i]).Any() ||
            ProgramEventHistory<MouMengHuoChargeSpentEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.Count == r.Targets.Count && e.Before - e.After == e.Count) != 1) return false;
        if (r.Payment is { } p && (r.Cursor >= r.Targets.Count || p.From.OwnerSeat != r.Targets[r.Cursor] || p.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
            ProgramEventHistory<MouMengHuoDiscardPaidEvent>().Count(e => e == new MouMengHuoDiscardPaidEvent(f.Id, r.Cursor, r.Targets[r.Cursor], p)) != 1 ||
            !_cardMovements.Any(m => m.Sequence == p.Sequence && m.CardId == p.CardId && m.From == p.From && m.To == p.To && m.CardKind == p.PrintedKind && m.Reason.Value == MouMhDiscardReason))) return false;
        if (r.Stage == MouMengHuoStage.DrawChildren) return r.Draw is { } d && d.RecipientSeat == f.OwnerSeat && d.RequestedCount == 1 &&
            ProgramEventHistory<MouMengHuoDrawReturnedEvent>().Count(e => e == new MouMengHuoDrawReturnedEvent(f.Id, r.Cursor, 1, d.ActualDrawIds.Count, d.ReplacementGainedIds.Count, d.SequenceBefore, d.SequenceAfter, d.AttemptFrameId)) == 1;
        return r.Draw is null && r.Stage is MouMengHuoStage.RecipientChoice or MouMengHuoStage.DiscardChildren or MouMengHuoStage.RecoveryChildren or MouMengHuoStage.Drawing;
    }
    private void SealMouMh(long id, long before)
    {
        var f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f with { MouMengHuo = f.MouMengHuo! with { After = _movementSequence } });
        foreach (var m in _cardMovements.Where(m => m.Sequence > before && m.Sequence <= _movementSequence))
        {
            var fact = new MouMengHuoMovementSignedEvent(id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence);
            if (!ProgramEventHistory<MouMengHuoMovementSignedEvent>().Contains(fact)) AdvanceEventRulesAndQueueFact(fact);
        }
    }
    private bool DrainMouMh(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 30900)]
    private bool IsMouMhMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) => effect is not null && MouMengHuoComposition.Handles(effect.Op) &&
        ValidMouMh(f) && pending == f.PendingMovementContinuation && pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        f.MouMengHuo!.Stage is MouMengHuoStage.ObtainChildren or MouMengHuoStage.DiscardChildren or MouMengHuoStage.RecoveryChildren or MouMengHuoStage.DrawChildren;
    private bool MouMhFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouMh(f)) return false;
        if (NativeDrawAttemptRidesOn(child, f)) return f.NativeDraw is not null && f.MouMengHuo!.Stage == MouMengHuoStage.Drawing;
        if (IsMouMhSilverLionChild(f, child)) return true;
        if (child is RecoveryReplacementFrame recovery) return recovery.ParentFrameId == f.Id && ProgramRecoveryReplacementReceiptMatches(f, recovery, CompleteProgramEventHistory().ToArray());
        if (child is HpChangedTriggerWindowFrame hp) return ProgramHpReturnMatches(f, hp) && hp.Change.ParentFrameId == f.Id && ProgramRecoveryReceiptMatches(f, hp, CompleteProgramEventHistory().ToArray());
        if (child is CardsMovedTriggerWindowFrame moved) return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat &&
            moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.All(m => ProgramEventHistory<MouMengHuoMovementSignedEvent>().Contains(new(f.Id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence)));
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool IsMouMhSilverLionChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouMh(f) || f.MouMengHuo is not { Stage: MouMengHuoStage.DiscardChildren, Payment: { PrintedKind: CardKind.SilverLion, From.Zone: CardZoneKind.Equipment } paid } r ||
            r.Cursor >= r.Targets.Count || paid.From.OwnerSeat != r.Targets[r.Cursor] ||
            !_cardMovements.Any(m => m.Sequence == paid.Sequence && m.CardId == paid.CardId && m.CardKind == CardKind.SilverLion && m.From == paid.From && m.To == paid.To && m.Reason.Value == MouMhDiscardReason)) return false;
        var recipient = r.Targets[r.Cursor];
        if (child is RecoveryReplacementFrame recovery)
            return RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value == MouMhDiscardReason &&
                recovery.Attempt.Amount == 1 && recovery.Attempt.SourceSeat == recipient && recovery.Attempt.TargetSeat == recipient;
        return child is HpChangedTriggerWindowFrame hp && hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id &&
            hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.Amount > 0 &&
            hp.Change.SourceSeat == recipient && hp.Change.TargetSeat == recipient && ProgramEventHistory<SilverLionRemovedRecoveryEvent>().Any(e =>
                e.ResolutionId > f.Id && e.ResolutionId < hp.Change.Id && e.PlayerSeat == recipient && e.Reason.Value == MouMhDiscardReason && e.RecoveredAmount == hp.Change.Amount && e.RemainingHp == hp.Change.HpAfter);
    }
    private bool MouMhObserverSuffix(int i) => HasObserverSuffix(i, static (g, n) => g.IsMovementObserverEdge(n) || g.IsSameNameHandObserverEdge(n) ||
        g.IsYanPuDyingExitEdge(n) || g.TypedDamagePostEventEdge(g._resolutionStack[n - 1], g._resolutionStack[n]) ||
        g._resolutionStack[n - 1] is ProgramSkillFrame f && g.MouMhFirstChild(f, g._resolutionStack[n]) ||
        NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]),
        static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d));
    private ProgramSkillFrame? MouMhObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.MouMhFirstChild(f, child), static (g, i) => g.MouMhObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 30900)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 30900)]
    private bool HasMouMhDamageObserver(long id) => MouMhObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 30900)]
    private bool HasMouMhBeforeDamageObserver(long id) => MouMhObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 30900)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 30900)]
    private bool IsMouMhDying() => ActiveDying is { } dying && MouMhObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 30900)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 30900)]
    private bool AllowsMouMhNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || MouMhObserverRoot() is not { } root || root.Id == f.Id || sourceLess) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 30900)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 30900)]
    private bool IsMouMhRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (!ValidMouMh(f)) return false;
        if (IsMouMhSilverLionChild(f, recovery)) return true;
        if (recovery.ParentFrameId == f.Id) return ProgramRecoveryReplacementReceiptMatches(f, recovery, CompleteProgramEventHistory().ToArray());
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return index >= 0 && child > index + 1 && MouMhFirstChild(f, _resolutionStack[index + 1]) && MouMhObserverSuffix(index) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 30900)]
    private void AssertMouMh(ProgramSkillFrame f)
    { if (f.MouMengHuo is not null && !ValidMouMh(f)) throw new InvalidOperationException("Mou Meng Huo changed its exact owning frame, charge debit or native receipt."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 30900)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 30900)]
    private bool KeepPaidMouMh(ProgramSkillFrame f) => f.MouMengHuo is { Accepted: true } && ValidMouMh(f);
    [ProgramContinuation(ProgramContinuationKind.Resume, 30900)]
    private bool ResumeMouMh(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouMengHuo: not null } f || f.Id != id) return false; ContinueMouMh(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 30900)]
    private bool ReturnMouMhMovement(ProgramSkillFrame f) => f.PendingMovementContinuation is not null && ResumeMouMh(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 30900)]
    private bool AdvanceMouMhChildren() => _pendingDecision is null && MouMhObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
