namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool MobileJwParent(ProgramSkillFrame f)
    {
        var r = f.MobileJiangWei!;
        if (r.Operation == SkillProgramEffectOp.ResolveMobileJiangWeiXinghun) return f.TriggerId is null && f.WindowContext is null && r.ParentId == 0 && f.ActivationId == "five-stars";
        if (f.WindowContext is not { } w || w.OwnerSeat != f.OwnerSeat || w.ParentFrameId != r.ParentId || f.ActivationId != f.TriggerId) return false;
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (i < 1 || _resolutionStack[i - 1].Id != r.ParentId) return false;
        var candidate = _resolutionStack[i - 1] switch
        {
            ProgramLifecycleTriggerWindowFrame p when p.Window == w.Window && p.CandidateIndex >= 0 && p.CandidateIndex < p.Candidates.Count => p.Candidates[p.CandidateIndex],
            TurnEndingBoundaryFrame p when p.ItemIndex >= 0 && p.ItemIndex < p.Items.Count => p.Items[p.ItemIndex].Candidate,
            _ => null
        };
        return candidate is not null && MountObserverCandidateMatches(f, candidate);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 40600)] private void RequireMobileJw(ProgramSkillFrame f)
    {
        if (f.MobileJiangWei is not { } r) return;
        if (r.Source != MobileJwSource(f) || r.GameplayHash != f.GameplayHash || f.InstructionIndex != 1 || !MobileJiangWeiComposition.Owns(r.Operation) ||
            !Enum.IsDefined(r.Stage) || !MobileJwParent(f) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            r.Shown.Count > 5 || r.Shown.Select(s => s.Id).Distinct().Count() != r.Shown.Count || r.Top.Count > 5 || r.Top.Distinct().Count() != r.Top.Count ||
            r.Ordered.Distinct().Count() != r.Ordered.Count || r.Ordered.Any(id => !r.Top.Contains(id)) || r.Targets.Distinct().Count() != r.Targets.Count ||
            r.HpLossSeats.Distinct().Count() != r.HpLossSeats.Count || r.Targets.Any(s => !IsValidPlayerSeat(s) || s == f.OwnerSeat) || r.HpLossSeats.Any(s => !IsValidPlayerSeat(s)) ||
            ProgramEventHistory<MobileJiangWeiStartedEvent>().Count(e => e == new MobileJiangWeiStartedEvent(f.Id, r.Source, r.GameplayHash, r.Operation, r.ParentId, r.DyingId, r.Amount)) != 1)
            throw new InvalidOperationException("Mobile Jiang Wei lost his original instance, owning native window, private selection or paid cursor.");
        if (r.Operation == SkillProgramEffectOp.ResolveMobileJiangWeiShenpei && (r.DyingId <= 0 || r.Amount < 1 ||
            _resolutionStack.SingleOrDefault(x => x.Id == r.DyingId) is not DyingFrame { VictimSeat: var victim } || victim != f.OwnerSeat ||
            ProgramEventHistory<PlayerDyingEvent>().Where(e => e.VictimSeat == f.OwnerSeat && e.ResolutionId <= r.DyingId).Select(e => e.ResolutionId).Distinct().Count() != r.Amount))
            throw new InvalidOperationException("Shenpei lost its original including-current true dying-entry count.");
        if (f.PendingMovementContinuation is { } p && !MobileJwMovement(f, GetPausedPrivateOfferEffect(f), p))
            throw new InvalidOperationException("Mobile Jiang Wei lost his frozen native movement/recovery invoice.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && _pendingDecision is { } prompt && prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == MobileJwAction) &&
            (prompt.PlayerSeat != (r.Stage == MobileJiangWeiStage.Showing ? r.Target : f.OwnerSeat) || !AssistedChoicesEqual(prompt.Choices, MobileJwChoices(f))))
            throw new InvalidOperationException("Mobile Jiang Wei changed the original private chooser frontier.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 40600)]
    private bool MobileJwMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p)
    {
        if (f.MobileJiangWei is not { } r || effect?.Op != r.Operation || p != f.PendingMovementContinuation || p.SubjectSeat != f.OwnerSeat || p.BeforeCount != 0 || p.CoverageResultBind is not null ||
            r.Stage is not (MobileJiangWeiStage.ExchangeChildren or MobileJiangWeiStage.ReserveChildren or MobileJiangWeiStage.ReturnChildren or MobileJiangWeiStage.DiscardChildren or MobileJiangWeiStage.RecoveryChildren or MobileJiangWeiStage.GrantChildren) ||
            !r.Paid.SequenceEqual(_cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= r.After)) ||
            ProgramEventHistory<MobileJiangWeiPaidEvent>().Count(e => e == new MobileJiangWeiPaidEvent(f.Id, r.PaymentIndex, r.Before, r.After, r.Paid.Count)) != 1) return false;
        return r.Paid.All(m => r.Stage switch
        {
            MobileJiangWeiStage.ExchangeChildren => m.Reason.Value == "mobile.xinghun.exchange" && (m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) || m.From == CardLocation.Hand(f.OwnerSeat) && m.To == CardLocation.DrawPile),
            MobileJiangWeiStage.ReserveChildren => m.Reason.Value == "mobile.xinghun.show" && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing && r.Shown.Any(s => s.Id == m.CardId && s.Kind == m.CardKind && s.From == m.From),
            MobileJiangWeiStage.ReturnChildren => m.Reason.Value == "mobile.xinghun.return" && m.From == CardLocation.Processing && m.To == CardLocation.DrawPile && r.Shown.Any(s => s.Id == m.CardId && s.From == CardLocation.DrawPile),
            MobileJiangWeiStage.DiscardChildren => m.Reason.Value == "mobile.tiantao.discard" && m.From == new CardLocation(r.Region, r.Target) || m.Reason == CardMoveReasons.WoodenOxGrainDiscard && m.From == CardLocation.WoodenOxGrain(r.Target) && r.Paid.Any(x => x.From == CardLocation.Equipment(r.Target) && x.CardKind == CardKind.WoodenOx),
            _ => false
        });
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 40600)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 40600)]
    private bool MobileJwRecovery(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.MobileJiangWei is not { } r || !RecoveryReplacementFrameRidesOn(recovery, f) || recovery.Return.Continuation != PostEventContinuation.AwaitedProgramMovement) return false;
        if (r.Stage == MobileJiangWeiStage.RecoveryChildren) return recovery.Attempt.SourceSeat == f.OwnerSeat && recovery.Attempt.TargetSeat == f.OwnerSeat && recovery.Attempt.Amount == r.Amount && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.Program && recovery.Attempt.Completion.InstructionIndex == f.InstructionIndex;
        return r.Stage == MobileJiangWeiStage.DiscardChildren && r.Paid.Any(m => m.CardKind == CardKind.SilverLion && m.From == CardLocation.Equipment(r.Target)) && recovery.Attempt.TargetSeat == r.Target && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion;
    }
    private bool IsMobileJwShenpeiDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess, int? explicitSource)
    {
        if (f.MobileJiangWei is not { Operation: SkillProgramEffectOp.ResolveMobileJiangWeiShenpei, Stage: MobileJiangWeiStage.ThunderIssued } r ||
            f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || target != r.Target || amount != r.Amount || source is not null || nature != DamageNature.Thunder || sourceLess || explicitSource is not null ||
            ActiveDying?.Id != r.DyingId || f.WindowContext is not { Window: SkillProgramTriggerWindow.DyingEntering } w ||
            _resolutionStack.SingleOrDefault(x => x.Id == w.ParentFrameId) is not ProgramLifecycleTriggerWindowFrame { Continuation: ProgramLifecycleContinuation.ResumeDyingEntry } entry || entry.ResumeDyingFrameId != r.DyingId) return false;
        RequireMobileJw(f); return true;
    }
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 40600)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 40600)]
    private bool MobileJwNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess) => IsMobileJwShenpeiDamage(f, target, amount, source, nature, sourceLess, null);
    private bool IsMobileJwSuspendedDying(long attackId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileJiangWei: { Operation: SkillProgramEffectOp.ResolveMobileJiangWeiShenpei, Stage: MobileJiangWeiStage.ThunderIssued } r, AttackAttempt: { Nature: DamageNature.Thunder, SourceLess: false } a } f ||
            f.Id != attackId || a.SourceSeat != f.OwnerSeat || ActiveDying?.Id != r.DyingId || f.AttackReturn is null) return false;
        RequireMobileJw(f); return true;
    }
    private bool MobileJwFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MobileJiangWei is not { } r) return false;
        if (child is CardUseFrame use) return use.MobileJiangWeiReturn is { } ret && r.Use == ret && ret.ParentId == f.Id && r.Stage == MobileJiangWeiStage.SlashIssued && use.Action?.ActionId == ret.ActionId;
        if (child is RecoveryReplacementFrame recovery) return MobileJwRecovery(f, recovery);
        if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } changed) return r.Stage == MobileJiangWeiStage.GrantChildren && changed.ResumeProgramFrameId == f.Id && changed.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is { } p && MobileJwMovement(f, GetPausedPrivateOfferEffect(f), p) && ProgramMovementReturnMatches(f, moved) && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.All(r.Paid.Contains);
        return r.Stage == MobileJiangWeiStage.ThunderIssued && f.AttackAttempt is not null && f.AttackReturn is not null && DyingSuitsStructuralEdge(f, child);
    }
    private ProgramSkillFrame? MobileJwObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.MobileJwFirstChild(f, c), static (e, i) => e.SameNameHandObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 40600)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 40600)]
    private bool MobileJwDamageObserver(long id) => MobileJwObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 40600)]
    private bool MobileJwBeforeDamageObserver(long id) => MobileJwObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 40600)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 40600)]
    private bool MobileJwDyingProof()
    {
        if (ActiveDying is not { } dying) return false;
        var root = _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.MobileJiangWei is { } r && r.DyingId == dying.Id);
        if (root is not null) { RequireMobileJw(root); return true; }
        return MobileJwObserverRoot() is { } observer && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == observer.Id);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 40600)] private bool AdvanceMobileJwSubtree() => _pendingDecision is null && MobileJwObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
