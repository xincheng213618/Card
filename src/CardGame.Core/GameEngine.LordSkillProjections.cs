namespace CardGame.Core;

public sealed record LordSkillProjectionChangedEvent(int OwnerSeat, string GrantId, string SkillId,
    LordSkillProjectionSource Source, bool Added) : IGameEvent;

public sealed partial class GameEngine
{
    private readonly bool _hasLordProjectionCapability;
    private bool HasLordProjectionCapability => _hasLordProjectionCapability;
    private bool _synchronizingLordProjections;

    // Pure qualification. Local disable remains on the derived grant; upstream disable is never copied into it.
    private IEnumerable<SkillGrant> EffectiveUnprojectedGrants(CharacterState owner)
    {
        var active = owner.SkillGrants.Grants.Where(g => g.LordProjection is null && g.IsEnabled &&
            (g.SourceId != CharacterState.PrimarySkillSource || !IsNationalWarMode || owner.GeneralSelected && owner.GeneralRevealed) &&
            (g.SourceId != CharacterState.SecondarySkillSource || IsNationalWarMode && owner.SecondaryGeneralSelected && owner.SecondaryGeneralRevealed) &&
            (g.SourceId is not (CharacterState.PrimarySkillSource or CharacterState.SecondarySkillSource) ||
             !_contentRegistry.GetSkill(g.SkillId).Tags.HasFlag(SkillTag.Lord) || owner.Role == Role.Lord)).ToArray();
        var suppressors = active.Where(g => _contentRegistry.GetSkill(g.SkillId).SuppressionRule is {} r && owner.Hp == r.OwnerHpEquals)
            .Select(g => g.SkillId).ToHashSet(StringComparer.Ordinal);
        return suppressors.Count == 0 ? active : active.Where(g => suppressors.Contains(g.SkillId) || g.SourceId.StartsWith("equipment:", StringComparison.Ordinal));
    }

    private bool IsProjectedGrantQualified(CharacterState owner, SkillGrant grant)
    {
        if (grant.LordProjection is not {} source) return true;
        if (!owner.IsAlive || IsTeamMode || IsNationalWarMode || !IsValidPlayerSeat(source.LordSeat) || source.LordSeat == owner.Seat) return false;
        var lord = _players[source.LordSeat];
        return lord.IsAlive && lord.Role == Role.Lord &&
            EffectiveUnprojectedGrants(owner).Any(g => g.GrantId == source.CapabilityGrantId && g.SkillInstanceId == source.CapabilitySkillInstanceId && _contentRegistry.GetSkill(g.SkillId).Program?.LordSkillProjection == true) &&
            EffectiveUnprojectedGrants(lord).Any(g => g.GrantId == source.LordGrantId && g.SkillInstanceId == source.LordSkillInstanceId && g.SkillId == grant.SkillId && _contentRegistry.GetSkill(g.SkillId).Tags.HasFlag(SkillTag.Lord));
    }

    private readonly record struct ProjectionDependency(long Revision, bool Alive, int Hp, Role Role,
        bool PrimarySelected, bool PrimaryRevealed, bool SecondarySelected, bool SecondaryRevealed);
    private ProjectionDependency[] _lordProjectionDependencies = [];
    private long _lordProjectionDependencyRevision;
    // Cache invalidation only: no rules, grants or events are changed by a view/AI read.
    private long CaptureLordProjectionDependencyStamp()
    {
        if (!HasLordProjectionCapability) return 0;
        var changed = _lordProjectionDependencies.Length != _players.Count;
        if (changed) _lordProjectionDependencies = new ProjectionDependency[_players.Count];
        for (var i = 0; i < _players.Count; i++)
        {
            var p = _players[i];
            var current = new ProjectionDependency(p.SkillGrants.Revision, p.IsAlive, p.Hp, p.Role,
                p.GeneralSelected, p.GeneralRevealed, p.SecondaryGeneralSelected, p.SecondaryGeneralRevealed);
            if (_lordProjectionDependencies[i] != current) { changed = true; _lordProjectionDependencies[i] = current; }
        }
        if (changed) _lordProjectionDependencyRevision++;
        return _lordProjectionDependencyRevision;
    }

