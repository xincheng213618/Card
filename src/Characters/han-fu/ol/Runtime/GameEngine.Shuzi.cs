namespace CardGame.Core;
public sealed partial class GameEngine
{
    private IReadOnlyList<HanFuMaterial> ShuziBoardCards(int target) => Array.AsReadOnly(_players.Where(p => p.IsAlive && p.Seat != target).SelectMany(p => GetEquipment(p).Concat(GetJudgment(p)))
        .Where(c => !c.IsGeneralWeapon && !IsForeignEquipmentDiscardPrevented(_cardZones.GetLocation(c.Id).OwnerSeat!.Value, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Transfer) &&
            CanMoveProgramCardToCorrespondingZone(c, _cardZones.GetLocation(c.Id).Zone, target)).Select(c => HanFuMaterialFor(c.Id)).ToArray());
    private void PayShuziBoard(ProgramSkillFrame f,HanFuMaterial m)
    {
        var r=f.HanFu!; var card=GetAdvancedCard(m.CardId); var destination=ProgramCorrespondingZoneLocation(card,m.From.Zone,r.TargetSeat); var before=_movementSequence; int? replacedId=null;
        ReplaceRuntimeTop(f=f with { HanFu=r with { Stage=HanFuStage.BoardChildren,Board=m },PendingMovementContinuation=new(f.OwnerSeat,0,null) });
        if (destination.Zone==CardZoneKind.Equipment)
        {
            var old=GetEquipment(_players[r.TargetSeat]).SingleOrDefault(c=>EquipmentCatalog.Get(c.Kind).Slot==EquipmentCatalog.Get(card.Kind).Slot); replacedId=old?.Id;
            if(old is not null) MoveCard(old,destination,CardLocation.DiscardPile,CardMoveReasons.EquipmentReplace);
        }
        MoveProgramCardsFromMultipleSources([m.CardId],destination,new(ShuziBoardReason));
        var movement=_cardMovements.Single(move=>move.Sequence>before && move.CardId==m.CardId && move.Reason.Value==ShuziBoardReason);
        AdvanceEventRulesAndQueueFact(new HanFuMaterialPaidEvent(f.Id,HanFuStage.BoardChildren,0,m,destination,movement.Sequence));
        if(destination.Zone==CardZoneKind.Equipment) AdvanceEventRulesAndQueueFact(new EquipmentChangedEvent(f.Id,r.TargetSeat,EquipmentCatalog.Get(card.Kind).Slot,card.Id,card.Kind,replacedId));
        RecordHanFuMovement(f.Id,HanFuStage.BoardChildren,ShuziBoardReason,before,1);
    }
}
