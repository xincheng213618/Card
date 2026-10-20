namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_lv_kuang_lv_xiang_ol_LvKuangLvXiangPrograms
{
    internal static CardConversionSource LvKuangLvXiangSource(ProgramSkillFrame f)=>new(f.SkillId,CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);

    internal static int[] LvKuangLvXiangValidTargets(ProgramSkillFrame f,IReadOnlyList<PromptChoice> choices)=>f.LvKuangLvXiang is{Stage:"slash"}r?[r.Target]:choices.SelectMany(c=>c.Targets).Distinct().ToArray();
}
