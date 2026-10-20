namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramViewContributor(ProgramViewContribution.PublicCardsAfterResolution, 10)]
    private IReadOnlyList<CardSnapshot> ChengliePublicCards()=>!HasMaChengCapability?[]:Array.AsReadOnly(_resolutionStack.OfType<ProgramSkillFrame>().Where(f=>f.Chenglie is{Outcome:false}).SelectMany(f=>f.Chenglie!.Revealed).Distinct().Select(id=>ToSnapshot(GetAdvancedCard(id))).ToArray());
    [ProgramViewContributor(ProgramViewContribution.NamedAmbushPiles, 20)]
    private IReadOnlyList<NamedAmbushPileSnapshot>? IncludeChenglieSnapshots(IReadOnlyList<NamedAmbushPileSnapshot>? ordinary,int holder,int viewer,bool revealAll)
    {
        if(!HasMaChengCapability)return ordinary;var piles=_resolutionStack.OfType<CardUseFrame>().Where(u=>u.ChenglieLease is{Settled:false}).Select(u=>u.ChenglieLease!).SelectMany(l=>l.Cards.Where(c=>c.HolderSeat==holder&&_cardZones.GetLocation(c.CardId)==c.Location).Select(c=>(Lease:l,Card:c))).GroupBy(x=>x.Lease.Source.OwnerSeat).Select(g=>new NamedAmbushPileSnapshot("ol:chenglie","骋烈",g.Count(),revealAll||g.Key==viewer?Array.AsReadOnly(g.Select(x=>new NamedAmbushPrivateEntrySnapshot(ToSnapshot(GetAdvancedCard(x.Card.CardId)),x.Card.Kind)).ToArray()):null)).ToArray();if(piles.Length==0)return ordinary;return Array.AsReadOnly((ordinary??[]).Concat(piles).ToArray());
    }
    private bool IsChengliePrivateProcessingCard(int cardId)=>HasMaChengCapability&&_resolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.Chenglie is{Outcome:false}r&&r.Stage is ChenglieStage.Exchange or ChenglieStage.ExchangeChildren or ChenglieStage.Assignment or ChenglieStage.AssignmentChildren or ChenglieStage.CleanupChildren&&r.Pool.Contains(cardId)&&_cardZones.GetLocation(cardId)==CardLocation.Processing);
    [ProgramViewContributor(ProgramViewContribution.InvariantProcessing, 20)]
    [ProgramViewContributor(ProgramViewContribution.PublicProcessing, 10)]
    private IReadOnlyList<Card> WithoutChengliePrivateProcessing(IReadOnlyList<Card> cards)=>!HasMaChengCapability?cards:Array.AsReadOnly(cards.Where(c=>!IsChengliePrivateProcessingCard(c.Id)).ToArray());
    private IReadOnlyList<CardSnapshot> ChengliePrivateCards(int viewer)=>!HasMaChengCapability?[]:Array.AsReadOnly(_resolutionStack.OfType<ProgramSkillFrame>().Where(f=>f.OwnerSeat==viewer&&f.Chenglie is{Outcome:false}).SelectMany(f=>f.Chenglie!.Pool).Where(IsChengliePrivateProcessingCard).Distinct().Select(id=>ToSnapshot(GetAdvancedCard(id))).ToArray());
}
