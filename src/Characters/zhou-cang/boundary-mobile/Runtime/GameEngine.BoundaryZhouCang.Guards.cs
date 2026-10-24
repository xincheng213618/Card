namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool BoundaryZhongyongChildren(BoundaryZhongyongStage stage) => stage is BoundaryZhongyongStage.AcquireSlashChildren or
        BoundaryZhongyongStage.AcquireDodgeChildren or BoundaryZhongyongStage.GiveSlashChildren or BoundaryZhongyongStage.GiveDodgeChildren;

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 53300)]
    private void AssertBoundaryZhongyong(ProgramSkillFrame f)
    {
        if (f.BoundaryZhongyong is not { } r) return;
        var use = RequireBoundaryZhongyongUse(f);
        if (f.SkillId != BoundaryZhouCangComposition.Skill || f.TriggerId != BoundaryZhouCangComposition.Binding || f.InstructionIndex != 1 ||
            GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ResolveBoundaryZhouCangZhongyong || r.Source != BoundaryZhongyongSource(f) ||
            r.Hash != f.GameplayHash || r.UseId != use.Id || r.ActionId != use.Action!.ActionId || r.WindowId != f.WindowContext!.ParentFrameId ||
            !IsRecordedActualDiscardRecoveryPhase(r.Phase) || r.Phase.Kind != ActualDiscardRecoveryPhaseKind.Play || r.Phase.ActorSeat != f.OwnerSeat ||
            r.Phase != ProgramEventHistory<BoundaryZhongyongUseOriginEvent>().Single(e => e.UseId == use.Id).Phase ||
            !Enum.IsDefined(r.Stage) || r.Index < 0 || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            r.Slash.Distinct().Count() != r.Slash.Count || r.Dodge.Distinct().Count() != r.Dodge.Count || r.Slash.Intersect(r.Dodge).Any() ||
            r.Slash.Any(id => !use.Action.PhysicalCards.Any(c => c.CardId == id)) ||
            r.Dodge.Any(id => !BoundaryZhongyongResponses(use).Any(a => a.PhysicalCards.Any(c => c.CardId == id))) ||
            r.Responded != (BoundaryZhongyongResponses(use).Length > 0) || !r.Excluded.SequenceEqual(use.TargetSeats) ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            ProgramEventHistory<BoundaryZhongyongStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var started] ||
            started != new BoundaryZhongyongStartedEvent(f.Id, r.Source, r.Hash, r.UseId, r.ActionId, r.WindowId, r.Responded, r.Slash.Count, r.Dodge.Count, r.Phase))
            throw new InvalidOperationException("Zhongyong lost its original completed Play Slash, actual responses, source/hash or entity invoice.");
        if (BoundaryZhongyongChildren(r.Stage))
        {
            if (!r.Paid || r.Requested.Count == 0 || r.Requested.Distinct().Count() != r.Requested.Count || r.Index is < 1 or > 2 ||
                !IsValidPlayerSeat(r.Recipient) || r.Settlement is null ||
                (r.Stage is BoundaryZhongyongStage.AcquireSlashChildren or BoundaryZhongyongStage.AcquireDodgeChildren ? r.Recipient != f.OwnerSeat :
                    r.Recipient == f.OwnerSeat || r.Excluded.Contains(r.Recipient)) ||
                r.Requested.Any(id => !(r.Stage is BoundaryZhongyongStage.AcquireSlashChildren or BoundaryZhongyongStage.GiveSlashChildren ? r.Slash : r.Dodge).Contains(id)) ||
                f.PendingMovementContinuation is { } p && (p.SubjectSeat != r.Recipient || p.BeforeCount != 0 || p.CoverageResultBind is not null) ||
                ProgramEventHistory<BoundaryZhongyongMovementSettledEvent>().Where(e => e.FrameId == f.Id && e.Index == r.Index).ToArray() is not [var paid] ||
                paid != new BoundaryZhongyongMovementSettledEvent(f.Id, r.Index, r.Stage, r.Recipient, r.Requested.Count, r.Before, r.After))
                throw new InvalidOperationException("Zhongyong lost its one paid native movement request and typed child return.");
            ValidateHappyWeiYanGainRequestSettlement(r.Settlement, f.Id, r.Requested, CardLocation.Hand(r.Recipient), r.Before);
            var facts = ProgramEventHistory<BoundaryZhongyongMovementEvent>().Where(e => e.FrameId == f.Id && e.Index == r.Index).ToArray();
            if (facts.Length != r.Settlement.Movements.Count || r.Settlement.Movements.Any(m => m.Sequence > r.After ||
                !facts.Any(e => e.Stage == r.Stage && e.Recipient == r.Recipient && e.CardId == m.CardId && e.From == m.From && e.To == m.To && e.Sequence == m.Sequence)))
                throw new InvalidOperationException("Zhongyong's original request differs from its native movement ledger.");
        }
        else if (f.PendingMovementContinuation is not null || r.Settlement is not null || r.Requested.Count != 0 ||
            r.Stage == BoundaryZhongyongStage.Choose && (!r.Responded || r.Paid || r.Index != 0) || r.Stage == BoundaryZhongyongStage.GiveSlash && (!r.Responded || !r.Paid))
            throw new InvalidOperationException("Zhongyong retained an unrelated movement continuation or changed branches.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && _pendingDecision is { } prompt &&
            (prompt.PlayerSeat != f.OwnerSeat || prompt.SkillPrompt?.SkillId != f.SkillId || !AssistedChoicesEqual(prompt.Choices, BoundaryZhongyongChoices(f))))
            throw new InvalidOperationException("Zhongyong's original recipient and available-entity menu changed.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 53300)]
    private bool IsBoundaryZhongyongMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        f.BoundaryZhongyong is { } r && BoundaryZhongyongChildren(r.Stage) && e?.Op == SkillProgramEffectOp.ResolveBoundaryZhouCangZhongyong &&
        p == f.PendingMovementContinuation && p.SubjectSeat == r.Recipient && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool BoundaryZhongyongFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.BoundaryZhongyong is not { } r || !BoundaryZhongyongChildren(r.Stage)) return false;
        AssertBoundaryZhongyong(f);
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } changed)
            return changed.ResumeProgramFrameId == f.Id && changed.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange)
            return state.ResumeProgramFrameId == f.Id;
        if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        return child is CardsMovedTriggerWindowFrame moved && ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id &&
            moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.OriginOwnerSeat == f.OwnerSeat &&
            moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) &&
                m.Sequence > r.Before && m.Sequence <= r.After && r.Settlement!.Movements.Contains(m));
    }
    private ProgramSkillFrame? BoundaryZhongyongObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.BoundaryZhongyongFirstChild(f, c),
        static (e, i) => e.HasObserverSuffix(i, static (g, n) => g.IsMovementObserverEdge(n) || g.IsYanPuDyingExitEdge(n) ||
            g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d)));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 53300)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 53300)]
    private bool BoundaryZhongyongDamageObserver(long id) => BoundaryZhongyongObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 53300)]
    private bool BoundaryZhongyongBeforeDamageObserver(long id) => BoundaryZhongyongObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 53300)]
    private bool BoundaryZhongyongDyingObserver() => ActiveDying is { } d && BoundaryZhongyongObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 53300)]
    private bool BoundaryZhongyongNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || BoundaryZhongyongObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    // Called by the native completed-Slash invariants while this exact original
    // completed-use trigger or one of its audited movement descendants is active.
    private bool HasBoundaryZhouCangCompletedUseObserver(long useId)
    {
        var i = _resolutionStack.FindIndex(f => f.Id == useId);
        if (i < 0 || i + 2 >= _resolutionStack.Count || _resolutionStack[i] is not CardUseFrame { Step: ResolutionFrameStep.Completed } use ||
            _resolutionStack[i + 1] is not ProgramCardTriggerWindowFrame { Continuation: ProgramCardContinuation.CompletedSlash or ProgramCardContinuation.CompletedCard } window ||
            window.ParentFrameId != use.Id || _resolutionStack[i + 2] is not ProgramSkillFrame f || f.WindowContext is not { } c ||
            BoundaryZhongyongUse(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, 0), c)?.Id != use.Id) return false;
        return f.BoundaryZhongyong is null ? f.InstructionIndex is 0 or 1 : f.BoundaryZhongyong.UseId == use.Id;
    }
    private bool IsBoundaryZhouCangPhysicalClaim(long useId, int cardId)
    {
        if (LifecycleCardUse(useId) is not { Action: { } a } use || !a.PhysicalCards.Any(p => p.CardId == cardId)) return false;
        return ProgramEventHistory<BoundaryZhongyongStartedEvent>().Any(s => s.UseId == use.Id && s.ActionId == a.ActionId &&
            s.Source.SkillId == BoundaryZhouCangComposition.Skill && s.Source.BindingId == BoundaryZhouCangComposition.Binding &&
            s.Source.OwnerSeat == a.ActorSeat && _contentRegistry.GetSkill(s.Source.SkillId).Program?.GameplayHash == s.Hash &&
            ProgramEventHistory<BoundaryZhongyongMovementEvent>().Any(e => e.FrameId == s.FrameId && e.CardId == cardId &&
                e.Stage is BoundaryZhongyongStage.AcquireSlashChildren or BoundaryZhongyongStage.GiveSlashChildren &&
                e.From.Zone is CardZoneKind.Processing or CardZoneKind.DiscardPile &&
                _cardMovements.Any(m => m.CardId == e.CardId && m.From == e.From && m.To == e.To && m.Sequence == e.Sequence && m.Reason.Value == BoundaryZhongyongReason)));
    }

    private bool BoundaryZhongyongUseForbidden(int actor, IReadOnlyList<int> ids) => HasBoundaryZhouCang &&
        ProgramEventHistory<BoundaryZhongyongUseBannedEvent>().Any(e => e.Owner == actor && e.Turn == _turnNumber && e.TurnOwner == _currentSeat &&
            ids.Contains(e.CardId) && _cardZones.GetLocation(e.CardId).OwnerSeat == actor &&
            _cardMovements.Any(m => m.Sequence == e.Sequence && m.CardId == e.CardId && m.To == CardLocation.Hand(actor) && m.Reason.Value == BoundaryZhongyongReason));
    private bool BoundaryZhongyongResponseIsUse(int actor, CardKind kind, bool dying = false) => dying || kind == CardKind.Nullification || IsProgramResponseCardUse(_players[actor], kind);
    [ProgramCardLegality(ProgramCardLegalityStage.MaterialUseBan, 53300)]
    private bool BoundaryZhongyongMaterialBan(ProgramCardMaterialQuery q) => (!q.IsResponse || BoundaryZhongyongResponseIsUse(q.ActorSeat, q.Kind)) && BoundaryZhongyongUseForbidden(q.ActorSeat, q.CardIds.ToArray());
    [ProgramCardLegality(ProgramCardLegalityStage.NativeResponseBan, 53300)]
    private bool BoundaryZhongyongNativeResponseBan(ProgramNativeResponseQuery q) => BoundaryZhongyongResponseIsUse(q.Provider.Seat, q.Kind, q.DyingUse) && BoundaryZhongyongUseForbidden(q.Provider.Seat, q.CardIds);
    [ProgramCardLegality(ProgramCardLegalityStage.ViewAsBan, 53300)]
    private bool BoundaryZhongyongViewAsBan(ProgramViewAsLegalityQuery q) => (!q.ForResponse || q.RequestedUseTarget is not null || BoundaryZhongyongResponseIsUse(q.Owner.Seat, q.Kind, q.DyingUse)) && BoundaryZhongyongUseForbidden(q.Owner.Seat, q.CardIds);
    [ProgramCardLegality(ProgramCardLegalityStage.AfterLegalActionKind, 53300)]
    private bool BoundaryZhongyongLegalBan(ProgramLegalActionQuery q) => q.Action.Kind is not (LegalActionKind.Recast or LegalActionKind.UseProgramSkill or LegalActionKind.EndPlay or LegalActionKind.UseEquipmentEffect) &&
        q.Action.CardId is { } id && BoundaryZhongyongUseForbidden(q.Actor.Seat, [id]);
    [ProgramCardLegality(ProgramCardLegalityStage.SubmittedUseValidation, 53300)]
    private void BoundaryZhongyongSubmittedBan(ProgramSubmittedUseQuery q)
    { if (BoundaryZhongyongUseForbidden(q.ActorSeat, q.CardIds)) throw new InvalidOperationException("Zhongyong's obtained physical entities cannot be used during this actual turn."); }
}
