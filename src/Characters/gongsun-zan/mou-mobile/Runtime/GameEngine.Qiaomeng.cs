namespace CardGame.Core;
public sealed partial class GameEngine
{
    private DamageFrame? MouGszMobileDamage(ProgramSkillWindowContext c,int owner)
    {
        if(c.Window!=SkillProgramTriggerWindow.AfterDamageApplied||c.DamageFrameId is not{}id||c.SourceSeat!=owner||c.OccurrenceIndex!=0)return null;
        var d=_resolutionStack.OfType<DamageFrame>().SingleOrDefault(d=>d.Id==id);
        var w=_resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w=>w.Id==c.ParentFrameId);
        return d is not null&&d.SourceSeat==owner&&d.Amount>0&&w is{SourceCard:{}kind}&&IsSlashCard(kind)&&w.ParentFrameId==d.Id?d:null;
    }
    private IEnumerable<MouGongsunZanMobileDistanceEvent> ActiveMouGszMobileDistance(int owner)=>ProgramEventHistory<MouGongsunZanMobileDistanceEvent>().Where(e=>e.Source.OwnerSeat==owner&&e.Round==_roundNumber&&!ProgramEventHistory<RoundEndedEvent>().Any(r=>r.RoundNumber==e.Round));
    [ProgramDistanceRule(ProgramDistanceStage.DistanceBaseTerms,33600)]
    private void AddMouGszMobileDistance(ProgramDistanceQuery q,List<RuleQueryBaseTerm> terms)
    {
        if(q.Source.Seat==q.Target.Seat)return;
        foreach(var e in ActiveMouGszMobileDistance(q.Source.Seat).Where(e=>e.Offensive))terms.Add(new($"issued:yicong:{e.FrameId}:outgoing",-1));
        foreach(var e in ActiveMouGszMobileDistance(q.Target.Seat).Where(e=>!e.Offensive))terms.Add(new($"issued:yicong:{e.FrameId}:incoming",1));
    }
}
