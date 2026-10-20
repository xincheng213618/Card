using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_xiahou_shi_mou_mobile_MouXiahouShiYanyu
{
    internal static CardConversionSource MouXiahouShiSource(ProgramSkillFrame f) => new(f.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    internal static bool MouXiahouShiSlash(CardKind k) => k is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash;
}
