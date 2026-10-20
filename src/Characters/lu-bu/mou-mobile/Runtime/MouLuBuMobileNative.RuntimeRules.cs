namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_lu_bu_mou_mobile_MouLuBuMobileNative
{
    internal static bool IsMouLbDuelOrigin(ProgramDamageTargetDuelOrigin o) => o.SkillId == MouLuBuMobileComposition.Liyu && o.BindingId == "obtain-draw-and-choose";
}
