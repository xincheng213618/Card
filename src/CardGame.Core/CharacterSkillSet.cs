namespace CardGame.Core;

public sealed record SkillGrant(
    string GrantId, string SkillId, string SkillInstanceId, string SourceId, bool IsEnabled = true);

/// <summary>
/// Source-aware skill ownership for one character. Definitions and execution
/// are deliberately absent; callers provide stable identities and lifetimes.
/// </summary>
public sealed class CharacterSkillSet
{
    private readonly Dictionary<string, SkillGrant> _grants = new(StringComparer.Ordinal);
    public long Revision { get; private set; }
    public IReadOnlyList<SkillGrant> Grants => Array.AsReadOnly(_grants.Values
        .OrderBy(grant => grant.GrantId, StringComparer.Ordinal).ToArray());
    public IReadOnlyList<string> EffectiveSkillIds => Array.AsReadOnly(_grants.Values
        .Where(grant => grant.IsEnabled).Select(grant => grant.SkillId)
        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());

    public bool Grant(SkillGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentException.ThrowIfNullOrWhiteSpace(grant.GrantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(grant.SkillInstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(grant.SourceId);
        _ = new ContentId(grant.SkillId);
        if (_grants.TryGetValue(grant.GrantId, out var existing))
        {
            if (existing == grant) return false;
            throw new InvalidOperationException($"Skill grant '{grant.GrantId}' conflicts with an existing grant.");
        }
        _grants.Add(grant.GrantId, grant);
        Revision++;
        return true;
    }

    public bool RemoveGrant(string grantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grantId);
        if (!_grants.Remove(grantId)) return false;
        Revision++;
        return true;
    }

    public bool SetEnabled(string grantId, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grantId);
        if (!_grants.TryGetValue(grantId, out var grant))
            throw new KeyNotFoundException($"Unknown skill grant '{grantId}'.");
        if (grant.IsEnabled == enabled) return false;
        _grants[grantId] = grant with { IsEnabled = enabled };
        Revision++;
        return true;
    }
}
