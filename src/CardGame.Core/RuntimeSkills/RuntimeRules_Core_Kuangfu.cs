using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_Kuangfu
{
    internal static CardConversionSource KuangfuSource(ProgramSkillFrame f) => new(f.SkillId, f.ActivationId!, f.OwnerSeat, f.SkillInstanceId);
}
