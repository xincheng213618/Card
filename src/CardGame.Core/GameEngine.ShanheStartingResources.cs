namespace CardGame.Core;

// Trusted-host provenance. Player snapshots and the LAN public-event whitelist
// never include these hand identities or the campaign recipe index.
public sealed record ShanheStartingResourceCreatedEvent(int CardId, bool EquipmentResource, int RecipeIndex) : IGameEvent;
public sealed record ShanheStartingResourceUseEnteredEvent(int CardId, long UseFrameId, long ActionId, int EntrySequence) : IGameEvent;
public sealed record ShanheStartingResourceDestroyedEvent(int CardId, int MovementSequence, int EntrySequence,
    long ParentFrameId, long ActionId) : IGameEvent;

public sealed partial class GameEngine
{
    private void PublishShanheStartingResourceOrigins()
    {
        if (_options.ShanheBattle is not { StartingResourcesExpireOnUse: true } setup ||
            ProgramEventHistory<ShanheStartingResourceCreatedEvent>().Count != 0) return;
        // Noninteractive initial dealing runs in the constructor, before the
        // event journal is open. Bind those exact generated initial-deal moves
        // when Start opens it, rather than keeping an uncommitted side ledger.
        var entries = _cardMovements.Where(m => m.From == CardLocation.OutsideGame && m.To == CardLocation.Hand(0) &&
            m.Reason == CardMoveReasons.InitialDeal && _generatedPhysicalCardIds.Contains(m.CardId)).OrderBy(m => m.Sequence).ToArray();
        var recipe = setup.StartingCards.Concat(setup.StartingEquipment).ToArray();
        if (entries.Length != recipe.Length) throw new InvalidOperationException("The starting resource recipe lost its exact generated initial-deal movements.");
        for (var index = 0; index < entries.Length; index++)
        {
            var card = GetAdvancedCard(entries[index].CardId); var expected = recipe[index];
            if (card.Kind != expected.Kind || card.Suit != expected.Suit || card.Rank != expected.Rank)
                throw new InvalidOperationException("The starting resource initial-deal order no longer matches its original recipe.");
            var equipment = index >= setup.StartingCards.Count;
            AdvanceEventRulesAndQueueFact(new ShanheStartingResourceCreatedEvent(card.Id, equipment,
                equipment ? index - setup.StartingCards.Count : index));
        }
    }

    private bool IsShanheStartingResource(int id)
    {
        if (_options.ShanheBattle is not { StartingResourcesExpireOnUse: true } setup || !_generatedPhysicalCardIds.Contains(id) ||
            ProgramEventHistory<ShanheStartingResourceCreatedEvent>().Where(e => e.CardId == id).ToArray() is not [var origin]) return false;
        var recipe = origin.EquipmentResource ? setup.StartingEquipment : setup.StartingCards;
        if (origin.RecipeIndex < 0 || origin.RecipeIndex >= recipe.Count) return false;
        var item = recipe[origin.RecipeIndex]; var card = GetAdvancedCard(id);
        return card.Kind == item.Kind && card.Suit == item.Suit && card.Rank == item.Rank;
    }

    private bool ShanheStartingResourceWasAcquired(int id, int before = int.MaxValue) => _cardMovements.Any(m =>
        m.CardId == id && m.Sequence < before && m.To.Zone == CardZoneKind.Hand && m.From != m.To &&
        m.Reason != CardMoveReasons.InitialDeal && m.Reason != CardMoveReasons.Draw && m.Reason != CardMoveReasons.RecastDraw);

    private void CaptureShanheClaimedDeclarationUse(DeclaredCardPayment paid)
    {
        if (!IsShanheStartingResource(paid.Cost.CardId) || ShanheStartingResourceWasAcquired(paid.Cost.CardId) ||
            _resolutionStack.LastOrDefault() is not CardUseFrame { Action: { Type: CardActionType.Use } action } use ||
            _resolutionStack.SingleOrDefault(f => f.Id == paid.OwnerFrameId)?.AcceptedDeclarationPayment is not { Claimed: true } invoice ||
            invoice != paid with { Claimed = true } || !action.PhysicalCards.Contains(paid.Cost) || !action.ConversionChain.Contains(paid.Source) ||
            _cardMovements.LastOrDefault(m => m.CardId == paid.Cost.CardId) is not { To.Zone: CardZoneKind.Processing } entry ||
            entry.From != paid.Cost.From || entry.Reason.Value != "conversion.declaration.pay") return;
        AdvanceEventRulesAndQueueFact(new ShanheStartingResourceUseEnteredEvent(entry.CardId, use.Id, action.ActionId, entry.Sequence));
    }

