using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void PayDiciGift(ProgramSkillFrame f, int cardId, bool next)
    {
        var r = f.ZhaoZhong!; var from = CardLocation.Hand(r.TargetSeat); var card = _cardZones.CardsAt(from).Single(c => c.Id == cardId);
        if (!_players[r.TargetSeat].IsAlive || !_players[f.OwnerSeat].IsAlive || r.Stage != (next ? ZhaoZhongStage.NextChoice : ZhaoZhongStage.InitialGive)) throw new InvalidOperationException("Dici cannot give another person's card or give to a dead recipient.");
        if (!next)
        {
            if (r.Paid || r.Phase is not { } phase || ProgramEventHistory<DiciAcceptedEvent>().Any(e => e.Source.OwnerSeat == f.OwnerSeat && e.Phase == phase)) throw new InvalidOperationException("Dici repeated its actual Play-phase payment.");
            AdvanceEventRulesAndQueueFact(new DiciAcceptedEvent(f.Id, r.Source, r.GameplayHash, r.TargetSeat, phase));
        }
        else AdvanceEventRulesAndQueueFact(new DiciNextChoiceEvent(f.Id, r.LeaseId, "give"));
        ReplaceRuntimeTop(f = f with { ZhaoZhong = r with { Paid = true, CardId = card.Id, CardKind = card.Kind, From = from, Before = _movementSequence, After = _movementSequence, Stage = next ? ZhaoZhongStage.NextGiftChildren : ZhaoZhongStage.GiftChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveCard(card, from, CardLocation.Hand(f.OwnerSeat), new(next ? DiciNextGiveReason : DiciGiveReason), NativeGiveCompletion(f.Id,from.OwnerSeat!.Value,f.OwnerSeat,[card.Id],_movementSequence,m => AdvanceEventRulesAndQueueFact(new DiciGiftEntityEvent(f.Id, r.LeaseId, m.CardId, m.CardKind, m.From, m.To, m.Sequence))));
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { ZhaoZhong = f.ZhaoZhong! with { After = _movementSequence } }); ContinueZhaoZhong(f.Id);
    }
    private int[] DiciNeighbors(int target)
    {
        var living = CurrentTableOrder().Where(seat => _players[seat].IsAlive).ToArray(); var index = Array.IndexOf(living, target);
        return index < 0 || living.Length < 2 ? [] : new[] { living[(index + 1) % living.Length], living[(index + living.Length - 1) % living.Length] }.Distinct().ToArray();
    }
}
