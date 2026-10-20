namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void GrantCaoTengSkill(ProgramSkillFrame f,int recipient)
    {
        var faction=GetEffectiveFactionId(_players[recipient]);if(faction is not("wei" or "qun"))return;
        var skill=faction=="wei"?"ol:caoteng-jianxiong":"ol:tianming";_= _contentRegistry.GetSkill(skill);var r=f.CaoTeng!;
        var source=$"caoteng-yongzu:{f.Id}:{r.Cursor}:{recipient}";var grantId=source+":"+skill;
        var existing=GetSkillBindingShard(_players[recipient]).ActiveGrants.Where(g=>g.SkillId==skill).OrderBy(g=>g.SourceId.StartsWith("caoteng-yongzu:",StringComparison.Ordinal)?1:0).ThenBy(g=>g.SkillInstanceId,StringComparer.Ordinal).FirstOrDefault();
        var instance=existing?.SkillInstanceId??grantId;_players[recipient].SkillGrants.Grant(new(grantId,skill,instance,source));
        AdvanceEventRulesAndQueueFact(new CaoTengSkillGrantedEvent(f.Id,r.Cursor,recipient,skill,instance,grantId,source,_turnNumber));
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,27300)]private void ExpireCaoTengNextOwnTurn(TurnStartedEvent started)
    {
        if(!HasCaoTengCapability)return;var expired=ProgramEventHistory<CaoTengSkillExpiredEvent>().Select(e=>e.GrantId).ToHashSet(StringComparer.Ordinal);
        foreach(var grant in ProgramEventHistory<CaoTengSkillGrantedEvent>().Where(e=>e.RecipientSeat==started.ActorSeat&&e.ActualTurn<started.TurnNumber&&!expired.Contains(e.GrantId)).ToArray())
        {
            var actual=_players[grant.RecipientSeat].SkillGrants.Grants.SingleOrDefault(g=>g.GrantId==grant.GrantId);
            if(actual is not null){if(actual.SkillId!=grant.SkillId||actual.SkillInstanceId!=grant.SkillInstanceId||actual.SourceId!=grant.SourceId)throw new InvalidOperationException("Yongzu's exact grant source was replaced.");_players[grant.RecipientSeat].SkillGrants.RemoveGrant(grant.GrantId);}
            AdvanceEventRulesAndQueueFact(new CaoTengSkillExpiredEvent(grant.GrantId,grant.RecipientSeat,started.TurnNumber));
        }
    }
    private int CaoTengYongzuHandLimitBonus(int seat)=>HasCaoTengCapability?ProgramEventHistory<CaoTengHandLimitGrantedEvent>().Count(e=>e.RecipientSeat==seat):0;
}
