namespace CardGame.Core;
public sealed partial class GameEngine
{
 private IReadOnlyList<LegalAction> SidaoActions(int owner,GuoSiPair pair)
 {
  if(!_players[owner].IsAlive)return [];var result=new List<LegalAction>();var actor=_players[owner];
  foreach(var card in GetHand(actor).Where(c=>!IsTurnHandCardRestricted(actor,c)&&!IsTurnPhysicalUseForbidden(owner,[c.Id])))
  {
   var suit=GetProgramEffectiveSuit(actor,card);
   foreach(var o in BuildProgramOrdinaryTrickUseOptions(actor,CardKind.Snatch,suit,enforceUsePermission:true,beneficiaryShieldSuit:suit,actualEffectiveColor:SuitColor(suit),hasActualColor:true,physicalCardIds:[card.Id],includeNextActualUseAdjustment:false))
    if(o.TargetSeats.Count==1&&pair.CommonTargets.Contains(o.TargetSeats[0])&&_players[o.TargetSeats[0]].IsAlive&&!IsDirectedCardTargetProhibited(owner,o.TargetSeats[0],CardKind.Snatch))result.Add(new(o.ActionKind,card.Id,o.TargetSeats[0],$"将【{card.DisplayName}】当【顺手牵羊】对{_players[o.TargetSeats[0]].Name}使用",CardKind.Snatch,o.TargetCardId,Array.AsReadOnly(o.TargetSeats.ToArray())));
  }
  return Array.AsReadOnly(result.ToArray());
 }
}