    private bool HasSkillRoleQualification(CharacterState owner, string skillId, string? instance, Role role)
    {
        if (owner.Role == role) return true;
        if (role != Role.Lord || !_contentRegistry.GetSkill(skillId).Tags.HasFlag(SkillTag.Lord)) return false;
        // The menu uses the same stable instance that execution resolves. Do not let another grant authorize it.
        instance ??= GetSkillBindingShard(owner).ActiveGrants.Where(g => g.SkillId == skillId)
            .OrderBy(g => g.SkillInstanceId, StringComparer.Ordinal).Select(g => g.SkillInstanceId).FirstOrDefault();
        return owner.SkillGrants.Grants.Any(g => g.SkillId == skillId && g.SkillInstanceId == instance && g.IsEnabled && g.LordProjection is not null && IsProjectedGrantQualified(owner, g));
    }

    // Preserve the established local ownership semantics for ordinary grants. A retained,
    // upstream-disabled projection is not currently owned for skill-count/replacement rules.
    private IReadOnlyList<string> AdvancedOwnedSkillIds(CharacterState owner) => owner.SkillGrants.Grants
        .Where(g => g.IsEnabled && (g.LordProjection is null || IsProjectedGrantQualified(owner, g)))
        .Select(g => g.SkillId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private void SynchronizeLordSkillProjections()
    {
        if (!HasLordProjectionCapability || _synchronizingLordProjections) return;
        _synchronizingLordProjections = true;
        try
        {
            var lord = IsTeamMode || IsNationalWarMode ? null : _players.SingleOrDefault(p => p.IsAlive && p.Role == Role.Lord);
            foreach (var owner in _players)
            {
                var desired = new Dictionary<string, SkillGrant>(StringComparer.Ordinal);
                if (lord is not null && owner.IsAlive && owner.Seat != lord.Seat)
                    foreach (var capability in owner.SkillGrants.Grants.Where(g => g.LordProjection is null && _contentRegistry.GetSkill(g.SkillId).Program?.LordSkillProjection == true))
                    foreach (var source in lord.SkillGrants.Grants.Where(g => g.LordProjection is null && _contentRegistry.GetSkill(g.SkillId).Tags.HasFlag(SkillTag.Lord)))
                    {
                        var relation = new LordSkillProjectionSource(lord.Seat, source.GrantId, source.SkillInstanceId, capability.GrantId, capability.SkillInstanceId);
                        var id = $"lord-projection:{owner.Seat}:{capability.GrantId.Length}:{capability.GrantId}:{capability.SkillInstanceId.Length}:{capability.SkillInstanceId}:{lord.Seat}:{source.GrantId.Length}:{source.GrantId}:{source.SkillInstanceId.Length}:{source.SkillInstanceId}:{source.SkillId}";
                        desired.Add(id, new SkillGrant(id, source.SkillId, id, $"lord-projection:{capability.SourceId}", LordProjection: relation));
                    }
                foreach (var stale in owner.SkillGrants.Grants.Where(g => g.LordProjection is not null && !desired.ContainsKey(g.GrantId)))
                {
                    owner.SkillGrants.RemoveGrant(stale.GrantId);
                    AdvanceEventRulesAndQueueFact(new LordSkillProjectionChangedEvent(owner.Seat, stale.GrantId, stale.SkillId, stale.LordProjection!, false));
                }
                foreach (var grant in desired.Values.Where(g => !owner.SkillGrants.Grants.Any(old => old.GrantId == g.GrantId)))
                {
                    owner.SkillGrants.Grant(grant);
                    AdvanceEventRulesAndQueueFact(new LordSkillProjectionChangedEvent(owner.Seat, grant.GrantId, grant.SkillId, grant.LordProjection!, true));
                }
            }
        }
        finally { _synchronizingLordProjections = false; }
    }
}
