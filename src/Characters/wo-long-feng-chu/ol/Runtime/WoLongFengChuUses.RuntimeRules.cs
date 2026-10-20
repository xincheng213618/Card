using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_wo_long_feng_chu_ol_WoLongFengChuUses
{
    internal static string EquipmentSlotNamedLabel(EquipmentSlot slot) => slot switch
    { EquipmentSlot.Weapon => "武器", EquipmentSlot.Armor => "防具", EquipmentSlot.OffensiveHorse => "进攻坐骑", EquipmentSlot.DefensiveHorse => "防御坐骑", _ => "宝物" };

    internal static string EquipmentSlotNamedTargetSignature(LegalAction? action, PromptChoice? choice) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join(',', action?.TargetSeats ?? choice?.Targets ?? []) + ":" + action?.TargetCardId)));

    internal static string EquipmentSlotNamedDecisionSignature(PendingDecision decision) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(decision))));
}
