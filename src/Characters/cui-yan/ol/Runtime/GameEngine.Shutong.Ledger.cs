namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsCuiYanNextUseForbidden(int actorSeat,IReadOnlyList<int> materialIds,Suit? nativeEffectiveSuit)
    {
        if(!HasCuiYanCapability||!CuiYanActualTurnOpen)return false;
        var suit=CuiYanTrueUseSuit(materialIds,nativeEffectiveSuit);if(suit==Suit.None)return false;
        return OutstandingShutong(actorSeat).Any(g=>g.Suit==suit);
    }
    private IEnumerable<ShutongNextUseGrantedEvent> OutstandingShutong(int seat)=>ProgramEventHistory<ShutongNextUseGrantedEvent>().Where(g=>g.ActualTurn==_turnNumber&&g.TurnOwner==_turnProgression.OwnerSeat&&g.RecipientSeat==seat&&!ProgramEventHistory<ShutongNextUseConsumedEvent>().Any(c=>c.GrantFrameId==g.FrameId&&c.RecipientSeat==seat));
    private void ConsumeShutongForActualUse(long useId,CardActionContext? a,int actor,bool response)
    {
        var id=a?.ActionId??0;
        if(ProgramEventHistory<ShutongNextUseConsumedEvent>().Any(e=>e.UseId==useId&&e.ActionId==id&&e.RecipientSeat==actor&&e.ResponseUse==response))return;
        foreach(var grant in OutstandingShutong(actor).ToArray())
            AdvanceEventRulesAndQueueFact(new ShutongNextUseConsumedEvent(grant.FrameId,actor,useId,id,response,_turnNumber,_turnProgression.OwnerSeat));
    }
}
