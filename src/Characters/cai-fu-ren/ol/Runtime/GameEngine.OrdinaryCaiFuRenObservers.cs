namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool MatchesOrdinaryQietingRoot(ProgramSkillFrame f)
    {
        if (f.OrdinaryQieting is not { } r || f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterTurnEnded, OrdinaryQieting: { } q } c || f.SkillId != "ol:qieting" || f.TriggerId != "ordinary-turn-end" || f.ActivationId != f.TriggerId || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || r.Source != OrdinaryCaiFuRenSource(f) || r.GameplayHash != f.GameplayHash || r.Qualification != q || q.UsedOnAnother || q.ActualTurn != _turnNumber || q.TurnOwnerSeat != _currentSeat || q.TurnOwnerSeat == f.OwnerSeat || r.WindowId != c.ParentFrameId || r.OccurrenceIndex != c.OccurrenceIndex || GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ResolveOrdinaryCaiFuRenQieting || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash || _resolutionStack.OfType<DeferredTurnEndFrame>().SingleOrDefault(p => p.Id == r.WindowId) is not { } parent || !IsActualAfterTurnEndedParent(parent) || parent.OwnerSeat != q.TurnOwnerSeat || parent.TurnNumber != q.ActualTurn || parent.AfterTurnEnded is not { CurrentChild: { } child } w || child.FrameId != f.Id || w.ItemIndex < 0 || w.ItemIndex >= w.Items.Count || w.Items[w.ItemIndex].Candidate != child.Candidate || !MountObserverCandidateMatches(f, child.Candidate) || c.Facts != w.Items[w.ItemIndex].Facts || c.OwnerSeat != f.OwnerSeat || c.SourceSeat != parent.OwnerSeat || c.TargetSeat != parent.OwnerSeat || c.OccurrenceIndex != child.Candidate.OccurrenceIndex || q.HistoryCutoff < 0 || q.HistoryCutoff > CompleteProgramEventHistory().Count() || OrdinaryQietingUsedOnAnother(q)) return false;
        return ProgramEventHistory<OrdinaryQietingStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is [var started] && started == new OrdinaryQietingStartedEvent(f.Id, r.Source, r.GameplayHash, q, r.WindowId, r.OccurrenceIndex);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 27596)]
    private void AssertOrdinaryQieting(ProgramSkillFrame f)
    {
        if (f.OrdinaryQieting is not { } r) return;
        if (!MatchesOrdinaryQietingRoot(f) || !Enum.IsDefined(r.Stage) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence || r.GrainIds is not System.Collections.IList { IsReadOnly: true } || r.GrainIds.Distinct().Count() != r.GrainIds.Count)
            throw new InvalidOperationException("Ordinary Qieting lost its immutable exact ended-turn owner, source or instruction.");
        var issued = ProgramEventHistory<OrdinaryQietingOptionIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (!r.Paid)
        {
            if (r.Stage != OrdinaryQietingStage.Choice || r.Option is not null || r.CardId is not null || r.PrintedKind is not null || r.DrawOutcome is not null || r.GrainIds.Count != 0 || issued.Length != 0 || r.Before != 0 || r.After != 0) throw new InvalidOperationException("Unpaid ordinary Qieting cannot hold a benefit or payment cursor.");
            return;
        }
        if (issued is not [var option] || option.Option != r.Option || option.CardId != r.CardId || option.PrintedKind != r.PrintedKind || option.Before < 0 || option.Before > r.Before || r.Stage == OrdinaryQietingStage.Choice)
            throw new InvalidOperationException("Ordinary Qieting must issue exactly one option before its native children.");
        if (r.Option == "equipment")
        {
            if (r.Stage != OrdinaryQietingStage.MovementChildren || r.CardId is not { } id || r.PrintedKind is not { } kind || !EquipmentCatalog.IsEquipment(kind) || r.DrawOutcome is not null || option.Before != r.Before || ProgramEventHistory<OrdinaryQietingEquipmentMovedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var moved] || moved.CardId != id || moved.PrintedKind != kind || moved.From != CardLocation.Equipment(r.Qualification.TurnOwnerSeat) || moved.To != CardLocation.Equipment(f.OwnerSeat) || !_cardMovements.Any(m => m.Sequence == moved.MovementSequence && IsOrdinaryQietingPaidMovement(f, m)))
                throw new InvalidOperationException("Ordinary Qieting lost its one real equipment placement without replacement.");
        }
        else if (r.Option == "draw")
        {
            if (r.Stage is not (OrdinaryQietingStage.Draw or OrdinaryQietingStage.DrawChildren) || r.CardId is not null || r.PrintedKind is not null || r.GrainIds.Count != 0) throw new InvalidOperationException("Ordinary Qieting cannot mix its draw and equipment branches.");
            if (r.Stage == OrdinaryQietingStage.DrawChildren && (r.DrawOutcome is not { } o || o.RecipientSeat != f.OwnerSeat || o.RequestedCount != 1 || o.SequenceBefore != r.Before || o.SequenceAfter != r.After || ProgramEventHistory<OrdinaryQietingDrawReturnedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var returned] || returned != new OrdinaryQietingDrawReturnedEvent(f.Id, o.ActualDrawIds.Count, o.ReplacementGainedIds.Count, o.SequenceBefore, o.SequenceAfter, o.AttemptFrameId))) throw new InvalidOperationException("Ordinary Qieting draw lost its native outcome or repeated its typed return.");
        }
        else throw new InvalidOperationException("Ordinary Qieting has no third benefit.");
        if (f.PendingMovementContinuation is { } movement && !IsOrdinaryCaiFuRenMovement(f, GetPausedPrivateOfferEffect(f), movement)) throw new InvalidOperationException("Ordinary Qieting retained an unrelated movement return.");
    }
    private bool CanContinueIssuedOrdinaryCaiFuRen(ProgramSkillFrame f)
    {
        if (IsIssuedOrdinaryCaiFuRenXianzhou(f)) return IsValidAllEquipmentDonation(f);
        if (f.OrdinaryQieting is not { Paid: true }) return false;
        AssertOrdinaryQieting(f); return true;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 27595)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 27595)]
    private bool PermitPaidOrdinaryCaiFuRen(ProgramSkillFrame f) => CanContinueIssuedOrdinaryCaiFuRen(f);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 140)]
    private bool IsOrdinaryCaiFuRenMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) => f.OrdinaryQieting is { Paid: true, Stage: OrdinaryQietingStage.MovementChildren or OrdinaryQietingStage.DrawChildren } && effect?.Op == SkillProgramEffectOp.ResolveOrdinaryCaiFuRenQieting && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool IsOrdinaryQietingPaidMovement(ProgramSkillFrame f, CardMovementRecord m)
    {
        if (f.OrdinaryQieting is not { Paid: true } r || !_cardMovements.Contains(m) || m.Sequence <= r.Before || m.Sequence > r.After) return false;
        if (r.Stage == OrdinaryQietingStage.MovementChildren) return m.CardId == r.CardId && m.CardKind == r.PrintedKind && m.From == CardLocation.Equipment(r.Qualification.TurnOwnerSeat) && m.To == CardLocation.Equipment(f.OwnerSeat) && m.Reason.Value == OrdinaryQietingMoveReason || r.PrintedKind == CardKind.WoodenOx && r.GrainIds.Contains(m.CardId) && m.From == CardLocation.WoodenOxGrain(r.Qualification.TurnOwnerSeat) && m.To == CardLocation.WoodenOxGrain(f.OwnerSeat) && m.Reason == CardMoveReasons.WoodenOxTransfer;
        return r.Stage == OrdinaryQietingStage.DrawChildren && r.DrawOutcome is { } o && (o.ActualDrawIds.Contains(m.CardId) && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == OrdinaryQietingDrawReason || m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle);
    }
    private bool OrdinaryQietingFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.OrdinaryQieting is not { Paid: true } r) return false;
        AssertOrdinaryQieting(f);
        if (r.Stage == OrdinaryQietingStage.Draw && f.NativeDraw is not null) return NativeDrawAttemptRidesOn(child, f);
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills) return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state) return state.ResumeProgramFrameId == f.Id && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id);
        if (child is RecoveryReplacementFrame recovery) return IsOrdinaryCaiFuRenRecoveryParent(f, recovery);
        if (child is HpChangedTriggerWindowFrame hp) return r.Stage == OrdinaryQietingStage.MovementChildren && r.PrintedKind == CardKind.SilverLion && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.ParentFrameId == f.Id && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == r.Qualification.TurnOwnerSeat && hp.Change.TargetSeat == r.Qualification.TurnOwnerSeat && hp.Change.Amount == 1;
        return child is CardsMovedTriggerWindowFrame moved && ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => IsOrdinaryQietingPaidMovement(f, m));
    }
    private bool OrdinaryQietingObserverSuffix(int index) => HasObserverSuffix(index, static (g, n) => g.IsMovementObserverEdge(n) || g.IsSameNameHandObserverEdge(n) || g.IsYanPuDyingExitEdge(n) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) || NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]), static (g, n, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(n, dying));
    private ProgramSkillFrame? OrdinaryQietingObserverRoot() => FindProgramObserverRoot(static (e, f, child) => e.OrdinaryQietingFirstChild(f, child), static (e, index) => e.OrdinaryQietingObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1070)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1180)]
    private bool HasOrdinaryCaiFuRenDamageObserver(long id) => OrdinaryQietingObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1210)]
    private bool HasOrdinaryCaiFuRenBeforeDamageObserver(long id) => OrdinaryQietingObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 1040)]
    private bool IsOrdinaryCaiFuRenDying() => ActiveDying is { } dying && OrdinaryQietingObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1610)]
    private bool AllowsOrdinaryCaiFuRenNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || OrdinaryQietingObserverRoot() is not { } root || root.Id == f.Id) return false;
        var effect = GetPausedPrivateOfferEffect(f); return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature && target == (effect.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, effect.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 300)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 290)]
    private bool IsOrdinaryCaiFuRenRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.OrdinaryQieting is not { Paid: true } r) return false;
        if (recovery.ParentFrameId == f.Id) return r.Stage == OrdinaryQietingStage.MovementChildren && r.PrintedKind == CardKind.SilverLion && RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.SourceSeat == r.Qualification.TurnOwnerSeat && recovery.Attempt.TargetSeat == r.Qualification.TurnOwnerSeat && recovery.Attempt.Amount == 1 && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.CardId == r.CardId && recovery.Attempt.Completion.MoveReason?.Value == OrdinaryQietingMoveReason;
        var i = _resolutionStack.FindIndex(p => p.Id == f.Id); var j = _resolutionStack.FindIndex(p => p.Id == recovery.Id);
        return i >= 0 && j > i + 1 && OrdinaryQietingFirstChild(f, _resolutionStack[i + 1]) && OrdinaryQietingObserverSuffix(i) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[j - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 27596)]
    private bool AdvanceOrdinaryQietingChildren() => _pendingDecision is null && OrdinaryQietingObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
