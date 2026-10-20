using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasSunYuanCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveSayingPlayStartObtainAndReply)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveDongxinDamageDiscardAndReclaim);
    private static CardConversionSource SunYuanSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_sun_yuan_ol_SunYuanQueries.SunYuanSource(f);
    private static string SunYuanHash<T>(T value) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_sun_yuan_ol_SunYuanQueries.SunYuanHash<T>(value);
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
    private IReadOnlyList<Card> SunYuanEligibleCards(int chooser,int holder,bool obtain)=>GetHand(_players[holder]).Concat(GetEquipment(holder)).Concat(obtain?GetJudgment(_players[holder]):[]).Where(card=>
        (!obtain||!card.IsGeneralWeapon)&&!IsForeignEquipmentDiscardPrevented(chooser,card,_cardZones.GetLocation(card.Id),obtain?OwnedCardMoveIntent.Transfer:OwnedCardMoveIntent.Discard)&&
        (_cardZones.GetLocation(card.Id)!=CardLocation.Hand(chooser)||obtain||!IsSelfHandCategoryDiscardForbidden(chooser,card,CardLocation.Hand(chooser),OwnedCardMoveIntent.Discard))&&
        (_cardZones.GetLocation(card.Id)!=CardLocation.Equipment(chooser)||!_resolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.OwnerSeat==chooser&&f.SunYuan is not null&&IsActiveProgramSourceEquipmentCard(chooser,f.SkillId,f.SkillInstanceId,card)))).ToArray();
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeCondition,27490)]
    private bool CanOfferSunYuan(SkillProgramTrigger t,ProgramSkillWindowContext c,int owner)=>!t.Effects.Any(e=>SunYuanComposition.Handles(e.Op))||_players[owner].IsAlive&&
        (t.Effects[0].Op==SkillProgramEffectOp.ResolveSayingPlayStartObtainAndReply?c.Window==SkillProgramTriggerWindow.PlayPhaseStarting&&c.OwnerSeat==owner&&_players.Any(p=>p.IsAlive&&p.Seat!=owner&&SunYuanEligibleCards(owner,p.Seat,true).Count>0):DongxinContextLegal(owner,t.Id,c));

}

public sealed partial class GameEngine
{
}

public sealed partial class GameEngine
{
}
