using System.Globalization;
using System.Text;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_cai_yong_le_LeCaiYongFeibai
{
    internal static CardConversionSource FeibaiSource(ProgramSkillFrame f) => new(f.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    internal static int FeibaiNameLength(CardKind kind) => CardCatalog.Get(kind).DisplayName.EnumerateRunes().Count();
}
