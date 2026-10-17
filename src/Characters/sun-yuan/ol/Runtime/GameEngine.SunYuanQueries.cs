using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string DongxinDiscardReason="skill-program.sun-yuan.dongxin.discard";
    private const string SayingObtainReason="skill-program.sun-yuan.saying.obtain";
    private const string DongxinReclaimReason="skill-program.sun-yuan.dongxin.reclaim";
    private bool HasSunYuanCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveSayingPlayStartObtainAndReply)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveDongxinDamageDiscardAndReclaim);
    private static CardConversionSource SunYuanSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private static string SunYuanHash<T>(T value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private bool SunYuanParent(ProgramSkillFrame f)
    {
        if(f.TriggerId is null||f.WindowContext is not{}c||c.OwnerSeat!=f.OwnerSeat)return false;
        var index=_resolutionStack.FindIndex(x=>x.Id==f.Id);if(index<1)return false;
        if(c.Window==SkillProgramTriggerWindow.PlayPhaseStarting)
        {
            if(_resolutionStack[index-1]is not PlayPhaseStartingBoundaryFrame w||w.Id!=c.ParentFrameId||w.OwnerSeat!=f.OwnerSeat||w.ItemIndex<0||w.ItemIndex>=w.Items.Count||w.Items[w.ItemIndex].Candidate is not{}a)return false;
            return a.OwnerSeat==f.OwnerSeat&&a.SkillId==f.SkillId&&a.BindingId==f.TriggerId&&a.SkillInstanceId==f.SkillInstanceId&&a.GameplayHash==f.GameplayHash&&a.OccurrenceIndex==c.OccurrenceIndex;
        }
        if(c.Window!=SkillProgramTriggerWindow.AfterDamageApplied||_resolutionStack[index-1]is not DamageTriggerWindowFrame d||d.Id!=c.ParentFrameId||d.ParentFrameId!=c.DamageFrameId||d.TriggerWindow!=c.Window||d.CandidateIndex<0||d.CandidateIndex>=d.Candidates.Count)return false;
        var candidate=d.Candidates[d.CandidateIndex].ToProgramCandidate();
        return candidate.OwnerSeat==f.OwnerSeat&&candidate.SkillId==f.SkillId&&candidate.BindingId==f.TriggerId&&candidate.SkillInstanceId==f.SkillInstanceId&&candidate.GameplayHash==f.GameplayHash&&candidate.OccurrenceIndex==c.OccurrenceIndex&&d.TargetSeat==c.TargetSeat&&_resolutionStack.OfType<DamageFrame>().Any(a=>a.Id==d.ParentFrameId&&a.TargetSeat==c.TargetSeat&&a.Amount==c.Amount&&a.SourceSeat==c.SourceSeat);
    }
    private bool DongxinDirectionAvailable(int owner,string direction)=>!ProgramEventHistory<SunYuanPaidEvent>().Any(e=>e.OwnerSeat==owner&&e.ActualTurn==_turnNumber&&e.TurnOwner==_turnProgression.OwnerSeat&&e.Direction==direction);
    private bool DongxinContextLegal(int owner,string direction,ProgramSkillWindowContext c)=>c.Window==SkillProgramTriggerWindow.AfterDamageApplied&&c.Amount>0&&c.TargetSeat is{}victim&&IsValidPlayerSeat(victim)&&c.SourceSeat is{}source&&IsValidPlayerSeat(source)&&_players[source].IsAlive&&
        (direction=="dealt"?source==owner:direction=="received"&&victim==owner)&&DongxinDirectionAvailable(owner,direction)&&DongxinCanPay(owner,victim);
    private bool DongxinCanPay(int owner,int victim)
    {
        var total=GetHand(_players[victim]).Count+GetEquipment(victim).Count;
        var required=Math.Min(Math.Max(0,_players[victim].MaxHp-_players[victim].Hp)+1,total);
        return SunYuanEligibleCards(owner,victim,false).Count>=required;
    }
    private IReadOnlyList<Card> SunYuanEligibleCards(int chooser,int holder,bool obtain)=>GetHand(_players[holder]).Concat(GetEquipment(holder)).Concat(obtain?GetJudgment(_players[holder]):[]).Where(card=>
        (!obtain||!card.IsGeneralWeapon)&&!IsForeignEquipmentDiscardPrevented(chooser,card,_cardZones.GetLocation(card.Id),obtain?OwnedCardMoveIntent.Transfer:OwnedCardMoveIntent.Discard)&&
        (_cardZones.GetLocation(card.Id)!=CardLocation.Hand(chooser)||obtain||!IsSelfHandCategoryDiscardForbidden(chooser,card,CardLocation.Hand(chooser),OwnedCardMoveIntent.Discard))&&
        (_cardZones.GetLocation(card.Id)!=CardLocation.Equipment(chooser)||!_resolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.OwnerSeat==chooser&&f.SunYuan is not null&&IsActiveProgramSourceEquipmentCard(chooser,f.SkillId,f.SkillInstanceId,card)))).ToArray();
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeCondition,27490)]
    private bool CanOfferSunYuan(SkillProgramTrigger t,ProgramSkillWindowContext c,int owner)=>!t.Effects.Any(e=>SunYuanComposition.Handles(e.Op))||_players[owner].IsAlive&&
        (t.Effects[0].Op==SkillProgramEffectOp.ResolveSayingPlayStartObtainAndReply?c.Window==SkillProgramTriggerWindow.PlayPhaseStarting&&c.OwnerSeat==owner&&_players.Any(p=>p.IsAlive&&p.Seat!=owner&&SunYuanEligibleCards(owner,p.Seat,true).Count>0):DongxinContextLegal(owner,t.Id,c));
    private bool HasSayingRoundRange(int actor,int target)=>HasSunYuanCapability&&actor!=target&&IsValidPlayerSeat(actor)&&IsValidPlayerSeat(target)&&_players[actor].IsAlive&&_players[target].IsAlive&&ProgramEventHistory<SayingRangeGrantedEvent>().Any(e=>e.Round==_roundNumber&&(e.OwnerSeat==actor&&e.Holder==target||e.Holder==actor&&e.OwnerSeat==target));
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
    private bool TryGetSayingSlashRequest(ProgramSkillFrame f,out int actorSeat,out IReadOnlyList<int> targetSeats)
    {
        actorSeat=-1;targetSeats=[];
        if(f.SunYuan is not{Paid:true}r||r.Op!=SkillProgramEffectOp.ResolveSayingPlayStartObtainAndReply||r.Stage is not(SunYuanStage.Request or SunYuanStage.SlashIssuing or SunYuanStage.NativeUse or SunYuanStage.NativeChildren)||r.Source!=SunYuanSource(f)||r.GameplayHash!=f.GameplayHash||f.InstructionIndex!=1||ProgramEventHistory<SayingRequestIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.ActorSeat==r.Holder&&e.TargetSeat==f.OwnerSeat)!=1)return false;
        actorSeat=r.Holder;targetSeats=Array.AsReadOnly(new[]{f.OwnerSeat});return true;
    }
    private bool IsSayingSlashRequest(ProgramSkillFrame f)=>f.SunYuan?.Stage==SunYuanStage.Request&&TryGetSayingSlashRequest(f,out _,out _);
    private bool IsSayingSlashQuery(CharacterState actor)
    {
        if(!actor.IsAlive)return false;
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame top)return false;
        if(top.SunYuan?.Stage is SunYuanStage.Request or SunYuanStage.SlashIssuing&&TryGetSayingSlashRequest(top,out var seat,out _)&&seat==actor.Seat&&_players[top.OwnerSeat].IsAlive)return true;
        return top.EquipmentSlotNamedUse is{OriginalParentId:{}parent}&&_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==parent)is{}owner&&top.OwnerSeat==actor.Seat&&_players[owner.OwnerSeat].IsAlive&&SayingEquipmentSlotPaymentRidesOn(owner,top);
    }
}
