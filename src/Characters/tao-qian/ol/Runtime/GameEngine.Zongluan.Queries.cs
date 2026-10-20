using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int[] ZongluanTargets(int actor) => !_players[actor].IsAlive ? [] : _players.Where(p => CanUseVirtualSlashTarget(_players[actor], p, CardKind.Slash, Suit.None, countsTowardLimit: false, effectiveColor: null)).Select(p => p.Seat).ToArray();
    private IReadOnlyList<PromptChoice> ZongluanDiscardCards(ProgramSkillFrame f) => BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, f.OwnerSeat, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard, canSelect: (_, card) => !f.TaoQian!.Selected.Contains(card.Id));
    private void PayZongluanDiscard(ProgramSkillFrame f)
    {
        var r = f.TaoQian!;
        if (!_players[f.OwnerSeat].IsAlive) { FinishTaoQian(f); return; }
        var legal = BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, f.OwnerSeat, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard).SelectMany(c => c.Cards).ToHashSet();
        if (r.Selected.Count != r.DiscardRequired || r.Selected.Distinct().Count() != r.Selected.Count || r.Selected.Any(id => !legal.Contains(id))) throw new InvalidOperationException("Zongluan requires exactly its published real legal HE discard entities.");
        var materials = r.Selected.Select(id => new CardActionCost(id, GetAdvancedCard(id).Kind, _cardZones.GetLocation(id))).ToArray();
        ReplaceRuntimeTop(f = f with { TaoQian = r with { Materials = materials, Before = _movementSequence, After = _movementSequence, Stage = TaoQianStage.DiscardChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramLogicalDiscard(f, f.OwnerSeat, r.Selected, new(ZongluanDiscardReason), (batch, records) =>
        { for (var i = 0; i < records.Count; i++) { var m = records[i]; AdvanceEventRulesAndQueueFact(new ZongluanDiscardEntityEvent(f.Id, i, batch, m.CardId, m.CardKind, m.From, m.To, m.Sequence)); } });
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { TaoQian = f.TaoQian! with { After = _movementSequence } }); ContinueTaoQian(f.Id);
    }
}
