namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ExactTurnDefaultStatPhaseParent(ProgramSkillFrame f)
    {
        if (f.TriggerId is null || f.WindowContext is not { } c || c.OwnerSeat != f.OwnerSeat ||
            c.SourceSeat != f.OwnerSeat || c.TargetSeat != f.OwnerSeat || f.OwnerSeat != _currentSeat) return false;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (index < 1) return false;
        if (c.Window == SkillProgramTriggerWindow.PlayPhaseStarting)
            return _phase == TurnPhase.Play && _resolutionStack[index - 1] is PlayPhaseStartingBoundaryFrame parent &&
                parent.Id == c.ParentFrameId && parent.OwnerSeat == f.OwnerSeat && parent.ItemIndex >= 0 &&
                parent.ItemIndex < parent.Items.Count && parent.Items[parent.ItemIndex].Candidate is { } candidate &&
                MountObserverCandidateMatches(f, candidate);
        if (_resolutionStack[index - 1] is not ProgramLifecycleTriggerWindowFrame lifecycle || lifecycle.Id != c.ParentFrameId ||
            lifecycle.OwnerSeat != f.OwnerSeat || lifecycle.Window != c.Window || lifecycle.CandidateIndex < 0 ||
            lifecycle.CandidateIndex >= lifecycle.Candidates.Count || !MountObserverCandidateMatches(f, lifecycle.Candidates[lifecycle.CandidateIndex])) return false;
        return c.Window switch {
            SkillProgramTriggerWindow.DrawPhaseStarting => _phase == TurnPhase.Draw && lifecycle.Continuation == ProgramLifecycleContinuation.CompleteDrawPhase,
            SkillProgramTriggerWindow.DiscardPhaseStarting => _phase == TurnPhase.Discard && lifecycle.Continuation == ProgramLifecycleContinuation.CompleteDiscardPhase,
            _ => false };
    }

    private bool ExactTurnDefaultStatDamageParent(ProgramSkillFrame f, out DamageFrame damage,
        out DamageTriggerWindowFrame window, out IDamageAttempt attack)
    {
        damage = null!; window = null!; attack = null!;
        if (f.TriggerId is null || f.OwnerSeat == _currentSeat || f.WindowContext is not
                { Window: SkillProgramTriggerWindow.AfterDamageApplied, DamageFrameId: { } damageId, Amount: > 0 } c ||
            c.OwnerSeat != f.OwnerSeat || c.TargetSeat != f.OwnerSeat) return false;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (index < 2 || _resolutionStack[index - 1] is not DamageTriggerWindowFrame w || w.Id != c.ParentFrameId ||
            w.ParentFrameId != damageId || w.TriggerWindow != c.Window || w.TargetSeat != f.OwnerSeat ||
            w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count ||
            !MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex].ToProgramCandidate()) ||
            _resolutionStack[index - 2] is not DamageFrame d || d.Id != damageId || d.TargetSeat != f.OwnerSeat ||
            d.Amount != c.Amount || d.Amount <= 0) return false;
        var a = GetDamageTriggerAttack(w);
        if (a.ResolutionId != d.ParentFrameId || !a.DamageWasApplied || a.TargetSeat != f.OwnerSeat ||
            a.SourceSeat != d.SourceSeat || a.DamageAmount != d.Amount || GetDamageNature(a) != d.Nature ||
            c.SourceSeat != (a.IsSourceLess ? null : a.SourceSeat) || w.SourceSeat != d.SourceSeat) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (!TurnDefaultStatDamageRequestMatches(history, d.Id, d.SourceSeat, f.OwnerSeat, d.Amount, d.Nature, a.IsSourceLess)) return false;
        damage = d; window = w; attack = a; return true;
    }

    private static bool TurnDefaultStatDamageRequestMatches(IReadOnlyList<IGameEvent> history, long damageId,
        int source, int target, int amount, DamageNature nature, bool sourceLess)
    {
        if (damageId <= 0 || amount <= 0) return false;
        var requests = history.OfType<DamageRequestedEvent>().Where(e => e.ResolutionId == damageId).ToArray();
        return requests is [var request] && request.SourceSeat == source && request.TargetSeat == target &&
            request.Amount == amount && request.Nature == nature && request.SourceLess == sourceLess;
    }

    private bool ValidTurnDefaultStatAllocation(ProgramSkillFrame f)
    {
        if (f.TurnDefaultStatAllocation is not { } r || f.InstructionIndex != 1 || !ExactTurnDefaultStatPhaseParent(f) ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.PendingMovementContinuation is not null ||
            r.Origin.FrameId != f.Id || r.Origin.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) ||
            r.Origin.GameplayHash != f.GameplayHash || r.Origin.ParentFrameId != f.WindowContext!.ParentFrameId ||
            r.Origin.Window != f.WindowContext.Window || r.Origin.ActualTurnNumber != _turnNumber ||
            r.Origin.ActualTurnOwnerSeat != _currentSeat || r.Origin.FirstStat != FirstTurnDefaultStat(r.Origin.Window) ||
            r.Slots.Count != 4 || r.Slots.Any(n => n < 1) || r.UsedSlotMask is < 0 or > 15 ||
            !r.Slots.SequenceEqual(new[] { r.Origin.Slot0, r.Origin.Slot1, r.Origin.Slot2, r.Origin.Slot3 }) ||
            GetProgramTrigger(f).Effects is not [{ Op: SkillProgramEffectOp.AllocateCurrentTurnDefaultStats } effect] ||
            effect.StateId != r.Origin.StateId || !_turnDefaultStatStates.TryGetValue(new(f.OwnerSeat, f.SkillId, r.Origin.StateId), out var state) ||
            state.ActualTurnNumber != _turnNumber || !state.HasPool || !state.Slots.SequenceEqual(r.Slots) ||
            state.UsedSlotMask != r.UsedSlotMask || state.Assigned.ContainsKey(r.CurrentStat)) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<TurnDefaultStatAllocationStartedEvent>().Count(e => e.FrameId == f.Id) != 1 ||
            !history.Contains(r.Origin) || history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == f.Id &&
                e.OwnerSeat == f.OwnerSeat && e.SkillId == f.SkillId && e.BindingId == f.TriggerId &&
                e.SkillInstanceId == f.SkillInstanceId && e.Window == r.Origin.Window) != 1) return false;
        var assigned = history.OfType<TurnDefaultStatAssignedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.CurrentStat == r.Origin.FirstStat) return assigned.Length == 0 && r.UsedSlotMask == r.Origin.UsedSlotMask;
        return r.Origin.FirstStat == TurnDefaultStatKind.AttackRange && r.CurrentStat == TurnDefaultStatKind.SlashLimit &&
            assigned is [{ Stat: TurnDefaultStatKind.AttackRange } range] && range.Source == r.Origin.Source &&
            range.StateId == r.Origin.StateId && range.SlotIndex is >= 0 and < 4 &&
            range.Value == r.Slots[range.SlotIndex] && (r.Origin.UsedSlotMask & (1 << range.SlotIndex)) == 0 &&
            range.UsedSlotMask == (r.Origin.UsedSlotMask | (1 << range.SlotIndex)) && r.UsedSlotMask == range.UsedSlotMask &&
            range.ActualTurnNumber == _turnNumber && range.ActualTurnOwnerSeat == _currentSeat;
    }
    private void AssertTurnDefaultStatAllocation(ProgramSkillFrame f, ProgramExecutionPlan plan)
    {
        if (f.TurnDefaultStatAllocation is null)
        {
            if (!plan.Features.HasOperation(SkillProgramEffectOp.AllocateCurrentTurnDefaultStats)) return;
            var history = CompleteProgramEventHistory().ToArray();
            var origin = history.OfType<TurnDefaultStatAllocationStartedEvent>().SingleOrDefault(e => e.FrameId == f.Id);
            if (origin is not null && history.OfType<TurnDefaultStatAssignedEvent>().Count(e => e.FrameId == f.Id) !=
                    (origin.FirstStat == TurnDefaultStatKind.AttackRange ? 2 : 1))
                throw new InvalidOperationException("An issued default-stat allocation lost its owning receipt.");
            return;
        }
        if (!ValidTurnDefaultStatAllocation(f) || _resolutionStack.LastOrDefault()?.Id != f.Id)
            throw new InvalidOperationException("A default-stat allocation changed its pool, actual turn, exact phase parent or source.");
        if (_pendingDecision is { } decision && !IsTurnDefaultStatChoice(f, decision))
            throw new InvalidOperationException("A default-stat allocation changed its frozen published choices.");
    }
    private bool IsTurnDefaultStatChoice(ProgramSkillFrame f, PendingDecision decision)
    {
        if (f.TurnDefaultStatAllocation is null || decision.Kind != DecisionKind.ProgramTrigger || !decision.IsPrivate ||
            decision.PlayerSeat != f.OwnerSeat || decision.SourceSeat != f.OwnerSeat || decision.TargetSeat != f.OwnerSeat ||
            decision.SkillPrompt?.SkillId != f.SkillId || decision.ValidCardIds.Count != 0 || decision.ValidTargetSeats.Count != 0 ||
            decision.ValidContentIds.Count != 0 || decision.RequiredCardCount != 0) return false;
        return AssistedChoicesEqual(decision.Choices, TurnDefaultStatChoices(f));
    }

    private bool TurnDefaultStatSourceMatches(CardConversionSource source, string hash, string stateId, SkillProgramEffectOp? requiredOp = null)
    {
        if (!IsValidPlayerSeat(source.OwnerSeat) || string.IsNullOrWhiteSpace(source.SkillInstanceId) ||
            !_contentRegistry.Skills.TryGetValue(source.SkillId, out var skill) || skill.Program is not { } program || program.GameplayHash != hash)
            return false;
        var trigger = program.Triggers.SingleOrDefault(t => t.Id == source.BindingId);
        return trigger?.Effects is [var effect] && TurnDefaultStatsComposition.IsOperation(effect.Op) &&
            effect.StateId == stateId && (requiredOp is null || effect.Op == requiredOp);
    }
    private void AssertTurnDefaultStatState()
    {
        if (!TracksTurnDefaultStats) return;
        // The monotonic diagnostic bit keeps the no-family path constant-time,
        // while still detecting a lost state dictionary after its first fact.
        if (!_turnDefaultStatLedgerStarted)
        {
            if (_turnDefaultStatStates.Count != 0) throw new InvalidOperationException("Default-stat state exists without its issued ledger.");
            return;
        }
        var history = CompleteProgramEventHistory().Where(e => e is TurnDefaultStatPoolInitializedEvent or
            TurnDefaultStatMinimumIncreasedEvent or TurnDefaultStatAllocationStartedEvent or TurnDefaultStatAssignedEvent or
            TurnStartedEvent or DamageRequestedEvent or AfterDamageEvent or ProgramBindingStartedEvent or DamageTriggerWindowOpenedEvent).ToArray();
        var turns = history.OfType<TurnStartedEvent>().ToLookup(e => (e.TurnNumber, e.ActorSeat));
        var bindings = history.OfType<ProgramBindingStartedEvent>().ToLookup(e => e.FrameId);
        var windows = history.OfType<DamageTriggerWindowOpenedEvent>().ToLookup(e => e.ResolutionId);
        var requests = history.OfType<DamageRequestedEvent>().ToLookup(e => e.ResolutionId);
        var completedDamage = history.OfType<AfterDamageEvent>().ToLookup(e => e.ResolutionId);
        bool MatchesAppliedDamage(TurnDefaultStatMinimumIncreasedEvent increased, int owner)
        {
            var requested = requests[increased.DamageFrameId].ToArray();
            if (requested is not [var request] || request.TargetSeat != owner || request.Amount != increased.Amount ||
                request.Nature != increased.Nature || increased.Amount <= 0) return false;
            var completed = completedDamage[increased.DamageFrameId].ToArray();
            if (completed.Length != 0) return completed is [var paid] && paid.SourceSeat == request.SourceSeat &&
                paid.TargetSeat == owner && paid.Amount == increased.Amount && paid.Nature == increased.Nature && paid.SourceLess == request.SourceLess;
            // BeforeDamage observers can themselves apply damage. A chronological
            // "last request" heuristic would attribute the outer application to
            // that nested request. The still-live typed producer is authoritative
            // until its exact AfterDamage invoice is published.
            if (_resolutionStack.SingleOrDefault(f => f.Id == increased.DamageFrameId) is not DamageFrame damage ||
                damage.ParentFrameId != increased.AttackFrameId || damage.SourceSeat != request.SourceSeat || damage.TargetSeat != owner ||
                damage.Amount != increased.Amount || damage.Nature != increased.Nature ||
                _resolutionStack.SingleOrDefault(f => f.Id == increased.DamageWindowId) is not DamageTriggerWindowFrame window ||
                window.ParentFrameId != damage.Id || window.TriggerWindow != SkillProgramTriggerWindow.AfterDamageApplied) return false;
            var attack = GetDamageTriggerAttack(window);
            return attack.ResolutionId == increased.AttackFrameId && attack.DamageWasApplied && attack.SourceSeat == request.SourceSeat &&
                attack.TargetSeat == owner && attack.DamageAmount == increased.Amount && GetDamageNature(attack) == increased.Nature &&
                attack.IsSourceLess == request.SourceLess;
        }
        var expected = new Dictionary<TurnDefaultStatKey, TurnDefaultStatState>();
        var origins = new Dictionary<long, TurnDefaultStatAllocationStartedEvent>();
        var frameAssignments = new Dictionary<long, List<TurnDefaultStatAssignedEvent>>();
        var damages = new HashSet<(TurnDefaultStatKey Key, long DamageId)>();
        foreach (var fact in history)
        {
            if (fact is TurnDefaultStatPoolInitializedEvent initialized)
            {
                var key = new TurnDefaultStatKey(initialized.Source.OwnerSeat, initialized.Source.SkillId, initialized.StateId);
                if (!TurnDefaultStatSourceMatches(initialized.Source, initialized.GameplayHash, initialized.StateId) ||
                    initialized.ActualTurnNumber <= 0 || initialized.ActualTurnOwnerSeat != key.OwnerSeat ||
                    turns[(initialized.ActualTurnNumber, key.OwnerSeat)].Count() != 1)
                    throw new InvalidOperationException("A default-stat pool lost its real actual-turn boundary or source metadata.");
                expected.TryGetValue(key, out var previous);
                var slots = new[] { initialized.Slot0, initialized.Slot1, initialized.Slot2, initialized.Slot3 };
                if (slots.Any(n => n < 1) || initialized.MidTurn && (!initialized.HasPool ||
                        previous is { HasPool: true } && previous.ActualTurnNumber == initialized.ActualTurnNumber || !slots.SequenceEqual(NewTurnDefaultSlots())) ||
                    !initialized.MidTurn && (previous?.ActualTurnNumber >= initialized.ActualTurnNumber ||
                        !slots.SequenceEqual(previous?.NextSlots ?? NewTurnDefaultSlots())))
                    throw new InvalidOperationException("A default-stat pool reused a turn, retained expired boosts or changed its four stable slots.");
                expected[key] = new(initialized.Source, initialized.GameplayHash, initialized.ActualTurnNumber, initialized.HasPool,
                    Array.AsReadOnly(slots), 0, EmptyTurnDefaultAssignments(), initialized.MidTurn ? previous?.NextSlots ?? NewTurnDefaultSlots() : NewTurnDefaultSlots());
            }
            else if (fact is TurnDefaultStatMinimumIncreasedEvent increased)
            {
                var key = new TurnDefaultStatKey(increased.Source.OwnerSeat, increased.Source.SkillId, increased.StateId);
                if (!TurnDefaultStatSourceMatches(increased.Source, increased.GameplayHash, increased.StateId, SkillProgramEffectOp.IncreaseNextTurnDefaultStatMinimum) ||
                    increased.FrameId <= 0 || increased.DamageWindowId <= 0 || increased.ActualTurnOwnerSeat == key.OwnerSeat ||
                    increased.ActualTurnNumber <= 0 || increased.AttackFrameId <= 0 || !IsValidPlayerSeat(increased.ActualTurnOwnerSeat) ||
                    turns[(increased.ActualTurnNumber, increased.ActualTurnOwnerSeat)].Count() != 1 ||
                    !damages.Add((key, increased.DamageFrameId)) || !MatchesAppliedDamage(increased, key.OwnerSeat) ||
                    windows[increased.DamageWindowId].Count(e => e.DamageFrameId == increased.DamageFrameId && e.TargetSeat == key.OwnerSeat &&
                        e.Candidates.Count(c => c.OwnerSeat == key.OwnerSeat && c.ProgramId == key.SkillId &&
                            c.ProgramTriggerId == increased.Source.BindingId && c.SkillInstanceId == increased.Source.SkillInstanceId &&
                            c.GameplayHash == increased.GameplayHash && c.OccurrenceIndex == 0) == 1) != 1 ||
                    bindings[increased.FrameId].Count(e => e.OwnerSeat == key.OwnerSeat &&
                        e.SkillId == key.SkillId && e.BindingId == increased.Source.BindingId && e.SkillInstanceId == increased.Source.SkillInstanceId &&
                        e.Window == SkillProgramTriggerWindow.AfterDamageApplied) != 1)
                    throw new InvalidOperationException("A next-turn minimum increment lost its one genuine out-of-turn damage event.");
                var previous = expected.GetValueOrDefault(key) ?? EmptyTurnDefaultState(increased.Source, increased.GameplayHash);
                var slots = previous.NextSlots.ToArray(); var minimum = slots.Min(); var slot = Array.IndexOf(slots, minimum);
                if (increased.SlotIndex != slot || increased.Before != minimum || increased.After != checked(minimum + 1))
                    throw new InvalidOperationException("A next-turn increment changed the stable first minimum slot.");
                slots[slot] = increased.After; expected[key] = previous with { NextSlots = Array.AsReadOnly(slots) };
            }
            else if (fact is TurnDefaultStatAllocationStartedEvent started)
            {
                var key = new TurnDefaultStatKey(started.Source.OwnerSeat, started.Source.SkillId, started.StateId);
                if (!TurnDefaultStatSourceMatches(started.Source, started.GameplayHash, started.StateId, SkillProgramEffectOp.AllocateCurrentTurnDefaultStats) ||
                    !expected.TryGetValue(key, out var state) || !state.HasPool || state.ActualTurnNumber != started.ActualTurnNumber ||
                    started.ActualTurnOwnerSeat != key.OwnerSeat || started.FirstStat != FirstTurnDefaultStat(started.Window) ||
                    state.Assigned.ContainsKey(started.FirstStat) || started.UsedSlotMask != state.UsedSlotMask ||
                    !state.Slots.SequenceEqual(new[] { started.Slot0, started.Slot1, started.Slot2, started.Slot3 }) ||
                    started.FrameId <= 0 || started.ParentFrameId <= 0 || bindings[started.FrameId].Count(e => e.OwnerSeat == key.OwnerSeat &&
                        e.SkillId == key.SkillId && e.BindingId == started.Source.BindingId && e.SkillInstanceId == started.Source.SkillInstanceId &&
                        e.Window == started.Window) != 1 || !origins.TryAdd(started.FrameId, started))
                    throw new InvalidOperationException("A default-stat allocation lost its current pool or repeated an already assigned phase value.");
                frameAssignments.Add(started.FrameId, []);
            }
            else if (fact is TurnDefaultStatAssignedEvent assigned)
            {
                var key = new TurnDefaultStatKey(assigned.Source.OwnerSeat, assigned.Source.SkillId, assigned.StateId);
                if (!origins.TryGetValue(assigned.FrameId, out var origin) || assigned.Source != origin.Source ||
                    assigned.StateId != origin.StateId || assigned.ActualTurnNumber != origin.ActualTurnNumber ||
                    assigned.ActualTurnOwnerSeat != key.OwnerSeat || !expected.TryGetValue(key, out var state) ||
                    assigned.SlotIndex is < 0 or > 3 || state.Assigned.ContainsKey(assigned.Stat) ||
                    (state.UsedSlotMask & (1 << assigned.SlotIndex)) != 0 || assigned.Value != state.Slots[assigned.SlotIndex] ||
                    assigned.UsedSlotMask != (state.UsedSlotMask | (1 << assigned.SlotIndex)))
                    throw new InvalidOperationException("A default-stat assignment changed its frozen slot, turn or source.");
                var prior = frameAssignments[assigned.FrameId];
                if (prior.Count == 0 ? assigned.Stat != origin.FirstStat : prior.Count != 1 ||
                    origin.FirstStat != TurnDefaultStatKind.AttackRange || assigned.Stat != TurnDefaultStatKind.SlashLimit)
                    throw new InvalidOperationException("Default-stat assignments lost Draw / Range-then-Slash / HandLimit ordering.");
                prior.Add(assigned);
                var assignments = state.Assigned.ToDictionary(p => p.Key, p => p.Value);
                assignments.Add(assigned.Stat, new(assigned.Value, assigned.FrameId, assigned.SlotIndex));
                expected[key] = state with { UsedSlotMask = assigned.UsedSlotMask,
                    Assigned = new System.Collections.ObjectModel.ReadOnlyDictionary<TurnDefaultStatKind, TurnDefaultStatAssignment>(assignments) };
            }
        }
        foreach (var pair in origins)
        {
            var required = pair.Value.FirstStat == TurnDefaultStatKind.AttackRange ? 2 : 1;
            if (frameAssignments[pair.Key].Count < required && !_resolutionStack.OfType<ProgramSkillFrame>().Any(f =>
                    f.Id == pair.Key && f.TurnDefaultStatAllocation?.Origin == pair.Value))
                throw new InvalidOperationException("An unfinished default-stat allocation lost its owning frame and frozen receipt.");
        }
        if (expected.Count != _turnDefaultStatStates.Count || expected.Any(pair =>
                !_turnDefaultStatStates.TryGetValue(pair.Key, out var actual) || actual.Source != pair.Value.Source ||
                actual.GameplayHash != pair.Value.GameplayHash || actual.ActualTurnNumber != pair.Value.ActualTurnNumber ||
                actual.HasPool != pair.Value.HasPool || actual.UsedSlotMask != pair.Value.UsedSlotMask ||
                !actual.Slots.SequenceEqual(pair.Value.Slots) || !actual.NextSlots.SequenceEqual(pair.Value.NextSlots) ||
                !actual.Assigned.OrderBy(p => p.Key).SequenceEqual(pair.Value.Assigned.OrderBy(p => p.Key))))
            throw new InvalidOperationException("Durable default-stat pools diverged from their scalar actual-turn ledger.");
    }
}
