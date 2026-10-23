namespace CardGame.Core;

public sealed partial class GameEngine
{
    /// <summary>One configured discard cost may include several owner zones, but it is one rules batch.</summary>
    private void MoveProgramCardsFromMultipleSources(IReadOnlyList<int> ids, CardLocation destination, CardMoveReason reason,
        Action<long, IReadOnlyList<CardMovementRecord>>? afterPhysicalRecords = null,
        Func<Card, CardLocation, CardLocation>? destinationForCard = null,
        CardMovementDisclosure disclosure = CardMovementDisclosure.Unspecified,
        Action<HappyWeiYanGainRequestSettlement>? afterRequestSettled = null,
        long? producerFrameId = null)
    {
        if (afterPhysicalRecords is not null && afterRequestSettled is not null)
            throw new InvalidOperationException("A gain request must choose one physical or request-settlement continuation.");
        var sequenceBefore = _movementSequence;
        var entries = ids.Select(id =>
        {
            var source = _cardZones.GetLocation(id);
            var card = _cardZones.CardsAt(source).Single(item => item.Id == id);
            var target = destinationForCard?.Invoke(card, source) ?? destination;
            var phantomDestination = PlanHappyZuoCiPhantomDestination(card, source, target, reason);
            if (source.Zone == CardZoneKind.Equipment && card.IsGeneralWeapon || IsGuanduThunderChariotDeparture(card, source, target)) target = CardLocation.OutsideGame;
            target = NormalizeEphemeralCardDestination(card, source, target, reason);
            target = NormalizeLishiDiscardDestination(card, source, target, reason);
            var gainDestination = PlanHappyWeiYanGainDestination(card, source, target, reason);
            target = gainDestination?.ActualTo ?? target;
            if (NormalizeHappyZuoCiPhantomDestination(card, source, target) != target)
                throw new InvalidOperationException("A second atomic destination replacement requires the phantom's original request invoice.");
            return (Card: card, Source: source, Target: target, GainDestination: gainDestination, PhantomDestination: phantomDestination);
        }).ToArray();
        if (entries.Length == 0) return;
        if (ids.Distinct().Count() != ids.Count || entries.Any(item => item.Source == item.Target &&
            (item.GainDestination is null || item.Source.Zone != CardZoneKind.Hand ||
             item.GainDestination.From != item.Source || item.GainDestination.ActualTo != item.Target)))
            throw new InvalidOperationException("An atomic card move requires distinct cards leaving their source.");
        var retainedEntries = entries.Where(item => item.Source == item.Target).ToArray();
        if (retainedEntries.Length > 0 && afterPhysicalRecords is not null)
            throw new InvalidOperationException("A replaced gain retaining its source hand requires a typed request-settlement continuation, not physical movement evidence.");
        var producerId = producerFrameId ?? _resolutionStack.LastOrDefault()?.Id;
        if ((retainedEntries.Length > 0 || afterRequestSettled is not null) && producerId is null)
            throw new InvalidOperationException("A gain request settlement requires its original active producer.");
        var retained = retainedEntries.Select(item =>
            SettleHappyWeiYanRetainedGain(item.GainDestination!, producerId)).ToArray();
        entries = entries.Where(item => item.Source != item.Target).ToArray();
        if (entries.Length == 0)
        {
            afterRequestSettled?.Invoke(new(producerId!.Value, null, sequenceBefore, reason) { RetainedGains = retained });
            return;
        }
        var shiyou = entries.Any(item => CanPlanHellDiscard(item.Target)) ? PrepareHellShiyouDiscardTransfers(entries.Select(item => new CardTransfer(item.Card.Id, item.Source, item.Target)).ToArray(), reason) : null;
        if (shiyou is not null) entries = entries.Select((item, index) => (item.Card, item.Source, Target: shiyou.Transfers[index].To, item.GainDestination, item.PhantomDestination)).ToArray();
        if (entries.Any(item => item.Source == item.Target))
            throw new InvalidOperationException("An atomic discard replacement must still leave its original source.");
        var batch = BeginCardMovementBatch(entries.Select(item => item.Source), entries.Select(item => item.Target));
        var movements = new List<CardMovementRecord>(entries.Length);
        var committed = false;
        try
        {
            // Commit all physical cards before removal hooks observe the discard cost.
            foreach (var group in entries.GroupBy(item => (item.Source, item.Target)))
                _cardZones.MoveMany(group.Select(item => item.Card.Id), group.Key.Source, group.Key.Target);
            if (afterPhysicalRecords is not null || afterRequestSettled is not null)
            {
                foreach (var item in entries)
                    movements.Add(RecordMovement(item.Card, item.Source, item.Target, reason, disclosure: disclosure,
                        happyWeiYanGainDestination: item.GainDestination, happyZuoCiPhantomDestination: item.PhantomDestination));
                if (shiyou is not null) CompleteHellShiyouDiscardTransfers(shiyou, movements);
                var records = Array.AsReadOnly(movements.ToArray());
                if (afterRequestSettled is not null)
                    afterRequestSettled(new(producerId!.Value, batch.Id, sequenceBefore, reason) { Movements = records, RetainedGains = retained });
                else afterPhysicalRecords!(batch.Id, records);
                foreach (var item in entries)
                {
                    ResolveEquipmentSkillGrant(item.Card, item.Source, item.Target);
                    ClearJudgmentEffectiveKindAfterMove(item.Card, item.Source, item.Target);
                    ResolveSilverLionRemoval(item.Card, item.Source, reason);
                    ResolveWoodenOxMove(item.Card, item.Source, item.Target);
                    CollectDiscardPhaseHandDiscard(item.Card, item.Source, item.Target);
                }
            }
            else
            foreach (var item in entries)
            {
                movements.Add(RecordMovement(item.Card, item.Source, item.Target, reason, disclosure: disclosure,
                    happyWeiYanGainDestination: item.GainDestination, happyZuoCiPhantomDestination: item.PhantomDestination));
                ResolveEquipmentSkillGrant(item.Card, item.Source, item.Target);
                ClearJudgmentEffectiveKindAfterMove(item.Card, item.Source, item.Target);
                ResolveSilverLionRemoval(item.Card, item.Source, reason);
                ResolveWoodenOxMove(item.Card, item.Source, item.Target);
                CollectDiscardPhaseHandDiscard(item.Card, item.Source, item.Target);
            }
            if (afterPhysicalRecords is null && afterRequestSettled is null && shiyou is not null) CompleteHellShiyouDiscardTransfers(shiyou, movements);
            committed = true;
        }
        finally { CompleteCardMovementBatch(batch, movements, committed); }
        foreach (var item in entries.Where(item => item.Card.Kind == CardKind.ReturnSoul)) DestroyHellReturnSoulIfDiscarded(item.Card, _cardZones.GetLocation(item.Card.Id));
    }

    private bool IsRedirectedPaidDiscardMovement(CardMovementRecord move, CardLocation expectedSource, CardMoveReason expectedReason) =>
        IsHellShiyouPaidDiscardMovement(move, expectedSource, expectedReason) ||
        IsLishiPaidDiscardMovement(move, expectedSource, expectedReason);
}
