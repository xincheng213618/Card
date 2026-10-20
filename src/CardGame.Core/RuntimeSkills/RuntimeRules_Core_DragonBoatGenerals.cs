using System.Text.Json.Serialization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_DragonBoatGenerals
{
    internal static CardMoveReason DragonBoatGeneralDrawReason(string skillId) => new("skill-program." + skillId + ".draw");
}
