using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private CardConversionSource SimaHuiIssuer(ProgramSkillFrame f)=>new("ol:jianjie","seals",f.OwnerSeat,f.SkillInstanceId);
    private static string SimaHuiSealSource(CardConversionSource issuer,PlayerMarkerKind marker)=>$"sima-hui:seal:{issuer.OwnerSeat}:{issuer.SkillInstanceId}:{marker}";
    private int? SimaHuiHolder(CardConversionSource issuer,PlayerMarkerKind marker)=>CompleteProgramEventHistory().OfType<SimaHuiSealMovedEvent>().LastOrDefault(e=>e.Issuer==issuer && e.Marker==marker)?.Holder;
    private int SimaHuiOwnTurns(int owner)=>CompleteProgramEventHistory().OfType<TurnStartedEvent>().Count(e=>e.ActorSeat==owner);
    private void SetSimaHuiSeal(ProgramSkillFrame f,CardConversionSource issuer,PlayerMarkerKind marker,int? from,int? to)
    {
        if(SimaHuiHolder(issuer,marker)!=from || to==from || to is{}recipient && !_players[recipient].IsAlive)throw new InvalidOperationException("Jianjie seal transfer lost its exact issuer or recipient.");
        void Change(int seat,int delta)
        {
            var p=_players[seat];var key=(marker,issuer.OwnerSeat);var attributed=p.MarkerSourceCounts.GetValueOrDefault(key);var total=p.Markers.GetValueOrDefault(marker);
            if(attributed+delta<0 || total+delta<0)throw new InvalidOperationException("Jianjie cannot remove another source's seal.");
            if(attributed+delta==0)p.MarkerSourceCounts.Remove(key);else p.MarkerSourceCounts[key]=attributed+delta;
            if(total+delta==0)p.Markers.Remove(marker);else p.Markers[marker]=total+delta;
            AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(f.Id,seat,marker,delta,total+delta,issuer.OwnerSeat,"program.sima-hui.seal"));
        }
        if(from is{}old)Change(old,-1);if(to is{}next)Change(next,1);
        var sourceId=SimaHuiSealSource(issuer,marker);foreach(var p in _players)foreach(var grant in p.SkillGrants.Grants.Where(g=>g.SourceId==sourceId).ToArray())p.SkillGrants.RemoveGrant(grant.GrantId);
        if(to is{}holder){var id=marker==PlayerMarkerKind.DragonSeal?"ol:huoji-jianjie":"ol:lianhuan-jianjie";var grantId=sourceId+":"+id;_players[holder].SkillGrants.Grant(new(grantId,id,grantId,sourceId));}
        AdvanceEventRulesAndQueueFact(new SimaHuiSealMovedEvent(f.Id,issuer,f.GameplayHash,f.SimaHui!.ActualTurn,marker,from,to,sourceId));RefreshSimaHuiCombinedGrants();
    }
    private void RefreshSimaHuiCombinedGrants()
    {
        foreach(var p in _players)
        {
            var source=$"sima-hui:combined:{p.Seat}";var id=source+":ol:yeyan-jianjie";var both=SimaHuiSeals.All(m=>p.Markers.GetValueOrDefault(m)>0);
            if(both)p.SkillGrants.Grant(new(id,"ol:yeyan-jianjie",id,source));else p.SkillGrants.RemoveGrant(id);
        }
    }
}