    private void CaptureShanheStartingResourceUseEntries(CardMovementBatchContext batch)
    {
        if (_options.ShanheBattle is not { StartingResourcesExpireOnUse: true } || batch.ParentFrameId is not { } parent ||
            LifecycleCardUse(parent) is not { Action: { Type: CardActionType.Use } action } use) return;
        foreach (var move in batch.Movements)
        {
            if (!IsShanheStartingResource(move.CardId) || ShanheStartingResourceWasAcquired(move.CardId, move.Sequence) ||
                move.To != CardLocation.Processing || move.Reason != CardMoveReasons.Use && move.Reason != CardMoveReasons.IronChainUse && move.Reason != CardMoveReasons.EquipmentUse ||
                !action.PhysicalCards.Any(c => c.CardId == move.CardId && c.CardKind == move.CardKind && c.From == move.From)) continue;
            AdvanceEventRulesAndQueueFact(new ShanheStartingResourceUseEnteredEvent(move.CardId, use.Id, action.ActionId, move.Sequence));
        }
    }

    private bool TryDescribeShanheResourceExit(int cardId, CardLocation from, CardMoveReason reason, int before,
        out int entrySequence, out long parentFrame, out long actionId)
    {
        entrySequence = 0; parentFrame = 0; actionId = 0;
        if (!IsShanheStartingResource(cardId) || ShanheStartingResourceWasAcquired(cardId, before)) return false;
        var used = ProgramEventHistory<ShanheStartingResourceUseEnteredEvent>().LastOrDefault(e => e.CardId == cardId && e.EntrySequence < before);
        if (from.Zone is CardZoneKind.Equipment or CardZoneKind.Judgment && used is not null)
        { entrySequence = used.EntrySequence; parentFrame = used.UseFrameId; actionId = used.ActionId; return true; }
        if (from != CardLocation.Processing || _cardMovements.LastOrDefault(m => m.CardId == cardId && m.Sequence < before) is not { To.Zone: CardZoneKind.Processing } entry)
            return false;
        if (used is not null && used.EntrySequence == entry.Sequence && (IsNativeUseFinishReason(reason) || reason.Value == "area.abolished.equipment") &&
            LifecycleCardUse(used.UseFrameId) is { Action: { } action } && action.ActionId == used.ActionId &&
            action.PhysicalCards.Any(c => c.CardId == cardId && c.From == entry.From && c.CardKind == entry.CardKind))
        { entrySequence = entry.Sequence; parentFrame = used.UseFrameId; actionId = used.ActionId; return true; }
        if (used is not null && entry.From.Zone is CardZoneKind.Equipment or CardZoneKind.Judgment &&
            (reason == CardMoveReasons.DismantlementFinished || reason == CardMoveReasons.DismantlementJudgmentFinished))
        { entrySequence = used.EntrySequence; parentFrame = used.UseFrameId; actionId = used.ActionId; return true; }
        if (reason != CardMoveReasons.ResponseFinished && reason != CardMoveReasons.NullificationFinished) return false;
        var current = _resolutionStack.LastOrDefault();
        var response = ProgramEventHistory<CardActionAcceptedEvent>().Select(e => e.Action).LastOrDefault(a =>
            a.Type == CardActionType.Response && a.PhysicalCards.Any(c => c.CardId == cardId && c.From == entry.From && c.CardKind == entry.CardKind) &&
            (current is CardUseFrame incoming && a.ParentActionId == incoming.Action?.ActionId ||
             current is ProgramSkillFrame program && IsResponseCompletionProgramDuelParent(program, ProgramCardContinuation.DuelSlash) ||
             current is NullificationWindowFrame counter && a.ParentActionId == NullificationParentActionId(counter)));
        var claimedDeclaration = entry.Reason.Value == "conversion.declaration.pay" && current?.AcceptedDeclarationPayment is { Claimed: true } paid &&
            paid.OwnerFrameId == current.Id && paid.Cost.CardId == cardId && paid.Cost.CardKind == entry.CardKind && paid.Cost.From == entry.From;
        if (response is not null && (entry.Reason == CardMoveReasons.Respond || entry.Reason == CardMoveReasons.Nullification ||
            claimedDeclaration && current!.AcceptedDeclarationPayment is { } responsePaid && response.ConversionChain.Contains(responsePaid.Source)))
        { entrySequence = entry.Sequence; parentFrame = current!.Id; actionId = response.ActionId; return true; }
        // The single native counterspell records its accepted response after its
        // physical finish. Its exact current cursor owns both movements already.
        if (current is NullificationWindowFrame n && (entry.Reason == CardMoveReasons.Nullification || claimedDeclaration && n.AcceptedDeclarationPayment!.DeclaredKind == CardKind.Nullification) && reason == CardMoveReasons.NullificationFinished &&
            entry.From.OwnerSeat is { } provider && n.CandidateIndex >= 0 && n.CandidateIndex < n.CandidateSeats.Count && n.CandidateSeats[n.CandidateIndex] == provider &&
            HasNullificationEffectOwner(n))
        { entrySequence = entry.Sequence; parentFrame = n.Id; return true; }
        return false;
    }

