using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void PayDongxin(ProgramSkillFrame f)
    {
        var r=f.SunYuan!;
        if(r.Paid||r.Selected.Count!=r.Required||r.Selected.Any(id=>!SunYuanEligibleCards(f.OwnerSeat,r.Holder,false).Any(c=>c.Id==id))||!DongxinDirectionAvailable(f.OwnerSeat,f.TriggerId!))throw new InvalidOperationException("Dongxin cannot underpay, repeat a direction, or replace its exact HE primary set.");
        ReplaceRuntimeTop(f=f with{SunYuan=r with{Paid=true}});AdvanceEventRulesAndQueueFact(new SunYuanPaidEvent(f.Id,r.ActualTurn,r.TurnOwner,f.OwnerSeat,f.TriggerId!,r.Selected.Count));
        IssueSunYuanMove(f,r.Selected,CardLocation.DiscardPile,SunYuanStage.DiscardChildren,DongxinDiscardReason,true);ContinueSunYuan(f.Id);
    }
    private void ReclaimDongxin(ProgramSkillFrame f)
    {
        var r=f.SunYuan!;var eligible=r.Materials.Where(c=>IsSlashCard(c.EffectiveKind)&&_cardZones.Contains(CardLocation.DiscardPile,c.CardId)&&ProgramEventHistory<SunYuanEntityMovedEvent>().SingleOrDefault(e=>e.FrameId==f.Id&&e.Stage==SunYuanStage.DiscardChildren&&e.CardId==c.CardId)is{}paid&&_cardMovements.Last(m=>m.CardId==c.CardId).Sequence==paid.Sequence).Select(c=>c.CardId).ToArray();
        if(!_players[f.OwnerSeat].IsAlive||_winner!=Winner.None||eligible.Length==0){FinishSunYuan(f,true);return;}
        IssueSunYuanMove(f,eligible,CardLocation.Hand(f.OwnerSeat),SunYuanStage.ReclaimChildren,DongxinReclaimReason,false);ContinueSunYuan(f.Id);
    }
}
