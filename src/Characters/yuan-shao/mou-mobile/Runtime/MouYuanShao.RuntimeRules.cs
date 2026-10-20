namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_yuan_shao_mou_mobile_MouYuanShao
{
    internal static bool IsMouYuanShaoTargetTrigger(string skill, SkillProgramTrigger trigger) => MouYuanShaoComposition.TargetTrigger(skill, trigger);
}
