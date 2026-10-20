namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void PayTianhouExchange(ProgramSkillFrame f)
    {
        var r = f.ZhouQun!; var deck = _cardZones.CardsAt(CardLocation.DrawPile).Select(c => c.Id).ToArray();
        if (!deck.TakeLast(r.Viewed.Count).Reverse().SequenceEqual(r.Viewed) || r.SelectedHand.Count != r.SelectedTop.Count || r.SelectedHand.Any(id => _cardZones.GetLocation(id) != CardLocation.Hand(f.OwnerSeat)) || r.SelectedTop.Any(id => !r.Viewed.Contains(id))) throw new InvalidOperationException("Tianhou must exchange exact original Hand and original top entities in equal numbers.");
        var desiredTop = r.Viewed.ToArray(); for (var i = 0; i < r.SelectedTop.Count; i++) desiredTop[Array.IndexOf(desiredTop, r.SelectedTop[i])] = r.SelectedHand[i];
        var desired = deck.Take(deck.Length - r.Viewed.Count).Concat(desiredTop.Reverse()).ToArray(); BeginZhouQunMovement(ref f, ZhouQunStage.ExchangeChildren);
        void Reorder() { var all = _cardZones.CardsAt(CardLocation.DrawPile).Reverse().Select(c => c.Id).ToArray(); _cardZones.ReorderDrawPileTop(all, desired.Reverse().ToArray(), []); }
        if (r.SelectedHand.Count > 0) MoveProgramCardsFromMultipleSources(r.SelectedHand.Concat(r.SelectedTop).ToArray(), CardLocation.DrawPile, new(TianhouExchangeReason), (batch, records) => { RecordZhouQunMoves(f, batch, records); Reorder(); }, (_, from) => from == CardLocation.DrawPile ? CardLocation.Hand(f.OwnerSeat) : CardLocation.DrawPile);
        else Reorder();
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { ZhouQun = f.ZhouQun! with { Viewed = desiredTop } }); CompleteZhouQunMovement(f.Id); ContinueZhouQun(f.Id);
    }
}
