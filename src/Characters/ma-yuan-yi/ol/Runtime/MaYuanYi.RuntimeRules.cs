using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_ma_yuan_yi_ol_MaYuanYi
{
    internal static CardConversionSource MaYuanYiSource(ProgramSkillFrame f)=>new(f.SkillId,CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
}
