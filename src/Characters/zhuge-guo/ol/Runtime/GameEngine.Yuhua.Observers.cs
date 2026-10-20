namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void MarkYuhuaTopPlacement(ProgramSkillFrame supplied,ProgramTopReorder state)
    {
        if(supplied.ZhugeGuo is not{Stage:ZhugeGuoStage.YuhuaInspect}r)return;
        var f=GetActiveProgramFrame(supplied.Id);RequireZhugeGuo(f);
        var selected=state.TopCardIds.Concat(state.BottomCardIds).ToArray();
        if(r.PlacementIssued||!state.ViewedCardIds.SequenceEqual(r.Viewed)||selected.Length!=r.Viewed.Count||selected.Distinct().Count()!=selected.Length||selected.Any(id=>!r.Viewed.Contains(id))||!_cardZones.CardsAt(CardLocation.DrawPile).TakeLast(state.TopCardIds.Count).Reverse().Select(c=>c.Id).SequenceEqual(state.TopCardIds)||!_cardZones.CardsAt(CardLocation.DrawPile).Take(state.BottomCardIds.Count).Select(c=>c.Id).SequenceEqual(state.BottomCardIds))throw new InvalidOperationException("Yuhua placement requires exactly the original private entities and real native top/bottom partition.");
        ReplaceRuntimeTop(f with{ZhugeGuo=r with{PlacementIssued=true,PlacedTop=state.TopCardIds,PlacedBottom=state.BottomCardIds}});AdvanceEventRulesAndQueueFact(new YuhuaPrivatePeekPlacedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.WindowId,state.TopCardIds.Count,state.BottomCardIds.Count));
    }
}
