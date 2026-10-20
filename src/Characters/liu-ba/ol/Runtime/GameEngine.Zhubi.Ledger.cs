namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidLiuBaCoin(LiuBaCoinMintedEvent e)=>e.Source.SkillId=="ol:zhubi"&&_contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash==e.GameplayHash&&IsValidPlayerSeat(e.RecipientSeat)&&ProgramEventHistory<LiuBaStartedEvent>().Count(s=>s.FrameId==e.FrameId&&s.Source==e.Source&&s.GameplayHash==e.GameplayHash&&s.Op==SkillProgramEffectOp.RecastZhubiAndMarkActualDraw)==1&&ProgramEventHistory<LiuBaEntityMovedEvent>().Count(p=>p.FrameId==e.FrameId&&p.Step==1&&p.From.OwnerSeat==e.RecipientSeat&&p.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment&&_cardMovements.Any(m=>m.Sequence==p.Sequence&&m.CardId==p.CardId&&m.From==p.From&&m.To==p.To&&m.Reason==CardMoveReasons.RecastDiscard))==1&&_cardMovements.Count(m=>m.Sequence==e.GainSequence&&m.CardId==e.CardId&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(e.RecipientSeat)&&m.Reason==CardMoveReasons.RecastDraw)==1;
    private LiuBaCoinMintedEvent? LiuBaCoin(int id)=>HasLiuBaCapability?ProgramEventHistory<LiuBaCoinMintedEvent>().FirstOrDefault(e=>e.CardId==id&&ValidLiuBaCoin(e)):null;
    private bool HasLiuBaCoinEntity(int cardId)=>LiuBaCoin(cardId)is not null;
    private void AppendLiuBaCoinEndingItems(List<TurnEndingBoundaryItem> items)
    {
        if(!HasLiuBaCapability||!_players[_currentSeat].IsAlive)return;var coin=LiuBaCoins(_currentSeat).Select(e=>LiuBaCoin(e.CardId)).FirstOrDefault(e=>e is not null);if(coin is null)return;var t=_contentRegistry.GetSkill("ol:zhubi").Program!.Triggers.Single(t=>t.Id=="issued-holder-ending");var c=new ProgramTriggerCandidate(_currentSeat,"ol:zhubi",t.Id,coin.Source.SkillInstanceId,coin.GameplayHash,t.Priority,items.Count);items.Add(new(TurnEndingBoundaryItemKind.Program,t.Priority,$"liu-ba.coin:{_turnNumber}:{_currentSeat}:{coin.CardId}",c,CaptureProgramTriggerFacts(_players[c.OwnerSeat])));
    }
    private bool IsIssuedLiuBaCoinEndingCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(!HasLiuBaCapability||c.SkillId!="ol:zhubi"||c.BindingId!="issued-holder-ending"||_winner!=Winner.None||c.OwnerSeat!=_currentSeat||!_players[c.OwnerSeat].IsAlive||context.Window!=SkillProgramTriggerWindow.TurnEnding||context.OwnerSeat!=c.OwnerSeat||context.SourceSeat!=_currentSeat||context.TargetSeat!=_currentSeat||context.OccurrenceIndex!=c.OccurrenceIndex||_resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId)is not{}parent||parent.OwnerSeat!=_currentSeat||parent.TurnNumber!=_turnNumber||parent.ItemIndex<0||parent.ItemIndex>=parent.Items.Count||parent.Items[parent.ItemIndex].Candidate!=c)return false;
        return ProgramEventHistory<LiuBaCoinMintedEvent>().Any(e=>ValidLiuBaCoin(e)&&e.Source.SkillInstanceId==c.SkillInstanceId&&e.GameplayHash==c.GameplayHash&&parent.Items[parent.ItemIndex].StableIdentity==$"liu-ba.coin:{_turnNumber}:{_currentSeat}:{e.CardId}")&&(LiuBaCoins(c.OwnerSeat).Length>0||_resolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.LiuBa is{Op:SkillProgramEffectOp.ExchangeZhubiCoinsAtEnding,Paid:true}&&f.WindowContext==context&&MountObserverCandidateMatches(f,c)));
    }
}
