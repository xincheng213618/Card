namespace CardGame.Core;

public sealed partial class SimpleAiBrain
{
    private static IEnumerable<CardSnapshot> VisibleOriginalPublicPileCards(PlayerSnapshot self) =>
        (self.PublicPersistentPiles?.SelectMany(pile => pile.Cards) ?? [])
            .Concat(self.PublicPersistentPileCards ?? []).DistinctBy(card => card.Id);

    private CardSnapshot AiActionPhysicalCard(PlayerSnapshot self, LegalAction action) =>
        self.Hand.Concat(self.WoodenOxGrain ?? []).Concat(self.Equipment).Concat(self.AuthorityCards ?? [])
            .Concat(_includeJudgmentMaterials ? self.Judgment : [])
            .Concat(action.NativePublicPileSource is { } source && source.OwnerSeat == self.Seat
                ? VisibleOriginalPublicPileCards(self) : [])
            .DistinctBy(card => card.Id).Single(card => card.Id == action.CardId);
}
