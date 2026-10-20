using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_ke_bi_neng_ol_KeBiNeng
{
    internal static CardConversionSource KeBiNengSource(ProgramSkillFrame f) => new(f.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    internal static Dictionary<string, string> KeBiNengArgs(string option) => new() { ["program-action"] = "ke-bi-neng-pingduan", ["option"] = option };

    internal static IReadOnlyList<int> KeBiNengMaterials(LegalAction a) => a.MinCardCount > 0 || a.MaxCardCount > 1 ? a.SelectableCardIds : a.CardId is > 0 ? [a.CardId.Value] : [];
}
