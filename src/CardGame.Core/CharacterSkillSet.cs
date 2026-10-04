namespace CardGame.Core;

public sealed record LordSkillProjectionSource(int LordSeat, string LordGrantId, string LordSkillInstanceId,
    string CapabilityGrantId, string CapabilitySkillInstanceId);
public sealed record SkillGrant(
    string GrantId, string SkillId, string SkillInstanceId, string SourceId, bool IsEnabled = true,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    LordSkillProjectionSource? LordProjection = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    GeneralLibraryProjectionSource? GeneralLibraryProjection = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    PrintedLordSkillQualification? PrintedLordQualification = null);

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

    public bool HasEnabledSkill(IReadOnlySet<string> skillIds) =>
        _grants.Values.Any(grant => grant.IsEnabled && skillIds.Contains(grant.SkillId));

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

    internal bool SetPrintedLordQualification(string grantId, PrintedLordSkillQualification? qualification)
    {
        if (!_grants.TryGetValue(grantId, out var grant)) throw new KeyNotFoundException($"Unknown skill grant '{grantId}'.");
        if (qualification is not null && (qualification.GrantId != grant.GrantId || qualification.SkillId != grant.SkillId ||
            qualification.SkillInstanceId != grant.SkillInstanceId || qualification.TemplateSourceId != grant.SourceId ||
            grant.SourceId is not (CharacterState.PrimarySkillSource or CharacterState.SecondarySkillSource) ||
            grant.LordProjection is not null || grant.GeneralLibraryProjection is not null))
            throw new InvalidOperationException("Printed-lord qualification requires its exact original template grant.");
        if (grant.PrintedLordQualification == qualification) return false;
        _grants[grantId] = grant with { PrintedLordQualification = qualification };
        Revision++; return true;
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
