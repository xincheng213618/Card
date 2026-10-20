namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_lu_bu_ol_magic_MoLuBuGangquan
{
    internal static bool IsMoGangquanSource(CardConversionSource? s) => s?.SkillId == MoLuBuComposition.Gangquan && s.BindingId == "qi-equipment-fire-slash";
}
