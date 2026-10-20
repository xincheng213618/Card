using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private QuHuangEntity[] QiejianFieldCards(ProgramSkillFrame f)=>new[]{f.OwnerSeat,f.QuHuang!.TargetSeat}.Distinct().Where(s=>_players[s].IsAlive).SelectMany(s=>new[]{CardLocation.Equipment(s),CardLocation.Judgment(s)}.SelectMany(l=>_cardZones.CardsAt(l).Where(c=>!IsForeignEquipmentDiscardPrevented(f.OwnerSeat,c,l,OwnedCardMoveIntent.Discard)&&!IsActiveProgramSourceEquipmentCard(f.OwnerSeat,f.SkillId,f.SkillInstanceId,c)).Select(c=>new QuHuangEntity(c.Id,c.Kind,l)))).ToArray();
}