    private CardLocation NormalizeShanheStartingResourceDestination(Card card, CardLocation from, CardLocation to, CardMoveReason? reason)
    {
        if (!IsShanheStartingResource(card.Id)) return to;
        if (from == CardLocation.OutsideGame && ProgramEventHistory<ShanheStartingResourceDestroyedEvent>().Any(e => e.CardId == card.Id)) return from;
        if (to != CardLocation.DiscardPile) return to;
        // Pure proof callers omit a reason only when forecasting a continuing
        // field departure. Processing exits always require the actual finish.
        return (reason is { } actual && TryDescribeShanheResourceExit(card.Id, from, actual, int.MaxValue, out _, out _, out _) ||
            reason is null && from.Zone is CardZoneKind.Equipment or CardZoneKind.Judgment && !ShanheStartingResourceWasAcquired(card.Id) &&
                ProgramEventHistory<ShanheStartingResourceUseEnteredEvent>().Any(e => e.CardId == card.Id)) ? CardLocation.OutsideGame : to;
    }

    private void RecordShanheStartingResourceDestruction(CardMovementRecord move)
    {
        if (move.To != CardLocation.OutsideGame || !IsShanheStartingResource(move.CardId)) return;
        if (!TryDescribeShanheResourceExit(move.CardId, move.From, move.Reason, move.Sequence, out var entry, out var parent, out var action)) return;
        if (ProgramEventHistory<ShanheStartingResourceDestroyedEvent>().Any(e => e.CardId == move.CardId))
            throw new InvalidOperationException("A starting resource exit needs its exact native use/response or continuing field provenance.");
        AdvanceEventRulesAndQueueFact(new ShanheStartingResourceDestroyedEvent(move.CardId, move.Sequence, entry, parent, action));
    }

    private bool IsShanheStartingResourceDestroyedMaterial(CardMovementRecord move) =>
        move.To == CardLocation.OutsideGame && IsShanheStartingResource(move.CardId) && _cardMovements.Contains(move) &&
        ProgramEventHistory<ShanheStartingResourceDestroyedEvent>().Where(e => e.CardId == move.CardId).ToArray() is [var fact] &&
        fact.MovementSequence == move.Sequence && !ShanheStartingResourceWasAcquired(move.CardId, move.Sequence) &&
        _cardMovements.SingleOrDefault(m => m.Sequence == fact.EntrySequence) is { } entry && entry.CardId == move.CardId && entry.To == CardLocation.Processing &&
        (ProgramEventHistory<ShanheStartingResourceUseEnteredEvent>().Any(e => e.CardId == move.CardId && e.EntrySequence == entry.Sequence &&
             e.UseFrameId == fact.ParentFrameId && e.ActionId == fact.ActionId) ||
         move.From == CardLocation.Processing && entry.Sequence < move.Sequence &&
         (entry.Reason == CardMoveReasons.Respond || entry.Reason == CardMoveReasons.Nullification || entry.Reason.Value == "conversion.declaration.pay") &&
         (move.Reason == CardMoveReasons.ResponseFinished || move.Reason == CardMoveReasons.NullificationFinished));

    private bool IsShanheStartingResourceResponseFinish(CardActionContext action, CardActionCost cost, CardMovementRecord finish) =>
        action.Type == CardActionType.Response && action.PhysicalCards.Contains(cost) && finish.CardId == cost.CardId && finish.CardKind == cost.CardKind &&
        IsShanheStartingResourceDestroyedMaterial(finish) &&
        ProgramEventHistory<ShanheStartingResourceDestroyedEvent>().Single(e => e.CardId == cost.CardId) is { } fact &&
        (fact.ActionId == 0 || fact.ActionId == action.ActionId) && _cardMovements.Single(m => m.Sequence == fact.EntrySequence) is { } entry &&
        entry.From == cost.From && entry.Sequence < finish.Sequence;

    private bool IsShanheStartingResourceDestroyedUseMaterial(long useId, int cardId) =>
        ProgramEventHistory<ShanheStartingResourceDestroyedEvent>().Any(e => e.CardId == cardId && e.ParentFrameId == useId &&
            _cardMovements.SingleOrDefault(m => m.Sequence == e.MovementSequence) is { } m && IsShanheStartingResourceDestroyedMaterial(m));
}
