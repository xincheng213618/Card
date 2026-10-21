namespace CardGame.Core;
internal sealed partial class MouZhuRanRuntime
{
    private void AcceptHeyuan(ProgramSkillFrame f)
    {
        var r=f.MouZhuRan!;
        if(r.Selected.Count!=r.X||!Beneficiaries(f.OwnerSeat).Contains(r.Recipient)||r.Effects is not[var effect]||
            r.Selected.Any(c=>!facts.Discardable(f.OwnerSeat).Any(l=>l.CardId==c.CardId&&l.From==c.From)))
            throw new InvalidOperationException("Heyuan must pay the latest exact X from real HE for a still-eligible wounded beneficiary.");
        frames.ReplaceTop(f=f with{MouZhuRan=r with{Accepted=true,Stage=MouZhuRanStage.Pay,Participants=[f.OwnerSeat],
            Votes=[new(f.OwnerSeat){Cards=r.Selected}],Selected=[],Cursor=0}});
        // This fact is both the lifetime per-target debit and the future support
        // link. Skill instance replacement cannot erase either issued obligation.
        events.AdvanceFact(new MouZhuRanHeyuanAcceptedEvent(f.Id,r.Source,r.GameplayHash,r.Recipient,r.PreviousFrame,r.X,effect));
    }
}
