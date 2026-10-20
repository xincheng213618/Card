namespace CardGame.Core;
public sealed partial class GameEngine
{
 private int FaluTokenCount(int owner)=>FaluTokens.Count(m=>_players[owner].Markers.GetValueOrDefault(m)>0);
 private CardLocation? FaluDiscardOwnedSource(CardMovementRecord movement)
 {
  if(movement.To!=CardLocation.DiscardPile)return null;
  var origin=GetProgramDiscardSource(movement);
  if(origin is{OwnerSeat:not null,Zone:CardZoneKind.Hand or CardZoneKind.Equipment})return origin;
  return movement.From is{OwnerSeat:not null,Zone:CardZoneKind.WoodenOxGrain}&&IsDiscardMovementReason(movement.Reason)?movement.From:null;
 }
 private void SetFaluToken(ProgramSkillFrame f,PlayerMarkerKind marker,int value,string reason)
 {
  var owner=_players[f.OwnerSeat];var before=owner.Markers.GetValueOrDefault(marker);if(value is <0 or >1||before is <0 or >1)throw new InvalidOperationException("Falu tokens are binary.");
  if(before==value)return;if(value==0)owner.Markers.Remove(marker);else owner.Markers[marker]=value;
  AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(f.Id,f.OwnerSeat,marker,value-before,value,f.OwnerSeat,"falu."+reason));
  AdvanceEventRulesAndQueueFact(new FaluTokenChangedEvent(f.Id,f.OwnerSeat,marker,before,value,reason));
 }
}
