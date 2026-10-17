namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool JiguRootMatches(ProgramSkillFrame f)
    {
        if (f.Jigu is not { Paid: true } r || f.SkillId != "le:jigu" || f.TriggerId != "damage-equipment-draw" ||
            f.ActivationId != f.TriggerId || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 ||
            f.SelectedTargetSeats.Count != 0 || r.Source != JiguSource(f) || r.GameplayHash != f.GameplayHash ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ResolveJiguDamageEquipmentDraw ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, OccurrenceIndex: 0 } c ||
            c.OwnerSeat != f.OwnerSeat || c.ParentFrameId != r.DamageWindowId || c.DamageFrameId != r.DamageFrameId ||
            c.Amount <= 0 || c.SourceSeat != f.OwnerSeat && c.TargetSeat != f.OwnerSeat ||
            _resolutionStack.SingleOrDefault(x => x.Id == r.DamageWindowId) is not DamageTriggerWindowFrame window ||
            window.ParentFrameId != r.DamageFrameId || window.TriggerWindow != c.Window ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex].ToProgramCandidate()) ||
            _resolutionStack.SingleOrDefault(x => x.Id == r.DamageFrameId) is not DamageFrame damage ||
            damage.TargetSeat != c.TargetSeat || damage.Amount != c.Amount) return false;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        return index > 0 && _resolutionStack[index - 1].Id == r.DamageWindowId &&
            ProgramEventHistory<JiguActivationPaidEvent>().Where(e => e.FrameId == f.Id).ToArray() is [var paid] &&
            paid == JiguPayment(f.Id, r) && ProgramEventHistory<JiguActivationPaidEvent>()
                .Count(e => e.Source.OwnerSeat == f.OwnerSeat && e.DamageFrameId == r.DamageFrameId) == 1;
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 28211)]
    private void AssertJigu(ProgramSkillFrame f)
    {
        if (f.Jigu is not { } r) return;
        if (!JiguRootMatches(f) || !Enum.IsDefined(r.Stage) || r.RoundNumber < 0 || r.ActualTurn < 0 ||
            !IsValidPlayerSeat(r.TurnOwner) || r.MarkedHandCount < 0 || r.MarkedHandCount != r.EquipmentCount ||
            r.EmptySlotCount < 0 || r.StartedSeatCount < 0 || r.StartedSeatCount > _players.Count || r.UseNumber < 1 ||
            r.DisabledForRound != (r.UseNumber >= r.StartedSeatCount) ||
            ProgramEventHistory<JiguActivationPaidEvent>().Where(e => e.Source.OwnerSeat == f.OwnerSeat &&
                e.RoundNumber == r.RoundNumber).Count(e => e.UseNumber < r.UseNumber) != r.UseNumber - 1 ||
            ProgramEventHistory<JiguActivationPaidEvent>().Any(e => e.Source.OwnerSeat == f.OwnerSeat &&
                e.RoundNumber == r.RoundNumber && e.UseNumber < r.UseNumber && e.DisabledForRound))
            throw new InvalidOperationException("Jigu lost its exact damage mother, paid equipment sample or round latch.");
        var returned = ProgramEventHistory<JiguDrawReturnedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Stage == JiguStage.Drawing)
        {
            if (r.DrawOutcome is not null || returned.Length != 0 || f.PendingMovementContinuation is not null)
                throw new InvalidOperationException("Jigu cannot reuse an earlier native draw as a new request.");
            return;
        }
        if (r.DrawOutcome is not { } o || o.RecipientSeat != f.OwnerSeat || o.RequestedCount != r.EmptySlotCount ||
            o.SequenceBefore < 0 || o.SequenceAfter < o.SequenceBefore || o.SequenceAfter > _movementSequence ||
            returned is not [var actual] || actual != new JiguDrawReturnedEvent(f.Id, o.ActualDrawIds.Count,
                o.ReplacementGainedIds.Count, o.SequenceBefore, o.SequenceAfter, o.AttemptFrameId) ||
            f.NativeDraw is not null || f.PendingMovementContinuation is not { } p ||
            !IsLeMiHengMovement(f, GetPausedPrivateOfferEffect(f), p))
            throw new InvalidOperationException("Jigu lost its native actual/replacement outcome or unfinished movement children.");
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 28211)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 28211)]
    private bool PermitPaidJigu(ProgramSkillFrame f)
    {
        if (f.Jigu is not { Paid: true }) return false;
        AssertJigu(f); return true;
    }

    private bool IsLeMiHengMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) =>
        f.Jigu is { Paid: true, Stage: JiguStage.DrawChildren, DrawOutcome: not null } &&
        effect?.Op == SkillProgramEffectOp.ResolveJiguDamageEquipmentDraw && p == f.PendingMovementContinuation &&
        p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;

    private bool JiguDrawMovement(ProgramSkillFrame f, CardMovementRecord movement)
    {
        if (f.Jigu is not { Paid: true, Stage: JiguStage.DrawChildren, DrawOutcome: { } o } ||
            !_cardMovements.Contains(movement) || movement.Sequence <= o.SequenceBefore ||
            movement.Sequence > o.SequenceAfter) return false;
        return o.ActualDrawIds.Contains(movement.CardId) && movement.From == CardLocation.DrawPile &&
                movement.To == CardLocation.Hand(f.OwnerSeat) && movement.Reason.Value == JiguDrawReason ||
            o.ReplacementGainedIds.Contains(movement.CardId) && movement.To == CardLocation.Hand(f.OwnerSeat) ||
            movement.From == CardLocation.DiscardPile && movement.To == CardLocation.DrawPile &&
                movement.Reason == CardMoveReasons.Reshuffle;
    }

    private bool JiguFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.Jigu is not { Paid: true } r) return false;
        AssertJigu(f);
        if (r.Stage == JiguStage.Drawing && f.NativeDraw is not null) return NativeDrawAttemptRidesOn(child, f);
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state)
            return state.ResumeProgramFrameId == f.Id && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
                ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id &&
                    e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id &&
                hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        return child is CardsMovedTriggerWindowFrame moved && ProgramMovementReturnMatches(f, moved) &&
            moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat &&
            moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
            moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => JiguDrawMovement(f, m));
    }

    private bool JiguObserverSuffix(int index) => HasObserverSuffix(index, static (g, n) =>
        g.IsMovementObserverEdge(n) || g.TypedDamagePostEventEdge(g._resolutionStack[n - 1], g._resolutionStack[n]) ||
        g._resolutionStack[n - 1] is ProgramSkillFrame { Zenrun: { Paid: true } } replacement &&
            g.ZenrunFirstChild(replacement, g._resolutionStack[n]) ||
        g.IsSameNameHandObserverEdge(n) || g.IsYanPuDyingExitEdge(n) ||
        g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) ||
        NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]),
        static (g, n, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(n, dying));
    private ProgramSkillFrame? JiguObserverRoot() => FindProgramObserverRoot(
        static (g, f, child) => g.JiguFirstChild(f, child), static (g, index) => g.JiguObserverSuffix(index));
    private bool HasLeMiHengDamageObserver(long id)
    {
        if (JiguObserverRoot() is not { Jigu: { Paid: true } r } root ||
            _resolutionStack.SingleOrDefault(f => f.Id == id) is not DamageTriggerWindowFrame window) return false;
        if (_resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id)) return true;
        // A paid Jigu draw also suspends the original damage window directly
        // below it. Its whole native draw/replacement/observer suffix must be
        // proved before that exact mother can retain its outstanding cursor.
        return r.DamageWindowId == window.Id && r.DamageFrameId == window.ParentFrameId &&
            root.WindowContext?.ParentFrameId == window.Id &&
            window.TriggerWindow == SkillProgramTriggerWindow.AfterDamageApplied &&
            DamageProgramMatchesWindow(root, window);
    }
    private bool HasLeMiHengBeforeDamageObserver(long id) => JiguObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsLeMiHengDying() => ActiveDying is { } d && JiguObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsLeMiHengNestedDamage(ProgramSkillFrame f, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id ||
            JiguObserverRoot() is not { } root || root.Id == f.Id) return false;
        var effect = GetPausedPrivateOfferEffect(f);
        return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount &&
            effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, effect.Target));
    }
    private bool IsLeMiHengRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.Jigu is not { Paid: true } || recovery.ParentFrameId == f.Id) return false;
        var parent = _resolutionStack.FindIndex(x => x.Id == f.Id);
        var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return parent >= 0 && child > parent + 1 && JiguFirstChild(f, _resolutionStack[parent + 1]) &&
            JiguObserverSuffix(parent) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 28211)]
    private bool AdvanceJiguChildren() => _pendingDecision is null && JiguObserverRoot() is not null &&
        TryAdvanceObserverSubtree(true);
}
