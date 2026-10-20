using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private SkillProgramStepOutcome PaySimaHuiYeyanSeals(ProgramSkillFrame f)
    {
        if(!SimaHuiSeals.All(m=>_players[f.OwnerSeat].Markers.GetValueOrDefault(m)>0))throw new InvalidOperationException("Jianjie Yeyan needs both actual seals.");
        if(f.SelectedCardIds.Count is not(0 or 4) || f.SelectedCardIds.Any(id=>_cardZones.GetLocation(id)!=CardLocation.Hand(f.OwnerSeat)))throw new InvalidOperationException("Jianjie Yeyan requires its original actual hand materials.");
        var r=NewSimaHuiReceipt(f,"yeyan-paid") with{OriginalCards=f.SelectedCardIds,MovementBefore=_movementSequence,MovementAfter=_movementSequence};ReplaceRuntimeTop(f=f with{SimaHui=r});var ids=new List<string>();
        foreach(var marker in SimaHuiSeals)
        foreach(var latest in CompleteProgramEventHistory().OfType<SimaHuiSealMovedEvent>().Where(e=>e.Marker==marker).GroupBy(e=>e.Issuer).Select(g=>g.Last()).Where(e=>e.Holder==f.OwnerSeat).ToArray())
        {ids.Add(latest.GrantSourceId);SetSimaHuiSeal(f,latest.Issuer,marker,f.OwnerSeat,null);}
        if(SimaHuiSeals.Any(m=>_players[f.OwnerSeat].Markers.GetValueOrDefault(m)>0))throw new InvalidOperationException("Jianjie Yeyan refuses an unissued seal.");
        AdvanceEventRulesAndQueueFact(new SimaHuiYeyanSealsPaidEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,Array.AsReadOnly(ids.ToArray()),r.OriginalCards));return SkillProgramStepOutcome.Continue;
    }
}
