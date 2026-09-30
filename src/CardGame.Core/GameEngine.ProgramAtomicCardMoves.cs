namespace CardGame.Core;

public sealed partial class GameEngine
{
    /// <summary>One configured discard cost may include several owner zones, but it is one rules batch.</summary>
    private void MoveProgramCardsFromMultipleSources(IReadOnlyList<int> ids, CardLocation destination, CardMoveReason reason)
    {
        var entries = ids.Select(id =>
        {
            var source = _cardZones.GetLocation(id);
            var card = _cardZones.CardsAt(source).Single(item => item.Id == id);
            var target = source.Zone == CardZoneKind.Equipment && card.IsGeneralWeapon ? CardLocation.OutsideGame : destination;
            return (Card: card, Source: source, Target: target);
        }).ToArray();
        if (entries.Length == 0) return;
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
            foreach (var item in entries)
            {
                movements.Add(RecordMovement(item.Card, item.Source, item.Target, reason));
                ResolveEquipmentSkillGrant(item.Card, item.Source, item.Target);
                ClearJudgmentEffectiveKindAfterMove(item.Card, item.Source, item.Target);
                ResolveSilverLionRemoval(item.Card, item.Source, reason);
                ResolveWoodenOxMove(item.Card, item.Source, item.Target);
                CollectDiscardPhaseHandDiscard(item.Card, item.Source, item.Target);
            }
            committed = true;
        }
        finally { CompleteCardMovementBatch(batch, movements, committed); }
    }
}
