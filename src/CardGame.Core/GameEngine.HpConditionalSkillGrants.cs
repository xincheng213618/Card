namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlySet<string> HpConditionalCapabilitySkillIds => _contentRegistry.ProgramDependencies.HpConditionalCapabilitySkillIds;
    private bool _synchronizingHpConditionalSkillGrants;
    // A source relation remembers its first effective instance through HP exits.
    // This preserves scoped runtime state even when an independent shared grant
    // disappears while the conditional grant is absent. Replay reconstructs it.
    private readonly Dictionary<(int OwnerSeat, HpConditionalSkillGrantSource Source), string> _hpConditionalSkillInstances = [];

    private ProgramSkillFrame CaptureHpConditionalProgramSource(ProgramSkillFrame frame)
    {
        var grant = GetSkillBindingShard(_players[frame.OwnerSeat]).ActiveGrants.FirstOrDefault(grant =>
            grant.SkillId == frame.SkillId && grant.SkillInstanceId == frame.SkillInstanceId && grant.HpConditionalSource is not null);
        return grant is null ? frame : frame with
        {
            HpConditionalSourceReceipt = new(frame.Id, frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId,
                frame.GameplayHash, grant.GrantId, grant.HpConditionalSource!)
        };
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 500)]
    private bool CanContinueHpConditionalProgram(ProgramSkillFrame frame) =>
        frame.HpConditionalSourceReceipt is { } receipt && receipt.FrameId == frame.Id &&
        receipt.OwnerSeat == frame.OwnerSeat && receipt.SkillId == frame.SkillId &&
        receipt.SkillInstanceId == frame.SkillInstanceId && receipt.GameplayHash == frame.GameplayHash &&
        receipt.GrantId == HpConditionalGrantId(frame.OwnerSeat, receipt.Source) &&
        _contentRegistry.GetSkill(receipt.Source.CapabilitySkillId).Program?.HpSkillGrants.Any(rule =>
            rule.Id == receipt.Source.RuleId && rule.SkillId == frame.SkillId) == true;

    private static string HpConditionalGrantId(int ownerSeat, HpConditionalSkillGrantSource source) =>
        $"hp-conditional:{ownerSeat}:{source.CapabilityGrantId.Length}:{source.CapabilityGrantId}:{source.CapabilitySkillInstanceId.Length}:{source.CapabilitySkillInstanceId}:{source.RuleId}";

    // Pure qualification also rejects a stale derived grant between a source/HP
    // mutation and the next explicit rule-progression synchronization.
    private bool IsHpConditionalGrantQualified(CharacterState owner, SkillGrant grant)
    {
        if (grant.HpConditionalSource is not { } relation) return true;
        var source = owner.SkillGrants.Grants.FirstOrDefault(candidate =>
            candidate.GrantId == relation.CapabilityGrantId &&
            candidate.SkillId == relation.CapabilitySkillId &&
            candidate.SkillInstanceId == relation.CapabilitySkillInstanceId &&
            candidate.HpConditionalSource is null);
        if (source is null || !HpConditionalSourceQualified(owner, source)) return false;
        return _contentRegistry.GetSkill(source.SkillId).Program?.HpSkillGrants.Any(rule =>
            rule.Id == relation.RuleId && rule.SkillId == grant.SkillId && rule.Matches(owner.Hp) &&
            MatchesTangZiWoundedGrant(owner, rule)) == true;
    }

    private bool HpConditionalSourceQualified(CharacterState owner, SkillGrant source)
    {
        if (!LibraryBaseGrantLive(owner, source) || !IsCurrentTurnSkillGrantQualified(owner, source) ||
            !IsProjectedGrantQualified(owner, source) || !IsGeneralLibraryGrantQualified(owner, source)) return false;
        var suppressors = HasPrivateGeneralLibraryCapability ? PrivateGeneralLibrarySuppressionInputs(owner) :
            owner.SkillGrants.Grants.Where(grant => grant.HpConditionalSource is null &&
                _contentRegistry.GetSkill(grant.SkillId).SuppressionRule is { } rule && owner.Hp == rule.OwnerHpEquals &&
                LibraryBaseGrantLive(owner, grant) && IsCurrentTurnSkillGrantQualified(owner, grant) && IsProjectedGrantQualified(owner, grant))
                .Select(grant => grant.SkillId).ToHashSet(StringComparer.Ordinal);
        return suppressors.Count == 0 || suppressors.Contains(source.SkillId) ||
            source.SourceId.StartsWith("equipment:", StringComparison.Ordinal);
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 50)]
    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 30)]
    private void SynchronizeHpConditionalSkillGrants()
    {
        if (HpConditionalCapabilitySkillIds.Count == 0 || _synchronizingHpConditionalSkillGrants) return;
        // The catalogue may contain Baobian while this match never owns it.
        // Use raw grant membership (including later grants), not a match-start cache.
        if (_hpConditionalSkillInstances.Count == 0 &&
            !_players.Any(owner => owner.SkillGrants.HasEnabledSkill(HpConditionalCapabilitySkillIds))) return;
        _synchronizingHpConditionalSkillGrants = true;
        try
        {
            foreach (var owner in _players)
            {
                foreach (var key in _hpConditionalSkillInstances.Keys.Where(key => key.OwnerSeat == owner.Seat &&
                             !owner.SkillGrants.Grants.Any(grant => grant.GrantId == key.Source.CapabilityGrantId &&
                                 grant.SkillId == key.Source.CapabilitySkillId &&
                                 grant.SkillInstanceId == key.Source.CapabilitySkillInstanceId)).ToArray())
                    _hpConditionalSkillInstances.Remove(key);
                var desired = new Dictionary<string, SkillGrant>(StringComparer.Ordinal);
                foreach (var source in owner.SkillGrants.Grants.Where(grant => grant.HpConditionalSource is null &&
                             _contentRegistry.GetSkill(grant.SkillId).Program?.HpSkillGrants.Count > 0 &&
                             HpConditionalSourceQualified(owner, grant)))
                foreach (var rule in _contentRegistry.GetSkill(source.SkillId).Program!.HpSkillGrants.Where(rule =>
                             rule.Matches(owner.Hp) && MatchesTangZiWoundedGrant(owner, rule)))
                {
                    var relation = new HpConditionalSkillGrantSource(source.GrantId, source.SkillId, source.SkillInstanceId, rule.Id);
                    var id = HpConditionalGrantId(owner.Seat, relation);
                    // Separate grants preserve independent ownership; an already owned
                    // effective skill shares its instance, just like ordinary grants.
                    if (!_hpConditionalSkillInstances.TryGetValue((owner.Seat, relation), out var instance))
                    {
                        var existing = GetSkillBindingShard(owner).ActiveGrants.Where(grant => grant.SkillId == rule.SkillId)
                            .OrderBy(grant => grant.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault() ??
                            desired.Values.FirstOrDefault(grant => grant.SkillId == rule.SkillId);
                        instance = existing?.SkillInstanceId ?? id;
                        _hpConditionalSkillInstances.Add((owner.Seat, relation), instance);
                    }
                    desired.Add(id, new SkillGrant(id, rule.SkillId, instance,
                        "hp-conditional:" + source.SourceId, HpConditionalSource: relation));
                }
                foreach (var stale in owner.SkillGrants.Grants.Where(grant => grant.HpConditionalSource is not null &&
                             !desired.ContainsKey(grant.GrantId)))
                {
                    owner.SkillGrants.RemoveGrant(stale.GrantId);
                    AdvanceEventRulesAndQueueFact(new HpConditionalSkillGrantChangedEvent(owner.Seat, stale.GrantId,
                        stale.SkillId, stale.SkillInstanceId, stale.HpConditionalSource!, false, owner.Hp));
                }
                foreach (var grant in desired.Values.Where(grant => !owner.SkillGrants.Grants.Any(old => old.GrantId == grant.GrantId)))
                {
                    owner.SkillGrants.Grant(grant);
                    AdvanceEventRulesAndQueueFact(new HpConditionalSkillGrantChangedEvent(owner.Seat, grant.GrantId,
                        grant.SkillId, grant.SkillInstanceId, grant.HpConditionalSource!, true, owner.Hp));
                }
            }
        }
        finally { _synchronizingHpConditionalSkillGrants = false; }
    }
}
