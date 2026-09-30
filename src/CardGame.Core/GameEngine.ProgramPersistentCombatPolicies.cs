namespace CardGame.Core;

public sealed record ProgramDamageHandLimitChangedEvent(int OwnerSeat, string SkillId, int TurnNumber, int PhaseInstanceId, int DamageCount) : IGameEvent;
public sealed record ProgramGameFactionAttackRangeTargetsGrantedEvent(int OwnerSeat, string SkillId, string FactionId, IReadOnlyList<int> TargetSeats) : IGameEvent;

public sealed partial class GameEngine
{
    private sealed record ProgramPlayDamageHandLimit(int OwnerSeat, string SkillId, string SkillInstanceId, int TurnNumber, int PhaseInstanceId, int DamageCount);
    private sealed record ProgramGameFactionRangeTargets(int OwnerSeat, string SkillId, string FactionId, IReadOnlyList<int> TargetSeats);
    private readonly Dictionary<int, ProgramPlayDamageHandLimit> _programPlayDamageHandLimits = [];
    private readonly List<ProgramGameFactionRangeTargets> _programGameFactionRangeTargets = [];

    private sealed partial class ProgramSkillHost : IPersistentCombatPolicyProgramHost
    {
        public void SetTurnHandLimitFromPlayDamage(ProgramSkillFrame frame) => engine.SetProgramTurnHandLimitFromPlayDamage(frame);
        public void GrantGameFactionAttackRangeTargets(ProgramSkillFrame frame, string factionId) => engine.GrantProgramGameFactionAttackRangeTargets(frame, factionId);
    }

    private void SetProgramTurnHandLimitFromPlayDamage(ProgramSkillFrame frame)
    {
        if (frame.OwnerSeat != _currentSeat || _phase != TurnPhase.Play ||
            frame.WindowContext?.Window != SkillProgramTriggerWindow.PlayPhaseStarting)
            throw new InvalidOperationException("Damage-derived hand limit requires the owner's current play phase start.");
        var state = new ProgramPlayDamageHandLimit(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId,
            _turnNumber, _cardUseDebitPhaseInstanceId, _playPhaseDamageDealtByCurrentPlayer);
        _programPlayDamageHandLimits[frame.OwnerSeat] = state;
        QueueGameEvent(new ProgramDamageHandLimitChangedEvent(state.OwnerSeat, state.SkillId, state.TurnNumber, state.PhaseInstanceId, state.DamageCount));
    }

    // Called only after a real damage event has committed. A later inserted play
    // phase cannot count toward a prior phase's promise unless it activates anew.
    private void AccumulateProgramPlayDamageHandLimit(int sourceSeat, int amount)
    {
        if (amount <= 0 || _phase != TurnPhase.Play || sourceSeat != _currentSeat ||
            !_programPlayDamageHandLimits.TryGetValue(sourceSeat, out var state) ||
            state.TurnNumber != _turnNumber || state.PhaseInstanceId != _cardUseDebitPhaseInstanceId) return;
        state = state with { DamageCount = checked(state.DamageCount + amount) };
        _programPlayDamageHandLimits[sourceSeat] = state;
        QueueGameEvent(new ProgramDamageHandLimitChangedEvent(state.OwnerSeat, state.SkillId, state.TurnNumber, state.PhaseInstanceId, state.DamageCount));
    }

    private IEnumerable<RuleQueryContribution> ProgramDamageHandLimitContributions(CharacterState owner)
    {
        if (_currentSeat == owner.Seat && _programPlayDamageHandLimits.TryGetValue(owner.Seat, out var state) && state.TurnNumber == _turnNumber)
            yield return new FiniteRuleQueryContribution($"turn:{state.TurnNumber}:{state.SkillId}:{state.SkillInstanceId}:play-damage-hand-limit",
                SkillRuleOperation.Set, state.DamageCount);
    }

    private void GrantProgramGameFactionAttackRangeTargets(ProgramSkillFrame frame, string factionId)
    {
        if (_players[frame.OwnerSeat].Role != Role.Lord || frame.SelectedTargetSeats.Count is < 1 or > 2 ||
            frame.SelectedTargetSeats.Distinct().Count() != frame.SelectedTargetSeats.Count ||
            frame.SelectedTargetSeats.Any(seat => !IsValidPlayerSeat(seat) || !_players[seat].IsAlive))
            throw new InvalidOperationException("A game-long faction range grant requires a lord and one or two distinct living targets.");
        var state = new ProgramGameFactionRangeTargets(frame.OwnerSeat, frame.SkillId, factionId, frame.SelectedTargetSeats.ToArray());
        _programGameFactionRangeTargets.Add(state);
        QueueGameEvent(new ProgramGameFactionAttackRangeTargetsGrantedEvent(state.OwnerSeat, state.SkillId, state.FactionId, state.TargetSeats));
    }

    private bool IsGameFactionAttackRangeTarget(int actorSeat, int targetSeat) => actorSeat != targetSeat &&
        IsValidPlayerSeat(actorSeat) && IsValidPlayerSeat(targetSeat) && _players[actorSeat].IsAlive && _players[targetSeat].IsAlive &&
        _programGameFactionRangeTargets.Any(state => actorSeat != state.OwnerSeat && state.TargetSeats.Contains(targetSeat) &&
            string.Equals(GetEffectiveFactionId(_players[actorSeat]), state.FactionId, StringComparison.Ordinal));
}
