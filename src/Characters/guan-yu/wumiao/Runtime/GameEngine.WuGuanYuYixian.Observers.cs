using System.Collections;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool MatchesWuYixianRoot(ProgramSkillFrame f) => f.Yixian is { } r &&
        f.SkillId == "wumiao:yixian" && f.ActivationId == "all-equipment" && f.TriggerId is null &&
        f.WindowContext is null && f.SelectedCardIds.Count == 0 && f.SelectedTargetSeats.Count == 0 &&
        f.InstructionIndex == 1 && r.InstructionIndex == f.InstructionIndex && r.Source == WuYixianSource(f) &&
        r.GameplayHash == f.GameplayHash && _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash == f.GameplayHash &&
        GetPausedPrivateOfferEffect(f)?.Op == SkillProgramEffectOp.ResolveYixianAllEquipmentObtainAndCompensate &&
        r.ActualTurn >= 1 && IsValidPlayerSeat(r.TurnOwner);

    private bool HasPaidWuYixian(ProgramSkillFrame f)
    {
        if (!MatchesWuYixianRoot(f) || f.Yixian is not { Paid: true, Branch: { } branch } r) return false;
        return ProgramEventHistory<YixianPaidEvent>().Where(e => e.FrameId == f.Id).ToArray() is [var paid] &&
            paid == new YixianPaidEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurn, r.TurnOwner, branch,
                r.BatchId, r.Entities.Count, r.Recipients.Count, r.SequenceBefore, r.SequenceAfter);
    }
    private bool CanContinueIssuedWuYixian(ProgramSkillFrame f)
    { if (!HasPaidWuYixian(f)) return false; AssertWuYixian(f); return true; }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 28530)]
    private void AssertWuYixian(ProgramSkillFrame f)
    {
        if (f.Yixian is not { } r) return;
        if (!MatchesWuYixianRoot(f) || !Enum.IsDefined(r.Stage) ||
            r.Entities is not IList { IsReadOnly: true } || r.Recipients is not IList { IsReadOnly: true } ||
            r.Cursor < 0 || r.Cursor > r.Recipients.Count || r.SequenceBefore < 0 ||
            r.SequenceAfter < r.SequenceBefore || r.SequenceAfter > _movementSequence)
            throw new InvalidOperationException("Yixian lost its original source, operation or immutable native cursor.");
        var paidFacts = ProgramEventHistory<YixianPaidEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (!r.Paid)
        {
            if (r.Stage != YixianStage.BranchChoice || r.Branch is not null || r.BatchId != 0 || r.Cursor != 0 ||
                r.Entities.Count != 0 || r.Recipients.Count != 0 || r.DrawOutcome is not null || r.BenefitAccepted ||
                f.NativeDraw is not null || f.PendingMovementContinuation is not null || paidFacts.Length != 0)
                throw new InvalidOperationException("Unpaid Yixian cannot own equipment or a compensation queue.");
            return;
        }
        if (!HasPaidWuYixian(f) || r.Stage == YixianStage.BranchChoice || r.Branch is not { } branch ||
            !Enum.IsDefined(branch) || r.Entities.Select(e => e.CardId).Distinct().Count() != r.Entities.Count ||
            (r.Entities.Count == 0 ? r.BatchId != 0 || r.SequenceBefore != r.SequenceAfter : r.BatchId <= 0))
            throw new InvalidOperationException("Yixian lost or repeated its committed limited equipment batch.");
        var entities = ProgramEventHistory<YixianEntityObtainedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (entities.Length != r.Entities.Count || !entities.Select((fact, i) =>
            fact == new YixianEntityObtainedEvent(f.Id, i, r.Entities[i].CardId, r.Entities[i].PrintedKind,
                r.Entities[i].From, r.Entities[i].To, r.Entities[i].MovementSequence)).All(v => v) ||
            r.Entities.Any(e => !EquipmentCatalog.IsEquipment(e.PrintedKind) ||
                (branch == YixianBranch.Field ? e.From.Zone != CardZoneKind.Equipment || e.From.OwnerSeat is not { } owner || !IsValidPlayerSeat(owner)
                    : e.From != CardLocation.DiscardPile) ||
                e.To != CardLocation.Hand(f.OwnerSeat) && e.To != CardLocation.OutsideGame ||
                e.MovementSequence <= r.SequenceBefore || e.MovementSequence > r.SequenceAfter ||
                _cardMovements.Count(m => m.Sequence == e.MovementSequence && m.CardId == e.CardId &&
                    m.CardKind == e.PrintedKind && m.From == e.From && m.To == e.To && m.Reason.Value == WuYixianObtainReason) != 1))
            throw new InvalidOperationException("Yixian's actual obtained/destroyed equipment identities or original locations changed.");
        var expected = r.Entities.Where(e => e.From.Zone == CardZoneKind.Equipment && e.From.OwnerSeat is not null &&
                e.To == CardLocation.Hand(f.OwnerSeat)).GroupBy(e => e.From.OwnerSeat!.Value)
            .OrderBy(g => (g.Key - r.TurnOwner + _players.Count) % _players.Count).Select(g => new YixianRecipient(g.Key, g.Count())).ToArray();
        var recipients = ProgramEventHistory<YixianRecipientFrozenEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (!r.Recipients.SequenceEqual(expected) || recipients.Length != expected.Length ||
            !recipients.Select((e, i) => e == new YixianRecipientFrozenEvent(f.Id, i, expected[i].Seat, expected[i].ObtainedCount)).All(v => v))
            throw new InvalidOperationException("Yixian compensation is based only on actual equipment obtained from each original owner.");
        var decisions = ProgramEventHistory<YixianRecipientDecisionEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (decisions.Any(e => e.Index < 0 || e.Index > r.Cursor || e.Index >= r.Recipients.Count ||
                e.RecipientSeat != r.Recipients[e.Index].Seat || e.ObtainedCount != r.Recipients[e.Index].ObtainedCount) ||
            decisions.Select(e => e.Index).Distinct().Count() != decisions.Length ||
            Enumerable.Range(0, r.Cursor).Any(i => decisions.Count(e => e.Index == i) != 1) ||
            (r.BenefitAccepted ? decisions.Count(e => e.Index == r.Cursor && e.Accepted) != 1 : decisions.Any(e => e.Index == r.Cursor)) ||
            r.BenefitAccepted != (r.Stage is YixianStage.Drawing or YixianStage.DrawChildren or YixianStage.RecoveryChildren))
            throw new InvalidOperationException("Yixian repeated or invented an optional original-owner benefit.");
        var returned = ProgramEventHistory<YixianDrawReturnedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var recoveries = ProgramEventHistory<YixianRecoveryIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (returned.Any(e => e.Index < 0 || e.Index > r.Cursor || e.Index >= r.Recipients.Count ||
                e.RecipientSeat != r.Recipients[e.Index].Seat || e.RequestedCount != r.Recipients[e.Index].ObtainedCount ||
                decisions.Count(d => d.Index == e.Index && d.Accepted) != 1) ||
            returned.Select(e => e.Index).Distinct().Count() != returned.Length ||
            recoveries.Any(e => returned.Count(d => d.Index == e.Index && d.RecipientSeat == e.RecipientSeat) != 1) ||
            recoveries.Select(e => e.Index).Distinct().Count() != recoveries.Length ||
            decisions.Where(e => e.Index < r.Cursor && e.Accepted).Any(e => returned.Count(d => d.Index == e.Index) != 1 || recoveries.Count(d => d.Index == e.Index) != 1))
            throw new InvalidOperationException("Yixian lost its once-only draw then recovery ordering.");
        if (r.Stage is YixianStage.DrawChildren or YixianStage.RecoveryChildren)
        {
            if (r.DrawOutcome is not { } draw || draw.RecipientSeat != r.Recipients[r.Cursor].Seat ||
                draw.RequestedCount != r.Recipients[r.Cursor].ObtainedCount ||
                draw.ActualDrawIds is not IList { IsReadOnly: true } || draw.ReplacementGainedIds is not IList { IsReadOnly: true } ||
                draw.SequenceBefore < r.SequenceAfter || draw.SequenceAfter < draw.SequenceBefore || draw.SequenceAfter > _movementSequence ||
                draw.ActualDrawIds.Distinct().Count() != draw.ActualDrawIds.Count || draw.ActualDrawIds.Intersect(draw.ReplacementGainedIds).Any() ||
                draw.ActualDrawIds.Any(id => _cardMovements.Count(m => m.CardId == id && m.From == CardLocation.DrawPile &&
                    m.To == CardLocation.Hand(draw.RecipientSeat) && m.Sequence > draw.SequenceBefore && m.Sequence <= draw.SequenceAfter && m.Reason.Value == WuYixianDrawReason) != 1) ||
                returned.Where(e => e.Index == r.Cursor).ToArray() is not [var fact] ||
                fact != new YixianDrawReturnedEvent(f.Id, r.Cursor, draw.RecipientSeat, draw.RequestedCount,
                    draw.ActualDrawIds.Count, draw.ReplacementGainedIds.Count, draw.SequenceBefore, draw.SequenceAfter, draw.AttemptFrameId) ||
                f.NativeDraw is not null || (r.Stage == YixianStage.RecoveryChildren) != recoveries.Any(e => e.Index == r.Cursor))
                throw new InvalidOperationException("Yixian lost its precise immutable native draw return.");
        }
        else if (r.DrawOutcome is not null || returned.Any(e => e.Index == r.Cursor))
            throw new InvalidOperationException("Yixian reused a prior recipient's native draw result.");
        if (f.PendingMovementContinuation is { } pending && !IsWuYixianMovement(f, GetPausedPrivateOfferEffect(f), pending))
            throw new InvalidOperationException("Yixian's movement/recovery child lost its typed parent return.");
    }

    private bool IsWuYixianMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        f.Yixian is { Paid: true } r && effect?.Op == SkillProgramEffectOp.ResolveYixianAllEquipmentObtainAndCompensate &&
        pending == f.PendingMovementContinuation && pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        (r.Stage == YixianStage.EquipmentChildren ? pending.SubjectSeat == f.OwnerSeat :
            r.Stage is YixianStage.DrawChildren or YixianStage.RecoveryChildren && r.Cursor < r.Recipients.Count && pending.SubjectSeat == r.Recipients[r.Cursor].Seat);
    private bool WuYixianPaidMovement(ProgramSkillFrame f, CardMovementRecord m)
    {
        if (f.Yixian is not { Paid: true } r || !_cardMovements.Contains(m)) return false;
        if (r.Stage == YixianStage.EquipmentChildren)
        {
            if (m.Sequence <= r.SequenceBefore || m.Sequence > r.SequenceAfter) return false;
            return r.Entities.Any(e => m.Sequence == e.MovementSequence && m.CardId == e.CardId && m.From == e.From && m.To == e.To && m.Reason.Value == WuYixianObtainReason) ||
                m.From.Zone == CardZoneKind.WoodenOxGrain && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard &&
                r.Entities.Any(e => e.PrintedKind == CardKind.WoodenOx && e.From.Zone == CardZoneKind.Equipment && e.From.OwnerSeat == m.From.OwnerSeat);
        }
        return r.Stage == YixianStage.DrawChildren && r.DrawOutcome is { } o && m.Sequence > o.SequenceBefore && m.Sequence <= o.SequenceAfter &&
            (o.ActualDrawIds.Contains(m.CardId) && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(o.RecipientSeat) && m.Reason.Value == WuYixianDrawReason ||
             o.ReplacementGainedIds.Contains(m.CardId) && m.To == CardLocation.Hand(o.RecipientSeat) ||
             m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle);
    }
    private bool WuYixianLionRemoval(ProgramSkillFrame f, int seat) => f.Yixian is { Stage: YixianStage.EquipmentChildren } r &&
        r.Entities.Any(e => e.PrintedKind == CardKind.SilverLion && e.From == CardLocation.Equipment(seat) && e.To != e.From);
    private bool IsWuYixianRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.Yixian is not { Paid: true } r || !HasPaidWuYixian(f)) return false;
        if (RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement)
            return r.Stage == YixianStage.RecoveryChildren && r.Cursor < r.Recipients.Count &&
                recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.Program && recovery.Attempt.Completion.InstructionIndex == f.InstructionIndex &&
                recovery.Attempt.SourceSeat == f.OwnerSeat && recovery.Attempt.TargetSeat == r.Recipients[r.Cursor].Seat && recovery.Attempt.Amount == 1 ||
                r.Stage == YixianStage.EquipmentChildren && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
                recovery.Attempt.SourceSeat == recovery.Attempt.TargetSeat && recovery.Attempt.Amount == 1 &&
                recovery.Attempt.Completion.MoveReason == new CardMoveReason(WuYixianObtainReason) && WuYixianLionRemoval(f, recovery.Attempt.TargetSeat);
        var index = _resolutionStack.FindIndex(s => s.Id == f.Id); var child = _resolutionStack.FindIndex(s => s.Id == recovery.Id);
        return index >= 0 && child > index + 1 && WuYixianFirstChild(f, _resolutionStack[index + 1]) &&
            WuYixianObserverSuffix(index) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    private bool WuYixianFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!HasPaidWuYixian(f) || f.Yixian is not { } r) return false;
        AssertWuYixian(f);
        if (r.Stage == YixianStage.Drawing && f.NativeDraw is not null) return NativeDrawAttemptRidesOn(child, f);
        if (child is RecoveryReplacementFrame recovery) return IsWuYixianRecoveryParent(f, recovery);
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id &&
                hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.Amount == 1 &&
                (r.Stage == YixianStage.EquipmentChildren && hp.Change.SourceSeat == hp.Change.TargetSeat && WuYixianLionRemoval(f, hp.Change.TargetSeat) ||
                 r.Stage == YixianStage.RecoveryChildren && hp.Change.SourceSeat == f.OwnerSeat && hp.Change.TargetSeat == r.Recipients[r.Cursor].Seat);
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state)
            return state.ResumeProgramFrameId == f.Id && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
                ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id);
        return child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is not null &&
            ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id &&
            moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
            moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => WuYixianPaidMovement(f, m));
    }
    private bool WuYixianObserverSuffix(int index) => HasObserverSuffix(index,
        static (g, next) => g.IsMovementObserverEdge(next) || g.IsSameNameHandObserverEdge(next) ||
            g.TypedDamagePostEventEdge(g._resolutionStack[next - 1], g._resolutionStack[next]) ||
            g._resolutionStack[next - 1] is ProgramSkillFrame { Zenrun: not null } zenrun && g.ZenrunFirstChild(zenrun, g._resolutionStack[next]) ||
            NativeDrawAttemptRidesOn(g._resolutionStack[next], g._resolutionStack[next - 1]) ||
            g.RecoveryReplacementFrameRidesOn(g._resolutionStack[next], g._resolutionStack[next - 1]) || g.IsYanPuDyingExitEdge(next),
        static (g, next, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(next, dying));
    private ProgramSkillFrame? WuYixianObserverRoot() => FindProgramObserverRoot(
        static (g, f, child) => g.WuYixianFirstChild(f, child), static (g, index) => g.WuYixianObserverSuffix(index));
    private bool HasWuYixianDamageObserver(long id) => WuYixianObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasWuYixianBeforeDamageObserver(long id) => WuYixianObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsWuYixianDying() => ActiveDying is { } dying && WuYixianObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsWuYixianNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || WuYixianObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature && !sourceLess &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(f, reference) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 28530)]
    private bool AdvanceWuYixianChildren() => _pendingDecision is null && WuYixianObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
