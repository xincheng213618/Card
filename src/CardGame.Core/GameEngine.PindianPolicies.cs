namespace CardGame.Core;
public sealed record PindianTopSourceChosenEvent(int OwnerSeat,CardConversionSource Source,int CardId) : IGameEvent;
public sealed partial class GameEngine
{
    private bool CanOfferPindianTopChoice(CharacterState owner)=>(_cardZones.Count(CardLocation.DrawPile)>0 || _cardZones.Count(CardLocation.DiscardPile)>0) && CardPolicies(owner,SkillProgramCardPolicyKind.PindianTopCardChoice).Any();
    private int EffectivePindianRank(CharacterState owner, Card card)
    {
        var rank = CardPolicies(owner, SkillProgramCardPolicyKind.PindianRankBySuit)
            .Where(binding => binding.Policy.InputSuit == EffectiveSuit(owner, card))
            .Select(binding => binding.Policy.Value).DefaultIfEmpty(EffectiveOwnedCardRank(owner, card)).Max();
        // Several policy nodes on one enabled instance still describe one bonus.
        // Recompute from the real face/identity each time, never from a prior result.
        foreach (var instance in CardPolicies(owner, SkillProgramCardPolicyKind.PindianRankBonus)
            .GroupBy(binding => (binding.Source.SkillId, binding.Source.SkillInstanceId)))
            rank = (int)Math.Min(13L, (long)rank + instance.Max(binding => binding.Policy.Value));
        return rank;
    }
    private bool HasCurrentPindianSourceCard(PindianFrame frame)=>frame.OwnedPileSource is not null
        ? IsGuanHaiPindianSource(frame, false)
        : frame.SourceUsesDrawPileTop
        ? frame.SourceCardId is { } id && _cardZones.GetLocation(id)==CardLocation.Processing && !(frame.ParentProcessingCardIds??[]).Contains(id)
        : GetHand(_players[frame.SourceSeat]).Any(c=>c.Id==frame.SourceCardId);
    private int ReserveOrReadPindianTop(CharacterState owner,bool reserve)
    {
        if(!CanOfferPindianTopChoice(owner) || !EnsureDrawPile()) throw new InvalidOperationException("The optional Pindian top source is unavailable.");
        var card=_cardZones.CardsAt(CardLocation.DrawPile).Last();
        var binding=CardPolicies(owner,SkillProgramCardPolicyKind.PindianTopCardChoice).First();
        AdvanceEventRulesAndQueueFact(new PindianTopSourceChosenEvent(owner.Seat,new(binding.Source.SkillId,binding.Policy.Id,owner.Seat,binding.Source.SkillInstanceId),card.Id));
        AddLog("SkillTriggered",$"{owner.Name} 发动【{_contentRegistry.GetSkill(binding.Source.SkillId).Name}】，使用牌堆顶的牌拼点。",owner.Seat);
        if(reserve) MoveCard(card,CardLocation.DrawPile,CardLocation.Processing,new CardMoveReason("pindian.reserve-top"));
        return card.Id;
    }
    private int[] CurrentPindianProcessingIds(PindianFrame frame)=>frame.Result is { } result?AvailablePindianCards(result):frame.SourceUsesDrawPileTop && frame.SourceCardId is { } id?[id]:[];
}
