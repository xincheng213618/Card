namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidMouLuZhi(ProgramSkillFrame f)
    {
        if (f.MouLuZhi is not { } r || r.Source != MouLuZhiSource(f) || r.GameplayHash != f.GameplayHash || f.InstructionIndex != 1 || GetPausedPrivateOfferEffect(f)?.Op != r.Op || !Enum.IsDefined(r.Stage) ||
            r.Before < 0 || r.Before > r.After || r.After > _movementSequence || r.Cards is not System.Collections.IList { IsReadOnly: true } || r.Cards.Distinct().Count() != r.Cards.Count ||
            ProgramEventHistory<MouLuZhiStartedEvent>().Count(e => e == new MouLuZhiStartedEvent(f.Id, r.Source, r.GameplayHash, r.Op, r.ActionId, r.Phase)) != 1) return false;
        if (r.Op == SkillProgramEffectOp.ResolveMouLuZhiZhenliangYang)
        {
            if (f.SkillId != MouLuZhiZhenliang || f.ActivationId != "yang-damage" || f.TriggerId is not null || f.WindowContext is not null || r.Phase is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase ||
                phase.ActorSeat != f.OwnerSeat || !IsRecordedActualDiscardRecoveryPhase(phase) || r.Ren is null || r.ActionId is not null || r.Cards.Count > r.RequiredCards) return false;
            if (r.Stage is MouLuZhiStage.MovementChildren or MouLuZhiStage.DamageIssued)
            {
                if (r.RequiredCards < 1 || r.Cards.Count != r.RequiredCards || r.IsRed is null || ProgramEventHistory<MouLuZhiPaidEvent>().Count(e =>
                        e == new MouLuZhiPaidEvent(f.Id, r.TargetSeat, r.RequiredCards, r.IsRed, r.Before, r.After)) != 1 ||
                    r.Cards.Any(id => !ProgramEventHistory<MouLuZhiMovementEvent>().Any(e => e.FrameId == f.Id && e.CardId == id && e.From.OwnerSeat == f.OwnerSeat &&
                        e.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment && e.Reason.Value == MouLuZhiDiscardReason && e.Sequence > r.Before && e.Sequence <= r.After &&
                        _cardMovements.Any(m => m.Sequence == e.Sequence && m.CardId == id && m.From == e.From && m.To == e.To && m.Reason == e.Reason)))) return false;
            }
        }
        else if (f.ActivationId != f.TriggerId || f.WindowContext is not { } context || context.OwnerSeat != f.OwnerSeat ||
            (r.Op == SkillProgramEffectOp.InitializeMouLuZhiMingren ? f.SkillId != MouLuZhiMingren || f.TriggerId != "initial-ren" || context.Window != SkillProgramTriggerWindow.GameStarting :
             r.Op == SkillProgramEffectOp.ExchangeMouLuZhiMingren ? f.SkillId != MouLuZhiMingren || f.TriggerId != "exchange-ren" || context.Window != SkillProgramTriggerWindow.TurnEnding :
             r.Op != SkillProgramEffectOp.ResolveMouLuZhiZhenliangYin || f.SkillId != MouLuZhiZhenliang || context.CardUse?.CardActionId != r.ActionId ||
                _resolutionStack.SingleOrDefault(x => x.Id == context.ParentFrameId) is not ProgramCardTriggerWindowFrame w || w.Action.ActionId != r.ActionId || GetCardActionWindow(w) != context.Window)) return false;
        if (r.Stage == MouLuZhiStage.DrawChildren && (r.Draw is not { } d || d.RecipientSeat != r.TargetSeat || d.RequestedCount != 2 || ProgramEventHistory<MouLuZhiDrawReturnedEvent>().Count(e =>
            e == new MouLuZhiDrawReturnedEvent(f.Id, r.TargetSeat, 2, d.ActualDrawIds.Count, d.ReplacementGainedIds.Count, d.AttemptFrameId)) != 1)) return false;
        return true;
    }
    private void SealMouLuZhiMovements(long id, long before)
    {
        var f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f with { MouLuZhi = f.MouLuZhi! with { After = _movementSequence } });
        foreach (var m in _cardMovements.Where(m => m.Sequence > before && m.Sequence <= _movementSequence).ToArray())
        {
            var fact = new MouLuZhiMovementEvent(id, m.CardId, m.From, m.To, m.Reason, m.Sequence);
            if (!ProgramEventHistory<MouLuZhiMovementEvent>().Contains(fact)) AdvanceEventRulesAndQueueFact(fact);
        }
    }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 31000)]
    private bool IsMouLuZhiMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation pending) => f.MouLuZhi is { Stage: MouLuZhiStage.MovementChildren or MouLuZhiStage.DrawChildren } r &&
        e?.Op == r.Op && pending == f.PendingMovementContinuation && pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null && ValidMouLuZhi(f);
    private bool MouLuZhiFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouLuZhi(f) || f.MouLuZhi is not { } r) return false;
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == MouLuZhiStage.Drawing && f.NativeDraw is not null;
        if (MouLuZhiSilverLionChild(f, child)) return true;
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } p && IsMouLuZhiMovement(f, GetPausedPrivateOfferEffect(f), p))
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat &&
                moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m =>
                    m.Sequence > r.Before && m.Sequence <= r.After && _cardMovements.Contains(m) && ProgramEventHistory<MouLuZhiMovementEvent>().Contains(new(f.Id, m.CardId, m.From, m.To, m.Reason, m.Sequence)));
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouLuZhiSilverLionChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MouLuZhi is not { Op: SkillProgramEffectOp.ResolveMouLuZhiZhenliangYang, Stage: MouLuZhiStage.MovementChildren } r) return false;
        var paid = ProgramEventHistory<MouLuZhiMovementEvent>().FirstOrDefault(e => e.FrameId == f.Id && r.Cards.Contains(e.CardId) && e.From == CardLocation.Equipment(f.OwnerSeat) &&
            e.Reason.Value == MouLuZhiDiscardReason && _cardMovements.Any(m => m.Sequence == e.Sequence && m.CardId == e.CardId && m.CardKind == CardKind.SilverLion));
        if (paid is null) return false;
        if (child is RecoveryReplacementFrame recovery) return RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
            recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value == MouLuZhiDiscardReason &&
            recovery.Attempt.Amount == 1 && recovery.Attempt.SourceSeat == f.OwnerSeat && recovery.Attempt.TargetSeat == f.OwnerSeat;
        return child is HpChangedTriggerWindowFrame hp && hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
            hp.Change.Kind == HpChangeKind.Recovery && hp.Change.Amount > 0 && hp.Change.SourceSeat == f.OwnerSeat && hp.Change.TargetSeat == f.OwnerSeat && ProgramEventHistory<SilverLionRemovedRecoveryEvent>().Any(e =>
                e.ResolutionId > f.Id && e.ResolutionId < hp.Change.Id && e.PlayerSeat == f.OwnerSeat && e.Reason.Value == MouLuZhiDiscardReason && e.RecoveredAmount == hp.Change.Amount && e.RemainingHp == hp.Change.HpAfter);
    }
    private bool MouLuZhiObserverEdge(int n) => IsMovementObserverEdge(n) || IsSameNameHandObserverEdge(n) || IsYanPuDyingExitEdge(n) || TypedDamagePostEventEdge(_resolutionStack[n - 1], _resolutionStack[n]) ||
        _resolutionStack[n - 1] is ProgramSkillFrame f && MouLuZhiFirstChild(f, _resolutionStack[n]) || NativeDrawAttemptRidesOn(_resolutionStack[n], _resolutionStack[n - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[n], _resolutionStack[n - 1]);
    private bool MouLuZhiObserverSuffix(int index) => _resolutionStack[index + 1] is DyingFrame dying && index + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(index + 1, dying) ||
        HasObserverSuffix(index, static (g, n) => g.MouLuZhiObserverEdge(n), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d));
    private ProgramSkillFrame? MouLuZhiObserverRoot() => FindProgramObserverRoot(static (g, f, c) => g.MouLuZhiFirstChild(f, c), static (g, n) => g.MouLuZhiObserverSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 31000)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 31000)]
    private bool HasMouLuZhiDamageObserver(long id) => MouLuZhiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 31000)]
    private bool HasMouLuZhiBeforeDamageObserver(long id) => MouLuZhiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 31000)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 31000)]
    private bool IsMouLuZhiDying() => ActiveDying is { } dying && MouLuZhiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 31000)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 31000)]
    private bool AllowsMouLuZhiNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess || MouLuZhiObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 31000)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 31000)]
    private bool IsMouLuZhiRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (MouLuZhiSilverLionChild(f, recovery)) return true;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return index >= 0 && child > index + 1 && MouLuZhiFirstChild(f, _resolutionStack[index + 1]) && MouLuZhiObserverSuffix(index) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 31000)]
    private bool ResumeMouLuZhi(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouLuZhi: { } r } f || f.Id != id) return false;
        // CompleteProgramAttack clears both native attack fields before it
        // returns to the ordinary executor. Do not consume that completed
        // return as another movement continuation.
        if (r.Stage == MouLuZhiStage.DamageIssued) return f.AttackAttempt is not null || f.AttackReturn is not null;
        ContinueMouLuZhi(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 31000)]
    private bool ReturnMouLuZhiMovement(ProgramSkillFrame f) => f.PendingMovementContinuation is not null && ResumeMouLuZhi(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 31000)]
    private bool AdvanceMouLuZhiChildren() => _pendingDecision is null && MouLuZhiObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 31000)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 31000)]
    private bool ContinueAcceptedMouLuZhi(ProgramSkillFrame f) => f.MouLuZhi is { } r && ValidMouLuZhi(f) &&
        (r.Op == SkillProgramEffectOp.InitializeMouLuZhiMingren || r.Stage is MouLuZhiStage.Drawing or MouLuZhiStage.DrawChildren or MouLuZhiStage.MovementChildren or MouLuZhiStage.DamageIssued);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 31000)]
    private void AssertMouLuZhi(ProgramSkillFrame f) { if (f.MouLuZhi is not null && !ValidMouLuZhi(f)) throw new InvalidOperationException("Mou Lu Zhi lost its owning receipt or paid movement."); }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 31000)]
    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 31000)]
    private void CleanupMouLuZhiRen()
    {
        if (!HasMouLuZhiCapability || !_cardZones.HasPrivatePersistentCards()) return;
        foreach (var ren in _players.SelectMany(p => LiveMouLuZhiRen(p.Seat)).Where(r => !_players[r.Source.OwnerSeat].IsAlive || !_players[r.Source.OwnerSeat].SkillGrants.Grants.Any(g => g.SkillId == r.Source.SkillId && g.SkillInstanceId == r.Source.SkillInstanceId)).ToArray())
        {
            MoveCard(GetAdvancedCard(ren.CardId), ren.Location, CardLocation.DiscardPile, new("skill-program.mou-mobile:mingren.source-lost"));
            AdvanceEventRulesAndQueueFact(new MouLuZhiRenRemovedEvent(ren));
        }
    }
    private IReadOnlyList<PrivateWhirlPileSnapshot>? AppendMouLuZhiRenPiles(IReadOnlyList<PrivateWhirlPileSnapshot>? existing, int owner, int viewer, bool revealAll)
    {
        var ren = LiveMouLuZhiRen(owner); if (ren.Length == 0) return existing;
        return Array.AsReadOnly((existing ?? []).Concat(ren.Select(r => new PrivateWhirlPileSnapshot { SkillId = MouLuZhiMingren, PileName = "任", Count = 1,
            Cards = owner == viewer || revealAll ? [ToSnapshot(GetAdvancedCard(r.CardId))] : null })).ToArray());
    }
}
