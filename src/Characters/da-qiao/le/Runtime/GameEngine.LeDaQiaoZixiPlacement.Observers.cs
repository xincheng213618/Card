using System.Collections;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ZixiPlacementRoot(ProgramSkillFrame f) => f.ZixiPlacement is { } r && ZixiPlacementMother(f) &&
        r.Source == ZixiPlacementSource(f) && r.GameplayHash == f.GameplayHash && r.WindowId == f.WindowContext!.ParentFrameId &&
        r.InstructionIndex == f.InstructionIndex && r.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play && r.Phase.ActorSeat == f.OwnerSeat &&
        IsRecordedActualDiscardRecoveryPhase(r.Phase) && r.Entities is IList { IsReadOnly: true } && r.Grains is IList { IsReadOnly: true } &&
        r.Entities.Count <= 2 && r.Entities.Select(e => e.CardId).Distinct().Count() == r.Entities.Count &&
        r.Entities.Select(e => e.SlotKind).Distinct().Count() == r.Entities.Count &&
        (r.Entities.Count == 0 ? r.TargetSeat is null : r.TargetSeat is { } target && IsValidPlayerSeat(target)) &&
        r.Entities.All(e => HasQiqinEntityMark(e.CardId) && ZixiSlots.Contains(e.SlotKind) &&
            e.From.OwnerSeat == f.OwnerSeat && e.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment && GetAdvancedCard(e.CardId).Kind == e.PrintedKind) &&
        ProgramEventHistory<ZixiPlacementOpenedEvent>().Where(e => e.FrameId == f.Id).ToArray() is [var opened] &&
        opened == new ZixiPlacementOpenedEvent(f.Id, r.Source, r.GameplayHash, r.WindowId, r.Phase);
    private bool HasPaidZixiPlacement(ProgramSkillFrame f)
    {
        if (!ZixiPlacementRoot(f) || f.ZixiPlacement is not { Paid: true, Stage: ZixiPlacementStage.MovementChildren, TargetSeat: { } target, BatchId: > 0 } r ||
            r.Entities.Count is < 1 or > 2 || r.SequenceBefore < 0 || r.BatchId <= f.Id ||
            r.PrimaryAfter != r.SequenceBefore + r.Entities.Count || r.SequenceAfter < r.PrimaryAfter || r.SequenceAfter > _movementSequence ||
            ProgramEventHistory<ZixiPlacementCommittedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var commit] ||
            commit != new ZixiPlacementCommittedEvent(f.Id, r.Source, r.GameplayHash, target, r.Entities.Count, r.SequenceBefore) ||
            ProgramEventHistory<ZixiPlacementPaidEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var paid] ||
            paid != new ZixiPlacementPaidEvent(f.Id, r.BatchId, r.Entities.Count, r.SequenceBefore, r.PrimaryAfter)) return false;
        var selected = ProgramEventHistory<ZixiPlacementEntityFrozenEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
        var moved = ProgramEventHistory<ZixiQinPlacedEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
        if (selected.Length != r.Entities.Count || moved.Length != r.Entities.Count) return false;
        for (var i = 0; i < r.Entities.Count; i++)
        {
            var e = r.Entities[i]; var m = moved[i];
            if (selected[i] != new ZixiPlacementEntityFrozenEvent(f.Id, i, e.CardId, e.PrintedKind, e.From, e.SlotKind) ||
                m != new ZixiQinPlacedEvent(f.Id, i, r.Source, r.GameplayHash, target, e.CardId, e.PrintedKind, e.From, e.SlotKind, r.SequenceBefore + i + 1, r.BatchId) ||
                _cardMovements.Count(p => p.Sequence == m.MovementSequence && p.CardId == e.CardId && p.CardKind == e.PrintedKind &&
                    p.From == e.From && p.To == CardLocation.Judgment(target) && p.Reason.Value == ZixiPlacementReason) != 1) return false;
        }
        return true;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 28710)]
    private void AssertZixiPlacement(ProgramSkillFrame f)
    {
        if (f.ZixiPlacement is not { } r) return;
        if (!ZixiPlacementRoot(f) || !Enum.IsDefined(r.Stage)) throw new InvalidOperationException("Zixi placement lost its immutable source, actual phase or real boundary.");
        if (!r.Paid)
        {
            if (r.Stage != ZixiPlacementStage.Selecting || r.BatchId != 0 || r.SequenceBefore != 0 || r.PrimaryAfter != 0 || r.SequenceAfter != 0 ||
                r.Grains.Count != 0 || r.TailSealed || ProgramEventHistory<ZixiPlacementCommittedEvent>().Any(e => e.FrameId == f.Id) ||
                r.Entities.Any(e => _cardZones.GetLocation(e.CardId) != e.From || !CanPlaceZixiQin(f.OwnerSeat, GetAdvancedCard(e.CardId)) ||
                    HasJudgmentEffectiveCard(_players[r.TargetSeat!.Value], e.SlotKind)))
                throw new InvalidOperationException("An unpaid Qin draft cannot move cards or reserve an occupied judgment slot.");
            if (_resolutionStack.LastOrDefault()?.Id == f.Id && (_pendingDecision is not { IsPrivate: true } p || p.PlayerSeat != f.OwnerSeat ||
                    !AssistedChoicesEqual(p.Choices, ZixiPlacementChoices(f)))) throw new InvalidOperationException("Zixi lost its private canonical placement draft.");
            return;
        }
        if (!HasPaidZixiPlacement(f) || !r.TailSealed || r.SequenceAfter != r.PrimaryAfter + r.Grains.Count ||
            ProgramEventHistory<ZixiPlacementTailSealedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var sealedTail] ||
            sealedTail != new ZixiPlacementTailSealedEvent(f.Id, r.BatchId, r.Grains.Count, r.SequenceBefore, r.SequenceAfter) ||
            r.Grains.Select(g => g.CardId).Distinct().Count() != r.Grains.Count)
            throw new InvalidOperationException("Zixi lost its exact whole placement or original equipment-removal tail.");
        var grains = ProgramEventHistory<ZixiPlacementGrainFrozenEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
        if (grains.Length != r.Grains.Count || grains.Where((e, i) => e != new ZixiPlacementGrainFrozenEvent(f.Id, i, r.Grains[i].CardId, r.Grains[i].PrintedKind, r.Grains[i].From)).Any() ||
            r.Grains.Any(g => g.From.Zone != CardZoneKind.WoodenOxGrain ||
                !r.Entities.Any(e => e.PrintedKind == CardKind.WoodenOx && e.From == CardLocation.Equipment(g.From.OwnerSeat!.Value)) ||
                _cardMovements.Count(m => m.CardId == g.CardId && m.CardKind == g.PrintedKind && m.From == g.From && m.To == CardLocation.DiscardPile &&
                    m.Reason == CardMoveReasons.WoodenOxGrainDiscard && m.Sequence > r.PrimaryAfter && m.Sequence <= r.SequenceAfter) != 1))
            throw new InvalidOperationException("Zixi cannot replace or duplicate the exact original Ox grain payment children.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 470)]
    private bool IsZixiPlacementMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        HasPaidZixiPlacement(f) && effect is { Op: SkillProgramEffectOp.ResolveZixiQinJudgmentPlacement, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } &&
        pending == f.PendingMovementContinuation && pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null;
    private bool ZixiPlacementBatch(ProgramSkillFrame f, CardMovementBatchContext batch)
    {
        if (!HasPaidZixiPlacement(f) || f.ZixiPlacement is not { } r || batch.ParentFrameId != f.Id || batch.AwaitingProgramFrameId != f.Id ||
            batch.OriginOwnerSeat != f.OwnerSeat || batch.OriginSkillId != f.SkillId || batch.OriginSkillInstanceId != f.SkillInstanceId) return false;
        if (batch.Id == r.BatchId) return batch.Movements.Count == r.Entities.Count && !batch.Movements.Where((m, i) =>
            m.CardId != r.Entities[i].CardId || m.CardKind != r.Entities[i].PrintedKind || m.From != r.Entities[i].From ||
            m.To != CardLocation.Judgment(r.TargetSeat!.Value) || m.Reason.Value != ZixiPlacementReason || m.Sequence != r.SequenceBefore + i + 1).Any();
        return batch.ParentBatchId == r.BatchId && batch.Movements.Count > 0 && batch.Movements.All(m =>
            r.Grains.Any(g => g.CardId == m.CardId && g.PrintedKind == m.CardKind && g.From == m.From) &&
            m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard && m.Sequence > r.PrimaryAfter && m.Sequence <= r.SequenceAfter && _cardMovements.Contains(m));
    }
    private bool ZixiPlacementLionRemoval(ProgramSkillFrame f, int seat) => f.ZixiPlacement is { Paid: true } r &&
        r.Entities.Any(e => e.PrintedKind == CardKind.SilverLion && e.From == CardLocation.Equipment(seat));
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 650)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 590)]
    private bool IsZixiPlacementRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (!HasPaidZixiPlacement(f)) return false;
        if (RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement)
            return recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.SourceSeat == recovery.Attempt.TargetSeat &&
                recovery.Attempt.Amount == 1 && recovery.Attempt.Completion.MoveReason == new CardMoveReason(ZixiPlacementReason) && ZixiPlacementLionRemoval(f, recovery.Attempt.TargetSeat);
        var index = _resolutionStack.FindIndex(p => p.Id == f.Id); var child = _resolutionStack.FindIndex(p => p.Id == recovery.Id);
        return index >= 0 && child > index + 1 && ZixiPlacementFirstChild(f, _resolutionStack[index + 1]) && ZixiPlacementObserverSuffix(index) &&
            RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    private bool ZixiPlacementFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!HasPaidZixiPlacement(f)) return false;
        AssertZixiPlacement(f);
        if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is not null && ProgramMovementReturnMatches(f, moved) && ZixiPlacementBatch(f, moved.Batch);
        if (child is RecoveryReplacementFrame recovery) return IsZixiPlacementRecoveryParent(f, recovery);
        if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id &&
            hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind == HpChangeKind.Recovery &&
            hp.Change.SourceSeat == hp.Change.TargetSeat && hp.Change.Amount == 1 && ZixiPlacementLionRemoval(f, hp.Change.TargetSeat);
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
            ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id && e.Change.Window == state.Window);
    }
    private bool ZixiPlacementObserverSuffix(int index) => HasObserverSuffix(index,
        static (g, next) => g.IsMovementObserverEdge(next) || g.IsSameNameHandObserverEdge(next) ||
            g.TypedDamagePostEventEdge(g._resolutionStack[next - 1], g._resolutionStack[next]) ||
            g._resolutionStack[next - 1] is ProgramSkillFrame { Zenrun: not null } zenrun && g.ZenrunFirstChild(zenrun, g._resolutionStack[next]) ||
            NativeDrawAttemptRidesOn(g._resolutionStack[next], g._resolutionStack[next - 1]) ||
            g.RecoveryReplacementFrameRidesOn(g._resolutionStack[next], g._resolutionStack[next - 1]) || g.IsYanPuDyingExitEdge(next),
        static (g, next, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(next, dying));
    private ProgramSkillFrame? ZixiPlacementObserverRoot() => FindProgramObserverRoot(
        static (g, f, child) => g.ZixiPlacementFirstChild(f, child), static (g, index) => g.ZixiPlacementObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1440)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1550)]
    private bool HasZixiPlacementDamageObserver(long id) => ZixiPlacementObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1570)]
    private bool HasZixiPlacementBeforeDamageObserver(long id) => ZixiPlacementObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 1400)]
    private bool IsZixiPlacementDying() => ActiveDying is { } dying && ZixiPlacementObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1990)]
    private bool AllowsZixiPlacementNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || ZixiPlacementObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature && !sourceLess &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(f, reference) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 28710)]
    private bool AdvanceZixiPlacementChildren() => _pendingDecision is null && ZixiPlacementObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
