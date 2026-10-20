using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 420)]
    private bool CanPreventSimaHuiDamage(int owner,SkillProgramTrigger trigger,ProgramSkillWindowContext context)
    {
        if(!ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.PreventDamageWithoutSealsAndArmor))return true;
        if(context.Window!=SkillProgramTriggerWindow.BeforeDamageApplied || context.TargetSeat!=owner || SimaHuiSeals.Any(m=>_players[owner].Markers.GetValueOrDefault(m)>0) || GetEquipment(_players[owner]).Any(c=>EquipmentCatalog.Get(c.Kind).Slot==EquipmentSlot.Armor) ||
            _resolutionStack.OfType<BeforeDamageProgramWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId) is not{}window || window.TargetSeat!=owner || window.Prevented || CurrentDamageAttempt is not{}attack ||
            attack.ResolutionId!=(window.ContinuationAttackResolutionId??window.ParentFrameId) || attack.TargetSeat!=owner)return false;
        return GetDamageNature(attack)!=DamageNature.Normal || attack.EffectiveCardKind is{}kind && (IsOrdinaryTrick(kind) || kind is CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning);
    }
}
