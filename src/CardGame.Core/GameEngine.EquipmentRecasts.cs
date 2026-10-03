namespace CardGame.Core;

public sealed record EquipmentRecastFrame(long Id, int ActorSeat, int CardId, CardKind EffectiveKind,
    bool DrawApplied = false, int DrawCount = 0)
    : ResolutionFrame(Id, ResolutionFrameKind.EquipmentRecast, ResolutionFrameStep.ResolvingEffect);

public sealed partial class GameEngine
{
    private void BeginEquipmentRecast(CharacterState actor, Card card, CardKind effectiveKind)
    {
        if (_resolutionStack.Count != 0 || FindOwnedCardLocation(actor, card) != CardLocation.Equipment(actor.Seat))
            throw new InvalidOperationException("Equipment recast requires its fresh play boundary and owned equipment cost.");
        var frame = new EquipmentRecastFrame(++_resolutionSequence, actor.Seat, card.Id, effectiveKind);
        PushRuntimeFrame(frame);
        MoveCards([card], CardLocation.Equipment(actor.Seat), CardLocation.DiscardPile, CardMoveReasons.RecastDiscard);
        ContinueEquipmentRecast(frame.Id);
    }

    private void ContinueEquipmentRecast(long frameId)
    {
        while (_resolutionStack.LastOrDefault() is EquipmentRecastFrame frame && frame.Id == frameId)
        {
            if (TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.EquipmentRecast) ||
                TryBeginCardsMovedProgramWindow(frame.Id)) return;
            if (!frame.DrawApplied)
            {
                // The physical equipment has already left its zone. Child returns
                // resume this scalar receipt and cannot pay or draw a second time.
                ReplaceRuntimeTop(frame with { DrawApplied = true });
                var actor = _players[frame.ActorSeat];
                var drawn = _winner == Winner.None && actor.IsAlive
                    ? DrawCards(actor, 1, log: false, reason: CardMoveReasons.RecastDraw) : Array.Empty<int>();
                ReplaceRuntimeTop(((EquipmentRecastFrame)_resolutionStack.Last()) with { DrawCount = drawn.Count });
                if (_aiBrains.TryGetValue(actor.Seat, out var brain)) brain.ObserveRecast(_turnNumber, frame.CardId);
                AdvanceEventRulesAndQueueFact(new CardRecastEvent(actor.Seat, frame.CardId, frame.EffectiveKind, drawn.Count));
                AddLog("Recast", $"{actor.Name} 重铸【铁索连环】，摸 {drawn.Count} 张牌。", actor.Seat);
                continue;
            }
            PopResolutionFrame(frame.Id, ResolutionFrameKind.EquipmentRecast);
            return;
        }
    }
}
