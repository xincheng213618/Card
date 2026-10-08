using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed record TurnDefaultStatKey(int OwnerSeat, string SkillId, string StateId);
    private sealed record TurnDefaultStatAssignment(int Value, long FrameId, int SlotIndex);
    private sealed record TurnDefaultStatState(CardConversionSource Source, string GameplayHash,
        int ActualTurnNumber, bool HasPool, IReadOnlyList<int> Slots, int UsedSlotMask,
        IReadOnlyDictionary<TurnDefaultStatKind, TurnDefaultStatAssignment> Assigned, IReadOnlyList<int> NextSlots);
    // This is durable gameplay state. Choices and their frozen pool remain only
    // on ProgramSkillFrame; no pending action is retained in this dictionary.
    private readonly Dictionary<TurnDefaultStatKey, TurnDefaultStatState> _turnDefaultStatStates = new();
    private bool _turnDefaultStatLedgerStarted;
    private bool TracksTurnDefaultStats => _contentRegistry.ProgramDependencies
        .HasTriggerOperation(SkillProgramEffectOp.AllocateCurrentTurnDefaultStats);
    private static IReadOnlyList<int> NewTurnDefaultSlots() => Array.AsReadOnly(new[] { 1, 2, 3, 4 });
    private static IReadOnlyDictionary<TurnDefaultStatKind, TurnDefaultStatAssignment> EmptyTurnDefaultAssignments() =>
        new System.Collections.ObjectModel.ReadOnlyDictionary<TurnDefaultStatKind, TurnDefaultStatAssignment>(new Dictionary<TurnDefaultStatKind, TurnDefaultStatAssignment>());
    private static TurnDefaultStatKind FirstTurnDefaultStat(SkillProgramTriggerWindow window) => window switch {
        SkillProgramTriggerWindow.DrawPhaseStarting => TurnDefaultStatKind.DrawCount,
        SkillProgramTriggerWindow.PlayPhaseStarting => TurnDefaultStatKind.AttackRange,
        SkillProgramTriggerWindow.DiscardPhaseStarting => TurnDefaultStatKind.HandLimit,
        _ => throw new InvalidOperationException("A default-stat allocation has no phase boundary.") };
    private static string TurnDefaultStatName(TurnDefaultStatKind stat) => stat switch {
        TurnDefaultStatKind.DrawCount => "摸牌数", TurnDefaultStatKind.AttackRange => "攻击范围",
        TurnDefaultStatKind.SlashLimit => "杀使用次数", TurnDefaultStatKind.HandLimit => "手牌上限",
        _ => throw new InvalidOperationException("Unknown default-stat kind.") };
    private static TurnDefaultStatState EmptyTurnDefaultState(CardConversionSource source, string hash) =>
        new(source, hash, 0, false, NewTurnDefaultSlots(), 0, EmptyTurnDefaultAssignments(), NewTurnDefaultSlots());
    private void RecordTurnDefaultStatFact(IGameEvent fact)
    { _turnDefaultStatLedgerStarted = true; AdvanceEventRulesAndQueueFact(fact); }

    private void ObserveTurnDefaultStats(IGameEvent payload)
    {
        if (!TracksTurnDefaultStats || payload is not TurnStartedEvent started || started.TurnNumber != _turnNumber ||
            started.ActorSeat != _currentSeat || !IsValidPlayerSeat(started.ActorSeat)) return;
        var owner = _players[started.ActorSeat];
        var sources = GetSkillBindingShard(owner).ProgramInstances.SelectMany(instance => instance.Program.Triggers
                .Where(t => t.Window == SkillProgramTriggerWindow.DrawPhaseStarting && t.Effects is
                    [{ Op: SkillProgramEffectOp.AllocateCurrentTurnDefaultStats }])
                .Select(t => (Key: new TurnDefaultStatKey(owner.Seat, instance.SkillId, t.Effects[0].StateId!),
                    Source: new CardConversionSource(instance.SkillId, t.Id, owner.Seat, instance.SkillInstanceId),
                    Hash: instance.Program.GameplayHash)))
            .GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Source.SkillInstanceId, StringComparer.Ordinal).First());
        foreach (var key in _turnDefaultStatStates.Keys.Where(k => k.OwnerSeat == owner.Seat).Union(sources.Keys)
                     .OrderBy(k => k.SkillId, StringComparer.Ordinal).ThenBy(k => k.StateId, StringComparer.Ordinal).ToArray())
        {
            var hasBinding = sources.TryGetValue(key, out var binding); var qualified = owner.IsAlive && hasBinding;
            var old = _turnDefaultStatStates.GetValueOrDefault(key) ?? EmptyTurnDefaultState(binding.Source, binding.Hash);
            if (old.ActualTurnNumber == _turnNumber) throw new InvalidOperationException("An actual turn cannot initialize its default-stat pool twice.");
            var source = qualified ? binding.Source : old.Source; var hash = qualified ? binding.Hash : old.GameplayHash;
            var slots = old.NextSlots;
            _turnDefaultStatStates[key] = new(source, hash, _turnNumber, qualified, slots, 0, EmptyTurnDefaultAssignments(), NewTurnDefaultSlots());
            // Even an unqualified or face-down owner consumes the pending pool.
            // A later acquisition during this turn starts from the base values.
            RecordTurnDefaultStatFact(new TurnDefaultStatPoolInitializedEvent(source, hash, key.StateId,
                _turnNumber, owner.Seat, qualified, false, slots[0], slots[1], slots[2], slots[3]));
        }
    }

    private int? GetCurrentTurnDefaultStat(int seat, SkillRuleQuery query)
    {
        if (!TracksTurnDefaultStats || seat != _currentSeat || _turnNumber <= 0 ||
            _phase is TurnPhase.Finished or TurnPhase.NotStarted) return null;
        var stat = query switch { SkillRuleQuery.DrawCount => TurnDefaultStatKind.DrawCount,
            SkillRuleQuery.AttackRange => TurnDefaultStatKind.AttackRange, SkillRuleQuery.SlashLimit => TurnDefaultStatKind.SlashLimit,
            SkillRuleQuery.HandLimit => TurnDefaultStatKind.HandLimit, _ => (TurnDefaultStatKind?)null };
        if (stat is null) return null;
        // Concurrent generic sources use the last actual assignment, rather than
        // dictionary iteration or the current lifetime of a grant instance.
        return _turnDefaultStatStates.Where(p => p.Key.OwnerSeat == seat && p.Value.ActualTurnNumber == _turnNumber && p.Value.HasPool)
            .Select(p => p.Value.Assigned.GetValueOrDefault(stat.Value)).OfType<TurnDefaultStatAssignment>()
            .MaxBy(a => a.FrameId)?.Value;
    }

    private bool CanOfferTurnDefaultStatProgram(int ownerSeat, string skillId, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Effects is not [var effect] || !TurnDefaultStatsComposition.IsOperation(effect.Op)) return true;
        if (context.OwnerSeat != ownerSeat || !IsValidPlayerSeat(ownerSeat) || !_players[ownerSeat].IsAlive) return false;
        if (effect.Op == SkillProgramEffectOp.IncreaseNextTurnDefaultStatMinimum)
            return _turnNumber > 0 && context.Window == SkillProgramTriggerWindow.AfterDamageApplied && context.TargetSeat == ownerSeat &&
                ownerSeat != _currentSeat && context.Amount > 0 && context.DamageFrameId is { } damageId &&
                !CompleteProgramEventHistory().OfType<TurnDefaultStatMinimumIncreasedEvent>().Any(e =>
                    e.Source.OwnerSeat == ownerSeat && e.Source.SkillId == skillId && e.StateId == effect.StateId && e.DamageFrameId == damageId);
        if (ownerSeat != _currentSeat || context.SourceSeat != ownerSeat || context.TargetSeat != ownerSeat ||
            context.Window is not (SkillProgramTriggerWindow.DrawPhaseStarting or SkillProgramTriggerWindow.PlayPhaseStarting or
                SkillProgramTriggerWindow.DiscardPhaseStarting)) return false;
        var key = new TurnDefaultStatKey(ownerSeat, skillId, effect.StateId!);
        return !_turnDefaultStatStates.TryGetValue(key, out var state) || state.ActualTurnNumber != _turnNumber || !state.HasPool ||
            !state.Assigned.ContainsKey(FirstTurnDefaultStat(context.Window));
    }

    private SkillProgramStepOutcome ExecuteTurnDefaultStatOperation(ProgramSkillFrame supplied, SkillProgramEffect effect)
    {
        var f = GetActiveProgramFrame(supplied.Id); var trigger = GetProgramTrigger(f);
        if (f.InstructionIndex != 1 || f.TurnDefaultStatAllocation is not null || trigger.Effects is not [var configured] || configured != effect ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.WindowContext is not { } context ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            !CanOfferTurnDefaultStatProgram(f.OwnerSeat, f.SkillId, trigger, context))
            throw new InvalidOperationException("The default-stat operation lost its exact mandatory candidate or source.");
        return effect.Op == SkillProgramEffectOp.AllocateCurrentTurnDefaultStats
            ? BeginTurnDefaultStatAllocation(f, effect.StateId!) : IncreaseNextTurnDefaultStatMinimum(f, effect.StateId!);
    }

    private SkillProgramStepOutcome BeginTurnDefaultStatAllocation(ProgramSkillFrame f, string stateId)
    {
        if (!ExactTurnDefaultStatPhaseParent(f)) throw new InvalidOperationException("Default-stat allocation lost its actual owner phase parent.");
        var key = new TurnDefaultStatKey(f.OwnerSeat, f.SkillId, stateId);
        var source = new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
        var state = _turnDefaultStatStates.GetValueOrDefault(key);
        if (state is null || state.ActualTurnNumber != _turnNumber || !state.HasPool)
        {
            var next = state?.NextSlots ?? NewTurnDefaultSlots();
            state = new(source, f.GameplayHash, _turnNumber, true, NewTurnDefaultSlots(), 0, EmptyTurnDefaultAssignments(), next);
            _turnDefaultStatStates[key] = state;
            RecordTurnDefaultStatFact(new TurnDefaultStatPoolInitializedEvent(source, f.GameplayHash, stateId,
                _turnNumber, _currentSeat, true, true, 1, 2, 3, 4));
        }
        var stat = FirstTurnDefaultStat(f.WindowContext!.Window);
        var origin = new TurnDefaultStatAllocationStartedEvent(f.Id, source, f.GameplayHash, stateId,
            f.WindowContext.ParentFrameId, f.WindowContext.Window, _turnNumber, _currentSeat, stat,
            state.UsedSlotMask, state.Slots[0], state.Slots[1], state.Slots[2], state.Slots[3]);
        ReplaceRuntimeTop(f = f with { TurnDefaultStatAllocation = new() { Origin = origin,
            CurrentStat = stat, UsedSlotMask = state.UsedSlotMask, Slots = state.Slots } });
        RecordTurnDefaultStatFact(origin); PublishTurnDefaultStatChoice(f); return SkillProgramStepOutcome.AwaitChoice;
    }

    private SkillProgramStepOutcome IncreaseNextTurnDefaultStatMinimum(ProgramSkillFrame f, string stateId)
    {
        if (!ExactTurnDefaultStatDamageParent(f, out var damage, out var window, out var attack))
            throw new InvalidOperationException("A next-turn minimum increment requires one real positive applied-damage event outside the owner's turn.");
        var source = new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
        var key = new TurnDefaultStatKey(f.OwnerSeat, f.SkillId, stateId);
        var state = _turnDefaultStatStates.GetValueOrDefault(key) ?? EmptyTurnDefaultState(source, f.GameplayHash);
        var values = state.NextSlots.ToArray(); var minimum = values.Min(); var slot = Array.IndexOf(values, minimum);
        values[slot] = checked(minimum + 1);
        _turnDefaultStatStates[key] = state with { NextSlots = Array.AsReadOnly(values) };
        RecordTurnDefaultStatFact(new TurnDefaultStatMinimumIncreasedEvent(f.Id, source, f.GameplayHash, stateId,
            window.Id, damage.Id, attack.ResolutionId, _turnNumber, _currentSeat, damage.Amount, damage.Nature, slot, minimum, values[slot]));
        return SkillProgramStepOutcome.Continue;
    }

    private IReadOnlyList<PromptChoice> TurnDefaultStatChoices(ProgramSkillFrame f)
    {
        var r = f.TurnDefaultStatAllocation!;
        return Array.AsReadOnly(Enumerable.Range(0, 4).Where(slot => (r.UsedSlotMask & (1 << slot)) == 0).Select(slot =>
            FreezeDirectedDistanceChoice(new PromptChoice(new($"turn-default-stat.{f.Id}.{r.CurrentStat}.{slot}"),
                $"{TurnDefaultStatName(r.CurrentStat)}：{r.Slots[slot]}", [], [], new Dictionary<string, string> {
                    ["program-action"] = "turn-default-stat", ["stat-kind"] = r.CurrentStat.ToString(),
                    ["slot-index"] = slot.ToString(CultureInfo.InvariantCulture), ["value"] = r.Slots[slot].ToString(CultureInfo.InvariantCulture) }))).ToArray());
    }
    private void PublishTurnDefaultStatChoice(ProgramSkillFrame f)
    {
        var choices = TurnDefaultStatChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        if (choices.Count == 0) throw new InvalidOperationException("A mandatory default-stat allocation has no remaining slot.");
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, $"请选择本回合的{TurnDefaultStatName(f.TurnDefaultStatAllocation!.CurrentStat)}。", [], [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    private void ResolveTurnDefaultStatChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Default-stat choice lost its owner.");
        var plan = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!);
        AssertTurnDefaultStatAllocation(f, plan);
        if (f.TurnDefaultStatAllocation is not { } r || _pendingDecision is not { } decision || !IsTurnDefaultStatChoice(f, decision) ||
            !AssistedChoicesEqual([choice], TurnDefaultStatChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("A default-stat choice changed its frozen slot or phase.");
        var slot = int.Parse(choice.Parameters["slot-index"], CultureInfo.InvariantCulture);
        var key = new TurnDefaultStatKey(f.OwnerSeat, f.SkillId, r.Origin.StateId); var state = _turnDefaultStatStates[key];
        var assignments = state.Assigned.ToDictionary(p => p.Key, p => p.Value);
        assignments.Add(r.CurrentStat, new(r.Slots[slot], f.Id, slot));
        var mask = r.UsedSlotMask | (1 << slot);
        _turnDefaultStatStates[key] = state with { UsedSlotMask = mask,
            Assigned = new System.Collections.ObjectModel.ReadOnlyDictionary<TurnDefaultStatKind, TurnDefaultStatAssignment>(assignments) };
        ClearPendingDecision();
        RecordTurnDefaultStatFact(new TurnDefaultStatAssignedEvent(f.Id, r.Origin.Source, r.Origin.StateId,
            _turnNumber, _currentSeat, r.CurrentStat, slot, r.Slots[slot], mask));
        if (r.CurrentStat == TurnDefaultStatKind.DrawCount && _resolutionStack[^2] is ProgramLifecycleTriggerWindowFrame
                { Window: SkillProgramTriggerWindow.DrawPhaseStarting, FrozenBaseDrawCount: not null } draw &&
            draw.Id == r.Origin.ParentFrameId && draw.OwnerSeat == f.OwnerSeat)
            ReplaceRuntimeFrame(draw.Id, draw with { FrozenBaseDrawCount = GetTurnDrawCount(_players[f.OwnerSeat]) });
        if (r.CurrentStat == TurnDefaultStatKind.AttackRange)
        {
            ReplaceRuntimeTop(f = f with { TurnDefaultStatAllocation = r with { CurrentStat = TurnDefaultStatKind.SlashLimit, UsedSlotMask = mask } });
            PublishTurnDefaultStatChoice(f); return;
        }
        ReplaceRuntimeTop(f with { TurnDefaultStatAllocation = null }); AdvanceRuntimeProgram(f.Id);
    }
    private sealed partial class ProgramSkillHost : ITurnDefaultStatsProgramHost
    {
        public SkillProgramStepOutcome ExecuteTurnDefaultStatOperation(ProgramSkillFrame f, SkillProgramEffect e) =>
            engine.ExecuteTurnDefaultStatOperation(f, e);
    }
}
