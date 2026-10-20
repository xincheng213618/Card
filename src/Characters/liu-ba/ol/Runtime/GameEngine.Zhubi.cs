namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCondition,27390,SkillProgramEffectOp.RecastZhubiAndMarkActualDraw)]
    private bool AllowsLiuBaRecast(ProgramActivationLegalityContext c)=>_programPhaseUses.GetValueOrDefault((c.OwnerSeat,c.SkillId,c.Activation.UsageGroup))<c.Owner.MaxHp&&_players.Any(p=>p.IsAlive&&LiuBaRecastEntities(p.Seat).Length>0);
    private LiuBaEntity[] LiuBaRecastEntities(int seat)=>new[]{CardLocation.Hand(seat),CardLocation.Equipment(seat)}.SelectMany(l=>_cardZones.CardsAt(l).Select(c=>new LiuBaEntity(c.Id,c.Kind,l))).OrderBy(c=>c.CardId).ToArray();
    private LiuBaEntity[] LiuBaCoins(int seat)=>LiuBaOwnedEntities(seat).Where(e=>HasLiuBaCoinEntity(e.CardId)).ToArray();
    private void PayLiuBaRecast(ProgramSkillFrame f,LiuBaEntity material)
    {
        f=BeginFrameNativeRecastInvocation(f,f.LiuBa!.Recipient,[material.CardId]);
        PrepareLiuBaMovement(ref f,LiuBaStage.RecastChildren,[material]);MoveProgramCardsFromMultipleSources([material.CardId],CardLocation.DiscardPile,CardMoveReasons.RecastDiscard,(batch,records)=>RecordLiuBaMovement(f,batch,records));CompleteLiuBaMovement(f,1);
    }
    private void PayLiuBaExchange(ProgramSkillFrame f)
    {
        var r=f.LiuBa!;var live=LiuBaCoins(f.OwnerSeat);if(r.Coins.Count==0||r.Coins.Count!=r.SelectedBottom.Count||r.Coins.Distinct().Count()!=r.Coins.Count||r.SelectedBottom.Distinct().Count()!=r.SelectedBottom.Count||r.Entities.Any(e=>!live.Contains(e))||!r.ViewedBottom.SequenceEqual(_cardZones.CardsAt(CardLocation.DrawPile).Take(r.ViewedBottom.Count).Select(c=>c.Id)))throw new InvalidOperationException("Liu Ba exchange requires both exact frozen original sets.");
        var deck=_cardZones.CardsAt(CardLocation.DrawPile).Select(c=>c.Id).ToArray();var mapping=r.SelectedBottom.Select((id,i)=>(id,coin:r.Coins[i])).ToDictionary(x=>x.id,x=>x.coin);var desired=deck.Select(id=>mapping.GetValueOrDefault(id,id)).ToArray();
        var entities=r.Entities.Concat(r.SelectedBottom.Select(id=>new LiuBaEntity(id,GetAdvancedCard(id).Kind,CardLocation.DrawPile))).ToArray();PrepareLiuBaMovement(ref f,LiuBaStage.ExchangeChildren,entities);
        MoveProgramCardsFromMultipleSources(entities.Select(e=>e.CardId).ToArray(),CardLocation.DrawPile,new(LiuBaExchangeReason),(batch,records)=>
        {RecordLiuBaMovement(f,batch,records);var now=_cardZones.CardsAt(CardLocation.DrawPile).Reverse().Select(c=>c.Id).ToArray();_cardZones.ReorderDrawPileTop(now,desired.Reverse().ToArray(),[]);},(card,from)=>from==CardLocation.DrawPile?CardLocation.Hand(f.OwnerSeat):CardLocation.DrawPile);
        CompleteLiuBaMovement(f,entities.Length);
    }
    [ProgramViewContributor(ProgramViewContribution.PrivateCards, 50)]
    private IReadOnlyList<CardSnapshot> LiuBaPrivatelyViewedBottomCards(int viewerSeat)=>_resolutionStack.LastOrDefault()is ProgramSkillFrame{LiuBa:{Stage:LiuBaStage.Coins or LiuBaStage.Bottom}r}f&&f.OwnerSeat==viewerSeat&&_pendingDecision is{IsPrivate:true,PlayerSeat:var actor}&&actor==viewerSeat?r.ViewedBottom.Select(id=>ToSnapshot(GetAdvancedCard(id))).ToArray():[];
}
