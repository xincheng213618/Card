namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_zhu_ling_ol_ZhuLingPrograms
{
    internal static string ZhanyiCategoryName(SkillProgramCardCategory category) => category switch
    {
        SkillProgramCardCategory.Basic => "基本牌",
        SkillProgramCardCategory.Trick => "锦囊牌",
        _ => "装备牌"
    };
}
