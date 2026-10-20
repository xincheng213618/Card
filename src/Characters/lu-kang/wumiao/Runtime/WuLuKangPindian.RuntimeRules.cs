using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_lu_kang_wumiao_WuLuKangPindian
{
    internal static CardConversionSource WuLuKangSource(ProgramSkillFrame f) => new(f.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    internal static CardConversionSource WuLuKangSource(ProgramTriggerCandidate c) => new(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId);
}
