using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_FriendTrio
{
    internal static bool IsFriendTrioOperation(SkillProgramEffectOp op) => op is >= SkillProgramEffectOp.BeginFriendPrediction and <= SkillProgramEffectOp.GainFriendCategoryMark;

    internal static string FriendValueLabel(string v) => v switch
    { "basic" => "基本牌", "trick" => "锦囊牌", "equipment" => "装备牌", "red" => "红色", "black" => "黑色", _ => v };
}
