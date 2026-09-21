namespace CardGame.Core;

[Flags]
public enum SkillTag
{
    None = 0,
    Lord = 1 << 0,
    Locked = 1 << 1,
    Limited = 1 << 2,
    Awakening = 1 << 3,
    Conversion = 1 << 4
}

[Flags]
public enum SkillExecutionForm
{
    None = 0,
    State = 1 << 0,
    Trigger = 1 << 1
}

[Flags]
public enum SkillActionForm
{
    None = 0,
    Active = 1 << 0
}

public enum SkillUsageScope
{
    Game,
    Round,
    Turn,
    Phase,
    Event
}

public enum SkillPolarity
{
    Yang,
    Yin
}

/// <summary>
/// Trusted runtime records for named skills. Tags describe the skill; this
/// store records actual use and conversion state. Neither is inferred from
/// localized presentation text.
/// </summary>
public sealed class SkillRuntimeStateStore
{
    private readonly Dictionary<SkillUsageKey, int> _usage = [];
    private readonly Dictionary<SkillStateKey, SkillPolarity> _initialPolarities = [];
    private readonly Dictionary<SkillStateKey, SkillPolarity> _polarities = [];

    public int GetUsage(int ownerSeat, string skillId, string usageId, SkillUsageScope scope)
    {
        var key = CreateUsageKey(ownerSeat, skillId, usageId, scope);
        return _usage.GetValueOrDefault(key);
    }

