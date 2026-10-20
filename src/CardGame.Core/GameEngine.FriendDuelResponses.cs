using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<PromptChoice> FriendDuelResponseChoices(DuelHandle duel, CharacterState responder)
    {
        var use = LifecycleCardUse(duel.ResolutionId);
        if (use?.FriendRecastDuel is not { } origin || !IsFriendRecastDuelUse(use.Id) ||
            use.FriendRecastDuelResponses >= origin.RecastCount || responder.Seat != duel.ResponderSeat ||
            IsCardUseForbidden(responder.Seat, CardKind.Slash, CardActionType.Response)) return [];
        var choices = new List<PromptChoice>();
        foreach (var zone in new[] { CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment })
        {
            var location = new CardLocation(zone, responder.Seat);
            var cards = _cardZones.CardsAt(location);
            for (var slot = 0; slot < cards.Count; slot++)
            {
                var card = cards[slot];
                // The shared discard query also enforces self hand-category restrictions.
                if (card.IsGeneralWeapon || !FriendDuelCategoryAllowed(origin.OwnerSeat, card.Kind) ||
                    IsForeignEquipmentDiscardPrevented(responder.Seat, card, location, OwnedCardMoveIntent.Discard)) continue;
                choices.Add(new PromptChoice(new($"friend-duel.use-{use.Id}.zone-{zone}.slot-{slot}"),
                    $"钦英：弃置【{card.DisplayName}】，视为打出普通杀。", [card.Id], [],
                    new Dictionary<string, string>
                    {
                        ["response"] = "friend-duel-discard", ["frame-id"] = use.Id.ToString(CultureInfo.InvariantCulture),
                        ["card-owner-seat"] = responder.Seat.ToString(CultureInfo.InvariantCulture),
                        ["source-zone"] = zone.ToString(), ["slot-index"] = slot.ToString(CultureInfo.InvariantCulture)
                    }));
            }
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void ResolveFriendDuelDiscard(PromptChoice supplied)
    {
        var duel = ActiveDuel ?? throw new InvalidOperationException("Recast Duel payment lost its active Duel.");
        var responder = _players[duel.ResponderSeat];
        var canonical = FriendDuelResponseChoices(duel, responder).SingleOrDefault(c => c.Id == supplied.Id);
        if (_pendingDecision is not { Kind: DecisionKind.RespondSlash } prompt || prompt.PlayerSeat != responder.Seat ||
            canonical is null || !AssistedChoicesEqual([canonical], [supplied]) || supplied.Cards is not [var id])
            throw new InvalidOperationException("Recast Duel accepts only its exact published current discard.");
        var use = LifecycleCardUse(duel.ResolutionId)!;
        var from = _cardZones.GetLocation(id);
        var card = GetAttackCard(id);
        if (from.OwnerSeat != responder.Seat || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment))
            throw new InvalidOperationException("Recast Duel cannot substitute another owner's card.");
        PopResponseWindow(use.Id);
        SetResponseParentStep(use.Id, ResolutionFrameStep.ResolvingEffect);
        ClearPendingDecision();
        var frame = new FriendDuelResponseFrame(++_resolutionSequence, use.Id, responder.Seat, duel.OpponentSeat,
            id, card.Kind, from, use.FriendRecastDuelResponses, use.FriendRecastDuel!);
        PushRuntimeFrame(frame);
        UpdateLifecycleCardUse(use.Id, parent => parent with { FriendRecastDuelResponses = parent.FriendRecastDuelResponses + 1 });
        MoveLogicalDiscard(frame.Origin.OwnerSeat, responder.Seat, card, from, new("skill.friend-qinying.response-cost"));
        AdvanceEventRulesAndQueueFact(new FriendDuelDiscardPaidEvent(use.Id, responder.Seat, id, from, frame.PreviousResponses + 1));
        ContinueFriendDuelResponse(frame.Id);
    }
    private bool IsFriendResponseParent(FriendDuelResponseFrame f) => ActiveDuel is { } duel && duel.ResolutionId == f.CardUseFrameId &&
        duel.ResponderSeat == f.ResponderSeat && duel.OpponentSeat == f.OpponentSeat && LifecycleCardUse(f.CardUseFrameId) is { } use &&
        use.FriendRecastDuel == f.Origin && use.FriendRecastDuelResponses == f.PreviousResponses + 1 &&
        f.PreviousResponses >= 0 && use.FriendRecastDuelResponses <= f.Origin.RecastCount && IsFriendRecastDuelUse(use.Id);
    [NativeFrameAutomaticStep(NativeFrameAutomaticPhase.AfterOpening, 70, NativeFrameAutomaticPending.RequireNoneBeforeFrame, NativeFrameAutomaticPublish.AfterAdvance)]
    [NativeFrameRoute(typeof(FriendDuelResponseFrame), 100)]
    private void ContinueFriendDuelResponse(long id)
    {
        if (_resolutionStack.LastOrDefault() is not FriendDuelResponseFrame f || f.Id != id || !IsFriendResponseParent(f))
            throw new InvalidOperationException("Paid Recast Duel response lost its original typed owner.");
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.FriendDuelResponse) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.FriendDuelResponse) || TryBeginCardsMovedProgramWindow(id)) return;
        if (CompleteProgramEventHistory().OfType<FriendDuelDiscardPaidEvent>().Count(e => e.CardUseFrameId == f.CardUseFrameId &&
            e.ResponderSeat == f.ResponderSeat && e.CardId == f.CardId && e.ResponseCount == f.PreviousResponses + 1) != 1 ||
            !_cardMovements.Any(m => m.CardId == f.CardId && m.From == f.From && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill.friend-qinying.response-cost"))
            throw new InvalidOperationException("Recast Duel response lost its actual paid discard.");
        PopResolutionFrame(id, ResolutionFrameKind.FriendDuelResponse);
        var duel = ActiveDuel!; var responder = _players[f.ResponderSeat];
        if (!responder.IsAlive || !_players[f.OpponentSeat].IsAlive) { CompleteAttack(duel.Attack); return; }
        AdvanceEventRulesAndQueueFact(new DuelResponseEvent(f.CardUseFrameId, f.ResponderSeat, true, null, CardKind.Slash));
        // The discarded cost is not the material of the virtual Slash.
        if (!TryBeginCardResponsePrograms(duel.Attack, responder, responder, null, f.OpponentSeat, CardKind.Slash, [], ProgramCardContinuation.DuelSlash))
            ContinueDuelAfterSuccessfulSlash(duel, f.ResponderSeat);
    }
    private bool IsFriendDuelResponseMovementReturn(FriendDuelResponseFrame parent, CardsMovedTriggerWindowFrame movement) =>
        movement.ResumeFriendDuelResponseFrameId == parent.Id && movement.Batch.ParentFrameId == parent.Id &&
        movement.Batch.AwaitingProgramFrameId is null && IsFriendResponseParent(parent);
    private bool HasFriendDuelResponseObserver(long useId) => _resolutionStack.OfType<FriendDuelResponseFrame>()
        .Any(f => f.CardUseFrameId == useId && IsFriendResponseParent(f));
    private void AssertFriendEventFrames()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(f => f.FriendRecastDuel is not null))
            if (!IsFriendRecastDuelUse(use.Id) || use.FriendRecastDuelResponses < 0 || use.FriendRecastDuelResponses > use.FriendRecastDuel!.RecastCount)
                throw new InvalidOperationException("Recast Duel lost its budget or exact paid parent.");
        foreach (var f in _resolutionStack.OfType<FriendDuelResponseFrame>())
        {
            var index = _resolutionStack.FindIndex(frame => frame.Id == f.Id);
            if (index < 1 || _resolutionStack[index - 1] is not CardUseFrame use || use.Id != f.CardUseFrameId || !IsFriendResponseParent(f))
                throw new InvalidOperationException("Recast Duel response lost its original Use frame.");
        }
    }
}
