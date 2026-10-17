using System.Collections;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidQiqinReceipt(ProgramSkillFrame f)
    {
        if (f.Qiqin is not { } r || f.SkillId != "le:qiqin" || f.TriggerId != "collect-discard-qin" ||
            f.ActivationId != f.TriggerId || f.InstructionIndex != 1 || r.InstructionIndex != f.InstructionIndex ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash || r.Source != QiqinSource(f) ||
            r.GameplayHash != f.GameplayHash || f.WindowContext?.ParentFrameId != r.WindowId ||
            !ExactOriginalHandLifecycleParent(f, SkillProgramTriggerWindow.TurnStartBeforeNormalFlow) ||
            r.ActualTurn <= 0 || r.TurnOwner != f.OwnerSeat || r.Stage != QiqinStage.ObtainChildren ||
            r.SequenceBefore < 0 || r.SequenceAfter < r.SequenceBefore || r.SequenceAfter > _movementSequence ||
            r.Entities is not IList { IsReadOnly: true } || r.Entities.Select(e => e.CardId).Distinct().Count() != r.Entities.Count ||
            r.Entities.Any(e => !HasQiqinEntityMark(e.CardId) || GetAdvancedCard(e.CardId).Kind != e.PrintedKind) ||
            ProgramEventHistory<QiqinCollectStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var issued] ||
            issued != new QiqinCollectStartedEvent(f.Id, r.Source, r.GameplayHash, r.WindowId, r.ActualTurn,
                r.TurnOwner, r.Entities.Count, r.SequenceBefore)) return false;
        var selected = ProgramEventHistory<QiqinCollectEntityIssuedEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
        if (selected.Length != r.Entities.Count || selected.Where((e, i) => e !=
                new QiqinCollectEntityIssuedEvent(f.Id, i, r.Entities[i].CardId, r.Entities[i].PrintedKind)).Any()) return false;
        var moved = ProgramEventHistory<QiqinCollectedEntityEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
        var paid = ProgramEventHistory<QiqinCollectPaidEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Entities.Count == 0) return r.BatchId == 0 && r.SequenceBefore == r.SequenceAfter && moved.Length == 0 && paid.Length == 0;
        return r.BatchId > f.Id && r.SequenceAfter == r.SequenceBefore + r.Entities.Count &&
            paid is [var fact] && fact == new QiqinCollectPaidEvent(f.Id, r.BatchId, r.Entities.Count, r.SequenceBefore, r.SequenceAfter) &&
            moved.Length == r.Entities.Count && !moved.Where((e, i) => e.Index != i || e.CardId != r.Entities[i].CardId ||
                e.PrintedKind != r.Entities[i].PrintedKind || e.MovementSequence != r.SequenceBefore + i + 1 ||
                _cardMovements.Count(m => m.Sequence == e.MovementSequence && m.CardId == e.CardId && m.CardKind == e.PrintedKind &&
                    m.From == CardLocation.DiscardPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == QiqinObtainReason) != 1).Any();
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 28610)]
    private void AssertQiqin(ProgramSkillFrame f)
    {
        if (f.Qiqin is not null && !ValidQiqinReceipt(f))
            throw new InvalidOperationException("Qiqin lost its original preparation, immutable whole-pile selection or once-paid physical obtain.");
    }
    private bool IsQiqinMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        f.Qiqin is { BatchId: > 0 } && ValidQiqinReceipt(f) && effect is
            { Op: SkillProgramEffectOp.ResolveQiqinDiscardPileQin, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } &&
        pending == f.PendingMovementContinuation && pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null;
    private bool QiqinBatchMatches(ProgramSkillFrame f, CardMovementBatchContext batch)
    {
        if (f.Qiqin is not { } r || !ValidQiqinReceipt(f)) return false;
        return batch.Id == r.BatchId && batch.ParentFrameId == f.Id && batch.AwaitingProgramFrameId == f.Id &&
            batch.OriginOwnerSeat == f.OwnerSeat && batch.OriginSkillId == f.SkillId && batch.OriginSkillInstanceId == f.SkillInstanceId &&
            batch.Movements.Count == r.Entities.Count && !batch.Movements.Where((m, i) =>
                m.CardId != r.Entities[i].CardId || m.CardKind != r.Entities[i].PrintedKind || m.Sequence != r.SequenceBefore + i + 1 ||
                m.From != CardLocation.DiscardPile || m.To != CardLocation.Hand(f.OwnerSeat) || m.Reason.Value != QiqinObtainReason).Any();
    }
    private bool QiqinFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidQiqinReceipt(f)) return false;
        if (child is CardsMovedTriggerWindowFrame moved)
            return f.PendingMovementContinuation is not null && ProgramMovementReturnMatches(f, moved) && QiqinBatchMatches(f, moved.Batch);
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
            ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id);
    }
    private bool QiqinObserverSuffix(int index) => HasObserverSuffix(index,
        static (g, next) => g.IsMovementObserverEdge(next) || g.IsSameNameHandObserverEdge(next) ||
            g.TypedDamagePostEventEdge(g._resolutionStack[next - 1], g._resolutionStack[next]) ||
            g._resolutionStack[next - 1] is ProgramSkillFrame { Zenrun: not null } zenrun && g.ZenrunFirstChild(zenrun, g._resolutionStack[next]) ||
            NativeDrawAttemptRidesOn(g._resolutionStack[next], g._resolutionStack[next - 1]) ||
            g.RecoveryReplacementFrameRidesOn(g._resolutionStack[next], g._resolutionStack[next - 1]) || g.IsYanPuDyingExitEdge(next),
        static (g, next, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(next, dying));
    private ProgramSkillFrame? QiqinObserverRoot() => FindProgramObserverRoot(
        static (g, f, child) => g.QiqinFirstChild(f, child), static (g, index) => g.QiqinObserverSuffix(index));
    private bool HasQiqinDamageObserver(long id) => QiqinObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasQiqinBeforeDamageObserver(long id) => QiqinObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsQiqinDying() => ActiveDying is { } dying && QiqinObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsQiqinNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || QiqinObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature && !sourceLess &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(f, reference) : ResolveProgramEffectTarget(f, e.Target));
    }
    private bool IsQiqinRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        var index = _resolutionStack.FindIndex(s => s.Id == f.Id); var child = _resolutionStack.FindIndex(s => s.Id == recovery.Id);
        return index >= 0 && child > index + 1 && QiqinFirstChild(f, _resolutionStack[index + 1]) &&
            QiqinObserverSuffix(index) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 28610)]
    private bool AdvanceQiqinChildren() => _pendingDecision is null && QiqinObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
