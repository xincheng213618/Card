namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlySet<string> EquipmentSuitCapabilitySkillIds => _contentRegistry.ProgramDependencies.EquipmentSuitCapabilitySkillIds;
    private bool _synchronizingEquipmentSuitSkillGrants;
    // A source relation remembers its first effective instance through equipment suit exits.
    // This preserves scoped runtime state even when an independent shared grant
    // disappears while the conditional grant is absent. Replay reconstructs it.
    private readonly Dictionary<(int OwnerSeat, EquipmentSuitSkillGrantSource Source), string> _equipmentSuitSkillInstances = [];

    private ProgramSkillFrame CaptureEquipmentSuitProgramSource(ProgramSkillFrame frame)
    {
        var grant = GetSkillBindingShard(_players[frame.OwnerSeat]).ActiveGrants.FirstOrDefault(grant =>
            grant.SkillId == frame.SkillId && grant.SkillInstanceId == frame.SkillInstanceId && grant.EquipmentSuitSource is not null);
        return grant is null ? frame : frame with
        {
            EquipmentSuitSourceReceipt = new(frame.Id, frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId,
                frame.GameplayHash, grant.GrantId, grant.EquipmentSuitSource!)
        };
    }

    private bool ValidateEquipmentSuitProgramSource(ProgramSkillFrame frame) =>
        frame.EquipmentSuitSourceReceipt is { } receipt && receipt.FrameId == frame.Id &&
        receipt.OwnerSeat == frame.OwnerSeat && receipt.SkillId == frame.SkillId &&
        receipt.SkillInstanceId == frame.SkillInstanceId && receipt.GameplayHash == frame.GameplayHash &&
        receipt.GrantId == EquipmentSuitGrantId(frame.OwnerSeat, receipt.Source) &&
        _contentRegistry.GetSkill(receipt.Source.CapabilitySkillId).Program?.EquipmentSuitSkillGrants.Any(rule =>
            rule.Id == receipt.Source.RuleId && rule.SkillId == frame.SkillId) == true;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 510)]
    private bool CanContinueEquipmentSuitProgram(ProgramSkillFrame frame)=>ValidateEquipmentSuitProgramSource(frame) &&
        frame.FrozenHandRoster is { Stage: "movement" } or { Stage: "damage-issued", DamageIssued: true } && ValidFrozenHandRoster(frame);

    private static string EquipmentSuitGrantId(int ownerSeat, EquipmentSuitSkillGrantSource source) =>
        $"equipment-suit:{ownerSeat}:{source.CapabilityGrantId.Length}:{source.CapabilityGrantId}:{source.CapabilitySkillInstanceId.Length}:{source.CapabilitySkillInstanceId}:{source.RuleId}";

    // Pure qualification also rejects a stale derived grant between a source/equipment
    // mutation and the next explicit rule-progression synchronization.
    private bool IsEquipmentSuitGrantQualified(CharacterState owner, SkillGrant grant)
    {
        if (grant.EquipmentSuitSource is not { } relation) return true;
        var source = owner.SkillGrants.Grants.FirstOrDefault(candidate =>
            candidate.GrantId == relation.CapabilityGrantId &&
            candidate.SkillId == relation.CapabilitySkillId &&
            candidate.SkillInstanceId == relation.CapabilitySkillInstanceId &&
            candidate.EquipmentSuitSource is null);
        if (source is null || !EquipmentSuitSourceQualified(owner, source)) return false;
        return _contentRegistry.GetSkill(source.SkillId).Program?.EquipmentSuitSkillGrants.Any(rule =>
            rule.Id == relation.RuleId && rule.SkillId == grant.SkillId && EquipmentSuitCount(owner)>=rule.MinimumDistinctSuits) == true;
    }

    private int EquipmentSuitCount(CharacterState owner)
    {
        if(owner.IdentityConcealed)return 0;
        // Build only source-independent rewrite bindings. Calling the final binding
        // shard here would recurse through this very qualification predicate.
        var policies=owner.SkillGrants.Grants.Where(g=>g.EquipmentSuitSource is null && HpConditionalSourceQualified(owner,g) && IsHpConditionalGrantQualified(owner,g))
            .GroupBy(g=>(g.SkillId,g.SkillInstanceId)).OrderBy(g=>g.Key.SkillId,StringComparer.Ordinal).ThenBy(g=>g.Key.SkillInstanceId,StringComparer.Ordinal)
            .SelectMany(g=>_contentRegistry.GetSkill(g.Key.SkillId).Program?.CardPolicies.Where(p=>p.Kind==SkillProgramCardPolicyKind.RewriteSuit)??[]).ToArray();
        foreach(var policy in policies)
            if(policy.Condition.Kind!=SkillProgramConditionKind.Always || policy.OwnerRole is not null)throw new InvalidOperationException("Equipment suit qualification does not support context-dependent suit rewrites.");
        Suit SuitOf(Card card)
        {
            var suit=card.Suit;
            foreach(var policy in policies)
                if(policy.CardKinds.Count==0 && policy.RequiredCardKinds.Count==0 && policy.InputSuit==suit)suit=policy.OutputSuit!.Value;
            return suit;
        }
        return GetEquipment(owner).Select(SuitOf).Where(s=>s!=Suit.None).Distinct().Count();
    }
    private bool EquipmentSuitSourceQualified(CharacterState owner,SkillGrant source) => source.EquipmentSuitSource is null && HpConditionalSourceQualified(owner,source) && IsHpConditionalGrantQualified(owner,source);

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 60)]
    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 40)]
    private void SynchronizeEquipmentSuitSkillGrants()
    {
        if (EquipmentSuitCapabilitySkillIds.Count == 0 || _synchronizingEquipmentSuitSkillGrants) return;
        // The catalogue may contain Qizhou while this match never owns it.
        // Use raw grant membership (including later grants), not a match-start cache.
        if (_equipmentSuitSkillInstances.Count == 0 &&
            !_players.Any(owner => owner.SkillGrants.HasEnabledSkill(EquipmentSuitCapabilitySkillIds))) return;
        _synchronizingEquipmentSuitSkillGrants = true;
        try
        {
            foreach (var owner in _players)
            {
                foreach (var key in _equipmentSuitSkillInstances.Keys.Where(key => key.OwnerSeat == owner.Seat &&
                             !owner.SkillGrants.Grants.Any(grant => grant.GrantId == key.Source.CapabilityGrantId &&
                                 grant.SkillId == key.Source.CapabilitySkillId &&
                                 grant.SkillInstanceId == key.Source.CapabilitySkillInstanceId)).ToArray())
                    _equipmentSuitSkillInstances.Remove(key);
                var desired = new Dictionary<string, SkillGrant>(StringComparer.Ordinal);
                foreach (var source in owner.SkillGrants.Grants.Where(grant => grant.EquipmentSuitSource is null &&
                             _contentRegistry.GetSkill(grant.SkillId).Program?.EquipmentSuitSkillGrants.Count > 0 &&
                             EquipmentSuitSourceQualified(owner, grant)))
                foreach (var rule in _contentRegistry.GetSkill(source.SkillId).Program!.EquipmentSuitSkillGrants.Where(rule => EquipmentSuitCount(owner)>=rule.MinimumDistinctSuits))
                {
                    var relation = new EquipmentSuitSkillGrantSource(source.GrantId, source.SkillId, source.SkillInstanceId, rule.Id);
                    var id = EquipmentSuitGrantId(owner.Seat, relation);
                    // Separate grants preserve independent ownership; an already owned
                    // effective skill shares its instance, just like ordinary grants.
                    if (!_equipmentSuitSkillInstances.TryGetValue((owner.Seat, relation), out var instance))
                    {
                        var existing = GetSkillBindingShard(owner).ActiveGrants.Where(grant => grant.SkillId == rule.SkillId)
                            .OrderBy(grant => grant.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault() ??
                            desired.Values.FirstOrDefault(grant => grant.SkillId == rule.SkillId);
                        instance = existing?.SkillInstanceId ?? id;
                        _equipmentSuitSkillInstances.Add((owner.Seat, relation), instance);
                    }
                    desired.Add(id, new SkillGrant(id, rule.SkillId, instance,
                        "equipment-suit:" + source.SourceId, EquipmentSuitSource: relation));
                }
                foreach (var stale in owner.SkillGrants.Grants.Where(grant => grant.EquipmentSuitSource is not null &&
                             !desired.ContainsKey(grant.GrantId)))
                {
                    owner.SkillGrants.RemoveGrant(stale.GrantId);
                    AdvanceEventRulesAndQueueFact(new EquipmentSuitSkillGrantChangedEvent(owner.Seat, stale.GrantId,
                        stale.SkillId, stale.SkillInstanceId, stale.EquipmentSuitSource!, false, EquipmentSuitCount(owner)));
                }
                foreach (var grant in desired.Values.Where(grant => !owner.SkillGrants.Grants.Any(old => old.GrantId == grant.GrantId)))
                {
                    owner.SkillGrants.Grant(grant);
                    AdvanceEventRulesAndQueueFact(new EquipmentSuitSkillGrantChangedEvent(owner.Seat, grant.GrantId,
                        grant.SkillId, grant.SkillInstanceId, grant.EquipmentSuitSource!, true, EquipmentSuitCount(owner)));
                }
            }
        }
        finally { _synchronizingEquipmentSuitSkillGrants = false; }
    }
    private bool HeQiFirstChild(ProgramSkillFrame root,ResolutionFrame child)
    {
        if(!CanContinueHeQi(root)||root.HeQi is not{}r||child is not CardsMovedTriggerWindowFrame moved)return false;
        return moved.Batch.ParentFrameId==root.Id && moved.Batch.AwaitingProgramFrameId==root.Id && moved.ResumeProgramFrameId is null && moved.Batch.OriginOwnerSeat==root.OwnerSeat && moved.Batch.OriginSkillId==root.SkillId && moved.Batch.OriginSkillInstanceId==root.SkillInstanceId && moved.Batch.Movements.Count>0 && moved.Batch.Movements.All(m=>_cardMovements.Contains(m) && m.Sequence>r.Before && m.Sequence<=r.After && (m.Reason.Value==HeQiReason(r.Stage) && r.Paid.Any(p=>p.CardId==m.CardId && p.From==m.From) || r.Stage==HeQiStage.ObtainChildren && r.Paid.Any(p=>p.PrintedKind==CardKind.WoodenOx && p.From==CardLocation.Equipment(r.TargetSeat)) && m.From==CardLocation.WoodenOxGrain(r.TargetSeat) && m.To==CardLocation.DiscardPile && m.Reason==CardMoveReasons.WoodenOxGrainDiscard));
    }
    private ProgramSkillFrame? HeQiObserverRoot()=>FindProgramObserverRoot(static(e,r,c)=>e.HeQiFirstChild(r,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 90)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 200)]
    private bool HasHeQiDamageObserver(long id)=>HeQiObserverRoot()is{}r && _resolutionStack.Any(f=>f.Id==id && f is DamageTriggerWindowFrame) && _resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 50)]
    private bool IsHeQiDying()=>ActiveDying is{}dying && HeQiObserverRoot()is{}r && _resolutionStack.FindIndex(f=>f.Id==dying.Id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 670)]
    private bool AllowsHeQiNestedDamage(ProgramSkillFrame observer,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(sourceLess||observer.AttackAttempt is not null||observer.InstructionIndex<1||_resolutionStack.LastOrDefault()?.Id!=observer.Id||observer.WindowContext?.Window is not(SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged)||HeQiObserverRoot()is not{}root||root.Id==observer.Id)return false;
        var e=GetPausedPrivateOfferEffect(observer);return e is{Op:SkillProgramEffectOp.Damage} && amount==e.Amount && source==e.ActorReference && nature==e.DamageNature && target==(e.TargetReference is{}reference?ResolveProgramParticipant(observer,reference):ResolveProgramEffectTarget(observer,e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 170)]
    private bool TryAdvanceHeQiSubtree()=>_pendingDecision is null && HeQiObserverRoot()is not null && TryAdvanceObserverSubtree(advanceRecoveryReplacement:true);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 300)]
    private bool HasHeQiBeforeDamageObserver(long id)=>HeQiObserverRoot()is{}r && _resolutionStack.Any(f=>f.Id==id && f is BeforeDamageProgramWindowFrame) && _resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
}
