namespace CardGame.Core;
internal sealed partial class MouJuShouRuntime
{
 internal void ObserveMovement(CardMovementRecord m)
 {
  if(m.From==m.To)return;
  if(m.From.Zone==CardZoneKind.Hand&&m.From.OwnerSeat is{}holder)
  {
   var old=facts.Grants(holder);if(old.Any(g=>g.CardId==m.CardId))native.SetGrants(holder,old.Where(g=>g.CardId!=m.CardId).ToArray());
  }
 }
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,36900)]private void ObserveTurn(IGameEvent payload)
 {
  if(payload is TurnEndedEvent end)
  foreach(var seat in facts.Seats)
  {
   var grants=facts.Grants(seat);var keep=grants.Where(g=>g.Recipient!=end.ActorSeat||end.TurnNumber<=g.GrantedTurn).ToArray();if(keep.Length!=grants.Count)native.SetGrants(seat,keep);
  }
  if(payload is TurnStartedEvent start)foreach(var seat in facts.Seats)if(facts.Losses(seat).Any(l=>l.Turn!=start.TurnNumber))native.SetLosses(seat,facts.Losses(seat).Where(l=>l.Turn==start.TurnNumber).ToArray());
 }
 internal void ObserveUse(int actor,IReadOnlyList<int> materials)
 {
  foreach(var f in frames.OfType<ProgramSkillFrame>().Where(f=>f.MouJuShou is{Operation:SkillProgramEffectOp.ResolveMouJuShouZuoJun}r&&r.Actor==actor&&r.Bound.Intersect(materials).Any()).ToArray())frames.Replace(f.Id,f with{MouJuShou=f.MouJuShou! with{Bound=f.MouJuShou.Bound.Except(materials).ToArray()}});
 }
}
