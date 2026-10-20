namespace CardGame.Core;

public sealed partial record EquipmentRecastFrame(long Id, int ActorSeat, int CardId, CardKind EffectiveKind,
    bool DrawApplied = false, int DrawCount = 0)
    : ResolutionFrame(Id, ResolutionFrameKind.EquipmentRecast, ResolutionFrameStep.ResolvingEffect);

public sealed partial class GameEngine
{
    private bool HasEquipmentRecastNativeDrawBoundary() =>
        _resolutionStack.FirstOrDefault() is EquipmentRecastFrame
        {
            DrawApplied: false,
            NativeDraw: { ActiveAttemptId: { } attemptId, AttemptReturned: false,
                ReturnKind: NativeDrawAttemptReturnKind.EquipmentRecast }
        } recast && _resolutionStack.Count > 1 &&
        _resolutionStack[1] is NativeDrawAttemptFrame attempt && attempt.Id == attemptId &&
        attempt.Context.ParentFrameId == recast.Id && attempt.Context.RecipientSeat == recast.ActorSeat &&
        attempt.Context.RequestedCount == 1 && attempt.Context.Reason == CardMoveReasons.RecastDraw &&
        attempt.Return is { Kind: NativeDrawAttemptReturnKind.EquipmentRecast, InstructionIndex: 0, RecipientIndex: 0 };

    private void BeginEquipmentRecast(CharacterState actor, Card card, CardKind effectiveKind)
    {
        var cost = FindOwnedCardLocation(actor, card);
        if (_resolutionStack.Count != 0 || cost != CardLocation.Equipment(actor.Seat) &&
            !(cost == CardLocation.Hand(actor.Seat) && (CanHulaoWeaponRecast(card) || CanHellDitingMountRecast(actor, card) ||
                IsGuanduActivity && effectiveKind == CardKind.UnitedEffort && card.Kind == CardKind.UnitedEffort)))
            throw new InvalidOperationException("Equipment recast requires its fresh play boundary and owned equipment cost.");
        var frame = new EquipmentRecastFrame(++_resolutionSequence, actor.Seat, card.Id, effectiveKind);
        frame = frame with { NativeRecastInvocation = CaptureNativeRecastInvocation(actor.Seat, actor.Seat, [card.Id], frame.Id) };
        PushRuntimeFrame(frame);
        MoveCards([card], cost, CardLocation.DiscardPile, CardMoveReasons.RecastDiscard);
        ContinueEquipmentRecast(frame.Id);
    }

    [NativeFrameAutomaticStep(NativeFrameAutomaticPhase.AfterOpening, 180, NativeFrameAutomaticPending.Ignore, NativeFrameAutomaticPublish.AfterAdvance)]
    [NativeFrameRoute(typeof(EquipmentRecastFrame), 340)]
    private void ContinueEquipmentRecast(long frameId)
    {
        while (_resolutionStack.LastOrDefault() is EquipmentRecastFrame frame && frame.Id == frameId)
        {
            if (TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.EquipmentRecast) ||
                TryBeginCardsMovedProgramWindow(frame.Id)) return;
            if (!frame.DrawApplied)
            {
                if (TryContinueEquipmentRecastNativeDraw(frame)) return;
                // The physical equipment has already left its zone. Child returns
                // resume this scalar receipt and cannot pay or draw a second time.
                ReplaceRuntimeTop(frame with { DrawApplied = true });
                var actor = _players[frame.ActorSeat];
                var drawn = _winner == Winner.None && actor.IsAlive
                    ? DrawCards(actor, 1, log: false, reason: CardMoveReasons.RecastDraw) : Array.Empty<int>();
                ReplaceRuntimeTop(((EquipmentRecastFrame)_resolutionStack.Last()) with { DrawCount = drawn.Count });
                if (_aiBrains.TryGetValue(actor.Seat, out var brain)) brain.ObserveRecast(_turnNumber, frame.CardId);
                AdvanceEventRulesAndQueueFact(new CardRecastEvent(actor.Seat, frame.CardId, frame.EffectiveKind, drawn.Count));
                CompleteNativeRecastInvocation(frame.NativeRecastInvocation, drawn.Count);
                if (frame.NativeRecastInvocation is not null)
                    ReplaceRuntimeTop(((EquipmentRecastFrame)_resolutionStack.Last()) with { NativeRecastInvocation = null });
                AddLog("Recast", $"{actor.Name} 重铸【{CardCatalog.Get(frame.EffectiveKind).DisplayName}】，摸 {drawn.Count} 张牌。", actor.Seat);
                continue;
            }
            PopResolutionFrame(frame.Id, ResolutionFrameKind.EquipmentRecast);
            return;
        }
    }
}
