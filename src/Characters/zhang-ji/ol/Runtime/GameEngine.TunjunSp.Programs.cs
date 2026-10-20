using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
 private void IssueZhangJiEquipment(ProgramSkillFrame f)
 {
  var r=f.ZhangJi!;var target=_players[r.Target];
  if(r.Slots.Count>=r.HistoricalCount || !target.IsAlive || _winner!=Winner.None){FinishZhangJi(f);return;}
  var candidates=_cardZones.CardsAt(CardLocation.DrawPile).Where(c=>EquipmentCatalog.IsEquipment(c.Kind) && !r.Slots.Contains(EquipmentCatalog.Get(c.Kind).Slot) && target.EquipmentSlotCapacity(EquipmentCatalog.Get(c.Kind).Slot)>0 &&
   !IsCardUseForbidden(target.Seat,c.Kind,CardActionType.Use) && !IsSelfTargetForbiddenAction(target,c.Kind,[target.Seat]) && !IsResponseEntityRestricted(target.Seat,c.Id) && !IsPlayPhasePhysicalCardRestricted(target,c)).OrderBy(c=>c.Id).ToArray();
  if(candidates.Length==0){FinishZhangJi(f);return;}
  var card=candidates[_random.Next(candidates.Length)];var slot=EquipmentCatalog.Get(card.Kind).Slot;
  ReplaceRuntimeTop(f=f with{ZhangJi=r with{Stage="issuing-equipment",NativeUse=null,NativeReturned=false,Slots=r.Slots.Append(slot).ToArray()}});
  var id=BeginCardUse(card,target.Seat,[],conversionSource:r.Source);MoveCard(card,CardLocation.DrawPile,CardLocation.Processing,CardMoveReasons.EquipmentUse);
  if(!TryBeginEquipmentTargetPrograms(target,card,id))CompleteEquipmentUse(target,card,id);
 }
}
