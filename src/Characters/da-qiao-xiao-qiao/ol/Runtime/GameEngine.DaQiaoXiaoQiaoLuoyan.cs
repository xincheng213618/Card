namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasDaQiaoXiaoQiaoCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveXingwuDepositAndBurst)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.MaintainLuoyanPileSkills);
    private bool DaQiaoXiaoQiaoHasPile(int seat)=>PublicPileSources(seat).Any(p=>p.SkillId=="ol:xingwu"&&PublicPileCards(p).Count>0);
    private bool DaQiaoXiaoQiaoLuoyanSourceLive(CharacterState owner,SkillGrant source)=>owner.IsAlive&&source.IsEnabled&&source.SkillId=="ol:luoyan"&&source.DaQiaoXiaoQiaoLuoyanSource is null&&
        HpConditionalSourceQualified(owner,source)&&IsHpConditionalGrantQualified(owner,source)&&IsEquipmentSuitGrantQualified(owner,source);
    private bool IsDaQiaoXiaoQiaoLuoyanGrantQualified(CharacterState owner,SkillGrant grant)
    {
        if(grant.DaQiaoXiaoQiaoLuoyanSource is not{}relation)return true;
        return HasDaQiaoXiaoQiaoCapability&&relation.LeafSkillId==grant.SkillId&&grant.SkillId is("boundary:tianxiang"or"classic:liuli")&&DaQiaoXiaoQiaoHasPile(owner.Seat)&&
            owner.SkillGrants.Grants.Any(s=>s.GrantId==relation.CapabilityGrantId&&s.SkillInstanceId==relation.CapabilitySkillInstanceId&&DaQiaoXiaoQiaoLuoyanSourceLive(owner,s));
    }
    private readonly Dictionary<int,((long Base,long Grant,long Movement,int Hp,bool Alive,bool Concealed) Key,long Stamp)> _daQiaoXiaoQiaoQualificationCache=[];
    private long _daQiaoXiaoQiaoQualificationRevision;
    private long CaptureDaQiaoXiaoQiaoLuoyanQualificationStamp(CharacterState owner,long existingStamp)
    {
        if(!HasDaQiaoXiaoQiaoCapability||!owner.SkillGrants.Grants.Any(g=>g.SkillId=="ol:luoyan"||g.DaQiaoXiaoQiaoLuoyanSource is not null))return existingStamp;
        var key=(existingStamp,owner.SkillGrants.Revision,_movementSequence,owner.Hp,owner.IsAlive,owner.IdentityConcealed);
        if(!_daQiaoXiaoQiaoQualificationCache.TryGetValue(owner.Seat,out var cached)||cached.Key!=key)
        {var stamp=long.MinValue/4+ ++_daQiaoXiaoQiaoQualificationRevision;if(stamp==existingStamp)stamp=long.MinValue/4+ ++_daQiaoXiaoQiaoQualificationRevision;cached=(key,stamp);_daQiaoXiaoQiaoQualificationCache[owner.Seat]=cached;}
        return cached.Stamp;
    }
    private static string DaQiaoXiaoQiaoDerivedId(int seat,DaQiaoXiaoQiaoLuoyanSource r) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_da_qiao_xiao_qiao_ol_DaQiaoXiaoQiaoLuoyan.DaQiaoXiaoQiaoDerivedId(seat,r);
    private bool _synchronizingDaQiaoXiaoQiaoLuoyan;
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,27340)]
    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish,27340)]
    private void SynchronizeDaQiaoXiaoQiaoLuoyan()
    {
        if(!HasDaQiaoXiaoQiaoCapability||_synchronizingDaQiaoXiaoQiaoLuoyan)return;
        _synchronizingDaQiaoXiaoQiaoLuoyan=true;
        try
        {
        foreach(var owner in _players)
        {
            var desired=new Dictionary<string,SkillGrant>(StringComparer.Ordinal);
            if(DaQiaoXiaoQiaoHasPile(owner.Seat))foreach(var source in owner.SkillGrants.Grants.Where(s=>DaQiaoXiaoQiaoLuoyanSourceLive(owner,s)).ToArray())foreach(var skill in new[]{"boundary:tianxiang","classic:liuli"})
            {
                _=_contentRegistry.GetSkill(skill);var relation=new DaQiaoXiaoQiaoLuoyanSource(source.GrantId,source.SkillInstanceId,skill);var id=DaQiaoXiaoQiaoDerivedId(owner.Seat,relation);
                var previous=ProgramEventHistory<DaQiaoXiaoQiaoLuoyanChangedEvent>().LastOrDefault(e=>e.OwnerSeat==owner.Seat&&e.GrantId==id&&e.Added);
                var independent=owner.SkillGrants.Grants.FirstOrDefault(g=>g.SkillId==skill&&g.DaQiaoXiaoQiaoLuoyanSource is null&&g.IsEnabled&&HpConditionalSourceQualified(owner,g)&&IsHpConditionalGrantQualified(owner,g));
                desired[id]=new(id,skill,previous?.InstanceId??independent?.SkillInstanceId??id,"luoyan:"+source.SourceId){DaQiaoXiaoQiaoLuoyanSource=relation};
            }
            foreach(var stale in owner.SkillGrants.Grants.Where(g=>g.DaQiaoXiaoQiaoLuoyanSource is not null&&!desired.ContainsKey(g.GrantId)).ToArray())
            {foreach(var p in _players)_observedSkillGrantRevisions.TryAdd(p.Seat,p.SkillGrants.Revision);if(owner.SkillGrants.RemoveGrant(stale.GrantId))AdvanceEventRulesAndQueueFact(new DaQiaoXiaoQiaoLuoyanChangedEvent(owner.Seat,stale.GrantId,stale.SkillId,stale.SkillInstanceId,stale.DaQiaoXiaoQiaoLuoyanSource!,false));}
            foreach(var grant in desired.Values)
            {foreach(var p in _players)_observedSkillGrantRevisions.TryAdd(p.Seat,p.SkillGrants.Revision);if(owner.SkillGrants.Grant(grant))AdvanceEventRulesAndQueueFact(new DaQiaoXiaoQiaoLuoyanChangedEvent(owner.Seat,grant.GrantId,grant.SkillId,grant.SkillInstanceId,grant.DaQiaoXiaoQiaoLuoyanSource!,true));}
        }
        }
        finally{_synchronizingDaQiaoXiaoQiaoLuoyan=false;}
    }
}
