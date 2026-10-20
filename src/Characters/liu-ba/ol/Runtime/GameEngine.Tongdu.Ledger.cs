namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidTongduGift(TongduGiftIssuedEvent e)=>e.Source.SkillId=="ol:tongdu"&&e.ActualTurn>0&&e.TurnOwner==e.Source.OwnerSeat&&IsValidPlayerSeat(e.DonorSeat)&&_contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash==e.GameplayHash&&ProgramEventHistory<LiuBaStartedEvent>().Count(s=>s.FrameId==e.FrameId&&s.Source==e.Source&&s.GameplayHash==e.GameplayHash&&s.Op==SkillProgramEffectOp.IssueTongduHandGift&&s.ActualTurn==e.ActualTurn&&s.TurnOwner==e.TurnOwner)==1&&(e.DonorSeat==e.Source.OwnerSeat?e.GiftSequence is null&&_cardMovements.Any(m=>m.Sequence==e.HandEntry&&m.CardId==e.CardId&&m.To==CardLocation.Hand(e.DonorSeat)):e.GiftSequence is{}seq&&_cardMovements.Any(m=>m.Sequence==seq&&m.CardId==e.CardId&&m.CardKind==e.Kind&&m.From==CardLocation.Hand(e.DonorSeat)&&m.To==CardLocation.Hand(e.Source.OwnerSeat)&&m.Reason.Value==TongduGiftReason));
    private bool TongduConsumed(long id)=>ProgramEventHistory<TongduDebtConsumedEvent>().Any(e=>e.DebtId==id);
    private IEnumerable<ProgramTriggerCandidate> IssuedTongduPlayEndingCandidates(CharacterState owner)
    {
        if(!HasLiuBaCapability||!owner.IsAlive)yield break;var debts=ProgramEventHistory<TongduGiftIssuedEvent>().ToArray();
        for(var i=0;i<debts.Length;i++){var d=debts[i];if(d.ActualTurn!=_turnNumber||d.TurnOwner!=owner.Seat||d.Source.OwnerSeat!=owner.Seat||TongduConsumed(d.FrameId)||!ValidTongduGift(d))continue;var t=_contentRegistry.GetSkill(d.Source.SkillId).Program!.Triggers.Single(t=>t.Id=="issued-play-end");yield return new(owner.Seat,d.Source.SkillId,t.Id,d.Source.SkillInstanceId,d.GameplayHash,t.Priority,i);}
    }
    private TongduGiftIssuedEvent? CurrentTongduDebt(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(!HasLiuBaCapability||c.SkillId!="ol:tongdu"||c.BindingId!="issued-play-end"||context.Window!=SkillProgramTriggerWindow.PlayEnding||context.OwnerSeat!=c.OwnerSeat||c.OwnerSeat!=_currentSeat||context.OccurrenceIndex!=c.OccurrenceIndex||_resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId)is not{Window:SkillProgramTriggerWindow.PlayEnding}parent||parent.OwnerSeat!=c.OwnerSeat||parent.CandidateIndex<0||parent.CandidateIndex>=parent.Candidates.Count||parent.Candidates[parent.CandidateIndex]!=c)return null;
        var all=ProgramEventHistory<TongduGiftIssuedEvent>().ToArray();if(c.OccurrenceIndex<0||c.OccurrenceIndex>=all.Length)return null;var d=all[c.OccurrenceIndex];return ValidTongduGift(d)&&d.Source.OwnerSeat==c.OwnerSeat&&d.Source.SkillInstanceId==c.SkillInstanceId&&d.GameplayHash==c.GameplayHash&&d.ActualTurn==_turnNumber&&d.TurnOwner==_currentSeat?d:null;
    }
    private bool IsIssuedTongduCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext context)=>_winner==Winner.None&&IsValidPlayerSeat(c.OwnerSeat)&&_players[c.OwnerSeat].IsAlive&&CurrentTongduDebt(c,context)is{}d&&(!TongduConsumed(d.FrameId)||_resolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.LiuBa is{DebtId:{}id}&&id==d.FrameId&&MountObserverCandidateMatches(f,c)&&f.WindowContext==context));
}