    public bool TryConsumeUsage(
        int ownerSeat,
        string skillId,
        string usageId,
        SkillUsageScope scope,
        int limit)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "A skill usage limit must be positive.");

        var key = CreateUsageKey(ownerSeat, skillId, usageId, scope);
        var current = _usage.GetValueOrDefault(key);
        if (current >= limit) return false;
        _usage[key] = current + 1;
        return true;
    }

    /// <summary>
    /// Starts a new phase. Phase limits belong to the shared game phase, so
    /// records for every skill owner must expire together.
    /// </summary>
    public void ResetPhase() =>
        RemoveUsage(scope => scope is SkillUsageScope.Phase or SkillUsageScope.Event);

    /// <summary>
    /// Starts a new turn. Off-turn triggers also receive a fresh per-turn
    /// allowance, so this clears every owner's turn and phase records.
    /// </summary>
    public void ResetTurn() =>
        RemoveUsage(scope => scope is SkillUsageScope.Turn or SkillUsageScope.Phase or SkillUsageScope.Event);

    /// <summary>
    /// Starts a new round and expires all shorter-lived usage records.
    /// Game-scoped limited-skill records remain consumed.
    /// </summary>
    public void ResetRound() =>
        RemoveUsage(scope => scope is SkillUsageScope.Round or SkillUsageScope.Turn or SkillUsageScope.Phase or SkillUsageScope.Event);

    /// <summary>
    /// Closes one exact usage window. Event-scoped consumers call this when
    /// their owning resolution finishes so nested or unrelated events remain
    /// intact.
    /// </summary>
    public bool ClearUsage(
        int ownerSeat,
        string skillId,
        string usageId,
        SkillUsageScope scope)
    {
        var key = CreateUsageKey(ownerSeat, skillId, usageId, scope);
        return _usage.Remove(key);
    }

    public void RegisterConversionSkill(
        int ownerSeat,
        string skillId,
        SkillPolarity initialState = SkillPolarity.Yang)
    {
        var key = CreateStateKey(ownerSeat, skillId);
        if (_initialPolarities.TryGetValue(key, out var existing) && existing != initialState)
            throw new InvalidOperationException(
                $"Conversion skill '{skillId}' for seat {ownerSeat} was already registered with {existing} as its initial state.");
        _initialPolarities[key] = initialState;
        _polarities.TryAdd(key, initialState);
    }

    public SkillPolarity GetConversionState(int ownerSeat, string skillId)
    {
        var key = CreateStateKey(ownerSeat, skillId);
        return _polarities.TryGetValue(key, out var state) ? state : SkillPolarity.Yang;
    }

    public SkillPolarity ToggleConversionState(int ownerSeat, string skillId)
    {
        var key = CreateStateKey(ownerSeat, skillId);
        if (!_initialPolarities.ContainsKey(key)) RegisterConversionSkill(ownerSeat, skillId);
        var next = GetConversionState(ownerSeat, skillId) == SkillPolarity.Yang
            ? SkillPolarity.Yin
            : SkillPolarity.Yang;
        _polarities[key] = next;
        return next;
    }

    public SkillRuntimeStateSnapshot CreateSnapshot(
        int ownerSeat,
        string skillId,
        bool isAcquired)
    {
        ValidateOwnerAndSkill(ownerSeat, skillId);
        var usages = _usage
            .Where(entry => entry.Key.OwnerSeat == ownerSeat &&
                            string.Equals(entry.Key.SkillId, skillId, StringComparison.Ordinal))
            .OrderBy(entry => entry.Key.Scope)
            .ThenBy(entry => entry.Key.UsageId, StringComparer.Ordinal)
            .Select(entry => new SkillUsageStateSnapshot(
                entry.Key.UsageId,
                entry.Key.Scope,
                entry.Value))
            .ToArray();
        var stateKey = new SkillStateKey(ownerSeat, skillId);
        var polarity = _polarities.TryGetValue(stateKey, out var current)
            ? current
            : (SkillPolarity?)null;
        return new SkillRuntimeStateSnapshot(
            skillId,
            isAcquired,
            Array.AsReadOnly(usages),
            polarity);
    }

    /// <summary>
    /// Restores one skill to its game-start state: all of its usage records are
    /// removed and a registered conversion skill returns to its initial side.
    /// </summary>
    public void ResetSkill(int ownerSeat, string skillId)
    {
        ValidateOwnerAndSkill(ownerSeat, skillId);
        foreach (var key in _usage.Keys
                     .Where(key => key.OwnerSeat == ownerSeat &&
                                   string.Equals(key.SkillId, skillId, StringComparison.Ordinal))
                     .ToArray())
            _usage.Remove(key);

        var stateKey = new SkillStateKey(ownerSeat, skillId);
        if (_initialPolarities.TryGetValue(stateKey, out var initialState))
            _polarities[stateKey] = initialState;
        else
            _polarities.Remove(stateKey);
    }

    private void RemoveUsage(Func<SkillUsageScope, bool> scopePredicate)
    {
        foreach (var key in _usage.Keys
                     .Where(key => scopePredicate(key.Scope))
                     .ToArray())
            _usage.Remove(key);
    }

    private static SkillUsageKey CreateUsageKey(
        int ownerSeat,
        string skillId,
        string usageId,
        SkillUsageScope scope)
    {
        ValidateOwnerAndSkill(ownerSeat, skillId);
        if (string.IsNullOrWhiteSpace(usageId))
            throw new ArgumentException("A skill usage id must be non-empty.", nameof(usageId));
        if (!Enum.IsDefined(scope)) throw new ArgumentOutOfRangeException(nameof(scope));
        return new SkillUsageKey(ownerSeat, skillId, usageId, scope);
    }

    private static SkillStateKey CreateStateKey(int ownerSeat, string skillId)
    {
        ValidateOwnerAndSkill(ownerSeat, skillId);
        return new SkillStateKey(ownerSeat, skillId);
    }

    private static void ValidateOwnerAndSkill(int ownerSeat, string skillId)
    {
        if (ownerSeat < 0) throw new ArgumentOutOfRangeException(nameof(ownerSeat));
        if (string.IsNullOrWhiteSpace(skillId))
            throw new ArgumentException("A runtime skill id must be non-empty.", nameof(skillId));
    }

    private readonly record struct SkillUsageKey(
        int OwnerSeat,
        string SkillId,
        string UsageId,
        SkillUsageScope Scope);

    private readonly record struct SkillStateKey(int OwnerSeat, string SkillId);
}
