namespace CardGame.Core;

public sealed partial class GameEngine
{
    /// <summary>One configured discard cost may include several owner zones, but it is one rules batch.</summary>
    private void MoveProgramCardsFromMultipleSources(IReadOnlyList<int> ids, CardLocation destination, CardMoveReason reason,
        Action<long, IReadOnlyList<CardMovementRecord>>? afterPhysicalRecords = null,
        Func<Card, CardLocation, CardLocation>? destinationForCard = null,
        CardMovementDisclosure disclosure = CardMovementDisclosure.Unspecified)
    {
        var entries = ids.Select(id =>
        {
            var source = _cardZones.GetLocation(id);
            var card = _cardZones.CardsAt(source).Single(item => item.Id == id);
            var target = destinationForCard?.Invoke(card, source) ?? destination;
            if (source.Zone == CardZoneKind.Equipment && card.IsGeneralWeapon || IsGuanduThunderChariotDeparture(card, source, target)) target = CardLocation.OutsideGame;
            return (Card: card, Source: source, Target: target);
        }).ToArray();
        if (entries.Length == 0) return;
        var shiyou = entries.Any(item => CanPlanHellDiscard(item.Target)) ? PrepareHellShiyouDiscardTransfers(entries.Select(item => new CardTransfer(item.Card.Id, item.Source, item.Target)).ToArray(), reason) : null;
        if (shiyou is not null) entries = entries.Select((item, index) => (item.Card, item.Source, Target: shiyou.Transfers[index].To)).ToArray();
        if (ids.Distinct().Count() != ids.Count || entries.Any(item => item.Source == item.Target))
            throw new InvalidOperationException("An atomic card move requires distinct cards leaving their source.");
        var batch = BeginCardMovementBatch(entries.Select(item => item.Source), entries.Select(item => item.Target));
        var movements = new List<CardMovementRecord>(entries.Length);
        var committed = false;
        try
        {
            // Commit all physical cards before removal hooks observe the discard cost.
            foreach (var group in entries.GroupBy(item => (item.Source, item.Target)))
                _cardZones.MoveMany(group.Select(item => item.Card.Id), group.Key.Source, group.Key.Target);
            if (afterPhysicalRecords is not null)
            {
                foreach (var item in entries)
                    movements.Add(RecordMovement(item.Card, item.Source, item.Target, reason, disclosure: disclosure));
                if (shiyou is not null) CompleteHellShiyouDiscardTransfers(shiyou, movements);
                afterPhysicalRecords(batch.Id, Array.AsReadOnly(movements.ToArray()));
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
                movements.Add(RecordMovement(item.Card, item.Source, item.Target, reason, disclosure: disclosure));
                ResolveEquipmentSkillGrant(item.Card, item.Source, item.Target);
                ClearJudgmentEffectiveKindAfterMove(item.Card, item.Source, item.Target);
                ResolveSilverLionRemoval(item.Card, item.Source, reason);
                ResolveWoodenOxMove(item.Card, item.Source, item.Target);
                CollectDiscardPhaseHandDiscard(item.Card, item.Source, item.Target);
            }
            if (afterPhysicalRecords is null && shiyou is not null) CompleteHellShiyouDiscardTransfers(shiyou, movements);
            committed = true;
        }
        finally { CompleteCardMovementBatch(batch, movements, committed); }
        foreach (var item in entries.Where(item => item.Card.Kind == CardKind.ReturnSoul)) DestroyHellReturnSoulIfDiscarded(item.Card, _cardZones.GetLocation(item.Card.Id));
    }
}
