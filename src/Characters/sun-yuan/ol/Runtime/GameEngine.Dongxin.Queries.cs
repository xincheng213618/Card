using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string DongxinDiscardReason="skill-program.sun-yuan.dongxin.discard";
    private const string DongxinReclaimReason="skill-program.sun-yuan.dongxin.reclaim";
    private bool DongxinDirectionAvailable(int owner,string direction)=>!ProgramEventHistory<SunYuanPaidEvent>().Any(e=>e.OwnerSeat==owner&&e.ActualTurn==_turnNumber&&e.TurnOwner==_turnProgression.OwnerSeat&&e.Direction==direction);
    private bool DongxinContextLegal(int owner,string direction,ProgramSkillWindowContext c)=>c.Window==SkillProgramTriggerWindow.AfterDamageApplied&&c.Amount>0&&c.TargetSeat is{}victim&&IsValidPlayerSeat(victim)&&c.SourceSeat is{}source&&IsValidPlayerSeat(source)&&_players[source].IsAlive&&
        (direction=="dealt"?source==owner:direction=="received"&&victim==owner)&&DongxinDirectionAvailable(owner,direction)&&DongxinCanPay(owner,victim);
    private bool DongxinCanPay(int owner,int victim)
    {
        var total=GetHand(_players[victim]).Count+GetEquipment(victim).Count;
        var required=Math.Min(Math.Max(0,_players[victim].MaxHp-_players[victim].Hp)+1,total);
        return SunYuanEligibleCards(owner,victim,false).Count>=required;
    }
    private bool HasDongxinEntitySlashQuota(CharacterState actor,CardKind kind,IReadOnlyList<int> ids)=>HasSunYuanCapability&&actor.IsAlive&&IsSlashCard(kind)&&ids is[var id]&&id>0&&_cardZones.Contains(CardLocation.Hand(actor.Seat),id)&&ProgramEventHistory<DongxinEntityAcquiredEvent>().Any(e=>e.RecipientSeat==actor.Seat&&e.CardId==id&&IsSlashCard(e.EffectiveKind));
    private void BindDongxinEntityUse(CardUseFrame supplied)
    {
        if(!HasSunYuanCapability||supplied.DongxinUse is not null||supplied.Action is not{Type:CardActionType.Use}a||a.ActorSeat!=a.ProviderSeat||!HasDongxinEntitySlashQuota(_players[a.ActorSeat],a.EffectiveKind,a.PhysicalCards.Select(c=>c.CardId).ToArray()))return;
        var id=a.PhysicalCards.Single().CardId;var grant=ProgramEventHistory<DongxinEntityAcquiredEvent>().Last(e=>e.RecipientSeat==a.ActorSeat&&e.CardId==id);
        var receipt=new DongxinUseReceipt(grant.FrameId,id,grant.AcquiredSequence,a.ActorSeat,a.ProviderSeat,supplied.Id,a.ActionId);
        UpdateLifecycleCardUse(supplied.Id,u=>u with{DongxinUse=receipt});AdvanceEventRulesAndQueueFact(new DongxinEntityUseBoundEvent(receipt));
    }
    private bool HasIssuedDongxinEntitySlashQuota(long? useId,int actor)=>useId is{}id&&LifecycleCardUse(id)is{DongxinUse:{}r,Action:{}a}u&&r.UseId==u.Id&&r.ActionId==a.ActionId&&u.SourceSeat==actor&&a.ActorSeat==actor&&a.ProviderSeat==r.Provider&&ShownEntityUseActorMatches(u,r.OriginalActor,r.Provider)&&a.PhysicalCards is[var cost]&&cost.CardId==r.CardId&&IsSlashCard(a.EffectiveKind)&&ProgramEventHistory<DongxinEntityUseBoundEvent>().Count(e=>e.Receipt==r)==1&&ProgramEventHistory<DongxinEntityAcquiredEvent>().Count(e=>e.FrameId==r.GrantFrameId&&e.CardId==r.CardId&&e.RecipientSeat==r.OriginalActor&&e.AcquiredSequence==r.AcquiredSequence)==1;
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent,27491)]
    private void RecordDongxinEntityDeparture(CardMovedEvent e)
    {
        if(!HasSunYuanCapability||e.From.Zone!=CardZoneKind.Hand||e.From.OwnerSeat is not{}owner||e.To==e.From||_cardMovements.LastOrDefault()is not{}m||m.CardId!=e.CardId||m.From!=e.From||m.To!=e.To||m.Reason!=e.Reason)return;
        foreach(var grant in ProgramEventHistory<DongxinEntityAcquiredEvent>().Where(g=>g.RecipientSeat==owner&&g.CardId==m.CardId).DistinctBy(g=>g.FrameId).ToArray())
            if(!ProgramEventHistory<DongxinEntityDepartedEvent>().Any(d=>d.GrantFrameId==grant.FrameId&&d.CardId==m.CardId&&d.Sequence==m.Sequence))AdvanceEventRulesAndQueueFact(new DongxinEntityDepartedEvent(grant.FrameId,owner,m.CardId,m.Sequence));
    }
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeSlashDebit, 30)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.SlashCountExemption, 70)]
    private bool HasIssuedDongxinSlashDebitExemption(long useId, int actorSeat) =>
        HasIssuedDongxinEntitySlashQuota(useId, actorSeat);

}

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaBeforeLimit, 50)]
    private bool DongxinSlashQuotaBeforeLimit(ProgramSlashQuotaQuery query) =>
        HasDongxinEntitySlashQuota(query.Actor, query.Kind, query.PhysicalCardIds ?? (query.PhysicalCard is { Id: > 0 } ? [query.PhysicalCard.Id] : []));
}
